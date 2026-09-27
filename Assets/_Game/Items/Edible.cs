using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// Food: hold Secondary with it in your hands to eat it (bites everyone hears), then it's gone and your
    /// food meter (<see cref="PlayerVitals"/>) fills up. The host removes the item.
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Edible : NetworkBehaviour
    {
        [Tooltip("How much of the food meter one serving fills (0..1).")]
        [SerializeField] private float _food = 0.35f;
        [SerializeField] private float _seconds = 1.8f;
        [SerializeField] private AudioSource _audio;

        public float Food => _food;
        public float Seconds => _seconds;

        /// <summary>Local bite (sound here, relayed to everyone else).</summary>
        public void PlayBite()
        {
            Crunch();
            BiteServer();
        }

        private void Crunch()
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Crunch, Random.Range(0.8f, 1f));
        }

        [ServerRpc(RequireOwnership = false)]
        private void BiteServer() => BiteObservers();

        [ObserversRpc(ExcludeOwner = true)]
        private void BiteObservers() => Crunch();

        /// <summary>The eater finished it: the host removes it.</summary>
        public void Consume(PlayerHub eater) => ConsumeServer();

        [ServerRpc(RequireOwnership = false)]
        private void ConsumeServer(NetworkConnection caller = null)
        {
            Item item = GetComponent<Item>();
            PlayerHub holder = item != null ? item.Holder : null;
            if (holder == null || holder.Owner != caller) return;
            Debug.Log($"[Item] {holder.DisplayName} ate a {item.DisplayName}");
            Despawn(gameObject);
        }
    }
}
