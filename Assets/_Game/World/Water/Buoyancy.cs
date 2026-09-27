using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// Makes a rigidbody float on <see cref="WaterSurface"/>. Samples a few probe spheres; each pushes up in
    /// proportion to how deep it is, applied at its position so objects tilt with waves and self-right.
    /// Runs only where the body is simulated (non-kinematic), i.e. on its physics owner.
    ///
    /// Density = fraction of the object that sits below the waterline at rest
    /// (0.1 bobs on top like a beach ball, 0.5 floats half under, above 1 sinks slowly).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Buoyancy : MonoBehaviour
    {
        [SerializeField, Range(0.02f, 2f)] private float _density = 0.5f;
        [SerializeField] private float _waterDrag = 1.2f;
        [SerializeField] private float _waterAngularDrag = 1.5f;
        [SerializeField] private float _verticalDamping = 1.5f;

        private Rigidbody _rb;
        private Vector3[] _probes;   // local-space centers
        private float _probeRadius;

        /// <summary>Set while the object is held: no floating forces.</summary>
        public bool Suspended { get; set; }
        public bool InWater { get; private set; }
        public float SubmergedFraction { get; private set; }
        /// <summary>Can change at runtime (an unconscious swimmer sinks, a conscious one floats).</summary>
        public float Density
        {
            get => _density;
            set => _density = Mathf.Clamp(value, 0.02f, 2f);
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            BuildProbes();
        }

        /// <summary>
        /// Probe layout from the object's local mesh bounds: one probe for round things, five otherwise.
        /// Only meshes that move with this body count (a ragdoll's limbs have their own bodies and buoyancy).
        /// </summary>
        private void BuildProbes()
        {
            Bounds local = default;
            bool any = false;
            foreach (MeshFilter mf in GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || mf.GetComponentInParent<Rigidbody>() != _rb) continue;
                Matrix4x4 toRoot = transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                Bounds b = mf.sharedMesh.bounds;
                Vector3 c = b.center, e = b.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(c.x + ((i & 1) == 0 ? -e.x : e.x), c.y + ((i & 2) == 0 ? -e.y : e.y), c.z + ((i & 4) == 0 ? -e.z : e.z));
                    Vector3 p = toRoot.MultiplyPoint3x4(corner);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any) local = new Bounds(Vector3.zero, Vector3.one * 0.5f);

            Vector3 ext = local.extents;
            float max = Mathf.Max(ext.x, Mathf.Max(ext.y, ext.z));
            float min = Mathf.Min(ext.x, Mathf.Min(ext.y, ext.z));
            if (min > max * 0.8f)
            {
                _probes = new[] { local.center };
                _probeRadius = max;
            }
            else
            {
                Vector3 c = local.center;
                float ox = ext.x * 0.7f, oz = ext.z * 0.7f;
                _probes = new[]
                {
                    c, c + new Vector3(ox, 0f, oz), c + new Vector3(-ox, 0f, oz), c + new Vector3(ox, 0f, -oz), c + new Vector3(-ox, 0f, -oz)
                };
                _probeRadius = Mathf.Max(0.05f, ext.y);
            }
        }

        private void FixedUpdate()
        {
            if (_rb.isKinematic || Suspended || !WaterSurface.Exists)
            {
                InWater = false;
                SubmergedFraction = 0f;
                return;
            }

            float diameter = _probeRadius * 2f;
            float maxForcePerProbe = _rb.mass * -Physics.gravity.y / _density / _probes.Length;
            float total = 0f;
            foreach (Vector3 local in _probes)
            {
                Vector3 p = transform.TransformPoint(local);
                float surface = WaterSurface.HeightAt(p);
                float submerged = Mathf.Clamp01((surface - (p.y - _probeRadius)) / diameter);
                if (submerged <= 0f) continue;
                total += submerged;
                _rb.AddForceAtPosition(Vector3.up * (maxForcePerProbe * submerged), p, ForceMode.Force);
            }

            SubmergedFraction = total / _probes.Length;
            InWater = SubmergedFraction > 0.001f;
            if (!InWater) return;

            Vector3 v = _rb.linearVelocity;
            _rb.AddForce(-v * (_waterDrag * SubmergedFraction) + Vector3.up * (-v.y * _verticalDamping * SubmergedFraction), ForceMode.Acceleration);
            _rb.AddTorque(-_rb.angularVelocity * (_waterAngularDrag * SubmergedFraction), ForceMode.Acceleration);
        }

        /// <summary>True if the body's center sits near the surface (used by followers that don't simulate).</summary>
        public bool IsNearSurface(Vector3 position) =>
            WaterSurface.Exists && Mathf.Abs(WaterSurface.HeightAt(position) - position.y) < _probeRadius + 0.6f;
    }
}
