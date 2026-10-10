using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>Island 1 (the station island): the smaller shape in the saved scene, review pictures, what stands where.</summary>
    public static partial class GameSceneBuilder
    {
        /// <summary>Batch: open the Game scene and reshape island 1 in it (with -pdd-player, then build the player).</summary>
        public static void ShrinkIsland1Batch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ShrinkIsland1InScene();
            if (!Environment.GetCommandLineArgs().Contains("-pdd-player")) return;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = BuildExe, target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Windows build failed: " + report.summary.totalErrors + " errors");
            File.Copy("steam_appid.txt", "Builds/Win64/steam_appid.txt", true);
            Debug.Log("[Island1] Windows build succeeded.");
        }

        /// <summary>
        /// Island 1 gets its new shape (<see cref="IslandCenter"/>, <see cref="IslandHalfSize"/>) without rebuilding the
        /// scene: the terrain is redrawn, the toilets and the bar move to their new spots, the west towels come round to
        /// the front beach, anything else the sea now covers is moved in onto the sand (palms there are taken away), the
        /// robber's land area is updated and the island's navmesh baked again.
        /// </summary>
        public static void ShrinkIsland1InScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            Transform env = GameObject.Find("Environment")?.transform;
            GameObject terrain = GameObject.Find("Environment/BeachTerrain");
            if (scene.path != ScenePath || env == null || terrain == null) throw new InvalidOperationException("Open the Game scene first.");
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, "Logs/Game-before-island1-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity", true);

            // What stands on island 1's sand now (top-most objects only), and the ground under each.
            var terrainCollider = terrain.GetComponent<MeshCollider>();
            var standing = new List<(Transform t, float ground)>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Vector3 p = t.position;
                if (p == Vector3.zero || p.x < -75f || p.x > 75f || p.z < -2f || p.z > 62f) continue;
                if (t == terrain.transform) continue;
                bool palmTree = t.name == "palm_tall_Pivot";
                if (!terrainCollider.Raycast(new Ray(new Vector3(p.x, 60f, p.z), Vector3.down), out RaycastHit hit, 120f)) continue;
                if (hit.point.y < WaterLevel + 0.1f && !palmTree) continue; // out at sea already (docks, boats, buoys)
                standing.Add((t, hit.point.y));
            }
            var set = new HashSet<Transform>(standing.Select(s => s.t));
            standing.RemoveAll(s => Ancestors(s.t).Any(set.Contains));

            RefreshBeachTerrain(terrain);
            var log = new List<string>();

            // The toilets and the bar move inland (the user's map); the west towels come round to the front beach.
            var moves = new Dictionary<string, Vector3>
            {
                ["Environment/Island1Fun/BeachToilets"] = HutSpot,
                ["Environment/Island1Fun/BeachBar"] = BarSpot,
                ["Environment/BeachTowels_Island1/Towel_0"] = new(-14f, 0f, 8.5f),
                ["Environment/BeachTowels_Island1/Towel_1"] = new(-11f, 0f, 11f),
                ["Environment/BeachTowels_Island1/Towel_2"] = new(7.5f, 0f, 6f),
                ["Environment/BeachTowels_Island1/Towel_3"] = new(-13.5f, 0f, 13.5f),
            };
            var moved = new List<Vector3>();
            // The two trampolines were on the cut-away west end: next to the basketball court now.
            Transform fun = FindByPath("Environment/Island1Fun");
            var trampolines = fun != null ? fun.Cast<Transform>().Where(c => c.name == "Trampoline").ToArray() : new Transform[0];
            Vector3[] trampolineSpots = { new(-16f, 0f, 23f), new(-12f, 0f, 40f) };
            var targets = moves.Select(m => (t: FindByPath(m.Key), path: m.Key, spot: m.Value)).ToList();
            for (int i = 0; i < trampolines.Length && i < trampolineSpots.Length; i++)
                targets.Add((trampolines[i], "Environment/Island1Fun/Trampoline", trampolineSpots[i]));
            foreach ((Transform t, string path, Vector3 spot) in targets)
            {
                if (t == null) { log.Add("missing " + path); continue; }
                float before = BeachHeightUnder(standing, t);
                Vector3 to = new(spot.x, t.position.y - before + BeachHeight(spot.x, spot.z), spot.z);
                log.Add($"moved {path} {t.position} -> {to}");
                t.position = to;
                moved.Add(to);
                standing.RemoveAll(s => s.t == t);
            }

            // Anything else whose sand the cut took away (things already at the water's edge stay: piers, the life ring),
            // and palms where the toilets, bar and towels now stand. Palms go (with the parrots' perches on them).
            var inland = new Vector2(IslandCenter.x, IslandCenter.y - 2f);
            Transform parrots = FindByPath("Environment/Island1Fun/Parrots");
            var goneCrowns = new List<Vector2>();
            foreach ((Transform t, float ground) in standing)
            {
                if (t == null || (parrots != null && t.IsChildOf(parrots))) continue;
                Vector3 p = t.position;
                bool palm = t.name.IndexOf("palm", StringComparison.OrdinalIgnoreCase) >= 0;
                bool inTheWay = palm && moved.Any(m => new Vector2(m.x - p.x, m.z - p.z).magnitude < 5f);
                bool sunk = BeachHeight(p.x, p.z) < ground - 0.05f && ShoreCoordinate(p.x, p.z) < 7f;
                bool drowned = palm && BeachHeight(p.x, p.z) < WaterLevel + 0.1f; // dry sand is at 0, the sea at WaterLevel
                if (!sunk && !inTheWay && !drowned) continue;
                if (palm)
                {
                    log.Add($"removed {ScenePathOf(t)} at {p}");
                    goneCrowns.Add(new Vector2(p.x, p.z));
                    Object.DestroyImmediate(t.gameObject);
                    continue;
                }
                var q = new Vector2(p.x, p.z);
                for (int i = 0; i < 200 && ShoreCoordinate(q.x, q.y) < 8f; i++) q = Vector2.MoveTowards(q, inland, 0.5f);
                Vector3 to = new(q.x, p.y - ground + BeachHeight(q.x, q.y), q.y);
                log.Add($"moved in {ScenePathOf(t)} {p} -> {to}");
                t.position = to;
            }
            var flock = parrots != null ? parrots.GetComponent<Fun.ParrotFlock>() : null;
            if (flock != null)
            {
                var so = new SerializedObject(flock);
                SerializedProperty perches = Require(so, "_perches");
                var keep = new List<Object>();
                for (int i = 0; i < perches.arraySize; i++)
                {
                    var perch = perches.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (perch == null) continue;
                    var at = new Vector2(perch.position.x, perch.position.z);
                    if (goneCrowns.Any(c => (c - at).magnitude < 2.5f))
                    {
                        log.Add($"removed perch {perch.name} at {perch.position}");
                        Object.DestroyImmediate(perch.gameObject);
                        continue;
                    }
                    keep.Add(perch);
                }
                Require(so, "_skyCentre").vector3Value = new Vector3(IslandCenter.x, 0f, IslandCenter.y - 8f);
                so.ApplyModifiedPropertiesWithoutUndo();
                SetRefs(flock, "_perches", keep.ToArray());
            }

            // The robber's run-about area on island 1.
            var director = Object.FindFirstObjectByType<StoryDirector>();
            if (director != null)
            {
                var so = new SerializedObject(director);
                Require(so, "_island1").FindPropertyRelative("LandArea").rectValue = Island1Land;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            BakeNavMeshes(only: "Island1");
            File.WriteAllLines("Logs/island1-shrink.txt", log);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            CaptureIsland1();
            Debug.Log($"[Island1] PASS: island 1 reshaped ({log.Count} changes, see Logs/island1-shrink.txt).");
        }

        private static IEnumerable<Transform> Ancestors(Transform t)
        {
            for (Transform up = t.parent; up != null; up = up.parent) yield return up;
        }

        private static float BeachHeightUnder(List<(Transform t, float ground)> standing, Transform t)
        {
            foreach ((Transform s, float ground) in standing) if (s == t) return ground;
            return 0f;
        }

        private static Transform FindByPath(string path)
        {
            string[] parts = path.Split('/');
            Transform t = GameObject.Find(parts[0])?.transform;
            for (int i = 1; t != null && i < parts.Length; i++) t = t.Find(parts[i]);
            return t;
        }

        private static void CaptureIsland1() => CaptureViews("Screenshots/Review/Island1", new[]
        {
            ("overview-top", new Vector3(12f, 120f, 22f), new Vector3(12f, 0f, 23f)),
            ("overview", new Vector3(12f, 60f, -50f), new Vector3(12f, 0f, 24f)),
            ("bar", new Vector3(22f, 6f, 20f), new Vector3(24f, 2f, 35f)),
        });

        /// <summary>Batch: top-down and angled pictures of island 1, and every object on it with its position.</summary>
        public static void ReviewIsland1Batch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var lines = new List<string>();
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                Vector3 p = t.position;
                if (p.x < -90f || p.x > 90f || p.z < -15f || p.z > 75f || p.y < -1.5f) continue;
                int depth = Ancestors(t).Count();
                if (depth > 2 || (t.GetComponentInChildren<Renderer>() == null && t.GetComponent<Collider>() == null && depth > 0)) continue;
                lines.Add($"{p.x,7:0.0} {p.z,7:0.0} {p.y,6:0.0}  {ScenePathOf(t)}");
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllLines("Logs/island1-objects.txt", lines.OrderBy(l => l));
            CaptureIsland1();
        }

        private static string ScenePathOf(Transform t) => t.parent == null ? t.name : ScenePathOf(t.parent) + "/" + t.name;
    }
}
