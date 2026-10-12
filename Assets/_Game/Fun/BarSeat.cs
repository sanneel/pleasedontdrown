using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using PleaseDontDrown.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// A seat you order from (its <see cref="Vehicle"/> is a chair): a bar stool at the counter, or an armchair at one of
    /// the bar's little tables (two face each other across each one). Sit down and the menu card opens: click a dish
    /// or press its number; the team's wallet pays and it's put down in front of you (on the counter or the table) and
    /// into your hand when it's free, so you can eat it sitting there (hold Secondary). Tab opens the menu again, Esc
    /// or Close puts it away (to look about and eat), Interact stands you up.
    /// </summary>
    [RequireComponent(typeof(Vehicle))]
    public class BarSeat : MonoBehaviour
    {
        [SerializeField] private Barista _barista;
        [Tooltip("On the counter or table in front of this seat: where what you order is put down.")]
        [SerializeField] private Transform _servePoint;

        /// <summary>The local player is sitting at a bar: the number keys order, they don't change the hotbar slot.</summary>
        public static bool MenuOpen { get; private set; }

        private Vehicle _vehicle;
        private float _nextOrder;
        private bool _seated;         // the local player sits here
        private bool _cardOpen;       // the menu card is up (mouse free)
        private bool _pushedUI;
        private int _moneyShown = -1;
        private string _moneyText;
        private string[] _prices;

        public Transform ServePoint => _servePoint;
        public Barista Barista => _barista;

        private void Awake()
        {
            _vehicle = GetComponent<Vehicle>();
            useGUILayout = false;
        }

        private void OnEnable() =>
            DevCommands.Register("barorder", "<1-5>", "Order from the bar menu while sitting at the bar or a table (tests).", args =>
            {
                foreach (BarSeat seat in FindObjectsByType<BarSeat>(FindObjectsSortMode.None))
                    if (seat.LocalSitsHere)
                    {
                        seat.Order(Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) - 1);
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
            if (here != _seated)
            {
                _seated = here;
                MenuOpen = here;
                SetCard(here); // sat down: the menu comes up; stood up: it goes
            }
            if (!here) return;

            Keyboard keys = Keyboard.current;
            if (_cardOpen)
            {
                if (GameInput.ToggleMenu.WasPressedThisFrame()) SetCard(false);
                else if (keys != null)
                    for (int i = 0; i < _barista.Menu.Length && i < 9; i++)
                        if (keys[Key.Digit1 + i].wasPressedThisFrame || keys[Key.Numpad1 + i].wasPressedThisFrame)
                        {
                            Order(i);
                            break;
                        }
            }
            else if (keys != null && keys.tabKey.wasPressedThisFrame && GameInput.GameplayActive) SetCard(true);
        }

        private void Order(int index)
        {
            if (Time.time < _nextOrder || index < 0 || index >= _barista.Menu.Length) return;
            _nextOrder = Time.time + 0.6f;
            _barista.OrderFromSeat(PlayerHub.Local, this, index);
            // Ordered: the card goes away so the hands are free to eat (Tab brings it back).
            SetCard(false);
            // (It's put down in front of you: look at it and E takes a bite, Fun.TableMeal.)
            PlayerHud.ShowToast($"<b>[Tab]</b> menu   look at your food, <b>[{GameInput.KeyLabel(GameInput.Interact)}]</b> to eat   <b>[{GameInput.KeyLabel(GameInput.Interact)}]</b> elsewhere: stand up", 4f);
        }

        private void SetCard(bool open)
        {
            if (_cardOpen == open) return;
            _cardOpen = open;
            if (open && !_pushedUI) { GameInput.PushUI(); _pushedUI = true; }
            if (!open && _pushedUI) { GameInput.PopUI(); _pushedUI = false; }
        }

        private void OnDisable()
        {
            DevCommands.Unregister("barorder", this);
            SetCard(false);
            if (_seated) MenuOpen = _seated = false;
        }

        // ------------------------------------------------------------------ the menu card

        private const float CardWidth = 760f, Row = 92f;

        private void OnGUI()
        {
            if (!_cardOpen || !_seated) return;
            Barista.MenuItem[] menu = _barista.Menu;
            if (_prices == null || _prices.Length != menu.Length)
            {
                _prices = new string[menu.Length];
                for (int i = 0; i < menu.Length; i++) _prices[i] = "$" + menu[i].Price;
            }
            if (_moneyShown != Economy.Money || _moneyText == null)
            {
                _moneyShown = Economy.Money;
                _moneyText = "Wallet $" + _moneyShown;
            }

            Hud.Dim(0.35f);
            float height = 120f + menu.Length * Row + 100f;
            float fit = Mathf.Min(1f, (Hud.Height - 40f) / height);
            Matrix4x4 before = GUI.matrix;
            if (fit < 1f) GUIUtility.ScaleAroundPivot(new Vector2(fit, fit), new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            var panel = new Rect((Hud.Width - CardWidth) * 0.5f, (Hud.Height - height) * 0.5f, CardWidth, height);
            Hud.Panel(panel, 0.94f);
            Hud.Label(new Rect(panel.x + 36f, panel.y + 22f, CardWidth - 72f, 60f), "MENU", 44f, Hud.Sand, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            Hud.Label(new Rect(panel.x + 36f, panel.y + 22f, CardWidth - 72f, 60f), _moneyText, 26f, new Color(0.56f, 1f, 0.56f), TextAnchor.MiddleRight, heavy: true, shadow: false);
            Hud.Fill(new Rect(panel.x + 36f, panel.y + 92f, CardWidth - 72f, 2f), new Color(1f, 1f, 1f, 0.15f));

            float y = panel.y + 108f;
            for (int i = 0; i < menu.Length; i++)
            {
                Barista.MenuItem dish = menu[i];
                bool afford = Economy.Money >= dish.Price;
                var row = new Rect(panel.x + 24f, y, CardWidth - 48f, Row - 10f);
                GUI.enabled = afford;
                // The whole row is the button: click anywhere on it (or press its number).
                bool pressed = Hud.Button(row, "", selected: false);
                GUI.enabled = true;
                if (Event.current.type == EventType.Repaint)
                {
                    Color text = afford ? Color.white : new Color(1f, 1f, 1f, 0.4f);
                    Hud.Fill(new Rect(row.x + 18f, row.y + 18f, 46f, 46f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, afford ? 0.9f : 0.3f), 10f);
                    Hud.Label(new Rect(row.x + 18f, row.y + 18f, 46f, 46f), (i + 1).ToString(), 26f, Hud.Ink, TextAnchor.MiddleCenter, heavy: true, shadow: false);
                    Hud.Label(new Rect(row.x + 84f, row.y + 6f, row.width - 230f, 40f), dish.Label, 30f, text, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                    Hud.Label(new Rect(row.x + 84f, row.y + 44f, row.width - 230f, 30f), Blurb(dish.Item), 18f, new Color(text.r, text.g, text.b, text.a * 0.7f),
                        TextAnchor.MiddleLeft, shadow: false);
                    Hud.Label(new Rect(row.xMax - 140f, row.y, 116f, row.height), _prices[i], 32f, afford ? Hud.Teal : text, TextAnchor.MiddleRight, heavy: true, shadow: false);
                }
                if (pressed && afford) Order(i);
                y += Row;
            }

            bool close = Hud.Button(new Rect(panel.x + 36f, panel.yMax - 82f, 260f, 56f), "CLOSE", centred: true);
            bool stand = Hud.Button(new Rect(panel.xMax - 296f, panel.yMax - 82f, 260f, 56f), "STAND UP", centred: true);
            GUI.matrix = before;
            if (close) SetCard(false);
            if (stand)
            {
                SetCard(false);
                _vehicle.OnInteract(PlayerHub.Local); // (aboard: gets off)
            }
        }

        /// <summary>A line under each dish on the card.</summary>
        private static string Blurb(string item) => item switch
        {
            "Khinkali" => "Georgian dumplings, juicy inside. Hold by the knot!",
            "Fries" => "Hot, salty, straight from the fryer.",
            "Cola" => "Ice cold can. Burp guaranteed.",
            "Beer" => "Cold bottle from the cooler.",
            "Cocktail" => "Tropical, with a little umbrella.",
            _ => ""
        };
    }
}
