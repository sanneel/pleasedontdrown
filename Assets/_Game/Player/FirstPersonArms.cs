using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using UnityEngine;
using UnityEngine.Rendering;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// The local player's own arms and hands, How to Fish style.
    ///
    /// Idle hands rest at the bottom of the view in a frame that follows where you face with a springy lag and a
    /// fixed downward tilt: they sway when you turn and come up into view when you look down. Picking something
    /// up blends each hand from wherever it is into that item's grip (palm position, finger direction, palm
    /// direction, finger curl); letting go blends back. Hands follow a thrown item out for a moment. Swimming
    /// strokes, CPR presses, reaching for things, waving and climbing have their own poses. Arms reach the hands
    /// with two-bone IK from shoulders below the camera. Same skin and sleeves as your character.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FirstPersonArms : MonoBehaviour
    {
        private const float LengthScale = 1.15f;
        private const float Thickness = 1.18f;
        private const float IdlePitch = 5f;
        private static readonly Vector3 ShoulderL = new(-0.2f, -0.3f, 0.05f);
        private static readonly Vector3 ShoulderR = new(0.2f, -0.3f, 0.05f);

        private enum State { Idle, Run, Swim, Climb, Item, Cpr, Reach, ThrownItem, FollowThrough, Wave }

        private sealed class Hand
        {
            public bool Right;
            public float Side;
            public Transform Upper, Fore, Wrist;
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
        private readonly Transform[] _bones = new Transform[6 + 2 * HandBones.BoneCount];
        private readonly Hand _left = new() { Right = false, Side = -1f };
        private readonly Hand _right = new() { Right = true, Side = 1f };
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private float _upper, _fore;
        private AvatarLook _look;
        private bool _built;

        private float _frameYaw;
        private float _frameYawVelocity;
        private float _stride;
        private float _stroke;
        private float _run;
        private AvatarGesture _gesture;
        private float _gestureStart = -10f;
        private Vector3 _reach;
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
            _upper = 0.29f * s * LengthScale;
            _fore = 0.26f * s * LengthScale;

            // Arm chains: upper arm (at the shoulder), forearm, wrist.
            string[] names = { "UpperArmL", "ForearmL", "HandL", "UpperArmR", "ForearmR", "HandR" };
            for (int i = 0; i < 6; i++)
            {
                if (_bones[i] == null) _bones[i] = new GameObject(names[i]).transform;
                int chainStart = i < 3 ? 0 : 3;
                _bones[i].SetParent(i == chainStart ? _root : _bones[i - 1], false);
                _bones[i].localRotation = Quaternion.identity;
                _bones[i].localPosition = i == chainStart ? (i == 0 ? ShoulderL : ShoulderR)
                    : new Vector3(0f, -(i % 3 == 1 ? _upper : _fore), 0f);
            }
            SetupHand(_left, 0, s);
            SetupHand(_right, 3, s);

            var kit = new AvatarMeshKit();
            var bindposes = new Matrix4x4[_bones.Length];
            _left.Bones.ResetPose();
            _right.Bones.ResetPose();
            Matrix4x4 rootToWorld = _root.localToWorldMatrix;
            for (int i = 0; i < _bones.Length; i++) bindposes[i] = _bones[i].worldToLocalMatrix * rootToWorld;
            void Use(int index) => kit.SetBone(index, bindposes[index].inverse);
            float limb = AvatarRig.LimbWidthFor(look.Build);
            void On(Bone b) => Use(b switch
            {
                Bone.UpperArmL => 0, Bone.ForearmL => 1, Bone.HandL => 2,
                Bone.UpperArmR => 3, Bone.ForearmR => 4, _ => 5
            });
            AvatarParts.BuildArm(kit, look, true, limb, s, On, LengthScale, Thickness);
            AvatarParts.BuildArm(kit, look, false, limb, s, On, LengthScale, Thickness);
            _left.Bones.BuildMesh(kit, look.SkinColor, f => Use(f < 0 ? 2 : 6 + f));
            _right.Bones.BuildMesh(kit, look.SkinColor, f => Use(f < 0 ? 5 : 6 + HandBones.BoneCount + f));
            _mesh = kit.ToMesh("FirstPersonArms", bindposes, _mesh);

            if (_renderer == null)
            {
                _renderer = _root.gameObject.AddComponent<SkinnedMeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.updateWhenOffscreen = true;
                _renderer.skinnedMotionVectors = false;
                _renderer.quality = SkinQuality.Bone1;
            }
            _renderer.sharedMesh = _mesh;
            _renderer.bones = _bones;
            _renderer.rootBone = _root;
            _renderer.sharedMaterial = AvatarRig.SharedMaterial;
            _built = true;
        }

        private void SetupHand(Hand hand, int chain, float s)
        {
            hand.Upper = _bones[chain];
            hand.Fore = _bones[chain + 1];
            hand.Wrist = _bones[chain + 2];
            int first = 6 + (hand.Right ? HandBones.BoneCount : 0);
            var reuse = new Transform[HandBones.BoneCount];
            System.Array.Copy(_bones, first, reuse, 0, HandBones.BoneCount);
            hand.Bones = new HandBones(hand.Wrist, hand.Side, s * AvatarRig.HandScale, reuse);
            System.Array.Copy(hand.Bones.Bones, 0, _bones, first, HandBones.BoneCount);
            hand.Palm = _root.TransformPoint((hand.Right ? ShoulderR : ShoulderL) + new Vector3(0f, -0.4f, 0.2f));
            hand.Rot = _root.rotation;
        }

        public void Play(AvatarGesture gesture, Vector3 point = default)
        {
            if (gesture is AvatarGesture.ChargeStart or AvatarGesture.ChargeEnd or AvatarGesture.EatStart or AvatarGesture.EatStop or AvatarGesture.Bite)
                return;
            _gesture = gesture;
            _gestureStart = Time.time;
            _reach = point;
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
            if (_root != null) Destroy(_root.gameObject);
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (!_built || _hub == null) return;
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

            // What we hold (or just threw).
            HandGrip gripL = default, gripR = default;
            PlayerHands.GripKind kind = PlayerHands.GripKind.None;
            int itemId = 0;
            Item held = hands != null ? hands.HeldItem : null;
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

            if (sincePump < 1.2f)
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
            }
            else if (hand.Right && _gesture == AvatarGesture.Interact && sinceGesture < 0.4f)
            {
                // Reach out and poke / press what we used.
                state = State.Reach;
                Vector3 shoulder = _root.TransformPoint(ShoulderR);
                Vector3 target = _reach != Vector3.zero ? _reach : cam.position + cam.forward * 0.7f - cam.up * 0.15f;
                Vector3 toTarget = Vector3.ClampMagnitude(target - shoulder, (_upper + _fore) * 0.97f);
                palm = shoulder + toTarget;
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
                var offset = new Vector3(side * 0.2f, -0.245f, 0.44f);
                offset += new Vector3(0f, -Mathf.Abs(Mathf.Sin(_stride * Mathf.PI)) * 0.018f * walk, swing * 0.03f * walk * (1f - _run));
                offset += new Vector3(-side * 0.04f, Mathf.Max(0f, swing) * 0.05f, swing * 0.13f) * _run;
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

        /// <summary>Arm IK to the wrist that puts the palm where it should be; the shoulder slides forward if it can't reach.</summary>
        private void Solve(Hand hand)
        {
            hand.Upper.localPosition = hand.Right ? ShoulderR : ShoulderL;
            Vector3 wrist = hand.Palm - hand.Rot * hand.Bones.PalmContact;
            Vector3 toWrist = wrist - hand.Upper.position;
            float reach = (_upper + _fore) * 0.985f;
            if (toWrist.magnitude > reach) hand.Upper.position = wrist - toWrist.normalized * reach; // off-screen anyway

            Vector3 elbow = -_root.up + _root.right * (hand.Side * 0.7f) - _root.forward * 0.3f;
            IK.Solve(hand.Upper, hand.Fore, _upper, _fore, wrist, elbow, 1f, false);
            hand.Wrist.rotation = hand.Rot;
            hand.Bones.Pose(hand.Pose);
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
