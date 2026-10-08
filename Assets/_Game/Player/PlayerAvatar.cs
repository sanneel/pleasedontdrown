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
        private Vector3 _cprPoint, _kneelPoint; // where the hands press; where the body kneels beside it
        private float _kneelReach = 0.55f;      // how far out from the middle of their body (clear of a big belly)
        private bool _inCannon;                 // drawn up a cannon's barrel, at _cannonBody
        private Vector3 _cannonBody;
        private float _groundCheckAt;
        private bool _grounded = true;
        private Vector3 _kneelShift;      // the body moved over to the tourist it is doing CPR on (the player stays put)
        private Vector3 _bodyRest;
        private bool _bodyRestKnown;

        public AvatarRig Rig => _rig;
        public AvatarAnimator Animator => _animator;
        /// <summary>A remote player is eating (their food sits at their mouth for everyone).</summary>
        public bool RemoteEating => _remoteEating;

        private void Start()
        {
            _lastPosition = transform.position;
        }

        private Combat.PlayerCombat _combat;

        public void ApplyLook(AvatarLook look)
        {
            if (_rig.IsBuilt && _rig.Look.Equals(look)) return;
            _rig.Build(look);
            if (_hub != null && _hub.IsOwner) _rig.SetShadowsOnly(true);
        }

        public void SetLocal(bool local) => _rig.SetShadowsOnly(local);

        /// <summary>A gesture from this player (played locally for the owner, relayed to everyone else).</summary>
        public void OnGesture(AvatarGesture gesture) => OnGesture(gesture, Vector3.zero);

        /// <summary>A CPR breath or punch: stays kneeling by the tourist.</summary>
        public void OnCprGesture(AvatarGesture gesture, Vector3 point)
        {
            _lastPumpTime = Time.time;
            _animator.Play(gesture, point);
        }

        public void OnGesture(AvatarGesture gesture, Vector3 point)
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
                case AvatarGesture.JumpShot:
                    _remoteCharging = false;
                    break;
                case AvatarGesture.EatStart:
                    _remoteEating = true;
                    return;
                case AvatarGesture.EatStop:
                    _remoteEating = false;
                    return;
            }
            _animator.Play(gesture, point);
        }

        /// <summary>A CPR compression by this player on a chest at <paramref name="chest"/>.</summary>
        public void OnPump(Vector3 chest, Vector3 head = default, float reach = 0.55f)
        {
            _lastPumpTime = Time.time;
            _cprPoint = chest;
            _kneelPoint = Rescue.VictimBody.KneelSpot(chest, head);
            _kneelReach = reach;
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
                PlayerHands.GripKind grip = hands.GetGrip(out m.GripLeft, out m.GripRight);
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
                // The eating layer puts the hand at the mouth; a bottle stays in the hand that tips it up.
                m.Drinking = m.Eating && hands.HeldItem != null && hands.HeldItem.TryGetComponent(out Items.Edible drink) && drink.Drink;
                if (m.Eating && !m.Drinking) m.Holding = false;
            }

            // Knocked flat (three punches, a coconut to the head): down on the sand for everyone to see.
            if (_combat == null) _combat = _hub.GetComponent<Combat.PlayerCombat>();
            if (_combat != null && _combat.IsDazed && !m.Swimming)
            {
                m.Pose = AvatarPose.Down;
                m.Mood = AvatarMood.Hurt;
            }

            // In the air: shot out of the cannon or thrown (fast, flying superman) or bounced high (a star jump).
            if (!m.Grounded && !m.Swimming && !m.Climbing)
            {
                Vector3 v = _velocity;
                m.Flying = new Vector2(v.x, v.z).magnitude > 8.5f && v.magnitude > 10f;
                m.StarJump = !m.Flying && v.y > 4.5f;
            }

            m.Cpr = Time.time - _lastPumpTime < 1.3f && !m.Swimming;
            m.CprPoint = _cprPoint;
            KneelBeside(m.Cpr && _cprPoint != Vector3.zero, position, dt);

            // On a vehicle: sitting, hands on the handlebars.
            Vehicles.Vehicle seat = Vehicles.Vehicle.RideOf(_hub);
            if (seat != null)
            {
                m.Seated = true;
                m.Crouch = 0f;
                m.Swimming = m.Underwater = false;
                m.Grounded = true;
                m.Velocity = Vector3.zero; // no walking legs
                m.Straddle = seat.Straddle;
                m.FloatSeat = seat.RestHands;
                if (!m.Holding && seat.GetGrips(_hub, out HandGrip left, out HandGrip right))
                {
                    m.GripLeft = left;
                    m.GripRight = right;
                    m.Holding = m.TwoHanded = true;
                }
            }
            CarryPoses(ref m, position);
            CannonPoses(ref m);
            // Led by the hand to the beach hut: our hand in hers.
            if (!m.Holding && Story.LoveHut.HandHold(_hub.transform, out Story.LoveHut.HandGripPoint hold))
            {
                m.Holding = true;
                m.GripRight = new HandGrip(hold.Point, hold.Toward, Vector3.Cross(Vector3.up, hold.Toward), HandPose.LooseFist);
            }
            _animator.Motion = m;
        }

        /// <summary>
        /// The cannon: stuffed up the barrel, the head and both hands sticking out of the muzzle (for everyone, whatever
        /// the body's network position is doing); or pushing it about, both hands on its push bar.
        /// </summary>
        private void CannonPoses(ref AvatarMotion m)
        {
            if (Fun.HumanCannon.InBarrel(_hub, out Vector3 muzzle, out Quaternion rotation))
            {
                m.Pose = AvatarPose.HandsUp;
                m.Mood = AvatarMood.Happy;
                m.Velocity = Vector3.zero;
                m.Grounded = false;
                m.Flying = m.StarJump = m.Swimming = m.Underwater = m.Climbing = m.Sprinting = false;
                m.Holding = m.TwoHanded = m.CarryingPerson = false;
                m.LookPitch = 0f;
                _animator.RootOverride = rotation;
                // The whole head out of the muzzle (chin at the rim) and the hands up above it: the feet that far
                // down the barrel. (The body's own eye height: the goofy lifeguard's eyes are well under the camera's.)
                float eyes = _rig != null && _rig.EyeHeight > 0.3f ? _rig.EyeHeight : _standEyeHeight;
                float s = _rig != null ? _rig.Scale : 1f;
                _cannonBody = muzzle - rotation * Vector3.up * Mathf.Max(0.4f, eyes - 0.2f * s);
                _inCannon = true;
                return;
            }
            _animator.RootOverride = null;
            _inCannon = false;
            if (!m.Holding && Fun.HumanCannon.PushGrips(_hub, out HandGrip left, out HandGrip right))
            {
                m.Holding = m.TwoHanded = true;
                m.GripLeft = left;
                m.GripRight = right;
            }
        }

        /// <summary>
        /// Carrying another lifeguard (<see cref="PlayerCarry"/>): the carrier's arms scooped under them, and the
        /// carried one lying back across those arms, head to the carrier's left, taking it easy.
        /// </summary>
        private void CarryPoses(ref AvatarMotion m, Vector3 position)
        {
            PlayerHub carrier = PlayerCarry.Of(_hub)?.Carrier;
            if (carrier != null)
            {
                PlayerCarry.HoldPoint(carrier, out float yaw);
                m.Pose = AvatarPose.Carried;
                m.Mood = AvatarMood.Scared;
                m.FacingYaw = yaw + 180f; // over the shoulder, facing behind us
                m.LookPitch = 0f;
                m.Velocity = Vector3.zero;
                m.Grounded = false; // (in the air in someone's arms: no feet planted on the sand below)
                m.Swimming = m.Underwater = m.Climbing = m.Sprinting = false;
                m.Holding = m.TwoHanded = m.CarryingPerson = false;
                return;
            }
            if (PlayerCarry.CarriedBy(_hub) == null) return;
            Quaternion facing = Quaternion.Euler(0f, m.FacingYaw, 0f);
            Vector3 forward = facing * Vector3.forward, right = facing * Vector3.right;
            Vector3 chest = position + Vector3.up * 1.2f;
            m.Holding = true;
            m.TwoHanded = false;
            m.CarryingPerson = true;
            m.Charge = 0f;
            // Holding the legs of the one over our shoulder: right arm round the thighs, left hand on a knee below
            // (higher, it would be under our own chin: PlayerCarry.HoldOffset).
            m.GripLeft = new HandGrip(chest + forward * 0.32f + right * 0.16f - Vector3.up * 0.47f, -right, -forward, HandPose.Carry);
            m.GripRight = new HandGrip(chest + forward * 0.24f + right * 0.3f - Vector3.up * 0.12f, -right, -forward, HandPose.Carry);
        }

        /// <summary>
        /// CPR is allowed from a couple of metres away, but hands pressing on thin air next to the tourist look wrong:
        /// while it goes on, the body (not the player) kneels right beside the chest it is pressing.
        /// </summary>
        private void KneelBeside(bool kneeling, Vector3 position, float dt)
        {
            const float most = 2.2f;
            float arm = _kneelReach;
            Transform body = _animator.transform;
            if (body == transform) return; // the body is the player itself here: nothing to shift
            if (!_bodyRestKnown)
            {
                _bodyRest = body.localPosition;
                _bodyRestKnown = true;
            }
            Vector3 shift = Vector3.zero;
            if (kneeling)
            {
                Vector3 to = _kneelPoint - position;
                to.y = 0f;
                float distance = to.magnitude;
                // Pulled in from further away; pushed back out when standing too close (knees inside a big belly).
                if (distance > arm) shift = to / distance * Mathf.Min(distance - arm, most);
                else if (distance > 0.05f) shift = to / distance * (distance - arm);
            }
            _kneelShift = Vector3.Lerp(_kneelShift, shift, 1f - Mathf.Exp(-9f * dt));
            if (_kneelShift.sqrMagnitude < 1e-6f && shift == Vector3.zero) _kneelShift = Vector3.zero;
            body.position = _inCannon ? _cannonBody : transform.TransformPoint(_bodyRest) + _kneelShift;
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
