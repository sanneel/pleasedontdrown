using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>
    /// The beach bar's sounds, made in code like the rest: a gulp of beer, and the big burp that comes up about ten
    /// seconds later (a few variants so two players never burp in unison).
    /// </summary>
    public static class BurpSound
    {
        private const int SampleRate = 44100;
        public const int Variants = 3;
        private static AudioClip[] _burps;
        private static AudioClip _gulp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _burps = null;
            _gulp = null;
        }

        public static AudioClip Burp(int variant)
        {
            _burps ??= new AudioClip[Variants];
            variant = Mathf.Clamp(variant, 0, Variants - 1);
            return _burps[variant] != null ? _burps[variant] : _burps[variant] = BuildBurp(variant);
        }

        /// <summary>One swallow: the throat's soft thump and the glug of a bubble going down.</summary>
        public static AudioClip Gulp => _gulp != null ? _gulp : _gulp = Build("Gulp", Synth(0.26f, t =>
        {
            float thump = Mathf.Sin(2f * Mathf.PI * 85f * t) * Mathf.Exp(-30f * t) * 0.5f;
            float b = t - 0.05f;
            float glug = b < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * (260f * b + 2600f * b * b)) * Mathf.Exp(-26f * b) * Mathf.Clamp01(b / 0.004f) * 0.6f;
            return (thump + glug) * Mathf.Clamp01(t / 0.003f);
        }));

        /// <summary>
        /// A burp is a low, rattling voice from the gullet: an uneven pulse train (the "fry" of loose tissue flapping,
        /// every other pulse weaker) dropping in pitch, sung through a deep "aw" mouth.
        /// </summary>
        private static AudioClip BuildBurp(int variant)
        {
            var rng = new System.Random(4021 + variant * 77);
            float seconds = 0.95f + 0.25f * variant;
            float startPitch = 112f - 12f * variant, endPitch = 68f - 6f * variant;
            int n = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[n];
            float phase = 0f, jitter = 0f, pulseGain = 1f;
            Resonator f1 = new(560f - 40f * variant, 140f), f2 = new(980f, 190f), f3 = new(2450f, 300f);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate, u = t / seconds;
                jitter += ((float)rng.NextDouble() * 2f - 1f) * 0.002f - jitter * 0.0015f; // the pitch never sits still
                float f0 = Mathf.Lerp(startPitch, endPitch, Mathf.SmoothStep(0f, 1f, u)) * (1f + Mathf.Clamp(jitter, -0.12f, 0.12f));
                phase += f0 / SampleRate;
                if (phase >= 1f)
                {
                    phase -= 1f;
                    pulseGain = (rng.NextDouble() < 0.5 ? 0.55f : 1f) * (0.8f + 0.2f * (float)rng.NextDouble()); // the rattle
                }
                float open = phase < 0.4f ? Mathf.Sin(Mathf.PI * phase / 0.4f) : 0f;
                float source = (open * open - 0.2f) * pulseGain + ((float)rng.NextDouble() * 2f - 1f) * 0.04f;
                float voice = f1.Step(source) * 1f + f2.Step(source) * 0.55f + f3.Step(source) * 0.12f + source * 0.18f;
                float envelope = Mathf.Clamp01(t / 0.035f) * (u < 0.7f ? 1f : Mathf.SmoothStep(1f, 0f, (u - 0.7f) / 0.3f));
                envelope *= 0.85f + 0.15f * Mathf.Sin(t * 2f * Mathf.PI * (5f + variant)); // the gut pushing it out in waves
                data[i] = voice * envelope;
            }
            float peak = 1e-4f;
            foreach (float s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(data[i] / peak * 0.92f, -1f, 1f);
            return Build($"Burp{variant}", data);
        }

        /// <summary>A two-pole band-pass: one formant of the mouth.</summary>
        private struct Resonator
        {
            private readonly float _a, _b1, _b2;
            private float _y1, _y2;

            public Resonator(float frequency, float bandwidth)
            {
                float r = Mathf.Exp(-Mathf.PI * bandwidth / SampleRate);
                _b1 = 2f * r * Mathf.Cos(2f * Mathf.PI * frequency / SampleRate);
                _b2 = -r * r;
                _a = 1f - r;
                _y1 = _y2 = 0f;
            }

            public float Step(float x)
            {
                float y = _a * x + _b1 * _y1 + _b2 * _y2;
                _y2 = _y1;
                _y1 = y;
                return y * 8f;
            }
        }

        private static float[] Synth(float seconds, System.Func<float, float> wave)
        {
            var data = new float[Mathf.CeilToInt(seconds * SampleRate)];
            for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(wave(i / (float)SampleRate), -1f, 1f);
            return data;
        }

        private static AudioClip Build(string name, float[] data)
        {
            AudioClip clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
