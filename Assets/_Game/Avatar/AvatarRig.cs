using UnityEngine;
using UnityEngine.Rendering;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// A cartoon character built in code from an <see cref="AvatarLook"/>: a humanoid skeleton of plain transforms
    /// and one vertex-coloured skinned mesh (every part rigidly on its bone, rounded ends hide the joints).
    /// A look with a <see cref="AvatarLook.Body"/> uses a generated <see cref="AvatarBody"/> instead: the same
    /// skeleton, moved to that character's joints, with its textured mesh skinned to it.
    /// The root is at the feet, facing +Z; in the rest pose arms and legs hang straight down, and every limb bone
    /// points down its local -Y. Animation sets bone rotations (see <see cref="AvatarAnimator"/>).
    /// </summary>
    [DefaultExecutionOrder(190)] // after everything that poses the arms (AvatarAnimator 50, FirstPersonArms 100)
    public class AvatarRig : MonoBehaviour
    {
        public enum Bone
        {
            Hips, Spine, Chest, Neck, Head,
            UpperArmL, ForearmL, HandL, UpperArmR, ForearmR, HandR,
            ThighL, ShinL, FootL, ThighR, ShinR, FootR,
            EyeL, EyeR, Mouth, BrowL, BrowR,
            BustL, BustR,   // feminine figures: chest shapes on spring bones (see AvatarJiggle)
            ShoulderL, ShoulderR,   // generated bodies: turn half as far as the upper arm (round armpits, see LateUpdate)
            Count
        }

        [SerializeField] private Material _material;
        [SerializeField] private bool _buildOnAwake = true;

        private static Material _sharedMaterial;

        /// <summary>Cartoon hands are drawn this much bigger than life (same factor in first person).</summary>
        public const float HandScale = 1.15f;
        // Body bones (Bone enum) first, then the left hand's fingers, then the right hand's.
        private const int FingerStart = (int)Bone.Count;
        private const int BoneTotal = FingerStart + 2 * HandBones.BoneCount;

        private readonly Transform[] _bones = new Transform[BoneTotal];
        private readonly Vector3[] _restPosition = new Vector3[BoneTotal];
        private readonly Quaternion[] _restRotation = new Quaternion[BoneTotal];
        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private bool _built;
        // Generated bodies: undoes each upper arm's bind turn (the model's A-pose arm), to measure the arm's turn from it.
        private Quaternion _armUnbindL = Quaternion.identity, _armUnbindR = Quaternion.identity;

        public AvatarLook Look { get; private set; } = AvatarLook.Lifeguard;
        /// <summary>The generated body in use, or null for the code-built one.</summary>
        public AvatarBody GeneratedBody { get; private set; }
        public float Scale { get; private set; } = 1f;
        public float UpperArmLength { get; private set; }
        public float ForearmLength { get; private set; }
        public float HandLength { get; private set; }
        public float ThighLength { get; private set; }
        public float ShinLength { get; private set; }
        public float AnkleHeight { get; private set; }
        public float HipHeight { get; private set; }
        public float EyeHeight { get; private set; }
        /// <summary>The head's size measured from the head bone (for hats and masks put on at run time).</summary>
        public float HeadTop { get; private set; }
        public float HeadHalfWidth { get; private set; }
        public float HeadFront { get; private set; }
        public float HeadBack { get; private set; }
        public SkinnedMeshRenderer Renderer => _renderer;
        public HandBones LeftHand { get; private set; }
        public HandBones RightHand { get; private set; }
        public HandBones Hand(bool right) => right ? RightHand : LeftHand;
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

        /// <summary>
        /// Shoulder helpers follow half the upper arm's turn away from the model's own pose. The skin between the
        /// chest and the arm is weighted to them (prepare_character.py), so it never blends two bones more than half
        /// a raised arm apart: straight blending across 160 degrees pinched the armpits into a hard fold.
        /// </summary>
        private void LateUpdate()
        {
            if (!_built || GeneratedBody == null) return;
            this[Bone.ShoulderL].localRotation = Quaternion.Slerp(Quaternion.identity, this[Bone.UpperArmL].localRotation * _armUnbindL, 0.5f);
            this[Bone.ShoulderR].localRotation = Quaternion.Slerp(Quaternion.identity, this[Bone.UpperArmR].localRotation * _armUnbindR, 0.5f);
        }

        /// <summary>
        /// Resets every bone to its rest pose. A caller that poses both hands itself afterwards passes
        /// <paramref name="fingers"/> false and saves posing all thirty finger bones twice.
        /// </summary>
        public void ResetPose(bool fingers = true)
        {
            for (int i = 0; i < FingerStart; i++)
            {
                if (_bones[i] == null) continue;
                _bones[i].localPosition = _restPosition[i];
                _bones[i].localRotation = _restRotation[i];
                _bones[i].localScale = Vector3.one;
            }
            if (!fingers) return;
            // Fingers rest slightly curled (animators and ragdolls pose them over this).
            LeftHand?.Pose(HandPose.Relaxed);
            RightHand?.Pose(HandPose.Relaxed);
        }

        public void SetShadowsOnly(bool shadowsOnly)
        {
            if (_renderer != null) _renderer.shadowCastingMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        /// <summary>Face: eyes 0 (shut) .. 1 (open) .. 1.5 (wide), mouth 0 (closed) .. 1 (open wide), brows -1 (worried) .. 1 (angry).</summary>
        public void SetExpression(float eyes, float mouth, float brows = 0f)
        {
            if (!_built) return;
            if (GeneratedBody != null)
            {
                // A painted face: the eyelids come down over the eyes, the open mouth grows over the lips.
                if (!GeneratedBody.HasFace) return;
                // A lid half way down leaves half a painted eye staring out from under it, so these eyes are either
                // open or shut (a blink is two frames).
                var lid = eyes < 0.45f ? Vector3.one : Vector3.zero;
                this[Bone.EyeL].localScale = lid;
                this[Bone.EyeR].localScale = lid;
                float open = Mathf.Clamp01((mouth - 0.1f) * 1.6f);
                this[Bone.Mouth].localScale = open > 0.02f ? new Vector3(1f, open, 1f) : Vector3.zero; // shut: nothing, not a dark line on the lips
                return;
            }
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
            GeneratedBody = AvatarBodyLibrary.Get(look.Body);
            Scale = GeneratedBody != null ? GeneratedBody.Scale : 0.93f + look.Height * 0.05f;
            CreateBones(look);
            if (GeneratedBody != null) UseBody(GeneratedBody);
            else BuildMesh(look);
            _built = true;
            ResetPose();
            Rebuilt?.Invoke();
        }

        private struct Body
        {
            public float Width, Belly, Limb, Shoulder;
        }

        /// <summary>Feminine figures: narrower shoulders and waist, wider hips (the rest of the shape is in AvatarParts).</summary>
        private static Body BodyFor(AvatarLook look)
        {
            Body b = BodyFor(look.Build);
            if (look.Feminine)
            {
                b.Shoulder *= 0.9f;
                b.Limb *= 0.88f;
            }
            return b;
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
            Body b = BodyFor(look);
            UpperArmLength = 0.29f * s;
            ForearmLength = 0.26f * s;
            HandLength = 0.1f * s;
            ThighLength = 0.41f * s;
            ShinLength = 0.39f * s;
            AnkleHeight = 0.08f * s;
            HipHeight = 0.92f * s;
            EyeHeight = 1.68f * s;
            // The code-built head: an ellipsoid a little above and in front of the head bone (AvatarParts).
            float hs = s * AvatarParts.HeadScale;
            HeadTop = 0.298f * hs;
            HeadHalfWidth = 0.148f * hs;
            HeadFront = 0.162f * hs;
            HeadBack = 0.138f * hs;
            AvatarBody generated = GeneratedBody;
            if (generated != null)
            {
                if (generated.HeadTop > 0f)
                {
                    HeadTop = generated.HeadTop;
                    HeadHalfWidth = generated.HeadHalfWidth;
                    HeadFront = generated.HeadFront;
                    HeadBack = generated.HeadBack;
                }
                UpperArmLength = generated.UpperArmLength;
                ForearmLength = generated.ForearmLength;
                HandLength = generated.HandLength;
                ThighLength = generated.ThighLength;
                ShinLength = generated.ShinLength;
                AnkleHeight = generated.AnkleHeight;
                HipHeight = generated.HipHeight;
                EyeHeight = generated.EyeHeight;
            }

            void Make(Bone bone, Bone? parent, Vector3 localPosition)
            {
                int i = (int)bone;
                if (_bones[i] == null)
                {
                    _bones[i] = new GameObject(bone.ToString()).transform;
                }
                _bones[i].SetParent(parent.HasValue ? _bones[(int)parent.Value] : transform, false);
                _restPosition[i] = generated != null && i < generated.RestPositions.Length ? generated.RestPositions[i] : localPosition * s;
                _restRotation[i] = generated == null ? Quaternion.identity
                    : bone == Bone.HandL ? generated.HandRestL : bone == Bone.HandR ? generated.HandRestR : Quaternion.identity;
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
            Vector3 bust = AvatarParts.BustOffset(look);
            Make(Bone.BustL, Bone.Chest, new Vector3(-bust.x, bust.y, bust.z));
            Make(Bone.BustR, Bone.Chest, bust);
            // Shoulder helpers on the shoulder joints: only generated bodies have skin on them.
            Make(Bone.ShoulderL, Bone.Chest, new Vector3(-b.Shoulder, 0.17f, 0f));
            Make(Bone.ShoulderR, Bone.Chest, new Vector3(b.Shoulder, 0.17f, 0f));

            // Fingers: keep the same transforms across rebuilds.
            var leftFingers = new Transform[HandBones.BoneCount];
            var rightFingers = new Transform[HandBones.BoneCount];
            System.Array.Copy(_bones, FingerStart, leftFingers, 0, HandBones.BoneCount);
            System.Array.Copy(_bones, FingerStart + HandBones.BoneCount, rightFingers, 0, HandBones.BoneCount);
            if (generated != null && generated.HasFingers)
            {
                // The finger bones run down the model's own fingers (measured when it was baked, in metres).
                LeftHand = new HandBones(_bones[(int)Bone.HandL], -1f, 1f, leftFingers, FingerShape(generated.FingerBasesL, generated.FingerLengthsL));
                RightHand = new HandBones(_bones[(int)Bone.HandR], 1f, 1f, rightFingers, FingerShape(generated.FingerBasesR, generated.FingerLengthsR));
            }
            else
            {
                LeftHand = new HandBones(_bones[(int)Bone.HandL], -1f, s * HandScale, leftFingers);
                RightHand = new HandBones(_bones[(int)Bone.HandR], 1f, s * HandScale, rightFingers);
            }
            System.Array.Copy(LeftHand.Bones, 0, _bones, FingerStart, HandBones.BoneCount);
            System.Array.Copy(RightHand.Bones, 0, _bones, FingerStart + HandBones.BoneCount, HandBones.BoneCount);
            ResetPose();
        }

        private static HandBones.Shape FingerShape(Vector3[] bases, float[] lengths)
        {
            var shape = new HandBones.Shape { Bases = bases, Lengths = lengths, ThumbTuck = 0f };
            for (int f = 0; f < HandBones.Fingers; f++) shape.Directions[f] = Vector3.down;
            shape.Palm = new Vector3(-0.02f, bases[2].y * 0.6f, 0f);
            return shape;
        }

        /// <summary>A generated body: its mesh is already skinned to these bones (bone order and bind poses baked).</summary>
        private void UseBody(AvatarBody body)
        {
            LeftHand.ResetPose();
            RightHand.ResetPose();
            if (_renderer == null)
            {
                _renderer = gameObject.GetComponent<SkinnedMeshRenderer>();
                if (_renderer == null) _renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            }
            _renderer.sharedMesh = body.Mesh;
            _renderer.bones = _bones;
            _renderer.rootBone = _bones[(int)Bone.Hips];
            _renderer.localBounds = new Bounds(Vector3.zero, Vector3.one * 3.2f);
            _renderer.updateWhenOffscreen = false;
            _renderer.quality = SkinQuality.Bone4; // smooth weights, unlike the rigid code-built parts
            _renderer.sharedMaterial = body.Material;
            _renderer.skinnedMotionVectors = false;
            Matrix4x4[] bindposes = body.Mesh.bindposes; // root space, so an arm's rotation is its bind turn undone
            _armUnbindL = bindposes[(int)Bone.UpperArmL].rotation;
            _armUnbindR = bindposes[(int)Bone.UpperArmR].rotation;
        }

        private void BuildMesh(AvatarLook look)
        {
            var kit = new AvatarMeshKit();
            var bindposes = new Matrix4x4[_bones.Length];
            Matrix4x4 rootToWorld = transform.localToWorldMatrix;
            for (int i = 0; i < _bones.Length; i++)
                bindposes[i] = _bones[i].worldToLocalMatrix * rootToWorld;

            // Bind with straight fingers (the rest pose), whatever pose they were left in.
            LeftHand.ResetPose();
            RightHand.ResetPose();
            for (int i = FingerStart; i < BoneTotal; i++)
                bindposes[i] = _bones[i].worldToLocalMatrix * rootToWorld;

            void On(Bone bone) => kit.SetBone((int)bone, bindposes[(int)bone].inverse);
            Body body = BodyFor(look);
            AvatarParts.Build(kit, look, body.Width, body.Belly, body.Limb, body.Shoulder, Scale, On);
            void HandMesh(HandBones hand, Bone handBone, int first) => hand.BuildMesh(kit, look.SkinColor, f =>
            {
                int index = f < 0 ? (int)handBone : first + f;
                kit.SetBone(index, bindposes[index].inverse);
            });
            HandMesh(LeftHand, Bone.HandL, FingerStart);
            HandMesh(RightHand, Bone.HandR, FingerStart + HandBones.BoneCount);

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
            _renderer.quality = SkinQuality.Bone1; // every part rigidly on one bone
            _renderer.sharedMaterial = _material != null ? _material : SharedMaterial;
            _renderer.skinnedMotionVectors = false;
        }
    }
}
