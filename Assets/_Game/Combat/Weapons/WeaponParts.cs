using System;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>Which synthesized shot a gun makes (Core/ProceduralAudio).</summary>
    public enum GunSound : byte { Pistol, Smg, Shotgun, Rifle, Sniper }

    /// <summary>How a projectile looks and flies (ProjectileSystem).</summary>
    public enum ProjectileKind : byte { Bullet, Pellet, Heavy }

    /// <summary>
    /// A sight the gun can wear (iron sights, red dot, scope). Only the chosen one is shown; it decides where the
    /// gun sits when aiming (its eye point lines up with the middle of the screen) and how far the view zooms.
    /// </summary>
    [Serializable]
    public class WeaponSight
    {
        public string Name = "Iron sights";
        [Tooltip("Shown only while this sight is fitted (null for the gun's own iron sights).")]
        public GameObject Model;
        [Tooltip("The point that lines up with the middle of the screen when aiming.")]
        public Transform EyePoint;
        [Tooltip("How far in front of the eye the eye point sits when aiming (m).")]
        public float EyeDistance = 0.28f;
        public float AimFov = 62f;
        [Tooltip("Seconds to get the gun up to the eye.")]
        public float AimTime = 0.12f;
        [Tooltip("Scopes: the view goes through a full-screen scope picture once the gun is up.")]
        public bool Scope;
        [Tooltip("How much the gun sways with the mouse while aiming (1 = as much as from the hip).")]
        public float AimSway = 0.3f;
        public int Price;
    }

    /// <summary>What's on the end of the barrel: nothing, a suppressor (quiet, a little less kick), a compensator (much less climb).</summary>
    [Serializable]
    public class WeaponBarrel
    {
        public string Name = "Standard barrel";
        public GameObject Model;
        [Tooltip("Where bullets and the flash come out (the end of this barrel).")]
        public Transform Muzzle;
        public bool Suppressed;
        [Tooltip("Multiplies how far the view climbs per shot.")]
        public float ClimbScale = 1f;
        [Tooltip("Multiplies how hard the gun kicks in the hands.")]
        public float KickScale = 1f;
        public int Price;
    }

    /// <summary>A bullet upgrade: damage per projectile (tier 0 is what the gun comes with).</summary>
    [Serializable]
    public class WeaponTier
    {
        public string Name = "Standard rounds";
        public int Damage = 30;
        public int Price;
    }
}
