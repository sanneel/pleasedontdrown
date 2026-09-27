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
        private static void ResetStatics() => _bell = _click = null;

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
