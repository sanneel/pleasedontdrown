using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// All game input, defined in code (keyboard/mouse + gamepad). Gameplay actions switch off while any
    /// UI "blocker" (console, pause menu, chat...) is open, and the cursor follows automatically.
    /// </summary>
    public static class GameInput
    {
        public static InputAction Move { get; private set; }
        public static InputAction LookMouse { get; private set; }
        public static InputAction LookStick { get; private set; }
        public static InputAction Jump { get; private set; }
        public static InputAction Sprint { get; private set; }
        public static InputAction Crouch { get; private set; }
        public static InputAction Interact { get; private set; }
        public static InputAction Primary { get; private set; }
        public static InputAction Secondary { get; private set; }
        public static InputAction Drop { get; private set; }
        public static InputAction Emote { get; private set; }
        /// <summary>Guns: reload / look the gun over.</summary>
        public static InputAction Reload { get; private set; }
        public static InputAction Inspect { get; private set; }
        /// <summary>Z / C: the held item's previous / next skin (How to Fish's weapon skins).</summary>
        public static InputAction SkinPrev { get; private set; }
        public static InputAction SkinNext { get; private set; }
        /// <summary>Inventory slots 1-4.</summary>
        public static InputAction[] Slots { get; private set; }
        /// <summary>Mouse wheel (y) cycles slots.</summary>
        public static InputAction SlotScroll { get; private set; }

        /// <summary>Always active, even while UI is open.</summary>
        public static InputAction ToggleConsole { get; private set; }
        public static InputAction ToggleMenu { get; private set; }
        public static InputAction ToggleOverlay { get; private set; }

        public static bool GameplayActive => _uiBlockers == 0;

        private static InputActionMap _gameplay;
        private static InputActionMap _global;
        private static int _uiBlockers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            _gameplay?.Dispose();
            _global?.Dispose();
            _uiBlockers = 0;

            _gameplay = new InputActionMap("Gameplay");
            Move = _gameplay.AddAction("Move", InputActionType.Value);
            Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Move.AddBinding("<Gamepad>/leftStick");

            LookMouse = _gameplay.AddAction("LookMouse", InputActionType.Value, "<Mouse>/delta");
            LookStick = _gameplay.AddAction("LookStick", InputActionType.Value, "<Gamepad>/rightStick");

            Jump = _gameplay.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");
            Jump.AddBinding("<Gamepad>/buttonSouth");
            Sprint = _gameplay.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            Sprint.AddBinding("<Gamepad>/leftStickPress");
            Crouch = _gameplay.AddAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            Crouch.AddBinding("<Gamepad>/buttonEast");
            Interact = _gameplay.AddAction("Interact", InputActionType.Button, "<Keyboard>/e");
            Interact.AddBinding("<Gamepad>/buttonWest");
            Primary = _gameplay.AddAction("Primary", InputActionType.Button, "<Mouse>/leftButton");
            Primary.AddBinding("<Gamepad>/rightTrigger");
            Secondary = _gameplay.AddAction("Secondary", InputActionType.Button, "<Mouse>/rightButton");
            Secondary.AddBinding("<Gamepad>/leftTrigger");
            Drop = _gameplay.AddAction("Drop", InputActionType.Button, "<Keyboard>/g");
            Drop.AddBinding("<Gamepad>/buttonNorth");
            Emote = _gameplay.AddAction("Emote", InputActionType.Button, "<Keyboard>/v");
            Emote.AddBinding("<Gamepad>/dpad/up");
            Reload = _gameplay.AddAction("Reload", InputActionType.Button, "<Keyboard>/r");
            Reload.AddBinding("<Gamepad>/rightShoulder");
            Inspect = _gameplay.AddAction("Inspect", InputActionType.Button, "<Keyboard>/f");
            Inspect.AddBinding("<Gamepad>/dpad/down");
            SkinPrev = _gameplay.AddAction("SkinPrev", InputActionType.Button, "<Keyboard>/z");
            SkinNext = _gameplay.AddAction("SkinNext", InputActionType.Button, "<Keyboard>/c");
            Slots = new InputAction[6];
            for (int i = 0; i < Slots.Length; i++)
                Slots[i] = _gameplay.AddAction($"Slot{i + 1}", InputActionType.Button, $"<Keyboard>/{i + 1}");
            SlotScroll = _gameplay.AddAction("SlotScroll", InputActionType.Value, "<Mouse>/scroll/y");
            SlotScroll.AddCompositeBinding("1DAxis").With("Negative", "<Gamepad>/dpad/left").With("Positive", "<Gamepad>/dpad/right");

            _global = new InputActionMap("Global");
            ToggleConsole = _global.AddAction("ToggleConsole", InputActionType.Button, "<Keyboard>/backquote");
            ToggleConsole.AddBinding("<Keyboard>/f2");
            ToggleConsole.AddBinding("<Keyboard>/f1"); // laptops where F2 needs Fn
            ToggleMenu = _global.AddAction("ToggleMenu", InputActionType.Button, "<Keyboard>/escape");
            ToggleMenu.AddBinding("<Gamepad>/start");
            ToggleOverlay = _global.AddAction("ToggleOverlay", InputActionType.Button, "<Keyboard>/tab");

            Rebindable = new[]
            {
                new Rebind("FORWARD", Move, 1), new Rebind("BACK", Move, 2), new Rebind("LEFT", Move, 3), new Rebind("RIGHT", Move, 4),
                new Rebind("JUMP", Jump, 0), new Rebind("SPRINT", Sprint, 0), new Rebind("CROUCH", Crouch, 0), new Rebind("USE / RESCUE", Interact, 0),
                new Rebind("PUNCH / FIRE", Primary, 0), new Rebind("CPR / AIM", Secondary, 0), new Rebind("DROP / THROW", Drop, 0), new Rebind("WAVE", Emote, 0),
                new Rebind("RELOAD", Reload, 0), new Rebind("INSPECT / KISS OF LIFE", Inspect, 0)
            };
            _rebinding = null;
            IsRebinding = false;
            _rebindEndFrame = -1;
            string saved = PlayerPrefs.GetString(BindingsKey, string.Empty);
            if (!string.IsNullOrEmpty(saved)) _gameplay.LoadBindingOverridesFromJson(saved);
            BindingsVersion++;

            _global.Enable();
            _gameplay.Enable();
        }

        // ------------------------------------------------------------------ the player's own keys

        /// <summary>One key the player can change in OPTIONS: what it's called, and which binding of which action it is.</summary>
        public readonly struct Rebind
        {
            public readonly string Label;
            public readonly InputAction Action;
            public readonly int Binding;

            public Rebind(string label, InputAction action, int binding)
            {
                Label = label;
                Action = action;
                Binding = binding;
            }
        }

        private const string BindingsKey = "pdd.input.bindings";
        private static InputActionRebindingExtensions.RebindingOperation _rebinding;
        private static int _rebindEndFrame = -1;

        /// <summary>The keyboard and mouse keys that can be changed (gamepad buttons and the 1-6 slots stay as they are).</summary>
        public static Rebind[] Rebindable { get; private set; }
        /// <summary>Waiting for the player to press the new key.</summary>
        public static bool IsRebinding { get; private set; }
        /// <summary>The key press that ended (or cancelled) a rebind belongs to it: menus ignore Esc on that frame.</summary>
        public static bool RebindJustEnded => Time.frameCount <= _rebindEndFrame + 1;
        /// <summary>Goes up whenever a key changes (cached prompt texts compare it).</summary>
        public static int BindingsVersion { get; private set; }

        /// <summary>Wait for the next key or mouse button and make it <paramref name="index"/>'s key (Esc cancels).</summary>
        public static void StartRebind(int index)
        {
            if (IsRebinding || index < 0 || index >= Rebindable.Length) return;
            Rebind target = Rebindable[index];
            string before = target.Action.bindings[target.Binding].effectivePath;
            target.Action.Disable(); // an action can't be rebound while it's listening
            IsRebinding = true;
            _rebinding = target.Action.PerformInteractiveRebinding(target.Binding)
                .WithControlsHavingToMatchPath("<Keyboard>")
                .WithControlsHavingToMatchPath("<Mouse>")
                .WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").WithControlsExcluding("<Mouse>/scroll")
                .WithControlsExcluding("<Keyboard>/anyKey")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.05f)
                .OnComplete(_ => EndRebind(index, before, true))
                .OnCancel(_ => EndRebind(index, before, false));
            _rebinding.Start();
        }

        private static void EndRebind(int index, string before, bool changed)
        {
            _rebinding?.Dispose();
            _rebinding = null;
            IsRebinding = false;
            _rebindEndFrame = Time.frameCount;
            if (changed)
            {
                GiveBack(index, before);
                SaveBindings();
            }
            Apply();
        }

        // After every SubsystemRegistration reset (the console clears its commands in one of those).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterCommands()
        {
            DevCommands.Register("bind", "<n> <control>", "Give key n (0 = forward, 4 = jump...) a control, e.g. bind 4 <Keyboard>/j (tests; not saved).", args =>
            {
                if (args.Length < 2 || !int.TryParse(args[0], out int n)) throw new System.ArgumentException("bind <n> <control>");
                SetBinding(n, args[1]);
                DevCommands.Print($"{Rebindable[n].Label} is now {KeyLabel(Rebindable[n].Action, Rebindable[n].Binding)}");
            });
        }

        /// <summary>Two actions on one key would both fire: whoever had <paramref name="index"/>'s new key gets its old one.</summary>
        private static void GiveBack(int index, string before)
        {
            Rebind target = Rebindable[index];
            string now = target.Action.bindings[target.Binding].effectivePath;
            for (int i = 0; i < Rebindable.Length; i++)
            {
                Rebind other = Rebindable[i];
                if (i != index && other.Action.bindings[other.Binding].effectivePath == now)
                    other.Action.ApplyBindingOverride(other.Binding, before);
            }
        }

        /// <summary>
        /// Give key <paramref name="index"/> a control directly, e.g. "&lt;Keyboard&gt;/j" (the console's <c>bind</c>, for
        /// tests: the options screen listens for a key press instead). Not saved: a test never touches the player's own keys.
        /// </summary>
        public static void SetBinding(int index, string path)
        {
            if (index < 0 || index >= Rebindable.Length) return;
            Rebind target = Rebindable[index];
            string before = target.Action.bindings[target.Binding].effectivePath;
            target.Action.ApplyBindingOverride(target.Binding, path);
            GiveBack(index, before);
            BindingsVersion++;
        }

        /// <summary>Back to the keys the game ships with.</summary>
        public static void ResetBindings()
        {
            _gameplay.RemoveAllBindingOverrides();
            SaveBindings();
        }

        private static void SaveBindings()
        {
            PlayerPrefs.SetString(BindingsKey, _gameplay.SaveBindingOverridesAsJson());
            PlayerPrefs.Save();
            BindingsVersion++;
        }

        /// <summary>Call when a UI that needs the mouse opens. Must be paired with <see cref="PopUI"/>.</summary>
        public static void PushUI()
        {
            _uiBlockers++;
            Apply();
        }

        public static void PopUI()
        {
            _uiBlockers = Mathf.Max(0, _uiBlockers - 1);
            Apply();
        }

        /// <summary>Locks the cursor for first-person play when nothing blocks it.</summary>
        public static void Apply()
        {
            bool play = _uiBlockers == 0;
            if (play && !IgnoreDevices) _gameplay.Enable();
            else _gameplay.Disable();
            if (IgnoreDevices) _global.Disable();
            Cursor.lockState = play && LocalPlayerExists ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !play || !LocalPlayerExists;
        }

        /// <summary>
        /// Automated tests (-pdd-noinput): ignore the real mouse, keyboard and gamepad, so someone using the PC
        /// doesn't steer a test window. Console commands still drive everything.
        /// </summary>
        public static bool IgnoreDevices { get; set; }

        /// <summary>Set by the player system; without a local player the cursor stays free (menus).</summary>
        public static bool LocalPlayerExists { get; set; }

        /// <summary>
        /// Short key label for prompts, e.g. "E". Built from the binding path (the physical key), not the active
        /// keyboard layout: on non-English layouts the display string would otherwise show local letters (e.g. "ð" for G).
        /// </summary>
        public static string KeyLabel(InputAction action, int binding = 0)
        {
            if (action == null || binding >= action.bindings.Count) return "?";
            string path = action.bindings[binding].effectivePath;
            int slash = path.LastIndexOf('/');
            string key = slash >= 0 ? path.Substring(slash + 1) : path;
            switch (key)
            {
                case "leftButton": return "LMB";
                case "rightButton": return "RMB";
                case "middleButton": return "MMB";
                case "space": return "Space";
                case "leftShift": case "rightShift": return "Shift";
                case "leftCtrl": case "rightCtrl": return "Ctrl";
                case "leftAlt": case "rightAlt": return "Alt";
                case "escape": return "Esc";
                case "backquote": return "`";
                case "enter": return "Enter";
                case "tab": return "Tab";
                case "forwardButton": return "Mouse 5";
                case "backButton": return "Mouse 4";
                case "upArrow": return "Up";
                case "downArrow": return "Down";
                case "leftArrow": return "Left";
                case "rightArrow": return "Right";
            }
            return key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1);
        }
    }
}
