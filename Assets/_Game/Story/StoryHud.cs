using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Story HUD (IMGUI placeholder like the rest of the prototype): chapter + objective + money in the top left, the
    /// objective marker (pinned to the screen edge when off-screen), dialogue subtitles, chapter title cards, and a
    /// small sparkle over lost things lying nearby.
    /// </summary>
    public class StoryHud : MonoBehaviour
    {
        private static string _title, _subtitle;
        private static float _titleAt = -100f;

        private GUIStyle _chapter, _objective, _money, _marker, _speaker, _line, _titleStyle, _subStyle, _sparkle;
        private Texture2D _dot;

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
            EnsureStyles();
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
            float x = 18f, y = 14f;
            if (story != null && !string.IsNullOrEmpty(story.Objective))
            {
                Shadow(new Rect(x, y, 600f, 22f), story.Chapter, _chapter);
                y += 22f;
                string progress = story.Goal > 0 ? $"  <b>{story.Progress}/{story.Goal}</b>" : "";
                Shadow(new Rect(x, y, 640f, 50f), $"▸ {story.Objective}{progress}", _objective);
                y += 30f;
            }
            if (Economy.Instance != null)
                Shadow(new Rect(x, y, 300f, 26f), $"<color=#90ff90>${Economy.Money}</color>", _money);
        }

        private void DrawMarker(Camera cam, PlayerHub local)
        {
            StoryDirector story = StoryDirector.Instance;
            Vector3? world = story != null ? story.MarkerPosition : null;
            if (!world.HasValue) return;
            Vector3 screen = cam.WorldToScreenPoint(world.Value);
            bool behind = screen.z < 0f;
            if (behind) screen = new Vector3(Screen.width - screen.x, Screen.height - screen.y, 0f);
            const float margin = 60f;
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
            float sx = screen.x, sy = Screen.height - screen.y;
            float distance = Vector3.Distance(local.transform.position, world.Value);
            if (distance < 4f && !offscreen) return; // right there: the prompt and name tag say enough
            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 5f);
            float size = 16f * pulse;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(sx - size * 0.5f - 2f, sy - size * 0.5f - 2f, size + 4f, size + 4f), _dot);
            GUI.color = new Color(1f, 0.8f, 0.2f);
            GUI.DrawTexture(new Rect(sx - size * 0.5f, sy - size * 0.5f, size, size), _dot);
            GUI.color = Color.white;
            bool below = sy < 80f;
            Shadow(new Rect(sx - 150f, below ? sy + 12f : sy - 48f, 300f, 40f), $"<b>{story.MarkerLabel}</b>\n{distance:F0} m", _marker);
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
                float a = Mathf.Clamp01((14f - d) / 6f) * (0.55f + 0.45f * Mathf.Sin(Time.time * 6f + item.GetInstanceID()));
                _sparkle.normal.textColor = lost.IsEvidence ? new Color(1f, 0.5f, 0.4f, a) : new Color(1f, 0.95f, 0.5f, a);
                GUI.Label(new Rect(s.x - 40f, Screen.height - s.y - 14f, 80f, 28f), "✦", _sparkle);
            }
        }

        private void DrawDialogue()
        {
            DialogueLine? line = DialogueService.Current;
            if (!line.HasValue) return;
            DialogueLine l = line.Value;
            float w = Mathf.Min(900f, Screen.width - 80f);
            var box = new Rect((Screen.width - w) * 0.5f, Screen.height - 250f, w, 92f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, _dot);
            GUI.color = Color.white;
            string speaker = l.Speaker == DialogueService.PlayerSpeaker ? "You" : l.Speaker;
            _speaker.normal.textColor = l.Color;
            GUI.Label(new Rect(box.x + 16f, box.y + 8f, w - 32f, 24f), speaker, _speaker);
            // Typewriter.
            int shown = Mathf.Clamp(Mathf.CeilToInt((Time.time - l.Start) * 55f), 0, l.Text.Length);
            GUI.Label(new Rect(box.x + 16f, box.y + 32f, w - 32f, 58f), l.Text.Substring(0, shown), _line);
        }

        private void DrawTitle()
        {
            float t = Time.unscaledTime - _titleAt;
            if (_title == null || t > 4.5f) return;
            float a = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((4.5f - t) / 0.8f);
            _titleStyle.normal.textColor = new Color(1f, 0.92f, 0.6f, a);
            _subStyle.normal.textColor = new Color(1f, 1f, 1f, a);
            GUI.color = new Color(0f, 0f, 0f, 0.4f * a);
            GUI.DrawTexture(new Rect(0f, Screen.height * 0.3f, Screen.width, 120f), _dot);
            GUI.color = Color.white;
            GUI.Label(new Rect(0f, Screen.height * 0.3f + 8f, Screen.width, 70f), _title, _titleStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.3f + 74f, Screen.width, 36f), _subtitle, _subStyle);
        }

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
            _chapter ??= new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, normal = { textColor = new Color(1f, 0.85f, 0.5f) } };
            _objective ??= new GUIStyle(GUI.skin.label) { fontSize = 19, richText = true, wordWrap = true, normal = { textColor = Color.white } };
            _money ??= new GUIStyle(GUI.skin.label) { fontSize = 20, richText = true, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _marker ??= new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
            _speaker ??= new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, richText = true };
            _line ??= new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, richText = false, normal = { textColor = Color.white } };
            _titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _subStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            _sparkle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        }
    }
}
