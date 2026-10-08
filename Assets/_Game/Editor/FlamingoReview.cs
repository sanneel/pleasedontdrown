using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static class FlamingoReview
    {
        public static void VerifyAndBuildBatch()
        {
            CaptureBatch();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SpeechBubbleReview.Capture();
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            WeaponGripReview.BuildSavedGameBatch();
        }

        public static void CaptureBatch()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.35f, .4f, .5f);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 2f;
            sun.transform.rotation = Quaternion.Euler(40, -30, 0);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Meshy/flamingo.glb");
            if (prefab == null) throw new System.InvalidOperationException("Flamingo import failed");
            var model = Object.Instantiate(prefab);
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            var camera = new GameObject("Review camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .2f, .27f);
            camera.nearClipPlane = .02f;
            camera.fieldOfView = 45f;
            Directory.CreateDirectory("Screenshots/Review/Flamingo");
            for (int i = 0; i < 4; i++)
            {
                Vector3 direction = Quaternion.Euler(0, i * 90, 0) * new Vector3(1, .6f, -1).normalized;
                camera.transform.position = bounds.center + direction * bounds.size.magnitude * 1.2f;
                camera.transform.LookAt(bounds.center);
                var target = new RenderTexture(1200, 900, 24) { antiAliasing = 4 };
                var old = RenderTexture.active;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(1200, 900, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0);
                image.Apply();
                File.WriteAllBytes($"Screenshots/Review/Flamingo/sun-{i}.png", image.EncodeToPNG());
                camera.targetTexture = null;
                RenderTexture.active = old;
                Object.DestroyImmediate(image);
                target.Release();
                Object.DestroyImmediate(target);
            }
            Debug.Log("[FlamingoReview] Four sun-angle captures complete.");
        }
    }
}
