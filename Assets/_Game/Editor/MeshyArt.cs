using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>Editor-only placement for the Meshy props (structures and palms). Characters are built in code (Avatars).</summary>
    internal static class MeshyArt
    {
        private const string DirectoryPath = "Assets/_Game/Art/Meshy";

        /// <param name="widen">Extra stretch along the model's x (sideways), e.g. a roomier tower cabin.</param>
        public static GameObject Place(string asset, Transform parent, float height, Vector3 feet, float yaw = 0f, float widen = 1f)
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
            var scales = new Vector3(scale * widen, scale, scale);
            go.transform.localScale = scales;
            go.transform.localPosition = feet - Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), scales);
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

        /// <summary>Palm spots (x, z) around the island; the first six frame the station.</summary>
        public static readonly Vector2[] PalmSpots =
        {
            new(-13f, 12f), new(-18f, 21f), new(13f, 19f), new(23f, 11f), new(-26f, 8f), new(28f, 24f),
            new(-40f, 16f), new(-52f, 31f), new(-34f, 44f), new(-12f, 52f), new(8f, 38f), new(20f, 58f),
            new(44f, 20f), new(58f, 37f), new(40f, 47f), new(-64f, 18f)
        };

        public static System.Collections.Generic.List<GameObject> Palms(Transform env, Func<float, float, float> groundHeight)
        {
            var palms = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < PalmSpots.Length; i++)
            {
                Vector2 s = PalmSpots[i];
                // Sink the foot a little so the trunk never floats over a dune slope.
                var spot = new Vector3(s.x, groundHeight(s.x, s.y) - 0.15f, s.y);
                var palm = Place("palm_tall", env, 6.5f + i % 3 * 0.65f, spot, i * 67f);
                if (palm == null) continue;
                // A narrow trunk proxy leaves the canopy open and avoids expensive mesh collision.
                var col = palm.AddComponent<CapsuleCollider>();
                col.center = new Vector3(0f, 2.4f, 0f);
                col.height = 4.8f;
                col.radius = 0.3f;
                palms.Add(palm);
            }
            return palms;
        }

        /// <summary>Where a structure's door goes (structure-local): hinge at the bottom of one side, leaf toward the other.</summary>
        public struct DoorSpec
        {
            public Vector3 Hinge;       // bottom of the hinge side, on the wall plane
            public float Width, Height;
            public float LeafDirection; // +1: the leaf extends toward local +x from the hinge, -1: toward -x
            public float WallFacing;    // +1: the wall's outside faces local +z, -1: -z
        }

        /// <summary>
        /// The lifeguard shack, big enough to walk into (the GLB's door is cut out; a real door goes in the gap).
        /// Measurements are from orthographic renders of this GLB at the 4 m height, scaled by <paramref name="scale"/>.
        /// </summary>
        public static bool Shack(Transform parent, float scale, out DoorSpec door)
        {
            door = default;
            var model = Place("station_rusty", parent, 4f * scale, Vector3.zero);
            if (model == null) return false;
            ClearPrimitives(parent, model);
            float k = scale;
            // Walls x -0.75..1.30, z -1.18..0.65, floor 0.575, eaves 2.55; door x -0.16..0.40, y 0.61..2.25 (at 4 m).
            float x0 = -0.75f * k, x1 = 1.30f * k, z0 = -1.18f * k, z1 = 0.65f * k, floor = 0.575f * k, eaves = 2.55f * k;
            float dx0 = -0.16f * k, dx1 = 0.40f * k, dTop = 2.25f * k;
            CutOpening(model, parent, new Bounds(new Vector3((dx0 + dx1) * 0.5f, (floor + 0.03f + dTop + 0.03f) * 0.5f, z1),
                new Vector3(dx1 - dx0 + 0.05f, dTop - floor + 0.02f, 0.5f)), "station_rusty_door");

            Box(parent, "FloorCollision", new Vector3((x0 + x1) * 0.5f, floor * 0.5f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, floor, z1 - z0));
            Walls(parent, x0, x1, z0, z1, floor, eaves, dx0, dx1, dTop, frontIsPlusZ: true);
            Ramp(parent, "StepsCollision", new Vector3((dx0 + dx1) * 0.5f - 0.05f * k, 0f, 1.72f * k), new Vector3((dx0 + dx1) * 0.5f - 0.05f * k, floor, z1), 1.3f * k * 0.7f);
            door = new DoorSpec { Hinge = new Vector3(dx1, floor, z1), Width = dx1 - dx0, Height = dTop - floor, LeafDirection = -1f, WallFacing = 1f };
            return true;
        }

        /// <summary>
        /// The watch tower (turned to face the sea by its parent), scaled so its cabin fits a person and stretched
        /// sideways by <paramref name="widen"/> so a few lifeguards fit in it.
        /// Measured at 5.5 m: cabin x -0.98..0.96, z -1.95..-0.20, deck top 2.285, eaves 4.26; door x -0.70..-0.02, top 4.175;
        /// deck z -2.4..0.15, x -1.51..1.51; the ramp runs down toward +z.
        /// </summary>
        public static bool Tower(Transform parent, float scale, float widen, out DoorSpec door)
        {
            door = default;
            var model = Place("tower", parent, 5.5f * scale, Vector3.zero, widen: widen);
            if (model == null) return false;
            ClearPrimitives(parent, model);
            float k = scale, kx = scale * widen;
            float x0 = -0.98f * kx, x1 = 0.96f * kx, z0 = -1.95f * k, z1 = -0.20f * k, deck = 2.285f * k, eaves = 4.26f * k;
            float dx0 = -0.70f * kx, dx1 = -0.02f * kx, dTop = 4.175f * k;
            CutOpening(model, parent, new Bounds(new Vector3((dx0 + dx1) * 0.5f, (deck + 0.03f + dTop + 0.03f) * 0.5f, z1),
                new Vector3(dx1 - dx0 + 0.05f, dTop - deck + 0.02f, 0.4f)), "tower_door");

            Box(parent, "DeckCollision", new Vector3(0f, deck - 0.09f, -1.125f * k), new Vector3(3.02f * kx, 0.18f, 2.55f * k));
            foreach (float x in new[] { -1.25f * kx, 1.25f * kx })
                foreach (float z in new[] { -2.2f * k, -0.15f * k })
                    Box(parent, "StiltCollision", new Vector3(x, (deck - 0.18f) * 0.5f, z), new Vector3(0.22f, deck - 0.18f, 0.22f));
            Walls(parent, x0, x1, z0, z1, deck, eaves, dx0, dx1, dTop, frontIsPlusZ: true);
            Ramp(parent, "StairCollision", new Vector3(0f, 0.04f, 2.35f * k), new Vector3(0f, deck, 0.15f * k), 1.05f * kx);
            door = new DoorSpec { Hinge = new Vector3(dx0, deck, z1), Width = dx1 - dx0, Height = dTop - deck, LeafDirection = 1f, WallFacing = 1f };
            return true;
        }

        private static void ClearPrimitives(Transform parent, GameObject keep)
        {
            // Replace only the primitive stand-in. Interactables are added by the scene builder afterward.
            foreach (Transform child in parent.Cast<Transform>().ToArray())
                if (child.gameObject != keep) Object.DestroyImmediate(child.gameObject);
        }

        private static void Box(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var proxy = new GameObject(name);
            proxy.transform.SetParent(parent, false);
            proxy.transform.localPosition = center;
            proxy.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>Four thin collision walls around a room, the front one with a doorway.</summary>
        private static void Walls(Transform parent, float x0, float x1, float z0, float z1, float floor, float top,
            float doorX0, float doorX1, float doorTop, bool frontIsPlusZ)
        {
            const float t = 0.12f;
            float h = top - floor, y = floor + h * 0.5f;
            float front = frontIsPlusZ ? z1 : z0, back = frontIsPlusZ ? z0 : z1;
            Box(parent, "WallBack", new Vector3((x0 + x1) * 0.5f, y, back), new Vector3(x1 - x0 + t, h, t));
            Box(parent, "WallLeft", new Vector3(x0, y, (z0 + z1) * 0.5f), new Vector3(t, h, z1 - z0 + t));
            Box(parent, "WallRight", new Vector3(x1, y, (z0 + z1) * 0.5f), new Vector3(t, h, z1 - z0 + t));
            Box(parent, "WallFrontA", new Vector3((x0 + doorX0) * 0.5f, y, front), new Vector3(doorX0 - x0, h, t));
            Box(parent, "WallFrontB", new Vector3((doorX1 + x1) * 0.5f, y, front), new Vector3(x1 - doorX1, h, t));
            Box(parent, "WallLintel", new Vector3((doorX0 + doorX1) * 0.5f, (doorTop + top) * 0.5f, front), new Vector3(doorX1 - doorX0, top - doorTop, t));
            Box(parent, "Ceiling", new Vector3((x0 + x1) * 0.5f, top + 0.06f, (z0 + z1) * 0.5f), new Vector3(x1 - x0, 0.12f, z1 - z0));
        }

        private static void Ramp(Transform parent, string name, Vector3 low, Vector3 high, float width)
        {
            var ramp = new GameObject(name).transform;
            ramp.SetParent(parent, false);
            ramp.localPosition = (low + high) * 0.5f - Vector3.up * 0.08f;
            ramp.localRotation = Quaternion.LookRotation(high - low, Vector3.up);
            ramp.gameObject.AddComponent<BoxCollider>().size = new Vector3(width, 0.16f, Vector3.Distance(low, high));
        }

        /// <summary>
        /// Removes the model's triangles whose centre lies in <paramref name="box"/> (in <paramref name="space"/>'s
        /// coordinates) so there's a real hole to walk through. The cut mesh is saved next to the art.
        /// </summary>
        private static void CutOpening(GameObject model, Transform space, Bounds box, string assetName)
        {
            const string dir = "Assets/_Game/Art/Generated";
            Directory.CreateDirectory(dir);
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                Mesh source = filter.sharedMesh;
                Matrix4x4 toSpace = space.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                Vector3[] vertices = source.vertices;
                string path = $"{dir}/{assetName}.asset";
                var cut = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                bool isNew = cut == null;
                if (isNew) cut = new Mesh();
                cut.Clear();
                cut.name = assetName;
                cut.indexFormat = source.indexFormat;
                cut.vertices = vertices;
                cut.normals = source.normals;
                cut.tangents = source.tangents;
                cut.uv = source.uv;
                cut.colors = source.colors;
                cut.subMeshCount = source.subMeshCount;
                int removed = 0;
                for (int sm = 0; sm < source.subMeshCount; sm++)
                {
                    int[] tris = source.GetTriangles(sm);
                    var kept = new System.Collections.Generic.List<int>(tris.Length);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        Vector3 c = toSpace.MultiplyPoint3x4((vertices[tris[i]] + vertices[tris[i + 1]] + vertices[tris[i + 2]]) / 3f);
                        if (box.Contains(c)) { removed++; continue; }
                        kept.Add(tris[i]); kept.Add(tris[i + 1]); kept.Add(tris[i + 2]);
                    }
                    cut.SetTriangles(kept, sm);
                }
                cut.RecalculateBounds();
                if (isNew) AssetDatabase.CreateAsset(cut, path);
                EditorUtility.SetDirty(cut);
                filter.sharedMesh = cut;
                Debug.Log($"[Build] {assetName}: cut {removed} triangles for the doorway");
                break; // these GLBs are a single mesh
            }
        }
    }
}
