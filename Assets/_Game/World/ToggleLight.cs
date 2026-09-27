using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// A light switch whose state lives on the host (SyncVar), so late joiners see the correct state.
    /// Put this on the switch; it drives any number of lights and emissive bulbs.
    /// </summary>
    public class ToggleLight : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private Light[] _lights;
        [SerializeField] private Renderer[] _bulbs;
        [SerializeField] private Color _emission = new Color(1f, 0.78f, 0.45f) * 3f;
        [SerializeField] private AudioSource _audio;

        private readonly SyncVar<bool> _isOn = new SyncVar<bool>(true);
        private MaterialPropertyBlock _block;

        public bool CanInteract(PlayerHub player) => true;
        public string GetPrompt(PlayerHub player) => _isOn.Value ? "Turn the light off" : "Turn the light on";
        public void OnInteract(PlayerHub player) => ToggleServer();

        private void Awake() => _isOn.OnChange += (_, next, _) => Apply(next, playSound: true);

        public override void OnStartClient()
        {
            base.OnStartClient();
            Apply(_isOn.Value, playSound: false);
            DevCommands.Register("lights", "", "Toggle the shack light (networked test).", _ => ToggleServer(), owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("lights", this);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ToggleServer(NetworkConnection caller = null)
        {
            _isOn.Value = !_isOn.Value;
            Debug.Log($"[Light] {(_isOn.Value ? "on" : "off")} by {PlayerHub.NameOf(caller)}");
        }

        private void Apply(bool on, bool playSound)
        {
            foreach (Light l in _lights)
                if (l != null) l.enabled = on;

            _block ??= new MaterialPropertyBlock();
            foreach (Renderer r in _bulbs)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor("_EmissionColor", on ? _emission : Color.black);
                r.SetPropertyBlock(_block);
            }

            if (playSound && _audio != null)
                _audio.PlayOneShot(ProceduralAudio.Click);
        }
    }
}
