using System.Collections.Generic;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Minimal in-game HUD: crosshair, interaction prompt and short toast messages.
    /// IMGUI for the prototype phase; the look is replaced when real UI lands.
    /// </summary>
    public class PlayerHud : MonoBehaviour
    {
        private struct Toast
        {
            public string Text;
            public float Until;
        }

        private static readonly List<Toast> _toasts = new();
        private GUIStyle _prompt;
        private GUIStyle _toast;
        private Texture2D _dot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _toasts.Clear();

        /// <summary>Shows a short message to the local player only.</summary>
        public static void ShowToast(string text, float seconds = 4f)
        {
            _toasts.Add(new Toast { Text = text, Until = Time.unscaledTime + seconds });
            if (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        private void OnGUI()
        {
            PlayerHub local = PlayerHub.Local;
            if (local == null || !GameInput.GameplayActive)
                return;

            EnsureStyles();
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            bool hovering = !string.IsNullOrEmpty(local.Interactor.CurrentPrompt);
            float size = hovering ? 8f : 4f;
            GUI.color = hovering ? new Color(1f, 0.86f, 0.25f) : new Color(1f, 1f, 1f, 0.8f);
            GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), _dot);
            GUI.color = Color.white;

            if (hovering)
            {
                string key = GameInput.KeyLabel(GameInput.Interact);
                DrawShadowed(new Rect(cx - 300f, cy + 28f, 600f, 30f), $"<b>[{key}]</b> {local.Interactor.CurrentPrompt}", _prompt);
            }

            _toasts.RemoveAll(t => t.Until < Time.unscaledTime);
            for (int i = 0; i < _toasts.Count; i++)
                DrawShadowed(new Rect(cx - 400f, Screen.height * 0.16f + i * 30f, 800f, 30f), _toasts[i].Text, _toast);
        }

        private static void DrawShadowed(Rect rect, string text, GUIStyle style)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            style.normal.textColor = c;
            GUI.Label(rect, text, style);
        }

        private void EnsureStyles()
        {
            if (_dot == null)
            {
                _dot = new Texture2D(1, 1);
                _dot.SetPixel(0, 0, Color.white);
                _dot.Apply();
            }
            _prompt ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, richText = true, normal = { textColor = Color.white } };
            _toast ??= new GUIStyle(_prompt) { fontSize = 20, normal = { textColor = new Color(1f, 0.95f, 0.8f) } };
        }
    }
}
