namespace PleaseDontDrown.Rescue
{
    /// <summary>
    /// Fine ─► Distressed ─► Panicking ─► Drowning ─► Unconscious ─► (CPR) ─► Saved,
    /// or Unconscious ─► (condition runs out) ─► Lost. Calming moves back down the chain; reaching the shallows saves.
    /// Injured (shark bite) = awake on land, bleeding: carry them to a hospital bed.
    /// </summary>
    public enum VictimState : byte
    {
        Fine,
        Distressed,
        Panicking,
        Drowning,
        Unconscious,
        Saved,
        Lost,
        Injured
    }

    /// <summary>What the next CPR press does (story CPR: compressions, then breaths for women / a punch for men).</summary>
    public enum CprStep : byte { Compress, Breath, Punch }

    public static class VictimStateExtensions
    {
        /// <summary>In trouble (in the water or not, conscious or not): shows a HUD marker, the rescue clock runs.</summary>
        public static bool NeedsHelp(this VictimState s) => (s >= VictimState.Distressed && s <= VictimState.Unconscious) || s == VictimState.Injured;

        public static bool IsConscious(this VictimState s) => s != VictimState.Unconscious && s != VictimState.Lost;

        /// <summary>Awake and struggling in the water (swims, splashes, calls for help).</summary>
        public static bool IsStruggling(this VictimState s) => s >= VictimState.Distressed && s <= VictimState.Drowning;

        public static string Label(this VictimState s) => s switch
        {
            VictimState.Fine => "fine",
            VictimState.Distressed => "needs help",
            VictimState.Panicking => "PANICKING",
            VictimState.Drowning => "DROWNING",
            VictimState.Unconscious => "UNCONSCIOUS",
            VictimState.Saved => "saved",
            VictimState.Injured => "BLEEDING",
            _ => "lost"
        };
    }
}
