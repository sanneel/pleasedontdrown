using System;

namespace PleaseDontDrown.Audio
{
    /// <summary>
    /// The game's music, written as notes and synthesized here (no audio files): a ukulele strum, a marimba tune, a
    /// bass and a shaker for the calm beach loop; drums and a driving bass for the "someone is drowning" layer that
    /// plays on top of it, bar for bar; and a few short jingles.
    ///
    /// Everything is plain arithmetic on sample buffers (no Unity calls), so the loops can be rendered on a worker
    /// thread while the menu is already up.
    /// </summary>
    public static class MusicSynth
    {
        public const int Rate = 44100;
        public const float Bpm = 96f;
        private const int Bars = 8, BeatsPerBar = 4;

        /// <summary>One beat, in samples.</summary>
        private static readonly int Beat = (int)Math.Round(Rate * 60.0 / Bpm);
        /// <summary>Both loops are exactly this long, so they stay in step.</summary>
        public static int LoopSamples => Beat * BeatsPerBar * Bars;

        // C, Am, F, G twice round: the four ukulele strings (low to high as strummed) and the bass note of each bar.
        private static readonly int[][] Chords =
        {
            new[] { 67, 60, 64, 72 }, new[] { 69, 60, 64, 69 }, new[] { 69, 60, 65, 69 }, new[] { 67, 62, 67, 71 }
        };
        private static readonly int[] Roots = { 48, 45, 41, 43 };
        private static readonly int[] Fifths = { 43, 40, 48, 38 };

        // "Down, down-up, up-down-up": which eighths of a bar are strummed, and which of those go upward.
        private static readonly int[] StrumSlots = { 0, 2, 3, 5, 6, 7 };
        private static readonly bool[] StrumUp = { false, false, true, true, false, true };

        // The tune: per bar, pairs of (eighth of the bar, note).
        private static readonly int[][] Tune =
        {
            new[] { 0, 76, 2, 79, 3, 81, 6, 79 },
            new[] { 0, 76, 2, 72, 4, 74, 6, 76 },
            new[] { 0, 81, 2, 84, 4, 81, 6, 77 },
            new[] { 0, 74, 2, 79, 4, 83, 5, 81, 6, 79 },
            new[] { 0, 84, 3, 79, 4, 76, 6, 79 },
            new[] { 0, 81, 2, 76, 4, 72, 6, 76 },
            new[] { 0, 77, 2, 81, 3, 84, 6, 81 },
            new[] { 0, 83, 2, 81, 4, 79, 6, 74 }
        };

        /// <summary>The calm beach loop: interleaved stereo, <see cref="LoopSamples"/> frames.</summary>
        public static float[] RenderCalm()
        {
            int total = LoopSamples, eighth = Beat / 2;
            var left = new float[total];
            var right = new float[total];
            var rng = new Random(7);
            int swing = (int)(eighth * 0.14f); // the off-beat eighths sit a little late
            for (int bar = 0; bar < Bars; bar++)
            {
                int start = bar * Beat * BeatsPerBar;
                int[] chord = Chords[bar % 4];
                for (int s = 0; s < StrumSlots.Length; s++)
                {
                    int slot = StrumSlots[s];
                    int at = start + slot * eighth + (slot % 2 == 1 ? swing : 0);
                    bool up = StrumUp[s];
                    for (int i = 0; i < 4; i++)
                    {
                        int note = chord[up ? 3 - i : i];
                        // The pick crosses the strings in about 30 ms; an upstroke is lighter and only catches three.
                        if (up && i == 3) continue;
                        Pluck(left, right, at + i * (Rate * 9 / 1000), note, (up ? 0.1f : 0.14f) * (slot == 0 ? 1.25f : 1f), 0.38f, 0.9f, rng);
                    }
                }
                Bass(left, right, start, Roots[bar % 4], 0.3f);
                Bass(left, right, start + Beat * 2, Fifths[bar % 4], 0.22f);
                Bass(left, right, start + Beat * 3 + eighth + swing, Roots[bar % 4], 0.16f);
                for (int e = 0; e < 8; e++)
                    Shaker(left, right, start + e * eighth + (e % 2 == 1 ? swing : 0), e % 2 == 1 ? 0.085f : 0.045f, rng);
                int[] tune = Tune[bar];
                for (int n = 0; n < tune.Length; n += 2)
                    Marimba(left, right, start + tune[n] * eighth + (tune[n] % 2 == 1 ? swing : 0), tune[n + 1], 0.2f, 0.64f);
            }
            return Finish(left, right, 0.8f);
        }

        /// <summary>The danger layer: drums, a ticking block and a pushing bass on the same chords.</summary>
        public static float[] RenderDanger()
        {
            int total = LoopSamples, eighth = Beat / 2, sixteenth = Beat / 4;
            var left = new float[total];
            var right = new float[total];
            var rng = new Random(11);
            for (int bar = 0; bar < Bars; bar++)
            {
                int start = bar * Beat * BeatsPerBar;
                for (int b = 0; b < BeatsPerBar; b++)
                {
                    Drum(left, right, start + b * Beat, 118f, 46f, 0.55f);
                    if (b % 2 == 1) Drum(left, right, start + b * Beat + eighth, 150f, 70f, 0.32f);
                }
                Drum(left, right, start + Beat * 3 + sixteenth * 3, 175f, 90f, 0.26f); // a pick-up into the next bar
                for (int s = 0; s < 16; s++)
                    Shaker(left, right, start + s * sixteenth, s % 4 == 0 ? 0.07f : 0.04f, rng);
                for (int e = 0; e < 8; e++)
                {
                    Bass(left, right, start + e * eighth, Roots[bar % 4] - (e % 4 == 3 ? 0 : 12), 0.2f, 14f);
                    if (e % 2 == 1) Block(left, right, start + e * eighth, 0.07f);
                }
            }
            return Finish(left, right, 0.8f);
        }

        /// <summary>Someone was saved: a quick run up the marimba and a bright chord. Mono.</summary>
        public static float[] RenderSaved()
        {
            var a = new float[(int)(Rate * 1.5f)];
            int step = Rate * 85 / 1000;
            int[] run = { 72, 76, 79, 84 };
            for (int i = 0; i < run.Length; i++) Marimba(a, null, i * step, run[i], 0.3f, 0.5f, wrap: false);
            var rng = new Random(3);
            foreach (int note in new[] { 72, 76, 79, 84 }) Pluck(a, null, step * 4, note, 0.13f, 0.5f, 1.1f, rng, wrap: false);
            return Finish(a, null, 0.7f);
        }

        /// <summary>Someone was lost: three notes going down, left to ring. Mono.</summary>
        public static float[] RenderLost()
        {
            var a = new float[(int)(Rate * 2.2f)];
            int step = Rate * 330 / 1000;
            int[] fall = { 69, 65, 62 };
            for (int i = 0; i < fall.Length; i++)
            {
                Marimba(a, null, i * step, fall[i], 0.28f, 0.5f, wrap: false, decay: 3.2f);
                Bass(a, null, i * step, fall[i] - 24, 0.2f, 3f, wrap: false);
            }
            return Finish(a, null, 0.6f);
        }

        /// <summary>A chapter is over: four strummed chords and the tune's last bar on top. Mono.</summary>
        public static float[] RenderChapter()
        {
            var a = new float[(int)(Rate * 4.2f)];
            var rng = new Random(5);
            int half = Beat / 2;
            int[] order = { 0, 2, 3, 0 }; // C, F, G, C
            for (int c = 0; c < order.Length; c++)
            {
                int at = c * Beat;
                int[] chord = Chords[order[c]];
                for (int i = 0; i < 4; i++) Pluck(a, null, at + i * (Rate * 12 / 1000), chord[i], c == 3 ? 0.17f : 0.12f, 0.5f, c == 3 ? 2.2f : 0.9f, rng, wrap: false);
                Bass(a, null, at, Roots[order[c]], 0.3f, 4f, wrap: false);
            }
            int[] flourish = { 79, 81, 83, 84, 88 };
            for (int i = 0; i < flourish.Length; i++) Marimba(a, null, Beat * 2 + i * (half / 2) + half, flourish[i], 0.24f, 0.5f, wrap: false);
            return Finish(a, null, 0.75f);
        }

        // ------------------------------------------------------------------ instruments

        private static float Hz(int midi) => 440f * MathF.Pow(2f, (midi - 69) / 12f);

        /// <summary>Adds one sample to the mix; a loop's tail wraps round to its start so the join can't be heard.</summary>
        private static void Add(float[] left, float[] right, int index, float value, float pan, bool wrap)
        {
            if (index >= left.Length)
            {
                if (!wrap) return;
                index %= left.Length;
            }
            if (right == null)
            {
                left[index] += value;
                return;
            }
            left[index] += value * (1f - pan) * 1.4f;
            right[index] += value * pan * 1.4f;
        }

        /// <summary>A plucked nylon string (a short burst of noise going round a delay line that dulls it each lap).</summary>
        private static void Pluck(float[] left, float[] right, int start, int midi, float gain, float pan, float seconds, Random rng, bool wrap = true)
        {
            int period = Math.Max(2, (int)MathF.Round(Rate / Hz(midi) - 0.5f)); // the averaging below adds half a sample
            var line = new float[period];
            float last = 0f, mean = 0f;
            for (int i = 0; i < period; i++)
            {
                // Soft fingers, not a pick: the starting noise is already rounded off.
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                last += (n - last) * 0.45f;
                line[i] = last;
                mean += last;
            }
            // No standing offset: it would go round the line with the note and thump when the note is cut.
            mean /= period;
            for (int i = 0; i < period; i++) line[i] -= mean;
            int count = (int)(seconds * Rate);
            float damp = MathF.Pow(0.001f, period / (seconds * 0.8f * Rate)); // gone (-60 dB) a little before the note ends
            for (int i = 0, p = 0; i < count; i++)
            {
                int next = p + 1 == period ? 0 : p + 1;
                float sample = line[p];
                line[p] = (line[p] + line[next]) * 0.5f * damp;
                p = next;
                float fade = i > count - 400 ? (count - i) / 400f : 1f;
                Add(left, right, start + i, sample * gain * fade, pan, wrap);
            }
        }

        /// <summary>A wooden bar struck with a soft mallet: the note and a short ring two octaves up.</summary>
        private static void Marimba(float[] left, float[] right, int start, int midi, float gain, float pan, bool wrap = true, float decay = 6.5f)
        {
            float f = Hz(midi);
            int count = (int)(Rate * Math.Min(1.4f, 5f / decay));
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float body = MathF.Sin(2f * MathF.PI * f * t) * MathF.Exp(-decay * t);
                float ring = MathF.Sin(2f * MathF.PI * f * 4f * t) * MathF.Exp(-decay * 3.5f * t) * 0.3f;
                float attack = Math.Min(1f, t / 0.002f);
                Add(left, right, start + i, (body + ring) * gain * attack, pan, wrap);
            }
        }

        private static void Bass(float[] left, float[] right, int start, int midi, float gain, float decay = 5f, bool wrap = true)
        {
            float f = Hz(midi);
            int count = (int)(Rate * Math.Min(1.2f, 5f / decay));
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float s = MathF.Sin(2f * MathF.PI * f * t) + 0.35f * MathF.Sin(4f * MathF.PI * f * t) + 0.12f * MathF.Sin(6f * MathF.PI * f * t);
                Add(left, right, start + i, s * MathF.Exp(-decay * t) * Math.Min(1f, t / 0.006f) * gain, 0.5f, wrap);
            }
        }

        /// <summary>A shake of seeds: only the hiss of the noise, gone in a twentieth of a second.</summary>
        private static void Shaker(float[] left, float[] right, int start, float gain, Random rng)
        {
            int count = Rate * 70 / 1000;
            float low = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (n - low) * 0.35f;
                Add(left, right, start + i, (n - low) * MathF.Exp(-48f * t) * Math.Min(1f, t / 0.004f) * gain, 0.3f, true);
            }
        }

        /// <summary>A hand drum: a tone dropping from <paramref name="from"/> to <paramref name="to"/> Hz.</summary>
        private static void Drum(float[] left, float[] right, int start, float from, float to, float gain)
        {
            int count = Rate * 300 / 1000;
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float f = to + (from - to) * MathF.Exp(-22f * t);
                phase += 2f * MathF.PI * f / Rate;
                Add(left, right, start + i, MathF.Sin(phase) * MathF.Exp(-15f * t) * Math.Min(1f, t / 0.002f) * gain, 0.5f, true);
            }
        }

        private static void Block(float[] left, float[] right, int start, float gain)
        {
            int count = Rate * 60 / 1000;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate;
                float s = MathF.Sin(2f * MathF.PI * 1180f * t) + 0.5f * MathF.Sin(2f * MathF.PI * 1770f * t);
                Add(left, right, start + i, s * MathF.Exp(-70f * t) * gain, 0.7f, true);
            }
        }

        /// <summary>Brings the loudest moment to <paramref name="peak"/> and returns the buffer Unity wants (interleaved if stereo).</summary>
        private static float[] Finish(float[] left, float[] right, float peak)
        {
            float loudest = 1e-6f;
            for (int i = 0; i < left.Length; i++)
            {
                loudest = Math.Max(loudest, Math.Abs(left[i]));
                if (right != null) loudest = Math.Max(loudest, Math.Abs(right[i]));
            }
            float gain = peak / loudest;
            if (right == null)
            {
                for (int i = 0; i < left.Length; i++) left[i] *= gain;
                return left;
            }
            var mixed = new float[left.Length * 2];
            for (int i = 0; i < left.Length; i++)
            {
                mixed[i * 2] = left[i] * gain;
                mixed[i * 2 + 1] = right[i] * gain;
            }
            return mixed;
        }
    }
}
