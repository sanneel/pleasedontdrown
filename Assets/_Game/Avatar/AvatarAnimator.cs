using System.Collections.Generic;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>One-off moves that play over whatever the body is doing. TableBite: seated, a bite off the dish on the
    /// table in front (the point), up to the mouth (Fun.TableMeal).</summary>
    public enum AvatarGesture : byte { None, Interact, Throw, ChargeStart, ChargeEnd, Pump, Wave, Bite, EatStart, EatStop, Punch, Breath, Zap, Shoot, JumpShot, Burp, TableBite,
        PunchLeft, Hook, HookLeft, Uppercut, UppercutLeft, Overhand, OverhandLeft,
        /// <summary>A step-in lunge: front foot forward, hips and back in after it (a long weapon's thrust, a grab).</summary>
        Lunge }

    /// <summary>
    /// Whole-body poses held for a while (story characters, knockouts). SitStool: on a bar stool exactly as a player
    /// sits on one (its seat point 0.5 m over the root; see <see cref="AvatarMotion.HighSeat"/>).
    /// </summary>
    public enum AvatarPose : byte { Normal, Down, Kneel, Scared, HandsUp, Lie, LieFront, Sit, SitChair, Carried, SitStool }

    /// <summary>Face on top of the automatic expression.</summary>
    public enum AvatarMood : byte { Neutral, Happy, Scared, Angry, Hurt }

    /// <summary>A fist fighter squaring up (Story.StoryNpc.Combat): Guard, both fists up at the chin; WindUp, the
    /// right fist drawing back for a blow (<see cref="AvatarMotion.WindUp"/> says how far), the tell to step out of.</summary>
    public enum FightStance : byte { None, Guard, WindUp }

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
        public bool Basketball;      // exact palm contact on the ball; keep both grips during shot charging
        public bool Shouldered;       // a long gun up in the shoulder: right elbow out, left elbow under the barrel
        public bool LongProp;         // a pole held low in front in one or two hands (a giant grabber): palms right on its grips, elbows down
        public float Brace;           // 0..1 rocked back onto the rear foot (a long weapon drawn back before its lunge: the tell)
        public bool CarryingPerson;
        public HandGrip GripLeft, GripRight; // world palm points, finger/palm directions, finger curls
        public float Charge;          // 0..1 throw wind-up
        public bool Eating;
        public bool Drinking;         // eating a drink: the hand keeps its grip on the bottle (tipped up at the lips), the head goes back
        public bool Cpr;
        public Vector3 CprPoint;      // world, the chest being pressed
        public bool Defib;            // holding the defibrillator's paddles: kneeling (Cpr) keeps the hands on them
        public bool Seated;           // on a vehicle seat (hands come from the grips)
        public bool Straddle;         // seated astride a fat seat (a banana boat): knees well apart, feet back down its sides
        public bool FloatSeat;        // seated on a ring: knees together so the legs fit its opening
        public bool ChairSeat;        // seated on a chair or a stool: the bottom right on the seat, hands on the seat beside the lap
        public bool HighSeat;         // ...a bar stool (Art/Props/bar_stool.glb): knees bent, feet on its footrest, hands on the knees
        public float SeatEdge;        // ...a chair: its cushion's front edge this far ahead of the seat point (0: not known)
        public bool Flying;           // shot through the air (a cannon, a throw, flung off the banana): superman, arms out ahead
        public bool StarJump;         // bounced high (a trampoline): arms and legs flung out in a star
        public AvatarPose Pose;
        public AvatarMood Mood;
        public bool Talking;          // flap the mouth
        public bool LookBack;         // running away: glances back over the shoulder now and then
        public FightStance Stance;    // squared up for a fist fight (story fighters)
        public float WindUp;          // ...0..1 through drawing the fist back (FightStance.WindUp)
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
        /// Motion-captured clips for this one (<see cref="AvatarClips"/>; <see cref="AvatarClips.Enabled"/> turns them
        /// on for everyone): while it just stands, walks or runs, the body, legs and arms come from the clips.
        /// </summary>
        public bool UseClips;
        private AvatarClips _clips;
        /// <summary>This frame's body came from the clips (the head and the rest go on top of it).</summary>
        public bool ClipDriven { get; private set; }

        /// <summary>
        /// Sitting on a towel (<see cref="AvatarPose.Sit"/>), the hips sit this far behind the character's root. Lying
        /// poses have the feet at the root and the body behind it, so all towel poses share one root: turning over or
        /// sitting up blends in place instead of jumping.
        /// </summary>
        public const float SitBack = 0.8f;

        // The bar stool (ArtSource/Tools/model_bar_stool.py), from its cushion top: the footrest ring this far down and
        // this wide, and the lower rail's front bar for longer legs.
        private const float StoolRingDrop = 0.29f, StoolRingRadius = 0.24f, StoolRingTube = 0.014f;
        private const float StoolRailDrop = 0.56f, StoolRailFront = 0.13f, StoolRailTube = 0.014f;

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
        private float _shoulder, _shoulderRaw; // a long gun up in the shoulder (bladed stance)
        private float _charge;
        private float _lunge;         // 0..1 how far into a lunge (AvatarGesture.Lunge) the body is
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
        /// <summary>The gesture playing now (None once it's over), for tests.</summary>
        public AvatarGesture CurrentGesture => Now - _gestureStart < 1.6f ? _gesture : AvatarGesture.None;
        private float _gestureStart;
        private float _lastPump = -10f;
        private float _nextBlink;
        private float _dt;
        private HandPose _poseL = HandPose.Relaxed, _poseR = HandPose.Relaxed;
        private float _blinkUntil;

        // Reactions (story characters): flinching from a blow, falling when knocked out, glancing back while fleeing.
        private float _flinchStart = -10f;
        private Vector3 _flinchPush;
        private float _flinchSide = 1f, _flinchPower = 1f;
        private float _downSince = -10f, _upSince = -10f;
        private bool _wasDown;
        // Knocked down: the body goes over in this long (knees give, sits back, the back lands); getting up again
        // (sit up, feet in, up from a crouch) takes this long.
        private const float FallTime = 0.8f, GetUpTime = 1.2f;
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

        private static float FarInterval => Core.GraphicsQuality.Current == Core.GraphicsQuality.Tier.Low ? 1f / 15f : 1f / 20f;
        private static int _viewFrame = -1;
        private static Vector3 _view;
        private static bool _hasView;

        /// <summary>Further than 25 m (Low) / 45 m (Medium) from the camera. Never on High.</summary>
        private bool IsFar()
        {
            float far = Core.GraphicsQuality.Current switch
            {
                Core.GraphicsQuality.Tier.Low => 25f,
                Core.GraphicsQuality.Tier.Medium => 45f,
                _ => 0f,
            };
            if (far <= 0f) return false;
            if (_viewFrame != Time.frameCount)
            {
                _viewFrame = Time.frameCount;
                Camera cam = Camera.main;
                _hasView = cam != null;
                if (_hasView) _view = cam.transform.position;
            }
            return _hasView && (transform.position - _view).sqrMagnitude > far * far;
        }

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
        /// <param name="power">0..1 how hard (1: the full snap; a jab is about a third of it).</param>
        public void Flinch(Vector3 push, float power = 1f)
        {
            _flinchPower = Mathf.Clamp01(power);
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
        /// <summary>Tests: everybody's hands closed into fists (console "fists").</summary>
        public static bool DebugFists;

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            // (Batch runs draw nothing, so nothing is ever "visible": tests there keep the full rate.)
            if (CullWhenHidden && !Application.isBatchMode && _rig != null && _rig.Renderer != null)
            {
                // Seen, but far away on a weaker graphics level: posed 15-20 times a second, nobody can tell.
                float interval = !_rig.Renderer.isVisible ? HiddenInterval : IsFar() ? FarInterval : 0f;
                if (interval > 0f)
                {
                    _hidden += dt;
                    if (_hidden < interval) return;
                    dt = _hidden;
                }
            }
            _hidden = 0f;
            Tick(dt);
        }

        public void Tick(float dt)
        {
            if (_rig == null || !_rig.IsBuilt) return;
            _dt = dt;
            if (Motion.Pose == AvatarPose.SitStool)
            {
                // A bar guest: sat on the stool just as a player is.
                Motion.Pose = AvatarPose.Normal;
                Motion.Seated = Motion.ChairSeat = Motion.HighSeat = true;
                Motion.Velocity = Vector3.zero;
                Motion.Grounded = true;
            }
            UpdateBlends(dt);
            UpdateIdle();
            SampleGround(dt);
            _rig.ResetPose(fingers: false); // PoseHands sets every finger bone
            ClipDriven = WantsClips() && _clips.Prepare();
            if (ClipDriven)
            {
                // Standing, walking or running: the clips pose the body, legs and arms; the hands rest as they hang.
                Vector3 ground = Motion.Velocity;
                ground.y = 0f;
                _clips.Evaluate(ground.magnitude, dt);
                _rig.LeftHand?.Pose(HandPose.Relaxed);
                _rig.RightHand?.Pose(HandPose.Relaxed);
            }
            else
            {
                PoseBody();
                PoseBurp();
                PoseLegs();
                PoseArms();
            }
            PoseHead();
            PoseSpecial();
            KeepHandsOutOfHead();
            KeepHandsOutOfOthers();
            if (ClipDriven) KeepHandsOutOfSelf();
            PoseFace();
            if (DebugFists) { _rig.LeftHand?.Pose(HandPose.Fist); _rig.RightHand?.Pose(HandPose.Fist); }
            _rig.DriveShoulders();
            // Sitting on a chair: measure the seat of the pants now the whole sitting pose is on (see SeatDrop).
            if (_seatDropWanted && MeasureSeatDrop(out float drop, out float stoolDrop, out float chairDrop))
            {
                // (Never lower on the second pass: that would sink it back in.)
                _seatDrop = Mathf.Max(_seatDrop, drop);
                _stoolDrop = Mathf.Max(_stoolDrop, stoolDrop);
                _chairDrop = Mathf.Max(_chairDrop, chairDrop);
                _seatDropPasses++;
                _seatDropFrames = 0;
            }
            _seatDropWanted = false;
        }

        /// <summary>
        /// Only plain standing, walking and running go to the clips for now: anything with hands on something, a pose
        /// of its own, water, a seat or a fight stays with the hand-made animation.
        /// </summary>
        private bool WantsClips()
        {
            if (!UseClips && !AvatarClips.Enabled) return false;
            AvatarMotion m = Motion;
            if (!m.Grounded || m.Swimming || m.Underwater || m.Climbing || m.Pose != AvatarPose.Normal || m.Seated || m.Cpr ||
                m.Defib || m.CarryingPerson || m.Holding || m.Stance != FightStance.None || m.Flying || m.StarJump ||
                m.Crouch > 0.05f || m.Eating || m.Drinking || m.Charge > 0.01f || GestureActive(AvatarGesture.Lunge, LungeSeconds)) return false;
            _clips ??= new AvatarClips(_rig);
            return true;
        }

        private void OnDestroy() => _clips?.Release();

        /// <summary>
        /// Never a hand inside somebody else: a punch stops on the face, CPR hands rest on the chest, arms round a
        /// carried friend's legs hug them instead of passing through. Whatever the pose wanted, a hand that would end
        /// up inside another character (see <see cref="BodySpace"/>) is moved out to their skin by bending the arm,
        /// and keeps the way it was turned.
        /// </summary>
        private void KeepHandsOutOfOthers()
        {
            if (BodySpace.Rigs.Count < 2) return;
            float s = _rig.Scale;
            float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                bool right = i == 1;
                Transform upper = B(right ? Bone.UpperArmR : Bone.UpperArmL), fore = B(right ? Bone.ForearmR : Bone.ForearmL), hand = B(right ? Bone.HandR : Bone.HandL);
                if (upper == null || fore == null || hand == null) continue;
                BodySpace.Near(hand.position, 0.3f * s, _rig, IgnoreBody, _nearBodies);
                if (_nearBodies.Count == 0) continue;
                HandBones fingers = _rig.Hand(right);
                // The whole hand, not just the middle of the palm: the cartoon hands are 17 cm long with fanned
                // fingers, so fingertips, the thumb and the wrist went into people while the palm stayed out (the
                // rescuer's fingers in the face of the one given a breath, a carried tourist's back, a passer-by).
                // The hand moves by the furthest any of them has to, a few passes (moving it can put another point in).
                for (int pass = 0; pass < 3; pass++)
                {
                    Vector3 palm = hand.position - hand.up * (_rig.HandLength * 0.5f); // (limbs point down their -Y)
                    Vector3 push = Vector3.zero;
                    PushPoint(palm, 0.035f * s, ref push);
                    PushPoint(hand.position, 0.03f * s, ref push); // the wrist
                    if (fingers != null)
                        for (int f = 0; f < HandBones.Fingers; f++) PushPoint(fingers.Tip(f), 0.014f * s, ref push);
                    if (push.sqrMagnitude < 1e-6f) break;
                    // Holding somebody up from underneath: out downward only (the shortest way out of a round belly
                    // is often up and through it, which put the hands on top of the body they should hold up).
                    if (Motion.CarryingPerson && _hold > 0.5f)
                    {
                        float down = 0f;
                        for (float d = 0.01f * s; d <= 0.25f * s; d += 0.01f * s)
                        {
                            Vector3 low = Vector3.zero, shift = Vector3.down * d;
                            PushPoint(palm + shift, 0.035f * s, ref low);
                            PushPoint(hand.position + shift, 0.03f * s, ref low);
                            if (fingers != null)
                                for (int f = 0; f < HandBones.Fingers; f++) PushPoint(fingers.Tip(f) + shift, 0.014f * s, ref low);
                            down = d;
                            if (low.sqrMagnitude < 1e-6f) break;
                        }
                        push = Vector3.down * down;
                        pass = 2; // (one move: done)
                    }
                    Vector3 target = palm + push;
                    Quaternion turned = hand.rotation;
                    // Bend the elbow the way it already points.
                    Vector3 elbow = fore.position - (upper.position + target) * 0.5f;
                    IK.Solve(upper, fore, la, lb, target, elbow, 1f, false);
                    hand.rotation = turned;
                }
                SwivelForearmClear(upper, fore, hand, s);
            }
        }

        private readonly List<AvatarRig> _nearBodies = new();

        /// <summary>
        /// A forearm through somebody (holding a big-bellied tourist against the chest, a passer-by brushing past):
        /// the elbow swings round the line from the shoulder to the hand, which stays where it is, until the forearm
        /// lies along the body instead of in it.
        /// </summary>
        private void SwivelForearmClear(Transform upper, Transform fore, Transform hand, float s)
        {
            float radius = 0.03f * s;
            float Depth()
            {
                Vector3 p = Vector3.Lerp(fore.position, hand.position, 0.45f), q = p;
                float d = BodySpace.PushOut(ref q, radius, _nearBodies) ? (q - p).magnitude : 0f;
                Vector3 e = Vector3.Lerp(fore.position, hand.position, 0.1f), r = e;
                if (BodySpace.PushOut(ref r, radius, _nearBodies)) d = Mathf.Max(d, (r - e).magnitude);
                return d;
            }
            float depth = Depth();
            if (depth < 0.005f) return;
            Vector3 axis = hand.position - upper.position;
            if (axis.sqrMagnitude < 1e-4f) return;
            axis.Normalize();
            Quaternion handTurn = hand.rotation, start = upper.rotation;
            // Which way round frees it sooner: try a step each way.
            upper.rotation = Quaternion.AngleAxis(12f, axis) * start;
            float plus = Depth();
            upper.rotation = Quaternion.AngleAxis(-12f, axis) * start;
            float minus = Depth();
            float sign = plus <= minus ? 1f : -1f;
            float best = depth, bestAngle = 0f;
            for (int step = 1; step <= 10; step++)
            {
                float angle = sign * 12f * step;
                upper.rotation = Quaternion.AngleAxis(angle, axis) * start;
                float d = Depth();
                if (d < best) { best = d; bestAngle = angle; }
                if (d < 0.005f) break;
            }
            upper.rotation = Quaternion.AngleAxis(bestAngle, axis) * start;
            hand.rotation = handTurn;
        }

        /// <summary>The push that takes this point out of the bodies near, if it is the biggest so far.</summary>
        /// <summary>
        /// The clips' arms swing across the body as they did on the slim dummy they were made on: on our rounder hips,
        /// bellies and skirts a hand sank in. Each hand is moved out to its own skin by bending the arm (as for other
        /// people, <see cref="KeepHandsOutOfOthers"/>), keeping the way it was turned.
        /// </summary>
        private void KeepHandsOutOfSelf()
        {
            float s = _rig.Scale;
            float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                bool right = i == 1;
                Transform upper = B(right ? Bone.UpperArmR : Bone.UpperArmL), fore = B(right ? Bone.ForearmR : Bone.ForearmL), hand = B(right ? Bone.HandR : Bone.HandL);
                if (upper == null || fore == null || hand == null) continue;
                HandBones fingers = _rig.Hand(right);
                for (int pass = 0; pass < 3; pass++)
                {
                    Vector3 palm = hand.position - hand.up * (_rig.HandLength * 0.5f); // (limbs point down their -Y)
                    Vector3 push = Vector3.zero;
                    PushOwn(palm, 0.035f * s, ref push);
                    PushOwn(hand.position, 0.03f * s, ref push);
                    if (fingers != null)
                        for (int f = 0; f < HandBones.Fingers; f++) PushOwn(fingers.Tip(f), 0.014f * s, ref push);
                    if (push.sqrMagnitude < 1e-6f) break;
                    Vector3 target = palm + push;
                    Quaternion turned = hand.rotation;
                    Vector3 elbow = fore.position - (upper.position + target) * 0.5f;
                    IK.Solve(upper, fore, la, lb, target, elbow, 1f, false);
                    hand.rotation = turned;
                }
            }
        }

        private void PushOwn(Vector3 point, float radius, ref Vector3 push)
        {
            Vector3 q = point;
            if (!BodySpace.PushOutOfOwn(_rig, ref q, radius)) return;
            if ((q - point).sqrMagnitude > push.sqrMagnitude) push = q - point;
        }

        private void PushPoint(Vector3 point, float radius, ref Vector3 push)
        {
            Vector3 q = point;
            if (!BodySpace.PushOut(ref q, radius, _nearBodies)) return;
            if ((q - point).sqrMagnitude > push.sqrMagnitude) push = q - point;
        }

        /// <summary>A body this one's hands may be inside of (somebody being carried on the shoulder, held for a kiss...). Null: none.</summary>
        public AvatarRig IgnoreBody { get; set; }

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
            if (Motion.Shouldered && _hold > 0.01f) return; // (the trigger hand of a shouldered gun is right under the chin)
            if (Motion.Basketball && _hold > 0.01f) return; // ball grips already place the palms; swinging them breaks contact
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
                if (_striking != 0) continue; // (a punch passes the face on its way out, and the guard sits right in front of it: both are placed clear of the head already)
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
            _shoulder = Eased(ref _shoulderRaw, m.Holding && m.Shouldered, 5f, dt);
            _charge = Mathf.MoveTowards(_charge, m.Charge, dt * 8f);
            _eat = Eased(ref _eatRaw, m.Eating, 5f, dt);
            _cpr = Eased(ref _cprRaw, m.Cpr, 4f, dt);
            _climb = Eased(ref _climbRaw, m.Climbing, 6f, dt);
            _seat = Eased(ref _seatRaw, m.Seated, 6f, dt);
            _fly = Eased(ref _flyRaw, m.Flying, 5f, dt);
            _star = Eased(ref _starRaw, m.StarJump && !m.Flying, 7f, dt);
            bool isDown = m.Pose == AvatarPose.Down;
            if (isDown && !_wasDown) _downSince = Now;
            if (!isDown && _wasDown) _upSince = Now;
            _wasDown = isDown;
            // (Not an eased blend: PoseDown plays the fall and the get-up through the lying pose itself. Fully on at
            // once when going down, so the fall starts from the standing pose; while getting up it lets go at the end.)
            float rising = (Now - _upSince) / GetUpTime;
            _down = _downRaw = isDown ? Mathf.Clamp01((Now - _downSince) / 0.05f)
                : rising < 1f ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 1f, rising)) : 0f;
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
            _lunge = m.Pose == AvatarPose.Normal && !m.Swimming && !m.Seated ? LungeWeight() : 0f;

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

        /// <summary>Degrees the chest turns to the right holding a long gun up in the shoulder.</summary>
        private const float BladeTwist = 15f;

        private float GestureT(float duration) => Mathf.Clamp01((Now - _gestureStart) / duration);

        /// <summary>A lunge: in fast (<see cref="LungeIn"/>), held out until <see cref="LungeHold"/>, back by the end.</summary>
        public const float LungeSeconds = 0.8f, LungeIn = 0.16f, LungeHold = 0.4f;

        /// <summary>0..1 how far into a lunge the body is now (fully in from LungeIn to LungeHold).</summary>
        private float LungeWeight()
        {
            if (!GestureActive(AvatarGesture.Lunge, LungeSeconds)) return 0f;
            float t = Now - _gestureStart;
            if (t < LungeIn)
            {
                float k = 1f - t / LungeIn;
                return 1f - k * k; // out of the gate fast, slowing into the reach
            }
            return t < LungeHold ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (t - LungeHold) / (LungeSeconds - LungeHold));
        }
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
            float drop = _crouch * 0.32f * s * legs * legs * land + _cpr * 0.3f * s * land; // (CPR: knees on the sand, not in it)
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
            float lean = (4f + 9f * _run) * _move * land + _crouch * 18f * land + _cpr * 2f + _air * -6f + _leanForward.Value * standing;
            // Carrying somebody in our arms: leaning back against the weight (and the big head back over them).
            lean -= 11f * CarryLean * standing;
            float tip = (_leanSide.Value + _turnLag.Value * 0.25f - sway * 2.2f) * standing;
            // The crawl rolls the whole body round its long axis: the shoulder of the arm coming over is the high one.
            Quaternion roll = Quaternion.AngleAxis(CrawlRoll * crawl, Vector3.up);
            hips.localRotation = Quaternion.Euler(lean * 0.35f + swimPitch * _swim, Wave(_phase) * 4f * _move * land, stroke * 5f + tip) * roll;

            B(Bone.Spine).localRotation = Quaternion.Euler(lean * 0.35f + breath * 1.2f + _variety.Slouch * standing, -Wave(_phase) * 5f * _move * land,
                -stroke * 2.5f - tip * 0.6f);
            // The chest takes a share of looking around (before the arms, which hang off it).
            B(Bone.Chest).localRotation = Quaternion.Euler(lean * 0.3f + LookPitch * 0.15f + _variety.Slouch * 0.5f * standing,
                -Wave(_phase) * 5f * _move * land + LookYaw * 0.25f + _turnLag.Value * 0.5f * standing, stroke * 1.5f - tip * 0.3f);
            // A long gun up in the shoulder: side on to the target, the right shoulder back to take the stock and the
            // left one forward so that hand reaches out along the barrel (the head turns back to the front, PoseHead).
            if (_shoulder > 0.01f)
            {
                float blade = BladeTwist * _shoulder * standing;
                B(Bone.Spine).localRotation *= Quaternion.Euler(0f, blade * 0.4f, 0f);
                B(Bone.Chest).localRotation *= Quaternion.Euler(0f, blade * 0.6f, 0f);
            }

            // Braced for a lunge (the weapon drawn back): rocked back onto the rear foot, the back leaning away a little.
            float brace = Mathf.Clamp01(Motion.Brace) * standing * (1f - _lunge);
            if (brace > 0.001f)
            {
                hips.position -= transform.forward * (0.07f * s * brace);
                B(Bone.Spine).localRotation *= Quaternion.Euler(-6f * brace, 0f, 0f);
                B(Bone.Chest).localRotation *= Quaternion.Euler(-4f * brace, 0f, 0f);
            }
            // A lunge: the hips go forward and down over the front foot (FootTarget steps it in) and the back leans into
            // it, here before the arms, so hands on a held weapon's grips go with the body.
            if (_lunge > 0.001f)
            {
                float l = _lunge * standing;
                hips.position += transform.forward * (0.26f * s * l) - Vector3.up * (0.08f * s * l);
                B(Bone.Spine).localRotation *= Quaternion.Euler(10f * l, 0f, 0f);
                B(Bone.Chest).localRotation *= Quaternion.Euler(8f * l, 0f, 0f);
            }

            // Seated: the hips go down onto the seat here, before the arms reach for the handlebars or handles (done
            // after the arms, it dragged the hands 40 cm down into the rider's lap and through the banana).
            if (_seat > 0.01f)
            {
                Vector3 seatedHip = _rig.RestPosition(Bone.Hips);
                // The float's anchor is 0.5 m above the player root; match it
                // independently of body height so short bodies do not sink into the ring.
                // A chair's seat point is its cushion top: the seat of the pants sits right on it. How far that is under
                // the hip bone is measured on the posed body (the goofy lifeguard's round bottom: 24 cm).
                seatedHip.y = Motion.ChairSeat ? 0.5f + SeatDrop() : Motion.FloatSeat ? 0.5f : seatedHip.y - 0.4f * s;
                hips.localPosition = Vector3.Lerp(hips.localPosition, seatedHip, _seat);
                // (On a chair: upright, not leaning back off the seat as on a jet ski.)
                hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(Motion.ChairSeat ? 0f : 8f, 0f, 0f), _seat);
                if (Motion.Straddle) // leaning on toward the handle in front, like on a horse
                    B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.Euler(14f, 0f, 0f), _seat);
                else if (Motion.HighSeat) // at the bar: a little forward over the knees
                    B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.Euler(6f, 0f, 0f), _seat);
            }
        }

        /// <summary>
        /// A hand laid on a point (seated: on the knee, on the cushion): the arm reaches so the palm lands right there,
        /// the fingers pointing <paramref name="fingers"/>, the palm facing <paramref name="palm"/>. (Reaching only for
        /// the middle of the hand, it kept the forearm's slope and the fingers went down into the cushion.)
        /// </summary>
        private void PlaceHand(bool right, Vector3 point, Vector3 fingers, Vector3 palm, Vector3 elbow, float weight, HandPose pose)
        {
            Transform upper = B(right ? Bone.UpperArmR : Bone.UpperArmL), fore = B(right ? Bone.ForearmR : Bone.ForearmL);
            HandBones hand = _rig.Hand(right);
            if (hand == null)
            {
                IK.Solve(upper, fore, _rig.UpperArmLength, _rig.ForearmLength + _rig.HandLength * 0.5f, point, elbow, weight, false);
                return;
            }
            Quaternion turn = HandBones.Orient(fingers, palm, right ? 1f : -1f);
            Vector3 wrist = point - turn * Vector3.Scale(hand.PalmContact, hand.Hand.lossyScale);
            IK.Solve(upper, fore, _rig.UpperArmLength, _rig.ForearmLength, wrist, elbow, weight, false);
            hand.Hand.rotation = Quaternion.Slerp(hand.Hand.rotation, turn, weight);
            hand.Pose(pose);
        }

        private float _seatDrop = -1f;
        private float _stoolDrop = -1f;      // ...on a bar stool: over its round cushion, the whole width of the bottom
        private float _chairDrop = -1f;      // ...in an armchair: over its whole cushion, back to its front edge (the thighs too)
        private int _seatDropVersion = -1;
        private int _seatDropFrames;
        private int _seatDropPasses;
        private bool _seatDropWanted;
        private float _lapHalfWidth = -1f;   // half the width of the legs and shorts when sitting (where the hands go beside them)
        private float _kneeTop = -1f;        // sitting: how far the top of the leg is over the bone just behind the knee (the hands rest there)
        private float _heelBack = -1f;       // how far the heel sticks out behind the ankle (the goofy lifeguard's cartoon feet: 12 cm)
        private const float StoolCushionRadius = 0.2f;

        /// <summary>How far the heel sticks out behind the ankle (measured with the seat drop; a guess before).</summary>
        private float HeelBack() => _heelBack >= 0f && _seatDropVersion == _rig.BuildVersion ? _heelBack : 0.06f * _rig.Scale;

        /// <summary>Sitting: the top of the leg over the thigh bone just behind the knee (measured with the seat drop; a guess before).</summary>
        private float KneeTop() => _kneeTop >= 0f && _seatDropVersion == _rig.BuildVersion ? _kneeTop : 0.09f * _rig.Scale;

        /// <summary>
        /// Sitting on a chair: how far the seat of the pants is below the hip bone, measured per body on the mesh as it
        /// sits (thighs forward), so the bottom rests on the cushion instead of in it. A guess until measured; measured
        /// twice (the second time with the hips at the first measure: the thighs lie a little differently then).
        /// </summary>
        private float SeatDrop()
        {
            if (_seatDropVersion != _rig.BuildVersion)
            {
                _seatDropVersion = _rig.BuildVersion;
                _seatDrop = _stoolDrop = _chairDrop = -1f;
                _seatDropFrames = _seatDropPasses = 0;
            }
            // Measured at the end of the frame (the legs are posed after the hips), once the sitting pose is fully in.
            if (_seatDropPasses < 2 && _seat > 0.98f && ++_seatDropFrames > 3) _seatDropWanted = true;
            if (_seatDropPasses == 0) return 0.2f * _rig.Scale;
            return Motion.HighSeat ? _stoolDrop : Motion.SeatEdge > 0f ? _chairDrop : _seatDrop;
        }

        private bool MeasureSeatDrop(out float drop, out float stoolDrop, out float chairDrop)
        {
            drop = stoolDrop = chairDrop = 0f;
            SkinnedMeshRenderer body = null;
            foreach (SkinnedMeshRenderer r in _rig.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (r.sharedMesh != null && (body == null || r.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = r;
            if (body == null) return false;
            var baked = new Mesh();
            body.BakeMesh(baked, true);
            Transform hips = B(Bone.Hips);
            float bottom = float.MaxValue, lap = 0f;
            Quaternion toLocal = Quaternion.Inverse(transform.rotation);
            Vector3[] vertices = baked.vertices;
            // What each vertex is (by its heaviest bone): the seat of the pants is the hips' and the thighs' (a heel tucked
            // under a stool, the girls' long hair, the goofy lifeguard's big head hanging over his knees: none of it).
            BoneWeight[] weights = body.sharedMesh.boneWeights;
            Transform[] bones = body.bones;
            bool weighted = weights.Length == vertices.Length && bones != null;
            Transform upperL = B(Bone.UpperArmL), upperR = B(Bone.UpperArmR), neck = B(Bone.Neck), shinL = B(Bone.ShinL), shinR = B(Bone.ShinR);
            // Just behind each knee, on the thigh bone (where the hands rest on a stool).
            Vector3 kneeL = Vector3.Lerp(B(Bone.ThighL).position, shinL.position, 0.72f);
            Vector3 kneeR = Vector3.Lerp(B(Bone.ThighR).position, shinR.position, 0.72f);
            float kneeTop = float.MinValue, near = 0.07f * _rig.Scale, onCushion = float.MaxValue, onChair = float.MaxValue, heel = 0f;
            Transform footL = B(Bone.FootL), footR = B(Bone.FootR);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = body.transform.position + body.transform.rotation * vertices[i];
                Vector3 local = toLocal * (world - hips.position);
                Transform bone = null;
                if (weighted)
                {
                    int b = weights[i].boneIndex0;
                    bone = b >= 0 && b < bones.Length ? bones[b] : null;
                }
                bool arm = bone != null && (bone.IsChildOf(upperL) || bone.IsChildOf(upperR));
                bool head = bone != null && bone.IsChildOf(neck);
                bool lowerLeg = bone != null && (bone.IsChildOf(shinL) || bone.IsChildOf(shinR));
                if (arm || head) continue;
                // The top of each leg just behind the knee.
                if (new Vector2(world.x - kneeL.x, world.z - kneeL.z).sqrMagnitude < near * near) kneeTop = Mathf.Max(kneeTop, world.y - kneeL.y);
                if (new Vector2(world.x - kneeR.x, world.z - kneeR.z).sqrMagnitude < near * near) kneeTop = Mathf.Max(kneeTop, world.y - kneeR.y);
                // The heel: how far the foot reaches back behind the ankle, low down (within a hand over the ankle).
                if (lowerLeg)
                {
                    Vector3 ankle = bone.IsChildOf(shinL) ? footL.position : footR.position;
                    if (world.y < ankle.y + 0.1f * _rig.Scale) heel = Mathf.Max(heel, Vector3.Dot(ankle - world, transform.forward));
                    continue;
                }
                // On a bar stool: anything over its round cushion (the seat point is straight over the root).
                Vector3 fromSeat = toLocal * (world - transform.position);
                if (new Vector2(fromSeat.x, fromSeat.z).sqrMagnitude < StoolCushionRadius * StoolCushionRadius)
                    onCushion = Mathf.Min(onCushion, world.y - hips.position.y);
                // In an armchair: anything over its cushion, out to its front edge.
                if (Motion.SeatEdge > 0f && Mathf.Abs(fromSeat.x) < 0.4f && fromSeat.z < Motion.SeatEdge && fromSeat.z > -0.45f)
                    onChair = Mathf.Min(onChair, world.y - hips.position.y);
                // The seat of the pants: under the pelvis, not out along the thighs (ahead).
                if (Mathf.Abs(local.x) < 0.14f * _rig.Scale && local.z < 0.06f && local.z > -0.25f && local.y < 0f) bottom = Mathf.Min(bottom, local.y);
                // The lap: legs and shorts from the seat up to a hand over the hips, from the back to the knees.
                if (local.y > 0.12f * _rig.Scale || local.z < -0.25f || local.z > 0.45f || local.y < -0.4f) continue;
                lap = Mathf.Max(lap, Mathf.Abs(local.x));
            }
            if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            if (bottom == float.MaxValue) return false;
            drop = Mathf.Clamp(-bottom, 0.05f, 0.45f);
            stoolDrop = onCushion < float.MaxValue ? Mathf.Clamp(-onCushion, 0.05f, 0.45f) : drop;
            chairDrop = onChair < float.MaxValue ? Mathf.Clamp(-onChair, 0.05f, 0.45f) : drop;
            _lapHalfWidth = Mathf.Clamp(lap, 0.08f, 0.45f);
            _kneeTop = kneeTop > 0f ? Mathf.Clamp(kneeTop, 0.02f, 0.3f) : -1f;
            if (heel > 0f) _heelBack = Mathf.Clamp(heel, 0.03f, 0.2f);
            Debug.Log($"[Avatar] {name}: sitting, the bottom {drop * 100f:F0} cm under the hips ({stoolDrop * 100f:F0} on a stool, {chairDrop * 100f:F0} in an armchair), the knees' top {kneeTop * 100f:F0} cm over the bone, the heels {heel * 100f:F0} cm behind the ankles");
            return true;
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
            world = LockFoot(leftFoot ? 0 : 1, world, Mathf.Repeat(_phase + offset, 1f));
            Vector3 knee = transform.forward;
            float weight = 1f - _swim;
            IK.Solve(thigh, shin, _rig.ThighLength, _rig.ShinLength, world, knee, weight, true);

            // Feet flat on the ground (toes point where the body faces, the sole lies along the slope), rolling a
            // little while stepping.
            Quaternion slope = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, leftFoot ? _normalL : _normalR), _groundWeight);
            Quaternion flat = slope * transform.rotation * Quaternion.Euler(-roll + _cpr * 70f, 0f, 0f);
            foot.rotation = Quaternion.Slerp(foot.rotation, flat, weight);
        }

        // Foot lock: where each foot is drawn (world), and where a lifted one left the ground.
        private readonly Vector3[] _footDrawn = new Vector3[2], _footLift = new Vector3[2];
        private readonly bool[] _footKnown = new bool[2];

        /// <summary>
        /// A planted foot stays where it was put down while the body walks on over it (the cycle is only right at a
        /// steady speed in a straight line: speeding up, slowing down and turning slid it along the sand); it may only
        /// creep, pivoting, toward where the cycle wants it. A lifting foot swings from where it really was. Standing,
        /// the feet glide back to their places no faster than a shuffle. The feet never cross over.
        /// </summary>
        private Vector3 LockFoot(int i, Vector3 cycle, float q)
        {
            float s = _rig.Scale, dt = Mathf.Max(_dt, 1e-4f);
            Vector3 drawn = _footDrawn[i];
            if (!_footKnown[i] || (new Vector2(drawn.x - cycle.x, drawn.z - cycle.z)).sqrMagnitude > 0.6f * 0.6f * s * s)
            {
                _footKnown[i] = true;
                drawn = _footLift[i] = cycle; // first frame, a teleport, a seat: no gliding from far away
            }
            bool walking = _move > 0.05f && _swim < 0.01f && _air < 0.01f && _climb < 0.01f && _upright > 0.95f &&
                           _cpr < 0.01f && _crouch < 0.5f && Motion.Grounded && !Motion.Seated && _seat < 0.01f;
            bool posed = _swim > 0.01f || _air > 0.01f || _climb > 0.01f || _upright < 0.95f || _cpr > 0.01f || _seat > 0.01f || Motion.Seated ||
                         _lunge > 0.01f; // (a lunge steps the front foot in faster than a planted foot may shuffle)
            if (posed)
            {
                // Swimming, jumping, kneeling, sitting...: those poses place the feet themselves.
                _footDrawn[i] = _footLift[i] = cycle;
                return cycle;
            }
            if (walking && q >= 0.5f)
            {
                // Swing: from where it left the ground to the cycle's next footfall, eased in.
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((q - 0.5f) / 0.35f));
                drawn = new Vector3(Mathf.Lerp(_footLift[i].x, cycle.x, t), cycle.y, Mathf.Lerp(_footLift[i].z, cycle.z, t));
            }
            else
            {
                // Planted (creeping at most 0.35 m/s), or standing (shuffling back at most 1.4 m/s).
                float rate = (walking ? 0.35f : 1.4f) * s * dt;
                Vector2 at = Vector2.MoveTowards(new Vector2(drawn.x, drawn.z), new Vector2(cycle.x, cycle.z), rate);
                drawn = new Vector3(at.x, cycle.y, at.y);
                _footLift[i] = drawn;
            }
            // Never crossed over the other foot's side of the body.
            Vector3 local = transform.InverseTransformPoint(drawn);
            float side = i == 0 ? -1f : 1f, inner = 0.065f * s;
            if (local.x * side < inner)
            {
                // (Eased out, a few centimetres a frame: shoved out at once, the foot jumped on a sharp turn.)
                local.x = Mathf.MoveTowards(local.x, inner * side, 0.045f * s);
                drawn = transform.TransformPoint(local);
            }
            _footDrawn[i] = drawn;
            return drawn;
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
            // Lunging: the left foot steps well in, the right one pushes back a little.
            if (_lunge > 0f) target.z += (side < 0f ? 0.4f : -0.1f) * s * _lunge;

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
        // CPR: kneel tall over the chest, never folded over it (a big head bowed that far comes down over the hands).
        private const float MaxCprLean = 30f;
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
                    weight = _hold * (m.Defib ? 1f : 1f - _cpr);
                    rotation = grip.Rotation(side);
                    pose = HandPose.Lerp(pose, grip.Pose, weight);
                }
                if (_cpr > 0.01f && !m.Defib)
                {
                    rotation = HandBones.Orient(fwd, Vector3.down, side);
                    weight = _cpr;
                    // The lower hand's heel on the chest, fingers lifted a little; the upper one's fingers curl down
                    // round it (spread flat, the cartoon fingers fanned out like claws).
                    pose = HandPose.Lerp(pose, right ? CprLowerHand : CprUpperHand, _cpr);
                }
                if (right && _eat > 0.01f && !m.Drinking) pose = HandPose.Lerp(pose, HandPose.Cup, _eat);
                if (_lunge > 0.01f && !(usesGrip && _hold > 0.01f) && !m.LongProp) pose = HandPose.Lerp(pose, HandPose.Cup, _lunge); // (a grab: hands open to take hold)
                if (!right && m.LongProp && !usesGrip) pose = HandPose.Lerp(pose, HandPose.Point, Mathf.Clamp01(m.Brace) * (1f - _lunge) * _hold); // (the free hand points at whoever is about to be grabbed)
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

        /// <summary>Where the wrist goes so that, turned the grip's way, the palm sits on the grip.</summary>
        private Vector3 WristFor(HandGrip grip, bool right)
        {
            HandBones hand = _rig.Hand(right);
            if (hand == null) return grip.Point;
            return grip.Point - grip.Rotation(right ? 1f : -1f) * Vector3.Scale(hand.PalmContact, hand.Hand.lossyScale);
        }

        /// <summary>Hands on things: held items, the mouth while eating, a chest during CPR, the wind-up of a throw.</summary>
        /// <summary>
        /// Sitting with both hands on handlebars or a wheel the short cartoon arms can't reach (a jet ski's bars are
        /// 30 cm beyond them): slide forward on the seat and lean in from the hips, as riders do, until they can.
        /// </summary>
        private void ReachForGrips(Vector3 gripL, Vector3 gripR, float reach, float weight)
        {
            Transform hips = B(Bone.Hips), spine = B(Bone.Spine), chest = B(Bone.Chest);
            Transform sl = B(Bone.UpperArmL), sr = B(Bone.UpperArmR);
            if (hips == null || spine == null || chest == null) return;
            float s = _rig.Scale, want = reach * 0.93f;
            float Short() => Mathf.Max(Vector3.Distance(sl.position, gripL), Vector3.Distance(sr.position, gripR)) - want;
            float over = Short();
            if (over <= 0f) return;
            Vector3 toward = Vector3.ProjectOnPlane((gripL + gripR) * 0.5f - (sl.position + sr.position) * 0.5f, Vector3.up);
            if (toward.sqrMagnitude < 1e-4f) return;
            toward.Normalize();
            // Slide forward on the seat first (at most 20 cm), then lean in (at most 45 degrees, the chest a little more).
            float slide = Mathf.Min(over * 0.6f, 0.2f * s) * weight;
            hips.position += toward * slide;
            Vector3 axis = Vector3.Cross(Vector3.up, toward);
            float lean = 0f;
            for (int i = 0; i < 8 && lean < 45f; i++)
            {
                over = Short();
                if (over <= 0.005f) break;
                float torso = Mathf.Max(0.2f, Vector3.Distance(spine.position, (sl.position + sr.position) * 0.5f));
                float step = Mathf.Min(45f - lean, Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp01(over / torso)) * 0.8f + 1f);
                spine.rotation = Quaternion.AngleAxis(step * 0.55f * weight, axis) * spine.rotation;
                chest.rotation = Quaternion.AngleAxis(step * 0.45f * weight, axis) * chest.rotation;
                lean += step;
            }
            // (The head stays looking ahead: the lean bowed it down onto the bars.)
            if (B(Bone.Neck) is { } neck) neck.rotation = Quaternion.AngleAxis(-lean * 0.6f * weight, axis) * neck.rotation;
        }

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
                float w = _hold * (m.Defib ? 1f : 1f - _cpr);
                Vector3 gl = m.GripLeft.Point, gr = m.GripRight.Point;
                if (m.TwoHanded && m.GripLeft.Active && m.GripRight.Active && _seat > 0.01f) ReachForGrips(gl, gr, la + lb, w * _seat);
                if (m.Basketball)
                {
                    if (m.GripLeft.Active && m.TwoHanded)
                        IK.Solve(upperL, foreL, la, _rig.ForearmLength, WristFor(m.GripLeft, false), elbowL, w, false);
                    if (m.GripRight.Active)
                        IK.Solve(upperR, foreR, la, _rig.ForearmLength, WristFor(m.GripRight, true), elbowR, w, false);
                }
                else if (m.LongProp)
                {
                    // A pole held low in front (a giant grabber): each palm exactly on its grip (the wrist aimed so the
                    // turned hand lands there, as for a shouldered gun), the elbows hanging down and a little out.
                    if (m.GripLeft.Active && m.TwoHanded)
                        IK.Solve(upperL, foreL, la, _rig.ForearmLength, WristFor(m.GripLeft, false), -up - right * 0.35f, w, false);
                    if (m.GripRight.Active)
                        IK.Solve(upperR, foreR, la, _rig.ForearmLength, WristFor(m.GripRight, true), -up + right * 0.5f - fwd * 0.2f, w, false);
                    if (!(m.GripLeft.Active && m.TwoHanded))
                    {
                        // In one hand: the free arm balances. Braced to lunge it points ahead at the target (the tell);
                        // in the lunge it's flung back and out, low, like a fencer's.
                        float s = _rig.Scale, brace = Mathf.Clamp01(m.Brace) * (1f - _lunge);
                        if (brace > 0.01f)
                            IK.Solve(upperL, foreL, la, lb, upperL.position + fwd * ((la + lb) * 0.92f) + up * (0.06f * s) + right * (0.05f * s), -up - right * 0.5f, brace * w, false);
                        if (_lunge > 0.01f)
                            IK.Solve(upperL, foreL, la, lb, upperL.position - fwd * (0.22f * s) - right * (0.24f * s) - up * (0.26f * s), -right + fwd * 0.3f, _lunge * w, false);
                    }
                }
                else if (!m.TwoHanded && !m.CarryingPerson)
                {
                    // One hand under it, the other relaxed.
                    IK.Solve(upperR, foreR, la, lb, gr, elbowR, w, false);
                }
                else
                {
                    if (m.Shouldered)
                    {
                        // Elbows out and under: the wrists bend a lot, so aim each wrist where the palm lands on its grip.
                        elbowR = right + up * 0.45f;
                        elbowL = -up - right * 0.2f;
                        IK.Solve(upperL, foreL, la, _rig.ForearmLength, WristFor(m.GripLeft, false), elbowL, w, false);
                        IK.Solve(upperR, foreR, la, _rig.ForearmLength, WristFor(m.GripRight, true), elbowR, w, false);
                    }
                    else
                    {
                        IK.Solve(upperL, foreL, la, lb, gl, elbowL, w, false);
                        IK.Solve(upperR, foreR, la, lb, gr, elbowR, w, false);
                    }
                }
            }

            if (_charge > 0.01f && !m.Basketball)
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
                // Arms straight down, hands stacked on the chest (with the defibrillator: on its two paddles).
                Vector3 point = m.Defib ? (m.GripLeft.Point + m.GripRight.Point) * 0.5f : m.CprPoint;
                if (point == Vector3.zero) point = transform.position + fwd * 0.55f + up * 0.15f;
                // Leaning further over a chest the straight arms don't reach (a wide round dad is knelt beside from
                // further out): the shoulders come over the hands, the way compressions are really done, instead of
                // the hands hovering in the air in front of an upright rescuer.
                Transform spine = B(Bone.Spine);
                float armReach = (la + lb) * 0.97f, leaned = 0f;
                for (int k = 0; k < 4 && leaned < MaxCprLean; k++)
                {
                    Vector3 shoulders = (upperL.position + upperR.position) * 0.5f;
                    float gap = Vector3.Distance(shoulders, point + up * 0.05f) - armReach;
                    if (gap <= 0.005f) break;
                    float torso = Mathf.Max(Vector3.Distance(spine.position, shoulders), 0.1f);
                    float step = Mathf.Min(gap / torso * Mathf.Rad2Deg, MaxCprLean - leaned);
                    Turn(spine, right, step * _cpr);
                    leaned += step;
                }
                if (m.Defib)
                {
                    // Leaned over: the hands again on the paddles' handles.
                    IK.Solve(upperL, foreL, la, lb, m.GripLeft.Point, elbowL, _hold, false);
                    IK.Solve(upperR, foreR, la, lb, m.GripRight.Point, elbowR, _hold, false);
                    return;
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
            // A lunge with nothing in the hands is a grab: both hands shoot out ahead at chest height, a little apart.
            if (_lunge > 0.01f && _hold < 0.5f)
            {
                Transform upperL = B(Bone.UpperArmL), foreL = B(Bone.ForearmL);
                float s = _rig.Scale, la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
                Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;
                float w = _lunge * (1f - _hold);
                IK.Solve(upperL, foreL, la, lb, upperL.position + fwd * ((la + lb) * 0.95f) - right * (0.04f * s) - up * (0.06f * s), -up - right * 0.4f, w, false);
                IK.Solve(upperR, foreR, la, lb, upperR.position + fwd * ((la + lb) * 0.95f) + right * (0.04f * s) - up * (0.06f * s), -up + right * 0.4f, w, false);
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
                    // On a chair or stool the knees go a little apart: out past a round stool cushion's edge (straight
                    // ahead the shins came down through it) and clear of the counter in front.
                    Quaternion thighPose = Motion.Straddle ? Quaternion.Euler(-30f, 0f, 58f * side)
                        : Quaternion.Euler(-78f, 0f, (Motion.ChairSeat ? 34f : 16f) * side);
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
                if (Motion.ChairSeat && !Motion.HighSeat && Motion.SeatEdge > 0f)
                {
                    // An armchair: the knees bent over the cushion's front edge, the shins hanging straight down just in
                    // front of it, the toes pointing down like a child's whose feet don't reach the floor (hanging straight
                    // down from fixed angles his shins went down through the cushion; reaching forward, his toes went
                    // into the low table in front).
                    Vector3 fwd = transform.forward, across = transform.right;
                    Vector3 seatTop = transform.position + Vector3.up * 0.5f;
                    for (int leg = 0; leg < 2; leg++)
                    {
                        float side = leg == 0 ? -1f : 1f;
                        Transform thigh = B(leg == 0 ? Bone.ThighL : Bone.ThighR), shin = B(leg == 0 ? Bone.ShinL : Bone.ShinR);
                        Transform foot = B(leg == 0 ? Bone.FootL : Bone.FootR);
                        // (The foot bone is in the middle of the foot: the heel, the bottom of the leg, is HeelBack behind it.)
                        Vector3 ankle = seatTop + fwd * (Motion.SeatEdge + HeelBack() + 0.02f * s) + across * (side * 0.12f * s);
                        ankle.y = thigh.position.y - _rig.ShinLength * 0.97f;
                        IK.Solve(thigh, shin, _rig.ThighLength, _rig.ShinLength, ankle, fwd + Vector3.up * 0.3f + across * (side * 0.2f), _seat, true);
                        foot.rotation = Quaternion.Slerp(foot.rotation, transform.rotation * Quaternion.Euler(55f, 0f, 0f), _seat);
                    }
                }
                if (Motion.HighSeat)
                {
                    // A bar stool, as a person sits on one: thighs forward about level, knees bent, the feet on its brass
                    // ring (the goofy lifeguard's short legs) or on the rail further down (the tall girls': on the ring
                    // their knees came up to their chins), whichever is nearer where the foot hangs with the thigh level.
                    Vector3 fwd = transform.forward, across = transform.right;
                    Vector3 seatTop = transform.position + Vector3.up * 0.5f; // the stool's seat point (Vehicle.DriverFeetPosition)
                    for (int leg = 0; leg < 2; leg++)
                    {
                        float side = leg == 0 ? -1f : 1f;
                        Transform thigh = B(leg == 0 ? Bone.ThighL : Bone.ThighR), shin = B(leg == 0 ? Bone.ShinL : Bone.ShinR);
                        Transform foot = B(leg == 0 ? Bone.FootL : Bone.FootR);
                        float hangs = thigh.position.y - _rig.ShinLength - _rig.AnkleHeight;
                        float ring = seatTop.y - StoolRingDrop + StoolRingTube, rail = seatTop.y - StoolRailDrop + StoolRailTube;
                        bool onRail = Mathf.Abs(hangs - rail) < Mathf.Abs(hangs - ring);
                        float spread = 0.13f * s; // the feet apart, either side of the middle (the goofy lifeguard's big ones touched)
                        float ahead = onRail ? StoolRailFront : Mathf.Sqrt(StoolRingRadius * StoolRingRadius - spread * spread);
                        // The heel hooked on the bar, the ankle in front of it by the heel's length (with the ankle over
                        // the bar, the goofy lifeguard's long cartoon heels went up into the cushion).
                        Vector3 rest = seatTop + fwd * (ahead + HeelBack() - 0.02f * s) + across * (side * spread);
                        rest.y = (onRail ? rail : ring) + _rig.AnkleHeight;
                        IK.Solve(thigh, shin, _rig.ThighLength, _rig.ShinLength, rest, fwd + Vector3.up * 0.3f + across * (side * 0.25f), _seat, true);
                        // The foot level along the bar, the toes a little down.
                        foot.rotation = Quaternion.Slerp(foot.rotation, transform.rotation * Quaternion.Euler(12f, 0f, 0f), _seat);
                    }
                    // Hands resting on the knees (just behind them, on top of the legs as measured on the body), or the
                    // free one there while the other holds a drink.
                    for (int arm = 0; arm < 2; arm++)
                    {
                        bool right = arm == 1;
                        if (Motion.Holding && (right || Motion.TwoHanded || Motion.CarryingPerson)) continue;
                        float side = right ? 1f : -1f;
                        Transform thigh = B(right ? Bone.ThighR : Bone.ThighL), shin = B(right ? Bone.ShinR : Bone.ShinL);
                        Vector3 knee = Vector3.Lerp(thigh.position, shin.position, 0.72f);
                        Vector3 along = (shin.position - thigh.position).normalized;
                        PlaceHand(right, knee + Vector3.up * KneeTop(), along - Vector3.up * 0.3f, Vector3.down,
                            across * (side * 0.8f) - Vector3.up * 0.5f - fwd * 0.2f, _seat, HandPose.Lerp(HandPose.Flat, HandPose.Relaxed, 0.4f));
                    }
                }
                else if (Motion.ChairSeat && !Motion.Holding)
                {
                    // Nothing in the hands: palms down on the seat either side of the lap, just outside the widest of the
                    // legs and shorts (measured on the body). "On the thighs" by fixed joint angles put the hands through
                    // the goofy lifeguard's legs: they came out under his shorts.
                    Vector3 fwd = transform.forward, across = transform.right;
                    // (The width measured on the mesh caught the arms too; the players' shorts are this wide.)
                    float lap = 0.27f * s;
                    Vector3 seatTop = transform.position + Vector3.up * 0.502f + fwd * (0.06f * s); // the cushion (the seat point: 0.5 over the root)
                    PlaceHand(false, seatTop - across * (lap + 0.07f * s), fwd, Vector3.down, -across - fwd * 0.4f, _seat, HandPose.Flat);
                    PlaceHand(true, seatTop + across * (lap + 0.07f * s), fwd, Vector3.down, across - fwd * 0.4f, _seat, HandPose.Flat);
                }
            }

            PoseTableBite(upperR, foreR);

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

            if (_down > 0.01f) PoseDown(hips, upperL, foreL, upperR, foreR, s);

            BeachPoses(hips, upperL, foreL, upperR, foreR, s);
            AimedGestures(upperL, foreL, upperR, foreR);
            PoseReactions(hips, upperL, upperR, s);
            KeepLegsAboveGround(hips, s);
        }

        /// <summary>One shape of the knocked-down body: hips (local position and turn about X), spine curl, legs.</summary>
        private struct DownKey
        {
            public Vector3 Hips;
            public float HipTurn, Curl, Thigh, Shin, Foot, Spread;

            public DownKey(Vector3 hips, float hipTurn, float curl, float thigh, float shin, float foot, float spread)
            {
                Hips = hips; HipTurn = hipTurn; Curl = curl; Thigh = thigh; Shin = shin; Foot = foot; Spread = spread;
            }

            public static DownKey Lerp(DownKey a, DownKey b, float t) => new(Vector3.Lerp(a.Hips, b.Hips, t), Mathf.Lerp(a.HipTurn, b.HipTurn, t),
                Mathf.Lerp(a.Curl, b.Curl, t), Mathf.Lerp(a.Thigh, b.Thigh, t), Mathf.Lerp(a.Shin, b.Shin, t), Mathf.Lerp(a.Foot, b.Foot, t),
                Mathf.Lerp(a.Spread, b.Spread, t));
        }

        /// <summary>
        /// Knocked out flat on the back (arms and legs out like a starfish), and the way there and back. It used to be
        /// one blend of the whole body, so it tipped over stiff as a plank about the heels and rose the same way. Now
        /// going down the knees give first, the hips drop and sit back, then the back goes over and lands; getting up
        /// it sits up with the feet drawn in, rolls forward into a crouch over the feet and stands from there.
        /// </summary>
        private void PoseDown(Transform hips, Transform upperL, Transform foreL, Transform upperR, Transform foreR, float s)
        {
            Vector3 rest = _rig.RestPosition(Bone.Hips);
            var lying = new DownKey(new Vector3(0f, 0.13f * s, -0.45f * s), -90f, 0f, 0f, 8f, -50f, 1f);
            var crouch = new DownKey(new Vector3(0f, rest.y - 0.42f * s, -0.04f * s), 12f, 6f, -100f, 125f, -5f, 0.2f);
            DownKey key;
            float lie; // 0..1 how much the arms, neck and head are in the lying pose
            if (Motion.Pose == AvatarPose.Down)
            {
                float f = Mathf.Clamp01((Now - _downSince) / FallTime);
                var standing = new DownKey(hips.localPosition, 0f, 0f, 0f, 8f, 0f, 0f);
                var squat = new DownKey(new Vector3(0f, rest.y - 0.36f * s, -0.08f * s), 10f, 14f, -80f, 110f, 10f, 0.3f);
                var sat = new DownKey(new Vector3(0f, 0.16f * s, -0.28f * s), -25f, 20f, -70f, 100f, -10f, 0.4f);
                // Knees give, the bottom lands on the sand, and only then the back goes over (the hips turning while
                // still up in the air swung the legs up with them).
                key = f < 0.3f ? DownKey.Lerp(standing, squat, Mathf.SmoothStep(0f, 1f, f / 0.3f))
                    : f < 0.58f ? DownKey.Lerp(squat, sat, Mathf.SmoothStep(0f, 1f, (f - 0.3f) / 0.28f))
                    : DownKey.Lerp(sat, lying, Mathf.SmoothStep(0f, 1f, (f - 0.58f) / 0.42f));
                lie = Mathf.SmoothStep(0f, 1f, (f - 0.58f) / 0.42f);
            }
            else
            {
                float u = Mathf.Clamp01((Now - _upSince) / GetUpTime);
                var sitUp = new DownKey(new Vector3(0f, 0.17f * s, -0.3f * s), -28f, 16f, -70f, 120f, -10f, 0.5f);
                key = u < 0.4f ? DownKey.Lerp(lying, sitUp, Mathf.SmoothStep(0f, 1f, u / 0.4f))
                    : DownKey.Lerp(sitUp, crouch, Mathf.SmoothStep(0f, 1f, (u - 0.4f) / 0.3f));
                lie = 1f - Mathf.SmoothStep(0f, 1f, u / 0.35f);
            }

            bool gettingUp = Motion.Pose != AvatarPose.Down;
            float w = _down;
            hips.localPosition = Vector3.Lerp(hips.localPosition, key.Hips, w);
            hips.localRotation = Quaternion.Slerp(hips.localRotation, Quaternion.Euler(key.HipTurn, 0f, 0f), w);
            B(Bone.Spine).localRotation = Quaternion.Slerp(B(Bone.Spine).localRotation, Quaternion.Euler(key.Curl, 0f, 0f), w);
            B(Bone.Chest).localRotation = Quaternion.Slerp(B(Bone.Chest).localRotation, Quaternion.Euler(key.Curl * 0.6f, 0f, 0f), w);
            float tuck = gettingUp ? -6f : 12f; // chin tucked going over; looking ahead getting up
            B(Bone.Neck).localRotation = Quaternion.Slerp(B(Bone.Neck).localRotation, Quaternion.Euler(Mathf.Lerp(tuck, -10f, lie), 0f, 0f), w);
            B(Bone.Head).localRotation = Quaternion.Slerp(B(Bone.Head).localRotation, Quaternion.Euler(Mathf.Lerp(tuck * 0.7f, -10f, lie), 25f * lie, 0f), w);
            // Arms: out like a starfish lying; on the way down and up, reaching back and down for the ground.
            // (Getting up the hands push off the knees in front instead.)
            Quaternion reachL = gettingUp ? Quaternion.Euler(-38f, 0f, -10f) : Quaternion.Euler(30f, 0f, -25f);
            Quaternion reachR = gettingUp ? Quaternion.Euler(-38f, 0f, 10f) : Quaternion.Euler(30f, 0f, 25f);
            float bend = gettingUp ? -45f : -10f;
            upperL.localRotation = Quaternion.Slerp(upperL.localRotation, Quaternion.Slerp(reachL, Quaternion.Euler(0f, 0f, -80f), lie), w);
            foreL.localRotation = Quaternion.Slerp(foreL.localRotation, Quaternion.Euler(Mathf.Lerp(bend, -20f, lie), 0f, 0f), w);
            upperR.localRotation = Quaternion.Slerp(upperR.localRotation, Quaternion.Slerp(reachR, Quaternion.Euler(0f, 0f, 95f), lie), w);
            foreR.localRotation = Quaternion.Slerp(foreR.localRotation, Quaternion.Euler(Mathf.Lerp(bend, -35f, lie), 0f, 0f), w);
            for (int leg = 0; leg < 2; leg++)
            {
                bool left = leg == 0;
                float side = left ? -1f : 1f;
                Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                thigh.localRotation = Quaternion.Slerp(thigh.localRotation, Quaternion.Euler(key.Thigh, 0f, 14f * side * key.Spread), w);
                shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(key.Shin, 0f, 0f), w);
                foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(key.Foot, 0f, 0f), w);
            }
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
                float w = (t < 0.07f ? t / 0.07f : Mathf.Exp(-(t - 0.07f) * 7f) * Mathf.Cos((t - 0.07f) * 7f)) * _flinchPower;
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

            if (_down > 0.01f && Motion.Pose == AvatarPose.Down)
            {
                float since = Now - _downSince;
                // Going over: the arms fly up as the back goes over...
                if (since > 0.15f && since < FallTime)
                {
                    float fling = Mathf.Sin((since - 0.15f) / (FallTime - 0.15f) * Mathf.PI);
                    Turn(upperL, transform.right, -110f * fling);
                    Turn(upperR, transform.right, -110f * fling);
                }
                // ...it lands with a bounce (and the head knocks back on the sand)...
                const float land = FallTime - 0.05f;
                if (since > land && since < land + 0.3f)
                {
                    float bounce = Mathf.Sin((since - land) / 0.3f * Mathf.PI);
                    hips.position += Vector3.up * (bounce * 0.06f * s * _down);
                    Turn(head, transform.right, -14f * bounce);
                }
                // ...and lies there seeing stars, the head rolling slowly side to side.
                if (since > FallTime + 0.2f)
                {
                    float dizzy = Mathf.Clamp01((since - FallTime - 0.2f) * 2f) * _down;
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
                    bool kneeUp = !left && _rig.Look.Feminine; // (the round dads' raised thigh went into their belly)
                    thigh.localRotation = Quaternion.Slerp(thigh.localRotation, kneeUp ? Quaternion.Euler(-45f, 0f, 6f * side) : Quaternion.Euler(0f, 0f, 8f * side), w);
                    shin.localRotation = Quaternion.Slerp(shin.localRotation, Quaternion.Euler(kneeUp ? 85f : 4f, 0f, 0f), w);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(kneeUp ? -40f : -45f, 0f, 0f), w);
                }
                // Arms lying on the towel along the body, nearly straight, a little out from it, palms down. (Reaching
                // for a point beside the hips bent the goofy lifeguard's arms double, elbows up, his torso being so
                // short; the hands followed the forearm's twist and lay on their backs. The user, 2026-10-12: "very
                // weird".) Each arm goes 20 degrees out from the body's line, so the hand clears a big belly or hips.
                Vector3 toFeet = Vector3.ProjectOnPlane(hips.position - B(Bone.Head).position, Vector3.up);
                toFeet = toFeet.sqrMagnitude > 1e-4f ? toFeet.normalized : -transform.forward;
                Vector3 across = Vector3.Cross(-toFeet, Vector3.up); // the body's right, lying on its back (face up)
                float arm = (_rig.UpperArmLength + _rig.ForearmLength + _rig.HandLength * 0.5f) * 0.97f;
                float ground = transform.position.y + 0.025f * s;
                for (int i = 0; i < 2; i++)
                {
                    bool right = i == 1;
                    float side = right ? 1f : -1f;
                    Vector3 shoulder = (right ? upperR : upperL).position;
                    Vector3 along = (toFeet * Mathf.Cos(20f * Mathf.Deg2Rad) + across * (side * Mathf.Sin(20f * Mathf.Deg2Rad))).normalized;
                    Vector3 palm = shoulder + along * arm;
                    palm.y = ground;
                    PlaceHand(right, palm, along, Vector3.down, across * side - Vector3.up * 0.5f, w, HandPose.Lerp(HandPose.Flat, HandPose.Relaxed, 0.5f));
                }
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
                // Propped up on the elbows (reading on the towel): the chest arches up just far enough for the shoulders to
                // stand an upper arm over the sand, so the elbows rest on it right under them. (A fixed arch left the
                // chest hanging in the air with the arms hovering in front of it on some bodies.) Measured flat first.
                Transform spine = B(Bone.Spine), chest = B(Bone.Chest);
                Quaternion spineWas = spine.localRotation, chestWas = chest.localRotation;
                Quaternion hipsWas = hips.localRotation;
                hips.localRotation = Quaternion.Euler(90f, 0f, 0f);
                spine.localRotation = chest.localRotation = Quaternion.identity;
                float sand = transform.position.y;
                Vector3 shoulders = (upperL.position + upperR.position) * 0.5f;
                float torso = Vector3.Distance(shoulders, spine.position);
                float lift = sand + 0.04f * s + _rig.UpperArmLength * 0.95f - shoulders.y;
                float arch = torso > 0.01f ? Mathf.Asin(Mathf.Clamp(lift / torso, 0f, 0.9f)) * Mathf.Rad2Deg : 20f;
                hips.localRotation = hipsWas;
                spine.localRotation = Quaternion.Slerp(spineWas, Quaternion.Euler(-arch * 0.45f, 0f, 0f), w);
                chest.localRotation = Quaternion.Slerp(chestWas, Quaternion.Euler(-arch * 0.55f, 0f, 0f), w);
                // The face looks ahead over the hands, a little down.
                float look = Mathf.Max(0f, 80f - arch);
                B(Bone.Neck).localRotation = Quaternion.Slerp(B(Bone.Neck).localRotation, Quaternion.Euler(-look * 0.6f, 0f, 0f), w);
                B(Bone.Head).localRotation = Quaternion.Slerp(B(Bone.Head).localRotation, Quaternion.Euler(-look * 0.4f, 0f, 0f), w);
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
                {
                    // Elbows on the sand under the shoulders (a little out), forearms flat on it pointing ahead and in, the
                    // hands palms down in front of the chest, near each other.
                    Vector3 toHead = Vector3.ProjectOnPlane(B(Bone.Head).position - hips.position, Vector3.up);
                    toHead = toHead.sqrMagnitude > 1e-4f ? toHead.normalized : -transform.forward;
                    Vector3 across = Vector3.Cross(Vector3.up, toHead); // the body's right, lying face down
                    float sandTop = transform.position.y + 0.025f * s;
                    for (int i = 0; i < 2; i++)
                    {
                        bool right = i == 1;
                        float side = right ? 1f : -1f;
                        Vector3 elbow = (right ? upperR : upperL).position + toHead * (0.04f * s) + across * (side * 0.03f * s);
                        elbow.y = sandTop;
                        Vector3 along = (toHead * 0.85f - across * (side * 0.4f)).normalized;
                        Vector3 palm = elbow + along * (_rig.ForearmLength + _rig.HandLength * 0.55f);
                        PlaceHand(right, palm, along, Vector3.down, across * side - Vector3.up - toHead * 0.4f, w, HandPose.Lerp(HandPose.Flat, HandPose.Relaxed, 0.5f));
                    }
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
                // Sitting up on the towel, leaning back on the hands; one leg out, the other knee up. All by IK to points
                // on the sand (fixed joint angles crossed the Tripo girls' legs into each other).
                Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 across = Vector3.Cross(Vector3.up, fwd);
                float ground = transform.position.y + 0.04f * s;
                float half = 0.5f * Vector3.Distance(upperL.position, upperR.position);
                float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
                Vector3 handL = hips.position - fwd * (0.22f * s) - across * (half + 0.12f * s);
                Vector3 handR = hips.position - fwd * (0.22f * s) + across * (half + 0.12f * s);
                handL.y = handR.y = ground;
                IK.Solve(upperL, foreL, la, lb, handL, -across - fwd * 0.6f, w, false);
                IK.Solve(upperR, foreR, la, lb, handR, across - fwd * 0.6f, w, false);
                float legLength = _rig.ThighLength + _rig.ShinLength;
                for (int leg = 0; leg < 2; leg++)
                {
                    bool left = leg == 0;
                    float side = left ? -1f : 1f;
                    Transform thigh = B(left ? Bone.ThighL : Bone.ThighR), shin = B(left ? Bone.ShinL : Bone.ShinR), foot = B(left ? Bone.FootL : Bone.FootR);
                    // Left leg out along the towel, right knee up with the foot flat on it, the feet a hip apart.
                    float reach = left ? 0.92f : 0.6f;
                    Vector3 heel = thigh.position + fwd * (legLength * reach) + across * (side * 0.06f * s);
                    heel.y = ground + 0.04f * s;
                    IK.Solve(thigh, shin, _rig.ThighLength, _rig.ShinLength, heel, Vector3.up + fwd * 0.5f + across * (side * 0.2f), w, true);
                    foot.localRotation = Quaternion.Slerp(foot.localRotation, Quaternion.Euler(-20f, 0f, 0f), w);
                }
                _rig.LeftHand?.Pose(HandPose.Flat);
                _rig.RightHand?.Pose(HandPose.Flat);
            }
        }

        /// <summary>
        /// A bite off the dish on the table (seated at the bar, Fun.TableMeal): the right hand reaches to it, takes a
        /// piece, brings it up to the mouth, holds there a moment (chewing), and goes back down.
        /// </summary>
        private void PoseTableBite(Transform upperR, Transform foreR)
        {
            const float seconds = 1.3f;
            if (!GestureActive(AvatarGesture.TableBite, seconds) || _gesturePoint == Vector3.zero) return;
            float t = GestureT(seconds) * seconds;
            // Leaning in over the counter for it (back and chest, the shoulder reaching on), sitting back up to eat: on
            // a bar stool the dish is about an arm off, more than that for the goofy body's short arms.
            Vector3 dish = _gesturePoint;
            float lean = t < 0.35f ? Mathf.SmoothStep(0f, 1f, t / 0.3f) : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.35f);
            if (lean > 0f)
            {
                if (B(Bone.Spine) is { } spine) spine.rotation = Quaternion.AngleAxis(26f * lean, transform.right) * spine.rotation;
                if (B(Bone.Chest) is { } chest) chest.rotation = Quaternion.AngleAxis(14f * lean, transform.right) * chest.rotation;
                // (The head stays up, looking at the dish: bent down with the back, the big goofy head dived into the counter.)
                if (B(Bone.Neck) is { } neck) neck.rotation = Quaternion.AngleAxis(-26f * lean, transform.right) * neck.rotation;
                Vector3 toDish = Vector3.ProjectOnPlane(dish - upperR.position, Vector3.up);
                if (toDish.sqrMagnitude > 1e-4f) upperR.position += toDish.normalized * (0.045f * _rig.Scale * lean);
            }
            Vector3 mouth = Lips + transform.forward * (0.07f * _rig.Scale) - Vector3.up * (0.03f * _rig.Scale);
            Vector3 hand = t < 0.35f ? dish : t < 0.7f ? Vector3.Lerp(dish, mouth, Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.35f)) : mouth;
            float w = Mathf.SmoothStep(0f, 1f, t / 0.3f) * (1f - Mathf.SmoothStep(0f, 1f, (t - 1.0f) / 0.3f));
            float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
            IK.Solve(upperR, foreR, la, lb, hand, transform.right - Vector3.up * 0.6f, w, false);
            _rig.RightHand?.Pose(HandPose.Lerp(HandPose.Relaxed, HandPose.Cup, w));
            if (t > 0.7f && t < 1.0f) Motion.Talking = true; // chewing
        }

        private void AimedGestures(Transform upperL, Transform foreL, Transform upperR, Transform foreR)
        {
            _striking = 0;
            Kissing = 0f;
            float la = _rig.UpperArmLength, lb = _rig.ForearmLength + _rig.HandLength * 0.5f;
            Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;
            bool squared = Motion.Stance != FightStance.None && Motion.Pose == AvatarPose.Normal && !Motion.Swimming;
            _stanceW = Mathf.MoveTowards(_stanceW, squared ? 1f : 0f, Time.deltaTime * (squared ? 5f : 3f));
            if (ActiveStrike(out StrikeKind strikeKind, out bool strikeLeft, out float strikeTime))
                Strike(strikeKind, strikeLeft, strikeTime, upperL, foreL, upperR, foreR, la, lb);
            else if (_stanceW > 0.001f) PoseFightStance(upperL, foreL, upperR, foreR, la, lb);
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

        private enum StrikeKind { Straight, Hook, Uppercut, Overhand }

        private float CarryLean => Motion.CarryingPerson ? _hold : 0f;
        private int _striking; // the arm throwing a punch this frame: 1 right, -1 left, 0 none
        private float _stanceW; // 0..1 how far into the fight stance (fists up) the arms are
        private float _drawn;   // 0..1 how far the right fist was drawn back when the blow was thrown (the punch starts there)

        /// <summary>A punch being thrown now: which kind, which hand, how long it takes.</summary>
        private bool ActiveStrike(out StrikeKind kind, out bool left, out float duration)
        {
            (kind, left) = _gesture switch
            {
                AvatarGesture.Punch => (StrikeKind.Straight, false),
                AvatarGesture.PunchLeft => (StrikeKind.Straight, true),
                AvatarGesture.Hook => (StrikeKind.Hook, false),
                AvatarGesture.HookLeft => (StrikeKind.Hook, true),
                AvatarGesture.Uppercut => (StrikeKind.Uppercut, false),
                AvatarGesture.UppercutLeft => (StrikeKind.Uppercut, true),
                AvatarGesture.Overhand => (StrikeKind.Overhand, false),
                AvatarGesture.OverhandLeft => (StrikeKind.Overhand, true),
                _ => (StrikeKind.Straight, false),
            };
            bool punching = _gesture is AvatarGesture.Punch or AvatarGesture.PunchLeft or AvatarGesture.Hook or AvatarGesture.HookLeft
                or AvatarGesture.Uppercut or AvatarGesture.UppercutLeft or AvatarGesture.Overhand or AvatarGesture.OverhandLeft;
            duration = kind switch { StrikeKind.Hook => 0.44f, StrikeKind.Uppercut => 0.46f, StrikeKind.Overhand => 0.52f, _ => 0.36f };
            return punching && Now - _gestureStart < duration;
        }

        /// <summary>
        /// A punch as others see it (the first-person arms have their own): the fist cocks back to its starting place,
        /// whips along the kind's path into the target (the point hit, or chest height ahead) while the chest and hips
        /// turn into it, stays there a moment and comes back to the guard; the other hand guards the chin.
        /// Straight: from the chin, straight out. Hook: elbow up and out, swinging round from the side. Uppercut: dips,
        /// fist down by the hip, rising up into the chin. Overhand: cocked high behind the ear, looping over and down,
        /// leaning and stepping in. (It used to be one right jab for all of them.)
        /// </summary>
        private void Strike(StrikeKind kind, bool left, float duration, Transform upperL, Transform foreL, Transform upperR, Transform foreR, float la, float lb)
        {
            float t = GestureT(duration), s = _rig.Scale;
            Vector3 fwd = transform.forward, up = Vector3.up, side = left ? -transform.right : transform.right;
            Transform upper = left ? upperL : upperR, fore = left ? foreL : foreR;
            Transform guardUpper = left ? upperR : upperL, guardFore = left ? foreR : foreL;
            Transform hips = B(Bone.Hips), chest = B(Bone.Chest), head = B(Bone.Head);

            // Phases: cock back until a, drive in until b, stay until b + 0.1, come back to the guard by the end.
            (float a, float b, float twist, float lean) = kind switch
            {
                StrikeKind.Hook => (0.3f, 0.56f, 36f, 4f),
                StrikeKind.Uppercut => (0.32f, 0.58f, 18f, -4f),
                StrikeKind.Overhand => (0.34f, 0.62f, 28f, 14f),
                _ => (0.22f, 0.46f, 22f, 7f),
            };
            float cock = Mathf.SmoothStep(0f, 1f, t / a);
            float drive = Mathf.Clamp01((t - a) / (b - a));
            drive = 1f - (1f - drive) * (1f - drive) * (1f - drive); // fast out of the gate, slowing into the target
            float back = Mathf.SmoothStep(0f, 1f, (t - b - 0.1f) / Mathf.Max(0.05f, 0.9f - b));
            float w = t < 0.07f ? t / 0.07f : t > 0.88f ? (1f - t) / 0.12f : 1f;
            float into = drive * (1f - back); // 0..1 how far into the blow the body is

            // The body first (the shoulder moves with it): wind the other way, then turn into the punch.
            float turn = (left ? 1f : -1f) * (twist * into - twist * 0.45f * cock * (1f - drive));
            Turn(hips, up, turn * 0.4f * w);
            Turn(chest, up, turn * 0.6f * w);
            Turn(B(Bone.Spine), transform.right, lean * into * w);
            Vector3 shift = Vector3.zero;
            if (kind == StrikeKind.Uppercut) shift += up * ((-0.06f * cock * (1f - drive) + 0.03f * into) * s); // dip, then drive up
            if (kind == StrikeKind.Overhand) shift += fwd * (0.07f * into * s);                                  // a step in
            if (kind == StrikeKind.Straight) shift += fwd * (0.03f * into * s);
            if (shift.sqrMagnitude > 1e-8f)
            {
                hips.position += shift * w;
                PoseLegs(); // (the feet stay planted)
            }

            Vector3 shoulder = upper.position;
            Vector3 chin = GuardChin();
            Vector3 guard = chin + side * (0.1f * s);
            // A fighter squared up throws the blow from where the wind-up drew the fist back to (and stays in the guard).
            if (!left && _drawn > 0f) guard = Vector3.Lerp(guard, DrawnFist(chin), _drawn * (1f - drive));
            if (_stanceW > 0f) w = Mathf.Max(w, _stanceW);
            float reach = (la + lb) * 0.98f;
            Vector3 target = _gesturePoint != Vector3.zero ? _gesturePoint : shoulder + fwd * (0.7f * s);
            if (kind == StrikeKind.Uppercut) target += up * (0.06f * s); // up under the chin
            if ((target - shoulder).magnitude > reach) target = shoulder + (target - shoulder).normalized * reach;

            (Vector3 cocked, Vector3 via, Vector3 elbow) = kind switch
            {
                StrikeKind.Hook => (shoulder + side * (0.3f * s) + fwd * (0.06f * s), shoulder + side * (0.34f * s) + fwd * (0.42f * s), side + up * 0.7f),
                StrikeKind.Uppercut => (shoulder - up * (0.4f * s) + fwd * (0.12f * s) + side * (0.04f * s), target - up * (0.32f * s), -up - fwd * 0.3f),
                StrikeKind.Overhand => (shoulder + up * (0.22f * s) - fwd * (0.16f * s) + side * (0.14f * s), shoulder + up * (0.34f * s) + fwd * (0.32f * s), up + side),
                _ => (guard + fwd * (0.02f * s), Vector3.Lerp(guard, target, 0.5f), -up + side * 0.8f),
            };
            Vector3 fist = t < a ? Vector3.Lerp(guard, cocked, cock)
                : back <= 0f ? Bezier(cocked, via, target, drive)
                : Vector3.Lerp(target, guard, back);
            IK.Solve(upper, fore, la, lb, fist, elbow, w, false);
            IK.Solve(guardUpper, guardFore, la, lb, chin - side * (0.1f * s), -up - side, w * 0.9f, false);
            _striking = left ? -1 : 1;
            (left ? _rig.LeftHand : _rig.RightHand)?.Pose(HandPose.Fist);
            (left ? _rig.RightHand : _rig.LeftHand)?.Pose(HandPose.Lerp(HandPose.LooseFist, HandPose.Fist, 0.8f)); // the guard: a fist too
            Turn(head, transform.right, 6f * w); // chin tucked
        }

        /// <summary>
        /// Where a fighter's guard sits: in front of the chin, clear of the big cartoon heads (a chin guard sat inside the
        /// head, and KeepHandsOutOfHead swung that arm out, so the guard hand ended up stuck out flat). (From the lips, not
        /// the head bone: the goofy lifeguard's head bone is down at the neck, so "under the head" put it at the chest.)
        /// </summary>
        private Vector3 GuardChin()
        {
            float s = _rig.Scale;
            Vector3 fwd = transform.forward, up = Vector3.up;
            Transform mouth = B(Bone.Mouth);
            return mouth != null
                ? mouth.position + fwd * (0.24f * s) - up * (0.06f * s)
                : B(Bone.Head).position + fwd * (Mathf.Max(_rig.HeadFront, _rig.HeadHalfWidth) + 0.16f * s) - up * (0.16f * s);
        }

        /// <summary>The right fist drawn all the way back for a blow: beside the cheek, outside the head.</summary>
        private Vector3 DrawnFist(Vector3 chin)
        {
            float s = _rig.Scale;
            float clear = Mathf.Max(0.2f * s, _rig.HeadHalfWidth + 0.09f * s);
            return chin + transform.right * clear - transform.forward * (0.2f * s) + Vector3.up * (0.03f * s);
        }

        /// <summary>
        /// Squared up for a fist fight (<see cref="AvatarMotion.Stance"/>): both fists up in front of the chin, elbows
        /// down, chin tucked. Winding up a blow the right fist draws back beside the cheek and the chest turns away from
        /// the target (<see cref="AvatarMotion.WindUp"/>): the tell. <see cref="Strike"/> then throws it from there.
        /// </summary>
        private void PoseFightStance(Transform upperL, Transform foreL, Transform upperR, Transform foreR, float la, float lb)
        {
            float s = _rig.Scale, w = Mathf.SmoothStep(0f, 1f, _stanceW);
            Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;
            float draw = Motion.Stance == FightStance.WindUp ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Motion.WindUp)) : 0f;
            _drawn = draw;
            // Turning away to load it (as far as the punch's own cock goes, so the throw carries straight on).
            Turn(B(Bone.Hips), up, 4f * draw * w);
            Turn(B(Bone.Chest), up, 6f * draw * w);
            Vector3 chin = GuardChin();
            Vector3 rightFist = Vector3.Lerp(chin + right * (0.1f * s), DrawnFist(chin), draw);
            IK.Solve(upperR, foreR, la, lb, rightFist, -up + right * (0.8f + 0.6f * draw), w, false);
            IK.Solve(upperL, foreL, la, lb, chin - right * (0.1f * s) + fwd * (0.03f * s), -up - right * 0.8f, w * 0.95f, false);
            _rig.RightHand?.Pose(HandPose.Lerp(HandPose.Relaxed, HandPose.Fist, w));
            _rig.LeftHand?.Pose(HandPose.Lerp(HandPose.Relaxed, HandPose.Lerp(HandPose.LooseFist, HandPose.Fist, 0.8f), w));
            Turn(B(Bone.Head), right, 6f * w); // chin tucked
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) =>
            Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

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
            Transform[] chain = { B(Bone.Spine), B(Bone.Chest), B(Bone.Neck) };
            float[] share = { 0.55f, 0.65f, 0.5f };
            for (int pass = 0; pass < 6; pass++)
                for (int i = 0; i < chain.Length; i++)
                {
                    Transform bone = chain[i];
                    Vector3 toMouth = MouthPoint - bone.position, toLips = target - bone.position;
                    if (toMouth.sqrMagnitude < 1e-6f || toLips.sqrMagnitude < 1e-6f) continue;
                    Quaternion bend = Quaternion.FromToRotation(toMouth, toLips);
                    bone.rotation = Quaternion.Slerp(Quaternion.identity, bend, share[i] * w) * bone.rotation;
                }
            // 2. Face down onto theirs, tilted sideways a little, eyes shut (the face code does the eyes).
            Quaternion faceDown = Quaternion.LookRotation(Vector3.down + along * 0.35f, along) * Quaternion.Euler(0f, 0f, 38f);
            head.rotation = Quaternion.Slerp(head.rotation, faceDown, w * 0.85f);
            // 3. Whatever is still missing: lean the hips in (up to 40 cm), knees stay; then curl the neck the last bit.
            Vector3 gap = target - MouthPoint;
            if (gap.sqrMagnitude > 1e-6f)
            {
                hips.position += Vector3.ClampMagnitude(gap, 0.4f * _rig.Scale) * w; // (0.3 left a big-bellied man 5 cm short)
                PoseLegs();
            }
            for (int pass = 0; pass < 6; pass++)
                foreach (Transform bone in new[] { B(Bone.Chest), B(Bone.Neck) })
                {
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
            // Shouldering a gun: facing the target again over the turned chest, cheek down on the stock.
            float aiming = _shoulder * standing;
            yaw -= BladeTwist * aiming / 0.75f;
            pitch = pitch * (1f - 0.4f * aiming) + 3f * aiming; // (bowed all the way, the big head lands on the gun)
            tilt -= 12f * aiming;
            // Carrying somebody: chin up, looking over them, not down into their belly.
            pitch = Mathf.Lerp(pitch, Mathf.Min(pitch, -14f), CarryLean * standing);
            Quaternion neck = Quaternion.Euler(pitch * 0.3f + swimTilt * 0.4f, yaw * 0.25f + turn * 0.4f, tilt * 0.4f);
            // (CPR: chin well up, so the big head stays clear above the hands on the chest.)
            Quaternion head = Quaternion.Euler(pitch * 0.55f + swimTilt * 0.6f - _cpr * 32f, yaw * 0.5f + turn * 0.6f, tilt * 0.6f);
            // On the clips' body the looking goes on top of the clip's own head movement.
            B(Bone.Neck).localRotation = ClipDriven ? B(Bone.Neck).localRotation * neck : neck;
            B(Bone.Head).localRotation = ClipDriven ? B(Bone.Head).localRotation * head : head;
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
