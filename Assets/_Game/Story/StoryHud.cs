using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Story HUD: chapter + objective in the top left, money above the vitals in the bottom left, the objective
    /// marker (pinned to the screen edge when off-screen), dialogue subtitles, chapter title cards, and a small
    /// sparkle over lost things lying nearby. Drawn with <see cref="Hud"/> on its 1080-high sheet.
    /// </summary>
    public class StoryHud : MonoBehaviour
    {
        private static string _title, _subtitle;
        private static float _titleAt = -100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _title = _subtitle = null;
            _titleAt = -100f;
        }

        /// <summary>A big centred card for a few seconds ("CHAPTER 2").</summary>
        public static void ShowTitle(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
            _titleAt = Time.unscaledTime;
        }

        private void OnGUI()
        {
            PlayerHub local = PlayerHub.Local;
            if (local == null || !GameInput.GameplayActive) return;
            Camera cam = local.Look != null ? local.Look.Camera : null;

            DrawObjective();
            if (cam != null)
            {
                DrawMarker(cam, local);
                DrawLostSparkles(cam, local);
            }
            DrawDialogue();
            DrawTitle();
        }

        private void DrawObjective()
        {
            StoryDirector story = StoryDirector.Instance;
            if (story != null && !string.IsNullOrEmpty(story.Objective))
            {
                Hud.Label(new Rect(30f, 22f, 900f, 28f), story.Chapter, 20f, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleLeft, heavy: true);
                string progress = story.Goal > 0 ? $"  <color=#ffd24a>{story.Progress}/{story.Goal}</color>" : "";
                Hud.Label(new Rect(30f, 52f, 820f, 80f), story.Objective + progress, 27f, Color.white, TextAnchor.UpperLeft, wrap: true);
            }
            if (Economy.Instance != null)
            {
                // Above the vitals squares.
                string money = "$" + Economy.Money;
                float width = Hud.TextWidth(money, 30f, true) + 36f;
                var pill = new Rect(50f, Hud.Height - 25f - 64f - 14f - 46f, width, 46f);
                Hud.Fill(pill, new Color(0f, 0f, 0f, 0.5f), 12f);
                Hud.Label(pill, money, 30f, new Color(0.56f, 1f, 0.56f), heavy: true, shadow: false);
            }
        }

        private void DrawMarker(Camera cam, PlayerHub local)
        {
            StoryDirector story = StoryDirector.Instance;
            Vector3? world = story != null ? story.MarkerPosition : null;
            if (!world.HasValue) return;
            Vector3 screen = cam.WorldToScreenPoint(world.Value);
            bool behind = screen.z < 0f;
            if (behind) screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f);
            float margin = 80f * Hud.Scale;
            bool offscreen = behind || screen.x < margin || screen.x > Screen.width - margin || screen.y < margin || screen.y > Screen.height - margin;
            if (offscreen)
            {
                Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 dir = new Vector2(screen.x, screen.y) - center;
                if (behind && dir.sqrMagnitude < 1f) dir = Vector2.down;
                float scale = Mathf.Min((center.x - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.x)), (center.y - margin) / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                Vector2 edge = center + dir * scale;
                screen = new Vector3(edge.x, edge.y, 0f);
            }
            // From here on: sheet units.
            float sx = screen.x / Hud.Scale, sy = (Screen.height - screen.y) / Hud.Scale;
            float distance = Vector3.Distance(local.transform.position, world.Value);
            if (distance < 4f && !offscreen) return; // right there: the prompt and name tag say enough
            float size = 22f * (1f + 0.15f * Mathf.Sin(Time.time * 5f));
            Hud.Fill(new Rect(sx - size * 0.5f - 3f, sy - size * 0.5f - 3f, size + 6f, size + 6f), new Color(0f, 0f, 0f, 0.6f), size * 0.5f + 3f);
            Hud.Fill(new Rect(sx - size * 0.5f, sy - size * 0.5f, size, size), new Color(1f, 0.8f, 0.2f), size * 0.5f);
            bool below = sy < 110f;
            Hud.Label(new Rect(sx - 250f, below ? sy + 18f : sy - 74f, 500f, 56f), $"<b>{story.MarkerLabel}</b>\n{distance:F0} m", 21f,
                new Color(1f, 0.85f, 0.35f), TextAnchor.UpperCenter);
        }

        private void DrawLostSparkles(Camera cam, PlayerHub local)
        {
            foreach (Item item in Item.All)
            {
                if (item.IsHeld || !item.TryGetComponent(out LostItem lost)) continue;
                float d = Vector3.Distance(local.transform.position, item.transform.position);
                if (d > 14f) continue;
                Vector3 s = cam.WorldToScreenPoint(item.transform.position + Vector3.up * 0.35f);
                if (s.z < 0f) continue;
                float twinkle = 0.55f + 0.45f * Mathf.Sin(Time.time * 6f + item.GetInstanceID());
                float a = Mathf.Clamp01((14f - d) / 6f) * twinkle;
                Color color = lost.IsEvidence ? new Color(1f, 0.5f, 0.4f, a) : new Color(1f, 0.95f, 0.5f, a);
                // A four-pointed glint: two thin crossed strokes.
                float x = s.x / Hud.Scale, y = (Screen.height - s.y) / Hud.Scale, arm = 9f + 5f * twinkle;
                Hud.Fill(new Rect(x - 2f, y - arm, 4f, arm * 2f), color, 2f);
                Hud.Fill(new Rect(x - arm, y - 2f, arm * 2f, 4f), color, 2f);
            }
        }

        private void DrawDialogue()
        {
            DialogueLine? line = DialogueService.Current;
            if (!line.HasValue) return;
            DialogueLine l = line.Value;
            float w = Mathf.Min(1100f, Hud.Width - 80f);
            // Clear of the hotbar and the name of what you hold.
            var box = new Rect((Hud.Width - w) * 0.5f, Hud.HotbarTop - 96f - 124f, w, 124f);
            Hud.Fill(box, new Color(0f, 0f, 0f, 0.6f), 16f);
            string speaker = l.Speaker == DialogueService.PlayerSpeaker ? "You" : l.Speaker;
            Hud.Label(new Rect(box.x + 24f, box.y + 10f, w - 48f, 32f), speaker, 24f, l.Color, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            // Typewriter.
            int shown = Mathf.Clamp(Mathf.CeilToInt((Time.time - l.Start) * 55f), 0, l.Text.Length);
            GUIStyle style = Hud.Style(25f, TextAnchor.UpperLeft, wrap: true);
            style.richText = false; // spoken lines are shown as written
            Hud.Label(new Rect(box.x + 24f, box.y + 44f, w - 48f, 76f), l.Text.Substring(0, shown), 25f, Color.white, TextAnchor.UpperLeft, wrap: true, shadow: false);
            style.richText = true;
        }

        private void DrawTitle()
        {
            float t = Time.unscaledTime - _titleAt;
            if (_title == null || t > 4.5f) return;
            float a = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((4.5f - t) / 0.8f);
            float y = Hud.Height * 0.28f;
            Hud.Fill(new Rect(0f, y, Hud.Width, 176f), new Color(0f, 0f, 0f, 0.45f * a));
            Hud.Label(new Rect(0f, y + 14f, Hud.Width, 96f), _title, 78f, new Color(1f, 0.92f, 0.6f, a), heavy: true);
            Hud.Label(new Rect(0f, y + 112f, Hud.Width, 46f), _subtitle, 30f, new Color(1f, 1f, 1f, a));
        }
    }
}
