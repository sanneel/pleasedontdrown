using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using PleaseDontDrown.Vehicles;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// A bar stool you sit on (its <see cref="Vehicle"/> is a chair): while you sit, the bar's menu card shows and the
    /// number keys order from it (1 fries, 2 cola...); the barista puts it down on the counter in front of you and the
    /// team's wallet pays. The hotbar keys are the menu's while you sit.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class BarSeat : MonoBehaviour
    {
        [SerializeField] private Barista _barista;
        [Tooltip("On the counter in front of this stool: where what you order is put down.")]
        [SerializeField] private Transform _servePoint;

        /// <summary>The local player is sitting at a bar: the number keys order, they don't change the hotbar slot.</summary>
        public static bool MenuOpen { get; private set; }

        private Vehicle _vehicle;
        private float _nextOrder;
        private bool _opened; // this stool opened the menu
        private string[] _lines;
        private int _moneyShown = -1;
        private string _moneyText;

        public Transform ServePoint => _servePoint;
        public Barista Barista => _barista;

        private void Awake() => _vehicle = GetComponent<Vehicle>();

        private void OnEnable() =>
            DevCommands.Register("barorder", "<1-4>", "Order from the bar menu while sitting on a stool (tests).", args =>
            {
                foreach (BarSeat seat in FindObjectsByType<BarSeat>(FindObjectsSortMode.None))
                    if (seat.LocalSitsHere)
                    {
                        seat._barista.OrderFromSeat(PlayerHub.Local, seat, Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) - 1);
                        DevCommands.Print($"ordered {args[0]} at {seat.name}");
                        return;
                    }
                DevCommands.Print("not sitting at a bar");
            }, owner: this);

        private bool LocalSitsHere
        {
            get
            {
                PlayerHub local = PlayerHub.Local;
                return local != null && local.Motor != null && local.Motor.Seat == _vehicle && _barista != null && _barista.HasMenu;
            }
        }

        private void Update()
        {
            bool here = LocalSitsHere;
            if (here != _opened)
            {
                _opened = here;
                MenuOpen = here;
            }
            if (!here) return;
            if (!GameInput.GameplayActive || Time.time < _nextOrder) return;
            int count = Mathf.Min(_barista.Menu.Length, GameInput.Slots.Length);
            for (int i = 0; i < count; i++)
                if (GameInput.Slots[i].WasPressedThisFrame())
                {
                    _nextOrder = Time.time + 0.6f;
                    _barista.OrderFromSeat(PlayerHub.Local, this, i);
                    break;
                }
        }

        private void OnDisable()
        {
            DevCommands.Unregister("barorder", this);
            if (_opened) MenuOpen = _opened = false;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !LocalSitsHere || !GameInput.GameplayActive) return;
            Barista.MenuItem[] menu = _barista.Menu;
            if (_lines == null || _lines.Length != menu.Length)
            {
                _lines = new string[menu.Length];
                for (int i = 0; i < menu.Length; i++)
                    _lines[i] = $"{GameInput.KeyLabel(GameInput.Slots[i])}   {menu[i].Label}";
            }
            if (_moneyShown != Economy.Money || _moneyText == null)
            {
                _moneyShown = Economy.Money;
                _moneyText = "Wallet $" + _moneyShown;
            }
            const float width = 420f, row = 46f;
            float height = 96f + menu.Length * row + 54f;
            var panel = new Rect(Hud.Width - width - 40f, (Hud.Height - height) * 0.5f, width, height);
            Hud.Panel(panel, 0.88f);
            Hud.Label(new Rect(panel.x + 28f, panel.y + 16f, width - 56f, 50f), "MENU", 38f, Hud.Sand, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            Hud.Label(new Rect(panel.x + 28f, panel.y + 16f, width - 56f, 50f), _moneyText, 22f, new Color(0.56f, 1f, 0.56f), TextAnchor.MiddleRight, heavy: true, shadow: false);
            float y = panel.y + 82f;
            for (int i = 0; i < menu.Length; i++)
            {
                bool afford = Economy.Money >= menu[i].Price;
                var color = afford ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                Hud.Label(new Rect(panel.x + 28f, y, width - 140f, row), _lines[i], 26f, color, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                Hud.Label(new Rect(panel.xMax - 128f, y, 100f, row), "$" + menu[i].Price, 26f, afford ? Hud.Teal : color, TextAnchor.MiddleRight, heavy: true, shadow: false);
                y += row;
            }
            Hud.Label(new Rect(panel.x + 28f, panel.yMax - 50f, width - 56f, 36f), $"{GameInput.KeyLabel(GameInput.Interact)} to stand up", 18f,
                new Color(1f, 1f, 1f, 0.6f), TextAnchor.MiddleLeft, shadow: false);
        }
    }
}
