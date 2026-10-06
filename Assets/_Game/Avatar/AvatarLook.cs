using System;
using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    public enum HairStyle : byte { Bald, Buzz, Short, Spiky, Long, Ponytail, Afro, Mohawk }
    public enum TopStyle : byte { None, Tank, LifeguardTank, TShirt, Hawaiian, RashGuard, Swimsuit, Bikini }
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
        public byte Figure;       // 0 masculine, 1 feminine (hips, waist, bust with jiggle bones)
        public byte Body;         // 0 code-built from the fields above, else a generated AvatarBody (see AvatarBodies)
        // The funny player body (Bodies.Goofy): 0 is always the model as it was generated.
        public byte HeadSize;     // 0 normal, 3 small (the big sizes 1 and 2 are gone: see Tame)
        public byte Belly;        // BellyNames: normal, round, beach ball, flat
        public byte Nose;         // NoseNames: normal, big, clown, button
        public byte Eyes;         // EyeNames (googly): normal, huge, cross-eyed, tiny pupils
        public byte Teeth;        // TeethNames: pearly, bucky, gold, rotten

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

        /// <summary>Ids of the generated bodies (Editor/MeshyCharacters.cs bakes them).</summary>
        public static class Bodies
        {
            public const byte Sandy = 1;
            public const byte SandyBoss = 2;
            public const byte TouristRed = 3;      // red bikini
            public const byte TouristSporty = 4;   // turquoise sports bikini
            public const byte TouristPurple = 5;   // purple bikini, curvy
            public const byte TouristBuddy = 6;    // sunburnt dad in flowery trunks
            public const byte Robber = 7;          // the thief: his own model, hands taken out of his pockets (ArtSource/Tools/robber_arms.py)
            /// <summary>The players' funny lifeguard (googly eyes, buck teeth, pot belly): recoloured and reshaped per look.</summary>
            public const byte Goofy = 8;

            /// <summary>
            /// Look-alikes of the tourists (ArtSource/Tools/make_variants.py): other skin, hair, eyes, outfit colour, face
            /// and build. Id = VariantFirst + 16 * base index + (n - 1), n = 1..VariantsPerBase (files *_v01.glb ...).
            /// </summary>
            public const byte VariantFirst = 32;
            public const int VariantsPerBase = 10;
            public static readonly byte[] VariantBases = { TouristRed, TouristSporty, TouristPurple, TouristBuddy };

            public static byte Variant(byte baseBody, int n)
            {
                int index = System.Array.IndexOf(VariantBases, baseBody);
                return index < 0 || n <= 0 ? baseBody : (byte)(VariantFirst + 16 * index + (n - 1));
            }

            /// <summary>The base tourist or one of its look-alikes (a missing body falls back to the code-built one).</summary>
            /// <summary>Women's bodies (the look's Figure should say feminine: bust jiggle, CPR lines).</summary>
            public static bool IsFeminine(byte body) =>
                body is >= Sandy and <= TouristPurple || body >= VariantFirst && body < VariantFirst + 48;

            public static byte PickVariant(System.Random rng, byte baseBody) => Variant(baseBody, rng.Next(VariantsPerBase + 1));
        }

        public static readonly string[] BuildNames = { "Slim", "Regular", "Broad", "Round" };
        public static readonly string[] FigureNames = { "Masculine", "Feminine" };
        public bool Feminine => Figure == 1;
        public static readonly string[] HeightNames = { "Short", "Medium", "Tall", "Very tall" };
        public const byte SmallHead = 3;
        public static string HeadSizeName(byte size) => size == SmallHead ? "Small" : "Normal";

        /// <summary>
        /// The hats anyone may wear: snug ones only. Big hats (straw, bucket, cowboy, pirate, crown, party hat,
        /// headphones), like big heads, changed the whole character and got in the way of everything (the kiss of
        /// life, held things, the camera).
        /// </summary>
        public static readonly HatStyle[] Hats =
            { HatStyle.None, HatStyle.Cap, HatStyle.CapBackwards, HatStyle.Visor, HatStyle.Headband, HatStyle.Bandana, HatStyle.Beanie };

        /// <summary>No big head and no big hat, whatever was saved or sent.</summary>
        public AvatarLook Tame()
        {
            AvatarLook look = this;
            if (Array.IndexOf(Hats, look.Hat) < 0) look.Hat = HatStyle.None;
            if (look.HeadSize != SmallHead) look.HeadSize = 0;
            return look;
        }
        public static readonly string[] BellyNames = { "Normal", "Round", "Beach ball", "Flat" };
        public static readonly string[] NoseNames = { "Normal", "Big", "Clown", "Button" };
        public static readonly string[] EyeNames = { "Googly", "Huge googly", "Cross-eyed", "Tiny pupils" };
        public static readonly string[] TeethNames = { "Pearly", "Bucky", "Gold", "Rotten" };
        public bool IsGoofy => Body == Bodies.Goofy;

        /// <summary>The station uniform on the goofy lifeguard: red shorts, white tank, whistle (the code-built fields are the classic body's).</summary>
        public static AvatarLook Lifeguard => new()
        {
            Build = 1, Height = 1, Skin = 2, Hair = HairStyle.Short, HairColor = 1,
            Top = TopStyle.LifeguardTank, TopColor = 1, Bottom = BottomStyle.Shorts, BottomColor = 0,
            Hat = HatStyle.None, HatColor = 0, Glasses = GlassesStyle.None, Face = FacialHair.None,
            Extras = AvatarExtras.Whistle, Body = Bodies.Goofy
        };

        /// <summary>The classic code-built lifeguard (cap on backwards, sunscreen nose).</summary>
        public static AvatarLook ClassicLifeguard
        {
            get
            {
                AvatarLook look = Lifeguard;
                look.Body = 0;
                look.Hat = HatStyle.CapBackwards;
                look.Extras |= AvatarExtras.Sunscreen;
                return look;
            }
        }

        /// <summary>Same seed, same tourist on every machine: loud shirts, hats, the occasional pair of arm floaties.</summary>
        public static AvatarLook RandomTourist(int seed) => RandomTourist(seed, -1);

        /// <param name="figure">0 masculine, 1 feminine, -1 decided by the seed.</param>
        public static AvatarLook RandomTourist(int seed, int figure)
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
                Hat = (HatStyle)Pick(rng, 0, 0, 0, 1, 4, 4, 8, 8, 6, 7),
                HatColor = (byte)rng.Next(ClothColors.Length),
                Glasses = (GlassesStyle)Pick(rng, 0, 0, 0, 1, 1, 2, 3),
                Face = (FacialHair)Pick(rng, 0, 0, 0, 1, 2, 3)
            };
            bool feminine = figure >= 0 ? figure == 1 : rng.NextDouble() < 0.5;
            if (feminine)
            {
                // Beach outfits: mostly bikinis, some swimsuits; longer hair, no beards, a bit less wide.
                look.Figure = 1;
                look.Top = (TopStyle)Pick(rng, 7, 7, 7, 6);
                look.Hair = (HairStyle)Pick(rng, 4, 4, 5, 5, 2, 6);
                look.Face = FacialHair.None;
                look.Build = (byte)Pick(rng, 0, 0, 1, 1, 3);
                look.Bottom = BottomStyle.Trunks;
                if (look.Hat is HatStyle.Cap or HatStyle.CapBackwards or HatStyle.Bandana) look.Hat = HatStyle.Visor;
            }
            if (look.Top == TopStyle.Swimsuit) look.Face = FacialHair.None;
            if (rng.NextDouble() < 0.3) look.Extras |= AvatarExtras.Floaties;
            if (rng.NextDouble() < 0.2) look.Extras |= AvatarExtras.Sunscreen;
            if (look.TopColor == look.BottomColor) look.BottomColor = (byte)((look.BottomColor + 7) % ClothColors.Length);
            // Every tourist is a generated model (a base or one of its look-alikes); the code-built fields above are only
            // the fallback if the model library is missing.
            look.Body = feminine
                ? Bodies.PickVariant(rng, (byte)Pick(rng, Bodies.TouristRed, Bodies.TouristSporty, Bodies.TouristPurple))
                : Bodies.PickVariant(rng, Bodies.TouristBuddy);
            return look;
        }

        public static AvatarLook Random(System.Random rng)
        {
            AvatarLook look = RandomTourist(rng.Next());
            // Players are the goofy lifeguard, as silly as the dice say.
            look.Body = Bodies.Goofy;
            look.HeadSize = (byte)Pick(rng, 0, 0, 0, SmallHead);
            look.Belly = (byte)rng.Next(4);
            look.Nose = (byte)Pick(rng, 0, 0, 1, 2, 3);
            look.Eyes = (byte)rng.Next(4);
            look.Teeth = (byte)rng.Next(4);
            look.Hat = (HatStyle)Pick(rng, 0, 0, 1, 2, 3, 4, 5, 6, 7);
            look.Extras = (AvatarExtras)rng.Next(8) | AvatarExtras.Whistle;
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

        // Tells a real look from 0 / garbage, and which layout it is. 0xA7 (top byte): the old layout, 49 bits with an
        // 8-bit Body. 0xB (top nibble): Body in 7 bits (ids up to 127) and the goofy body's shape after it, 58 bits.
        private const ulong OldMarker = 0xA7UL << 56;
        private const ulong Marker = 0xBUL << 60;

        private static readonly int[] OldWidths = { 2, 2, 3, 3, 3, 3, 4, 2, 4, 3, 4, 2, 2, 3, 1, 8 };
        private static readonly int[] Widths = { 2, 2, 3, 3, 3, 3, 4, 2, 4, 3, 4, 2, 2, 3, 1, 7, 2, 2, 2, 2, 2 };

        public ulong Pack()
        {
            int[] values = { Build, Height, Skin, (int)Hair, HairColor, (int)Top, TopColor, (int)Bottom, BottomColor, (int)Hat, HatColor, (int)Glasses, (int)Face, (int)Extras, Figure, Body,
                HeadSize, Belly, Nose, Eyes, Teeth };
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
            int[] widths = (packed & (0xFUL << 60)) == Marker ? Widths : (packed & (0xFFUL << 56)) == OldMarker ? OldWidths : null;
            if (widths == null) return Lifeguard;
            var v = new int[Widths.Length];
            int shift = 0;
            for (int i = 0; i < widths.Length; i++)
            {
                v[i] = (int)((packed >> shift) & ((1UL << widths[i]) - 1));
                shift += widths[i];
            }
            return new AvatarLook
            {
                Build = (byte)v[0], Height = (byte)v[1], Skin = (byte)v[2], Hair = (HairStyle)v[3], HairColor = (byte)v[4],
                Top = (TopStyle)Mathf.Min(v[5], (int)TopStyle.Bikini), TopColor = (byte)v[6], Bottom = (BottomStyle)v[7],
                BottomColor = (byte)v[8], Hat = (HatStyle)v[9], HatColor = (byte)v[10], Glasses = (GlassesStyle)v[11],
                Face = (FacialHair)v[12], Extras = (AvatarExtras)v[13], Figure = (byte)v[14], Body = (byte)v[15],
                HeadSize = (byte)v[16], Belly = (byte)v[17], Nose = (byte)v[18], Eyes = (byte)v[19], Teeth = (byte)v[20]
            }.Tame();
        }

        public bool Equals(AvatarLook other) => Pack() == other.Pack();
        public override bool Equals(object obj) => obj is AvatarLook other && Equals(other);
        public override int GetHashCode() => Pack().GetHashCode();
    }
}
