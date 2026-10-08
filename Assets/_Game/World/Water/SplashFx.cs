using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// One shared particle system for water splashes, plus a synthesized splash sound.
    /// Splashes are purely local effects: every machine spawns them when it sees something hit the water.
    /// </summary>
    public class SplashFx : MonoBehaviour
    {
        [SerializeField] private Material _particleMaterial;

        private static SplashFx _instance;

        /// <summary>The soft round particle (other effects tint it: blood).</summary>
        public static Material ParticleMaterial => _instance != null ? _instance._particleMaterial : null;
        private ParticleSystem _system;
        private readonly AudioSource[] _sounds = new AudioSource[8];
        private int _soundFrame, _soundsThisFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private void Awake()
        {
            _instance = this;
            _system = BuildSystem();
            for (int i = 0; i < _sounds.Length; i++)
            {
                var go = new GameObject("Water impact " + i);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 2f;
                source.maxDistance = 32f;
                source.priority = 160;
                _sounds[i] = source;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <param name="strength">0 = a plop, 1 = a cannonball.</param>
        public static void Spawn(Vector3 position, float strength)
        {
            if (_instance == null) return;
            strength = Mathf.Clamp01(strength);
            var emit = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
                startSize = Mathf.Lerp(0.12f, 0.28f, strength)
            };
            _instance._system.Emit(emit, Mathf.RoundToInt(Mathf.Lerp(10f, 70f, strength)));
            _instance.PlayImpact(position, strength);
        }

        private void PlayImpact(Vector3 position, float strength)
        {
            if (_soundFrame != Time.frameCount) { _soundFrame = Time.frameCount; _soundsThisFrame = 0; }
            // A pile of floating objects can cross the surface together; keep that from becoming a noise wall.
            if (_soundsThisFrame >= 3) return;
            foreach (var source in _sounds)
            {
                if (source.isPlaying) continue;
                source.transform.position = position;
                source.clip = BeachAudio.WaterImpact(strength);
                source.volume = Mathf.Lerp(0.22f, 0.72f, strength);
                source.pitch = Random.Range(0.96f, 1.04f);
                source.Play();
                _soundsThisFrame++;
                break;
            }
        }

        private ParticleSystem BuildSystem()
        {
            var go = new GameObject("SplashParticles");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.gravityModifier = 1.4f;
            main.maxParticles = 600;
            main.startColor = new Color(0.95f, 0.98f, 1f, 0.9f);

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.25f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }
    }
}
