using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// Host-owned ocean conditions (currently the wave size). Day modifiers like Storm Day will drive this.
    /// </summary>
    public class OceanState : NetworkBehaviour
    {
        private readonly SyncVar<float> _waveScale = new SyncVar<float>(1f);

        private static OceanState _instance;

        /// <summary>Multiplier on all wave amplitudes (1 = calm beach day).</summary>
        public static float WaveScale => _instance != null ? _instance._waveScale.Value : 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        private void Awake() => _instance = this;

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("waves", "<scale>", "Wave size for everyone (1 = normal, 3 = storm).", args =>
                SetWaveScaleServer(Mathf.Clamp(DevCommands.ParseFloat(args, 0), 0f, 4f)), cheat: true, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("waves", this);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetWaveScaleServer(float scale)
        {
            if (!DevCommands.CheatsAllowed) return;
            _waveScale.Value = scale;
            Debug.Log($"[Ocean] wave scale {scale:F2}");
        }
    }
}
