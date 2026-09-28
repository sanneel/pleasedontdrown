using System;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// A counter that sells equipment from the shared wallet (the hotel reception sells the pistol). Interact opens a
    /// small list; buying is checked by the host, and the item appears on the counter.
    /// </summary>
    public class ShopCounter : NetworkBehaviour, IInteractionHandler
    {
        [Serializable]
        public struct Product
        {
            public string Item;      // item catalog name
            public int Price;
            public string Blurb;
        }

        [SerializeField] private string _title = "Reception";
        [SerializeField] private Product[] _products = Array.Empty<Product>();
        [SerializeField] private Transform _spawnPoint;
        [Tooltip("Closed until the story opens it (the receptionist has to talk to you first).")]
        [SerializeField] private bool _startsOpen;

        private bool _open;           // local window
        private bool _pushedUI;
        private GUIStyle _style, _header, _button;

        /// <summary>Host: someone bought something.</summary>
        public static event Action<ShopCounter, PlayerHub, string> ServerPurchased;

        public string Title => _title;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerPurchased = null;

        private readonly FishNet.Object.Synchronizing.SyncVar<bool> _available = new FishNet.Object.Synchronizing.SyncVar<bool>();

        public override void OnStartServer()
        {
            base.OnStartServer();
            _available.Value = _startsOpen;
        }

        /// <summary>Host: open or close the shop.</summary>
        [Server] public void ServerSetAvailable(bool available) => _available.Value = available;

        public bool CanInteract(PlayerHub player) => _available.Value;
        public string GetPrompt(PlayerHub player) => $"{_title}: buy equipment (${Economy.Money})";
        public void OnInteract(PlayerHub player) => SetOpen(true);

        private void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (open && !_pushedUI) { GameInput.PushUI(); _pushedUI = true; }
            if (!open && _pushedUI) { GameInput.PopUI(); _pushedUI = false; }
        }

        private void OnDisable() => SetOpen(false);

        private void Update()
        {
            if (!_open) return;
            PlayerHub local = PlayerHub.Local;
            if (GameInput.ToggleMenu.WasPressedThisFrame() || local == null || (local.transform.position - transform.position).sqrMagnitude > 6f * 6f)
                SetOpen(false);
        }

        private void OnGUI()
        {
            if (!_open) return;
            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, wordWrap = true, normal = { textColor = Color.white } };
            _header ??= new GUIStyle(_style) { fontSize = 24, fontStyle = FontStyle.Bold };
            _button ??= new GUIStyle(GUI.skin.button) { fontSize = 16, richText = true };
            float w = 520f, h = 110f + _products.Length * 64f;
            var area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(area.x + 18f, area.y + 14f, area.width - 36f, area.height - 28f));
            GUILayout.Label($"{_title}   <color=#90ff90>${Economy.Money}</color>", _header);
            GUILayout.Space(8f);
            for (int i = 0; i < _products.Length; i++)
            {
                Product p = _products[i];
                Item prefab = GameContent.Items != null ? GameContent.Items.Find(p.Item) : null;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{(prefab != null ? prefab.DisplayName : p.Item)}</b>\n<size=13>{p.Blurb}</size>", _style, GUILayout.Width(330f));
                bool afford = Economy.Money >= p.Price;
                GUI.enabled = afford;
                if (GUILayout.Button(afford ? $"Buy  ${p.Price}" : $"${p.Price}", _button, GUILayout.Height(44f)))
                    BuyServer(i);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(6f);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close (Esc)", _button, GUILayout.Height(30f))) SetOpen(false);
            GUILayout.EndArea();
        }

        [ServerRpc(RequireOwnership = false)]
        private void BuyServer(int index, NetworkConnection caller = null)
        {
            PlayerHub buyer = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p.Owner == caller) buyer = p;
            if (buyer == null || !_available.Value || index < 0 || index >= _products.Length) return;
            if ((buyer.transform.position - transform.position).sqrMagnitude > 7f * 7f) return;
            ServerBuy(buyer, index);
        }

        /// <summary>Host: sell product <paramref name="index"/> (also the 'buy' test command).</summary>
        [Server]
        public bool ServerBuy(PlayerHub buyer, int index)
        {
            Product product = _products[index];
            Item prefab = GameContent.Items != null ? GameContent.Items.Find(product.Item) : null;
            if (prefab == null)
            {
                Debug.LogError($"[Shop] no item '{product.Item}' in the catalog");
                return false;
            }
            if (Economy.Instance == null || !Economy.Instance.ServerSpend(product.Price, $"bought {product.Item}"))
            {
                Tell(buyer.Owner, $"Not enough money: the {prefab.DisplayName} costs ${product.Price} (you have ${Economy.Money}).");
                return false;
            }
            Vector3 at = _spawnPoint != null ? _spawnPoint.position : transform.position + Vector3.up * 1.2f;
            Item bought = Instantiate(prefab, at, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            Spawn(bought.gameObject);
            BoughtObservers(prefab.DisplayName, product.Price, at);
            Debug.Log($"[Shop] {buyer.DisplayName} bought {prefab.DisplayName} for ${product.Price}");
            ServerPurchased?.Invoke(this, buyer, product.Item);
            return true;
        }

        [ObserversRpc]
        private void BoughtObservers(string itemName, int price, Vector3 at)
        {
            FloatingText.Spawn(at + Vector3.up * 0.4f, $"-${price}", new Color(1f, 0.55f, 0.4f), 1f, 1.6f);
            PlayerHud.ShowToast($"Bought a <b>{itemName}</b>. It's on the counter.", 3f);
        }

        [TargetRpc]
        private void Tell(NetworkConnection target, string text) => PlayerHud.ShowToast(text, 3f);

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("buy", "<n>", "Buy product n (1..) at the nearest open shop counter (automated tests).", args =>
            {
                int n = Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) - 1;
                BuyServer(n);
                DevCommands.Print($"buying product {n + 1} at {_title}");
            }, cheat: true, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("buy", this);
            SetOpen(false);
        }
    }
}
