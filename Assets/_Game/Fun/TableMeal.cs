using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Vehicles;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// A bar order eaten where it's put down (the user, 2026-10-12: you sit, you eat, that's it; no taking it in your
    /// hand). Sitting at the bar or at a table (a <see cref="BarSeat"/>), E on your dish in front of you takes a bite:
    /// the hand reaches over, a khinkali comes off the plate to the mouth (or the glass comes up for a sip and goes back
    /// down), everyone sees it, and the food meter fills a little. After the last bite the plate's cleared away.
    /// Standing, it's an item like any other (E picks it up).
    /// </summary>
    [RequireComponent(typeof(Item), typeof(Edible))]
    public class TableMeal : NetworkBehaviour, IGroundUse
    {
        public const float BiteSeconds = 1.3f;
        private const float Grab = 0.35f, AtMouth = 0.7f, Swallow = 1.0f;

        [Tooltip("Bites (or sips) it takes.")]
        [SerializeField] private int _bites = 3;
        [Tooltip("Parts of the model eaten one per bite, the one nearest the eater first (the khinkali off the plate). Empty: it all goes at the end.")]
        [SerializeField] private string[] _pieceNames = new string[0];

        private readonly SyncVar<int> _left = new(-1);
        private readonly SyncVar<int> _eaten = new(0); // which pieces are gone (bits)
        private Item _item;
        private Interaction.Interactable _interactable;
        private Edible _edible;
        private Transform _model;
        private readonly List<Transform> _pieces = new();
        private float _busyUntil;

        private int Left => _left.Value < 0 ? _bites : _left.Value;

        private void Awake()
        {
            _item = GetComponent<Item>();
            _edible = GetComponent<Edible>();
            _interactable = GetComponent<Interaction.Interactable>();
            foreach (Transform child in transform)
                if (child.name.StartsWith("Model_")) _model = child;
            if (_model != null)
                foreach (string piece in _pieceNames)
                    if (FindDeep(_model, piece) is { } t) _pieces.Add(t);
            _eaten.OnChange += (_, eaten, _) => ShowEaten(eaten);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _left.Value = _bites;
        }

        // ------------------------------------------------------------------ E on it, sitting at the bar

        /// <summary>Sitting at a bar seat with this dish in front of them.</summary>
        public bool CanUseOnGround(PlayerHub player)
        {
            if (player == null || _item.IsHeld || Left <= 0) return false;
            if (Vehicle.RideOf(player) is not { } seat || !seat.TryGetComponent(out BarSeat bar)) return false;
            Vector3 serve = bar.ServePoint != null ? bar.ServePoint.position : seat.transform.position;
            return (transform.position - serve).sqrMagnitude < 0.8f * 0.8f;
        }

        public string GroundUsePrompt(PlayerHub player)
        {
            string name = _item.DisplayName.ToLowerInvariant();
            return _edible.Drink ? $"Drink the {name} ({Left} {(Left == 1 ? "sip" : "sips")} left)" : $"Eat the {name} ({Left} left)";
        }

        public void UseOnGround(PlayerHub player)
        {
            if (Time.time < _busyUntil) return; // (one bite at a time)
            _busyUntil = Time.time + BiteSeconds;
            BiteServer(player);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BiteServer(PlayerHub eater, NetworkConnection caller = null)
        {
            if (eater == null || eater.Owner != caller || Left <= 0 || !CanUseOnGround(eater)) return;
            int left = Left - 1;
            int piece = NearestPiece(eater.transform.position);
            _left.Value = left;
            if (piece >= 0) _eaten.Value |= 1 << piece;
            BiteObservers(eater, left, piece);
            if (left == 0) StartCoroutine(ClearAway());
        }

        // The khinkali still on the plate nearest the eater (the one their hand reaches first); -1 with none.
        private int NearestPiece(Vector3 eater)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _pieces.Count; i++)
            {
                if ((_eaten.Value & (1 << i)) != 0 || _pieces[i] == null) continue;
                Vector3 flat = Vector3.ProjectOnPlane(Bounds(_pieces[i]).center - eater, Vector3.up);
                if (flat.sqrMagnitude < bestDistance) { bestDistance = flat.sqrMagnitude; best = i; }
            }
            return best;
        }

        // The last bite's done: the plate (or the empty glass) is cleared away.
        private IEnumerator ClearAway()
        {
            yield return new WaitForSeconds(BiteSeconds + 0.6f);
            if (IsSpawned) Despawn(gameObject);
        }

        /// <summary>Every machine: the eater's hand to the dish and to the mouth, the bite carried up in it.</summary>
        [ObserversRpc]
        private void BiteObservers(PlayerHub eater, int left, int piece)
        {
            if (eater == null) return;
            Transform moving = Moving(piece);
            eater.ShowTableBite(moving != null ? Bounds(moving).center : transform.position + Vector3.up * 0.05f); // (the hand goes to that)
            if (eater.IsOwner && eater.Vitals != null) eater.Vitals.Eat(_edible.Food / Mathf.Max(1, _bites));
            StartCoroutine(Carry(eater, left, piece));
        }

        private IEnumerator Carry(PlayerHub eater, int left, int piece)
        {
            Transform moving = Moving(piece);
            if (moving == null) yield break;
            bool whole = moving == _model;
            moving.gameObject.SetActive(true); // (a piece is still on the plate until the hand takes it)
            Vector3 restPos = moving.localPosition;
            Quaternion restRot = moving.localRotation;
            Transform restParent = moving.parent;
            float sips = (_bites - left - 1) / (float)Mathf.Max(1, _bites - 1); // 0 on the first sip, 1 on the last
            Transform view = eater.IsOwner && eater.Look != null && eater.Look.Camera != null ? eater.Look.Camera.transform : null;
            LocalGrip(moving, view, out Vector3 gripPoint, out Vector3 gripFingers, out Vector3 gripPalm, out HandPose gripPose, out Vector3 takePoint);
            Vector3 centre = moving.InverseTransformPoint(Bounds(moving).center);
            // (No "use this" outline on it while it's in our hand at our mouth; back on after if we still look at it.)
            if (view != null) _interactable?.SetOutlined(false);

            float start = Time.time;
            bool sounded = false;
            while (Time.time - start < BiteSeconds && eater != null)
            {
                float t = Time.time - start;
                bool eaten = !whole && t >= Swallow;
                if (view != null)
                {
                    // Our own view: it comes up off the table to where held food is eaten (a drink tipped up at the
                    // lips the way a held one is), and our hand holds it there.
                    float up = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Grab, AtMouth, t))
                        * (whole ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Swallow, BiteSeconds - 0.05f, t)) : 1f);
                    float chew = t > AtMouth && t < Swallow ? Mathf.Sin((t - AtMouth) * 30f) * 0.008f : 0f;
                    Vector3 restWorld = restParent.TransformPoint(restPos);
                    Quaternion restWorldRot = restParent.rotation * restRot;
                    MouthPose(moving, view, centre, restWorld, restWorldRot, sips, out Vector3 mouthPos, out Quaternion mouthRot);
                    moving.SetPositionAndRotation(Vector3.Lerp(restWorld, mouthPos, up) + view.up * chew, Quaternion.Slerp(restWorldRot, mouthRot, up));
                    if (!eaten && t < BiteSeconds - 0.05f)
                    {
                        s_biter = eater;
                        // Food is taken from above (palm down over it, on the plate) and turned palm up as it comes up.
                        float turn = takePoint == gripPoint ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Grab, AtMouth, t));
                        Vector3 palmNow = Quaternion.AngleAxis(180f * (1f - turn), gripFingers) * gripPalm; // (thumb up on the way: the wrist rolls, not over)
                        s_bite = new HandGrip(moving.TransformPoint(Vector3.Lerp(takePoint, gripPoint, turn)), moving.TransformDirection(gripFingers),
                            moving.TransformDirection(palmNow), gripPose);
                        s_biteFrame = Time.frameCount;
                    }
                }
                else if (t >= Grab && Palm(eater, out Vector3 palm, out _, out Vector3 facing))
                {
                    // Someone else eating: what they eat rides in their hand (the avatar's TableBite gesture), taken off
                    // the plate into the palm (the last few centimetres a short arm doesn't reach) and a dish or glass
                    // put back down the same way.
                    float back = whole ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Swallow, BiteSeconds - 0.05f, t)) : 0f;
                    float inHand = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Grab, Grab + 0.12f, t)) * (1f - back);
                    if (!eaten)
                    {
                        float tip = _edible.Drink ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(Grab + 0.15f, AtMouth, t)) * (1f - Mathf.SmoothStep(0f, 1f, back * 2f)) : 0f;
                        Quaternion held = Quaternion.AngleAxis(-70f * tip, Vector3.Cross(Vector3.up, facing)) * Quaternion.LookRotation(facing, Vector3.up);
                        Vector3 heldAt = palm + facing * 0.03f - Vector3.up * 0.03f - held * Vector3.Scale(centre, moving.lossyScale) * (whole ? 0f : 1f);
                        moving.SetPositionAndRotation(Vector3.Lerp(restParent.TransformPoint(restPos), heldAt, inHand),
                            Quaternion.Slerp(restParent.rotation * restRot, held, inHand));
                    }
                }
                // (Every frame: the eaten pieces syncing in hide this one too, ShowEaten.)
                if (!whole) moving.gameObject.SetActive(!eaten);
                if (!sounded && t >= AtMouth)
                {
                    sounded = true;
                    _edible.PlaySoundHere();
                }
                yield return null;
            }
            moving.SetLocalPositionAndRotation(restPos, restRot);
            if (!whole) moving.gameObject.SetActive(false);
            if (view != null && eater != null && eater.Interactor != null && eater.Interactor.Current == _interactable) _interactable.SetOutlined(true);
        }

        /// <summary>What goes up to the mouth: that khinkali (eaten), or the whole dish or glass (it goes back down).</summary>
        private Transform Moving(int piece) => _edible.Drink || _pieces.Count == 0 ? _model : piece >= 0 && piece < _pieces.Count ? _pieces[piece] : null;

        // Our own first-person hand on the bite being eaten (FirstPersonArms asks every frame).
        private static PlayerHub s_biter;
        private static HandGrip s_bite;
        private static int s_biteFrame = -10;

        /// <summary>The local player's right hand during a bite at the table: on what's being lifted to the mouth.</summary>
        public static bool LocalBiteGrip(PlayerHub player, out HandGrip grip)
        {
            grip = s_bite;
            return player != null && player == s_biter && Time.frameCount - s_biteFrame <= 1 && grip.Active;
        }

        // Fingers cupped round it, the thumb in over it (HandPose.Cup's straight thumb stuck out beside a khinkali).
        private static readonly HandPose Holding = new(0.55f, 0.8f, 0.06f);

        private static Bounds Bounds(Transform root)
        {
            var bounds = new Bounds(root.position, Vector3.zero);
            bool first = true;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            return bounds;
        }

        /// <summary>
        /// Where the hand holds it, in its own space (taken as it stands on the table): a bottle by the grip it's made
        /// with, a can or glass round its near side, food taken from above, then cupped from underneath as it comes up (as a held thing is, Player.PlayerHands).
        /// </summary>
        private void LocalGrip(Transform moving, Transform view, out Vector3 point, out Vector3 fingers, out Vector3 palm, out HandPose pose, out Vector3 take)
        {
            Bounds b = Bounds(moving);
            Vector3 c = b.center, e = b.extents, up = Vector3.up;
            Vector3 eye = view != null ? view.position : c - transform.forward;
            Vector3 toUs = Vector3.ProjectOnPlane(eye - c, up);
            toUs = toUs.sqrMagnitude > 1e-4f ? toUs.normalized : -transform.forward;
            Vector3 side = Vector3.Cross(up, -toUs); // to the eater's right
            float depth = Mathf.Abs(toUs.x) * e.x + Mathf.Abs(toUs.z) * e.z;
            Vector3 p, f, n;
            if (_edible.Drink && moving == _model && _item.GripRight != null)
            {
                p = _item.GripRight.position;
                f = _item.GripRight.forward;
                n = -_item.GripRight.up;
                pose = _item.GripPose;
            }
            else if (_edible.Drink)
            {
                // Round the near side, a little low, fingers wrapping across to the left, thumb up.
                p = c + toUs * (depth + 0.012f) - up * (e.y * 0.2f) + side * (depth * 0.25f);
                f = (-side - toUs * 0.3f).normalized;
                n = -toUs;
                pose = Holding;
            }
            else
            {
                p = c - up * (e.y * 0.92f) + toUs * (depth * 0.12f);
                f = (-toUs - side * 0.2f).normalized;
                n = up;
                pose = Holding;
            }
            point = moving.InverseTransformPoint(p);
            fingers = moving.InverseTransformDirection(f);
            palm = moving.InverseTransformDirection(n);
            // (Food off the plate: first the palm over the top of it, fingers round it; a glass is held the one way.)
            take = _edible.Drink ? point : moving.InverseTransformPoint(c + up * (e.y * 1.05f) + toUs * (depth * 0.15f));
        }

        /// <summary>
        /// Up at the mouth in our own view: food where a held thing is eaten, a drink tipped up, its rim at the lips
        /// and tipping further with each sip (the same offsets as Player.PlayerHands).
        /// </summary>
        private void MouthPose(Transform moving, Transform view, Vector3 centre, Vector3 restPos, Quaternion restRot, float sips, out Vector3 position, out Quaternion rotation)
        {
            if (_edible.Drink)
            {
                // The held drink's pose is for the item's root: carried over to the model under it.
                Quaternion tipped = Quaternion.Euler(-(108f + 22f * sips), 0f, 0f);
                Vector3 scale = transform.lossyScale;
                Quaternion rootRot = view.rotation * tipped;
                Vector3 rootPos = view.TransformPoint(new Vector3(0.015f, -0.1f, 0.1f)) - rootRot * Vector3.Scale(_edible.Lip, scale);
                position = rootPos + rootRot * (Quaternion.Inverse(transform.rotation) * (restPos - transform.position));
                rotation = rootRot * (Quaternion.Inverse(transform.rotation) * restRot);
                return;
            }
            // Food: its middle where held food is eaten, standing the way it stood on the plate.
            rotation = restRot;
            position = view.TransformPoint(new Vector3(0.03f, -0.13f, 0.3f)) - rotation * Vector3.Scale(centre, moving.lossyScale);
        }

        /// <summary>Where the eater's right palm is now (their first-person hand on their own machine), their mouth and the way they face.</summary>
        private static bool Palm(PlayerHub eater, out Vector3 palm, out Vector3 mouth, out Vector3 facing)
        {
            Transform head = eater.Head;
            facing = Vector3.ProjectOnPlane(head != null ? head.forward : eater.transform.forward, Vector3.up).normalized;
            if (facing.sqrMagnitude < 0.01f) facing = eater.transform.forward;
            mouth = head != null ? head.position - Vector3.up * 0.08f + facing * 0.1f : eater.transform.position + Vector3.up * 1.4f;
            palm = default;
            if (eater.IsOwner && eater.Arms != null && eater.Arms.TryGetPalm(true, out palm)) return true;
            AvatarRig rig = eater.Avatar != null ? eater.Avatar.Rig : null;
            if (rig != null && rig.Hand(true) is { } hand && hand.Hand != null)
            {
                palm = hand.Hand.TransformPoint(hand.PalmContact);
                mouth = eater.Avatar.Animator != null ? eater.Avatar.Animator.Lips : mouth;
                return true;
            }
            return false;
        }

        /// <summary>The pieces already eaten (for anyone who arrives later too). The one being eaten right now shows again
        /// while it's carried up, <see cref="Carry"/>.</summary>
        private void ShowEaten(int eaten)
        {
            for (int i = 0; i < _pieces.Count; i++)
                if ((eaten & (1 << i)) != 0 && _pieces[i] != null) _pieces[i].gameObject.SetActive(false);
        }
    }
}
