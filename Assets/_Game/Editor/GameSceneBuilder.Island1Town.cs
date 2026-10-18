using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Fun;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Island 1's beach village (the user, 2026-10-18: "make it bigger buildings, only have Sandy's house this size and
    /// toilet, make bar bigger", plus boardwalks, dunes with beach grass, sunbeds and umbrellas, a volleyball net, rocks,
    /// a rope fence, a PLEASE DON'T DROWN arch where the docks land and a lighthouse at the back). Done as edits to the
    /// saved Game scene, never a rebuild; run again after a merge (it replaces what it made before). The models are
    /// ArtSource/Tools/model_props.py (lifeguard_station, beach_bar_big, boardwalk...) and the Tripo lighthouse
    /// (Art/Tripo/Island1); the colliders below use the same numbers as the models.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        // ------------------------------------------------------------------ dunes (part of the terrain: BeachHeight)

        /// <summary>Low dunes on island 1: (centre, half size, height). Grass grows on them; the back ones carry palms.</summary>
        private static readonly (Vector2 centre, Vector2 radius, float height)[] Island1Dunes =
        {
            (new Vector2(5f, 43.5f), new Vector2(7f, 4f), 1.6f),
            (new Vector2(-6f, 45f), new Vector2(5f, 3.5f), 1.3f),
            (new Vector2(38f, 44.5f), new Vector2(6f, 3.5f), 1.4f),
            (new Vector2(6f, 27f), new Vector2(4.5f, 3.5f), 0.9f),
        };

        private static bool _island1DunesOff; // (the batch checks the saved terrain against the generator without them)

        /// <summary>The dunes' height at a point (0 off them), fading out toward the waterline.</summary>
        private static float Island1DuneHeight(float x, float z)
        {
            if (_island1DunesOff) return 0f;
            float h = 0f;
            foreach ((Vector2 c, Vector2 r, float top) in Island1Dunes)
            {
                float d = new Vector2((x - c.x) / r.x, (z - c.y) / r.y).magnitude;
                if (d < 1f) h = Mathf.Max(h, top * 0.5f * (1f + Mathf.Cos(Mathf.PI * d)));
            }
            return h <= 0f ? 0f : h * Mathf.Clamp01((ShoreCoordinate(x, z) - 6f) / 4f);
        }

        private static bool OnIsland1Dune(Vector3 p, float grow = 1.05f) =>
            Island1Dunes.Any(d => new Vector2((p.x - d.centre.x) / d.radius.x, (p.z - d.centre.y) / d.radius.y).magnitude < grow);

        // ------------------------------------------------------------------ the numbers the models are built to

        // lifeguard_station (station-local, the sea +z): floor, ground floor walls, roof deck, lookout cabin, stairs.
        private const float StF = 0.3f, StGx = 4.5f, StGz0 = -6f, StH1 = 3.3f, StU = 3.5f, StSx = 4.7f, StSz0 = -6.2f, StSz1 = 2f;
        private const float StCx = 3f, StCz0 = -5.4f, StCz1 = -1.6f, StE = 6.1f, StDoor0 = -3.5f, StDoor1 = -2.4f, StDoorH = 2.2f;
        private const float StCabDoor0 = 1.3f, StCabDoor1 = 2.3f, StStair0 = 4.75f, StStair1 = 5.95f, StStairLow = 1.6f, StStairHigh = -4f;
        // beach_bar_big (bar-local, the counter faces +z).
        private const float BbW = 4.4f, BbD = 2.4f, BbTop = 3f, BbTerrace = 4.2f, BbCounterZ = BbD - 0.35f;
        private const int BbStools = 8;

        private static readonly Vector3 StationSpot = new(12f, 0f, 8.8f);
        private static readonly Vector2 LighthouseWish = new(22f, 47.5f);

        /// <summary>The boardwalks (straight runs, end to end): the docks to a promenade behind the station, up to the
        /// toilets, the bar's terrace and the lighthouse.</summary>
        private static readonly Vector2[][] Island1Boardwalks =
        {
            new[] { new Vector2(-8f, 6.2f), new Vector2(-8f, 18f) },
            new[] { new Vector2(-8f, 18f), new Vector2(38f, 18f) },
            new[] { new Vector2(32f, 7.6f), new Vector2(32f, 18f) },
            new[] { new Vector2(14f, 18f), new Vector2(14f, 32.4f) },
            new[] { new Vector2(31f, 18f), new Vector2(31f, 28f) },
            new[] { new Vector2(14f, 30f), new Vector2(21f, 30f) },
        };

        /// <summary>Batch: island 1's village (see the class summary). Logs to Logs/island1-town.txt.</summary>
        public static void Island1TownBatch()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var log = new List<string>();
            Transform env = GameObject.Find("Environment").transform;
            GameObject terrain = GameObject.Find("Environment/BeachTerrain");
            Physics.SyncTransforms();

            RaiseIsland1Dunes(terrain, env, log);
            var collider = terrain.GetComponent<MeshCollider>();
            float GroundAt(float x, float z) =>
                collider.Raycast(new Ray(new Vector3(x, 80f, z), Vector3.down), out RaycastHit hit, 200f) ? hit.point.y : 0f;

            var keepClear = new List<Rect>();
            BuildLifeguardStation(env, GroundAt, keepClear, log);
            EnlargeBeachBar(log, keepClear);

            if (env.Find("Island1Town") is { } old) Object.DestroyImmediate(old.gameObject);
            var town = new GameObject("Island1Town").transform;
            town.SetParent(env, false);

            // Boardwalks.
            var walks = new GameObject("Boardwalks").transform;
            walks.SetParent(town, false);
            int pieces = 0;
            foreach (Vector2[] run in Island1Boardwalks)
            {
                Vector2 a = run[0], b = run[1];
                float length = Vector2.Distance(a, b);
                int n = Mathf.CeilToInt(length / 1.96f);
                Vector2 dir = (b - a) / length;
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = a + dir * (length * i / n), p1 = a + dir * (length * (i + 1) / n), mid = (p0 + p1) * 0.5f;
                    float y0 = GroundAt(p0.x, p0.y), y1 = GroundAt(p1.x, p1.y), ym = GroundAt(mid.x, mid.y);
                    float pitch = -Mathf.Atan2(y1 - y0, Vector2.Distance(p0, p1)) * Mathf.Rad2Deg;
                    var piece = new GameObject("Boardwalk").transform;
                    piece.SetParent(walks, false);
                    piece.SetPositionAndRotation(new Vector3(mid.x, Mathf.Max(ym, (y0 + y1) * 0.5f) - 0.01f, mid.y), Quaternion.Euler(pitch, yaw, 0f));
                    PropModel("boardwalk", piece, scale: 1f).transform.localScale = new Vector3(1f, 1f, length / n / 2f + 0.03f);
                    var box = piece.gameObject.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.04f, 0f);
                    box.size = new Vector3(1.8f, 0.08f, length / n + 0.06f);
                    TagSurface(piece.gameObject, SurfaceKind.Wood);
                    pieces++;
                }
                keepClear.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x) - 0.9f, Mathf.Min(a.y, b.y) - 0.9f, Mathf.Max(a.x, b.x) + 0.9f, Mathf.Max(a.y, b.y) + 0.9f));
            }
            log.Add($"boardwalk: {pieces} pieces on {Island1Boardwalks.Length} runs");

            // The welcome arches where the docks land (Milo's pier: a little further in, clear of his spot and the cannon).
            foreach ((string name, Vector2 at) in new[] { ("ArchWestDock", new Vector2(-8f, 7.6f)), ("ArchMilosPier", new Vector2(32f, 12f)) })
            {
                var arch = new GameObject(name).transform;
                arch.SetParent(town, false);
                arch.SetPositionAndRotation(new Vector3(at.x, Mathf.Min(GroundAt(at.x - 3f, at.y), GroundAt(at.x + 3f, at.y)), at.y), Quaternion.identity);
                PropModel("welcome_arch", arch);
                foreach (float x in new[] { -3f, 3f })
                {
                    var post = new GameObject("Post").AddComponent<BoxCollider>();
                    post.transform.SetParent(arch, false);
                    post.center = new Vector3(x, 2f, 0f);
                    post.size = new Vector3(0.32f, 4.6f, 0.32f);
                }
                keepClear.Add(new Rect(at.x - 3.6f, at.y - 3f, 7.2f, 6f)); // (no palm in front of the board)
                log.Add($"{name} at {arch.position}");
            }

            // Sunbeds in rows facing the sea, an umbrella between each pair; one row on the west side.
            var beds = new GameObject("Sunbeds").transform;
            beds.SetParent(town, false);
            int bedCount = 0;
            void Pair(float x, float z, int k)
            {
                foreach (float dx in new[] { 0f, 0.95f })
                {
                    var bed = new GameObject("Sunbed").transform;
                    bed.SetParent(beds, false);
                    bed.SetPositionAndRotation(new Vector3(x + dx, GroundAt(x + dx, z), z), Quaternion.Euler(0f, 180f, 0f)); // the head end inland
                    PropModel(k % 2 == 0 ? "sunbed" : "sunbed_orange", bed);
                    var box = bed.gameObject.AddComponent<BoxCollider>();
                    box.center = new Vector3(0f, 0.25f, 0.1f);
                    box.size = new Vector3(0.72f, 0.5f, 1.85f);
                    bedCount++;
                }
                var umbrella = new GameObject("Umbrella").transform;
                umbrella.SetParent(beds, false);
                umbrella.SetPositionAndRotation(new Vector3(x + 2.05f, GroundAt(x + 2.05f, z - 0.3f), z - 0.3f), Quaternion.Euler(0f, k * 37f, 0f));
                PropModel(k % 2 == 0 ? "parasol_red" : "parasol_teal", umbrella);
                var pole = umbrella.gameObject.AddComponent<CapsuleCollider>();
                pole.center = new Vector3(0f, 1.1f, 0f);
                pole.radius = 0.06f;
                pole.height = 2.2f;
            }
            int group = 0;
            foreach (float z in new[] { 22.5f, 26f })
                foreach (float x in new[] { 17f, 20.4f, 23.8f })
                    Pair(x, z, group++);
            foreach (float x in new[] { -20.4f, -17f, -13.6f })
                Pair(x, 18f, group++);
            keepClear.Add(Rect.MinMaxRect(16.6f, 21.2f, 26.3f, 27.3f));
            keepClear.Add(Rect.MinMaxRect(-20.8f, 16.8f, -11.2f, 19.3f));
            log.Add($"sunbeds: {bedCount}, umbrellas: {group}");

            // The volleyball court, east of the bar.
            var court = new GameObject("VolleyballCourt").transform;
            court.SetParent(town, false);
            court.SetPositionAndRotation(new Vector3(42f, GroundAt(42f, 27f), 27f), Quaternion.identity);
            PropModel("volleyball_net", court);
            foreach (float x in new[] { -4.8f, 4.8f })
            {
                var post = new GameObject("Post").AddComponent<CapsuleCollider>();
                post.transform.SetParent(court, false);
                post.center = new Vector3(x, 1.3f, 0f);
                post.radius = 0.07f;
                post.height = 2.6f;
            }
            var net = new GameObject("Net").AddComponent<BoxCollider>(); // a ball (or a lifeguard) bounces off it
            net.transform.SetParent(court, false);
            net.center = new Vector3(0f, 1.92f, 0f);
            net.size = new Vector3(8f, 0.98f, 0.04f);
            keepClear.Add(Rect.MinMaxRect(36.8f, 18.8f, 47.4f, 35.2f));
            log.Add($"volleyball court at {court.position}");

            // The lighthouse at the back, its door toward the beach, on the first spot back there with solid sand round it.
            Vector2 lightAt = LighthouseWish;
            for (int i = 0; i < 20; i++)
            {
                bool solid = true;
                for (int k = 0; k < 8 && solid; k++)
                {
                    float t = k * Mathf.PI / 4f;
                    solid = GroundAt(lightAt.x + Mathf.Cos(t) * 3.2f, lightAt.y + Mathf.Sin(t) * 3.2f) > WaterLevel + 0.25f; // (inland sand is at 0)
                }
                if (solid) break;
                lightAt.y -= 0.5f;
            }
            var lighthouse = new GameObject("Lighthouse").transform;
            lighthouse.SetParent(town, false);
            float lightFloor = Enumerable.Range(0, 8).Min(k => GroundAt(lightAt.x + Mathf.Cos(k * Mathf.PI / 4f) * 3f, lightAt.y + Mathf.Sin(k * Mathf.PI / 4f) * 3f));
            lighthouse.SetPositionAndRotation(new Vector3(lightAt.x, lightFloor - 0.05f, lightAt.y), Quaternion.Euler(0f, -90f, 0f)); // the model's door is on its -x side
            GameObject tower = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Tripo/Island1/lighthouse.glb");
            if (tower != null)
            {
                GameObject model = Object.Instantiate(tower, lighthouse, false);
                model.name = "Tripo_lighthouse";
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>())
                    mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                foreach (Renderer r in model.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
                TagSurface(lighthouse.gameObject, SurfaceKind.Rock);
            }
            else log.Add("NO LIGHTHOUSE MODEL (Assets/_Game/Art/Tripo/Island1/lighthouse.glb)");
            keepClear.Add(new Rect(lightAt.x - 4f, lightAt.y - 4f, 8f, 8f));
            // A path to its door and rocks round its foot.
            Vector2 from = new(21f, 30f), to = new(21f, lightAt.y - 4f);
            for (int i = 0, n = Mathf.CeilToInt((to.y - from.y) / 1.96f); i < n; i++)
            {
                float z0 = from.y + (to.y - from.y) * i / n, z1 = from.y + (to.y - from.y) * (i + 1) / n;
                var piece = new GameObject("Boardwalk").transform;
                piece.SetParent(walks, false);
                float y0 = GroundAt(21f, z0), y1 = GroundAt(21f, z1);
                piece.SetPositionAndRotation(new Vector3(21f, (y0 + y1) * 0.5f - 0.01f, (z0 + z1) * 0.5f), Quaternion.Euler(-Mathf.Atan2(y1 - y0, z1 - z0) * Mathf.Rad2Deg, 0f, 0f));
                PropModel("boardwalk", piece).transform.localScale = new Vector3(1f, 1f, (z1 - z0) / 2f + 0.03f);
                var box = piece.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.04f, 0f);
                box.size = new Vector3(1.8f, 0.08f, z1 - z0 + 0.06f);
                TagSurface(piece.gameObject, SurfaceKind.Wood);
            }
            keepClear.Add(Rect.MinMaxRect(20.1f, 30f, 21.9f, lightAt.y - 4f));
            log.Add($"lighthouse at {lighthouse.position}");

            var rng = new System.Random(18);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(town, false);
            void Rock(float x, float z, float size)
            {
                var rock = new GameObject("Rock").transform;
                rock.SetParent(rocks, false);
                rock.SetPositionAndRotation(new Vector3(x, GroundAt(x, z) + size * 0.22f, z), Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f)));
                GameObject m = PropModel("rock", rock);
                m.transform.localScale = new Vector3(size * R(0.9f, 1.4f), size * R(0.6f, 0.9f), size * R(0.9f, 1.3f));
                var sphere = rock.gameObject.AddComponent<SphereCollider>();
                sphere.radius = size * 0.42f;
                TagSurface(rock.gameObject, SurfaceKind.Rock);
            }
            for (int k = 0; k < 9; k++)
            {
                float t = (k / 9f) * Mathf.PI * 2f + 0.4f;
                if (Mathf.Abs(Mathf.DeltaAngle(t * Mathf.Rad2Deg, -90f)) < 30f) continue; // the door side stays open
                Rock(lightAt.x + Mathf.Cos(t) * R(4f, 4.8f), lightAt.y + Mathf.Sin(t) * R(4f, 4.8f), R(0.7f, 1.6f));
            }
            foreach ((float x, float z, float s) in new[] { (33.5f, 42.5f, 1.3f), (34.6f, 43.4f, 0.8f), (43.2f, 45.5f, 1.1f), (-1.5f, 41.2f, 1.0f),
                         (11.8f, 42.6f, 0.9f), (-10.8f, 44.6f, 1.2f), (-11.6f, 43.5f, 0.7f), (2.2f, 24.4f, 0.8f) })
                Rock(x, z, s);

            // Beach grass on the dunes (and a little round the lighthouse), a rope fence along the front of two dunes.
            var grass = new GameObject("BeachGrass").transform;
            grass.SetParent(town, false);
            int tufts = 0;
            foreach ((Vector2 c, Vector2 r, float _) in Island1Dunes)
            {
                int n = Mathf.RoundToInt(r.x * r.y * 1.3f);
                for (int i = 0; i < n; i++)
                {
                    float t = R(0f, Mathf.PI * 2f), d = Mathf.Sqrt(R(0f, 1f)) * 0.9f;
                    float x = c.x + Mathf.Cos(t) * r.x * d, z = c.y + Mathf.Sin(t) * r.y * d;
                    if (keepClear.Any(k => k.Contains(new Vector2(x, z)))) continue;
                    var tuft = new GameObject("Grass").transform;
                    tuft.SetParent(grass, false);
                    tuft.SetPositionAndRotation(new Vector3(x, GroundAt(x, z) - 0.03f, z), Quaternion.Euler(0f, R(0f, 360f), 0f));
                    PropModel("beach_grass", tuft).transform.localScale = Vector3.one * R(0.75f, 1.35f);
                    tufts++;
                }
            }
            for (int i = 0; i < 10; i++)
            {
                float t = R(0f, Mathf.PI * 2f);
                float x = lightAt.x + Mathf.Cos(t) * R(3.6f, 6f), z = lightAt.y + Mathf.Sin(t) * R(3.6f, 6f);
                if (Mathf.Abs(x - 21f) < 1.2f && z < lightAt.y) continue;
                var tuft = new GameObject("Grass").transform;
                tuft.SetParent(grass, false);
                tuft.SetPositionAndRotation(new Vector3(x, GroundAt(x, z) - 0.03f, z), Quaternion.Euler(0f, R(0f, 360f), 0f));
                PropModel("beach_grass", tuft).transform.localScale = Vector3.one * R(0.7f, 1.2f);
                tufts++;
            }
            log.Add($"beach grass: {tufts} tufts");
            var fences = new GameObject("RopeFences").transform;
            fences.SetParent(town, false);
            foreach (int d in new[] { 0, 3 })
            {
                (Vector2 c, Vector2 r, float _) = Island1Dunes[d];
                float z = c.y - r.y - 0.4f;
                for (float x = c.x - r.x * 0.75f + 2f; x <= c.x + r.x * 0.75f - 1.9f; x += 4f)
                {
                    var fence = new GameObject("RopeFence").transform;
                    fence.SetParent(fences, false);
                    fence.SetPositionAndRotation(new Vector3(x, GroundAt(x, z), z), Quaternion.identity);
                    PropModel("rope_fence", fence);
                    foreach (float px in new[] { -2f, 0f, 2f })
                    {
                        var post = new GameObject("Post").AddComponent<BoxCollider>();
                        post.transform.SetParent(fence, false);
                        post.center = new Vector3(px, 0.45f, 0f);
                        post.size = new Vector3(0.12f, 0.9f, 0.12f);
                    }
                }
            }

            // Palms, towels and loose things that stand where the new things are go to the nearest free sand.
            ClearIsland1Spots(env, keepClear, GroundAt, log);

            Physics.SyncTransforms();
            BakeNavMeshes(only: "Island1");
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/island1-town.txt", log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Island1Town] PASS: island 1 village built\n" + string.Join("\n", log));
        }

        /// <summary>The dunes go into the terrain (only if the saved terrain is what the generator makes, so no hand fix
        /// elsewhere is lost); whatever stands on them is lifted with the sand.</summary>
        private static void RaiseIsland1Dunes(GameObject terrain, Transform env, List<string> log)
        {
            Mesh saved = terrain.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] vertices = saved.vertices;
            float worst = 0f, dunes = 0f;
            Vector3 worstAt = default;
            _island1DunesOff = true;
            try
            {
                foreach (Vector3 v in vertices)
                {
                    if (OnIsland1Dune(v, 1.02f)) continue; // (the dunes themselves may be an older version of them)
                    float d = Mathf.Abs(v.y - Island3Seabed(v.x, v.z));
                    if (d > worst) { worst = d; worstAt = v; }
                }
            }
            finally { _island1DunesOff = false; }
            foreach (Vector3 v in vertices) dunes = Mathf.Max(dunes, Mathf.Abs(v.y - Island3Seabed(v.x, v.z)));
            if (dunes < 0.01f) { log.Add("dunes already in the terrain"); return; }
            if (worst > 0.02f)
            {
                log.Add($"DUNES SKIPPED: the saved terrain differs from the generator by {worst:0.000} m at {worstAt} (a hand edit?)");
                return;
            }
            var collider = terrain.GetComponent<MeshCollider>();
            float Ground(Vector3 p) => collider.Raycast(new Ray(new Vector3(p.x, 80f, p.z), Vector3.down), out RaycastHit hit, 200f) ? hit.point.y : p.y;
            var standing = new List<(Transform t, float before)>();
            foreach (Transform group in new[] { env, env.Find("Island1Palms"), env.Find("Island1Fun"), env.Find("BeachTowels_Island1"),
                         GameObject.Find("Items")?.transform, GameObject.Find("Story")?.transform })
            {
                if (group == null) continue;
                foreach (Transform t in group)
                {
                    Vector3 p = t.position;
                    if (t.name is "BeachTerrain" or "Island1Town" || p.x < -35f || p.x > 60f || p.z < -5f || p.z > 60f || !OnIsland1Dune(p, 1.1f)) continue;
                    standing.Add((t, Ground(p)));
                }
            }
            RefreshBeachTerrain(terrain);
            Physics.SyncTransforms();
            foreach ((Transform t, float before) in standing)
            {
                float lift = Ground(t.position) - before;
                if (Mathf.Abs(lift) < 0.01f) continue;
                t.position += Vector3.up * lift;
                log.Add($"lifted with a dune: {ScenePathOf(t)} {lift:0.00} m");
            }
            log.Add($"dunes raised ({Island1Dunes.Length})");
        }

        /// <summary>The red-roof lifeguard house makes way for a two-storey station on the same spot.</summary>
        private static void BuildLifeguardStation(Transform env, Func<float, float, float> groundAt, List<Rect> keepClear, List<string> log)
        {
            if (env.Find("Tower") is { } tower) { log.Add($"old lifeguard house removed at {tower.position}"); Object.DestroyImmediate(tower.gameObject); }
            if (env.Find("LifeguardStation") is { } again) Object.DestroyImmediate(again.gameObject);
            var root = new GameObject("LifeguardStation").transform;
            root.SetParent(env, false);
            // The floor 30 cm over the highest sand under the building (the footing goes 60 cm down).
            float sand = float.MinValue;
            for (float x = -StSx; x <= StSx + 0.01f; x += 1f)
                for (float z = StGz0; z <= StSz1 + 0.01f; z += 1f)
                    sand = Mathf.Max(sand, groundAt(StationSpot.x - x, StationSpot.z - z));
            root.SetPositionAndRotation(new Vector3(StationSpot.x, sand, StationSpot.z), Quaternion.Euler(0f, 180f, 0f)); // the sea side faces the sea
            TagSurface(root.gameObject, SurfaceKind.Wood);
            PropModel("lifeguard_station", root);

            void Solid(string name, Vector3 min, Vector3 max)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.localPosition = (min + max) * 0.5f;
                go.AddComponent<BoxCollider>().size = max - min;
            }
            const float t = 0.1f, rail = 1.1f;
            Solid("Floor", new Vector3(-StSx, -0.6f, StGz0), new Vector3(StSx, StF, StSz1));
            Solid("WallFrontL", new Vector3(-StGx, StF, -t), new Vector3(StDoor0, StH1, t));
            Solid("WallFrontR", new Vector3(StDoor1, StF, -t), new Vector3(StGx, StH1, t));
            Solid("WallFrontHead", new Vector3(StDoor0, StF + StDoorH, -t), new Vector3(StDoor1, StH1, t));
            Solid("WallBack", new Vector3(-StGx, StF, StGz0 - t), new Vector3(StGx, StH1, StGz0 + t));
            Solid("WallLeft", new Vector3(-StGx - t, StF, StGz0), new Vector3(-StGx + t, StH1, 0f));
            Solid("WallRight", new Vector3(StGx - t, StF, StGz0), new Vector3(StGx + t, StH1, 0f));
            Solid("Deck", new Vector3(-StSx, StH1, StSz0), new Vector3(StSx, StU, StSz1));
            Solid("Landing", new Vector3(StSx, StH1, StSz0), new Vector3(StStair1, StU, StStairHigh));
            foreach (float x in new[] { -StGx, -1.5f, 1.5f, StGx })
                Solid("PorchPost", new Vector3(x - 0.1f, StF, StSz1 - 0.25f), new Vector3(x + 0.1f, StH1, StSz1 - 0.05f));
            foreach (float z in new[] { StSz0 + 0.15f, StStairHigh - 0.15f })
                Solid("LandingPost", new Vector3(StStair1 - 0.21f, 0f, z - 0.09f), new Vector3(StStair1 - 0.03f, StH1, z + 0.09f));
            Solid("RailFront", new Vector3(-StSx, StU, StSz1 - 0.1f), new Vector3(StSx, StU + rail, StSz1));
            Solid("RailLeft", new Vector3(-StSx, StU, StSz0), new Vector3(-StSx + 0.1f, StU + rail, StSz1));
            Solid("RailBack", new Vector3(-StSx, StU, StSz0), new Vector3(StStair1, StU + rail, StSz0 + 0.1f));
            Solid("RailRight", new Vector3(StSx - 0.1f, StU, StStairHigh), new Vector3(StSx, StU + rail, StSz1));
            Solid("RailLanding", new Vector3(StStair1 - 0.1f, StU, StSz0), new Vector3(StStair1, StU + rail, StStairHigh));
            Solid("CabinFrontL", new Vector3(-StCx, StU, StCz1 - t), new Vector3(StCabDoor0, StE, StCz1 + t));
            Solid("CabinFrontR", new Vector3(StCabDoor1, StU, StCz1 - t), new Vector3(StCx, StE, StCz1 + t));
            Solid("CabinFrontHead", new Vector3(StCabDoor0, StU + 2.15f, StCz1 - t), new Vector3(StCabDoor1, StE, StCz1 + t));
            Solid("CabinBack", new Vector3(-StCx, StU, StCz0 - t), new Vector3(StCx, StE, StCz0 + t));
            Solid("CabinLeft", new Vector3(-StCx - t, StU, StCz0), new Vector3(-StCx + t, StE, StCz1));
            Solid("CabinRight", new Vector3(StCx - t, StU, StCz0), new Vector3(StCx + t, StE, StCz1));
            Solid("CabinRoof", new Vector3(-StCx - 0.45f, StE, StCz0 - 0.45f), new Vector3(StCx + 0.45f, StE + 1.1f, StCz1 + 0.45f));
            // The outside stairs: a ramp along the flight (it climbs toward -z), a hand rail on the open side.
            float run = StStairLow - StStairHigh, climb = Mathf.Atan2(StU, run) * Mathf.Rad2Deg, length = Mathf.Sqrt(run * run + StU * StU);
            Vector3 middle = new((StStair0 + StStair1) * 0.5f, StU * 0.5f - 0.05f, (StStairLow + StStairHigh) * 0.5f);
            Collider(root, "StairRamp", middle, new Vector3(StStair1 - StStair0 - 0.1f, 0.1f, length), Quaternion.Euler(climb, 0f, 0f));
            Collider(root, "StairRail", middle + new Vector3(StStair1 - middle.x - 0.04f, 0.55f, 0f), new Vector3(0.08f, rail, length), Quaternion.Euler(climb, 0f, 0f));

            BuildDoor(root, "StationDoor", new MeshyArt.DoorSpec
            {
                Hinge = new Vector3(StDoor0, StF, 0f), Width = StDoor1 - StDoor0, Height = StDoorH, LeafDirection = 1f, WallFacing = 1f,
            }, new Color(0.24f, 0.45f, 0.63f), new Color(0.95f, 0.95f, 0.93f), planks: true);
            // A stool in the lookout to sit and watch the water.
            if (PropModel("stool", root, new Vector3(-1.2f, StU, -3.4f), yaw: 20f, scale: 0.86f) != null)
                Collider(root, "Stool", new Vector3(-1.2f, StU + 0.3f, -3.4f), new Vector3(0.36f, 0.6f, 0.36f));
            NavKeepOut(root, new Vector3(0.6f, 2f, -2.1f), new Vector3(11.4f, 4f, 8.6f));
            keepClear.Add(Rect.MinMaxRect(StationSpot.x - StStair1 - 0.2f, StationSpot.z - StSz1 - 0.2f, StationSpot.x + StSx + 0.2f, StationSpot.z - StSz0 + 0.2f));
            log.Add($"lifeguard station at {root.position} (floor {root.position.y + StF:0.00}, deck {root.position.y + StU:0.00})");
        }

        /// <summary>The beach bar gets its big model: the colliders, the barista and every spot on the counter follow it.</summary>
        private static void EnlargeBeachBar(List<string> log, List<Rect> keepClear)
        {
            Transform bar = FindByPath("Environment/Island1Fun/BeachBar");
            if (bar == null) { log.Add("NO BEACH BAR"); return; }
            foreach (Transform c in bar.Cast<Transform>().ToArray())
                if (c.name is "Model_beach_bar" or "Model_beach_bar_big" or "Deck" or "Counter" or "BackShelf" or "Roof" or "Post" or "Stool" or "Menu"
                    or "TableTop" or "TableLeg" or "Torch" or "Rail" or "BarStoolSpot")
                    Object.DestroyImmediate(c.gameObject);
            PropModel("beach_bar_big", bar);
            void Solid(string name, Vector3 centre, Vector3 size)
            {
                var go = new GameObject(name);
                go.transform.SetParent(bar, false);
                go.transform.localPosition = centre;
                go.AddComponent<BoxCollider>().size = size;
            }
            const float deckTop = 0.15f;
            Solid("Deck", new Vector3(0f, deckTop * 0.5f, BbTerrace * 0.5f), new Vector3(2f * BbW + 0.4f, deckTop, 2f * BbD + 0.4f + BbTerrace));
            Solid("Counter", new Vector3(0f, 0.62f, BbCounterZ + 0.05f), new Vector3(2f * BbW + 0.1f, 1.14f, 0.72f));
            Solid("BackShelf", new Vector3(0f, 1.2f, -BbD + 0.2f), new Vector3(2f * BbW - 0.4f, 2.4f, 0.42f));
            Solid("Roof", new Vector3(0f, BbTop + 0.7f, 0f), new Vector3(2f * BbW + 1.5f, 1.2f, 2f * BbD + 1.5f));
            foreach (float x in new[] { -BbW, 0f, BbW })
                foreach (float z in new[] { -BbD, BbD })
                    Solid("Post", new Vector3(x, 1.6f, z), new Vector3(0.22f, 3f, 0.22f));
            for (int i = 0; i < BbStools; i++)
                Solid("Stool", new Vector3(-3.5f + i, 0.45f, BbD + 0.35f), new Vector3(0.22f, 0.6f, 0.22f)); // (slim: you can step between them to the counter)
            foreach (float x in new[] { -3f, 0f, 3f })
            {
                Solid("TableTop", new Vector3(x, 1.045f, BbD + 2.6f), new Vector3(0.9f, 0.05f, 0.9f));
                Solid("TableLeg", new Vector3(x, 0.6f, BbD + 2.6f), new Vector3(0.14f, 0.9f, 0.14f));
                foreach (float dx in new[] { -0.75f, 0.75f })
                    Solid("Stool", new Vector3(x + dx, 0.45f, BbD + 2.75f), new Vector3(0.22f, 0.6f, 0.22f));
            }
            foreach (float side in new[] { -1f, 1f })
            {
                Solid("Torch", new Vector3(side * (BbW - 0.1f), 1.1f, BbD + BbTerrace - 0.1f), new Vector3(0.12f, 2.1f, 0.12f));
                Solid("Rail", new Vector3(side * (BbW + 0.1f), 0.45f, BbD + 0.3f + (BbTerrace - 0.3f) * 0.5f), new Vector3(0.1f, 0.9f, BbTerrace - 0.3f));
                Solid("Menu", new Vector3(side * (BbW + 0.34f), 0.7f, BbD - 0.1f), new Vector3(0.1f, 1.4f, 0.7f));
            }

            // The barista behind the long counter; the serving spots, one per stool; the wipe and drink spots.
            if (bar.Find("Barista") is { } barista) barista.localPosition = new Vector3(0.25f, 0.158f, BbCounterZ - 0.55f);
            var serve = new List<Object>();
            foreach (Transform c in bar.Cast<Transform>().Where(c => c.name.StartsWith("ServeSpot")).ToArray()) Object.DestroyImmediate(c.gameObject);
            for (int i = 0; i < BbStools; i++)
            {
                var spot = new GameObject($"ServeSpot{i}").transform;
                spot.SetParent(bar, false);
                spot.localPosition = new Vector3(-3.5f + i, 1.36f, BbCounterZ + 0.23f);
                serve.Add(spot);
            }
            if (bar.GetComponentInChildren<Barista>() is { } b) SetRefs(b, "_serveSpots", serve.ToArray());
            if (bar.Find("WipeFrom") is { } wipeFrom) wipeFrom.localPosition = new Vector3(-0.25f, 1.19f, BbCounterZ - 0.17f);
            if (bar.Find("WipeTo") is { } wipeTo) wipeTo.localPosition = new Vector3(0.75f, 1.19f, BbCounterZ - 0.17f);
            foreach (Transform c in bar)
                if (c.name.StartsWith("DrinkSpot") || c.name == "BeerSpot")
                    c.localPosition = new Vector3(c.localPosition.x, c.localPosition.y, BbCounterZ);
            Vector3 p = bar.position;
            keepClear.Add(Rect.MinMaxRect(p.x - BbW - 1f, p.z - BbD - BbTerrace - 0.4f, p.x + BbW + 1f, p.z + BbD + 1f)); // (turned 180: the terrace is toward -z)
            log.Add($"beach bar enlarged at {p}");
        }

        /// <summary>Palms, towels and loose items standing where the village went move to the nearest free sand.</summary>
        private static void ClearIsland1Spots(Transform env, List<Rect> keepClear, Func<float, float, float> groundAt, List<string> log)
        {
            Vector2 Nearest(Vector2 p, float margin)
            {
                for (int pass = 0; pass < 6; pass++)
                {
                    Rect hit = keepClear.FirstOrDefault(r => Grow(r, margin).Contains(p));
                    if (hit.width <= 0f) return p;
                    Rect g = Grow(hit, margin + 0.05f);
                    float[] d = { p.x - g.xMin, g.xMax - p.x, p.y - g.yMin, g.yMax - p.y };
                    int k = Array.IndexOf(d, d.Min());
                    p = k switch { 0 => new Vector2(g.xMin, p.y), 1 => new Vector2(g.xMax, p.y), 2 => new Vector2(p.x, g.yMin), _ => new Vector2(p.x, g.yMax) };
                }
                return p;
            }
            void Move(Transform t, float margin)
            {
                Vector3 at = t.position;
                Vector2 to = Nearest(new Vector2(at.x, at.z), margin);
                if ((to - new Vector2(at.x, at.z)).sqrMagnitude < 1e-4f) return;
                float before = groundAt(at.x, at.z), after = groundAt(to.x, to.y);
                t.position = new Vector3(to.x, at.y + after - before, to.y);
                log.Add($"moved {ScenePathOf(t)}: {at} -> {t.position}");
            }
            if (env.Find("Island1Palms") is { } palms)
                foreach (Transform palm in palms)
                {
                    // The palm that stood where Milo's pier lands (the boardwalk and the arch go there): onto free sand east
                    // of it, not next to Milo.
                    Vector3 p = palm.position;
                    if (new Vector2(p.x - 33f, p.z - 9.2f).magnitude < 2.2f)
                    {
                        palm.position = new Vector3(37.2f, p.y + groundAt(37.2f, 11.2f) - groundAt(p.x, p.z), 11.2f);
                        log.Add($"moved {ScenePathOf(palm)}: {p} -> {palm.position} (clear of Milo's pier)");
                    }
                    Move(palm, 1.1f);
                }
            if (env.Find("BeachTowels_Island1") is { } towels)
                foreach (Transform towel in towels) Move(towel, 1.6f);
            if (GameObject.Find("Items") is { } items)
                foreach (Transform item in items.transform)
                    if (item.position.x > -35f && item.position.x < 60f && item.position.z > 0f && item.position.z < 60f) Move(item, 0.8f);
        }

        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        /// <summary>Batch: the sand height round the back of island 1 (Logs/island1-back.txt), to place things on the shore.</summary>
        public static void Island1BackShoreBatch()
        {
            var lines = new List<string>();
            for (float x = -24f; x <= 50f; x += 4f)
            {
                var row = new System.Text.StringBuilder($"x {x,4:0}: ");
                for (float z = 30f; z <= 56f; z += 2f) row.Append($"{BeachHeight(x, z),6:0.0}");
                lines.Add(row.ToString());
            }
            lines.Insert(0, "z from 30 to 56 step 2");
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/island1-back.txt", lines);
        }

        /// <summary>Batch: writes what stands on island 1 (each object's ground footprint) to Logs/island1-map.txt.</summary>
        public static void Island1MapBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var groups = new Dictionary<string, Bounds>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Bounds b = r.bounds;
                if (b.center.x < -35f || b.center.x > 60f || b.center.z < -12f || b.center.z > 58f || b.center.y > 40f) continue;
                string path = ScenePathOf(r.transform);
                string[] parts = path.Split('/');
                string key = string.Join("/", parts.Take(parts[0] == "Environment" && parts.Length > 2 && parts[1].Contains("Island1") ? 3 : 2));
                if (key.Contains("BeachTerrain") || key.Contains("Water") || key.Contains("Sea")) continue;
                key += "#" + Mathf.RoundToInt(b.center.x / 6f) + "," + Mathf.RoundToInt(b.center.z / 6f);
                if (groups.TryGetValue(key, out Bounds g)) { g.Encapsulate(b); b = g; }
                groups[key] = b;
            }
            var lines = groups.OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key.Split('#')[0],-60} x {kv.Value.min.x,6:0.0}..{kv.Value.max.x,6:0.0}  z {kv.Value.min.z,6:0.0}..{kv.Value.max.z,6:0.0}  y {kv.Value.min.y,5:0.0}..{kv.Value.max.y,5:0.0}");
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/island1-map.txt", lines);
            Debug.Log($"[Island1Town] PASS: map written ({groups.Count} groups)");
        }
    }
}
