using UnityEngine;

namespace PleaseDontDrown.Vehicles
{
    /// <summary>
    /// A boat tied up at its berth (the hotel's yacht at the marina, the cushion at its jetty): while nobody drives it,
    /// nothing tows it and it's still near the berth, it's held there, heading and all, instead of drifting off on the
    /// waves. Taken away and left somewhere else, it stays where it was left. Only its physics authority pulls it.
    /// The story ties boats up where a scene needs them (<see cref="Tie"/>: the pirate boat at Skull Cove's night dock).
    /// </summary>
    public sealed class Mooring : MonoBehaviour
    {
        [SerializeField] private Vehicle _vehicle;
        [SerializeField] private Transform _berth;
        [Tooltip("Held only this close to the berth (further: it was taken away on purpose).")]
        [SerializeField] private float _reach = 8f;

        private Fun.CushionRide _ride;
        private Vector3? _at;   // a berth given by Tie (instead of _berth)
        private Vector3 _facing;

        private void Awake() => _ride = GetComponent<Fun.CushionRide>();

        /// <summary>Host: hold <paramref name="vehicle"/> at a point, bow toward <paramref name="yaw"/> (adds the
        /// mooring if it has none; null <paramref name="at"/> unties it).</summary>
        public static Mooring Tie(Vehicle vehicle, Vector3? at, float yaw, float reach = 8f)
        {
            if (vehicle == null) return null;
            if (!vehicle.TryGetComponent(out Mooring m))
            {
                m = vehicle.gameObject.AddComponent<Mooring>();
                m._vehicle = vehicle;
            }
            m._at = at;
            m._facing = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            m._reach = reach;
            if (at.HasValue) m._berth = null;
            return m;
        }

        private void FixedUpdate()
        {
            if (_vehicle == null || (_berth == null && !_at.HasValue) || _vehicle.Body == null || _vehicle.Body.isKinematic) return;
            if (_vehicle.Driver != null || _vehicle.OnAutopilot || (_ride != null && _ride.IsTowed)) return;
            Rigidbody body = _vehicle.Body;
            Vector3 berth = _at ?? _berth.position;
            Vector3 delta = berth - body.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > _reach * _reach) return;
            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            body.AddForce(Vector3.ClampMagnitude(delta * 6f - velocity * 3f, 12f), ForceMode.Acceleration);
            // Bow the way it was tied up.
            Vector3 forward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
            Vector3 want = Vector3.ProjectOnPlane(_at.HasValue ? _facing : _berth.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f || want.sqrMagnitude < 0.01f) return;
            float turn = Vector3.SignedAngle(forward, want, Vector3.up);
            body.AddTorque(Vector3.up * (turn * 0.08f - body.angularVelocity.y * 1.5f), ForceMode.Acceleration);
        }
    }
}
