using System;
using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>Shared mastering for generated foley: DC removal, click-free edges and peak headroom.</summary>
    public static class SoundClip
    {
        public const int Rate = 44100;

        public static AudioClip Build(string name, float seconds, Func<float, float> wave)
        {
            var samples = new float[Mathf.CeilToInt(seconds * Rate)];
            for (int i = 0; i < samples.Length; i++) samples[i] = wave(i / (float)Rate);
            return Create(name, samples);
        }

        public static AudioClip Create(string name, float[] samples)
        {
            float previous = 0f, dc = 0f, peak = 0f;
            int fade = Mathf.Min(220, samples.Length / 4);
            for (int i = 0; i < samples.Length; i++)
            {
                float input = samples[i];
                if (float.IsNaN(input) || float.IsInfinity(input))
                    throw new InvalidOperationException(name + " contains invalid audio");
                // 15 Hz DC blocker; preserve bass impacts without carrying a static offset.
                dc = input - previous + 0.99786f * dc;
                previous = input;
                float edge = Mathf.Clamp01(Mathf.Min(i, samples.Length - 1 - i) / (float)Mathf.Max(1, fade));
                samples[i] = dc * edge;
                peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            }
            // Attenuate the entire waveform instead of flattening individual peaks.
            float gain = peak > 0.82f ? 0.82f / peak : 1f;
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
            var clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
