using System;
using System.Collections;
using System.IO;
using UnityEngine;

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
        }

        private void OnDisable() => DevCommands.Unregister("screenshot", this);

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
