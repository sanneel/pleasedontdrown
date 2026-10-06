using System.Collections.Generic;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
using UnityEngine;
using UnityEngine.Rendering;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// Every bullet in flight on this machine. Bullets are plain data (no GameObjects): stepped at the physics rate
    /// with a sphere cast along each step (they drop with gravity and take time to arrive), drawn as glowing tracers in
    /// one instanced batch. Every machine flies its own copy of each shot; only the shooter's copy reports hits
    /// (<see cref="Weapon"/>), everyone else's just shows impacts. Shots that arrive late over the network fly faster
    /// for a moment until they are where the shooter's are.
    /// Also owns the shared effects: muzzle flashes, ejected casings, impact puffs and bullet holes.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class ProjectileSystem : MonoBehaviour
    {
        private sealed class Projectile
        {
            public Weapon Weapon;
            public PlayerHub Shooter;
            public bool Local;
            public ProjectileKind Kind;
            public Vector3 Position, Previous, Velocity, Spawn, VisualOffset;
            public float Gravity, Radius, Age, CatchUp, Range;
            public int Damage;
        }

        private const int MaxHoles = 300;
        private const float HoleLife = 60f;
        private const float MaxAge = 4f;

        private static ProjectileSystem _instance;

        private readonly List<Projectile> _live = new();
        private readonly Stack<Projectile> _pool = new();
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Matrix4x4[] _tracers = new Matrix4x4[128];
        private readonly Matrix4x4[] _holes = new Matrix4x4[MaxHoles];
        private readonly float[] _holeTimes = new float[MaxHoles];
        private int _holeCount, _holeNext;
        private Mesh _tracerMesh, _holeMesh;
        private Material _tracerMaterial, _holeMaterial;
        private ParticleSystem _puffs, _flashes, _casings;
        private Light _flashLight;
        private float _flashLightUntil;

        public static ProjectileSystem Instance
        {
            get
            {
                if (_instance == null) _instance = new GameObject("Projectiles").AddComponent<ProjectileSystem>();
                return _instance;
            }
        }

        public static int LiveCount => _instance != null ? _instance._live.Count : 0;

        private static bool CanDraw => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null && SystemInfo.supportsInstancing;

        /// <summary>'shotlog': print where every bullet ends up (tests).</summary>
        private static bool _log;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _log = false;
            DevCommands.Register("shotlog", "<0|1>", "Print where every bullet ends up (tests).", args => _log = args.Length == 0 || DevCommands.ParseFloat(args, 0) > 0.5f, cheat: true);
        }

        private void Awake()
        {
            _instance = this;
            _tracerMesh = BoxMesh();
            _holeMesh = QuadMesh();
            _tracerMaterial = InstancedMaterial("ProjectileTracer", new Color(1f, 0.85f, 0.35f));
            _holeMaterial = InstancedMaterial("BulletHole", new Color(0.12f, 0.1f, 0.09f));
            Texture2D soft = SoftDot();
            _puffs = MakeParticles("ImpactPuffs", soft, 800, 0.9f, false);
            _flashes = MakeParticles("MuzzleFlashes", soft, 200, 0f, false);
            _casings = MakeParticles("Casings", soft, 300, 1.6f, true);
            var lightGo = new GameObject("MuzzleLight");
            lightGo.transform.SetParent(transform, false);
            _flashLight = lightGo.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.range = 7f;
            _flashLight.intensity = 4f;
            _flashLight.color = new Color(1f, 0.78f, 0.45f);
            _flashLight.shadows = LightShadows.None;
            _flashLight.enabled = false;
        }

        // ------------------------------------------------------------------ firing

        /// <summary>
        /// Adds one shot (one or more projectiles). <paramref name="origin"/> is where the bullets really start (the
        /// shooter's eye, so they go where the crosshair is); the tracers start at <paramref name="muzzle"/> and blend onto
        /// the real path over the first metres. <paramref name="catchUp"/>: seconds this shot is behind the shooter's.
        /// </summary>
        public void Fire(Weapon weapon, PlayerHub shooter, bool local, Vector3 origin, Vector3 muzzle, Vector3[] directions,
            float speed, float gravity, ProjectileKind kind, int damage, float catchUp, float range)
        {
            foreach (Vector3 dir in directions)
            {
                Projectile p = _pool.Count > 0 ? _pool.Pop() : new Projectile();
                p.Weapon = weapon;
                p.Shooter = shooter;
                p.Local = local;
                p.Kind = kind;
                p.Position = p.Previous = p.Spawn = origin;
                p.Velocity = dir.normalized * speed;
                p.VisualOffset = muzzle - origin;
                p.Gravity = gravity;
                p.Radius = kind == ProjectileKind.Heavy ? 0.035f : kind == ProjectileKind.Pellet ? 0.025f : 0.02f;
                p.Age = 0f;
                p.CatchUp = Mathf.Clamp(catchUp, 0f, 0.6f);
                p.Damage = damage;
                p.Range = range;
                _live.Add(p);
            }
        }

        /// <summary>Spreads <paramref name="count"/> directions in a cone round <paramref name="forward"/>, the same on every machine for one seed.</summary>
        public static Vector3[] SpreadDirections(Vector3 forward, float coneDegrees, int seed, int count)
        {
            var rng = new System.Random(seed);
            var result = new Vector3[Mathf.Max(1, count)];
            Quaternion frame = Quaternion.LookRotation(forward);
            for (int i = 0; i < result.Length; i++)
            {
                // Uniform over the cone's disc: shotguns fill it evenly, single bullets mostly land near the middle.
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float radius = Mathf.Sqrt((float)rng.NextDouble()) * coneDegrees;
                if (count == 1) radius *= (float)rng.NextDouble();
                result[i] = frame * (Quaternion.Euler(Mathf.Sin(angle) * radius, Mathf.Cos(angle) * radius, 0f) * Vector3.forward);
            }
            return result;
        }

        // ------------------------------------------------------------------ flight

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                Projectile p = _live[i];
                // Late shots fly up to four times as fast until they have caught up.
                float extra = p.CatchUp > 0f ? Mathf.Min(p.CatchUp, dt * 3f) : 0f;
                p.CatchUp -= extra;
                if (!Step(p, dt + extra)) Remove(i);
            }
        }

        /// <returns>False once the projectile is done (hit something, fell in the sea, flew too far).</returns>
        private bool Step(Projectile p, float dt)
        {
            Vector3 move = p.Velocity * dt;
            float distance = move.magnitude;
            p.Previous = p.Position;
            p.Age += dt;
            if (distance < 1e-5f) return false;
            Vector3 dir = move / distance;

            if (FindHit(p, dir, distance, out RaycastHit hit))
            {
                if (_log) Debug.Log($"[Shot] {(p.Local ? "ours" : "theirs")} hit {hit.collider.name} ({hit.collider.GetType().Name}) at {hit.point:F2}, {(hit.point - p.Spawn).magnitude:F1} m");
                p.Position = hit.distance > 0f ? hit.point : p.Position;
                OnHit(p, hit, dir);
                return false;
            }

            Vector3 next = p.Position + move;
            if (WaterSurface.Exists)
            {
                float water = WaterSurface.HeightAt(next);
                if (next.y < water && p.Position.y >= water - 0.05f)
                {
                    // Into the sea: a splash where it went in, and that's it.
                    float t = Mathf.Clamp01((p.Position.y - water) / Mathf.Max(1e-4f, p.Position.y - next.y));
                    Vector3 entry = Vector3.Lerp(p.Position, next, t);
                    SplashFx.Spawn(new Vector3(entry.x, water, entry.z), p.Kind == ProjectileKind.Heavy ? 0.5f : 0.25f);
                    if (_log) Debug.Log($"[Shot] into the sea at {entry:F2}, {(entry - p.Spawn).magnitude:F1} m");
                    p.Position = entry;
                    return false;
                }
            }
            p.Position = next;
            p.Velocity += Vector3.down * (p.Gravity * dt);
            bool alive = p.Age < MaxAge && (p.Position - p.Spawn).sqrMagnitude < p.Range * p.Range;
            if (!alive && _log) Debug.Log($"[Shot] flew out at {p.Position:F1}");
            return alive;
        }

        private bool FindHit(Projectile p, Vector3 dir, float distance, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(p.Position, p.Radius, dir, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (h.distance >= bestDistance || IsShooter(p, h.collider)) continue;
                bestDistance = h.distance;
                best = h;
            }
            if (bestDistance < float.MaxValue && best.distance <= 0f) best.point = p.Position; // started inside it
            return bestDistance < float.MaxValue;
        }

        private static bool IsShooter(Projectile p, Collider c)
        {
            if (p.Shooter == null) return false;
            if (c == p.Shooter.BodyCollider) return true;
            PlayerHub owner = c.GetComponentInParent<PlayerHub>();
            if (owner == p.Shooter) return true;
            Items.Item held = p.Shooter.Hands != null ? p.Shooter.Hands.HeldItem : null;
            return held != null && held.OwnsCollider(c);
        }

        private void OnHit(Projectile p, RaycastHit hit, Vector3 dir)
        {
            Vector3 normal = hit.normal.sqrMagnitude > 0.5f ? hit.normal : -dir;
            bool character = DamageUtil.Find(hit.collider) != null || hit.collider.GetComponentInParent<PlayerHub>() != null ||
                             hit.collider.GetComponentInParent<Rescue.VictimBody>() != null;
            Color color = character ? new Color(1f, 0.93f, 0.85f) : SurfaceColor(hit.collider);
            float size = p.Kind == ProjectileKind.Heavy ? 1.6f : p.Kind == ProjectileKind.Pellet ? 0.7f : 1f;
            Puff(hit.point, normal, color, Mathf.RoundToInt(6 * size), size);
            if (!character && hit.collider.attachedRigidbody == null) AddHole(hit.point, normal, 0.055f * size);
            BeachAudio.PlayAt(ProceduralAudio.BulletImpact, hit.point, p.Kind == ProjectileKind.Pellet ? 0.35f : 0.6f);
            if (p.Local && p.Weapon != null) p.Weapon.OnProjectileHit(hit, dir, p.Damage);
        }

        private static Color SurfaceColor(Collider c) => SurfaceType.Of(c) switch
        {
            SurfaceKind.Wood => new Color(0.62f, 0.45f, 0.28f),
            SurfaceKind.Rock => new Color(0.62f, 0.62f, 0.6f),
            _ => new Color(0.93f, 0.84f, 0.6f)
        };

        private void Remove(int index)
        {
            Projectile p = _live[index];
            p.Weapon = null;
            p.Shooter = null;
            _live[index] = _live[_live.Count - 1];
            _live.RemoveAt(_live.Count - 1);
            _pool.Push(p);
        }

        // ------------------------------------------------------------------ drawing

        private void LateUpdate()
        {
            if (_flashLight.enabled && Time.time > _flashLightUntil) _flashLight.enabled = false;
            if (!CanDraw) return; // headless servers and tests: nothing to draw with

            // Tracers: between the last two physics positions, starting from the muzzle and sliding onto the true path.
            float blend = Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
            if (_tracers.Length < _live.Count) _tracers = new Matrix4x4[Mathf.NextPowerOfTwo(_live.Count)];
            int n = 0;
            foreach (Projectile p in _live)
            {
                Vector3 head = Vector3.Lerp(p.Previous, p.Position, blend) + p.VisualOffset * Mathf.Clamp01(1f - p.Age / 0.09f);
                float speed = p.Velocity.magnitude;
                if (speed < 1f) continue;
                Vector3 dir = p.Velocity / speed;
                float length = Mathf.Clamp(speed * (p.Kind == ProjectileKind.Heavy ? 0.022f : 0.014f), 0.3f, 3.2f);
                float width = p.Kind == ProjectileKind.Heavy ? 0.035f : p.Kind == ProjectileKind.Pellet ? 0.018f : 0.022f;
                length = Mathf.Min(length, (head - (p.Spawn + p.VisualOffset)).magnitude + 0.05f); // no tail sticking out behind the gun
                _tracers[n++] = Matrix4x4.TRS(head - dir * (length * 0.5f), Quaternion.LookRotation(dir), new Vector3(width, width, length));
            }
            if (n > 0)
                Graphics.RenderMeshInstanced(new RenderParams(_tracerMaterial) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false },
                    _tracerMesh, 0, _tracers, n);

            // Bullet holes, oldest first out.
            bool anyHole = false;
            for (int i = 0; i < _holeCount; i++)
            {
                if (Time.time - _holeTimes[i] > HoleLife) _holes[i] = Matrix4x4.zero;
                else anyHole = true;
            }
            if (anyHole)
                Graphics.RenderMeshInstanced(new RenderParams(_holeMaterial) { shadowCastingMode = ShadowCastingMode.Off },
                    _holeMesh, 0, _holes, _holeCount);
        }

        private void AddHole(Vector3 point, Vector3 normal, float size)
        {
            Quaternion rot = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            _holes[_holeNext] = Matrix4x4.TRS(point + normal * 0.004f, rot, Vector3.one * size);
            _holeTimes[_holeNext] = Time.time;
            _holeNext = (_holeNext + 1) % MaxHoles;
            _holeCount = Mathf.Max(_holeCount, _holeNext == 0 ? MaxHoles : _holeNext);
        }

        // ------------------------------------------------------------------ effects

        /// <summary>A burst of dust / splinters / sparks thrown off a surface.</summary>
        public void Puff(Vector3 point, Vector3 normal, Color color, int count, float size = 1f)
        {
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                emit.position = point + normal * 0.02f;
                emit.velocity = (normal + Random.insideUnitSphere * 0.8f).normalized * Random.Range(1f, 3.2f) * Mathf.Sqrt(size);
                emit.startSize = Random.Range(0.05f, 0.13f) * size;
                emit.startLifetime = Random.Range(0.3f, 0.65f);
                emit.startColor = Color.Lerp(color, Color.white, Random.Range(0f, 0.25f));
                _puffs.Emit(emit, 1);
            }
        }

        /// <summary>Flash at the muzzle (and a short light, unless suppressed).</summary>
        public void MuzzleFlash(Vector3 point, Vector3 forward, float size, bool suppressed)
        {
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            int count = suppressed ? 1 : 3;
            for (int i = 0; i < count; i++)
            {
                emit.position = point + forward * (0.03f + i * 0.05f * size);
                emit.velocity = forward * Random.Range(0.5f, 1.5f);
                emit.startSize = (suppressed ? 0.1f : Random.Range(0.18f, 0.3f)) * size * (1f - i * 0.2f);
                emit.startLifetime = 0.05f;
                emit.startColor = suppressed ? new Color(1f, 0.9f, 0.7f, 0.5f) : new Color(1f, Random.Range(0.75f, 0.9f), 0.4f);
                _flashes.Emit(emit, 1);
            }
            if (suppressed) return;
            _flashLight.transform.position = point + forward * 0.1f;
            _flashLight.intensity = 3f * size;
            _flashLight.enabled = true;
            _flashLightUntil = Time.time + 0.045f;
        }

        /// <summary>A spent case flicked out of the ejection port.</summary>
        public void EjectCasing(Vector3 point, Vector3 right, Vector3 up, bool shell)
        {
            var emit = new ParticleSystem.EmitParams
            {
                applyShapeToPosition = false,
                position = point,
                velocity = right * Random.Range(1.4f, 2.4f) + up * Random.Range(1f, 1.8f) + Random.insideUnitSphere * 0.3f,
                startSize = shell ? 0.03f : 0.016f,
                startLifetime = 1.4f,
                startColor = shell ? new Color(0.75f, 0.12f, 0.1f) : new Color(0.78f, 0.58f, 0.2f)
            };
            _casings.Emit(emit, 1);
        }

        // ------------------------------------------------------------------ building bits

        private ParticleSystem MakeParticles(string name, Texture2D texture, int max, float gravity, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = gravity;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, collide ? 0.8f : 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            if (collide)
            {
                ParticleSystem.CollisionModule collision = ps.collision;
                collision.enabled = true;
                collision.type = ParticleSystemCollisionType.World;
                collision.mode = ParticleSystemCollisionMode.Collision3D;
                collision.bounce = 0.35f;
                collision.dampen = 0.4f;
                collision.quality = ParticleSystemCollisionQuality.Low;
            }
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { mainTexture = texture }; // always in builds
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private static Material InstancedMaterial(string resource, Color fallback)
        {
            Material source = Resources.Load<Material>(resource);
            var mat = source != null ? new Material(source) : new Material(Shader.Find("Sprites/Default")) { color = fallback };
            mat.enableInstancing = true;
            return mat;
        }

        private static Texture2D SoftDot()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = new Vector2(x - size * 0.5f + 0.5f, y - size * 0.5f + 0.5f).magnitude / (size * 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(1.4f - d * 1.5f)));
                }
            tex.Apply();
            return tex;
        }

        private static Mesh BoxMesh()
        {
            // A unit box round the origin (the tracer's middle), stretched per bullet.
            var mesh = new Mesh { name = "Tracer" };
            Vector3[] corners =
            {
                new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
                new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f)
            };
            mesh.vertices = corners;
            mesh.triangles = new[]
            {
                0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5
            };
            mesh.RecalculateNormals();
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2f);
            return mesh;
        }

        private static Mesh QuadMesh()
        {
            // Faces -Z like Unity's quad: turned to look into the surface, it shows on the outside.
            var mesh = new Mesh { name = "BulletHole" };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            const int sides = 8;
            verts.Add(Vector3.zero);
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (0.5f * (i % 2 == 0 ? 1f : 0.8f)));
            }
            for (int i = 0; i < sides; i++)
            {
                tris.Add(0);
                tris.Add(1 + (i + 1) % sides);
                tris.Add(1 + i);
            }
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
