using FishNet;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Host: keeps this shop open exactly when another one is (island 2's gun shop opens and closes with the hotel
    /// reception, so the story's pacing doesn't change: the reception opens when the receptionist has talked to you).
    /// </summary>
    [RequireComponent(typeof(ShopCounter))]
    public class ShopFollowsAnother : MonoBehaviour
    {
        [SerializeField] private ShopCounter _leader;

        private ShopCounter _shop;
        private float _nextCheck;

        private void Awake() => _shop = GetComponent<ShopCounter>();

        private void Update()
        {
            if (_leader == null || Time.time < _nextCheck || !InstanceFinder.IsServerStarted || !_shop.IsSpawned) return;
            _nextCheck = Time.time + 0.5f;
            if (_shop.IsOpen != _leader.IsOpen) _shop.ServerSetAvailable(_leader.IsOpen);
        }
    }
}
