using System;
using System.IO;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>Exports the original runtime sounds as WAVs for listening and checks for silent/broken clips.</summary>
    public static class AudioPreview
    {
        [MenuItem("Tools/PDD/Export audio preview")]
        public static void ExportBatch()
        {
            try
            {
                string dir = Path.GetFullPath("Screenshots/Review/Audio");
                Directory.CreateDirectory(dir);
                Export(dir, "footstep-sand", BeachAudio.Footstep(SurfaceKind.Sand));
                Export(dir, "footstep-wood", BeachAudio.Footstep(SurfaceKind.Wood));
                Export(dir, "footstep-rock", BeachAudio.Footstep(SurfaceKind.Rock));
                Export(dir, "water-light", BeachAudio.WaterImpact(0.15f));
                Export(dir, "water-medium", BeachAudio.WaterImpact(0.5f));
                Export(dir, "water-heavy", BeachAudio.WaterImpact(1f));
                Export(dir, "swim", BeachAudio.SwimStroke);
                Export(dir, "pickup", BeachAudio.Pickup);
                Export(dir, "drop", BeachAudio.Drop);
                Export(dir, "throw", BeachAudio.Throw);
                Export(dir, "equip", BeachAudio.Equip);
                Export(dir, "punch-swoosh", BeachAudio.PunchSwoosh);
                for (int i = 0; i < 3; i++)
                {
                    Export(dir, $"coconut-bite-{i}", Audio.ActionFoley.Bite);
                    Export(dir, $"drink-glug-{i}", Audio.ActionFoley.Gulp);
                    Export(dir, $"air-punch-{i}", Audio.ActionFoley.Punch);
                    Export(dir, $"air-throw-{i}", Audio.ActionFoley.Throw);
                    Export(dir, $"air-blade-{i}", Audio.ActionFoley.Blade);
                }
                Export(dir, "menu-hover", BeachAudio.MenuHover);
                Export(dir, "menu-select", BeachAudio.MenuSelect);
                Export(dir, "bird", BeachAudio.Bird);
                Export(dir, "surf-loop", BeachAudio.Surf);
                Export(dir, "gun-pistol", ProceduralAudio.Shot(GunSound.Pistol));
                Export(dir, "gun-rifle", ProceduralAudio.Shot(GunSound.Rifle));
                Export(dir, "gun-smg", ProceduralAudio.Shot(GunSound.Smg));
                Export(dir, "gun-shotgun", ProceduralAudio.Shot(GunSound.Shotgun));
                Export(dir, "gun-sniper", ProceduralAudio.Shot(GunSound.Sniper));
                Export(dir, "gun-suppressed", ProceduralAudio.GunshotSuppressed);
                Export(dir, "gun-mag-out", ProceduralAudio.MagOut);
                Export(dir, "gun-mag-in", ProceduralAudio.MagIn);
                Export(dir, "gun-rack", ProceduralAudio.Rack);
                Debug.Log("[AudioPreview] Exported 25 sound previews to " + dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioPreview] FAILED: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>The music loops, the jingles and a spoken line per kind of voice, as WAVs to listen to.</summary>
        [MenuItem("Tools/PDD/Export music and voice preview")]
        public static void ExportMusicBatch()
        {
            try
            {
                string dir = Path.GetFullPath("Screenshots/Review/Audio");
                Directory.CreateDirectory(dir);
                float[] calm = Audio.MusicSynth.RenderCalm(), danger = Audio.MusicSynth.RenderDanger();
                WriteWav(dir, "music-calm", calm, 2, Audio.MusicSynth.Rate);
                WriteWav(dir, "music-danger-layer", danger, 2, Audio.MusicSynth.Rate);
                var both = new float[calm.Length];
                for (int i = 0; i < both.Length; i++) both[i] = calm[i] * 0.35f + danger[i] * 0.6f; // as mixed in game with someone drowning
                WriteWav(dir, "music-someone-drowning", both, 2, Audio.MusicSynth.Rate);
                WriteWav(dir, "jingle-saved", Audio.MusicSynth.RenderSaved(), 1, Audio.MusicSynth.Rate);
                WriteWav(dir, "jingle-lost", Audio.MusicSynth.RenderLost(), 1, Audio.MusicSynth.Rate);
                WriteWav(dir, "jingle-chapter", Audio.MusicSynth.RenderChapter(), 1, Audio.MusicSynth.Rate);
                string[] who = { "deep", "man", "woman", "high" };
                for (int register = 0; register < Audio.SpeechSynth.Registers; register++)
                    WriteWav(dir, "voice-" + who[register], Spoken("Hey! You must be the new lifeguard. Welcome to the island, are you ready?", register), 1, 44100);
                Debug.Log("[AudioPreview] Exported music and voice previews to " + dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioPreview] FAILED: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        /// <summary>A line as <see cref="Audio.SpeechVoice"/> says it (same letter timing, without each speaker's pitch wobble).</summary>
        private static float[] Spoken(string text, int register)
        {
            const int rate = 44100;
            var mix = new float[(int)(rate * (text.Length * 0.08f + 1f))];
            float at = 0.05f;
            bool afterConsonant = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int vowel = Audio.SpeechSynth.VowelOf(c);
                if (vowel >= 0)
                {
                    if (i == 0 || Audio.SpeechSynth.VowelOf(text[i - 1]) < 0)
                    {
                        AudioClip clip = Audio.SpeechSynth.Syllable(register, vowel, afterConsonant);
                        var data = new float[clip.samples];
                        clip.GetData(data, 0);
                        int start = (int)(at * rate);
                        for (int s = 0; s < data.Length && start + s < mix.Length; s++) mix[start + s] += data[s] * 0.8f;
                    }
                    afterConsonant = false;
                    at += 0.058f;
                }
                else if (char.IsLetter(c))
                {
                    afterConsonant = true;
                    at += 0.058f;
                }
                else
                {
                    afterConsonant = false;
                    at += c is '.' or '!' or '?' ? 0.3f : c == ',' ? 0.17f : 0.045f;
                }
            }
            return mix;
        }

        private static void WriteWav(string dir, string name, float[] samples, int channels, int rate)
        {
            double energy = 0;
            float peak = 0f;
            foreach (float sample in samples)
            {
                if (float.IsNaN(sample) || float.IsInfinity(sample)) throw new InvalidOperationException(name + " has invalid samples");
                energy += sample * sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            float rms = Mathf.Sqrt((float)(energy / samples.Length));
            using var stream = File.Create(Path.Combine(dir, name + ".wav"));
            using var writer = new BinaryWriter(stream);
            int dataBytes = samples.Length * 2;
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * 2 * channels);
            writer.Write((short)(2 * channels));
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);
            foreach (float sample in samples)
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * 32767f));
            Debug.Log($"[AudioPreview] {name}: {samples.Length / (float)(rate * channels):F2}s, RMS {rms:F3}, peak {peak:F3}");
        }

        private static void Export(string dir, string name, AudioClip clip)
        {
            if (clip == null || clip.samples <= 0 || clip.frequency <= 0 || clip.channels != 1)
                throw new InvalidOperationException(name + " is not a usable mono clip");
            float[] samples = new float[clip.samples];
            if (!clip.GetData(samples, 0)) throw new InvalidOperationException(name + " cannot be read");
            double energy = 0;
            float peak = 0f;
            foreach (float sample in samples)
            {
                if (float.IsNaN(sample) || float.IsInfinity(sample))
                    throw new InvalidOperationException(name + " has invalid samples");
                energy += sample * sample;
                peak = Mathf.Max(peak, Mathf.Abs(sample));
            }
            float rms = Mathf.Sqrt((float)(energy / samples.Length));
            if (rms < 0.002f || peak > 0.99f)
                throw new InvalidOperationException(name + $" has invalid level (RMS {rms:F3}, peak {peak:F3})");

            using var stream = File.Create(Path.Combine(dir, name + ".wav"));
            using var writer = new BinaryWriter(stream);
            int dataBytes = samples.Length * 2;
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E' });
            writer.Write(new[] { 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(clip.frequency);
            writer.Write(clip.frequency * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);
            foreach (float sample in samples)
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1f, 1f) * 32767f));
            Debug.Log($"[AudioPreview] {name}: {clip.length:F2}s, RMS {rms:F3}, peak {peak:F3}");
        }
    }
}

