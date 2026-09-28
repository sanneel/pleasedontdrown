using System;
using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    /// <summary>How curled each finger is (0 straight .. 1 fully bent) and how far they fan out.</summary>
    [Serializable]
    public struct HandPose
    {
        public float Thumb, Index, Middle, Ring, Pinky, Spread;

        public HandPose(float fingers, float thumb, float spread)
        {
            Index = Middle = Ring = Pinky = fingers;
            Thumb = thumb;
            Spread = spread;
        }

        public static readonly HandPose Relaxed = new(0.34f, 0.26f, 0.02f) { Index = 0.26f, Pinky = 0.42f };
        public static readonly HandPose Flat = new(0.04f, 0.02f, 0.18f);
        public static readonly HandPose Wave = new(0f, 0f, 0.55f);
        public static readonly HandPose BoxGrip = new(0.34f, 0.08f, 0.04f);
        public static readonly HandPose BallGrip = new(0.3f, 0.22f, 0.5f);
        public static readonly HandPose Cup = new(0.55f, 0.42f, 0.1f);
        public static readonly HandPose Fist = new(0.95f, 0.72f, 0f);
        public static readonly HandPose LooseFist = new(0.68f, 0.5f, 0f);
        public static readonly HandPose Carry = new(0.5f, 0.18f, 0.06f);
        public static readonly HandPose Swim = new(0.1f, 0.04f, 0f);
        public static readonly HandPose Point = new(0.9f, 0.6f, 0f) { Index = 0.04f };

        public static HandPose Lerp(HandPose a, HandPose b, float t) => new()
        {
            Thumb = Mathf.Lerp(a.Thumb, b.Thumb, t),
            Index = Mathf.Lerp(a.Index, b.Index, t),
            Middle = Mathf.Lerp(a.Middle, b.Middle, t),
            Ring = Mathf.Lerp(a.Ring, b.Ring, t),
            Pinky = Mathf.Lerp(a.Pinky, b.Pinky, t),
            Spread = Mathf.Lerp(a.Spread, b.Spread, t)
        };

        /// <summary>Frame-rate independent move toward a target pose.</summary>
        public static HandPose Towards(HandPose current, HandPose target, float sharpness, float dt) =>
            Lerp(current, target, 1f - Mathf.Exp(-sharpness * dt));

        public float Finger(int i) => i switch { 0 => Thumb, 1 => Index, 2 => Middle, 3 => Ring, _ => Pinky };
    }

    /// <summary>One hand on something: where the palm touches, where the fingers point, which way the palm faces (world).</summary>
    public struct HandGrip
    {
        public bool Active;
        public Vector3 Point;
        public Vector3 Fingers;
        public Vector3 Palm;
        public HandPose Pose;

        public HandGrip(Vector3 point, Vector3 fingers, Vector3 palm, HandPose pose)
        {
            Active = true;
            Point = point;
            Fingers = fingers;
            Palm = palm;
            Pose = pose;
        }

        public Quaternion Rotation(float side) => HandBones.Orient(Fingers, Palm, side);
    }

    /// <summary>
    /// A hand with a palm, four fingers and a thumb, three jointed segments each (bones under the hand bone),
    /// built into the character's skinned mesh. Rest pose: fingers straight down the hand's local -Y, palm facing
    /// the body's midline (local -X on the right hand, +X on the left), thumb toward +Z.
    /// </summary>
    public sealed class HandBones
    {
        public const int Fingers = 5;     // thumb, index, middle, ring, pinky
        public const int Segments = 3;
        public const int BoneCount = Fingers * Segments;

        // Per finger: base position on the hand (right hand, before scale), segment lengths, base radius.
        private static readonly Vector3[] Bases =
        {
            new(-0.007f, -0.024f, 0.036f),  // thumb, low on the palm near the wrist
            new(-0.002f, -0.088f, 0.03f),
            new(-0.002f, -0.092f, 0.0095f),
            new(-0.002f, -0.089f, -0.011f),
            new(-0.002f, -0.082f, -0.03f)
        };
        private static readonly float[][] Lengths =
        {
            new[] { 0.032f, 0.024f, 0.02f },
            new[] { 0.037f, 0.023f, 0.019f },
            new[] { 0.04f, 0.025f, 0.02f },
            new[] { 0.037f, 0.024f, 0.019f },
            new[] { 0.029f, 0.019f, 0.016f }
        };
        // Chunky cartoon fingers.
        private static readonly float[] Radii = { 0.0158f, 0.0134f, 0.014f, 0.0132f, 0.0118f };
        // How far each segment bends at full curl (degrees).
        private static readonly float[][] Flex =
        {
            new[] { 38f, 45f, 55f },
            new[] { 82f, 98f, 70f },
            new[] { 85f, 100f, 70f },
            new[] { 86f, 100f, 70f },
            new[] { 88f, 100f, 70f }
        };
        private static readonly float[] SpreadFactor = { 0f, -1f, -0.3f, 0.35f, 1f };

        public readonly Transform Hand;
        public readonly Transform[] Bones = new Transform[BoneCount];
        public readonly float Side;       // +1 right, -1 left
        private readonly Quaternion[] _rest = new Quaternion[BoneCount];
        private readonly float _scale;

        /// <summary>Palm centre and palm surface, in hand space (for putting the palm on a point).</summary>
        public Vector3 PalmCenter => new Vector3(0f, -0.05f, 0f) * _scale;
        public Vector3 PalmContact => new Vector3(-Side * 0.02f, -0.052f, 0f) * _scale;

        public static int BoneIndex(int finger, int segment) => finger * Segments + segment;

        /// <summary>Creates (or re-seats) the finger bones under <paramref name="hand"/>.</summary>
        public HandBones(Transform hand, float side, float scale, Transform[] reuse = null)
        {
            Hand = hand;
            Side = side;
            _scale = scale;
            string prefix = side > 0f ? "R" : "L";
            string[] names = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
            for (int f = 0; f < Fingers; f++)
            {
                Transform parent = hand;
                Vector3 local = Mirror(Bases[f]) * scale;
                for (int s = 0; s < Segments; s++)
                {
                    int i = BoneIndex(f, s);
                    Transform bone = reuse != null && reuse[i] != null ? reuse[i] : new GameObject($"{names[f]}{s + 1}{prefix}").transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = local;
                    // The thumb starts angled down, forward and toward the palm; fingers start straight.
                    _rest[i] = f == 0 && s == 0 ? Quaternion.Euler(-42f, 0f, -Side * 18f) : Quaternion.identity;
                    bone.localRotation = _rest[i];
                    bone.localScale = Vector3.one;
                    Bones[i] = bone;
                    parent = bone;
                    local = new Vector3(0f, -Lengths[f][s] * scale, 0f);
                }
            }
        }

        private Vector3 Mirror(Vector3 v) => new(v.x * Side, v.y, v.z);

        /// <summary>Bends the fingers into a pose.</summary>
        public void Pose(HandPose pose)
        {
            for (int f = 0; f < Fingers; f++)
            {
                float curl = Mathf.Clamp01(pose.Finger(f));
                for (int s = 0; s < Segments; s++)
                {
                    int i = BoneIndex(f, s);
                    // Curling bends toward the palm side (about the hand's Z axis); spreading fans the fingers in the palm's plane.
                    Quaternion bend = Quaternion.Euler(0f, 0f, -Side * curl * Flex[f][s]);
                    Quaternion fan = s == 0 ? Quaternion.Euler(SpreadFactor[f] * pose.Spread * 16f, 0f, 0f) : Quaternion.identity;
                    Bones[i].localRotation = _rest[i] * fan * bend;
                }
            }
        }

        public void ResetPose()
        {
            for (int i = 0; i < BoneCount; i++) Bones[i].localRotation = _rest[i];
        }

        /// <summary>
        /// Adds the hand's shapes to a character mesh. <paramref name="on"/> switches to a bone: -1 = the hand (palm),
        /// otherwise a finger bone index (see <see cref="BoneIndex"/>).
        /// </summary>
        /// <param name="lowPoly">
        /// Chunky, faceted first-person hand (How to Fish look): fewer sides, thicker fingers, a stub of wrist that
        /// just ends (no arm). Build the mesh with flat shading.
        /// </param>
        public void BuildMesh(AvatarMeshKit kit, Color skin, Action<int> on, bool lowPoly = false)
        {
            float k = _scale;
            if (_smoothStyle)
            {
                BuildSmooth(kit, skin, on);
                return;
            }
            int big = lowPoly ? 7 : 12, small = lowPoly ? 6 : 10, rings = lowPoly ? 4 : 7, fingerSides = lowPoly ? 6 : 7;
            float chunk = lowPoly ? 1.22f : 1f;
            on(-1);
            // Palm: a soft flattened block, a little wider across the knuckles, with the thumb's fleshy base.
            kit.Ellipsoid(new Vector3(0f, -0.05f, 0f) * k, new Vector3(0.021f * chunk, 0.049f, 0.047f) * k, skin, segments: big, rings: rings + 1);
            kit.Ellipsoid(Mirror(new Vector3(0.002f, -0.08f, 0f)) * k, new Vector3(0.02f * chunk, 0.017f, 0.048f) * k, skin, segments: small, rings: rings);  // knuckles
            kit.Ellipsoid(Mirror(new Vector3(-0.009f, -0.032f, 0.024f)) * k, new Vector3(0.018f * chunk, 0.028f, 0.019f) * k, skin, segments: small, rings: rings); // thumb pad
            if (lowPoly)
                kit.Limb(0.035f * k, 0.025f * k, 0.026f * k, skin, new Vector3(0f, 0.03f, 0f) * k, segments: 7, crossSection: new Vector2(0.82f, 1.15f)); // a short wrist that just ends
            else
                kit.Ellipsoid(new Vector3(0f, -0.004f, 0f) * k, new Vector3(0.02f, 0.02f, 0.032f) * k, skin, segments: 10, rings: 6);                             // wrist
            Color nail = Color.Lerp(skin, Color.white, 0.45f);
            for (int f = 0; f < Fingers; f++)
            {
                for (int s = 0; s < Segments; s++)
                {
                    on(BoneIndex(f, s));
                    float r0 = Radii[f] * k * (1f - s * 0.1f) * chunk;
                    float r1 = r0 * 0.9f;
                    kit.Limb(Lengths[f][s] * k, r0, r1, skin, segments: fingerSides);
                    if (s == Segments - 1 && !lowPoly)
                        kit.Ellipsoid(Mirror(new Vector3(r1 / k * 0.55f, -Lengths[f][s] * 0.75f, 0f)) * k,
                            new Vector3(0.0035f, r1 / k * 0.75f, r1 / k * 0.7f) * k, nail, segments: 6, rings: 4); // nail on the back
                }
            }
        }

        private bool _smoothStyle;

        /// <summary>
        /// The first-person look (How to Fish): one soft rounded palm, round sausage fingers, a short wrist that just
        /// ends. No knuckle bumps, thumb pads or nails, so the smooth shading stays clean. Build with smooth normals.
        /// </summary>
        public void BuildSmoothMesh(AvatarMeshKit kit, Color skin, Action<int> on)
        {
            _smoothStyle = true;
            BuildMesh(kit, skin, on);
            _smoothStyle = false;
        }

        private void BuildSmooth(AvatarMeshKit kit, Color skin, Action<int> on)
        {
            float k = _scale;
            const float chunk = 1.25f;
            on(-1);
            kit.Ellipsoid(new Vector3(0f, -0.054f, 0f) * k, new Vector3(0.025f, 0.056f, 0.05f) * k, skin, segments: 18, rings: 12);
            kit.Limb(0.05f * k, 0.028f * k, 0.03f * k, skin, new Vector3(0f, 0.035f, 0f) * k, segments: 14, crossSection: new Vector2(0.85f, 1.15f));
            for (int f = 0; f < Fingers; f++)
            {
                for (int s = 0; s < Segments; s++)
                {
                    on(BoneIndex(f, s));
                    float r0 = Radii[f] * k * (1f - s * 0.08f) * chunk;
                    // Segments overlap a little so bent joints stay round instead of showing a gap.
                    kit.Limb(Lengths[f][s] * k, r0, r0 * 0.94f, skin, segments: 12);
                }
            }
        }

        /// <summary>
        /// Rotation that points the fingers along <paramref name="fingers"/> with the palm facing <paramref name="palm"/>
        /// (world directions) for a hand of this side.
        /// </summary>
        public static Quaternion Orient(Vector3 fingers, Vector3 palm, float side)
        {
            Vector3 y = -fingers.normalized;
            Vector3 x = Vector3.ProjectOnPlane(-side * palm, y);
            if (x.sqrMagnitude < 1e-6f) x = Vector3.ProjectOnPlane(Vector3.right, y);
            x.Normalize();
            Vector3 z = Vector3.Cross(x, y);
            return Quaternion.LookRotation(z, y);
        }
    }
}
