using UnityEngine;
using PleaseDontDrown.World.Water;

namespace PleaseDontDrown.Vehicles
{
    /// <summary>Rental skis stay tethered until the crew recovers the fleet keys. Only their physics authority pulls them.</summary>
    public sealed class RentalMooring : MonoBehaviour
    {
        [SerializeField] private Vehicle _vehicle;
        [SerializeField] private Transform _berth;
        private void FixedUpdate()
        {
            if (_vehicle == null || _berth == null || !_vehicle.IsLocked || _vehicle.Body.isKinematic) return;
            Vector3 delta = _berth.position - _vehicle.Body.position;
            delta.y = 0f;
            Vector3 velocity = _vehicle.Body.linearVelocity;
            velocity.y = 0f;
            _vehicle.Body.AddForce(Vector3.ClampMagnitude(delta * 8f - velocity * 4f, 18f), ForceMode.Acceleration);
        }
    }
}
