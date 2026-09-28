using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    public enum DamageKind : byte { Punch, Bullet, Bite }

    /// <summary>Something that can be punched or shot (robbers, pirates, the shark). Resolved on the host.</summary>
    public interface IDamageable
    {
        /// <returns>True if the hit counted (for hit markers).</returns>
        bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction);
    }

    /// <summary>
    /// Held items that do something when you press Primary (pistol, defibrillator) instead of winding up a throw.
    /// You can still throw them by holding Drop.
    /// </summary>
    public interface IHeldTool
    {
        /// <summary>Shown under the crosshair while held, e.g. "Shoot".</summary>
        string UseLabel { get; }
        /// <summary>Runs on the holder's machine when Primary is pressed.</summary>
        void Use(PlayerHub holder);
    }

    public static class DamageUtil
    {
        /// <summary>The damageable a collider belongs to (colliders on children count).</summary>
        public static IDamageable Find(Collider c) => c != null ? c.GetComponentInParent<IDamageable>() : null;
    }
}
