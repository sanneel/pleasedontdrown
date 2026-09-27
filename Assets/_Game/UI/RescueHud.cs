using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Markers over tourists in trouble (name, state, distance, air or time left), pinned to the screen edge when
    /// off-screen, plus a hint while you carry someone. IMGUI placeholder like the rest of the prototype HUD.
    /// </summary>
    public class RescueHud : MonoBehaviour
    {
        private GUIStyle _label;
        private GUIStyle _hint;
        private Texture2D _dot;

        private void OnGUI()
        {
            PlayerHub local = PlayerHub.Local;
            Camera cam = local != null && local.Look != null ? local.Look.Camera : null;
            if (cam == null || !GameInput.GameplayActive)
                return;
            EnsureStyles();

            foreach (VictimBrain v in VictimBrain.All)
            {
                VictimState state = v.State;
                if (!state.NeedsHelp() && state != VictimState.Saved) continue;
                if (v.Item.IsHeld && v.Item.Holder == local) continue; // it's in your arms
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

            const float margin = 50f;
            bool offscreen = behind || screen.x < margin || screen.x > Screen.width - margin || screen.y < margin || screen.y > Screen.height - margin;
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

            float x = screen.x;
            float y = Screen.height - screen.y;
            Color color = ColorOf(state);
            if (state == VictimState.Unconscious)
                color = Color.Lerp(color, Color.white, 0.35f * (0.5f + 0.5f * Mathf.Sin(Time.time * 8f)));

            // Diamond-ish dot.
            float size = offscreen ? 14f : 10f;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - size * 0.5f - 2f, y - size * 0.5f - 2f, size + 4f, size + 4f), _dot);
            GUI.color = color;
            GUI.DrawTexture(new Rect(x - size * 0.5f, y - size * 0.5f, size, size), _dot);

            string info = state switch
            {
                VictimState.Unconscious => $"UNCONSCIOUS  {v.ConditionSecondsLeft:F0}s",
                VictimState.Saved => "SAVED",
                _ => state.Label()
            };
            if (v.Item.IsHeld) info += $"  (with {v.Item.Holder.DisplayName})";
            string text = $"<b>{v.Name}</b>  {info}\n{distance:F0} m";
            // Label and bar above the dot (clear of the tourist), or below it when pinned to the top edge.
            bool below = y < 70f;
            var rect = new Rect(x - 150f, below ? y + 10f : y - 58f, 300f, 40f);
            GUI.color = Color.white;
            _label.normal.textColor = color;
            Shadow(rect, text, _label);

            // Air (awake) or CPR progress (unconscious on land).
            float bar = state == VictimState.Unconscious ? v.Cpr01 : v.Air01;
            if (state.NeedsHelp() && (state != VictimState.Unconscious || bar > 0f))
            {
                var back = new Rect(x - 30f, below ? rect.y + 42f : y - 16f, 60f, 5f);
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(back, _dot);
                GUI.color = state == VictimState.Unconscious ? new Color(0.5f, 1f, 0.5f) : bar < 0.35f ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.8f, 1f);
                GUI.DrawTexture(new Rect(back.x, back.y, back.width * Mathf.Clamp01(bar), back.height), _dot);
            }
            GUI.color = Color.white;
        }

        private void DrawCarryHint(PlayerHub local)
        {
            var held = local.Hands != null ? local.Hands.HeldItem : null;
            if (held == null || !held.TryGetComponent(out VictimBrain v)) return;
            string drop = GameInput.KeyLabel(GameInput.Drop);
            string cpr = GameInput.KeyLabel(GameInput.Secondary);
            string text = v.State == VictimState.Unconscious
                ? $"Get <b>{v.Name}</b> onto the sand, put them down (tap <b>[{drop}]</b>) and do CPR (<b>[{cpr}]</b>)  -  {v.ConditionSecondsLeft:F0}s left"
                : v.State.NeedsHelp()
                    ? $"Bring <b>{v.Name}</b> back to the shallows!"
                    : $"Carrying <b>{v.Name}</b>";
            Shadow(new Rect(Screen.width * 0.5f - 450f, Screen.height - 100f, 900f, 30f), text, _hint);
        }

        private static Color ColorOf(VictimState s) => s switch
        {
            VictimState.Distressed => new Color(1f, 0.9f, 0.35f),
            VictimState.Panicking => new Color(1f, 0.6f, 0.2f),
            VictimState.Drowning => new Color(1f, 0.4f, 0.25f),
            VictimState.Unconscious => new Color(1f, 0.2f, 0.2f),
            VictimState.Saved => new Color(0.45f, 1f, 0.5f),
            _ => Color.white
        };

        private static void Shadow(Rect rect, string text, GUIStyle style)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.75f);
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
            _label ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 15, richText = true };
            _hint ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, richText = true, normal = { textColor = new Color(1f, 0.95f, 0.8f) } };
        }
    }
}
