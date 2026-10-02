using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Rescue
{
    /// <summary>
    /// Blood, for the shark: a spray where the bite happens, a red cloud spreading on the water, drops and a
    /// trail of stains on the sand from whoever is bleeding, and a splatter across the screen of anyone close by.
    /// All of it is local dressing (every machine makes its own when it sees the bite or the wound), built on
    /// first use from the splash particles' material.
    /// </summary>
    public sealed class BloodFx : MonoBehaviour
    {
        private static readonly Color Fresh = new(0.62f, 0.02f, 0.04f, 0.95f), Dark = new(0.4f, 0.01f, 0.03f, 0.85f), InWater = new(0.5f, 0.0f, 0.03f, 0.5f);
        private const float SplatSeconds = 3.5f;

        private static BloodFx _instance;

        private ParticleSystem _spray, _cloud, _stains;
        private float _splatAt = -100f;
        private readonly Vector4[] _splats = new Vector4[12]; // x, y (0..1 of the screen), size (sheet units), a number that shapes it

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private static BloodFx Instance
        {
            get
            {
                if (_instance != null) return _instance;
                Material material = SplashFx.ParticleMaterial;
                if (material == null) return null; // no ocean in this scene (or a headless run with nothing to draw)
                var go = new GameObject("BloodFx");
                _instance = go.AddComponent<BloodFx>();
                _instance.useGUILayout = false;
                _instance.Build(material);
                return _instance;
            }
        }

        /// <summary>The bite itself: blood flying, the water going red, and a splatter on the screen if we're close.</summary>
        public static void Bite(Vector3 at)
        {
            BloodFx fx = Instance;
            if (fx == null) return;
            float surface = WaterSurface.Exists ? WaterSurface.HeightAt(at) : at.y;
            var top = new Vector3(at.x, Mathf.Max(at.y, surface) + 0.1f, at.z);
            fx._spray.Emit(new ParticleSystem.EmitParams { position = top, applyShapeToPosition = true }, 90);
            if (surface > at.y - 0.5f)
                for (int i = 0; i < 16; i++)
                    fx.Cloud(new Vector3(at.x, surface, at.z) + new Vector3(Random.Range(-0.9f, 0.9f), 0f, Random.Range(-0.9f, 0.9f)), Random.Range(0.9f, 1.6f));
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - at).sqrMagnitude < 9f * 9f) fx.Splat();
        }

        /// <summary>
        /// One beat of an open wound at <paramref name="wound"/>: a few drops; in the water a puff of red, on land a
        /// stain where they fall. Call it a few times a second while the bleeding lasts.
        /// </summary>
        public static void Bleed(Vector3 wound)
        {
            BloodFx fx = Instance;
            if (fx == null) return;
            fx._spray.Emit(new ParticleSystem.EmitParams
            {
                position = wound, velocity = new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(0.2f, 1.2f), Random.Range(-0.5f, 0.5f)),
                startSize = Random.Range(0.05f, 0.1f), startLifetime = Random.Range(0.4f, 0.7f)
            }, 3);
            float surface = WaterSurface.Exists ? WaterSurface.HeightAt(wound) : float.NegativeInfinity;
            if (surface > wound.y - 0.15f)
            {
                fx.Cloud(new Vector3(wound.x, surface, wound.z), Random.Range(0.5f, 0.9f));
                return;
            }
            if (!GroundBelow(wound, out Vector3 ground)) return;
            fx._stains.Emit(new ParticleSystem.EmitParams
            {
                position = ground + Vector3.up * 0.015f + new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.12f, 0.12f)),
                startSize = Random.Range(0.12f, 0.34f), startColor = Dark, rotation = Random.Range(0f, 360f)
            }, 1);
        }

        private void Cloud(Vector3 at, float size) =>
            _cloud.Emit(new ParticleSystem.EmitParams
            {
                position = at + Vector3.up * 0.03f, startSize = size, startColor = InWater, rotation = Random.Range(0f, 360f),
                velocity = new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f))
            }, 1);

        private void Splat()
        {
            _splatAt = Time.unscaledTime;
            for (int i = 0; i < _splats.Length; i++)
            {
                // Mostly round the edges of the view, a couple right across it.
                float x = Random.value, y = Random.value;
                if (i > 2 && Mathf.Abs(x - 0.5f) < 0.28f && Mathf.Abs(y - 0.5f) < 0.28f) x = x < 0.5f ? x * 0.4f : 1f - (1f - x) * 0.4f;
                _splats[i] = new Vector4(x, y, Random.Range(34f, 110f), Random.value);
            }
        }

        private void OnGUI()
        {
            float t = Time.unscaledTime - _splatAt;
            if (t > SplatSeconds || Event.current.type != EventType.Repaint) return;
            float alpha = Mathf.Clamp01((SplatSeconds - t) / 1.6f) * 0.8f;
            var red = new Color(Fresh.r, Fresh.g, Fresh.b, alpha);
            foreach (Vector4 s in _splats)
            {
                // A blot, smaller ones thrown off round it, and a run starting down from it.
                float x = s.x * UI.Hud.Width, y = s.y * UI.Hud.Height, size = s.z, seed = s.w;
                Dot(x, y, size, red);
                for (int k = 0; k < 4; k++)
                {
                    float angle = seed * 6.28f + k * 1.9f, far = size * (0.5f + 0.16f * k), d = size * (0.48f - 0.1f * k);
                    Dot(x + Mathf.Cos(angle) * far, y + Mathf.Sin(angle) * far * 0.8f, d, red);
                }
                float run = size * 0.14f;
                UI.Hud.Fill(new Rect(x + (seed - 0.5f) * size * 0.3f - run * 0.5f, y, run, size * (0.5f + seed * 0.5f) + t * 30f), red, run * 0.5f);
            }
        }

        private static void Dot(float x, float y, float size, Color color) =>
            UI.Hud.Fill(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), color, size * 0.5f);

        private static readonly RaycastHit[] _groundHits = new RaycastHit[8];

        /// <summary>The ground under a point: the nearest thing below it that isn't a body, an item or a vehicle.</summary>
        private static bool GroundBelow(Vector3 from, out Vector3 point)
        {
            int n = Physics.RaycastNonAlloc(from + Vector3.up * 0.15f, Vector3.down, _groundHits, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            point = default;
            for (int i = 0; i < n; i++)
            {
                if (_groundHits[i].rigidbody != null || _groundHits[i].distance >= nearest) continue;
                nearest = _groundHits[i].distance;
                point = _groundHits[i].point;
            }
            return nearest < float.MaxValue;
        }

        private void Build(Material material)
        {
            // Drops flying: up and out, then falling.
            _spray = Particles("BloodSpray", material, 400);
            ParticleSystem.MainModule main = _spray.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
            main.gravityModifier = 1.3f;
            main.startColor = Fresh;
            _spray.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // the cone points up
            ParticleSystem.ShapeModule shape = _spray.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 42f;
            shape.radius = 0.25f;
            Fade(_spray, 1f, 0.3f);

            // On the water: flat blots that spread and thin out.
            _cloud = Particles("BloodCloud", material, 300);
            main = _cloud.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
            main.startSpeed = 0f;
            Flat(_cloud);
            Fade(_cloud, 1f, 2.6f);

            // On the ground: stains that stay a good while.
            _stains = Particles("BloodStains", material, 260);
            main = _stains.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(50f, 70f);
            main.startSpeed = 0f;
            Flat(_stains);
            Fade(_stains, 1f, 1f);
        }

        private ParticleSystem Particles(string systemName, Material material, int most)
        {
            var go = new GameObject(systemName);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = most;
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
            return ps;
        }

        /// <summary>Lying on the surface (water or sand) instead of facing the camera.</summary>
        private static void Flat(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.MainModule main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        /// <summary>Fades out over its life while growing from <paramref name="from"/> to <paramref name="to"/> times its size.</summary>
        private static void Fade(ParticleSystem ps, float from, float to)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, from, 1f, to));
            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;
        }
    }
}
