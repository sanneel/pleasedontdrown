using System.Collections.Generic;
using System.IO;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Items;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The guns (Combat/Weapon): pistol, SMG, pump shotgun, assault rifle and bolt-action sniper. Greybox models built
    /// from primitives, with every part the shop sells already on the gun (switched off): sights with an eye point
    /// that lines up with the screen when aiming, barrel attachments with their own muzzle, a laser, an extended
    /// magazine. Hand grips put the first-person hands on the grip and the fore-end.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        /// <summary>A gun's numbers (see Weapon for what each does).</summary>
        private sealed class GunSetup
        {
            public string Kind;
            public GunSound Sound;
            public ProjectileKind Projectile;
            public bool FullAuto;
            public float Interval, HipSpread, AimSpread, Speed, Gravity, Force, ShooterPush, FlashSize = 1f, Range = 250f;
            public int Pellets = 1, Magazine, ExtendedMagazine, ExtendedMagazinePrice = 150, LaserPrice = 100;
            public float ReloadTime;
            public Vector2 Climb, KickTurn;
            public Vector3 Kick;
            public float SlideTravel = 0.03f;
            public (string name, int damage, int price)[] Tiers;
            public (string name, string model, string eye, float eyeDistance, float fov, bool scope, int price)[] Sights;
            public (string name, string model, string muzzle, bool suppressed, float climb, float kick, int price)[] Barrels;
        }

        private static IEnumerable<Object> BuildWeapons(PhysicsMaterial physics)
        {
            Material metal = GetMaterial("GunMetal", new Color(0.16f, 0.16f, 0.18f), metallic: 0.5f, smoothness: 0.5f);
            Material polymer = GetMaterial("GunPolymer", new Color(0.2f, 0.21f, 0.19f), smoothness: 0.3f);
            Material wood = GetMaterial("GunWood", new Color(0.5f, 0.3f, 0.15f), smoothness: 0.35f);
            Material tan = GetMaterial("GunTan", new Color(0.72f, 0.62f, 0.44f), smoothness: 0.3f);
            Material glass = GetMaterial("ScopeGlass", new Color(0.12f, 0.22f, 0.32f), smoothness: 0.95f);
            Material glow = GetMaterial("RedDotGlow", new Color(1f, 0.15f, 0.1f), emission: new Color(3f, 0.25f, 0.15f));
            Material laserBody = GetMaterial("LaserBody", new Color(0.3f, 0.3f, 0.33f), metallic: 0.4f);

            // ---------------------------------------------------------------- pistol
            yield return BuildGun("Pistol", "Pistol", 1.1f, new Vector3(0.16f, -0.17f, 0.42f), physics, root =>
            {
                Transform slide = Node(root, "Slide", new Vector3(0f, 0.022f, 0.015f));
                Part(slide, PrimitiveType.Cube, "SlideBody", Vector3.zero, new Vector3(0.036f, 0.034f, 0.19f), metal, collider: true);
                Part(slide, PrimitiveType.Cube, "RearSightL", new Vector3(-0.009f, 0.021f, -0.085f), new Vector3(0.006f, 0.009f, 0.008f), metal);
                Part(slide, PrimitiveType.Cube, "RearSightR", new Vector3(0.009f, 0.021f, -0.085f), new Vector3(0.006f, 0.009f, 0.008f), metal);
                Part(slide, PrimitiveType.Cube, "FrontSight", new Vector3(0f, 0.021f, 0.085f), new Vector3(0.005f, 0.009f, 0.006f), metal);
                Part(root, PrimitiveType.Cube, "Frame", new Vector3(0f, 0f, 0.02f), new Vector3(0.032f, 0.018f, 0.17f), polymer, collider: true);
                Part(root, PrimitiveType.Cube, "Grip", new Vector3(0f, -0.055f, -0.05f), new Vector3(0.033f, 0.11f, 0.05f), polymer, euler: new Vector3(-12f, 0f, 0f), collider: true);
                Part(root, PrimitiveType.Cube, "Guard", new Vector3(0f, -0.02f, 0.015f), new Vector3(0.01f, 0.03f, 0.05f), polymer);
                Part(root, PrimitiveType.Cylinder, "BarrelTip", new Vector3(0f, 0.022f, 0.112f), new Vector3(0.022f, 0.004f, 0.022f), metal, euler: new Vector3(90f, 0f, 0f));
                Node(root, "EyeIron", new Vector3(0f, 0.047f, -0.07f));
                Node(root, "EjectPort", new Vector3(0.02f, 0.035f, 0.005f));
                Node(root, "MuzzleStandard", new Vector3(0f, 0.022f, 0.118f));
                // Magazine base (standard) and a longer one sticking out of the grip.
                GameObject mag = Part(root, PrimitiveType.Cube, "Magazine", new Vector3(0f, -0.114f, -0.062f), new Vector3(0.03f, 0.012f, 0.046f), metal, euler: new Vector3(-12f, 0f, 0f));
                GameObject ext = Part(root, PrimitiveType.Cube, "MagazineExtended", new Vector3(0f, -0.13f, -0.066f), new Vector3(0.03f, 0.045f, 0.044f), metal, euler: new Vector3(-12f, 0f, 0f));
                RedDot(root, "SightRedDot", new Vector3(0f, 0.052f, -0.03f), 0.9f, metal, glass, glow);
                Suppressor(root, "BarrelSuppressor", new Vector3(0f, 0.022f, 0.118f), 0.017f, 0.12f, metal);
                Compensator(root, "BarrelCompensator", new Vector3(0f, 0.022f, 0.118f), 0.03f, metal);
                Laser(root, new Vector3(0f, -0.018f, 0.075f), laserBody, glow);
                HandGrips(root, new Vector3(0.033f, -0.06f, -0.05f), -12f, left: null, leftPalmUp: false); // one-handed
            }, new GunSetup
            {
                Kind = "Pistol", Sound = GunSound.Pistol, Projectile = ProjectileKind.Bullet, Interval = 0.16f, HipSpread = 1.6f, AimSpread = 0.15f,
                Speed = 200f, Gravity = 3f, Force = 3f, FlashSize = 0.8f, Magazine = 12, ExtendedMagazine = 18, ReloadTime = 1.35f,
                Climb = new Vector2(0.35f, 1.3f), Kick = new Vector3(0.004f, 0.012f, 0.05f), KickTurn = new Vector2(2.5f, 9f), SlideTravel = 0.028f,
                Tiers = new[] { ("Standard rounds", 34, 0), ("Hollow points", 45, 150), ("Magnum rounds", 60, 300) },
                Sights = new[] { ("Iron sights", "", "EyeIron", 0.38f, 62f, false, 0), ("Red dot", "SightRedDot", "SightRedDot/Eye", 0.38f, 58f, false, 120) },
                Barrels = new[] { ("Standard barrel", "", "MuzzleStandard", false, 1f, 1f, 0),
                                  ("Suppressor", "BarrelSuppressor", "BarrelSuppressor/Muzzle", true, 0.9f, 0.85f, 150),
                                  ("Compensator", "BarrelCompensator", "BarrelCompensator/Muzzle", false, 0.6f, 0.9f, 120) }
            });

            // ---------------------------------------------------------------- SMG
            yield return BuildGun("SMG", "SMG", 2.4f, new Vector3(0.15f, -0.17f, 0.38f), physics, root =>
            {
                Part(root, PrimitiveType.Cube, "Receiver", new Vector3(0f, 0.02f, 0.05f), new Vector3(0.045f, 0.06f, 0.28f), metal, collider: true);
                Part(root, PrimitiveType.Cube, "Grip", new Vector3(0f, -0.055f, -0.035f), new Vector3(0.032f, 0.1f, 0.045f), polymer, euler: new Vector3(-12f, 0f, 0f), collider: true);
                Part(root, PrimitiveType.Cube, "StockTop", new Vector3(0f, 0.035f, -0.17f), new Vector3(0.018f, 0.018f, 0.16f), metal);
                Part(root, PrimitiveType.Cube, "StockBottom", new Vector3(0f, -0.02f, -0.17f), new Vector3(0.018f, 0.018f, 0.16f), metal);
                Part(root, PrimitiveType.Cube, "ButtPlate", new Vector3(0f, 0.008f, -0.25f), new Vector3(0.03f, 0.08f, 0.02f), polymer, collider: true);
                Part(root, PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0.03f, 0.22f), new Vector3(0.022f, 0.035f, 0.022f), metal, euler: new Vector3(90f, 0f, 0f));
                Part(root, PrimitiveType.Cube, "RearSight", new Vector3(0f, 0.058f, -0.06f), new Vector3(0.02f, 0.016f, 0.01f), metal);
                Part(root, PrimitiveType.Cube, "FrontSight", new Vector3(0f, 0.058f, 0.17f), new Vector3(0.005f, 0.016f, 0.006f), metal);
                Transform handle = Node(root, "ChargingHandle", new Vector3(0.028f, 0.035f, 0.02f));
                Part(handle, PrimitiveType.Cube, "Knob", Vector3.zero, new Vector3(0.014f, 0.012f, 0.03f), metal);
                Node(root, "EyeIron", new Vector3(0f, 0.066f, -0.065f));
                Node(root, "EjectPort", new Vector3(0.025f, 0.035f, 0.07f));
                Node(root, "MuzzleStandard", new Vector3(0f, 0.03f, 0.258f));
                Part(root, PrimitiveType.Cube, "Magazine", new Vector3(0f, -0.085f, 0.085f), new Vector3(0.025f, 0.13f, 0.04f), metal);
                Part(root, PrimitiveType.Cube, "MagazineExtended", new Vector3(0f, -0.115f, 0.09f), new Vector3(0.025f, 0.19f, 0.04f), metal, euler: new Vector3(6f, 0f, 0f));
                RedDot(root, "SightRedDot", new Vector3(0f, 0.068f, 0.02f), 1f, metal, glass, glow);
                Scope(root, "SightScope", new Vector3(0f, 0.08f, 0.03f), 0.018f, 0.16f, metal, glass);
                Suppressor(root, "BarrelSuppressor", new Vector3(0f, 0.03f, 0.258f), 0.019f, 0.15f, metal);
                Compensator(root, "BarrelCompensator", new Vector3(0f, 0.03f, 0.258f), 0.032f, metal);
                Laser(root, new Vector3(0.03f, 0.02f, 0.17f), laserBody, glow);
                HandGrips(root, new Vector3(0.032f, -0.058f, -0.035f), -12f, left: new Vector3(0f, -0.018f, 0.14f), leftPalmUp: true);
            }, new GunSetup
            {
                Kind = "SMG", Sound = GunSound.Smg, Projectile = ProjectileKind.Bullet, FullAuto = true, Interval = 0.075f, HipSpread = 2.8f, AimSpread = 0.6f,
                Speed = 180f, Gravity = 3f, Force = 1.5f, FlashSize = 0.8f, Range = 150f, Magazine = 30, ExtendedMagazine = 45, ReloadTime = 1.7f,
                Climb = new Vector2(0.35f, 0.55f), Kick = new Vector3(0.003f, 0.006f, 0.022f), KickTurn = new Vector2(1.5f, 3f), SlideTravel = 0.04f,
                Tiers = new[] { ("Standard rounds", 16, 0), ("Hollow points", 21, 200), ("Armour piercing", 27, 400) },
                Sights = new[] { ("Iron sights", "", "EyeIron", 0.28f, 62f, false, 0), ("Red dot", "SightRedDot", "SightRedDot/Eye", 0.28f, 55f, false, 150),
                                 ("Scope (2x)", "SightScope", "SightScope/Eye", 0.14f, 38f, true, 250) },
                Barrels = new[] { ("Standard barrel", "", "MuzzleStandard", false, 1f, 1f, 0),
                                  ("Suppressor", "BarrelSuppressor", "BarrelSuppressor/Muzzle", true, 0.9f, 0.85f, 200),
                                  ("Compensator", "BarrelCompensator", "BarrelCompensator/Muzzle", false, 0.55f, 0.9f, 150) }
            });

            // ---------------------------------------------------------------- pump shotgun
            yield return BuildGun("Shotgun", "Shotgun", 3.2f, new Vector3(0.14f, -0.2f, 0.38f), physics, root =>
            {
                Part(root, PrimitiveType.Cube, "Receiver", new Vector3(0f, 0.015f, 0f), new Vector3(0.05f, 0.07f, 0.24f), metal, collider: true);
                Part(root, PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0.035f, 0.37f), new Vector3(0.032f, 0.25f, 0.032f), metal, euler: new Vector3(90f, 0f, 0f), collider: true);
                Part(root, PrimitiveType.Cube, "Stock", new Vector3(0f, -0.035f, -0.26f), new Vector3(0.042f, 0.085f, 0.3f), wood, euler: new Vector3(8f, 0f, 0f), collider: true);
                Part(root, PrimitiveType.Cube, "Wrist", new Vector3(0f, -0.04f, -0.1f), new Vector3(0.036f, 0.07f, 0.08f), wood, euler: new Vector3(-20f, 0f, 0f));
                Part(root, PrimitiveType.Sphere, "Bead", new Vector3(0f, 0.055f, 0.61f), Vector3.one * 0.008f, metal);
                Node(root, "EyeIron", new Vector3(0f, 0.055f, -0.1f));
                Node(root, "EjectPort", new Vector3(0.027f, 0.02f, 0.03f));
                Node(root, "MuzzleStandard", new Vector3(0f, 0.035f, 0.622f));
                // The pump (moves back and forth) around the magazine tube.
                Transform pump = Node(root, "Pump", new Vector3(0f, 0f, 0.26f));
                Part(pump, PrimitiveType.Cube, "Forend", Vector3.zero, new Vector3(0.052f, 0.046f, 0.15f), wood);
                Part(root, PrimitiveType.Cylinder, "Magazine", new Vector3(0f, 0f, 0.33f), new Vector3(0.026f, 0.17f, 0.026f), metal, euler: new Vector3(90f, 0f, 0f));
                Part(root, PrimitiveType.Cylinder, "MagazineExtended", new Vector3(0f, 0f, 0.37f), new Vector3(0.026f, 0.24f, 0.026f), metal, euler: new Vector3(90f, 0f, 0f));
                RedDot(root, "SightRedDot", new Vector3(0f, 0.062f, -0.02f), 1.1f, metal, glass, glow);
                Suppressor(root, "BarrelSuppressor", new Vector3(0f, 0.035f, 0.622f), 0.024f, 0.16f, metal);
                Compensator(root, "BarrelCompensator", new Vector3(0f, 0.035f, 0.622f), 0.04f, metal);
                Laser(root, new Vector3(0.03f, 0f, 0.36f), laserBody, glow);
                HandGrips(root, new Vector3(0.03f, -0.045f, -0.1f), -20f, left: new Vector3(0f, -0.028f, 0.27f), leftPalmUp: true);
            }, new GunSetup
            {
                Kind = "Shotgun", Sound = GunSound.Shotgun, Projectile = ProjectileKind.Pellet, Interval = 0.8f, Pellets = 8, HipSpread = 5.5f, AimSpread = 4f,
                Speed = 120f, Gravity = 6f, Force = 2f, ShooterPush = 3.5f, Range = 60f, FlashSize = 1.4f, Magazine = 6, ExtendedMagazine = 9, ReloadTime = 2.6f,
                Climb = new Vector2(1.2f, 5f), Kick = new Vector3(0.01f, 0.02f, 0.1f), KickTurn = new Vector2(4f, 16f), SlideTravel = 0.08f,
                Tiers = new[] { ("Buckshot", 14, 0), ("Magnum buckshot", 18, 250), ("Dragon shells", 23, 500) },
                Sights = new[] { ("Bead", "", "EyeIron", 0.3f, 64f, false, 0), ("Red dot", "SightRedDot", "SightRedDot/Eye", 0.3f, 58f, false, 150) },
                Barrels = new[] { ("Standard barrel", "", "MuzzleStandard", false, 1f, 1f, 0),
                                  ("Suppressor", "BarrelSuppressor", "BarrelSuppressor/Muzzle", true, 0.9f, 0.85f, 250),
                                  ("Choke", "BarrelCompensator", "BarrelCompensator/Muzzle", false, 0.75f, 0.9f, 200) }
            });

            // ---------------------------------------------------------------- assault rifle
            yield return BuildGun("Rifle", "Assault Rifle", 3.5f, new Vector3(0.14f, -0.2f, 0.38f), physics, root =>
            {
                Part(root, PrimitiveType.Cube, "Receiver", new Vector3(0f, 0.02f, 0f), new Vector3(0.05f, 0.08f, 0.3f), tan, collider: true);
                Part(root, PrimitiveType.Cube, "Handguard", new Vector3(0f, 0.025f, 0.26f), new Vector3(0.05f, 0.06f, 0.24f), polymer, collider: true);
                Part(root, PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0.035f, 0.44f), new Vector3(0.022f, 0.07f, 0.022f), metal, euler: new Vector3(90f, 0f, 0f));
                Part(root, PrimitiveType.Cube, "Grip", new Vector3(0f, -0.06f, -0.08f), new Vector3(0.03f, 0.1f, 0.045f), polymer, euler: new Vector3(-15f, 0f, 0f), collider: true);
                Part(root, PrimitiveType.Cube, "Stock", new Vector3(0f, 0.005f, -0.27f), new Vector3(0.045f, 0.08f, 0.22f), tan, collider: true);
                Part(root, PrimitiveType.Cube, "RearSight", new Vector3(0f, 0.07f, -0.11f), new Vector3(0.022f, 0.02f, 0.02f), metal);
                Part(root, PrimitiveType.Cube, "FrontSight", new Vector3(0f, 0.075f, 0.34f), new Vector3(0.005f, 0.03f, 0.006f), metal);
                Part(root, PrimitiveType.Cube, "Rail", new Vector3(0f, 0.062f, 0.05f), new Vector3(0.022f, 0.006f, 0.26f), metal);
                Transform bolt = Node(root, "BoltCover", new Vector3(0.027f, 0.04f, 0.03f));
                Part(bolt, PrimitiveType.Cube, "Cover", Vector3.zero, new Vector3(0.006f, 0.022f, 0.06f), metal);
                Node(root, "EyeIron", new Vector3(0f, 0.082f, -0.115f));
                Node(root, "EjectPort", new Vector3(0.03f, 0.04f, 0.03f));
                Node(root, "MuzzleStandard", new Vector3(0f, 0.035f, 0.51f));
                Part(root, PrimitiveType.Cube, "Magazine", new Vector3(0f, -0.075f, 0.07f), new Vector3(0.028f, 0.14f, 0.05f), metal, euler: new Vector3(10f, 0f, 0f));
                Part(root, PrimitiveType.Cube, "MagazineExtended", new Vector3(0f, -0.1f, 0.075f), new Vector3(0.028f, 0.19f, 0.05f), metal, euler: new Vector3(14f, 0f, 0f));
                RedDot(root, "SightRedDot", new Vector3(0f, 0.08f, 0.02f), 1f, metal, glass, glow);
                Scope(root, "SightScope", new Vector3(0f, 0.095f, 0.02f), 0.02f, 0.22f, metal, glass);
                Suppressor(root, "BarrelSuppressor", new Vector3(0f, 0.035f, 0.51f), 0.02f, 0.16f, metal);
                Compensator(root, "BarrelCompensator", new Vector3(0f, 0.035f, 0.51f), 0.035f, metal);
                Laser(root, new Vector3(0.033f, 0.025f, 0.3f), laserBody, glow);
                HandGrips(root, new Vector3(0.03f, -0.062f, -0.08f), -15f, left: new Vector3(0f, -0.012f, 0.25f), leftPalmUp: true);
            }, new GunSetup
            {
                Kind = "Rifle", Sound = GunSound.Rifle, Projectile = ProjectileKind.Bullet, FullAuto = true, Interval = 0.1f, HipSpread = 2.2f, AimSpread = 0.2f,
                Speed = 260f, Gravity = 2f, Force = 2.5f, Range = 300f, Magazine = 30, ExtendedMagazine = 40, ReloadTime = 2f,
                Climb = new Vector2(0.45f, 0.8f), Kick = new Vector3(0.003f, 0.008f, 0.03f), KickTurn = new Vector2(1.5f, 4f), SlideTravel = 0.04f,
                Tiers = new[] { ("Standard rounds", 25, 0), ("Match rounds", 32, 250), ("Armour piercing", 40, 500) },
                Sights = new[] { ("Iron sights", "", "EyeIron", 0.24f, 60f, false, 0), ("Red dot", "SightRedDot", "SightRedDot/Eye", 0.26f, 54f, false, 150),
                                 ("Scope (4x)", "SightScope", "SightScope/Eye", 0.12f, 26f, true, 350) },
                Barrels = new[] { ("Standard barrel", "", "MuzzleStandard", false, 1f, 1f, 0),
                                  ("Suppressor", "BarrelSuppressor", "BarrelSuppressor/Muzzle", true, 0.9f, 0.85f, 250),
                                  ("Compensator", "BarrelCompensator", "BarrelCompensator/Muzzle", false, 0.55f, 0.9f, 200) }
            });

            // ---------------------------------------------------------------- sniper
            yield return BuildGun("Sniper", "Sniper Rifle", 4.5f, new Vector3(0.14f, -0.21f, 0.42f), physics, root =>
            {
                Part(root, PrimitiveType.Cube, "Receiver", new Vector3(0f, 0.02f, 0f), new Vector3(0.05f, 0.07f, 0.3f), metal, collider: true);
                Part(root, PrimitiveType.Cylinder, "Barrel", new Vector3(0f, 0.035f, 0.47f), new Vector3(0.026f, 0.32f, 0.026f), metal, euler: new Vector3(90f, 0f, 0f), collider: true);
                Part(root, PrimitiveType.Cube, "Stock", new Vector3(0f, -0.02f, -0.24f), new Vector3(0.046f, 0.11f, 0.32f), wood, euler: new Vector3(4f, 0f, 0f), collider: true);
                Part(root, PrimitiveType.Cube, "Forestock", new Vector3(0f, -0.005f, 0.24f), new Vector3(0.048f, 0.05f, 0.3f), wood);
                Transform bolt = Node(root, "Bolt", new Vector3(0.04f, 0.04f, -0.03f));
                Part(bolt, PrimitiveType.Cube, "Handle", Vector3.zero, new Vector3(0.04f, 0.012f, 0.012f), metal);
                Part(bolt, PrimitiveType.Sphere, "Knob", new Vector3(0.022f, 0f, 0f), Vector3.one * 0.018f, metal);
                Node(root, "EjectPort", new Vector3(0.027f, 0.04f, 0.02f));
                Node(root, "MuzzleStandard", new Vector3(0f, 0.035f, 0.795f));
                Part(root, PrimitiveType.Cube, "Magazine", new Vector3(0f, -0.03f, 0.05f), new Vector3(0.03f, 0.04f, 0.07f), metal);
                Part(root, PrimitiveType.Cube, "MagazineExtended", new Vector3(0f, -0.05f, 0.05f), new Vector3(0.03f, 0.08f, 0.07f), metal);
                Scope(root, "SightScope", new Vector3(0f, 0.09f, 0.02f), 0.024f, 0.3f, metal, glass);
                RedDot(root, "SightRedDot", new Vector3(0f, 0.07f, 0.02f), 1f, metal, glass, glow);
                Suppressor(root, "BarrelSuppressor", new Vector3(0f, 0.035f, 0.795f), 0.022f, 0.18f, metal);
                Compensator(root, "BarrelCompensator", new Vector3(0f, 0.035f, 0.795f), 0.04f, metal);
                Laser(root, new Vector3(0.033f, 0.005f, 0.3f), laserBody, glow);
                HandGrips(root, new Vector3(0.03f, -0.035f, -0.1f), -18f, left: new Vector3(0f, -0.03f, 0.22f), leftPalmUp: true);
            }, new GunSetup
            {
                Kind = "Sniper", Sound = GunSound.Sniper, Projectile = ProjectileKind.Heavy, Interval = 1.2f, HipSpread = 7f, AimSpread = 0.02f,
                Speed = 400f, Gravity = 1.5f, Force = 6f, FlashSize = 1.3f, Range = 500f, Magazine = 5, ExtendedMagazine = 8, ReloadTime = 2.8f,
                Climb = new Vector2(0.6f, 6f), Kick = new Vector3(0.01f, 0.02f, 0.12f), KickTurn = new Vector2(3f, 14f), SlideTravel = 0.06f,
                Tiers = new[] { ("Standard rounds", 100, 0), ("Match rounds", 130, 300), ("Elephant rounds", 170, 600) },
                Sights = new[] { ("Scope (8x)", "SightScope", "SightScope/Eye", 0.1f, 14f, true, 0), ("Red dot", "SightRedDot", "SightRedDot/Eye", 0.26f, 55f, false, 150) },
                Barrels = new[] { ("Standard barrel", "", "MuzzleStandard", false, 1f, 1f, 0),
                                  ("Suppressor", "BarrelSuppressor", "BarrelSuppressor/Muzzle", true, 0.9f, 0.85f, 300),
                                  ("Muzzle brake", "BarrelCompensator", "BarrelCompensator/Muzzle", false, 0.6f, 0.85f, 250) }
            });
        }

        private static Item BuildGun(string file, string displayName, float mass, Vector3 holdOffset, PhysicsMaterial physics,
            System.Action<Transform> buildVisual, GunSetup setup)
        {
            return BuildItem(file, displayName, mass, holdOffset, Vector3.zero, 1f, physics, buildVisual, density: 1.6f, configure: go =>
            {
                Item item = go.GetComponent<Item>();
                SetBool(item, "_pocketable", true);
                SetBool(item, "_rigidInHand", true);
                SetEnum(item, "_grip", (int)(Find(go, "GripLeft") != null ? ItemGrip.TwoHands : ItemGrip.OneHand));
                SetRef(item, "_gripRight", Find(go, "GripRight"));
                SetRef(item, "_gripLeft", Find(go, "GripLeft"));

                var weapon = go.AddComponent<Weapon>();
                var so = new SerializedObject(weapon);
                Require(so, "_kind").stringValue = setup.Kind;
                Require(so, "_sound").enumValueIndex = (int)setup.Sound;
                Require(so, "_projectile").enumValueIndex = (int)setup.Projectile;
                Require(so, "_fullAuto").boolValue = setup.FullAuto;
                Require(so, "_interval").floatValue = setup.Interval;
                Require(so, "_pellets").intValue = setup.Pellets;
                Require(so, "_hipSpread").floatValue = setup.HipSpread;
                Require(so, "_aimSpread").floatValue = setup.AimSpread;
                Require(so, "_speed").floatValue = setup.Speed;
                Require(so, "_gravity").floatValue = setup.Gravity;
                Require(so, "_force").floatValue = setup.Force;
                Require(so, "_range").floatValue = setup.Range;
                Require(so, "_shooterPush").floatValue = setup.ShooterPush;
                Require(so, "_flashSize").floatValue = setup.FlashSize;
                Require(so, "_magazine").intValue = setup.Magazine;
                Require(so, "_extendedMagazine").intValue = setup.ExtendedMagazine;
                Require(so, "_extendedMagazinePrice").intValue = setup.ExtendedMagazinePrice;
                Require(so, "_laserPrice").intValue = setup.LaserPrice;
                Require(so, "_reloadTime").floatValue = setup.ReloadTime;
                Require(so, "_climb").vector2Value = setup.Climb;
                Require(so, "_kick").vector3Value = setup.Kick;
                Require(so, "_kickTurn").vector2Value = setup.KickTurn;
                Require(so, "_slideTravel").floatValue = setup.SlideTravel;

                SerializedProperty tiers = Require(so, "_tiers");
                tiers.arraySize = setup.Tiers.Length;
                for (int i = 0; i < setup.Tiers.Length; i++)
                {
                    SerializedProperty t = tiers.GetArrayElementAtIndex(i);
                    t.FindPropertyRelative("Name").stringValue = setup.Tiers[i].name;
                    t.FindPropertyRelative("Damage").intValue = setup.Tiers[i].damage;
                    t.FindPropertyRelative("Price").intValue = setup.Tiers[i].price;
                }
                SerializedProperty sights = Require(so, "_sights");
                sights.arraySize = setup.Sights.Length;
                for (int i = 0; i < setup.Sights.Length; i++)
                {
                    var s = setup.Sights[i];
                    SerializedProperty p = sights.GetArrayElementAtIndex(i);
                    p.FindPropertyRelative("Name").stringValue = s.name;
                    p.FindPropertyRelative("Model").objectReferenceValue = string.IsNullOrEmpty(s.model) ? null : Find(go, s.model).gameObject;
                    p.FindPropertyRelative("EyePoint").objectReferenceValue = Find(go, s.eye);
                    p.FindPropertyRelative("EyeDistance").floatValue = s.eyeDistance;
                    p.FindPropertyRelative("AimFov").floatValue = s.fov;
                    p.FindPropertyRelative("AimTime").floatValue = s.scope ? 0.2f : setup.Pellets > 1 || setup.Kind == "Rifle" ? 0.15f : 0.11f;
                    p.FindPropertyRelative("Scope").boolValue = s.scope;
                    p.FindPropertyRelative("AimSway").floatValue = s.scope ? 0.15f : 0.3f;
                    p.FindPropertyRelative("Price").intValue = s.price;
                }
                SerializedProperty barrels = Require(so, "_barrels");
                barrels.arraySize = setup.Barrels.Length;
                for (int i = 0; i < setup.Barrels.Length; i++)
                {
                    var b = setup.Barrels[i];
                    SerializedProperty p = barrels.GetArrayElementAtIndex(i);
                    p.FindPropertyRelative("Name").stringValue = b.name;
                    p.FindPropertyRelative("Model").objectReferenceValue = string.IsNullOrEmpty(b.model) ? null : Find(go, b.model).gameObject;
                    p.FindPropertyRelative("Muzzle").objectReferenceValue = Find(go, b.muzzle);
                    p.FindPropertyRelative("Suppressed").boolValue = b.suppressed;
                    p.FindPropertyRelative("ClimbScale").floatValue = b.climb;
                    p.FindPropertyRelative("KickScale").floatValue = b.kick;
                    p.FindPropertyRelative("Price").intValue = b.price;
                }
                Require(so, "_laser").objectReferenceValue = Find(go, "Laser").gameObject;
                Require(so, "_magazineModel").objectReferenceValue = Find(go, "Magazine").gameObject;
                Require(so, "_extendedMagazineModel").objectReferenceValue = Find(go, "MagazineExtended").gameObject;
                Require(so, "_ejectPort").objectReferenceValue = Find(go, "EjectPort");
                Transform slide = Find(go, "Slide", "Pump", "ChargingHandle", "BoltCover", "Bolt");
                Require(so, "_slide").objectReferenceValue = slide;
                Require(so, "_audio").objectReferenceValue = SpatialAudio(go, 5f, 160f);
                so.ApplyModifiedPropertiesWithoutUndo();

                // Parts start hidden (the gun switches on what's fitted).
                foreach (string part in new[] { "SightRedDot", "SightScope", "BarrelSuppressor", "BarrelCompensator", "Laser", "MagazineExtended" })
                {
                    Transform t = go.transform.Find(part);
                    if (t != null && setup.Sights[0].model != part) t.gameObject.SetActive(false);
                }
            });
        }

        // ------------------------------------------------------------------ gun parts

        private static Transform Node(Transform parent, string name, Vector3 position, Vector3? euler = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = position;
            t.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            return t;
        }

        private static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material,
            Vector3? euler = null, bool collider = false)
        {
            GameObject go = Primitive(type, name, parent, position, scale, material, keepCollider: collider);
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            return go;
        }

        /// <summary>First child anywhere below <paramref name="go"/> with one of these names (or a path like "SightScope/Eye").</summary>
        private static Transform Find(GameObject go, params string[] names)
        {
            foreach (string name in names)
            {
                Transform direct = go.transform.Find(name);
                if (direct != null) return direct;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    if (t.name == name) return t;
            }
            return null;
        }

        /// <summary>A small reflex sight: a frame, a lens and a glowing dot; its eye point is the lens centre.</summary>
        private static void RedDot(Transform gun, string name, Vector3 at, float size, Material frame, Material glass, Material glow)
        {
            Transform root = Node(gun, name, at);
            float s = size;
            Part(root, PrimitiveType.Cube, "Base", new Vector3(0f, -0.006f * s, 0f), new Vector3(0.028f, 0.008f, 0.04f) * s, frame);
            Part(root, PrimitiveType.Cube, "HoodL", new Vector3(-0.015f * s, 0.01f * s, 0.008f * s), new Vector3(0.004f, 0.03f, 0.02f) * s, frame);
            Part(root, PrimitiveType.Cube, "HoodR", new Vector3(0.015f * s, 0.01f * s, 0.008f * s), new Vector3(0.004f, 0.03f, 0.02f) * s, frame);
            Part(root, PrimitiveType.Cube, "HoodTop", new Vector3(0f, 0.026f * s, 0.008f * s), new Vector3(0.034f, 0.004f, 0.02f) * s, frame);
            Part(root, PrimitiveType.Sphere, "Dot", new Vector3(0f, 0.01f * s, 0.011f * s), Vector3.one * 0.0022f * s, glow);
            Node(root, "Eye", new Vector3(0f, 0.01f * s, -0.01f));
        }

        /// <summary>A scope tube with lenses and turrets; its eye point is behind the rear lens.</summary>
        private static void Scope(Transform gun, string name, Vector3 at, float radius, float length, Material body, Material glass)
        {
            Transform root = Node(gun, name, at);
            Part(root, PrimitiveType.Cylinder, "Tube", Vector3.zero, new Vector3(radius * 2f, length * 0.5f, radius * 2f), body, euler: new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Cylinder, "Objective", new Vector3(0f, 0f, length * 0.42f), new Vector3(radius * 2.7f, length * 0.1f, radius * 2.7f), body, euler: new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Cylinder, "Ocular", new Vector3(0f, 0f, -length * 0.42f), new Vector3(radius * 2.4f, length * 0.08f, radius * 2.4f), body, euler: new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Cylinder, "FrontLens", new Vector3(0f, 0f, length * 0.52f + 0.001f), new Vector3(radius * 2.5f, 0.001f, radius * 2.5f), glass, euler: new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Cylinder, "RearLens", new Vector3(0f, 0f, -length * 0.5f - 0.001f), new Vector3(radius * 2.2f, 0.001f, radius * 2.2f), glass, euler: new Vector3(90f, 0f, 0f));
            Part(root, PrimitiveType.Cylinder, "TurretTop", new Vector3(0f, radius * 1.2f, 0f), new Vector3(radius * 1.1f, radius * 0.5f, radius * 1.1f), body);
            Part(root, PrimitiveType.Cylinder, "TurretSide", new Vector3(radius * 1.2f, 0f, 0f), new Vector3(radius * 1.1f, radius * 0.5f, radius * 1.1f), body, euler: new Vector3(0f, 0f, 90f));
            Part(root, PrimitiveType.Cube, "Mount", new Vector3(0f, -radius * 1.4f, 0f), new Vector3(radius * 1.2f, radius * 1.6f, length * 0.5f), body);
            Node(root, "Eye", new Vector3(0f, 0f, -length * 0.5f));
        }

        private static void Suppressor(Transform gun, string name, Vector3 muzzle, float radius, float length, Material body)
        {
            Transform root = Node(gun, name, muzzle);
            Part(root, PrimitiveType.Cylinder, "Can", new Vector3(0f, 0f, length * 0.5f), new Vector3(radius * 2f, length * 0.5f, radius * 2f), body, euler: new Vector3(90f, 0f, 0f));
            Node(root, "Muzzle", new Vector3(0f, 0f, length + 0.003f));
        }

        private static void Compensator(Transform gun, string name, Vector3 muzzle, float size, Material body)
        {
            Transform root = Node(gun, name, muzzle);
            Part(root, PrimitiveType.Cube, "Block", new Vector3(0f, 0f, size * 0.55f), new Vector3(size, size * 0.9f, size * 1.1f), body);
            Part(root, PrimitiveType.Cube, "PortL", new Vector3(-size * 0.5f, 0.2f * size, size * 0.55f), new Vector3(0.004f, size * 0.3f, size * 0.7f), body);
            Part(root, PrimitiveType.Cube, "PortR", new Vector3(size * 0.5f, 0.2f * size, size * 0.55f), new Vector3(0.004f, size * 0.3f, size * 0.7f), body);
            Node(root, "Muzzle", new Vector3(0f, 0f, size * 1.15f));
        }

        /// <summary>The laser's own transform is its emitter (the beam starts there, pointing forward).</summary>
        private static void Laser(Transform gun, Vector3 emitter, Material body, Material glow)
        {
            Transform root = Node(gun, "Laser", emitter);
            Part(root, PrimitiveType.Cube, "Body", new Vector3(0f, 0f, -0.025f), new Vector3(0.018f, 0.018f, 0.05f), body);
            Part(root, PrimitiveType.Sphere, "Lens", Vector3.zero, Vector3.one * 0.008f, glow);
        }

        /// <summary>
        /// Where the hands go: the right hand round the grip (fingers along it, palm against its right side), the left
        /// either under the fore-end (palm up), cupping the right hand on the other side of the grip, or nowhere (null:
        /// a one-handed gun, the left hand stays free).
        /// </summary>
        private static void HandGrips(Transform gun, Vector3 right, float gripTilt, Vector3? left, bool leftPalmUp)
        {
            Quaternion tilt = Quaternion.Euler(gripTilt, 0f, 0f);
            var r = Node(gun, "GripRight", right);
            r.localRotation = tilt * Quaternion.LookRotation(Vector3.forward, Vector3.right); // palm faces -x, onto the grip
            if (left == null) return;
            var l = Node(gun, "GripLeft", left.Value);
            l.localRotation = leftPalmUp
                ? Quaternion.LookRotation(Vector3.forward, Vector3.down) // palm faces up under the fore-end
                : tilt * Quaternion.LookRotation(Vector3.forward, Vector3.left);
        }

        /// <summary>The tracer and bullet-hole materials the ProjectileSystem loads (URP Unlit, instanced).</summary>
        private static void BuildProjectileMaterials()
        {
            const string dir = "Assets/_Game/Resources";
            Directory.CreateDirectory(dir);
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            void Make(string name, Color color)
            {
                string path = $"{dir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(unlit);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = unlit;
                mat.SetColor("_BaseColor", color);
                mat.enableInstancing = true;
                EditorUtility.SetDirty(mat);
            }
            Make("ProjectileTracer", new Color(1f, 0.86f, 0.4f));
            Make("BulletHole", new Color(0.1f, 0.085f, 0.075f));
        }
    }
}
