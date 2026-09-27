using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// Placeholder sounds synthesized at runtime so the prototype has audio feedback without any asset files.
    /// Each clip is generated once and cached.
    /// </summary>
    public static class ProceduralAudio
    {
        private const int SampleRate = 44100;
        private static AudioClip _bell;
        private static AudioClip _click;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _bell = _click = _splash = _waterStep = _cough = _thump = null;
            _steps = null;
            _cries = null;
        }

        /// <summary>Brass hand bell: inharmonic partials with individual decay rates.</summary>
        // Explicit null checks: Unity can unload these clips, and "??=" doesn't see Unity's destroyed objects.
        public static AudioClip Bell => _bell != null ? _bell : _bell = Build("Bell", 1.8f, t =>
        {
            float f = 740f;
            float s = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-2.2f * t)
                      + 0.6f * Mathf.Sin(2f * Mathf.PI * f * 2.76f * t) * Mathf.Exp(-3.5f * t)
                      + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 5.40f * t) * Mathf.Exp(-6f * t)
                      + 0.2f * Mathf.Sin(2f * Mathf.PI * f * 8.93f * t) * Mathf.Exp(-9f * t);
            float strike = Mathf.Clamp01(t / 0.004f); // avoid a click at the very start
            return s * 0.35f * strike;
        });

        /// <summary>Short mechanical switch click.</summary>
        public static AudioClip Click => _click != null ? _click : _click = Build("Click", 0.06f, t =>
        {
            float noise = Mathf.PerlinNoise(t * 9000f, 0.37f) * 2f - 1f;
            return (noise * 0.6f + Mathf.Sin(2f * Mathf.PI * 2100f * t) * 0.4f) * Mathf.Exp(-70f * t) * 0.6f;
        });

        private static AudioClip _splash;

        /// <summary>Water splash: low-passed noise with a fast attack and a bubbly tail.</summary>
        public static AudioClip Splash
        {
            get
            {
                if (_splash != null) return _splash;
                var rng = new System.Random(1234);
                float low = 0f;
                _splash = Build("Splash", 0.7f, t =>
                {
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float cutoff = Mathf.Lerp(0.5f, 0.08f, Mathf.Clamp01(t / 0.5f)); // bright at impact, darker tail
                    low += (noise - low) * cutoff;
                    float bubbles = 0.3f * Mathf.Sin(2f * Mathf.PI * (380f + 120f * Mathf.Sin(t * 37f)) * t) * Mathf.Exp(-6f * t);
                    return (low * 1.6f + bubbles) * Mathf.Exp(-5f * t) * Mathf.Clamp01(t / 0.005f);
                });
                return _splash;
            }
        }

        private static AudioClip[] _steps;
        private static AudioClip _waterStep;
        private static int _stepVariant;

        /// <summary>A footstep for the surface, cycling through a few variants so steps don't sound identical.</summary>
        public static AudioClip Step(World.SurfaceKind kind)
        {
            if (_steps == null || _steps[0] == null)
            {
                _steps = new AudioClip[9];
                for (int v = 0; v < 3; v++)
                {
                    _steps[v] = SandStep(v);
                    _steps[3 + v] = WoodStep(v);
                    _steps[6 + v] = RockStep(v);
                }
            }
            _stepVariant = (_stepVariant + 1) % 3;
            return _steps[(int)kind * 3 + _stepVariant];
        }

        public static AudioClip WaterStep => _waterStep != null ? _waterStep : _waterStep = Noise("WaterStep", 0.28f, 77, 0.35f, 0.06f, 9f, 0.5f);

        private static AudioClip SandStep(int v) => Noise($"SandStep{v}", 0.14f, 11 + v, 0.22f + 0.04f * v, 0.05f, 28f, 0.55f);

        private static AudioClip RockStep(int v) => Noise($"RockStep{v}", 0.07f, 31 + v, 0.9f, 0.4f, 60f, 0.5f);

        private static AudioClip WoodStep(int v)
        {
            var rng = new System.Random(51 + v);
            float f = 150f + 25f * v;
            return Build($"WoodStep{v}", 0.18f, t =>
            {
                float knock = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-28f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * f * 2.7f * t) * Mathf.Exp(-45f * t);
                float scuff = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-70f * t) * 0.35f;
                return (knock * 0.6f + scuff) * Mathf.Clamp01(t / 0.002f);
            });
        }

        private static AudioClip[] _cries;
        private static AudioClip _cough;
        private static AudioClip _thump;

        /// <summary>
        /// A wobbly "heeelp!"-ish cry: a voiced tone (harmonics shaped by two vowel formants) that rises and falls,
        /// with vibrato and a breathy edge. Four voices (low to high).
        /// </summary>
        public static AudioClip Cry(int voice)
        {
            if (_cries == null || _cries[0] == null)
            {
                _cries = new AudioClip[4];
                float[] pitches = { 170f, 225f, 290f, 360f };
                for (int v = 0; v < 4; v++)
                    _cries[v] = BuildCry(v, pitches[v]);
            }
            return _cries[Mathf.Clamp(voice, 0, 3)];
        }

        private static AudioClip BuildCry(int v, float f0)
        {
            const float length = 0.85f;
            var rng = new System.Random(900 + v);
            float phase = 0f;
            float breath = 0f;
            return Build($"Cry{v}", length, t =>
            {
                float u = t / length;
                float glide = 1f + 0.35f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.4f)) - 0.25f * u;
                float f = f0 * glide * (1f + 0.025f * Mathf.Sin(2f * Mathf.PI * 6.5f * t));
                phase += 2f * Mathf.PI * f / SampleRate;
                // Vowel moves from "e" (help) to "a" (aaah).
                float f1 = Mathf.Lerp(550f, 780f, u), f2 = Mathf.Lerp(1850f, 1200f, u);
                float s = 0f;
                for (int h = 1; h <= 16; h++)
                {
                    float fh = f * h;
                    float amp = Mathf.Exp(-Sq((fh - f1) / 180f)) + 0.6f * Mathf.Exp(-Sq((fh - f2) / 260f)) + 0.05f / h;
                    s += amp * Mathf.Sin(phase * h);
                }
                breath += ((float)(rng.NextDouble() * 2.0 - 1.0) - breath) * 0.3f;
                float envelope = Mathf.Clamp01(t / 0.05f) * Mathf.Clamp01((length - t) / 0.25f);
                return (s * 0.22f + breath * 0.08f) * envelope;
            });
        }

        private static float Sq(float x) => x * x;

        /// <summary>Two rough coughs: noisy bursts over a low grunt.</summary>
        public static AudioClip Cough
        {
            get
            {
                if (_cough != null) return _cough;
                var rng = new System.Random(4242);
                float low = 0f;
                _cough = Build("Cough", 0.75f, t =>
                {
                    float local = t < 0.3f ? t : t - 0.34f;
                    if (local < 0f) return 0f;
                    float env = Mathf.Clamp01(local / 0.01f) * Mathf.Exp(-11f * local);
                    low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * 0.25f;
                    float grunt = Mathf.Sin(2f * Mathf.PI * 140f * t) * 0.4f;
                    return (low * 1.4f + grunt) * env * 0.7f;
                });
                return _cough;
            }
        }

        /// <summary>Soft chest-compression thump.</summary>
        public static AudioClip Thump => _thump != null ? _thump : _thump = Build("Thump", 0.2f, t =>
        {
            float f = Mathf.Lerp(95f, 55f, t / 0.2f);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-22f * t) * Mathf.Clamp01(t / 0.003f) * 0.8f;
        });

        /// <summary>Low-passed noise burst: cutoff falls from <paramref name="brightStart"/> to <paramref name="brightEnd"/>.</summary>
        private static AudioClip Noise(string name, float seconds, int seed, float brightStart, float brightEnd, float decay, float gain)
        {
            var rng = new System.Random(seed);
            float low = 0f;
            return Build(name, seconds, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * Mathf.Lerp(brightStart, brightEnd, t / seconds);
                return low * gain * 2f * Mathf.Exp(-decay * t) * Mathf.Clamp01(t / 0.003f);
            });
        }

        private static AudioClip Build(string name, float seconds, System.Func<float, float> wave)
        {
            int count = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[count];
            for (int i = 0; i < count; i++)
                data[i] = Mathf.Clamp(wave(i / (float)SampleRate), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
