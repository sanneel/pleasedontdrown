using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// Builds one vertex-coloured skinned mesh out of simple rounded shapes. Every shape is attached rigidly to one
    /// bone (weight 1) and described in that bone's space; the kit places it in bind pose. Shapes are lathed
    /// (revolved profiles), so capsules, domes, discs, cones and rings all come from the same code.
    /// </summary>
    public sealed class AvatarMeshKit
    {
        private readonly List<Vector3> _vertices = new();
        private readonly List<Color32> _colors = new();
        private readonly List<BoneWeight> _weights = new();
        private readonly List<int> _triangles = new();
        private readonly List<Vector2> _profile = new();

        private Matrix4x4 _boneToMesh = Matrix4x4.identity;
        private int _bone;

        public int VertexCount => _vertices.Count;

        /// <summary>Following shapes belong to this bone. <paramref name="boneToMesh"/> is the bone's bind pose in mesh space.</summary>
        public void SetBone(int index, Matrix4x4 boneToMesh)
        {
            _bone = index;
            _boneToMesh = boneToMesh;
        }

        // ------------------------------------------------------------------ shapes (bone space)

        /// <summary>Ellipsoid with the given radii, centred at <paramref name="center"/>.</summary>
        public void Ellipsoid(Vector3 center, Vector3 radii, Color color, Quaternion? rotation = null, int segments = 12, int rings = 8)
        {
            _profile.Clear();
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * 0.5f - Mathf.PI * i / rings; // +90 (top) .. -90 (bottom)
                _profile.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            Lathe(_profile, segments, Matrix4x4.TRS(center, rotation ?? Quaternion.identity, radii), color);
        }

        /// <summary>A limb segment: rounded capsule from its origin down local -Y for <paramref name="length"/>, tapering r0 -> r1.</summary>
        public void Limb(float length, float r0, float r1, Color color, Vector3? origin = null, Quaternion? rotation = null,
            int segments = 10, Vector2? crossSection = null)
        {
            _profile.Clear();
            const int cap = 4;
            for (int i = 0; i <= cap; i++)
            {
                float a = Mathf.PI * 0.5f * (1f - i / (float)cap);
                _profile.Add(new Vector2(r0 * Mathf.Cos(a), r0 * Mathf.Sin(a)));
            }
            for (int i = 0; i <= cap; i++)
            {
                float a = -Mathf.PI * 0.5f * i / cap;
                _profile.Add(new Vector2(r1 * Mathf.Cos(a), -length + r1 * Mathf.Sin(a)));
            }
            Vector2 cs = crossSection ?? Vector2.one;
            Lathe(_profile, segments, Matrix4x4.TRS(origin ?? Vector3.zero, rotation ?? Quaternion.identity, new Vector3(cs.x, 1f, cs.y)), color);
        }

        /// <summary>Upper part of a sphere (a cap/hair shell). <paramref name="coverDegrees"/> 90 = hemisphere, 180 = full.</summary>
        public void Dome(Vector3 center, Vector3 radii, float coverDegrees, Color color, Quaternion? rotation = null, int segments = 14)
        {
            _profile.Clear();
            int rings = Mathf.Max(3, Mathf.RoundToInt(coverDegrees / 15f));
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * 0.5f - coverDegrees * Mathf.Deg2Rad * i / rings;
                _profile.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
            // Close the rim with a slightly smaller inner ring so the shell has some thickness.
            Vector2 rim = _profile[^1];
            _profile.Add(new Vector2(rim.x * 0.92f, rim.y - 0.02f));
            _profile.Add(new Vector2(0f, rim.y - 0.02f));
            Lathe(_profile, segments, Matrix4x4.TRS(center, rotation ?? Quaternion.identity, radii), color);
        }

        /// <summary>Flat round disc (hat brims): radius, thickness, optional hole.</summary>
        public void Disc(Vector3 center, float radius, float thickness, Color color, Quaternion? rotation = null, float innerRadius = 0f, Vector2? scale = null, int segments = 18)
        {
            _profile.Clear();
            float h = thickness * 0.5f;
            _profile.Add(new Vector2(innerRadius, h));
            _profile.Add(new Vector2(radius, h));
            _profile.Add(new Vector2(radius, -h));
            _profile.Add(new Vector2(innerRadius, -h));
            if (innerRadius > 0f) _profile.Add(new Vector2(innerRadius, h)); // closes the inner wall
            Vector2 s = scale ?? Vector2.one;
            Lathe(_profile, segments, Matrix4x4.TRS(center, rotation ?? Quaternion.identity, new Vector3(s.x, 1f, s.y)), color);
        }

        /// <summary>Closed cylinder / cone frustum from y=0 up to y=height (r0 bottom, r1 top).</summary>
        public void Frustum(Vector3 center, float r0, float r1, float height, Color color, Quaternion? rotation = null, Vector2? scale = null, int segments = 14)
        {
            _profile.Clear();
            _profile.Add(new Vector2(0f, height));
            _profile.Add(new Vector2(r1, height));
            _profile.Add(new Vector2(r0, 0f));
            _profile.Add(new Vector2(0f, 0f));
            Vector2 s = scale ?? Vector2.one;
            Lathe(_profile, segments, Matrix4x4.TRS(center, rotation ?? Quaternion.identity, new Vector3(s.x, 1f, s.y)), color);
        }

        /// <summary>Ring around the local Y axis (headbands, arm floaties, lanyards).</summary>
        public void Torus(Vector3 center, float radius, float tube, Color color, Quaternion? rotation = null, Vector2? scale = null, int segments = 16, int tubeSegments = 6)
        {
            _profile.Clear();
            for (int i = 0; i <= tubeSegments; i++)
            {
                float a = -Mathf.PI * 2f * i / tubeSegments; // clockwise, like every profile
                _profile.Add(new Vector2(radius + tube * Mathf.Cos(a), tube * Mathf.Sin(a)));
            }
            Vector2 s = scale ?? Vector2.one;
            Lathe(_profile, segments, Matrix4x4.TRS(center, rotation ?? Quaternion.identity, new Vector3(s.x, 1f, s.y)), color);
        }

        public void Box(Vector3 center, Vector3 size, Color color, Quaternion? rotation = null)
        {
            Matrix4x4 m = _boneToMesh * Matrix4x4.TRS(center, rotation ?? Quaternion.identity, size);
            Color32 c = color;
            // 6 faces x 4 vertices, so the edges stay crisp after RecalculateNormals.
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (Vector3 f in n)
            {
                Vector3 u = Mathf.Abs(f.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(f, u);
                int start = _vertices.Count;
                Add(m.MultiplyPoint3x4((f - u - w) * 0.5f), c);
                Add(m.MultiplyPoint3x4((f + u - w) * 0.5f), c);
                Add(m.MultiplyPoint3x4((f + u + w) * 0.5f), c);
                Add(m.MultiplyPoint3x4((f - u + w) * 0.5f), c);
                _triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
        }

        // ------------------------------------------------------------------ core

        /// <summary>
        /// Revolves a (radius, y) profile around Y. Points with radius 0 become single pole vertices.
        /// Profiles must run clockwise around the cross-section (e.g. top to bottom down the outside).
        /// </summary>
        private void Lathe(List<Vector2> profile, int segments, Matrix4x4 local, Color color)
        {
            Matrix4x4 m = _boneToMesh * local;
            Color32 c = color;
            int count = profile.Count;
            var rowStart = new int[count];
            for (int r = 0; r < count; r++)
            {
                Vector2 p = profile[r];
                rowStart[r] = _vertices.Count;
                if (p.x <= 1e-5f)
                {
                    Add(m.MultiplyPoint3x4(new Vector3(0f, p.y, 0f)), c);
                    continue;
                }
                for (int s = 0; s < segments; s++)
                {
                    float a = Mathf.PI * 2f * s / segments;
                    Add(m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x)), c);
                }
            }

            // Profiles run clockwise around the shape's cross-section (down the outside), which faces outward;
            // a mirrored placement flips that.
            bool reverse = m.determinant < 0f;
            for (int r = 0; r < count - 1; r++)
            {
                bool poleA = profile[r].x <= 1e-5f, poleB = profile[r + 1].x <= 1e-5f;
                if (poleA && poleB) continue;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    int a0 = poleA ? rowStart[r] : rowStart[r] + s, a1 = poleA ? rowStart[r] : rowStart[r] + s1;
                    int b0 = poleB ? rowStart[r + 1] : rowStart[r + 1] + s, b1 = poleB ? rowStart[r + 1] : rowStart[r + 1] + s1;
                    if (!poleA) Tri(a0, a1, b0, reverse);
                    if (!poleB) Tri(a1, b1, b0, reverse);
                }
            }
        }

        private void Tri(int a, int b, int c, bool reverse)
        {
            if (reverse) { _triangles.Add(a); _triangles.Add(c); _triangles.Add(b); }
            else { _triangles.Add(a); _triangles.Add(b); _triangles.Add(c); }
        }

        private void Add(Vector3 position, Color32 color)
        {
            _vertices.Add(position);
            _colors.Add(color);
            _weights.Add(new BoneWeight { boneIndex0 = _bone, weight0 = 1f });
        }

        public Mesh ToMesh(string name, Matrix4x4[] bindposes, Mesh reuse = null)
        {
            Mesh mesh = reuse != null ? reuse : new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = _vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.boneWeights = _weights.ToArray();
            mesh.bindposes = bindposes;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
