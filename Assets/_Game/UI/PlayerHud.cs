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

            PlayerHands hands = local.Hands;
            if (hands != null && hands.HeldItem != null)
            {
                string throwKey = GameInput.KeyLabel(GameInput.Primary);
                string dropKey = GameInput.KeyLabel(GameInput.Drop);
                DrawShadowed(new Rect(cx - 450f, Screen.height - 70f, 900f, 30f),
                    $"Holding <b>{hands.HeldItem.DisplayName}</b>    <b>[{throwKey}]</b> or hold <b>[{dropKey}]</b> to throw    tap <b>[{dropKey}]</b> to drop", _prompt);

                if (hands.IsCharging)
                {
                    const float width = 140f;
                    var back = new Rect(cx - width * 0.5f, cy + 60f, width, 8f);
                    GUI.color = new Color(0f, 0f, 0f, 0.5f);
                    GUI.DrawTexture(back, _dot);
                    GUI.color = Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(1f, 0.35f, 0.2f), hands.Charge01);
                    GUI.DrawTexture(new Rect(back.x, back.y, width * hands.Charge01, back.height), _dot);
                    GUI.color = Color.white;
                }
            }

            DrawBreath(local.Motor, cx);

            _toasts.RemoveAll(t => t.Until < Time.unscaledTime);
            for (int i = 0; i < _toasts.Count; i++)
                DrawShadowed(new Rect(cx - 400f, Screen.height * 0.16f + i * 30f, 800f, 30f), _toasts[i].Text, _toast);
        }

        /// <summary>Air and stamina bars, shown only while they're not full.</summary>
        private void DrawBreath(PlayerMotor motor, float cx)
        {
            const float width = 220f;
            float y = Screen.height - 110f;
            if (motor.Air01 < 0.999f || motor.IsHeadUnderwater)
            {
                DrawBar(new Rect(cx - width * 0.5f, y, width, 10f), motor.Air01,
                    motor.Air01 < 0.3f ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.8f, 1f));
                DrawShadowed(new Rect(cx - width * 0.5f - 60f, y - 6f, 55f, 22f), "AIR", _prompt);
                y -= 18f;
            }
            if (motor.Stamina01 < 0.999f)
            {
                DrawBar(new Rect(cx - width * 0.5f, y, width, 8f), motor.Stamina01, new Color(1f, 0.85f, 0.35f));
                DrawShadowed(new Rect(cx - width * 0.5f - 60f, y - 7f, 55f, 22f), "STAM", _prompt);
            }
            if (motor.IsOutOfBreath)
                DrawShadowed(new Rect(cx - 300f, Screen.height * 0.3f, 600f, 34f), "<b>OUT OF AIR!</b> Get to the surface!", _toast);
        }

        private void DrawBar(Rect rect, float value, Color fill)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(rect, _dot);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), _dot);
            GUI.color = Color.white;
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
