using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Read-only: every object under one scene path with its world position, size and parts (for planning scene work).
    /// <c>-executeMethod PleaseDontDrown.Editor.SceneDump.Run -pdd-path Environment/Island3/PirateCastle</c> writes
    /// <c>Screenshots/Survey/dump.txt</c>. Never saves the scene.
    /// </summary>
    public static class SceneDump
    {
        public static void Run()
        {
            string[] cl = System.Environment.GetCommandLineArgs();
            int a = System.Array.IndexOf(cl, "-pdd-path");
            string path = a >= 0 && a + 1 < cl.Length ? cl[a + 1] : "Environment";
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            GameObject root = GameObject.Find(path);
            var text = new StringBuilder();
            if (root == null) text.AppendLine("not found: " + path);
            else Walk(root.transform, 0, text);
            // -pdd-rays "x,z;x,z": everything a ray straight down from 60 m hits at each point (name and height).
            int r = System.Array.IndexOf(cl, "-pdd-rays");
            if (r >= 0 && r + 1 < cl.Length)
            {
                Physics.SyncTransforms();
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                foreach (string p in cl[r + 1].Split(';'))
                {
                    string[] xz = p.Split(',');
                    float x = float.Parse(xz[0], inv), z = float.Parse(xz[1], inv);
                    text.Append($"ray {x} {z}:");
                    var hits = Physics.RaycastAll(new Vector3(x, 60f, z), Vector3.down, 90f, ~0, QueryTriggerInteraction.Collide);
                    System.Array.Sort(hits, (h1, h2) => h2.point.y.CompareTo(h1.point.y));
                    foreach (RaycastHit h in hits)
                        text.Append($" {h.collider.name}{(h.collider.isTrigger ? "(trigger)" : "")}@{h.point.y:F1}");
                    text.AppendLine();
                }
            }
            Directory.CreateDirectory("Screenshots/Survey");
            File.WriteAllText("Screenshots/Survey/dump.txt", text.ToString());
            Debug.Log("[Dump] " + path);
        }

        private static void Walk(Transform t, int depth, StringBuilder text)
        {
            text.Append(new string(' ', depth * 2)).Append(t.name).Append($"  pos {t.position.x:F2} {t.position.y:F2} {t.position.z:F2} yaw {t.eulerAngles.y:F0}");
            Bounds? b = null;
            foreach (Renderer r in t.GetComponents<Renderer>()) b = r.bounds;
            if (b == null && t.TryGetComponent(out Collider c)) b = c.bounds;
            if (b.HasValue) text.Append($"  box c {b.Value.center.x:F2} {b.Value.center.y:F2} {b.Value.center.z:F2} size {b.Value.size.x:F2} {b.Value.size.y:F2} {b.Value.size.z:F2}");
            if (!t.gameObject.activeSelf) text.Append("  (off)");
            text.AppendLine();
            if (depth < 4) foreach (Transform k in t) Walk(k, depth + 1, text);
        }
    }
}
