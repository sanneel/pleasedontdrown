using UnityEngine;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// A spring bone: the bone lags behind its parent's movement and wobbles back (secondary motion). Works on any
    /// transform, so a generated, rigged model only needs to name the bones.
    /// </summary>
    public sealed class JiggleBone
    {
        public readonly Transform Bone;
        public float Stiffness = 170f;   // spring (1/s²)
        public float Damping = 5.5f;     // (1/s)
        public float MaxOffset = 0.05f;  // metres (for a 1.8 m person)
        public float Inertia = 1f;       // how much the parent's acceleration shoves it

        private Vector3 _restLocal;
        private Vector3 _offset;         // parent space
        private Vector3 _velocity;       // parent space
        private Vector3 _lastAnchor;
        private Vector3 _lastAnchorVelocity;
        private bool _started;

        public JiggleBone(Transform bone) => Bone = bone;

        /// <summary>Kick it (world velocity change), e.g. a CPR compression.</summary>
        public void Impulse(Vector3 worldVelocity)
        {
            if (Bone != null && Bone.parent != null) _velocity += Bone.parent.InverseTransformVector(worldVelocity);
        }

        public void Reset()
        {
            _started = false;
            _offset = _velocity = Vector3.zero;
        }

        public void Tick(float dt, float scale)
        {
            if (Bone == null || Bone.parent == null || dt <= 0f) return;
            Transform parent = Bone.parent;
            if (!_started)
            {
                _restLocal = Bone.localPosition;
                _lastAnchor = parent.TransformPoint(_restLocal);
                _lastAnchorVelocity = Vector3.zero;
                _started = true;
            }
            // Animators reset bones to rest every frame; remember where rest is (in case of a rebuild).
            Vector3 anchor = parent.TransformPoint(_restLocal);
            Vector3 anchorVelocity = (anchor - _lastAnchor) / dt;
            Vector3 accel = (anchorVelocity - _lastAnchorVelocity) / dt;
            _lastAnchor = anchor;
            _lastAnchorVelocity = anchorVelocity;
            if (accel.sqrMagnitude > 2500f * 2500f) accel = Vector3.zero; // teleports

            // Inertia: the flesh wants to stay where it was while the body accelerates (plus a little gravity sag).
            // Steady state offset = force / stiffness, so a 1 g push moves it about 2 cm.
            Vector3 force = parent.InverseTransformVector(-accel * (0.35f * Inertia) + Physics.gravity * 0.05f);
            force /= Mathf.Max(0.1f, parent.lossyScale.y);
            // Semi-implicit integration in small steps (stiff springs at low frame rates).
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.008f), 1, 8);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 a = force - _offset * Stiffness - _velocity * Damping;
                _velocity += a * h;
                _offset += _velocity * h;
            }
            float max = MaxOffset * scale;
            if (_offset.sqrMagnitude > max * max)
            {
                _offset = _offset.normalized * max;
                _velocity *= 0.5f;
            }
            Bone.localPosition = _restLocal + _offset;
            // Squash and stretch a touch with the bounce, so it reads at a distance.
            float squash = Mathf.Clamp(-_offset.y / Mathf.Max(0.001f, max), -1f, 1f) * 0.12f;
            Bone.localScale = new Vector3(1f + squash * 0.5f, 1f - squash, 1f + squash * 0.5f);
        }
    }

    /// <summary>
    /// Drives an avatar's spring bones after its pose is set (runs after AvatarAnimator and the tourist ragdoll).
    /// Feminine figures jiggle their chest; <see cref="Bounce"/> gives an extra kick (CPR compressions, landings).
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class AvatarJiggle : MonoBehaviour
    {
        [SerializeField] private AvatarRig _rig;
        [SerializeField] private float _strength = 1f;

        private JiggleBone[] _bones = System.Array.Empty<JiggleBone>();

        public AvatarRig Rig
        {
            get => _rig;
            set
            {
                if (_rig != null) _rig.Rebuilt -= Rebind;
                _rig = value;
                if (isActiveAndEnabled && _rig != null) _rig.Rebuilt += Rebind;
                Rebind();
            }
        }

        private void Reset() => _rig = GetComponent<AvatarRig>();

        private void OnEnable()
        {
            if (_rig == null) _rig = GetComponent<AvatarRig>();
            if (_rig != null) _rig.Rebuilt += Rebind;
            Rebind();
        }

        private void OnDisable()
        {
            if (_rig != null) _rig.Rebuilt -= Rebind;
        }

        private void Rebind()
        {
            if (_rig == null || !_rig.IsBuilt || !_rig.Look.Feminine)
            {
                _bones = System.Array.Empty<JiggleBone>();
                return;
            }
            _bones = new[] { new JiggleBone(_rig[AvatarRig.Bone.BustL]), new JiggleBone(_rig[AvatarRig.Bone.BustR]) };
            // Bigger builds wobble more and slower.
            float soft = _rig.Look.Build switch { 3 => 0.75f, 2 => 0.9f, 0 => 1.15f, _ => 1f };
            foreach (JiggleBone b in _bones)
            {
                b.Stiffness = 170f * soft;
                b.MaxOffset = 0.045f / soft;
            }
            _bones[1].Stiffness *= 1.06f; // a little out of phase
        }

        /// <summary>Extra kick in world space (m/s), e.g. a CPR compression pushes down.</summary>
        public void Bounce(Vector3 velocity)
        {
            foreach (JiggleBone b in _bones) b.Impulse(velocity * _strength);
        }

        private void LateUpdate()
        {
            if (_bones.Length == 0) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float scale = _rig != null ? _rig.Scale : 1f;
            foreach (JiggleBone b in _bones) b.Tick(dt, scale);
        }
    }
}
