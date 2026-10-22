using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// The rival company's helicopter on Finders Keepers (island 4): parked on its helipad, or flown by the host (the
    /// final boss drops tourists from it). The host moves it (its NetworkTransform carries that to everyone); every
    /// machine spins its rotors while the engine runs.
    /// </summary>
    public sealed class FkHelicopter : NetworkBehaviour
    {
        private readonly SyncVar<bool> _engine = new SyncVar<bool>();
        private Transform _rotor, _tail;
        private float _spin;
        private Vector3? _target;
        private float _targetYaw, _speed = 12f;

        public bool EngineOn => _engine.Value;
        /// <summary>Host: it has got where it was sent.</summary>
        public bool Arrived => !_target.HasValue;

        private void Awake()
        {
            _rotor = transform.Find("Body/Rotor");
            _tail = transform.Find("Body/TailRotor");
        }

        /// <summary>Host: fly to a point (engine on), turning to <paramref name="yaw"/> on the way.</summary>
        [Server]
        public void ServerFlyTo(Vector3 point, float yaw, float speed = 12f)
        {
            _engine.Value = true;
            _target = point;
            _targetYaw = yaw;
            _speed = speed;
        }

        /// <summary>Host: put it down here at once, engine off (parked).</summary>
        [Server]
        public void ServerPark(Vector3 point, float yaw)
        {
            _target = null;
            _engine.Value = false;
            transform.SetPositionAndRotation(point, Quaternion.Euler(0f, yaw, 0f));
        }

        private void Update()
        {
            float want = _engine.Value ? 1f : 0f;
            _spin = Mathf.MoveTowards(_spin, want, Time.deltaTime * 0.6f);
            if (_rotor != null) _rotor.Rotate(0f, _spin * 1300f * Time.deltaTime, 0f, Space.Self);
            if (_tail != null) _tail.Rotate(_spin * 1800f * Time.deltaTime, 0f, 0f, Space.Self);
            if (!IsServerInitialized || !_target.HasValue) return;
            if (_spin < 0.6f) return; // spinning up first
            Vector3 p = transform.position, to = _target.Value - p;
            float step = _speed * Time.deltaTime;
            Quaternion face = Quaternion.Euler(0f, _targetYaw, 0f);
            if (to.magnitude <= step)
            {
                transform.SetPositionAndRotation(_target.Value, face);
                _target = null;
                return;
            }
            // Nose a little down when going fast, level when hovering.
            Vector3 flat = new(to.x, 0f, to.z);
            Quaternion heading = flat.sqrMagnitude > 4f ? Quaternion.LookRotation(flat) * Quaternion.Euler(8f, 0f, 0f) : face;
            transform.SetPositionAndRotation(p + to.normalized * step, Quaternion.RotateTowards(transform.rotation, heading, 60f * Time.deltaTime));
        }
    }
}
