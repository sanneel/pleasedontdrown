using System;
using System.IO;
using System.Linq;
using FishNet.Object;
using PleaseDontDrown.World.Water;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        [MenuItem("PLEASE DON'T DROWN/Fix hotel island height and remove unwanted rocks")]
        public static void FixIslandHeightAndRocks()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Open Game in edit mode first.");
            Transform environment = GameObject.Find("Environment")?.transform;
            var terrain = GameObject.Find("Environment/BeachTerrain");
            if (environment == null || terrain == null) throw new InvalidOperationException("Terrain/environment missing.");
            var collider = terrain.GetComponent<MeshCollider>();
            var filter = terrain.GetComponent<MeshFilter>();
            if (filter == null || collider == null || filter.sharedMesh == null)
                throw new InvalidOperationException("Terrain mesh/collision missing.");
            if (terrain.transform.position != Vector3.zero || terrain.transform.rotation != Quaternion.identity || terrain.transform.lossyScale != Vector3.one)
                throw new InvalidOperationException("Shared terrain transform was moved; inspect it before changing island heights.");
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, $"Logs/Game-before-rock-height-fix-{DateTime.Now:yyyyMMdd-HHmmss}.unity", true);
            Physics.SyncTransforms();
            float OldGround(Vector3 p) => collider.Raycast(new Ray(new Vector3(p.x, 100f, p.z), Vector3.down), out var hit, 200f)
                ? hit.point.y : BeachHeight(p.x, p.z);

            int removed = 0, moved = 0, changed = 0;
            foreach (Transform rock in environment.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Rock").ToArray())
            {
                Vector3 p = rock.position;
                bool firstIsland = Mathf.Abs(p.x) < 100f && p.z > -100f && p.z < 130f;
                bool hotelIsland = p.x > -210f && p.x < 245f && p.z > -535f && p.z < -175f;
                if ((!firstIsland && !hotelIsland) || rock.GetComponentInChildren<NetworkObject>(true) != null) continue;
                Debug.Log($"[IslandFix] Removed unwanted decorative rock at {p:F2}.");
                Object.DestroyImmediate(rock.gameObject);
                removed++;
            }

            // Keep props' existing offsets from the sand while repairing the terrain beneath them.
            void MovePlaced(Transform t)
            {
                if (t == terrain.transform || t.GetComponent<WaterSurface>() != null) return;
                Vector3 p = t.position;
                bool container = new Vector2(p.x, p.z).sqrMagnitude < .01f;
                if (!container && OnIsland2(p))
                {
                    float delta = BeachHeight(p.x, p.z) - OldGround(p);
                    if (Mathf.Abs(delta) > .005f) { t.position += Vector3.up * delta; moved++; }
                    return;
                }
                if (container) foreach (Transform child in t.Cast<Transform>().ToArray()) MovePlaced(child);
            }
            foreach (GameObject root in scene.GetRootGameObjects()) MovePlaced(root.transform);

            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i];
                if (p.x < -210f || p.x > 245f || p.z < -535f || p.z > -175f) continue;
                float height = BeachHeight(p.x, p.z);
                if (Mathf.Abs(p.y - height) > .0001f) { p.y = height; vertices[i] = p; changed++; }
            }
            if (changed > 0)
            {
                mesh.vertices = vertices;
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                EditorUtility.SetDirty(mesh);
            }
            collider.sharedMesh = null; collider.sharedMesh = mesh;
            var seabed = new SerializedObject(terrain.GetComponent<Seabed>());
            Require(seabed, "_grid").objectReferenceValue = mesh;
            seabed.ApplyModifiedPropertiesWithoutUndo();
            Transform hotel = environment.Find("Hotel");
            if (hotel == null) throw new InvalidOperationException("Hotel missing.");
            float hotelGround = BeachHeight(hotel.position.x, hotel.position.z);
            if (Mathf.Abs(hotel.position.y - hotelGround) > .005f)
            { hotel.position = new Vector3(hotel.position.x, hotelGround, hotel.position.z); moved++; }
            Physics.SyncTransforms();
            foreach (Vector3 p in new[] { hotel.position, new Vector3(20f, 0f, -350f), new Vector3(-80f, 0f, -350f), new Vector3(120f, 0f, -350f) })
            {
                float actual = OldGround(p), expected = BeachHeight(p.x, p.z);
                if (actual < 1.45f || Mathf.Abs(actual - expected) > .03f)
                    throw new InvalidOperationException($"Hotel island height mismatch at {p:F2}: {actual:F3}, expected {expected:F3}.");
            }
            BakeNavMeshes(only: "Island1");
            BakeNavMeshes(only: "Island2");
            AssignSceneIds(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            CaptureViews("Screenshots/Review/IslandHeightFix", new[] {
                ("first-island", new Vector3(55f, 45f, -45f), new Vector3(0f, 0f, 18f)),
                ("hotel-island", new Vector3(180f, 130f, -140f), new Vector3(20f, 0f, -330f)),
                ("hotel-approach", new Vector3(32f, 4.2f, -206f), new Vector3(20f, 3f, -239f)) });
            string report = $"PASS: removed {removed} unwanted rocks; repaired {changed} terrain vertices; adjusted {moved} placed objects. Hotel floor {hotel.position.y:F2} m, inland sand {hotelGround:F2} m. Island 1/2 navigation rebaked.\n";
            File.WriteAllText("Logs/island-height-fix-result.txt", report);
            Debug.Log("[IslandFix] " + report);
        }

        public static void FixIslandHeightAndRocksBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            FixIslandHeightAndRocks();
            if (Environment.GetCommandLineArgs().Contains("-pddBuild")) BuildResortPlayer();
        }
    }
}
