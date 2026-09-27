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
            _bell = _click = _splash = _waterStep = null;
            _steps = null;
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
