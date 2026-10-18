using UnityEngine;

namespace PleaseDontDrown.Combat
{
    public enum NpcWeaponKind : byte { Fists, Cutlass, Pistol, Musket }

    /// <summary>
    /// What a fighting character (pirate, drunk, cage fighter) has in hand, as numbers only: the AI in
    /// <see cref="Story.StoryNpc"/> reads these; <see cref="Story.NpcWeaponView"/> draws the weapon in their hands.
    ///
    /// Melee: the blow is wound up for <see cref="WindUp"/> seconds (the lifeguard's chance to step back or aside),
    /// lands only if they are still in <see cref="Reach"/> and in front, then <see cref="Recover"/> seconds of
    /// opening. Guns: <see cref="AimTime"/> to line up the first shot (the aim trails a moving target), a burst, then
    /// a reload; misses grow with distance and with how fast the target moves. The pirates' guns are flintlocks: one
    /// ball, then a long reload by hand (muzzle up, ramrod), which is the lifeguards' moment to rush them.
    /// </summary>
    public struct NpcWeapon
    {
        public NpcWeaponKind Kind;
        // Melee (every weapon has some: a gun is swung at someone too close to shoot).
        public int MeleeDamage;
        public float Reach, WindUp, Recover, Knock;
        // Ranged.
        public bool Ranged;
        public int ShotDamage;
        public float MinRange, MaxRange, AimTime, FollowUpAim, Spread;
        public int Burst, Magazine;
        public float BurstGap, Reload;
        public Combat.GunSound Sound;

        public static NpcWeapon Of(NpcWeaponKind kind) => kind switch
        {
            NpcWeaponKind.Cutlass => new NpcWeapon
            {
                Kind = kind, MeleeDamage = 20, Reach = 1.9f, WindUp = 0.55f, Recover = 0.7f, Knock = 5.5f
            },
            NpcWeaponKind.Pistol => new NpcWeapon
            {
                Kind = kind, MeleeDamage = 10, Reach = 1.4f, WindUp = 0.5f, Recover = 0.6f, Knock = 4f,
                Ranged = true, ShotDamage = 22, MinRange = 4f, MaxRange = 22f, AimTime = 0.9f, FollowUpAim = 0.45f,
                Spread = 2.2f, Burst = 1, Magazine = 1, BurstGap = 0f, Reload = 2.6f, Sound = Combat.GunSound.Pistol
            },
            NpcWeaponKind.Musket => new NpcWeapon
            {
                Kind = kind, MeleeDamage = 14, Reach = 1.6f, WindUp = 0.6f, Recover = 0.8f, Knock = 6f,
                Ranged = true, ShotDamage = 32, MinRange = 6f, MaxRange = 35f, AimTime = 1.5f, FollowUpAim = 1.2f,
                Spread = 1.2f, Burst = 1, Magazine = 1, BurstGap = 0f, Reload = 3.2f, Sound = Combat.GunSound.Rifle
            },
            _ => new NpcWeapon
            {
                // (The wind-up is the fist drawn back past the cheek: long enough to see and step out of.)
                Kind = NpcWeaponKind.Fists, MeleeDamage = 15, Reach = 1.35f, WindUp = 0.5f, Recover = 0.5f, Knock = 5f
            }
        };

        public float PreferredRange => Ranged ? Mathf.Lerp(MinRange, MaxRange, 0.4f) : Reach * 0.8f;
    }

    public enum FightStyle : byte
    {
        /// <summary>Pirates: steady, take turns two at a time, back off after a swing.</summary>
        Pirate,
        /// <summary>Drunks: slow, wide swings, sway about, give up easily.</summary>
        Drunk,
        /// <summary>Cage fighter: guards (blocks blows from the front), combos, slips back out of range.</summary>
        Brawler
    }

    /// <summary>How one fighter fights: the weapon, the style and how good they are at it (0..1).</summary>
    public struct FighterProfile
    {
        public NpcWeapon Weapon;
        public FightStyle Style;
        public float Skill;     // aim, reaction, how often a combo follows
        public int Poise;       // damage taken within a moment that knocks them over
        public float Guard;     // with the fists up, how often a blow from the front is blocked (a heavy one a third as often)

        // (Poise: two jabs used to floor a pirate, so mashing the button kept them all on the ground. Now it takes a
        // combo: jab, jab, hook is 25 + 25 + 50.)
        public static FighterProfile Pirate(NpcWeaponKind weapon, float skill = 0.5f) =>
            new() { Weapon = NpcWeapon.Of(weapon), Style = FightStyle.Pirate, Skill = skill, Poise = 90, Guard = 0.75f };

        public static FighterProfile Drunk() =>
            new() { Weapon = NpcWeapon.Of(NpcWeaponKind.Fists), Style = FightStyle.Drunk, Skill = 0.15f, Poise = 55, Guard = 0.2f };

        public static FighterProfile Brawler(float skill = 0.7f)
        {
            NpcWeapon fists = NpcWeapon.Of(NpcWeaponKind.Fists);
            fists.MeleeDamage = 15;
            fists.WindUp = 0.36f;
            fists.Recover = 0.4f;
            return new FighterProfile { Weapon = fists, Style = FightStyle.Brawler, Skill = skill, Poise = 120, Guard = 0.85f };
        }
    }
}
