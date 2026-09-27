using PleaseDontDrown.Avatars;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Drives a player's third-person body on every machine. Everything is read from what the network already
    /// syncs (position, head rotation/height, who holds what) plus a few gestures, so a remote player walks,
    /// crouches, swims, carries, throws and does CPR without any extra per-frame traffic.
    /// The local player only sees the body's shadow (and their first-person arms).
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class PlayerAvatar : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private AvatarRig _rig;
        [SerializeField] private AvatarAnimator _animator;
        [SerializeField] private float _standEyeHeight = 1.65f;
        [SerializeField] private float _crouchEyeHeight = 1.0f;

        private readonly RaycastHit[] _hits = new RaycastHit[6];
        private Vector3 _lastPosition;
        private Vector3 _velocity;
        private bool _remoteCharging;
        private bool _remoteEating;
        private float _remoteChargeTime;
        private float _lastPumpTime = -10f;
        private Vector3 _cprPoint;
        private float _groundCheckAt;
        private bool _grounded = true;

        public AvatarRig Rig => _rig;
        public AvatarAnimator Animator => _animator;
        /// <summary>A remote player is eating (their food sits at their mouth for everyone).</summary>
        public bool RemoteEating => _remoteEating;

        private void Start()
        {
            _lastPosition = transform.position;
        }

        public void ApplyLook(AvatarLook look)
        {
            if (_rig.IsBuilt && _rig.Look.Equals(look)) return;
            _rig.Build(look);
            if (_hub != null && _hub.IsOwner) _rig.SetShadowsOnly(true);
        }

        public void SetLocal(bool local) => _rig.SetShadowsOnly(local);

        /// <summary>A gesture from this player (played locally for the owner, relayed to everyone else).</summary>
        public void OnGesture(AvatarGesture gesture)
        {
            switch (gesture)
            {
                case AvatarGesture.ChargeStart:
                    _remoteCharging = true;
                    _remoteChargeTime = Time.time;
                    return;
                case AvatarGesture.ChargeEnd:
                    _remoteCharging = false;
                    return;
                case AvatarGesture.Throw:
                    _remoteCharging = false;
                    break;
                case AvatarGesture.EatStart:
                    _remoteEating = true;
                    return;
                case AvatarGesture.EatStop:
                    _remoteEating = false;
                    return;
            }
            _animator.Play(gesture);
        }

        /// <summary>A CPR compression by this player on a chest at <paramref name="chest"/>.</summary>
        public void OnPump(Vector3 chest)
        {
            _lastPumpTime = Time.time;
            _cprPoint = chest;
            _animator.Play(AvatarGesture.Pump);
        }

        private void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 position = transform.position;
            bool local = _hub.IsOwner;
            PlayerMotor motor = _hub.Motor;
            PlayerHands hands = _hub.Hands;
            Transform head = _hub.Head;

            if (local && motor != null && motor.enabled)
                _velocity = motor.Velocity;
            else
            {
                Vector3 raw = (position - _lastPosition) / dt;
                if (raw.sqrMagnitude > 400f) raw = Vector3.zero; // teleport / snap
                _velocity = Vector3.Lerp(_velocity, raw, 1f - Mathf.Exp(-12f * dt));
            }
            _lastPosition = position;

            Vector3 look = head.forward;
            var m = new AvatarMotion
            {
                Velocity = _velocity,
                FacingYaw = head.eulerAngles.y,
                LookPitch = -Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg,
                Crouch = Mathf.Clamp01(Mathf.InverseLerp(_standEyeHeight, _crouchEyeHeight, head.localPosition.y))
            };

            if (local && motor != null && motor.enabled)
            {
                m.Grounded = motor.IsGrounded;
                m.Swimming = motor.IsSwimming;
                m.Underwater = motor.IsHeadUnderwater;
                m.Climbing = motor.IsClimbing;
                m.Sprinting = motor.IsSprinting;
            }
            else
            {
                float depth = WaterSurface.Exists ? WaterSurface.HeightAt(position) - position.y : -10f;
                m.Swimming = depth > 1.1f;
                m.Underwater = WaterSurface.Exists && WaterSurface.DepthOf(head.position) > 0.05f;
                if (Time.time >= _groundCheckAt)
                {
                    _groundCheckAt = Time.time + 0.1f;
                    _grounded = Mathf.Abs(_velocity.y) < 2.5f && GroundBelow(position, 0.35f);
                }
                m.Grounded = _grounded || m.Swimming;
                m.Sprinting = new Vector2(_velocity.x, _velocity.z).magnitude > 6f;
            }

            if (hands != null)
            {
                PlayerHands.GripKind grip = hands.GetGrips(out m.GripLeft, out m.GripRight);
                m.Holding = grip != PlayerHands.GripKind.None;
                m.TwoHanded = grip == PlayerHands.GripKind.TwoHands;
                m.CarryingPerson = grip == PlayerHands.GripKind.Person;
                if (local)
                {
                    m.Charge = hands.Charge01;
                    m.Eating = hands.IsEating;
                }
                else
                {
                    if (_remoteCharging && (hands.HeldItem == null || Time.time - _remoteChargeTime > 4f)) _remoteCharging = false;
                    m.Charge = _remoteCharging ? Mathf.Clamp01((Time.time - _remoteChargeTime) / 0.75f) : 0f;
                    if (_remoteEating && hands.HeldItem == null) _remoteEating = false;
                    m.Eating = _remoteEating;
                }
                if (m.Eating) m.Holding = false; // the eating layer puts the hand at the mouth
            }

            m.Cpr = Time.time - _lastPumpTime < 1.3f && !m.Swimming;
            m.CprPoint = _cprPoint;
            _animator.Motion = m;
        }

        private bool GroundBelow(Vector3 feet, float distance)
        {
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * 0.2f, Vector3.down, _hits, distance + 0.2f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (_hits[i].collider != _hub.BodyCollider && !_hits[i].collider.transform.IsChildOf(transform))
                    return true;
            return false;
        }
    }
}
