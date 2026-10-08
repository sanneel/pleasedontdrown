using UnityEngine;

namespace PleaseDontDrown.Audio
{
    /// <summary>Original layered eating, drinking and air movement; short enough to follow each gesture.</summary>
    public static class ActionFoley
    {
        private static AudioClip[] _bites, _gulps, _punches, _throws, _blades;
        private static int _bite, _gulp, _punch, _throw, _blade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _bites = _gulps = _punches = _throws = _blades = null;
            _bite = _gulp = _punch = _throw = _blade = 0;
        }

        public static AudioClip Bite => Next(ref _bites, ref _bite, false);
        public static AudioClip Gulp => Next(ref _gulps, ref _gulp, true);
        public static AudioClip Punch => Swing(ref _punches, ref _punch, 0);
        public static AudioClip Throw => Swing(ref _throws, ref _throw, 1);
        public static AudioClip Blade => Swing(ref _blades, ref _blade, 2);

        private static AudioClip Next(ref AudioClip[] bank, ref int sequence, bool drink)
        {
            bank ??= new AudioClip[3];
            int variant = sequence++ % bank.Length;
            if (bank[variant] != null) return bank[variant];
            var rng = new System.Random((drink ? 7801 : 9203) + variant * 173);
            float low = 0, mid = 0;
            float length = drink ? .34f : .38f;
            return bank[variant] = SoundClip.Build((drink ? "DrinkGlug" : "CoconutBite") + variant, length, t =>
            {
                float noise = (float)rng.NextDouble() * 2 - 1;
                low += (noise - low) * .035f;
                mid += (noise - mid) * .28f;
                float result = 0;
                if (drink)
                {
                    // Three irregular bubbles followed by a soft throat swallow.
                    for (int i = 0; i < 3; i++)
                    {
                        float b = t - (.028f + i * .071f + variant * .003f);
                        if (b < 0) continue;
                        float phase = (420f - variant * 28f - i * 45f) * b - 1800f * b * b;
                        float envelope = Mathf.Exp(-b * (43 + i * 3)) * Mathf.Clamp01(b / .006f);
                        result += Mathf.Sin(2 * Mathf.PI * phase) * envelope * (.22f - i * .035f);
                    }
                    float swallow = t - .16f;
                    if (swallow > 0)
                        result += (low * 1.4f + Mathf.Sin(2 * Mathf.PI * 105 * swallow) * .12f)
                            * Mathf.Exp(-swallow * 27) * Mathf.Clamp01(swallow / .012f);
                    result += mid * .12f * Mathf.Sin(Mathf.PI * t / length);
                }
                else
                {
                    // A crisp first bite, smaller fractures, then a quiet soft chew.
                    for (int i = 0; i < 5; i++)
                    {
                        float b = t - (.015f + i * (.026f + variant * .002f));
                        if (b < 0) continue;
                        float envelope = Mathf.Exp(-b * (90 - i * 9)) * Mathf.Clamp01(b / .0015f);
                        result += ((noise - mid) * .38f + mid * .22f) * envelope * (1 - i * .12f);
                    }
                    float chew = t - .18f;
                    if (chew > 0)
                        result += (mid * .17f + low * .8f) * Mathf.Exp(-chew * 17) * Mathf.Clamp01(chew / .014f);
                    result += low * .65f * Mathf.Exp(-t * 24);
                }
                return result;
            });
        }

        private static AudioClip Swing(ref AudioClip[] bank, ref int sequence, int kind)
        {
            bank ??= new AudioClip[3];
            int variant = sequence++ % bank.Length;
            if (bank[variant] != null) return bank[variant];
            var rng = new System.Random(1811 + kind * 571 + variant * 37);
            float bass = 0, band = 0, air = 0;
            float length = kind == 1 ? .30f : kind == 2 ? .20f : .23f;
            return bank[variant] = SoundClip.Build($"AirSwing{kind}_{variant}", length, t =>
            {
                float u = t / length;
                float rush = Mathf.Exp(-Mathf.Pow((u - .38f) / .22f, 2));
                float noise = (float)rng.NextDouble() * 2 - 1;
                bass += (noise - bass) * .025f;
                band += (noise - band) * Mathf.Lerp(.06f, .32f, rush);
                air += (noise - air) * .64f;
                float cloth = (band - bass) * .15f * Mathf.Pow(1 - u, 3);
                float body = bass * (kind == 2 ? .25f : 1.7f);
                float hiss = (air - band) * (kind == 2 ? .50f : .19f);
                return (body + (band - bass) * .9f + hiss) * rush + cloth;
            });
        }
    }
}
