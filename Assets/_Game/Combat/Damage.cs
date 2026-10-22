using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    public enum DamageKind : byte { Punch, Bullet, Bite, Blade }

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

    /// <summary>Health is on a 100 scale: a punch takes a quarter, guns do their own (see Weapon).</summary>
    public static class Damage
    {
        public const int Punch = 25;
        /// <summary>The knife: a robber goes down in three.</summary>
        public const int Knife = 45;
        /// <summary>The most one melee blow may claim (the host clamps what clients send).</summary>
        public const int MaxMelee = 60;
    }

    public static class DamageUtil
    {
        /// <summary>
        /// The damageable a collider belongs to (colliders on children count, and so do a tourist's arms and legs, which
        /// hang outside the body's hierarchy on joints: a bullet in the leg missed them).
        /// </summary>
        public static IDamageable Find(Collider c)
        {
            if (c == null) return null;
            if (c.GetComponentInParent<IDamageable>() is { } found) return found;
            return Items.Item.FromCollider(c) is { } item ? item.GetComponent<IDamageable>() : null;
        }
    }
}
