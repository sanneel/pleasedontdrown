using PleaseDontDrown.Avatars;
using UnityEngine;
using UnityEngine.Rendering;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// The local player's own arms, How to Fish style: two arms hanging off the camera whose hands (arm IK) grab
    /// whatever you hold, stroke when you swim, press during CPR, reach for things you use and swing through throws.
    /// Out of sight when idle; running pumps them into the bottom corners. Same skin and sleeves as your character.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FirstPersonArms : MonoBehaviour
    {
        private const float LengthScale = 1.32f;
        private static readonly Vector3 ShoulderL = new(-0.2f, -0.3f, 0f);
        private static readonly Vector3 ShoulderR = new(0.2f, -0.3f, 0f);

        private PlayerHub _hub;
        private Camera _camera;
        private Transform _root;
        private readonly Transform[] _bones = new Transform[6]; // upper, fore, hand (left), upper, fore, hand (right)
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private float _upper, _lower;
        private AvatarLook _look;
        private bool _built;

        private Vector3 _handL, _handR, _velL, _velR;
        private float _stride;
        private float _stroke;
        private AvatarGesture _gesture;
        private float _gestureStart = -10f;
        private Vector3 _reach;
        private float _lastPump = -10f;
        private Vector3 _pumpPoint;

        private void OnEnable() => Core.DevCommands.Register("armsdebug", "", "First-person arms: bones, targets, renderer.", _ =>
        {
            Core.DevCommands.Print($"built {_built}, renderer {(_renderer != null ? $"{_renderer.enabled} mat {(_renderer.sharedMaterial != null ? _renderer.sharedMaterial.name : "NONE")} verts {(_mesh != null ? _mesh.vertexCount : 0)} bounds {_renderer.bounds}" : "none")}");
            for (int i = 0; i < 6; i++)
                if (_bones[i] != null) Core.DevCommands.Print($"  {_bones[i].name}: world {_bones[i].position:F2} local-to-cam {_camera.transform.InverseTransformPoint(_bones[i].position):F2}");
            Core.DevCommands.Print($"  targets L {_handL:F2} R {_handR:F2}");
        }, owner: this);

        private void OnDisable() => Core.DevCommands.Unregister("armsdebug", this);

        public void Init(PlayerHub hub, Camera cam)
        {
            _hub = hub;
            _camera = cam;
            _root = new GameObject("FirstPersonArms").transform;
            _root.SetParent(cam.transform, false);
            _handL = ShoulderL + new Vector3(-0.04f, -0.5f, 0.2f);
            _handR = ShoulderR + new Vector3(0.04f, -0.5f, 0.2f);
        }

        public void Build(AvatarLook look)
        {
            if (_built && look.Equals(_look)) return;
            _look = look;
            float s = 0.93f + look.Height * 0.05f;
            _upper = 0.29f * s * LengthScale;
            _lower = (0.26f * LengthScale + 0.05f) * s;

            string[] names = { "UpperArmL", "ForearmL", "HandL", "UpperArmR", "ForearmR", "HandR" };
            for (int i = 0; i < 6; i++)
            {
                if (_bones[i] == null) _bones[i] = new GameObject(names[i]).transform;
                int chainStart = i < 3 ? 0 : 3;
                Transform parent = i == chainStart ? _root : _bones[i - 1];
                _bones[i].SetParent(parent, false);
                _bones[i].localRotation = Quaternion.identity;
                _bones[i].localPosition = i == chainStart ? (i == 0 ? ShoulderL : ShoulderR)
                    : new Vector3(0f, -(i % 3 == 1 ? 0.29f : 0.26f) * s * LengthScale, 0f);
            }

            var kit = new AvatarMeshKit();
            var bindposes = new Matrix4x4[6];
            Matrix4x4 rootToWorld = _root.localToWorldMatrix;
            for (int i = 0; i < 6; i++) bindposes[i] = _bones[i].worldToLocalMatrix * rootToWorld;
            float limb = AvatarRig.LimbWidthFor(look.Build);
            void On(Bone b)
            {
                int index = b switch
                {
                    Bone.UpperArmL => 0, Bone.ForearmL => 1, Bone.HandL => 2,
                    Bone.UpperArmR => 3, Bone.ForearmR => 4, _ => 5
                };
                kit.SetBone(index, bindposes[index].inverse);
            }
            AvatarParts.BuildArm(kit, look, true, limb, s, On, LengthScale);
            AvatarParts.BuildArm(kit, look, false, limb, s, On, LengthScale);
            _mesh = kit.ToMesh("FirstPersonArms", bindposes, _mesh);

            if (_renderer == null)
            {
                _renderer = _root.gameObject.AddComponent<SkinnedMeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
                _renderer.receiveShadows = false;
                _renderer.updateWhenOffscreen = true;
                _renderer.skinnedMotionVectors = false;
            }
            _renderer.sharedMesh = _mesh;
            _renderer.bones = _bones;
            _renderer.rootBone = _root;
            _renderer.sharedMaterial = AvatarRig.SharedMaterial;
            _built = true;
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

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_root != null) Destroy(_root.gameObject);
        }

        private Vector3 ToLocal(Vector3 world) => _root.InverseTransformPoint(world);

        private void LateUpdate()
        {
            if (!_built || _hub == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            PlayerMotor motor = _hub.Motor;
            PlayerHands hands = _hub.Hands;
            float speed = motor != null ? motor.HorizontalSpeed : 0f;
            float t = Time.time;

            // ---------------------------------------------------------- base: hands low, out of sight
            bool running = motor != null && motor.IsGrounded && motor.IsSprinting && speed > 5f;
            _stride += speed * dt / 1.5f;
            float pump = Mathf.Sin(_stride * Mathf.PI);
            float run = running ? 1f : 0f;
            Vector3 targetL = ShoulderL + new Vector3(-0.05f, -0.52f + Mathf.Max(0f, -pump) * 0.2f * run, 0.16f - pump * 0.2f * run);
            Vector3 targetR = ShoulderR + new Vector3(0.05f, -0.52f + Mathf.Max(0f, pump) * 0.2f * run, 0.16f + pump * 0.2f * run);
            float smooth = 0.09f;

            // ---------------------------------------------------------- swimming strokes
            if (motor != null && motor.IsSwimming && (hands == null || hands.HeldItem == null))
            {
                bool swimming = speed > 0.6f || motor.IsHeadUnderwater;
                _stroke = Mathf.Repeat(_stroke + dt * (swimming ? 0.55f + speed * 0.12f : 0.7f), 1f);
                if (swimming) Breaststroke(_stroke, out targetL, out targetR);
                else
                {
                    // Treading water: little sculling circles in front of the chest.
                    float a = _stroke * Mathf.PI * 2f;
                    targetL = new Vector3(-0.3f + Mathf.Cos(a) * 0.08f, -0.42f + Mathf.Sin(a) * 0.04f, 0.42f + Mathf.Sin(a) * 0.06f);
                    targetR = new Vector3(0.3f - Mathf.Cos(a) * 0.08f, -0.42f + Mathf.Sin(a + 1f) * 0.04f, 0.42f + Mathf.Sin(a) * 0.06f);
                }
                smooth = 0.05f;
            }

            if (motor != null && motor.IsClimbing)
            {
                targetL = new Vector3(-0.28f, -0.1f, 0.5f);
                targetR = new Vector3(0.28f, -0.1f, 0.5f);
                smooth = 0.05f;
            }

            // ---------------------------------------------------------- hands on what we hold
            Quaternion? gripRotation = null;
            if (hands != null && hands.HeldItem != null)
            {
                PlayerHands.GripKind grip = hands.GetGrips(out Vector3 gl, out Vector3 gr);
                if (grip != PlayerHands.GripKind.None)
                {
                    targetR = ToLocal(gr);
                    if (grip != PlayerHands.GripKind.OneHand) targetL = ToLocal(gl);
                    smooth = 0.02f;
                    gripRotation = Quaternion.identity;
                }
            }

            // ---------------------------------------------------------- CPR: both hands on the chest, pressing
            float sincePump = t - _lastPump;
            if (sincePump < 1.2f)
            {
                float press = sincePump < 0.18f ? Mathf.Sin(sincePump / 0.18f * Mathf.PI) * 0.06f : 0f;
                Vector3 chest = ToLocal(_pumpPoint + Vector3.down * press);
                targetL = chest + new Vector3(-0.03f, 0.03f, 0f);
                targetR = chest + new Vector3(0.03f, 0.05f, 0f);
                smooth = 0.03f;
            }

            // ---------------------------------------------------------- gestures
            float g = t - _gestureStart;
            if (_gesture == AvatarGesture.Interact && g < 0.38f)
            {
                float w = Mathf.Sin(g / 0.38f * Mathf.PI);
                Vector3 reach = _reach != Vector3.zero ? ToLocal(_reach) : new Vector3(0.12f, -0.2f, 0.7f);
                reach = ShoulderR + Vector3.ClampMagnitude(reach - ShoulderR, (_upper + _lower) * 0.95f);
                targetR = Vector3.Lerp(targetR, reach, w);
                smooth = 0.03f;
            }
            else if (_gesture == AvatarGesture.Throw && g < 0.35f)
            {
                // Follow-through: from behind the shoulder, whip forward and down.
                float e = 1f - (1f - g / 0.35f) * (1f - g / 0.35f);
                targetR = Vector3.Lerp(new Vector3(0.32f, -0.05f, 0.05f), new Vector3(0.06f, -0.42f, 0.62f), e);
                smooth = 0.015f;
            }
            else if (_gesture == AvatarGesture.Wave && g < 1.5f)
            {
                targetR = new Vector3(0.32f + Mathf.Sin(t * 12f) * 0.08f, 0.02f, 0.55f);
                smooth = 0.05f;
            }

            _handL = Vector3.SmoothDamp(_handL, targetL, ref _velL, smooth, Mathf.Infinity, dt);
            _handR = Vector3.SmoothDamp(_handR, targetR, ref _velR, smooth, Mathf.Infinity, dt);

            // ---------------------------------------------------------- solve
            Vector3 fwd = _root.forward, up = _root.up, right = _root.right;
            IK.Solve(_bones[0], _bones[1], _upper, _lower, _root.TransformPoint(_handL), -up - right * 0.7f - fwd * 0.3f, 1f, false);
            IK.Solve(_bones[3], _bones[4], _upper, _lower, _root.TransformPoint(_handR), -up + right * 0.7f - fwd * 0.3f, 1f, false);
            if (gripRotation.HasValue)
            {
                // Palms toward the item, fingers forward, thumbs up.
                // (Mitten palms face the body's midline at rest, so this one rotation suits both hands.)
                Quaternion grip = Quaternion.LookRotation(up, -fwd);
                _bones[2].rotation = grip;
                _bones[5].rotation = grip;
            }
        }

        /// <summary>Hands together ahead, sweep out to the sides and back, tuck under the chest, reach ahead again.</summary>
        private static void Breaststroke(float c, out Vector3 left, out Vector3 right)
        {
            Vector3 p;
            if (c < 0.3f) p = Vector3.Lerp(new Vector3(0.1f, -0.3f, 0.45f), new Vector3(0.08f, -0.2f, 0.7f), Mathf.SmoothStep(0f, 1f, c / 0.3f));
            else if (c < 0.65f) p = Vector3.Lerp(new Vector3(0.08f, -0.2f, 0.7f), new Vector3(0.48f, -0.26f, 0.36f), Mathf.SmoothStep(0f, 1f, (c - 0.3f) / 0.35f));
            else p = Vector3.Lerp(new Vector3(0.48f, -0.26f, 0.36f), new Vector3(0.1f, -0.3f, 0.45f), Mathf.SmoothStep(0f, 1f, (c - 0.65f) / 0.35f));
            right = p;
            left = new Vector3(-p.x, p.y, p.z);
        }
    }
}
