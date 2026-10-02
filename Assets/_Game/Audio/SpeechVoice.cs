using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>
    /// Says a line out loud as babble: walks through the text at reading speed and plays a syllable for each vowel
    /// (<see cref="SpeechSynth"/>), pausing at commas and full stops, rising at a question mark. On a character it
    /// comes from where they stand; <see cref="Local"/> is for lines with nobody in the world behind them (the
    /// player's own, a radio). How loud: <see cref="SoundSettings.Voice"/>.
    /// </summary>
    public sealed class SpeechVoice : MonoBehaviour
    {
        private const float LetterSeconds = 0.058f, SpaceSeconds = 0.045f, CommaSeconds = 0.17f, StopSeconds = 0.3f;

        private static SpeechVoice _local;

        private AudioSource _audio;
        private string _text;
        private int _index, _register;
        private float _next, _pitch, _gain;
        private bool _question, _afterConsonant;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _local = null;

        /// <summary>A voice heard the same everywhere (no place in the world).</summary>
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

        /// <summary>The voice of something in the world, heard from where it is (falls off like <paramref name="like"/>).</summary>
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
            if (like != null)
            {
                _audio.rolloffMode = like.rolloffMode;
                _audio.minDistance = like.minDistance;
                _audio.maxDistance = Mathf.Min(like.maxDistance, 28f); // talk doesn't carry like a scream
                _audio.dopplerLevel = 0f;
            }
            enabled = false; // only ticks while a line is being said
        }

        /// <summary>
        /// Start saying <paramref name="text"/> (whatever was being said stops). <paramref name="register"/> picks the
        /// kind of voice (0..3, deep to high), <paramref name="pitch"/> makes it this person's own.
        /// </summary>
        public void Speak(string text, int register, float pitch = 1f, float gain = 1f)
        {
            if (string.IsNullOrEmpty(text) || SoundSettings.Voice <= 0.001f || Application.isBatchMode) return;
            _text = text;
            _index = 0;
            _register = register;
            _pitch = pitch;
            _gain = gain;
            _next = Time.time + 0.05f;
            _afterConsonant = false;
            _question = text.IndexOf('?', System.Math.Max(0, text.Length - 3)) >= 0;
            enabled = true;
        }

        public void Stop()
        {
            _text = null;
            enabled = false;
        }

        private void Update()
        {
            if (_text == null)
            {
                enabled = false;
                return;
            }
            // Catch up at most a few letters a frame (a long hitch doesn't turn into a burst of syllables).
            for (int steps = 0; steps < 4 && Time.time >= _next; steps++)
            {
                if (_index >= _text.Length)
                {
                    Stop();
                    return;
                }
                char c = _text[_index++];
                int vowel = SpeechSynth.VowelOf(c);
                if (vowel >= 0)
                {
                    bool previousVowel = _index >= 2 && SpeechSynth.VowelOf(_text[_index - 2]) >= 0;
                    if (!previousVowel) Say(vowel, c);
                    _afterConsonant = false;
                    _next += LetterSeconds;
                }
                else if (char.IsLetter(c))
                {
                    _afterConsonant = true;
                    _next += LetterSeconds;
                }
                else
                {
                    _afterConsonant = false;
                    _next += c is '.' or '!' or '?' ? StopSeconds : c is ',' or ';' or ':' ? CommaSeconds : SpaceSeconds;
                }
            }
        }

        private void Say(int vowel, char letter)
        {
            // The same letter at the same place always sounds the same: a small sing-song, a rise at the end of a question.
            float wobble = 1f + (((letter * 7 + _index * 13) % 9) - 4) * 0.014f;
            if (_question && _index > _text.Length - 9) wobble *= 1f + 0.016f * (9 - (_text.Length - _index));
            _audio.pitch = _pitch * wobble;
            bool loud = char.IsUpper(letter);
            _audio.PlayOneShot(SpeechSynth.Syllable(_register, vowel, _afterConsonant), SoundSettings.Voice * _gain * (loud ? 1f : 0.8f));
        }
    }
}
