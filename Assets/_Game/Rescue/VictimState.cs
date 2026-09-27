namespace PleaseDontDrown.Rescue
{
    /// <summary>
    /// Fine ─► Distressed ─► Panicking ─► Drowning ─► Unconscious ─► (CPR) ─► Saved,
    /// or Unconscious ─► (condition runs out) ─► Lost. Calming moves back down the chain; reaching the shallows saves.
    /// </summary>
    public enum VictimState : byte
    {
        Fine,
        Distressed,
        Panicking,
        Drowning,
        Unconscious,
        Saved,
        Lost
    }

    public static class VictimStateExtensions
    {
        /// <summary>In the water and in trouble (conscious or not): shows a HUD marker, the rescue clock runs.</summary>
        public static bool NeedsHelp(this VictimState s) => s >= VictimState.Distressed && s <= VictimState.Unconscious;

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
            _ => "lost"
        };
    }
}
