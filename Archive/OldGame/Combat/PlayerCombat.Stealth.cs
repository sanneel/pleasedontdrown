using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// A lifeguard as the sentries see and hear them (host): how fast they move (worked out from where they are,
    /// the host doesn't run their movement), the noise their feet make, and the pirate disguise (hat and eye patch
    /// from the THIEF on island 3; a monkey can steal the hat).
    /// </summary>
    public partial class PlayerCombat
    {
        private readonly SyncVar<bool> _disguised = new SyncVar<bool>();
        private Vector3 _serverLastPosition;
        private float _serverSpeed, _nextStepNoise;
        private bool _serverMoved;

        /// <summary>Every machine: wearing the pirate disguise.</summary>
        public bool IsDisguised => _disguised.Value;
        /// <summary>Host: how fast this lifeguard is going over the ground (m/s, smoothed).</summary>
        public float ServerSpeed => _serverSpeed;

        [Server]
        public void ServerSetDisguise(bool on)
        {
            if (_disguised.Value == on) return;
            _disguised.Value = on;
            Debug.Log($"[Stealth] {_hub.DisplayName} {(on ? "puts on" : "loses")} the pirate disguise");
        }

        /// <summary>Host, every frame: the speed estimate and footstep noise.</summary>
        private void ServerStealthUpdate()
        {
            Vector3 p = transform.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (!_serverMoved)
            {
                _serverMoved = true;
                _serverLastPosition = p;
            }
            Vector3 d = p - _serverLastPosition;
            _serverLastPosition = p;
            float raw = new Vector2(d.x, d.z).magnitude / dt;
            if (raw > 30f) raw = 0f; // a teleport, not a sprint
            _serverSpeed = Mathf.Lerp(_serverSpeed, raw, 1f - Mathf.Exp(-6f * dt));
            if (_downed.Value || Time.time < _nextStepNoise || _serverSpeed < 2.2f) return;
            if (_hub.Motor != null && _hub.Motor.Seat != null) return; // riding something: the engine is the noise
            _nextStepNoise = Time.time + 0.5f;
            Stealth.ServerNoise(p, _serverSpeed > 4.5f ? Stealth.Running : Stealth.Walking, _hub, NoiseKind.Footsteps);
        }
    }
}
