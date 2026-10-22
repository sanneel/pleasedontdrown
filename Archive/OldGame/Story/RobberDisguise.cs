using PleaseDontDrown.Avatars;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// What makes the thief read as a thief from across the beach: a black beanie pulled down to the eyebrows and a
    /// red bandana over his nose and mouth, knotted at the back. Built to fit whichever head the body has (measured
    /// from the head bone, the eyes and the mouth), so it works on any generated model and on the code-built one.
    /// </summary>
    public static class RobberDisguise
    {
        private static readonly Color Wool = new(0.09f, 0.09f, 0.11f), WoolFold = new(0.17f, 0.17f, 0.2f);
        private static readonly Color Cloth = new(0.7f, 0.12f, 0.11f), ClothDark = new(0.5f, 0.07f, 0.08f);

        // One mesh per head it has been fitted to (the thief comes back with the same head: nothing to build twice or leak).
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> _fitted = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _fitted.Clear();

        /// <summary>The disguise, on <paramref name="rig"/>'s head bone.</summary>
        public static GameObject Create(AvatarRig rig)
        {
            Transform head = rig[AvatarRig.Bone.Head];
            int key = System.HashCode.Combine(Mathf.RoundToInt(rig.HeadTop * 1000f), Mathf.RoundToInt(rig.HeadHalfWidth * 1000f),
                Mathf.RoundToInt(rig.HeadFront * 1000f), Mathf.RoundToInt(rig[AvatarRig.Bone.EyeL].localPosition.y * 1000f),
                Mathf.RoundToInt(rig[AvatarRig.Bone.Mouth].localPosition.y * 1000f));
            if (!_fitted.TryGetValue(key, out Mesh mesh) || mesh == null) _fitted[key] = mesh = Fit(rig);
            var disguise = new GameObject("RobberDisguise");
            disguise.AddComponent<MeshFilter>().sharedMesh = mesh;
            disguise.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
            disguise.transform.SetParent(head, false);
            return disguise;
        }

        private static Mesh Fit(AvatarRig rig)
        {
            Vector3 eyeL = rig[AvatarRig.Bone.EyeL].localPosition, eyeR = rig[AvatarRig.Bone.EyeR].localPosition;
            Vector3 mouth = rig[AvatarRig.Bone.Mouth].localPosition;
            float eyeY = (eyeL.y + eyeR.y) * 0.5f;
            // The skull as an ellipse seen from above: as wide as the head, from the face to the back of it.
            float rx = rig.HeadHalfWidth, rz = (rig.HeadFront + rig.HeadBack) * 0.5f;
            float cz = (rig.HeadFront - rig.HeadBack) * 0.5f;
            float face = Mathf.Max(eyeL.z, mouth.z); // how far forward the face itself is (the nose sticks out further)

            var kit = new AvatarMeshKit();
            kit.SetBone(0, Matrix4x4.identity);

            // Beanie: pulled down to the eyebrows, a little slack above the skull, its edge turned up.
            float brow = eyeY + Mathf.Max(0.03f, (rig.HeadTop - eyeY) * 0.3f);
            float h = rig.HeadTop + 0.05f - brow;
            var around = new Vector2(rx + 0.005f, rz + 0.005f);
            kit.Turned(new[]
            {
                new Vector2(0f, h), new Vector2(0.35f, h * 0.985f), new Vector2(0.65f, h * 0.9f), new Vector2(0.86f, h * 0.72f),
                new Vector2(0.97f, h * 0.45f), new Vector2(1f, h * 0.2f), new Vector2(1f, 0.03f)
            }, new Vector3(0f, brow, cz), around, Wool, segments: 18);
            kit.Turned(new[]
            {
                new Vector2(1f, 0.036f), new Vector2(1.05f, 0.033f), new Vector2(1.06f, 0.012f), new Vector2(1.05f, -0.01f), new Vector2(0.97f, -0.012f)
            }, new Vector3(0f, brow, cz), around, WoolFold, segments: 18);

            // Bandana: tied round the head under the eyes, over the nose and mouth, gathering in to a point under the chin.
            float top = eyeY - Mathf.Max(0.022f, (eyeY - mouth.y) * 0.3f);
            float chin = mouth.y - Mathf.Max(0.04f, (eyeY - mouth.y) * 0.5f);
            float reach = Mathf.Max(rz + 0.004f, face - cz + 0.012f); // out to the lips at the front
            kit.Turned(new[]
            {
                new Vector2(0.97f, top + 0.004f), new Vector2(1f, top - 0.006f), new Vector2(1f, mouth.y), new Vector2(0.8f, chin),
                new Vector2(0.3f, chin - 0.05f), new Vector2(0f, chin - 0.06f)
            }, new Vector3(0f, 0f, cz), new Vector2(rx + 0.004f, reach), Cloth, segments: 18);
            // The knot and its two tails at the back.
            float back = cz - reach - 0.008f, knotY = top - 0.02f;
            kit.Ellipsoid(new Vector3(0f, knotY, back), new Vector3(0.024f, 0.02f, 0.018f), ClothDark, null, 8, 6);
            foreach (float side in new[] { -1f, 1f })
                kit.Ellipsoid(new Vector3(side * 0.028f, knotY - 0.045f, back - 0.008f), new Vector3(0.014f, 0.05f, 0.008f), ClothDark, Quaternion.Euler(-10f, 0f, side * 24f), 8, 6);

            return kit.ToMesh("RobberDisguise", new[] { Matrix4x4.identity });
        }
    }
}
