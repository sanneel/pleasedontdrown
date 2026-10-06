using System;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>Original beach foley inspired by the reference game's short, varied sound cues.</summary>
    public static class BeachAudio
    {
        private const int Rate = 44100;
        private static AudioClip[] _water, _pickup, _swim, _footsteps;
        private static AudioClip _drop, _throw, _equip, _swoosh, _menu, _hover, _bird, _surf, _dive, _emerge;
        private static AudioSource _local;
        private static readonly System.Collections.Generic.List<AudioSource> _oneShots = new();
        private static int _waterIndex, _pickupIndex, _swimIndex, _footstepIndex;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _water = _pickup = _swim = _footsteps = null;
            _drop = _throw = _equip = _swoosh = _menu = _hover = _bird = _surf = _dive = _emerge = null;
            _local = null;
            _oneShots.Clear();
            _waterIndex = _pickupIndex = _swimIndex = _footstepIndex = 0;
        }

        public static AudioClip WaterImpact(float strength)
        {
            _water ??= new AudioClip[9];
            int size = strength < 0.3f ? 0 : strength < 0.72f ? 1 : 2;
            int variant = (_waterIndex++ + UnityEngine.Random.Range(0, 2)) % 3;
            int index = size * 3 + variant;
            return _water[index] != null ? _water[index] : _water[index] = MakeWater(size, variant);
        }

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

        public static AudioClip SwimStroke
        {
            get
            {
                _swim ??= new AudioClip[3];
                int i = _swimIndex++ % 3;
                return _swim[i] != null ? _swim[i] : _swim[i] = MakeSwim(i);
            }
        }

        public static AudioClip Drop => _drop != null ? _drop : _drop = MakeImpact("ItemDrop", 0.34f, 140f, 0.26f);
        public static AudioClip Throw => _throw != null ? _throw : _throw = MakeSweep("ItemThrow", 0.30f, 42, 0.54f);
        public static AudioClip Equip => _equip != null ? _equip : _equip = MakeImpact("Equip", 0.25f, 340f, 0.18f);
        public static AudioClip PunchSwoosh => _swoosh != null ? _swoosh : _swoosh = MakeSweep("PunchSwoosh", 0.28f, 81, 0.75f);
        public static AudioClip MenuSelect => _menu != null ? _menu : _menu = MakeChime();
        public static AudioClip MenuHover => _hover != null ? _hover : _hover = Build("MenuHover", 0.13f, t =>
            Mathf.Sin(2f * Mathf.PI * (510f * t + 80f * t * t)) *
            Mathf.Sin(Mathf.PI * t / 0.13f) * 0.11f);
        public static AudioClip Bird => _bird != null ? _bird : _bird = MakeBird();
        public static AudioClip Surf => _surf != null ? _surf : _surf = MakeSurf();
        public static AudioClip Dive => _dive != null ? _dive : _dive = MakeWater(2, 2);
        public static AudioClip Emerge => _emerge != null ? _emerge : _emerge = MakeWater(0, 1);

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

        private static AudioClip MakeWater(int size, int variant)
        {
            float length = new[] { 0.55f, 1.05f, 1.55f }[size];
            var rng = new System.Random(190 + size * 17 + variant);
            float low = 0f, high = 0f;
            return Build($"Water{size}_{variant}", length, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                low += (n - low) * (0.18f + 0.06f * size);
                high += (n - high) * 0.55f;
                float impact = (low * 0.95f + high * 0.25f) * Mathf.Exp(-(9f - size * 1.9f) * t);
                float wash = low * 0.6f * Mathf.Exp(-(3.5f - size * 0.7f) * t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / length));
                float bubbles = 0f;
                for (int b = 0; b < 4 + size * 2; b++)
                {
                    float at = 0.09f + b * (0.065f + 0.02f * size) + variant * 0.009f;
                    float u = t - at;
                    if (u >= 0f && u < 0.08f)
                    {
                        float f = 250f + b * 62f + variant * 35f;
                        bubbles += Mathf.Sin(2f * Mathf.PI * (f * u + 260f * u * u)) * Mathf.Exp(-38f * u) * 0.12f;
                    }
                }
                return (impact + wash + bubbles) * Mathf.Clamp01(t / 0.002f) * (0.7f + size * 0.13f);
            });
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
                float snap = u >= 0f ? Mathf.Sin(2f * Mathf.PI * (540f + variant * 45f) * u) * Mathf.Exp(-32f * u) * 0.24f : 0f;
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

        private static AudioClip MakeSwim(int variant)
        {
            var rng = new System.Random(814 + variant);
            float low = 0f;
            return Build($"SwimStroke{variant}", 0.63f, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                low += (n - low) * 0.17f;
                float pull = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.5f)) * (0.55f + 0.2f * Mathf.Sin(t * 21f));
                float droplets = t > 0.24f ? n * Mathf.Exp(-16f * (t - 0.24f)) * 0.11f : 0f;
                return low * pull * 0.65f + droplets;
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
                float hit = Mathf.Sin(2f * Mathf.PI * (frequency * t - frequency * 0.9f * t * t)) * Mathf.Exp(-22f * t);
                return (hit * 0.48f + low * noiseGain * Mathf.Exp(-14f * t)) * Mathf.Clamp01(t / 0.002f);
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

        private static AudioClip MakeBird() => Build("BeachBird", 0.72f, t =>
        {
            float local = t < 0.27f ? t : t - 0.34f;
            if (local < 0f || local > 0.27f) return 0f;
            float phase = 1050f * local + 340f * local * local + 35f * Mathf.Sin(local * 37f);
            return Mathf.Sin(2f * Mathf.PI * phase) * Mathf.Sin(Mathf.PI * local / 0.27f) * 0.16f;
        });

        private static AudioClip MakeSurf()
        {
            var rng = new System.Random(942);
            float low = 0f, slow = 0f;
            const float length = 12f;
            return Build("BeachSurfLoop", length, t =>
            {
                float n = (float)(rng.NextDouble() * 2 - 1);
                low += (n - low) * 0.12f;
                slow += (n - slow) * 0.008f;
                float swell = 0.48f + 0.3f * Mathf.Sin(2f * Mathf.PI * t / 6f - 0.7f);
                float seam = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((length - t) / 0.25f);
                return (low * 0.31f + slow * 0.62f) * swell * seam;
            });
        }

        private static AudioClip Build(string name, float seconds, Func<float, float> wave)
        {
            var samples = new float[Mathf.CeilToInt(seconds * Rate)];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, Rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

}
