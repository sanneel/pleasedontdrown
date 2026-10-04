using UnityEngine;

namespace PleaseDontDrown.Player
{
    public partial class PlayerMotor
    {
        /// <summary>
        /// Owner, every physics step: pulled along at this velocity (led by the hand). No say in where the feet go,
        /// but the body moves smoothly with the physics (no teleport steps) and the view stays the player's own.
        /// </summary>
        public void Drag(Vector3 velocity)
        {
            if (_rb == null || _rb.isKinematic || Seat != null) return;
            Stun(0.2f);
            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(velocity.x, Mathf.Max(v.y, velocity.y), velocity.z);
            _moveVelocity = new Vector3(velocity.x, 0f, velocity.z);
        }
    }
}
