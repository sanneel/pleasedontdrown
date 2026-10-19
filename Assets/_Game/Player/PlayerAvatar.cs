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
        private Vector3 _kneelHead;             // their head: the body lies chest to head along this
        private float _kneelSide;               // which side of them we kneel on (+1/-1, kept for the whole CPR; 0 = not chosen)
        private Quaternion? _kneelFacing;       // turned square to their chest while kneeling
        private float _kneelTurn;
        private float _breathUntil = float.NegativeInfinity;
        private Quaternion? _seatFacing;
        private Quaternion _kneelFacingTarget = Quaternion.identity;
        private float _kneelReach = 0.55f;      // how far out from the middle of their body (clear of a big belly)
        private bool _inCannon;                 // drawn up a cannon's barrel, at _cannonBody
        private Vector3 _cannonBody;
        private Vector3 _cannonFrom;            // where the body was when it went in (the loader's arms)
        private Quaternion _cannonFromRot;
        private float _cannonEnterAt;
        private const float CannonStuffTime = 0.5f;
        private float _groundCheckAt;
        private bool _grounded = true;
        private Vector3 _kneelShift;      // the body moved over to the tourist it is doing CPR on (the player stays put)
        private Vector3 _bodyRest;
        private bool _bodyRestKnown;

        public AvatarRig Rig => _rig;
        public AvatarAnimator Animator => _animator;
        /// <summary>A remote player is eating (their food sits at their mouth for everyone).</summary>
        public bool RemoteEating => _remoteEating;
        public bool RemoteCharging => _remoteCharging;

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

        /// <summary>A blow from <paramref name="push"/> (the way it travels, world): the body snaps away and comes back.</summary>
        /// The harder the push (a knockback's speed, m/s), the bigger the snap: a jab nods the head, an overhand
        /// bends them right back.
        public void Flinch(Vector3 push) => _animator.Flinch(push, Mathf.Clamp(push.magnitude / 6.5f, 0.3f, 1f));

        /// <summary>
        /// Stop kneeling over somebody now (they just got up): CPR otherwise holds the kneel 1.3 s after the last press,
        /// and the rescuer stayed down with the face in the belly of the friend who had stood up.
        /// </summary>
        public void EndCpr()
        {
            _lastPumpTime = -10f;
            if (_hub != null && _hub.IsOwner && _hub.Look != null) _hub.Look.EndKneel();
        }

        /// <summary>Kneeling for CPR within <paramref name="range"/> metres of <paramref name="point"/>.</summary>
        public bool CprNear(Vector3 point, float range) => Time.time - _lastPumpTime < 1.3f && (_cprPoint - point).sqrMagnitude < range * range;

        /// <summary>A gesture from this player (played locally for the owner, relayed to everyone else).</summary>
        public void OnGesture(AvatarGesture gesture) => OnGesture(gesture, Vector3.zero);

        /// <summary>A CPR breath or punch: stays kneeling by the tourist.</summary>
        /// <summary>A gesture aimed at a point (a bite off a dish on the table).</summary>
        public void PlayGesture(AvatarGesture gesture, Vector3 point) => _animator.Play(gesture, point);

        public void OnCprGesture(AvatarGesture gesture, Vector3 point)
        {
            _lastPumpTime = Time.time;
            if (gesture == AvatarGesture.Breath) _breathUntil = Time.time + 1.4f; // slide up to their face for it
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

        /// <summary>Kneel beside a chest as for CPR, without pressing it (the defibrillator's shock).</summary>
        public void OnKneel(Vector3 chest, Vector3 head = default, float reach = 0.55f)
        {
            _lastPumpTime = Time.time;
            _cprPoint = chest;
            _kneelHead = head;
            _kneelPoint = Rescue.VictimBody.KneelSpot(chest, head);
            _kneelReach = reach;
        }

        /// <summary>A CPR compression by this player on a chest at <paramref name="chest"/>.</summary>
        public void OnPump(Vector3 chest, Vector3 head = default, float reach = 0.55f)
        {
            _lastPumpTime = Time.time;
            // The hands on the lower half of the breastbone, not up at the collarbones by the face, and the body
            // kneeling tall at their side with the arms reaching out and down to it, never folded over (Avatar:
            // MaxCprLean): the goofy lifeguard's big head must stay well clear of the hands.
            Vector3 down = head != Vector3.zero ? chest - head : Vector3.zero;
            down.y = 0f;
            _cprPoint = down.sqrMagnitude > 1e-4f ? chest + down.normalized * 0.07f : chest;
            _kneelHead = head;
            _kneelPoint = Rescue.VictimBody.KneelSpot(_cprPoint, head);
            _kneelReach = reach + 0.07f; // (knees at their side, not on them; checked with ReviewCapture cpr:0.62)
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
                m.Basketball = hands.HeldItem != null && hands.HeldItem.GetComponent<Fun.BasketballDribble>() != null;
                if (m.Basketball) m.LookPitch = Mathf.Min(m.LookPitch, Fun.BasketballDribble.MaxLookDown);
                m.Defib = Combat.DefibPaddles.ActiveFor(_hub) != null; // (a shock from a defibrillator on the ground)
                PlayerHands.GripKind grip = hands.GetGrip(out m.GripLeft, out m.GripRight);
                m.Holding = grip != PlayerHands.GripKind.None;
                m.TwoHanded = grip == PlayerHands.GripKind.TwoHands;
                m.CarryingPerson = grip == PlayerHands.GripKind.Person;
                // (Our own third-person view shows the body the way everybody else sees it: rifle in the shoulder.)
                bool seenFromOutside = !local || (_hub.Look != null && _hub.Look.ThirdPerson);
                m.Shouldered = seenFromOutside && m.TwoHanded && hands.HeldItem != null && PlayerHands.Shoulders(hands.HeldItem);
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
            if (_combat != null && (_combat.IsDazed || _combat.IsDowned) && !m.Swimming)
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
            _seatFacing = null;
            if (seat != null)
            {
                m.Seated = true;
                m.Crouch = 0f;
                m.Swimming = m.Underwater = false;
                m.Grounded = true;
                m.Velocity = Vector3.zero; // no walking legs
                m.Straddle = seat.Straddle;
                m.FloatSeat = seat.RestHands;
                m.ChairSeat = seat.IsChair || seat.IsLand; // a chair, a stool, the golf cart's bench
                m.HighSeat = seat.IsStool;
                m.SeatEdge = seat.SeatEdge;
                // Sat on anything the body faces the way its seat does: looking about turns the head, not the legs
                // through the table or the counter, nor the rider round in the jet ski's saddle.
                _seatFacing = seat.SeatFacing(_hub);
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
        /// The cannon: stuffed down the barrel, out of sight (the team, 2026-10-12: a body sticking out of the muzzle
        /// looked wrong); or pushing it about, both hands on its push bar.
        /// </summary>
        private Renderer[] _hiddenInCannon;

        private void HideInCannon(bool hide)
        {
            if (hide == (_hiddenInCannon != null) || _rig == null) return;
            if (hide)
            {
                _hiddenInCannon = System.Array.FindAll(_rig.GetComponentsInChildren<Renderer>(), r => r.enabled);
                foreach (Renderer r in _hiddenInCannon) r.enabled = false;
                return;
            }
            foreach (Renderer r in _hiddenInCannon)
                if (r != null) r.enabled = true;
            _hiddenInCannon = null;
        }

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
                // The whole head out of the muzzle (chin at the rim) and the hands up above it: the feet that far
                // down the barrel. (The body's own eye height: the goofy lifeguard's eyes are well under the camera's.)
                float eyes = _rig != null && _rig.EyeHeight > 0.3f ? _rig.EyeHeight : _standEyeHeight;
                float s = _rig != null ? _rig.Scale : 1f;
                Vector3 inside = muzzle - rotation * Vector3.up * Mathf.Max(0.4f, eyes - 0.2f * s);
                if (!_inCannon)
                {
                    _cannonEnterAt = Time.time;
                    _cannonFrom = transform.TransformPoint(_bodyRest);
                    _cannonFromRot = Quaternion.Euler(0f, m.FacingYaw, 0f);
                }
                // Stuffed in, not teleported: up out of the arms to stand over the muzzle, turning to the barrel, then
                // down it feet first.
                float k = Mathf.Clamp01((Time.time - _cannonEnterAt) / CannonStuffTime);
                Vector3 over = muzzle + rotation * Vector3.up * 0.1f;
                if (k < 0.6f)
                {
                    float e = Mathf.SmoothStep(0f, 1f, k / 0.6f);
                    _cannonBody = Vector3.Lerp(_cannonFrom, over, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 0.4f);
                    _animator.RootOverride = Quaternion.Slerp(_cannonFromRot, rotation, e);
                }
                else
                {
                    _cannonBody = Vector3.Lerp(over, inside, Mathf.SmoothStep(0f, 1f, (k - 0.6f) / 0.4f));
                    _animator.RootOverride = rotation;
                }
                _inCannon = true;
                HideInCannon(k >= 0.8f);
                return;
            }
            _animator.RootOverride = _kneelFacing ?? _seatFacing;
            _inCannon = false;
            HideInCannon(false);
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
            PlayerHub carried = PlayerCarry.CarriedBy(_hub);
            if (carried == null) return;
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
            // On the friend's own legs as they hang now: the right palm on the front of the thighs, the left on the
            // front of the knees (fixed spots from our chest put the right forearm through the thighs).
            AvatarRig legs = carried.Avatar != null ? carried.Avatar.Rig : null;
            if (legs != null && legs.IsBuilt && legs[AvatarRig.Bone.ThighL] != null && legs[AvatarRig.Bone.ShinL] != null)
            {
                float palm = 0.03f * legs.Scale;
                Vector3 thighs = (Vector3.Lerp(legs[AvatarRig.Bone.ThighL].position, legs[AvatarRig.Bone.ShinL].position, 0.55f) +
                                  Vector3.Lerp(legs[AvatarRig.Bone.ThighR].position, legs[AvatarRig.Bone.ShinR].position, 0.55f)) * 0.5f;
                Vector3 knees = (legs[AvatarRig.Bone.ShinL].position + legs[AvatarRig.Bone.ShinR].position) * 0.5f - Vector3.up * (0.04f * legs.Scale);
                m.GripRight = new HandGrip(BodySpace.Surface(legs, thighs, forward, 0.5f) + forward * palm, -right, -forward, HandPose.Carry);
                m.GripLeft = new HandGrip(BodySpace.Surface(legs, knees, forward, 0.5f) + forward * palm, -right, -forward, HandPose.Carry);
            }
        }

        /// <summary>
        /// CPR is allowed from a couple of metres away, but hands pressing on thin air next to the tourist look wrong:
        /// while it goes on, the body (not the player) kneels right beside the chest it is pressing: at their side
        /// (whichever side we came from), turned square to them, so the hands on the chest show from outside. (It
        /// used to kneel wherever we stood, at their head or feet, bent over them along the body and hiding it.)
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
            Quaternion? facing = null;
            if (kneeling)
            {
                // Square to their side: along = their body (chest to head), across = out to the side we're on.
                Vector3 along = _kneelHead != Vector3.zero ? _kneelHead - _cprPoint : Vector3.zero;
                along.y = 0f;
                Vector3 kneelPoint = Time.time < _breathUntil && _kneelHead != Vector3.zero
                    ? Rescue.VictimBody.BreathSpot(_cprPoint, _kneelHead) : _kneelPoint;
                Vector3 to = kneelPoint - position;
                to.y = 0f;
                if (along.sqrMagnitude > 1e-4f)
                {
                    along.Normalize();
                    Vector3 across = Vector3.Cross(Vector3.up, along);
                    if (_kneelSide == 0f) _kneelSide = Vector3.Dot(position - kneelPoint, across) >= 0f ? 1f : -1f;
                    Vector3 spot = kneelPoint + across * (_kneelSide * arm);
                    shift = new Vector3(spot.x - position.x, 0f, spot.z - position.z);
                    shift = Vector3.ClampMagnitude(shift, most);
                    facing = Quaternion.LookRotation(-across * _kneelSide, Vector3.up);
                }
                else
                {
                    float distance = to.magnitude;
                    // Pulled in from further away; pushed back out when standing too close (knees inside a big belly).
                    if (distance > arm) shift = to / distance * Mathf.Min(distance - arm, most);
                    else if (distance > 0.05f) shift = to / distance * (distance - arm);
                }
            }
            else _kneelSide = 0f;
            // Turn square to them (and back to where we look after).
            _kneelTurn = Mathf.MoveTowards(_kneelTurn, facing.HasValue ? 1f : 0f, dt * 5f);
            if (facing.HasValue) _kneelFacingTarget = facing.Value;
            Quaternion free = Quaternion.Euler(0f, _animator.Motion.FacingYaw, 0f);
            _kneelFacing = _kneelTurn > 0f ? Quaternion.Slerp(free, _kneelFacingTarget, Mathf.SmoothStep(0f, 1f, _kneelTurn)) : null;
            _kneelShift = Vector3.Lerp(_kneelShift, shift, 1f - Mathf.Exp(-9f * dt));
            if (_kneelShift.sqrMagnitude < 1e-6f && shift == Vector3.zero) _kneelShift = Vector3.zero;
            body.position = _inCannon ? _cannonBody : transform.TransformPoint(_bodyRest) + _kneelShift;
        }

        // The ball's visual bounce runs after the item has been placed. Refresh these grips before avatar IK,
        // so the hands touch this frame's ball rather than its position from the previous frame.
        private void LateUpdate()
        {
            if (_hub == null || _animator == null || _hub.Hands == null || _hub.Hands.HeldItem == null) return;
            if (!_hub.Hands.HeldItem.TryGetComponent(out Fun.BasketballDribble ball)) return;
            AvatarMotion m = _animator.Motion;
            PlayerHands.GripKind kind = ball.GetGrips(_hub, out m.GripLeft, out m.GripRight);
            m.Holding = m.Basketball = true;
            m.TwoHanded = kind == PlayerHands.GripKind.TwoHands;
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
