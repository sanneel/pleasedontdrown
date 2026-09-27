using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// Camera-side water effects: distance haze above water, thick blue fog + tint + muffled audio below.
    /// Uses linear fog in both cases so only one fog shader variant needs to exist in builds.
    /// </summary>
    public class UnderwaterFx : MonoBehaviour
    {
        [SerializeField] private Color _aboveFog = new Color(0.72f, 0.85f, 0.95f);
        [SerializeField] private float _aboveStart = 70f;
        [SerializeField] private float _aboveEnd = 520f;
        [SerializeField] private Color _belowFog = new Color(0.05f, 0.3f, 0.4f);
        [SerializeField] private float _belowEnd = 24f;
        [SerializeField] private Color _tint = new Color(0.05f, 0.35f, 0.45f, 0.25f);

        private Camera _camera;
        private Texture2D _pixel;

        public static bool CameraUnderwater { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => CameraUnderwater = false;

        private void Start() => Apply(false);

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null || !WaterSurface.Exists) return;

            bool under = WaterSurface.DepthOf(cam.transform.position) > 0.02f;
            if (under == CameraUnderwater && cam == _camera) return;
            _camera = cam;
            Apply(under);
        }

        private void Apply(bool under)
        {
            CameraUnderwater = under;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = under ? _belowFog : _aboveFog;
            RenderSettings.fogStartDistance = under ? 0f : _aboveStart;
            RenderSettings.fogEndDistance = under ? _belowEnd : _aboveEnd;
            if (_camera == null) return;
            if (_camera.TryGetComponent(out AudioLowPassFilter muffle))
                muffle.enabled = under;
            // The sky isn't fogged, so underwater it would glow through the surface: clear to the fog color instead.
            _camera.clearFlags = under ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            _camera.backgroundColor = _belowFog;
        }

        private void OnGUI()
        {
            if (!CameraUnderwater) return;
            if (_pixel == null)
            {
                _pixel = new Texture2D(1, 1);
                _pixel.SetPixel(0, 0, Color.white);
                _pixel.Apply();
            }
            GUI.depth = 10; // behind the HUD
            GUI.color = _tint;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _pixel);
            GUI.color = Color.white;
        }
    }
}
