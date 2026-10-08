using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>Recorded How to Fish water bank. Source mapping is in Docs/WaterAudioSources.json.</summary>
    public static class WaterFoley
    {
        public const int StepVariants = 5;
        private static readonly Dictionary<string, AudioClip> Clips = new();
        private static readonly int[] LastImpact = new int[3];
        private static int _lastStep, _lastStroke, _lastEnter, _lastExit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Clips.Clear();
            System.Array.Clear(LastImpact, 0, LastImpact.Length);
            _lastStep = _lastStroke = _lastEnter = _lastExit = 0;
        }

        public static AudioClip Impact(float strength)
        {
            int size = strength < .3f ? 0 : strength < .72f ? 1 : 2;
            string category = size == 0 ? "Light" : size == 1 ? "Medium" : "Heavy";
            return Load("ItemHitWater" + category + "_V" + Next(ref LastImpact[size], 3));
        }

        public static AudioClip Step => Load("Footstep_Water_V" + Next(ref _lastStep, StepVariants));
        public static AudioClip Stroke => Load("Footstep_Water_V" + Next(ref _lastStroke, StepVariants));
        public static AudioClip Enter => Load("UnderwaterOnEnter_" + Next(ref _lastEnter, 2).ToString("00"));
        public static AudioClip Exit => Load("UnderwaterOnExit_" + Next(ref _lastExit, 2).ToString("00"));
        public static AudioClip SeaLoop => Load("SeaAmbient_Loop_Mono");
        public static AudioClip UnderwaterLoop => Load("UnderwaterLoop");

        private static int Next(ref int previous, int count)
        {
            int choice = previous == 0 ? Random.Range(1, count + 1) : Random.Range(1, count);
            if (previous != 0 && choice >= previous) choice++;
            previous = choice;
            return choice;
        }

        private static AudioClip Load(string name)
        {
            if (Clips.TryGetValue(name, out var clip) && clip != null) return clip;
            clip = Resources.Load<AudioClip>("Audio/Water/" + name);
            if (clip == null) throw new System.InvalidOperationException("Missing recorded water clip: " + name);
            Clips[name] = clip;
            return clip;
        }

        /// <summary>Deterministic enumeration lets the preview validate every asset, including rare variants.</summary>
        public static IEnumerable<AudioClip> AllClips()
        {
            for (int i = 1; i <= StepVariants; i++) yield return Load("Footstep_Water_V" + i);
            foreach (string size in new[] { "Light", "Medium", "Heavy" })
                for (int i = 1; i <= 3; i++) yield return Load("ItemHitWater" + size + "_V" + i);
            for (int i = 1; i <= 2; i++)
            {
                yield return Load("UnderwaterOnEnter_" + i.ToString("00"));
                yield return Load("UnderwaterOnExit_" + i.ToString("00"));
            }
            yield return SeaLoop;
            yield return UnderwaterLoop;
        }
    }
}
