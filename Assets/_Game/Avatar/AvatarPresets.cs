namespace PleaseDontDrown.Avatars
{
    public static class AvatarPresets
    {
        public static readonly string[] Names = { "Rescue crew", "Party animal", "Mister Moustache", "Bandana bandit", "Heart throb", "Winter swimmer", "Disco guard", "Sunset surfer" };

        public static AvatarLook Apply(int index, AvatarLook current)
        {
            // Keep the player's chosen skin and body/face shapes when trying on an outfit.
            var look = current;
            look.Body = AvatarLook.Bodies.Goofy;
            look.Face = FacialHair.None;
            look.Extras = AvatarExtras.Whistle;
            switch (index)
            {
                case 0: look.Hat = HatStyle.Cap; look.Glasses = GlassesStyle.Aviators; look.TopColor = 1; look.BottomColor = 0; look.HatColor = 0; break;
                case 1: look.Hat = HatStyle.Headband; look.Glasses = GlassesStyle.Stars; look.TopColor = 8; look.BottomColor = 5; look.HatColor = 4; look.Extras |= AvatarExtras.Floaties; break;
                case 2: look.Hat = HatStyle.Visor; look.Glasses = GlassesStyle.Sunglasses; look.Face = FacialHair.Mustache; look.TopColor = 12; look.BottomColor = 7; look.HatColor = 11; break;
                case 3: look.Hat = HatStyle.Bandana; look.Glasses = GlassesStyle.Round; look.Face = FacialHair.Beard; look.TopColor = 1; look.BottomColor = 14; look.HatColor = 14; break;
                case 4: look.Hat = HatStyle.Headband; look.Glasses = GlassesStyle.Hearts; look.TopColor = 5; look.BottomColor = 1; look.HatColor = 2; break;
                case 5: look.Hat = HatStyle.Beanie; look.Glasses = GlassesStyle.Goggles; look.TopColor = 6; look.BottomColor = 7; look.HatColor = 3; break;
                case 6: look.Hat = HatStyle.Beanie; look.Glasses = GlassesStyle.Stars; look.TopColor = 14; look.BottomColor = 4; look.HatColor = 8; break;
                default: look.Hat = HatStyle.CapBackwards; look.Glasses = GlassesStyle.Sport; look.TopColor = 3; look.BottomColor = 8; look.HatColor = 7; break;
            }
            return look;
        }
    }
}
