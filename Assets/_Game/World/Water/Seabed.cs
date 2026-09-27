using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// The ground under the sea, read from the terrain's height grid. Waves calm down as the water gets shallow and
    /// are gone entirely under the island, so the ocean (which is one big surface) never shows through the sand.
    /// <see cref="WaterSurface"/> applies <see cref="WaveFactor"/> on the CPU; the ocean shader samples the same
    /// heights from a texture, so buoyancy and visuals agree.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    public class Seabed : MonoBehaviour
    {
        [Tooltip("Terrain mesh laid out as a regular grid (row-major along x, then z).")]
        [SerializeField] private Mesh _grid;
        [SerializeField] private Vector2 _min;
        [SerializeField] private float _step = 2f;
        [SerializeField] private int _countX;
        [SerializeField] private int _countZ;
        [Tooltip("Water this deep or more gets full-size waves; shallower water fades them out.")]
        [SerializeField] private float _calmDepth = 2.6f;

        private static Seabed _instance;
        private float[] _heights;
        private Texture2D _texture;

        private static readonly int TextureId = Shader.PropertyToID("_PDD_Seabed");
        private static readonly int RectId = Shader.PropertyToID("_PDD_SeabedRect");
        private static readonly int ParamsId = Shader.PropertyToID("_PDD_SeabedParams");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            Shader.SetGlobalVector(ParamsId, Vector4.zero); // no seabed: full waves everywhere
        }

        /// <summary>Ground height at (x, z); very deep outside the mapped area.</summary>
        public static float HeightAt(float x, float z)
        {
            Seabed s = _instance;
            if (s == null) return -1000f;
            float fx = Mathf.Clamp((x - s._min.x) / s._step, 0f, s._countX - 1.001f);
            float fz = Mathf.Clamp((z - s._min.y) / s._step, 0f, s._countZ - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            int i = iz * s._countX + ix;
            float a = Mathf.Lerp(s._heights[i], s._heights[i + 1], tx);
            float b = Mathf.Lerp(s._heights[i + s._countX], s._heights[i + s._countX + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }

        /// <summary>0 on land and at the water's edge, 1 in open water. Multiplies the wave height.</summary>
        public static float WaveFactor(float x, float z, float waterLevel)
        {
            Seabed s = _instance;
            if (s == null) return 1f;
            float depth = waterLevel - HeightAt(x, z);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(depth / s._calmDepth));
        }

        private void Awake()
        {
            _instance = this;
            Vector3[] vertices = _grid != null ? _grid.vertices : null;
            if (vertices == null || vertices.Length != _countX * _countZ)
            {
                Debug.LogError($"[Water] Seabed grid doesn't match ({vertices?.Length ?? 0} vertices, expected {_countX}x{_countZ}); waves stay full everywhere.");
                _instance = null;
                return;
            }
            _heights = new float[vertices.Length];
            var pixels = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                _heights[i] = vertices[i].y;
                pixels[i] = new Color(vertices[i].y, 0f, 0f, 0f);
            }
            _texture = new Texture2D(_countX, _countZ, TextureFormat.RHalf, false, true)
            {
                name = "SeabedHeights",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _texture.SetPixels(pixels);
            _texture.Apply(false, true);

            // Texel centres sit on the grid vertices: uv = ((x - min) / step + 0.5) / count.
            float sizeX = _countX * _step, sizeZ = _countZ * _step;
            Shader.SetGlobalTexture(TextureId, _texture);
            Shader.SetGlobalVector(RectId, new Vector4(_min.x - _step * 0.5f, _min.y - _step * 0.5f, 1f / sizeX, 1f / sizeZ));
            Shader.SetGlobalVector(ParamsId, new Vector4(1f, 1f / _calmDepth, 0f, 0f));
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;
            Shader.SetGlobalVector(ParamsId, Vector4.zero);
            if (_texture != null) Destroy(_texture);
        }
    }
}
