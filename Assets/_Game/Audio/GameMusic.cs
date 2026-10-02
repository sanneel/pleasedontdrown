using System.Threading.Tasks;
using PleaseDontDrown.Core;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.Audio
{
    public enum Jingle { Saved, Lost, Chapter }

    /// <summary>
    /// Plays the music on this copy of the game: the calm beach loop (in the menu too), a drum layer that comes in on
    /// top of it while a tourist is in trouble, and short jingles (someone saved, someone lost, a chapter done).
    /// Starts itself; the loops are rendered on worker threads (<see cref="MusicSynth"/>) and begin when ready.
    /// How loud: <see cref="SoundSettings.Music"/>.
    /// </summary>
    public sealed class GameMusic : MonoBehaviour
    {
        private const float CalmLevel = 0.5f, DangerLevel = 0.6f, JingleLevel = 0.8f;

        private static GameMusic _instance;

        private AudioSource _calm, _danger, _jingles;
        private Task<float[]> _calmRender, _dangerRender;
        private readonly AudioClip[] _jingleClips = new AudioClip[3];
        private bool _playing;
        private float _dangerMix, _nextCheck, _duck;
        private bool _trouble;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            if (Application.isBatchMode || _instance != null) return; // headless tests have nobody listening
            var go = new GameObject("Music");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameMusic>();
        }

        /// <summary>A short jingle over the music (does nothing in a headless run).</summary>
        public static void Play(Jingle jingle)
        {
            if (_instance != null) _instance.PlayJingle(jingle);
        }

        private void Awake()
        {
            _calm = Source(true);
            _danger = Source(true);
            _jingles = Source(false);
            _calmRender = Task.Run(MusicSynth.RenderCalm);
            _dangerRender = Task.Run(MusicSynth.RenderDanger);
        }

        private AudioSource Source(bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.priority = 0; // never the voice that's dropped when many sounds play
            return source;
        }

        private void Update()
        {
            if (!_playing)
            {
                if (!_calmRender.IsCompleted || !_dangerRender.IsCompleted) return;
                if (_calmRender.IsFaulted || _dangerRender.IsFaulted)
                {
                    Debug.LogError($"[Music] could not render the loops: {(_calmRender.Exception ?? _dangerRender.Exception)?.GetBaseException().Message}");
                    enabled = false;
                    return;
                }
                _calm.clip = Stereo("MusicCalm", _calmRender.Result);
                _danger.clip = Stereo("MusicDanger", _dangerRender.Result);
                _calmRender = _dangerRender = null;
                // Both from the same moment on the audio clock: the layers stay bar for bar.
                double at = AudioSettings.dspTime + 0.2;
                _calm.PlayScheduled(at);
                _danger.PlayScheduled(at);
                _playing = true;
                Debug.Log($"[Music] loops ready ({_calm.clip.length:0.0} s each), music at {SoundSettings.Music:P0}");
            }

            float dt = Time.unscaledDeltaTime;
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                _trouble = AnyoneInTrouble();
            }
            // In quickly when someone goes under, out slowly once they're safe.
            _dangerMix = Mathf.MoveTowards(_dangerMix, _trouble ? 1f : 0f, dt * (_trouble ? 0.8f : 0.25f));
            _duck = Mathf.MoveTowards(_duck, _jingles.isPlaying ? 1f : 0f, dt * 3f);
            float music = SoundSettings.Music * (1f - 0.55f * _duck);
            _calm.volume = music * CalmLevel * (1f - 0.3f * _dangerMix);
            _danger.volume = music * DangerLevel * _dangerMix;
        }

        /// <summary>A tourist in the water who is past "fine", or one lying ashore who still needs CPR.</summary>
        private static bool AnyoneInTrouble()
        {
            var victims = VictimBrain.All;
            for (int i = 0; i < victims.Count; i++)
            {
                VictimBrain v = victims[i];
                if (v == null) continue;
                if (v.State is VictimState.Distressed or VictimState.Panicking or VictimState.Drowning or VictimState.Unconscious) return true;
            }
            return false;
        }

        private void PlayJingle(Jingle jingle)
        {
            int i = (int)jingle;
            if (_jingleClips[i] == null)
            {
                float[] samples = jingle switch
                {
                    Jingle.Saved => MusicSynth.RenderSaved(),
                    Jingle.Lost => MusicSynth.RenderLost(),
                    _ => MusicSynth.RenderChapter()
                };
                _jingleClips[i] = AudioClip.Create("Jingle" + jingle, samples.Length, 1, MusicSynth.Rate, false);
                _jingleClips[i].SetData(samples, 0);
            }
            _jingles.volume = 1f;
            _jingles.PlayOneShot(_jingleClips[i], SoundSettings.Music * JingleLevel);
        }

        private static AudioClip Stereo(string clipName, float[] interleaved)
        {
            AudioClip clip = AudioClip.Create(clipName, interleaved.Length / 2, 2, MusicSynth.Rate, false);
            clip.SetData(interleaved, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_calm != null && _calm.clip != null) Destroy(_calm.clip);
            if (_danger != null && _danger.clip != null) Destroy(_danger.clip);
            foreach (AudioClip clip in _jingleClips)
                if (clip != null) Destroy(clip);
        }
    }
}
