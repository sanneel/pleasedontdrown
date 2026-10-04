using System.Collections;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The human cannon on the beach: climb in (Interact), the fuse fizzes, BOOM, and you sail out over the sea and
    /// land with a splash (which the cannonball judge rates). The lifeguard's own machine flies them; everyone hears
    /// the bang and sees the smoke.
    /// </summary>
    public class HumanCannon : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _mouth;     // forward = where it fires
        [SerializeField] private float _power = 23f;
        [SerializeField] private AudioSource _audio;

        private bool _loading;

        public bool CanInteract(PlayerHub player) => !_loading && player != null && player.Motor != null && player.Motor.Seat == null;
        public string GetPrompt(PlayerHub player) => "Climb into the cannon";

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner || _loading) return;
            StartCoroutine(Fire(player));
        }

        private IEnumerator Fire(PlayerHub player)
        {
            _loading = true;
            PlayerHud.ShowToast("In you go... 3... 2... 1...", 1.5f);
            FuseServer();
            // Tucked in the barrel, looking where it points.
            Vector3 inside = _mouth.position - _mouth.forward * 1.2f - Vector3.up * 1.4f;
            float until = Time.time + 1.6f;
            while (Time.time < until && player != null)
            {
                player.Motor.Teleport(inside);
                player.Motor.Stun(0.2f);
                player.Look.LookAt(_mouth.position + _mouth.forward * 10f);
                yield return new WaitForFixedUpdate();
            }
            if (player != null)
            {
                player.Motor.Teleport(_mouth.position + _mouth.forward * 0.6f - Vector3.up * 1.0f);
                player.Motor.AddImpulse(_mouth.forward * _power);
                BoomServer();
            }
            yield return new WaitForSeconds(1f);
            _loading = false;
        }

        [ServerRpc(RequireOwnership = false)]
        private void FuseServer() => FuseObservers();

        [ObserversRpc]
        private void FuseObservers()
        {
            if (_audio != null) _audio.PlayOneShot(FunSounds.Swish, 0.6f);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BoomServer() => BoomObservers();

        [ObserversRpc]
        private void BoomObservers()
        {
            if (_audio != null)
            {
                _audio.PlayOneShot(Core.ProceduralAudio.Shot(Combat.GunSound.Shotgun), 1f);
                _audio.PlayOneShot(FunSounds.Whoop, 0.8f);
            }
            FloatingText.Spawn(_mouth.position + Vector3.up * 0.5f, "BOOM!", new Color(1f, 0.55f, 0.2f), 1.6f, 1.4f);
            for (int i = 0; i < 6; i++)
                FloatingText.Spawn(_mouth.position + Random.insideUnitSphere * 0.5f, "~", new Color(0.85f, 0.85f, 0.85f, 0.8f), 2f, 1.2f); // puffs of smoke
        }
    }
}
