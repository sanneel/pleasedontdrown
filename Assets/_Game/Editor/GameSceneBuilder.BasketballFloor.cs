using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        private const float BasketballFloorHeight = .45f;

        // Separate headless build after the graphical review: keeps large preview assets out of compiler memory.
        public static void BuildBasketballPlayerBatch()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = "Builds/Win64/PleaseDontDrown.exe", target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Basketball Windows build failed: " + report.summary.totalErrors + " errors");
            File.Copy("steam_appid.txt", "Builds/Win64/steam_appid.txt", true);
            Debug.Log("[BasketballFloor] Windows build succeeded.");
        }

        // Two metres of flat support beyond the painted area keep the coarse beach triangles flat too.
        private static float BasketballGroundHeight(float x, float z, float original)
        {
            float outside = Mathf.Max(Mathf.Abs(x + 6f) - 10f, Mathf.Abs(z - 26.5f) - 8.5f);
            float blend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outside / 4f));
            return Mathf.Lerp(original, BasketballFloorHeight, blend);
        }

        public static void FixBasketballFloorBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Scene scene = SceneManager.GetActiveScene();
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, $"Logs/Game-before-flat-court-{DateTime.Now:yyyyMMdd-HHmmss}.unity", true);
            var terrain = GameObject.Find("Environment/BeachTerrain");
            var filter = terrain.GetComponent<MeshFilter>();
            var collider = terrain.GetComponent<MeshCollider>();
            Physics.SyncTransforms();
            float OldHeight(Vector3 p) => collider.Raycast(new Ray(new Vector3(p.x, 100f, p.z), Vector3.down), out var hit, 200f)
                ? hit.point.y : p.y;
            int moved = 0;
            void MovePlaced(Transform t)
            {
                if (t == terrain.transform || t.GetComponent<PleaseDontDrown.World.Water.WaterSurface>() != null) return;
                Vector3 p = t.position;
                if (new Vector2(p.x, p.z).sqrMagnitude < .01f)
                {
                    foreach (Transform child in t.Cast<Transform>().ToArray()) MovePlaced(child);
                    return;
                }
                if (p.x < -20f || p.x > 8f || p.z < 14f || p.z > 39f || t.name == "Court") return;
                float delta = BeachHeight(p.x, p.z) - OldHeight(p);
                if (Mathf.Abs(delta) > .005f) { t.position += Vector3.up * delta; moved++; }
            }
            foreach (GameObject root in scene.GetRootGameObjects()) MovePlaced(root.transform);
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int changed = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                if (p.x < -20f || p.x > 8f || p.z < 14f || p.z > 39f) continue;
                float height = BeachHeight(p.x, p.z);
                if (Mathf.Abs(p.y - height) < .0001f) continue;
                vertices[i].y = height; changed++;
            }
            // This regular grid has unique vertices and valid triangles; PhysX does not need to weld/clean it.
            // Avoid the large temporary allocation when cooking the entire island seabed a second time.
            collider.sharedMesh = null;
            collider.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase;
            mesh.vertices = vertices; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            Transform hoop = Object.FindFirstObjectByType<PleaseDontDrown.Fun.BasketballHoop>().transform;
            hoop.position = new Vector3(hoop.position.x, BasketballFloorHeight, hoop.position.z);
            foreach (Transform court in hoop.parent.Cast<Transform>().Where(t => t.name == "Court").ToArray())
                Object.DestroyImmediate(court.gameObject);
            BuildBasketballCourt(hoop, hoop.parent);
            Physics.SyncTransforms();
            float min = float.MaxValue, max = float.MinValue;
            int probes = 0;
            for (float x = -14f; x <= 2f; x += .5f)
            for (float z = 21f; z <= 32f; z += .5f)
            {
                float height = OldHeight(new Vector3(x, 0f, z));
                min = Mathf.Min(min, height); max = Mathf.Max(max, height); probes++;
                if (Mathf.Abs(height - BasketballFloorHeight) > .002f)
                    throw new InvalidOperationException($"Court is not flat at {x},{z}: {height}.");
            }
            CheckBasketballCourt(hoop, hoop.parent.Find("Court"));
            BakeNavMeshes(only: "Island1");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            CaptureViews("Screenshots/Review/BasketballFloor", new[] {
                ("court-flat", new Vector3(7f, 5f, 17f), new Vector3(-6f, .45f, 26f)),
                ("court-low", new Vector3(-11f, 1.3f, 21f), new Vector3(-6f, .45f, 30f)) });
            File.WriteAllText("Logs/basketball-floor-result.txt", $"PASS: {probes} collision probes; height {min:F4}..{max:F4} m; {changed} terrain vertices flattened; {moved} props grounded.\n");
            // Discard preview cameras before grip review and player build save the scene.
            EditorSceneManager.OpenScene(ScenePath);
            ReviewBasketballGripsBatch();
        }
    }
}
