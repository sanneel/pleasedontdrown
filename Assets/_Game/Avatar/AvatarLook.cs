using System;
using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    public enum HairStyle : byte { Bald, Buzz, Short, Spiky, Long, Ponytail, Afro, Mohawk }
    public enum TopStyle : byte { None, Tank, LifeguardTank, TShirt, Hawaiian, RashGuard, Swimsuit }
    public enum BottomStyle : byte { Trunks, Shorts, BoardShorts, Trousers }
    public enum HatStyle : byte { None, Cap, CapBackwards, BucketHat, Visor, StrawHat, Headband, Bandana }
    public enum GlassesStyle : byte { None, Sunglasses, Round, Hearts }
    public enum FacialHair : byte { None, Mustache, Beard, Stubble }

    [Flags]
    public enum AvatarExtras : byte { None = 0, Whistle = 1, Sunscreen = 2, Floaties = 4 }

    /// <summary>
    /// Everything that makes one character look different: body, colours, clothes, hat, glasses.
    /// Packs into a single ulong for the network and PlayerPrefs. Colours are palette indices.
    /// </summary>
    [Serializable]
    public struct AvatarLook : IEquatable<AvatarLook>
    {
        public byte Build;        // 0 slim, 1 regular, 2 broad, 3 round
        public byte Height;       // 0..3 short -> tall
        public byte Skin;         // SkinTones
        public HairStyle Hair;
        public byte HairColor;    // HairColors
        public TopStyle Top;
        public byte TopColor;     // ClothColors
        public BottomStyle Bottom;
        public byte BottomColor;  // ClothColors
        public HatStyle Hat;
        public byte HatColor;     // ClothColors
        public GlassesStyle Glasses;
        public FacialHair Face;
        public AvatarExtras Extras;

        public static readonly Color[] SkinTones =
        {
            new(1f, 0.86f, 0.74f), new(1f, 0.72f, 0.6f) /* sunburnt */, new(0.93f, 0.74f, 0.56f), new(0.82f, 0.6f, 0.42f),
            new(0.66f, 0.46f, 0.31f), new(0.5f, 0.34f, 0.23f), new(0.36f, 0.24f, 0.16f), new(0.98f, 0.8f, 0.66f)
        };

        public static readonly Color[] HairColors =
        {
            new(0.12f, 0.08f, 0.06f), new(0.36f, 0.22f, 0.11f), new(0.62f, 0.4f, 0.18f), new(0.95f, 0.82f, 0.45f),
            new(0.78f, 0.32f, 0.12f), new(0.72f, 0.72f, 0.74f), new(0.95f, 0.95f, 0.95f), new(0.25f, 0.55f, 0.95f)
        };

        public static readonly Color[] ClothColors =
        {
            new(0.9f, 0.18f, 0.14f) /* rescue red */, new(0.98f, 0.97f, 0.94f) /* white */, new(1f, 0.84f, 0.22f) /* sun yellow */,
            new(1f, 0.55f, 0.12f) /* orange */, new(1f, 0.42f, 0.62f) /* pink */, new(0.62f, 0.38f, 0.92f) /* purple */,
            new(0.2f, 0.45f, 0.9f) /* blue */, new(0.13f, 0.2f, 0.42f) /* navy */, new(0.2f, 0.8f, 0.78f) /* turquoise */,
            new(0.3f, 0.78f, 0.32f) /* green */, new(0.16f, 0.42f, 0.22f) /* dark green */, new(0.55f, 0.36f, 0.2f) /* brown */,
            new(0.84f, 0.74f, 0.52f) /* khaki */, new(0.55f, 0.56f, 0.6f) /* grey */, new(0.14f, 0.14f, 0.16f) /* black */,
            new(0.7f, 0.9f, 1f) /* sky */
        };

        public static readonly string[] BuildNames = { "Slim", "Regular", "Broad", "Round" };
        public static readonly string[] HeightNames = { "Short", "Medium", "Tall", "Very tall" };

        /// <summary>The station uniform: red shorts, white tank with a red stripe, red cap on backwards, whistle.</summary>
        public static AvatarLook Lifeguard => new()
        {
            Build = 1, Height = 1, Skin = 2, Hair = HairStyle.Short, HairColor = 1,
            Top = TopStyle.LifeguardTank, TopColor = 1, Bottom = BottomStyle.Shorts, BottomColor = 0,
            Hat = HatStyle.CapBackwards, HatColor = 0, Glasses = GlassesStyle.None, Face = FacialHair.None,
            Extras = AvatarExtras.Whistle | AvatarExtras.Sunscreen
        };

        /// <summary>Same seed, same tourist on every machine: loud shirts, hats, the occasional pair of arm floaties.</summary>
        public static AvatarLook RandomTourist(int seed)
        {
            var rng = new System.Random(seed);
            var look = new AvatarLook
            {
                Build = (byte)Pick(rng, 0, 1, 1, 2, 3, 3),
                Height = (byte)rng.Next(4),
                Skin = (byte)rng.Next(SkinTones.Length),
                Hair = (HairStyle)rng.Next(8),
                HairColor = (byte)rng.Next(HairColors.Length - 1),
                Top = (TopStyle)Pick(rng, 0, 1, 3, 3, 4, 4, 4, 6),
                TopColor = (byte)rng.Next(ClothColors.Length),
                Bottom = (BottomStyle)Pick(rng, 0, 1, 1, 2, 2, 3),
                BottomColor = (byte)rng.Next(ClothColors.Length),
                Hat = (HatStyle)Pick(rng, 0, 0, 0, 1, 3, 3, 5, 5, 6, 7),
                HatColor = (byte)rng.Next(ClothColors.Length),
                Glasses = (GlassesStyle)Pick(rng, 0, 0, 0, 1, 1, 2, 3),
                Face = (FacialHair)Pick(rng, 0, 0, 0, 1, 2, 3)
            };
            if (look.Top == TopStyle.Swimsuit) look.Face = FacialHair.None;
            if (rng.NextDouble() < 0.3) look.Extras |= AvatarExtras.Floaties;
            if (rng.NextDouble() < 0.2) look.Extras |= AvatarExtras.Sunscreen;
            if (look.TopColor == look.BottomColor) look.BottomColor = (byte)((look.BottomColor + 7) % ClothColors.Length);
            return look;
        }

        public static AvatarLook Random(System.Random rng)
        {
            AvatarLook look = RandomTourist(rng.Next());
            look.Extras &= ~AvatarExtras.Floaties;
            return look;
        }

        private static int Pick(System.Random rng, params int[] options) => options[rng.Next(options.Length)];

        public Color SkinColor => SkinTones[Skin % SkinTones.Length];
        public Color HairTint => HairColors[HairColor % HairColors.Length];
        public Color TopTint => ClothColors[TopColor % ClothColors.Length];
        public Color BottomTint => ClothColors[BottomColor % ClothColors.Length];
        public Color HatTint => ClothColors[HatColor % ClothColors.Length];
        public bool Has(AvatarExtras extra) => (Extras & extra) != 0;

        // ------------------------------------------------------------------ packing

        private const ulong Marker = 0xA7UL << 56; // tells a real look from 0 / garbage

        private static readonly int[] Widths = { 2, 2, 3, 3, 3, 3, 4, 2, 4, 3, 4, 2, 2, 3 };

        public ulong Pack()
        {
            int[] values = { Build, Height, Skin, (int)Hair, HairColor, (int)Top, TopColor, (int)Bottom, BottomColor, (int)Hat, HatColor, (int)Glasses, (int)Face, (int)Extras };
            ulong packed = 0;
            int shift = 0;
            for (int i = 0; i < Widths.Length; i++)
            {
                packed |= ((ulong)values[i] & ((1UL << Widths[i]) - 1)) << shift;
                shift += Widths[i];
            }
            return packed | Marker;
        }

        public static AvatarLook Unpack(ulong packed)
        {
            if ((packed & (0xFFUL << 56)) != Marker) return Lifeguard;
            var v = new int[Widths.Length];
            int shift = 0;
            for (int i = 0; i < Widths.Length; i++)
            {
                v[i] = (int)((packed >> shift) & ((1UL << Widths[i]) - 1));
                shift += Widths[i];
            }
            return new AvatarLook
            {
                Build = (byte)v[0], Height = (byte)v[1], Skin = (byte)v[2], Hair = (HairStyle)v[3], HairColor = (byte)v[4],
                Top = (TopStyle)Mathf.Min(v[5], (int)TopStyle.Swimsuit), TopColor = (byte)v[6], Bottom = (BottomStyle)v[7],
                BottomColor = (byte)v[8], Hat = (HatStyle)v[9], HatColor = (byte)v[10], Glasses = (GlassesStyle)v[11],
                Face = (FacialHair)v[12], Extras = (AvatarExtras)v[13]
            };
        }

        public bool Equals(AvatarLook other) => Pack() == other.Pack();
        public override bool Equals(object obj) => obj is AvatarLook other && Equals(other);
        public override int GetHashCode() => Pack().GetHashCode();
    }
}
