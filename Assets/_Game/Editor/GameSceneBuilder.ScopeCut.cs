using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PleaseDontDrown.Items;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The scope cut (Docs/23-Scope-Change.md), done to the saved scene and the item prefabs by script, so it can run
    /// again after anyone's Game.unity is merged in:
    ///  * island 4 (Finders Keepers) goes, with its navmesh;
    ///  * the basketball hoop and its court, the octagon by the tavern, the rope swing over the river and the bars'
    ///    bartenders go;
    ///  * every component whose script was cut (shops, the Lost &amp; Found counter, bar seats and the blackjack table,
    ///    the tsunami, sewer ladders...) comes off the objects that carried it; the objects themselves stay as scenery;
    ///  * item prefabs lose cut components (the bar's table meals), and the item catalog forgets cut items.
    /// Batch: <c>-executeMethod PleaseDontDrown.Editor.GameSceneBuilder.ScopeCutBatch</c> (a copy of the scene before it
    /// goes to Logs/).
    /// </summary>
    public static partial class GameSceneBuilder
    {
        // Whole objects that go (scene paths, or bare names found anywhere).
        private static readonly string[] ScopeCutPaths = { "Environment/Island4" };
        private static readonly string[] ScopeCutNames = { "BasketballHoop", "Octagon", "RopeSwing", "Barista" };

        [MenuItem("PLEASE DON'T DROWN/Scope cut: clean the scene and items")]
        public static void ScopeCut()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            var log = new List<string>();

            // ---------------------------------------------------------------- prefabs
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int removed = 0;
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                if (removed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.Add($"{path}: {removed} cut components removed");
                }
                PrefabUtility.UnloadPrefabContents(root);
            }

            // The item catalog: the cut items' prefabs are gone (empty slots).
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ItemCatalogPath);
            if (catalog != null)
            {
                var so = new SerializedObject(catalog);
                SerializedProperty items = Require(so, "_items");
                int before = items.arraySize;
                for (int i = items.arraySize - 1; i >= 0; i--)
                    if (items.GetArrayElementAtIndex(i).objectReferenceValue == null)
                        items.DeleteArrayElementAtIndex(i);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(catalog);
                log.Add($"item catalog: {before} -> {items.arraySize}");
            }
            AssetDatabase.SaveAssets();
            RefreshFishNetPrefabs();

            // ---------------------------------------------------------------- the scene
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory("Logs");
            EditorSceneManager.SaveScene(scene, $"Logs/Game-before-scope-cut-{DateTime.Now:yyyyMMdd-HHmmss}.unity", true);

            foreach (string path in ScopeCutPaths)
                if (FindByPath(path) is { } t)
                {
                    log.Add($"removed {path}");
                    Object.DestroyImmediate(t.gameObject);
                }
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true).ToArray())
                {
                    if (t == null || Array.IndexOf(ScopeCutNames, t.name) < 0) continue;
                    // The basketball court is painted beside the hoop (a "Court" next to it, not the island 2 tennis court).
                    if (t.name == "BasketballHoop" && t.parent != null && t.parent.Find("Court") is { } court)
                    {
                        log.Add($"removed {ScenePathOf(court)}");
                        Object.DestroyImmediate(court.gameObject);
                    }
                    log.Add($"removed {ScenePathOf(t)}");
                    Object.DestroyImmediate(t.gameObject);
                }

            int stripped = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (n == 0) continue;
                    stripped += n;
                    log.Add($"cut components off {ScenePathOf(t)} ({n})");
                }

            // "Look at it and press E" with nothing behind it any more (the shops' counters, Lost & Found): no prompt.
            foreach (var press in Object.FindObjectsByType<Interaction.Interactable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (press.GetComponent<Interaction.IInteractionHandler>() == null)
                {
                    log.Add($"press-E removed from {ScenePathOf(press.transform)}");
                    Object.DestroyImmediate(press);
                }

            // Island 4's navmesh no longer loads.
            foreach (NavMeshLoader loader in Object.FindObjectsByType<NavMeshLoader>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(loader);
                SerializedProperty data = Require(so, "_data");
                for (int i = data.arraySize - 1; i >= 0; i--)
                {
                    Object nav = data.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (nav == null || nav.name.IndexOf("Island4", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        log.Add($"navmesh {(nav != null ? nav.name : "(missing)")} unloaded");
                        data.GetArrayElementAtIndex(i).objectReferenceValue = null;
                        data.DeleteArrayElementAtIndex(i);
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AssignSceneIds(scene, force: false);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            foreach (string line in log) Debug.Log("[ScopeCut] " + line);
            Debug.Log($"[ScopeCut] PASS: {stripped} cut components off scene objects, {log.Count} changes");
        }

        public static void ScopeCutBatch() => ScopeCut();

        /// <summary>Batch: the scope cut, then a dev build of the scene (DevPlayerBuild).</summary>
        public static void ScopeCutAndBuildBatch()
        {
            ScopeCut();
            DevPlayerBuild.Build();
        }
    }
}
