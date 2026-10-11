using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The casino's Tripo slot machines (Assets/_Game/Art/Tripo/Island2/slot_machine.glb, a chunky red and gold one-armed
    /// bandit) on the casino's black stands, in place of the box cabinets: BuildCasino places them at the next hotel
    /// rebuild (the saved scene still has the box ones, merged into the lobby's combined mesh).
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string TripoIsland2Dir = "Assets/_Game/Art/Tripo/Island2";

        private static GameObject TripoIsland2(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{TripoIsland2Dir}/{name}.glb");

        /// <summary>The model on a stand whose top middle is <paramref name="top"/>, facing <paramref name="yaw"/> (null without the model).</summary>
        private static GameObject TripoSlot(Transform parent, Vector3 top, float yaw)
        {
            GameObject prefab = TripoIsland2("slot_machine");
            if (prefab == null) return null;
            GameObject slot = Object.Instantiate(prefab, parent, false);
            slot.name = "Tripo_slot_machine";
            slot.transform.localPosition = top;
            slot.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            foreach (Renderer r in slot.GetComponentsInChildren<Renderer>()) r.allowOcclusionWhenDynamic = false;
            return slot;
        }

        /// <summary>Batch (one-off): takes the Tripo slots back out of the saved scene. The casino's box machines are merged
        /// into the lobby's combined mesh, so they can only go with a hotel rebuild, which then places the Tripo ones.</summary>
        public static void RemoveCasinoTripoSlotsBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            int removed = 0;
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.name == "Tripo_slot_machine") { Object.DestroyImmediate(t.gameObject); removed++; }
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Casino] removed {removed} Tripo slots from the saved scene");
        }
    }
}
