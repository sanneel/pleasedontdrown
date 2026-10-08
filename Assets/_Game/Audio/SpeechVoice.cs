using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>Spatial cartoon dialogue with stable syllable pitch, phrase pauses and live voice volume.</summary>
    public sealed class SpeechVoice : MonoBehaviour
    {
        private static SpeechVoice _local;
        private AudioSource _audio;
        private AudioClip _line;
        private float _gain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _local = null;

        public static SpeechVoice Local
        {
            get
            {
                if (_local != null) return _local;
                var go = new GameObject("Narrator Voice");
                DontDestroyOnLoad(go);
                _local = go.AddComponent<SpeechVoice>();
                _local.Setup(null);
                return _local;
            }
        }

        public static SpeechVoice On(GameObject speaker, AudioSource like)
        {
            if (speaker.TryGetComponent(out SpeechVoice voice)) return voice;
            voice = speaker.AddComponent<SpeechVoice>();
            voice.Setup(like);
            return voice;
        }

        private void Setup(AudioSource like)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = like != null ? 1f : 0f;
            _audio.dopplerLevel = 0f;
            _audio.priority = 80;
            if (like != null)
            {
                _audio.rolloffMode = like.rolloffMode;
                _audio.minDistance = like.minDistance;
                _audio.maxDistance = Mathf.Max(_audio.minDistance, Mathf.Min(like.maxDistance, 28f));
            }
            enabled = false;
        }

        public void Speak(string text, int register, float pitch = 1f, float gain = 1f)
        {
            Stop();
            if (string.IsNullOrWhiteSpace(text) || SoundSettings.Voice <= 0.001f || Application.isBatchMode) return;
            if (_audio == null) Setup(null);
            _gain = Mathf.Clamp01(gain);
            _line = SpeechSynth.Line(text, register, pitch);
            _audio.clip = _line;
            _audio.pitch = 1f;
            _audio.volume = SoundSettings.Voice * _gain;
            _audio.Play();
            enabled = true;
        }

        public void Stop()
        {
            Release();
            enabled = false;
        }

        private void Release()
        {
            if (_audio != null)
            {
                _audio.Stop();
                _audio.clip = null;
            }
            if (_line != null) Destroy(_line);
            _line = null;
        }

        private void OnDisable() => Release();
        private void OnDestroy() => Release();

        private void Update()
        {
            _audio.volume = SoundSettings.Voice * _gain;
            if (!_audio.isPlaying && !AudioListener.pause) Stop();
        }
    }
}
