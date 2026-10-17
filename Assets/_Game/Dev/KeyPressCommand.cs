using System;
using System.Collections;
using PleaseDontDrown.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// 'presskey &lt;key&gt;': taps a keyboard key through the Input System, exactly as a real press arrives, so automated
    /// runs (-pdd-exec "wait 5; presskey escape; screenshot 1") can test keys like Esc that open menus.
    /// </summary>
    public class KeyPressCommand : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("KeyPressCommand") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<KeyPressCommand>();
        }

        private void OnEnable() =>
            DevCommands.Register("presskey", "<key>", "Tap a keyboard key (Input System key name: escape, tab, e...).", args =>
            {
                if (args.Length == 0 || !Enum.TryParse(args[0], true, out Key key)) throw new ArgumentException("which key?");
                StartCoroutine(Tap(key));
            }, owner: this);

        private void OnDisable()
        {
            DevCommands.Unregister("presskey", this);
            DevCommands.Unregister("click", this);
        }

        private void Start() =>
            DevCommands.Register("click", "[left|right] [seconds]", "Press a mouse button through the Input System, as a real click arrives.", args =>
            {
                bool right = args.Length > 0 && args[0].Equals("right", StringComparison.OrdinalIgnoreCase);
                float hold = 0.05f;
                foreach (string a in args)
                    if (float.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h)) hold = h;
                StartCoroutine(Click(right, hold));
            }, owner: this);

        private static IEnumerator Click(bool right, float hold)
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Mouse mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
            if (!mouse.enabled) InputSystem.EnableDevice(mouse);
            InputAction primary = right ? GameInput.Secondary : GameInput.Primary;
            void Performed(InputAction.CallbackContext c) => Debug.Log($"[Keys] {c.action.name} performed");
            primary.performed += Performed;
            void Changed(object o, InputActionChange change)
            {
                if (change is InputActionChange.ActionStarted or InputActionChange.ActionPerformed or InputActionChange.ActionCanceled) return;
                Debug.Log($"[Keys] change {change} on {(o as InputAction)?.name ?? (o as InputActionMap)?.name ?? (o as InputActionAsset)?.name ?? o?.ToString()} at frame {Time.frameCount}: {System.Environment.StackTrace}");
            }
            InputSystem.onActionChange += Changed;
            Debug.Log($"[Keys] click {(right ? "right" : "left")}: mouse enabled {mouse.enabled}, action enabled {primary.enabled}, " +
                      $"gameplay {GameInput.GameplayActive}, ignore devices {GameInput.IgnoreDevices}, cursor {Cursor.lockState}");
            var state = new MouseState().WithButton(right ? MouseButton.Right : MouseButton.Left, true);
            float until = Time.unscaledTime + hold;
            do
            {
                InputSystem.QueueStateEvent(mouse, state);
                yield return null;
                Debug.Log($"[Keys] frame {Time.frameCount}: pressed {(right ? mouse.rightButton : mouse.leftButton).isPressed}, action {primary.phase}, enabled {primary.enabled}, wasPressed {primary.WasPressedThisFrame()}");
            } while (Time.unscaledTime < until);
            Debug.Log($"[Keys] button down: isPressed {(right ? mouse.rightButton : mouse.leftButton).isPressed}");
            InputSystem.QueueStateEvent(mouse, new MouseState());
            yield return null;
            primary.performed -= Performed;
            InputSystem.onActionChange -= Changed;
        }

        private static IEnumerator Tap(Key key)
        {
            // Test windows are often not focused: keep the keyboard live so the tap isn't dropped.
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            Keyboard keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();
            if (!keyboard.enabled) InputSystem.EnableDevice(keyboard);
            void Performed(InputAction.CallbackContext c) => Debug.Log($"[Keys] {c.action.name} performed");
            GameInput.ToggleMenu.performed += Performed;
            Debug.Log($"[Keys] tap {key}: keyboard enabled {keyboard.enabled}, ToggleMenu enabled {GameInput.ToggleMenu.enabled}, " +
                      $"gameplay {GameInput.GameplayActive}, console {UI.DevConsole.IsOpen}, customizer {UI.AvatarCustomizer.IsOpen}, " +
                      $"overlay {SteamBootstrap.IsOverlayOpen}, rebinding {GameInput.IsRebinding}/{GameInput.RebindJustEnded}, ignore devices {GameInput.IgnoreDevices}");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
            yield return null;
            Debug.Log($"[Keys] {key} down: isPressed {keyboard[key].isPressed}");
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            GameInput.ToggleMenu.performed -= Performed;
        }
    }
}
