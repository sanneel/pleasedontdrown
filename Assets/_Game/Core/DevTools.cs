using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Core
{
    /// <summary>Scene-level developer commands that need a MonoBehaviour (coroutines, frame timing).</summary>
    public class DevTools : MonoBehaviour
    {
        private void OnEnable()
        {
            DevCommands.Register("screenshot", "[delay seconds]", "Save a PNG to the Screenshots folder.", args =>
            {
                float delay = args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 0f;
                StartCoroutine(Capture(delay));
            }, owner: this);
            DevCommands.Register("viewshot", "<name>", "Render the live player camera to a named PNG (also works in background tests).", args =>
                StartCoroutine(CaptureView(args.Length > 0 ? args[0] : "view")), owner: this);
            DevCommands.Register("skin", "<index>", "Preview a finish on the held item without saving a preference.", args =>
            {
                if (args.Length > 0 && int.TryParse(args[0], out int index))
                    Items.ItemSkin.LocalHeld?.PreviewFinish(index);
            }, owner: this);
            DevCommands.Register("frameshot", "<name>", "Capture the final displayed frame, including post-processing and HUD.", args =>
                StartCoroutine(CaptureFrame(args.Length > 0 ? args[0] : "frame")), owner: this);
        }

        private void OnDisable()
        {
            DevCommands.Unregister("screenshot", this);
            DevCommands.Unregister("viewshot", this);
            DevCommands.Unregister("skin", this);
            DevCommands.Unregister("frameshot", this);
        }

        private static IEnumerator CaptureFrame(string name)
        {
            yield return new WaitForEndOfFrame();
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "GunGrips"));
            Directory.CreateDirectory(folder);
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null) { Debug.LogError("[Dev] Final frame capture failed."); yield break; }
            try
            {
                string path = Path.Combine(folder, name + ".png");
                File.WriteAllBytes(path, frame.EncodeToPNG());
                Debug.Log("[Dev] Displayed frame saved: " + path);
            }
            finally { Destroy(frame); }
        }

        private static IEnumerator CaptureView(string name)
        {
            // Let the held item and fingers settle through their LateUpdates before sampling the live scene.
            yield return null;
            Camera camera = Camera.main;
            if (camera == null) { Debug.LogWarning("[Dev] No player camera to capture."); yield break; }
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "GunGrips"));
            Directory.CreateDirectory(folder);
            // Match the player's HDR pipeline so bloom/exposure problems cannot be hidden by an LDR target.
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGBHalf);
            RenderTexture previous = RenderTexture.active, target = camera.targetTexture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0,0,1280,720),0,0);
                image.Apply();
                string path = Path.Combine(folder, name + ".png");
                File.WriteAllBytes(path, image.EncodeToPNG());
                Debug.Log("[Dev] Live camera saved: " + path);
            }
            finally
            {
                camera.targetTexture = target;
                RenderTexture.active = previous;
                Destroy(image); rt.Release(); Destroy(rt);
            }
        }

        private static IEnumerator Capture(float delay)
        {
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            // Next to the project in the editor, next to the exe in builds.
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"shot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            ScreenCapture.CaptureScreenshot(path);
            yield return null;
            Debug.Log($"[Dev] Screenshot saved: {path}");
        }
    }
}
