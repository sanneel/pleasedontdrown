using System;
using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>
    /// Sounds for island 1's fun and games, made in code like the rest (no recordings): the basketball (bounce, rim,
    /// swish, crowd cheer), getting bonked and seeing stars (slide whistle, boing, tweeting birds), the cannonball
    /// and the beach hut's door-shut gag (bed springs squeaking, "ooh"s, a cuckoo clock).
    /// </summary>
    public static class FunSounds
    {
        private const int SampleRate = 44100;
        private static AudioClip _bounce, _rim, _swish, _cheer, _slide, _boing, _tweet, _squeak, _oohLow, _oohHigh, _cuckoo, _whoop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() =>
            _bounce = _rim = _swish = _cheer = _slide = _boing = _tweet = _squeak = _oohLow = _oohHigh = _cuckoo = _whoop = null;

        private static Func<float> Noise(int seed)
        {
            var rng = new System.Random(seed);
            return () => (float)(rng.NextDouble() * 2.0 - 1.0);
        }

        /// <summary>Rubber ball on sand or boards: a hollow thump.</summary>
        public static AudioClip BallBounce => _bounce != null ? _bounce : _bounce = Core.ProceduralAudio.Build("BallBounce", 0.22f, t =>
        {
            float f = Mathf.Lerp(180f, 120f, t / 0.22f);
            return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2.7f * t)) * Mathf.Exp(-22f * t) * Mathf.Clamp01(t / 0.002f) * 0.75f;
        });

        /// <summary>Ball off the steel rim: a ringing clang.</summary>
        public static AudioClip Rim => _rim != null ? _rim : _rim = Core.ProceduralAudio.Build("Rim", 0.9f, t =>
        {
            float s = Mathf.Sin(2f * Mathf.PI * 523f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1391f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * 2207f * t) * 0.2f;
            return s * Mathf.Exp(-6f * t) * Mathf.Clamp01(t / 0.001f) * 0.6f;
        });

        /// <summary>Nothing but net.</summary>
        public static AudioClip Swish
        {
            get
            {
                if (_swish != null) return _swish;
                Func<float> noise = Noise(7);
                float low = 0f;
                return _swish = Core.ProceduralAudio.Build("Swish", 0.45f, t =>
                {
                    float x = noise();
                    low += (x - low) * 0.25f;
                    float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.45f));
                    return (x - low) * env * env * 0.45f;
                });
            }
        }

        /// <summary>The beach goes "WOOO!": a swell of crowd noise with a few whoops on top.</summary>
        public static AudioClip Cheer
        {
            get
            {
                if (_cheer != null) return _cheer;
                Func<float> noise = Noise(31);
                float band = 0f, band2 = 0f;
                float[] phases = new float[6];
                return _cheer = Core.ProceduralAudio.Build("Cheer", 2.2f, t =>
                {
                    float x = noise();
                    band += (x - band) * 0.12f;
                    band2 += (band - band2) * 0.12f;
                    float crowd = (band - band2) * 3f;
                    float voices = 0f;
                    for (int v = 0; v < phases.Length; v++)
                    {
                        float f = (300f + v * 70f) * (1f + 0.25f * Mathf.Sin(t * (2f + v) + v)) * (1f + 0.15f * t);
                        phases[v] += 2f * Mathf.PI * f / SampleRate;
                        voices += Mathf.Sin(phases[v]) * (0.5f + 0.5f * Mathf.Sin(t * 5f + v * 1.7f));
                    }
                    float env = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((2.2f - t) / 0.9f);
                    return (crowd * 0.6f + voices * 0.06f) * env;
                });
            }
        }

        /// <summary>Cartoon fall: a slide whistle going down.</summary>
        public static AudioClip SlideDown
        {
            get
            {
                if (_slide != null) return _slide;
                float phase = 0f;
                return _slide = Core.ProceduralAudio.Build("SlideDown", 0.8f, t =>
                {
                    float f = Mathf.Lerp(1400f, 300f, Mathf.Pow(t / 0.8f, 0.7f)) * (1f + 0.02f * Mathf.Sin(t * 40f));
                    phase += 2f * Mathf.PI * f / SampleRate;
                    return (Mathf.Sin(phase) + 0.2f * Mathf.Sin(phase * 2f)) * Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((0.8f - t) / 0.1f) * 0.4f;
                });
            }
        }

        /// <summary>A spring: boi-oi-oing.</summary>
        public static AudioClip Boing
        {
            get
            {
                if (_boing != null) return _boing;
                float phase = 0f;
                return _boing = Core.ProceduralAudio.Build("Boing", 0.7f, t =>
                {
                    float f = 160f * (1f + 0.6f * Mathf.Sin(2f * Mathf.PI * 9f * t) * Mathf.Exp(-3f * t)) + 40f * t;
                    phase += 2f * Mathf.PI * f / SampleRate;
                    return Mathf.Sin(phase) * Mathf.Exp(-4f * t) * Mathf.Clamp01(t / 0.004f) * 0.6f;
                });
            }
        }

        /// <summary>Little birds circling a dazed head.</summary>
        public static AudioClip Tweet
        {
            get
            {
                if (_tweet != null) return _tweet;
                float phase = 0f;
                return _tweet = Core.ProceduralAudio.Build("Tweet", 1.2f, t =>
                {
                    float chirp = Mathf.Repeat(t, 0.3f);
                    float f = 2600f + 1800f * Mathf.Sin(Mathf.PI * chirp / 0.12f) * (chirp < 0.12f ? 1f : 0f);
                    phase += 2f * Mathf.PI * f / SampleRate;
                    float env = chirp < 0.12f ? Mathf.Sin(Mathf.PI * chirp / 0.12f) : 0f;
                    return Mathf.Sin(phase) * env * 0.25f;
                });
            }
        }

        /// <summary>Old bed springs: squeak... squeak... (one squeak; played in a rhythm).</summary>
        public static AudioClip Squeak
        {
            get
            {
                if (_squeak != null) return _squeak;
                float phase = 0f;
                return _squeak = Core.ProceduralAudio.Build("Squeak", 0.28f, t =>
                {
                    float f = 900f + 500f * Mathf.Sin(Mathf.PI * t / 0.28f) + 60f * Mathf.Sin(t * 260f);
                    phase += 2f * Mathf.PI * f / SampleRate;
                    float s = Mathf.Sin(phase) * 0.6f + Mathf.Sin(phase * 2.03f) * 0.25f + Mathf.Sin(phase * 3.1f) * 0.1f;
                    return s * Mathf.Sin(Mathf.PI * t / 0.28f) * 0.35f;
                });
            }
        }

        /// <summary>A cartoon "ooh!" (low or high voice).</summary>
        public static AudioClip Ooh(bool high) => high
            ? _oohHigh != null ? _oohHigh : _oohHigh = Vowel("OohHigh", 420f)
            : _oohLow != null ? _oohLow : _oohLow = Vowel("OohLow", 170f);

        private static AudioClip Vowel(string name, float f0)
        {
            const float length = 0.75f;
            float phase = 0f;
            return Core.ProceduralAudio.Build(name, length, t =>
            {
                float u = t / length;
                float f = f0 * (1f + 0.25f * Mathf.Sin(Mathf.PI * u) - 0.1f * u) * (1f + 0.01f * Mathf.Sin(t * 38f));
                phase += 2f * Mathf.PI * f / SampleRate;
                float s = 0f;
                for (int h = 1; h <= 12; h++)
                {
                    float fh = f * h;
                    float amp = Mathf.Exp(-Sq((fh - 380f) / 160f)) + 0.5f * Mathf.Exp(-Sq((fh - 900f) / 220f)) + 0.04f / h; // "oo"
                    s += amp * Mathf.Sin(phase * h) / Mathf.Sqrt(h);
                }
                return s * 0.35f * Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((length - t) / 0.3f);
            });
        }

        /// <summary>A cuckoo clock: cuck-oo.</summary>
        public static AudioClip Cuckoo => _cuckoo != null ? _cuckoo : _cuckoo = Core.ProceduralAudio.Build("Cuckoo", 0.75f, t =>
        {
            float f = t < 0.32f ? 740f : 587f;
            float local = t < 0.32f ? t : t - 0.38f;
            if (local < 0f) return 0f;
            return (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t)) * Mathf.Exp(-7f * local) * Mathf.Clamp01(local / 0.01f) * 0.4f;
        });

        /// <summary>Rising whistle: up goes the score (and the cannonball's splash rating).</summary>
        public static AudioClip Whoop
        {
            get
            {
                if (_whoop != null) return _whoop;
                float phase = 0f;
                return _whoop = Core.ProceduralAudio.Build("Whoop", 0.5f, t =>
                {
                    float f = Mathf.Lerp(500f, 1500f, t / 0.5f);
                    phase += 2f * Mathf.PI * f / SampleRate;
                    return Mathf.Sin(phase) * Mathf.Sin(Mathf.PI * t / 0.5f) * 0.3f;
                });
            }
        }

        private static float Sq(float x) => x * x;
    }
}
