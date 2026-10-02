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
    /// A counter that sells equipment from the shared wallet (the hotel reception sells guns). Interact opens a
    /// list (drawn with <see cref="Hud"/>); buying is checked by the host, and the item appears on the counter. Holding a gun, the list also
    /// offers parts for it (sights, barrels, laser, bigger magazine, better rounds), fitted on the spot.
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
        [Tooltip("The dev island's armory: everything is free.")]
        [SerializeField] private bool _free;

        private bool _open;           // local window
        private bool _pushedUI, _openedFromAfar;
        // The window's words, kept between frames.
        private string _titleUpper, _moneyText, _partsTitle;
        private string[] _productNames, _productPrices;
        private int _moneyShown, _partsFrame = -1;
        private System.Collections.Generic.List<Combat.Weapon.PartOffer> _parts;
        private Combat.Weapon _partsGun;

        private void Awake() => useGUILayout = false; // drawn with fixed boxes: no layout pass needed

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
        public string GetPrompt(PlayerHub player) => _free ? $"{_title}: take guns and parts (free)" : $"{_title}: buy equipment (${Economy.Money})";
        public void OnInteract(PlayerHub player) => SetOpen(true);

        private void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (open && !_pushedUI) { GameInput.PushUI(); _pushedUI = true; }
            if (!open && _pushedUI) { GameInput.PopUI(); _pushedUI = false; }
            if (!open) _openedFromAfar = false;
        }

        private void OnDisable() => SetOpen(false);

        private void Update()
        {
            if (!_open) return;
            PlayerHub local = PlayerHub.Local;
            if (GameInput.ToggleMenu.WasPressedThisFrame() || local == null ||
                (!_openedFromAfar && (local.transform.position - transform.position).sqrMagnitude > 6f * 6f))
                SetOpen(false);
        }

        private const float ProductsWidth = 700f, PartsWidth = 560f, ProductRow = 78f, PartRow = 54f;

        private void OnGUI()
        {
            if (!_open) return;
            Combat.Weapon gun = HeldGun(PlayerHub.Local);
            // What the held gun takes changes when a part is fitted, not every event: list it once a frame.
            if (_partsFrame != Time.frameCount)
            {
                _partsFrame = Time.frameCount;
                _parts = gun != null ? gun.Offers() : null;
            }
            int parts = _parts != null ? _parts.Count : 0;
            if (_products.Length != _productNames?.Length) NameProducts();

            Hud.Dim(0.45f);
            float width = ProductsWidth + (parts > 0 ? PartsWidth : 0f);
            float height = 104f + Mathf.Max(_products.Length * ProductRow, parts > 0 ? 50f + parts * PartRow : 0f) + 92f;
            // A long list on a small window shrinks to fit instead of running off the screen.
            float fit = Mathf.Min(1f, (Hud.Height - 40f) / height, (Hud.Width - 40f) / width);
            Matrix4x4 before = GUI.matrix;
            if (fit < 1f) GUIUtility.ScaleAroundPivot(new Vector2(fit, fit), new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            var panel = new Rect((Hud.Width - width) * 0.5f, (Hud.Height - height) * 0.5f, width, height);
            Hud.Panel(panel, 0.92f);
            Hud.Label(new Rect(panel.x + 32f, panel.y + 18f, width - 260f, 56f), _titleUpper ??= _title.ToUpperInvariant(), 40f, Color.white, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            if (!_free)
            {
                if (_moneyShown != Economy.Money || _moneyText == null)
                {
                    _moneyShown = Economy.Money;
                    _moneyText = "$" + _moneyShown;
                }
                Hud.Label(new Rect(panel.xMax - 232f, panel.y + 18f, 200f, 56f), _moneyText, 40f, new Color(0.56f, 1f, 0.56f), TextAnchor.MiddleRight, heavy: true, shadow: false);
            }

            float x = panel.x + 32f, y = panel.y + 96f;
            for (int i = 0; i < _products.Length; i++)
            {
                Product p = _products[i];
                Hud.Label(new Rect(x, y + 4f, ProductsWidth - 290f, 34f), _productNames[i], 26f, Color.white, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                Hud.Label(new Rect(x, y + 38f, ProductsWidth - 290f, 28f), p.Blurb, 18f, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft, shadow: false);
                bool afford = _free || Economy.Money >= p.Price;
                GUI.enabled = afford;
                if (Hud.Button(new Rect(x + ProductsWidth - 264f, y + 10f, 200f, 52f), _free ? "TAKE" : _productPrices[i], centred: true, small: true))
                    BuyServer(i);
                GUI.enabled = true;
                y += ProductRow;
            }

            if (parts > 0)
            {
                float px = panel.x + ProductsWidth;
                float py = panel.y + 96f;
                Hud.Fill(new Rect(px - 16f, py, 2f, height - 96f - 100f), new Color(1f, 1f, 1f, 0.15f));
                if (!ReferenceEquals(gun, _partsGun))
                {
                    _partsGun = gun;
                    _partsTitle = "FOR YOUR " + gun.DisplayName.ToUpperInvariant();
                }
                Hud.Label(new Rect(px, py, PartsWidth - 32f, 40f), _partsTitle, 22f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                py += 50f;
                for (int i = 0; i < parts; i++)
                {
                    Combat.Weapon.PartOffer part = _parts[i];
                    Hud.Label(new Rect(px, py, PartsWidth - 230f, PartRow - 8f), part.Name, 22f, Color.white, TextAnchor.MiddleLeft, shadow: false);
                    bool afford = _free || Economy.Money >= part.Price;
                    GUI.enabled = afford && !part.Fitted;
                    string label = part.Fitted ? "FITTED" : _free ? "FIT" : "$" + part.Price;
                    if (Hud.Button(new Rect(px + PartsWidth - 216f, py, 184f, PartRow - 10f), label, centred: true, small: true))
                        BuyPartServer(gun.NetworkObject, (byte)part.Kind, part.Index);
                    GUI.enabled = true;
                    py += PartRow;
                }
            }

            bool close = Hud.Button(new Rect(panel.x + (width - 280f) * 0.5f, panel.yMax - 78f, 280f, 56f), "CLOSE", centred: true);
            GUI.matrix = before;
            if (close) SetOpen(false);
        }

        /// <summary>What each product is called and costs, as shown on its row (made once, not every frame).</summary>
        private void NameProducts()
        {
            _productNames = new string[_products.Length];
            _productPrices = new string[_products.Length];
            for (int i = 0; i < _products.Length; i++)
            {
                Item prefab = GameContent.Items != null ? GameContent.Items.Find(_products[i].Item) : null;
                _productNames[i] = prefab != null ? prefab.DisplayName : _products[i].Item;
                _productPrices[i] = "BUY  $" + _products[i].Price;
            }
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

        private static Combat.Weapon HeldGun(PlayerHub player)
        {
            Item held = player != null && player.Hands != null ? player.Hands.HeldItem : null;
            return held != null ? held.GetComponent<Combat.Weapon>() : null;
        }

        [ServerRpc(RequireOwnership = false)]
        private void BuyPartServer(NetworkObject gunObject, byte kind, byte index, NetworkConnection caller = null)
        {
            PlayerHub buyer = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p.Owner == caller) buyer = p;
            if (buyer == null || gunObject == null || !_available.Value) return;
            if ((buyer.transform.position - transform.position).sqrMagnitude > 7f * 7f) return;
            if (!gunObject.TryGetComponent(out Combat.Weapon gun) || !gunObject.TryGetComponent(out Item item) || item.Holder != buyer) return;
            ServerBuyPart(buyer, gun, (Combat.Weapon.PartKind)kind, index);
        }

        /// <summary>Host: fit a part to a player's gun if the wallet allows.</summary>
        [Server]
        public bool ServerBuyPart(PlayerHub buyer, Combat.Weapon gun, Combat.Weapon.PartKind kind, byte index)
        {
            int price = gun.PriceOf(kind, index);
            if (price < 0 || gun.IsFitted(kind, index)) return false;
            if (_free) price = 0;
            if (price > 0 && (Economy.Instance == null || !Economy.Instance.ServerSpend(price, $"{kind} for the {gun.DisplayName}")))
            {
                Tell(buyer.Owner, $"Not enough money: that costs ${price} (you have ${Economy.Money}).");
                return false;
            }
            gun.ServerFit(kind, index);
            PartObservers(gun.DisplayName, price, transform.position + Vector3.up * 1.5f);
            Debug.Log($"[Shop] {buyer.DisplayName} fitted {kind} {index} to the {gun.DisplayName} for ${price}");
            return true;
        }

        [ObserversRpc]
        private void PartObservers(string gunName, int price, Vector3 at)
        {
            if (price > 0) FloatingText.Spawn(at, $"-${price}", new Color(1f, 0.55f, 0.4f), 1f, 1.6f);
            PlayerHud.ShowToast($"Fitted to your <b>{gunName}</b>.", 3f);
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
            int price = _free ? 0 : product.Price;
            if (price > 0 && (Economy.Instance == null || !Economy.Instance.ServerSpend(price, $"bought {product.Item}")))
            {
                Tell(buyer.Owner, $"Not enough money: the {prefab.DisplayName} costs ${product.Price} (you have ${Economy.Money}).");
                return false;
            }
            Vector3 at = _spawnPoint != null ? _spawnPoint.position : transform.position + Vector3.up * 1.2f;
            Item bought = Instantiate(prefab, at, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            Spawn(bought.gameObject);
            BoughtObservers(prefab.DisplayName, price, at);
            Debug.Log($"[Shop] {buyer.DisplayName} bought {prefab.DisplayName} for ${product.Price}");
            ServerPurchased?.Invoke(this, buyer, product.Item);
            return true;
        }

        [ObserversRpc]
        private void BoughtObservers(string itemName, int price, Vector3 at)
        {
            if (price > 0) FloatingText.Spawn(at + Vector3.up * 0.4f, $"-${price}", new Color(1f, 0.55f, 0.4f), 1f, 1.6f);
            PlayerHud.ShowToast(price > 0 ? $"Bought a <b>{itemName}</b>. It's on the counter." : $"A <b>{itemName}</b> is on the counter.", 3f);
        }

        [TargetRpc]
        private void Tell(NetworkConnection target, string text) => PlayerHud.ShowToast(text, 3f);

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("shop", "", "Open the nearest shop counter's window (to look at it in tests).", _ =>
            {
                PlayerHub me = PlayerHub.Local;
                ShopCounter nearest = null;
                foreach (ShopCounter counter in FindObjectsByType<ShopCounter>(FindObjectsSortMode.None))
                    if (me != null && (nearest == null || (counter.transform.position - me.transform.position).sqrMagnitude < (nearest.transform.position - me.transform.position).sqrMagnitude))
                        nearest = counter;
                if (nearest == null) return;
                nearest._openedFromAfar = true; // a test looking at the window: don't close it for standing too far away
                nearest.SetOpen(true);
            }, owner: this);
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
            DevCommands.Unregister("shop", this);
            SetOpen(false);
        }
    }
}
