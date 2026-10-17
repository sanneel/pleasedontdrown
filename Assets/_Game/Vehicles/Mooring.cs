using UnityEngine;

namespace PleaseDontDrown.Vehicles
{
    /// <summary>
    /// A boat tied up at its berth (the hotel's yacht at the marina, the cushion at its jetty): while nobody drives it,
    /// nothing tows it and it's still near the berth, it's held there, heading and all, instead of drifting off on the
    /// waves. Taken away and left somewhere else, it stays where it was left. Only its physics authority pulls it.
    /// </summary>
    public sealed class Mooring : MonoBehaviour
    {
        [SerializeField] private Vehicle _vehicle;
        [SerializeField] private Transform _berth;
        [Tooltip("Held only this close to the berth (further: it was taken away on purpose).")]
        [SerializeField] private float _reach = 8f;

        private Fun.CushionRide _ride;

        private void Awake() => _ride = GetComponent<Fun.CushionRide>();

        private void FixedUpdate()
        {
            if (_vehicle == null || _berth == null || _vehicle.Body == null || _vehicle.Body.isKinematic) return;
            if (_vehicle.Driver != null || _vehicle.OnAutopilot || (_ride != null && _ride.IsTowed)) return;
            Rigidbody body = _vehicle.Body;
            Vector3 delta = _berth.position - body.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > _reach * _reach) return;
            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            body.AddForce(Vector3.ClampMagnitude(delta * 6f - velocity * 3f, 12f), ForceMode.Acceleration);
            // Bow the way it was tied up.
            Vector3 forward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
            Vector3 want = Vector3.ProjectOnPlane(_berth.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f || want.sqrMagnitude < 0.01f) return;
            float turn = Vector3.SignedAngle(forward, want, Vector3.up);
            body.AddTorque(Vector3.up * (turn * 0.08f - body.angularVelocity.y * 1.5f), ForceMode.Acceleration);
        }
    }
}
