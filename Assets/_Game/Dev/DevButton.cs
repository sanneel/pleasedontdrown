using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>A big push button on the dev island's test board (E to press).</summary>
    public class DevButton : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private DevAction _action;
        [SerializeField] private string _label = "Press";
        [SerializeField] private Transform _cap;
        [SerializeField] private AudioSource _audio;

        private float _pressedAt = -10f;
        private Vector3 _capRest;

        private void Awake()
        {
            if (_cap != null) _capRest = _cap.localPosition;
        }

        public bool CanInteract(PlayerHub player) => true;
        public string GetPrompt(PlayerHub player) => _label;

        public void OnInteract(PlayerHub player)
        {
            player.Gesture(AvatarGesture.Interact, transform.position);
            PressServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void PressServer(NetworkConnection caller = null)
        {
            PlayerHub by = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p.Owner == caller) by = p;
            if (by == null || (by.transform.position - transform.position).sqrMagnitude > 6f * 6f) return;
            if (DevIsland.Instance != null) DevIsland.Instance.ServerAction(_action, by);
            PressedObservers();
        }

        [ObserversRpc]
        private void PressedObservers()
        {
            _pressedAt = Time.time;
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Click, 1f);
        }

        private void Update()
        {
            if (_cap == null) return;
            float t = Time.time - _pressedAt;
            float press = t < 0.25f ? Mathf.Sin(t / 0.25f * Mathf.PI) : 0f;
            _cap.localPosition = _capRest - Vector3.forward * (0.03f * press);
        }
    }
}
