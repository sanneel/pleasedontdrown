using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// Keeps a few of one item ready at a spot (rescue rings by the tower, basketballs at the hoop). Host: every few
    /// seconds, if fewer than the spots' count lie near them, a new one appears on a free spot. Ones that drifted far
    /// out to sea or were left lying about are cleared away once there are too many in the world.
    /// </summary>
    public class ItemRack : NetworkBehaviour
    {
        [SerializeField] private string _itemName = "Life Ring";
        [SerializeField] private Transform[] _spots = new Transform[0];
        [SerializeField] private float _checkEvery = 6f;
        [SerializeField] private float _nearRadius = 2.5f;
        [Tooltip("Most of this item there may be in the world before old, untouched ones far from the rack are taken away.")]
        [SerializeField] private int _worldCap = 8;

        private float _nextCheck;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _nextCheck = Time.time + 3f;
        }

        private void Update()
        {
            if (!IsServerInitialized || Time.time < _nextCheck || _spots.Length == 0) return;
            _nextCheck = Time.time + _checkEvery;

            var all = new List<Item>();
            foreach (Item item in Item.All)
                if (item != null && item.IsSpawned && item.DisplayName == _itemName) all.Add(item);

            // Too many about: the oldest loose one far from here goes (never one somebody holds or just threw).
            if (all.Count > _worldCap)
            {
                Item oldest = null;
                foreach (Item item in all)
                    if (!item.IsHeld && !NearRack(item.transform.position, _nearRadius * 3f) && Time.time - item.ReleasedAt > 60f &&
                        (oldest == null || item.ReleasedAt < oldest.ReleasedAt))
                        oldest = item;
                if (oldest != null)
                {
                    Despawn(oldest.gameObject);
                    all.Remove(oldest);
                }
            }

            // Restock one free spot at a time.
            foreach (Transform spot in _spots)
            {
                bool taken = false;
                foreach (Item item in all)
                    if (!item.IsHeld && (item.transform.position - spot.position).sqrMagnitude < 0.6f * 0.6f) taken = true;
                if (taken) continue;
                Item prefab = GameContent.Items != null ? GameContent.Items.Find(_itemName) : null;
                if (prefab == null) return;
                Item made = Instantiate(prefab, spot.position, spot.rotation);
                Spawn(made.gameObject);
                return;
            }
        }

        private bool NearRack(Vector3 position, float radius)
        {
            foreach (Transform spot in _spots)
                if ((spot.position - position).sqrMagnitude < radius * radius) return true;
            return false;
        }
    }
}
