using System.IO;
using PleaseDontDrown.Items;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        public static void BuildConsumablePolishBatch()
        {
            PolishConsumablesBatch();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Game/Scenes/Game.unity" },
                locationPathName = "Builds/Win64/PleaseDontDrown.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new System.InvalidOperationException("Consumable polish build failed");
            File.Copy("steam_appid.txt", "Builds/Win64/steam_appid.txt", true);
            Debug.Log("[ConsumableReview] Windows build succeeded.");
        }

        public static void PolishConsumablesBatch()
        {
            AssetDatabase.Refresh();
            foreach (string name in new[] { "Beer", "Coconut" })
            {
                string path = $"Assets/_Game/Items/Prefabs/{name}.prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    RefreshConsumableModel(root.transform, name);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            // Update any scene copies as well as the rack's spawn prefabs.
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            foreach (Item item in Object.FindObjectsByType<Item>(FindObjectsSortMode.None))
                if (item.DisplayName is "Beer" or "Coconut") RefreshConsumableModel(item.transform, item.DisplayName);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            CaptureConsumables();
            AudioPreview.ExportBatch();
            Debug.Log("[ConsumableReview] Prefabs, scene, visual capture and audio exports complete.");
        }

        private static void RefreshConsumableModel(Transform root, string name)
        {
            string model = name == "Beer" ? "beer_bottle" : "coconut";
            var existing = root.Find("Model_" + model);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);
            PropModel(model, root);
        }

        private static void CaptureConsumables()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.65f, .69f, .75f);
            var key = new GameObject("Key").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 2;
            key.transform.rotation = Quaternion.Euler(35, -30, 0);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = .65f;
            fill.transform.rotation = Quaternion.Euler(20, 140, 0);
            PropModel("beer_bottle", null, new Vector3(-.13f, .15f, 0));
            PropModel("coconut", null, new Vector3(.15f, .115f, 0));
            var camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.075f, .14f, .17f);
            camera.orthographic = true; camera.orthographicSize = .25f;
            camera.nearClipPlane = .01f;
            camera.transform.position = new Vector3(.42f, .46f, -.95f);
            camera.transform.LookAt(new Vector3(.015f, .14f, 0));
            var target = new RenderTexture(1100, 850, 24) { antiAliasing = 4 };
            var previous = RenderTexture.active;
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            var texture = new Texture2D(1100, 850, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 1100, 850), 0, 0); texture.Apply();
            Directory.CreateDirectory("Screenshots/Review/Consumables");
            File.WriteAllBytes("Screenshots/Review/Consumables/items.png", texture.EncodeToPNG());
            camera.targetTexture = null; RenderTexture.active = previous;
            Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
