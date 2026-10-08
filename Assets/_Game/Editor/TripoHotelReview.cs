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
    public static class TripoHotelReview
    {
        public const string ModelPath = "Assets/TripoModels/apartment_building_3d_model/apartment_building_3d_model.fbx";
        public static Bounds ModelBounds(Transform root)
        {
            Bounds bounds = default; bool first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                var b = filter.sharedMesh.bounds;
                var matrix = root.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; } else bounds.Encapsulate(point);
                }
            }
            if (first) throw new InvalidOperationException("Hotel has no geometry");
            return bounds;
        }

        [MenuItem("PLEASE DON'T DROWN/Review imported Tripo hotel")]
        public static void Inspect()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (asset == null) throw new InvalidOperationException("Hotel FBX has not imported");
            var active = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            bool fog = RenderSettings.fog;
            try
            {
                RenderSettings.fog = false;
                var model = new GameObject("InspectionPivot");
                Object.Instantiate(asset, model.transform);
                Bounds b = ModelBounds(model.transform);
                long triangles = model.GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.GetIndexCount(0) / 3L);
                var report = $"Bounds: centre={b.center:F4} size={b.size:F4}\nTriangles: {triangles}\n";
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                    report += $"Renderer {r.name}: {string.Join(", ", r.sharedMaterials.Select(m => m == null ? "MISSING" : m.name + " / " + m.shader.name))}\n";
                Directory.CreateDirectory("Screenshots/Review/TripoHotel"); Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/tripo-hotel-inspection.txt", report); Debug.Log("[TripoHotel] " + report);
                // Normalize only this disposable inspection copy; original FBX and scene copy stay intact.
                float scale = 20 / Mathf.Max(b.size.x, b.size.z);
                model.transform.localScale = Vector3.one * scale;
                model.transform.position = -new Vector3(b.center.x, b.min.y, b.center.z) * scale;
                foreach (var t in model.GetComponentsInChildren<Transform>()) t.gameObject.layer = 30;
                var light = new GameObject("ReviewSun").AddComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.6f; light.cullingMask = 1 << 30;
                light.transform.rotation = Quaternion.Euler(35, -35, 0);
                var fill = new GameObject("ReviewFill").AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = .7f; fill.cullingMask = 1 << 30;
                fill.transform.rotation = Quaternion.Euler(20, 150, 0);
                var cam = new GameObject("ReviewCamera").AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.18f, .23f, .27f);
                cam.cullingMask = 1 << 30; cam.nearClipPlane = .01f; cam.farClipPlane = 200;
                cam.orthographic = true; cam.orthographicSize = Mathf.Max(8, b.size.y * scale * .7f);
                Vector3 centre = new(0, b.size.y * scale * .45f, 0);
                var views = new[] { new Vector3(24, 17, 30), new Vector3(-24, 17, -30), new Vector3(0, 5, 30), new Vector3(0, 5, -30) };
                for (int i = 0; i < views.Length; i++)
                {
                    cam.transform.position = centre + views[i]; cam.transform.LookAt(centre);
                    var rt = new RenderTexture(1400, 1000, 24) { antiAliasing = 4 };
                    var previous = RenderTexture.active; var texture = new Texture2D(1400, 1000, TextureFormat.RGB24, false);
                    try
                    {
                        cam.targetTexture = rt; cam.Render(); cam.Render(); RenderTexture.active = rt;
                        texture.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); texture.Apply();
                        File.WriteAllBytes($"Screenshots/Review/TripoHotel/source-{i}.png", texture.EncodeToPNG());
                    }
                    finally { cam.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(texture); rt.Release(); Object.DestroyImmediate(rt); }
                }
            }
            finally { RenderSettings.fog = fog; EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(active); }
        }

        public static void PlaceAndBuild()
        {
            GameSceneBuilder.ExpandResort();
            var hotel = GameObject.Find("Environment/Hotel/ResortExterior");
            if (hotel == null || hotel.GetComponentsInChildren<LODGroup>().Length != 1)
                throw new InvalidOperationException("Imported hotel LOD assets are not ready; no imported-hotel build produced.");
            GameSceneBuilder.BuildResortPlayer();
            Debug.Log("[TripoHotel] PASS: imported single hotel placed, reception clear, Windows build succeeded.");
        }

        public static void ReportScene()
        {
            var hotel = GameObject.Find("Environment/Hotel");
            if (hotel == null) throw new InvalidOperationException("Hotel scene root missing");
            string report = $"Hotel world {hotel.transform.position:F3}\n";
            foreach (Transform child in hotel.transform)
                report += $"Hotel child {child.name}, position={child.localPosition:F3}, components={string.Join(",", child.GetComponents<Component>().Select(c => c == null ? "missing" : c.GetType().Name))}\n";
            foreach (var group in hotel.GetComponentsInChildren<LODGroup>(true))
                report += $"Building {group.name}, active={group.gameObject.activeInHierarchy}, position={group.transform.localPosition:F3}, scale={group.transform.localScale:F3}\n";
            foreach (var box in hotel.GetComponentsInChildren<BoxCollider>(true))
                if (box.name.Contains("Shell") || box.name.Contains("Ground")) report += $"Collider {box.name}: {box.bounds}\n";
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var path = AssetDatabase.GetAssetPath(filter.sharedMesh);
                    if (path.StartsWith("Assets/TripoModels/apartment_building_3d_model"))
                        report += $"Raw hotel {filter.name}, mesh={path}, position={filter.transform.position:F3}, bounds={filter.GetComponent<Renderer>()?.bounds}\n";
                }
            File.WriteAllText("Logs/tripo-hotel-live-scene.txt", report); Debug.Log("[TripoHotelScene] " + report);
        }
    }

    [InitializeOnLoad]
    internal static class TripoHotelRequest
    {
        static TripoHotelRequest() { EditorApplication.update += Tick; }
        private static void Tick()
        {
            const string request = "Logs/tripo-hotel-request.txt";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string action = File.ReadAllText(request).Trim(); File.Delete(request);
            try
            {
                if (action == "place") TripoHotelReview.PlaceAndBuild();
                else if (action == "scene") TripoHotelReview.ReportScene();
                else if (action == "polish") GameSceneBuilder.PolishSingleResort();
                else TripoHotelReview.Inspect();
                File.WriteAllText("Logs/tripo-hotel-result.txt", "PASS: " + action);
            }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Logs/tripo-hotel-result.txt", "FAIL " + e); }
        }
    }
}
