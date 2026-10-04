using UnityEngine;

namespace PleaseDontDrown.Player
{
    public partial class PlayerMotor
    {
        /// <summary>Owner: no control for a while (knocked down and seeing stars); the body just stands, slides or floats.</summary>
        public void Stun(float seconds) => _controlLossUntil = Mathf.Max(_controlLossUntil, Time.time + seconds);
    }
}
