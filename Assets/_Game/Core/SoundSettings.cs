using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// The player's own sound setting (options menu): one master volume over everything this copy of the game plays.
    /// Kept in PlayerPrefs and put on the listener before the first scene loads, so the menu already plays at it.
    /// </summary>
    public static class SoundSettings
    {
        private const string VolumeKey = "pdd.sound.volume";

        public const float DefaultVolume = 1f;

        private static bool _loaded;
        private static float _volume;

        /// <summary>0 (silent) .. 1 (full).</summary>
        public static float Volume
        {
            get { Load(); return _volume; }
            set
            {
                Load();
                _volume = Mathf.Clamp01(value);
                AudioListener.volume = _volume;
                PlayerPrefs.SetFloat(VolumeKey, _volume);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _loaded = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply() => AudioListener.volume = Volume;

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));
        }
    }
}
