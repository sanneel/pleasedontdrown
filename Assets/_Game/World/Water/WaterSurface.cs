using System;
using FishNet;
using PleaseDontDrown.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// The ocean. One wave function, evaluated identically on the CPU (buoyancy, swimming, splashes) and in the
    /// ocean shader (visuals), driven by the network clock so every player sees the same wave at the same place.
    ///
    /// Waves are a sum of up to 4 directional sine waves. Their frequencies are rounded so that the whole pattern
    /// repeats exactly every <see cref="_loopSeconds"/>, which lets us wrap time and keep float precision forever.
    /// The visible surface is a grid that follows the camera (snapped, so vertices don't swim) plus a flat far ring.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class WaterSurface : MonoBehaviour
    {
        [Serializable]
        public struct Wave
        {
            public float Wavelength;
            public float Amplitude;
            public Vector2 Direction;
            [Range(0f, 6.2832f)] public float Phase;
        }

        private const int MaxWaves = 4;

        [SerializeField] private float _waterLevel = -0.35f;
        [SerializeField] private Wave[] _waves =
        {
            new Wave { Wavelength = 31f, Amplitude = 0.12f, Direction = new Vector2(-0.1f, 1f), Phase = 0f },
            new Wave { Wavelength = 18f, Amplitude = 0.2f, Direction = new Vector2(0.15f, 1f), Phase = 1.3f },
            new Wave { Wavelength = 9f, Amplitude = 0.09f, Direction = new Vector2(0.6f, 0.8f), Phase = 2.9f },
            new Wave { Wavelength = 5f, Amplitude = 0.045f, Direction = new Vector2(-0.5f, 0.86f), Phase = 4.4f },
        };
        [SerializeField] private float _loopSeconds = 1200f;

        [Header("Visual surface")]
        [SerializeField] private Material _material;
        [SerializeField] private int _gridResolution = 200;
        [SerializeField] private float _gridSize = 180f;
        [SerializeField] private float _farSize = 8000f;

        private static WaterSurface _instance;
        private readonly Vector4[] _gpuWaves = new Vector4[MaxWaves];
        private Vector4 _gpuPhases;
        private Transform _grid;
        private Transform _far;

        // Cached wave constants (wave vector, amplitude, angular frequency, phase).
        private readonly Vector2[] _k = new Vector2[MaxWaves];
        private readonly float[] _amp = new float[MaxWaves];
        private readonly float[] _omega = new float[MaxWaves];
        private readonly float[] _phase = new float[MaxWaves];
        private int _count;

        private static readonly int WavesId = Shader.PropertyToID("_PDD_Waves");
        private static readonly int PhasesId = Shader.PropertyToID("_PDD_WavePhases");
        private static readonly int TimeId = Shader.PropertyToID("_PDD_WaveTime");
        private static readonly int LevelId = Shader.PropertyToID("_PDD_WaterLevel");
        private static readonly int CenterId = Shader.PropertyToID("_PDD_OceanCenter");

        public static bool Exists => _instance != null;
        public static float BaseLevel => _instance != null ? _instance._waterLevel : float.NegativeInfinity;

        /// <summary>Shared wave time in seconds (network clock when connected), wrapped to the loop length.</summary>
        public static float WaveTime
        {
            get
            {
                if (_instance == null) return 0f;
                double now = Time.timeAsDouble;
                var tm = InstanceFinder.TimeManager;
                if (tm != null && (InstanceFinder.IsServerStarted || InstanceFinder.IsClientStarted))
                    now = tm.TicksToTime(tm.Tick) + tm.GetTickPercentAsDouble() * tm.TickDelta;
                return (float)(now % _instance._loopSeconds);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        // ------------------------------------------------------------------ queries

        /// <summary>Water surface height at a world position (ignores y).</summary>
        public static float HeightAt(Vector3 position) => HeightAt(position.x, position.z);

        public static float HeightAt(float x, float z)
        {
            WaterSurface w = _instance;
            if (w == null) return float.NegativeInfinity;
            float t = WaveTime;
            float scale = OceanState.WaveScale;
            float h = 0f;
            for (int i = 0; i < w._count; i++)
                h += w._amp[i] * Mathf.Sin(w._k[i].x * x + w._k[i].y * z + w._omega[i] * t + w._phase[i]);
            return w._waterLevel + h * scale * Seabed.WaveFactor(x, z, w._waterLevel);
        }

        /// <summary>Surface normal at a world position.</summary>
        public static Vector3 NormalAt(Vector3 position)
        {
            WaterSurface w = _instance;
            if (w == null) return Vector3.up;
            float t = WaveTime;
            float scale = OceanState.WaveScale;
            Vector2 slope = Vector2.zero;
            for (int i = 0; i < w._count; i++)
                slope += w._k[i] * (w._amp[i] * Mathf.Cos(w._k[i].x * position.x + w._k[i].y * position.z + w._omega[i] * t + w._phase[i]));
            slope *= scale * Seabed.WaveFactor(position.x, position.z, w._waterLevel);
            return new Vector3(-slope.x, 1f, -slope.y).normalized;
        }

        /// <summary>How deep a point is below the surface (negative = above water).</summary>
        public static float DepthOf(Vector3 position) => HeightAt(position) - position.y;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            _instance = this;
            Precompute();
            if (_material != null)
                BuildSurface();
            DevCommands.Register("water", "", "Water height and depth where you stand.", _ =>
            {
                Camera cam = Camera.main;
                Vector3 p = cam != null ? cam.transform.position : Vector3.zero;
                DevCommands.Print($"surface {HeightAt(p):F2} m, camera depth {DepthOf(p):F2} m, wave time {WaveTime:F1} s, scale {OceanState.WaveScale:F2}");
            }, owner: this);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            DevCommands.Unregister("water", this);
        }

        private void OnValidate()
        {
            if (Application.isPlaying) Precompute();
        }

        private void Precompute()
        {
            _count = Mathf.Min(_waves.Length, MaxWaves);
            const float g = 9.81f;
            for (int i = 0; i < MaxWaves; i++)
            {
                if (i >= _count)
                {
                    _gpuWaves[i] = Vector4.zero;
                    continue;
                }
                Wave wave = _waves[i];
                float k = 2f * Mathf.PI / Mathf.Max(0.5f, wave.Wavelength);
                // Deep-water dispersion, rounded to a whole number of cycles per loop so wrapped time stays seamless.
                float cycles = Mathf.Max(1f, Mathf.Round(Mathf.Sqrt(g * k) * _loopSeconds / (2f * Mathf.PI)));
                Vector2 dir = wave.Direction.sqrMagnitude > 1e-6f ? wave.Direction.normalized : Vector2.up;
                _k[i] = dir * k;
                _amp[i] = wave.Amplitude;
                _omega[i] = 2f * Mathf.PI * cycles / _loopSeconds;
                _phase[i] = wave.Phase;
            }
            _gpuPhases = new Vector4(_phase[0], _phase[1], _phase[2], _phase[3]);
        }

        private void LateUpdate()
        {
            float scale = OceanState.WaveScale;
            for (int i = 0; i < _count; i++)
                _gpuWaves[i] = new Vector4(_k[i].x, _k[i].y, _amp[i] * scale, _omega[i]);
            Shader.SetGlobalVectorArray(WavesId, _gpuWaves);
            Shader.SetGlobalVector(PhasesId, _gpuPhases);
            Shader.SetGlobalFloat(TimeId, WaveTime);
            Shader.SetGlobalFloat(LevelId, _waterLevel);

            if (_grid == null) return;
            Camera cam = Camera.main;
            Vector3 focus = cam != null ? cam.transform.position : transform.position;
            float step = _gridSize / _gridResolution;
            var center = new Vector3(Mathf.Round(focus.x / step) * step, 0f, Mathf.Round(focus.z / step) * step);
            _grid.position = center;
            _far.position = center;
            // Waves fade out toward the grid edge so it meets the flat far ring without a seam.
            Shader.SetGlobalVector(CenterId, new Vector4(center.x, center.z, _gridSize * 0.32f, _gridSize * 0.49f));
        }

        // ------------------------------------------------------------------ meshes

        private void BuildSurface()
        {
            _grid = CreateChild("OceanGrid", BuildGrid(_gridResolution, _gridSize));
            _far = CreateChild("OceanFar", BuildRing(_gridSize * 0.5f, _farSize * 0.5f));
        }

        private Transform CreateChild(string childName, Mesh mesh)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        private static Mesh BuildGrid(int resolution, float size)
        {
            int side = resolution + 1;
            var vertices = new Vector3[side * side];
            float half = size * 0.5f;
            float step = size / resolution;
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                    vertices[z * side + x] = new Vector3(-half + x * step, 0f, -half + z * step);

            var indices = new int[resolution * resolution * 6];
            int t = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int a = z * side + x;
                    int b = a + side;
                    indices[t++] = a; indices[t++] = b; indices[t++] = a + 1;
                    indices[t++] = a + 1; indices[t++] = b; indices[t++] = b + 1;
                }
            }

            var mesh = new Mesh { name = "OceanGrid", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.triangles = indices;
            // Vertices move in the shader; give culling generous vertical bounds.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(size, 20f, size));
            return mesh;
        }

        /// <summary>A flat square ring (outer square minus the inner grid area).</summary>
        private static Mesh BuildRing(float inner, float outer)
        {
            var vertices = new[]
            {
                new Vector3(-outer, 0f, -outer), new Vector3(outer, 0f, -outer), new Vector3(outer, 0f, outer), new Vector3(-outer, 0f, outer),
                new Vector3(-inner, 0f, -inner), new Vector3(inner, 0f, -inner), new Vector3(inner, 0f, inner), new Vector3(-inner, 0f, inner),
            };
            int[] triangles =
            {
                0, 4, 1, 1, 4, 5,   // south strip
                1, 5, 2, 2, 5, 6,   // east strip
                2, 6, 3, 3, 6, 7,   // north strip
                3, 7, 0, 0, 7, 4,   // west strip
            };
            var mesh = new Mesh { name = "OceanFar", vertices = vertices, triangles = triangles };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(outer * 2f, 20f, outer * 2f));
            return mesh;
        }
    }
}
