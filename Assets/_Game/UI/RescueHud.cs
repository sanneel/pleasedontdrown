using PleaseDontDrown.Core;
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
        private void OnGUI()
        {
            PlayerHub local = PlayerHub.Local;
            Camera cam = local != null && local.Look != null ? local.Look.Camera : null;
            if (cam == null || !GameInput.GameplayActive)
                return;
            foreach (VictimBrain v in VictimBrain.All)
            {
                VictimState state = v.State;
                if (!state.NeedsHelp() && state != VictimState.Saved) continue;
                if (v.Item.IsHeld && v.Item.Holder == local) continue; // it's in your arms
                // Silent drowners give no sign: only a marker once you're close (or they're out cold).
                if (v.IsSilent && state.IsStruggling() && Vector3.Distance(local.transform.position, v.transform.position) > 12f) continue;
                DrawMarker(cam, local, v, state);
            }

            DrawCarryHint(local);
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
                _ => state.Label()
            };
            if (v.HasLostLeg && state != VictimState.Saved && state != VictimState.Lost) info += $"  BLEEDING {v.BleedSecondsLeft:F0}s";
            if (v.Item.IsHeld) info += $"  (with {v.Item.Holder.DisplayName})";
            string text = $"<b>{v.Name}</b>  {info}\n{distance:F0} m";
            // Label and bar above the dot (clear of the tourist), or below it when pinned to the top edge.
            bool below = y < 100f;
            var rect = new Rect(x - 300f, below ? y + 16f : y - 84f, 600f, 56f);
            Hud.Label(rect, text, 21f, color, TextAnchor.UpperCenter);

            // Air (awake) or CPR progress (unconscious on land).
            float bar = state == VictimState.Unconscious ? v.Cpr01 : v.Air01;
            if (state.NeedsHelp() && (state != VictimState.Unconscious || bar > 0f))
                Hud.Bar(new Rect(x - 45f, below ? rect.y + 60f : y - 24f, 90f, 8f), bar,
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
            Hud.Label(new Rect(Hud.Width * 0.5f - 800f, Hud.HotbarTop - 56f, 1600f, 40f), text, 26f, Hud.Sand);
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
