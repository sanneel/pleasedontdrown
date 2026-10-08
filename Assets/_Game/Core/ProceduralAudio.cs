using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// Original synthesized effects, mastered with click-free edges and peak headroom.
    /// Each clip is generated once and cached.
    /// </summary>
    public static class ProceduralAudio
    {
        private const int SampleRate = 44100;
        private static AudioClip _bell;
        private static AudioClip _click;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _bell = _click = _cough = _thump = null;
            _crunch = _rustle = _bonk = _creak = _shut = null;
            _breath = _zap = _punch = _cash = _engine = null;
            _shots = null;
            _suppressed = _dryFire = _aimIn = _aimOut = _magOut = _magIn = _rack = _impact = null;
            _steps = null;
            _cries = null;
            _kiss = _knifeSwish = _stab = null;
            _stepVariant = 0;
        }

        /// <summary>Brass hand bell: inharmonic partials with individual decay rates.</summary>
        // Explicit null checks: Unity can unload these clips, and "??=" doesn't see Unity's destroyed objects.
        public static AudioClip Bell => _bell != null ? _bell : _bell = Build("Bell", 1.8f, t =>
        {
            float f = 740f;
            float s = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-2.2f * t)
                      + 0.6f * Mathf.Sin(2f * Mathf.PI * f * 2.76f * t) * Mathf.Exp(-3.5f * t)
                      + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 5.40f * t) * Mathf.Exp(-6f * t)
                      + 0.2f * Mathf.Sin(2f * Mathf.PI * f * 8.93f * t) * Mathf.Exp(-9f * t);
            float strike = Mathf.Clamp01(t / 0.004f); // avoid a click at the very start
            return s * 0.35f * strike;
        });

        /// <summary>Short mechanical switch click.</summary>
        public static AudioClip Click => _click != null ? _click : _click = Build("Click", 0.06f, t =>
        {
            float noise = Mathf.PerlinNoise(t * 9000f, 0.37f) * 2f - 1f;
            return (noise * 0.6f + Mathf.Sin(2f * Mathf.PI * 2100f * t) * 0.4f) * Mathf.Exp(-70f * t) * 0.6f;
        });

        /// <summary>Compatibility entry point: always uses the recorded water bank.</summary>
        public static AudioClip Splash => BeachAudio.WaterImpact(0.5f);

        private static AudioClip[] _steps;
        private static int _stepVariant;

        /// <summary>A footstep for the surface, cycling through a few variants so steps don't sound identical.</summary>
        public static AudioClip Step(World.SurfaceKind kind)
        {
            if (_steps == null || _steps[0] == null)
            {
                _steps = new AudioClip[9];
                for (int v = 0; v < 3; v++)
                {
                    _steps[v] = SandStep(v);
                    _steps[3 + v] = WoodStep(v);
                    _steps[6 + v] = RockStep(v);
                }
            }
            _stepVariant = (_stepVariant + 1) % 3;
            return _steps[(int)kind * 3 + _stepVariant];
        }

        public static AudioClip WaterStep => BeachAudio.Wade;

        private static AudioClip SandStep(int v) => Noise($"SandStep{v}", 0.14f, 11 + v, 0.22f + 0.04f * v, 0.05f, 28f, 0.55f);

        private static AudioClip RockStep(int v) => Noise($"RockStep{v}", 0.07f, 31 + v, 0.9f, 0.4f, 60f, 0.5f);

        private static AudioClip WoodStep(int v)
        {
            var rng = new System.Random(51 + v);
            float f = 150f + 25f * v;
            return Build($"WoodStep{v}", 0.18f, t =>
            {
                float knock = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-28f * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * f * 2.7f * t) * Mathf.Exp(-45f * t);
                float scuff = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-70f * t) * 0.35f;
                return (knock * 0.6f + scuff) * Mathf.Clamp01(t / 0.002f);
            });
        }

        private static AudioClip[,] _cries;
        private static int _crySequence;
        private static readonly string[] RescueNames =
        {
            "Help", "OverHere", "CantSwim", "MySkis", "ThrowRing", "HaveKeys", "RescueMe", "ImOverHere"
        };
        private static readonly string[] RescueWords =
        {
            "Help!", "Over here!", "I can't swim!", "My skis!", "A ring! Throw me a ring!",
            "You can have the keys!", "Rescue me first!", "I'm over here!"
        };
        private static AudioClip[,] _rescueBarks;

        /// <summary>Three short, intelligible lines for a general rescue call.</summary>
        public static string RescueWordsAt(int phrase) => RescueWords[Mathf.Clamp(phrase, 0, 2)];

        /// <summary>Local TTS recording. A missing recording falls back to a wordless cry.</summary>
        public static AudioClip RescueBark(int voice, string words)
        {
            int phrase = -1;
            for (int i = 0; i < RescueWords.Length; i++)
                if (string.Equals(words?.Trim(), RescueWords[i], System.StringComparison.OrdinalIgnoreCase))
                { phrase = i; break; }
            if (phrase < 0) return Cry(voice);
            _rescueBarks ??= new AudioClip[2, RescueNames.Length];
            int register = voice < 2 ? 0 : 1;
            if (_rescueBarks[register, phrase] == null)
            {
                string suffix = register == 0 ? "_low" : "_high";
                _rescueBarks[register, phrase] = Resources.Load<AudioClip>("Audio/Rescue/" + RescueNames[phrase] + suffix);
            }
            return _rescueBarks[register, phrase] != null ? _rescueBarks[register, phrase] : Cry(voice);
        }
        private static AudioClip _cough;
        private static AudioClip _thump;

        /// <summary>
        /// Three short wordless distress calls per voice. Subtitles carry the actual words.
        /// The variation applies to both rescue tourists and story NPCs without stacking voices.
        /// </summary>
        public static AudioClip Cry(int voice)
        {
            _cries ??= new AudioClip[4, 3];
            int next = _crySequence++;
            int v = Mathf.Clamp(voice, 0, 3), variant = next % 3;
            if (_cries[v, variant] == null)
            {
                float[] pitches = { 135f, 170f, 220f, 255f };
                _cries[v, variant] = BuildCry(v, variant, pitches[v]);
            }
            return _cries[v, variant];
        }

        private static AudioClip BuildCry(int v, int variant, float f0)
        {
            float length = variant == 0 ? 0.62f : variant == 1 ? 0.83f : 0.72f;
            var rng = new System.Random(900 + v * 3 + variant);
            float phase = 0f;
            float breath = 0f;
            return Build($"Cry{v}_{variant}", length, t =>
            {
                float u = t / length;
                float glide = variant switch
                {
                    0 => 1f + 0.08f * Mathf.Sin(Mathf.PI * u) - 0.06f * u,
                    1 => 0.93f + 0.18f * Mathf.Sin(Mathf.PI * u) - 0.05f * u,
                    _ => 1.04f - 0.13f * u + 0.05f * Mathf.Sin(2f * Mathf.PI * u)
                };
                float f = f0 * glide * (1f + 0.009f * Mathf.Sin(2f * Mathf.PI * 6.5f * t));
                phase += 2f * Mathf.PI * f / SampleRate;
                float f1 = Mathf.Lerp(variant == 2 ? 710f : 550f, variant == 1 ? 800f : 680f, u);
                float f2 = Mathf.Lerp(1850f, variant == 2 ? 1400f : 1200f, u);
                float s = 0f;
                for (int h = 1; h <= 16; h++)
                {
                    float fh = f * h;
                    float amp = Mathf.Exp(-Sq((fh - f1) / 180f)) + 0.6f * Mathf.Exp(-Sq((fh - f2) / 260f)) + 0.05f / h;
                    s += amp * Mathf.Sin(phase * h) / Mathf.Sqrt(h);
                }
                breath += ((float)(rng.NextDouble() * 2.0 - 1.0) - breath) * 0.3f;
                float envelope = Mathf.Clamp01(t / 0.035f) * Mathf.Clamp01((length - t) / 0.17f);
                return (s * 0.28f + breath * 0.13f) * envelope;
            });
        }

        private static float Sq(float x) => x * x;

        /// <summary>Two rough coughs: noisy bursts over a low grunt.</summary>
        public static AudioClip Cough
        {
            get
            {
                if (_cough != null) return _cough;
                var rng = new System.Random(4242);
                float low = 0f;
                _cough = Build("Cough", 0.75f, t =>
                {
                    float local = t < 0.3f ? t : t - 0.34f;
                    if (local < 0f) return 0f;
                    float env = Mathf.Clamp01(local / 0.01f) * Mathf.Exp(-11f * local);
                    low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * 0.25f;
                    float rasp = low * (0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 105f * local));
                    float grunt = Mathf.Sin(2f * Mathf.PI * 125f * local) * Mathf.Exp(-28f * local) * 0.08f;
                    return (rasp * 1.7f + grunt) * env * 0.65f;
                });
                return _cough;
            }
        }

        /// <summary>Soft chest-compression thump.</summary>
        public static AudioClip Thump => _thump != null ? _thump : _thump = Build("Thump", 0.2f, t =>
        {
            float f = Mathf.Lerp(95f, 55f, t / 0.2f);
            return Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-22f * t) * Mathf.Clamp01(t / 0.003f) * 0.8f;
        });

        private static AudioClip _crunch, _rustle, _bonk, _creak, _shut;

        /// <summary>A bite of something crunchy.</summary>
        public static AudioClip Crunch => _crunch != null ? _crunch : _crunch = Noise("Crunch", 0.16f, 91, 0.75f, 0.3f, 26f, 0.8f);

        /// <summary>Palm fronds shaking.</summary>
        public static AudioClip Rustle => _rustle != null ? _rustle : _rustle = Build("Rustle", 1.1f, RustleWave());

        private static System.Func<float, float> RustleWave()
        {
            var rng = new System.Random(57);
            float low = 0f;
            return t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.45f;
                float flutter = 0.55f + 0.45f * Mathf.Sin(t * 43f) * Mathf.Sin(t * 17f);
                return (noise - low) * 0.5f * flutter * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.1f));
            };
        }

        /// <summary>Hollow coconut knock.</summary>
        public static AudioClip Bonk => _bonk != null ? _bonk : _bonk = Build("Bonk", 0.35f, t =>
        {
            float f = Mathf.Lerp(430f, 250f, t / 0.35f);
            return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * f * 2.3f * t) * 0.3f) * Mathf.Exp(-14f * t) * Mathf.Clamp01(t / 0.002f) * 0.8f;
        });

        /// <summary>Old door hinge.</summary>
        public static AudioClip Creak => _creak != null ? _creak : _creak = Build("Creak", 0.55f, CreakWave());

        private static System.Func<float, float> CreakWave()
        {
            var rng = new System.Random(163);
            float phase = 0f, friction = 0f;
            return t =>
            {
                phase += 2f * Mathf.PI * (180f + 38f * Mathf.Sin(t * 9f) + 8f * Mathf.Sin(t * 51f)) / SampleRate;
                friction += ((float)(rng.NextDouble() * 2 - 1) - friction) * 0.2f;
                float strain = Mathf.Sin(phase) * 0.1f + Mathf.Sin(phase * 2.01f) * 0.04f;
                return (strain + friction * 0.35f) * Mathf.Sin(Mathf.PI * t / 0.55f) * (0.7f + 0.3f * Mathf.Sin(t * 35f));
            };
        }

        /// <summary>Wooden door closing.</summary>
        public static AudioClip Shut => _shut != null ? _shut : _shut = Build("Shut", 0.25f, t =>
        {
            float thud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(90f, 60f, t / 0.25f) * t) * Mathf.Exp(-18f * t);
            float click = (Mathf.PerlinNoise(t * 7000f, 0.2f) * 2f - 1f) * Mathf.Exp(-80f * t);
            return (thud * 0.8f + click * 0.4f) * Mathf.Clamp01(t / 0.002f);
        });

        private static AudioClip _breath, _zap, _punch, _cash, _engine;

        /// <summary>A long exhale (rescue breath).</summary>
        public static AudioClip Breath => _breath != null ? _breath : _breath = Build("Breath", 0.8f, BreathWave());

        private static System.Func<float, float> BreathWave()
        {
            var rng = new System.Random(311);
            float low = 0f;
            return t =>
            {
                low += ((float)(rng.NextDouble() * 2.0 - 1.0) - low) * 0.12f;
                return low * 1.6f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.8f));
            };
        }

        private static AudioClip _kiss;

        /// <summary>Mouth-to-mouth: a controlled breath with a soft seal and release.</summary>
        public static AudioClip Kiss => _kiss != null ? _kiss : _kiss = Build("Kiss", 1.0f, KissWave());

        private static System.Func<float, float> KissWave()
        {
            var rng = new System.Random(733);
            float air = 0f, low = 0f;
            return t =>
            {
                float s = 0f;
                if (t < 0.82f)
                {
                    air += ((float)(rng.NextDouble() * 2.0 - 1.0) - air) * 0.08f;
                    low += (air - low) * 0.03f;
                    float env = Mathf.Clamp01(t / 0.07f) * Mathf.Clamp01((0.82f - t) / 0.06f);
                    float pressure = 0.8f + 0.35f * (t / 0.82f); // pushing harder toward the end
                    s = (air - low) * 1.2f * env * pressure;
                }
                float u = t - 0.84f;
                if (u > 0f)
                {
                    float pop = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-u * 90f);
                    float click = Mathf.Sin(2f * Mathf.PI * 1900f * u) * Mathf.Exp(-u * 140f);
                    s += (pop * 0.09f + click * 0.025f) * Mathf.Clamp01(u / 0.004f);
                }
                return s;
            };
        }

        /// <summary>Defibrillator: a rising whine, then a buzzing crack.</summary>
        public static AudioClip Zap => _zap != null ? _zap : _zap = Build("Zap", 0.7f, t =>
        {
            if (t < 0.35f) return Mathf.Sin(2f * Mathf.PI * (600f + 2400f * t) * t) * 0.12f * (t / 0.35f);
            float u = t - 0.35f;
            float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 120f * u)) * 0.5f + (Mathf.PerlinNoise(u * 3000f, 0.5f) * 2f - 1f) * 0.6f;
            return buzz * Mathf.Exp(-9f * u) * 0.6f;
        });

        /// <summary>Cartoon punch: a slap of noise over a low thud.</summary>
        public static AudioClip Punch => _punch != null ? _punch : _punch = Build("Punch", 0.25f, t =>
        {
            float thud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(140f, 60f, t / 0.25f) * t) * Mathf.Exp(-16f * t);
            float slap = (Mathf.PerlinNoise(t * 9000f, 0.7f) * 2f - 1f) * Mathf.Exp(-60f * t);
            return (thud * 0.9f + slap * 0.7f) * Mathf.Clamp01(t / 0.002f);
        });

        // ------------------------------------------------------------------ guns

        private static AudioClip[] _shots;
        private static AudioClip _suppressed, _dryFire, _aimIn, _aimOut, _magOut, _magIn, _rack, _impact;

        /// <summary>A gunshot per kind of gun: a sharp crack, a low punch and a tail that rings out.</summary>
        public static AudioClip Shot(Combat.GunSound kind)
        {
            _shots ??= new AudioClip[5];
            int i = (int)kind;
            if (_shots[i] != null) return _shots[i];
            // length, crack brightness, crack decay, punch start/end Hz, tail decay, tail gain
            (float len, float bright, float crackDecay, float f0, float f1, float tailDecay, float tail) = kind switch
            {
                Combat.GunSound.Smg => (0.85f, 0.9f, 55f, 170f, 70f, 8f, 0.55f),
                Combat.GunSound.Shotgun => (1.8f, 0.7f, 30f, 95f, 38f, 4f, 0.9f),
                Combat.GunSound.Rifle => (1.5f, 0.97f, 45f, 130f, 50f, 5f, 0.75f),
                Combat.GunSound.Sniper => (2.4f, 1f, 35f, 90f, 32f, 2.8f, 0.9f),
                _ => (1.1f, 0.93f, 50f, 150f, 60f, 6f, 0.65f)
            };
            var rng = new System.Random(700 + i);
            float low = 0f, band = 0f;
            return _shots[i] = Build($"Shot{kind}", len, t =>
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (n - low) * Mathf.Lerp(bright, 0.05f, Mathf.Clamp01(t / len));
                band += (low - band) * 0.25f;
                float crack = n * Mathf.Exp(-crackDecay * t);
                float punch = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(f0, f1, Mathf.Clamp01(t / 0.12f)) * t) * Mathf.Exp(-22f * t);
                float ring = band * 2.2f * Mathf.Exp(-tailDecay * t) * tail;
                return (crack * 0.8f + punch * 0.9f + ring) * Mathf.Clamp01(t / 0.0015f) * 0.38f;
            });
        }

        /// <summary>Through a suppressor: a dull "thwp" and the action clacking.</summary>
        public static AudioClip GunshotSuppressed
        {
            get
            {
                if (_suppressed != null) return _suppressed;
                var rng = new System.Random(733);
                float low = 0f;
                return _suppressed = Build("ShotSuppressed", 0.3f, t =>
                {
                    float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                    low += (n - low) * 0.12f;
                    float puff = low * 3f * Mathf.Exp(-24f * t);
                    float clack = t > 0.035f ? Mathf.Sin(2f * Mathf.PI * 1900f * t) * Mathf.Exp(-90f * (t - 0.035f)) * 0.35f : 0f;
                    return (puff + clack) * Mathf.Clamp01(t / 0.003f) * 0.55f;
                });
            }
        }

        /// <summary>Empty: the hammer falls on nothing.</summary>
        public static AudioClip DryFire => _dryFire != null ? _dryFire : _dryFire = Build("DryFire", 0.08f, t =>
            (Mathf.Sin(2f * Mathf.PI * 2600f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 4100f * t) * 0.3f) * Mathf.Exp(-70f * t));

        public static AudioClip AimIn => _aimIn != null ? _aimIn : _aimIn = Cloth("AimIn", 0.13f, 810, 1.2f);
        public static AudioClip AimOut => _aimOut != null ? _aimOut : _aimOut = Cloth("AimOut", 0.11f, 820, 0.9f);

        /// <summary>Magazine out: a latch click and a metal slide.</summary>
        public static AudioClip MagOut => _magOut != null ? _magOut : _magOut = Build("MagOut", 0.22f, t =>
        {
            float click = Mathf.Sin(2f * Mathf.PI * 3100f * t) * Mathf.Exp(-80f * t);
            float scrape = (Mathf.PerlinNoise(t * 7000f, 0.3f) * 2f - 1f) * Mathf.Exp(-14f * t) * (t > 0.02f ? 0.5f : 0f);
            return click * 0.6f + scrape;
        });

        /// <summary>Magazine in: a solid clack.</summary>
        public static AudioClip MagIn => _magIn != null ? _magIn : _magIn = Build("MagIn", 0.14f, t =>
        {
            float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(900f, 500f, t / 0.14f) * t) * Mathf.Exp(-40f * t);
            float hit = (Mathf.PerlinNoise(t * 11000f, 0.8f) * 2f - 1f) * Mathf.Exp(-120f * t);
            return body * 0.46f + hit * 0.52f;
        });

        /// <summary>Slide, pump or bolt: back and forward.</summary>
        public static AudioClip Rack => _rack != null ? _rack : _rack = Build("Rack", 0.28f, t =>
        {
            float Clack(float at, float f) => t < at ? 0f : Mathf.Sin(2f * Mathf.PI * f * (t - at)) * Mathf.Exp(-70f * (t - at)) +
                                              (Mathf.PerlinNoise((t - at) * 9000f, at) * 2f - 1f) * Mathf.Exp(-110f * (t - at)) * 0.6f;
            return Clack(0f, 1400f) * 0.48f + Clack(0.13f, 1100f) * 0.54f;
        });

        /// <summary>A bullet thudding into something.</summary>
        public static AudioClip BulletImpact
        {
            get
            {
                if (_impact != null) return _impact;
                var rng = new System.Random(747);
                float low = 0f;
                return _impact = Build("BulletImpact", 0.12f, t =>
                {
                    float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                    low += (n - low) * 0.3f;
                    return (low * 2f + n * 0.3f * Mathf.Exp(-120f * t)) * Mathf.Exp(-35f * t);
                });
            }
        }

        private static AudioClip Cloth(string name, float seconds, int seed, float pitch)
        {
            var rng = new System.Random(seed);
            float low = 0f;
            return Build(name, seconds, t =>
            {
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (n - low) * 0.18f * pitch;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / seconds));
                return low * 1.6f * env;
            });
        }

        /// <summary>Cash register "ka-ching".</summary>
        public static AudioClip Cash => _cash != null ? _cash : _cash = Build("Cash", 0.6f, t =>
        {
            float clunk = t < 0.08f ? (Mathf.PerlinNoise(t * 5000f, 0.1f) * 2f - 1f) * Mathf.Exp(-40f * t) : 0f;
            float u = Mathf.Max(0f, t - 0.07f);
            float ding = (Mathf.Sin(2f * Mathf.PI * 1760f * u) + 0.5f * Mathf.Sin(2f * Mathf.PI * 2637f * u)) * Mathf.Exp(-6f * u) * (t > 0.07f ? 0.35f : 0f);
            return clunk * 0.5f + ding;
        });

        /// <summary>Small two-stroke engine, loopable (pitch it up with the throttle).</summary>
        public static AudioClip Engine => _engine != null ? _engine : _engine = Build("Engine", 1f, t =>
        {
            float f = 55f;
            float pulse = Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.PI * f * t)), 6f);
            float rasp = Mathf.Sin(2f * Mathf.PI * f * 4f * t) * 0.3f + Mathf.Sin(2f * Mathf.PI * f * 7f * t) * 0.15f;
            return (pulse * 0.7f + rasp * pulse) * 0.5f;
        });

        /// <summary>Low-passed noise burst: cutoff falls from <paramref name="brightStart"/> to <paramref name="brightEnd"/>.</summary>
        private static AudioClip _knifeSwish, _stab;

        /// <summary>A blade cutting the air: a bright rising-then-falling hiss.</summary>
        public static AudioClip KnifeSwish
        {
            get
            {
                return Audio.ActionFoley.Blade;
            }
        }

        /// <summary>A blade going in: a short dull thud with a wet scrape.</summary>
        public static AudioClip Stab
        {
            get
            {
                if (_stab != null) return _stab;
                var rng = new System.Random(733);
                float low = 0f;
                return _stab = Build("Stab", 0.2f, t =>
                {
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    low += (noise - low) * 0.18f;
                    float thud = Mathf.Sin(t * 2f * Mathf.PI * Mathf.Lerp(150f, 70f, t / 0.2f)) * Mathf.Exp(-t * 28f);
                    float scrape = low * 1.6f * Mathf.Exp(-t * 16f);
                    return (thud * 0.8f + scrape) * Mathf.Clamp01(t / 0.002f);
                });
            }
        }

        private static AudioClip Noise(string name, float seconds, int seed, float brightStart, float brightEnd, float decay, float gain)
        {
            var rng = new System.Random(seed);
            float low = 0f;
            return Build(name, seconds, t =>
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * Mathf.Lerp(brightStart, brightEnd, t / seconds);
                return low * gain * 2f * Mathf.Exp(-decay * t) * Mathf.Clamp01(t / 0.003f);
            });
        }

        private static AudioClip Build(string name, float seconds, System.Func<float, float> wave)
        {
            return Audio.SoundClip.Build(name, seconds, wave);
        }
    }
}
