using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>One-off moves that play over whatever the body is doing.</summary>
    public enum AvatarGesture : byte { None, Interact, Throw, ChargeStart, ChargeEnd, Pump, Wave, Bite, EatStart, EatStop, Punch, Breath, Zap, Shoot }

    /// <summary>Whole-body poses held for a while (story characters, knockouts).</summary>
    public enum AvatarPose : byte { Normal, Down, Kneel, Scared, HandsUp, Lie, LieFront, Sit, SitChair }

    /// <summary>Face on top of the automatic expression.</summary>
    public enum AvatarMood : byte { Neutral, Happy, Scared, Angry, Hurt }

    /// <summary>What the body is doing this frame. Filled in by a driver (e.g. PlayerAvatar) before LateUpdate.</summary>
    public struct AvatarMotion
    {
        public Vector3 Velocity;      // world
        public float FacingYaw;       // where the head looks, degrees
        public float LookPitch;       // degrees, positive = looking down
        public bool Grounded;
        public float Crouch;          // 0..1
        public bool Swimming;
        public bool Underwater;
        public bool Climbing;
        public bool Sprinting;
        public bool Holding;          // hands on an item (grips below)
        public bool TwoHanded;
        public bool CarryingPerson;
        public HandGrip GripLeft, GripRight; // world palm points, finger/palm directions, finger curls
        public float Charge;          // 0..1 throw wind-up
        public bool Eating;
        public bool Cpr;
        public Vector3 CprPoint;      // world, the chest being pressed
        public bool Seated;           // on a vehicle seat (hands come from the grips)
        public AvatarPose Pose;
        public AvatarMood Mood;
        public bool Talking;          // flap the mouth
    }

    /// <summary>
    /// Procedural animation for an <see cref="AvatarRig"/>: a stepping walk/run cycle with planted feet (leg IK),
    /// counter-swinging arms, crouch, jumps, crawl and breaststroke, treading water, hands on held items (arm IK),
    /// throw wind-up and release, CPR, eating, plus a looking head, blinking and gestures. No animation clips.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class AvatarAnimator : MonoBehaviour
    {
        [SerializeField] private AvatarRig _rig;
        [SerializeField] private float _turnSpeed = 10f;

        public AvatarMotion Motion;

        /// <summary>
        /// Sitting on a towel (<see cref="AvatarPose.Sit"/>), the hips sit this far behind the character's root. Lying
        /// poses have the feet at the root and the body behind it, so all towel poses share one root: turning over or
        /// sitting up blends in place instead of jumping.
        /// </summary>
        public const float SitBack = 0.8f;

        private float _phase;
        private float _swimPhase;
        private float _bodyYaw;
        private bool _yawInitialised;
        private bool _turningInPlace;
        private float _move;          // 0..1 how much we're walking
        private float _run;           // 0..1 walk -> run
        private float _swim;          // 0..1 blend into the water poses
        private float _swimMove;      // 0..1 swimming vs treading
        private bool _crawling;
        private float _under;         // 0..1 underwater (dive) pose
        private float _air;
        private float _crouch;
        private float _hold;
        private float _charge;
        private float _eat;
        private float _cpr;
        private float _climb;
        private float _seat;
        private float _down;
        private float _kneel;
        private float _scared;
        private float _handsUp;
        private float _lie;
        private float _lieFront;
        private float _sit;
        private float _chair;
        private Vector3 _gesturePoint;
        private Vector3 _smoothVelocity;
        private AvatarGesture _gesture;
        private float _gestureStart;
        private float _lastPump = -10f;
        private float _nextBlink;
        private float _dt;
        private HandPose _poseL = HandPose.Relaxed, _poseR = HandPose.Relaxed;
        private float _blinkUntil;

        public AvatarRig Rig
        {
            get => _rig;
            set => _rig = value;
        }
        public float BodyYaw => _bodyYaw;

        public void Play(AvatarGesture gesture) => Play(gesture, Vector3.zero);

        /// <param name="point">World target for aimed gestures (the face a punch lands on, the mouth for a rescue breath).</param>
        public void Play(AvatarGesture gesture, Vector3 point)
        {
            if (gesture == AvatarGesture.Pump) { _lastPump = Now; return; }
            if (gesture == AvatarGesture.Breath) _lastPump = Now; // stays kneeling
            _gesturePoint = point;
            if (gesture is AvatarGesture.ChargeStart or AvatarGesture.ChargeEnd or AvatarGesture.EatStart or AvatarGesture.EatStop or AvatarGesture.Bite)
                return; // these come through Motion
            _gesture = gesture;
            _gestureStart = Now;
        }

        private void Reset() => _rig = GetComponent<AvatarRig>();

        /// <summary>Review tools pose avatars in edit mode, where Time doesn't run.</summary>
        public static float? TimeOverride;
        private static float Now => TimeOverride ?? Time.time;

        private void LateUpdate() => Tick(Mathf.Min(Time.deltaTime, 0.1f));

        public void Tick(float dt)
        {
            if (_rig == null || !_rig.IsBuilt) return;
            _dt = dt;
            UpdateBlends(dt);
            _rig.ResetPose();
            PoseBody();
            PoseLegs();
            PoseArms();
            PoseHead();
            PoseSpecial();
            PoseFace();
        }

        // ------------------------------------------------------------------ state

        private void UpdateBlends(float dt)
        {
            AvatarMotion m = Motion;
            float k(float rate) => 1f - Mathf.Exp(-rate * dt);
            _smoothVelocity = Vector3.Lerp(_smoothVelocity, m.Velocity, k(10f));
            var flat = new Vector3(_smoothVelocity.x, 0f, _smoothVelocity.z);
            float speed = flat.magnitude;

            // The body follows the head: quickly while moving, in a little turn-in-place when looking far aside.
            if (!_yawInitialised) { _bodyYaw = m.FacingYaw; _yawInitialised = true; }
            float delta = Mathf.DeltaAngle(_bodyYaw, m.FacingYaw);
            bool moving = speed > 0.4f || m.Swimming || m.Holding;
            if (moving || Mathf.Abs(delta) > 55f) _turningInPlace = !moving;
            if (_turningInPlace && Mathf.Abs(delta) < 4f) _turningInPlace = false;
            if (moving || _turningInPlace)
                _bodyYaw += delta * k(moving ? _turnSpeed : _turnSpeed * 0.6f);
            transform.rotation = Quaternion.Euler(0f, _bodyYaw, 0f);

            _move = Mathf.MoveTowards(_move, Mathf.InverseLerp(0.15f, 1.4f, speed) + (_turningInPlace ? 0.35f : 0f), dt * 4f);
            _move = Mathf.Clamp01(_move);
            _run = Mathf.MoveTowards(_run, Mathf.InverseLerp(4.8f, 7f, speed), dt * 3f);
            _swim = Mathf.MoveTowards(_swim, m.Swimming ? 1f : 0f, dt * 3f);
            // Crawl while making way, tread water when (nearly) still; the gap between the two thresholds keeps a
            // slow swimmer, or one nudged sideways, from flickering between lying flat and standing up in the water.
            _crawling = m.Swimming && (_crawling ? speed > 0.18f : speed > 0.35f);
            _swimMove = Mathf.MoveTowards(_swimMove, _crawling ? 1f : 0f, dt * 2.5f);
            _under = Mathf.MoveTowards(_under, m.Swimming && m.Underwater ? 1f : 0f, dt * 2.5f);
            _air = Mathf.MoveTowards(_air, !m.Grounded && !m.Swimming && !m.Climbing ? 1f : 0f, dt * 7f);
            _crouch = Mathf.Lerp(_crouch, m.Crouch, k(12f));
            _hold = Mathf.MoveTowards(_hold, m.Holding ? 1f : 0f, dt * 6f);
            _charge = Mathf.MoveTowards(_charge, m.Charge, dt * 8f);
            _eat = Mathf.MoveTowards(_eat, m.Eating ? 1f : 0f, dt * 5f);
            _cpr = Mathf.MoveTowards(_cpr, m.Cpr ? 1f : 0f, dt * 4f);
            _climb = Mathf.MoveTowards(_climb, m.Climbing ? 1f : 0f, dt * 6f);
            _seat = Mathf.MoveTowards(_seat, m.Seated ? 1f : 0f, dt * 6f);
            _down = Mathf.MoveTowards(_down, m.Pose == AvatarPose.Down ? 1f : 0f, dt * 3.5f);
            _kneel = Mathf.MoveTowards(_kneel, m.Pose == AvatarPose.Kneel ? 1f : 0f, dt * 4f);
            _scared = Mathf.MoveTowards(_scared, m.Pose == AvatarPose.Scared ? 1f : 0f, dt * 5f);
            _handsUp = Mathf.MoveTowards(_handsUp, m.Pose == AvatarPose.HandsUp ? 1f : 0f, dt * 5f);
            _lie = Mathf.MoveTowards(_lie, m.Pose == AvatarPose.Lie ? 1f : 0f, dt * 2f);
            _lieFront = Mathf.MoveTowards(_lieFront, m.Pose == AvatarPose.LieFront ? 1f : 0f, dt * 2f);
            _sit = Mathf.MoveTowards(_sit, m.Pose == AvatarPose.Sit ? 1f : 0f, dt * 2.5f);
            _chair = Mathf.MoveTowards(_chair, m.Pose == AvatarPose.SitChair ? 1f : 0f, dt * 3f);

            // One cycle = two steps; stride grows with speed so feet don't skate.
            float stride = StepLength;
            _phase = Mathf.Repeat(_phase + speed * dt / (2f * stride) + (_turningInPlace ? dt * 1.3f : 0f), 1f);
            // One arm cycle (two strokes) carries a crawling swimmer about 1.8 m, so the arms keep pace with the
            // body instead of churning in place; treading sculls at an easy fixed rate.
            float strokeRate = Mathf.Clamp(speed / 1.8f, 0.45f, 1.1f);
            _swimPhase = Mathf.Repeat(_swimPhase + dt * Mathf.Lerp(0.45f, strokeRate, _swimMove), 1f);
        }

        private float GestureT(float duration) => Mathf.Clamp01((Now - _gestureStart) / duration);
        private bool GestureActive(AvatarGesture g, float duration) => _gesture == g && Now - _gestureStart < duration;

        private Transform B(Bone b) => _rig[b];

        private static float Wave(float cycles) => Mathf.Sin(cycles * Mathf.PI * 2f);

        // ------------------------------------------------------------------ body

        private void PoseBody()
        {
            float s = _rig.Scale;
            float land = 1f - _swim;
            float stepBob = -Mathf.Abs(Wave(_phase)) * (0.025f + 0.035f * _run) * _move * land;
            float drop = _crouch * 0.32f * s * land + _cpr * 0.42f * s * land;
            float pumpDip = Mathf.Max(0f, 1f - (Now - _lastPump) / 0.25f) * 0.05f * s * _cpr;
            float breathe = Mathf.Sin(Now * 1.7f) * 0.004f;

            // Swimming: lie forward (crawl), stand up to tread water, follow the look direction underwater.
            float swimPitch = Mathf.Lerp(Mathf.Lerp(8f, 72f, _swimMove), Mathf.Clamp(90f + Motion.LookPitch, 10f, 170f) * _swimMove + 12f * (1f - _swimMove), _under);
            float lift = _swim * Mathf.Lerp(0.18f, 0.32f, _swimMove * (1f - _under)) * s;
            float stroke = Wave(_swimPhase) * _swim * _swimMove * (1f - _under);
            float swimBob = Mathf.Sin(_swimPhase * Mathf.PI * 4f) * 0.025f * s * _swim * _swimMove;

            Transform hips = B(Bone.Hips);
            hips.localPosition = _rig.RestPosition(Bone.Hips) + new Vector3(0f, stepBob - drop - pumpDip + lift + breathe + swimBob + StandTall() * land, 0f);
            float lean = (4f + 9f * _run) * _move * land + _crouch * 18f * land + _cpr * 34f + _air * -6f;
            hips.localRotation = Quaternion.Euler(lean * 0.35f + swimPitch * _swim, Wave(_phase) * 4f * _move * land, stroke * 7f);

            B(Bone.Spine).localRotation = Quaternion.Euler(lean * 0.35f + Mathf.Sin(Now * 1.7f) * 1.2f, -Wave(_phase) * 5f * _move * land, -stroke * 2.5f);
            // The chest takes a share of looking around (before the arms, which hang off it).
            B(Bone.Chest).localRotation = Quaternion.Euler(lean * 0.3f + LookPitch * 0.15f, -Wave(_phase) * 5f * _move * land + LookYaw * 0.25f, stroke * 1.5f);
        }

        /// <summary>
        /// How far to lift the hips so a planted leg is straight: the legs reach a bit further than the hips are high
        /// (more so on the generated bodies), and walking plants each foot half a step away, so without this every
        /// step sags into bent knees. Faded out for crouching, kneeling, sitting and lying.
        /// </summary>
        private float StandTall()
        {
            float reach = (_rig.ThighLength + _rig.ShinLength) * 0.985f;
            float height = _rig.RestPosition(Bone.Hips).y + _rig.RestPosition(Bone.ThighL).y - _rig.AnkleHeight;
            float half = StepLength * 0.25f * _move; // where the planted foot is, on average
            float straight = Mathf.Sqrt(Mathf.Max(0f, reach * reach - half * half));
            float calm = (1f - _crouch) * (1f - _cpr) * (1f - _kneel) * (1f - _down) * (1f - _sit) * (1f - _chair) * (1f - _lie) * (1f - _lieFront) * (1f - _seat);
            return Mathf.Clamp(straight - height, 0f, 0.08f * _rig.Scale) * calm;
        }

        /// <summary>One step (the body travels this far per half cycle), so planted feet stay put on the ground.</summary>
        private float StepLength => Mathf.Lerp(0.62f, 1.05f, _run) * _rig.Scale;

        private float LookYaw => Mathf.Clamp(Mathf.DeltaAngle(_bodyYaw, Motion.FacingYaw), -85f, 85f);
        private float LookPitch => Mathf.Clamp(Motion.LookPitch, -70f, 70f) * (1f - _under); // underwater the whole body aims

        // ------------------------------------------------------------------ legs

        private void PoseLegs()
        {
            PoseLeg(Bone.ThighL, Bone.ShinL, Bone.FootL, 0f, -1f);
            PoseLeg(Bone.ThighR, Bone.ShinR, Bone.FootR, 0.5f, 1f);
        }

        private void PoseLeg(Bone thighBone, Bone shinBone, Bone footBone, float offset, float side)
        {
            Transform thigh = B(thighBone), shin = B(shinBone), foot = B(footBone);
            float s = _rig.Scale;
            float q = Mathf.Repeat(_phase + offset, 1f);

            if (_swim > 0.01f)
            {
                // FK kicks: fast flutter while swimming, slow bicycling while treading water, frog kick underwater.
                float flutter = Mathf.Sin((_swimPhase * 4f + offset) * Mathf.PI * 2f) * 24f;
                float bike = Mathf.Sin((_swimPhase + offset) * Mathf.PI * 2f);
                float thighX = Mathf.Lerp(-35f * Mathf.Max(0f, bike) - 10f, flutter, _swimMove);
                float kneeBend = Mathf.Lerp(40f + 40f * Mathf.Max(0f, bike), 12f + Mathf.Max(0f, -flutter) * 0.5f, _swimMove);
                float frog = Mathf.Max(0f, Mathf.Sin(_swimPhase * Mathf.PI * 2f));
                thighX = Mathf.Lerp(thighX, -30f * frog, _under);
                kneeBend = Mathf.Lerp(kneeBend, 90f * frog + 5f, _under);
                float spread = Mathf.Lerp(8f, 25f * frog, _under);
                Quaternion swimThigh = Quaternion.Euler(thighX, 0f, spread * side);
                thigh.localRotation = Quaternion.Slerp(thigh.localRotation, swimThigh, _swim);
                shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(kneeBend, 0f, 0f), _swim);
                foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(45f, 0f, 0f), _swim);
                if (_swim > 0.99f) return;
            }

            // Stepping: the foot is planted during stance and swings forward in an arc.
            Vector3 local = transform.InverseTransformDirection(new Vector3(_smoothVelocity.x, 0f, _smoothVelocity.z));
            Vector3 moveDir = local.sqrMagnitude > 0.01f ? local.normalized : Vector3.forward;
            // Planted, a foot slides back exactly as far as the body moves on in half a cycle (one step), so it
            // stays put on the ground; it only shortens while starting and stopping.
            float stride = StepLength * Mathf.SmoothStep(0f, 1f, _move);
            float along, lift;
            if (q < 0.5f)
            {
                along = Mathf.Lerp(0.5f, -0.5f, q / 0.5f) * stride;
                lift = 0f;
            }
            else
            {
                float t = (q - 0.5f) / 0.5f;
                along = Mathf.Lerp(-0.5f, 0.5f, Mathf.SmoothStep(0f, 1f, t)) * stride;
                lift = Mathf.Sin(t * Mathf.PI) * (0.1f + 0.1f * _run) * s * _move;
            }
            Vector3 hipRest = _rig.RestPosition(Bone.Hips) + _rig.RestPosition(thighBone);
            float footWidth = Mathf.Abs(hipRest.x) * (1f + 0.15f * _crouch);
            var target = new Vector3(footWidth * side, _rig.AnkleHeight + lift, 0f) + moveDir * along;

            // Kneeling for CPR: shins flat on the sand behind the knees.
            target = Vector3.Lerp(target, new Vector3(footWidth * side * 1.1f, _rig.AnkleHeight * 0.8f, -0.42f * s), _cpr);
            // Airborne: tuck the feet up a little.
            target = Vector3.Lerp(target, new Vector3(footWidth * side, 0.3f * s, (side > 0 ? -0.05f : 0.1f) * s), _air);
            // Climbing out: dangle.
            target = Vector3.Lerp(target, new Vector3(footWidth * side, 0.05f * s, -0.05f * s), _climb);

            Vector3 world = transform.TransformPoint(target);
            Vector3 knee = transform.forward;
            float weight = 1f - _swim;
            IK.Solve(thigh, shin, _rig.ThighLength, _rig.ShinLength, world, knee, weight, true);

            // Feet flat on the ground (toes point where the body faces), rolling a little while stepping.
            float roll = q >= 0.5f ? Mathf.Sin((q - 0.5f) * 2f * Mathf.PI) * 25f * _move : 0f;
            Quaternion flat = transform.rotation * Quaternion.Euler(-roll + _cpr * 70f, 0f, 0f);
            foot.rotation = Quaternion.Slerp(foot.rotation, flat, weight);
        }

        // ------------------------------------------------------------------ arms

        private void PoseArms()
        {
            float land = 1f - _swim;
            float swing = (24f + 26f * _run) * _move * land;
            float elbow = Mathf.Lerp(12f, 85f, _run) * _move + 8f;
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                float side = left ? -1f : 1f;
                Transform upper = B(left ? Bone.UpperArmL : Bone.UpperArmR);
                Transform fore = B(left ? Bone.ForearmL : Bone.ForearmR);

                // Base: relaxed, counter-swinging with the legs.
                float armSwing = (left ? -1f : 1f) * Mathf.Cos(_phase * Mathf.PI * 2f) * swing;
                float idleSway = Mathf.Sin(Now * 1.1f + i) * 2f * (1f - _move);
                float spread = 7f + 5f * _run + _air * 35f + _crouch * 6f;
                float raise = _air * 20f;
                float back = 16f * _move * land; // hands trail a little: the swing centres behind the hip, not in front
                upper.localRotation = Quaternion.Euler(-armSwing - raise + idleSway + back, 0f, spread * side);
                fore.localRotation = Quaternion.Euler(-elbow - _air * 25f, 0f, 0f);

                PoseSwimArm(upper, fore, side, i);

                if (_climb > 0.01f)
                {
                    upper.localRotation = Quaternion.Slerp(upper.localRotation, Quaternion.Euler(-150f, 0f, 12f * side), _climb);
                    fore.localRotation = Quaternion.Slerp(fore.localRotation, Quaternion.Euler(-35f, 0f, 0f), _climb);
                }
            }

            ArmIKLayers();
            GestureLayer();
            PoseHands();
        }

        /// <summary>
        /// Wrists and fingers: loose fists when running, flat when swimming or pressing a chest, the grip's hand pose
        /// on held things (palm on the item, fingers wrapped round), an open hand to wave, a pointing finger to press.
        /// </summary>
        private void PoseHands()
        {
            AvatarMotion m = Motion;
            Vector3 fwd = transform.forward;
            for (int i = 0; i < 2; i++)
            {
                bool right = i == 1;
                float side = right ? 1f : -1f;
                HandBones hand = _rig.Hand(right);
                if (hand == null) continue;
                HandPose pose = HandPose.Lerp(HandPose.Relaxed, HandPose.LooseFist, _run * _move * (1f - _swim));
                pose = HandPose.Lerp(pose, HandPose.Swim, _swim);
                Quaternion? rotation = null;
                float weight = 0f;

                HandGrip grip = right ? m.GripRight : m.GripLeft;
                bool usesGrip = grip.Active && (right || m.TwoHanded || m.CarryingPerson);
                if (usesGrip && _hold > 0.01f)
                {
                    weight = _hold * (1f - _cpr);
                    rotation = grip.Rotation(side);
                    pose = HandPose.Lerp(pose, grip.Pose, weight);
                }
                if (_cpr > 0.01f)
                {
                    rotation = HandBones.Orient(fwd, Vector3.down, side);
                    weight = _cpr;
                    pose = HandPose.Lerp(pose, HandPose.Flat, _cpr);
                }
                if (right && _eat > 0.01f) pose = HandPose.Lerp(pose, HandPose.Cup, _eat);
                if (right && GestureActive(AvatarGesture.Wave, 1.6f))
                {
                    rotation = HandBones.Orient(Vector3.up, fwd, side);
                    weight = Mathf.Clamp01(Mathf.Min(GestureT(1.6f) / 0.15f, (1f - GestureT(1.6f)) / 0.15f));
                    pose = HandPose.Lerp(pose, HandPose.Wave, weight);
                }
                else if (right && GestureActive(AvatarGesture.Interact, 0.4f))
                    pose = HandPose.Lerp(pose, HandPose.Point, Mathf.Sin(GestureT(0.4f) * Mathf.PI));

                if (rotation.HasValue && weight > 0f)
                    hand.Hand.rotation = Quaternion.Slerp(hand.Hand.rotation, rotation.Value, weight);
                if (right)
                {
                    _poseR = HandPose.Towards(_poseR, pose, 14f, _dt);
                    hand.Pose(_poseR);
                }
                else
                {
                    _poseL = HandPose.Towards(_poseL, pose, 14f, _dt);
                    hand.Pose(_poseL);
                }
            }
        }

        private void PoseSwimArm(Transform upper, Transform fore, float side, int index)
        {
            if (_swim <= 0.01f) return;
            // Crawl: each arm windmills, half a cycle apart. The body lies face down, so a positive swing (the arm's
            // "backwards" on land) carries the hand from the hip up over the back through the air, out ahead of the
            // head, then pulls it down under the chest back to the hip: recovery in the air, pull in the water.
            float crawlAngle = Mathf.Repeat(_swimPhase + index * 0.5f, 1f) * 360f;
            Quaternion crawl = Quaternion.Euler(crawlAngle, 0f, 14f * side);
            Quaternion crawlElbow = Quaternion.Euler(-Mathf.Lerp(10f, 60f, Mathf.Max(0f, Mathf.Sin(crawlAngle * Mathf.Deg2Rad))), 0f, 0f);
            // Treading water: sculling in front of the chest.
            float scull = Mathf.Sin(_swimPhase * Mathf.PI * 4f + index * Mathf.PI);
            Quaternion tread = Quaternion.Euler(-45f, scull * 25f * side, (40f + scull * 10f) * side);
            Quaternion treadElbow = Quaternion.Euler(-55f, 0f, 0f);
            // Breaststroke underwater: reach ahead, sweep out and back.
            float bs = Mathf.Repeat(_swimPhase, 1f);
            float reach = bs < 0.5f ? Mathf.Lerp(-170f, -95f, bs / 0.5f) : Mathf.Lerp(-95f, -170f, (bs - 0.5f) / 0.5f);
            float sweep = bs < 0.5f ? Mathf.Lerp(8f, 75f, bs / 0.5f) : Mathf.Lerp(75f, 8f, (bs - 0.5f) / 0.5f);
            Quaternion breast = Quaternion.Euler(reach, 0f, sweep * side);
            Quaternion breastElbow = Quaternion.Euler(bs < 0.5f ? -10f : -70f, 0f, 0f);

            Quaternion armPose = Quaternion.Slerp(Quaternion.Slerp(tread, crawl, _swimMove), breast, _under);
            Quaternion elbowPose = Quaternion.Slerp(Quaternion.Slerp(treadElbow, crawlElbow, _swimMove), breastElbow, _under);
            upper.localRotation = Quaternion.Slerp(upper.localRotation, armPose, _swim);
            fore.localRotation = Quaternion.Slerp(fore.localRotation, elbowPose, _swim);
        }

        /// <summary>Hands on things: held items, the mouth while eating, a chest during CPR, the wind-up of a throw.</summary>
        private void ArmIKLayers()
        {
            AvatarMotion m = Motion;
            Transform upperL = B(Bone.UpperArmL), foreL = B(Bone.ForearmL), upperR = B(Bone.UpperArmR), foreR = B(Bone.ForearmR);
            float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
            Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;
            Vector3 elbowL = -fwd * 0.4f - up * 0.6f - right * 0.5f;
            Vector3 elbowR = -fwd * 0.4f - up * 0.6f + right * 0.5f;

            if (_hold > 0.01f)
            {
                float w = _hold * (1f - _cpr);
                Vector3 gl = m.GripLeft.Point, gr = m.GripRight.Point;
                if (!m.TwoHanded && !m.CarryingPerson)
                {
                    // One hand under it, the other relaxed.
                    IK.Solve(upperR, foreR, la, lb, gr, elbowR, w, false);
                }
                else
                {
                    IK.Solve(upperL, foreL, la, lb, gl, elbowL, w, false);
                    IK.Solve(upperR, foreR, la, lb, gr, elbowR, w, false);
                }
            }

            if (_charge > 0.01f)
            {
                // Wind-up: throwing hand back behind the shoulder, the other one pointing ahead.
                Vector3 shoulder = upperR.position;
                Vector3 back = shoulder + up * 0.2f * _rig.Scale - fwd * 0.3f * _rig.Scale + right * 0.12f;
                IK.Solve(upperR, foreR, la, lb, back, -up + right * 0.3f, _charge, false);
                Vector3 point = upperL.position + fwd * 0.55f + up * 0.05f;
                IK.Solve(upperL, foreL, la, lb, point, elbowL, _charge * 0.7f, false);
            }

            if (_eat > 0.01f)
            {
                Transform head = B(Bone.Head);
                Vector3 mouth = head.TransformPoint(new Vector3(0.03f, 0.07f, 0.24f) * _rig.Scale);
                IK.Solve(upperR, foreR, la, lb, mouth, -up + right * 0.6f, _eat, false);
            }

            if (_cpr > 0.01f)
            {
                // Arms straight down, hands stacked on the chest.
                Vector3 point = m.CprPoint;
                if (point == Vector3.zero) point = transform.position + fwd * 0.55f + up * 0.15f;
                IK.Solve(upperL, foreL, la, lb, point + right * 0.025f + up * 0.03f, -fwd - right, _cpr, false);
                IK.Solve(upperR, foreR, la, lb, point - right * 0.025f + up * 0.05f, -fwd + right, _cpr, false);
            }
        }

        private void GestureLayer()
        {
            Transform upperR = B(Bone.UpperArmR), foreR = B(Bone.ForearmR);
            if (GestureActive(AvatarGesture.Throw, 0.4f))
            {
                // From overhead-back, whip forward and down.
                float t = GestureT(0.4f);
                float e = 1f - (1f - t) * (1f - t);
                float w = t < 0.8f ? 1f : 1f - (t - 0.8f) / 0.2f;
                Quaternion arm = Quaternion.Euler(Mathf.Lerp(-200f, -35f, e), 0f, 10f);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, arm, w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-Mathf.Lerp(80f, 5f, e), 0f, 0f), w);
            }
            else if (GestureActive(AvatarGesture.Interact, 0.4f))
            {
                float t = GestureT(0.4f);
                float w = Mathf.Sin(t * Mathf.PI);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-75f, 0f, 4f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-12f, 0f, 0f), w);
            }
            else if (GestureActive(AvatarGesture.Wave, 1.6f))
            {
                float t = GestureT(1.6f);
                float w = Mathf.Clamp01(Mathf.Min(t / 0.15f, (1f - t) / 0.15f));
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-20f, 0f, 150f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(0f, 0f, 25f * Mathf.Sin(Now * 14f)), w);
            }
        }

        // ------------------------------------------------------------------ special poses (on top of everything)

        /// <summary>
        /// Seated on a vehicle, knocked out on the back, kneeling and begging, scared, hands up; and the aimed
        /// gestures (punch, rescue breath, defibrillator zap, recoil). Blended over the normal pose.
        /// </summary>
        private void PoseSpecial()
        {
            float s = _rig.Scale;
            Transform hips = B(Bone.Hips);
            Transform upperL = B(Bone.UpperArmL), foreL = B(Bone.ForearmL), upperR = B(Bone.UpperArmR), foreR = B(Bone.ForearmR);

            if (_seat > 0.01f)
            {
                // Sitting astride: hips down onto the seat, thighs forward and apart, shins down to the footrests.
                hips.localPosition = Vector3.Lerp(hips.localPosition, _rig.RestPosition(Bone.Hips) + new Vector3(0f, -0.4f * s, 0f), _seat);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(8f, 0f, 0f), _seat);
                foreach (bool left in new[] { true, false })
                {
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(-78f, 0f, 16f * side), _seat);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(84f, 0f, 0f), _seat);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(-6f, 0f, 0f), _seat);
                }
            }

            if (_kneel > 0.01f)
            {
                // On the knees, hands together in front of the chest: "please, please!"
                float bob = Mathf.Sin(Now * 7f) * 0.03f * s;
                hips.localPosition = Vector3.Lerp(hips.localPosition, _rig.RestPosition(Bone.Hips) + new Vector3(0f, -0.42f * s + bob, 0f), _kneel);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(6f, 0f, 0f), _kneel);
                foreach (bool left in new[] { true, false })
                {
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(-8f, 0f, (left ? -1f : 1f) * 6f), _kneel);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(95f, 0f, 0f), _kneel);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(-60f, 0f, 0f), _kneel);
                }
                Vector3 hands = B(Bone.Chest).TransformPoint(new Vector3(0f, 0.1f * s, 0.3f * s)) + Vector3.up * (Mathf.Sin(Now * 7f) * 0.04f);
                float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
                IK.Solve(upperL, foreL, la, lb, hands - transform.right * 0.03f, -transform.up - transform.right, _kneel, false);
                IK.Solve(upperR, foreR, la, lb, hands + transform.right * 0.03f, -transform.up + transform.right, _kneel, false);
                B(Bone.Head).localRotation = Quaternion.Slerp(B(Bone.Head).localRotation, Quaternion.Euler(-18f, 0f, 0f), _kneel);
            }

            if (_scared > 0.01f || _handsUp > 0.01f)
            {
                // Scared: hands up by the face, shoulders in. Hands up: arms straight up ("don't shoot").
                float shake = Mathf.Sin(Now * 30f) * 2f;
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(-60f + shake, 0f, -35f), _scared);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-120f, 0f, 0f), _scared);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-60f - shake, 0f, 35f), _scared);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-120f, 0f, 0f), _scared);
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(0f, 0f, -165f), _handsUp);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-10f, 0f, 0f), _handsUp);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(0f, 0f, 165f), _handsUp);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-10f, 0f, 0f), _handsUp);
                _rig.LeftHand?.Pose(HandPose.Wave);
                _rig.RightHand?.Pose(HandPose.Wave);
            }

            if (_down > 0.01f)
            {
                // Knocked out flat on the back: arms and legs out like a starfish.
                hips.localPosition = Vector3.Lerp(hips.localPosition, new Vector3(0f, 0.13f * s, -0.45f * s), _down);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(-90f, 0f, 0f), _down);
                B(Bone.Spine).localRotation = Quaternion.Slerp(B(Bone.Spine).localRotation, Quaternion.identity, _down);
                B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.identity, _down);
                B(Bone.Neck).localRotation = Quaternion.Slerp(B(Bone.Neck).localRotation, Quaternion.Euler(-10f, 0f, 0f), _down);
                B(Bone.Head).localRotation = Quaternion.Slerp(B(Bone.Head).localRotation, Quaternion.Euler(-10f, 25f, 0f), _down);
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(0f, 0f, -80f), _down);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-20f, 0f, 0f), _down);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(0f, 0f, 95f), _down);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-35f, 0f, 0f), _down);
                foreach (bool left in new[] { true, false })
                {
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(0f, 0f, 14f * side), _down);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(8f, 0f, 0f), _down);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(-50f, 0f, 0f), _down);
                }
            }

            BeachPoses(hips, upperL, foreL, upperR, foreR, s);
            AimedGestures(upperL, foreL, upperR, foreR);
        }

        /// <summary>
        /// Lazing about on a towel: on the back with the hands behind the head and one knee up, on the belly
        /// propped on the elbows with a lazy kick, or sitting leaning back on the hands.
        /// </summary>
        private void BeachPoses(Transform hips, Transform upperL, Transform foreL, Transform upperR, Transform foreR, float s)
        {
            float t = Now;
            if (_lie > 0.01f)
            {
                float w = _lie;
                hips.localPosition = Vector3.Lerp(hips.localPosition, new Vector3(0f, 0.12f * s, -0.45f * s), w);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(-90f, 0f, 0f), w);
                B(Bone.Spine).localRotation = Quaternion.Slerp(B(Bone.Spine).localRotation, Quaternion.identity, w);
                B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.Euler(Mathf.Sin(t * 1.2f) * 1.5f, 0f, 0f), w);
                B(Bone.Neck).localRotation = Quaternion.Slerp(B(Bone.Neck).localRotation, Quaternion.Euler(12f, 0f, 0f), w);
                B(Bone.Head).localRotation = Quaternion.Slerp(B(Bone.Head).localRotation, Quaternion.Euler(8f, 0f, 0f), w);
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(0f, 0f, -150f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-125f, 0f, 0f), w);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(0f, 0f, 150f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-125f, 0f, 0f), w);
                foreach (bool left in new[] { true, false })
                {
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    bool kneeUp = !left;
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, kneeUp ? Quaternion.Euler(-45f, 0f, 6f * side) : Quaternion.Euler(0f, 0f, 8f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(kneeUp ? 85f : 4f, 0f, 0f), w);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(kneeUp ? -40f : -45f, 0f, 0f), w);
                }
                // Hands under the back of the head, elbows out to the sides. By IK: the fixed forearm bend above
                // folded the hands forward, onto the face, on bodies with other arm lengths (the Meshy ones).
                Transform head = B(Bone.Head);
                float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
                // Elbows out and a little up off the ground: flat on it, the arms swung so far back that the armpits
                // stretched thin.
                Vector3 nape = head.position + head.up * (0.1f * s) - head.forward * (0.08f * s);
                Vector3 across = B(Bone.Chest).right;
                IK.Solve(upperL, foreL, la, lb, nape - across * (0.04f * s), -across + head.up * 0.4f + head.forward * 0.35f, w, false);
                IK.Solve(upperR, foreR, la, lb, nape + across * (0.04f * s), across + head.up * 0.4f + head.forward * 0.35f, w, false);
            }

            if (_lieFront > 0.01f)
            {
                float w = _lieFront;
                hips.localPosition = Vector3.Lerp(hips.localPosition, new Vector3(0f, 0.13f * s, -0.45f * s), w);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(90f, 0f, 0f), w);
                B(Bone.Spine).localRotation = Quaternion.Slerp(B(Bone.Spine).localRotation, Quaternion.Euler(-8f, 0f, 0f), w);
                B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.Euler(-12f, 0f, 0f), w);
                B(Bone.Neck).localRotation = Quaternion.Slerp(B(Bone.Neck).localRotation, Quaternion.Euler(-35f, 0f, 0f), w);
                B(Bone.Head).localRotation = Quaternion.Slerp(B(Bone.Head).localRotation, Quaternion.Euler(-25f, 0f, 0f), w);
                // Propped on the elbows, chin on the hands.
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(-150f, 0f, -18f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-95f, 0f, 0f), w);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-150f, 0f, 18f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-95f, 0f, 0f), w);
                foreach (bool left in new[] { true, false })
                {
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    float kick = 55f + 35f * Mathf.Sin(t * 1.6f + (left ? 0f : 2.2f)); // lazy feet in the air
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(0f, 0f, 6f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(kick, 0f, 0f), w);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(40f, 0f, 0f), w);
                }
            }

            if (_chair > 0.01f)
            {
                // On a stool behind a counter: hips on the seat, feet on the floor, hands resting on the knees.
                float w = _chair;
                hips.localPosition = Vector3.Lerp(hips.localPosition, _rig.RestPosition(Bone.Hips) + new Vector3(0f, -0.42f * s, 0f), w);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(4f, 0f, 0f), w);
                B(Bone.Spine).localRotation = Quaternion.Slerp(B(Bone.Spine).localRotation, Quaternion.Euler(3f + Mathf.Sin(t * 1.1f) * 1.5f, 0f, 0f), w);
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(-28f, 0f, -10f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-55f, 0f, 0f), w);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-28f, 0f, 10f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-55f, 0f, 0f), w);
                foreach (bool left in new[] { true, false })
                {
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(-84f, 0f, 7f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(86f, 0f, 0f), w);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(-4f, 0f, 0f), w);
                }
            }

            if (_sit > 0.01f)
            {
                float w = _sit;
                hips.localPosition = Vector3.Lerp(hips.localPosition, new Vector3(0f, 0.14f * s, -SitBack), w);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(-14f, 0f, 0f), w);
                B(Bone.Spine).localRotation = Quaternion.Slerp(B(Bone.Spine).localRotation, Quaternion.Euler(4f, 0f, 0f), w);
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(38f, 0f, -16f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-4f, 0f, 0f), w);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(38f, 0f, 16f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-4f, 0f, 0f), w);
                foreach (bool left in new[] { true, false })
                {
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(-80f, 0f, 9f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(left ? 20f : 55f, 0f, 0f), w);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(-20f, 0f, 0f), w);
                }
                _rig.LeftHand?.Pose(HandPose.Flat);
                _rig.RightHand?.Pose(HandPose.Flat);
            }
        }

        private void AimedGestures(Transform upperL, Transform foreL, Transform upperR, Transform foreR)
        {
            float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
            Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;
            if (GestureActive(AvatarGesture.Punch, 0.38f))
            {
                // Wind back, then a straight jab into the target (or chest height ahead).
                float t = GestureT(0.38f);
                Vector3 shoulder = upperR.position;
                Vector3 target = _gesturePoint != Vector3.zero ? _gesturePoint : shoulder + fwd * 0.7f * _rig.Scale;
                Vector3 back = shoulder - fwd * 0.12f - up * 0.05f + right * 0.08f;
                float extend = t < 0.3f ? 0f : Mathf.Sin(Mathf.Clamp01((t - 0.3f) / 0.7f) * Mathf.PI);
                Vector3 hand = Vector3.Lerp(back, target, extend);
                float w = t < 0.15f ? t / 0.15f : t > 0.85f ? (1f - t) / 0.15f : 1f;
                IK.Solve(upperR, foreR, la, lb, hand, -up + right * 0.8f, w, false);
                // Guard up with the other hand.
                IK.Solve(upperL, foreL, la, lb, upperL.position + fwd * 0.3f + up * 0.05f + right * 0.1f, -up - right, w * 0.8f, false);
                _rig.RightHand?.Pose(HandPose.Fist);
                _rig.LeftHand?.Pose(HandPose.LooseFist);
                B(Bone.Chest).localRotation *= Quaternion.Euler(0f, -18f * extend, 0f);
            }
            else if (GestureActive(AvatarGesture.Breath, 1.1f))
            {
                // Rescue breath, mouth to mouth: bend right down until our lips are on theirs (head tilted, eyes
                // closed), one hand on the forehead, one lifting the chin; the knees stay where they are.
                float t = GestureT(1.1f);
                float w = Mathf.SmoothStep(0f, 1f, t < 0.28f ? t / 0.28f : t > 0.78f ? (1f - t) / 0.22f : 1f);
                if (_gesturePoint == Vector3.zero)
                {
                    B(Bone.Hips).localRotation *= Quaternion.Euler(28f * w, 0f, 0f);
                    B(Bone.Neck).localRotation *= Quaternion.Euler(25f * w, 0f, 0f);
                }
                else Kiss(_gesturePoint, w, upperL, foreL, upperR, foreR, la, lb);
            }
            else if (GestureActive(AvatarGesture.Zap, 0.6f))
            {
                // Paddles down on the chest, then a jolt.
                float t = GestureT(0.6f);
                float w = t < 0.2f ? t / 0.2f : 1f - Mathf.Clamp01((t - 0.7f) / 0.3f);
                Vector3 chest = _gesturePoint != Vector3.zero ? _gesturePoint : transform.position + fwd * 0.6f + up * 0.3f;
                IK.Solve(upperL, foreL, la, lb, chest + right * 0.12f + up * 0.05f, -fwd - right, w, false);
                IK.Solve(upperR, foreR, la, lb, chest - right * 0.12f + up * 0.05f, -fwd + right, w, false);
            }
            else if (GestureActive(AvatarGesture.Shoot, 0.18f))
            {
                float kick = 1f - GestureT(0.18f);
                upperR.localRotation *= Quaternion.Euler(-14f * kick, 0f, 0f);
                foreR.localRotation *= Quaternion.Euler(-10f * kick, 0f, 0f);
            }
        }

        /// <summary>Our lips, just in front of the head.</summary>
        private Vector3 MouthPoint => B(Bone.Head).TransformPoint(new Vector3(0f, 0.035f, 0.12f) * _rig.Scale);

        /// <summary>
        /// Lips on lips: the upper body pivots at the hips toward the other mouth, the face turns down to meet it
        /// (tipped sideways, the way people kiss), then the hips slide the last bit so the mouths touch. The legs are
        /// posed again afterwards, so the knees stay planted on the sand.
        /// </summary>
        private void Kiss(Vector3 lips, float w, Transform upperL, Transform foreL, Transform upperR, Transform foreR, float la, float lb)
        {
            if (w <= 0.001f) return;
            Transform hips = B(Bone.Hips), head = B(Bone.Head);
            Vector3 fwd = transform.forward, right = transform.right;
            Vector3 target = lips + Vector3.up * 0.015f;

            // 1. Swing the upper body round the hips so the mouth heads for theirs.
            Vector3 pivot = hips.position;
            Quaternion swing = Quaternion.FromToRotation(MouthPoint - pivot, target - pivot);
            hips.rotation = Quaternion.Slerp(Quaternion.identity, swing, w) * hips.rotation;
            // 2. Face down onto theirs, tilted sideways a little.
            Vector3 along = Vector3.ProjectOnPlane(target - pivot, Vector3.up);
            if (along.sqrMagnitude < 1e-4f) along = fwd;
            Quaternion faceDown = Quaternion.LookRotation(Vector3.down + along.normalized * 0.25f, along.normalized) * Quaternion.Euler(0f, 0f, 24f);
            B(Bone.Neck).rotation = Quaternion.Slerp(B(Bone.Neck).rotation, faceDown, w * 0.5f);
            head.rotation = Quaternion.Slerp(head.rotation, faceDown, w);
            // 3. Close the last gap by moving the whole upper body (not far: we're kneeling right next to them).
            Vector3 gap = Vector3.ClampMagnitude(target - MouthPoint, 0.6f * _rig.Scale);
            hips.position += gap * w;
            PoseLegs(); // knees back down where they were

            // Hands: one on the forehead, one under the chin.
            Vector3 over = Vector3.ProjectOnPlane(along, Vector3.up).normalized;
            IK.Solve(upperL, foreL, la, lb, lips + over * 0.12f + Vector3.up * 0.06f - right * 0.04f, -fwd - right, w, false);
            IK.Solve(upperR, foreR, la, lb, lips - over * 0.07f + Vector3.up * 0.01f + right * 0.03f, -fwd + right, w, false);
        }

        // ------------------------------------------------------------------ head & face

        private void PoseHead()
        {
            float yaw = LookYaw;
            float pitch = LookPitch;
            // Lying forward to swim: the head tips back up to look where we're going.
            float swimTilt = _swim * (1f - _under) * Mathf.Lerp(0f, -60f, _swimMove);
            B(Bone.Neck).localRotation = Quaternion.Euler(pitch * 0.3f + swimTilt * 0.4f, yaw * 0.25f, 0f);
            B(Bone.Head).localRotation = Quaternion.Euler(pitch * 0.55f + swimTilt * 0.6f - _cpr * 15f, yaw * 0.5f, 0f);
        }

        private void PoseFace()
        {
            float t = Now;
            if (t > _nextBlink)
            {
                _blinkUntil = t + 0.11f;
                _nextBlink = t + Random.Range(2.2f, 5f);
            }
            float eyes = t < _blinkUntil ? 0.1f : 1f;
            if (Motion.Underwater) eyes = Mathf.Min(eyes, 0.55f); // squinting
            float mouth = 0.05f + _run * 0.35f * _move;
            if (_eat > 0.5f) mouth = Mathf.Abs(Mathf.Sin(t * 9f)) * 0.7f;
            if (Motion.Underwater) mouth = 0f;
            float brows = _charge * 0.8f + _cpr * -0.5f;
            switch (Motion.Mood)
            {
                case AvatarMood.Happy: brows = 0.25f; mouth = Mathf.Max(mouth, 0.15f); break;
                case AvatarMood.Scared: brows = -1f; eyes = Mathf.Max(eyes, 1.4f); mouth = Mathf.Max(mouth, 0.35f); break;
                case AvatarMood.Angry: brows = 1f; eyes = Mathf.Min(eyes, 0.75f); break;
                case AvatarMood.Hurt: brows = -0.8f; eyes = Mathf.Min(eyes, 0.35f); mouth = 0.5f; break;
            }
            if (Motion.Talking) mouth = 0.12f + 0.5f * Mathf.Abs(Mathf.Sin(t * 13f) * Mathf.Sin(t * 5.3f + 1f));
            if (_down > 0.5f) { eyes = 0.08f; mouth = 0.45f; }
            else if (_lie > 0.5f && !Motion.Talking) { eyes = 0.12f; mouth = 0.05f; brows = 0.2f; } // soaking up the sun
            if (GestureActive(AvatarGesture.Breath, 1.1f)) { mouth = 0.08f; eyes = 0.08f; brows = 0.3f; } // a kiss: lips pressed, eyes shut
            _rig.SetExpression(eyes, mouth, brows);
        }
    }

    /// <summary>Two-bone limb IK for bones that point down their local -Y.</summary>
    public static class IK
    {
        /// <param name="bendHint">World direction the elbow/knee should point toward.</param>
        /// <param name="isLeg">Knees bend the lower bone backward (-Z); elbows bend it forward (+Z).</param>
        public static void Solve(Transform upper, Transform lower, float lenA, float lenB, Vector3 target, Vector3 bendHint, float weight, bool isLeg)
        {
            if (weight <= 0.001f) return;
            Vector3 a = upper.position;
            Vector3 toTarget = target - a;
            float distance = toTarget.magnitude;
            if (distance < 1e-4f) return;
            Vector3 dir = toTarget / distance;
            distance = Mathf.Clamp(distance, Mathf.Abs(lenA - lenB) + 0.01f, lenA + lenB - 0.001f);
            float cosA = Mathf.Clamp((lenA * lenA + distance * distance - lenB * lenB) / (2f * lenA * distance), -1f, 1f);
            float sinA = Mathf.Sqrt(1f - cosA * cosA);

            Vector3 bend = Vector3.ProjectOnPlane(bendHint, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.forward, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.right, dir);
            bend.Normalize();

            Vector3 upperDir = dir * cosA + bend * sinA;
            Vector3 joint = a + upperDir * lenA;
            Vector3 lowerDir = (a + dir * distance - joint).normalized;
            Vector3 front = isLeg ? bend : -bend;

            Quaternion upperRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(front, upperDir).normalized, -upperDir);
            upper.rotation = weight >= 0.999f ? upperRot : Quaternion.Slerp(upper.rotation, upperRot, weight);
            Vector3 lowerFront = Vector3.ProjectOnPlane(front, lowerDir);
            if (lowerFront.sqrMagnitude < 1e-6f) lowerFront = Vector3.ProjectOnPlane(upper.forward, lowerDir);
            Quaternion lowerRot = Quaternion.LookRotation(lowerFront.normalized, -lowerDir);
            lower.rotation = weight >= 0.999f ? lowerRot : Quaternion.Slerp(lower.rotation, lowerRot, weight);
        }
    }
}
