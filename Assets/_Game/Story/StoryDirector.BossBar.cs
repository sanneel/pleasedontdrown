using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Boss fights presented the way How to Fish does it: a boss doesn't just turn up, it is introduced (a title card
    /// with its name, a low sting; the drums play while it lasts) and a bar at the bottom shows how it's going. Creature
    /// bosses have their own bar (Creature.ActiveBoss); a mission run like a boss fight with nothing to shoot (Skull
    /// Cove's lookouts) has this one: its name, a bar that empties as the team closes in, and a clock ring (the last 10
    /// seconds counted down; out of time = the mission fails and starts again).
    /// </summary>
    public partial class StoryDirector
    {
        private readonly SyncVar<string> _bossBarTitle = new SyncVar<string>();
        private readonly SyncVar<float> _bossBar01 = new SyncVar<float>();
        private readonly SyncVar<float> _bossBarTimeLeft = new SyncVar<float>();
        private readonly SyncVar<float> _bossBarTimeTotal = new SyncVar<float>();

        /// <summary>Every machine: a mission's boss bar is up (see <see cref="ServerBossBar"/>).</summary>
        public static bool BossBarShown => Instance != null && !string.IsNullOrEmpty(Instance._bossBarTitle.Value);
        public static string BossBarTitle => Instance != null ? Instance._bossBarTitle.Value : null;
        public static float BossBar01 => Instance != null ? Instance._bossBar01.Value : 0f;
        /// <summary>Seconds left on the mission's clock and its full length (0: no clock).</summary>
        public static float BossBarTimeLeft => Instance != null ? Instance._bossBarTimeLeft.Value : 0f;
        public static float BossBarTimeTotal => Instance != null ? Instance._bossBarTimeTotal.Value : 0f;

        /// <summary>Everyone: a boss arrives. Its name big in the middle, the boss sting.</summary>
        [ObserversRpc]
        private void BossIntroObservers(string title, string subtitle)
        {
            StoryHud.ShowTitle(title, subtitle, boss: true);
            Audio.GameMusic.Play(Audio.Jingle.Boss);
        }

        /// <summary>Host: show/update the mission's boss bar.</summary>
        private void ServerBossBar(string title, float value01, float timeLeft = 0f, float timeTotal = 0f)
        {
            if (!IsServerInitialized) return;
            if (_bossBarTitle.Value != title) _bossBarTitle.Value = title;
            // Small steps don't need sending: the bar eases on screen anyway.
            if (Mathf.Abs(_bossBar01.Value - value01) > 0.004f) _bossBar01.Value = Mathf.Clamp01(value01);
            if (Mathf.Abs(_bossBarTimeLeft.Value - timeLeft) > 0.2f || timeLeft <= 0f) _bossBarTimeLeft.Value = Mathf.Max(0f, timeLeft);
            if (_bossBarTimeTotal.Value != timeTotal) _bossBarTimeTotal.Value = timeTotal;
        }

        /// <summary>Host: the mission's boss bar goes.</summary>
        private void HideBossBar()
        {
            if (!IsServerInitialized) return;
            _bossBarTitle.Value = string.Empty;
            _bossBar01.Value = 0f;
            _bossBarTimeLeft.Value = _bossBarTimeTotal.Value = 0f;
        }
    }
}
