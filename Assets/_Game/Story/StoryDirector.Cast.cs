using PleaseDontDrown.Avatars;

namespace PleaseDontDrown.Story
{
    /// <summary>Who the islands' people are and what they look like (the model each one wears).</summary>
    public partial class StoryDirector
    {
        public static AvatarLook SandyLook => new()
        {
            Figure = 1, Build = 0, Height = 2, Skin = 0, Hair = HairStyle.Ponytail, HairColor = 3,
            Top = TopStyle.Hawaiian, TopColor = 3, Bottom = BottomStyle.Trousers, BottomColor = 12,
            Body = AvatarLook.Bodies.Sandy // the Meshy model; the fields above are the code-built stand-in
        };

        public static AvatarLook ReceptionistLook => new()
        {
            Figure = 1, Build = 1, Height = 1, Skin = 4, Hair = HairStyle.Long, HairColor = 0,
            Top = TopStyle.TShirt, TopColor = 7, Bottom = BottomStyle.Trousers, BottomColor = 14,
            Body = AvatarLook.Bodies.HotelAttendantA // her own Tripo model: the hotel's flight-attendant style uniform
        };

        // The robber has his own model (sunglasses, headband, striped shirt); the pirates are the Tripo crew (Pete the
        // marksman, One-Eyed Olga, Big Sal, Salty Bill the quartermaster, round again). The code-built fields are the fallback.
        public static AvatarLook RobberLook => new()
        {
            Figure = 0, Build = 0, Height = 2, Skin = 2, Hair = HairStyle.Short, HairColor = 0,
            Top = TopStyle.TShirt, TopColor = 14, Bottom = BottomStyle.Trousers, BottomColor = 7,
            Hat = HatStyle.Bandana, HatColor = 14, Glasses = GlassesStyle.Sunglasses, Face = FacialHair.Stubble,
            Body = AvatarLook.Bodies.Robber
        };

        public static AvatarLook PirateLook(int i) => new()
        {
            Figure = (byte)(i % 4 == 1 ? 1 : 0), Build = (byte)(i % 2 == 0 ? 2 : 3), Height = (byte)(1 + i % 3), Skin = (byte)(2 + i % 4),
            Hair = HairStyle.Long, HairColor = (byte)(i % 3), Top = i % 2 == 0 ? TopStyle.Tank : TopStyle.None, TopColor = 1,
            Bottom = BottomStyle.Trousers, BottomColor = 11, Hat = HatStyle.Bandana, HatColor = 0, Face = i % 3 == 2 ? FacialHair.Mustache : FacialHair.Beard,
            Body = PirateCrew[((i % PirateCrew.Length) + PirateCrew.Length) % PirateCrew.Length]
        };

        private static readonly byte[] PirateCrew =
            { AvatarLook.Bodies.PirateMarksman, AvatarLook.Bodies.PirateGirl, AvatarLook.Bodies.PirateFat, AvatarLook.Bodies.PirateQuartermaster };

        /// <summary>A pirate's look by its body.</summary>
        public static AvatarLook PirateLookOf(byte body)
        {
            AvatarLook look = PirateLook(0);
            look.Body = body;
            look.Figure = (byte)(AvatarLook.Bodies.IsFeminine(body) ? 1 : 0);
            return look;
        }

        public static AvatarLook DirectorLook => new()
        {
            Figure = 0, Build = 1, Height = 2, Skin = 2, Hair = HairStyle.Short, HairColor = 5,
            Top = TopStyle.TShirt, TopColor = 1, Bottom = BottomStyle.Trousers, BottomColor = 7,
            Body = AvatarLook.Bodies.HotelDirector
        };

        public static AvatarLook RentalLook
        {
            get
            {
                AvatarLook look = AvatarLook.RandomTourist(715, 0);
                look.Body = AvatarLook.Bodies.Variant(AvatarLook.Bodies.TouristBuddy, 4);
                return look;
            }
        }

        /// <summary>A police officer: the lifeguard body in navy, a cap, a moustache.</summary>
        public static AvatarLook PoliceLook
        {
            get
            {
                AvatarLook look = AvatarLook.Lifeguard;
                look.TopColor = 7; // navy
                look.BottomColor = 7;
                look.Hat = HatStyle.Cap;
                look.HatColor = 7;
                look.Glasses = GlassesStyle.None;
                look.Face = FacialHair.Mustache;
                look.Skin = 1;
                return look;
            }
        }

        private static AvatarLook GuestLook(int seed, int figure) => AvatarLook.RandomTourist(seed, figure);
    }
}
