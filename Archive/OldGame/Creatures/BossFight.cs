using System;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// The boss framework (host), plan C7. A boss's fight is a few phases set by its health (e.g. 100-60%, 60-30%,
    /// 30-0%); each attack is telegraphed first (a pose, a sound: the players' cue to move), and some attacks leave the
    /// boss open afterwards: the stun window where hits count (<see cref="Vulnerable"/>). Health scales with the number
    /// of lifeguards. Everyone sees the boss bar (name, health, phase, "open!") through the boss creature's SyncVars.
    /// </summary>
    public class BossFight
    {
        private readonly float[] _phaseStarts; // health fraction each phase starts at, high to low: { 1, 0.6, 0.3 }
        private float _vulnerableUntil = -1f;

        public int Phase { get; private set; } = 1;
        public int PhaseCount => _phaseStarts.Length;
        public bool Vulnerable => Time.time < _vulnerableUntil;
        /// <summary>Host: the fight moved to a new phase (1-based).</summary>
        public event Action<int> PhaseChanged;

        public BossFight(params float[] phaseStarts)
        {
            _phaseStarts = phaseStarts.Length > 0 ? phaseStarts : new[] { 1f };
        }

        /// <summary>Host: health went down to <paramref name="health01"/>: a new phase?</summary>
        public void UpdatePhase(float health01)
        {
            int phase = 1;
            for (int i = 1; i < _phaseStarts.Length; i++)
                if (health01 <= _phaseStarts[i]) phase = i + 1;
            if (phase <= Phase) return;
            Phase = phase;
            Debug.Log($"[Boss] phase {Phase}");
            PhaseChanged?.Invoke(Phase);
        }

        /// <summary>Host: open for <paramref name="seconds"/> (the stun window).</summary>
        public void OpenFor(float seconds) => _vulnerableUntil = Time.time + seconds;
        public void Close() => _vulnerableUntil = -1f;

        /// <summary>Picks per phase: phase 1 gets [0], phase 2 [1]... (the last for any later phase).</summary>
        public T ByPhase<T>(params T[] values) => values[Mathf.Clamp(Phase - 1, 0, values.Length - 1)];

        /// <summary>Health for this many lifeguards: +60% for each beyond the first.</summary>
        public static int Scaled(int baseHealth)
        {
            int players = Mathf.Max(1, PlayerHub.All.Count);
            return Mathf.RoundToInt(baseHealth * (1f + 0.6f * (players - 1)));
        }
    }
}
