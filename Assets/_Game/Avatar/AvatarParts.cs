using System;
using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// The shapes that make up a character, per bone, in metres for a 1.8 m person (scaled by height).
    /// Chunky cartoon proportions: big head and hands, rounded everything.
    /// </summary>
    internal static class AvatarParts
    {
        private static readonly Color Black = new(0.06f, 0.06f, 0.07f);
        private static readonly Color EyeWhite = new(0.97f, 0.97f, 0.97f);
        private static readonly Color MouthColor = new(0.42f, 0.1f, 0.1f);
        private static readonly Color Floatie = new(1f, 0.5f, 0.1f);
        private static readonly Color Silver = new(0.82f, 0.84f, 0.88f);
        private static readonly Color Sunscreen = new(0.98f, 0.98f, 1f);

        /// <summary>Heads (and everything on them) are drawn this much bigger than life: cartoon proportions.</summary>
        public const float HeadScale = 1.14f;

        // Head ellipsoid (head bone space, before HeadScale).
        private static readonly Vector3 HeadCenter = new(0f, 0.14f, 0.012f);
        private static readonly Vector3 HeadRadii = new(0.148f, 0.158f, 0.15f);

        public static void Build(AvatarMeshKit kit, AvatarLook look, float width, float belly, float limb, float shoulder, float s,
            Action<Bone> on)
        {
            Color skin = look.SkinColor;
            Color top = look.TopTint;
            Color bottom = look.BottomTint;
            Color hair = look.HairTint;
            bool swimsuit = look.Top == TopStyle.Swimsuit;
            bool coveredTorso = look.Top != TopStyle.None;
            Color torso = coveredTorso ? top : skin;
            Color pelvis = swimsuit ? top : bottom;
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * s;

            // ---------------------------------------------------------- torso
            on(Bone.Hips);
            kit.Ellipsoid(V(0f, 0f, -0.005f), V(0.165f * width, 0.125f, 0.125f * Mathf.Min(belly, 1.2f)), pelvis, segments: 14);

            on(Bone.Spine);
            var bellyCenter = V(0f, 0.08f, 0.012f * belly);
            var bellyRadii = V(0.165f * width, 0.15f, 0.125f * belly);
            kit.Ellipsoid(bellyCenter, bellyRadii, torso, segments: 14, rings: 10);

            on(Bone.Chest);
            var chestCenter = V(0f, 0.08f, 0f);
            var chestRadii = V(0.19f * width, 0.17f, 0.125f * Mathf.Lerp(1f, belly, 0.3f));
            kit.Ellipsoid(chestCenter, chestRadii, torso, segments: 16, rings: 10);
            bool sleeves = look.Top is TopStyle.TShirt or TopStyle.RashGuard or TopStyle.Hawaiian;
            foreach (float side in new[] { -1f, 1f })
                kit.Ellipsoid(V(shoulder / s * side, 0.15f, 0f), V(0.068f * limb, 0.066f * limb, 0.07f * limb), sleeves ? top : skin, segments: 10, rings: 6);

            if (look.Top == TopStyle.LifeguardTank)
            {
                // One bold stripe across the chest (red on white, white on anything else).
                Color accent = look.TopColor == 1 ? AvatarLook.ClothColors[0] : AvatarLook.ClothColors[1];
                float k = 0.984f;
                kit.Frustum(V(0f, 0.035f, 0f), 1f, 1f, 0.05f * s, accent,
                    scale: new Vector2(chestRadii.x * k + 0.005f * s, chestRadii.z * k + 0.005f * s), segments: 18);
            }
            if (look.Top == TopStyle.Hawaiian)
            {
                // Worn open: a strip of skin down the front, flowers everywhere else.
                kit.Ellipsoid(V(0f, 0.05f, 0f) + Vector3.forward * (chestRadii.z - 0.02f * s), V(0.04f, 0.16f, 0.03f), skin, segments: 8, rings: 6);
                on(Bone.Spine);
                kit.Ellipsoid(bellyCenter + Vector3.forward * (bellyRadii.z - 0.018f * s), V(0.035f, 0.14f, 0.028f), skin, segments: 8, rings: 6);
                Flowers(kit, bellyCenter, bellyRadii, look.TopColor, s, 0f);
                on(Bone.Chest);
                Flowers(kit, chestCenter, chestRadii, look.TopColor, s, 1f);
            }
            if (look.Has(AvatarExtras.Whistle))
            {
                kit.Torus(V(0f, 0.2f, 0.012f), 0.075f * s * width, 0.006f * s, Black, Quaternion.Euler(28f, 0f, 0f), segments: 14, tubeSegments: 4);
                kit.Box(V(0f, 0.095f, 0f) + Vector3.forward * (chestRadii.z + 0.008f * s), V(0.028f, 0.02f, 0.05f), Silver);
            }

            on(Bone.Neck);
            kit.Limb(0.1f * s, 0.058f * s, 0.064f * s, skin, V(0f, 0.1f, 0f), segments: 10);

            // ---------------------------------------------------------- head (cartoon-big)
            float hs = s * HeadScale;
            Vector3 H(float x, float y, float z) => new Vector3(x, y, z) * hs;
            on(Bone.Head);
            kit.Ellipsoid(HeadCenter * hs, HeadRadii * hs, skin, segments: 18, rings: 12);
            kit.Ellipsoid(H(0f, 0.11f, 0.155f), H(0.028f, 0.034f, 0.034f), look.Has(AvatarExtras.Sunscreen) ? Sunscreen : skin * 0.97f, segments: 8, rings: 6);
            foreach (float side in new[] { -1f, 1f })
                kit.Ellipsoid(H(0.146f * side, 0.13f, -0.005f), H(0.022f, 0.038f, 0.03f), skin, segments: 8, rings: 6);

            switch (look.Face)
            {
                case FacialHair.Mustache:
                    kit.Ellipsoid(H(0f, 0.088f, 0.155f), H(0.056f, 0.017f, 0.022f), hair, segments: 10, rings: 6);
                    break;
                case FacialHair.Beard:
                    kit.Ellipsoid(H(0f, 0.02f, 0.06f), H(0.128f, 0.062f, 0.096f), hair, segments: 14, rings: 8);
                    kit.Ellipsoid(H(0f, 0.088f, 0.155f), H(0.05f, 0.015f, 0.02f), hair, segments: 10, rings: 6);
                    break;
                case FacialHair.Stubble:
                    kit.Ellipsoid(H(0f, 0.045f, 0.045f), H(0.142f, 0.075f, 0.1f), Color.Lerp(skin, hair, 0.35f) * 0.92f, segments: 14, rings: 8);
                    break;
            }

            BuildHair(kit, look, hair, hs);
            BuildHat(kit, look, hs);
            BuildGlasses(kit, look.Glasses, hs);

            // Face features live on their own bones so expressions can scale them.
            foreach (Bone eye in new[] { Bone.EyeL, Bone.EyeR })
            {
                on(eye);
                kit.Ellipsoid(Vector3.zero, H(0.034f, 0.042f, 0.02f), EyeWhite, segments: 10, rings: 6);
                kit.Ellipsoid(H(0f, -0.005f, 0.013f), H(0.02f, 0.025f, 0.011f), Black, segments: 8, rings: 6);
                kit.Ellipsoid(H(0.007f, 0.007f, 0.022f), H(0.006f, 0.006f, 0.004f), EyeWhite, segments: 6, rings: 4);
            }
            foreach (Bone brow in new[] { Bone.BrowL, Bone.BrowR })
            {
                on(brow);
                kit.Box(Vector3.zero, H(0.052f, 0.013f, 0.014f), Color.Lerp(hair, Black, 0.3f));
            }
            on(Bone.Mouth);
            kit.Ellipsoid(Vector3.zero, H(0.034f, 0.012f, 0.012f), MouthColor, segments: 10, rings: 6);

            // ---------------------------------------------------------- arms
            Color sandal = AvatarLook.ClothColors[(look.BottomColor + 5) % AvatarLook.ClothColors.Length];
            foreach (bool left in new[] { true, false })
            {
                BuildArm(kit, look, left, limb, s, on);

                // ------------------------------------------------------ legs
                on(left ? Bone.ThighL : Bone.ThighR);
                float t0 = 0.085f * limb * s, t1 = 0.068f * limb * s, tlen = 0.41f * s;
                kit.Limb(tlen, t0, t1, skin);
                if (!swimsuit)
                {
                    float cover = look.Bottom switch { BottomStyle.Trunks => 0.2f, BottomStyle.Shorts => 0.5f, BottomStyle.BoardShorts => 0.85f, _ => 1f };
                    kit.Limb(tlen * cover, t0 + 0.012f * s, Mathf.Lerp(t0, t1, cover) + (look.Bottom == BottomStyle.BoardShorts ? 0.02f : 0.012f) * s, bottom);
                }

                on(left ? Bone.ShinL : Bone.ShinR);
                float h0 = 0.065f * limb * s, h1 = 0.048f * limb * s, hlen = 0.39f * s;
                kit.Limb(hlen, h0, h1, skin);
                if (!swimsuit && look.Bottom == BottomStyle.Trousers)
                    kit.Limb(hlen * 0.5f, h0 + 0.012f * s, h0 + 0.01f * s, bottom); // rolled up to mid-shin

                on(left ? Bone.FootL : Bone.FootR);
                kit.Ellipsoid(V(0f, -0.045f, 0.045f), V(0.048f, 0.038f, 0.1f), skin, segments: 10, rings: 6);
                kit.Box(V(0f, -0.074f, 0.05f), V(0.1f, 0.022f, 0.25f), sandal);
                kit.Box(V(0f, -0.048f, 0.085f), V(0.092f, 0.012f, 0.016f), sandal);
            }
        }

        /// <summary>
        /// One arm (upper arm and forearm) with its sleeve and floatie. Also used for the first-person arms,
        /// which stretch their segments (<paramref name="length"/>) so hands reach items held in front of the camera.
        /// </summary>
        public static void BuildArm(AvatarMeshKit kit, AvatarLook look, bool left, float limb, float s, Action<Bone> on, float length = 1f, float thickness = 1f)
        {
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * s;
            Color skin = look.SkinColor;
            Color top = look.TopTint;
            on(left ? Bone.UpperArmL : Bone.UpperArmR);
            float r0 = 0.056f * limb * s * thickness, r1 = 0.048f * limb * s * thickness, len = 0.29f * s * length;
            kit.Limb(len, r0, r1, skin);
            float sleeve = look.Top switch { TopStyle.TShirt => 0.5f, TopStyle.Hawaiian => 0.55f, TopStyle.RashGuard => 1f, _ => 0f };
            if (sleeve > 0f)
                kit.Limb(len * sleeve, r0 + 0.012f * s, Mathf.Lerp(r0, r1, sleeve) + 0.011f * s, top);
            if (look.Has(AvatarExtras.Floaties))
                kit.Torus(V(0f, -0.12f * length, 0f), 0.075f * limb * s, 0.038f * s, Floatie, segments: 14, tubeSegments: 8);

            on(left ? Bone.ForearmL : Bone.ForearmR);
            float f0 = 0.049f * limb * s * thickness, f1 = 0.041f * limb * s * thickness, flen = 0.26f * s * length;
            kit.Limb(flen, f0, f1, skin);
            if (look.Top == TopStyle.RashGuard)
                kit.Limb(flen * 0.92f, f0 + 0.009f * s, f1 + 0.009f * s, top);

            // (The hand itself, with jointed fingers, is built by HandBones.)
        }

        /// <summary>A few flower dots on the surface of an ellipsoid (Hawaiian shirt).</summary>
        private static void Flowers(AvatarMeshKit kit, Vector3 center, Vector3 radii, int shirtColor, float s, float seed)
        {
            Color a = shirtColor == 1 ? AvatarLook.ClothColors[4] : AvatarLook.ClothColors[1];
            Color b = shirtColor == 2 ? AvatarLook.ClothColors[3] : AvatarLook.ClothColors[2];
            for (int i = 0; i < 9; i++)
            {
                float angle = (i * 83f + seed * 41f) % 300f - 150f;       // around the body, skipping the open front
                if (Mathf.Abs(angle) < 22f) angle += 30f * Mathf.Sign(angle == 0f ? 1f : angle);
                float h = ((i * 0.37f + seed * 0.21f) % 1f - 0.5f) * 1.3f;  // -0.65 .. 0.65 of the height
                float k = Mathf.Sqrt(Mathf.Max(0.05f, 1f - h * h));
                float rad = angle * Mathf.Deg2Rad;
                var local = new Vector3(Mathf.Sin(rad) * radii.x * k, h * radii.y, Mathf.Cos(rad) * radii.z * k);
                var normal = new Vector3(local.x / (radii.x * radii.x), local.y / (radii.y * radii.y), local.z / (radii.z * radii.z)).normalized;
                kit.Ellipsoid(center + local, new Vector3(0.026f, 0.007f, 0.026f) * s, i % 2 == 0 ? a : b,
                    Quaternion.FromToRotation(Vector3.up, normal), segments: 7, rings: 4);
            }
        }

        private static bool HatCoversTop(HatStyle hat) => hat is HatStyle.Cap or HatStyle.CapBackwards or HatStyle.BucketHat or HatStyle.StrawHat or HatStyle.Bandana;

        private static void BuildHair(AvatarMeshKit kit, AvatarLook look, Color hair, float s)
        {
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * s;
            bool hatOn = HatCoversTop(look.Hat);
            Quaternion tilt = Quaternion.Euler(-28f, 0f, 0f);
            Vector3 center = V(0f, 0.145f, -0.004f);
            switch (look.Hair)
            {
                case HairStyle.Bald:
                    return;
                case HairStyle.Buzz:
                    if (!hatOn) kit.Dome(center, V(0.153f, 0.163f, 0.156f), 100f, hair, tilt);
                    return;
                case HairStyle.Afro:
                    if (!hatOn) kit.Dome(V(0f, 0.15f, -0.03f), V(0.225f, 0.215f, 0.225f), 105f, hair, Quaternion.Euler(-26f, 0f, 0f), segments: 16);
                    else kit.Ellipsoid(V(0f, 0.1f, -0.1f), V(0.2f, 0.13f, 0.12f), hair, segments: 14, rings: 8); // puffs out under the hat
                    return;
            }
            if (!hatOn) kit.Dome(center, V(0.162f, 0.172f, 0.165f), 100f, hair, tilt);
            switch (look.Hair)
            {
                case HairStyle.Spiky when !hatOn:
                    for (int i = 0; i < 8; i++)
                    {
                        float yaw = i * 45f + 20f;
                        float pitch = i % 2 == 0 ? 35f : 60f;
                        Vector3 dir = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
                        dir = (tilt * Vector3.up + dir * 0.9f).normalized;
                        Vector3 root = center + Vector3.Scale(dir, V(0.15f, 0.16f, 0.15f));
                        kit.Frustum(root, 0.04f * s, 0f, 0.085f * s, hair, Quaternion.FromToRotation(Vector3.up, dir), segments: 6);
                    }
                    break;
                case HairStyle.Long:
                    kit.Ellipsoid(V(0f, 0.05f, -0.085f), V(0.158f, 0.2f, 0.09f), hair, segments: 14, rings: 8);
                    foreach (float side in new[] { -1f, 1f })
                        kit.Ellipsoid(V(0.132f * side, 0.06f, -0.02f), V(0.04f, 0.13f, 0.075f), hair, segments: 8, rings: 6);
                    break;
                case HairStyle.Ponytail:
                    kit.Ellipsoid(V(0f, 0.2f, -0.16f), V(0.05f, 0.05f, 0.05f), hair, segments: 8, rings: 6);
                    kit.Limb(0.2f * s, 0.045f * s, 0.022f * s, hair, V(0f, 0.19f, -0.19f), Quaternion.Euler(-25f, 0f, 0f), segments: 8);
                    break;
                case HairStyle.Mohawk when !hatOn:
                    for (int i = 0; i < 6; i++)
                    {
                        float phi = Mathf.Lerp(40f, -75f, i / 5f) * Mathf.Deg2Rad;
                        var dir = new Vector3(0f, Mathf.Cos(phi), Mathf.Sin(phi));
                        kit.Ellipsoid(HeadCenter * s + dir * (0.17f * s), V(0.02f, 0.075f, 0.04f), hair,
                            Quaternion.FromToRotation(Vector3.up, dir), segments: 8, rings: 6);
                    }
                    break;
            }
        }

        private static void BuildHat(AvatarMeshKit kit, AvatarLook look, float s)
        {
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * s;
            Color c = look.HatTint;
            Color trim = look.HatColor == 1 ? AvatarLook.ClothColors[0] : AvatarLook.ClothColors[1];
            switch (look.Hat)
            {
                case HatStyle.Cap:
                case HatStyle.CapBackwards:
                    bool back = look.Hat == HatStyle.CapBackwards;
                    kit.Dome(V(0f, 0.16f, 0f), V(0.168f, 0.15f, 0.172f), 95f, c, Quaternion.Euler(back ? 8f : -8f, 0f, 0f));
                    kit.Ellipsoid(V(0f, 0.31f, back ? 0.012f : -0.012f), V(0.02f, 0.012f, 0.02f), trim, segments: 8, rings: 4);
                    kit.Disc(V(0f, 0.205f, back ? -0.165f : 0.165f), 0.105f * s, 0.014f * s, c,
                        Quaternion.Euler(back ? -12f : 12f, 0f, 0f), scale: new Vector2(1f, 0.85f));
                    break;
                case HatStyle.BucketHat:
                    kit.Frustum(V(0f, 0.19f, 0f), 0.168f * s, 0.14f * s, 0.12f * s, c);
                    kit.Frustum(V(0f, 0.175f, 0f), 0.25f * s, 0.168f * s, 0.035f * s, c, segments: 18); // sloped brim
                    break;
                case HatStyle.Visor:
                    kit.Torus(V(0f, 0.215f, 0.004f), 0.16f * s, 0.014f * s, c, Quaternion.Euler(-10f, 0f, 0f), scale: new Vector2(1f, 1.03f));
                    kit.Disc(V(0f, 0.215f, 0.17f), 0.1f * s, 0.012f * s, c, Quaternion.Euler(14f, 0f, 0f), scale: new Vector2(1.05f, 0.8f));
                    break;
                case HatStyle.StrawHat:
                    kit.Dome(V(0f, 0.2f, 0f), V(0.158f, 0.13f, 0.158f), 90f, c);
                    kit.Disc(V(0f, 0.2f, 0f), 0.34f * s, 0.014f * s, c, segments: 22);
                    kit.Frustum(V(0f, 0.207f, 0f), 0.159f * s, 0.157f * s, 0.035f * s, trim);
                    break;
                case HatStyle.Headband:
                    kit.Torus(V(0f, 0.21f, 0.008f), 0.155f * s, 0.018f * s, c, Quaternion.Euler(-14f, 0f, 0f), scale: new Vector2(1f, 1.03f));
                    break;
                case HatStyle.Bandana:
                    kit.Dome(V(0f, 0.145f, -0.004f), V(0.166f, 0.176f, 0.17f), 100f, c, Quaternion.Euler(-24f, 0f, 0f));
                    kit.Ellipsoid(V(0.02f, 0.13f, -0.175f), V(0.035f, 0.03f, 0.03f), c, segments: 8, rings: 5);
                    kit.Limb(0.08f * s, 0.022f * s, 0.012f * s, c, V(-0.01f, 0.12f, -0.18f), Quaternion.Euler(-20f, 0f, 15f), segments: 6);
                    break;
            }
        }

        private static void BuildGlasses(AvatarMeshKit kit, GlassesStyle style, float s)
        {
            if (style == GlassesStyle.None) return;
            Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * s;
            Color frame = style == GlassesStyle.Hearts ? new Color(0.95f, 0.3f, 0.55f) : Black;
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 eye = V(0.058f * side, 0.156f, 0.166f);
                switch (style)
                {
                    case GlassesStyle.Sunglasses:
                        kit.Ellipsoid(eye, V(0.047f, 0.034f, 0.012f), new Color(0.08f, 0.09f, 0.12f), segments: 12, rings: 6);
                        break;
                    case GlassesStyle.Round:
                        kit.Torus(eye, 0.037f * s, 0.006f * s, frame, Quaternion.Euler(90f, 0f, 0f), segments: 14, tubeSegments: 4);
                        break;
                    case GlassesStyle.Hearts:
                        Color lens = new(1f, 0.4f, 0.65f);
                        kit.Ellipsoid(eye + V(-0.016f, 0.009f, 0f), V(0.024f, 0.022f, 0.01f), lens, segments: 8, rings: 5);
                        kit.Ellipsoid(eye + V(0.016f, 0.009f, 0f), V(0.024f, 0.022f, 0.01f), lens, segments: 8, rings: 5);
                        kit.Ellipsoid(eye + V(0f, -0.012f, 0f), V(0.026f, 0.024f, 0.01f), lens, Quaternion.Euler(0f, 0f, 45f), segments: 4, rings: 3);
                        break;
                }
                kit.Box(V(0.146f * side, 0.162f, 0.08f), V(0.008f, 0.008f, 0.16f), frame); // temple arm
            }
            kit.Box(V(0f, 0.162f, 0.172f), V(0.03f, 0.009f, 0.009f), frame); // bridge
        }
    }
}
