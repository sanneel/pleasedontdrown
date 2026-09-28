using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Something a tourist lost (wallet, phone, sunglasses, watch), something the robber stole, or evidence (the
    /// robber's baggies). Pick it up and hand it in at Lost &amp; Found for the reward. Shown as "Brenda's Phone".
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class LostItem : NetworkBehaviour
    {
        [SerializeField] private int _reward = 25;
        [Tooltip("Handed in as police evidence rather than returned to an owner.")]
        [SerializeField] private bool _evidence;

        private readonly SyncVar<string> _owner = new SyncVar<string>();
        private readonly SyncVar<bool> _stolen = new SyncVar<bool>();

        private Item _item;
        private string _baseName;

        public string OwnerName => _owner.Value;
        public bool IsStolen => _stolen.Value;
        public bool IsEvidence => _evidence;
        public int Reward => _stolen.Value ? Mathf.Max(_reward, 30) : _reward;
        public Item Item => _item;
        /// <summary>"wallet", "phone"... (the item's own name).</summary>
        public string Kind => _baseName;

        private void Awake()
        {
            _item = GetComponent<Item>();
            _baseName = _item.DisplayName;
            _owner.OnChange += (_, _, _) => Rename();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Rename();
        }

        private void Rename()
        {
            string owner = _owner.Value;
            _item.SetDisplayName(string.IsNullOrEmpty(owner) ? _baseName : $"{owner}'s {_baseName}");
        }

        [Server]
        public void ServerSetup(string owner, bool stolen)
        {
            _owner.Value = owner ?? string.Empty;
            _stolen.Value = stolen;
        }
    }
}
