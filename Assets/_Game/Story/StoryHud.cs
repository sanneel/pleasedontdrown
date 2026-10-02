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

        // Lines of text that only change when what they show does (not built again every frame).
        private string _objective, _objectiveLine;
        private int _objectiveProgress = -1, _objectiveGoal = -1;
        private int _money = int.MinValue;
        private string _moneyText;
        private float _moneyWidth, _moneyScale;
        private string _spoken, _spokenShown;
        private int _spokenLength = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _title = _subtitle = null;
            _titleAt = -100f;
            _reportTitle = null;
            _reportAt = -100f;
        }

        /// <summary>How long a chapter's report card stays up.</summary>
        public const float ReportSeconds = 9f;

        private static string _reportTitle;
        private static readonly string[] ReportLabels = { "Tourists rescued", "Lost to the rival company", "Lost things returned", "Earned", "In the team's wallet" };
        private static readonly string[] _reportValues = new string[5];
        private static float _reportAt = -100f;

        /// <summary>The end-of-chapter report card: what the team did, in the middle of the screen for a while.</summary>
        public static void ShowReport(string title, int rescued, int lost, int returned, int earned, int money)
        {
            _reportTitle = title;
            _reportValues[0] = rescued.ToString();
            _reportValues[1] = lost.ToString();
            _reportValues[2] = returned.ToString();
            _reportValues[3] = "$" + earned;
            _reportValues[4] = "$" + money;
            _reportAt = Time.unscaledTime;
            Audio.GameMusic.Play(Audio.Jingle.Chapter);
        }

        /// <summary>A big centred card for a few seconds ("CHAPTER 2").</summary>
        public static void ShowTitle(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
            _titleAt = Time.unscaledTime;
        }

        private void Awake() => useGUILayout = false; // drawn with fixed boxes: no layout pass needed

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return; // nothing here takes input
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
            DrawReport();
        }

        private void DrawObjective()
        {
            StoryDirector story = StoryDirector.Instance;
            if (story != null && !string.IsNullOrEmpty(story.Objective))
            {
                Hud.Label(new Rect(30f, 22f, 900f, 28f), story.Chapter, 20f, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleLeft, heavy: true);
                if (story.Objective != _objective || story.Progress != _objectiveProgress || story.Goal != _objectiveGoal)
                {
                    _objective = story.Objective;
                    _objectiveProgress = story.Progress;
                    _objectiveGoal = story.Goal;
                    _objectiveLine = story.Goal > 0 ? $"{_objective}  <color=#ffd24a>{story.Progress}/{story.Goal}</color>" : _objective;
                }
                Hud.Label(new Rect(30f, 52f, 820f, 80f), _objectiveLine, 27f, Color.white, TextAnchor.UpperLeft, wrap: true);
            }
            if (Economy.Instance != null)
            {
                // Above the vitals squares.
                if (Economy.Money != _money || !Mathf.Approximately(Hud.Scale, _moneyScale))
                {
                    _money = Economy.Money;
                    _moneyScale = Hud.Scale;
                    _moneyText = "$" + _money;
                    _moneyWidth = Hud.TextWidth(_moneyText, 30f, true) + 36f;
                }
                var pill = new Rect(50f, Hud.Height - 25f - 64f - 14f - 46f, _moneyWidth, 46f);
                Hud.Fill(pill, new Color(0f, 0f, 0f, 0.5f), 12f);
                Hud.Label(pill, _moneyText, 30f, new Color(0.56f, 1f, 0.56f), heavy: true, shadow: false);
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
            var items = Item.All;
            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
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
            if (!ReferenceEquals(l.Text, _spoken) || shown != _spokenLength)
            {
                _spoken = l.Text;
                _spokenLength = shown;
                _spokenShown = shown >= l.Text.Length ? l.Text : l.Text.Substring(0, shown);
            }
            GUIStyle style = Hud.Style(25f, TextAnchor.UpperLeft, wrap: true);
            style.richText = false; // spoken lines are shown as written
            Hud.Label(new Rect(box.x + 24f, box.y + 44f, w - 48f, 76f), _spokenShown, 25f, Color.white, TextAnchor.UpperLeft, wrap: true, shadow: false);
            style.richText = true;
        }

        /// <summary>The chapter's report card: a dark panel, the title, one line per number (the lost count in red if any).</summary>
        private void DrawReport()
        {
            float t = Time.unscaledTime - _reportAt;
            if (_reportTitle == null || t > ReportSeconds) return;
            float a = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((ReportSeconds - t) / 0.8f);
            const float width = 640f, row = 46f;
            float height = 120f + ReportLabels.Length * row + 26f;
            var panel = new Rect((Hud.Width - width) * 0.5f, (Hud.Height - height) * 0.5f - 40f, width, height);
            Hud.Fill(panel, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.88f * a), 20f);
            Hud.Label(new Rect(panel.x, panel.y + 22f, width, 64f), _reportTitle, 46f, new Color(1f, 0.92f, 0.6f, a), heavy: true, shadow: false);
            float y = panel.y + 110f;
            for (int i = 0; i < ReportLabels.Length; i++)
            {
                // Each line comes in a moment after the one above.
                float shown = Mathf.Clamp01((t - 0.5f - i * 0.35f) / 0.3f) * a;
                bool bad = i == 1 && _reportValues[1] != "0";
                Color value = bad ? new Color(1f, 0.5f, 0.4f, shown) : i >= 3 ? new Color(0.56f, 1f, 0.56f, shown) : new Color(1f, 1f, 1f, shown);
                Hud.Label(new Rect(panel.x + 48f, y, width - 96f, row), ReportLabels[i], 26f, new Color(1f, 1f, 1f, 0.85f * shown), TextAnchor.MiddleLeft, shadow: false);
                Hud.Label(new Rect(panel.x + 48f, y, width - 96f, row), _reportValues[i], 30f, value, TextAnchor.MiddleRight, heavy: true, shadow: false);
                y += row;
            }
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
