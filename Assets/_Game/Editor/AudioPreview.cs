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

