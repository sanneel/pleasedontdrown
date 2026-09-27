using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// The scene's overview camera, shown in menus and while no local player exists.
    /// </summary>
    public class SceneCameras : MonoBehaviour
    {
        [SerializeField] private Camera _menuCamera;

        private static SceneCameras _instance;

        private void Awake() => _instance = this;

        public static void SetMenuCameraActive(bool active)
        {
            if (_instance != null && _instance._menuCamera != null)
                _instance._menuCamera.gameObject.SetActive(active);
        }
    }
}
