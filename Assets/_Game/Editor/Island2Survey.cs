using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Read-only map of island 2 (the hotel island) for planning scene work: what stands where (world bounds of the
    /// hotel's parts, the story points, vehicles, characters) and a top-down picture with a 10 m grid.
    /// Never saves the scene.
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod PleaseDontDrown.Editor.Island2Survey.Run -quit</c>
    /// Writes <c>Screenshots/Survey/island2.txt</c> and <c>island2_top.png</c>.
    /// </summary>
    public static class Island2Survey
    {
        // The picture's area (world x/z) and its size.
        private const float X0 = -115f, X1 = 115f, Z0 = -320f, Z1 = -150f;
        private const int Width = 2400;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            string folder = Path.GetFullPath("Screenshots/Survey");
            Directory.CreateDirectory(folder);
            var text = new StringBuilder();
            text.AppendLine($"picture: x {X0}..{X1} (left to right), z {Z1}..{Z0} (top to bottom), {Width} px wide");
            foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                Walk(root.transform, 0, text);
            File.WriteAllText(Path.Combine(folder, "island2.txt"), text.ToString());
            TopDown(Path.Combine(folder, "island2_top.png"));
            Debug.Log("[Survey] written to " + folder);
        }

        private static bool OnIsland2(Vector3 p) => p.x > X0 - 40f && p.x < X1 + 40f && p.z > Z0 - 60f && p.z < Z1 + 20f;

        private static void Walk(Transform t, int depth, StringBuilder text)
        {
            if (depth > 4) return;
            Bounds? b = BoundsOf(t);
            Vector3 at = b?.center ?? t.position;
            if (!OnIsland2(at) && depth > 0)
            {
                // Containers far away (island 1, 3) are skipped, but their children may still be here.
                if (t.childCount == 0) return;
                bool any = false;
                foreach (Transform c in t) if (OnIsland2(c.position)) any = true;
                if (!any) return;
            }
            string kind = t.GetComponents<MonoBehaviour>().Where(m => m != null && m.GetType().Namespace != null && m.GetType().Namespace.StartsWith("PleaseDontDrown"))
                .Select(m => m.GetType().Name).FirstOrDefault();
            text.Append(new string(' ', depth * 2)).Append(t.name);
            text.Append($"  pos {t.position.x:F1} {t.position.y:F1} {t.position.z:F1} yaw {t.eulerAngles.y:F0}");
            if (b.HasValue) text.Append($"  bounds c {b.Value.center.x:F1} {b.Value.center.y:F1} {b.Value.center.z:F1} size {b.Value.size.x:F1} {b.Value.size.y:F1} {b.Value.size.z:F1}");
            if (kind != null) text.Append("  [").Append(kind).Append(']');
            if (t.childCount > 0) text.Append($"  ({t.childCount} children)");
            text.AppendLine();
            // Big containers of small pieces (combined meshes, props) are listed but not opened past a point.
            if (t.childCount > 60 && depth >= 2) return;
            foreach (Transform c in t) Walk(c, depth + 1, text);
        }

        private static Bounds? BoundsOf(Transform t)
        {
            Bounds? b = null;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                if (b == null) b = r.bounds;
                else { Bounds x = b.Value; x.Encapsulate(r.bounds); b = x; }
            }
            return b;
        }

        private static void TopDown(string path)
        {
            int height = Mathf.RoundToInt(Width * (Z1 - Z0) / (X1 - X0));
            var go = new GameObject("SurveyCamera");
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = (Z1 - Z0) * 0.5f;
            cam.aspect = (float)Width / height;
            cam.transform.SetPositionAndRotation(new Vector3((X0 + X1) * 0.5f, 120f, (Z0 + Z1) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.3f, 0.45f);
            var rt = new RenderTexture(Width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(Width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, height), 0, 0);
            // A 10 m grid (every 50 m brighter) to read positions off the picture.
            for (float x = Mathf.Ceil(X0 / 10f) * 10f; x <= X1; x += 10f)
            {
                int px = Mathf.RoundToInt((x - X0) / (X1 - X0) * Width);
                bool major = Mathf.Abs(x % 50f) < 0.01f;
                for (int y = 0; y < height; y++) Tint(image, px, y, major);
            }
            for (float z = Mathf.Ceil(Z0 / 10f) * 10f; z <= Z1; z += 10f)
            {
                int py = Mathf.RoundToInt((z - Z0) / (Z1 - Z0) * height);
                bool major = Mathf.Abs(z % 50f) < 0.01f;
                for (int x = 0; x < Width; x++) Tint(image, x, py, major);
            }
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            RenderTexture.active = null;
            Object.DestroyImmediate(go);
        }

        private static void Tint(Texture2D image, int x, int y, bool major)
        {
            if (x < 0 || y < 0 || x >= image.width || y >= image.height) return;
            Color c = image.GetPixel(x, y);
            image.SetPixel(x, y, Color.Lerp(c, major ? Color.red : Color.white, major ? 0.7f : 0.35f));
        }
    }
}
