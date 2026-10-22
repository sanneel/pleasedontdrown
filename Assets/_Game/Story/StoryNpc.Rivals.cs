using FishNet.Object.Synchronizing;
using PleaseDontDrown.Combat;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Knocked out or killed (host): fists knock people out (K.O., stars round the head); a bullet or a bite that takes
    /// the last of their health kills them (no stars, they lie still for good). People the islands still need (Sandy,
    /// the receptionist, anyone you can talk to) only ever get knocked out.
    /// </summary>
    public partial class StoryNpc
    {
        private readonly SyncVar<bool> _dead = new SyncVar<bool>();

        /// <summary>Killed (shot, bitten), not just knocked out: they won't get up again.</summary>
        public bool IsDead => _dead.Value;
        /// <summary>The islands still need them: they can be knocked out but never killed.</summary>
        public bool IsEssential => _role.Value is NpcRole.Guide or NpcRole.Receptionist || _talkable.Value;

        /// <summary>A gun or a bite finishes people off; fists only knock them out.</summary>
        private bool KillsOutright(DamageKind kind) => kind is DamageKind.Bullet or DamageKind.Bite && !IsEssential;

        private void Die()
        {
            _dead.Value = true;
            _shoutText = null;
        }
    }
}
