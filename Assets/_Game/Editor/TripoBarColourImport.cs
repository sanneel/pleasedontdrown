using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    public static partial class GameSceneBuilder
    {
        // A ready-to-place model prefab; does not overwrite the user's live scene.
        [MenuItem("PLEASE DON'T DROWN/Import coloured Tripo beach bar")]
        public static void ImportColouredTripoBar()
        {
            const string path = "Assets/_Game/Art/Props/TripoBeachBar.prefab";
            var root = new GameObject("TripoBeachBar");
            try
            {
                var levels = new LOD[3]; float[] thresholds = { .3f, .10f, .015f };
                for (int i = 0; i < 3; i++)
                {
                    var model = PropModel("resort_tripo_beach_bar_lod" + i, root.transform);
                    if (model == null) throw new InvalidOperationException("Coloured Tripo bar LOD not imported: " + i);
                    var renderers = model.GetComponentsInChildren<Renderer>();
                    if (renderers.Length == 0) throw new InvalidOperationException("Bar renderer missing");
                    foreach (var renderer in renderers)
                        foreach (var material in renderer.sharedMaterials)
                            if (material == null || material.shader == null) throw new InvalidOperationException("Bar material missing");
                    levels[i] = new LOD(thresholds[i], renderers);
                }
                var group = root.AddComponent<LODGroup>(); group.SetLODs(levels); group.RecalculateBounds();
                PrefabUtility.SaveAsPrefabAsset(root, path); AssetDatabase.SaveAssets();
                File.WriteAllText("Logs/tripo-bar-colour-import-result.txt", "PASS: coloured URP materials and three LODs saved to " + path);
                Debug.Log("[TripoBarColour] Coloured, optimized beach bar prefab saved. Existing scene preserved.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }

    [InitializeOnLoad]
    internal static class TripoBarColourRequest
    {
        static TripoBarColourRequest() { EditorApplication.update += Tick; }
        private static void Tick()
        {
            const string request = "Logs/tripo-bar-colour-import-request.txt";
            if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            File.Delete(request);
            try { GameSceneBuilder.ImportColouredTripoBar(); }
            catch (Exception e) { Debug.LogException(e); File.WriteAllText("Logs/tripo-bar-colour-import-result.txt", "FAIL " + e); }
        }
    }
}
