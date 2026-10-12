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

        /// <summary>Batch: B5 as Shako meant it (his screenshot, 2026-10-12): Sandy's house is the Lost &amp; Found shack.
        /// Its plank base (the model's bottom 0.83 m, up to the floor inside) goes under the sand: the whole shack comes
        /// down so its floor sits just above the sand, its step goes; the alarm bell (on its own post) stays put, and
        /// Sandy (who sits at her saved spot in the booth) comes down with her stool.</summary>
        public static void TeamBSandyHouseBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform shack = FindByPath("Environment/Station_Shack") ?? throw new InvalidOperationException("No shack");
            Transform sandy = FindByPath("Story/Npc_Sandy");
            const float FloorInside = 0.575f * ShackScale; // the model's floor height (GameSceneBuilder: shelf etc. stand on it)
            // The highest sand under the shack's footprint: the floor stays 4 cm over it.
            float sand = float.MinValue;
            Vector3 c = shack.position + new Vector3(0.4f, 0f, -0.4f);
            for (float dx = -1.6f; dx <= 1.6f; dx += 0.4f)
                for (float dz = -1.4f; dz <= 1.4f; dz += 0.4f)
                    sand = Mathf.Max(sand, GroundUnder(c + new Vector3(dx, 0f, dz)));
            float floorNow = shack.position.y + FloorInside;
            float drop = floorNow - (sand + 0.04f);
            if (drop < 0.2f) { Debug.Log($"[TeamB] PASS: Sandy's house already down (floor {floorNow:0.00}, sand {sand:0.00})"); return; }
            Transform bell = shack.Find("AlarmBell");
            Vector3 bellAt = bell != null ? bell.position : Vector3.zero;
            shack.position += Vector3.down * drop;
            if (bell != null) bell.position = bellAt;
            if (shack.Find("StepsCollision") is { } steps) Object.DestroyImmediate(steps.gameObject);
            if (sandy != null) sandy.position += Vector3.down * drop;
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island1");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: Sandy's house down {drop:0.00} m (floor {floorNow:0.00} -> {floorNow - drop:0.00}, sand {sand:0.00}), Sandy at {(sandy != null ? sandy.position : Vector3.zero)}");
        }

        /// <summary>Batch: every beach towel (islands 1 and 2) lies on the sand under it again: after island 2 was
        /// reshaped (B8) they kept their old heights and tilts, half sunk or tipped. Each towel follows the slope under
        /// it (its own yaw kept), 2 cm over the sand; umbrellas stand upright on the sand at their own spot.</summary>
        public static void TeamBTowelsOnSandBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var terrain = GameObject.Find("Environment/BeachTerrain").GetComponent<MeshCollider>();
            var log = new List<string>();
            foreach (string group in new[] { "Environment/BeachTowels_Island1", "Environment/BeachTowels_Island2" })
            {
                Transform towels = FindByPath(group);
                if (towels == null) continue;
                foreach (Transform towel in towels)
                {
                    Vector3 p = towel.position;
                    if (!terrain.Raycast(new Ray(new Vector3(p.x, p.y + 50f, p.z), Vector3.down), out RaycastHit hit, 100f)) continue;
                    float yaw = towel.eulerAngles.y;
                    // Children keep their places on the towel; the root lies on the sand, tipped to its slope.
                    var kids = towel.Cast<Transform>().Select(k => (k, k.position, k.rotation)).ToList();
                    Vector3 before = towel.position;
                    towel.SetPositionAndRotation(hit.point + hit.normal * 0.02f,
                        Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, yaw, 0f));
                    foreach ((Transform k, Vector3 at, Quaternion rot) in kids)
                    {
                        if (k.name == "Umbrella")
                        {
                            // Upright, its foot on the sand where it stands.
                            Vector3 u = at;
                            if (terrain.Raycast(new Ray(new Vector3(u.x, u.y + 50f, u.z), Vector3.down), out RaycastHit uh, 100f))
                                k.SetPositionAndRotation(new Vector3(u.x, uh.point.y, u.z), Quaternion.Euler(0f, rot.eulerAngles.y, 0f));
                        }
                        else
                        {
                            // The towel cloth: flat on the root (no leftover tilt), at the root's height.
                            k.localPosition = new Vector3(k.localPosition.x, 0f, k.localPosition.z);
                            k.localRotation = Quaternion.Euler(0f, k.localEulerAngles.y, 0f);
                        }
                    }
                    log.Add($"{group}/{towel.name}: {before} -> {towel.position} (slope {Vector3.Angle(hit.normal, Vector3.up):0} deg)");
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TeamB] PASS: towels on the sand\n" + string.Join("\n", log));
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

        // The pool east of the hotel. The ground is dug out (Island2Ground asks PoolDig) down to its floor, just above the
        // sea (so the sea's surface stays hidden under it); the water is the pool's own, 15 cm under the edge
        // (World/Water/PoolWater: WaterSurface answers its level there, so swimming, drowning and rescues work as in the
        // sea). Its edges sit on the terrain's 2 m grid: the slope from the deck down to the floor is inside the 2 m thick
        // tiled walls. Outer box x 52..72, z -258..-244; water x 54..70, z -256..-246 (16 x 10 m), about 1.6 m deep.
        private static readonly Rect PoolOuter = new(52f, -258f, 20f, 14f);
        private const float PoolFloor = -0.3f;
        private const float PoolWaterBelowEdge = 0.15f;
        private static Rect PoolWater => new(PoolOuter.xMin + 2f, PoolOuter.yMin + 2f, PoolOuter.width - 4f, PoolOuter.height - 4f);

        /// <summary>Clear turquoise pool water, both sides drawn (seen from under it too).</summary>
        private static Material PoolWaterMaterial()
        {
            Material mat = LoadOrCreateMaterial("PoolWaterSurface", Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.22f, 0.72f, 0.86f, 0.55f));
            mat.SetFloat("_Smoothness", 0.95f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

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
            // The paving: four slabs round the tiled walls, solid down into the sand (a raised terrace where it slopes).
            const float pave = 4f;
            foreach ((string n, Vector3 c, Vector3 s) in new[]
            {
                ("DeckNorth", new Vector3(o.center.x, (deck - 0.54f) * 0.5f, o.yMax + pave * 0.5f), new Vector3(o.width + pave * 2f, deck + 0.66f, pave)),
                ("DeckSouth", new Vector3(o.center.x, (deck - 0.54f) * 0.5f, o.yMin - pave * 0.5f), new Vector3(o.width + pave * 2f, deck + 0.66f, pave)),
                ("DeckWest", new Vector3(o.xMin - pave * 0.5f, (deck - 0.54f) * 0.5f, o.center.y), new Vector3(pave, deck + 0.66f, o.height)),
                ("DeckEast", new Vector3(o.xMax + pave * 0.5f, (deck - 0.54f) * 0.5f, o.center.y), new Vector3(pave, deck + 0.66f, o.height)),
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
            // The water: a clear turquoise surface just under the edge, and the pool's water for the game (PoolWater).
            float waterLevel = deck - PoolWaterBelowEdge;
            GameObject surface = Primitive(PrimitiveType.Quad, "PoolWaterSurface", root, new Vector3(w.center.x, waterLevel, w.center.y),
                new Vector3(w.width, w.height, 1f), PoolWaterMaterial(), keepCollider: false);
            surface.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            surface.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var water = new GameObject("PoolWater").AddComponent<PleaseDontDrown.World.Water.PoolWater>();
            water.transform.SetParent(root, false);
            water.transform.position = new Vector3(w.center.x, waterLevel, w.center.y);
            SetField(water, "_size", p => p.vector2Value = new Vector2(w.width, w.height));

            // The beach crowd walks round the pool, not into it.
            NavKeepOut(root, new Vector3(o.center.x, deck, o.center.y), new Vector3(o.width, 4f, o.height));

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island2");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TeamB] PASS: pool built (deck {deck:0.00}, water {WaterLevel}, floor {PoolFloor}, {steps} steps)");
        }

        // ---------------------------------------------------------------- B10: island 2's golf cart road

        // A 3.5 m cart road in a U: from beside the hotel's front, west past the bar, round the back of the island and
        // up to the pool's south side. Paved in 2 m tiles laid on the sand (they follow the ground), a dashed middle line,
        // and a parking bay with a marker at each end (CartParkingHotel, CartParkingPool) for the golf cart (A9, C11).
        private static readonly Vector2[] CartRoad =
        {
            new(-12f, -235f), new(-70f, -235f), new(-70f, -306f), new(62f, -306f), new(62f, -268f), new(50f, -268f),
        };
        private const float CartRoadWidth = 3.5f;

        /// <summary>Batch: lays the cart road and its parking bays on island 2.</summary>
        public static void TeamBCartRoadBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform env = GameObject.Find("Environment").transform;
            if (env.Find("Island2CartRoad") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Island2CartRoad").transform;
            root.SetParent(env, false);
            Material paving = GetMaterial("CartRoad", new Color(0.66f, 0.63f, 0.58f), smoothness: 0.15f);
            Material kerb = GetMaterial("CartRoadKerb", new Color(0.93f, 0.91f, 0.86f), smoothness: 0.2f);
            Material paint = GetMaterial("CartRoadPaint", new Color(0.97f, 0.96f, 0.9f), smoothness: 0.3f);
            int tiles = 0;
            for (int s = 0; s < CartRoad.Length - 1; s++)
            {
                Vector2 a = CartRoad[s], b = CartRoad[s + 1];
                Vector2 dir = (b - a).normalized;
                float length = (b - a).magnitude;
                // Run each leg on half a road width past its corners, so the corners are filled.
                float from = s == 0 ? 0f : -CartRoadWidth * 0.5f, to = s == CartRoad.Length - 2 ? length : length + CartRoadWidth * 0.5f;
                Quaternion yaw = Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y));
                for (float d = from; d < to - 0.01f; d += 2f)
                {
                    float piece = Mathf.Min(2f, to - d);
                    Vector2 c = a + dir * (d + piece * 0.5f);
                    float ground = GroundUnder(new Vector3(c.x, 0f, c.y));
                    // A slab from under the sand up to 4 cm proud of it; kerbs either side.
                    GameObject tile = Primitive(PrimitiveType.Cube, "Paving", root, new Vector3(c.x, ground - 0.13f, c.y),
                        new Vector3(CartRoadWidth, 0.34f, piece + 0.02f), paving, keepCollider: false);
                    tile.transform.rotation = yaw;
                    bool corner = (s > 0 && d < CartRoadWidth * 0.5f) || (s < CartRoad.Length - 2 && d + piece > length - CartRoadWidth * 0.5f);
                    if (!corner)
                    foreach (float side in new[] { -1f, 1f })
                    {
                        Vector3 edge = new Vector3(c.x, 0f, c.y) + yaw * Vector3.right * side * (CartRoadWidth * 0.5f + 0.1f);
                        Primitive(PrimitiveType.Cube, "Kerb", root, new Vector3(edge.x, ground - 0.1f, edge.z), new Vector3(0.2f, 0.36f, piece + 0.02f), kerb, keepCollider: false)
                            .transform.rotation = yaw;
                    }
                    if (Mathf.Repeat(d, 4f) < 1f && d > 0f && d < length - 2f)
                        Primitive(PrimitiveType.Cube, "Dash", root, new Vector3(c.x, ground + 0.045f, c.y), new Vector3(0.15f, 0.01f, 1.2f), paint, keepCollider: false)
                            .transform.rotation = yaw;
                    tiles++;
                }
            }
            // Parking bays: a widened pad with white bay lines and a marker where a cart waits.
            ParkingBay(root, "CartParkingHotel", CartRoad[0], (CartRoad[0] - CartRoad[1]).normalized, paving, paint);
            ParkingBay(root, "CartParkingPool", CartRoad[^1], (CartRoad[^1] - CartRoad[^2]).normalized, paving, paint);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: cart road laid ({tiles} tiles, {CartRoad.Length - 1} legs)");
        }

        private static void ParkingBay(Transform root, string name, Vector2 end, Vector2 outward, Material paving, Material paint)
        {
            var bay = new GameObject(name).transform;
            bay.SetParent(root, false);
            Vector2 c = end + outward * 2.5f;
            float ground = GroundUnder(new Vector3(c.x, 0f, c.y));
            bay.position = new Vector3(c.x, ground + 0.05f, c.y);
            bay.rotation = Quaternion.LookRotation(new Vector3(-outward.x, 0f, -outward.y)); // a parked cart faces back up the road
            Primitive(PrimitiveType.Cube, "BayPaving", bay, new Vector3(0f, -0.18f, 0f), new Vector3(7f, 0.34f, 5f), paving, keepCollider: false);
            foreach (float x in new[] { -3.4f, 0f, 3.4f })
                Primitive(PrimitiveType.Cube, "BayLine", bay, new Vector3(x, 0f, 0f), new Vector3(0.12f, 0.01f, 4.6f), paint, keepCollider: false);
        }

        // ---------------------------------------------------------------- B11: the hotel bar's look

        /// <summary>Batch: the beach bar dressed in the hotel's colours (teal, cream, gold): a "CORAL BAR" sign under the
        /// front beam, warm string lights along the eaves, palm planters at the deck's corners. Look only: the bar model,
        /// its colliders, the barista and the serving spots are untouched (orders are A6).</summary>
        public static void TeamBBarLookBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform bar = FindByPath("Environment/Hotel/HotelBeachBar") ?? throw new InvalidOperationException("No hotel bar");
            if (bar.Find("BarDress") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("BarDress").transform;
            root.SetParent(bar, false);
            root.position = Vector3.zero; root.rotation = Quaternion.identity; root.localScale = Vector3.one;

            Bounds model = RendererBounds(bar.Find("ColouredTripoBar"));
            float deckTop = RendererBounds(bar.Find("DeckSkirt")).max.y;
            Bounds deckBox = RendererBounds(bar.Find("DeckSkirt"));
            float front = deckBox.max.z, back = deckBox.min.z, west = deckBox.min.x, east = deckBox.max.x, mid = model.center.x;
            float beam = deckTop + 3.35f; // under the front and back beams (the model's posts carry them about 3.5 m up)
            // The eaves: find the roof's underside along the front from the model's colliders/renderers by a ray up.
            float eave = deckTop + 3.1f;
            if (Physics.Raycast(new Vector3(mid, deckTop + 0.5f, front - 1.2f), Vector3.up, out RaycastHit roofHit, 8f)) eave = roofHit.point.y - 0.15f;

            Material teal = GetMaterial("CoralBarTeal", new Color(0.09f, 0.42f, 0.46f), smoothness: 0.45f);
            Material gold = GetMaterial("CoralBarGold", new Color(1f, 0.78f, 0.38f), 1f, 0.78f);
            Material wire = GetMaterial("CoralBarWire", new Color(0.08f, 0.07f, 0.06f));
            Material[] bulbs =
            {
                GetMaterial("CoralBarBulbWarm", new Color(1f, 0.82f, 0.5f), emission: new Color(1f, 0.7f, 0.35f) * 2.2f),
                GetMaterial("CoralBarBulbCoral", new Color(1f, 0.55f, 0.45f), emission: new Color(1f, 0.42f, 0.32f) * 2f),
                GetMaterial("CoralBarBulbTeal", new Color(0.5f, 0.95f, 0.9f), emission: new Color(0.3f, 0.85f, 0.8f) * 1.8f),
            };

            // The sign: a teal board with a gold frame, hung under the front beam, lettering on both faces.
            var sign = new GameObject("CoralBarSign").transform;
            sign.SetParent(root, false);
            sign.position = new Vector3(mid, eave - 0.55f, front - 0.6f);
            Primitive(PrimitiveType.Cube, "Board", sign, Vector3.zero, new Vector3(4.2f, 0.85f, 0.12f), teal, keepCollider: false);
            foreach ((Vector3 at, Vector3 size) in new[]
            {
                (new Vector3(0f, 0.45f, 0f), new Vector3(4.36f, 0.08f, 0.16f)), (new Vector3(0f, -0.45f, 0f), new Vector3(4.36f, 0.08f, 0.16f)),
                (new Vector3(-2.14f, 0f, 0f), new Vector3(0.08f, 0.98f, 0.16f)), (new Vector3(2.14f, 0f, 0f), new Vector3(0.08f, 0.98f, 0.16f)),
            })
                Primitive(PrimitiveType.Cube, "Frame", sign, at, size, gold, keepCollider: false);
            foreach (float x in new[] { -1.6f, 1.6f })
                Primitive(PrimitiveType.Cylinder, "Chain", sign, new Vector3(x, 0.75f, 0f), new Vector3(0.03f, 0.3f, 0.03f), gold, keepCollider: false);
            foreach (float face in new[] { 1f, -1f })
            {
                TextMesh text = WorldText(sign, "Lettering", new Vector3(0f, 0f, 0.07f * face), "CORAL BAR", 96, 0.045f, gold.color);
                text.transform.localRotation = Quaternion.Euler(0f, face > 0 ? 180f : 0f, 0f);
            }

            // String lights along the front and back eaves: a sagging wire with bulbs every 0.9 m.
            int bulbCount = 0;
            foreach ((string name, float z) in new[] { ("LightsFront", front - 0.4f), ("LightsBack", back + 0.4f) })
            {
                var strand = new GameObject(name).transform;
                strand.SetParent(root, false);
                Vector3 a = new(west + 1f, beam, z), b = new(east - 1f, beam, z);
                const int spans = 3; // sags between four hooks
                for (int sp = 0; sp < spans; sp++)
                {
                    Vector3 p0 = Vector3.Lerp(a, b, sp / (float)spans), p1 = Vector3.Lerp(a, b, (sp + 1) / (float)spans);
                    Vector3 Sag(float t) => Vector3.Lerp(p0, p1, t) + Vector3.down * (0.35f * 4f * t * (1f - t));
                    int n = Mathf.Max(2, Mathf.RoundToInt((p1 - p0).magnitude / 0.9f));
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 q0 = Sag(i / (float)n), q1 = Sag((i + 1) / (float)n);
                        GameObject seg = Primitive(PrimitiveType.Cylinder, "Wire", strand, (q0 + q1) * 0.5f, new Vector3(0.02f, (q1 - q0).magnitude * 0.5f, 0.02f), wire, keepCollider: false);
                        seg.transform.up = (q1 - q0).normalized;
                        Primitive(PrimitiveType.Sphere, "Bulb", strand, q0 + Vector3.down * 0.09f, Vector3.one * 0.13f, bulbs[bulbCount % bulbs.Length], keepCollider: false);
                        bulbCount++;
                    }
                }
            }
            // Warm light from the strands at night (soft, no shadows).
            foreach (float x in new[] { mid - 7f, mid, mid + 7f })
            {
                var lamp = new GameObject("StrandGlow").AddComponent<Light>();
                lamp.transform.SetParent(root, false);
                lamp.transform.position = new Vector3(x, beam - 0.6f, (front + back) * 0.5f);
                lamp.type = LightType.Point; lamp.range = 9f; lamp.intensity = 1.2f; lamp.color = new Color(1f, 0.78f, 0.5f);
                lamp.shadows = LightShadows.None;
            }

            // Palms in planters at the deck's four corners.
            foreach (Vector3 corner in new[]
            {
                new Vector3(west + 1.3f, deckTop, front - 1.1f), new Vector3(east - 1.3f, deckTop, front - 1.1f),
                new Vector3(west + 1.3f, deckTop, back + 1.1f), new Vector3(east - 1.3f, deckTop, back + 1.1f),
            })
                BeachProp(root, "CornerPlanter", "resort_planter", corner, 90f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: bar dressed (eave {eave:0.00}, deck {deckTop:0.00}, {bulbCount} bulbs)");
        }

        // ---------------------------------------------------------------- B12: the casino's interior

        /// <summary>Batch: the casino (hotel west wing) gets its Tripo pieces: the six box slot machines (their own
        /// combined meshes) become the Tripo one-armed bandit on the same stands; a Tripo roulette table in the back half;
        /// two crystal chandeliers; velvet ropes inside the door; a champagne tower on a gold table. The blackjack table,
        /// its colliders and everything A7 will use stay as they are.</summary>
        public static void TeamBCasinoBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform lobby = FindByPath("Environment/Hotel/LobbyLuxury") ?? throw new InvalidOperationException("No lobby");
            var log = new List<string>();

            // The box slots out, the Tripo ones onto the stands (facing the stools, +x).
            foreach (Transform t in lobby.Cast<Transform>().Where(t => t.name.StartsWith("Combined_CasinoSlot")).ToArray())
            {
                log.Add("removed " + t.name);
                Object.DestroyImmediate(t.gameObject);
            }
            foreach (Transform t in lobby.Cast<Transform>().Where(t => t.name == "Tripo_slot_machine").ToArray()) Object.DestroyImmediate(t.gameObject);
            foreach (Transform stand in lobby.Cast<Transform>().Where(t => t.name == "SlotBase").ToArray())
            {
                Bounds b = stand.GetComponent<Renderer>().bounds;
                GameObject slot = TripoSlot(lobby, lobby.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z)), 90f);
                if (slot == null) throw new InvalidOperationException("Tripo slot machine missing");
                log.Add($"slot on {b.center} ({RendererBounds(slot.transform).size})");
            }

            if (lobby.Find("CasinoTripo") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("CasinoTripo").transform;
            root.SetParent(lobby, false);
            root.position = Vector3.zero; root.rotation = Quaternion.identity;
            float floor = RendererBounds(lobby.Find("Combined_CasinoCarpet")).max.y;
            float ceiling = 4.95f;
            Material gold = GetMaterial("LobbyGold", new Color(1f, .76f, .33f), .85f, .62f);
            Material black = GetMaterial("CasinoBlack", new Color(.025f, .025f, .03f), 0f, .85f);

            // Roulette in the back half, its long side along the room.
            GameObject roulette = TripoPiece(root, "roulette_table", new Vector3(3.5f, floor, -258.6f), 90f, log, longest: 2.6f);
            Bounds rb = RendererBounds(roulette.transform);
            var rc = new GameObject("RouletteCollider").AddComponent<BoxCollider>();
            rc.transform.SetParent(root, false);
            rc.transform.position = rb.center; rc.size = rb.size;

            // The casino's two drawn chandeliers (the lobby's style) give way to the Tripo crystal ones; the ceiling roses stay.
            var casinoChandeliers = new[] { new Vector3(2.2f, 0f, -248.6f), new Vector3(2.2f, 0f, -258.4f) };
            foreach (Transform t in lobby.Cast<Transform>().Where(t => t.name.StartsWith("Chandelier")).ToArray())
                if (casinoChandeliers.Any(c => new Vector2(t.position.x - c.x, t.position.z - c.z).magnitude < 1.3f))
                    Object.DestroyImmediate(t.gameObject);
            foreach (Vector3 at in casinoChandeliers)
            {
                GameObject chandelier = TripoPiece(root, "crystal_chandelier", new Vector3(at.x, 0f, at.z), 0f, log, height: 1.3f);
                Bounds cb = RendererBounds(chandelier.transform);
                chandelier.transform.position += Vector3.up * (ceiling - 0.12f - cb.max.y);
                var glow = new GameObject("ChandelierLight").AddComponent<Light>();
                glow.transform.SetParent(root, false);
                glow.transform.position = new Vector3(at.x, ceiling - 1.1f, at.z);
                glow.type = LightType.Point; glow.range = 7f; glow.intensity = 1.6f; glow.color = new Color(1f, 0.86f, 0.62f);
                glow.shadows = LightShadows.None;
            }

            // Velvet ropes inside the door, either side of the way in.
            foreach (float z in new[] { -247.2f, -251.0f })
                TripoPiece(root, "velvet_rope_stanchions", new Vector3(6.2f, floor, z), 0f, log, longest: 1.9f);

            // Champagne tower on a round black-and-gold table in the corner by the cash wall.
            Vector3 tableAt = new(6.6f, floor, -260.6f);
            Primitive(PrimitiveType.Cylinder, "ChampagneTableLeg", root, tableAt + Vector3.up * 0.37f, new Vector3(0.12f, 0.37f, 0.12f), gold);
            Primitive(PrimitiveType.Cylinder, "ChampagneTableTop", root, tableAt + Vector3.up * 0.76f, new Vector3(0.9f, 0.02f, 0.9f), black);
            Primitive(PrimitiveType.Cylinder, "ChampagneTableRim", root, tableAt + Vector3.up * 0.74f, new Vector3(0.94f, 0.015f, 0.94f), gold, keepCollider: false);
            TripoPiece(root, "champagne_tower", tableAt + Vector3.up * 0.78f, 0f, log, height: 0.75f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/teamb-casino.txt", log);
            Debug.Log("[TeamB] PASS: casino dressed\n" + string.Join("\n", log));
        }

        /// <summary>A Tripo island 2 model scaled to a height or a longest side, standing with its bottom middle on <paramref name="foot"/>.</summary>
        private static GameObject TripoPiece(Transform parent, string model, Vector3 foot, float yaw, List<string> log, float height = 0f, float longest = 0f)
        {
            GameObject prefab = TripoIsland2(model) ?? throw new InvalidOperationException("Tripo model missing: " + model);
            GameObject go = Object.Instantiate(prefab, parent, false);
            go.name = "Tripo_" + model;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.position = Vector3.zero;
            Bounds b = RendererBounds(go.transform);
            float scale = height > 0f ? height / b.size.y : longest / Mathf.Max(b.size.x, b.size.z);
            go.transform.localScale *= scale;
            b = RendererBounds(go.transform);
            go.transform.position += foot - new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
            log.Add($"{model}: scale {scale:0.###}, size {RendererBounds(go.transform).size}");
            return go;
        }

        // ---------------------------------------------------------------- B13: island 2's gun shop

        /// <summary>Batch: "Coral Arms", a walk-up gun boutique between the bar and the hotel, facing the beach: cream
        /// walls, a teal awning, gold sign, the guns on a dark wall behind the window. Its counter is a copy of the
        /// reception's shop counter (same guns and prices) that opens and closes with the reception (ShopFollowsAnother),
        /// so the story's "buy the pistol" step works at either.</summary>
        public static void TeamBGunShopBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform env = GameObject.Find("Environment").transform;
            if (env.Find("Island2GunShop") is { } old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("Island2GunShop").transform;
            root.SetParent(env, false);
            Vector3 centre = new(-20f, 0f, -245.5f);
            centre.y = GroundUnder(centre);
            root.position = centre;
            TagSurface(root.gameObject, SurfaceKind.Wood);

            Material cream = GetMaterial("GunShopCream", new Color(.93f, .85f, .71f), smoothness: .12f);
            Material teal = GetMaterial("CoralBarTeal", new Color(0.09f, 0.42f, 0.46f), smoothness: 0.45f);
            Material gold = GetMaterial("CoralBarGold", new Color(1f, 0.78f, 0.38f), 1f, 0.78f);
            Material dark = GetMaterial("GunShopWalnut", new Color(.16f, .09f, .05f), smoothness: .5f);
            Material floorMat = GetMaterial("GunShopFloor", new Color(.55f, .5f, .45f), smoothness: .3f);
            GameObject Box(string n, Vector3 p, Vector3 s, Material m, bool solid = true) => Primitive(PrimitiveType.Cube, n, root, p, s, m, solid);

            // Shell (local: x across, z toward the beach = +z, y up from the sand). 10 x 5 m, 3.6 m tall.
            const float w = 10f, d = 5f, h = 3.6f, winL = -3f, winR = 3f, sill = 1.15f, head = 2.75f;
            Box("Floor", new Vector3(0f, 0.1f, 0f), new Vector3(w, 0.2f, d), floorMat);
            Box("WallBack", new Vector3(0f, h * 0.5f, -d * 0.5f + 0.1f), new Vector3(w, h, 0.2f), cream);
            Box("WallWest", new Vector3(-w * 0.5f + 0.1f, h * 0.5f, 0f), new Vector3(0.2f, h, d), cream);
            Box("WallEast", new Vector3(w * 0.5f - 0.1f, h * 0.5f, 0f), new Vector3(0.2f, h, d), cream);
            Box("FrontWest", new Vector3((-w * 0.5f + winL) * 0.5f, h * 0.5f, d * 0.5f - 0.1f), new Vector3(winL + w * 0.5f, h, 0.2f), cream);
            Box("FrontEast", new Vector3((w * 0.5f + winR) * 0.5f, h * 0.5f, d * 0.5f - 0.1f), new Vector3(w * 0.5f - winR, h, 0.2f), cream);
            Box("FrontBelow", new Vector3(0f, sill * 0.5f, d * 0.5f - 0.1f), new Vector3(winR - winL, sill, 0.2f), cream);
            Box("FrontAbove", new Vector3(0f, (head + h) * 0.5f, d * 0.5f - 0.1f), new Vector3(winR - winL, h - head, 0.2f), cream);
            Box("Roof", new Vector3(0f, h + 0.1f, 0f), new Vector3(w + 0.4f, 0.2f, d + 0.4f), cream);
            Box("RoofTrim", new Vector3(0f, h + 0.22f, d * 0.5f + 0.2f), new Vector3(w + 0.44f, 0.06f, 0.06f), gold, false);
            // Inside: walnut panelling on the back wall, a gold rail, the guns hung on it.
            Box("Panelling", new Vector3(0f, 1.9f, -d * 0.5f + 0.22f), new Vector3(w - 0.6f, 2.4f, 0.04f), dark, false);
            foreach (float y in new[] { 1.45f, 2.25f })
                Box("GunRail", new Vector3(0f, y - 0.12f, -d * 0.5f + 0.26f), new Vector3(w - 1.2f, 0.03f, 0.04f), gold, false);
            string[] guns = { "pistol", "smg", "shotgun", "rifle", "sniper", "pistol" };
            for (int i = 0; i < guns.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Game/Art/Weapons/{guns[i]}.glb");
                if (prefab == null) continue;
                GameObject gun = Object.Instantiate(prefab, root, false);
                gun.name = "Display_" + guns[i];
                Vector3 at = new(-3.2f + (i % 3) * 3.2f, i < 3 ? 2.3f : 1.5f, -d * 0.5f + 0.34f);
                // Lay it along the wall: its longest side across (x), side on to the customers.
                gun.transform.localPosition = Vector3.zero;
                gun.transform.localRotation = Quaternion.identity;
                Bounds gb = RendererBounds(gun.transform);
                Vector3 sz = gb.size;
                if (sz.y >= sz.x && sz.y >= sz.z) gun.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                else if (sz.z >= sz.x) gun.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                gb = RendererBounds(gun.transform);
                float length = guns[i] switch { "pistol" => 0.32f, "smg" => 0.6f, "shotgun" => 1.05f, "rifle" => 0.95f, _ => 1.25f };
                float fit = length / Mathf.Max(gb.size.x, 0.01f); // real length
                gun.transform.localScale *= fit;
                gb = RendererBounds(gun.transform);
                gun.transform.position += root.TransformPoint(at) - gb.center;
            }
            var shopLight = new GameObject("ShopLight").AddComponent<Light>();
            shopLight.transform.SetParent(root, false);
            shopLight.transform.localPosition = new Vector3(0f, 3.1f, 0f);
            shopLight.type = LightType.Point; shopLight.range = 7f; shopLight.intensity = 1.3f; shopLight.color = new Color(1f, 0.88f, 0.7f);
            shopLight.shadows = LightShadows.None;

            // The awning over the window, the sign above it.
            GameObject awning = Box("Awning", new Vector3(0f, head + 0.35f, d * 0.5f + 0.75f), new Vector3(winR - winL + 1.2f, 0.08f, 1.6f), teal, false);
            awning.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            Box("AwningEdge", new Vector3(0f, head + 0.13f, d * 0.5f + 1.53f), new Vector3(winR - winL + 1.2f, 0.12f, 0.04f), gold, false);
            Box("SignBoard", new Vector3(0f, h + 0.55f, d * 0.5f + 0.1f), new Vector3(4.6f, 0.5f, 0.06f), teal, false);
            WorldText(root, "SignText", new Vector3(0f, h + 0.55f, d * 0.5f + 0.14f), "CORAL ARMS", 96, 0.04f, gold.color)
                .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // The counter: a copy of the reception's shop desk, in the window.
            Transform reception = FindByPath("Environment/Hotel/ReceptionDesk") ?? throw new InvalidOperationException("No reception desk");
            var receptionShop = reception.GetComponent<PleaseDontDrown.Story.ShopCounter>();
            GameObject counter = Object.Instantiate(reception.gameObject, root, false);
            counter.name = "GunShopCounter";
            foreach (Transform child in counter.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
            counter.transform.localRotation = Quaternion.identity;
            counter.transform.localScale = new Vector3(winR - winL, 0.1f, 0.6f);
            counter.transform.localPosition = new Vector3(0f, sill + 0.05f, d * 0.5f + 0.1f);
            counter.GetComponent<MeshRenderer>().sharedMaterial = dark;
            Box("CounterGold", new Vector3(0f, sill + 0.11f, d * 0.5f + 0.41f), new Vector3(winR - winL, 0.02f, 0.03f), gold, false);
            var spawn = new GameObject("GunShopSpawn").transform;
            spawn.SetParent(root, false);
            spawn.localPosition = new Vector3(0f, sill + 0.3f, d * 0.5f + 0.15f);
            var so = new SerializedObject(counter.GetComponent<PleaseDontDrown.Story.ShopCounter>());
            Require(so, "_title").stringValue = "Coral Arms";
            Require(so, "_spawnPoint").objectReferenceValue = spawn;
            Require(so, "_startsOpen").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            SetRef(counter.AddComponent<PleaseDontDrown.Story.ShopFollowsAnother>(), "_leader", receptionShop);

            // The beach crowd walks round it.
            NavKeepOut(root, new Vector3(0f, 1.8f, 0f), new Vector3(w + 1f, 3.6f, d + 1f));
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island2");
            AssignSceneIds(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: gun shop built at {root.position}, counter {counter.GetComponent<Collider>().bounds}");
        }

        // ---------------------------------------------------------------- Part C on island 3 (done from this branch)

        /// <summary>Batch: C1, the wooden stair tower against the crags (and its lookout deck) taken out of the saved
        /// scene. TowerFoot stays (the sneak mission starts and restarts there); the story skips the climb without a
        /// Lookout point. Island 3's navmesh is baked again.</summary>
        public static void TeamCRemoveTowerBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var log = new List<string>();
            foreach (string name in new[] { "LookoutTower", "LookoutDeck", "Lookout" })
                Remove("Environment/Island3/NightArrival/" + name, log);
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[TeamB] PASS: C1 tower removed\n" + string.Join("\n", log));
        }

        /// <summary>Batch: C4, the main waterfall (over the sea cave). Where the water starts, the roof stream (3.6 m) used
        /// to turn into a flat 13.4 m plate with square corners: now a fan widens smoothly from the stream to the falls and
        /// curls over the edge. The falling sheets reach a metre under the sea so no gap opens in a wave trough.</summary>
        public static void TeamCWaterfallBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform cave = FindByPath("Environment/Island3/MoneyCave") ?? throw new InvalidOperationException("No sea cave");
            Transform lip = cave.Find("FallsLip");
            Transform roof = cave.Find("RoofStream");
            if (cave.Find("FallsLipFan") is { } oldFan) Object.DestroyImmediate(oldFan.gameObject);
            if (lip == null || roof == null) throw new InvalidOperationException("Falls pieces missing");
            Material water = lip.GetComponent<MeshRenderer>().sharedMaterial;
            float y = lip.localPosition.y, edge = lip.localPosition.z - lip.localScale.y * 0.5f, wide = lip.localScale.x;
            float narrow = roof.localScale.x, start = roof.localPosition.z - roof.localScale.y * 0.5f + 0.3f; // under the stream's end
            lip.gameObject.SetActive(false); // kept (off) for the builder's checks; the fan draws the lip now

            // Rows from the stream's end to the edge, then a short curl down over it.
            const int rows = 14, cols = 12;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float length = start - edge;
            for (int r = 0; r <= rows; r++)
            {
                float k = r / (float)rows;
                float z = Mathf.Lerp(start, edge - 0.25f, k);
                float drop = k > 0.8f ? Mathf.SmoothStep(0f, 0.55f, (k - 0.8f) / 0.2f) : 0f; // the curl over the edge
                float width = Mathf.Lerp(narrow, wide, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.25f)));
                for (int c = 0; c <= cols; c++)
                {
                    float x = Mathf.Lerp(-width * 0.5f, width * 0.5f, c / (float)cols);
                    verts.Add(new Vector3(x, y - drop, z));
                    uvs.Add(new Vector2(x / 3.35f, (z - edge) / 6f)); // ~3.35 m per streak tile across, v grows upstream
                }
            }
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int a = r * (cols + 1) + c, b = a + 1, d = a + cols + 1, e = d + 1;
                    tris.AddRange(new[] { a, d, b, b, d, e });
                }
            var mesh = new Mesh { name = "FallsLipFan" };
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            Directory.CreateDirectory(MeshDir);
            string meshPath = $"{MeshDir}/FallsLipFan.asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);
            var fan = new GameObject("FallsLipFan");
            fan.transform.SetParent(cave, false);
            fan.AddComponent<MeshFilter>().sharedMesh = mesh;
            var fr = fan.AddComponent<MeshRenderer>();
            fr.sharedMaterial = water;
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var flow = new SerializedObject(fan.AddComponent<PleaseDontDrown.World.WaterfallFlow>());
            Require(flow, "_speed").floatValue = 0.8f;
            Require(flow, "_tiling").vector2Value = Vector2.one;
            flow.ApplyModifiedPropertiesWithoutUndo();

            // The sheets a metre further down, under the waves.
            foreach (string name in new[] { "WaterfallSheet", "WaterfallSheetFront" })
                if (cave.Find(name) is { } sheet && sheet.localScale.y < 40f)
                {
                    float bottom = sheet.localPosition.y - sheet.localScale.y * 0.5f;
                    if (bottom > WaterLevel - 0.5f)
                    {
                        sheet.localScale += new Vector3(0f, 1f, 0f);
                        sheet.localPosition -= new Vector3(0f, 0.5f, 0f);
                    }
                }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"[TeamB] PASS: C4 falls lip fan {narrow:0.0} -> {wide:0.0} m over {length:0.0} m");
        }

        /// <summary>Batch: C5, the palace (the Pirate King's castle): the gate shut (two iron-banded doors across the arch
        /// and a solid blocker: nobody walks in), and the sewer's way out in the castle yard: a stone drain with an iron
        /// grate on a hinge (SewerGrateHinge, for A11 to swing open) and the point SewerExit where you climb out.</summary>
        public static void TeamCPalaceBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform castle = FindByPath("Environment/Island3/PirateCastle") ?? throw new InvalidOperationException("No castle");
            foreach (string old in new[] { "GateShut", "SewerExitDrain" })
                if (castle.Find(old) is { } t) Object.DestroyImmediate(t.gameObject);
            const float Z = 20f, G = 0.3f; // the castle's frame (GameSceneBuilder.Island3: gate on the north wall line)
            Material wood = GetMaterial("PirateWoodDark", new Color(0.32f, 0.21f, 0.13f));
            Material iron = GetMaterial("GateIron", new Color(0.12f, 0.12f, 0.13f), 0.6f, 0.35f);
            Material stoneDark = GetMaterial("SewerStone", new Color(0.36f, 0.35f, 0.33f), smoothness: 0.15f);
            Material black = GetMaterial("SewerDark", new Color(0.02f, 0.02f, 0.02f));

            // The gate: shut, in the arch's outer half.
            var gate = new GameObject("GateShut").transform;
            gate.SetParent(castle, false);
            gate.localPosition = new Vector3(0f, G, Z + 2.8f);
            const float width = 3.9f, height = 6.6f;
            foreach (float side in new[] { -1f, 1f })
            {
                float cx = side * width * 0.25f;
                for (int p = 0; p < 4; p++) // vertical planks
                    Primitive(PrimitiveType.Cube, "Plank", gate, new Vector3(cx - width * 0.25f + (p + 0.5f) * width / 8f, height * 0.5f, 0f),
                        new Vector3(width / 8f - 0.03f, height, 0.22f), wood, keepCollider: false);
                foreach (float y in new[] { 1.1f, 3.3f, 5.5f }) // iron bands and studs
                    Primitive(PrimitiveType.Cube, "Band", gate, new Vector3(cx, y, 0.13f), new Vector3(width * 0.5f - 0.08f, 0.16f, 0.04f), iron, keepCollider: false);
                Primitive(PrimitiveType.Cylinder, "RingHandle", gate, new Vector3(side * 0.35f, 2.6f, 0.2f), new Vector3(0.36f, 0.02f, 0.36f), iron, keepCollider: false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            Primitive(PrimitiveType.Cube, "Bar", gate, new Vector3(0f, 2.9f, -0.2f), new Vector3(width - 0.2f, 0.25f, 0.2f), wood, keepCollider: false); // barred from inside
            var blocker = gate.gameObject.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0f, height * 0.5f + 1f, 0f);
            blocker.size = new Vector3(width + 0.6f, height + 2f, 0.8f);

            // The sewer's way out: a round stone drain in the yard's west half, an iron grate over it.
            var drain = new GameObject("SewerExitDrain").transform;
            drain.SetParent(castle, false);
            drain.localPosition = new Vector3(-21f, G, 4f);
            Primitive(PrimitiveType.Cylinder, "Kerb", drain, new Vector3(0f, 0.12f, 0f), new Vector3(2.2f, 0.12f, 2.2f), stoneDark);
            Primitive(PrimitiveType.Cylinder, "Shaft", drain, new Vector3(0f, 0.245f, 0f), new Vector3(1.6f, 0.005f, 1.6f), black, keepCollider: false);
            var hinge = new GameObject("SewerGrateHinge").transform;
            hinge.SetParent(drain, false);
            hinge.localPosition = new Vector3(-0.8f, 0.27f, 0f); // swings up on its west edge
            for (int i = -3; i <= 3; i++)
                Primitive(PrimitiveType.Cube, "GrateBar", hinge, new Vector3(0.8f, 0.02f, i * 0.22f), new Vector3(1.7f, 0.05f, 0.06f), iron, keepCollider: false);
            foreach (float x in new[] { 0.35f, 0.8f, 1.25f })
                Primitive(PrimitiveType.Cube, "GrateRib", hinge, new Vector3(x, 0.03f, 0f), new Vector3(0.06f, 0.05f, 1.6f), iron, keepCollider: false);
            var exit = new GameObject("SewerExit").transform;
            exit.SetParent(drain, false);
            exit.localPosition = new Vector3(1.6f, 0.3f, 0f);
            exit.localRotation = Quaternion.Euler(0f, 90f, 0f);

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: C5 gate shut at {gate.position}, sewer exit at {exit.position}");
        }

        /// <summary>Batch: C6, the jungle bigger: a 14 m band of trees, bushes and ferns all round its edge (same models
        /// and rules as BuildIsland3Jungle), clear of the roads, the river, the falls, the castle, the houses, the tavern,
        /// the gun stall and the night arrival; then everything settles onto the ground (SettleIsland3Plants).</summary>
        public static void TeamCJungleBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform island = FindByPath("Environment/Island3") ?? throw new InvalidOperationException("No island 3");
            Transform jungle = island.Find("Jungle");
            const float Band = 14f;
            // What stands on the island and must stay clear (their drawn boxes, 3 m round).
            var keepClear = new List<Bounds>();
            foreach (string group in new[] { "PirateVillage", "PirateTavern", "PirateCastle", "Story", "NightArrival", "CragFalls", "GoldVault", "PirateDock" })
                if (island.Find(group) is { } g)
                    foreach (Transform t in g)
                    {
                        if (t.name == "VillageDressing") { foreach (Transform d in t) Add(d); continue; }
                        Add(t);
                    }
            void Add(Transform t)
            {
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { keepClear.Add(new Bounds(t.position, new Vector3(4f, 4f, 4f))); return; }
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                if (b.size.x > 60f || b.size.z > 60f) return; // a whole group, not one thing
                b.Expand(new Vector3(6f, 0f, 6f));
                keepClear.Add(b);
            }
            var trees = new List<Vector2>(); var bushes = new List<Vector2>(); var ferns = new List<Vector2>();
            foreach (Transform t in jungle)
            {
                var p = new Vector2(t.position.x, t.position.z);
                if (t.name.Contains("tree") || t.name.Contains("palm")) trees.Add(p);
                else if (t.name.Contains("bush")) bushes.Add(p); else ferns.Add(p);
            }
            int before = jungle.childCount;
            bool Near(List<Vector2> list, Vector2 p, float d) => list.Any(q => (q - p).sqrMagnitude < d * d);
            float EdgeDistance(Vector2 p) => JunglePolygons.Min(poly => DistanceToLine(p, poly.Append(poly[0]).ToArray()));
            var rng = new System.Random(6611);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float minX = JunglePolygons.SelectMany(q => q).Min(q => q.x) - Band, maxX = JunglePolygons.SelectMany(q => q).Max(q => q.x) + Band;
            float minZ = JunglePolygons.SelectMany(q => q).Min(q => q.y) - Band, maxZ = JunglePolygons.SelectMany(q => q).Max(q => q.y) + Band;
            int addedTrees = 0, addedBushes = 0, addedFerns = 0;
            for (int i = 0; i < 40000; i++)
            {
                int kind = i < 15000 ? 0 : i < 27000 ? 1 : 2;
                if ((kind == 0 && addedTrees >= 70) || (kind == 1 && addedBushes >= 120) || (kind == 2 && addedFerns >= 150)) continue;
                var p = new Vector2(R(minX, maxX), R(minZ, maxZ));
                if (InJungle(p) || EdgeDistance(p) > Band) continue;
                float h = BeachHeight(p.x, p.y);
                if (h < 0.4f || h > 26f) continue;
                float boxOut = BoxDistanceOut(p.x, p.y, Island3Center, Island3HalfSize, Island3CornerRadius);
                if (Island3Shore(p.x, p.y, boxOut) < 8f) continue;
                if (DistanceToLine(p, Island3River) < 5f || Vector2.Distance(p, Island3Spring) < 10f || InCragFalls(p, 4f)) continue;
                if (DistanceToLine(p, Island3Stream) < 4f) continue;
                if (BoxDistanceOut(p.x, p.y, CastleCenter, CastleHalf, 0f) < 6f) continue;
                if (keepClear.Any(b => p.x > b.min.x && p.x < b.max.x && p.y > b.min.z && p.y < b.max.z)) continue;
                float road = DistanceToRoads(p);
                var feet = new Vector3(p.x, h - 0.1f, p.y);
                float yaw = R(0f, 360f);
                if (kind == 0)
                {
                    if (road < 4.5f || Near(trees, p, 5.5f)) continue;
                    GameObject tree = TripoModel("jungle_tree", jungle, feet, yaw, Vector3.one * R(0.8f, 1.3f));
                    if (tree == null) break;
                    var trunk = tree.AddComponent<CapsuleCollider>();
                    trunk.height = 6f; trunk.radius = 0.35f; trunk.center = Vector3.up * 3f;
                    trees.Add(p); addedTrees++;
                }
                else if (kind == 1)
                {
                    if (road < 2.6f || Near(trees, p, 1.8f) || Near(bushes, p, 2.8f)) continue;
                    if (TripoModel("jungle_bush", jungle, feet, yaw, Vector3.one * R(0.8f, 1.6f)) == null) break;
                    bushes.Add(p); addedBushes++;
                }
                else
                {
                    if (road < 2.2f || Near(trees, p, 1.2f) || Near(bushes, p, 1.5f) || Near(ferns, p, 1.5f)) continue;
                    if (TripoModel("jungle_fern", jungle, feet, yaw, Vector3.one * R(0.8f, 1.5f)) == null) break;
                    ferns.Add(p); addedFerns++;
                }
            }
            SettleIsland3Plants(island, dryRun: false);
            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: C6 jungle {before} -> {jungle.childCount} plants (+{addedTrees} trees, +{addedBushes} bushes, +{addedFerns} ferns)");
        }

        /// <summary>Batch: C7, the tavern: an octagon cage in the middle of the room for the cage fight (3.4: Iron Jaw Jack
        /// already stands in its middle), its gate toward the front door; the tables and stools in its way go. The bar
        /// gets a sign, a Jolly Roger on the back wall and hanging lanterns.</summary>
        public static void TeamCTavernBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform tavern = FindByPath("Environment/Island3/PirateTavern") ?? throw new InvalidOperationException("No tavern");
            foreach (string old in new[] { "Octagon", "TavernDress" })
                if (tavern.Find(old) is { } t) Object.DestroyImmediate(t.gameObject);
            Bounds floorBox = tavern.Find("RoomFloor").GetComponent<Renderer>().bounds;
            float floor = floorBox.max.y;
            var centre = new Vector3(86f, floor, -966.8f);
            const float R = 3.1f, PostH = 1.95f; // corner radius, cage height
            var removed = 0;
            foreach (Transform t in tavern.Cast<Transform>().ToArray())
                if (t.name is "RoundTable" or "TableFoot" or "Stool" or "Candle" or "Mug" &&
                    new Vector2(t.position.x - centre.x, t.position.z - centre.z).magnitude < R + 1.1f)
                { Object.DestroyImmediate(t.gameObject); removed++; }

            Material canvas = GetMaterial("OctagonCanvas", new Color(0.86f, 0.82f, 0.72f), smoothness: 0.1f);
            Material pad = GetMaterial("OctagonPad", new Color(0.08f, 0.08f, 0.09f), smoothness: 0.35f);
            Material red = GetMaterial("OctagonRed", new Color(0.62f, 0.07f, 0.06f), smoothness: 0.3f);
            Material wire = GetMaterial("OctagonWire", new Color(0.2f, 0.2f, 0.21f), 0.7f, 0.4f);
            var cage = new GameObject("Octagon").transform;
            cage.SetParent(tavern, false);
            cage.position = centre;
            TagSurface(cage.gameObject, SurfaceKind.Wood);
            GameObject Part(PrimitiveType type, string n, Vector3 local, Vector3 scale, Material m, bool solid = false) =>
                Primitive(type, n, cage, local, scale, m, solid);
            // The canvas: an octagon of 8 wedges (a flat cylinder reads round), a red ring painted near the edge.
            Part(PrimitiveType.Cylinder, "Canvas", new Vector3(0f, 0.03f, 0f), new Vector3(R * 2f * 0.97f, 0.03f, R * 2f * 0.97f), canvas);
            Part(PrimitiveType.Cylinder, "CanvasRing", new Vector3(0f, 0.062f, 0f), new Vector3(R * 1.5f, 0.002f, R * 1.5f), red);
            Part(PrimitiveType.Cylinder, "CanvasInner", new Vector3(0f, 0.064f, 0f), new Vector3(R * 1.42f, 0.002f, R * 1.42f), canvas);
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * Mathf.PI / 4f; // flat sides face the four walls
                corners[i] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * R;
                Part(PrimitiveType.Cylinder, "Post", corners[i] + Vector3.up * PostH * 0.5f, new Vector3(0.16f, PostH * 0.5f, 0.16f), pad, solid: true);
                Part(PrimitiveType.Cylinder, "PostPad", corners[i] + Vector3.up * 1.2f, new Vector3(0.24f, 0.45f, 0.24f), red);
            }
            for (int i = 0; i < 8; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 8], mid = (a + b) * 0.5f;
                float len = Vector3.Distance(a, b) - 0.16f;
                Quaternion yaw = Quaternion.LookRotation(b - a) * Quaternion.Euler(0f, 90f, 0f); // x along the side
                bool gate = mid.z > R * 0.8f; // the side toward the front door stays open
                if (gate) continue; // the way in, open
                var side = new GameObject("Side").transform;
                side.SetParent(cage, false);
                side.localPosition = mid;
                side.localRotation = yaw;
                Primitive(PrimitiveType.Cube, "Pad", side, new Vector3(0f, 0.45f, 0f), new Vector3(len, 0.9f, 0.14f), pad, keepCollider: false);
                Primitive(PrimitiveType.Cube, "PadStripe", side, new Vector3(0f, 0.86f, 0f), new Vector3(len, 0.06f, 0.16f), red, keepCollider: false);
                for (int k = 1; k < 7; k++) // the fence over the pad: thin uprights and two rails
                    Primitive(PrimitiveType.Cube, "Wire", side, new Vector3(-len * 0.5f + k * len / 7f, 1.42f, 0f), new Vector3(0.025f, 1.06f, 0.025f), wire, keepCollider: false);
                foreach (float y in new[] { 1.4f, PostH - 0.03f })
                    Primitive(PrimitiveType.Cube, "Rail", side, new Vector3(0f, y, 0f), new Vector3(len, 0.04f, 0.04f), wire, keepCollider: false);
                var block = side.gameObject.AddComponent<BoxCollider>();
                block.center = new Vector3(0f, PostH * 0.5f, 0f);
                block.size = new Vector3(len, PostH, 0.2f);
            }

            // The bar's look: a sign over it, the Jolly Roger on the back wall, lanterns hanging over the counter.
            var dress = new GameObject("TavernDress").transform;
            dress.SetParent(tavern, false);
            dress.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // parts below are placed in world space
            Bounds bar = tavern.Find("BarCounter").GetComponent<Renderer>().bounds;
            Bounds back = tavern.Find("RoomBack").GetComponent<Renderer>().bounds;
            float ceiling = tavern.Find("RoomCeiling").GetComponent<Renderer>().bounds.min.y;
            Material plank = GetMaterial("PirateWoodPlank", new Color(0.46f, 0.33f, 0.2f));
            Material brass = GetMaterial("PirateGold", new Color(0.95f, 0.72f, 0.2f), metallic: 0.7f, smoothness: 0.6f, emission: new Color(0.25f, 0.16f, 0.02f));
            Material lampGlow = GetMaterial("PirateLantern", new Color(1f, 0.75f, 0.35f), emission: new Color(2.2f, 1.3f, 0.4f));
            Vector3 signAt = new(bar.center.x, ceiling - 0.45f, bar.max.z + 0.2f);
            Primitive(PrimitiveType.Cube, "SignBoard", dress, signAt, new Vector3(3.4f, 0.5f, 0.08f), plank, keepCollider: false);
            WorldText(dress, "SignText", signAt + new Vector3(0f, 0f, 0.05f), "THE RUSTY ANCHOR", 96, 0.03f, brass.color)
                .transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            GameObject roger = Primitive(PrimitiveType.Quad, "JollyRoger", dress, new Vector3(back.center.x, ceiling - 0.75f, back.max.z + 0.03f),
                new Vector3(1.6f, 1.1f, 1f), JollyRogerMaterial(), keepCollider: false);
            roger.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3(Mathf.Lerp(bar.min.x + 1f, bar.max.x - 1f, i / 2f), ceiling - 0.7f, bar.center.z);
                Primitive(PrimitiveType.Cylinder, "LanternChain", dress, at + Vector3.up * 0.4f, new Vector3(0.02f, 0.3f, 0.02f), brass, keepCollider: false);
                Primitive(PrimitiveType.Cube, "Lantern", dress, at, new Vector3(0.22f, 0.3f, 0.22f), lampGlow, keepCollider: false);
                var light = new GameObject("LanternLight").AddComponent<Light>();
                light.transform.SetParent(dress, false);
                light.transform.position = at + Vector3.down * 0.3f;
                light.type = LightType.Point; light.range = 5f; light.intensity = 1.1f; light.color = new Color(1f, 0.72f, 0.4f);
                light.shadows = LightShadows.None;
            }

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: C7 octagon at {centre} (removed {removed} tables/stools), bar dressed");
        }

        /// <summary>Batch: C8, island 3's gun shop as a real building: the Tripo gun_shop (open shop front, awning,
        /// counter, steps; it faces its -x) stands behind the stall's counter (the working "Skull Cove guns" shop stays
        /// as its walk-up counter), its front toward the nearest road; the stall's awning, posts and sign go.</summary>
        public static void TeamCGunShopBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform shop = FindByPath("Environment/Island3/Story/Island3GunShop") ?? throw new InvalidOperationException("No gun stall");
            Transform counter = shop.Find("Counter");
            foreach (Transform t in shop.Cast<Transform>().ToArray())
                if (t.name is "AwningPost" or "Awning" or "Sign" or "Tripo_gun_shop" or "GunShopBody") Object.DestroyImmediate(t.gameObject);
            Vector3 c = shop.position + Vector3.up * 0.6f; // the stall's spot (the counter moves below: re-running stays put)
            // Which way is the road: the direction (16 tried) whose point 5 m out is nearest a road.
            Vector3 front = Vector3.forward;
            float best = float.MaxValue;
            for (int i = 0; i < 16; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                float d = DistanceToRoads(new Vector2(c.x + dir.x * 5f, c.z + dir.z * 5f));
                if (d < best) { best = d; front = dir; }
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TripoIsland3Dir}/gun_shop.glb") ?? throw new InvalidOperationException("gun_shop.glb missing");
            GameObject house = Object.Instantiate(prefab, shop, false);
            house.name = "Tripo_gun_shop";
            // The model's front is its -x: turn that to face the road.
            float yaw = Mathf.Atan2(front.x, front.z) * Mathf.Rad2Deg + 90f;
            house.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            house.transform.position = Vector3.zero;
            // Its width is along its z (1.0 m as imported), its depth along x (0.76): 7 m wide.
            const float Width = 7f;
            float scale = Width / 1.0f;
            house.transform.localScale = Vector3.one * scale;
            Bounds b = RendererBounds(house.transform);
            float depth = b.size.x * Mathf.Abs(front.x) + b.size.z * Mathf.Abs(front.z);
            Vector3 centre = c - front * (depth * 0.5f + 0.4f); // the counter just in front of its open front
            float ground = GroundUnder(centre);
            house.transform.position += new Vector3(centre.x - b.center.x, ground - 0.15f - b.min.y, centre.z - b.center.z);
            foreach (Renderer r in house.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
            // The working counter onto the model's own counter (front right, as imported about (-0.3, -0.2, -0.22)):
            // its box stops drawing, its collider stays to talk to; things bought appear on top.
            Vector3 onCounter = house.transform.TransformPoint(new Vector3(-0.3f, -0.2f, -0.22f));
            counter.SetPositionAndRotation(onCounter + Vector3.down * 0.25f, Quaternion.LookRotation(front));
            if (counter.GetComponent<MeshRenderer>() is { } box) box.enabled = false;
            if (shop.Find("ShopSpawn") is { } spawn) spawn.position = onCounter + Vector3.up * 0.15f;
            // Its body to bump into (the open front half stays walk-up).
            var body = new GameObject("GunShopBody").transform;
            body.SetParent(shop, false);
            body.position = centre - front * (depth * 0.15f) + Vector3.up * (ground + 3f - centre.y);
            body.rotation = Quaternion.LookRotation(front);
            body.gameObject.AddComponent<BoxCollider>().size = new Vector3(Width * 0.92f, 6f, depth * 0.7f);
            TagSurface(body.gameObject, SurfaceKind.Wood);

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island3");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[TeamB] PASS: C8 gun shop at {centre} facing {front} (road {best:0.0} m), scale {scale:0.0}, size {RendererBounds(house.transform).size}");
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
