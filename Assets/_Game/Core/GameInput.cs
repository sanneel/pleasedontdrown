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
        /// <summary>Inventory slots 1-4.</summary>
        public static InputAction[] Slots { get; private set; }
        /// <summary>Mouse wheel (y) cycles slots.</summary>
        public static InputAction SlotScroll { get; private set; }

        /// <summary>Always active, even while UI is open.</summary>
        public static InputAction ToggleConsole { get; private set; }
        public static InputAction ToggleMenu { get; private set; }

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
            Crouch.AddBinding("<Keyboard>/c");
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
            Slots = new InputAction[4];
            for (int i = 0; i < Slots.Length; i++)
                Slots[i] = _gameplay.AddAction($"Slot{i + 1}", InputActionType.Button, $"<Keyboard>/{i + 1}");
            SlotScroll = _gameplay.AddAction("SlotScroll", InputActionType.Value, "<Mouse>/scroll/y");
            SlotScroll.AddCompositeBinding("1DAxis").With("Negative", "<Gamepad>/dpad/left").With("Positive", "<Gamepad>/dpad/right");

            _global = new InputActionMap("Global");
            ToggleConsole = _global.AddAction("ToggleConsole", InputActionType.Button, "<Keyboard>/backquote");
            ToggleConsole.AddBinding("<Keyboard>/f2");
            ToggleMenu = _global.AddAction("ToggleMenu", InputActionType.Button, "<Keyboard>/escape");
            ToggleMenu.AddBinding("<Gamepad>/start");

            _global.Enable();
            _gameplay.Enable();
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
            if (play) _gameplay.Enable();
            else _gameplay.Disable();
            Cursor.lockState = play && LocalPlayerExists ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !play || !LocalPlayerExists;
        }

        /// <summary>Set by the player system; without a local player the cursor stays free (menus).</summary>
        public static bool LocalPlayerExists { get; set; }

        /// <summary>
        /// Short key label for prompts, e.g. "E". Built from the binding path (the physical key), not the active
        /// keyboard layout: on non-English layouts the display string would otherwise show local letters (e.g. "ð" for G).
        /// </summary>
        public static string KeyLabel(InputAction action)
        {
            if (action == null || action.bindings.Count == 0) return "?";
            string path = action.bindings[0].effectivePath;
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
            }
            return key.Length == 1 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1);
        }
    }
}
