using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>Part B (islands 1 and 2) helpers: dump the Game scene's hierarchy to a text file so the scene work can
    /// be planned without opening the editor.</summary>
    public static class TeamBSceneTools
    {
        private const string ScenePath = "Assets/_Game/Scenes/Game.unity";

        [MenuItem("PLEASE DON'T DROWN/Team B/Dump scene hierarchy")]
        public static void DumpHierarchy()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sb = new StringBuilder();
            foreach (GameObject root in scene.GetRootGameObjects())
                Dump(root.transform, "", sb);
            string outPath = System.Environment.GetEnvironmentVariable("PDD_DUMP") ?? "Temp/scene-dump.txt";
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log($"[TeamB] dumped hierarchy to {outPath}");
        }

        /// <summary>Renders views of the Game scene. Env PDD_SHOTS = file with lines "name px py pz lx ly lz [fov]"
        /// (camera position, look-at point); PNGs go to PDD_SHOTDIR (default Screenshots/TeamB).</summary>
        public static void CaptureShots()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            string outDir = System.Environment.GetEnvironmentVariable("PDD_SHOTDIR") ?? "Screenshots/TeamB";
            Directory.CreateDirectory(outDir);
            foreach (Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                c.enabled = false;
            RenderSettings.fog = false;
            var cam = new GameObject("TeamBShotCamera").AddComponent<Camera>();
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .75f, .95f);
            foreach (string raw in File.ReadAllLines(System.Environment.GetEnvironmentVariable("PDD_SHOTS")))
            {
                string[] f = raw.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 7 || f[0].StartsWith("#")) continue;
                float F(int i) => float.Parse(f[i], System.Globalization.CultureInfo.InvariantCulture);
                cam.transform.position = new Vector3(F(1), F(2), F(3));
                cam.transform.LookAt(new Vector3(F(4), F(5), F(6)));
                cam.fieldOfView = f.Length > 7 ? F(7) : 60f;
                const int w = 1280, h = 800;
                var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                cam.targetTexture = rt; cam.Render(); cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
                RenderTexture.active = null; cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(outDir, f[0] + ".jpg"), tex.EncodeToJPG(85));
                Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            Debug.Log($"[TeamB] shots written to {outDir}");
        }

        /// <summary>Renderer bounds of every object down to depth PDD_DEPTH (default 3) whose bounds touch the XZ box
        /// PDD_BOX ("minx maxx minz maxz"), to PDD_DUMP.</summary>
        public static void BoundsReport()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            float[] box = System.Environment.GetEnvironmentVariable("PDD_BOX").Split(' ')
                .Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            int maxDepth = int.TryParse(System.Environment.GetEnvironmentVariable("PDD_DEPTH"), out int d) ? d : 3;
            var sb = new StringBuilder();
            void Walk(Transform t, string path, int depth)
            {
                path = path.Length == 0 ? t.name : path + "/" + t.name;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0)
                {
                    Bounds b = rs[0].bounds;
                    foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                    if (b.max.x >= box[0] && b.min.x <= box[1] && b.max.z >= box[2] && b.min.z <= box[3])
                        sb.Append($"{path} | x {b.min.x:0} .. {b.max.x:0}  z {b.min.z:0} .. {b.max.z:0}  y {b.min.y:0.0} .. {b.max.y:0.0}\n");
                    else return;
                }
                if (depth < maxDepth) foreach (Transform c in t) Walk(c, path, depth + 1);
            }
            foreach (GameObject root in scene.GetRootGameObjects()) Walk(root.transform, "", 1);
            File.WriteAllText(System.Environment.GetEnvironmentVariable("PDD_DUMP") ?? "Temp/bounds.txt", sb.ToString());
            Debug.Log("[TeamB] bounds report written");
        }

        private static void Dump(Transform t, string parent, StringBuilder sb)
        {
            string path = parent.Length == 0 ? t.name : parent + "/" + t.name;
            Vector3 p = t.position;
            string comps = string.Join(",", t.GetComponents<Component>()
                .Where(c => c != null && c is not Transform)
                .Select(c => c.GetType().Name));
            string prefab = PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
                ? " prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) : "";
            sb.Append(path).Append(" | ").Append(t.gameObject.activeSelf ? "" : "OFF ")
              .Append($"({p.x:0.0},{p.y:0.0},{p.z:0.0}) s{t.lossyScale.x:0.##} ")
              .Append(comps).Append(prefab).Append('\n');
            foreach (Transform c in t) Dump(c, path, sb);
        }
    }
}
