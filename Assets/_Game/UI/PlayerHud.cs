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
        // The coral "just lost" part of each vital ring: it waits a moment, then eases down after the level.
        private float _foodTrail = 1f, _airTrail = 1f, _staminaTrail = 1f;
        private float _foodDropAt, _airDropAt, _staminaDropAt;
        private float _lastRepaint;
        // Lines of text that only change when what they describe does (not built again every frame).
        private static readonly string[] SlotNumbers = BuildSlotNumbers();
        private string _primary, _primaryLine, _secondary, _secondaryLine;
        private bool _heldIsPerson, _hintsWithSkin;
        private readonly List<(string key, string what)> _hints = new();
        private bool _hintsBuilt;
        private int _bindingsVersion = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _toasts.Clear();

        private static string[] BuildSlotNumbers()
        {
            var numbers = new string[PlayerHands.SlotCount];
            for (int i = 0; i < numbers.Length; i++) numbers[i] = (i + 1).ToString();
            return numbers;
        }

        // Drawn on the HUD canvas (no IMGUI): see HudCanvas. (The arrival fade goes over everything, under the
        // GameObject's name: one drawer per owner.)
        private void OnEnable()
        {
            HudCanvas.Add(this, HudCanvas.OrderPlayer, DrawHud);
            HudCanvas.Add(gameObject, HudCanvas.OrderFade, DrawArrival);
        }

        private void OnDisable()
        {
            HudCanvas.Remove(this);
            HudCanvas.Remove(gameObject);
        }

        private PlayerHub _arrived;
        private float _arrivedAt = -10f;

        /// <summary>Into the game from the menu (or back from a load): the view fades up from dark instead of cutting in.</summary>
        private void DrawArrival()
        {
            PlayerHub local = PlayerHub.Local;
            if (local != _arrived)
            {
                _arrived = local;
                _arrivedAt = Time.unscaledTime;
            }
            if (local == null) return;
            float a = 1f - Mathf.SmoothStep(0f, 1f, (Time.unscaledTime - _arrivedAt - 0.15f) / 0.9f);
            if (a > 0.005f) Hud.Fill(new Rect(0f, 0f, Hud.Width, Hud.Height), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, a));
        }

        /// <summary>Shows a short message to the local player only.</summary>
        public static void ShowToast(string text, float seconds = 4f)
        {
            _toasts.Add(new Toast { Text = text, From = Time.unscaledTime, Until = Time.unscaledTime + seconds });
            if (_toasts.Count > 2) _toasts.RemoveAt(0);
        }

        private void DrawHud()
        {
            PlayerHub local = PlayerHub.Local;
            if (local == null || !GameInput.GameplayActive)
                return;

            if (Combat.Weapon.LocalScoped) return; // the scope fills the screen
            float dt = Mathf.Min(0.1f, Time.unscaledTime - _lastRepaint);
            _lastRepaint = Time.unscaledTime;
            float cx = Hud.Width * 0.5f;
            float cy = Hud.Height * 0.5f;

            if (_bindingsVersion != GameInput.BindingsVersion)
            {
                // A key was changed in OPTIONS: the cached lines name the old one.
                _bindingsVersion = GameInput.BindingsVersion;
                _primary = _secondary = null;
                _hintsBuilt = false;
            }
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
                    if (primary != _primary)
                    {
                        _primary = primary;
                        _primaryLine = GameInput.KeyLabel(GameInput.Interact);
                    }
                    y += DrawKeyPrompt(cx, y, _primaryLine, primary);
                }
                if (!string.IsNullOrEmpty(secondary))
                {
                    if (secondary != _secondary)
                    {
                        _secondary = secondary;
                        _secondaryLine = GameInput.KeyLabel(local.Interactor.CurrentSecondaryAction);
                    }
                    DrawKeyPrompt(cx, y, _secondaryLine, secondary);
                }
            }

            PlayerHands hands = local.Hands;
            if (hands != null)
            {
                DrawHotbar(hands, cx, dt);
                DrawHeld(hands, cx, cy);
            }

            DrawVitals(local, dt);
            DrawHurt(local, cx, cy);

            _toasts.RemoveAll(t => t.Until < Time.unscaledTime);
            // Each message on its own dark pill under the top of the screen (the team, 2026-10-12: words floating in the
            // sky read as a log), sliding in and fading out.
            float toastY = DevConnectMenu.GuideOpen ? 414f : Hud.Margin;
            for (int i = _toasts.Count - 1; i >= 0; i--)
            {
                Toast toast = _toasts[i];
                float alpha = Mathf.Clamp01((Time.unscaledTime - toast.From) / 0.15f) * Mathf.Clamp01((toast.Until - Time.unscaledTime) / 0.4f);
                float slide = (1f - Mathf.Clamp01((Time.unscaledTime - toast.From) / 0.2f)) * 12f;
                // (A thin teal bar on its right edge makes it read as a message, not words floating in the sky.)
                const float size = 19f, width = 360f;
                float height = Hud.TextHeight(toast.Text, width, size) + 8f;
                var pill = new Rect(Hud.Width - Hud.Margin - width - 12f + slide, toastY, width, height);
                Hud.Fill(new Rect(pill.xMax + 8f, pill.y + 3f, 4f, height - 8f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, alpha), 2f);
                Hud.Label(new Rect(pill.x, pill.y, width, height), toast.Text, size,
                    new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, alpha), TextAnchor.UpperRight, wrap: true);
                toastY += height + 10f;
            }
        }

        /// <summary>
        /// A prompt under the crosshair: the key as a little keycap (light, dark letter) and what it does beside it,
        /// the pair centred. Returns the height used.
        /// </summary>
        private static float DrawKeyPrompt(float center, float y, string key, string text)
        {
            const float size = 22f, capHeight = 32f, gap = 10f;
            float capWidth = Mathf.Max(capHeight, Hud.TextWidth(key, capHeight * 0.53f, heavy: true) + capHeight * 0.5f);
            float textWidth = Mathf.Min(640f, Hud.TextWidth(text, size));
            float x = center - (capWidth + gap + textWidth) * 0.5f;
            Hud.KeyCap(x, y + 2f, key, capHeight);
            float height = Mathf.Max(capHeight + 6f, Hud.TextHeight(text, textWidth, size) + 10f);
            Hud.Label(new Rect(x + capWidth + gap, y + 4f, textWidth + 4f, height), text, size, Hud.Paper, TextAnchor.UpperLeft, wrap: true, shadow: false);
            return height + 6f;
        }


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
                // Light glass, not dark blocks: an empty slot is little more than its thin frame; the one in your hands
                // is lifted, framed in teal and a little darker so its picture stands out.
                bool empty = item == null;
                Hud.Fill(r, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, active ? 0.42f : empty ? 0.12f : 0.24f), 14f);
                Hud.Frame(r, active ? Hud.Teal : new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, empty ? 0.2f : 0.36f), active ? 3f : 1.5f, 14f);
                if (active) Hud.Fill(new Rect(r.x + 18f, r.yMax - 5f, r.width - 36f, 3f), Hud.Teal, 1.5f);

                if (item != null)
                {
                    float pop = Hud.EaseOutBack((Time.unscaledTime - _filledAt[i]) / 0.25f);
                    float inset = size * 0.5f - size * 0.41f * pop;
                    var inner = new Rect(r.x + inset, r.y + inset, size - inset * 2f, size - inset * 2f);
                    Texture picture = ItemIcons.Get(item);
                    if (picture != null) Hud.Picture(inner, picture, active ? Color.white : new Color(1f, 1f, 1f, 0.8f));
                    else Hud.Label(new Rect(r.x + 6f, r.y + 24f, size - 12f, size - 30f), item.DisplayName, 15f, Color.white, TextAnchor.MiddleCenter, wrap: true);
                }
                // The slot's number in its corner: a teal chip on the one in your hands, plain outlined digits elsewhere.
                var number = new Rect(r.x + 7f, r.y + 7f, 24f, 24f);
                if (active) Hud.Fill(number, Hud.Teal, 5f);
                Hud.Label(number, SlotNumbers[i], 15f, active ? Hud.Ink : new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, empty ? 0.55f : 0.85f), heavy: true, shadow: false);
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
                _heldIsPerson = held != null && held.GetComponent<Rescue.VictimBrain>() != null;
                _hintsBuilt = false;
            }
            if (held == null) return;

            float nameY = Hud.HotbarTop - 50f;
            if (!_heldIsPerson) // RescueHud says what to do with a person
            {
                Hud.Label(new Rect(cx - 500f, nameY, 1000f, 40f), held.DisplayName, 25f, Hud.Paper, heavy: true);
                float hintAlpha = Mathf.Clamp01(HintSeconds - (Time.unscaledTime - _heldSince));
                if (hintAlpha > 0f)
                {
                    // The skin keys join the line once the item's skins are known to be ours to change.
                    bool withSkin = Items.ItemSkin.LocalHeld != null;
                    if (!_hintsBuilt || withSkin != _hintsWithSkin)
                    {
                        Hints(held, _hints);
                        _hintsBuilt = true;
                        _hintsWithSkin = withSkin;
                    }
                    Hud.KeyHints(cx, nameY - 34f, _hints, hintAlpha, 17f);
                }
            }

            // Eating and winding up a throw: a small ring round the crosshair fills up (How to Fish's hold circle),
            // nothing across the middle of the screen.
            if (hands.IsEating || hands.EatProgress01 > 0.01f)
                HoldRing(cx, cy, hands.EatProgress01, Color.white);
            else if (hands.IsCharging)
                HoldRing(cx, cy, hands.Charge01, Color.Lerp(Color.white, Hud.Coral, hands.Charge01 * hands.Charge01));
        }

        private static void HoldRing(float cx, float cy, float value, Color fill)
        {
            const float size = 34f, thick = 0.12f;
            var r = new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size);
            Hud.Arc(new Rect(r.x + 1f, r.y + 1.5f, size, size), 0f, 1f, thick * 1.3f, new Color(0f, 0f, 0f, 0.3f));
            Hud.Arc(r, 0f, 1f, thick, new Color(1f, 1f, 1f, 0.25f));
            Hud.Arc(r, 0f, value, thick, fill);
        }

        /// <summary>What the keys do with the item in your hands, as keycap pairs (Hud.KeyHints).</summary>
        private static void Hints(Items.Item held, List<(string key, string what)> hints)
        {
            hints.Clear();
            string drop = GameInput.KeyLabel(GameInput.Drop);
            var tool = held.GetComponent<Combat.IHeldTool>();
            if (tool is Combat.Weapon)
            {
                hints.Add((GameInput.KeyLabel(GameInput.Primary), "shoot"));
                hints.Add((GameInput.KeyLabel(GameInput.Secondary), "aim"));
                hints.Add((GameInput.KeyLabel(GameInput.Reload), "reload"));
                hints.Add((GameInput.KeyLabel(GameInput.Inspect), "inspect"));
            }
            else if (tool != null) hints.Add((GameInput.KeyLabel(GameInput.Primary), tool.UseLabel));
            else hints.Add((GameInput.KeyLabel(GameInput.Primary), "throw"));
            if (held.GetComponent<Items.Edible>() != null) hints.Add((GameInput.KeyLabel(GameInput.Secondary), "hold to eat"));
            hints.Add((drop, "drop, hold to throw"));
            if (Items.ItemSkin.LocalHeld != null)
                hints.Add(($"{GameInput.KeyLabel(GameInput.SkinPrev)}/{GameInput.KeyLabel(GameInput.SkinNext)}", "skin"));
        }

        /// <summary>
        /// Vitals as rings in the bottom left, as How to Fish has them: food always, air and stamina only while
        /// they're in use (they fade in and slide the others along).
        /// </summary>
        private void DrawVitals(PlayerHub local, float dt)
        {
            const float size = 60f, gap = 14f;
            float x = Hud.Margin, y = Hud.Height - 25f - size;
            PlayerMotor motor = local.Motor;
            PlayerVitals vitals = local.Vitals;
            // Health: only while hurt (it fills back up on its own), first in the row.
            if (local.TryGetComponent(out Combat.PlayerCombat combat))
            {
                float health = combat.Health01;
                _healthShown = Mathf.MoveTowards(_healthShown, health < 0.999f ? 1f : 0f, dt / 0.25f);
                Trail(ref _healthTrail, ref _healthDropAt, health, dt);
                if (_healthShown > 0f)
                {
                    Hud.Vital(new Rect(x, y, size, size), health, _healthTrail, HudIcon.Health, new Color(1f, 0.3f, 0.3f), health < 0.3f, _healthShown);
                    x += (size + gap) * _healthShown;
                }
            }
            if (vitals != null)
            {
                Trail(ref _foodTrail, ref _foodDropAt, vitals.Food01, dt);
                Hud.Vital(new Rect(x, y, size, size), vitals.Food01, _foodTrail, HudIcon.Food, new Color(1f, 0.55f, 0.3f), vitals.Hungry);
                x += size + gap;
            }
            if (motor == null) return;

            bool air = motor.Air01 < 0.999f || motor.IsHeadUnderwater;
            _airShown = Mathf.MoveTowards(_airShown, air ? 1f : 0f, dt / 0.25f);
            Trail(ref _airTrail, ref _airDropAt, motor.Air01, dt);
            if (_airShown > 0f)
            {
                Hud.Vital(new Rect(x, y, size, size), motor.Air01, _airTrail, HudIcon.Air, new Color(1f, 0.4f, 0.35f), motor.Air01 < 0.3f, _airShown);
                x += (size + gap) * _airShown;
            }
            _staminaShown = Mathf.MoveTowards(_staminaShown, motor.Stamina01 < 0.999f ? 1f : 0f, dt / 0.25f);
            Trail(ref _staminaTrail, ref _staminaDropAt, motor.Stamina01, dt);
            if (_staminaShown > 0f)
                Hud.Vital(new Rect(x, y, size, size), motor.Stamina01, _staminaTrail, HudIcon.Stamina, new Color(1f, 0.75f, 0.2f), motor.Stamina01 < 0.15f, _staminaShown);

            if (motor.IsOutOfBreath)
                Hud.Label(new Rect(0f, Hud.Height * 0.3f, Hud.Width, 46f), "<b>OUT OF AIR!</b> Get to the surface!", 34f, new Color(1f, 0.5f, 0.4f), heavy: true);
        }

        private float _healthShown, _healthTrail = 1f, _healthDropAt;
        private static float _meleeHitAt = -10f;

        /// <summary>Your punch landed on something that can be hurt: the little hit marker round the crosshair.</summary>
        public static void ShowMeleeHit() => _meleeHitAt = Time.unscaledTime;

        /// <summary>
        /// A red flash at the screen's edges when hurt; while down, the screen darkens, says so, and shows how far a
        /// friend's CPR has got. Also the punch hit marker.
        /// </summary>
        private static void DrawHurt(PlayerHub local, float cx, float cy)
        {
            float since = Time.unscaledTime - _meleeHitAt;
            if (since < 0.22f)
            {
                float a = 1f - since / 0.22f, gap = 9f + since * 30f, len = 10f;
                var white = new Color(1f, 1f, 1f, a);
                for (int i = 0; i < 4; i++)
                {
                    float sx = i % 2 == 0 ? -1f : 1f, sy = i < 2 ? -1f : 1f;
                    Hud.Fill(new Rect(cx + sx * gap - (sx < 0 ? len : 0f), cy + sy * gap - (sy < 0 ? 3f : 0f), len, 3f), white, 1.5f);
                }
            }

            if (!local.TryGetComponent(out Combat.PlayerCombat combat)) return;
            float hurt = Mathf.Clamp01(1f - (Time.time - combat.LastHurtTime) / 0.45f) * (0.35f + combat.LastHurtStrength * 0.4f);
            float low = combat.IsDowned ? 0f : Mathf.Clamp01((0.35f - combat.Health01) / 0.35f) * (0.18f + 0.06f * Mathf.Sin(Time.time * 6f));
            float edge = Mathf.Max(hurt, low);
            if (edge > 0.01f)
            {
                // A red glow fading in from the screen's edges: thin rings, each fainter than the one outside it (four
                // flat 140 px bands made a hard-edged red box round the view).
                const float band = 160f;
                const int rings = 16;
                const float ring = band / rings;
                for (int i = 0; i < rings; i++)
                {
                    float fade = 1f - i / (float)rings;
                    var red = new Color(0.85f, 0.05f, 0.05f, edge * 0.55f * fade * fade);
                    float inset = i * ring, across = Hud.Width - inset * 2f, down = Hud.Height - (inset + ring) * 2f;
                    Hud.Fill(new Rect(inset, inset, across, ring), red, 0f);
                    Hud.Fill(new Rect(inset, Hud.Height - inset - ring, across, ring), red, 0f);
                    Hud.Fill(new Rect(inset, inset + ring, ring, down), red, 0f);
                    Hud.Fill(new Rect(Hud.Width - inset - ring, inset + ring, ring, down), red, 0f);
                }
            }

            if (!combat.IsDowned) return;
            // Down: lying on the sand says most of it. Just one quiet line above the hotbar, and the revive bar
            // while a friend is pumping.
            bool someoneUp = false;
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p != local && p.TryGetComponent(out Combat.PlayerCombat c) && !c.IsDowned) someoneUp = true;
            string line = combat.Revive01 > 0f ? "Being revived..." : someoneUp ? "You're down. A friend can revive you (CPR)" : "You're down. Getting your breath back...";
            float y = Hud.HotbarTop - 70f;
            var downed = new Rect(cx - 330f, y - 8f, 660f, combat.Revive01 > 0f ? 70f : 50f);
            Hud.Surface(downed, 0.94f, Hud.Danger);
            Hud.Label(new Rect(downed.x + 16f, y, downed.width - 32f, 34f), line, 22f, Hud.Paper, heavy: true, shadow: false);
            if (combat.Revive01 > 0f)
                Hud.Bar(new Rect(cx - 140f, y + 40f, 280f, 10f), combat.Revive01, new Color(0.5f, 1f, 0.6f));
        }

        /// <summary>Follows a level up at once; down, it holds a quarter second, then eases after it.</summary>
        private static void Trail(ref float trail, ref float dropAt, float value, float dt)
        {
            if (value >= trail) { trail = value; dropAt = Time.unscaledTime; return; }
            if (Time.unscaledTime - dropAt < 0.25f) return;
            trail = Mathf.MoveTowards(trail, value, Mathf.Max(0.02f, (trail - value) * 6f) * dt);
        }
    }
}
