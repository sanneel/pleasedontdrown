using FishNet.Object;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// A boat the story sails by itself (the police boat at the end of chapter 4): not a vehicle anyone drives. The host
    /// moves it over the water (its NetworkTransform carries that to everyone), bobbing on the waves, turning toward
    /// where it's going. <see cref="Deck"/> is where people stand on it (the story keeps them there while it sails).
    /// </summary>
    public sealed class ScriptedBoat : NetworkBehaviour
    {
        [SerializeField] private float _draft = 0.15f;
        private Vector3? _target;
        private float _targetYaw, _speed = 6f;

        /// <summary>Host: it has got where it was sent.</summary>
        public bool Arrived => !_target.HasValue;

        /// <summary>A point on its deck, in its own space (people ride there).</summary>
        public Vector3 Deck(Vector3 local) => transform.TransformPoint(local);

        [Server]
        public void ServerSailTo(Vector3 point, float yaw, float speed = 6f)
        {
            _target = new Vector3(point.x, 0f, point.z);
            _targetYaw = yaw;
            _speed = speed;
        }

        [Server]
        public void ServerPlace(Vector3 point, float yaw)
        {
            _target = null;
            transform.SetPositionAndRotation(new Vector3(point.x, SurfaceAt(point), point.z), Quaternion.Euler(0f, yaw, 0f));
        }

        private float SurfaceAt(Vector3 p) => (PleaseDontDrown.World.Water.WaterSurface.Exists ? PleaseDontDrown.World.Water.WaterSurface.HeightAt(p) : -0.35f) - _draft;

        private void Update()
        {
            if (!IsServerInitialized) return;
            Vector3 p = transform.position;
            Quaternion rot = transform.rotation;
            if (_target.HasValue)
            {
                Vector3 to = _target.Value - new Vector3(p.x, 0f, p.z);
                float step = _speed * Time.deltaTime;
                if (to.magnitude <= step)
                {
                    p = new Vector3(_target.Value.x, p.y, _target.Value.z);
                    rot = Quaternion.Euler(0f, _targetYaw, 0f);
                    _target = null;
                }
                else
                {
                    p += to.normalized * step;
                    // Bow first where it's going; the last few metres it comes round to how it ties up.
                    Quaternion want = to.magnitude > 8f ? Quaternion.LookRotation(to) : Quaternion.Euler(0f, _targetYaw, 0f);
                    rot = Quaternion.RotateTowards(rot, want, 50f * Time.deltaTime);
                }
            }
            p.y = SurfaceAt(p);
            transform.SetPositionAndRotation(p, rot);
        }
    }
}
