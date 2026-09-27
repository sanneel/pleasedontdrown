using UnityEngine;
using UnityEngine.Rendering;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// A cartoon character built in code from an <see cref="AvatarLook"/>: a humanoid skeleton of plain transforms
    /// and one vertex-coloured skinned mesh (every part rigidly on its bone, rounded ends hide the joints).
    /// The root is at the feet, facing +Z; in the rest pose arms and legs hang straight down, and every limb bone
    /// points down its local -Y. Animation sets bone rotations (see <see cref="AvatarAnimator"/>).
    /// </summary>
    public class AvatarRig : MonoBehaviour
    {
        public enum Bone
        {
            Hips, Spine, Chest, Neck, Head,
            UpperArmL, ForearmL, HandL, UpperArmR, ForearmR, HandR,
            ThighL, ShinL, FootL, ThighR, ShinR, FootR,
            EyeL, EyeR, Mouth, BrowL, BrowR,
            Count
        }

        [SerializeField] private Material _material;
        [SerializeField] private bool _buildOnAwake = true;

        private static Material _sharedMaterial;

        private readonly Transform[] _bones = new Transform[(int)Bone.Count];
        private readonly Vector3[] _restPosition = new Vector3[(int)Bone.Count];
        private readonly Quaternion[] _restRotation = new Quaternion[(int)Bone.Count];
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private bool _built;

        public AvatarLook Look { get; private set; } = AvatarLook.Lifeguard;
        public float Scale { get; private set; } = 1f;
        public float UpperArmLength { get; private set; }
        public float ForearmLength { get; private set; }
        public float HandLength { get; private set; }
        public float ThighLength { get; private set; }
        public float ShinLength { get; private set; }
        public float AnkleHeight { get; private set; }
        public float HipHeight { get; private set; }
        public float EyeHeight { get; private set; }
        public SkinnedMeshRenderer Renderer => _renderer;
        public bool IsBuilt => _built;
        /// <summary>Raised after every (re)build, so animators can re-read lengths.</summary>
        public event System.Action Rebuilt;

        public Transform this[Bone bone] => _bones[(int)bone];
        public Vector3 RestPosition(Bone bone) => _restPosition[(int)bone];
        public Quaternion RestRotation(Bone bone) => _restRotation[(int)bone];

        /// <summary>The one character material (scene objects set it; runtime-made rigs borrow it).</summary>
        public static Material SharedMaterial
        {
            get => _sharedMaterial;
            set { if (value != null) _sharedMaterial = value; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _sharedMaterial = null;

        private void Awake()
        {
            SharedMaterial = _material;
            if (_buildOnAwake && !_built) Build(Look);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        /// <summary>Resets every bone to its rest pose.</summary>
        public void ResetPose()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] == null) continue;
                _bones[i].localPosition = _restPosition[i];
                _bones[i].localRotation = _restRotation[i];
                _bones[i].localScale = Vector3.one;
            }
        }

        public void SetShadowsOnly(bool shadowsOnly)
        {
            if (_renderer != null) _renderer.shadowCastingMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        /// <summary>Face: eyes 0 (shut) .. 1 (open) .. 1.5 (wide), mouth 0 (closed) .. 1 (open wide), brows -1 (worried) .. 1 (angry).</summary>
        public void SetExpression(float eyes, float mouth, float brows = 0f)
        {
            if (!_built) return;
            Vector3 eyeScale = new Vector3(1f, Mathf.Clamp(eyes, 0.08f, 1.6f), 1f);
            this[Bone.EyeL].localScale = eyeScale;
            this[Bone.EyeR].localScale = eyeScale;
            this[Bone.Mouth].localScale = new Vector3(1f - mouth * 0.25f, 0.5f + mouth * 2.2f, 1f);
            this[Bone.BrowL].localRotation = _restRotation[(int)Bone.BrowL] * Quaternion.Euler(0f, 0f, -brows * 18f);
            this[Bone.BrowR].localRotation = _restRotation[(int)Bone.BrowR] * Quaternion.Euler(0f, 0f, brows * 18f);
        }

        // ------------------------------------------------------------------ building

        /// <summary>(Re)builds skeleton and mesh for a look. Keeps the same bone transforms when rebuilding.</summary>
        public void Build(AvatarLook look)
        {
            Look = look;
            Scale = 0.93f + look.Height * 0.05f;
            CreateBones(look);
            BuildMesh(look);
            _built = true;
            ResetPose();
            Rebuilt?.Invoke();
        }

        private struct Body
        {
            public float Width, Belly, Limb, Shoulder;
        }

        /// <summary>Arm/leg thickness multiplier for a body build (first-person arms match the body).</summary>
        public static float LimbWidthFor(byte build) => BodyFor(build).Limb;

        private static Body BodyFor(byte build) => build switch
        {
            0 => new Body { Width = 0.9f, Belly = 0.92f, Limb = 0.85f, Shoulder = 0.185f },
            2 => new Body { Width = 1.16f, Belly = 1.02f, Limb = 1.25f, Shoulder = 0.235f },
            3 => new Body { Width = 1.12f, Belly = 1.5f, Limb = 1.15f, Shoulder = 0.215f },
            _ => new Body { Width = 1f, Belly = 1f, Limb = 1f, Shoulder = 0.2f }
        };

        private void CreateBones(AvatarLook look)
        {
            float s = Scale;
            Body b = BodyFor(look.Build);
            UpperArmLength = 0.29f * s;
            ForearmLength = 0.26f * s;
            HandLength = 0.1f * s;
            ThighLength = 0.41f * s;
            ShinLength = 0.39f * s;
            AnkleHeight = 0.08f * s;
            HipHeight = 0.92f * s;
            EyeHeight = 1.68f * s;

            void Make(Bone bone, Bone? parent, Vector3 localPosition)
            {
                int i = (int)bone;
                if (_bones[i] == null)
                {
                    _bones[i] = new GameObject(bone.ToString()).transform;
                }
                _bones[i].SetParent(parent.HasValue ? _bones[(int)parent.Value] : transform, false);
                _restPosition[i] = localPosition * s;
                _restRotation[i] = Quaternion.identity;
            }

            Make(Bone.Hips, null, new Vector3(0f, 0.92f, 0f));
            Make(Bone.Spine, Bone.Hips, new Vector3(0f, 0.1f, 0f));
            Make(Bone.Chest, Bone.Spine, new Vector3(0f, 0.16f, 0f));
            Make(Bone.Neck, Bone.Chest, new Vector3(0f, 0.23f, 0f));
            Make(Bone.Head, Bone.Neck, new Vector3(0f, 0.08f, 0f));
            foreach (float side in new[] { -1f, 1f })
            {
                bool left = side < 0f;
                Make(left ? Bone.UpperArmL : Bone.UpperArmR, Bone.Chest, new Vector3(b.Shoulder * side, 0.17f, 0f));
                Make(left ? Bone.ForearmL : Bone.ForearmR, left ? Bone.UpperArmL : Bone.UpperArmR, new Vector3(0f, -0.29f, 0f));
                Make(left ? Bone.HandL : Bone.HandR, left ? Bone.ForearmL : Bone.ForearmR, new Vector3(0f, -0.26f, 0f));
                Make(left ? Bone.ThighL : Bone.ThighR, Bone.Hips, new Vector3(0.1f * side * Mathf.Lerp(1f, b.Width, 0.6f), -0.04f, 0f));
                Make(left ? Bone.ShinL : Bone.ShinR, left ? Bone.ThighL : Bone.ThighR, new Vector3(0f, -0.41f, 0f));
                Make(left ? Bone.FootL : Bone.FootR, left ? Bone.ShinL : Bone.ShinR, new Vector3(0f, -0.39f, 0f));
                Make(left ? Bone.EyeL : Bone.EyeR, Bone.Head, new Vector3(0.056f * side, 0.155f, 0.142f) * AvatarParts.HeadScale);
                Make(left ? Bone.BrowL : Bone.BrowR, Bone.Head, new Vector3(0.058f * side, 0.215f, 0.142f) * AvatarParts.HeadScale);
            }
            Make(Bone.Mouth, Bone.Head, new Vector3(0f, 0.068f, 0.145f) * AvatarParts.HeadScale);
            ResetPose();
        }

        private void BuildMesh(AvatarLook look)
        {
            var kit = new AvatarMeshKit();
            var bindposes = new Matrix4x4[_bones.Length];
            Matrix4x4 rootToWorld = transform.localToWorldMatrix;
            for (int i = 0; i < _bones.Length; i++)
                bindposes[i] = _bones[i].worldToLocalMatrix * rootToWorld;

            void On(Bone bone) => kit.SetBone((int)bone, bindposes[(int)bone].inverse);
            AvatarParts.Build(kit, look, BodyFor(look.Build).Width, BodyFor(look.Build).Belly, BodyFor(look.Build).Limb, BodyFor(look.Build).Shoulder, Scale, On);

            _mesh = kit.ToMesh("Avatar", bindposes, _mesh);
            if (_renderer == null)
            {
                _renderer = gameObject.GetComponent<SkinnedMeshRenderer>();
                if (_renderer == null) _renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            }
            _renderer.sharedMesh = _mesh;
            _renderer.bones = _bones;
            _renderer.rootBone = _bones[(int)Bone.Hips];
            _renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 3.2f); // generous: swimming and diving poses
            _renderer.updateWhenOffscreen = false;
            _renderer.quality = SkinQuality.Bone1;
            _renderer.sharedMaterial = _material != null ? _material : SharedMaterial;
            _renderer.skinnedMotionVectors = false;
        }
    }
}
