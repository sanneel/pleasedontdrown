using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// The Kleptopus's arms drawn as one smooth tentacle each: a tapered tube along the arm's curve, rounded off at the
    /// tip, that leaves the body sideways and low like the model's own tentacles. The chain of cylinders under it keeps
    /// the hit zones (the working arms' outer half can be shot) but isn't drawn: drawn, it read as a separate creature's
    /// arm (gaps at every bend, flat stumps, and on the Tripo body it started outside the model).
    /// </summary>
    public partial class Creature
    {
        private const int TubeRings = 24, TubeSides = 10;
        // Where a working arm leaves the Tripo body (model space): under the head, inside the narrow waist (1.4 m up,
        // the model's front there is 0.9 m out), so the join is hidden.
        private const float TripoArmHeight = 1.4f, TripoArmOut = 0.55f;
        private Mesh[] _tentacles;
        private Renderer[] _tentacleDrawn;
        private Vector3[] _ring;

        private void BuildTentacles()
        {
            _tentacles = new Mesh[_arms.Length];
            _tentacleDrawn = new Renderer[_arms.Length];
            _ring = new Vector3[TubeRings * TubeSides + 1];
            var triangles = new int[(TubeRings - 1) * TubeSides * 6 + TubeSides * 3];
            int t = 0;
            for (int r = 0; r < TubeRings - 1; r++)
                for (int s = 0; s < TubeSides; s++)
                {
                    int a = r * TubeSides + s, b = r * TubeSides + (s + 1) % TubeSides, c = a + TubeSides, d = b + TubeSides;
                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = c;  // outward-facing
                    triangles[t++] = b; triangles[t++] = d; triangles[t++] = c;
                }
            int tip = TubeRings * TubeSides, last = (TubeRings - 1) * TubeSides;
            for (int s = 0; s < TubeSides; s++)
            {
                triangles[t++] = last + s; triangles[t++] = last + (s + 1) % TubeSides; triangles[t++] = tip;
            }
            for (int a = 0; a < _arms.Length; a++)
            {
                var go = new GameObject("Tentacle" + a);
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "Tentacle" + a };
                mesh.MarkDynamic();
                mesh.vertices = _ring;
                mesh.triangles = triangles;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var drawn = go.AddComponent<MeshRenderer>();
                if ((int)CreaturePaint.Purple < _paints.Length) drawn.sharedMaterial = _paints[(int)CreaturePaint.Purple];
                drawn.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                _tentacles[a] = mesh;
                _tentacleDrawn[a] = drawn;
                foreach (Transform seg in _arms[a]) seg.GetComponent<MeshRenderer>().enabled = false;
            }
        }

        private void ShowTentacle(int a, bool show)
        {
            if (_tentacleDrawn != null && a < _tentacleDrawn.Length) _tentacleDrawn[a].enabled = show;
        }

        /// <summary>Where arm <paramref name="a"/> leaves the body (world).</summary>
        private Vector3 ArmRoot(int a)
        {
            if (!_tripo) return transform.TransformPoint(_armRoots[a]);
            Vector3 flat = new Vector3(_armRoots[a].x, 0f, _armRoots[a].z).normalized;
            // It leaves the body on the side facing its tip (leaning toward its own side, so the two arms don't share a
            // root): an arm reaching behind used to come out of the front and run back through the body and the
            // model's own tentacles (Docs/18 C3).
            Vector3 toTip = _model.InverseTransformPoint(_armTips[a]);
            toTip.y = 0f;
            if (toTip.sqrMagnitude > 0.04f)
            {
                Vector3 lean = toTip.normalized + flat * 0.45f;
                if (lean.sqrMagnitude > 1e-4f) flat = lean.normalized;
            }
            return _model.TransformPoint(Vector3.up * TripoArmHeight + flat * TripoArmOut);
        }

        /// <summary>The arm's curve (world): out of the body sideways and a little down, arching up over to the tip.</summary>
        private static Vector3 ArmCurve(Vector3 root, Vector3 outward, Vector3 tip, float k)
        {
            float reach = Vector3.Distance(root, tip);
            Vector3 p1 = root + outward * Mathf.Clamp(reach * 0.35f, 0.8f, 2.2f) - Vector3.up * 0.3f;
            Vector3 p2 = tip + Vector3.up * Mathf.Clamp(reach * 0.3f, 0.5f, 3f) - (tip - root).normalized * Mathf.Min(1.2f, reach * 0.2f);
            float u = 1f - k;
            return u * u * u * root + 3f * u * u * k * p1 + 3f * u * k * k * p2 + k * k * k * tip;
        }

        /// <summary>Rebuilds arm <paramref name="a"/>'s tube: <paramref name="thick"/> across at the root, a fine point at the tip.</summary>
        private void DrawTentacle(int a, Vector3 root, Vector3 outward, Vector3 tip, float thick)
        {
            if (_tentacles == null || !_tentacleDrawn[a].enabled) return;
            Vector3 up = Vector3.up;
            Vector3 prev = ArmCurve(root, outward, tip, 0f);
            for (int r = 0; r < TubeRings; r++)
            {
                float k = r / (float)(TubeRings - 1);
                Vector3 p = ArmCurve(root, outward, tip, k);
                Vector3 ahead = ArmCurve(root, outward, tip, Mathf.Min(1f, k + 0.02f)) - ArmCurve(root, outward, tip, Mathf.Max(0f, k - 0.02f));
                Vector3 along = ahead.sqrMagnitude > 1e-8f ? ahead.normalized : (p - prev).normalized;
                // Carry the ring's frame along the curve (no twisting where the arm turns over).
                Vector3 side = Vector3.Cross(up, along);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.Cross(Vector3.forward, along);
                side.Normalize();
                up = Vector3.Cross(along, side);
                float radius = thick * 0.5f * Mathf.Lerp(1f, 0.22f, k * k);
                for (int s = 0; s < TubeSides; s++)
                {
                    float angle = s * Mathf.PI * 2f / TubeSides;
                    Vector3 w = p + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius;
                    _ring[r * TubeSides + s] = transform.InverseTransformPoint(w);
                }
                prev = p;
            }
            Vector3 end = ArmCurve(root, outward, tip, 1f);
            Vector3 dir = (end - ArmCurve(root, outward, tip, 0.97f)).normalized;
            _ring[TubeRings * TubeSides] = transform.InverseTransformPoint(end + dir * thick * 0.12f);
            Mesh mesh = _tentacles[a];
            mesh.vertices = _ring;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
