using System.Collections.Generic;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// Every character's body as solid shapes, so a hand stops at somebody else's skin instead of sinking into them
    /// (a punch lands on the face, CPR hands rest on the chest, a hand held out meets the arm in front of it).
    /// Each body part is an oval tube along its bone (pelvis, belly, chest, neck, arms, legs) plus an egg for the
    /// head, measured once from the mesh actually skinned to it, so the shapes fit generated tourists, round dads
    /// and the goofy lifeguard alike. Pure maths on the bones: no colliders, no physics layers.
    /// </summary>
    public static class BodySpace
    {
        private static readonly List<AvatarRig> _rigs = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _rigs.Clear();
            _shapes.Clear();
        }

        internal static void Add(AvatarRig rig) { if (!_rigs.Contains(rig)) _rigs.Add(rig); }
        internal static void Remove(AvatarRig rig) => _rigs.Remove(rig);

        public static IReadOnlyList<AvatarRig> Rigs => _rigs;

        /// <summary>
        /// Moves <paramref name="point"/> (the centre of something <paramref name="radius"/> round, e.g. a palm) out
        /// of every body near it except <paramref name="ignore"/>'s own. True when it had to move.
        /// </summary>
        public static bool PushOut(ref Vector3 point, float radius, AvatarRig ignore, AvatarRig ignoreToo = null)
        {
            bool moved = false;
            for (int pass = 0; pass < 2; pass++)
            {
                bool hit = false;
                for (int i = 0; i < _rigs.Count; i++)
                {
                    AvatarRig rig = _rigs[i];
                    // (Plain reference checks: Unity's == costs a native call, and this runs for every hand of everybody.
                    // Rigs leave the list in OnDisable, so a listed rig is alive.)
                    if (ReferenceEquals(rig, ignore) || ReferenceEquals(rig, ignoreToo) || !rig.IsBuilt) continue;
                    // Broad phase: a whole body fits in 1.4 m of its hips (lying down, arms up).
                    Transform hips = rig[Bone.Hips];
                    if (ReferenceEquals(hips, null)) continue;
                    float reach = 1.4f * rig.Scale + radius;
                    if ((hips.position - point).sqrMagnitude > reach * reach) continue;
                    if (!rig.Renderer || !rig.Renderer.enabled || !rig.gameObject.activeInHierarchy) continue;
                    Shape shape = ShapeOf(rig);
                    if (shape == null) continue;
                    hit |= shape.PushOut(rig, ref point, radius);
                }
                moved |= hit;
                if (!hit) break;
            }
            return moved;
        }

        /// <summary>True when the point (with that radius) is inside somebody other than <paramref name="ignore"/>.</summary>
        public static bool Inside(Vector3 point, float radius, AvatarRig ignore, AvatarRig ignoreToo = null)
        {
            Vector3 p = point;
            return PushOut(ref p, radius, ignore, ignoreToo) && (p - point).sqrMagnitude > 0.0001f;
        }

        // ------------------------------------------------------------------ shapes

        private static readonly Dictionary<Mesh, Shape> _shapes = new();

        /// <summary>The rig's shapes (measured from its mesh the first time; code-built meshes again after a rebuild).</summary>
        internal static Shape ShapeOf(AvatarRig rig)
        {
            Mesh mesh = rig.Renderer != null ? rig.Renderer.sharedMesh : null;
            if (mesh == null) return null;
            if (_shapes.TryGetValue(mesh, out Shape shape) && shape.Version == rig.BuildVersion) return shape;
            shape = mesh.isReadable ? Shape.Measure(mesh) : null;
            if (shape != null) shape.Version = rig.BuildVersion;
            _shapes[mesh] = shape;
            return shape;
        }

        /// <summary>Forget a mesh that is about to be destroyed or rebuilt.</summary>
        internal static void Forget(Mesh mesh)
        {
            if (mesh != null) _shapes.Remove(mesh);
        }

        // Tube bones and the bone each one runs to.
        private static readonly (Bone bone, Bone to)[] Tubes =
        {
            (Bone.Hips, Bone.Spine), (Bone.Spine, Bone.Chest), (Bone.Chest, Bone.Neck), (Bone.Neck, Bone.Head),
            (Bone.UpperArmL, Bone.ForearmL), (Bone.ForearmL, Bone.HandL), (Bone.UpperArmR, Bone.ForearmR), (Bone.ForearmR, Bone.HandR),
            (Bone.ThighL, Bone.ShinL), (Bone.ShinL, Bone.FootL), (Bone.ThighR, Bone.ShinR), (Bone.ShinR, Bone.FootR),
        };

        /// <summary>The tube a vertex skinned mostly to this bone belongs to (-1: none, e.g. hands, eyes, the head).</summary>
        private static int TubeOf(int bone)
        {
            switch ((Bone)bone)
            {
                case Bone.ShoulderL: case Bone.ShoulderR: return 2; // the chest
                // (Not the bust: the chest tube is the ribcage, so CPR hands placed under the bust stay where they are.)
                case Bone.BustL: case Bone.BustR: return -1;
            }
            for (int i = 0; i < Tubes.Length; i++) if ((int)Tubes[i].bone == bone) return i;
            return -1;
        }

        internal sealed class Shape
        {
            public int Version;

            // Per tube, in its bone's space: the axis from the bone to the next joint, where along it the body part
            // starts and ends (fractions), across it the two directions measured, and the half sizes: sideways,
            // to the front, to the back.
            private readonly Vector3[] _axis = new Vector3[Tubes.Length];
            private readonly Vector3[] _side = new Vector3[Tubes.Length];
            private readonly Vector3[] _front = new Vector3[Tubes.Length];
            private readonly float[] _t0 = new float[Tubes.Length], _t1 = new float[Tubes.Length];
            private readonly float[] _half = new float[Tubes.Length], _ahead = new float[Tubes.Length], _behind = new float[Tubes.Length];
            private readonly bool[] _has = new bool[Tubes.Length];

            // The head (head-bone space): an egg with its own centre and half sizes.
            private Vector3 _headCentre;
            private float _headX, _headUp, _headDown, _headFront, _headBack;
            private bool _hasHead;

            public static Shape Measure(Mesh mesh)
            {
                Matrix4x4[] bind = mesh.bindposes;
                if (bind.Length < (int)Bone.Count) return null;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                if (weights.Length != vertices.Length || vertices.Length == 0) return null;
                var shape = new Shape();

                // Every tube's frame in its bone's space.
                var axisLength = new float[Tubes.Length];
                for (int k = 0; k < Tubes.Length; k++)
                {
                    int b = (int)Tubes[k].bone, to = (int)Tubes[k].to;
                    Vector3 end = bind[b].MultiplyPoint3x4(bind[to].inverse.MultiplyPoint3x4(Vector3.zero));
                    axisLength[k] = end.magnitude;
                    if (axisLength[k] < 1e-4f) continue;
                    Vector3 axis = end / axisLength[k];
                    Vector3 front = Vector3.ProjectOnPlane(bind[b].MultiplyVector(Vector3.forward), axis);
                    if (front.sqrMagnitude < 1e-6f) front = Vector3.ProjectOnPlane(Vector3.forward, axis);
                    front.Normalize();
                    shape._axis[k] = end; // (full length: t = 1 at the next joint)
                    shape._front[k] = front;
                    shape._side[k] = Vector3.Cross(axis, front).normalized;
                }

                var along = new List<float>[Tubes.Length];
                var side = new List<float>[Tubes.Length];
                var ahead = new List<float>[Tubes.Length];
                var behind = new List<float>[Tubes.Length];
                for (int k = 0; k < Tubes.Length; k++) { along[k] = new(); side[k] = new(); ahead[k] = new(); behind[k] = new(); }
                var head = new List<Vector3>();
                int headBone = (int)Bone.Head;

                for (int v = 0; v < vertices.Length; v++)
                {
                    BoneWeight w = weights[v];
                    int b = w.boneIndex0;
                    if (b == headBone && w.weight0 > 0.5f) { head.Add(bind[headBone].MultiplyPoint3x4(vertices[v])); continue; }
                    int k = TubeOf(b);
                    if (k < 0 || axisLength[k] < 1e-4f || w.weight0 < 0.4f) continue;
                    int tubeBone = (int)Tubes[k].bone;
                    Vector3 local = bind[tubeBone].MultiplyPoint3x4(vertices[v]);
                    Vector3 axis = shape._axis[k];
                    float t = Vector3.Dot(local, axis) / axis.sqrMagnitude;
                    Vector3 off = local - axis * t;
                    along[k].Add(t);
                    side[k].Add(Mathf.Abs(Vector3.Dot(off, shape._side[k])));
                    float f = Vector3.Dot(off, shape._front[k]);
                    if (f >= 0f) ahead[k].Add(f); else behind[k].Add(-f);
                }

                for (int k = 0; k < Tubes.Length; k++)
                {
                    if (along[k].Count < 12) continue;
                    // A little inside the skin (85th percentile), so a hand that is on the skin is not shoved off it.
                    shape._half[k] = Percentile(side[k], 0.85f);
                    shape._ahead[k] = ahead[k].Count > 3 ? Percentile(ahead[k], 0.85f) : shape._half[k];
                    shape._behind[k] = behind[k].Count > 3 ? Percentile(behind[k], 0.85f) : shape._half[k];
                    shape._t0[k] = Percentile(along[k], 0.03f);
                    shape._t1[k] = Percentile(along[k], 0.97f);
                    shape._has[k] = shape._half[k] > 0.005f;
                }

                if (head.Count > 12)
                {
                    var xs = new List<float>(head.Count);
                    var ys = new List<float>(head.Count);
                    var zs = new List<float>(head.Count);
                    foreach (Vector3 p in head) { xs.Add(p.x); ys.Add(p.y); zs.Add(p.z); }
                    float x0 = Percentile(xs, 0.03f), x1 = Percentile(xs, 0.97f);
                    float y0 = Percentile(ys, 0.02f), y1 = Percentile(ys, 0.98f);
                    float z0 = Percentile(zs, 0.03f), z1 = Percentile(zs, 0.97f);
                    shape._headCentre = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
                    shape._headX = (x1 - x0) * 0.5f;
                    shape._headUp = y1 - shape._headCentre.y;
                    shape._headDown = shape._headCentre.y - y0;
                    shape._headFront = z1 - shape._headCentre.z;
                    shape._headBack = shape._headCentre.z - z0;
                    shape._hasHead = shape._headX > 0.02f;
                }
                return shape;
            }

            /// <summary>(From at most 400 evenly spread samples: sorting whole meshes' worth froze the first frame a body was met.)</summary>
            private static float Percentile(List<float> values, float q)
            {
                int stride = Mathf.Max(1, values.Count / 400);
                var picked = new float[(values.Count + stride - 1) / stride];
                for (int i = 0, j = 0; i < values.Count && j < picked.Length; i += stride, j++) picked[j] = values[i];
                System.Array.Sort(picked);
                return picked[Mathf.Clamp(Mathf.RoundToInt(q * (picked.Length - 1)), 0, picked.Length - 1)];
            }

            public bool PushOut(AvatarRig rig, ref Vector3 point, float radius)
            {
                bool hit = false;
                for (int k = 0; k < Tubes.Length; k++)
                {
                    if (!_has[k]) continue;
                    Transform bone = rig[Tubes[k].bone];
                    if (ReferenceEquals(bone, null)) continue;
                    Vector3 local = bone.InverseTransformPoint(point);
                    Vector3 axis = _axis[k];
                    float t = Vector3.Dot(local, axis) / axis.sqrMagnitude;
                    if (t < _t0[k] || t > _t1[k]) continue;
                    Vector3 off = local - axis * t;
                    float x = Vector3.Dot(off, _side[k]), z = Vector3.Dot(off, _front[k]);
                    float rx = _half[k] + radius, rz = (z >= 0f ? _ahead[k] : _behind[k]) + radius;
                    float q = x * x / (rx * rx) + z * z / (rz * rz);
                    if (q >= 1f) continue;
                    // Out to the oval's rim, straight away from the bone (or forward, from dead centre).
                    Vector3 rim;
                    if (q < 1e-6f) rim = _front[k] * (_ahead[k] + radius);
                    else
                    {
                        float scale = 1f / Mathf.Sqrt(q);
                        rim = (_side[k] * x + _front[k] * z) * scale;
                    }
                    point = bone.TransformPoint(axis * t + rim);
                    hit = true;
                }
                if (_hasHead)
                {
                    Transform head = rig[Bone.Head];
                    if (!ReferenceEquals(head, null))
                    {
                        Vector3 off = head.InverseTransformPoint(point) - _headCentre;
                        float rx = _headX + radius, ry = (off.y >= 0f ? _headUp : _headDown) + radius, rz = (off.z >= 0f ? _headFront : _headBack) + radius;
                        float q = off.x * off.x / (rx * rx) + off.y * off.y / (ry * ry) + off.z * off.z / (rz * rz);
                        if (q < 1f)
                        {
                            Vector3 rim = q < 1e-6f ? new Vector3(0f, 0f, rz) : off / Mathf.Sqrt(q);
                            point = head.TransformPoint(_headCentre + rim);
                            hit = true;
                        }
                    }
                }
                return hit;
            }
        }
    }
}
