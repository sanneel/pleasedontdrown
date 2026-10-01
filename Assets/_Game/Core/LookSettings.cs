using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// The player's own view settings (options menu, console): mouse sensitivity and field of view. Kept in
    /// PlayerPrefs; they exist before any player does, so the start menu can change them too.
    /// </summary>
    public static class LookSettings
    {
        private const string SensitivityKey = "pdd.look.sensitivity";
        private const string FovKey = "pdd.look.fov";

        public const float DefaultSensitivity = 0.1f, MinSensitivity = 0.02f, MaxSensitivity = 0.5f;
        public const float DefaultFov = 74f, MinFov = 60f, MaxFov = 110f;

        private static bool _loaded;
        private static float _sensitivity, _fov;

        /// <summary>Degrees the view turns per pixel of mouse movement.</summary>
        public static float Sensitivity
        {
            get { Load(); return _sensitivity; }
            set
            {
                Load();
                _sensitivity = Mathf.Clamp(value, 0.01f, 1f);
                PlayerPrefs.SetFloat(SensitivityKey, _sensitivity);
            }
        }

        /// <summary>Vertical field of view, degrees (not aiming).</summary>
        public static float Fov
        {
            get { Load(); return _fov; }
            set
            {
                Load();
                _fov = Mathf.Clamp(value, MinFov, MaxFov);
                PlayerPrefs.SetFloat(FovKey, _fov);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _loaded = false;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _sensitivity = PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity);
            _fov = PlayerPrefs.GetFloat(FovKey, DefaultFov);
        }
    }
}
