using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// Graphics level for the machine: High (the full look), Medium, Low (for weak/integrated GPUs: 75% resolution
    /// upscaled with FSR, half-size textures, one short shadow cascade, no SSAO, small things vanish sooner).
    /// Each level is a Quality level with its own URP asset (Assets/Settings/PC_*RPAsset). Picked at start from
    /// <c>-pdd-quality low|medium|high</c>, else what the player chose last time (console <c>quality</c>), else from
    /// the hardware. Console: <c>quality [low|medium|high|auto]</c>.
    /// </summary>
    public static class GraphicsQuality
    {
        public enum Tier { Low, Medium, High }

        private const string PrefKey = "pdd.quality";
        private static readonly string[] LevelNames = { "Low", "Medium", "High" };

        public static Tier Current { get; private set; } = Tier.High;

        /// <summary>Called after the level changes (and once at start).</summary>
        public static event Action Changed;

        /// <summary>Edge smoothing for game cameras: SMAA on High, the much cheaper FXAA below.</summary>
        public static AntialiasingMode Antialiasing => Current == Tier.High
            ? AntialiasingMode.SubpixelMorphologicalAntiAliasing
            : AntialiasingMode.FastApproximateAntialiasing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtStart()
        {
            Tier? chosen = FromArgs();
            if (chosen == null && PlayerPrefs.HasKey(PrefKey)) chosen = Parse(PlayerPrefs.GetString(PrefKey));
            if (chosen == null)
            {
                chosen = Detect(out string why);
                Debug.Log($"[Quality] {chosen} for this machine ({why})");
            }
            Set(chosen.Value, save: false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterCommand() =>
            DevCommands.Register("quality", "[low|medium|high|auto]", "Graphics level (auto: pick for this machine). No argument: show it.", args =>
            {
                if (args.Length == 0)
                {
                    DevCommands.Print($"quality {Current} (this machine would get {Detect(out string why)}: {why})");
                    return;
                }
                if (args[0] == "auto")
                {
                    PlayerPrefs.DeleteKey(PrefKey);
                    Set(Detect(out _), save: false);
                }
                else if (Parse(args[0]) is Tier tier) Set(tier, save: true);
                else { DevCommands.Print("quality low|medium|high|auto"); return; }
                DevCommands.Print($"quality {Current}");
            });

        public static void Set(Tier tier, bool save)
        {
            int level = Array.IndexOf(QualitySettings.names, LevelNames[(int)tier]);
            if (level < 0)
            {
                Debug.LogWarning($"[Quality] no quality level named {LevelNames[(int)tier]}");
                return;
            }
            Current = tier;
            if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
            if (save)
            {
                PlayerPrefs.SetString(PrefKey, LevelNames[(int)tier]);
                PlayerPrefs.Save();
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Integrated or small GPUs, little memory or few cores: Low. Mid-range: Medium. The rest: High.
        /// (Batch-mode test runs keep High so their numbers stay comparable.)
        /// </summary>
        public static Tier Detect(out string why)
        {
            int vram = SystemInfo.graphicsMemorySize, ram = SystemInfo.systemMemorySize, cores = SystemInfo.processorCount;
            string gpu = SystemInfo.graphicsDeviceName ?? "";
            why = $"{gpu}, {vram} MB video, {ram} MB RAM, {cores} threads";
            if (Application.isBatchMode) return Tier.High;
            bool integrated = gpu.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0
                              && gpu.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) < 0
                              || gpu.IndexOf("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase) >= 0
                              || gpu.IndexOf("Vega", StringComparison.OrdinalIgnoreCase) >= 0
                              || gpu.IndexOf("Microsoft Basic", StringComparison.OrdinalIgnoreCase) >= 0;
            if (integrated || vram < 2500 || ram < 7000 || cores <= 2 || SystemInfo.graphicsShaderLevel < 45) return Tier.Low;
            if (vram < 5000 || ram < 12000 || cores <= 4) return Tier.Medium;
            return Tier.High;
        }

        private static Tier? FromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(args, "-pdd-quality");
            return at >= 0 && at + 1 < args.Length ? Parse(args[at + 1]) : null;
        }

        private static Tier? Parse(string text) => text?.ToLowerInvariant() switch
        {
            "low" => Tier.Low,
            "medium" => Tier.Medium,
            "high" => Tier.High,
            _ => null,
        };
    }
}
