using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// Fists and getting knocked about. With empty hands, Primary throws a punch at whatever is in front of you:
    /// robbers and pirates take damage (the host decides), other lifeguards get shoved, loose things get knocked.
    /// Also the host's way to knock this player back (a pirate's punch, a shark bump).
    ///
    /// Punching is How to Fish's (its tuning measured from the game; our own code): straight punches, right then
    /// left in turn. The target is picked when you press (1.5 m ahead, a 0.5 m wide reach for loose things); the
    /// fist gets there in 1/10 s, locked to the spot it's going for, and lands on arrival; it comes back in 1/4 s.
    /// The next punch can go as soon as the last fist has landed, so a quick one-two is quick; a press while a fist
    /// is still on its way is kept and thrown the moment it can be.
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _reach = 1.5f;
        [SerializeField] private float _radius = 0.5f;
        [SerializeField] private float _cooldown = 0.1f;
        [SerializeField] private float _shove = 5.5f;

        /// <summary>A fist travels to its target in this long, and back in <see cref="ReturnTime"/>.</summary>
        public const float StrikeTime = 0.1f, ReturnTime = 0.25f;

        private readonly RaycastHit[] _hits = new RaycastHit[12];
        private readonly float[] _punchedAt = { -10f, -10f }; // per fist: 0 right, 1 left
        private bool _lastLeft = true;
        private bool _queued;
        private float _lastServerPunch;

        public float LastKnockedTime { get; private set; } = -10f;

        private void Update()
        {
            if (!IsOwner || !GameInput.GameplayActive || _hub.Motor == null || !_hub.Motor.enabled) return;
            if (_hub.Motor.Seat != null) return;
            if (_hub.Hands != null && _hub.Hands.HeldItem != null)
            {
                _queued = false; // hands full: Primary uses the item
                return;
            }
            if (GameInput.Primary.WasPressedThisFrame())
            {
                if (CanPunch()) Punch();
                else _queued = true;
            }
            else if (_queued && CanPunch()) Punch();
        }

        private bool Striking(int fist) => Time.time - _punchedAt[fist] < StrikeTime;
        private bool Returning(int fist) => Time.time - _punchedAt[fist] < StrikeTime + ReturnTime;

        /// <summary>Not while a fist is still on its way; and not while both are still coming back.</summary>
        private bool CanPunch() => !Striking(0) && !Striking(1) && !(Returning(0) && Returning(1));

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (IsOwner)
                DevCommands.Register("punch", "[straight|hook|uppercut|overhand] [left|right]", "Throw a punch (automated tests).", args =>
                {
                    if (args.Length > 0 && System.Enum.TryParse(args[0], true, out PunchKind kind))
                        _forced = (kind, args.Length > 1 ? args[1] == "left" : false);
                    Punch();
                }, cheat: true, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("punch", this);
        }

        private (PunchKind kind, bool left)? _forced;

        /// <summary>Owner: punch (right, left, right...). The target is picked now; the fist lands on it in 1/10 s.</summary>
        public void Punch()
        {
            _queued = false;
            bool left = _forced is { } forced ? forced.left : !_lastLeft;
            _forced = null;
            _lastLeft = left;
            _punchedAt[left ? 1 : 0] = Time.time;
            Transform view = _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _hub.Head;
            bool found = FindTarget(view.position, view.forward, out RaycastHit hit);
            Transform target = found ? hit.collider.transform : null;
            Vector3 local = found ? target.InverseTransformPoint(hit.point - view.forward * 0.15f) : Vector3.zero; // stop short of the surface
            _hub.Gesture(AvatarGesture.Punch, found ? hit.point : view.position + view.forward * 1.1f);
            if (_hub.Arms != null) _hub.Arms.PlayPunch(PunchKind.Straight, left, target, local);
            PlaySwish();
            StartCoroutine(LandPunch(found, hit, target, local));
        }

        /// <summary>The fist arrives: the target picked at the press is hit (or, if there was none, whatever is there now).</summary>
        private System.Collections.IEnumerator LandPunch(bool found, RaycastHit hit, Transform target, Vector3 local)
        {
            yield return new WaitForSeconds(StrikeTime);
            Transform view = _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _hub.Head;
            Vector3 dir = view.forward;
            if (found && target != null) hit.point = target.TransformPoint(local);
            else if (!FindTarget(view.position, dir, out hit)) yield break; // swung at nothing

            ApplyHit(hit, dir, Damage.Punch, 4f, ProceduralAudio.Punch);
        }

        /// <summary>
        /// Owner: a blow lands (a fist, a blade). Damageable things take <paramref name="damage"/> (the host decides),
        /// other lifeguards get shoved, loose things knocked by <paramref name="force"/> (m/s), anything else just thuds.
        /// </summary>
        public void ApplyHit(RaycastHit hit, Vector3 dir, int damage, float force, AudioClip sound)
        {
            if (dir.y < 0f) dir = new Vector3(dir.x, 0f, dir.z).normalized; // never drives things into the ground
            IDamageable damageable = DamageUtil.Find(hit.collider);
            var targetObject = damageable as Component;
            PlayerHub otherPlayer = hit.collider.GetComponentInParent<PlayerHub>();
            bool blade = sound != ProceduralAudio.Punch;
            if (damageable != null && targetObject != null && targetObject.TryGetComponent(out NetworkObject nob))
                HitServer(nob, hit.point, dir, damage, blade);
            else if (otherPlayer != null && otherPlayer != _hub)
                ShovePlayerServer(otherPlayer, dir);
            else if (Item.FromCollider(hit.collider) is { } item && !item.IsHeld)
            {
                // Knock loose things about (we take over their physics for a moment).
                item.Sync.RequestAuthority();
                if (!item.Sync.Body.isKinematic)
                    item.Sync.Body.AddForceAtPosition(dir * force, hit.point, ForceMode.VelocityChange);
                PlayHit(hit.point, blade);
                PunchFxServer(hit.point, blade);
            }
            else
            {
                PlayHit(hit.point, blade);
                PunchFxServer(hit.point, blade);
            }
        }

        private bool FindTarget(Vector3 origin, Vector3 dir, out RaycastHit best) => FindTarget(origin, dir, _reach, _radius, out best);

        /// <summary>What a blow from here would land on: the nearest thing, preferring what can be hurt or shoved.</summary>
        public bool FindTarget(Vector3 origin, Vector3 dir, float reach, float radius, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(origin, radius, dir, _hits, reach, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            Item held = _hub.Hands != null ? _hub.Hands.HeldItem : null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == _hub.BodyCollider || (held != null && held.OwnsCollider(h.collider))) continue;
                if (h.distance <= 0f) h.point = h.collider.ClosestPoint(origin); // started inside it
                // Prefer things that can be hit over the ground under them.
                float score = h.distance + (DamageUtil.Find(h.collider) != null || h.collider.GetComponentInParent<PlayerHub>() != null ? -1f : 0f);
                if (score < bestDistance)
                {
                    bestDistance = score;
                    best = h;
                }
            }
            return bestDistance < float.MaxValue;
        }

        [ServerRpc]
        private void HitServer(NetworkObject target, Vector3 point, Vector3 dir, int damage, bool blade)
        {
            if (target == null || Time.time - _lastServerPunch < _cooldown * 0.6f) return;
            if ((target.transform.position - transform.position).sqrMagnitude > 4f * 4f) return;
            _lastServerPunch = Time.time;
            damage = Mathf.Clamp(damage, 0, Damage.MaxMelee);
            IDamageable damageable = target.GetComponent<IDamageable>();
            damageable?.ServerTakeHit(damage, blade ? DamageKind.Blade : DamageKind.Punch, _hub, point, dir);
            PunchFxObservers(point, blade);
        }

        [ServerRpc]
        private void ShovePlayerServer(PlayerHub other, Vector3 dir)
        {
            if (other == null || Time.time - _lastServerPunch < _cooldown * 0.6f) return;
            if ((other.transform.position - transform.position).sqrMagnitude > 4f * 4f) return;
            _lastServerPunch = Time.time;
            Vector3 flat = new Vector3(dir.x, 0f, dir.z).normalized;
            if (other.TryGetComponent(out PlayerCombat combat))
                combat.ServerKnockback(flat * _shove + Vector3.up * 2.5f, _hub.DisplayName);
            PunchFxObservers(other.transform.position + Vector3.up * 1.4f, false);
        }

        [ServerRpc]
        private void PunchFxServer(Vector3 point, bool blade) => PunchFxObservers(point, blade);

        [ObserversRpc(ExcludeOwner = true)]
        private void PunchFxObservers(Vector3 point, bool blade) => PlayHit(point, blade);

        private void PlayHit(Vector3 point, bool blade = false)
        {
            if (_audio != null) _audio.PlayOneShot(blade ? ProceduralAudio.Stab : ProceduralAudio.Punch, 1f);
            string word = blade ? (Random.value < 0.5f ? "SHNK!" : "STAB!") : Random.value < 0.5f ? "WHAP!" : "BOP!";
            FloatingText.Spawn(point + Vector3.up * 0.2f, word, blade ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.85f, 0.3f), 0.8f, 0.8f);
        }

        private void PlaySwish()
        {
            if (_audio != null) _audio.PlayOneShot(BeachAudio.PunchSwoosh, 0.5f);
        }

        // ------------------------------------------------------------------ getting hit

        /// <summary>Host: knock this player back (pirate punch, shove, shark).</summary>
        [Server]
        public void ServerKnockback(Vector3 velocity, string byWhom)
        {
            KnockTarget(Owner, velocity, byWhom ?? string.Empty);
            KnockFxObservers();
        }

        [TargetRpc]
        private void KnockTarget(NetworkConnection target, Vector3 velocity, string byWhom)
        {
            LastKnockedTime = Time.time;
            if (_hub.Motor != null && _hub.Motor.Seat == null) _hub.Motor.AddImpulse(velocity);
            PlayerHud.ShowToast(string.IsNullOrEmpty(byWhom) ? "OOF!" : $"OOF! {byWhom} got you.", 1.5f);
        }

        [ObserversRpc]
        private void KnockFxObservers()
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Punch, 0.9f);
        }
    }
}
