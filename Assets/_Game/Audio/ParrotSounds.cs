using System;
using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>
    /// The island's parrots, made in code like the rest: a harsh two-part "raa-AWK!" squawk (three takes, so a flock
    /// doesn't sound like one bird on repeat) and a burst of parrot "talk" (buzzy babble that rises and falls like
    /// words), plus the clatter of wings taking off.
    /// </summary>
    public static class ParrotSounds
    {
        private const int SampleRate = 44100;
        private static AudioClip[] _squawks;
        private static AudioClip _talk, _flap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _squawks = null;
            _talk = _flap = null;
        }

        /// <summary>One of the squawks, at random.</summary>
        public static AudioClip Squawk
        {
            get
            {
                _squawks ??= new[] { MakeSquawk(1, 1f), MakeSquawk(2, 1.12f), MakeSquawk(3, 0.9f) };
                return _squawks[UnityEngine.Random.Range(0, _squawks.Length)];
            }
        }

        /// <summary>"Hello! Pretty bird!" in parrot: six buzzy syllables.</summary>
        public static AudioClip Talk => _talk != null ? _talk : _talk = MakeTalk();

        /// <summary>Wings clattering as it takes off.</summary>
        public static AudioClip Flap => _flap != null ? _flap : _flap = MakeFlap();

        /// <summary>A raspy voice: a sawtooth with a rough flutter and breath noise, squeezed through a nasal band.</summary>
        private sealed class Voice
        {
            private float _phase, _low, _high;
            private readonly System.Random _rng;
            public Voice(int seed) => _rng = new System.Random(seed);

            public float Next(float t, float pitch, float rasp)
            {
                _phase = Mathf.Repeat(_phase + pitch / SampleRate, 1f);
                float saw = 2f * _phase - 1f;
                float flutter = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 62f * t);
                float noise = (float)(_rng.NextDouble() * 2.0 - 1.0);
                float x = saw * flutter + noise * rasp;
                _low += (x - _low) * 0.32f;       // take the fizz off
                _high += (_low - _high) * 0.045f; // and the boom: what's left is the nasal middle
                return _low - _high;
            }
        }

        private static AudioClip MakeSquawk(int seed, float pitch)
        {
            var voice = new Voice(seed);
            return Core.ProceduralAudio.Build($"ParrotSquawk{seed}", 0.62f, t =>
            {
                // "raa" (short, lower) then "AWK!" (longer, up and falling away)
                float f, env;
                if (t < 0.16f)
                {
                    float u = t / 0.16f;
                    f = 820f + 260f * u;
                    env = Mathf.Sin(Mathf.PI * u);
                }
                else if (t < 0.22f) return voice.Next(t, 900f, 0f) * 0f;
                else
                {
                    float u = (t - 0.22f) / 0.4f;
                    f = 1350f - 650f * u * u;
                    env = Mathf.Clamp01(u / 0.06f) * Mathf.Pow(1f - u, 1.6f);
                }
                return voice.Next(t, f * pitch, 0.35f) * env * 0.55f;
            });
        }

        private static AudioClip MakeTalk()
        {
            var voice = new Voice(7);
            float[] tones = { 1.0f, 1.25f, 0.9f, 1.35f, 1.1f, 0.8f };
            const float syllable = 0.15f, gap = 0.04f;
            return Core.ProceduralAudio.Build("ParrotTalk", tones.Length * (syllable + gap) + 0.1f, t =>
            {
                int k = Mathf.FloorToInt(t / (syllable + gap));
                if (k >= tones.Length) return 0f;
                float u = (t - k * (syllable + gap)) / syllable;
                if (u > 1f) return 0f;
                float f = 980f * tones[k] * (1f + 0.12f * Mathf.Sin(Mathf.PI * u));
                return voice.Next(t, f, 0.18f) * Mathf.Sin(Mathf.PI * u) * 0.45f;
            });
        }

        private static AudioClip MakeFlap()
        {
            var rng = new System.Random(11);
            float low = 0f;
            return Core.ProceduralAudio.Build("ParrotFlap", 0.7f, t =>
            {
                float beat = Mathf.Repeat(t, 0.11f) / 0.11f;
                float env = Mathf.Exp(-beat * 9f) * (1f - t / 0.7f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.18f;
                return low * env * 0.9f;
            });
        }
    }
}
