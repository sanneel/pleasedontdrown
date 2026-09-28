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
        private GUIStyle _slot;
        private GUIStyle _slotKey;
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

            if (Combat.Weapon.LocalScoped) return; // the scope fills the screen
            EnsureStyles();
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            string secondary = local.Interactor.CurrentSecondaryPrompt;
            bool hovering = !string.IsNullOrEmpty(local.Interactor.CurrentPrompt) || !string.IsNullOrEmpty(secondary);
            float size = hovering ? 8f : 4f;
            GUI.color = hovering ? new Color(1f, 0.86f, 0.25f) : new Color(1f, 1f, 1f, 0.8f);
            bool aimingGun = Combat.Weapon.Local != null && Combat.Weapon.Local.Aim > 0.5f; // the sights are the crosshair
            if (!aimingGun) GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), _dot);
            GUI.color = Color.white;

            if (hovering)
            {
                float y = cy + 28f;
                if (!string.IsNullOrEmpty(local.Interactor.CurrentPrompt))
                {
                    string key = GameInput.KeyLabel(GameInput.Interact);
                    DrawShadowed(new Rect(cx - 300f, y, 600f, 30f), $"<b>[{key}]</b> {local.Interactor.CurrentPrompt}", _prompt);
                    y += 26f;
                }
                if (!string.IsNullOrEmpty(secondary))
                    DrawShadowed(new Rect(cx - 300f, y, 600f, 30f), $"<b>[{GameInput.KeyLabel(GameInput.Secondary)}]</b> {secondary}", _prompt);
            }

            PlayerHands hands = local.Hands;
            if (hands != null) DrawHotbar(hands, cx);
            if (hands != null && hands.HeldItem != null)
            {
                string throwKey = GameInput.KeyLabel(GameInput.Primary);
                string dropKey = GameInput.KeyLabel(GameInput.Drop);
                bool food = hands.HeldItem.GetComponent<Items.Edible>() != null;
                string eat = food ? $"    hold <b>[{GameInput.KeyLabel(GameInput.Secondary)}]</b> to eat" : "";
                var tool = hands.HeldItem.GetComponent<Combat.IHeldTool>();
                string use = tool != null ? $"<b>[{throwKey}]</b> {tool.UseLabel}    hold <b>[{dropKey}]</b> to throw" : $"<b>[{throwKey}]</b> or hold <b>[{dropKey}]</b> to throw";
                if (tool is Combat.Weapon)
                    use = $"<b>[{throwKey}]</b> shoot    <b>[{GameInput.KeyLabel(GameInput.Secondary)}]</b> aim    <b>[{GameInput.KeyLabel(GameInput.Reload)}]</b> reload    " +
                          $"<b>[{GameInput.KeyLabel(GameInput.Inspect)}]</b> inspect    hold <b>[{dropKey}]</b> to throw";
                DrawShadowed(new Rect(cx - 500f, Screen.height - 112f, 1000f, 30f),
                    $"Holding <b>{hands.HeldItem.DisplayName}</b>    {use}    tap <b>[{dropKey}]</b> to drop{eat}", _prompt);

                if (hands.IsEating || hands.EatProgress01 > 0.01f)
                {
                    const float width = 160f;
                    var back = new Rect(cx - width * 0.5f, cy + 60f, width, 8f);
                    DrawBar(back, hands.EatProgress01, new Color(0.55f, 0.9f, 0.4f));
                    DrawShadowed(new Rect(cx - 150f, cy + 70f, 300f, 26f), "eating...", _prompt);
                }

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

        /// <summary>Four inventory slots along the bottom edge; the selected one is highlighted.</summary>
        private void DrawHotbar(PlayerHands hands, float cx)
        {
            const float size = 58f, gap = 6f;
            float total = PlayerHands.SlotCount * size + (PlayerHands.SlotCount - 1) * gap;
            float x = cx - total * 0.5f, y = Screen.height - size - 12f;
            for (int i = 0; i < PlayerHands.SlotCount; i++)
            {
                var r = new Rect(x + i * (size + gap), y, size, size);
                bool active = i == hands.ActiveSlot;
                GUI.color = active ? new Color(1f, 0.86f, 0.25f, 0.95f) : new Color(0f, 0f, 0f, 0.45f);
                GUI.DrawTexture(r, _dot);
                GUI.color = new Color(0f, 0f, 0f, active ? 0.55f : 0.25f);
                GUI.DrawTexture(new Rect(r.x + 3f, r.y + 3f, r.width - 6f, r.height - 6f), _dot);
                GUI.color = Color.white;
                Items.Item item = hands.SlotItem(i);
                if (item != null)
                {
                    string label = item.DisplayName.Length > 9 ? item.DisplayName.Substring(0, 8) + "." : item.DisplayName;
                    GUI.Label(new Rect(r.x + 2f, r.y + 14f, r.width - 4f, r.height - 16f), label, _slot);
                }
                GUI.Label(new Rect(r.x + 4f, r.y + 1f, 20f, 16f), (i + 1).ToString(), _slotKey);
            }
        }

        /// <summary>Air, stamina and food bars, shown only while they're not (nearly) full.</summary>
        private void DrawBreath(PlayerMotor motor, float cx)
        {
            const float width = 220f;
            float y = Screen.height - 150f;
            PlayerVitals vitals = PlayerHub.Local != null ? PlayerHub.Local.Vitals : null;
            if (vitals != null && (vitals.Food01 < 0.5f || Time.time - vitals.LastAteTime < 3f))
            {
                DrawBar(new Rect(cx - width * 0.5f, y, width, 8f), vitals.Food01,
                    vitals.Hungry ? new Color(1f, 0.45f, 0.25f) : new Color(0.55f, 0.9f, 0.4f));
                DrawShadowed(new Rect(cx - width * 0.5f - 60f, y - 7f, 55f, 22f), "FOOD", _prompt);
                y -= 18f;
            }
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
            _slot ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, wordWrap = true, normal = { textColor = Color.white } };
            _slotKey ??= new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 1f, 1f, 0.8f) } };
        }
    }
}
