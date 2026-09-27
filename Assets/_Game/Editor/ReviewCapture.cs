using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Renders review shots of the saved Game scene in batch mode (no player build needed).
    /// -executeMethod PleaseDontDrown.Editor.ReviewCapture.CaptureBatch -shots Screenshots/shots.txt
    /// Each line of the shots file: <c>name px py pz tx ty tz [fov]</c>, or <c>name top cx cz size</c> for an
    /// orthographic top-down view. Images go to Screenshots/Review/ (ignored by Git).
    /// Runtime-only things (the ocean mesh, players) are not in the saved scene and don't show.
    /// </summary>
    public static class ReviewCapture
    {
        public static void CaptureBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-shots");
                string file = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Screenshots/shots.txt";
                Capture(File.ReadAllLines(file));
                Debug.Log("[Review] captures complete");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Review] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Capture(string[] lines)
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            Directory.CreateDirectory("Screenshots/Review");
            var camera = GameObject.Find("MenuCamera").GetComponent<Camera>();
            camera.farClipPlane = 2000f;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                string[] p = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                float F(int k) => float.Parse(p[k], CultureInfo.InvariantCulture);
                RenderSettings.fog = p[1] != "top"; // haze would hide a map view
                if (p[1] == "top")
                {
                    camera.orthographic = true;
                    camera.orthographicSize = F(4) * 0.5f;
                    camera.transform.SetPositionAndRotation(new Vector3(F(2), 300f, F(3)), Quaternion.Euler(90f, 0f, 0f));
                }
                else
                {
                    camera.orthographic = false;
                    camera.fieldOfView = p.Length > 7 ? F(7) : 55f;
                    camera.transform.position = new Vector3(F(1), F(2), F(3));
                    camera.transform.LookAt(new Vector3(F(4), F(5), F(6)));
                }
                Render(camera, p[0]);
            }
        }

        private static void Render(Camera camera, string name)
        {
            const int w = 1200, h = 800;
            var rt = new RenderTexture(w, h, 24);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            texture.Apply();
            File.WriteAllBytes($"Screenshots/Review/{name}.jpg", texture.EncodeToJPG(88));
            camera.targetTexture = null;
            RenderTexture.active = null;
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(texture);
        }
    }
}
