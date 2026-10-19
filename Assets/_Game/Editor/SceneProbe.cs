using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Where things stand in the saved Game scene, for fixing a place without opening the editor:
    /// -executeMethod PleaseDontDrown.Editor.SceneProbe.FindBatch -names Barista,Receptionist [-under HotelBeachBar]
    /// logs "[Probe] path  world position  yaw  bounds" for every object whose name contains one of the parts.
    /// </summary>
    public static class SceneProbe
    {
        public static void FindBatch()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string Arg(string key)
                {
                    int i = Array.IndexOf(args, key);
                    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
                }
                string[] names = (Arg("-names") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
                string under = Arg("-under");
                EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
                int found = 0;
                foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (!names.Any(n => t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                    string path = Path(t);
                    if (under != null && path.IndexOf(under, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Bounds? b = null;
                    foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                        if (b == null) b = r.bounds; else { Bounds x = b.Value; x.Encapsulate(r.bounds); b = x; }
                    Debug.Log($"[Probe] {path}  at {t.position:F2}  yaw {t.eulerAngles.y:F0}" +
                              (b != null ? $"  bounds {b.Value.min:F1}..{b.Value.max:F1}" : "") + (t.gameObject.activeInHierarchy ? "" : "  (inactive)"));
                    if (++found >= 400) break;
                }
                Debug.Log($"[Probe] {found} found");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Probe] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
    }
}
