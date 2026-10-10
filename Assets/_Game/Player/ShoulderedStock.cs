using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Keeps a shouldered gun's stock on the body instead of in it, seen from outside: the gun slides forward along
    /// its barrel until the butt just touches the holder's real skin round the right shoulder (shoulder, chest,
    /// upper arm), whatever their build. The skin there is picked out of the body mesh once per character and
    /// skinned with the current pose each frame; each gun's butt is measured from its mesh once.
    /// </summary>
    public static class ShoulderedStock
    {
        private const float SkinRadius = 0.5f;   // skin this close to the right shoulder joint can meet the stock
        private const float StockDepth = 0.2f;   // how far along the gun from its butt counts as the stock
        private const float Reach = 0.45f;       // skin up to this far in front of the butt can be in the stock's way
        private const float Margin = 0.004f;
        private const float Deltoid = 0.1f;      // the arm's skin this close to its shoulder joint is the round of the shoulder

        private sealed class Skin
        {
            public Mesh Mesh;
            public Vector3[] Points;
            public BoneWeight[] Weights;
            public Matrix4x4[] Skinning;
            public Matrix4x4[] BindPoses;
        }

        private struct Butt
        {
            public float Back;                 // the butt's distance behind the item origin, along its forward
            public Vector2 Min, Max;           // the stock's cross-section (item's right, up)
        }

        private static readonly Dictionary<AvatarRig, Skin> Skins = new();
        private static readonly Dictionary<Item, Butt> Butts = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Skins.Clear();
            Butts.Clear();
        }

        /// <summary>How far forward along <paramref name="rotation"/> the gun at <paramref name="position"/> has to go for its stock to clear the body.</summary>
        public static float PushOut(Item item, AvatarRig rig, Vector3 position, Quaternion rotation)
        {
            if (item == null || rig == null) return 0f;
            Skin skin = SkinOf(rig);
            if (skin == null) return 0f;
            Butt butt = ButtOf(item);
            Transform[] bones = rig.Renderer.bones;
            for (int b = 0; b < bones.Length; b++)
                if (bones[b] != null) skin.Skinning[b] = bones[b].localToWorldMatrix * skin.BindPoses[b];
            Quaternion toGun = Quaternion.Inverse(rotation);
            float push = 0f;
            for (int i = 0; i < skin.Points.Length; i++)
            {
                Vector3 p = Skinned(skin, i);
                Vector3 local = toGun * (p - position);
                if (local.x < butt.Min.x - Margin || local.x > butt.Max.x + Margin || local.y < butt.Min.y - Margin || local.y > butt.Max.y + Margin) continue;
                float into = local.z + butt.Back; // how far in front of the butt (inside the stock) this bit of skin is
                if (into > -Margin && into < Reach) push = Mathf.Max(push, into + Margin);
            }
            return push;
        }

        private static Vector3 Skinned(Skin skin, int i)
        {
            BoneWeight w = skin.Weights[i];
            Vector3 v = skin.Points[i];
            Vector3 p = skin.Skinning[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0;
            if (w.weight1 > 0f) p += skin.Skinning[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1;
            if (w.weight2 > 0f) p += skin.Skinning[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2;
            if (w.weight3 > 0f) p += skin.Skinning[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
            return p;
        }

        private static int MainBone(BoneWeight w)
        {
            int best = w.boneIndex0;
            float most = w.weight0;
            if (w.weight1 > most) { best = w.boneIndex1; most = w.weight1; }
            if (w.weight2 > most) { best = w.boneIndex2; most = w.weight2; }
            if (w.weight3 > most) best = w.boneIndex3;
            return best;
        }

        private static Skin SkinOf(AvatarRig rig)
        {
            SkinnedMeshRenderer renderer = rig.Renderer;
            if (renderer == null || renderer.sharedMesh == null || !renderer.sharedMesh.isReadable) return null;
            Mesh mesh = renderer.sharedMesh;
            if (Skins.TryGetValue(rig, out Skin known) && known.Mesh == mesh) return known;

            // Every vertex, skinned as it is now; keep the ones near the right shoulder.
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Transform[] bones = renderer.bones;
            Matrix4x4[] bindPoses = mesh.bindposes;
            if (weights.Length != vertices.Length || bones.Length != bindPoses.Length) return null;
            var all = new Skin { Mesh = mesh, Skinning = new Matrix4x4[bones.Length], BindPoses = bindPoses, Points = vertices, Weights = weights };
            for (int b = 0; b < bones.Length; b++)
                if (bones[b] != null) all.Skinning[b] = bones[b].localToWorldMatrix * bindPoses[b];
            Vector3 shoulder = rig[AvatarRig.Bone.UpperArmR].position;
            // (Not the arms below the round of the shoulder: they hold the gun, and reaching for it they'd only push it
            // further away.)
            var holding = new bool[bones.Length];
            Transform armL = rig[AvatarRig.Bone.UpperArmL], armR = rig[AvatarRig.Bone.UpperArmR];
            for (int b = 0; b < bones.Length; b++)
                holding[b] = bones[b] != null && (armL != null && bones[b].IsChildOf(armL) || armR != null && bones[b].IsChildOf(armR));
            var points = new List<Vector3>();
            var kept = new List<BoneWeight>();
            for (int i = 0; i < vertices.Length; i++)
            {
                float fromShoulder = (Skinned(all, i) - shoulder).sqrMagnitude;
                if (fromShoulder > SkinRadius * SkinRadius) continue;
                if (holding[MainBone(weights[i])] && fromShoulder > Deltoid * Deltoid) continue;
                points.Add(vertices[i]);
                kept.Add(weights[i]);
            }
            var skin = new Skin { Mesh = mesh, Skinning = all.Skinning, BindPoses = bindPoses, Points = points.ToArray(), Weights = kept.ToArray() };
            Skins[rig] = skin;
            return skin;
        }

        private static Butt ButtOf(Item item)
        {
            if (Butts.TryGetValue(item, out Butt known)) return known;
            Transform t = item.transform;
            Quaternion toItem = Quaternion.Inverse(t.rotation);
            var points = new List<Vector3>();
            float back = 0f;
            foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.sharedMesh.isReadable || !filter.TryGetComponent(out Renderer shown) || !shown.enabled) continue;
                foreach (Vector3 v in filter.sharedMesh.vertices)
                {
                    Vector3 local = toItem * (filter.transform.TransformPoint(v) - t.position);
                    points.Add(local);
                    back = Mathf.Max(back, -local.z);
                }
            }
            var butt = new Butt { Back = back, Min = new Vector2(float.MaxValue, float.MaxValue), Max = new Vector2(float.MinValue, float.MinValue) };
            foreach (Vector3 p in points)
            {
                if (p.z + back > StockDepth) continue;
                butt.Min = Vector2.Min(butt.Min, p);
                butt.Max = Vector2.Max(butt.Max, p);
            }
            if (butt.Min.x > butt.Max.x) butt.Min = butt.Max = Vector2.zero;
            Butts[item] = butt;
            return butt;
        }
    }
}
