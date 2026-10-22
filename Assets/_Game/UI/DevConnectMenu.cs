using PleaseDontDrown.Core;
using PleaseDontDrown.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Start screen, pause menu, and quick in-game guide, laid out like How to Fish's: on the start screen the big
    /// title sits top left over the island and a column of wide buttons bottom left, a page (options, join) opening
    /// beside them; the pause menu is the same buttons down the left with the lifeguard's log (or options) beside.
    /// </summary>
    public class DevConnectMenu : MonoBehaviour
    {
        private enum Page { Main, Join, Options, Invite, Travel, ConfirmLeave, ConfirmQuit }

        [SerializeField] private ConnectionService _connection;

        private const float ButtonWidth = 320f, ButtonHeight = 60f, ButtonGap = 10f;

        private bool _pauseOpen;
        private bool _guideOpen;
        private Page _page;
        private string _lanAddress = "localhost";
        private GUIStyle _field;
        private int _fieldPx;
        private Texture2D _fade;
        private static DevConnectMenu _instance;
        public static bool GuideOpen => _instance != null && _instance._guideOpen;

        private void Awake() { useGUILayout = false; _instance = this; }

        private void OnEnable()
        {
            GameInput.ToggleMenu.performed += OnToggleMenu;
            GameInput.ToggleOverlay.performed += OnToggleOverlay;
            DevCommands.Register("menu", "<pause|options|sound|keys|guide|close>", "Open a menu page (to look at it in tests).", args =>
            {
                string page = args.Length > 0 ? args[0] : "pause";
                _guideOpen = page == "guide";
                bool options = page is "options" or "sound" or "keys";
                if (_connection.IsActive) SetPause(page == "pause" || options);
                _page = options ? Page.Options : Page.Main;
                if (options) _optionsTab = page == "keys" ? OptionsTab.Controls : page == "sound" ? OptionsTab.Sound : OptionsTab.Video;
            }, owner: this);
            DevCommands.Register("hudsize", "<0.85-1.3>", "How big the in-game HUD is drawn (Options: HUD SIZE, default 1).", args =>
            {
                Hud.HudSize = DevCommands.ParseFloat(args, 0);
                DevCommands.Print($"hud size = {Hud.HudSize:0.00}");
            }, owner: this);
        }

        private void OnDisable()
        {
            GameInput.ToggleMenu.performed -= OnToggleMenu;
            GameInput.ToggleOverlay.performed -= OnToggleOverlay;
            DevCommands.Unregister("menu", this);
            DevCommands.Unregister("hudsize", this);
            SetPause(false);
        }

        private void OnToggleMenu(InputAction.CallbackContext _)
        {
            if (DevConsole.IsOpen || AvatarCustomizer.IsOpen || SteamBootstrap.IsOverlayOpen) return;
            if (GameInput.IsRebinding || GameInput.RebindJustEnded) return; // that Esc cancelled a key change
            if (_page != Page.Main && (_pauseOpen || !_connection.IsActive))
            {
                _page = Page.Main; // Esc steps back out of a sub-screen first
                return;
            }
            if (!_connection.IsActive) return;
            if (!_pauseOpen && !GameInput.GameplayActive) return;
            SetPause(!_pauseOpen);
        }

        private void OnToggleOverlay(InputAction.CallbackContext _)
        {
            if (!_connection.IsActive || _pauseOpen || !GameInput.GameplayActive) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
                return; // Shift+Tab belongs to the Steam overlay.
            _guideOpen = !_guideOpen;
        }

        private void SetPause(bool open)
        {
            if (open == _pauseOpen) return;
            _pauseOpen = open;
            _page = Page.Main;
            if (open)
            {
                _guideOpen = false;
                GameInput.PushUI();
            }
            else GameInput.PopUI();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.f11Key.wasPressedThisFrame ||
                ((keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed) && keyboard.enterKey.wasPressedThisFrame)))
                GameDisplay.Toggle();

            if (_pauseOpen && !_connection.IsActive)
                SetPause(false);
            if (_connection.IsActive) _playPressedAt = -100f; // (in: PLAY says PLAY again next time)
            if (!_connection.IsActive)
                _guideOpen = false;
        }

        private void OnGUI()
        {
            bool active = _connection.IsActive;
            if (AvatarCustomizer.IsOpen || (active && !_pauseOpen && !_guideOpen))
                return;

            if (_guideOpen && !_pauseOpen) DrawGuide();
            else if (!active) DrawStartMenu();
            else DrawPauseMenu();
        }

        // ------------------------------------------------------------------ start screen

        private float _startShownAt = -1f;
        private int _startFrame = -10;
        private const int PageDimId = 0x5ade0001, PauseDimId = 0x5ade0002; // (Hud.Ease ids)
        private float _playPressedAt = -100f;
        private static Texture2D _backdrop;

        /// <summary>
        /// The menus' picture of island 1's village (Resources/UI/MenuBackdrop, filmed in the game: the island itself
        /// only exists once a session runs, so the menu camera saw empty sea). It covers the screen and drifts slowly
        /// in and across. Out of a session only (the character screen draws it too).
        /// </summary>
        public static void DrawMenuBackdrop()
        {
            if (Event.current.type != EventType.Repaint || _instance == null || _instance._connection.IsActive) return;
            if (_backdrop == null) _backdrop = Resources.Load<Texture2D>("UI/MenuBackdrop");
            if (_backdrop == null) return;
            float t = Time.unscaledTime;
            float zoom = 1.06f + 0.03f * Mathf.Sin(t * 0.045f);
            float screenAspect = Screen.width / (float)Mathf.Max(1, Screen.height), pictureAspect = _backdrop.width / (float)_backdrop.height;
            float w = 1f, h = 1f;
            if (screenAspect > pictureAspect) h = pictureAspect / screenAspect;
            else w = screenAspect / pictureAspect;
            w /= zoom;
            h /= zoom;
            float x = (1f - w) * (0.5f + 0.45f * Mathf.Sin(t * 0.031f)), y = (1f - h) * (0.5f + 0.35f * Mathf.Cos(t * 0.023f));
            GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, Screen.width, Screen.height), _backdrop, new Rect(x, y, w, h));
        }

        /// <summary>
        /// The start screen over the island 1 shot (SceneCameras): the title top left with a lifebuoy bobbing beside
        /// DROWN, the buttons bottom left. Everything slides in the first moment the screen shows.
        /// </summary>
        private void DrawStartMenu()
        {
            float width = Hud.Width, height = Hud.Height;
            if (Time.frameCount - _startFrame > 2) _startShownAt = Time.unscaledTime;
            _startFrame = Time.frameCount;
            float since = Time.unscaledTime - _startShownAt;
            float In(float delay, float length = 0.45f) => Mathf.SmoothStep(0f, 1f, (since - delay) / length);

            // The island stays in view: only the left, where the words are, is shaded; with a page open beside the
            // buttons (options, keys, join) the whole picture dims so the page reads cleanly.
            DrawMenuBackdrop();
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(Hud.Px(new Rect(0f, 0f, Mathf.Min(width, 1150f), height)), Fade(), ScaleMode.StretchToFill, true, 0f, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.78f), 0f, 0f);
            float paged = Hud.Ease(PageDimId, _page != Page.Main);
            if (paged > 0.01f) Hud.Fill(new Rect(0f, 0f, width, height), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.55f * paged));

            // The title: PLEASE DON'T over a big coral DROWN, a lifebuoy bobbing on the water beside it.
            float title = In(0.05f, 0.6f), slide = (1f - title) * -60f;
            Hud.Title(new Rect(58f + slide, 40f, 900f, 110f), "PLEASE DON'T", 98f, new Color(1f, 1f, 1f, title), TextAnchor.MiddleLeft);
            Hud.Title(new Rect(52f + slide, 124f, 900f, 200f), "DROWN", 190f, new Color(Hud.Coral.r, Hud.Coral.g, Hud.Coral.b, title), TextAnchor.MiddleLeft);
            float drownWidth = Hud.TitleWidth("DROWN", 190f);
            float bob = Mathf.Sin(Time.unscaledTime * 1.6f);
            Lifebuoy(new Rect(52f + slide + drownWidth + 36f, 152f + bob * 6f, 132f, 132f), bob * 0.025f, In(0.35f, 0.5f));
            float tagline = title * (1f - paged); // (out of the way of an open page)
            Hud.Label(new Rect(62f + slide, 318f, 700f, 70f), "A co-op lifeguard game. Save the tourists, find what they lose,\nand find out what's really going on at the beach.",
                24f, new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, tagline), TextAnchor.UpperLeft);

            // Buttons, stacked up from the bottom left, sliding in one after another.
            const int count = 5;
            float x = 48f, y = height - 52f - count * ButtonHeight - (count - 1) * ButtonGap;
            if (!string.IsNullOrEmpty(_connection.LastError))
                Hud.Label(new Rect(x, y - 74f, 900f, 60f), _connection.LastError, 22f, Hud.Coral, TextAnchor.LowerLeft, wrap: true);

            bool options = _page == Page.Options;
            Rect Next(int i)
            {
                Rect r = Row(x, ref y);
                r.x -= (1f - In(0.2f + i * 0.06f, 0.35f)) * (ButtonWidth + 60f);
                return r;
            }
            Rect play = Next(0);
            bool starting = Time.unscaledTime - _playPressedAt < 15f && string.IsNullOrEmpty(_connection.LastError);
            if (Hud.Button(play, starting ? "STARTING..." : "PLAY", primary: true) && !starting)
            {
                _playPressedAt = Time.unscaledTime;
                _connection.Play();
            }
            if (starting) // (hosting through Steam takes a moment: say so instead of sitting there)
                Hud.Label(new Rect(play.xMax + 18f, play.y, 420f, play.height), "Starting the island...", 22f,
                    new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 5f)), TextAnchor.MiddleLeft, heavy: true);
            if (Hud.Button(Next(1), "JOIN GAME", selected: _page == Page.Join)) _page = _page == Page.Join ? Page.Main : Page.Join;
            if (Hud.Button(Next(2), "CHARACTER")) AvatarCustomizer.Open();
            if (Hud.Button(Next(3), "OPTIONS", selected: options)) _page = options ? Page.Main : Page.Options;
            if (Hud.Button(Next(4), "QUIT")) Application.Quit();

            if (_page == Page.Join) DrawJoinPanel(x + ButtonWidth + 30f, height - 52f);
            else if (_page == Page.Options)
            {
                float tall = OptionsHeight(_optionsTab);
                DrawOptionsPanel(new Rect(x + ButtonWidth + 30f, height - 52f - tall, OptionsWidth, tall));
            }

            // Top right: whether friends can join (a green dot with Steam, grey without); the build, small, above it.
            float status = In(0.5f, 0.6f);
            bool steamUp = SteamBootstrap.IsReady;
            string steam = steamUp ? SteamBootstrap.LocalName + "  ·  friends can join you" : "Steam isn't running: offline only";
            float steamWidth = Hud.TextWidth(steam, 19f);
            Hud.Fill(new Rect(width - 34f - steamWidth - 20f, 25f, 10f, 10f), steamUp ? new Color(0.35f, 0.9f, 0.5f, status) : new Color(1f, 1f, 1f, 0.45f * status), 5f);
            Hud.Label(new Rect(width - 934f, 14f, 900f, 30f), steam, 19f, new Color(1f, 1f, 1f, 0.85f * status), TextAnchor.MiddleRight);
            Hud.Label(new Rect(width - 934f, 42f, 900f, 24f), "v" + NetVersion.Current, 15f, new Color(1f, 1f, 1f, 0.45f * status), TextAnchor.MiddleRight, shadow: false);
        }

        /// <summary>A red and white lifebuoy (eight bands round a thick ring), turned <paramref name="turn"/> of the way round.</summary>
        private static void Lifebuoy(Rect r, float turn, float alpha)
        {
            if (alpha <= 0.01f) return;
            const float thick = 0.27f;
            Hud.Arc(new Rect(r.x + 3f, r.y + 5f, r.width, r.height), 0f, 1f, thick + 0.02f, new Color(0f, 0f, 0f, 0.35f * alpha)); // (its shadow)
            for (int i = 0; i < 8; i++)
            {
                float from = Mathf.Repeat(turn + i / 8f, 1f), to = from + 1f / 8f;
                Color band = i % 2 == 0 ? new Color(Hud.Coral.r, Hud.Coral.g, Hud.Coral.b, alpha) : new Color(Hud.Paper.r, Hud.Paper.g, Hud.Paper.b, alpha);
                if (to <= 1f) Hud.Arc(r, from, to, thick, band);
                else
                {
                    Hud.Arc(r, from, 1f, thick, band);
                    Hud.Arc(r, 0f, to - 1f, thick, band);
                }
            }
            Hud.Arc(r, 0f, 1f, 0.025f, new Color(0f, 0f, 0f, 0.25f * alpha));
        }

        /// <summary>Friends with a session to join, and a box for a local / LAN address, beside the buttons.</summary>
        private void DrawJoinPanel(float x, float bottom)
        {
            const float width = 560f, row = 52f;
            var friends = SteamLobbyService.Friends();
            int playing = 0;
            if (SteamBootstrap.IsReady)
                foreach (SteamLobbyService.Friend friend in friends)
                {
                    if (!friend.InGame || playing >= 6) break; // sorted: the ones in this game come first
                    playing++;
                }
            float panelHeight = 200f + (playing > 0 ? 44f + playing * row : SteamBootstrap.IsReady ? 44f : 0f);
            var panel = new Rect(x, bottom - panelHeight, width, panelHeight);
            Hud.Panel(panel, 0.94f);
            float y = panel.y + 18f;

            if (SteamBootstrap.IsReady)
            {
                Hud.Label(new Rect(x + 24f, y, width - 48f, 30f), "FRIENDS PLAYING", 22f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
                y += 40f;
                if (playing == 0)
                    Hud.Label(new Rect(x + 24f, y - 8f, width - 48f, 30f), "None right now. An invite from Steam works too.", 18f, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleLeft, shadow: false);
                for (int i = 0; i < playing; i++)
                {
                    SteamLobbyService.Friend friend = friends[i];
                    bool joinable = friend.SameVersion && friend.Lobby != Steamworks.CSteamID.Nil;
                    string state = !friend.SameVersion ? "  (other build)" : !joinable ? "  (in the menu)" : string.Empty;
                    Hud.FitLabel(new Rect(x + 24f, y, width - 190f, row - 8f), friend.Name + state, 23f, Hud.Paper);
                    GUI.enabled = joinable;
                    if (Hud.Button(new Rect(x + width - 144f, y, 120f, row - 8f), "JOIN", small: true)) _connection.JoinFriend(friend.Lobby);
                    GUI.enabled = true;
                    y += row;
                }
                y += 4f;
            }

            Hud.Label(new Rect(x + 24f, y, width - 48f, 30f), "LOCAL / LAN GAME", 22f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            y += 40f;
            var box = new Rect(x + 24f, y, width - 190f, 48f);
            Hud.Fill(box, new Color(1f, 1f, 1f, 0.12f), 10f);
            _lanAddress = GUI.TextField(Hud.Px(box), _lanAddress, 64, Field());
            if (Hud.Button(new Rect(x + width - 144f, y, 120f, 48f), "JOIN", small: true)) _connection.JoinOffline(_lanAddress);
            y += 60f;
            Hud.Label(new Rect(x + 24f, y, width - 48f, 50f), "The host's address on your network (localhost for a second copy on this PC).",
                17f, new Color(1f, 1f, 1f, 0.65f), TextAnchor.UpperLeft, wrap: true, shadow: false);
        }

        // ------------------------------------------------------------------ pause

        private const float PauseLeft = 72f, PaneGap = 56f;

        /// <summary>
        /// Paused: PAUSED and the buttons down the left, and beside them a page: the lifeguard's log (the chapter, what
        /// to do now, the chapter's numbers, who's on duty), or OPTIONS when it's picked. Invite, travel and the yes/no
        /// questions are their own centred screens.
        /// </summary>
        private void DrawPauseMenu()
        {
            float width = Hud.Width, height = Hud.Height;
            float open = Hud.Ease(PauseDimId, true);
            Hud.Fill(new Rect(0f, 0f, width, height), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.7f * open)); // (fades in)
            var nm = _connection.NetworkManager;
            bool host = nm.IsServerStarted;
            float x = (width - ButtonWidth) * 0.5f;

            switch (_page)
            {
                case Page.Invite: DrawInvitePage(width, height); return;
                case Page.Travel:
                {
                    int count = Dev.DevIsland.DestinationNames.Length + 1;
                    float y = Header("TRAVEL", null, count, 440f, out float wide);
                    for (int i = 0; i < Dev.DevIsland.DestinationNames.Length; i++)
                    {
                        if (!Hud.Button(Row(wide, ref y, 440f), Dev.DevIsland.DestinationNames[i].ToUpperInvariant(), centred: true)) continue;
                        SetPause(false);
                        Dev.DevIsland.Travel((Dev.Destination)i);
                        return;
                    }
                    if (Hud.Button(Row(wide, ref y, 440f), "BACK", centred: true)) _page = Page.Main;
                    return;
                }
                case Page.ConfirmLeave:
                case Page.ConfirmQuit:
                {
                    bool quit = _page == Page.ConfirmQuit;
                    float y = Header(quit ? "QUIT THE GAME?" : host ? "END THE SESSION?" : "LEAVE THE SESSION?",
                        host ? "Everyone playing with you is sent back to the menu." : null, 2, ButtonWidth, out _);
                    if (Hud.Button(Row(x, ref y), "YES", centred: true))
                    {
                        SetPause(false);
                        _connection.Leave();
                        if (quit) Application.Quit();
                        return;
                    }
                    if (Hud.Button(Row(x, ref y), "NO", centred: true)) _page = Page.Main;
                    return;
                }
            }

            int players = host ? nm.ServerManager.Clients.Count : nm.ClientManager.Clients.Count;
            string info = (host ? "Hosting" : "Joined") + "  ·  " + _connection.Mode.ToString().ToLowerInvariant() + "  ·  " + players + (players == 1 ? " player" : " players");
            if (!SteamLobbyService.InLobby && SteamBootstrap.IsReady)
                info += "\nOffline session: friends can't join it. Leave and press PLAY to host through Steam.";

            // The left: PAUSED, how the session runs, the buttons (sliding in from the left as the screen dims).
            float slide = (1f - open) * 50f;
            float left = PauseLeft - slide, top = Mathf.Max(36f, height * 0.5f - 420f);
            Hud.Title(new Rect(left, top, 700f, 96f), "PAUSED", 86f, Color.white, TextAnchor.MiddleLeft);
            float infoHeight = Hud.TextHeight(info, 520f, 19f);
            Hud.Label(new Rect(left + 4f, top + 94f, 520f, infoHeight + 4f), info, 19f, new Color(1f, 1f, 1f, 0.75f), TextAnchor.UpperLeft, wrap: true);
            float y0 = top + 120f + infoHeight + 20f, row = y0;
            bool options = _page == Page.Options;
            if (Hud.Button(Row(left, ref row), "RESUME", primary: true)) SetPause(false);
            if (SteamLobbyService.InLobby && Hud.Button(Row(left, ref row), "INVITE FRIENDS")) _page = Page.Invite;
            if (Hud.Button(Row(left, ref row), "CHARACTER")) AvatarCustomizer.Open();
            if (Hud.Button(Row(left, ref row), "OPTIONS", selected: options)) _page = options ? Page.Main : Page.Options;
            if (Hud.Button(Row(left, ref row), "MAIN MENU")) _page = Page.ConfirmLeave;
            if (Hud.Button(Row(left, ref row), "QUIT")) _page = Page.ConfirmQuit;
            // The testing switches only exist in development builds, small, under the buttons.
            if (DevCommands.CheatsAllowed) DrawDevRow(left, row + 34f, host);

            // The right: the page beside the buttons (sliding in from the right).
            float paneX = PauseLeft + ButtonWidth + PaneGap + slide;
            float paneWidth = Mathf.Min(OptionsWidth, width - paneX - PauseLeft);
            if (options) DrawOptionsPanel(new Rect(paneX, top + 12f, paneWidth, OptionsHeight(_optionsTab)));
            else DrawLogbook(new Rect(paneX, top + 12f, paneWidth, height - top - 60f));
        }

        /// <summary>
        /// The pause screen's page: the chapter and what to do now, the chapter's numbers so far (the host counts them),
        /// the team's money, and everyone on duty with what they're doing.
        /// </summary>
        private static void DrawLogbook(Rect pane)
        {
            Hud.Panel(pane);
            float x = pane.x + 28f, w = pane.width - 56f, y = pane.y + 12f;
            Hud.Title(new Rect(x, y, w, 52f), "LIFEGUARD'S LOG", 42f, Hud.Sand, TextAnchor.MiddleLeft);
            y += 66f;

            Story.StoryDirector story = Story.StoryDirector.Instance;
            if (story != null && !string.IsNullOrEmpty(story.Objective))
            {
                Hud.FitLabel(new Rect(x, y, w, 26f), (story.Chapter ?? string.Empty).ToUpperInvariant(), 19f, Hud.Teal, heavy: true);
                y += 32f;
                float body = Hud.TextHeight(story.Objective, w, 30f, heavy: true);
                Hud.Label(new Rect(x, y, w, body + 4f), story.Objective, 30f, Hud.Paper, TextAnchor.UpperLeft, heavy: true, wrap: true);
                y += body + 10f;
                if (story.Goal > 0)
                {
                    Hud.Bar(new Rect(x, y + 8f, w - 110f, 10f), (float)story.Progress / story.Goal, Hud.Teal);
                    Hud.Label(new Rect(x + w - 100f, y - 2f, 100f, 30f), $"{story.Progress} / {story.Goal}", 22f, Hud.Sand, TextAnchor.MiddleRight, heavy: true, shadow: false);
                    y += 34f;
                }
                y += 12f;
                Divider(x, ref y, w);
            }

            // The numbers: this shift's so far (only the host counts them) and the team's money.
            bool counted = story != null && story.IsServerInitialized;
            var stats = counted ? story.ShiftStats : default;
            Hud.Label(new Rect(x, y, w, 24f), counted ? "THIS SHIFT" : "THE TEAM", 17f, Hud.Muted, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            y += 32f;
            int tiles = counted ? 4 : 1;
            const float gap = 14f, tall = 98f;
            float tile = counted ? (w - (tiles - 1) * gap) / tiles : 240f, tx = x;
            void Tile(string number, string label, Color color)
            {
                Hud.StatTile(new Rect(tx, y, tile, tall), number, label, color);
                tx += tile + gap;
            }
            if (counted)
            {
                Tile(stats.rescued.ToString(), "RESCUED", Hud.Paper);
                Tile(stats.lost.ToString(), "LOST", stats.lost > 0 ? Hud.Danger : Hud.Paper);
                Tile("$" + stats.earned.ToString("N0"), "EARNED", Hud.Teal);
            }
            Tile("$" + Story.Economy.Money.ToString("N0"), "TEAM FUNDS", Hud.Sand);
            y += tall + 22f;
            Divider(x, ref y, w);

            // Who's on duty: you first, then the others, each with what they're doing now.
            Hud.Label(new Rect(x, y, w, 24f), "ON DUTY", 17f, Hud.Muted, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            y += 34f;
            Player.PlayerHub local = Player.PlayerHub.Local;
            if (local != null) DutyRow(local, true, x, ref y, w);
            foreach (Player.PlayerHub p in Player.PlayerHub.All)
                if (p != null && p != local && y < pane.yMax - 40f) DutyRow(p, false, x, ref y, w);
        }

        private static void DutyRow(Player.PlayerHub p, bool you, float x, ref float y, float w)
        {
            string status;
            Color color = Hud.Muted;
            Items.Item held = p.Hands != null ? p.Hands.HeldItem : null;
            if (p.TryGetComponent(out Combat.PlayerCombat combat) && combat.IsDowned)
            {
                status = "DOWN: needs CPR";
                color = Hud.Danger;
            }
            else if (held != null && held.TryGetComponent(out Rescue.VictimBrain victim))
            {
                status = "carrying " + victim.Name;
                color = Hud.Teal;
            }
            else status = held != null ? "holding the " + held.DisplayName.ToLowerInvariant() : "on watch";
            Hud.Fill(new Rect(x + 2f, y + 12f, 14f, 14f), you ? Hud.Teal : Hud.Sand, 7f);
            Hud.FitLabel(new Rect(x + 28f, y, w * 0.5f, 38f), you ? p.DisplayName + "  (you)" : p.DisplayName, 23f, Hud.Paper, heavy: true);
            Hud.FitLabel(new Rect(x + w * 0.5f, y, w * 0.5f, 38f), status, 20f, color, TextAnchor.MiddleRight);
            y += 44f;
        }

        private static void Divider(float x, ref float y, float w)
        {
            Hud.Fill(new Rect(x, y, w, 1.5f), new Color(1f, 1f, 1f, 0.14f));
            y += 22f;
        }

        /// <summary>Development builds: god mode, flying, travel and a story restart, small, under the menu.</summary>
        private void DrawDevRow(float x, float y, bool host)
        {
            bool travel = Dev.DevIsland.Instance != null;
            bool story = host && Story.StoryDirector.Instance != null;
            const float w = 155f, gap = 8f;
            Hud.Label(new Rect(x, y - 26f, 300f, 22f), "DEV", 15f, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft, heavy: true, shadow: false);
            int n = 0;
            Rect Next()
            {
                var r = new Rect(x + (n % 2) * (w + gap), y + (n / 2) * 50f, w, 42f);
                n++;
                return r;
            }
            if (Hud.Button(Next(), Player.GodMode.On ? "GOD: ON" : "GOD: OFF", small: true, selected: Player.GodMode.On)) Player.GodMode.Toggle();
            if (Hud.Button(Next(), Player.GodMode.Flying ? "FLY: ON" : "FLY: OFF", small: true, selected: Player.GodMode.Flying)) Player.GodMode.ToggleFly();
            if (travel && Hud.Button(Next(), "TRAVEL", small: true)) _page = Page.Travel;
            if (story && Hud.Button(Next(), "RESTART STORY", small: true))
            {
                SetPause(false);
                DevCommands.Execute("story reset");
            }
        }

        // ------------------------------------------------------------------ options

        /// <summary>The options' tabs: what you see, what you hear, how you play.</summary>
        private enum OptionsTab { Video, Sound, Controls }
        private static readonly string[] OptionsTabs = { "VIDEO", "SOUND", "CONTROLS" };
        private OptionsTab _optionsTab;
        private float _tabLine = -1f; // where the teal line under the open tab is (it slides to the one picked)
        private const float OptionsWidth = 900f, KeyRow = 46f;

        /// <summary>The options panel's height for a tab (each tab only as tall as it needs).</summary>
        private static float OptionsHeight(OptionsTab tab) => 104f + tab switch
        {
            OptionsTab.Video => 2f * 82f,
            OptionsTab.Sound => 2f * 64f,
            _ => 64f + ((GameInput.Rebindable != null ? GameInput.Rebindable.Length : 14) + 1) / 2 * KeyRow + 8f
        } + 90f;

        /// <summary>
        /// OPTIONS: the tabs across the top (VIDEO, SOUND, CONTROLS) and the open one's settings in two columns
        /// underneath, applied as you change them; RESET puts the open tab back as it came.
        /// </summary>
        private void DrawOptionsPanel(Rect panel)
        {
            bool waiting = GameInput.IsRebinding;
            Hud.Panel(panel);
            Hud.Title(new Rect(panel.x + 28f, panel.y + 12f, 320f, 50f), "OPTIONS", 42f, Color.white, TextAnchor.MiddleLeft);

            // The tabs, on the title's line at the right; a line runs under them and the open tab's part of it is teal.
            const float tabWidth = 158f, tabGap = 8f;
            float tabsX = panel.xMax - 28f - OptionsTabs.Length * tabWidth - (OptionsTabs.Length - 1) * tabGap;
            GUI.enabled = !waiting;
            for (int i = 0; i < OptionsTabs.Length; i++)
            {
                var tab = new Rect(tabsX + i * (tabWidth + tabGap), panel.y + 16f, tabWidth, 44f);
                if (Hud.Button(tab, OptionsTabs[i], centred: true, small: true, selected: (int)_optionsTab == i)) _optionsTab = (OptionsTab)i;
            }
            GUI.enabled = true;
            float lineTo = tabsX + (int)_optionsTab * (tabWidth + tabGap);
            _tabLine = _tabLine < 0f ? lineTo : Mathf.Lerp(_tabLine, lineTo, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 18f));
            Hud.Fill(new Rect(panel.x + 28f, panel.y + 72f, panel.width - 56f, 1.5f), new Color(1f, 1f, 1f, 0.16f));
            Hud.Fill(new Rect(_tabLine, panel.y + 70f, tabWidth, 4f), Hud.Teal, 2f);

            // Two columns under the tabs.
            float column = (panel.width - 16f) * 0.5f, top = panel.y + 92f;
            var left = new Rect(panel.x, panel.y, column, panel.height);
            var right = new Rect(panel.x + column + 16f, panel.y, column, panel.height);
            float y = top, y2 = top;
            float changed;
            switch (_optionsTab)
            {
                case OptionsTab.Video:
                {
                    int tier = (int)GraphicsQuality.Current;
                    int picked = ChoiceRow(left, ref y, "GRAPHICS", new[] { "LOW", "MEDIUM", "HIGH" }, tier);
                    if (picked != tier) GraphicsQuality.Set((GraphicsQuality.Tier)picked, true);
                    int display = GameDisplay.IsFullscreen ? 1 : 0;
                    if (ChoiceRow(left, ref y, "DISPLAY", new[] { "WINDOWED", "FULLSCREEN" }, display) != display) GameDisplay.Toggle();

                    float fov = LookSettings.Fov;
                    changed = OptionRow(right, ref y2, "FIELD OF VIEW", fov, LookSettings.MinFov, LookSettings.MaxFov, Mathf.RoundToInt(fov) + "°");
                    if (!Mathf.Approximately(changed, fov)) LookSettings.Fov = Mathf.Round(changed);
                    // How big the in-game HUD is drawn (the menus stay as they are).
                    float hud = Hud.HudSize;
                    changed = OptionRow(right, ref y2, "HUD SIZE", hud, Hud.MinHudSize, Hud.MaxHudSize, Mathf.RoundToInt(hud * 100f) + "%");
                    if (!Mathf.Approximately(changed, hud)) Hud.HudSize = Mathf.Round(changed * 20f) / 20f;
                    break;
                }
                case OptionsTab.Sound:
                {
                    // Everything the game plays, as a percentage.
                    float volume = SoundSettings.Volume;
                    changed = OptionRow(left, ref y, "VOLUME", volume, 0f, 1f, Mathf.RoundToInt(volume * 100f).ToString());
                    if (!Mathf.Approximately(changed, volume)) SoundSettings.Volume = Mathf.Round(changed * 20f) / 20f;
                    float music = SoundSettings.Music;
                    changed = OptionRow(left, ref y, "MUSIC", music, 0f, 1f, Mathf.RoundToInt(music * 100f).ToString());
                    if (!Mathf.Approximately(changed, music)) SoundSettings.Music = Mathf.Round(changed * 20f) / 20f;
                    float voice = SoundSettings.Voice;
                    changed = OptionRow(right, ref y2, "VOICES", voice, 0f, 1f, Mathf.RoundToInt(voice * 100f).ToString());
                    if (!Mathf.Approximately(changed, voice)) SoundSettings.Voice = Mathf.Round(changed * 20f) / 20f;
                    break;
                }
                default:
                {
                    // Shown as a plain number, 1.0 = the default; the keys under it.
                    GUI.enabled = !waiting;
                    float sensitivity = LookSettings.Sensitivity / LookSettings.DefaultSensitivity;
                    changed = OptionRow(left, ref y, "MOUSE SENSITIVITY", sensitivity, LookSettings.MinSensitivity / LookSettings.DefaultSensitivity,
                        LookSettings.MaxSensitivity / LookSettings.DefaultSensitivity, sensitivity.ToString("0.00"));
                    if (!Mathf.Approximately(changed, sensitivity)) LookSettings.Sensitivity = Mathf.Round(changed * 20f) / 20f * LookSettings.DefaultSensitivity;
                    GUI.enabled = true;
                    DrawKeys(panel, y + 8f);
                    break;
                }
            }

            GUI.enabled = !waiting;
            if (Hud.Button(new Rect(panel.x + 28f, panel.yMax - 66f, 220f, 46f), "RESET", small: true))
            {
                switch (_optionsTab)
                {
                    case OptionsTab.Video:
                        LookSettings.Fov = LookSettings.DefaultFov;
                        Hud.HudSize = 1f;
                        break;
                    case OptionsTab.Sound:
                        SoundSettings.Volume = SoundSettings.DefaultVolume;
                        SoundSettings.Music = SoundSettings.DefaultMusic;
                        SoundSettings.Voice = SoundSettings.DefaultVoice;
                        break;
                    default:
                        LookSettings.Sensitivity = LookSettings.DefaultSensitivity;
                        GameInput.ResetBindings();
                        break;
                }
                PlayerPrefs.Save();
            }
            GUI.enabled = true;
            string note = _optionsTab switch
            {
                OptionsTab.Video => "Fullscreen also on F11.",
                OptionsTab.Sound => "Voices: what the tourists and the island's people say.",
                _ => waiting ? "Press the new key or mouse button. Esc keeps the old one." : "Click a key to change it. Slots 1-6 and the gamepad stay as they are."
            };
            Hud.Label(new Rect(panel.x + 270f, panel.yMax - 66f, panel.width - 298f, 46f), note, 18f, Hud.Muted, TextAnchor.MiddleRight, wrap: true, shadow: false);
        }

        // ------------------------------------------------------------------ keys

        private int _rebindRow = -1;
        private string[] _keyLabels;
        private int _keyLabelsVersion = -1;

        /// <summary>Every key that can be changed, in two columns from <paramref name="top"/>: click one, then press its new key.</summary>
        private void DrawKeys(Rect panel, float top)
        {
            GameInput.Rebind[] keys = GameInput.Rebindable;
            if (_keyLabels == null || _keyLabelsVersion != GameInput.BindingsVersion)
            {
                _keyLabelsVersion = GameInput.BindingsVersion;
                _keyLabels = new string[keys.Length];
                for (int i = 0; i < keys.Length; i++) _keyLabels[i] = GameInput.KeyLabel(keys[i].Action, keys[i].Binding).ToUpperInvariant();
            }
            bool waiting = GameInput.IsRebinding;
            if (!waiting) _rebindRow = -1;
            int perColumn = (keys.Length + 1) / 2;
            float column = (panel.width - 56f - 24f) * 0.5f;
            for (int i = 0; i < keys.Length; i++)
            {
                float x = panel.x + 28f + (i / perColumn) * (column + 24f), y = top + (i % perColumn) * KeyRow;
                Hud.Label(new Rect(x, y, column - 190f, KeyRow - 8f), keys[i].Label, 18f, Hud.Muted, TextAnchor.MiddleLeft, wrap: true, shadow: false);
                var box = new Rect(x + column - 180f, y, 180f, KeyRow - 8f);
                if (i == _rebindRow)
                {
                    Hud.Fill(box, Hud.Coral, 11f);
                    Hud.Label(box, "PRESS A KEY", 20f, Hud.Ink, heavy: true, shadow: false);
                }
                else
                {
                    GUI.enabled = !waiting;
                    if (Hud.Button(box, _keyLabels[i], centred: true, small: true))
                    {
                        _rebindRow = i;
                        GameInput.StartRebind(i);
                    }
                    GUI.enabled = true;
                }
            }
        }

        /// <summary>A label, a slider and the value's number on one line; returns the slider's value.</summary>
        private float OptionRow(Rect panel, ref float y, string label, float value, float min, float max, string number)
        {
            Hud.Label(new Rect(panel.x + 28f, y, 280f, 30f), label, 21f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            Hud.Label(new Rect(panel.xMax - 108f, y + 26f, 80f, 36f), number, 26f, Color.white, TextAnchor.MiddleRight, heavy: true, shadow: false);
            value = Hud.Slider(new Rect(panel.x + 28f, y + 26f, panel.width - 160f, 36f), value, min, max);
            y += 64f;
            return value;
        }

        /// <summary>A label and a row of choices (the picked one lit); returns the one picked now.</summary>
        private static int ChoiceRow(Rect panel, ref float y, string label, string[] choices, int current)
        {
            Hud.Label(new Rect(panel.x + 28f, y, 280f, 30f), label, 21f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            const float gap = 8f;
            float w = (panel.width - 56f - gap * (choices.Length - 1)) / choices.Length;
            for (int i = 0; i < choices.Length; i++)
                if (Hud.Button(new Rect(panel.x + 28f + i * (w + gap), y + 30f, w, 40f), choices[i], small: true, selected: i == current) && i != current)
                    current = i;
            y += 82f;
            return current;
        }

        /// <summary>Online Steam friends to invite: Steam sends them the invite, no overlay needed.</summary>
        private void DrawInvitePage(float width, float height)
        {
            const float panelWidth = 600f, row = 52f;
            var friends = SteamLobbyService.Friends();
            int shown = 0;
            foreach (SteamLobbyService.Friend friend in friends)
                if (!SteamLobbyService.IsMember(friend.Id) && shown < 10) shown++;
            float panelHeight = 96f + Mathf.Max(1, shown) * row + 150f;
            var panel = new Rect((width - panelWidth) * 0.5f, (height - panelHeight) * 0.5f, panelWidth, panelHeight);
            Hud.Panel(panel, 0.94f);
            Hud.Title(new Rect(panel.x, panel.y + 14f, panelWidth, 54f), "INVITE A FRIEND", 44f, Color.white);
            float y = panel.y + 84f;
            if (shown == 0)
            {
                Hud.Label(new Rect(panel.x + 24f, y, panelWidth - 48f, row), "No Steam friends online right now.", 22f, new Color(1f, 1f, 1f, 0.75f), shadow: false);
                y += row;
            }
            int drawn = 0;
            foreach (SteamLobbyService.Friend friend in friends)
            {
                if (SteamLobbyService.IsMember(friend.Id)) continue;
                if (++drawn > shown) break;
                Hud.FitLabel(new Rect(panel.x + 24f, y, panelWidth - 212f, row - 8f), friend.InGame ? friend.Name + "  (playing)" : friend.Name, 23f, Hud.Paper);
                bool invited = SteamLobbyService.RecentlyInvited(friend.Id);
                GUI.enabled = !invited;
                if (Hud.Button(new Rect(panel.xMax - 164f, y, 140f, row - 8f), invited ? "SENT" : "INVITE", small: true)) SteamLobbyService.Invite(friend.Id);
                GUI.enabled = true;
                y += row;
            }
            Hud.Label(new Rect(panel.x + 24f, y, panelWidth - 48f, 50f), "They accept it in Steam; the game must already be running on their PC.",
                17f, new Color(1f, 1f, 1f, 0.65f), TextAnchor.UpperLeft, wrap: true, shadow: false);
            y = panel.yMax - 76f;
            float half = (panelWidth - 48f - ButtonGap) * 0.5f;
            GUI.enabled = SteamBootstrap.OverlayAvailable;
            if (Hud.Button(new Rect(panel.x + 24f, y, half, 56f), "STEAM OVERLAY", centred: true, small: true)) _connection.InviteFriends();
            GUI.enabled = true;
            if (Hud.Button(new Rect(panel.x + 24f + half + ButtonGap, y, half, 56f), "BACK", centred: true, small: true)) _page = Page.Main;
        }

        /// <summary>
        /// The heading over a centred column of <paramref name="rows"/> buttons; returns where the first one goes
        /// (<paramref name="x"/> is the column's left edge).
        /// </summary>
        private static float Header(string title, string note, int rows, float columnWidth, out float x)
        {
            float width = Hud.Width, height = Hud.Height;
            float column = rows * ButtonHeight + (rows - 1) * ButtonGap;
            float noteHeight = string.IsNullOrEmpty(note) ? 0f : 70f;
            float top = Mathf.Max(20f, (height - column - 110f - noteHeight) * 0.5f);
            Hud.Title(new Rect(0f, top, width, 96f), title, 86f, Color.white);
            if (noteHeight > 0f)
                Hud.Label(new Rect(width * 0.5f - 500f, top + 92f, 1000f, 60f), note, 20f, new Color(1f, 1f, 1f, 0.8f), TextAnchor.UpperCenter, wrap: true);
            x = (width - columnWidth) * 0.5f;
            return top + 110f + noteHeight;
        }

        // ------------------------------------------------------------------ guide

        private (string key, string what)[] _guide;
        private int _guideVersion = -1;

        /// <summary>The quick guide (Tab), top right: two columns of keycaps with what each does.</summary>
        private void DrawGuide()
        {
            const float width = 540f, height = 380f, row = 38f;
            var panel = new Rect(Hud.Width - width - 24f, 24f, width, height);
            Hud.Panel(panel);
            Hud.Title(new Rect(panel.x + 24f, panel.y + 10f, width - 48f, 44f), "BEACH GUIDE", 34f, Hud.Teal, TextAnchor.MiddleLeft);
            if (_guide == null || _guideVersion != GameInput.BindingsVersion)
            {
                // The player's own keys (OPTIONS, KEYS), written out once until one changes.
                _guideVersion = GameInput.BindingsVersion;
                GameInput.Rebind[] k = GameInput.Rebindable;
                string move = GameInput.KeyLabel(k[0].Action, k[0].Binding) + GameInput.KeyLabel(k[2].Action, k[2].Binding) +
                              GameInput.KeyLabel(k[1].Action, k[1].Binding) + GameInput.KeyLabel(k[3].Action, k[3].Binding);
                _guide = new[]
                {
                    (move.Length <= 4 ? move : "Keys", "Move"), (GameInput.KeyLabel(GameInput.Sprint), "Sprint"),
                    (GameInput.KeyLabel(GameInput.Interact), "Rescue / use"), (GameInput.KeyLabel(GameInput.Jump), "Jump"),
                    (GameInput.KeyLabel(GameInput.Primary), "Punch / fire"), (GameInput.KeyLabel(GameInput.Secondary), "CPR / aim"),
                    (GameInput.KeyLabel(GameInput.Reload), "Reload"), ("1-6", "Inventory"),
                    (GameInput.KeyLabel(GameInput.Drop), "Drop (hold: throw)"), (GameInput.KeyLabel(GameInput.Emote), "Wave"),
                    ("Tab", "Close guide"), ("Esc", "Pause"),
                    ("F11", "Fullscreen"), ("Shift+Tab", "Steam")
                };
            }
            float y = panel.y + 64f, half = (width - 48f) * 0.5f;
            var white = new Color(1f, 1f, 1f, 0.94f);
            for (int i = 0; i < _guide.Length; i++)
            {
                if (i == 10 && i % 2 == 0) y += 8f; // (the menu keys a little apart)
                float x = panel.x + 24f + (i % 2) * (half + 10f);
                float cap = Hud.KeyCap(x, y + 3f, _guide[i].key, 28f);
                Hud.FitLabel(new Rect(x + cap + 10f, y, half - cap - 18f, 34f), _guide[i].what, 19f, white, minimum: 14f);
                if (i % 2 == 1) y += row;
            }
            Hud.Label(new Rect(panel.x + 24f, panel.yMax - 40f, width - 48f, 28f), "Keys, mouse and sound: Esc, then OPTIONS", 18f, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleLeft, shadow: false);
        }

        // ------------------------------------------------------------------ parts

        /// <summary>The next button's box in a column, moving <paramref name="y"/> down past it.</summary>
        private static Rect Row(float x, ref float y, float width = ButtonWidth)
        {
            var r = new Rect(x, y, width, ButtonHeight);
            y += ButtonHeight + ButtonGap;
            return r;
        }

        private GUIStyle Field()
        {
            int px = Mathf.RoundToInt(24f * Hud.Scale);
            if (_field != null && _fieldPx == px) return _field;
            _fieldPx = px;
            int pad = Mathf.RoundToInt(14f * Hud.Scale);
            _field = new GUIStyle(Hud.Style(24f, TextAnchor.MiddleLeft)) { clipping = TextClipping.Clip, padding = new RectOffset(pad, pad, 0, 0) };
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Color.white;
            return _field;
        }

        /// <summary>White fading out to the right (tinted when drawn), to shade the side the menu's words are on.</summary>
        private Texture2D Fade()
        {
            if (_fade != null) return _fade;
            const int n = 128;
            _fade = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[n];
            for (int i = 0; i < n; i++)
            {
                float t = 1f - i / (n - 1f);
                pixels[i] = new Color32(255, 255, 255, (byte)(255f * t * t * (3f - 2f * t)));
            }
            _fade.SetPixels32(pixels);
            _fade.Apply();
            return _fade;
        }

        private void OnDestroy()
        {
            if (_fade != null) Destroy(_fade);
            if (_instance == this) _instance = null;
        }
    }
}
