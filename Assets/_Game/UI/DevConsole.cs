using System.Collections.Generic;
using PleaseDontDrown.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Drop-down developer console (` , F1 or F2). Shows recent game logs and runs <see cref="DevCommands"/>.
    /// Works on a Georgian keyboard layout too: letters are typed as the Latin letter on the same key.
    /// IMGUI on purpose: zero setup, always works, replaced by nothing (it's a dev tool).
    /// </summary>
    public class DevConsole : MonoBehaviour
    {
        private const int MaxLines = 200;
        private const string InputControl = "DevConsoleInput";

        private readonly List<string> _lines = new();
        private readonly List<string> _history = new();
        private int _historyIndex;
        private string _input = string.Empty;
        private bool _open;
        private bool _focusInput;
        private Vector2 _scroll;
        private GUIStyle _lineStyle;

        public static bool IsOpen { get; private set; }

        private void OnEnable()
        {
            DevCommands.Output += AddLine;
            Application.logMessageReceived += OnLog;
            GameInput.ToggleConsole.performed += OnToggle;
            DevCommands.Register("clear", "", "Clear the console.", _ => _lines.Clear(), owner: this);
        }

        private void OnDisable()
        {
            DevCommands.Output -= AddLine;
            Application.logMessageReceived -= OnLog;
            GameInput.ToggleConsole.performed -= OnToggle;
            DevCommands.Unregister("clear", this);
            if (_open) GameInput.PopUI();
            _open = IsOpen = false;
        }

        private void OnToggle(InputAction.CallbackContext _) => SetOpen(!_open);

        private void SetOpen(bool open)
        {
            if (open == _open) return;
            _open = IsOpen = open;
            if (open) GameInput.PushUI();
            else GameInput.PopUI();
            _focusInput = open;
            _input = string.Empty;
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            // Only our tagged logs plus problems; engine chatter stays in the log file.
            bool tagged = message.StartsWith("[");
            if (type == LogType.Log && !tagged) return;
            string color = type switch
            {
                LogType.Warning => "#ffd27f",
                LogType.Error or LogType.Exception or LogType.Assert => "#ff8080",
                _ => "#c8c8c8"
            };
            AddLine($"<color={color}>{message}</color>");
        }

        private void AddLine(string line)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines) _lines.RemoveAt(0);
            _scroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (!_open) return;

            _lineStyle ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 13 };
            Event e = Event.current;

            // Keys handled before the text field. Toggling itself is done by the input action (` / F2);
            // here we only stop the ` character from being typed.
            if (e.type == EventType.KeyDown)
            {
                if (e.character == '`' || e.keyCode == KeyCode.BackQuote || e.keyCode == KeyCode.F2)
                {
                    e.Use();
                    return;
                }
                // Georgian (or any non-Latin) keyboard layout: type the Latin letter of the same key, so commands work
                // without switching the layout.
                if (e.character != '\0' && LatinForKey(e.character) is char latin) e.character = latin;
                switch (e.keyCode)
                {
                    case KeyCode.Escape:
                        SetOpen(false); e.Use(); return;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        Submit(); e.Use(); break;
                    case KeyCode.Tab:
                        _input = DevCommands.Complete(_input); MoveCursorToEnd(); e.Use(); break;
                    case KeyCode.UpArrow:
                        Recall(-1); e.Use(); break;
                    case KeyCode.DownArrow:
                        Recall(1); e.Use(); break;
                }
            }

            float h = Screen.height * 0.45f;
            GUI.Box(new Rect(0, 0, Screen.width, h), GUIContent.none);
            GUILayout.BeginArea(new Rect(8, 6, Screen.width - 16, h - 12));
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (string line in _lines)
                GUILayout.Label(line, _lineStyle);
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUILayout.Label(">", GUILayout.Width(12));
            GUI.SetNextControlName(InputControl);
            _input = GUILayout.TextField(_input);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (_focusInput)
            {
                GUI.FocusControl(InputControl);
                _focusInput = false;
            }
        }

        // Georgian keyboard (QWERTY-based) letters and the Latin letter on the same key.
        private const string GeorgianKeys = "ქწჭერღტთყუიოპასშდფგჰჯჟკლზძხცჩვბნმ";
        private const string LatinKeys = "qwWerRtTyuiopasSdfghjJklzZxcCvbnm";

        private static char? LatinForKey(char c)
        {
            int i = GeorgianKeys.IndexOf(c);
            return i >= 0 ? LatinKeys[i] : null;
        }

        private static string ToLatin(string text)
        {
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (LatinForKey(chars[i]) is char latin) chars[i] = latin;
            return new string(chars);
        }

        private void Submit()
        {
            string command = ToLatin(_input.Trim()); // anything pasted in Georgian too
            _input = string.Empty;
            if (command.Length == 0) return;
            AddLine($"<color=#8fd3ff>> {command}</color>");
            _history.Add(command);
            _historyIndex = _history.Count;
            DevCommands.Execute(command);
        }

        private void Recall(int direction)
        {
            if (_history.Count == 0) return;
            _historyIndex = Mathf.Clamp(_historyIndex + direction, 0, _history.Count);
            _input = _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
            MoveCursorToEnd();
        }

        private void MoveCursorToEnd()
        {
            if (GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl) is TextEditor editor)
                editor.MoveTextEnd();
        }
    }
}
