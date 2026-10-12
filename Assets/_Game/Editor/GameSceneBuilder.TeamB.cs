using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Part B of the team split (Docs/18): islands 1 and 2, done as small edits to the saved Game scene (never a
    /// rebuild: the hotel and the islands carry hand fixes). Each step logs what it did to Logs/teamb-*.txt.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        /// <summary>Batch: B1-B6, the quick removals and the overlaps on islands 1 and 2.</summary>
        public static void TeamBRemovalsBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var log = new List<string>();

            // B1: Milo's jet ski stall on island 1 (Milo himself stays by his pier: the story needs him).
            Remove("Environment/RentalKiosk", log);

            // B2: the purple "TRAVEL" pads on the islands (travel stays in Esc > Travel and the dev commands; the
            // dev island keeps its own pad).
            Remove("Environment/DevIsland/TravelPad_Island1", log);
            Remove("Environment/DevIsland/TravelPad_Island2", log);
            Remove("Environment/Island3/TravelPad_Island3", log);

            // B3: island 1's parrots (the parrots belong to island 3 now: its sentries are creatures, not these).
            Remove("Environment/Island1Fun/Parrots", log);

            // B4: the coconut palms (island 1's shakeable palms, the only ones on islands 1 and 2).
            Transform env = GameObject.Find("Environment").transform;
            foreach (Transform palm in env.Cast<Transform>().Where(t => t.name == "palm_tall_Pivot").ToArray())
            {
                log.Add($"removed coconut palm at {palm.position}");
                Object.DestroyImmediate(palm.gameObject);
            }

            // B5: the lifeguard house on stilts comes down to the sand: its deck one step up, no stairs.
            LowerStiltHouse(env.Find("Tower"), log);

            // B6: towels (and their umbrellas) lying on or under a pier move off it onto the sand.
            MoveTowelsOffDocks(env, log);

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island1");
            BakeNavMeshes(only: "Island2");
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/teamb-removals.txt", log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TeamB] PASS: removals done ({log.Count} changes, Logs/teamb-removals.txt)\n" + string.Join("\n", log));
        }

        private static void Remove(string path, List<string> log)
        {
            Transform t = FindByPath(path);
            if (t == null) { log.Add("already gone: " + path); return; }
            log.Add($"removed {path} at {t.position}");
            Object.DestroyImmediate(t.gameObject);
        }

        private static float GroundUnder(Vector3 p)
        {
            var terrain = GameObject.Find("Environment/BeachTerrain").GetComponent<MeshCollider>();
            return terrain.Raycast(new Ray(new Vector3(p.x, 200f, p.z), Vector3.down), out RaycastHit hit, 400f) ? hit.point.y : 0f;
        }

        private static void LowerStiltHouse(Transform tower, List<string> log)
        {
            if (tower == null) { log.Add("no Tower"); return; }
            Transform deck = tower.Find("DeckCollision");
            float deckTop = deck.GetComponent<BoxCollider>().bounds.max.y;
            float ground = GroundUnder(deck.position);
            float drop = deckTop - (ground + 0.3f); // the porch one step (30 cm) above the sand
            if (drop < 0.5f) { log.Add($"Tower already low (deck top {deckTop:0.00})"); return; }
            tower.position += Vector3.down * drop;
            // The stairs and the stilts are under the sand now: their colliders go (the model's are hidden in the sand).
            foreach (Transform c in tower.Cast<Transform>().ToArray())
                if (c.name is "StairCollision" or "StairRail" or "StiltCollision") Object.DestroyImmediate(c.gameObject);
            log.Add($"Tower lowered {drop:0.00} m (deck top {deckTop:0.00} -> {deckTop - drop:0.00}), stairs removed");
        }

        private static void MoveTowelsOffDocks(Transform env, List<string> log)
        {
            Physics.SyncTransforms();
            // Every collider of the piers (the station dock, Milo's T pier, the hotel dock), as flat XZ boxes.
            var piers = new List<Bounds>();
            foreach (string dock in new[] { "Dock", "MilosRentalDock", "HotelDock" })
                if (env.Find(dock) is { } d)
                    piers.AddRange(d.GetComponentsInChildren<Collider>().Select(c => c.bounds));
            // A first try pushed this towel along Milo's cross dock into the sea: back to its beach spot.
            if (FindByPath("Environment/BeachTowels_Island1/Towel_6") is { } lost && lost.position.z < -10f)
                lost.position = new Vector3(33.6f, GroundUnder(new Vector3(33.6f, 0f, 5.1f)), 5.1f);
            foreach (string group in new[] { "BeachTowels_Island1", "BeachTowels_Island2" })
            {
                Transform towels = env.Find(group);
                if (towels == null) continue;
                foreach (Transform towel in towels)
                {
                    Vector3 home = towel.position;
                    Bounds b = RendererBounds(towel);
                    Vector3 offset = b.center - home;
                    b.Expand(new Vector3(0.6f, 0f, 0.6f));
                    if (!piers.Any(p => FlatOverlap(p, b))) continue;
                    // The nearest dry spot around it that's clear of every pier.
                    bool placed = false;
                    for (float r = 0.5f; r <= 8f && !placed; r += 0.5f)
                        for (int a = 0; a < 24 && !placed; a++)
                        {
                            float ang = a * Mathf.PI * 2f / 24f;
                            Vector3 p = home + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                            p.y = GroundUnder(p);
                            if (p.y < WaterLevel + 0.2f) continue;
                            var test = new Bounds(p + offset, b.size);
                            if (piers.Any(q => FlatOverlap(q, test))) continue;
                            towel.position = p;
                            placed = true;
                            log.Add($"moved {group}/{towel.name} off the pier: {home} -> {p}");
                        }
                    if (!placed) log.Add($"could not clear {group}/{towel.name} at {home}");
                }
            }
        }

        // ---------------------------------------------------------------- B8: island 2 smaller

        /// <summary>Batch: redraws the shared ground with island 2's smaller shape (<see cref="Island2HalfSize"/>),
        /// checks nothing else on the map changed height and nothing on island 2 is left over water, bakes island 2's
        /// navmesh again.</summary>
        public static void TeamBShrinkIsland2Batch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject terrain = GameObject.Find("Environment/BeachTerrain");
            Vector3[] before = terrain.GetComponent<MeshFilter>().sharedMesh.vertices;
            RefreshBeachTerrain(terrain);
            Vector3[] after = terrain.GetComponent<MeshFilter>().sharedMesh.vertices;
            var log = new List<string>();
            int changed = 0, elsewhere = 0;
            for (int i = 0; i < before.Length && i < after.Length; i++)
            {
                if (Mathf.Abs(before[i].y - after[i].y) < 0.02f) continue;
                changed++;
                Vector3 p = after[i];
                if (p.x < -200f || p.x > 240f || p.z < -520f || p.z > -180f)
                {
                    if (elsewhere++ < 10) log.Add($"WARNING height changed away from island 2 at {p} ({before[i].y:0.00} -> {p.y:0.00})");
                }
            }
            log.Add($"{changed} ground points changed, {elsewhere} of them away from island 2");
            if (before.Length != after.Length) log.Add($"WARNING vertex count {before.Length} -> {after.Length}");

            // Everything standing on island 2 must still have sand under it.
            Physics.SyncTransforms();
            foreach (string path in new[] { "Environment/Hotel", "Environment/BeachTowels_Island2", "Environment/HotelDock" })
                foreach (Transform t in FindByPath(path).Cast<Transform>())
                {
                    Vector3 p = t.position;
                    float ground = GroundUnder(p);
                    if (ground < WaterLevel + 0.2f && !path.EndsWith("Dock")) log.Add($"WARNING {path}/{t.name} at {p} is over water ({ground:0.00})");
                }

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island2");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/teamb-island2.txt", log);
            Debug.Log("[TeamB] PASS: island 2 reshaped\n" + string.Join("\n", log));
        }

        // ---------------------------------------------------------------- B9: island 2's pool

        // The pool east of the hotel. The ground is dug out (Island2Ground asks PoolDig) down to its floor, so the sea's
        // own water fills it: swimming, diving, drowning and rescues work there as in the sea, with no new water code.
        // Its edges sit on the terrain's 2 m grid: the slope from the deck down to the floor is inside the 2 m thick
        // tiled walls. Outer box x 52..72, z -258..-244; water x 54..70, z -256..-246 (16 x 10 m), 2.2 m deep.
        private static readonly Rect PoolOuter = new(52f, -258f, 20f, 14f);
        private const float PoolFloor = -2.55f;
        private static Rect PoolWater => new(PoolOuter.xMin + 2f, PoolOuter.yMin + 2f, PoolOuter.width - 4f, PoolOuter.height - 4f);

        /// <summary>The pool's floor height where (x, z) is inside the water (grid points on its edge included).</summary>
        private static float? PoolDig(float x, float z)
        {
            Rect w = PoolWater;
            return x > w.xMin - 0.5f && x < w.xMax + 0.5f && z > w.yMin - 0.5f && z < w.yMax + 0.5f ? PoolFloor : null;
        }

        /// <summary>Batch: digs the pool, builds its walls, steps, deck and loungers, bakes island 2's navmesh.</summary>
        public static void TeamBPoolBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform env = GameObject.Find("Environment").transform;
            GameObject terrain = GameObject.Find("Environment/BeachTerrain");
            RefreshBeachTerrain(terrain);
            Physics.SyncTransforms();

            if (env.Find("Island2Pool") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Island2Pool").transform;
            root.SetParent(env, false);
            TagSurface(root.gameObject, SurfaceKind.Rock);

            Rect o = PoolOuter, w = PoolWater;
            float deck = GroundUnder(new Vector3(o.xMin - 1f, 0f, o.center.y)); // the plateau beside it
            float top = deck + 0.12f;
            Material tile = GetMaterial("PoolTile", new Color(0.86f, 0.94f, 0.97f), smoothness: 0.75f);
            Material deep = GetMaterial("PoolTileBlue", new Color(0.18f, 0.62f, 0.78f), smoothness: 0.7f);
            Material stone = GetMaterial("PoolCoping", new Color(0.95f, 0.92f, 0.85f), smoothness: 0.3f);
            Material line = GetMaterial("PoolLaneLine", new Color(0.06f, 0.25f, 0.45f), smoothness: 0.6f);

            float wallH = top - PoolFloor;
            void Wall(string name, Vector3 centre, Vector3 size) =>
                Primitive(PrimitiveType.Cube, name, root, centre, size, tile);
            float midY = (top + PoolFloor) * 0.5f;
            Wall("WallNorth", new Vector3(o.center.x, midY, w.yMax + 1f), new Vector3(o.width, wallH, 2f));
            Wall("WallSouth", new Vector3(o.center.x, midY, w.yMin - 1f), new Vector3(o.width, wallH, 2f));
            Wall("WallWest", new Vector3(w.xMin - 1f, midY, w.center.y), new Vector3(2f, wallH, w.height));
            Wall("WallEast", new Vector3(w.xMax + 1f, midY, w.center.y), new Vector3(2f, wallH, w.height));
            // The coping: a stone lip round the water, a hand's width proud of the tiles.
            foreach ((string n, Vector3 c, Vector3 s) in new[]
            {
                ("CopingNorth", new Vector3(w.center.x, top + 0.04f, w.yMax + 0.3f), new Vector3(w.width + 1.2f, 0.08f, 0.6f)),
                ("CopingSouth", new Vector3(w.center.x, top + 0.04f, w.yMin - 0.3f), new Vector3(w.width + 1.2f, 0.08f, 0.6f)),
                ("CopingWest", new Vector3(w.xMin - 0.3f, top + 0.04f, w.center.y), new Vector3(0.6f, 0.08f, w.height)),
                ("CopingEast", new Vector3(w.xMax + 0.3f, top + 0.04f, w.center.y), new Vector3(0.6f, 0.08f, w.height)),
            })
                Primitive(PrimitiveType.Cube, n, root, c, s, stone);
            // The floor, blue tiles, with two dark lane lines.
            Primitive(PrimitiveType.Cube, "Floor", root, new Vector3(w.center.x, PoolFloor + 0.02f, w.center.y), new Vector3(w.width, 0.1f, w.height), deep);
            foreach (float dz in new[] { -2.5f, 2.5f })
                Primitive(PrimitiveType.Cube, "LaneLine", root, new Vector3(w.center.x, PoolFloor + 0.075f, w.center.y + dz), new Vector3(w.width - 2f, 0.01f, 0.25f), line, keepCollider: false);

            // Steps down into the water at the west end (and up out of it): 0.3 m each.
            int steps = Mathf.CeilToInt((top - (PoolFloor + 0.6f)) / 0.3f);
            for (int i = 0; i < steps; i++)
            {
                float stepTop = top - 0.3f * (i + 1);
                float x0 = w.xMin + i * 0.45f;
                Primitive(PrimitiveType.Cube, "Step", root, new Vector3(x0 + 0.225f, (stepTop + PoolFloor) * 0.5f, w.center.y),
                    new Vector3(0.45f, stepTop - PoolFloor, 3f), tile);
            }
            // Rails either side of the steps.
            foreach (float dz in new[] { -1.6f, 1.6f })
                Primitive(PrimitiveType.Cylinder, "StepRail", root, new Vector3(w.xMin + 0.9f, top + 0.45f, w.center.y + dz),
                    new Vector3(0.06f, 0.5f, 0.06f), GetMaterial("Chrome", new Color(0.85f, 0.87f, 0.9f), 0.9f, 0.85f), keepCollider: false);

            // A paved deck round the pool, loungers with parasols on the long sides.
            // The paving: four slabs round the tiled walls (not over the water).
            const float pave = 4f;
            foreach ((string n, Vector3 c, Vector3 s) in new[]
            {
                ("DeckNorth", new Vector3(o.center.x, deck + 0.03f, o.yMax + pave * 0.5f), new Vector3(o.width + pave * 2f, 0.06f, pave)),
                ("DeckSouth", new Vector3(o.center.x, deck + 0.03f, o.yMin - pave * 0.5f), new Vector3(o.width + pave * 2f, 0.06f, pave)),
                ("DeckWest", new Vector3(o.xMin - pave * 0.5f, deck + 0.03f, o.center.y), new Vector3(pave, 0.06f, o.height)),
                ("DeckEast", new Vector3(o.xMax + pave * 0.5f, deck + 0.03f, o.center.y), new Vector3(pave, 0.06f, o.height)),
            })
                Primitive(PrimitiveType.Cube, n, root, c, s, stone);
            foreach (float side in new[] { -1f, 1f })
                for (int i = 0; i < 3; i++)
                {
                    float x = o.xMin + 3f + i * 7f;
                    float z = side > 0 ? o.yMax + 2f : o.yMin - 2f;
                    BeachProp(root, $"Lounge{(side > 0 ? "N" : "S")}{i}", "resort_lounge_set", new Vector3(x, deck + 0.06f, z), side > 0 ? 180f : 0f);
                    BeachProp(root, $"Parasol{(side > 0 ? "N" : "S")}{i}", "resort_parasol", new Vector3(x + 2.4f, deck + 0.06f, z));
                }
            // The beach crowd walks round the pool, not into it.
            NavKeepOut(root, new Vector3(o.center.x, deck, o.center.y), new Vector3(o.width, 4f, o.height));

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island2");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TeamB] PASS: pool built (deck {deck:0.00}, water {WaterLevel}, floor {PoolFloor}, {steps} steps)");
        }

        // ---------------------------------------------------------------- B7: the sand of islands 1 and 2

        private static readonly Color BeachSandColour = new(0.96f, 0.87f, 0.67f);

        /// <summary>Batch: islands 1 and 2 get island 3's sand scan (grain, ripples, bumps) instead of the flat colour,
        /// with wet, darker sand along the waterline.</summary>
        public static void TeamBSandBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject terrain = GameObject.Find("Environment/BeachTerrain");
            ShadeBeachSand(terrain.GetComponent<MeshFilter>().sharedMesh);
            terrain.GetComponent<MeshRenderer>().sharedMaterial = BeachSandMaterial();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[TeamB] PASS: beach sand textured");
        }

        private static Material BeachSandMaterial()
        {
            Material mat = Island3SurfaceMaterial("BeachSand", "Ground");
            mat.SetTexture("_SandD", Island3Scan("Sand_D"));
            mat.SetTexture("_SandN", Island3Scan("Sand_N"));
            mat.SetColor("_SandColor", BeachSandColour);
            mat.SetVector("_Scales", new Vector4(4.5f, 3f, 4f, 5f));
            mat.SetVector("_Contrast", new Vector4(0.8f, 1f, 1f, 1f));
            mat.SetFloat("_NormalStrength", 1.1f);
            mat.SetFloat("_VertexColor", 1f);
            mat.SetFloat("_VertexScale", 2f);
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Occlusion", 0.6f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>The shared terrain's vertex shade (0.5 = the sand colour): wet and darker from just above the
        /// waterline down, broad soft patches on the dry sand. uv1 (the other layers) is zero: all sand.</summary>
        private static void ShadeBeachSand(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            var colours = new Color[v.Length];
            var layers = new Vector4[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 p = v[i];
                float patches = (Mathf.PerlinNoise(p.x * 0.045f + 31.7f, p.z * 0.045f + 7.3f) - 0.5f) * 0.07f
                              + (Mathf.PerlinNoise(p.x * 0.17f + 3.1f, p.z * 0.17f + 91.4f) - 0.5f) * 0.03f;
                float dry = 0.5f + patches;
                float wet = 0.27f + patches * 0.5f; // damp sand at and under the water
                float t = Mathf.InverseLerp(WaterLevel - 0.15f, WaterLevel + 0.45f, p.y);
                float shade = Mathf.Lerp(wet, dry, t * t * (3f - 2f * t));
                colours[i] = new Color(shade, shade * 0.99f, shade * 0.97f, 1f);
            }
            mesh.colors = colours;
            Debug.Log($"[TeamB] sand shade: {v.Length} vertices, {colours.Count(c => c.r < 0.4f)} wet, y {v.Min(p => p.y):0.0}..{v.Max(p => p.y):0.0}, at (0,-12): {v.Where(p => Mathf.Abs(p.x) < 1.1f && Mathf.Abs(p.z + 12f) < 1.1f).Select(p => p.y).DefaultIfEmpty(99f).First():0.00}");
            mesh.SetUVs(1, layers);
            EditorUtility.SetDirty(mesh);
        }

        private static Bounds RendererBounds(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(t.position, Vector3.one);
            Bounds b = rs[0].bounds;
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static bool FlatOverlap(Bounds a, Bounds b) =>
            a.min.x < b.max.x && a.max.x > b.min.x && a.min.z < b.max.z && a.max.z > b.min.z;
    }
}
