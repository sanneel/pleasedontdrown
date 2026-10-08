using System.IO;
using System.Collections.Generic;
using PleaseDontDrown.UI;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>A quick, scene-independent visual check of short and wrapped speech bubbles.</summary>
    public static class SpeechBubbleReview
    {
        [MenuItem("Tools/PDD/Capture speech bubbles")]
        public static void Capture()
        {
            const int width = 1280, height = 720;
            var cameraObject = new GameObject("Speech Review Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 2.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.42f, 0.59f);
            camera.transform.position = new Vector3(0f, 0f, -8f);
            camera.transform.rotation = Quaternion.identity;
            var existing = new HashSet<int>();
            foreach (FloatingText text in Object.FindObjectsByType<FloatingText>(FindObjectsSortMode.None))
                existing.Add(text.GetInstanceID());
            RenderTexture target = null;
            Texture2D picture = null;
            RenderTexture old = RenderTexture.active;
            try
            {
                // Spawn uses the same bubble path as live NPC dialogue, without requiring a network scene.
                FloatingText.Spawn(new Vector3(0f, 1f, 0f), "Help! Over here!", Color.white, 1f, 20f, true);
                FloatingText.Spawn(new Vector3(0f, -0.7f, 0f), "Four keys. Four skis. You recovered them, and you brought my customers home. That's a proper rescue crew.", Color.white, 1f, 20f, true);
                target = new RenderTexture(width, height, 24);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                picture = new Texture2D(width, height, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                picture.Apply();
                string path = Path.Combine(Application.dataPath, "../Screenshots/Review/SpeechBubbles.png");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, picture.EncodeToPNG());
                Debug.Log("[SpeechBubbleReview] " + Path.GetFullPath(path));
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = old;
                if (target != null) Object.DestroyImmediate(target);
                if (picture != null) Object.DestroyImmediate(picture);
                foreach (FloatingText bubble in Object.FindObjectsByType<FloatingText>(FindObjectsSortMode.None))
                    if (!existing.Contains(bubble.GetInstanceID())) Object.DestroyImmediate(bubble.gameObject);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
