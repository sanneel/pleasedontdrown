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

        // Drawn on the HUD canvas (no IMGUI): see HudCanvas.
        private void OnEnable() => HudCanvas.Add(this, HudCanvas.OrderStory, DrawHud);

        private void OnDisable() => HudCanvas.Remove(this);

        private void DrawHud()
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
            DrawReport();
        }

        private void DrawObjective()
        {
            StoryDirector story = StoryDirector.Instance;
            if (story != null && !string.IsNullOrEmpty(story.Objective))
            {
                if (story.Objective != _objective || story.Progress != _objectiveProgress || story.Goal != _objectiveGoal)
                {
                    _objective = story.Objective;
                    _objectiveProgress = story.Progress;
                    _objectiveGoal = story.Goal;
                    _objectiveLine = _objective;
                }
                const float width = 440f;
                float body = Hud.TextHeight(_objectiveLine, width - 44f, 22f);
                var card = new Rect(Hud.Margin, Hud.Margin, width, 56f + body + (story.Goal > 0 ? 30f : 0f));
                Hud.Surface(card, 0.86f, Hud.Teal);
                Hud.FitLabel(new Rect(card.x + 22f, card.y + 12f, width - 44f, 24f), story.Chapter, 17f, Hud.Teal, heavy: true);
                Hud.Label(new Rect(card.x + 22f, card.y + 42f, width - 44f, body), _objectiveLine, 22f, Hud.Paper, TextAnchor.UpperLeft, wrap: true, shadow: false);
                if (story.Goal > 0)
                {
                    Hud.Bar(new Rect(card.x + 22f, card.yMax - 20f, width - 116f, 7f), (float)story.Progress / story.Goal, Hud.Teal);
                    Hud.Label(new Rect(card.xMax - 86f, card.yMax - 32f, 64f, 27f), $"{story.Progress} / {story.Goal}", 17f, Hud.Sand, TextAnchor.MiddleRight, shadow: false);
                }
            }
            if (Economy.Instance != null)
            {
                // Wallet sits above the vitals as a compact, readable status chip.
                if (Economy.Money != _money || !Mathf.Approximately(Hud.Scale, _moneyScale))
                {
                    _money = Economy.Money;
                    _moneyScale = Hud.Scale;
                    _moneyText = "$" + _money.ToString("N0");
                    _moneyWidth = Mathf.Max(156f, Hud.TextWidth(_moneyText, 28f, true) + 40f);
                }
                var pill = new Rect(Hud.Margin, Hud.Height - 25f - 60f - 16f - 70f, _moneyWidth, 70f);
                Hud.Surface(pill, 0.9f);
                Hud.Label(new Rect(pill.x + 18f, pill.y + 10f, pill.width - 36f, 18f), "TEAM FUNDS", 14f, Hud.Muted, TextAnchor.MiddleLeft, shadow: false);
                Hud.Label(new Rect(pill.x + 18f, pill.y + 29f, pill.width - 36f, 33f), _moneyText, 28f, Hud.Sand, TextAnchor.MiddleLeft, heavy: true, shadow: false);
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
            Hud.Fill(new Rect(sx - size * 0.5f, sy - size * 0.5f, size, size), Hud.Sand, size * 0.5f);
            bool below = sy < 110f;
            Hud.Label(new Rect(sx - 250f, below ? sy + 18f : sy - 74f, 500f, 56f), $"<b>{story.MarkerLabel}</b>\n{distance:F0} m", 21f,
                Hud.Sand, TextAnchor.UpperCenter);
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
            float w = Mathf.Min(880f, Hud.Width - 160f);
            // Clear of the hotbar and the name of what you hold.
            float body = Mathf.Max(26f, Hud.TextHeight(l.Text, w, 21f, richText: false));
            float height = body + 26f;
            var box = new Rect((Hud.Width - w) * 0.5f, Hud.HotbarTop - 104f - height, w, height);
            string speaker = l.Speaker == DialogueService.PlayerSpeaker ? "You" : l.Speaker;
            Hud.Label(new Rect(box.x, box.y, w, 22f), speaker, 16f, Color.Lerp(l.Color, Hud.Paper, 0.4f), heavy: true);
            // Typewriter.
            int shown = Mathf.Clamp(Mathf.CeilToInt((Time.time - l.Start) * 55f), 0, l.Text.Length);
            if (!ReferenceEquals(l.Text, _spoken) || shown != _spokenLength)
            {
                _spoken = l.Text;
                _spokenLength = shown;
                _spokenShown = shown >= l.Text.Length ? l.Text : l.Text.Substring(0, shown);
            }
            Hud.Label(new Rect(box.x, box.y + 26f, w, body + 2f), _spokenShown, 21f, Hud.Paper, TextAnchor.UpperCenter, wrap: true, richText: false);
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
            Hud.Panel(panel, 0.94f * a);
            Hud.FitLabel(new Rect(panel.x + 36f, panel.y + 22f, width - 72f, 64f), _reportTitle, 38f, new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, a), TextAnchor.MiddleCenter, heavy: true);
            float y = panel.y + 110f;
            for (int i = 0; i < ReportLabels.Length; i++)
            {
                // Each line comes in a moment after the one above.
                float shown = Mathf.Clamp01((t - 0.5f - i * 0.35f) / 0.3f) * a;
                bool bad = i == 1 && _reportValues[1] != "0";
                Color value = bad ? Hud.Danger : i >= 3 ? Hud.Teal : Hud.Paper;
                value.a = shown;
                Hud.Fill(new Rect(panel.x + 36f, y + row - 1f, width - 72f, 1f), new Color(1f, 1f, 1f, 0.08f * shown));
                Hud.Label(new Rect(panel.x + 36f, y, width - 190f, row), ReportLabels[i], 22f, new Color(Hud.Muted.r, Hud.Muted.g, Hud.Muted.b, shown), TextAnchor.MiddleLeft, shadow: false);
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
            Hud.Fill(new Rect(0f, y, Hud.Width, 176f), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.8f * a));
            Hud.Fill(new Rect(Hud.Width * 0.5f - 110f, y + 5f, 220f, 4f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, a), 2f);
            Hud.FitLabel(new Rect(80f, y + 14f, Hud.Width - 160f, 96f), _title, 68f, new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, a), TextAnchor.MiddleCenter, heavy: true);
            Hud.Label(new Rect(80f, y + 110f, Hud.Width - 160f, 60f), _subtitle, 25f, new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, a), wrap: true, shadow: false);
        }
    }
}
