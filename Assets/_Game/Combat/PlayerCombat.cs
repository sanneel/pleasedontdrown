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
    /// </summary>
    public class PlayerCombat : NetworkBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _reach = 1.9f;
        [SerializeField] private float _radius = 0.28f;
        [SerializeField] private float _cooldown = 0.42f;
        [SerializeField] private float _shove = 5.5f;

        private readonly RaycastHit[] _hits = new RaycastHit[12];
        private float _nextPunch;
        private float _lastPunchTime = -10f;
        private bool _lastLeft = true;
        private PunchKind _lastKind;
        private float _lastServerPunch;

        public float LastKnockedTime { get; private set; } = -10f;

        private void Update()
        {
            if (!IsOwner || !GameInput.GameplayActive || _hub.Motor == null || !_hub.Motor.enabled) return;
            if (_hub.Motor.Seat != null) return;
            if (_hub.Hands != null && _hub.Hands.HeldItem != null) return; // hands full: Primary throws / uses the item
            if (GameInput.Primary.WasPressedThisFrame() && Time.time >= _nextPunch)
                Punch();
        }

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

        /// <summary>
        /// UFC-style combos: a fresh combo opens with a left jab, then the hands alternate and each punch is a
        /// straight, hook, uppercut or overhand (no two of the same fancy one in a row).
        /// </summary>
        private (PunchKind kind, bool left)? _forced;

        private (PunchKind kind, bool left) NextPunch()
        {
            if (_forced is { } forced)
            {
                _forced = null;
                _lastPunchTime = Time.time;
                return forced;
            }
            bool fresh = Time.time - _lastPunchTime > 1.1f;
            bool left = fresh || !_lastLeft;
            PunchKind kind;
            if (fresh) kind = PunchKind.Straight;
            else
            {
                float r = Random.value;
                kind = r < 0.4f ? PunchKind.Straight : r < 0.65f ? PunchKind.Hook : r < 0.83f ? PunchKind.Uppercut : PunchKind.Overhand;
                if (kind != PunchKind.Straight && kind == _lastKind) kind = PunchKind.Straight;
            }
            _lastPunchTime = Time.time;
            _lastLeft = left;
            _lastKind = kind;
            return (kind, left);
        }

        /// <summary>Which way a punch shoves what it hits: hooks sweep across, uppercuts lift, overhands drive down.</summary>
        private static Vector3 PunchDirection(PunchKind kind, bool left, Transform view)
        {
            Vector3 f = view.forward, r = view.right;
            float across = left ? 1f : -1f; // a left hook sweeps to the right
            return kind switch
            {
                PunchKind.Hook => (f + r * (across * 0.9f)).normalized,
                PunchKind.Uppercut => (f * 0.6f + Vector3.up).normalized,
                PunchKind.Overhand => (f - Vector3.up * 0.35f).normalized,
                _ => f
            };
        }

        /// <summary>Owner: swing. Picks the target here (instant feel); the host validates and applies it.</summary>
        public void Punch()
        {
            (PunchKind kind, bool left) = NextPunch();
            _nextPunch = Time.time + FirstPersonArms.PunchDuration(kind) * 0.8f;
            Transform view = _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _hub.Head;
            _hub.Gesture(AvatarGesture.Punch, view.position + view.forward * 1.1f);
            if (_hub.Arms != null) _hub.Arms.PlayPunch(kind, left);
            StartCoroutine(LandPunch(kind, left, FirstPersonArms.PunchDuration(kind) * 0.42f));
        }

        /// <summary>The hit happens when the fist gets there, aimed where we look at that moment.</summary>
        private System.Collections.IEnumerator LandPunch(PunchKind kind, bool left, float delay)
        {
            yield return new WaitForSeconds(delay);
            Transform view = _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _hub.Head;
            Vector3 origin = view.position, dir = view.forward;
            bool found = FindTarget(origin, dir, out RaycastHit hit);
            dir = PunchDirection(kind, left, view);
            if (!found)
            {
                PlaySwish();
                yield break;
            }

            IDamageable damageable = DamageUtil.Find(hit.collider);
            var targetObject = damageable as Component;
            PlayerHub otherPlayer = hit.collider.GetComponentInParent<PlayerHub>();
            if (damageable != null && targetObject != null && targetObject.TryGetComponent(out NetworkObject nob))
                PunchServer(nob, hit.point, dir);
            else if (otherPlayer != null && otherPlayer != _hub)
                ShovePlayerServer(otherPlayer, dir);
            else if (Item.FromCollider(hit.collider) is { } item && !item.IsHeld)
            {
                // Knock loose things about (we take over their physics for a moment).
                item.Sync.RequestAuthority();
                if (!item.Sync.Body.isKinematic)
                    item.Sync.Body.AddForceAtPosition(dir * 4f, hit.point, ForceMode.VelocityChange);
                PlayHit(hit.point);
                PunchFxServer(hit.point);
            }
            else
            {
                PlayHit(hit.point);
                PunchFxServer(hit.point);
            }
        }

        private bool FindTarget(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(origin, _radius, dir, _hits, _reach, ~0, QueryTriggerInteraction.Ignore);
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
        private void PunchServer(NetworkObject target, Vector3 point, Vector3 dir)
        {
            if (target == null || Time.time - _lastServerPunch < _cooldown * 0.6f) return;
            if ((target.transform.position - transform.position).sqrMagnitude > 4f * 4f) return;
            _lastServerPunch = Time.time;
            IDamageable damageable = target.GetComponent<IDamageable>();
            damageable?.ServerTakeHit(Damage.Punch, DamageKind.Punch, _hub, point, dir);
            PunchFxObservers(point);
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
            PunchFxObservers(other.transform.position + Vector3.up * 1.4f);
        }

        [ServerRpc]
        private void PunchFxServer(Vector3 point) => PunchFxObservers(point);

        [ObserversRpc(ExcludeOwner = true)]
        private void PunchFxObservers(Vector3 point) => PlayHit(point);

        private void PlayHit(Vector3 point)
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Punch, 1f);
            FloatingText.Spawn(point + Vector3.up * 0.2f, Random.value < 0.5f ? "WHAP!" : "BOP!", new Color(1f, 0.85f, 0.3f), 0.8f, 0.8f);
        }

        private void PlaySwish()
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Breath, 0.35f);
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
