using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>Editor-only placement for the first unrigged Meshy exports.</summary>
    internal static class MeshyArt
    {
        private const string DirectoryPath = "Assets/_Game/Art/Meshy";

        public static GameObject Place(string asset, Transform parent, float height, Vector3 feet, float yaw = 0f)
        {
            string path = $"{DirectoryPath}/{asset}.glb";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                if (File.Exists(path)) throw new InvalidOperationException($"GLB import failed: {path}");
                return null; // Keep the original primitives when a source asset is absent.
            }
            var go = Object.Instantiate(prefab, parent, false);
            go.name = "Meshy_" + asset;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var bounds = LocalBounds(go.transform);
            float scale = height / bounds.size.y;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localPosition = feet - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
            // Rotate around the normalized foot pivot, not the source file's arbitrary origin.
            var pivot = new GameObject(asset + "_Pivot").transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = feet;
            go.transform.SetParent(pivot, true);
            pivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            foreach (var renderer in go.GetComponentsInChildren<Renderer>())
                renderer.allowOcclusionWhenDynamic = false;
            return pivot.gameObject;
        }

        private static Bounds LocalBounds(Transform root)
        {
            var bounds = new Bounds();
            bool first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var b = filter.sharedMesh.bounds;
                var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            if (first || bounds.size.y < 0.001f) throw new InvalidOperationException("Meshy asset has no valid mesh bounds.");
            return bounds;
        }

        public static bool Player(GameObject root, Transform body, Transform head)
        {
            // Body already has the capsule scale. Normalize in root space first, then retain the transform.
            var model = Place("lifeguard", root.transform, 1.8f, Vector3.zero);
            if (model == null) return false;
            model.transform.SetParent(body, true);
            body.GetComponent<Renderer>().enabled = false;
            head.Find("Visor").gameObject.SetActive(false);
            head.Find("Cap").gameObject.SetActive(false);
            return true;
        }

        public static bool Tourist(GameObject root, Transform visual)
        {
            var model = Place("tourist_dad", visual, 1.8f, new Vector3(0f, -1.13f, 0f));
            if (model == null) return false;
            // These exports are posed static meshes, not rigged characters. Preserve their shape.
            // The existing invisible torso/limb bodies continue rescue and carry physics.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                if (!renderer.transform.IsChildOf(model.transform)) renderer.enabled = false;
            return true;
        }

        public static void Palms(Transform env)
        {
            Vector3[] spots = { new(-13f, 0f, 12f), new(-18f, 0f, 21f), new(13f, 0f, 19f),
                new(23f, 0f, 9f), new(-26f, 0f, 4f), new(28f, 0f, 24f) };
            for (int i = 0; i < spots.Length; i++)
            {
                var palm = Place("palm_tall", env, 6.5f + i % 3 * 0.65f, spots[i], i * 67f);
                if (palm == null) continue;
                // A narrow trunk proxy leaves the canopy open and avoids expensive mesh collision.
                var col = palm.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 2.4f, 0f);
                col.height = 4.8f;
                col.radius = 0.3f;
            }
        }

        public static bool Structure(string asset, Transform parent, float height)
        {
            var model = Place(asset, parent, height, Vector3.zero);
            if (model == null) return false;
            // Replace only the primitive structure. Interactables are added by the scene builder afterward.
            foreach (Transform child in parent.Cast<Transform>().ToArray())
                if (child != model.transform) Object.DestroyImmediate(child.gameObject);
            void Box(string name, Vector3 center, Vector3 size)
            {
                var proxy = new GameObject(name);
                proxy.transform.SetParent(parent, false);
                proxy.transform.localPosition = center;
                proxy.AddComponent<BoxCollider>().size = size;
            }
            if (asset == "tower")
            {
                // Measured against this GLB: open stilts, usable deck and a continuous stair ramp.
                Box("DeckCollision", new Vector3(0f, 2.38f, -1.15f), new Vector3(2.9f, 0.18f, 2.6f));
                foreach (float x in new[] { -1.25f, 1.25f })
                    foreach (float z in new[] { -2.2f, -0.15f })
                        Box("StiltCollision", new Vector3(x, 1.15f, z), new Vector3(0.22f, 2.3f, 0.22f));
                Box("CabinCollision", new Vector3(0f, 3.4f, -1.4f), new Vector3(2.05f, 1.9f, 1.65f));
                Vector3 low = new Vector3(0f, 0.04f, 2.35f), high = new Vector3(0f, 2.45f, 0f);
                var ramp = new GameObject("StairCollision").transform;
                ramp.SetParent(parent, false);
                ramp.localPosition = (low + high) * 0.5f - Vector3.up * 0.08f;
                ramp.localRotation = Quaternion.LookRotation(high - low, Vector3.up);
                ramp.gameObject.AddComponent<BoxCollider>().size = new Vector3(1.05f, 0.16f, Vector3.Distance(low, high));
            }
            else
            {
                var b = LocalBounds(parent);
                Box("ShackCollision", new Vector3(b.center.x, 1.5f, b.center.z),
                    new Vector3(b.size.x * 0.72f, 2.7f, b.size.z * 0.65f));
            }
            return true;
        }
    }
}
