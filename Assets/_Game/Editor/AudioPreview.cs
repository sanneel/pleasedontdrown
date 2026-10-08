using System;
using System.IO;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.World;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>Exports the runtime sounds as WAVs for listening and checks for silent/broken clips.</summary>
    public static class AudioPreview
    {
        /// <summary>Build the current scene for listening without regenerating authored scenes or prefabs.</summary>
        [MenuItem("Tools/PDD/Build audio review player")]
        public static void BuildReviewBatch()
        {
            const string stampPath = "Assets/_Game/Resources/BuildStamp.txt";
            byte[] originalStamp = File.ReadAllBytes(stampPath);
            bool failed = false;
            try
            {
                // Review players must not handshake with an older build that has different prefab IDs.
                File.WriteAllText(stampPath, "audio-" + Guid.NewGuid().ToString("N"));
                AssetDatabase.ImportAsset(stampPath);
                const string output = "Builds/AudioReview/PleaseDontDrown.exe";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/_Game/Scenes/Game.unity" },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development
                });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                    throw new InvalidOperationException("Audio review build failed: " + report.summary.result);
                File.Copy("steam_appid.txt", "Builds/AudioReview/steam_appid.txt", true);
                Debug.Log("[AudioPreview] BUILD PASS: " + Path.GetFullPath(output));
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioPreview] BUILD FAILED: " + e);
                failed = true;
            }
            finally
            {
                File.WriteAllBytes(stampPath, originalStamp);
                AssetDatabase.ImportAsset(stampPath);
            }
            if (failed && Application.isBatchMode) EditorApplication.Exit(1);
        }

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
                Export(dir, "water-submerge", BeachAudio.Dive);
                Export(dir, "water-surface", BeachAudio.Emerge);
                for (int variant = 0; variant < 3; variant++)
                {
                    Export(dir, "swim-" + variant, BeachAudio.SwimStroke);
                    Export(dir, "wade-" + variant, BeachAudio.Wade);
                    Export(dir, "pickup-" + variant, BeachAudio.Pickup);
                }
                for (int size = 0; size < 3; size++)
                {
                    var surface = (SurfaceKind)size;
                    for (int variant = 0; variant < 3; variant++) Export(dir, $"step-{surface}-{variant}", BeachAudio.Footstep(surface));
                    for (int variant = 0; variant < 3; variant++) Export(dir, $"jump-{surface}-{variant}", BeachAudio.Jump(surface));
                    for (int variant = 0; variant < 3; variant++) Export(dir, $"land-{surface}-{variant}", BeachAudio.Land(surface));
                }
                int waterCount = 0;
                foreach (var clip in Audio.WaterFoley.AllClips())
                {
                    Export(dir, clip.name, clip);
                    waterCount++;
                }
                if (waterCount != 20) throw new InvalidOperationException("Missing recorded water clips");
                (string name, AudioClip clip)[] interactions =
                {
                    ("bell", ProceduralAudio.Bell), ("switch", ProceduralAudio.Click),
                    ("cough", ProceduralAudio.Cough), ("compression", ProceduralAudio.Thump),
                    ("crunch", ProceduralAudio.Crunch), ("palm-rustle", ProceduralAudio.Rustle),
                    ("coconut", ProceduralAudio.Bonk), ("door-open", ProceduralAudio.Creak),
                    ("door-close", ProceduralAudio.Shut), ("breath", ProceduralAudio.Breath),
                    ("rescue-breath", ProceduralAudio.Kiss), ("defibrillator", ProceduralAudio.Zap),
                    ("punch", ProceduralAudio.Punch), ("cash", ProceduralAudio.Cash),
                    ("engine", ProceduralAudio.Engine), ("knife-swish", ProceduralAudio.KnifeSwish),
                    ("stab", ProceduralAudio.Stab), ("dry-fire", ProceduralAudio.DryFire),
                    ("aim-in", ProceduralAudio.AimIn), ("aim-out", ProceduralAudio.AimOut),
                    ("bullet-impact", ProceduralAudio.BulletImpact), ("legacy-splash", ProceduralAudio.Splash)
                };
                foreach (var entry in interactions) Export(dir, entry.name, entry.clip);
                for (int register = 0; register < Audio.SpeechSynth.Registers; register++)
                {
                    Export(dir, "cry-" + register, ProceduralAudio.Cry(register));
                    var line = Audio.SpeechSynth.Line("Hey! Welcome to the beach. Are you ready?", register);
                    try { Export(dir, "dialogue-" + register, line); }
                    finally { UnityEngine.Object.DestroyImmediate(line); }
                    for (int vowel = 0; vowel < Audio.SpeechSynth.Vowels; vowel++)
                        for (int hard = 0; hard < 2; hard++)
                            Export(dir, $"syllable-{register}-{vowel}-{hard}", Audio.SpeechSynth.Syllable(register, vowel, hard == 1));
                }
                WriteWaterReview(dir);
                Debug.Log("[AudioPreview] PASS: effects, movement, 20 recorded water clips and all four voices: " + dir);
            }
            catch (Exception e)
            {
                Debug.LogError("[AudioPreview] FAILED: " + e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void WriteWaterReview(string dir)
        {
            var page = new System.Text.StringBuilder();
            page.Append("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
            page.Append("<title>Water sounds - How to Fish</title><style>body{background:#10282e;color:#eff7ec;font:17px system-ui;margin:0 auto;padding:36px 22px;max-width:1100px}h1{font-size:40px;margin-bottom:10px}p{color:#b6d2ce;line-height:1.6}h2{margin-top:36px;color:#f2cd77}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(290px,1fr));gap:14px}.card{padding:18px;border:1px solid #31535a;border-radius:16px;background:#18363d}audio{width:100%;margin-top:14px}</style>");
            page.Append("<h1>Water sounds</h1><p>Replaced with recordings from How to Fish.<br>Swimming, wading, splashes, diving, surfacing and sea ambience. In-game volume and distance are applied separately.</p>");
            void Group(string title) => page.Append("<h2>" + title + "</h2><div class='grid'>");
            void Clip(string name, string label)
            {
                string data = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(dir, name + ".wav")));
                page.Append("<div class='card'><strong>" + label + "</strong><audio controls preload='none' src='data:audio/wav;base64," + data + "'></audio></div>");
            }
            Group("Swimming and wading");
            for (int i = 1; i <= 5; i++) Clip("Footstep_Water_V" + i, "Water stroke " + i);
            page.Append("</div>");
            Group("Splashes");
            foreach (string size in new[] { "Light", "Medium", "Heavy" })
                for (int i = 1; i <= 3; i++) Clip("ItemHitWater" + size + "_V" + i, size + " splash " + i);
            page.Append("</div>");
            Group("Diving and surfacing");
            for (int i = 1; i <= 2; i++)
            {
                Clip("UnderwaterOnEnter_" + i.ToString("00"), "Dive under " + i);
                Clip("UnderwaterOnExit_" + i.ToString("00"), "Surface " + i);
            }
            page.Append("</div>");
            Group("Ambience");
            Clip("SeaAmbient_Loop_Mono", "Sea ambience");
            Clip("UnderwaterLoop", "Underwater ambience");
            page.Append("</div><script>document.addEventListener('play',e=>{if(e.target.tagName==='AUDIO')document.querySelectorAll('audio').forEach(a=>{if(a!==e.target)a.pause()})},true)</script></html>");
            File.WriteAllText(Path.Combine(dir, "index.html"), page.ToString());
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
            var clip = Audio.SpeechSynth.Line(text, register);
            try
            {
                var samples = new float[clip.samples];
                clip.GetData(samples, 0);
                return samples;
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
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
            bool loop = clip.name.Contains("Loop");
            if (!loop && (Mathf.Abs(samples[0]) > 0.001f || Mathf.Abs(samples[samples.Length - 1]) > 0.001f))
                throw new InvalidOperationException(name + " has an abrupt edge");
            if (loop && Mathf.Abs(samples[0] - samples[samples.Length - 1]) > 0.025f)
                throw new InvalidOperationException(name + " has an abrupt loop seam");

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

