using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Markers over tourists in trouble (name, state, distance, air or time left), pinned to the screen edge when
    /// off-screen, plus a hint while you carry someone. Drawn with <see cref="Hud"/> on its 1080-high sheet.
    /// </summary>
    public class RescueHud : MonoBehaviour
    {
        // Drawn on the HUD canvas (no IMGUI): see HudCanvas.
        private void OnEnable() => HudCanvas.Add(this, HudCanvas.OrderWorldMarkers, DrawHud);

        private void OnDisable() => HudCanvas.Remove(this);

        private void DrawHud()
        {
            PlayerHub local = PlayerHub.Local;
            Camera cam = local != null && local.Look != null ? local.Look.Camera : null;
            if (cam == null || !GameInput.GameplayActive)
                return;
            var victims = VictimBrain.All;
            for (int i = 0; i < victims.Count; i++)
            {
                VictimBrain v = victims[i];
                VictimState state = v.State;
                if (!state.NeedsHelp() && state != VictimState.Saved) continue;
                if (v.Item.IsHeld && v.Item.Holder == local) continue; // it's in your arms
                // Silent drowners give no sign: only a marker once you're close (or they're out cold).
                if (v.IsSilent && state.IsStruggling() && Vector3.Distance(local.transform.position, v.transform.position) > 12f) continue;
                DrawMarker(cam, local, v, state);
            }

            DrawCarryHint(local);
            DrawCpr(local);
        }

        // The CPR panel's words, made again only when the patient, the step or a key changes.
        private VictimBrain _cprPatient;
        private int _cprStamp = -1;
        private readonly string[] _cprKeys = new string[2], _cprWhat = new string[2], _cprCount = new string[2];
        private string _cprPercent;
        private int _cprPercentShown = -1;

        /// <summary>
        /// Looking at someone who needs CPR: the steps side by side, each with its own button (pump the chest, then
        /// the kiss of life for a woman or a slap for a man), the one to do now lit up, and how far along they are.
        /// </summary>
        private void DrawCpr(PlayerHub local)
        {
            Interactable target = local.Interactor != null ? local.Interactor.Current : null;
            if (target == null || local.Interactor.CurrentSecondaryPrompt == null || !target.TryGetComponent(out VictimBrain v)) return;
            if (v.State != VictimState.Unconscious || v.IsFlatlined) return;

            CprStep now = v.NextCprStep, second = v.IsFemale ? CprStep.Breath : CprStep.Punch;
            int stamp = System.HashCode.Combine((int)now, v.CprCount, GameInput.BindingsVersion);
            if (v != _cprPatient || stamp != _cprStamp)
            {
                _cprPatient = v;
                _cprStamp = stamp;
                _cprKeys[0] = GameInput.KeyLabel(VictimBrain.KeyOf(CprStep.Compress));
                _cprKeys[1] = GameInput.KeyLabel(VictimBrain.KeyOf(second));
                _cprWhat[0] = "PUMP THE CHEST";
                _cprWhat[1] = second == CprStep.Breath ? "KISS OF LIFE" : "SLAP AWAKE";
                _cprCount[0] = $"{(now == CprStep.Compress ? v.CprCount : v.CompressionsPerSet)} / {v.CompressionsPerSet}";
                int needed = second == CprStep.Breath ? v.BreathsPerSet : 1;
                _cprCount[1] = $"{(now == CprStep.Compress ? 0 : v.CprCount)} / {needed}";
            }
            int percent = Mathf.RoundToInt(v.Cpr01 * 100f);
            if (percent != _cprPercentShown)
            {
                _cprPercentShown = percent;
                _cprPercent = $"REVIVING  {percent}%";
            }

            // On the right, half way down: clear of the dialogue box and the hotbar (bottom middle), the objective (top
            // left) and the patient in the middle of the view.
            const float chip = 330f, gap = 12f, height = 80f;
            float left = Hud.Width - chip - 48f, top = Hud.Height * 0.5f - height - gap * 0.5f;
            Hud.Surface(new Rect(left - 18f, top - 54f, chip + 36f, 300f), 0.94f);
            Hud.Label(new Rect(left, top - 42f, chip, 28f), "RESCUE  /  CPR", 18f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            for (int i = 0; i < 2; i++)
            {
                bool current = (i == 0) == (now == CprStep.Compress);
                var box = new Rect(left, top + i * (height + gap), chip, height);
                // (No dark boxes: only the current step is framed; the words are outlined straight on the game.)
                if (current) Hud.Frame(box, Hud.Teal, 2f, 10f);
                Color words = current ? Hud.Paper : Hud.Muted;
                var key = new Rect(box.x + 12f, box.y + 11f, 84f, 58f);
                if (current) Hud.Fill(key, Hud.Teal, 8f);
                else Hud.Frame(key, new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, 0.5f), 1.5f, 8f);
                Hud.FitLabel(key, _cprKeys[i], 26f, current ? Hud.Ink : words, TextAnchor.MiddleCenter, heavy: true);
                Hud.FitLabel(new Rect(box.x + 108f, box.y + 8f, chip - 120f, 36f), _cprWhat[i], 21f, words, TextAnchor.MiddleLeft, heavy: true);
                Hud.Label(new Rect(box.x + 108f, box.y + 40f, chip - 116f, 30f), _cprCount[i], 21f, words, TextAnchor.MiddleLeft, shadow: false);
            }
            var bar = new Rect(left, top + 2f * height + gap + 14f, chip, 14f);
            Hud.Bar(bar, v.Cpr01, Hud.Teal);
            Hud.Label(new Rect(bar.x, bar.yMax + 4f, bar.width, 30f), _cprPercent, 17f, Hud.Teal, heavy: true, shadow: false);
        }

        private void DrawMarker(Camera cam, PlayerHub local, VictimBrain v, VictimState state)
        {
            Vector3 world = v.Body.HeadPosition + Vector3.up * 0.7f;
            float distance = Vector3.Distance(local.transform.position, v.transform.position);
            Vector3 screen = cam.WorldToScreenPoint(world);
            bool behind = screen.z < 0f;
            if (behind) screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f);

            float margin = 70f * Hud.Scale;
            bool offscreen = behind || screen.x < margin || screen.x > Screen.width - margin || screen.y < margin || screen.y > Screen.height - margin;
            if (!offscreen && distance < 2.6f) return; // right in front of you: the prompts under the crosshair say it all
            if (offscreen)
            {
                // Pin to the edge in the target's direction.
                Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 dir = new Vector2(screen.x, screen.y) - center;
                if (behind && dir.sqrMagnitude < 1f) dir = Vector2.down;
                float scale = Mathf.Min((center.x - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (center.y - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                Vector2 edge = center + dir * scale;
                screen = new Vector3(edge.x, edge.y, 0f);
            }

            // From here on: sheet units.
            float x = screen.x / Hud.Scale;
            float y = (Screen.height - screen.y) / Hud.Scale;
            Color color = ColorOf(state);
            if (state == VictimState.Unconscious)
                color = Color.Lerp(color, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 8f)));

            float size = offscreen ? 20f : 14f;
            Hud.Fill(new Rect(x - size * 0.5f - 3f, y - size * 0.5f - 3f, size + 6f, size + 6f), new Color(0f, 0f, 0f, 0.6f), size * 0.5f + 3f);
            Hud.Fill(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), color, size * 0.5f);

            string info = state switch
            {
                VictimState.Unconscious when v.IsFlatlined => $"NO PULSE: DEFIBRILLATOR!  {v.ConditionSecondsLeft:F0}s",
                VictimState.Unconscious => $"UNCONSCIOUS  {v.ConditionSecondsLeft:F0}s",
                VictimState.Saved => "SAVED",
                VictimState.Injured when v.IsKnockedOut => "KNOCKED OUT: TO THE INFIRMARY",
                _ => state.Label()
            };
            if (v.HasLostLeg && state != VictimState.Saved && state != VictimState.Lost) info += $"  BLEEDING {v.BleedSecondsLeft:F0}s";
            if (v.Item.IsHeld) info += $"  (with {v.Item.Holder.DisplayName})";
            string text = $"{v.Name}  ·  {distance:F0} m";
            // Label and bar above the dot (clear of the tourist), or below it when pinned to the top edge.
            bool below = y < 100f;
            float infoHeight = Hud.TextHeight(info, 280f, 17f, heavy: true);
            float labelHeight = 40f + infoHeight;
            var rect = new Rect(Mathf.Clamp(x - 156f, 12f, Hud.Width - 324f), below ? y + 18f : y - labelHeight - 30f, 312f, labelHeight);
            Hud.Surface(rect, 0.8f, color);
            Hud.FitLabel(new Rect(rect.x + 16f, rect.y + 6f, 280f, 27f), text, 21f, Hud.Paper, TextAnchor.MiddleCenter, heavy: true);
            Hud.Label(new Rect(rect.x + 16f, rect.y + 34f, 280f, infoHeight), info, 17f, color, TextAnchor.UpperCenter, heavy: true, wrap: true, shadow: false);

            // Air (awake) or CPR progress (unconscious on land).
            float bar = state == VictimState.Unconscious ? v.Cpr01 : v.Air01;
            if (state.NeedsHelp() && (state != VictimState.Unconscious || bar > 0f))
                Hud.Bar(new Rect(rect.center.x - 45f, below ? rect.yMax + 6f : y - 24f, 90f, 8f), bar,
                    state == VictimState.Unconscious ? new Color(0.5f, 1f, 0.5f) : bar < 0.35f ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.8f, 1f));
        }

        private void DrawCarryHint(PlayerHub local)
        {
            var held = local.Hands != null ? local.Hands.HeldItem : null;
            if (held == null || !held.TryGetComponent(out VictimBrain v)) return;
            string drop = GameInput.KeyLabel(GameInput.Drop);
            string cpr = GameInput.KeyLabel(GameInput.Secondary);
            string text = v.HasLostLeg && v.State != VictimState.Saved
                ? $"Rush <b>{v.Name}</b> to the hotel infirmary and put them on the bed (<b>[{GameInput.KeyLabel(GameInput.Interact)}]</b>)  -  {v.BleedSecondsLeft:F0}s left"
                : v.State == VictimState.Unconscious
                ? $"Get <b>{v.Name}</b> onto the sand, put them down (tap <b>[{drop}]</b>) and do CPR (<b>[{cpr}]</b>)  -  {v.ConditionSecondsLeft:F0}s left"
                : v.State.NeedsHelp()
                    ? $"Bring <b>{v.Name}</b> back to the shallows!"
                    : $"Carrying <b>{v.Name}</b>";
            // Where the held item's name and keys would be.
            float width = Mathf.Min(960f, Hud.Width - 160f);
            float height = Hud.TextHeight(text, width - 40f, 22f) + 24f;
            var card = new Rect((Hud.Width - width) * 0.5f, Hud.HotbarTop - height - 18f, width, height);
            Hud.Surface(card, 0.9f, Hud.Sand);
            Hud.Label(new Rect(card.x + 20f, card.y + 12f, width - 40f, height - 24f), text, 22f, Hud.Sand, wrap: true, shadow: false);
        }

        private static Color ColorOf(VictimState s) => s switch
        {
            VictimState.Distressed => new Color(1f, 0.9f, 0.35f),
            VictimState.Panicking => new Color(1f, 0.6f, 0.2f),
            VictimState.Drowning => new Color(1f, 0.4f, 0.25f),
            VictimState.Unconscious => new Color(1f, 0.2f, 0.2f),
            VictimState.Saved => new Color(0.45f, 1f, 0.5f),
            VictimState.Injured => new Color(1f, 0.35f, 0.35f),
            _ => Color.white
        };
    }
}
