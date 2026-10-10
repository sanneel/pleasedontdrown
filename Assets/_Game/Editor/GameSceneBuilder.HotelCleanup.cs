using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        /// <summary>Lists every renderer round the island-two hotel with its world bounds (Logs/hotel-renderers.txt).</summary>
        public static void DumpHotelRenderersBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var area = new Bounds(new Vector3(20, 20, -244), new Vector3(110, 80, 110));
            var log = new StringBuilder();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                         .Where(r => r.bounds.Intersects(area)).OrderBy(r => Path(r.transform)))
            {
                var b = r.bounds;
                log.AppendLine($"{Path(r.transform)}  active={r.gameObject.activeInHierarchy} min={b.min:F1} max={b.max:F1} " +
                               $"mat={string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "null"))} " +
                               $"mesh={(r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh ? f.sharedMesh.name : "-")}");
            }
            File.WriteAllText("Logs/hotel-renderers.txt", log.ToString());

            static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
        }

        /// <summary>Eye-level and close-up pictures all round the hotel (Screenshots/Review/HotelShell).</summary>
        public static void CaptureHotelShellBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            CaptureViews("Screenshots/Review/HotelShell", HotelShellViews());
        }

        /// <summary>Rays at the hotel from all four sides at several heights (Logs/hotel-probe.txt).</summary>
        public static void ProbeHotelBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Physics.SyncTransforms();
            var log = new StringBuilder();
            foreach (float y in new[] { 2.2f, 3.5f, 4.5f, 6f })
            {
                for (float x = -10; x <= 50; x += 2.5f)
                    foreach (var (from, dir) in new[] { (new Vector3(x, y, -300), Vector3.forward), (new Vector3(x, y, -200), Vector3.back) })
                        log.AppendLine($"y{y} x{x} {(dir.z > 0 ? "back" : "front")}: " + string.Join(" | ",
                            Physics.RaycastAll(from, dir, 120, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(3)
                                .Select(h => $"{h.collider.name}@z{h.point.z:F2}")));
                for (float z = -275; z <= -230; z += 2.5f)
                    foreach (var (from, dir) in new[] { (new Vector3(-40, y, z), Vector3.right), (new Vector3(80, y, z), Vector3.left) })
                        log.AppendLine($"y{y} z{z} {(dir.x > 0 ? "west" : "east")}: " + string.Join(" | ",
                            Physics.RaycastAll(from, dir, 120, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(3)
                                .Select(h => $"{h.collider.name}@x{h.point.x:F2}")));
            }
            File.WriteAllText("Logs/hotel-probe.txt", log.ToString());
        }

        /// <summary>Facade depth profiles: first hit on the hotel mesh along fine lines (Logs/hotel-profile.txt).</summary>
        public static void ProfileHotelBatch()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var tower = GameObject.Find("Environment/Hotel/ResortExterior/CentralTower").transform;
            var mesh = new GameObject("probe").AddComponent<MeshCollider>();
            mesh.sharedMesh = tower.Find("HotelLOD0/HotelLOD0").GetComponent<MeshFilter>().sharedMesh;
            mesh.transform.SetPositionAndRotation(tower.Find("HotelLOD0/HotelLOD0").position, tower.Find("HotelLOD0/HotelLOD0").rotation);
            mesh.transform.localScale = tower.Find("HotelLOD0/HotelLOD0").lossyScale;
            Physics.SyncTransforms();
            var log = new StringBuilder();
            foreach (float y in new[] { 2.5f, 4f, 6f, 9f, 13f, 18f, 24f, 30f, 36f })
            {
                log.Append($"BACK y{y}:");
                for (float x = -6; x <= 46; x += .5f)
                    log.Append(mesh.Raycast(new Ray(new Vector3(x, y, -290), Vector3.forward), out var h, 80) ? $" {x}:{h.point.z:F1}" : $" {x}:-");
                log.AppendLine();
                log.Append($"FRONT y{y}:");
                for (float x = -6; x <= 46; x += .5f)
                    log.Append(mesh.Raycast(new Ray(new Vector3(x, y, -215), Vector3.back), out var h, 80) ? $" {x}:{h.point.z:F1}" : $" {x}:-");
                log.AppendLine();
                log.Append($"WEST y{y}:");
                for (float z = -266; z <= -230; z += .5f)
                    log.Append(mesh.Raycast(new Ray(new Vector3(-30, y, z), Vector3.right), out var h, 80) ? $" {z}:{h.point.x:F1}" : $" {z}:-");
                log.AppendLine();
                log.Append($"EAST y{y}:");
                for (float z = -266; z <= -230; z += .5f)
                    log.Append(mesh.Raycast(new Ray(new Vector3(70, y, z), Vector3.left), out var h, 80) ? $" {z}:{h.point.x:F1}" : $" {z}:-");
                log.AppendLine();
            }
            File.WriteAllText("Logs/hotel-profile.txt", log.ToString());
        }

        private static (string, Vector3, Vector3)[] HotelShellViews()
        {
            var views = new System.Collections.Generic.List<(string, Vector3, Vector3)>();
            var centre = new Vector3(20, 8, -253);
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30 * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                views.Add(($"ring{i * 30:000}", centre + dir * 45 + Vector3.up * -4.7f, centre + Vector3.up * 2));
                views.Add(($"close{i * 30:000}", new Vector3(centre.x + dir.x * 33, 3.3f, centre.z + dir.z * 18), centre + Vector3.up * 8));
            }
            views.Add(("top", new Vector3(20, 90, -230), new Vector3(20, 0, -253)));
            views.Add(("topdown", new Vector3(20.01f, 110, -253), new Vector3(20, 0, -253)));
            foreach (float x in new[] { 2f, 9f, 14f, 20f, 26f, 31f, 38f })
            {
                views.Add(($"back_x{x}", new Vector3(x, 3.4f, -272), new Vector3(x, 14, -262)));
                views.Add(($"backhigh_x{x}", new Vector3(x - 6, 20, -280), new Vector3(x, 18, -262)));
            }
            for (float x = -2; x <= 42; x += 11)
            {
                views.Add(($"base_back_x{x}", new Vector3(x, 3.2f, -271), new Vector3(x, 4.5f, -255)));
                views.Add(($"base_front_x{x}", new Vector3(x, 3.2f, -234), new Vector3(x, 4.5f, -250)));
            }
            for (float z = -262; z <= -242; z += 10)
            {
                views.Add(($"base_w_z{z}", new Vector3(-13, 3.2f, z), new Vector3(5, 4.5f, z)));
                views.Add(($"base_e_z{z}", new Vector3(53, 3.2f, z), new Vector3(35, 4.5f, z)));
            }
            views.Add(("side_w",new Vector3(-16, 3.4f, -253), new Vector3(0, 10, -253)));
            views.Add(("side_e", new Vector3(56, 3.4f, -253), new Vector3(40, 10, -253)));
            return views.ToArray();
        }
    }
}
