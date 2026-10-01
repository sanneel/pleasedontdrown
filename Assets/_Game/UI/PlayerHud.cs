using System.Collections.Generic;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// The in-game HUD, laid out like How to Fish's: the inventory slots big in the bottom centre (the one in your
    /// hands lifted and framed), what you hold named above them, vitals as squares in the bottom left, the
    /// crosshair with its prompts, and short toast messages. Drawn with <see cref="Hud"/> (IMGUI).
    /// </summary>
    public class PlayerHud : MonoBehaviour
    {
        private struct Toast
        {
            public string Text;
            public float From, Until;
        }

        private const float HintSeconds = 8f;   // the key hints fade once you've had time to read them

        private static readonly List<Toast> _toasts = new();
        private readonly float[] _lift = new float[PlayerHands.SlotCount];
        private readonly float[] _filledAt = new float[PlayerHands.SlotCount];
        private readonly Items.Item[] _slotItems = new Items.Item[PlayerHands.SlotCount];
        private Items.Item _held;
        private float _heldSince;
        private float _staminaShown, _airShown;
        private float _lastRepaint;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _toasts.Clear();

        /// <summary>Shows a short message to the local player only.</summary>
        public static void ShowToast(string text, float seconds = 4f)
        {
            _toasts.Add(new Toast { Text = text, From = Time.unscaledTime, Until = Time.unscaledTime + seconds });
            if (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        private void OnGUI()
        {
            PlayerHub local = PlayerHub.Local;
            if (local == null || !GameInput.GameplayActive)
                return;

            if (Combat.Weapon.LocalScoped) return; // the scope fills the screen
            float dt = 0f;
            if (Event.current.type == EventType.Repaint)
            {
                dt = Mathf.Min(0.1f, Time.unscaledTime - _lastRepaint);
                _lastRepaint = Time.unscaledTime;
            }
            float cx = Hud.Width * 0.5f;
            float cy = Hud.Height * 0.5f;

            string primary = local.Interactor.CurrentPrompt;
            string secondary = local.Interactor.CurrentSecondaryPrompt;
            bool hovering = !string.IsNullOrEmpty(primary) || !string.IsNullOrEmpty(secondary);
            bool aimingGun = Combat.Weapon.Local != null && Combat.Weapon.Local.Aim > 0.5f; // the sights are the crosshair
            if (!aimingGun)
            {
                float size = hovering ? 11f : 6f;
                Hud.Fill(new Rect(cx - size * 0.5f - 1.5f, cy - size * 0.5f - 1.5f, size + 3f, size + 3f), new Color(0f, 0f, 0f, 0.35f), size * 0.5f + 1.5f);
                Hud.Fill(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), hovering ? new Color(1f, 0.86f, 0.25f) : new Color(1f, 1f, 1f, 0.85f), size * 0.5f);
            }

            if (hovering)
            {
                float y = cy + 34f;
                if (!string.IsNullOrEmpty(primary))
                {
                    Hud.Label(new Rect(cx - 500f, y, 1000f, 36f), $"{Key(GameInput.Interact)} {primary}", 26f, Color.white);
                    y += 36f;
                }
                if (!string.IsNullOrEmpty(secondary))
                    Hud.Label(new Rect(cx - 500f, y, 1000f, 36f), $"{Key(GameInput.Secondary)} {secondary}", 26f, Color.white);
            }

            PlayerHands hands = local.Hands;
            if (hands != null)
            {
                DrawHotbar(hands, cx, dt);
                DrawHeld(hands, cx, cy);
            }

            DrawVitals(local, dt);

            _toasts.RemoveAll(t => t.Until < Time.unscaledTime);
            for (int i = 0; i < _toasts.Count; i++)
            {
                Toast toast = _toasts[i];
                float alpha = Mathf.Clamp01((Time.unscaledTime - toast.From) / 0.15f) * Mathf.Clamp01((toast.Until - Time.unscaledTime) / 0.4f);
                Hud.Label(new Rect(cx - 700f, Hud.Height * 0.15f + i * 40f, 1400f, 40f), toast.Text, 28f, new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, alpha));
            }
        }

        private static string Key(UnityEngine.InputSystem.InputAction action) => $"<color=#ffd24a>[{GameInput.KeyLabel(action)}]</color>";

        /// <summary>
        /// The hotbar, How to Fish style: six big slots in the bottom centre, each with its number and a picture of
        /// what's in it. The slot in your hands rides higher with a white frame; a new item pops into its slot.
        /// </summary>
        private void DrawHotbar(PlayerHands hands, float cx, float dt)
        {
            const float size = Hud.SlotSize, gap = Hud.SlotGap;
            float total = PlayerHands.SlotCount * size + (PlayerHands.SlotCount - 1) * gap;
            float x0 = cx - total * 0.5f, y0 = Hud.Height - Hud.SlotBottom - size;
            for (int i = 0; i < PlayerHands.SlotCount; i++)
            {
                bool active = i == hands.ActiveSlot;
                Items.Item item = hands.SlotItem(i);
                if (item != _slotItems[i])
                {
                    if (item != null) _filledAt[i] = Time.unscaledTime;
                    _slotItems[i] = item;
                }
                _lift[i] = Mathf.MoveTowards(_lift[i], active ? 1f : 0f, dt / 0.2f);
                float lift = active ? Hud.EaseOutBack(_lift[i]) : _lift[i] * _lift[i];

                var r = new Rect(x0 + i * (size + gap), y0 - lift * Hud.SlotLift, size, size);
                Hud.Fill(r, new Color(0.02f, 0.06f, 0.09f, active ? 0.62f : 0.42f), 16f);
                if (active) Hud.Frame(r, new Color(1f, 1f, 1f, 0.95f), 4f, 16f);

                if (item != null)
                {
                    float pop = Hud.EaseOutBack((Time.unscaledTime - _filledAt[i]) / 0.25f);
                    float inset = size * 0.5f - size * 0.41f * pop;
                    var inner = new Rect(r.x + inset, r.y + inset, size - inset * 2f, size - inset * 2f);
                    Texture picture = ItemIcons.Get(item);
                    if (picture != null) Hud.Picture(inner, picture, active ? Color.white : new Color(1f, 1f, 1f, 0.8f));
                    else Hud.Label(new Rect(r.x + 6f, r.y + 24f, size - 12f, size - 30f), item.DisplayName, 15f, Color.white, TextAnchor.MiddleCenter, wrap: true);
                }
                Hud.Label(new Rect(r.x + 10f, r.y + 5f, 30f, 26f), (i + 1).ToString(), 20f, new Color(1f, 1f, 1f, active ? 1f : 0.7f), TextAnchor.UpperLeft, heavy: true);
            }
        }

        /// <summary>The name of what's in your hands above the hotbar, its keys for a few seconds, eat / throw bars.</summary>
        private void DrawHeld(PlayerHands hands, float cx, float cy)
        {
            Items.Item held = hands.HeldItem;
            if (held != _held)
            {
                _held = held;
                _heldSince = Time.unscaledTime;
            }
            if (held == null) return;

            float nameY = Hud.HotbarTop - 50f;
            bool person = held.GetComponent<Rescue.VictimBrain>() != null; // RescueHud says what to do with them
            if (!person)
            {
                Hud.Label(new Rect(cx - 500f, nameY, 1000f, 40f), held.DisplayName, 30f, Color.white, heavy: true);
                float hintAlpha = Mathf.Clamp01(HintSeconds - (Time.unscaledTime - _heldSince));
                if (hintAlpha > 0f)
                    Hud.Label(new Rect(cx - 800f, nameY - 32f, 1600f, 30f), Hints(held), 20f, new Color(1f, 1f, 1f, 0.9f * hintAlpha));
            }

            if (hands.IsEating || hands.EatProgress01 > 0.01f)
            {
                Hud.Bar(new Rect(cx - 110f, cy + 84f, 220f, 12f), hands.EatProgress01, new Color(0.55f, 0.9f, 0.4f));
                Hud.Label(new Rect(cx - 200f, cy + 100f, 400f, 30f), "eating...", 22f, Color.white);
            }
            if (hands.IsCharging)
                Hud.Bar(new Rect(cx - 100f, cy + 84f, 200f, 12f), hands.Charge01,
                    Color.Lerp(new Color(1f, 0.9f, 0.4f), new Color(1f, 0.35f, 0.2f), hands.Charge01));
        }

        private static string Hints(Items.Item held)
        {
            string drop = Key(GameInput.Drop);
            string hints;
            var tool = held.GetComponent<Combat.IHeldTool>();
            if (tool is Combat.Weapon)
                hints = $"{Key(GameInput.Primary)} shoot    {Key(GameInput.Secondary)} aim    {Key(GameInput.Reload)} reload    {Key(GameInput.Inspect)} inspect    hold {drop} throw";
            else if (tool != null)
                hints = $"{Key(GameInput.Primary)} {tool.UseLabel}    hold {drop} throw";
            else
                hints = $"{Key(GameInput.Primary)} or hold {drop} throw";
            hints += $"    tap {drop} drop";
            if (held.GetComponent<Items.Edible>() != null) hints += $"    hold {Key(GameInput.Secondary)} eat";
            if (Items.ItemSkin.LocalHeld != null)
                hints += $"    <color=#ffd24a>[{GameInput.KeyLabel(GameInput.SkinPrev)}/{GameInput.KeyLabel(GameInput.SkinNext)}]</color> skin";
            return hints;
        }

        /// <summary>
        /// Vitals as squares in the bottom left: food always, stamina and air only while they're in use.
        /// </summary>
        private void DrawVitals(PlayerHub local, float dt)
        {
            const float size = 64f, gap = 10f;
            float x = 50f, y = Hud.Height - 25f - size;
            PlayerMotor motor = local.Motor;
            PlayerVitals vitals = local.Vitals;
            if (vitals != null)
            {
                Hud.Gauge(new Rect(x, y, size, size), vitals.Food01, vitals.Hungry ? new Color(1f, 0.45f, 0.25f) : new Color(0.95f, 0.62f, 0.25f),
                    HudIcon.Food, vitals.Hungry);
                x += size + gap;
            }
            if (motor == null) return;

            bool air = motor.Air01 < 0.999f || motor.IsHeadUnderwater;
            _airShown = Mathf.MoveTowards(_airShown, air ? 1f : 0f, dt / 0.25f);
            if (_airShown > 0f)
            {
                Hud.Gauge(new Rect(x, y, size, size), motor.Air01, motor.Air01 < 0.3f ? new Color(1f, 0.35f, 0.3f) : new Color(0.3f, 0.7f, 1f),
                    HudIcon.Air, motor.Air01 < 0.3f, _airShown);
                x += (size + gap) * _airShown;
            }
            _staminaShown = Mathf.MoveTowards(_staminaShown, motor.Stamina01 < 0.999f ? 1f : 0f, dt / 0.25f);
            if (_staminaShown > 0f)
                Hud.Gauge(new Rect(x, y, size, size), motor.Stamina01, new Color(1f, 0.8f, 0.2f), HudIcon.Stamina, motor.Stamina01 < 0.15f, _staminaShown);

            if (motor.IsOutOfBreath)
                Hud.Label(new Rect(0f, Hud.Height * 0.3f, Hud.Width, 46f), "<b>OUT OF AIR!</b> Get to the surface!", 34f, new Color(1f, 0.5f, 0.4f), heavy: true);
        }
    }
}
