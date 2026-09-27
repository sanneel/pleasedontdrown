using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>Scene entry point to shared content assets (catalogs). One per game scene.</summary>
    public class GameContent : MonoBehaviour
    {
        [SerializeField] private ItemCatalog _items;

        public static GameContent Instance { get; private set; }
        public static ItemCatalog Items => Instance != null ? Instance._items : null;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
