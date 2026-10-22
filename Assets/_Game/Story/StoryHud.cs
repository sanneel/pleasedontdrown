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
        private static bool _titleBoss;
        // A boss's name flies up into its bar from 3.4 s, lands at 4.1 s, and the bar takes it over at 4.5 s.
        private const float TitleSeconds = 4.5f, BossFlyFrom = 3.4f, BossFlyTo = 4.1f;

        /// <summary>
        /// Seconds since a boss's name came up as a title card, while it's up (-1 otherwise). It flies up into the boss
        /// bar at the end, so the bar holds back its own name meanwhile (it showed twice).
        /// </summary>
        public static float BossIntroAge
        {
            get
            {
                float t = Time.unscaledTime - _titleAt;
                return _titleBoss && _title != null && t <= TitleSeconds ? t : -1f;
            }
        }

        /// <summary>Where the boss bar's name sits (ThreatHud.BossCard): the title card flies there.</summary>
        public static Rect BossNameRect => new((Hud.Width - 640f) * 0.5f, Hud.BossTop + 2f, 640f, 48f);
        public const float BossNameSize = 40f;

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
            _titleBoss = false;
            _reportTitle = null;
            _reportAt = -100f;
        }

        /// <summary>Where the top left's column (objective, gala fund) ends, in sheet units: the IN TROUBLE list goes under it.</summary>
        public static float TopLeftBottom { get; private set; } = Hud.Margin;

        /// <summary>How long a chapter's report card stays up.</summary>
        public const float ReportSeconds = 9f;

        private static string _reportTitle;
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

        /// <summary>
        /// How much a card is up now, 0 to 1: the report (whole screen) or the chapter title (the top half). Speech
        /// bubbles step aside meanwhile, so nothing floats over the card.
        /// </summary>
        public static float CardCover(float viewportY)
        {
            float report = Time.unscaledTime - _reportAt, title = Time.unscaledTime - _titleAt;
            float cover = 0f;
            if (_reportTitle != null && report <= ReportSeconds) cover = Mathf.Clamp01(report / 0.3f) * Mathf.Clamp01((ReportSeconds - report) / 0.8f);
            if (_title != null && title <= TitleSeconds && viewportY > 0.45f) cover = Mathf.Max(cover, Mathf.Clamp01(title / 0.3f) * Mathf.Clamp01((TitleSeconds - title) / 0.8f));
            return cover;
        }

        /// <summary>
        /// A big centred card for a few seconds ("CHAPTER 2"). <paramref name="boss"/>: a boss's name, which ends by
        /// flying up into the boss bar.
        /// </summary>
        public static void ShowTitle(string title, string subtitle, bool boss = false)
        {
            _title = title;
            _subtitle = subtitle;
            _titleAt = Time.unscaledTime;
            _titleBoss = boss;
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
            if (cam != null) DrawMarker(cam, local);
            DrawReport(); // (under the subtitles: its dim mustn't darken what's being said)
            DrawDialogue();
            DrawTitle();
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
            TopLeftBottom = below;
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

        // The name plate: who's talking (worked out again only when the speaker changes), and when they came in.
        private string _plateSpeaker, _plateName, _plateRole, _plateInitial;
        private float _plateAt = -10f;

        /// <summary>
        /// The line being said, above the hotbar: a name plate (a live picture of the speaker's face in a ring of their
        /// colour that beats while they talk, or their initial for a voice with no body; their name big and what they
        /// do beside it) and the words typed out under the name. No box behind it: the words are outlined.
        /// </summary>
        private void DrawDialogue()
        {
            DialogueLine? line = DialogueService.Current;
            if (!line.HasValue) return;
            DialogueLine l = line.Value;
            bool you = l.Speaker == DialogueService.PlayerSpeaker;
            if (l.Speaker != _plateSpeaker)
            {
                _plateSpeaker = l.Speaker;
                _plateAt = Time.unscaledTime;
                string speaker = you ? "You" : string.IsNullOrEmpty(l.Speaker) ? "?" : l.Speaker;
                _plateName = speaker.ToUpperInvariant();
                _plateInitial = _plateName.Substring(0, 1);
                _plateRole = you || l.Npc == null ? null : RoleOf(l.Npc.Role);
            }

            const float portrait = 96f, gap = 22f;
            float w = Mathf.Min(860f, Hud.Width - 400f);
            float body = Mathf.Max(30f, Hud.TextHeight(l.Text, w, 25f, richText: false));
            float height = Mathf.Max(portrait, 48f + body);
            float age = Time.time - l.Start;
            float alpha = Mathf.Clamp01(age / 0.2f) * Mathf.Clamp01((l.Duration + 0.3f - age) / 0.3f);
            float rise = (1f - Mathf.SmoothStep(0f, 1f, age / 0.25f)) * 12f;
            float x = (Hud.Width - portrait - gap - w) * 0.5f, top = Hud.HotbarTop - 100f - height + rise;

            // The portrait pops in when someone new speaks; its ring beats while their line is being said.
            Color tone = l.Color;
            tone.a = alpha;
            float pop = Hud.EaseOutBack(Mathf.Clamp01((Time.unscaledTime - _plateAt) / 0.35f));
            float size = portrait * Mathf.Lerp(0.55f, 1f, pop);
            var disc = new Rect(x + (portrait - size) * 0.5f, top + (portrait - size) * 0.5f, size, size);
            Hud.Fill(new Rect(disc.x - 5f, disc.y - 5f, disc.width + 10f, disc.height + 10f), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.55f * alpha), size * 0.5f + 5f);
            Texture face = UI.SpeakerPortrait.Of(l.Npc);
            if (face != null) Hud.RoundPicture(disc, face, new Color(1f, 1f, 1f, alpha));
            else
            {
                Hud.Fill(disc, tone, size * 0.5f);
                Hud.Title(disc, _plateInitial, size * 0.56f, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, alpha));
            }
            float beat = age < l.Duration ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 13f) : 0f;
            Hud.Arc(new Rect(disc.x - 4f, disc.y - 4f, disc.width + 8f, disc.height + 8f), 0f, 1f, 0.045f + 0.03f * beat, tone);

            // The name in the title face (their colour, lightened), what they do beside it, the words under it.
            float tx = x + portrait + gap;
            Color name = Color.Lerp(l.Color, Hud.Paper, 0.3f);
            name.a = alpha;
            Hud.Title(new Rect(tx, top - 2f, w, 44f), _plateName, 34f, name, TextAnchor.MiddleLeft);
            if (_plateRole != null)
            {
                float nameWidth = Hud.TitleWidth(_plateName, 34f);
                Hud.Label(new Rect(tx + nameWidth + 16f, top + 8f, w - nameWidth - 16f, 26f), _plateRole, 16f,
                    new Color(Hud.Muted.r, Hud.Muted.g, Hud.Muted.b, alpha), TextAnchor.MiddleLeft, heavy: true);
            }
            // Typewriter.
            int shown = Mathf.Clamp(Mathf.CeilToInt((Time.time - l.Start) * 55f), 0, l.Text.Length);
            if (!ReferenceEquals(l.Text, _spoken) || shown != _spokenLength)
            {
                _spoken = l.Text;
                _spokenLength = shown;
                _spokenShown = shown >= l.Text.Length ? l.Text : l.Text.Substring(0, shown);
            }
            Hud.Label(new Rect(tx, top + 46f, w, body + 2f), _spokenShown, 25f, new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, alpha), TextAnchor.UpperLeft, wrap: true, richText: false);
        }

        /// <summary>What a speaker does, small beside their name.</summary>
        private static string RoleOf(NpcRole role) => role switch
        {
            NpcRole.Guide => "LOST & FOUND",
            NpcRole.Receptionist => "HOTEL RECEPTION",
            NpcRole.Robber => "ROBBER",
            NpcRole.Pirate => "PIRATE",
            NpcRole.Guest => "HOTEL GUEST",
            NpcRole.Police => "POLICE",
            _ => "TOURIST"
        };

        private static readonly string[] TileLabels = { "RESCUED", "LOST TO RIVALS", "THINGS RETURNED", "EARNED" };

        /// <summary>
        /// The chapter's report card in the middle over a dimmed game: the title, the chapter's numbers as tiles (the
        /// same tiles as the pause screen's log), each popping in and counting up in turn, and the team's wallet under
        /// them. The lost count is red when there are any.
        /// </summary>
        private void DrawReport()
        {
            float t = Time.unscaledTime - _reportAt;
            if (_reportTitle == null || t > ReportSeconds) return;
            float a = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((ReportSeconds - t) / 0.8f);
            const float tile = 176f, gap = 16f, tall = 116f, width = 4f * tile + 3f * gap;
            const float height = 104f + tall + 120f;
            var panel = new Rect((Hud.Width - width) * 0.5f, (Hud.Height - height) * 0.5f - 40f, width, height);
            // The game dims a little behind it while it's up (a screen dim, not a box), so it reads on any view.
            Hud.Fill(new Rect(0f, 0f, Hud.Width, Hud.Height), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.42f * a));
            float grow = Hud.EaseOutBack(Mathf.Clamp01(t / 0.45f));
            Hud.FitTitle(new Rect(panel.x - 40f, panel.y + 6f, width + 80f, 80f), _reportTitle, 60f * Mathf.Lerp(0.85f, 1f, grow), new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, a));
            Hud.Fill(new Rect(panel.center.x - 140f * grow, panel.y + 90f, 280f * grow, 4f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, a), 2f);

            float y = panel.y + 116f;
            for (int i = 0; i < 4; i++)
            {
                // Each tile pops in a moment after the one before, its number counting up.
                float from = 0.5f + i * 0.3f;
                float shown = Mathf.Clamp01((t - from) / 0.25f) * a;
                float pop = Mathf.Lerp(0.82f, 1f, Hud.EaseOutBack(Mathf.Clamp01((t - from) / 0.35f)));
                int n = Mathf.RoundToInt(_reportNumbers[i] * Mathf.SmoothStep(0f, 1f, (t - from) / 0.7f));
                _reportValues[i] = i == 3 ? "$" + n.ToString("N0") : n.ToString();
                Color color = i == 1 && _reportNumbers[1] != 0 ? Hud.Danger : i == 3 ? Hud.Teal : Hud.Paper;
                var r = new Rect(panel.x + i * (tile + gap), y, tile, tall);
                r = new Rect(r.center.x - r.width * pop * 0.5f, r.center.y - r.height * pop * 0.5f, r.width * pop, r.height * pop);
                Hud.StatTile(r, _reportValues[i], TileLabels[i], color, shown);
            }

            // The wallet under the tiles, last.
            float walletFrom = 0.5f + 4 * 0.3f;
            float wallet = Mathf.Clamp01((t - walletFrom) / 0.3f) * a;
            int money = Mathf.RoundToInt(_reportNumbers[4] * Mathf.SmoothStep(0f, 1f, (t - walletFrom) / 0.7f));
            _reportValues[4] = "$" + money.ToString("N0");
            Hud.Label(new Rect(panel.x, y + tall + 22f, width, 24f), "IN THE TEAM'S WALLET", 17f, new Color(Hud.Muted.r, Hud.Muted.g, Hud.Muted.b, wallet), TextAnchor.MiddleCenter, heavy: true);
            Hud.Title(new Rect(panel.x, y + tall + 46f, width, 60f), _reportValues[4], 50f, new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, wallet));
        }

        private void DrawTitle()
        {
            float t = Time.unscaledTime - _titleAt;
            if (_title == null || t > TitleSeconds) return;
            float a = Mathf.Clamp01(t / 0.4f) * Mathf.Clamp01((TitleSeconds - t) / 0.8f);
            float y = Hud.Height * 0.24f;
            // (No dark band behind it: text sits straight on the game, outlined. The user, 2026-10-13 and again 2026-10-17.)
            // The words grow in, the teal line opens out under them, the subtitle follows.
            float grow = Hud.EaseOutBack(Mathf.Clamp01(t / 0.5f)), line = Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.5f);
            var words = new Rect(80f, y, Hud.Width - 160f, 130f);
            float size = 112f * Mathf.Lerp(0.8f, 1f, grow), wordsAlpha = a;
            Color tint = Hud.Sand;
            if (_titleBoss)
            {
                // A boss's name doesn't fade: it shrinks and flies up to the top, where the boss bar takes it over.
                float fly = Mathf.SmoothStep(0f, 1f, (t - BossFlyFrom) / (BossFlyTo - BossFlyFrom));
                Rect to = BossNameRect;
                words = new Rect(Mathf.Lerp(words.x, to.x, fly), Mathf.Lerp(words.center.y, to.center.y, fly), Mathf.Lerp(words.width, to.width, fly), Mathf.Lerp(words.height, to.height, fly));
                words.y -= words.height * 0.5f;
                size = Mathf.Lerp(size, BossNameSize, fly);
                tint = Color.Lerp(Hud.Sand, Hud.Paper, fly); // (the bar's name is white: no jump when it takes over)
                wordsAlpha = Mathf.Clamp01(t / 0.4f);
                a *= 1f - Mathf.SmoothStep(0f, 1f, (t - (BossFlyFrom - 0.4f)) / 0.4f); // the line and the tip go first
            }
            Hud.FitTitle(words, _title, size, new Color(tint.r, tint.g, tint.b, wordsAlpha));
            Hud.Fill(new Rect(Hud.Width * 0.5f - 160f * line, y + 134f, 320f * line, 5f), new Color(Hud.Teal.r, Hud.Teal.g, Hud.Teal.b, a), 2.5f);
            float sub = Mathf.Clamp01((t - 0.35f) / 0.4f) * a;
            Hud.Label(new Rect(80f, y + 148f, Hud.Width - 160f, 60f), _subtitle, 32f, new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, sub), wrap: true, heavy: true);
        }
    }
}
