using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>One-off moves that play over whatever the body is doing.</summary>
    public enum AvatarGesture : byte { None, Interact, Throw, ChargeStart, ChargeEnd, Pump, Wave, Bite, EatStart, EatStop, Punch, Breath, Zap, Shoot, JumpShot, Burp }

    /// <summary>Whole-body poses held for a while (story characters, knockouts).</summary>
    public enum AvatarPose : byte { Normal, Down, Kneel, Scared, HandsUp, Lie, LieFront, Sit, SitChair, Carried }

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
        public bool Holding;          // hands on an item (grips below)
        public bool TwoHanded;
        public bool CarryingPerson;
        public HandGrip GripLeft, GripRight; // world palm points, finger/palm directions, finger curls
        public float Charge;          // 0..1 throw wind-up
        public bool Eating;
        public bool Drinking;         // eating a drink: the hand keeps its grip on the bottle (tipped up at the lips), the head goes back
        public bool Cpr;
        public Vector3 CprPoint;      // world, the chest being pressed
        public bool Seated;           // on a vehicle seat (hands come from the grips)
        public bool Straddle;         // seated astride a fat seat (a banana boat): knees well apart, feet back down its sides
        public bool FloatSeat;        // seated on a ring: knees together so the legs fit its opening
        public bool Flying;           // shot through the air (a cannon, a throw, flung off the banana): superman, arms out ahead
        public bool StarJump;         // bounced high (a trampoline): arms and legs flung out in a star
        public AvatarPose Pose;
        public AvatarMood Mood;
        public bool Talking;          // flap the mouth
        public bool LookBack;         // running away: glances back over the shoulder now and then
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
        private float _fly, _flyRaw, _star, _starRaw;
        private float _down;
        private float _kneel;
        private float _scared;
        private float _handsUp;
        private float _lie;
        private float _lieFront;
        private float _sit;
        private float _carried; // lying back in someone's arms (picked up by another lifeguard)
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

        // Reactions (story characters): flinching from a blow, falling when knocked out, glancing back while fleeing.
        private float _flinchStart = -10f;
        private Vector3 _flinchPush;
        private float _flinchSide = 1f;
        private float _downSince = -10f;
        private bool _wasDown;
        private float _lookBack, _lookBackRaw;

        // The blends above are eased (slow out of one pose, slow into the next); these run at a steady rate under them.
        private float _swimRaw, _swimMoveRaw, _underRaw, _holdRaw, _eatRaw, _cprRaw, _climbRaw, _seatRaw, _downRaw, _kneelRaw,
            _scaredRaw, _handsUpRaw, _lieRaw, _lieFrontRaw, _sitRaw, _chairRaw, _carriedRaw;
        private float _upright;       // 1 standing or walking .. 0 in any sitting, lying or kneeling pose
        private float _talk;

        // Secondary motion: the body leans into speeding up and turning, sinks on landing, and swings back.
        private Spring _leanForward, _leanSide, _turnLag, _landing;
        private bool _wasAirborne;
        private float _fallSpeed;

        // Feet on the ground that is really there (slopes, steps): how far it is above the root under each foot.
        private static readonly RaycastHit[] _groundHits = new RaycastHit[8];
        private float _groundL, _groundR, _hipDrop, _groundWeight;
        private Vector3 _normalL = Vector3.up, _normalR = Vector3.up;

        // Standing about: looking around and small habits (see IdleAct).
        private Variety _variety = Variety.None;
        private IdleAct _act;
        private float _actStart, _actLength, _nextAct = -1f, _actWeight;
        private Vector2 _glance, _glanceTarget;
        private float _nextGlance = -1f;
        private float _hidden;        // seconds since the last pose, while nobody can see us
        private float _slowSwim;      // seconds a swimmer has been (nearly) still

        /// <summary>Things people do with their hands while they stand around.</summary>
        public enum IdleAct : byte { None, ScratchHead, HandsOnHips, Stretch, ShieldEyes, WipeBrow }

        private const float HiddenInterval = 0.25f;

        /// <summary>
        /// Characters nobody is looking at are only posed a few times a second (their joints stay roughly where they
        /// are, for whatever reads them). For crowds; leave it off for players.
        /// </summary>
        public bool CullWhenHidden { get; set; }

        /// <summary>Glances about and has small habits while standing idle (tourists, not players).</summary>
        public bool Lively { get; set; }

        /// <summary>
        /// Swims breaststroke at the surface instead of the crawl: head up, both hands reaching out in front together
        /// (holidaymakers; a lifeguard in a hurry crawls).
        /// </summary>
        public bool Breaststroke { get; set; }

        /// <summary>
        /// Gives this character its own way of moving (stride, arm swing, posture, rhythm), the same on every machine
        /// for the same seed. 0 = the neutral walk.
        /// </summary>
        public int Seed
        {
            set
            {
                _variety = Variety.From(value);
                _hidden = HiddenInterval * Variety.Unit(value, 7); // crowds don't all pose on the same frame
            }
        }

        public AvatarRig Rig
        {
            get => _rig;
            set => _rig = value;
        }
        public float BodyYaw => _bodyYaw;

        /// <summary>Set: the whole body turned this way instead of upright (stuffed up a cannon's barrel).</summary>
        public Quaternion? RootOverride { get; set; }

        /// <summary>Starts an idle habit now (review tools; in play they come up by themselves).</summary>
        public void Play(IdleAct act, float seconds)
        {
            _act = act;
            _actStart = Now;
            _actLength = seconds;
        }

        public void Play(AvatarGesture gesture) => Play(gesture, Vector3.zero);

        /// <summary>
        /// Hit: the body snaps away from the blow (<paramref name="push"/> = the way it travels, world), the head
        /// whips round, the arms fly, and it all comes back over half a second.
        /// </summary>
        public void Flinch(Vector3 push)
        {
            push.y = 0f;
            _flinchPush = push.sqrMagnitude > 1e-4f ? push.normalized : -transform.forward;
            _flinchSide = Vector3.Dot(transform.right, _flinchPush) >= 0f ? 1f : -1f;
            _flinchStart = Now;
        }

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

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            // (Batch runs draw nothing, so nothing is ever "visible": tests there keep the full rate.)
            if (CullWhenHidden && !Application.isBatchMode && _rig != null && _rig.Renderer != null && !_rig.Renderer.isVisible)
            {
                _hidden += dt;
                if (_hidden < HiddenInterval) return;
                dt = _hidden;
            }
            _hidden = 0f;
            Tick(dt);
        }

        public void Tick(float dt)
        {
            if (_rig == null || !_rig.IsBuilt) return;
            _dt = dt;
            UpdateBlends(dt);
            UpdateIdle();
            SampleGround(dt);
            _rig.ResetPose(fingers: false); // PoseHands sets every finger bone
            PoseBody();
            PoseBurp();
            PoseLegs();
            PoseArms();
            PoseHead();
            PoseSpecial();
            KeepHandsOutOfHead();
            PoseFace();
        }

        /// <summary>
        /// A raised hand never goes into the head: the goofy lifeguard's head is as wide as his shoulders and his arms
        /// are short, so waving, throwing, stretching, diving, flying out of the cannon or a jump shot put his hands
        /// in his face. Such an arm swings out to the side, a few degrees at a time, until the hand is clear. (Not
        /// when the hand is meant to be there: eating, scratching the head, shading the eyes...)
        /// </summary>
        private void KeepHandsOutOfHead()
        {
            // (CPR and the kiss of life put our own head right down by the hands on the chest, forehead and chin.)
            if (_eat > 0.01f || _actWeight > 0.01f || _cpr > 0.01f || GestureActive(AvatarGesture.Breath, 1.1f) || _rig.HeadTop <= 0f) return;
            if (Motion.CarryingPerson && _hold > 0.01f) return; // (arms round the legs of someone on our shoulder, right under our chin)
            Transform head = B(Bone.Head);
            float s = _rig.Scale;
            // The head as a box-ish ellipsoid: as wide as it is, as far forward as the face (and its googly eyes),
            // as far back as the skull, from the jaw to the top.
            Vector3 centre = head.position + head.up * (_rig.HeadTop * 0.45f);
            float margin = 0.07f * s;
            float across = _rig.HeadHalfWidth + margin, tall = _rig.HeadTop * 0.62f + margin;
            float front = Mathf.Max(_rig.HeadFront, _rig.HeadHalfWidth) + margin + 0.04f * s, back = Mathf.Max(_rig.HeadBack, _rig.HeadHalfWidth * 0.8f) + margin;
            Vector3 up = head.up, ahead = Vector3.ProjectOnPlane(head.forward, up).normalized, sideways = Vector3.Cross(up, ahead);
            for (int i = 0; i < 2; i++)
            {
                bool right = i == 1;
                Transform upper = B(right ? Bone.UpperArmR : Bone.UpperArmL), hand = B(right ? Bone.HandR : Bone.HandL);
                if (upper == null || hand == null) continue;
                float side = right ? 1f : -1f;
                for (int step = 0; step < 16; step++)
                {
                    Vector3 off = hand.position - centre;
                    float along = Vector3.Dot(off, up), fwd = Vector3.Dot(off, ahead), wide = Vector3.Dot(off, sideways);
                    float depth = fwd >= 0f ? front : back;
                    float inside = wide * wide / (across * across) + fwd * fwd / (depth * depth) + along * along / (tall * tall);
                    if (inside >= 1.2f) break; // (clear of it, not grazing it)
                    // Swing the arm so the hand moves straight away from the head (out to the side for a raised arm,
                    // into a V for arms reaching ahead past the face).
                    Vector3 arm = hand.position - upper.position;
                    Vector3 away = Vector3.ProjectOnPlane(off, arm);
                    if (away.sqrMagnitude < 1e-6f) away = sideways * side;
                    Vector3 axis = Vector3.Cross(arm, away);
                    if (axis.sqrMagnitude < 1e-8f) break;
                    upper.rotation = Quaternion.AngleAxis(6f, axis.normalized) * upper.rotation;
                }
            }
        }

        // ------------------------------------------------------------------ state

        private void UpdateBlends(float dt)
        {
            AvatarMotion m = Motion;
            float k(float rate) => 1f - Mathf.Exp(-rate * dt);
            Vector3 velocityBefore = _smoothVelocity;
            _smoothVelocity = Vector3.Lerp(_smoothVelocity, m.Velocity, k(10f));
            var flat = new Vector3(_smoothVelocity.x, 0f, _smoothVelocity.z);
            float speed = flat.magnitude;

            // The body follows the head: quickly while moving, in a little turn-in-place when looking far aside.
            if (!_yawInitialised) { _bodyYaw = m.FacingYaw; _yawInitialised = true; }
            float yawBefore = _bodyYaw;
            float delta = Mathf.DeltaAngle(_bodyYaw, m.FacingYaw);
            bool moving = speed > 0.4f || m.Swimming || m.Holding;
            if (moving || Mathf.Abs(delta) > 55f) _turningInPlace = !moving;
            if (_turningInPlace && Mathf.Abs(delta) < 4f) _turningInPlace = false;
            if (moving || _turningInPlace)
                _bodyYaw += delta * k(moving ? _turnSpeed : _turnSpeed * 0.6f);
            transform.rotation = RootOverride ?? Quaternion.Euler(0f, _bodyYaw, 0f);

            _move = Mathf.MoveTowards(_move, Mathf.InverseLerp(0.15f, 1.4f, speed) + (_turningInPlace ? 0.35f : 0f), dt * 4f);
            _move = Mathf.Clamp01(_move);
            _run = Mathf.MoveTowards(_run, Mathf.InverseLerp(4.8f, 7f, speed), dt * 3f);
            _swim = Eased(ref _swimRaw, m.Swimming, 3f, dt);
            // Crawl while making way, tread water when (nearly) still; the gap between the two thresholds keeps a
            // slow swimmer, or one nudged sideways, from flickering between lying flat and standing up in the water.
            // A swimmer who only stops for a moment (turning at the end of a length) glides on stretched out; it takes
            // a second and a half of staying put before they come upright.
            _slowSwim = speed > 0.18f ? 0f : _slowSwim + dt;
            _crawling = m.Swimming && (_crawling ? _slowSwim < 1.5f : speed > 0.35f);
            _swimMove = Eased(ref _swimMoveRaw, _crawling, 2.5f, dt);
            _under = Eased(ref _underRaw, m.Swimming && m.Underwater, 2.5f, dt);
            bool airborne = !m.Grounded && !m.Swimming && !m.Climbing && m.Pose != AvatarPose.Carried; // (carried: held, not falling)
            _air = Mathf.MoveTowards(_air, airborne ? 1f : 0f, dt * 7f);
            _crouch = Mathf.Lerp(_crouch, m.Crouch, k(12f));
            _hold = Eased(ref _holdRaw, m.Holding, 6f, dt);
            _charge = Mathf.MoveTowards(_charge, m.Charge, dt * 8f);
            _eat = Eased(ref _eatRaw, m.Eating, 5f, dt);
            _cpr = Eased(ref _cprRaw, m.Cpr, 4f, dt);
            _climb = Eased(ref _climbRaw, m.Climbing, 6f, dt);
            _seat = Eased(ref _seatRaw, m.Seated, 6f, dt);
            _fly = Eased(ref _flyRaw, m.Flying, 5f, dt);
            _star = Eased(ref _starRaw, m.StarJump && !m.Flying, 7f, dt);
            _down = Eased(ref _downRaw, m.Pose == AvatarPose.Down, 3.5f, dt);
            if (m.Pose == AvatarPose.Down && !_wasDown) _downSince = Now;
            _wasDown = m.Pose == AvatarPose.Down;
            _lookBack = Eased(ref _lookBackRaw, m.LookBack && speed > 1.5f, 3f, dt);
            _kneel = Eased(ref _kneelRaw, m.Pose == AvatarPose.Kneel, 4f, dt);
            _scared = Eased(ref _scaredRaw, m.Pose == AvatarPose.Scared, 5f, dt);
            _handsUp = Eased(ref _handsUpRaw, m.Pose == AvatarPose.HandsUp, 5f, dt);
            _lie = Eased(ref _lieRaw, m.Pose == AvatarPose.Lie, 2f, dt);
            _lieFront = Eased(ref _lieFrontRaw, m.Pose == AvatarPose.LieFront, 2f, dt);
            _carried = Eased(ref _carriedRaw, m.Pose == AvatarPose.Carried, 6f, dt);
            _sit = Eased(ref _sitRaw, m.Pose == AvatarPose.Sit, 2.5f, dt);
            _chair = Eased(ref _chairRaw, m.Pose == AvatarPose.SitChair, 3f, dt);
            _upright = (1f - _cpr) * (1f - _kneel) * (1f - _down) * (1f - _sit) * (1f - _chair) * (1f - _lie) * (1f - _lieFront) * (1f - _seat) * (1f - _carried);
            _talk = Mathf.MoveTowards(_talk, m.Talking ? 1f : 0f, dt * 5f);

            // Secondary motion. Speeding up tips the body into it and it swings back; a turn drags the chest behind;
            // landing from a jump sinks the hips (the knees give, the feet stay planted) before they come back up.
            float perSecond = dt > 1e-5f ? 1f / dt : 0f;
            Vector3 push = transform.InverseTransformDirection((_smoothVelocity - velocityBefore) * perSecond);
            _leanForward.Tick(Mathf.Clamp(push.z * 0.45f, -7f, 7f), 55f, 8f, dt);
            _leanSide.Tick(Mathf.Clamp(-push.x * 0.45f, -7f, 7f), 55f, 8f, dt);
            _turnLag.Tick(Mathf.Clamp(-Mathf.DeltaAngle(yawBefore, _bodyYaw) * perSecond * 0.035f, -14f, 14f), 45f, 7f, dt);
            if (airborne) _fallSpeed = Mathf.Max(_fallSpeed, -m.Velocity.y);
            else if (_wasAirborne)
            {
                _landing.Velocity -= Mathf.Clamp(_fallSpeed, 2f, 10f) * 0.2f * _rig.Scale;
                _fallSpeed = 0f;
            }
            _wasAirborne = airborne;
            _landing.Tick(0f, 110f, 13f, dt);
            _landing.Value = Mathf.Max(_landing.Value, -0.22f * _rig.Scale);

            // One cycle = two steps; stride grows with speed so feet don't skate.
            float stride = StepLength;
            _phase = Mathf.Repeat(_phase + speed * dt / (2f * stride) + (_turningInPlace ? dt * 1.3f : 0f), 1f);
            // One arm cycle (two strokes) carries a crawling swimmer about 1.8 m, so the arms keep pace with the
            // body instead of churning in place; treading sculls at an easy fixed rate.
            float strokeRate = Mathf.Clamp(speed / 1.8f, 0.45f, 1.1f);
            _swimPhase = Mathf.Repeat(_swimPhase + dt * Mathf.Lerp(0.45f, strokeRate, _swimMove), 1f);
        }

        /// <summary>Moves a blend at a steady rate and returns it eased at both ends (no sudden start, no sudden stop).</summary>
        private static float Eased(ref float raw, bool on, float rate, float dt)
        {
            raw = Mathf.MoveTowards(raw, on ? 1f : 0f, dt * rate);
            return raw * raw * (3f - 2f * raw);
        }

        /// <summary>A damped spring on one number (it overshoots a little and settles).</summary>
        private struct Spring
        {
            public float Value, Velocity;

            public void Tick(float target, float stiffness, float damping, float dt)
            {
                if (dt <= 0f) return;
                // Small steps: a pose that was skipped for a while (hidden characters) must not blow the spring up.
                int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.017f), 1, 16);
                float h = dt / steps;
                for (int i = 0; i < steps; i++)
                {
                    Velocity += ((target - Value) * stiffness - Velocity * damping) * h;
                    Value += Velocity * h;
                }
            }
        }

        /// <summary>One character's own way of moving, so a crowd doesn't walk in step.</summary>
        private struct Variety
        {
            public float Stride;      // step length (longer steps = slower cadence at the same speed)
            public float Swing;       // arm swing
            public float Bounce;      // up and down per step
            public float Slouch;      // degrees the upper back leans forward (negative: chest out)
            public float HeadTilt;    // degrees the head sits to one side
            public float Spread;      // degrees the arms hang out from the body
            public float BreatheRate;
            public float SwayRate;    // shifting weight from foot to foot while standing
            public float Phase;       // so idle rhythms are out of step with everyone else's

            public static readonly Variety None = new() { Stride = 1f, Swing = 1f, Bounce = 1f, BreatheRate = 1.7f, SwayRate = 0.7f };

            public static Variety From(int seed)
            {
                if (seed == 0) return None;
                return new Variety
                {
                    Stride = Mathf.Lerp(0.9f, 1.1f, Unit(seed, 1)),
                    Swing = Mathf.Lerp(0.7f, 1.35f, Unit(seed, 2)),
                    Bounce = Mathf.Lerp(0.75f, 1.3f, Unit(seed, 3)),
                    Slouch = Mathf.Lerp(-2.5f, 6f, Unit(seed, 4)),
                    HeadTilt = Mathf.Lerp(-3.5f, 3.5f, Unit(seed, 5)),
                    Spread = Mathf.Lerp(-1.5f, 5f, Unit(seed, 6)),
                    BreatheRate = Mathf.Lerp(1.3f, 2.1f, Unit(seed, 8)),
                    SwayRate = Mathf.Lerp(0.5f, 0.95f, Unit(seed, 9)),
                    Phase = Unit(seed, 10) * 40f
                };
            }

            /// <summary>A fixed number 0..1 for a seed and a slot (integer hash: no shared random state touched).</summary>
            public static float Unit(int seed, int slot)
            {
                unchecked
                {
                    uint h = (uint)seed * 2654435761u + (uint)slot * 40503u;
                    h ^= h >> 15;
                    h *= 2246822519u;
                    h ^= h >> 13;
                    h *= 3266489917u;
                    h ^= h >> 16;
                    return (h & 0xFFFFFF) / (float)0x1000000;
                }
            }
        }

        // ------------------------------------------------------------------ idle life

        /// <summary>1 while standing still with nothing to do (the only time people fidget).</summary>
        private float IdleWeight => (1f - _move) * (1f - _swim) * (1f - _air) * (1f - _crouch) * (1f - _hold) * (1f - _charge) *
                                    (1f - _eat) * (1f - _climb) * (1f - _scared) * (1f - _handsUp) * _upright;

        /// <summary>Picks when to glance somewhere else and when to start a habit.</summary>
        private void UpdateIdle()
        {
            float now = Now;
            float idle = IdleWeight;
            bool free = idle > 0.95f && now - _gestureStart > 1.6f;

            // Glances: now and then the head turns to something else for a moment.
            if (_nextGlance < 0f) _nextGlance = now + Random.Range(1f, 6f);
            if (now > _nextGlance)
            {
                bool away = _glanceTarget == Vector2.zero && Lively && free && !Motion.Talking;
                _glanceTarget = away ? new Vector2(Random.Range(-38f, 38f), Random.Range(-6f, 9f)) : Vector2.zero;
                _nextGlance = now + (away ? Random.Range(0.9f, 2.6f) : Random.Range(2.5f, 7f));
            }
            if (!free) _glanceTarget = Vector2.zero;
            _glance = Vector2.Lerp(_glance, _glanceTarget, 1f - Mathf.Exp(-5f * _dt));

            // Habits.
            if (_nextAct < 0f) _nextAct = now + Random.Range(3f, 16f);
            if (_act != IdleAct.None)
            {
                float t = now - _actStart;
                if (t > _actLength || t < 0f) // (t < 0: a review tool rewound the clock)
                {
                    _act = IdleAct.None;
                    _nextAct = now + Random.Range(7f, 20f);
                }
                float ease = Mathf.Clamp01(Mathf.Min(t, _actLength - t) / 0.45f);
                _actWeight = ease * ease * (3f - 2f * ease) * idle;
            }
            else
            {
                _actWeight = 0f;
                if (!Lively || !free || Motion.Talking || Motion.Mood is AvatarMood.Scared or AvatarMood.Hurt or AvatarMood.Angry)
                    _nextAct = Mathf.Max(_nextAct, now + 2f);
                else if (now > _nextAct)
                {
                    IdleAct act = (IdleAct)Random.Range(1, 6);
                    Play(act, act switch
                    {
                        IdleAct.HandsOnHips => Random.Range(4f, 7f),
                        IdleAct.ShieldEyes => Random.Range(3f, 4.5f),
                        IdleAct.Stretch => 3.2f,
                        IdleAct.WipeBrow => 1.9f,
                        _ => 2.6f
                    });
                }
            }
        }

        // ------------------------------------------------------------------ ground

        /// <summary>
        /// Finds the ground under each foot. On a slope or a step one foot stands higher than the other: each foot goes
        /// to its own height and the hips come down far enough for the lower one to reach.
        /// </summary>
        private void SampleGround(float dt)
        {
            _groundWeight = (1f - _swim) * (1f - _air) * (1f - _climb) * _upright;
            float k = 1f - Mathf.Exp(-16f * dt);
            float left = 0f, right = 0f;
            Vector3 normalL = Vector3.up, normalR = Vector3.up;
            if (_groundWeight > 0.02f)
            {
                GroundUnder(FootTarget(Bone.ThighL, 0f, -1f, out _), out left, out normalL);
                GroundUnder(FootTarget(Bone.ThighR, 0.5f, 1f, out _), out right, out normalR);
            }
            _groundL = Mathf.Lerp(_groundL, left, k);
            _groundR = Mathf.Lerp(_groundR, right, k);
            _normalL = Vector3.Slerp(_normalL, normalL, k);
            _normalR = Vector3.Slerp(_normalR, normalR, k);
            _hipDrop = Mathf.Lerp(_hipDrop, Mathf.Min(0f, Mathf.Min(left, right)), 1f - Mathf.Exp(-10f * dt));
        }

        /// <summary>Height of solid ground above the root's level under a foot (root space x/z), and its slope.</summary>
        private void GroundUnder(Vector3 local, out float height, out Vector3 normal)
        {
            height = 0f;
            normal = Vector3.up;
            float reach = 0.5f * _rig.Scale;
            Vector3 at = transform.TransformPoint(new Vector3(local.x, 0f, local.z));
            int count = Physics.RaycastNonAlloc(at + Vector3.up * reach, Vector3.down, _groundHits, reach * 2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                // Only what stays put: not people, things lying about or vehicles (they all have bodies).
                if (hit.collider.attachedRigidbody != null || hit.point.y <= best) continue;
                best = hit.point.y;
                normal = hit.normal;
            }
            if (float.IsNegativeInfinity(best)) return;
            height = best - at.y;
            // A foot out over an edge (a dock, a step too deep) hangs there; the body doesn't sink after it.
            if (height < -0.3f * _rig.Scale)
            {
                height = 0f;
                normal = Vector3.up;
                return;
            }
            // A wall or a very steep bank isn't something to stand on at an angle.
            if (normal.y < 0.75f) normal = Vector3.up;
        }

        private float GestureT(float duration) => Mathf.Clamp01((Now - _gestureStart) / duration);
        private const float BurpSeconds = 1.7f;

        /// <summary>
        /// A burp you can see from across the beach (the goofy face can't open its mouth): the chest fills and leans
        /// back to wind up, then heaves forward with it, with a couple of aftershocks.
        /// </summary>
        private void PoseBurp()
        {
            if (!GestureActive(AvatarGesture.Burp, BurpSeconds)) return;
            float b = GestureT(BurpSeconds) * BurpSeconds;
            float wind = Mathf.SmoothStep(0f, 1f, b / 0.22f) * (1f - Mathf.SmoothStep(0f, 1f, (b - 0.22f) / 0.1f));
            float heave = b < 0.24f ? 0f : Mathf.Clamp01((b - 0.24f) / 0.07f) * (1f - Mathf.SmoothStep(0f, 1f, (b - 0.75f) / 0.6f));
            float shake = heave * Mathf.Max(0f, Mathf.Sin((b - 0.24f) * 26f)) * 0.5f;
            float pitch = -9f * wind + (11f + 5f * shake) * heave;
            B(Bone.Spine).localRotation *= Quaternion.Euler(pitch * 0.45f, 0f, 0f);
            B(Bone.Chest).localRotation *= Quaternion.Euler(pitch * 0.55f, 0f, 0f);
        }
        private bool GestureActive(AvatarGesture g, float duration) => _gesture == g && Now - _gestureStart < duration;

        private Transform B(Bone b) => _rig[b];
        private static readonly float[] KissShare = { 0.55f, 0.65f, 0.5f };

        private static float Wave(float cycles) => Mathf.Sin(cycles * Mathf.PI * 2f);

        // ------------------------------------------------------------------ body

        private void PoseBody()
        {
            float s = _rig.Scale;
            float land = 1f - _swim;
            float standing = land * _upright;
            float stepBob = -Mathf.Abs(Wave(_phase)) * (0.025f + 0.035f * _run) * _move * land * _variety.Bounce;
            // (Crouching goes down as far as the legs allow: the goofy lifeguard's short legs folded right up at the
            // full 32 cm and he collapsed into a heap.)
            float legs = Mathf.Clamp((_rig.ThighLength + _rig.ShinLength) / (0.82f * s), 0.4f, 1f);
            float drop = _crouch * 0.32f * s * legs * legs * land + _cpr * 0.42f * s * land;
            float pumpDip = Mathf.Max(0f, 1f - (Now - _lastPump) / 0.25f) * 0.05f * s * _cpr;
            float breath = Mathf.Sin(Now * _variety.BreatheRate + _variety.Phase);
            float breathe = breath * 0.004f;
            // Standing still, the weight wanders from one foot to the other (the feet stay where they are).
            float sway = Mathf.Sin(Now * _variety.SwayRate + _variety.Phase) * (1f - _move) * (1f - _crouch) * standing * (1f - _air);

            // Swimming: lie forward (crawl), stand up to tread water, follow the look direction underwater.
            float swimPitch = Mathf.Lerp(Mathf.Lerp(8f, 72f, _swimMove), Mathf.Clamp(90f + Motion.LookPitch, 10f, 170f) * _swimMove + 12f * (1f - _swimMove), _under);
            float lift = _swim * Mathf.Lerp(0.18f, 0.32f, _swimMove * (1f - _under)) * s;
            float crawl = _swim * _swimMove * (1f - _under) * (Breaststroke ? 0f : 1f);
            if (Breaststroke)
            {
                // Breaststroke: not as flat as the crawl (the head stays out), and the chest comes up with each pull.
                float flat = Mathf.Lerp(8f, 58f - 10f * BreastPull(_swimPhase, out _), _swimMove);
                swimPitch = Mathf.Lerp(flat, Mathf.Clamp(90f + Motion.LookPitch, 10f, 170f) * _swimMove + 12f * (1f - _swimMove), _under);
            }
            float stroke = Wave(_swimPhase) * crawl;
            float swimBob = Mathf.Sin(_swimPhase * Mathf.PI * 4f) * 0.025f * s * _swim * _swimMove;

            Transform hips = B(Bone.Hips);
            float grounded = (_hipDrop * _groundWeight + _landing.Value) * land;
            hips.localPosition = _rig.RestPosition(Bone.Hips) +
                                 new Vector3(sway * 0.03f * s, stepBob - drop - pumpDip + lift + breathe + swimBob + StandTall() * land + grounded, 0f);
            float lean = (4f + 9f * _run) * _move * land + _crouch * 18f * land + _cpr * 34f + _air * -6f + _leanForward.Value * standing;
            float tip = (_leanSide.Value + _turnLag.Value * 0.25f - sway * 2.2f) * standing;
            // The crawl rolls the whole body round its long axis: the shoulder of the arm coming over is the high one.
            Quaternion roll = Quaternion.AngleAxis(CrawlRoll * crawl, Vector3.up);
            hips.localRotation = Quaternion.Euler(lean * 0.35f + swimPitch * _swim, Wave(_phase) * 4f * _move * land, stroke * 5f + tip) * roll;

            B(Bone.Spine).localRotation = Quaternion.Euler(lean * 0.35f + breath * 1.2f + _variety.Slouch * standing, -Wave(_phase) * 5f * _move * land,
                -stroke * 2.5f - tip * 0.6f);
            // The chest takes a share of looking around (before the arms, which hang off it).
            B(Bone.Chest).localRotation = Quaternion.Euler(lean * 0.3f + LookPitch * 0.15f + _variety.Slouch * 0.5f * standing,
                -Wave(_phase) * 5f * _move * land + LookYaw * 0.25f + _turnLag.Value * 0.5f * standing, stroke * 1.5f - tip * 0.3f);

            // Seated: the hips go down onto the seat here, before the arms reach for the handlebars or handles (done
            // after the arms, it dragged the hands 40 cm down into the rider's lap and through the banana).
            if (_seat > 0.01f)
            {
                Vector3 seatedHip = _rig.RestPosition(Bone.Hips);
                // The float's anchor is 0.5 m above the player root; match it
                // independently of body height so short bodies do not sink into the ring.
                seatedHip.y = Motion.FloatSeat ? 0.5f : seatedHip.y - 0.4f * s;
                hips.localPosition = Vector3.Lerp(hips.localPosition, seatedHip, _seat);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(8f, 0f, 0f), _seat);
                if (Motion.Straddle) // leaning on toward the handle in front, like on a horse
                    B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.Euler(14f, 0f, 0f), _seat);
            }
        }

        /// <summary>
        /// Degrees the swimmer is rolled to the right (right shoulder up) at this point of the crawl: fully over to
        /// one side in the middle of that arm's trip through the air, level in between.
        /// </summary>
        private float CrawlRoll => -22f * Mathf.Cos((_swimPhase - RecoveryShare * 0.5f) * Mathf.PI * 2f);

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
            return Mathf.Clamp(straight - height, 0f, 0.08f * _rig.Scale) * (1f - _crouch) * _upright;
        }

        /// <summary>One step (the body travels this far per half cycle), so planted feet stay put on the ground.</summary>
        private float StepLength => Mathf.Lerp(0.62f, 1.05f, _run) * _rig.Scale * _variety.Stride;

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
                // Frog kick: the heels come up to the seat while the arms reach forward again, snap out and back, then
                // the legs lie straight through the glide.
                float frog = FrogKick(_swimPhase);
                float frogs = Breaststroke ? Mathf.Max(_under, _swimMove) : _under; // breaststroke kicks like a frog on top too
                thighX = Mathf.Lerp(thighX, -30f * frog, frogs);
                kneeBend = Mathf.Lerp(kneeBend, 90f * frog + 5f, frogs);
                float spread = Mathf.Lerp(8f, 25f * frog, frogs);
                Quaternion swimThigh = Quaternion.Euler(thighX, 0f, spread * side);
                thigh.localRotation = Quaternion.Slerp(thigh.localRotation, swimThigh, _swim);
                shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(kneeBend, 0f, 0f), _swim);
                foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(45f, 0f, 0f), _swim);
                if (_swim > 0.99f) return;
            }

            // Stepping: the foot is planted during stance and swings forward in an arc, on the ground that is there.
            Vector3 target = FootTarget(thighBone, offset, side, out float roll);
            bool leftFoot = side < 0f;
            target.y += (leftFoot ? _groundL : _groundR) * _groundWeight;

            Vector3 world = transform.TransformPoint(target);
            Vector3 knee = transform.forward;
            float weight = 1f - _swim;
            IK.Solve(thigh, shin, _rig.ThighLength, _rig.ShinLength, world, knee, weight, true);

            // Feet flat on the ground (toes point where the body faces, the sole lies along the slope), rolling a
            // little while stepping.
            Quaternion slope = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, leftFoot ? _normalL : _normalR), _groundWeight);
            Quaternion flat = slope * transform.rotation * Quaternion.Euler(-roll + _cpr * 70f, 0f, 0f);
            foot.rotation = Quaternion.Slerp(foot.rotation, flat, weight);
        }

        /// <summary>
        /// Where a foot belongs right now on level ground, in root space (the ankle), and how far it is rolled onto
        /// the toes. Depends only on the walk cycle, not on the bones, so the ground can be looked up before posing.
        /// </summary>
        private Vector3 FootTarget(Bone thighBone, float offset, float side, out float roll)
        {
            float s = _rig.Scale;
            float q = Mathf.Repeat(_phase + offset, 1f);
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
                roll = 0f;
            }
            else
            {
                float t = (q - 0.5f) / 0.5f;
                along = Mathf.Lerp(-0.5f, 0.5f, Mathf.SmoothStep(0f, 1f, t)) * stride;
                lift = Mathf.Sin(t * Mathf.PI) * (0.1f + 0.1f * _run) * s * _move * _variety.Bounce;
                roll = Mathf.Sin(t * Mathf.PI) * 25f * _move;
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
            return target;
        }

        /// <summary>0 legs straight .. 1 knees drawn right up, over a breaststroke cycle (see PoseSwimArm for the arms).</summary>
        private static float FrogKick(float phase)
        {
            // Draw up slowly (0.55 .. 0.8), kick out fast (0.8 .. 0.95), glide the rest of the time.
            if (phase < 0.55f || phase > 0.95f) return 0f;
            return phase < 0.8f ? Mathf.SmoothStep(0f, 1f, (phase - 0.55f) / 0.25f) : 1f - Mathf.SmoothStep(0f, 1f, (phase - 0.8f) / 0.15f);
        }

        // ------------------------------------------------------------------ arms

        private void PoseArms()
        {
            float land = 1f - _swim;
            float swing = (24f + 26f * _run) * _move * land * _variety.Swing;
            float elbow = Mathf.Lerp(12f, 85f, _run) * _move + 8f;
            float drag = _leanForward.Value * 1.6f * land * _upright; // speeding up leaves the hands behind for a moment
            for (int i = 0; i < 2; i++)
            {
                bool left = i == 0;
                float side = left ? -1f : 1f;
                Transform upper = B(left ? Bone.UpperArmL : Bone.UpperArmR);
                Transform fore = B(left ? Bone.ForearmL : Bone.ForearmR);

                // Base: relaxed, counter-swinging with the legs.
                float armSwing = (left ? -1f : 1f) * Mathf.Cos(_phase * Mathf.PI * 2f) * swing;
                float idleSway = Mathf.Sin(Now * 1.1f + i + _variety.Phase) * 2f * (1f - _move);
                // (Landing throws the arms out for balance.)
                float spread = 7f + 5f * _run + _air * 35f + _crouch * 6f + _variety.Spread - _landing.Value * 120f / _rig.Scale;
                // The goofy lifeguard's belly: his swinging arms go round it, not into it.
                if (_rig.Look.IsGoofy) spread += (10f + 12f * _run) * _move + 4f;
                float raise = _air * 20f;
                float back = 16f * _move * land; // hands trail a little: the swing centres behind the hip, not in front
                upper.localRotation = Quaternion.Euler(-armSwing - raise + idleSway + back + drag, 0f, spread * side);
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
            IdleLayer();
            PoseHands();
        }

        /// <summary>
        /// Habits of someone standing around: scratching the head, hands on the hips, a stretch, shading the eyes
        /// to look out to sea, wiping the brow; and talking with the hands.
        /// </summary>
        private void IdleLayer()
        {
            float s = _rig.Scale;
            Transform upperL = B(Bone.UpperArmL), foreL = B(Bone.ForearmL), upperR = B(Bone.UpperArmR), foreR = B(Bone.ForearmR);
            float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
            Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;

            // Talking: the right hand comes up and beats time with the words (only someone standing with free hands).
            float talking = _talk * IdleWeight * (1f - _actWeight) * (Lively ? 1f : 0f);
            if (talking > 0.01f)
            {
                float beat = Mathf.Sin(Now * 5.2f + _variety.Phase) * 0.5f + Mathf.Sin(Now * 2.1f) * 0.5f;
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-22f + beat * 6f, 0f, 14f), talking * 0.85f);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-78f + beat * 16f, 0f, 0f), talking * 0.85f);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-30f - beat * 8f, 0f, 0f), talking * 0.5f);
            }

            float w = _actWeight;
            if (w <= 0.01f) return;
            float t = Mathf.Clamp01((Now - _actStart) / Mathf.Max(0.1f, _actLength));
            Transform chest = B(Bone.Chest);
            // Between the eyes (the head isn't turned yet this frame, but that barely moves this point).
            Vector3 eyes = (B(Bone.EyeL).position + B(Bone.EyeR).position) * 0.5f;
            switch (_act)
            {
                case IdleAct.ScratchHead:
                {
                    Vector3 spot = eyes + up * (0.13f * s) + right * (0.085f * s) - fwd * (0.13f * s) + up * (Mathf.Sin(Now * 19f) * 0.012f * s);
                    IK.Solve(upperR, foreR, la, lb, spot, right + up * 0.5f - fwd * 0.3f, w, false);
                    break;
                }
                case IdleAct.HandsOnHips:
                {
                    Vector3 hips = B(Bone.Hips).position + up * (0.07f * s);
                    float out_ = Mathf.Abs(_rig.RestPosition(Bone.ThighL).x) + 0.13f * s;
                    IK.Solve(upperL, foreL, la, lb, hips - right * out_ + fwd * (0.02f * s), -right - fwd * 0.7f, w, false);
                    IK.Solve(upperR, foreR, la, lb, hips + right * out_ + fwd * (0.02f * s), right - fwd * 0.7f, w, false);
                    break;
                }
                case IdleAct.Stretch:
                {
                    // Arms up and out, back arched, held, and down again.
                    float reach = Mathf.Sin(t * Mathf.PI);
                    B(Bone.Spine).localRotation *= Quaternion.Euler(-9f * w * reach, 0f, 0f);
                    chest.localRotation *= Quaternion.Euler(-7f * w * reach, 0f, 0f);
                    upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(-12f, 0f, -152f), w);
                    foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-28f + 20f * reach, 0f, 0f), w);
                    upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-12f, 0f, 152f), w);
                    foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-28f + 20f * reach, 0f, 0f), w);
                    break;
                }
                case IdleAct.ShieldEyes:
                {
                    Vector3 spot = eyes + up * (0.075f * s) + fwd * (0.035f * s) + right * (0.05f * s);
                    IK.Solve(upperR, foreR, la, lb, spot, right + up * 0.25f, w, false);
                    break;
                }
                case IdleAct.WipeBrow:
                {
                    // The back of the hand drawn across the forehead, from the far side to the near one.
                    Vector3 spot = eyes + up * (0.085f * s) + fwd * (0.03f * s) + right * (Mathf.Lerp(-0.07f, 0.09f, Mathf.SmoothStep(0f, 1f, t)) * s);
                    IK.Solve(upperR, foreR, la, lb, spot, right + up * 0.6f, w, false);
                    break;
                }
            }
        }

        /// <summary>
        /// Wrists and fingers: loose fists when running, flat when swimming or pressing a chest, the grip's hand pose
        /// on held things (palm on the item, fingers wrapped round), an open hand to wave, a pointing finger to press.
        /// </summary>
        private static readonly HandPose CprLowerHand = new(0.22f, 0.15f, 0f), CprUpperHand = new(0.62f, 0.35f, 0f);

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
                // (The goofy lifeguard's cartoon hand is modelled with its fingers fanned wide: barely curled, as
                // relaxed hands are, it read as a claw. His rest half way to a loose fist.)
                HandPose rest = _rig.Look.IsGoofy ? HandPose.Lerp(HandPose.Relaxed, HandPose.LooseFist, 0.6f) : HandPose.Relaxed;
                HandPose pose = HandPose.Lerp(rest, HandPose.LooseFist, _run * _move * (1f - _swim));
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
                    // The lower hand's heel on the chest, fingers lifted a little; the upper one's fingers curl down
                    // round it (spread flat, the cartoon fingers fanned out like claws).
                    pose = HandPose.Lerp(pose, right ? CprLowerHand : CprUpperHand, _cpr);
                }
                if (right && _eat > 0.01f && !m.Drinking) pose = HandPose.Lerp(pose, HandPose.Cup, _eat);
                if (right && GestureActive(AvatarGesture.Wave, 1.6f))
                {
                    rotation = HandBones.Orient(Vector3.up, fwd, side);
                    weight = Mathf.Clamp01(Mathf.Min(GestureT(1.6f) / 0.15f, (1f - GestureT(1.6f)) / 0.15f));
                    pose = HandPose.Lerp(pose, HandPose.Wave, weight);
                }
                else if (right && GestureActive(AvatarGesture.Interact, 0.4f))
                    pose = HandPose.Lerp(pose, HandPose.Point, Mathf.Sin(GestureT(0.4f) * Mathf.PI));

                if (_actWeight > 0.01f)
                {
                    // Idle habits: a flat hand over the eyes, hands flat on the hips, fingers hooked to scratch.
                    Vector3 across = transform.right;
                    if (right && _act == IdleAct.ShieldEyes)
                    {
                        rotation = HandBones.Orient(-across + fwd * 0.35f, Vector3.down, side);
                        weight = _actWeight;
                        pose = HandPose.Lerp(pose, HandPose.Flat, _actWeight);
                    }
                    else if (right && _act == IdleAct.WipeBrow)
                    {
                        rotation = HandBones.Orient(-across + Vector3.up * 0.2f, fwd, side);
                        weight = _actWeight;
                    }
                    else if (_act == IdleAct.HandsOnHips)
                    {
                        rotation = HandBones.Orient(fwd - Vector3.up * 0.9f, -across * side, side);
                        weight = _actWeight;
                        pose = HandPose.Lerp(pose, HandPose.BoxGrip, _actWeight);
                    }
                    else if (right && _act == IdleAct.ScratchHead)
                        pose = HandPose.Lerp(pose, HandPose.Cup, _actWeight);
                    else if (_act == IdleAct.Stretch)
                        pose = HandPose.Lerp(pose, HandPose.Wave, _actWeight);
                }

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
            // The arm doesn't turn like a wheel: it comes over quickly and loose (elbow high), stretches out ahead and
            // rests there a moment, then pulls back through the water, speeding up to the hip.
            float crawlAngle = CrawlTurn(Mathf.Repeat(_swimPhase + index * 0.5f, 1f)) * 360f;
            Quaternion crawl = Quaternion.Euler(crawlAngle, 0f, 14f * side);
            Quaternion crawlElbow = Quaternion.Euler(-Mathf.Lerp(8f, 78f, Mathf.Max(0f, Mathf.Sin(crawlAngle * Mathf.Deg2Rad))), 0f, 0f);
            // Treading water: sculling in front of the chest.
            float scull = Mathf.Sin(_swimPhase * Mathf.PI * 4f + index * Mathf.PI);
            Quaternion tread = Quaternion.Euler(-45f, scull * 25f * side, (40f + scull * 10f) * side);
            Quaternion treadElbow = Quaternion.Euler(-55f, 0f, 0f);
            // Breaststroke underwater: glide with the arms stretched ahead, sweep out and back to the chest, then
            // slide them forward again with the elbows tucked while the legs kick (see FrogKick).
            float pull = BreastPull(_swimPhase, out bool recovering);
            // (At the surface the body isn't flat, so stretched out ahead is less far round than when diving.)
            float ahead = Mathf.Lerp(Breaststroke ? -146f : -170f, -170f, _under);
            Quaternion breast = Quaternion.Euler(Mathf.Lerp(ahead, -95f, pull), 0f, Mathf.Lerp(8f, recovering ? 30f : 75f, pull) * side);
            Quaternion breastElbow = Quaternion.Euler(-Mathf.Lerp(8f, recovering ? 95f : 35f, pull), 0f, 0f);

            Quaternion armPose = Quaternion.Slerp(Quaternion.Slerp(tread, Breaststroke ? breast : crawl, _swimMove), breast, _under);
            Quaternion elbowPose = Quaternion.Slerp(Quaternion.Slerp(treadElbow, Breaststroke ? breastElbow : crawlElbow, _swimMove), breastElbow, _under);
            upper.localRotation = Quaternion.Slerp(upper.localRotation, armPose, _swim);
            fore.localRotation = Quaternion.Slerp(fore.localRotation, elbowPose, _swim);
        }

        /// <summary>
        /// Breaststroke arms over one cycle: 0 = both stretched out ahead (the glide), 1 = swept out and back to the
        /// shoulders (the end of the pull). <paramref name="recovering"/>: sliding forward again, elbows tucked in.
        /// </summary>
        private static float BreastPull(float phase, out bool recovering)
        {
            float bs = Mathf.Repeat(phase, 1f);
            recovering = bs >= 0.55f;
            return bs < 0.25f ? 0f : bs < 0.55f ? Mathf.SmoothStep(0f, 1f, (bs - 0.25f) / 0.3f) : 1f - Mathf.SmoothStep(0f, 1f, (bs - 0.55f) / 0.35f);
        }

        /// <summary>The share of a crawl cycle an arm spends in the air (the rest: reaching, then pulling).</summary>
        private const float RecoveryShare = 0.36f;

        /// <summary>
        /// How far round its circle a crawling arm is (0 at the hip, 0.5 stretched out ahead, 1 back at the hip) at
        /// a point of its cycle: over through the air in the first third, a short rest out in front, then the pull.
        /// </summary>
        private static float CrawlTurn(float phase)
        {
            const float rest = 0.12f, ahead = 0.54f; // rests while creeping from 0.5 to 0.54 of the circle
            if (phase < RecoveryShare) return Mathf.SmoothStep(0f, 1f, phase / RecoveryShare) * 0.5f;
            if (phase < RecoveryShare + rest) return Mathf.Lerp(0.5f, ahead, (phase - RecoveryShare) / rest);
            float t = (phase - RecoveryShare - rest) / (1f - RecoveryShare - rest);
            return Mathf.Lerp(ahead, 1f, t * (0.45f + 0.55f * t)); // starts easy, ends fast
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

            if (_eat > 0.01f && !m.Drinking)
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
                // Leaning further over a chest the straight arms don't reach (a wide round dad is knelt beside from
                // further out): the shoulders come over the hands, the way compressions are really done, instead of
                // the hands hovering in the air in front of an upright rescuer.
                Transform spine = B(Bone.Spine);
                float armReach = (la + lb) * 0.97f, leaned = 0f;
                for (int k = 0; k < 4 && leaned < 40f; k++)
                {
                    Vector3 shoulders = (upperL.position + upperR.position) * 0.5f;
                    float gap = Vector3.Distance(shoulders, point + up * 0.05f) - armReach;
                    if (gap <= 0.005f) break;
                    float torso = Mathf.Max(Vector3.Distance(spine.position, shoulders), 0.1f);
                    float step = Mathf.Min(gap / torso * Mathf.Rad2Deg, 40f - leaned);
                    Turn(spine, right, step * _cpr);
                    leaned += step;
                }
                // (One right on top of the other, on the middle of the chest: 5 cm apart sideways they read as two
                // hands side by side.)
                IK.Solve(upperR, foreR, la, lb, point + up * 0.02f, -fwd + right, _cpr, false);
                IK.Solve(upperL, foreL, la, lb, point + up * 0.075f, -fwd - right, _cpr, false);
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
            else if (GestureActive(AvatarGesture.JumpShot, 0.8f))
            {
                // A basketball jump shot: both hands up over the forehead (the guide hand on the side of the ball),
                // the shooting arm pushes up and out until the elbow is straight, the wrist flicks over ("hand in the
                // cookie jar"), and the arms hang there in the follow-through a moment before dropping.
                Transform upperL = B(Bone.UpperArmL), foreL = B(Bone.ForearmL), handR = B(Bone.HandR);
                float t = GestureT(0.8f);
                float w = t < 0.7f ? Mathf.Clamp01(t / 0.08f) : 1f - (t - 0.7f) / 0.3f;
                float push = Mathf.SmoothStep(0f, 1f, t / 0.3f);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(Mathf.Lerp(-135f, -158f, push), 0f, 6f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-Mathf.Lerp(85f, 6f, push), 0f, 0f), w);
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(Mathf.Lerp(-130f, -115f, push), 0f, -14f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-Mathf.Lerp(80f, 40f, push), 0f, 0f), w);
                if (handR != null) handR.localRotation *= Quaternion.Euler(Mathf.Lerp(-35f, 55f, push) * w, 0f, 0f); // the flick
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
                // Sitting astride (the hips went down onto the seat in PoseBody): thighs forward and apart, shins down
                // to the footrests.
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    // Astride something fat (a banana): like on a horse, thighs down and apart round it (not out in
                    // front as on a chair: that bunched the shorts up into a balloon), shins hanging down its sides.
                    Quaternion thighPose = Motion.Straddle ? Quaternion.Euler(-30f, 0f, 58f * side) : Quaternion.Euler(-78f, 0f, 16f * side);
                    Quaternion shinPose = Quaternion.Euler(Motion.Straddle ? 38f : 84f, 0f, Motion.Straddle ? -12f * side : 0f);
                    if (Motion.FloatSeat)
                    {
                        thighPose = Quaternion.Euler(-85f, 0f, 4f * side);
                        shinPose = Quaternion.Euler(65f, 0f, 0f);
                    }
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, thighPose, _seat);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, shinPose, _seat);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(Motion.FloatSeat ? 12f : Motion.Straddle ? 20f : -6f, 0f, 0f), _seat);
                }
            }

            if (_fly > 0.01f)
            {
                // Superman: the body lies along the flight, arms stretched out ahead (fists), legs together behind,
                // the cape we don't have flapping in our imagination.
                float w = _fly;
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(72f, 0f, Mathf.Sin(Now * 9f) * 4f), w);
                B(Bone.Neck).localRotation = Quaternion.Slerp(B(Bone.Neck).localRotation, Quaternion.Euler(-45f, 0f, 0f), w);
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(-172f, 0f, -8f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-4f, 0f, 0f), w);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-172f, 0f, 8f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-4f, 0f, 0f), w);
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    float kick = Mathf.Sin(Now * 14f + leg * Mathf.PI) * 8f; // a little flutter
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(6f + kick, 0f, 3f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(12f, 0f, 0f), w);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(40f, 0f, 0f), w);
                }
                _rig.LeftHand?.Pose(HandPose.Fist);
                _rig.RightHand?.Pose(HandPose.Fist);
            }
            else if (_star > 0.01f)
            {
                // A star jump at the top of a bounce: arms up and out, legs apart, a big grin.
                float w = _star;
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(0f, 0f, -135f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-8f, 0f, 0f), w);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(0f, 0f, 135f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-8f, 0f, 0f), w);
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR);
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(-6f, 0f, 28f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(10f, 0f, 0f), w);
                }
                _rig.LeftHand?.Pose(HandPose.Wave);
                _rig.RightHand?.Pose(HandPose.Wave);
            }

            if (_kneel > 0.01f)
            {
                // On the knees, hands together in front of the chest: "please, please!" Bowing again and again while
                // looking up at whoever caught him.
                float bow = 0.5f + 0.5f * Mathf.Sin(Now * 3.2f);
                Turn(B(Bone.Spine), transform.right, (6f + 16f * bow) * _kneel);
                float bob = Mathf.Sin(Now * 7f) * 0.03f * s;
                hips.localPosition = Vector3.Lerp(hips.localPosition, _rig.RestPosition(Bone.Hips) + new Vector3(0f, -0.42f * s + bob, 0f), _kneel);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(6f, 0f, 0f), _kneel);
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
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
                Turn(B(Bone.Head), transform.right, -(4f + 14f * bow) * _kneel);
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
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(0f, 0f, 14f * side), _down);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(8f, 0f, 0f), _down);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(-50f, 0f, 0f), _down);
                }
            }

            BeachPoses(hips, upperL, foreL, upperR, foreR, s);
            AimedGestures(upperL, foreL, upperR, foreR);
            PoseReactions(hips, upperL, upperR, s);
            KeepLegsAboveGround(hips, s);
        }

        /// <summary>
        /// Knocked down or going down onto the knees (and getting up again) the hips get there before the legs have
        /// folded under them, so for a moment the legs would hang through the floor. If a knee or an ankle ends up below
        /// the ground, the whole body is lifted just enough to keep it on top. (Not for sitting or lying on a towel:
        /// there the heels rest on the sand on purpose, and lifting them sat people in the air.)
        /// </summary>
        private void KeepLegsAboveGround(Transform hips, float s)
        {
            float low = Mathf.Max(_down, _kneel);
            if (low < 0.01f || _swim > 0.01f || _seat > 0.01f || _sit > 0.01f || _lie > 0.01f || _lieFront > 0.01f || _carried > 0.01f || !Motion.Grounded) return;
            float floor = transform.position.y;
            float margin = 0.05f * s; // half a leg's thickness
            float lowest = float.MaxValue;
            lowest = Mathf.Min(lowest, B(Bone.ShinL).position.y, B(Bone.ShinR).position.y);
            lowest = Mathf.Min(lowest, B(Bone.FootL).position.y, B(Bone.FootR).position.y);
            float below = floor + margin - lowest;
            if (below > 0f) hips.position += Vector3.up * below;
        }

        /// <summary>Turns a bone about a world axis, on top of the pose it already has (children go with it).</summary>
        private static void Turn(Transform bone, Vector3 axis, float degrees)
        {
            if (Mathf.Abs(degrees) > 0.01f) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /// <summary>
        /// Laid over every other pose: the flinch from a blow, the knockout fall (arms thrown up, a bounce on landing,
        /// then the head lolling dizzily) and the look back over the shoulder of someone running away.
        /// </summary>
        private void PoseReactions(Transform hips, Transform upperL, Transform upperR, float s)
        {
            Transform spine = B(Bone.Spine), chest = B(Bone.Chest), neck = B(Bone.Neck), head = B(Bone.Head);

            // Fleeing: every couple of seconds a quick look back, over one shoulder then the other.
            if (_lookBack > 0.01f)
            {
                const float cycle = 2.3f;
                float u = Mathf.Repeat(Now + _variety.Phase, cycle);
                float glance = u < 0.7f ? Mathf.Sin(u / 0.7f * Mathf.PI) : 0f;
                glance = glance * glance * (3f - 2f * glance) * _lookBack;
                float side = Mathf.Repeat(Mathf.Floor((Now + _variety.Phase) / cycle), 2f) < 1f ? 1f : -1f;
                Turn(chest, Vector3.up, 28f * glance * side);
                Turn(neck, Vector3.up, 32f * glance * side);
                Turn(head, Vector3.up, 48f * glance * side);
            }

            // A blow: snap away from it, then come back (a little past, then settle).
            float t = Now - _flinchStart;
            if (t < 0.75f)
            {
                float w = t < 0.07f ? t / 0.07f : Mathf.Exp(-(t - 0.07f) * 7f) * Mathf.Cos((t - 0.07f) * 7f);
                Vector3 axis = Vector3.Cross(Vector3.up, _flinchPush);
                hips.position += _flinchPush * (0.14f * s * w);
                Turn(hips, axis, 6f * w);
                Turn(spine, axis, 14f * w);
                Turn(chest, axis, 20f * w);
                Turn(chest, Vector3.up, 22f * w * _flinchSide);
                Turn(neck, axis, 14f * w);
                Turn(head, axis, 30f * w);
                Turn(head, Vector3.up, 38f * w * _flinchSide);
                Turn(upperL, axis, -65f * w); // the arms fly the other way, left behind
                Turn(upperR, axis, -65f * w);
            }

            if (_down > 0.01f)
            {
                float since = Now - _downSince;
                // Going over: the arms fly up as the body drops...
                if (since < 0.55f)
                {
                    float fling = Mathf.Sin(since / 0.55f * Mathf.PI);
                    Turn(upperL, transform.right, -110f * fling);
                    Turn(upperR, transform.right, -110f * fling);
                }
                // ...it lands with a bounce...
                if (since > 0.3f && since < 0.62f) hips.position += Vector3.up * (Mathf.Sin((since - 0.3f) / 0.32f * Mathf.PI) * 0.07f * s * _down);
                // ...and lies there seeing stars, the head rolling slowly side to side.
                if (since > 0.8f)
                {
                    float dizzy = Mathf.Clamp01((since - 0.8f) * 2f) * _down;
                    Vector3 along = (head.position - neck.position).normalized;
                    Turn(head, along, Mathf.Sin(Now * 2.4f) * 30f * dizzy);
                }
            }
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
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
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

            if (_carried > 0.01f)
            {
                // Slung over someone's shoulder, fireman's carry (PlayerCarry.HoldOffset; we face the other way): hips on
                // their shoulder, the body hanging head down their back with the arms dangling, legs down their front
                // with the feet kicking. (Lying across their arms, our big head was in their face.)
                float w = _carried;
                hips.localPosition = Vector3.Lerp(hips.localPosition, Vector3.zero, w);
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(100f, 0f, 0f), w);
                B(Bone.Spine).localRotation = Quaternion.Slerp(B(Bone.Spine).localRotation, Quaternion.Euler(12f, 0f, 0f), w);
                B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.Euler(8f, 0f, 0f), w);
                B(Bone.Neck).localRotation = Quaternion.Slerp(B(Bone.Neck).localRotation, Quaternion.Euler(-35f, 0f, 0f), w);
                float sway = Mathf.Sin(t * 3f) * 8f;
                upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Euler(-165f + sway, 0f, -12f), w);
                foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(-12f, 0f, 0f), w);
                upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Euler(-165f - sway, 0f, 12f), w);
                foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(-12f, 0f, 0f), w);
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    float kick = Mathf.Sin(t * 6f + (left ? 0f : 1.9f)) * 22f; // kicking to be put down
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(-55f, 0f, 6f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(40f + kick, 0f, 0f), w);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(20f, 0f, 0f), w);
                }
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
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
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
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
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
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
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
            Kissing = 0f;
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

        /// <summary>
        /// Our lips: the mouth bone sits on them (every body: built ones and the baked Meshy faces). Puckered up for a
        /// kiss (the players' goofy body) they stick out in front of the face, and that's where the lips are then.
        /// </summary>
        private Vector3 MouthPoint
        {
            get
            {
                Transform head = B(Bone.Head);
                Vector3 lips = B(Bone.Mouth) != null ? B(Bone.Mouth).position : head.TransformPoint(new Vector3(0f, 0.035f, 0.12f) * _rig.Scale);
                return lips + head.TransformVector(Vector3.forward * (PuckerLength * Kissing));
            }
        }

        /// <summary>How far the lips pucker out for a kiss, in head space (0: they don't). Set by AvatarFunny.</summary>
        public float PuckerLength { get; set; }

        /// <summary>0..1: how far into a kiss (a rescue breath) we are this frame.</summary>
        public float Kissing { get; private set; }

        /// <summary>Where our lips are right now (the kiss check lines two of these up).</summary>
        public Vector3 Lips => MouthPoint;

        /// <summary>
        /// Lips on lips, kneeling: the knees and hips stay where the kneel put them; the back curls down (spine, then
        /// chest, then neck, each turning a share of the way so the mouth heads for theirs, a few passes), the face
        /// turns down to meet theirs tipped sideways the way people kiss, and only the last few centimetres are made
        /// up by leaning the hips in. One hand cradles the forehead, the other lifts the chin.
        /// </summary>
        private void Kiss(Vector3 lips, float w, Transform upperL, Transform foreL, Transform upperR, Transform foreR, float la, float lb)
        {
            if (w <= 0.001f) return;
            Kissing = w;
            Transform hips = B(Bone.Hips), head = B(Bone.Head);
            Vector3 fwd = transform.forward, right = transform.right;
            Vector3 target = lips + Vector3.up * 0.01f;
            Vector3 along = Vector3.ProjectOnPlane(target - hips.position, Vector3.up);
            along = along.sqrMagnitude > 1e-4f ? along.normalized : fwd;

            // 1. Curl the back down toward their face: spine, chest and neck share the bend (CCD, a few passes).
            Transform spine = B(Bone.Spine), chest = B(Bone.Chest), neck = B(Bone.Neck);
            for (int pass = 0; pass < 6; pass++)
                for (int i = 0; i < 3; i++)
                {
                    Transform bone = i == 0 ? spine : i == 1 ? chest : neck;
                    Vector3 toMouth = MouthPoint - bone.position, toLips = target - bone.position;
                    if (toMouth.sqrMagnitude < 1e-6f || toLips.sqrMagnitude < 1e-6f) continue;
                    Quaternion bend = Quaternion.FromToRotation(toMouth, toLips);
                    bone.rotation = Quaternion.Slerp(Quaternion.identity, bend, KissShare[i] * w) * bone.rotation;
                }
            // 2. Face down onto theirs, tilted sideways a little, eyes shut (the face code does the eyes).
            Quaternion faceDown = Quaternion.LookRotation(Vector3.down + along * 0.35f, along) * Quaternion.Euler(0f, 0f, 38f);
            head.rotation = Quaternion.Slerp(head.rotation, faceDown, w * 0.85f);
            // 3. Whatever is still missing: lean the hips in (up to 30 cm), knees stay; then curl the neck the last bit.
            Vector3 gap = target - MouthPoint;
            if (gap.sqrMagnitude > 1e-6f)
            {
                hips.position += Vector3.ClampMagnitude(gap, 0.3f * _rig.Scale) * w;
                PoseLegs();
            }
            for (int pass = 0; pass < 12; pass++)
            {
                Transform bone = pass % 2 == 0 ? chest : neck;
                Vector3 toMouth = MouthPoint - bone.position, toLips = target - bone.position;
                if (toMouth.sqrMagnitude < 1e-6f || toLips.sqrMagnitude < 1e-6f) continue;
                bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(toMouth, toLips), 0.6f * w) * bone.rotation;
            }

            // Hands: one on the forehead, one under the chin.
            IK.Solve(upperL, foreL, la, lb, lips + along * 0.13f + Vector3.up * 0.07f - right * 0.05f, -fwd - right, w, false);
            IK.Solve(upperR, foreR, la, lb, lips - along * 0.08f + Vector3.up * 0.02f + right * 0.04f, -fwd + right, w, false);
        }

        // ------------------------------------------------------------------ head & face

        private void PoseHead()
        {
            float idle = IdleWeight;
            float standing = (1f - _swim) * _upright;
            // Looking somewhere else for a moment; nodding along while talking (in any pose).
            float yaw = Mathf.Clamp(LookYaw + _glance.x * idle + Mathf.Sin(Now * 1.9f + _variety.Phase) * 4f * _talk, -85f, 85f);
            float pitch = LookPitch + _glance.y * idle + (Mathf.Sin(Now * 6.5f + _variety.Phase) * 3f + Mathf.Sin(Now * 2.3f) * 2f) * _talk
                          - _leanForward.Value * 0.5f * standing; // the head stays level while the body tips
            if (_actWeight > 0.01f && _act == IdleAct.ShieldEyes)
                yaw += Mathf.Sin((Now - _actStart) * 1.6f) * 24f * _actWeight; // scanning the sea
            // Lying forward to swim: the head tips back up to look where we're going, stays level while the body
            // rolls under it, and turns out to the right for air as that arm comes over.
            float crawl = _swim * _swimMove * (1f - _under) * (Breaststroke ? 0f : 1f);
            float swimTilt = _swim * (1f - _under) * Mathf.Lerp(0f, Breaststroke ? -52f : -60f, _swimMove);
            float rightArm = Mathf.Repeat(_swimPhase + 0.5f, 1f);
            float breath = !Breaststroke && rightArm < RecoveryShare ? Mathf.Sin(rightArm / RecoveryShare * Mathf.PI) : 0f;
            swimTilt *= 1f - 0.55f * breath;
            float turn = (-CrawlRoll * 0.75f + 62f * breath) * crawl;
            float tilt = (_variety.HeadTilt + _leanSide.Value * -0.4f) * standing;
            if (Motion.Drinking) pitch -= 22f * _eat; // chugging: the head goes back with the bottle
            if (GestureActive(AvatarGesture.Burp, BurpSeconds))
            {
                // A beer coming back up: a little lean back to wind up, then the head jerks forward with it and shakes.
                float b = GestureT(BurpSeconds) * BurpSeconds;
                float wind = Mathf.SmoothStep(0f, 1f, b / 0.22f) * (1f - Mathf.SmoothStep(0f, 1f, (b - 0.22f) / 0.12f));
                float out_ = b < 0.25f ? 0f : Mathf.Clamp01((b - 0.25f) / 0.08f) * (1f - Mathf.SmoothStep(0f, 1f, (b - 0.9f) / 0.45f));
                pitch += -16f * wind + (10f + 3f * Mathf.Sin(Now * 31f)) * out_;
            }
            B(Bone.Neck).localRotation = Quaternion.Euler(pitch * 0.3f + swimTilt * 0.4f, yaw * 0.25f + turn * 0.4f, tilt * 0.4f);
            B(Bone.Head).localRotation = Quaternion.Euler(pitch * 0.55f + swimTilt * 0.6f - _cpr * 15f, yaw * 0.5f + turn * 0.6f, tilt * 0.6f);
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
            else if (GestureActive(AvatarGesture.Burp, BurpSeconds))
            {
                float b = GestureT(BurpSeconds) * BurpSeconds;
                if (b < 0.25f) { mouth = 0.05f; brows = 0.6f; } // holding it in, cheeks puffed
                else if (b < 1.05f) { mouth = 0.8f + 0.15f * Mathf.Sin(t * 37f); eyes = 0.35f; brows = -0.4f; }
                else { mouth = 0.12f; eyes = 1.15f; brows = 0.35f; } // ...pardon me
            }
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
