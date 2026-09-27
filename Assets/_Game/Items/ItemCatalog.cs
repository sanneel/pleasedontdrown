using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>Every spawnable item prefab. Used by the spawn command now and by shops / emergencies later.</summary>
    [CreateAssetMenu(menuName = "PLEASE DON'T DROWN/Item Catalog", fileName = "ItemCatalog")]
    public class ItemCatalog : ScriptableObject
    {
        [SerializeField] private List<Item> _items = new();

        public IReadOnlyList<Item> Items => _items;

        /// <summary>Finds by display name or prefab name, ignoring case and spaces ("life ring" == "LifeRing").</summary>
        public Item Find(string name)
        {
            string key = Normalize(name);
            foreach (Item item in _items)
                if (item != null && (Normalize(item.DisplayName) == key || Normalize(item.name) == key))
                    return item;
            return null;
        }

        private static string Normalize(string s) => (s ?? string.Empty).Replace(" ", "").ToLowerInvariant();
    }
}
