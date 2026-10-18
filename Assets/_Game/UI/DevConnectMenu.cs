using PleaseDontDrown.Core;
using PleaseDontDrown.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Start screen, pause menu, and quick in-game guide, laid out like How to Fish's: on the start screen the big
    /// title sits top left over the island and a column of wide buttons bottom left (a button lights up with a white
    /// frame under the mouse); the pause menu is the same buttons in a centred column over the dimmed game.
    /// </summary>
    public class DevConnectMenu : MonoBehaviour
    {
        private enum Page { Main, Join, Options, Controls, Invite, Travel, ConfirmLeave, ConfirmQuit }

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
            DevCommands.Register("menu", "<pause|options|keys|guide|close>", "Open a menu page (to look at it in tests).", args =>
            {
                string page = args.Length > 0 ? args[0] : "pause";
                _guideOpen = page == "guide";
                if (_connection.IsActive) SetPause(page is "pause" or "options" or "keys");
                _page = page == "options" ? Page.Options : page == "keys" ? Page.Controls : Page.Main;
            }, owner: this);
        }

        private void OnDisable()
        {
            GameInput.ToggleMenu.performed -= OnToggleMenu;
            GameInput.ToggleOverlay.performed -= OnToggleOverlay;
            DevCommands.Unregister("menu", this);
            SetPause(false);
        }

        private void OnToggleMenu(InputAction.CallbackContext _)
        {
            if (DevConsole.IsOpen || AvatarCustomizer.IsOpen || SteamBootstrap.IsOverlayOpen) return;
            if (GameInput.IsRebinding || GameInput.RebindJustEnded) return; // that Esc cancelled a key change
            if (_page != Page.Main && (_pauseOpen || !_connection.IsActive))
            {
                _page = _page == Page.Controls ? Page.Options : Page.Main; // Esc steps back out of a sub-screen first
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
            Hud.Label(new Rect(58f + slide, 46f, 900f, 100f), "PLEASE DON'T", 92f, new Color(1f, 1f, 1f, title), TextAnchor.MiddleLeft, heavy: true);
            Hud.Label(new Rect(52f + slide, 128f, 900f, 190f), "DROWN", 176f, new Color(Hud.Coral.r, Hud.Coral.g, Hud.Coral.b, title), TextAnchor.MiddleLeft, heavy: true);
            float drownWidth = Hud.TextWidth("DROWN", 176f, heavy: true);
            float bob = Mathf.Sin(Time.unscaledTime * 1.6f);
            Lifebuoy(new Rect(52f + slide + drownWidth + 26f, 150f + bob * 6f, 132f, 132f), bob * 0.025f, In(0.35f, 0.5f));
            Hud.Label(new Rect(62f + slide, 318f, 700f, 70f), "A co-op lifeguard game. Save the tourists, find what they lose,\nand find out what's really going on at the beach.",
                24f, new Color(Hud.Sand.r, Hud.Sand.g, Hud.Sand.b, title), TextAnchor.UpperLeft);

            // Buttons, stacked up from the bottom left, sliding in one after another.
            const int count = 5;
            float x = 48f, y = height - 52f - count * ButtonHeight - (count - 1) * ButtonGap;
            if (!string.IsNullOrEmpty(_connection.LastError))
                Hud.Label(new Rect(x, y - 74f, 900f, 60f), _connection.LastError, 22f, Hud.Coral, TextAnchor.LowerLeft, wrap: true);

            bool options = _page is Page.Options or Page.Controls;
            Rect Next(int i)
            {
                Rect r = Row(x, ref y);
                r.x -= (1f - In(0.2f + i * 0.06f, 0.35f)) * (ButtonWidth + 60f);
                return r;
            }
            if (Hud.Button(Next(0), "PLAY", primary: true)) _connection.Play();
            if (Hud.Button(Next(1), "JOIN GAME", selected: _page == Page.Join)) _page = _page == Page.Join ? Page.Main : Page.Join;
            if (Hud.Button(Next(2), "CHARACTER")) AvatarCustomizer.Open();
            if (Hud.Button(Next(3), "OPTIONS", selected: options)) _page = options ? Page.Main : Page.Options;
            if (Hud.Button(Next(4), "QUIT")) Application.Quit();

            if (_page == Page.Join) DrawJoinPanel(x + ButtonWidth + 30f, height - 52f);
            else if (_page == Page.Options) DrawOptionsPanel(new Rect(x + ButtonWidth + 30f, height - 52f - OptionsHeight, OptionsWidth, OptionsHeight));
            else if (_page == Page.Controls) DrawControlsPanel(new Rect(x + ButtonWidth + 30f, height - 52f - ControlsHeight, ControlsWidth, ControlsHeight));

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

        private void DrawPauseMenu()
        {
            float width = Hud.Width, height = Hud.Height;
            Hud.Fill(new Rect(0f, 0f, width, height), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.66f * Hud.Ease(PauseDimId, true))); // (fades in)
            var nm = _connection.NetworkManager;
            bool host = nm.IsServerStarted;
            float x = (width - ButtonWidth) * 0.5f;

            switch (_page)
            {
                case Page.Invite: DrawInvitePage(width, height); return;
                case Page.Options:
                {
                    var panel = new Rect((width - OptionsWidth) * 0.5f, (height - OptionsHeight - ButtonHeight - 20f) * 0.5f, OptionsWidth, OptionsHeight);
                    DrawOptionsPanel(panel);
                    if (Hud.Button(new Rect(x, panel.yMax + 20f, ButtonWidth, ButtonHeight), "BACK", centred: true)) _page = Page.Main;
                    return;
                }
                case Page.Controls:
                {
                    var panel = new Rect((width - ControlsWidth) * 0.5f, (height - ControlsHeight - ButtonHeight - 20f) * 0.5f, ControlsWidth, ControlsHeight);
                    DrawControlsPanel(panel);
                    if (!GameInput.IsRebinding && Hud.Button(new Rect(x, panel.yMax + 20f, ButtonWidth, ButtonHeight), "BACK", centred: true)) _page = Page.Options;
                    return;
                }
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
            // The player's menu is short; the testing switches only exist in development builds, in a small row below.
            bool dev = DevCommands.CheatsAllowed;
            int rows = 5 + (SteamLobbyService.InLobby ? 1 : 0);
            float top = Header("PAUSED", info, rows + (dev ? 1 : 0), ButtonWidth, out _);

            if (Hud.Button(Row(x, ref top), "RESUME", centred: true, primary: true)) SetPause(false);
            if (SteamLobbyService.InLobby && Hud.Button(Row(x, ref top), "INVITE FRIENDS", centred: true)) _page = Page.Invite;
            if (Hud.Button(Row(x, ref top), "CHARACTER", centred: true)) AvatarCustomizer.Open();
            if (Hud.Button(Row(x, ref top), "OPTIONS", centred: true)) _page = Page.Options;
            if (Hud.Button(Row(x, ref top), "MAIN MENU", centred: true)) _page = Page.ConfirmLeave;
            if (Hud.Button(Row(x, ref top), "QUIT", centred: true)) _page = Page.ConfirmQuit;
            if (dev) DrawDevRow(top + 14f, host);
        }

        /// <summary>Development builds: god mode, flying, travel and a story restart, small, under the menu.</summary>
        private void DrawDevRow(float y, bool host)
        {
            bool travel = Dev.DevIsland.Instance != null;
            bool story = host && Story.StoryDirector.Instance != null;
            int count = 2 + (travel ? 1 : 0) + (story ? 1 : 0);
            const float w = 170f, gap = 8f;
            float total = count * w + (count - 1) * gap, x = (Hud.Width - total) * 0.5f;
            Hud.Label(new Rect(x, y - 26f, total, 22f), "DEV", 15f, new Color(1f, 1f, 1f, 0.45f), TextAnchor.MiddleLeft, heavy: true, shadow: false);
            Rect Next()
            {
                var r = new Rect(x, y, w, 42f);
                x += w + gap;
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

        private const float OptionsWidth = 620f, OptionsHeight = 630f;

        /// <summary>
        /// Graphics quality and the display first (picked from a row), then mouse sensitivity, field of view and the
        /// volumes, each a slider with its number; they apply as you drag.
        /// </summary>
        private void DrawOptionsPanel(Rect panel)
        {
            Hud.Panel(panel);
            Hud.Label(new Rect(panel.x + 28f, panel.y + 16f, panel.width - 56f, 44f), "OPTIONS", 36f, Color.white, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            float y = panel.y + 72f;

            int tier = (int)GraphicsQuality.Current;
            int picked = ChoiceRow(panel, ref y, "GRAPHICS", new[] { "LOW", "MEDIUM", "HIGH" }, tier);
            if (picked != tier) GraphicsQuality.Set((GraphicsQuality.Tier)picked, true);
            int display = GameDisplay.IsFullscreen ? 1 : 0;
            if (ChoiceRow(panel, ref y, "DISPLAY", new[] { "WINDOWED", "FULLSCREEN" }, display) != display) GameDisplay.Toggle();

            // Shown as a plain number, 1.0 = the default.
            float sensitivity = LookSettings.Sensitivity / LookSettings.DefaultSensitivity;
            float changed = OptionRow(panel, ref y, "MOUSE SENSITIVITY", sensitivity, LookSettings.MinSensitivity / LookSettings.DefaultSensitivity,
                LookSettings.MaxSensitivity / LookSettings.DefaultSensitivity, sensitivity.ToString("0.00"));
            if (!Mathf.Approximately(changed, sensitivity)) LookSettings.Sensitivity = Mathf.Round(changed * 20f) / 20f * LookSettings.DefaultSensitivity;

            float fov = LookSettings.Fov;
            changed = OptionRow(panel, ref y, "FIELD OF VIEW", fov, LookSettings.MinFov, LookSettings.MaxFov, Mathf.RoundToInt(fov) + "°");
            if (!Mathf.Approximately(changed, fov)) LookSettings.Fov = Mathf.Round(changed);

            // Everything the game plays, as a percentage.
            float volume = SoundSettings.Volume;
            changed = OptionRow(panel, ref y, "VOLUME", volume, 0f, 1f, Mathf.RoundToInt(volume * 100f).ToString());
            if (!Mathf.Approximately(changed, volume)) SoundSettings.Volume = Mathf.Round(changed * 20f) / 20f;

            float music = SoundSettings.Music;
            changed = OptionRow(panel, ref y, "MUSIC", music, 0f, 1f, Mathf.RoundToInt(music * 100f).ToString());
            if (!Mathf.Approximately(changed, music)) SoundSettings.Music = Mathf.Round(changed * 20f) / 20f;

            float voice = SoundSettings.Voice;
            changed = OptionRow(panel, ref y, "VOICES", voice, 0f, 1f, Mathf.RoundToInt(voice * 100f).ToString());
            if (!Mathf.Approximately(changed, voice)) SoundSettings.Voice = Mathf.Round(changed * 20f) / 20f;

            if (Hud.Button(new Rect(panel.x + 28f, panel.yMax - 66f, 220f, 46f), "RESET", small: true))
            {
                LookSettings.Sensitivity = LookSettings.DefaultSensitivity;
                LookSettings.Fov = LookSettings.DefaultFov;
                SoundSettings.Volume = SoundSettings.DefaultVolume;
                SoundSettings.Music = SoundSettings.DefaultMusic;
                SoundSettings.Voice = SoundSettings.DefaultVoice;
                PlayerPrefs.Save();
            }
            if (Hud.Button(new Rect(panel.xMax - 248f, panel.yMax - 66f, 220f, 46f), "KEYS", small: true)) _page = Page.Controls;
        }

        // ------------------------------------------------------------------ keys

        private const float ControlsWidth = 900f, ControlsHeight = 526f;
        private int _rebindRow = -1;
        private string[] _keyLabels;
        private int _keyLabelsVersion = -1;

        /// <summary>Every key that can be changed, in two columns: click one, then press the key it should be.</summary>
        private void DrawControlsPanel(Rect panel)
        {
            Hud.Panel(panel);
            Hud.Label(new Rect(panel.x + 28f, panel.y + 16f, panel.width - 56f, 44f), "KEYS", 36f, Color.white, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            GameInput.Rebind[] keys = GameInput.Rebindable;
            if (_keyLabels == null || _keyLabelsVersion != GameInput.BindingsVersion)
            {
                _keyLabelsVersion = GameInput.BindingsVersion;
                _keyLabels = new string[keys.Length];
                for (int i = 0; i < keys.Length; i++) _keyLabels[i] = GameInput.KeyLabel(keys[i].Action, keys[i].Binding).ToUpperInvariant();
            }
            bool waiting = GameInput.IsRebinding;
            if (!waiting) _rebindRow = -1;
            const float row = 46f;
            int perColumn = (keys.Length + 1) / 2;
            float column = (panel.width - 56f - 24f) * 0.5f;
            for (int i = 0; i < keys.Length; i++)
            {
                float x = panel.x + 28f + (i / perColumn) * (column + 24f), y = panel.y + 78f + (i % perColumn) * row;
                Hud.Label(new Rect(x, y, column - 190f, row - 8f), keys[i].Label, 18f, Hud.Muted, TextAnchor.MiddleLeft, wrap: true, shadow: false);
                var box = new Rect(x + column - 180f, y, 180f, row - 8f);
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
            Hud.Label(new Rect(panel.x + 270f, panel.yMax - 66f, panel.width - 298f, 46f),
                waiting ? "Press the new key or mouse button. Esc keeps the old one." : "Click a key to change it. Slots 1-6 and the gamepad stay as they are.",
                18f, Hud.Muted, TextAnchor.MiddleRight, wrap: true, shadow: false);
            GUI.enabled = !waiting;
            if (Hud.Button(new Rect(panel.x + 28f, panel.yMax - 66f, 220f, 46f), "RESET KEYS", small: true)) GameInput.ResetBindings();
            GUI.enabled = true;
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
            Hud.Label(new Rect(panel.x, panel.y + 16f, panelWidth, 50f), "INVITE A FRIEND", 40f, Color.white, heavy: true, shadow: false);
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
            Hud.Label(new Rect(0f, top, width, 90f), title, 76f, Color.white, heavy: true);
            if (noteHeight > 0f)
                Hud.Label(new Rect(width * 0.5f - 500f, top + 92f, 1000f, 60f), note, 20f, new Color(1f, 1f, 1f, 0.8f), TextAnchor.UpperCenter, wrap: true);
            x = (width - columnWidth) * 0.5f;
            return top + 110f + noteHeight;
        }

        // ------------------------------------------------------------------ guide

        private string[] _guide;
        private int _guideVersion = -1;

        private void DrawGuide()
        {
            const float width = 500f, height = 366f;
            var panel = new Rect(Hud.Width - width - 24f, 24f, width, height);
            Hud.Panel(panel);
            Hud.Label(new Rect(panel.x + 24f, panel.y + 14f, width - 48f, 40f), "BEACH GUIDE", 30f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            if (_guide == null || _guideVersion != GameInput.BindingsVersion)
            {
                // The player's own keys (OPTIONS, KEYS), written out once until one changes.
                _guideVersion = GameInput.BindingsVersion;
                GameInput.Rebind[] k = GameInput.Rebindable;
                string move = GameInput.KeyLabel(k[0].Action, k[0].Binding) + GameInput.KeyLabel(k[2].Action, k[2].Binding) +
                              GameInput.KeyLabel(k[1].Action, k[1].Binding) + GameInput.KeyLabel(k[3].Action, k[3].Binding);
                _guide = new[]
                {
                    GuideText(move.Length <= 4 ? move : "Keys", "Move"), GuideText(GameInput.KeyLabel(GameInput.Sprint), "Sprint"),
                    GuideText(GameInput.KeyLabel(GameInput.Interact), "Rescue / use"), GuideText(GameInput.KeyLabel(GameInput.Jump), "Jump"),
                    GuideText(GameInput.KeyLabel(GameInput.Primary), "Punch / fire"), GuideText(GameInput.KeyLabel(GameInput.Secondary), "CPR / aim"),
                    GuideText(GameInput.KeyLabel(GameInput.Reload), "Reload"), GuideText("1-6", "Inventory"),
                    GuideText(GameInput.KeyLabel(GameInput.Drop), "Drop (hold: throw)"), GuideText(GameInput.KeyLabel(GameInput.Emote), "Wave"),
                    GuideText("Tab", "Close guide"), GuideText("Esc", "Pause"),
                    GuideText("F11", "Fullscreen"), GuideText("Shift+Tab", "Steam")
                };
            }
            float y = panel.y + 64f, half = (width - 48f) * 0.5f;
            var white = new Color(1f, 1f, 1f, 0.92f);
            for (int i = 0; i < _guide.Length; i += 2)
            {
                if (i == 10) y += 10f;
                Hud.FitLabel(new Rect(panel.x + 24f, y, half - 8f, 32f), _guide[i], 19f, white, minimum: 15f);
                Hud.FitLabel(new Rect(panel.x + 24f + half + 10f, y, half - 8f, 32f), _guide[i + 1], 19f, white, minimum: 15f);
                y += 34f;
            }
            Hud.Label(new Rect(panel.x + 24f, panel.yMax - 40f, width - 48f, 28f), "Keys, mouse and sound: Esc, then OPTIONS", 18f, new Color(1f, 1f, 1f, 0.65f), TextAnchor.MiddleLeft, shadow: false);
        }

        private static string GuideText(string key, string what) => $"<color=#ffd24a>{key}</color>  {what}";

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
