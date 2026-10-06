using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// A melee tool held in the right hand (the knife). How to Fish's melee, rebuilt with our own code from its
    /// numbers: a press picks what's in front of you (up to 1.5 m, 0.5 m wide), and the hand with the blade flies
    /// to it along a curve that swings out to one side (a random side each time), turning the blade over for the
    /// cut; it lands, then comes back along another curve. You can strike again once 40% of the way back; a press
    /// before that is kept. Other players see the same flight (the press is sent round).
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Melee : NetworkBehaviour, IHoldPose, IHeldTool
    {
        [SerializeField] private int _damage = 45;
        [SerializeField] private float _range = 1.5f;
        [SerializeField] private float _hitRadius = 0.5f;
        [SerializeField] private float _hitOffset = 0.1f;          // stop this short of the surface
        [SerializeField] private float _forwardSpeed = 5f;         // share of the strike per second
        [SerializeField] private float _returnSpeed = 3f;
        [SerializeField, Range(0f, 1f)] private float _nextAttackAt = 0.6f; // strike again once back below this
        [SerializeField] private Vector3 _noTargetPoint = new(0f, 0f, 1f);  // camera space, swinging at air
        [SerializeField] private float _force = 5f;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private ToolFeel _feel = ToolFeel.Default;

        // The strike: how far along (0..1) the hand is by the share of time, and how far it swings out sideways.
        private static readonly AnimationCurve Forward = new(new Keyframe(0f, 0f, 0.05f, 0.05f),
            new Keyframe(0.7f, 0.505f, 1.94f, 1.94f), new Keyframe(1f, 1f, 0.02f, 0.02f));
        private static readonly AnimationCurve ForwardSide = new(new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(0.501f, -0.694f, 0.02f, 0.02f), new Keyframe(0.807f, -0.696f, 0.6f, 0.6f), new Keyframe(1f, 0f, 4.95f, 4.95f));
        private static readonly AnimationCurve Back = new(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 0.03f, 0.03f));
        private static readonly AnimationCurve BackSide = new(new Keyframe(0f, 0f, 0.01f, 0.01f),
            new Keyframe(0.147f, 0.05f, 0.86f, 0.86f), new Keyframe(0.85f, 0.695f, -1.09f, -1.09f), new Keyframe(1f, 0f, -3.96f, -3.96f));
        private static readonly AnimationCurve Turn = new(new Keyframe(0f, 0f, 0.05f, 0.05f),
            new Keyframe(0.627f, 0.925f, 0.58f, 0.58f), new Keyframe(1f, 1f, 0f, 0f));

        private Item _item;
        private readonly HeldToolMotion _motion = new();
        private bool _local;

        // One strike at a time (the same on every machine that sees it).
        private enum Phase { Idle, Out, Back }
        private Phase _phase;
        private float _percent;            // 0 at rest .. 1 at the target
        private int _swing = 1;            // which side it swings out to
        private Vector3 _from;             // camera space, where the strike started
        private Transform _target;         // what it's going for (it follows it), or null
        private Vector3 _targetLocal;      // the spot on it (its space); camera space when there's no target
        private Vector3 _hitWorld;         // where it got to (it comes back from there)
        private Quaternion _fromRotation = Quaternion.identity;
        private bool _queued;
        private RaycastHit _hit;
        private bool _hasHit;
        private float _inspectStart = -10f;

        public string UseLabel => "Stab";

        private const float InspectSeconds = 1.6f;

        private void Awake()
        {
            _item = GetComponent<Item>();
        }

        // ------------------------------------------------------------------ input (holder)

        public void Use(PlayerHub holder)
        {
            if (holder != PlayerHub.Local) return;
            if (CanStrike()) Strike(holder);
            else _queued = true;
        }

        private bool CanStrike() => _phase == Phase.Idle || (_phase == Phase.Back && _percent <= _nextAttackAt);

        private void Update()
        {
            PlayerHub holder = _item.Holder;
            bool mine = holder != null && holder == PlayerHub.Local && !_item.IsStowed;
            if (mine && !_local)
            {
                _local = true;
                _motion.Feel = _feel;
                _motion.BasePosition = _item.HoldOffset;
                _motion.Draw();
                _phase = Phase.Idle;
                _queued = false;
            }
            else if (!mine && _local)
            {
                _local = false;
                _phase = Phase.Idle;
                _queued = false;
            }
            if (holder == null) _phase = Phase.Idle;
            if (!_local) return;

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (_queued && CanStrike()) Strike(holder);
            if (GameInput.GameplayActive && GameInput.Inspect.WasPressedThisFrame() && _phase == Phase.Idle) _inspectStart = Time.time;
            if (_phase != Phase.Idle) _inspectStart = -10f;

            PlayerLook look = holder.Look;
            PlayerMotor motor = holder.Motor;
            _motion.Feel = _feel;
            _motion.BasePosition = _item.HoldOffset;
            _motion.Update(dt, look != null ? look.LookDelta : Vector2.zero, look != null ? look.BobPosition : Vector3.zero,
                motor != null ? motor.MoveInput : Vector2.zero, motor != null && motor.IsSprinting, motor == null || motor.IsGrounded,
                motor != null ? motor.Velocity.y : 0f, look != null ? look.RollSmoothTime : 0.15f);
        }

        private void LateUpdate()
        {
            if (_phase == Phase.Idle) return;
            float dt = Time.deltaTime;
            if (_phase == Phase.Out)
            {
                _percent += _forwardSpeed * dt;
                if (_percent >= 1f)
                {
                    _percent = 1f;
                    Land();
                }
            }
            else
            {
                _percent -= _returnSpeed * dt;
                if (_percent <= 0f)
                {
                    _percent = 0f;
                    _phase = Phase.Idle;
                }
            }
        }

        private void Strike(PlayerHub holder)
        {
            _queued = false;
            Transform view = ViewOf(holder);
            PlayerCombat combat = holder.GetComponent<PlayerCombat>();
            _hasHit = combat != null && combat.FindTarget(view.position, view.forward, _range, _hitRadius, out _hit);
            Transform target = _hasHit ? _hit.collider.transform : null;
            Vector3 local = _hasHit ? target.InverseTransformPoint(_hit.point - view.forward * _hitOffset) : _noTargetPoint;
            int swing = Random.value < 0.5f ? 1 : -1;
            Begin(holder, target, local, swing);
            PlaySwing();
            // Everyone else sees the same strike.
            Vector3 world = _hasHit ? _hit.point - view.forward * _hitOffset : view.TransformPoint(_noTargetPoint);
            StrikeServer(world, (sbyte)swing);
        }

        private void Begin(PlayerHub holder, Transform target, Vector3 local, int swing)
        {
            Transform view = ViewOf(holder);
            GetRestPose(holder, out Vector3 restOffset, out Quaternion restRotation);
            _from = _phase == Phase.Idle ? restOffset : view.InverseTransformPoint(transform.position);
            _fromRotation = _phase == Phase.Idle ? restRotation : Quaternion.Inverse(view.rotation) * transform.rotation;
            _target = target;
            _targetLocal = local;
            _swing = swing;
            _percent = 0f;
            _phase = Phase.Out;
        }

        /// <summary>The blade gets there: the hit counts now (holder), and it heads back from where it is.</summary>
        private void Land()
        {
            _phase = Phase.Back;
            _hitWorld = TargetWorld();
            PlayerHub holder = _item.Holder;
            if (holder == null || holder != PlayerHub.Local) return;
            Transform view = ViewOf(holder);
            PlayerCombat combat = holder.GetComponent<PlayerCombat>();
            if (combat == null) return;
            // Nothing was there at the press: whatever is there now.
            if (!_hasHit && !combat.FindTarget(view.position, view.forward, _range, _hitRadius, out _hit)) return;
            if (_target != null) _hit.point = _target.TransformPoint(_targetLocal) + view.forward * _hitOffset;
            combat.ApplyHit(_hit, view.forward, _damage, _force, ProceduralAudio.Stab);
        }

        private Vector3 TargetWorld()
        {
            PlayerHub holder = _item.Holder;
            Transform view = holder != null ? ViewOf(holder) : transform;
            if (_target != null) return _target.TransformPoint(_targetLocal);
            return view.TransformPoint(_targetLocal);
        }

        [ServerRpc(RequireOwnership = false)]
        private void StrikeServer(Vector3 world, sbyte swing) => StrikeObservers(world, swing);

        [ObserversRpc(ExcludeOwner = false)]
        private void StrikeObservers(Vector3 world, sbyte swing)
        {
            PlayerHub holder = _item.Holder;
            if (holder == null || holder == PlayerHub.Local) return; // the holder already runs its own
            Begin(holder, null, ViewOf(holder).InverseTransformPoint(world), swing);
            PlaySwing();
        }

        private void PlaySwing()
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.KnifeSwish, 0.5f);
        }

        // ------------------------------------------------------------------ holding (IHoldPose)

        private void GetRestPose(PlayerHub holder, out Vector3 offset, out Quaternion rotation)
        {
            if (_local && holder == PlayerHub.Local)
            {
                offset = _motion.Position;
                rotation = _motion.Rotation * _item.HoldRotation;
            }
            else
            {
                offset = _item.HoldOffset;
                rotation = _item.HoldRotation;
            }
            // Inspect: flip it up, turn it over to look at both sides of the blade, give it a spin.
            float since = Time.time - _inspectStart;
            if (since < InspectSeconds)
            {
                float u = since / InspectSeconds;
                float show = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);
                float spin = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.55f) / 0.35f)) * 360f;
                float look = Mathf.Lerp(-70f, 70f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((u - 0.1f) / 0.45f)));
                offset += new Vector3(-0.14f, 0.09f, -0.08f) * show;
                rotation = Quaternion.Euler(-20f * show, look * show, 0f) * rotation * Quaternion.Euler(0f, 0f, spin);
            }
        }

        public void GetHoldPose(PlayerHub holder, out Vector3 offset, out Quaternion rotation, out float pitchFollow)
        {
            pitchFollow = 1f;
            GetRestPose(holder, out Vector3 restOffset, out Quaternion restRotation);
            if (_phase == Phase.Idle)
            {
                offset = restOffset;
                rotation = restRotation;
                return;
            }
            Transform view = ViewOf(holder);
            Vector3 right = Vector3.right; // camera space
            if (_phase == Phase.Out)
            {
                Vector3 to = _target != null ? view.InverseTransformPoint(_target.TransformPoint(_targetLocal)) : _targetLocal;
                offset = Vector3.Lerp(_from, to, Forward.Evaluate(_percent)) + right * (_swing * ForwardSide.Evaluate(_percent));
            }
            else
            {
                Vector3 hit = view.InverseTransformPoint(_hitWorld);
                offset = Vector3.Lerp(restOffset, hit, Back.Evaluate(_percent)) + right * (_swing * BackSide.Evaluate(_percent));
            }
            // The blade turns over into the cut, edge leading the way it swings, and points at what it's going for.
            Vector3 aimAt = _phase == Phase.Out
                ? (_target != null ? view.InverseTransformPoint(_target.TransformPoint(_targetLocal)) : _targetLocal)
                : view.InverseTransformPoint(_hitWorld);
            Vector3 dir = aimAt.sqrMagnitude > 1e-4f ? aimAt.normalized : Vector3.forward;
            Quaternion cut = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0f, 0f, _swing * -70f) * Quaternion.Euler(-15f, 0f, 0f);
            Quaternion start = _phase == Phase.Out ? _fromRotation : restRotation;
            rotation = Quaternion.Slerp(start, cut, Turn.Evaluate(_percent));
        }

        private static Transform ViewOf(PlayerHub holder) =>
            holder.Look != null && holder.Look.Camera != null ? holder.Look.Camera.transform : holder.Head;
    }
}
