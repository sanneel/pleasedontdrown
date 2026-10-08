using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// Food: hold Secondary with it in your hands to eat it (bites everyone hears), then it's gone and your
    /// food meter (<see cref="PlayerVitals"/>) fills up. The host removes the item. Drinks gulp instead of crunching,
    /// and a beer comes back up a little later as a burp (<see cref="PlayerHub.BurpLater"/>).
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Edible : NetworkBehaviour
    {
        [Tooltip("How much of the food meter one serving fills (0..1).")]
        [SerializeField] private float _food = 0.35f;
        [SerializeField] private float _seconds = 1.8f;
        [SerializeField] private AudioSource _audio;
        [Tooltip("Drunk, not eaten: gulps instead of crunches.")]
        [SerializeField] private bool _drink;
        [Tooltip("Seconds after finishing it that the drinker burps (0 = never).")]
        [SerializeField] private float _burpAfter;
        [Tooltip("A drink: where the lips go (the bottle's mouth), in the item's own space.")]
        [SerializeField] private Vector3 _lip = new(0f, 0.156f, 0f);

        public float Food => _food;
        public float Seconds => _seconds;
        public float BurpAfter => _burpAfter;
        public bool Drink => _drink;
        public Vector3 Lip => _lip;

        /// <summary>Local bite (sound here, relayed to everyone else).</summary>
        public void PlayBite()
        {
            Crunch();
            BiteServer();
        }

        private void Crunch()
        {
            if (_audio != null) _audio.PlayOneShot(_drink ? ActionFoley.Gulp : ActionFoley.Bite, _drink ? 0.85f : 0.75f);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BiteServer(NetworkConnection caller = null)
        {
            var holder = GetComponent<Item>().Holder;
            if (holder == null || holder.Owner != caller) return;
            BiteObservers();
        }

        [ObserversRpc]
        private void BiteObservers()
        {
            // The holder already heard the immediate local bite. Item ownership may
            // belong to the server, so ExcludeOwner alone could play it twice.
            if (GetComponent<Item>().Holder == PlayerHub.Local) return;
            Crunch();
        }

        /// <summary>The eater finished it: the host removes it.</summary>
        public void Consume(PlayerHub eater) => ConsumeServer();

        [ServerRpc(RequireOwnership = false)]
        private void ConsumeServer(NetworkConnection caller = null)
        {
            Item item = GetComponent<Item>();
            PlayerHub holder = item != null ? item.Holder : null;
            if (holder == null || holder.Owner != caller) return;
            Debug.Log($"[Item] {holder.DisplayName} {(_drink ? "drank" : "ate")} a {item.DisplayName}");
            Despawn(gameObject);
        }
    }
}
