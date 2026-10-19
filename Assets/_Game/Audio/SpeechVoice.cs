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
        // The line being made on a worker thread (played as soon as it's ready, a frame or two later).
        private System.Threading.Tasks.Task<float[]> _making;

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
            // Nobody near enough to hear it (beach chatter across the island): don't make the sound at all.
            if (_audio.spatialBlend > 0.5f && Listener() is { } ear &&
                (ear.position - transform.position).sqrMagnitude > Sq(_audio.maxDistance + 2f)) return;
            _gain = Mathf.Clamp01(gain);
            _making = System.Threading.Tasks.Task.Run(() => SoundClip.Master("Dialogue", SpeechSynth.LineSamples(text, register, pitch)));
            enabled = true;
        }

        /// <summary>Main thread: the made line becomes a clip and plays.</summary>
        private void PlayMade()
        {
            System.Threading.Tasks.Task<float[]> made = _making;
            _making = null;
            if (made.IsFaulted || made.IsCanceled)
            {
                Debug.LogWarning($"[Speech] line failed: {made.Exception?.GetBaseException().Message}");
                Stop();
                return;
            }
            _line = SoundClip.FromMastered("Dialogue", made.Result);
            MeasureLoudness(made.Result, _line.frequency);
            _audio.clip = _line;
            _audio.pitch = 1f;
            _audio.volume = SoundSettings.Voice * _gain;
            _audio.Play();
        }

        // How loud the line is, every 10 ms (0..1 of its loud parts): the speaker's mouth opens with it (AvatarAnimator).
        private float[] _loudness;
        private int _loudnessHop = 1;

        /// <summary>How loud the line being said is right now, 0..1 (-1: nothing being said out loud here).</summary>
        public float Loudness
        {
            get
            {
                if (_loudness == null || _audio == null || !_audio.isPlaying) return -1f;
                int at = _audio.timeSamples / _loudnessHop;
                return at < _loudness.Length ? _loudness[at] : 0f;
            }
        }

        private void MeasureLoudness(float[] samples, int frequency)
        {
            _loudnessHop = Mathf.Max(1, frequency / 100);
            int count = samples.Length / _loudnessHop + 1;
            _loudness = new float[count];
            for (int i = 0; i < count; i++)
            {
                double sum = 0;
                int from = i * _loudnessHop, to = Mathf.Min(samples.Length, from + _loudnessHop);
                for (int k = from; k < to; k++) sum += samples[k] * samples[k];
                _loudness[i] = to > from ? Mathf.Sqrt((float)(sum / (to - from))) : 0f;
            }
            float[] sorted = (float[])_loudness.Clone();
            System.Array.Sort(sorted);
            float loud = Mathf.Max(1e-4f, sorted[Mathf.Clamp((int)(sorted.Length * 0.9f), 0, sorted.Length - 1)]);
            for (int i = 0; i < count; i++) _loudness[i] = Mathf.Clamp01(_loudness[i] / loud);
        }

        private static AudioListener _listener;

        private static Transform Listener()
        {
            if (_listener == null || !_listener.isActiveAndEnabled) _listener = FindAnyObjectByType<AudioListener>();
            return _listener != null ? _listener.transform : null;
        }

        private static float Sq(float x) => x * x;

        public void Stop()
        {
            Release();
            enabled = false;
        }

        private void Release()
        {
            _making = null; // (a line still being made is dropped when it's done)
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
            if (_making != null)
            {
                if (_making.IsCompleted) PlayMade();
                return;
            }
            _audio.volume = SoundSettings.Voice * _gain;
            if (!_audio.isPlaying && !AudioListener.pause) Stop();
        }
    }
}
