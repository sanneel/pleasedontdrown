using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Moves the saved scene's golf cart steering wheel and its hand grips back within the lifeguard's reach
    /// (GameSceneBuilder.CartWheelZ), without rebuilding the whole scene.
    /// <c>Unity.exe -batchmode -projectPath . -executeMethod PleaseDontDrown.Editor.PatchGolfCartReach.Run -quit</c>
    /// </summary>
    public static class PatchGolfCartReach
    {
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/Game.unity");
            int moved = 0;
            foreach (Vehicles.Vehicle v in Object.FindObjectsByType<Vehicles.Vehicle>(FindObjectsSortMode.None))
            {
                if (v.name != "GolfCart") continue;
                Transform r = v.transform;
                foreach (Transform t in r.GetComponentsInChildren<Transform>(true))
                {
                    if (t.parent == null) continue;
                    Vector3 p = t.localPosition;
                    switch (t.name)
                    {
                        case "SteeringWheel": t.localPosition = new Vector3(p.x, 1.22f, GameSceneBuilder.CartWheelZ); moved++; break;
                        case "GripLeft" when t.parent == r || t.parent.name != "PassengerSeat":
                            t.localPosition = new Vector3(-0.4f, 1.24f, GameSceneBuilder.CartWheelZ - 0.03f); moved++; break;
                        case "GripRight" when t.parent == r || t.parent.name != "PassengerSeat":
                            t.localPosition = new Vector3(-0.16f, 1.24f, GameSceneBuilder.CartWheelZ - 0.03f); moved++; break;
                    }
                }
            }
            Debug.Log($"[CartPatch] moved {moved} transforms");
            if (moved > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
    }
}
