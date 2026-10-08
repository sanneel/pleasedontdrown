using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The beach bar's barista, behind the counter. Wipes the counter with a rag, turns to watch lifeguards who come
    /// up to the bar and waves them over; Interact orders a beer, which he puts down on the counter in front of you
    /// (the host spawns it). Drink it (hold Secondary) and it comes back up as a burp about ten seconds later.
    /// The body is a stand-in until his own model arrives (one of the tourist dads): set <see cref="_body"/> to its
    /// AvatarLook.Bodies id.
    /// </summary>
    public class Barista : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private AvatarRig _rig;
        [SerializeField] private AvatarAnimator _animator;
        [SerializeField] private AudioSource _audio;
        [Tooltip("Where served beers go down, along the front of the counter (one per stool).")]
        [SerializeField] private Transform[] _serveSpots = new Transform[0];
        [Tooltip("The stretch of counter top he wipes, end to end (the hand goes back and forth between them).")]
        [SerializeField] private Transform _wipeFrom, _wipeTo;
        [SerializeField] private Transform _rag;
        [Tooltip("0 = the stand-in (a white-haired tourist dad); else an AvatarLook.Bodies id (his own model, once it's in).")]
        [SerializeField] private byte _body;

        private const string BeerName = "Beer";
        private const int BeerCap = 12;
        private const float ServeRange = 4.5f;

        private static readonly string[] ServeLines =
        {
            "One cold one, coming up!", "Here you go, hero. On the house.", "Don't drink and swim, eh?",
            "Another one? You lifeguards are thirsty.", "Easy, champ. Somebody has to save the tourists.",
            "Cheers! Mind the burp.", "Fresh from the cooler!"
        };
        private static readonly string[] BusyLines = { "Whoa, whoa. Finish that one first.", "One at a time, pal." };

        private readonly Dictionary<int, float> _lastOrder = new(); // host: client id -> time
        private float _talkUntil, _waveCooldown, _reachUntil, _yaw, _wipePhase;
        private Vector3 _reachPoint;
        private PlayerHub _watching;

        /// <summary>The stand-in look: the white-haired, moustached tourist dad in flowery trunks.</summary>
        public static AvatarLook MockLook => new() { Body = AvatarLook.Bodies.Variant(AvatarLook.Bodies.TouristBuddy, 1), Figure = 0 };

        private void Start()
        {
            if (_rig == null) return;
            AvatarLook look = MockLook;
            if (_body != 0)
            {
                look.Body = _body;
                look.Figure = (byte)(AvatarLook.Bodies.IsFeminine(_body) ? 1 : 0);
            }
            _rig.Build(look);
            _yaw = transform.eulerAngles.y;
            if (_animator != null)
            {
                _animator.Seed = 4242;
                _animator.Lively = true;
                _animator.CullWhenHidden = true;
            }
        }

        // ------------------------------------------------------------------ ordering (the player's machine, then the host)

        public bool CanInteract(PlayerHub player) => player != null && (player.transform.position - transform.position).sqrMagnitude < ServeRange * ServeRange;

        public string GetPrompt(PlayerHub player) => "Order a beer";

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner) return;
            OrderServer(player);
        }

        [ServerRpc(RequireOwnership = false)]
        private void OrderServer(PlayerHub player, NetworkConnection caller = null)
        {
            if (player == null || player.Owner != caller) return;
            if ((player.transform.position - transform.position).sqrMagnitude > (ServeRange + 1f) * (ServeRange + 1f)) return;
            if (_lastOrder.TryGetValue(caller.ClientId, out float last) && Time.time - last < 2.5f) return;
            _lastOrder[caller.ClientId] = Time.time;

            // Still holding a full one, or the beach is already littered with bottles: no more.
            Item held = player.Hands != null ? player.Hands.HeldItem : null;
            int beers = 0;
            foreach (Item item in Item.All)
                if (item != null && item.IsSpawned && item.DisplayName == BeerName) beers++;
            if ((held != null && held.DisplayName == BeerName) || beers >= BeerCap)
            {
                SayObservers(-1 - Random.Range(0, BusyLines.Length), player.transform.position);
                return;
            }

            Transform spot = NearestSpot(player.transform.position);
            Item prefab = GameContent.Items != null ? GameContent.Items.Find(BeerName) : null;
            if (spot == null || prefab == null) return;
            Item beer = Instantiate(prefab, spot.position, spot.rotation);
            Spawn(beer.gameObject);
            Debug.Log($"[Bar] {player.DisplayName} ordered a beer");
            SayObservers(Random.Range(0, ServeLines.Length), spot.position);
        }

        private Transform NearestSpot(Vector3 from)
        {
            Transform best = null;
            foreach (Transform s in _serveSpots)
                if (s != null && (best == null || (s.position - from).sqrMagnitude < (best.position - from).sqrMagnitude)) best = s;
            return best;
        }

        /// <summary>Every machine: he says it (line &gt;= 0 a serving line, -1-n a "no more" line) and reaches to the spot.</summary>
        [ObserversRpc]
        private void SayObservers(int line, Vector3 point)
        {
            string text = line >= 0 ? ServeLines[line % ServeLines.Length] : BusyLines[(-1 - line) % BusyLines.Length];
            Vector3 head = _rig != null && _rig.IsBuilt ? _rig[AvatarRig.Bone.Head].position : transform.position + Vector3.up * 1.7f;
            FloatingText.Spawn(head + Vector3.up * 0.38f, text, new Color(1f, 0.95f, 0.8f), 0.7f, 3f);
            _talkUntil = Time.time + 1.6f;
            if (_audio != null) SpeechVoice.On(gameObject, _audio).Speak(text, 0, 0.86f);
            if (line >= 0)
            {
                _reachPoint = point;
                _reachUntil = Time.time + 0.9f;
                if (_animator != null) _animator.Play(AvatarGesture.Interact);
            }
        }

        // ------------------------------------------------------------------ the look of it (every machine)

        private void Update()
        {
            if (_animator == null || _rig == null || !_rig.IsBuilt) return;
            Vector3 position = transform.position;

            // Who's at the bar: the nearest lifeguard in front of the counter.
            PlayerHub nearest = null;
            float best = ServeRange * ServeRange;
            foreach (PlayerHub p in PlayerHub.All)
            {
                if (p == null) continue;
                Vector3 to = p.transform.position - position;
                if (Vector3.Dot(to, transform.forward) < 0.2f) continue; // not behind the bar with him
                if (to.sqrMagnitude < best) { best = to.sqrMagnitude; nearest = p; }
            }
            if (nearest != null && nearest != _watching && Time.time > _waveCooldown)
            {
                _animator.Play(AvatarGesture.Wave);
                _waveCooldown = Time.time + 25f;
            }
            _watching = nearest;

            float target = transform.eulerAngles.y;
            if (Time.time < _reachUntil) target = Yaw(_reachPoint - position, target);
            else if (nearest != null) target = Yaw(nearest.transform.position - position, target);
            _yaw = Mathf.LerpAngle(_yaw, target, 1f - Mathf.Exp(-5f * Time.deltaTime));

            // Nobody to serve: wiping the counter, slowly, back and forth in little circles.
            bool wiping = nearest == null && Time.time >= _reachUntil && _wipeFrom != null && _wipeTo != null;
            var motion = new AvatarMotion
            {
                FacingYaw = _yaw,
                LookPitch = wiping ? 28f : 0f,
                Grounded = true,
                Mood = AvatarMood.Happy,
                Talking = Time.time < _talkUntil
            };
            if (wiping)
            {
                _wipePhase += Time.deltaTime;
                float along = 0.5f - 0.5f * Mathf.Cos(_wipePhase * 0.9f);
                Vector3 circle = (transform.right * Mathf.Cos(_wipePhase * 6f) + transform.forward * Mathf.Sin(_wipePhase * 6f)) * 0.04f;
                Vector3 hand = Vector3.Lerp(_wipeFrom.position, _wipeTo.position, along) + circle;
                motion.Holding = true;
                motion.GripRight = new HandGrip(hand, transform.forward, Vector3.down, HandPose.Flat);
                if (_rag != null)
                {
                    _rag.gameObject.SetActive(true);
                    _rag.SetPositionAndRotation(hand - Vector3.up * 0.035f, Quaternion.Euler(0f, _yaw, 0f)); // under the palm, on the wood
                }
            }
            else if (_rag != null && _rag.gameObject.activeSelf)
            {
                // Put down on the counter where it was.
                _rag.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
            _animator.Motion = motion;
        }

        private static float Yaw(Vector3 to, float fallback)
        {
            to.y = 0f;
            return to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to).eulerAngles.y : fallback;
        }
    }
}
