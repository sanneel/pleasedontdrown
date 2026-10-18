using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Component.Transforming;
using FishNet.Object;
using PleaseDontDrown.Dev;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 4, "Finders Keepers" (Docs/Prompt-Shako-Chapter-4-Finale.md): Sandy's private island and the head office of
    /// her rival lifeguard company, out in the open sea east of the hotel island (centre (560, -400), 200 x 200 m; 250 m
    /// from the hotel island's east end, 330 m from Skull Cove). Daylight, too clean, too orange. From the west jetty
    /// inland: the jetty and the company's lifeguard tower; the lagoon beach on the south-west (a wide wading shelf, then
    /// a sudden drop to deep water: where the rescue race happens); the warehouse hangar ("FINDERS KEEPERS" on its roof)
    /// full of everything "lost" on islands 1-3; the smoothie bar ("Blackout Berry"); the helipad and the company's
    /// helicopter; a cliff path up to the plateau with Sandy's villa and a giant golden statue of her; her private jetty
    /// at the foot of the east cliff, over deep water (the final fight); the little police dock on the north shore.
    /// Its own ground mesh (sand, lawns, paving, cliffs: island 3's ground material) covers the island and its seabed out
    /// to ~70 m (past it the open sea has no bottom, like everywhere off the grid). Greybox: clean primitives at their
    /// final size and place, a few existing props; Tripo models replace them once Shako says yes.
    /// Everything lives under Environment/Island4 and is rebuilt on every run (never duplicated); the island's navmesh is
    /// baked (Data/Navigation/Island4.asset) and added to the loader; the dev island's travel menu learns it.
    /// Batch: <c>-executeMethod PleaseDontDrown.Editor.GameSceneBuilder.Island4Batch</c>
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private static readonly Vector2 Island4Center = new(560f, -400f);
        private static readonly Vector2 Island4Half = new(100f, 100f);
        private const float Island4Corner = 40f;
        private const float Island4MinX = 300f, Island4MaxX = 820f, Island4MinZ = -660f, Island4MaxZ = -140f; // (to the main grid's east edge)
        private const float I4Ground = 1.3f, I4PlateauTop = 9f, I4Deck = 1.25f;
        // The plateau (x 612..660, z -472..-412) and the cliff path up to it from the helipad side.
        private static readonly Vector2 I4PlateauCentre = new(636f, -442f), I4PlateauHalf = new(24f, 30f);
        private static readonly Vector2 I4RampFoot = new(586f, -394f), I4RampTop = new(617f, -419f);
        // The lagoon: the wading shelf off the south-west beach.
        private static readonly Vector2 I4LagoonCentre = new(452f, -472f), I4LagoonHalf = new(44f, 44f);
        // Places.
        private static readonly Vector2 I4JettyRoot = new(474f, -380f), I4JettyEnd = new(430f, -380f);
        private static readonly Vector2 I4Tower = new(478f, -362f);
        private static readonly Vector2 I4Warehouse = new(532f, -336f), I4WarehouseHalf = new(19f, 12f);
        private static readonly Vector2 I4Bar = new(486f, -440f);
        private static readonly Vector2 I4Helipad = new(592f, -336f);
        private static readonly Vector2 I4Villa = new(642f, -446f), I4Statue = new(622f, -432f);
        private static readonly Vector2 I4SandyStairsTop = new(656f, -456f), I4SandyStairsFoot = new(676f, -456f), I4SandyJettyEnd = new(704f, -456f);
        private static readonly Vector2 I4PoliceRoot = new(600f, -312f), I4PoliceEnd = new(600f, -282f);
        private static readonly Vector2 I4Plaza = new(522f, -384f);
        private static readonly Vector2[][] I4Paths =
        {
            new[] { I4JettyRoot, I4Plaza, new Vector2(532f, -352f) },               // jetty → plaza → the hangar's door
            new[] { I4Plaza, new Vector2(502f, -414f), I4Bar + new Vector2(6f, 4f) }, // → the smoothie bar
            new[] { I4Plaza, new Vector2(566f, -372f), I4Helipad + new Vector2(0f, -9f) }, // → the helipad
            new[] { new Vector2(566f, -372f), I4RampFoot },                          // → the cliff path
            new[] { I4Helipad + new Vector2(6f, 6f), I4PoliceRoot },                 // → the police dock
        };

        private static Transform _i4Story;

        // ------------------------------------------------------------------ the ground

        /// <summary>Inland from the coast in metres (negative: out at sea), the coastline a little wobbly.</summary>
        private static float Island4Shore(float x, float z)
        {
            float wobble = 2.5f * Mathf.Sin(x * 0.05f + 0.7f) + 2f * Mathf.Sin(z * 0.043f + 2.1f);
            return -BoxDistanceOut(x, z, Island4Center, Island4Half, Island4Corner) + wobble;
        }

        private static float SegmentT(Vector2 p, Vector2 a, Vector2 b) => Mathf.Clamp01(Vector2.Dot(p - a, b - a) / (b - a).sqrMagnitude);

        private static float Island4Height(float x, float z)
        {
            float s = Island4Shore(x, z);
            float h = s >= 0f
                ? Mathf.Lerp(0.15f, I4Ground, Smooth(0f, 14f, s))
                : s > -6f ? Mathf.Lerp(0.15f, -0.9f, -s / 6f)
                : s > -16f ? Mathf.Lerp(-0.9f, -2.4f, (-s - 6f) / 10f)
                : s > -30f ? Mathf.Lerp(-2.4f, -5f, (-s - 16f) / 14f)
                : s > -50f ? Mathf.Lerp(-5f, -12f, (-s - 30f) / 20f)
                : Mathf.Lerp(-12f, -40f, Smooth(-50f, -120f, s)); // (so deep by the edge of the patch that its edge doesn't show)
            // The lagoon: knee-deep for 34 m out, then a sudden drop into deep water.
            float lagoon = Near(x, z, I4LagoonCentre, I4LagoonHalf, 16f);
            if (lagoon > 0f && s < 6f)
            {
                float shelf = s >= -34f ? Mathf.Lerp(Mathf.Max(h, -0.95f), -0.95f, Smooth(0f, -10f, s))
                    : s >= -48f ? Mathf.Lerp(-0.95f, -6.5f, Smooth(-34f, -40f, s))
                    : Mathf.Min(h, Mathf.Lerp(-6.5f, h, Smooth(-48f, -58f, s)));
                if (s >= 0f) shelf = h;
                h = Mathf.Lerp(h, shelf, lagoon);
            }
            // The plateau: sheer cliffs all round, the east ones straight into the sea.
            float plateau = 1f - Smooth(0f, 4.5f, BoxDistanceOut(x, z, I4PlateauCentre, I4PlateauHalf, 7f));
            if (plateau > 0f) h = Mathf.Max(h, Mathf.Lerp(h, I4PlateauTop, plateau));
            // The cliff path: an embankment up its north-west side.
            var p = new Vector2(x, z);
            float t = SegmentT(p, I4RampFoot, I4RampTop);
            float d = Vector2.Distance(p, Vector2.Lerp(I4RampFoot, I4RampTop, t));
            float ramp = Mathf.Lerp(I4Ground, I4PlateauTop + 0.05f, t);
            float onRamp = 1f - Smooth(3.2f, 6.5f, d);
            if (onRamp > 0f && t > 0f) h = Mathf.Max(h, Mathf.Lerp(h, ramp, onRamp));
            return h;
        }

        private static float I4PathDistance(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (Vector2[] path in I4Paths) best = Mathf.Min(best, DistanceToLine(p, path));
            // The cliff path itself is paved too.
            float t = SegmentT(p, I4RampFoot, I4RampTop);
            best = Mathf.Min(best, Vector2.Distance(p, Vector2.Lerp(I4RampFoot, I4RampTop, t)));
            return best;
        }

        private static (Vector4 weights, Color shade) Island4GroundLayers(Vector3 v, Vector3 normal)
        {
            float x = v.x, z = v.z, h = v.y;
            var p = new Vector2(x, z);
            float s = Island4Shore(x, z);
            // Manicured lawns inland, white sand by the water; paving along the paths and round the buildings.
            float green = Mathf.Max(Smooth(14f, 22f, s), Smooth(3f, 6f, h)) * Smooth(0.8f, 1.2f, h); // (the plateau is lawn right to its cliffs)
            float paved = 0f; // (the paths and the plaza are pale paving slabs on top: BuildI4Paving)
            _ = p;
            float rocky = Mathf.Max(Smooth(0.78f, 0.55f, normal.y), Smooth(-3f, -5f, h));
            float sand = (1f - Mathf.Max(green, paved)) * (1f - rocky);
            var weights = new Vector4(green * (1f - paved) * (1f - rocky), 0f, paved * (1f - rocky), rocky);
            // Bright and even: this island is kept clean.
            Vector3 shade = Vector3.one * (0.95f + 0.1f * Mathf.PerlinNoise(x * 0.11f + 5f, z * 0.11f));
            shade *= Mathf.Lerp(1f, 0.78f, Smooth(0.1f, -1.5f, h) * sand); // wet sand
            return (weights, new Color(shade.x * 0.5f, shade.y * 0.5f, shade.z * 0.5f, 1f));
        }

        private static Mesh GetIsland4Mesh()
        {
            const float step = TerrainStep;
            int nx = Mathf.RoundToInt((Island4MaxX - Island4MinX) / step) + 1;
            int nz = Mathf.RoundToInt((Island4MaxZ - Island4MinZ) / step) + 1;
            var vertices = new Vector3[nx * nz];
            var uvs = new Vector2[nx * nz];
            for (int iz = 0; iz < nz; iz++)
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = Island4MinX + ix * step, z = Island4MinZ + iz * step;
                    vertices[iz * nx + ix] = new Vector3(x, Island4Height(x, z), z);
                    uvs[iz * nx + ix] = new Vector2(x, z) * 0.25f;
                }
            var triangles = new List<int>((nx - 1) * (nz - 1) * 6);
            for (int iz = 0; iz < nz - 1; iz++)
                for (int ix = 0; ix < nx - 1; ix++)
                {
                    int a = iz * nx + ix, b = a + nx;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            string dir = MeshDir + "/Island4";
            Directory.CreateDirectory(dir);
            string path = dir + "/Island4Ground.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = "Island4Ground" };
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Vector3[] normals = mesh.normals;
            var colours = new Color[vertices.Length];
            var weights = new Vector4[vertices.Length];
            for (int i = 0; i < vertices.Length; i++) (weights[i], colours[i]) = Island4GroundLayers(vertices[i], normals[i]);
            mesh.colors = colours;
            mesh.SetUVs(1, weights);
            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Vector3 I4At(Vector2 p, float lift = 0f) => new(p.x, Island4Height(p.x, p.y) + lift, p.y);

        private static Transform I4Point(string name, Vector3 at, float yaw) => Point(_i4Story, name, at, yaw);

        // ------------------------------------------------------------------ the batch

        public static void Island4Batch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform env = FindByPath("Environment") ?? throw new InvalidOperationException("No Environment");
            if (env.Find("Island4") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Island4").transform;
            root.SetParent(env, false);
            var ground = new GameObject("Island4Ground");
            ground.transform.SetParent(root, false);
            Mesh mesh = GetIsland4Mesh();
            ground.AddComponent<MeshFilter>().sharedMesh = mesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = Island3GroundMaterial();
            ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            TagSurface(ground, SurfaceKind.Sand);
            _i4Story = new GameObject("Story").transform;
            _i4Story.SetParent(root, false);
            Physics.SyncTransforms();

            var taken = new List<Vector3>(); // (x, z, radius): palms and loungers keep clear
            Transform arrival = BuildI4Jetty(root, taken);
            BuildI4Tower(root, taken);
            BuildI4Lagoon(root, taken);
            BuildI4Warehouse(root, taken);
            BuildI4SmoothieBar(root, taken);
            BuildI4Helipad(root, taken);
            BuildI4Plateau(root, taken);
            BuildI4SandyJetty(root, taken);
            BuildI4PoliceDock(root, taken);
            BuildI4Paving(root);
            taken.Add(new Vector3(I4Plaza.x, I4Plaza.y, 11f)); // (no palm in the middle of the plaza)
            BuildI4Palms(root, taken);
            BuildI4Buoys(root);

            var dev = Object.FindFirstObjectByType<DevIsland>();
            if (dev != null) SetRef(dev, "_finders", arrival);
            Material padGlow = GetMaterial("PadGlow", new Color(0.55f, 0.35f, 1f), emission: new Color(0.9f, 0.5f, 2.2f));
            TeleportPadAt(root, "TravelPad_Island4", I4At(new Vector2(480f, -394f)), 90f, Destination.FindersIsland, padGlow);

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island4");
            AddNavMeshToLoader("Island4");
            AssignSceneIds(scene, force: false); // only the new networked things get ids
            int count = root.GetComponentsInChildren<Transform>().Length;
            Debug.Log($"[Island4] PASS: Finders Keepers built ({count} objects, {_i4Story.childCount} story points)");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>The navmesh loader also loads <paramref name="area"/>'s baked navmesh (once).</summary>
        private static void AddNavMeshToLoader(string area)
        {
            var data = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>($"{NavMeshDir}/{area}.asset");
            var loader = Object.FindFirstObjectByType<NavMeshLoader>();
            if (data == null || loader == null) throw new InvalidOperationException($"No navmesh {area} or no loader");
            var so = new SerializedObject(loader);
            SerializedProperty list = Require(so, "_data");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return;
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ materials

        private static Material FkOrange => GetMaterial("FkOrange", new Color(1f, 0.46f, 0.08f), smoothness: 0.45f);
        private static Material FkWhite => GetMaterial("FkWhite", new Color(0.96f, 0.96f, 0.94f), smoothness: 0.4f);
        private static Material FkConcrete => GetMaterial("FkConcrete", new Color(0.8f, 0.79f, 0.76f), smoothness: 0.15f);
        private static Material FkDeck => GetMaterial("FkDeck", new Color(0.86f, 0.84f, 0.79f), smoothness: 0.2f);
        private static Material FkSteel => GetMaterial("FkSteel", new Color(0.82f, 0.84f, 0.86f), metallic: 0.35f, smoothness: 0.45f);
        private static Material FkDark => GetMaterial("FkDark", new Color(0.16f, 0.17f, 0.19f), smoothness: 0.3f);
        private static Material FkGlass => GetMaterial("FkGlass", new Color(0.32f, 0.5f, 0.6f), metallic: 0.2f, smoothness: 0.92f);

        private static TextMesh FkText(Transform parent, string name, Vector3 local, string text, float letterHeight, Color colour, float yaw = 0f, float pitch = 0f)
        {
            TextMesh t = WorldText(parent, name, local, text, 80, letterHeight / 8f, colour);
            t.fontStyle = FontStyle.Bold;
            t.transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);
            return t;
        }

        // ------------------------------------------------------------------ the jetty and the rival tower

        private static Transform BuildI4Jetty(Transform root, List<Vector3> taken)
        {
            var dock = new GameObject("FKDock").transform; // ("Dock" in the name: the seabed under it is off the navmesh)
            dock.SetParent(root, false);
            TagSurface(dock.gameObject, SurfaceKind.Wood);
            float x0 = I4JettyEnd.x, x1 = I4JettyRoot.x, z = I4JettyRoot.y;
            Primitive(PrimitiveType.Cube, "Deck", dock, new Vector3((x0 + x1) * 0.5f, I4Deck - 0.12f, z), new Vector3(x1 - x0, 0.24f, 3.2f), FkDeck);
            Primitive(PrimitiveType.Cube, "DeckHead", dock, new Vector3(x0 + 2.5f, I4Deck - 0.12f, z), new Vector3(5f, 0.24f, 9f), FkDeck);
            for (float x = x0 + 0.4f; x < x1 - 8f; x += 4f)
                foreach (float side in new[] { -1.45f, 1.45f })
                    Primitive(PrimitiveType.Cylinder, "Post", dock, new Vector3(x, I4Deck - 2.6f, z + side * (x < x0 + 5f ? 2.9f : 1f)), new Vector3(0.26f, 2.5f, 0.26f), FkWhite, keepCollider: false);
            // Orange-and-white rope rails, a gate across the middle with the toll booth.
            foreach (float side in new[] { -1f, 1f })
            {
                Primitive(PrimitiveType.Cube, "Rail", dock, new Vector3((x0 + 5f + x1) * 0.5f, I4Deck + 0.9f, z + side * 1.55f), new Vector3(x1 - x0 - 5f, 0.07f, 0.07f), FkOrange, keepCollider: false);
                Collider(dock, "RailBlock", new Vector3((x0 + 5f + x1) * 0.5f, I4Deck + 0.5f, z + side * 1.62f), new Vector3(x1 - x0 - 5f, 1f, 0.1f));
                for (float x = x0 + 5f; x <= x1; x += 3f)
                    Primitive(PrimitiveType.Cube, "Stanchion", dock, new Vector3(x, I4Deck + 0.45f, z + side * 1.55f), new Vector3(0.08f, 0.9f, 0.08f), FkWhite, keepCollider: false);
            }
            // The toll booth halfway: a little white kiosk on the deck's north edge, "PRIVATE BEACH  LANDING FEE $50".
            var booth = new GameObject("TollBooth").transform;
            booth.SetParent(dock, false);
            booth.position = new Vector3(452f, I4Deck, z + 2.9f);
            TagSurface(booth.gameObject, SurfaceKind.Wood);
            Primitive(PrimitiveType.Cube, "Floor", booth, new Vector3(0f, -0.12f, 0f), new Vector3(2.6f, 0.24f, 2.6f), FkDeck);
            Primitive(PrimitiveType.Cube, "Back", booth, new Vector3(0f, 1.2f, 1.2f), new Vector3(2.6f, 2.4f, 0.15f), FkWhite);
            Primitive(PrimitiveType.Cube, "Counter", booth, new Vector3(0f, 0.55f, -0.9f), new Vector3(2.4f, 1.1f, 0.3f), FkOrange);
            Primitive(PrimitiveType.Cube, "Roof", booth, new Vector3(0f, 2.5f, 0f), new Vector3(3f, 0.15f, 3f), FkOrange);
            foreach (float sx in new[] { -1.2f, 1.2f })
                Primitive(PrimitiveType.Cube, "Pole", booth, new Vector3(sx, 1.2f, -1.2f), new Vector3(0.12f, 2.4f, 0.12f), FkWhite);
            FkText(booth, "Sign", new Vector3(0f, 2.15f, -1.32f), "PRIVATE BEACH", 0.28f, new Color(1f, 0.46f, 0.08f), 0f);
            FkText(booth, "Fee", new Vector3(0f, 0.6f, -1.07f), "LANDING FEE $50", 0.2f, Color.white, 0f);
            // Arrival: on the deck at the land end, facing inland. The boat ties up at the head.
            Transform arrival = Point(root, "Island4Arrival", new Vector3(x1 - 3f, I4Deck + 0.15f, z), 90f);
            I4Point("Island4Arrival", arrival.position, 90f);
            I4Point("BoatBerth", new Vector3(x0 + 1.5f, WaterLevel, z - 6.4f), 270f);
            I4Point("JettyHead", new Vector3(x0 + 2.5f, I4Deck + 0.1f, z), 90f);
            I4Point("TollBooth", booth.position + new Vector3(0f, 0.1f, -0.2f), 180f);
            I4Point("TollGuard_0", new Vector3(452f, I4Deck + 0.1f, z - 0.9f), 270f);
            I4Point("TollGuard_1", new Vector3(454.5f, I4Deck + 0.1f, z + 0.7f), 270f);
            taken.Add(new Vector3((x0 + x1) * 0.5f, z, 6f));
            taken.Add(new Vector3(x1 + 4f, z, 6f));
            return arrival;
        }

        /// <summary>The rival company's lifeguard tower on the beach north of the jetty: orange and white, "FK RESCUE".</summary>
        private static void BuildI4Tower(Transform root, List<Vector3> taken)
        {
            var tower = new GameObject("FKTower").transform;
            tower.SetParent(root, false);
            tower.SetPositionAndRotation(I4At(I4Tower), Quaternion.Euler(0f, 90f, 0f)); // its front (local -z) looks west, out to sea
            TagSurface(tower.gameObject, SurfaceKind.Wood);
            const float deck = 3.2f;
            foreach (float sx in new[] { -1.3f, 1.3f })
                foreach (float sz in new[] { -1.3f, 1.3f })
                    Primitive(PrimitiveType.Cube, "Leg", tower, new Vector3(sx, deck * 0.5f, sz), new Vector3(0.22f, deck, 0.22f), FkWhite);
            Primitive(PrimitiveType.Cube, "Deck", tower, new Vector3(0f, deck, 0f), new Vector3(3.4f, 0.2f, 3.4f), FkDeck);
            Primitive(PrimitiveType.Cube, "Hut", tower, new Vector3(0f, deck + 1.2f, 0.5f), new Vector3(2.6f, 2.2f, 2f), FkOrange);
            for (int i = 0; i < 4; i++)
                Primitive(PrimitiveType.Cube, "Stripe", tower, new Vector3(0f, deck + 0.35f + i * 0.55f, -0.51f), new Vector3(2.62f, 0.22f, 0.02f), FkWhite, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Roof", tower, new Vector3(0f, deck + 2.45f, 0.3f), new Vector3(3.4f, 0.18f, 3f), FkWhite);
            Primitive(PrimitiveType.Cube, "Rail", tower, new Vector3(0f, deck + 0.9f, -1.65f), new Vector3(3.4f, 0.08f, 0.08f), FkOrange, keepCollider: false);
            Collider(tower, "RailBlock", new Vector3(0f, deck + 0.5f, -1.7f), new Vector3(3.4f, 1f, 0.1f));
            // The ramp up the back.
            GameObject ramp = Primitive(PrimitiveType.Cube, "Ramp", tower, new Vector3(0f, deck * 0.5f, 3.9f), new Vector3(1.2f, 0.12f, 5.6f), FkDeck);
            ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(deck, 5.2f) * Mathf.Rad2Deg, 0f, 0f);
            Primitive(PrimitiveType.Cube, "NameBoard", tower, new Vector3(0f, deck + 2.95f, -0.3f), new Vector3(3f, 0.7f, 0.08f), FkWhite, keepCollider: false);
            FkText(tower, "Name", new Vector3(0f, deck + 2.95f, -0.35f), "FK RESCUE", 0.5f, new Color(1f, 0.46f, 0.08f), 0f);
            FkText(tower, "NameBack", new Vector3(0f, deck + 2.95f, -0.25f), "FK RESCUE", 0.5f, new Color(1f, 0.46f, 0.08f), 180f);
            // A flag on top.
            Primitive(PrimitiveType.Cylinder, "FlagPole", tower, new Vector3(1.3f, deck + 3.6f, 1.3f), new Vector3(0.06f, 1.3f, 0.06f), FkWhite, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Flag", tower, new Vector3(1.75f, deck + 4.5f, 1.3f), new Vector3(0.9f, 0.55f, 0.02f), FkOrange, keepCollider: false);
            I4Point("RivalTower", tower.TransformPoint(new Vector3(0f, 0f, -3f)) + Vector3.up * 0.1f, 270f);
            I4Point("RivalLookout", tower.TransformPoint(new Vector3(0f, deck + 0.1f, -1f)), 270f);
            taken.Add(new Vector3(I4Tower.x, I4Tower.y, 7f));
        }

        // ------------------------------------------------------------------ the lagoon beach

        private static void BuildI4Lagoon(Transform root, List<Vector3> taken)
        {
            var beach = new GameObject("LagoonBeach").transform;
            beach.SetParent(root, false);
            // Rows of identical orange sunbeds under identical white parasols, at the top of the sand.
            var rng = new System.Random(44);
            int beds = 0;
            for (float zz = -414f; zz >= -470f; zz -= 7f)
            {
                // West beach, about 10 m up from the water.
                float x = 460f;
                while (x < 520f && Island4Shore(x, zz) < 9f) x += 0.5f;
                PlaceSunbed(beach, new Vector2(x, zz), 270f, ref beds);
            }
            for (float xx = 478f; xx <= 530f; xx += 7f)
            {
                float zz = -510f;
                while (zz < -440f && Island4Shore(xx, zz) < 9f) zz += 0.5f;
                if (Vector2.Distance(new Vector2(xx, zz), I4Bar) < 12f) continue;
                PlaceSunbed(beach, new Vector2(xx, zz), 180f, ref beds);
            }
            _ = rng;
            // Where the race is: the beach, the rivals' start (by their tower) and the sea past the drop-off.
            I4Point("RaceBeach", I4At(new Vector2(470f, -446f), 0.1f), 270f);
            I4Point("RaceStart", I4At(new Vector2(474f, -392f), 0.1f), 230f);
            I4Point("DropOff", new Vector3(422f, WaterLevel, -452f), 270f);
            I4Point("CastWave", I4At(new Vector2(500f, -400f), 0.1f), 270f); // the ending: everyone on the beach by the plaza
            Debug.Log($"[Island4] lagoon: {beds} sunbeds");
        }

        private static void PlaceSunbed(Transform parent, Vector2 at, float yaw, ref int count)
        {
            var bed = new GameObject("Sunbed").transform;
            bed.SetParent(parent, false);
            bed.SetPositionAndRotation(I4At(at), Quaternion.Euler(0f, yaw, 0f));
            if (PropModel("sunbed_orange", bed) == null)
                Primitive(PrimitiveType.Cube, "Bed", bed, new Vector3(0f, 0.3f, 0f), new Vector3(0.75f, 0.12f, 1.9f), FkOrange);
            Collider(bed, "BedBox", new Vector3(0f, 0.25f, 0f), new Vector3(0.8f, 0.5f, 1.95f));
            var parasol = new GameObject("Parasol").transform;
            parasol.SetParent(bed, false);
            parasol.localPosition = new Vector3(0.95f, 0f, 0.4f);
            Primitive(PrimitiveType.Cylinder, "Pole", parasol, new Vector3(0f, 1.15f, 0f), new Vector3(0.06f, 1.15f, 0.06f), FkWhite);
            Cone(parasol, "Canopy", new Vector3(0f, 2.05f, 0f), 1.35f, 0.45f, FkWhite);
            Primitive(PrimitiveType.Cylinder, "Trim", parasol, new Vector3(0f, 2.06f, 0f), new Vector3(2.72f, 0.03f, 2.72f), FkOrange, keepCollider: false);
            count++;
        }

        // ------------------------------------------------------------------ the warehouse

        private static void BuildI4Warehouse(Transform root, List<Vector3> taken)
        {
            var hangar = new GameObject("Warehouse").transform;
            hangar.SetParent(root, false);
            float floor = Island4Height(I4Warehouse.x, I4Warehouse.y);
            hangar.SetPositionAndRotation(new Vector3(I4Warehouse.x, floor, I4Warehouse.y), Quaternion.identity); // its door faces south
            TagSurface(hangar.gameObject, SurfaceKind.Rock);
            float W = I4WarehouseHalf.x, D = I4WarehouseHalf.y, H = 10f, door = 5f;
            Primitive(PrimitiveType.Cube, "Floor", hangar, new Vector3(0f, 0.05f, 0f), new Vector3(W * 2f, 0.3f, D * 2f), FkConcrete);
            Primitive(PrimitiveType.Cube, "WallNorth", hangar, new Vector3(0f, H * 0.5f, D), new Vector3(W * 2f, H, 0.4f), FkSteel);
            foreach (float sx in new[] { -1f, 1f })
            {
                Primitive(PrimitiveType.Cube, "WallSide", hangar, new Vector3(sx * W, H * 0.5f, 0f), new Vector3(0.4f, H, D * 2f), FkSteel);
                // The south wall either side of the big door.
                float half = (W - door) * 0.5f;
                Primitive(PrimitiveType.Cube, "WallSouth", hangar, new Vector3(sx * (door + half), H * 0.5f, -D), new Vector3(half * 2f, H, 0.4f), FkSteel);
                // Orange corner stripes.
                Primitive(PrimitiveType.Cube, "Corner", hangar, new Vector3(sx * W, H * 0.5f, -D), new Vector3(0.6f, H + 0.02f, 0.6f), FkOrange, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Corner", hangar, new Vector3(sx * W, H * 0.5f, D), new Vector3(0.6f, H + 0.02f, 0.6f), FkOrange, keepCollider: false);
            }
            Primitive(PrimitiveType.Cube, "OverDoor", hangar, new Vector3(0f, H - 1.5f, -D), new Vector3(door * 2f, 3f, 0.4f), FkSteel);
            Primitive(PrimitiveType.Cube, "DoorFrame", hangar, new Vector3(0f, H - 3.05f, -D - 0.22f), new Vector3(door * 2f + 0.6f, 0.3f, 0.1f), FkOrange, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Roof", hangar, new Vector3(0f, H + 0.2f, 0f), new Vector3(W * 2f + 1f, 0.4f, D * 2f + 1f), FkWhite);
            // On the roof, readable from the air, the cliff and the villa; over the door, from the plaza.
            FkText(hangar, "RoofWords", new Vector3(0f, H + 0.42f, 0f), "FINDERS KEEPERS", 3.4f, new Color(1f, 0.46f, 0.08f), 0f, 90f);
            FkText(hangar, "DoorWords", new Vector3(0f, H - 1.4f, -D - 0.23f), "FINDERS KEEPERS  LOST & FOUND", 1.1f, new Color(1f, 0.46f, 0.08f), 0f);
            // Light from the roof (a strip of skylights and two lamps without shadows).
            Material sky = GetMaterial("FkSkylight", new Color(1f, 0.98f, 0.92f), emission: new Color(2.4f, 2.3f, 2f));
            for (int i = -1; i <= 1; i++)
                Primitive(PrimitiveType.Cube, "Skylight", hangar, new Vector3(i * 11f, H - 0.02f, 0f), new Vector3(6f, 0.05f, D * 1.4f), sky, keepCollider: false);
            foreach (float lx in new[] { -9f, 9f })
            {
                var lamp = new GameObject("WarehouseLight").AddComponent<Light>();
                lamp.transform.SetParent(hangar, false);
                lamp.transform.localPosition = new Vector3(lx, H - 1.5f, 0f);
                lamp.type = LightType.Point;
                lamp.range = 22f;
                lamp.intensity = 2.2f;
                lamp.color = new Color(1f, 0.96f, 0.88f);
                lamp.shadows = LightShadows.None;
            }
            // Towers of "lost" things in rows: sunglasses, phones, wallets, watches, the casino's chips, in crates and bins.
            Material chipRed = GetMaterial("FkChipRed", new Color(0.8f, 0.12f, 0.12f)), chipBlue = GetMaterial("FkChipBlue", new Color(0.15f, 0.3f, 0.75f));
            Material bin = GetMaterial("FkBin", new Color(0.95f, 0.55f, 0.15f));
            string[] loot = { "sunglasses", "phone", "wallet", "watch" };
            int towers = 0;
            var rng = new System.Random(4);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 5; col++)
                {
                    if (row == 0 && col == 2) continue; // the aisle from the door stays clear
                    float x = -14f + col * 7f, zz = -4f + row * 7f;
                    var tower = new GameObject("LootTower").transform;
                    tower.SetParent(hangar, false);
                    tower.localPosition = new Vector3(x, 0.2f, zz);
                    int levels = 2 + rng.Next(3);
                    for (int l = 0; l < levels; l++)
                    {
                        float size = 2.1f - l * 0.25f;
                        Primitive(PrimitiveType.Cube, "Bin", tower, new Vector3(0f, 0.55f + l * 1.1f, 0f), new Vector3(size, 1.1f, size), l % 2 == 0 ? bin : FkWhite);
                    }
                    float top = 0.55f + levels * 1.1f - 0.55f;
                    // What's piled on top of each tower.
                    // (Big enough to read from the aisle: a heap of sunglasses, phones... each one 5x its size.)
                    bool chips = (row + col) % 4 == 1;
                    string kind = loot[(row * 5 + col) % loot.Length];
                    float topSize = 2.1f - (levels - 1) * 0.25f;
                    for (int k = 0; k < 9 && !chips; k++)
                    {
                        float a = k * 2.4f, r = 0.15f + (k % 3) * 0.25f;
                        GameObject thing = PropModel(kind, tower, new Vector3(Mathf.Cos(a) * r, top + 0.06f + (k / 3) * 0.09f, Mathf.Sin(a) * r), (float)rng.NextDouble() * 360f, 5f);
                        if (thing == null) break;
                    }
                    if (chips) // the casino's chips, in stacks
                        for (int k = 0; k < 9; k++)
                            Primitive(PrimitiveType.Cylinder, "Chips", tower, new Vector3(-0.6f + (k % 3) * 0.6f, top + 0.1f + 0.15f * (k % 2), -0.6f + (k / 3) * 0.6f), new Vector3(0.4f, 0.1f + 0.15f * (k % 2), 0.4f), k % 2 == 0 ? chipRed : chipBlue, keepCollider: false);
                    // A label on the front of the bottom bin.
                    string label = chips ? "CASINO CHIPS" : kind == "sunglasses" ? "SUNGLASSES" : kind == "phone" ? "PHONES" : kind == "wallet" ? "WALLETS" : "WATCHES";
                    FkText(tower, "Label", new Vector3(0f, 0.6f, -1.07f), label, 0.26f, Color.white, 0f);
                    _ = topSize;
                    towers++;
                }
            // Five Lost & Found tags lie about (4.3's evidence), and the guards' beats.
            Vector3[] tags = { new(-14f, 0.2f, -6.4f), new(7f, 0.2f, 1.6f), new(14f, 0.2f, 9.5f), new(-7f, 0.2f, 9.6f), new(16.5f, 0.2f, -9f) };
            for (int i = 0; i < tags.Length; i++) I4Point($"Evidence_{i}", hangar.TransformPoint(tags[i]) + Vector3.up * 0.15f, 0f);
            (Vector3 a, Vector3 b)[] beats = { (new(-17f, 0.3f, -8f), new(-17f, 0.3f, 9f)), (new(-3.5f, 0.3f, -8f), new(-3.5f, 0.3f, 10f)), (new(3.5f, 0.3f, 10f), new(3.5f, 0.3f, -8f)), (new(17f, 0.3f, 9f), new(17f, 0.3f, -8f)) };
            for (int i = 0; i < beats.Length; i++)
            {
                I4Point($"WarehouseGuard_{i}_0", hangar.TransformPoint(beats[i].a), 0f);
                I4Point($"WarehouseGuard_{i}_1", hangar.TransformPoint(beats[i].b), 180f);
            }
            I4Point("WarehouseDoor", hangar.TransformPoint(new Vector3(0f, 0.3f, -D - 4f)), 0f);
            I4Point("WarehouseInside", hangar.TransformPoint(new Vector3(0f, 0.3f, -D + 3f)), 0f);
            NavKeepOut(hangar, new Vector3(0f, H * 0.5f, 0f), new Vector3(W * 2f + 1f, H, D * 2f + 1f));
            taken.Add(new Vector3(I4Warehouse.x, I4Warehouse.y, 26f));
            Debug.Log($"[Island4] warehouse: {towers} towers of lost things");
        }

        // ------------------------------------------------------------------ the smoothie bar

        private static void BuildI4SmoothieBar(Transform root, List<Vector3> taken)
        {
            var bar = new GameObject("SmoothieBar").transform;
            bar.SetParent(root, false);
            bar.SetPositionAndRotation(I4At(I4Bar), Quaternion.Euler(0f, 90f, 0f)); // the counter (local -z) faces the lagoon (west)
            TagSurface(bar.gameObject, SurfaceKind.Wood);
            Material berry = GetMaterial("FkBerry", new Color(0.55f, 0.12f, 0.45f), smoothness: 0.5f);
            Primitive(PrimitiveType.Cube, "Deck", bar, new Vector3(0f, 0.1f, 0f), new Vector3(9f, 0.25f, 8f), FkDeck);
            Primitive(PrimitiveType.Cube, "Counter", bar, new Vector3(0f, 0.6f, -0.6f), new Vector3(6f, 1.05f, 0.7f), FkWhite);
            Primitive(PrimitiveType.Cube, "CounterTop", bar, new Vector3(0f, 1.16f, -0.65f), new Vector3(6.2f, 0.08f, 0.9f), FkOrange);
            Primitive(PrimitiveType.Cube, "BackBar", bar, new Vector3(0f, 1.1f, 2.4f), new Vector3(6f, 2.2f, 0.5f), FkWhite);
            for (int i = 0; i < 6; i++) // blenders and bottles on the back bar
                Primitive(PrimitiveType.Cylinder, "Blender", bar, new Vector3(-2.3f + i * 0.9f, 2.42f, 2.3f), new Vector3(0.22f, 0.22f, 0.22f), i % 2 == 0 ? berry : FkOrange, keepCollider: false);
            foreach (float sx in new[] { -4.2f, 4.2f })
                foreach (float sz in new[] { -3.6f, 3.6f })
                    Primitive(PrimitiveType.Cylinder, "Post", bar, new Vector3(sx, 1.6f, sz), new Vector3(0.18f, 1.6f, 0.18f), FkWhite);
            Primitive(PrimitiveType.Cube, "Canopy", bar, new Vector3(0f, 3.3f, 0f), new Vector3(9.4f, 0.25f, 8.4f), FkOrange);
            FkText(bar, "Sign", new Vector3(0f, 3.8f, -3.51f), "BLACKOUT BERRY", 0.75f, new Color(0.55f, 0.12f, 0.45f), 0f);
            FkText(bar, "Sign2", new Vector3(0f, 3.0f, -4.25f), "SMOOTHIES  -  FINDERS KEEPERS", 0.3f, Color.white, 0f);
            Primitive(PrimitiveType.Cube, "SignBoard", bar, new Vector3(0f, 3.8f, -3.45f), new Vector3(6.4f, 0.95f, 0.08f), FkWhite);
            // Stools along the front of the counter.
            for (int i = 0; i < 4; i++)
            {
                var stool = new GameObject("Stool").transform;
                stool.SetParent(bar, false);
                stool.localPosition = new Vector3(-2.25f + i * 1.5f, 0.22f, -1.65f);
                if (PropModel("stool", stool) == null)
                    Primitive(PrimitiveType.Cylinder, "Seat", stool, new Vector3(0f, 0.4f, 0f), new Vector3(0.4f, 0.4f, 0.4f), FkOrange);
                Collider(stool, "StoolBox", new Vector3(0f, 0.35f, 0f), new Vector3(0.45f, 0.7f, 0.45f));
                I4Point($"BarRegular_{i}", bar.TransformPoint(new Vector3(-2.25f + i * 1.5f, 0.25f, -2.4f)), 90f);
            }
            I4Point("Barman", bar.TransformPoint(new Vector3(0f, 0.25f, 0.9f)), 90f);
            I4Point("SmoothieBar", bar.TransformPoint(new Vector3(0f, 0.25f, -4.5f)), 90f);
            NavKeepOut(bar, new Vector3(0f, 1.5f, 0.9f), new Vector3(6.4f, 3f, 3.6f));
            taken.Add(new Vector3(I4Bar.x, I4Bar.y, 9f));
        }

        // ------------------------------------------------------------------ the helipad and the helicopter

        private static void BuildI4Helipad(Transform root, List<Vector3> taken)
        {
            var pad = new GameObject("Helipad").transform;
            pad.SetParent(root, false);
            pad.position = I4At(I4Helipad);
            TagSurface(pad.gameObject, SurfaceKind.Rock);
            Primitive(PrimitiveType.Cylinder, "Pad", pad, new Vector3(0f, 0.08f, 0f), new Vector3(16f, 0.12f, 16f), FkConcrete);
            Primitive(PrimitiveType.Cylinder, "Ring", pad, new Vector3(0f, 0.15f, 0f), new Vector3(13f, 0.02f, 13f), FkOrange, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "Inner", pad, new Vector3(0f, 0.16f, 0f), new Vector3(12f, 0.02f, 12f), FkConcrete, keepCollider: false);
            foreach (float sx in new[] { -1.6f, 1.6f })
                Primitive(PrimitiveType.Cube, "H", pad, new Vector3(sx, 0.17f, 0f), new Vector3(0.7f, 0.02f, 5f), FkWhite, keepCollider: false);
            Primitive(PrimitiveType.Cube, "H", pad, new Vector3(0f, 0.17f, 0f), new Vector3(3.2f, 0.02f, 0.7f), FkWhite, keepCollider: false);
            // The company's helicopter, parked (a networked object the host flies in the boss fight).
            Transform heli = BuildFkHelicopter(root, pad.position + new Vector3(0f, 0.15f, 0f), 200f);
            I4Point("HeliPark", heli.position, heli.eulerAngles.y);
            I4Point("Helipad", pad.position + new Vector3(-8.5f, 0.1f, -4f), 60f); // the THIEF is cornered here (4.5)
            taken.Add(new Vector3(I4Helipad.x, I4Helipad.y, 10f));
        }

        private static Transform BuildFkHelicopter(Transform parent, Vector3 feet, float yaw)
        {
            var heli = new GameObject("FKHelicopter").transform;
            heli.SetParent(parent, false);
            heli.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            heli.gameObject.AddComponent<NetworkObject>();
            var sync = heli.gameObject.AddComponent<NetworkTransform>();
            sync.SetSynchronizeScale(false);
            var body = new GameObject("Body").transform;
            body.SetParent(heli, false);
            Primitive(PrimitiveType.Sphere, "Cabin", body, new Vector3(0f, 1.6f, 0.6f), new Vector3(2.3f, 2.1f, 4.2f), FkOrange);
            Primitive(PrimitiveType.Sphere, "Window", body, new Vector3(0f, 1.95f, 1.75f), new Vector3(1.9f, 1.3f, 2.1f), FkGlass, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Stripe", body, new Vector3(0f, 1.25f, 0.4f), new Vector3(2.32f, 0.3f, 3.2f), FkWhite, keepCollider: false);
            GameObject boom = Primitive(PrimitiveType.Cylinder, "TailBoom", body, new Vector3(0f, 2f, -3.4f), new Vector3(0.45f, 2.6f, 0.45f), FkOrange, keepCollider: false);
            boom.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Primitive(PrimitiveType.Cube, "Fin", body, new Vector3(0f, 2.6f, -5.8f), new Vector3(0.12f, 1.4f, 0.9f), FkWhite, keepCollider: false);
            foreach (float sx in new[] { -1f, 1f })
            {
                Primitive(PrimitiveType.Cube, "Skid", body, new Vector3(sx * 1f, 0.12f, 0.5f), new Vector3(0.12f, 0.12f, 3.8f), FkDark, keepCollider: false);
                foreach (float sz in new[] { -0.6f, 1.6f })
                    Primitive(PrimitiveType.Cube, "Strut", body, new Vector3(sx * 0.85f, 0.45f, sz), new Vector3(0.08f, 0.7f, 0.08f), FkDark, keepCollider: false);
                FkText(body, "Name", new Vector3(sx * 1.17f, 1.55f, 0.2f), "FK RESCUE", 0.42f, Color.white, sx > 0 ? 270f : 90f);
            }
            var rotor = new GameObject("Rotor").transform;
            rotor.SetParent(body, false);
            rotor.localPosition = new Vector3(0f, 2.85f, 0.4f);
            Primitive(PrimitiveType.Cylinder, "Hub", rotor, Vector3.zero, new Vector3(0.35f, 0.2f, 0.35f), FkDark, keepCollider: false);
            for (int i = 0; i < 2; i++)
                Primitive(PrimitiveType.Cube, "Blade", rotor, new Vector3(0f, 0.12f, 0f), new Vector3(0.3f, 0.04f, 10f), FkDark, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(0f, i * 90f, 0f);
            var tail = new GameObject("TailRotor").transform;
            tail.SetParent(body, false);
            tail.localPosition = new Vector3(0.18f, 2.6f, -5.9f);
            Primitive(PrimitiveType.Cube, "Blade", tail, Vector3.zero, new Vector3(0.04f, 1.4f, 0.14f), FkDark, keepCollider: false);
            heli.gameObject.AddComponent<FkHelicopter>();
            return heli;
        }

        // ------------------------------------------------------------------ the plateau: villa and statue

        private static void BuildI4Plateau(Transform root, List<Vector3> taken)
        {
            var top = new GameObject("SandyPlateau").transform;
            top.SetParent(root, false);
            // The cliff path's low walls (orange posts and a white rail on the sea side).
            var path = new GameObject("CliffPath").transform;
            path.SetParent(top, false);
            Vector2 along = (I4RampTop - I4RampFoot).normalized, side = new Vector2(along.y, -along.x);
            for (float d = 2f; d < Vector2.Distance(I4RampFoot, I4RampTop) - 1f; d += 3.5f)
                foreach (float s in new[] { -3f, 3f })
                {
                    Vector2 q = I4RampFoot + along * d + side * s;
                    Primitive(PrimitiveType.Cube, "Post", path, I4At(q, 0.45f), new Vector3(0.18f, 0.9f, 0.18f), FkOrange, keepCollider: false);
                }
            // The villa: a white two-storey cube house with a glass front, on the plateau's east half.
            var villa = new GameObject("SandyVilla").transform;
            villa.SetParent(top, false);
            villa.SetPositionAndRotation(new Vector3(I4Villa.x, I4PlateauTop, I4Villa.y), Quaternion.Euler(0f, 270f, 0f)); // its front faces west, the statue
            TagSurface(villa.gameObject, SurfaceKind.Rock);
            const float vw = 8f, vd = 6f, vh = 4f;
            Primitive(PrimitiveType.Cube, "Floor", villa, new Vector3(0f, 0.08f, 0f), new Vector3(vw * 2f, 0.3f, vd * 2f), FkDeck);
            Primitive(PrimitiveType.Cube, "Back", villa, new Vector3(0f, vh * 0.5f, vd), new Vector3(vw * 2f, vh, 0.35f), FkWhite);
            foreach (float sx in new[] { -1f, 1f })
                Primitive(PrimitiveType.Cube, "Side", villa, new Vector3(sx * vw, vh * 0.5f, 0f), new Vector3(0.35f, vh, vd * 2f), FkWhite);
            // The glass front, a wide doorway in the middle.
            foreach (float sx in new[] { -1f, 1f })
                Primitive(PrimitiveType.Cube, "Glass", villa, new Vector3(sx * 5f, vh * 0.5f, -vd), new Vector3(6f, vh, 0.12f), FkGlass);
            Primitive(PrimitiveType.Cube, "OverDoor", villa, new Vector3(0f, vh - 0.5f, -vd), new Vector3(4f, 1f, 0.3f), FkWhite);
            Primitive(PrimitiveType.Cube, "Ceiling", villa, new Vector3(0f, vh + 0.15f, 0f), new Vector3(vw * 2f + 0.6f, 0.3f, vd * 2f + 0.6f), FkWhite);
            // Upper storey, set back, an orange band; a terrace in front of it.
            Primitive(PrimitiveType.Cube, "Upper", villa, new Vector3(1.5f, vh + 1.95f, 1.5f), new Vector3(10f, 3.3f, 8f), FkWhite);
            Primitive(PrimitiveType.Cube, "UpperGlass", villa, new Vector3(1.5f, vh + 1.95f, -2.52f), new Vector3(8f, 2.4f, 0.1f), FkGlass, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Band", villa, new Vector3(1.5f, vh + 3.7f, 1.5f), new Vector3(10.3f, 0.3f, 8.3f), FkOrange, keepCollider: false);
            // Inside: a desk, a big orange sofa, a pile of "lost" things she's counting.
            Primitive(PrimitiveType.Cube, "Desk", villa, new Vector3(0f, 0.5f, 3.4f), new Vector3(3.2f, 0.9f, 1.2f), FkDark);
            Primitive(PrimitiveType.Cube, "Sofa", villa, new Vector3(-5.2f, 0.45f, 2.5f), new Vector3(1.1f, 0.7f, 3.6f), FkOrange);
            for (int i = 0; i < 4; i++)
                PropModel(i % 2 == 0 ? "wallet" : "watch", villa, new Vector3(-0.8f + i * 0.5f, 0.97f, 3.3f), i * 40f, 1.6f);
            var lamp = new GameObject("VillaLight").AddComponent<Light>();
            lamp.transform.SetParent(villa, false);
            lamp.transform.localPosition = new Vector3(0f, vh - 0.6f, 0f);
            lamp.type = LightType.Point;
            lamp.range = 14f;
            lamp.intensity = 1.6f;
            lamp.color = new Color(1f, 0.93f, 0.82f);
            lamp.shadows = LightShadows.None;
            NavKeepOut(villa, new Vector3(0f, vh * 0.5f, 0f), new Vector3(vw * 2f, vh, vd * 2f));
            I4Point("SandyVilla", villa.TransformPoint(new Vector3(0f, 0.25f, 1.9f)), 90f); // behind her desk... in front of it, facing the door
            I4Point("VillaDoor", villa.TransformPoint(new Vector3(0f, 0.25f, -vd - 3f)), 90f);
            taken.Add(new Vector3(I4Villa.x, I4Villa.y, 12f));

            // The statue: Sandy herself, 11 m tall, gold, on a white plinth.
            var statue = new GameObject("SandyStatue").transform;
            statue.SetParent(top, false);
            statue.SetPositionAndRotation(new Vector3(I4Statue.x, I4PlateauTop, I4Statue.y), Quaternion.Euler(0f, 300f, 0f)); // looks out over the island
            Primitive(PrimitiveType.Cube, "Plinth", statue, new Vector3(0f, 1.1f, 0f), new Vector3(4.2f, 2.2f, 4.2f), FkWhite);
            Primitive(PrimitiveType.Cube, "PlinthTrim", statue, new Vector3(0f, 2.15f, 0f), new Vector3(4.4f, 0.12f, 4.4f), FkOrange, keepCollider: false);
            FkText(statue, "Plaque", new Vector3(0f, 1.2f, -2.12f), "SANDY\nFOUNDER", 0.45f, new Color(1f, 0.46f, 0.08f), 0f);
            Material gold = GetMaterial("FkStatueGold", new Color(0.93f, 0.72f, 0.3f), metallic: 0.85f, smoothness: 0.62f);
            Mesh sandy = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Game/Avatar/Bodies/sandy_mesh.asset");
            if (sandy != null)
            {
                var figure = new GameObject("Figure");
                figure.transform.SetParent(statue, false);
                float scale = 11f / Mathf.Max(0.5f, sandy.bounds.size.y);
                figure.transform.localScale = Vector3.one * scale;
                figure.transform.localPosition = new Vector3(0f, 2.2f - sandy.bounds.min.y * scale, 0f);
                figure.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // (the body faces +z; the statue looks the plinth's front way)
                figure.AddComponent<MeshFilter>().sharedMesh = sandy;
                figure.AddComponent<MeshRenderer>().sharedMaterial = gold;
                Collider(statue, "FigureBox", new Vector3(0f, 2.2f + 5.5f, 0f), new Vector3(2.4f, 11f, 1.6f));
            }
            else Debug.LogWarning("[Island4] no sandy_mesh.asset: the statue has only its plinth");
            I4Point("SandyStatue", statue.TransformPoint(new Vector3(0f, 0f, -4f)), 300f);
            taken.Add(new Vector3(I4Statue.x, I4Statue.y, 5f));
            I4Point("CliffPathFoot", I4At(I4RampFoot, 0.1f), 50f);
            I4Point("CliffPathTop", I4At(I4RampTop, 0.1f), 50f);
        }

        // ------------------------------------------------------------------ Sandy's private jetty (the final fight)

        private static void BuildI4SandyJetty(Transform root, List<Vector3> taken)
        {
            var jetty = new GameObject("SandyDock").transform;
            jetty.SetParent(root, false);
            TagSurface(jetty.gameObject, SurfaceKind.Wood);
            // Stairs down the east cliff: a landing at the top, a flight, the deck at the bottom.
            float z = I4SandyStairsTop.y;
            Vector3 top = new(I4SandyStairsTop.x, I4PlateauTop, z), foot = new(I4SandyStairsFoot.x, I4Deck, z);
            Primitive(PrimitiveType.Cube, "Landing", jetty, top + new Vector3(-1f, -0.12f, 0f), new Vector3(4f, 0.24f, 2.6f), FkDeck);
            Vector3 mid = (top + foot) * 0.5f;
            float run = Vector3.Distance(new Vector3(top.x, 0f, top.z), new Vector3(foot.x, 0f, foot.z));
            float pitch = Mathf.Atan2(top.y - foot.y, run) * Mathf.Rad2Deg;
            float length = Vector3.Distance(top, foot);
            GameObject flight = Primitive(PrimitiveType.Cube, "Stairs", jetty, mid + Vector3.down * 0.12f, new Vector3(length, 0.24f, 2.2f), FkDeck);
            flight.transform.rotation = Quaternion.Euler(0f, 0f, -pitch);
            for (float d = 0.6f; d < length; d += 0.55f) // step nosings
            {
                Vector3 at = Vector3.Lerp(top, foot, d / length);
                Primitive(PrimitiveType.Cube, "Step", jetty, at + Vector3.up * 0.02f, new Vector3(0.06f, 0.04f, 2.2f), FkOrange, keepCollider: false);
            }
            foreach (float s in new[] { -1.15f, 1.15f })
            {
                GameObject rail = Primitive(PrimitiveType.Cube, "StairRail", jetty, mid + new Vector3(0f, 0.9f, s), new Vector3(length, 0.07f, 0.07f), FkWhite, keepCollider: false);
                rail.transform.rotation = Quaternion.Euler(0f, 0f, -pitch);
                GameObject block = Primitive(PrimitiveType.Cube, "StairRailBlock", jetty, mid + new Vector3(0f, 0.5f, s * 1.05f), new Vector3(length, 1f, 0.1f), FkWhite);
                block.transform.rotation = Quaternion.Euler(0f, 0f, -pitch);
                Object.DestroyImmediate(block.GetComponent<MeshRenderer>());
            }
            // The deck: out over deep water, a wide square head at the end (the fight's ring).
            float x0 = foot.x - 1f, x1 = I4SandyJettyEnd.x - 5f;
            Primitive(PrimitiveType.Cube, "Deck", jetty, new Vector3((x0 + x1) * 0.5f, I4Deck - 0.12f, z), new Vector3(x1 - x0, 0.24f, 3.2f), FkDeck);
            Primitive(PrimitiveType.Cube, "DeckHead", jetty, new Vector3(I4SandyJettyEnd.x - 1f, I4Deck - 0.12f, z), new Vector3(10f, 0.24f, 10f), FkDeck);
            for (float x = x0 + 1f; x <= I4SandyJettyEnd.x + 3f; x += 4f)
                foreach (float s in new[] { -1.4f, 1.4f })
                    Primitive(PrimitiveType.Cylinder, "Post", jetty, new Vector3(x, I4Deck - 3.6f, z + s * (x > x1 ? 3.4f : 1f)), new Vector3(0.28f, 3.5f, 0.28f), FkWhite, keepCollider: false);
            // Gold bollards and a private sign.
            Material gold = GetMaterial("FkStatueGold", new Color(0.93f, 0.72f, 0.3f), metallic: 0.85f, smoothness: 0.62f);
            foreach (float s in new[] { -4.6f, 4.6f })
                Primitive(PrimitiveType.Cylinder, "Bollard", jetty, new Vector3(I4SandyJettyEnd.x + 3f, I4Deck + 0.3f, z + s), new Vector3(0.4f, 0.3f, 0.4f), gold);
            var sign = new GameObject("PrivateSign").transform;
            sign.SetParent(jetty, false);
            sign.position = new Vector3(x0 + 3f, I4Deck, z + 1.9f);
            Primitive(PrimitiveType.Cube, "Post", sign, new Vector3(0f, 0.8f, 0f), new Vector3(0.1f, 1.6f, 0.1f), FkWhite);
            Primitive(PrimitiveType.Cube, "Board", sign, new Vector3(0f, 1.55f, 0f), new Vector3(1.6f, 0.5f, 0.05f), FkOrange, keepCollider: false);
            FkText(sign, "Words", new Vector3(0f, 1.55f, -0.03f), "PRIVATE - SANDY ONLY", 0.13f, Color.white, 0f);
            FkText(sign, "Words2", new Vector3(0f, 1.55f, 0.03f), "PRIVATE - SANDY ONLY", 0.13f, Color.white, 180f);
            I4Point("SandyStairsTop", top + Vector3.up * 0.1f, 90f);
            I4Point("SandyJetty", new Vector3((x0 + x1) * 0.5f, I4Deck + 0.1f, z), 90f);
            I4Point("SandyJettyEnd", new Vector3(I4SandyJettyEnd.x, I4Deck + 0.1f, z), 270f);
            I4Point("KrakenLair", new Vector3(I4SandyJettyEnd.x - 4f, WaterLevel - 0.6f, z - 9f), 0f);
            I4Point("HeliDrop", new Vector3(I4SandyJettyEnd.x + 6f, 18f, z + 14f), 270f); // the helicopter hovers here in phase 1
        }

        // ------------------------------------------------------------------ the police dock

        private static void BuildI4PoliceDock(Transform root, List<Vector3> taken)
        {
            var dock = new GameObject("PoliceDock").transform;
            dock.SetParent(root, false);
            TagSurface(dock.gameObject, SurfaceKind.Wood);
            float x = I4PoliceRoot.x, z0 = I4PoliceRoot.y, z1 = I4PoliceEnd.y;
            Primitive(PrimitiveType.Cube, "Deck", dock, new Vector3(x, I4Deck - 0.12f, (z0 + z1) * 0.5f), new Vector3(2.6f, 0.24f, z1 - z0), FkDeck);
            for (float z = z1 - 0.4f; z > z0 + 8f; z -= 4f)
                foreach (float s in new[] { -1.1f, 1.1f })
                    Primitive(PrimitiveType.Cylinder, "Post", dock, new Vector3(x + s, I4Deck - 2.6f, z), new Vector3(0.24f, 2.5f, 0.24f), FkWhite, keepCollider: false);
            Material blue = GetMaterial("PoliceBlue", new Color(0.12f, 0.24f, 0.62f));
            var sign = new GameObject("Sign").transform;
            sign.SetParent(dock, false);
            sign.position = new Vector3(x - 1.6f, I4Deck, z1 - 2f);
            Primitive(PrimitiveType.Cube, "Post", sign, new Vector3(0f, 0.9f, 0f), new Vector3(0.1f, 1.8f, 0.1f), FkWhite);
            Primitive(PrimitiveType.Cube, "Board", sign, new Vector3(0f, 1.7f, 0f), new Vector3(0.05f, 0.45f, 1.4f), blue, keepCollider: false);
            FkText(sign, "Words", new Vector3(-0.03f, 1.7f, 0f), "POLICE", 0.24f, Color.white, 90f);
            FkText(sign, "Words2", new Vector3(0.03f, 1.7f, 0f), "POLICE", 0.24f, Color.white, 270f);
            I4Point("PoliceDock", new Vector3(x, I4Deck + 0.1f, z1 - 3f), 180f);
            I4Point("PoliceBoatBerth", new Vector3(x + 4.2f, WaterLevel, z1 - 4f), 0f);
            I4Point("PoliceBoatStart", new Vector3(x + 30f, WaterLevel, z1 + 140f), 200f);
            taken.Add(new Vector3(x, (z0 + z1) * 0.5f, 4f));
        }

        // ------------------------------------------------------------------ paving

        /// <summary>Pale paving: every path in slabs, the plaza, the hangar's apron and the cliff path up its embankment
        /// (no colliders: the ground under them is already flat; they're 4 cm proud of it).</summary>
        private static void BuildI4Paving(Transform root)
        {
            var paving = new GameObject("Paving").transform;
            paving.SetParent(root, false);
            Material slab = GetMaterial("FkPaving", new Color(0.9f, 0.88f, 0.84f), smoothness: 0.2f);
            Material edge = FkOrange;
            void Strip(Vector2 a, Vector2 b, float width, float ya, float yb)
            {
                Vector3 pa = new(a.x, ya + 0.04f, a.y), pb = new(b.x, yb + 0.04f, b.y);
                float length = Vector3.Distance(pa, pb) + width * 0.5f; // overlapping at the corners
                GameObject s = Primitive(PrimitiveType.Cube, "Slab", paving, (pa + pb) * 0.5f, new Vector3(width, 0.08f, length), slab, keepCollider: false);
                s.transform.rotation = Quaternion.LookRotation(pb - pa);
                foreach (float side in new[] { -1f, 1f }) // thin orange kerbs
                {
                    GameObject k = Primitive(PrimitiveType.Cube, "Kerb", s.transform, new Vector3(side * 0.5f, 0.2f, 0f), new Vector3(0.08f / width, 1.2f, 1f), edge, keepCollider: false);
                    _ = k;
                }
            }
            foreach (Vector2[] path in I4Paths)
                for (int i = 0; i < path.Length - 1; i++)
                    Strip(path[i], path[i + 1], 2.8f, I4Ground, I4Ground);
            Strip(I4RampFoot, I4RampTop, 3f, I4Ground, I4PlateauTop + 0.05f);
            Primitive(PrimitiveType.Cube, "Plaza", paving, new Vector3(I4Plaza.x, I4Ground + 0.07f, I4Plaza.y), new Vector3(18f, 0.08f, 18f), slab, keepCollider: false);
            Primitive(PrimitiveType.Cube, "Apron", paving, new Vector3(I4Warehouse.x, I4Ground + 0.07f, I4Warehouse.y - I4WarehouseHalf.y - 4f), new Vector3(16f, 0.08f, 8f), slab, keepCollider: false);
            // A fountain in the middle of the plaza: an orange bowl and a gold "FK".
            Primitive(PrimitiveType.Cylinder, "FountainBowl", paving, new Vector3(I4Plaza.x, I4Ground + 0.35f, I4Plaza.y), new Vector3(4f, 0.35f, 4f), FkWhite);
            Material water = GetMaterial("FkFountainWater", new Color(0.35f, 0.7f, 0.85f), smoothness: 0.9f);
            Primitive(PrimitiveType.Cylinder, "FountainWater", paving, new Vector3(I4Plaza.x, I4Ground + 0.68f, I4Plaza.y), new Vector3(3.6f, 0.02f, 3.6f), water, keepCollider: false);
            Primitive(PrimitiveType.Cylinder, "FountainPillar", paving, new Vector3(I4Plaza.x, I4Ground + 1.3f, I4Plaza.y), new Vector3(0.5f, 0.7f, 0.5f), FkOrange);
            Material gold = GetMaterial("FkStatueGold", new Color(0.93f, 0.72f, 0.3f), metallic: 0.85f, smoothness: 0.62f);
            foreach (float yaw in new[] { 0f, 180f })
            {
                TextMesh t = FkText(paving, "FK", new Vector3(I4Plaza.x, I4Ground + 2.35f, I4Plaza.y), "FK", 0.9f, new Color(0.95f, 0.75f, 0.3f), yaw);
                t.transform.position += t.transform.forward * -0.02f;
            }
            _ = gold;
        }

        // ------------------------------------------------------------------ the way there

        /// <summary>The sea route from the hotel island: orange company buoys every ~60 m from off the hotel's beach,
        /// east past its north-east corner, to the jetty (also the autoplay's waypoints, story point Route_i).</summary>
        public static readonly Vector2[] Island4Route =
            { new(60f, -165f), new(150f, -160f), new(240f, -175f), new(320f, -220f), new(380f, -290f), new(415f, -350f) };

        private static void BuildI4Buoys(Transform root)
        {
            var buoys = new GameObject("RouteBuoys").transform;
            buoys.SetParent(root, false);
            for (int i = 0; i < Island4Route.Length; i++)
            {
                Vector2 p = Island4Route[i];
                var b = new GameObject("Buoy").transform;
                b.SetParent(buoys, false);
                b.position = new Vector3(p.x + 9f, WaterLevel - 0.25f, p.y + 6f); // beside the line, not on it
                Primitive(PrimitiveType.Cylinder, "Float", b, new Vector3(0f, 0.4f, 0f), new Vector3(0.9f, 0.5f, 0.9f), FkOrange, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Band", b, new Vector3(0f, 0.75f, 0f), new Vector3(0.92f, 0.08f, 0.92f), FkWhite, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "Mast", b, new Vector3(0f, 1.6f, 0f), new Vector3(0.08f, 0.8f, 0.08f), FkWhite, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Flag", b, new Vector3(0.3f, 2.2f, 0f), new Vector3(0.6f, 0.4f, 0.02f), FkOrange, keepCollider: false);
                I4Point($"Route_{i}", new Vector3(p.x, WaterLevel, p.y), 0f);
            }
        }

        // ------------------------------------------------------------------ palms

        private static void BuildI4Palms(Transform root, List<Vector3> taken)
        {
            var palms = new GameObject("Palms").transform;
            palms.SetParent(root, false);
            var rng = new System.Random(404);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int placed = 0;
            bool Clear(Vector2 p, float r)
            {
                foreach (Vector3 t in taken)
                    if (Vector2.Distance(p, new Vector2(t.x, t.y)) < t.z + r) return false;
                return I4PathDistance(p) > 3.2f;
            }
            void Palm(Vector2 p)
            {
                if (!Clear(p, 2f)) return;
                float h = Island4Height(p.x, p.y);
                if (h < 0.9f || h > I4Ground + 0.2f) return; // not in the water, not up the cliff
                GameObject palm = MeshyArt.Place("palm_tall", palms, R(7.5f, 9.5f), new Vector3(p.x, h, p.y), R(0f, 360f));
                if (palm == null) return;
                var trunk = palm.AddComponent<CapsuleCollider>();
                trunk.center = new Vector3(0f, 2f, 0f);
                trunk.radius = 0.25f;
                trunk.height = 4f;
                taken.Add(new Vector3(p.x, p.y, 2f));
                placed++;
            }
            // In neat rows: along the paths (both sides), round the plaza and up the beach.
            foreach (Vector2[] path in I4Paths)
                for (int i = 0; i < path.Length - 1; i++)
                {
                    Vector2 a = path[i], b = path[i + 1], dir = (b - a).normalized, side = new(dir.y, -dir.x);
                    for (float d = 6f; d < Vector2.Distance(a, b) - 4f; d += 11f)
                        foreach (float s in new[] { -4.5f, 4.5f })
                            Palm(a + dir * d + side * s);
                }
            for (float zz = -330f; zz >= -500f; zz -= 12f) Palm(new Vector2(492f + R(-2f, 2f), zz));
            for (float xx = 520f; xx <= 640f; xx += 13f) Palm(new Vector2(xx, -312f + R(-2f, 2f)));
            Debug.Log($"[Island4] {placed} palms");
        }
    }
}
