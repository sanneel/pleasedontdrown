using PleaseDontDrown.Avatars;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The thief's backpack. The model is the Meshy one (cleaned by ArtSource/Tools/polish_prop.py, saved by the
    /// scene builder as Resources/RobberBackpack: 0.46 m tall, standing on its base, its flat strap side toward +Z);
    /// without it, a lumpy code-built bag.
    /// </summary>
    public static class RobberBag
    {
        public const string ResourceName = "RobberBackpack";
        private const float ModelSize = 1.25f;   // a stuffed 58 cm rucksack: reads as "the loot" from across the beach

        public static GameObject Create()
        {
            var prefab = Resources.Load<GameObject>(ResourceName);
            if (prefab != null)
            {
                GameObject model = Object.Instantiate(prefab);
                model.name = "Bag";
                return model;
            }

            var kit = new AvatarMeshKit();
            kit.SetBone(0, Matrix4x4.identity);
            Color canvas = new Color(0.36f, 0.27f, 0.18f), strap = new Color(0.16f, 0.12f, 0.08f);
            kit.Ellipsoid(Vector3.zero, new Vector3(0.17f, 0.22f, 0.11f), canvas, segments: 12, rings: 8);
            kit.Ellipsoid(new Vector3(0f, 0.15f, -0.02f), new Vector3(0.15f, 0.07f, 0.1f), canvas * 0.85f, segments: 10, rings: 6);
            kit.Box(new Vector3(0f, -0.02f, -0.11f), new Vector3(0.16f, 0.12f, 0.04f), canvas * 0.9f);
            foreach (float side in new[] { -1f, 1f })
                kit.Box(new Vector3(0.1f * side, 0.05f, 0.1f), new Vector3(0.03f, 0.34f, 0.02f), strap);
            var bag = new GameObject("Bag");
            bag.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("RobberBag", new[] { Matrix4x4.identity });
            bag.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
            return bag;
        }

        /// <summary>Hangs it on the back, just clear of the shoulder blades.</summary>
        public static void Wear(GameObject bag, AvatarRig rig)
        {
            bool codeBuilt = bag.GetComponent<MeshFilter>() != null; // that one's origin is its middle
            bag.transform.SetParent(rig[AvatarRig.Bone.Chest], false);
            bag.transform.localPosition = (codeBuilt ? new Vector3(0f, 0.05f, -0.22f) : new Vector3(0f, -0.26f, -0.3f)) * rig.Scale;
            bag.transform.localRotation = Quaternion.identity;
            if (!codeBuilt) bag.transform.localScale = Vector3.one * ModelSize;
        }
    }
}
