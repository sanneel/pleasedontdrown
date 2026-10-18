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
        // Motion: when the objective last changed (it slides in and flashes), the wallet's last change (it pops, the
        // difference rises beside it).
        private float _objectiveAt = -100f, _moneyAt = -100f;
        private int _moneyDelta;

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
        private static readonly int[] _reportNumbers = new int[5];
        private static float _reportAt = -100f;

        /// <summary>The end-of-chapter report card: what the team did, in the middle of the screen for a while.</summary>
        public static void ShowReport(string title, int rescued, int lost, int returned, int earned, int money)
        {
            _reportTitle = title;
            _reportNumbers[0] = rescued;
            _reportNumbers[1] = lost;
            _reportNumbers[2] = returned;
            _reportNumbers[3] = earned;
            _reportNumbers[4] = money;
            for (int i = 0; i < 5; i++) _reportValues[i] = null;
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
            float below = Hud.Margin; // where the next thing in the top left can go
            if (story != null && !string.IsNullOrEmpty(story.Objective))
            {
                if (story.Objective != _objective || story.Progress != _objectiveProgress || story.Goal != _objectiveGoal)
                {
                    if (story.Objective != _objective) _objectiveAt = Time.unscaledTime; // a new one: it slides in
                    _objective = story.Objective;
                    _objectiveProgress = story.Progress;
                    _objectiveGoal = story.Goal;
                    _objectiveLine = _objective;
                }
                // A new objective slides in from the left and its words flash sand for a moment before settling white;
                // a thin teal line down the left edge holds the card together (no box behind the words).
                float since = Time.unscaledTime - _objectiveAt;
                float enter = Mathf.SmoothStep(0f, 1f, since / 0.35f), flash = 1f - Mathf.SmoothStep(0f, 1f, (since - 0.6f) / 0.8f);
                const float width = 460f;
                float body = Hud.TextHeight(_objectiveLine, width - 44f, 24f);
                var card = new Rect(Hud.Margin - (1f - enter) * 40f, Hud.Margin, width, 58f + body + (story.Goal > 0 ? 30f : 0f));
                Hud.Fill(new Rect(card.x + 4f, card.y + 12f, 4f, card.height - 22f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, 0.9f * enter), 2f);
                Hud.FitLabel(new Rect(card.x + 22f, card.y + 10f, width - 44f, 26f), story.Chapter, 18f, new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, enter), heavy: true);
                Color words = Color.Lerp(Hud.Paper, Hud.Sand, flash);
                words.a = enter;
                Hud.Label(new Rect(card.x + 22f, card.y + 40f, width - 44f, body + 4f), _objectiveLine, 24f, words, TextAnchor.UpperLeft, wrap: true, heavy: flash > 0.5f, shadow: false);
                if (story.Goal > 0)
                {
                    Hud.Bar(new Rect(card.x + 22f, card.yMax - 20f, width - 116f, 7f), (float)story.Progress / story.Goal, Hud.Teal);
                    Hud.Label(new Rect(card.xMax - 86f, card.yMax - 32f, 64f, 27f), $"{story.Progress} / {story.Goal}", 18f, Hud.Sand, TextAnchor.MiddleRight, heavy: true, shadow: false);
                }
                below = card.yMax + 6f;
            }
            if (story != null && story.GalaGoal > 0) DrawGalaBar(story, below);
            if (Economy.Instance != null)
            {
                // Wallet sits above the vitals as a compact, readable status chip.
                if (Economy.Money != _money || !Mathf.Approximately(Hud.Scale, _moneyScale))
                {
                    if (_money != int.MinValue && Economy.Money != _money)
                    {
                        _moneyDelta = Economy.Money - _money;
                        _moneyAt = Time.unscaledTime;
                    }
                    _money = Economy.Money;
                    _moneyScale = Hud.Scale;
                    _moneyText = "$" + _money.ToString("N0");
                    _moneyWidth = Mathf.Max(156f, Hud.TextWidth(_moneyText, 30f, true) + 40f);
                }
                var pill = new Rect(Hud.Margin, Hud.Height - 25f - 60f - 16f - 70f, _moneyWidth, 70f);
                Hud.Label(new Rect(pill.x + 4f, pill.y + 8f, pill.width - 8f, 18f), "TEAM FUNDS", 15f, Hud.Muted, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                // The amount pops a moment when it changes (green up, coral down) and the difference rises beside it.
                float m = Time.unscaledTime - _moneyAt;
                float pop = m < 0.5f ? 1f + 0.25f * Mathf.Sin(Mathf.Clamp01(m / 0.5f) * Mathf.PI) : 1f;
                Color amount = m < 0.9f ? Color.Lerp(_moneyDelta >= 0 ? new Color(0.55f, 1f, 0.6f) : Hud.Coral, Hud.Sand, m / 0.9f) : Hud.Sand;
                Hud.Label(new Rect(pill.x + 4f, pill.y + 27f - (pop - 1f) * 18f, pill.width * pop, 36f * pop), _moneyText, 30f * pop, amount, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                if (m < 1.6f)
                {
                    float rise = Mathf.SmoothStep(0f, 1f, m / 1.6f), alpha = Mathf.Clamp01(m / 0.15f) * (1f - rise);
                    string delta = (_moneyDelta >= 0 ? "+$" : "-$") + Mathf.Abs(_moneyDelta).ToString("N0");
                    Color c = _moneyDelta >= 0 ? new Color(0.55f, 1f, 0.6f, alpha) : new Color(Hud.Coral.r, Hud.Coral.g, Hud.Coral.b, alpha);
                    Hud.Label(new Rect(pill.x + _moneyWidth - 20f, pill.y + 24f - rise * 34f, 200f, 32f), delta, 24f, c, TextAnchor.MiddleLeft, heavy: true);
                }
            }
        }

        /// <summary>
        /// The gala fund (chapter 2): a plain bar under the objective that fills with the team's money toward the gala
        /// ($1,000 per lifeguard). No numbers and no percent on purpose (Shako, Docs/16); it glows gold once full.
        /// </summary>
        private static void DrawGalaBar(StoryDirector story, float top)
        {
            const float width = 300f;
            float fill = story.GalaFill01;
            Hud.Label(new Rect(Hud.Margin + 2f, top, width, 20f), "GALA FUND", 15f, Hud.Sand, TextAnchor.MiddleLeft, heavy: true);
            var bar = new Rect(Hud.Margin, top + 24f, width, 12f);
            if (fill >= 1f)
            {
                float glow = 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f);
                Hud.Fill(new Rect(bar.x - 3f, bar.y - 3f, bar.width + 6f, bar.height + 6f), new Color(1f, 0.82f, 0.35f, glow), 9f);
            }
            Hud.Bar(bar, fill, new Color(1f, 0.8f, 0.32f));
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
            float w = Mathf.Min(900f, Hud.Width - 160f);
            // Clear of the hotbar and the name of what you hold; it fades in and out.
            float body = Mathf.Max(30f, Hud.TextHeight(l.Text, w, 24f, richText: false));
            float height = body + 30f;
            var box = new Rect((Hud.Width - w) * 0.5f, Hud.HotbarTop - 104f - height, w, height);
            float age = Time.time - l.Start;
            float alpha = Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01((l.Duration + 0.3f - age) / 0.3f);
            string speaker = l.Speaker == DialogueService.PlayerSpeaker ? "You" : l.Speaker;
            Color name = Color.Lerp(l.Color, Hud.Paper, 0.35f);
            name.a = alpha;
            Hud.Label(new Rect(box.x, box.y, w, 24f), speaker, 18f, name, heavy: true);
            // Typewriter.
            int shown = Mathf.Clamp(Mathf.CeilToInt((Time.time - l.Start) * 55f), 0, l.Text.Length);
            if (!ReferenceEquals(l.Text, _spoken) || shown != _spokenLength)
            {
                _spoken = l.Text;
                _spokenLength = shown;
                _spokenShown = shown >= l.Text.Length ? l.Text : l.Text.Substring(0, shown);
            }
            Hud.Label(new Rect(box.x, box.y + 28f, w, body + 2f), _spokenShown, 24f, new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, alpha), TextAnchor.UpperCenter, wrap: true, richText: false);
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
            // (No Hud.Panel: its little teal accent floated alone over the title; the line under the title is enough.)
            float grow = Hud.EaseOutBack(Mathf.Clamp01(t / 0.45f));
            Hud.FitLabel(new Rect(panel.x + 20f, panel.y + 18f, width - 40f, 70f), _reportTitle, 48f * Mathf.Lerp(0.85f, 1f, grow), new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, a), TextAnchor.MiddleCenter, heavy: true);
            Hud.Fill(new Rect(panel.center.x - 120f * grow, panel.y + 92f, 240f * grow, 4f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, a), 2f);
            float y = panel.y + 110f;
            for (int i = 0; i < ReportLabels.Length; i++)
            {
                // Each line comes in a moment after the one above, its number counting up.
                float shown = Mathf.Clamp01((t - 0.5f - i * 0.35f) / 0.3f) * a;
                float count = Mathf.SmoothStep(0f, 1f, (t - 0.5f - i * 0.35f) / 0.7f);
                int n = Mathf.RoundToInt(_reportNumbers[i] * count);
                _reportValues[i] = i >= 3 ? "$" + n.ToString("N0") : n.ToString();
                bool bad = i == 1 && _reportNumbers[1] != 0;
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
            float y = Hud.Height * 0.24f;
            // (No dark band behind it: text sits straight on the game, outlined. The user, 2026-10-13 and again 2026-10-17.)
            // The words grow in, the teal line opens out under them, the subtitle follows.
            float grow = Hud.EaseOutBack(Mathf.Clamp01(t / 0.5f)), line = Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.5f);
            Hud.FitLabel(new Rect(80f, y, Hud.Width - 160f, 130f), _title, 104f * Mathf.Lerp(0.8f, 1f, grow), new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, a), TextAnchor.MiddleCenter, heavy: true);
            Hud.Fill(new Rect(Hud.Width * 0.5f - 160f * line, y + 134f, 320f * line, 5f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, a), 2.5f);
            float sub = Mathf.Clamp01((t - 0.35f) / 0.4f) * a;
            Hud.Label(new Rect(80f, y + 148f, Hud.Width - 160f, 60f), _subtitle, 32f, new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, sub), wrap: true, heavy: true);
        }
    }
}
