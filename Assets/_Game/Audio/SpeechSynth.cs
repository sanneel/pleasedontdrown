using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>
    /// The sounds people talk with: short spoken syllables ("ba", "e", "do"...) synthesized once per kind of voice.
    /// A line of dialogue is played as a string of these, one per vowel of its words (<see cref="SpeechVoice"/>), so
    /// everyone has a voice without a single recorded line. No real words: the subtitles carry the meaning.
    /// </summary>
    public static class SpeechSynth
    {
        /// <summary>Four kinds of voice: two men's (deep, medium), two women's (medium, high).</summary>
        public const int Registers = 4;
        public const int Vowels = 5;

        private const int Rate = 44100;
        private const float Seconds = 0.15f;
        private static readonly float[] Pitch = { 104f, 132f, 198f, 232f };
        // The first two resonances of a, e, i, o, u (what makes each vowel sound like itself).
        private static readonly float[] F1 = { 780f, 520f, 310f, 480f, 340f };
        private static readonly float[] F2 = { 1250f, 1850f, 2250f, 900f, 820f };

        private static AudioClip[] _clips;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _clips = null;

        /// <summary>The vowel a letter stands for (0..4), or -1.</summary>
        public static int VowelOf(char c) => c switch
        {
            'a' or 'A' => 0, 'e' or 'E' => 1, 'i' or 'I' or 'y' or 'Y' => 2, 'o' or 'O' => 3, 'u' or 'U' => 4, _ => -1
        };

        /// <summary>One syllable; <paramref name="hard"/> starts it with a consonant's puff of air.</summary>
        public static AudioClip Syllable(int register, int vowel, bool hard)
        {
            _clips ??= new AudioClip[Registers * Vowels * 2];
            register = Mathf.Clamp(register, 0, Registers - 1);
            int index = (register * Vowels + Mathf.Clamp(vowel, 0, Vowels - 1)) * 2 + (hard ? 1 : 0);
            if (_clips[index] != null) return _clips[index]; // explicit: Unity can unload a clip behind a "??="
            return _clips[index] = Build(register, vowel, hard);
        }

        private static AudioClip Build(int register, int vowel, bool hard)
        {
            int count = Mathf.CeilToInt(Seconds * Rate);
            var data = new float[count];
            float f0 = Pitch[register];
            // Women's resonances sit a little higher than men's.
            float shift = register >= 2 ? 1.16f : 1f;
            float f1 = F1[vowel] * shift, f2 = F2[vowel] * shift, f3 = 2700f * shift;
            int harmonics = Mathf.Min(30, Mathf.FloorToInt(4200f / f0));
            var gains = new float[harmonics + 1];
            for (int h = 1; h <= harmonics; h++)
            {
                float f = f0 * h;
                gains[h] = Mathf.Exp(-Sq((f - f1) / 150f)) + 0.55f * Mathf.Exp(-Sq((f - f2) / 230f)) + 0.15f * Mathf.Exp(-Sq((f - f3) / 300f)) + 0.04f / h;
            }
            var rng = new System.Random(4000 + register * 31 + vowel * 7 + (hard ? 1 : 0));
            float phase = 0f, breath = 0f, loudest = 0f;
            float onset = hard ? 0.028f : 0f; // the voice starts after the consonant
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)Rate, u = t / Seconds;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                breath += (n - breath) * 0.4f;
                float sample = 0f;
                if (hard && t < 0.04f) sample += (n - breath) * 0.5f * Mathf.Sin(Mathf.PI * t / 0.04f);
                if (t >= onset)
                {
                    float v = t - onset;
                    // The pitch drops a little through the syllable, like speech does.
                    phase += 2f * Mathf.PI * f0 * (1.05f - 0.1f * u) / Rate;
                    float voiced = 0f;
                    for (int h = 1; h <= harmonics; h++) voiced += gains[h] * Mathf.Sin(phase * h);
                    float envelope = Mathf.Clamp01(v / 0.012f) * Mathf.Clamp01((Seconds - t) / 0.05f);
                    sample += (voiced * 0.26f + breath * 0.05f) * envelope;
                }
                data[i] = sample;
                loudest = Mathf.Max(loudest, Mathf.Abs(sample));
            }
            // Every voice and vowel equally loud (a deep voice has more harmonics under the same resonances).
            float level = 0.6f / Mathf.Max(loudest, 1e-4f);
            for (int i = 0; i < count; i++) data[i] *= level;
            AudioClip clip = AudioClip.Create($"Syllable{register}_{vowel}{(hard ? "h" : "")}", count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sq(float x) => x * x;
    }
}
