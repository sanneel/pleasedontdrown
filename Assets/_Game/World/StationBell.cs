using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// The station's alarm bell. Any player can ring it; the host rate-limits and broadcasts, so everyone hears
    /// and sees it swing. Later the emergency director rings it too.
    /// </summary>
    public class StationBell : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _swingPivot;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _cooldown = 0.6f;
        [SerializeField] private float _swingKick = 260f;
        [SerializeField] private float _swingStiffness = 60f;
        [SerializeField] private float _swingDamping = 3.5f;

        private float _lastRingTime = float.NegativeInfinity;
        private float _angle;
        private float _angularVelocity;

        public bool CanInteract(PlayerHub player) => true;
        public string GetPrompt(PlayerHub player) => "Ring the alarm bell";
        public void OnInteract(PlayerHub player) => RingServer();

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("ring", "", "Ring the station bell (networked test).", _ => RingServer(), owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("ring", this);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RingServer(NetworkConnection caller = null) => ServerRing(PlayerHub.NameOf(caller));

        /// <summary>Host: ring it (players, drills, later the emergency director).</summary>
        [Server]
        public void ServerRing(string who)
        {
            if (Time.time - _lastRingTime < _cooldown)
                return;
            _lastRingTime = Time.time;
            Debug.Log($"[Bell] rung by {who}");
            RingObservers(who);
        }

        [ObserversRpc]
        private void RingObservers(string who)
        {
            _angularVelocity += _swingKick * (Random.value < 0.5f ? -1f : 1f);
            if (_audio != null)
            {
                _audio.pitch = Random.Range(0.97f, 1.03f);
                _audio.PlayOneShot(ProceduralAudio.Bell);
            }
            FloatingText.Spawn(_swingPivot.position + Vector3.up * 0.5f, "DING!", new Color(1f, 0.85f, 0.3f));
            Debug.Log($"[Bell] DING ({who})");
        }

        private void Update()
        {
            // Damped spring swing, purely visual on each client.
            float accel = -_swingStiffness * _angle - _swingDamping * _angularVelocity;
            _angularVelocity += accel * Time.deltaTime;
            _angle += _angularVelocity * Time.deltaTime;
            _swingPivot.localRotation = Quaternion.Euler(0f, 0f, _angle);
        }
    }
}
