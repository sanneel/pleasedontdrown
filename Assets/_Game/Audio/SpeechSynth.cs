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
        private const float Seconds = 0.13f;
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
            vowel = Mathf.Clamp(vowel, 0, Vowels - 1);
            int index = (register * Vowels + vowel) * 2 + (hard ? 1 : 0);
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
                gains[h] = (Mathf.Exp(-Sq((f - f1) / 220f)) + 0.4f * Mathf.Exp(-Sq((f - f2) / 300f)) + 0.08f * Mathf.Exp(-Sq((f - f3) / 380f))) / Mathf.Sqrt(h) + 0.16f / (h * h);
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
                if (hard && t < 0.035f) sample += breath * 0.12f * Mathf.Sin(Mathf.PI * t / 0.035f);
                if (t >= onset)
                {
                    float v = t - onset;
                    // The pitch drops a little through the syllable, like speech does.
                    phase += 2f * Mathf.PI * f0 * (1.025f - 0.05f * u + 0.003f * Mathf.Sin(t * 117f)) / Rate;
                    float voiced = 0f;
                    for (int h = 1; h <= harmonics; h++) voiced += gains[h] * Mathf.Sin(phase * h);
                    float envelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(v / (Seconds - onset)));
                    sample += (voiced * 0.3f + breath * 0.085f) * envelope;
                }
                data[i] = sample;
                loudest = Mathf.Max(loudest, Mathf.Abs(sample));
            }
            // Every voice and vowel equally loud (a deep voice has more harmonics under the same resonances).
            float level = 0.36f / Mathf.Max(loudest, 1e-4f);
            for (int i = 0; i < count; i++) data[i] *= level;
            return SoundClip.Create($"Syllable{register}_{vowel}{(hard ? "h" : "")}", data);
        }

        /// <summary>Pitch and timing belong to each syllable, never to overlapping sources.</summary>
        public static AudioClip Line(string text, int register, float pitch = 1f)
        {
            text = System.Text.RegularExpressions.Regex.Replace(text ?? string.Empty, "<[^>]*>", string.Empty);
            pitch = Mathf.Clamp(pitch, 0.65f, 1.5f);
            bool question = text.TrimEnd().EndsWith("?");
            var samples = new System.Collections.Generic.List<float>();
            float at = 0.035f, voiceEnd = 0f;
            bool consonant = false, previousVowel = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int vowel = VowelOf(c);
                if (vowel >= 0 && !previousVowel)
                {
                    at = Mathf.Max(at, voiceEnd - 0.008f);
                    float variation = 1f + ((i * 13 + char.ToLowerInvariant(c) * 7) % 7 - 3) * 0.009f;
                    float progress = i / (float)Mathf.Max(1, text.Length - 1);
                    float contour = question ? Mathf.Lerp(0.98f, 1.07f, progress * progress) : Mathf.Lerp(1.025f, 0.96f, progress);
                    float rate = pitch * variation * contour;
                    AudioClip syllable = Syllable(register, vowel, consonant);
                    var source = new float[syllable.samples];
                    syllable.GetData(source, 0);
                    int start = Mathf.RoundToInt(at * Rate), count = Mathf.CeilToInt(source.Length / rate);
                    while (samples.Count < start + count) samples.Add(0f);
                    float gain = 0.77f + ((i * 17) % 5) * 0.025f;
                    for (int s = 0; s < count; s++)
                    {
                        float pos = s * rate;
                        int a = Mathf.Min((int)pos, source.Length - 1), b = Mathf.Min(a + 1, source.Length - 1);
                        samples[start + s] += Mathf.Lerp(source[a], source[b], pos - a) * gain;
                    }
                    voiceEnd = at + count / (float)Rate;
                }
                if (char.IsLetter(c))
                {
                    consonant = vowel < 0;
                    previousVowel = vowel >= 0;
                    at += 0.043f + (i % 3) * 0.004f;
                }
                else
                {
                    consonant = previousVowel = false;
                    if (c == '\'' || c == '\u2019') continue;
                    at = Mathf.Max(at, voiceEnd) + (c is '.' or '!' or '?' ? 0.23f : c is ',' or ';' or ':' ? 0.13f : 0.05f);
                }
            }
            while (samples.Count < Mathf.CeilToInt((Mathf.Max(at, voiceEnd) + 0.04f) * Rate)) samples.Add(0f);
            return SoundClip.Create("Dialogue" + register, samples.ToArray());
        }

        private static float Sq(float x) => x * x;
    }
}
