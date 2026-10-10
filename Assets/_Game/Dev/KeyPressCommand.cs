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

        private void OnDisable() => DevCommands.Unregister("presskey", this);

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
