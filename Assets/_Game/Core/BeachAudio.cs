using System;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>Beach foley with recorded water sounds from the requested How to Fish reference.</summary>
    public static class BeachAudio
    {
        private const int Rate = 44100;
        private static AudioClip[] _pickup, _footsteps, _jump, _land;
        private static AudioClip _drop, _throw, _equip, _swoosh, _menu, _hover, _bird;
        private static AudioSource _local;
        private static readonly System.Collections.Generic.List<AudioSource> _oneShots = new();
        private static int _pickupIndex, _footstepIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _pickup = _footsteps = _jump = _land = null;
            _drop = _throw = _equip = _swoosh = _menu = _hover = _bird = null;
            _local = null;
            _oneShots.Clear();
            _pickupIndex = _footstepIndex = 0;
        }

        public static AudioClip WaterImpact(float strength) => Audio.WaterFoley.Impact(strength);

        public static AudioClip Pickup
        {
            get
            {
                _pickup ??= new AudioClip[3];
                int i = _pickupIndex++ % 3;
                return _pickup[i] != null ? _pickup[i] : _pickup[i] = MakePickup(i);
            }
        }

        public static AudioClip Footstep(World.SurfaceKind surface)
        {
            _footsteps ??= new AudioClip[9];
            int variant = _footstepIndex++ % 3;
            int index = Mathf.Clamp((int)surface, 0, 2) * 3 + variant;
            return _footsteps[index] != null ? _footsteps[index] :
                _footsteps[index] = MakeFootstep((int)surface, variant);
        }

        public static AudioClip SwimStroke => Audio.WaterFoley.Stroke;
        public static AudioClip Wade => Audio.WaterFoley.Step;

        public static AudioClip Jump(World.SurfaceKind surface) => Movement(surface, false);
        public static AudioClip Land(World.SurfaceKind surface) => Movement(surface, true);

        private static AudioClip Movement(World.SurfaceKind surface, bool landing)
        {
            _jump ??= new AudioClip[9];
            _land ??= new AudioClip[9];
            int variant = _footstepIndex++ % 3;
            int material = Mathf.Clamp((int)surface, 0, 2);
            int index = material * 3 + variant;
            var bank = landing ? _land : _jump;
            if (bank[index] != null) return bank[index];
            var rng = new System.Random(731 + index + (landing ? 30 : 0));
            float low = 0f, air = 0f;
            return bank[index] = Build($"{(landing ? "Land" : "Jump")}{material}_{variant}", landing ? 0.48f : 0.29f, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                low += (n - low) * 0.045f;
                air += (n - air) * (material == 0 ? 0.28f : 0.5f);
                float body = low * (landing ? 2.3f : 0.65f) * Mathf.Exp(-t * 18f);
                float grit = (air - low) * (material == 0 ? 0.7f : 0.38f) * Mathf.Exp(-t * (landing ? 13f : 20f));
                float knock = material == 0 ? 0f : Mathf.Sin(2f * Mathf.PI * (material == 1 ? 128f : 210f) * t) * Mathf.Exp(-t * 38f) * (landing ? 0.2f : 0.07f);
                float cloth = air * 0.13f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.26f));
                return body + grit + knock + cloth;
            });
        }

        public static AudioClip Drop => _drop != null ? _drop : _drop = MakeImpact("ItemDrop", 0.34f, 140f, 0.26f);
        public static AudioClip Throw => Audio.ActionFoley.Throw;
        public static AudioClip Equip => _equip != null ? _equip : _equip = MakeImpact("Equip", 0.25f, 340f, 0.18f);
        public static AudioClip PunchSwoosh => Audio.ActionFoley.Punch;
        public static AudioClip MenuSelect => _menu != null ? _menu : _menu = MakeChime();
        public static AudioClip MenuHover => _hover != null ? _hover : _hover = Build("MenuHover", 0.13f, t =>
            Mathf.Sin(2f * Mathf.PI * (510f * t + 80f * t * t)) *
            Mathf.Sin(Mathf.PI * t / 0.13f) * 0.11f);
        public static AudioClip Bird => _bird != null ? _bird : _bird = MakeBird();
        public static AudioClip Surf => Audio.WaterFoley.SeaLoop;
        public static AudioClip Dive => Audio.WaterFoley.Enter;
        public static AudioClip Emerge => Audio.WaterFoley.Exit;

        public static void PlayLocal(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            if (_local == null)
            {
                var go = new GameObject("Local Foley");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _local = go.AddComponent<AudioSource>();
                _local.playOnAwake = false;
                _local.spatialBlend = 0f;
            }
            _local.PlayOneShot(clip, volume);
        }

        /// <summary>A sound at a point in the world, as AudioSource.PlayClipAtPoint plays it, from reused sources.</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f)
        {
            if (clip == null) return;
            AudioSource source = null;
            for (int i = _oneShots.Count - 1; i >= 0; i--)
            {
                AudioSource candidate = _oneShots[i];
                if (candidate == null) _oneShots.RemoveAt(i);
                else if (!candidate.isPlaying)
                {
                    source = candidate;
                    break;
                }
            }
            if (source == null)
            {
                var go = new GameObject("One shot audio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                _oneShots.Add(source);
            }
            source.transform.position = position;
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        private static AudioClip MakePickup(int variant)
        {
            var rng = new System.Random(511 + variant);
            float filtered = 0f;
            return Build($"Pickup{variant}", 0.45f, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                filtered += (n - filtered) * 0.25f;
                float rustle = filtered * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.24f)) * (t < 0.24f ? 0.28f : 0f);
                float u = t - 0.105f;
                float snap = u >= 0f ? (filtered * 0.65f + Mathf.Sin(2f * Mathf.PI * (240f + variant * 25f) * u) * 0.08f) * Mathf.Clamp01(u / 0.003f) * Mathf.Exp(-48f * u) : 0f;
                return rustle + snap;
            });
        }

        private static AudioClip MakeFootstep(int surface, int variant)
        {
            var rng = new System.Random(1010 + surface * 23 + variant);
            float low = 0f;
            float length = surface == 0 ? 0.42f : 0.35f;
            return Build($"Footstep{surface}_{variant}", length, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                float cutoff = surface == 0 ? 0.2f : surface == 1 ? 0.32f : 0.65f;
                low += (n - low) * cutoff;
                float body = surface == 0 ? 0f :
                    Mathf.Sin(2f * Mathf.PI * (surface == 1 ? 155f : 265f) * t) * Mathf.Exp(-24f * t) * 0.3f;
                float grit = low * Mathf.Exp(-(surface == 0 ? 13f : 23f) * t) *
                    (0.52f + 0.12f * Mathf.Sin(t * (47f + variant * 8f)));
                float scuff = low * Mathf.Exp(-36f * Mathf.Max(0f, t - 0.11f)) *
                    (t >= 0.11f ? 0.1f : 0f);
                return (body + grit + scuff) * Mathf.Clamp01(t / 0.003f) * (surface == 0 ? 1.5f : 1f);
            });
        }

        private static AudioClip MakeImpact(string name, float length, float frequency, float noiseGain)
        {
            var rng = new System.Random((int)frequency);
            float low = 0f;
            return Build(name, length, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                low += (n - low) * 0.24f;
                float hit = (Mathf.Sin(2f * Mathf.PI * frequency * t) + 0.24f * Mathf.Sin(2f * Mathf.PI * frequency * 2.37f * t)) * Mathf.Exp(-35f * t);
                return (hit * 0.19f + low * noiseGain * 2f * Mathf.Exp(-18f * t)) * Mathf.Clamp01(t / 0.002f);
            });
        }

        private static AudioClip MakeSweep(string name, float length, int seed, float gain)
        {
            var rng = new System.Random(seed);
            float low = 0f;
            return Build(name, length, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                low += (n - low) * Mathf.Lerp(0.08f, 0.58f, t / length);
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / length));
                return low * env * gain;
            });
        }

        private static AudioClip MakeChime() => Build("MenuSelect", 0.27f, t =>
        {
            float ping = Mathf.Sin(2f * Mathf.PI * (680f * t + 180f * t * t));
            float overtone = Mathf.Sin(2f * Mathf.PI * 1240f * t) * 0.26f;
            return (ping + overtone) * Mathf.Exp(-17f * t) * Mathf.Clamp01(t / 0.003f) * 0.22f;
        });

        private static AudioClip MakeBird() => Build("BeachBird", 0.92f, BirdWave());

        private static Func<float, float> BirdWave()
        {
            float phase = 0f;
            return t =>
            {
                float local = t < 0.36f ? t : t - 0.49f;
                if (local < 0f || local > 0.36f) return 0f;
                float u = local / 0.36f;
                phase += 2f * Mathf.PI * (870f + 240f * Mathf.Sin(Mathf.PI * u) + 18f * Mathf.Sin(local * 43f)) / Rate;
                return (Mathf.Sin(phase) + 0.17f * Mathf.Sin(phase * 2f)) * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), 1.4f) * 0.13f;
            };
        }

        private static AudioClip Build(string name, float seconds, Func<float, float> wave)
        {
            return Audio.SoundClip.Build(name, seconds, wave);
        }
    }

}
