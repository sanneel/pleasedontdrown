using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using UnityEngine;
using UnityEngine.Rendering;

namespace PleaseDontDrown.Player
{
    /// <summary>Boxing punches (UFC style). Straight = jab with the left hand, cross with the right.</summary>
    public enum PunchKind : byte { Straight, Hook, Uppercut, Overhand }

    /// <summary>
    /// The local player's own hands, How to Fish style: just two big, smooth, softly lit hands floating in view
    /// (a stub of wrist, no arms). Empty hands box: jab, cross, hook, uppercut, overhand, with a guard between.
    ///
    /// Idle hands rest at the bottom of the view in a frame that follows where you face with a springy lag and a
    /// fixed downward tilt: they sway when you turn and come up into view when you look down. Picking something
    /// up blends each hand from wherever it is into that item's grip (palm position, finger direction, palm
    /// direction, finger curl); letting go blends back. Hands follow a thrown item out for a moment. Swimming
    /// strokes, CPR presses, reaching for things, waving and climbing have their own poses. Same skin as your character.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FirstPersonArms : MonoBehaviour
    {
        private const float HandSize = 1.35f;    // x life size: big cartoon hands
        private const float MaxReach = 0.85f;    // from the eye
        private const float IdlePitch = 5f;

        private enum State { Idle, Run, Swim, Climb, Item, Cpr, Reach, ThrownItem, FollowThrough, Wave, Punch, Breath, Drive }

        private sealed class Hand
        {
            public bool Right;
            public float Side;
            public Transform Wrist;
            public HandBones Bones;
            public int Key = -1;
            public Vector3 FromPos;        // camera space, at the start of a blend
            public Quaternion FromRot;
            public HandPose FromPose;
            public float T = 1f;
            public float Duration = 0.2f;
            public Vector3 Palm;           // current palm point (world)
            public Quaternion Rot = Quaternion.identity;
            public HandPose Pose = HandPose.Relaxed;
        }

        private PlayerHub _hub;
        private Camera _camera;
        private Transform _root;
        // Two wrists, then the left hand's fingers, then the right hand's.
        private readonly Transform[] _bones = new Transform[2 + 2 * HandBones.BoneCount];
        private readonly Hand _left = new() { Right = false, Side = -1f };
        private readonly Hand _right = new() { Right = true, Side = 1f };
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private AvatarLook _look;
        private bool _built;

        private float _frameYaw;
        private float _frameYawVelocity;
        private float _stride;
        private float _stroke;
        private float _run;
        private float _air;   // 0..1 airborne: hands fly up into view
        private AvatarGesture _gesture;
        private float _gestureStart = -10f;
        private bool _rigidGrip; // holding a gun: hands stay exactly on it (it does its own kick)
        private Vector3 _reach;
        private PunchKind _punchKind;
        private bool _punchLeft;
        private float _punchStart = -10f;
        private Vector3 _punchFrom;     // camera space: where the punching fist was when the punch started
        private float _lastPump = -10f;
        private Vector3 _pumpPoint;

        public void Init(PlayerHub hub, Camera cam)
        {
            _hub = hub;
            _camera = cam;
            _root = new GameObject("FirstPersonArms").transform;
            _root.SetParent(cam.transform, false);
            _frameYaw = cam.transform.eulerAngles.y;
        }

        public void Build(AvatarLook look)
        {
            if (_built && look.Equals(_look)) return;
            _look = look;
            float s = 0.93f + look.Height * 0.05f;

            for (int i = 0; i < 2; i++)
            {
                if (_bones[i] == null) _bones[i] = new GameObject(i == 0 ? "HandL" : "HandR").transform;
                _bones[i].SetParent(_root, false);
                _bones[i].localPosition = new Vector3(i == 0 ? -0.2f : 0.2f, -0.35f, 0.4f);
                _bones[i].localRotation = Quaternion.identity;
            }
            // The modelled hand (Meshy sculpt) when it's there; the code-built one otherwise.
            FirstPersonHandModel model = FirstPersonHandModel.Load();
            SetupHand(_left, 0, s, model);
            SetupHand(_right, 1, s, model);

            var bindposes = new Matrix4x4[_bones.Length];
            _left.Bones.ResetPose();
            _right.Bones.ResetPose();
            Matrix4x4 rootToWorld = _root.localToWorldMatrix;
            for (int i = 0; i < _bones.Length; i++) bindposes[i] = _bones[i].worldToLocalMatrix * rootToWorld;
            if (model != null)
            {
                var vertices = new List<Vector3>();
                var colors = new List<Color32>();
                var weights = new List<BoneWeight>();
                var triangles = new List<int>();
                Color32 skin = look.SkinColor;
                model.AddHand(-1f, s * HandSize, bindposes[0].inverse, 0, 2, skin, vertices, colors, weights, triangles);
                model.AddHand(1f, s * HandSize, bindposes[1].inverse, 1, 2 + HandBones.BoneCount, skin, vertices, colors, weights, triangles);
                if (_mesh == null) _mesh = new Mesh();
                _mesh.Clear();
                _mesh.name = "FirstPersonHands";
                _mesh.indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                _mesh.SetVertices(vertices);
                _mesh.SetColors(colors);
                _mesh.SetTriangles(triangles, 0);
                _mesh.boneWeights = weights.ToArray();
                _mesh.bindposes = bindposes;
                _mesh.RecalculateNormals();
                _mesh.RecalculateBounds();
            }
            else
            {
                var kit = new AvatarMeshKit();
                void Use(int index) => kit.SetBone(index, bindposes[index].inverse);
                // How to Fish's look: chunky low-poly hands with flat, faceted shading (not smooth plastic ones).
                _left.Bones.BuildMesh(kit, look.SkinColor, f => Use(f < 0 ? 0 : 2 + f), lowPoly: true);
                _right.Bones.BuildMesh(kit, look.SkinColor, f => Use(f < 0 ? 1 : 2 + HandBones.BoneCount + f), lowPoly: true);
                _mesh = kit.ToMesh("FirstPersonHands", bindposes, _mesh, flat: true);
            }

            if (_renderer == null)
            {
                _renderer = _root.gameObject.AddComponent<SkinnedMeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.updateWhenOffscreen = true;
                _renderer.skinnedMotionVectors = false;
                _renderer.quality = SkinQuality.Bone2; // the modelled hand blends across the knuckles
            }
            _renderer.sharedMesh = _mesh;
            _renderer.bones = _bones;
            _renderer.rootBone = _root;
            _renderer.sharedMaterial = HandMaterial();
            _built = true;
        }

        private Material _handMaterial;

        /// <summary>
        /// Lit like How to Fish's hands: the sun shades the facets (each face its own tone), the side away from it goes
        /// a warm dark, no rim light and no shadows falling on them.
        /// </summary>
        private Material HandMaterial()
        {
            if (_handMaterial != null) return _handMaterial;
            Material source = AvatarRig.SharedMaterial;
            if (source == null) return null;
            _handMaterial = new Material(source) { name = "FirstPersonHands" };
            _handMaterial.SetFloat("_ShadowAmount", 0f);
            _handMaterial.SetFloat("_Softness", 0.6f);
            _handMaterial.SetFloat("_Rim", 0f);
            _handMaterial.SetFloat("_Ambient", 0.5f);
            _handMaterial.SetColor("_ShadowTint", new Color(0.62f, 0.54f, 0.5f));
            // Curled fingers of the modelled hand fold skin over itself: show the inside rather than a hole.
            _handMaterial.SetFloat("_Cull", (float)CullMode.Off);
            return _handMaterial;
        }

        private void SetupHand(Hand hand, int wrist, float s, FirstPersonHandModel model)
        {
            hand.Wrist = _bones[wrist];
            int first = 2 + (hand.Right ? HandBones.BoneCount : 0);
            var reuse = new Transform[HandBones.BoneCount];
            System.Array.Copy(_bones, first, reuse, 0, HandBones.BoneCount);
            hand.Bones = new HandBones(hand.Wrist, hand.Side, s * HandSize, reuse, model?.Shape);
            System.Array.Copy(hand.Bones.Bones, 0, _bones, first, HandBones.BoneCount);
            hand.Palm = hand.Wrist.position;
            hand.Rot = _root.rotation;
        }

        /// <summary>Editor review renders: put a hand on a grip right now (or out of the way with none).</summary>
        public void PlaceForReview(bool right, HandGrip? grip)
        {
            Hand hand = right ? _right : _left;
            if (grip is not { } g)
            {
                hand.Wrist.position += Vector3.down * 50f;
                return;
            }
            Quaternion rot = g.Rotation(hand.Side);
            hand.Wrist.SetPositionAndRotation(g.Point - rot * hand.Bones.PalmContact, rot);
            hand.Bones.Pose(g.Pose);
        }

        public void Play(AvatarGesture gesture, Vector3 point = default)
        {
            if (gesture is AvatarGesture.ChargeStart or AvatarGesture.ChargeEnd or AvatarGesture.EatStart or AvatarGesture.EatStop or AvatarGesture.Bite)
                return;
            _gesture = gesture;
            _gestureStart = Time.time;
            _reach = point;
        }

        /// <summary>How long a punch takes: out to the target, then back (How to Fish's 1/10 s + 1/4 s).</summary>
        public static float PunchDuration(PunchKind kind) => Combat.PlayerCombat.StrikeTime + Combat.PlayerCombat.ReturnTime;

        private const float GuardHold = 0f; // no guard stance: the fist goes back to where the hand rests

        private Transform _punchTarget;   // what the fist is going for (it follows it), or none
        private Vector3 _punchLocal;      // the spot on it (its local space)
        private Vector3 _punchImpact;     // camera space: where the fist got to (it comes back from there)

        // The fist's speed along its way out, and back (as in the original).
        private static readonly AnimationCurve PunchOut = new(new Keyframe(0f, 0f, 0.485f, 0.485f), new Keyframe(1f, 1f, 1.559f, 1.559f));
        private static readonly AnimationCurve PunchBack = new(new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.4334f, 0.609f, 1.502f, 1.502f),
            new Keyframe(1f, 1f, -0.0108f, -0.0108f));

        /// <summary>Throw a punch with one fist at a spot on <paramref name="target"/> (null: straight ahead).</summary>
        public void PlayPunch(PunchKind kind, bool leftHand, Transform target = null, Vector3 localPoint = default)
        {
            _punchKind = kind;
            _punchLeft = leftHand;
            _punchStart = Time.time;
            _punchTarget = target;
            _punchLocal = localPoint;
            Hand hand = leftHand ? _left : _right;
            _punchFrom = _camera.transform.InverseTransformPoint(hand.Palm);
            _punchImpact = new Vector3(0f, 0f, 1f);
        }

        /// <summary>Boxing (camera space; <paramref name="side"/> -1 left fist, +1 right).</summary>
        private static Vector3 Guard(float side) => new(side * 0.2f, -0.25f, 0.36f);

        private void PunchPath(PunchKind kind, float side, out Vector3 wind, out Vector3 control, out Vector3 impact)
        {
            switch (kind)
            {
                case PunchKind.Hook:
                    // Swings out wide and comes round across the middle, elbow up.
                    wind = new Vector3(side * 0.42f, -0.15f, 0.26f);
                    control = new Vector3(side * 0.4f, -0.1f, 0.66f);
                    impact = new Vector3(-side * 0.04f, -0.09f, 0.62f);
                    break;
                case PunchKind.Uppercut:
                    // Dips low, then drives straight up the middle.
                    wind = new Vector3(side * 0.14f, -0.48f, 0.3f);
                    control = new Vector3(side * 0.08f, -0.36f, 0.58f);
                    impact = new Vector3(side * 0.03f, -0.02f, 0.56f);
                    break;
                case PunchKind.Overhand:
                    // Cocked high behind, loops over the top and down.
                    wind = new Vector3(side * 0.34f, 0.06f, 0.04f);
                    control = new Vector3(side * 0.26f, 0.14f, 0.52f);
                    impact = new Vector3(side * 0.02f, -0.12f, 0.68f);
                    break;
                default:
                    // Straight down the pipe, turning over at the end.
                    wind = new Vector3(side * 0.16f, -0.21f, 0.22f);
                    impact = new Vector3(side * 0.03f, -0.09f, 0.74f);
                    control = (wind + impact) * 0.5f;
                    break;
            }
        }

        /// <summary>Where a fist's knuckles point and where its palm faces, for each punch at impact.</summary>
        private static void PunchOrientation(PunchKind kind, float side, Transform cam, out Vector3 knuckles, out Vector3 palm)
        {
            Vector3 f = cam.forward, u = cam.up, r = cam.right;
            switch (kind)
            {
                case PunchKind.Hook:
                    knuckles = (f - r * (side * 0.8f)).normalized;
                    palm = -u;
                    break;
                case PunchKind.Uppercut:
                    knuckles = (u + f * 0.35f).normalized;
                    palm = -f;
                    break;
                case PunchKind.Overhand:
                    knuckles = (f - u * 0.45f).normalized;
                    palm = (-u - r * (side * 0.4f)).normalized;
                    break;
                default:
                    knuckles = f;
                    palm = -u;
                    break;
            }
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float m = 1f - t;
            return m * m * a + 2f * m * t * b + t * t * c;
        }

        /// <summary>A fist throwing (or recovering from) the current punch, or up in guard. False when not boxing.</summary>
        private bool BoxingPose(Hand hand, Transform cam, out Vector3 palm, out Quaternion rot, out HandPose pose)
        {
            palm = default;
            rot = default;
            pose = HandPose.Fist;
            float since = Time.time - _punchStart;
            float duration = PunchDuration(_punchKind);
            if (since > duration + GuardHold) return false;
            float side = hand.Side;
            // Only the punching fist moves; the other hand stays as it is (resting).
            if (hand.Right == _punchLeft) return false;

            // How to Fish: out to the spot it's going for (following it if it moves) in 1/10 s, then back to where the
            // hand rests in 1/4 s. The fist turns over on the way out and back again on the way home.
            float strike = Combat.PlayerCombat.StrikeTime, back = Combat.PlayerCombat.ReturnTime;
            Vector3 restKnuckles = (cam.forward * 0.6f + cam.up * 0.8f).normalized;
            Vector3 restPalm = (-cam.forward * 0.6f - cam.right * (side * 0.7f)).normalized;
            PunchOrientation(PunchKind.Straight, side, cam, out Vector3 knuckles, out Vector3 palmDir);
            float percent; // 0 at rest .. 1 at the target
            if (since < strike)
            {
                Vector3 target = _punchTarget != null ? _punchTarget.TransformPoint(_punchLocal) : cam.TransformPoint(new Vector3(side * 0.05f, -0.05f, 1f));
                percent = since / strike;
                Vector3 local = Vector3.Lerp(_punchFrom, cam.InverseTransformPoint(target), PunchOut.Evaluate(percent));
                _punchImpact = local;
                palm = cam.TransformPoint(local);
            }
            else
            {
                percent = Mathf.Clamp01(1f - (since - strike) / back);
                palm = cam.TransformPoint(Vector3.Lerp(_punchFrom, _punchImpact, PunchBack.Evaluate(percent)));
            }
            float turn = 1f - (1f - percent) * (1f - percent);
            rot = Quaternion.Slerp(HandBones.Orient(restKnuckles, restPalm, side), HandBones.Orient(knuckles, palmDir, side), turn);
            return true;
        }

        /// <summary>We pressed a chest (CPR).</summary>
        public void OnPump(Vector3 chest)
        {
            _lastPump = Time.time;
            _pumpPoint = chest;
        }

        private void OnEnable() => Core.DevCommands.Register("armsdebug", "", "First-person arms: hands, states, renderer.", _ =>
        {
            Core.DevCommands.Print($"built {_built}, verts {(_mesh != null ? _mesh.vertexCount : 0)}, material {(_renderer != null && _renderer.sharedMaterial != null ? _renderer.sharedMaterial.name : "none")}");
            foreach (Hand h in new[] { _left, _right })
                Core.DevCommands.Print($"  {(h.Right ? "right" : "left")}: state {(State)(h.Key & 15)}, palm (camera) {_camera.transform.InverseTransformPoint(h.Palm):F2}, curl {h.Pose.Middle:F2}");
        }, owner: this);

        private void OnDisable() => Core.DevCommands.Unregister("armsdebug", this);

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_handMaterial != null) Destroy(_handMaterial);
            if (_root != null) Destroy(_root.gameObject);
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (!_built || _hub == null) return;
            // Looking through a scope: no arms in the picture.
            if (_renderer != null) _renderer.forceRenderingOff = Combat.Weapon.LocalScoped;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            Transform cam = _camera.transform;
            PlayerMotor motor = _hub.Motor;
            PlayerHands hands = _hub.Hands;
            float speed = motor != null ? motor.HorizontalSpeed : 0f;

            // The idle frame: eye height, facing where we face (springy), tilted a fixed amount down.
            _frameYaw = Mathf.SmoothDampAngle(_frameYaw, cam.eulerAngles.y, ref _frameYawVelocity, 0.06f, Mathf.Infinity, dt);
            bool grounded = motor == null || motor.IsGrounded;
            _stride += (grounded ? speed : 0f) * dt / 1.5f;
            _run = Mathf.MoveTowards(_run, motor != null && motor.IsSprinting && grounded && speed > 5f ? 1f : 0f, dt * 4f);
            bool airborne = motor != null && !motor.IsGrounded && !motor.IsSwimming && !motor.IsClimbing && !motor.Noclip;
            _air = Mathf.MoveTowards(_air, airborne ? 1f : 0f, dt * (airborne ? 5f : 7f));

            // What we hold (or just threw).
            HandGrip gripL = default, gripR = default;
            PlayerHands.GripKind kind = PlayerHands.GripKind.None;
            int itemId = 0;
            Item held = hands != null ? hands.HeldItem : null;
            _rigidGrip = held != null && held.RigidInHand;
            if (held != null)
            {
                kind = hands.GetGrip(out gripL, out gripR);
                itemId = held.GetInstanceID();
            }
            bool followThrown = false;
            if (held == null && hands != null && hands.LastThrown != null)
            {
                float since = Time.time - hands.LastThrowTime;
                if (since < 0.08f + 0.2f * hands.LastThrowCharge)
                {
                    kind = hands.GetGrip(hands.LastThrown, out gripL, out gripR);
                    itemId = hands.LastThrown.GetInstanceID();
                    followThrown = kind != PlayerHands.GripKind.None;
                }
            }

            UpdateHand(_left, gripL, kind, itemId, followThrown, motor, speed, dt);
            UpdateHand(_right, gripR, kind, itemId, followThrown, motor, speed, dt);
        }

        private void UpdateHand(Hand hand, HandGrip grip, PlayerHands.GripKind kind, int itemId, bool followThrown,
            PlayerMotor motor, float speed, float dt)
        {
            Transform cam = _camera.transform;
            float side = hand.Side;
            float t = Time.time;
            State state;
            Vector3 palm;
            Quaternion rot;
            HandPose pose;
            float blend;

            bool usesGrip = grip.Active && (kind != PlayerHands.GripKind.OneHand || hand.Right);
            float sincePump = t - _lastPump;
            float sinceGesture = t - _gestureStart;

            if (!usesGrip && !(motor != null && (motor.IsSwimming || motor.IsClimbing)) && BoxingPose(hand, cam, out palm, out rot, out pose))
            {
                // Boxing: the path itself is smooth, so only a short blend when the fists first come up.
                state = State.Punch;
                blend = 0.08f;
            }
            else if (_gesture == AvatarGesture.Breath && sinceGesture < 1.1f && _reach != Vector3.zero)
            {
                // Mouth-to-mouth: the view leans right in to the lips (PlayerLook.LeanIn), so the hands hold the face
                // from the sides, on the cheeks, and stay out of the way.
                state = State.Breath;
                Vector3 across = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
                palm = _reach + across * (hand.Right ? 0.2f : -0.2f) - Vector3.up * 0.03f; // beside the head, out of view
                rot = HandBones.Orient(Vector3.down, -across * (hand.Right ? 1f : -1f), side);
                pose = HandPose.Cup;
                blend = 0.12f;
            }
            else if (sincePump < 1.2f)
            {
                // CPR: palms flat on the chest, right hand on top, pressing on each pump.
                state = State.Cpr;
                float press = sincePump < 0.18f ? Mathf.Sin(sincePump / 0.18f * Mathf.PI) * 0.06f : 0f;
                palm = _pumpPoint + Vector3.up * ((hand.Right ? 0.035f : 0f) - press) + FrameRight * (side * 0.01f);
                rot = HandBones.Orient(FrameForward, Vector3.down, side);
                pose = HandPose.Flat;
                blend = 0.15f;
            }
            else if (usesGrip)
            {
                state = followThrown ? State.ThrownItem : State.Item;
                palm = grip.Point;
                rot = grip.Rotation(side);
                pose = grip.Pose;
                blend = followThrown ? 0.05f : 0.16f;
                // Tools kick: the defibrillator's paddles push forward (guns kick by themselves).
                if (_gesture == AvatarGesture.Shoot && sinceGesture < 0.16f && !_rigidGrip)
                    palm += (cam.up * 0.05f - cam.forward * 0.06f) * (1f - sinceGesture / 0.16f);
                else if (_gesture == AvatarGesture.Zap && sinceGesture < 0.5f)
                    palm += (cam.forward * 0.18f - cam.up * 0.08f) * Mathf.Sin(sinceGesture / 0.5f * Mathf.PI);
            }
            else if (Vehicles.Vehicle.SeatOf(_hub) is { } vehicle && vehicle.GetHandlebars(out HandGrip barL, out HandGrip barR))
            {
                // Driving: both hands on the handlebars.
                state = State.Drive;
                HandGrip bar = hand.Right ? barR : barL;
                palm = bar.Point;
                rot = bar.Rotation(side);
                pose = bar.Pose;
                blend = 0.15f;
            }
            else if (hand.Right && _gesture == AvatarGesture.Interact && sinceGesture < 0.4f)
            {
                // Reach out and poke / press what we used.
                state = State.Reach;
                Vector3 target = _reach != Vector3.zero ? _reach : cam.position + cam.forward * 0.7f - cam.up * 0.15f;
                Vector3 toTarget = Vector3.ClampMagnitude(target - cam.position, MaxReach);
                palm = cam.position + toTarget;
                rot = HandBones.Orient(toTarget, Vector3.down, side);
                pose = HandPose.Point;
                blend = 0.1f;
            }
            else if (hand.Right && _gesture == AvatarGesture.Throw && sinceGesture < 0.35f)
            {
                // Follow-through after letting go.
                state = State.FollowThrough;
                float u = sinceGesture / 0.35f;
                float e = 1f - (1f - u) * (1f - u);
                palm = cam.TransformPoint(Vector3.Lerp(new Vector3(0.28f, -0.12f, 0.2f), new Vector3(0.08f, -0.36f, 0.62f), e));
                rot = HandBones.Orient(cam.forward - cam.up * 0.3f, -cam.up, side);
                pose = HandPose.Flat;
                blend = 0.05f;
            }
            else if (hand.Right && _gesture == AvatarGesture.Wave && sinceGesture < 1.5f)
            {
                state = State.Wave;
                palm = cam.TransformPoint(new Vector3(0.3f + Mathf.Sin(t * 12f) * 0.07f, 0.02f, 0.55f));
                rot = HandBones.Orient(cam.up + cam.right * (Mathf.Sin(t * 12f) * 0.35f), cam.forward, side);
                pose = HandPose.Wave;
                blend = 0.12f;
            }
            else if (motor != null && motor.IsClimbing)
            {
                state = State.Climb;
                palm = cam.TransformPoint(new Vector3(side * 0.26f, -0.1f, 0.5f));
                rot = HandBones.Orient(cam.forward, -cam.up, side);
                pose = HandPose.Carry;
                blend = 0.1f;
            }
            else if (motor != null && motor.IsSwimming)
            {
                state = State.Swim;
                bool stroking = speed > 0.6f || motor.IsHeadUnderwater;
                if (hand.Right) _stroke = Mathf.Repeat(_stroke + dt * (stroking ? 0.55f + speed * 0.12f : 0.7f), 1f);
                Vector3 local;
                Vector3 palmLocal;
                if (stroking)
                {
                    local = Breaststroke(_stroke, out float sweep);
                    local.x *= side;
                    palmLocal = Vector3.Lerp(new Vector3(side * 0.2f, -1f, 0f), new Vector3(side * 0.9f, -0.2f, -0.5f), sweep);
                }
                else
                {
                    float a = _stroke * Mathf.PI * 2f;
                    local = new Vector3(side * (0.3f - Mathf.Cos(a) * 0.08f), -0.36f + Mathf.Sin(a) * 0.04f, 0.42f + Mathf.Sin(a) * 0.06f);
                    palmLocal = new Vector3(side * 0.3f * Mathf.Cos(a), -1f, 0f);
                }
                palm = cam.TransformPoint(local);
                rot = HandBones.Orient(cam.forward, cam.TransformDirection(palmLocal), side);
                pose = HandPose.Swim;
                blend = 0.15f;
            }
            else
            {
                // Resting at the bottom of the view; running pumps them, walking swings them a little.
                state = _run > 0.5f ? State.Run : State.Idle;
                Quaternion frame = Quaternion.Euler(IdlePitch, _frameYaw, 0f);
                Vector3 origin = _hub.Head.position;
                float swing = Mathf.Sin(_stride * Mathf.PI + (hand.Right ? 0f : Mathf.PI));
                float walk = Mathf.Clamp01(speed / 4.5f) * (motor == null || motor.IsGrounded ? 1f : 0f);
                // Low enough to stay out of view when looking straight ahead: look down (or jump) to see them.
                var offset = new Vector3(side * 0.24f, -0.44f, 0.36f);
                offset += new Vector3(0f, -Mathf.Abs(Mathf.Sin(_stride * Mathf.PI)) * 0.018f * walk, swing * 0.03f * walk * (1f - _run));
                offset += new Vector3(-side * 0.04f, Mathf.Max(0f, swing) * 0.05f, swing * 0.13f) * _run;
                // Jumping / falling: hands flail up to the sides of the view.
                float flap = Mathf.Sin(Time.time * 9f + (hand.Right ? 0f : 1.3f)) * 0.03f;
                offset = Vector3.Lerp(offset, new Vector3(side * 0.34f, -0.16f + flap, 0.42f), Mathf.SmoothStep(0f, 1f, _air));
                palm = origin + frame * offset;
                Vector3 fingers = frame * new Vector3(-side * 0.12f, -0.5f, 1f);
                Vector3 palmDir = frame * Vector3.Lerp(new Vector3(-side * 0.9f, -0.35f, 0f), new Vector3(-side, 0f, 0f), _run);
                rot = HandBones.Orient(fingers, palmDir, side);
                pose = HandPose.Lerp(HandPose.Relaxed, HandPose.LooseFist, _run);
                blend = 0.22f;
            }

            // Blend from wherever the hand was when its job changed (in camera space, so it moves with the view).
            int key = (int)state | (state is State.Item or State.ThrownItem ? itemId << 4 : 0);
            if (key != hand.Key)
            {
                hand.Key = key;
                hand.FromPos = cam.InverseTransformPoint(hand.Palm);
                hand.FromRot = Quaternion.Inverse(cam.rotation) * hand.Rot;
                hand.FromPose = hand.Pose;
                hand.T = 0f;
                hand.Duration = blend;
            }
            hand.T = Mathf.Min(1f, hand.T + dt / Mathf.Max(0.01f, hand.Duration));
            float k = Mathf.SmoothStep(0f, 1f, hand.T);
            hand.Palm = Vector3.Lerp(cam.TransformPoint(hand.FromPos), palm, k);
            hand.Rot = Quaternion.Slerp(cam.rotation * hand.FromRot, rot, k);
            hand.Pose = HandPose.Lerp(hand.FromPose, pose, k);

            Solve(hand);
        }

        /// <summary>Puts the hand so its palm is where it should be (hands float; nothing to reach with).</summary>
        private void Solve(Hand hand)
        {
            Vector3 eye = _camera.transform.position;
            Vector3 palm = eye + Vector3.ClampMagnitude(hand.Palm - eye, MaxReach + 0.3f);
            // Resting, swimming, holding things: never into a wall (reaching, pressing and punching do touch it).
            var state = (State)(hand.Key & 15);
            // A gun pulls itself back from walls; its hands stay on it.
            bool onGun = state == State.Item && _rigidGrip;
            if (state is not (State.Reach or State.Cpr or State.Punch or State.Breath) && !onGun) palm = KeepOutOfWalls(eye, palm);
            hand.Wrist.SetPositionAndRotation(palm - hand.Rot * hand.Bones.PalmContact, hand.Rot);
            hand.Bones.Pose(hand.Pose);
        }

        private readonly RaycastHit[] _wallHits = new RaycastHit[8];

        /// <summary>
        /// Standing against a wall (or a counter, a tree), the hands pull back toward the eye so they end at its
        /// surface instead of sinking into it. Only static things count: held items and bodies are handled by physics.
        /// </summary>
        private Vector3 KeepOutOfWalls(Vector3 eye, Vector3 palm)
        {
            Vector3 to = palm - eye;
            float distance = to.magnitude;
            if (distance < 0.05f) return palm;
            Vector3 dir = to / distance;
            const float fingers = 0.1f; // the fingers stick out past the palm point
            int count = Physics.SphereCastNonAlloc(eye, 0.05f, dir, _wallHits, distance + fingers, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _wallHits[i];
                if (h.distance <= 0f || h.collider.attachedRigidbody != null) continue;
                nearest = Mathf.Min(nearest, h.distance);
            }
            if (nearest >= distance + fingers) return palm;
            return eye + dir * Mathf.Max(0.12f, nearest - fingers);
        }

        private Vector3 FrameForward => Quaternion.Euler(0f, _frameYaw, 0f) * Vector3.forward;
        private Vector3 FrameRight => Quaternion.Euler(0f, _frameYaw, 0f) * Vector3.right;

        /// <summary>Right hand's path (camera space): hands together ahead, sweep out and back, tuck in, reach again.</summary>
        private static Vector3 Breaststroke(float c, out float sweep)
        {
            if (c < 0.3f)
            {
                sweep = 0f;
                return Vector3.Lerp(new Vector3(0.1f, -0.3f, 0.45f), new Vector3(0.08f, -0.2f, 0.7f), Mathf.SmoothStep(0f, 1f, c / 0.3f));
            }
            if (c < 0.65f)
            {
                sweep = Mathf.Sin((c - 0.3f) / 0.35f * Mathf.PI);
                return Vector3.Lerp(new Vector3(0.08f, -0.2f, 0.7f), new Vector3(0.48f, -0.26f, 0.36f), Mathf.SmoothStep(0f, 1f, (c - 0.3f) / 0.35f));
            }
            sweep = 0f;
            return Vector3.Lerp(new Vector3(0.48f, -0.26f, 0.36f), new Vector3(0.1f, -0.3f, 0.45f), Mathf.SmoothStep(0f, 1f, (c - 0.65f) / 0.35f));
        }
    }
}
