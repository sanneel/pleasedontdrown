using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// The player's own sound settings (options menu): a master volume over everything this copy of the game plays,
    /// and on top of it how loud the music and the voices are. Kept in PlayerPrefs; the master is put on the listener
    /// before the first scene loads, so the menu already plays at it. Music and voices read theirs as they play.
    /// </summary>
    public static class SoundSettings
    {
        private const string VolumeKey = "pdd.sound.volume", MusicKey = "pdd.sound.music", VoiceKey = "pdd.sound.voice";

        public const float DefaultVolume = 1f, DefaultMusic = 0.6f, DefaultVoice = 0.9f;

        private static bool _loaded;
        private static float _volume, _music, _voice;

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

        /// <summary>The music, 0 (off) .. 1.</summary>
        public static float Music
        {
            get { Load(); return _music; }
            set
            {
                Load();
                _music = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MusicKey, _music);
            }
        }

        /// <summary>People talking, 0 (subtitles only) .. 1.</summary>
        public static float Voice
        {
            get { Load(); return _voice; }
            set
            {
                Load();
                _voice = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(VoiceKey, _voice);
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
            _music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusic));
            _voice = Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceKey, DefaultVoice));
        }
    }
}
