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
        private enum Page { Main, Join, Invite, Travel, ConfirmLeave, ConfirmQuit }

        [SerializeField] private ConnectionService _connection;

        private const float ButtonWidth = 320f, ButtonHeight = 60f, ButtonGap = 10f;

        private bool _pauseOpen;
        private bool _guideOpen;
        private Page _page;
        private string _lanAddress = "localhost";
        private GUIStyle _field;
        private int _fieldPx;
        private Texture2D _fade;
        private string _hoveredButton;
        private string _hoveredThisFrame;

        private void OnEnable()
        {
            GameInput.ToggleMenu.performed += OnToggleMenu;
            GameInput.ToggleOverlay.performed += OnToggleOverlay;
        }

        private void OnDisable()
        {
            GameInput.ToggleMenu.performed -= OnToggleMenu;
            GameInput.ToggleOverlay.performed -= OnToggleOverlay;
            SetPause(false);
        }

        private void OnToggleMenu(InputAction.CallbackContext _)
        {
            if (DevConsole.IsOpen || AvatarCustomizer.IsOpen || SteamBootstrap.IsOverlayOpen) return;
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
            if (!_connection.IsActive)
                _guideOpen = false;
        }

        private void OnGUI()
        {
            bool active = _connection.IsActive;
            if (AvatarCustomizer.IsOpen || (active && !_pauseOpen && !_guideOpen))
                return;

            if (Event.current.type == EventType.Repaint) _hoveredThisFrame = null;
            if (_guideOpen && !_pauseOpen) DrawGuide();
            else if (!active) DrawStartMenu();
            else DrawPauseMenu();
            if (Event.current.type == EventType.Repaint) _hoveredButton = _hoveredThisFrame;
        }

        // ------------------------------------------------------------------ start screen

        private void DrawStartMenu()
        {
            float width = Hud.Width, height = Hud.Height;
            // The island stays in view: only the left, where the words are, is shaded.
            if (Event.current.type == EventType.Repaint)
                GUI.DrawTexture(Hud.Px(new Rect(0f, 0f, Mathf.Min(width, 1250f), height)), Fade(), ScaleMode.StretchToFill, true, 0f, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.82f), 0f, 0f);

            Hud.Label(new Rect(56f, 30f, 900f, 150f), "PLEASE", 136f, Color.white, TextAnchor.MiddleLeft, heavy: true);
            Hud.Label(new Rect(56f, 156f, 900f, 150f), "DON'T", 136f, Color.white, TextAnchor.MiddleLeft, heavy: true);
            Hud.Label(new Rect(56f, 282f, 900f, 150f), "DROWN", 136f, Hud.Coral, TextAnchor.MiddleLeft, heavy: true);
            Hud.Label(new Rect(62f, 436f, 640f, 70f), "Rescue tourists. Return lost items.\nFind out what is happening on the beach.", 24f, Hud.Sand, TextAnchor.UpperLeft);

            // Buttons, stacked up from the bottom left.
            const int count = 5;
            float x = 40f, y = height - 40f - count * ButtonHeight - (count - 1) * ButtonGap;
            if (!string.IsNullOrEmpty(_connection.LastError))
                Hud.Label(new Rect(x, y - 74f, 900f, 60f), _connection.LastError, 22f, Hud.Coral, TextAnchor.LowerLeft, wrap: true);

            if (Button(Row(x, ref y), "PLAY", primary: true)) _connection.Play();
            if (Button(Row(x, ref y), "JOIN GAME", selected: _page == Page.Join)) _page = _page == Page.Join ? Page.Main : Page.Join;
            if (Button(Row(x, ref y), "CHARACTER")) AvatarCustomizer.Open();
            if (Button(Row(x, ref y), GameDisplay.IsFullscreen ? "WINDOWED" : "FULLSCREEN")) GameDisplay.Toggle();
            if (Button(Row(x, ref y), "QUIT")) Application.Quit();

            if (_page == Page.Join) DrawJoinPanel(x + ButtonWidth + 30f, height - 40f);

            string steam = SteamBootstrap.IsReady
                ? "Steam: " + SteamBootstrap.LocalName + "   Your Steam friends can join you."
                : "Steam isn't running: offline only. Start Steam to play with friends.";
            Hud.Label(new Rect(width - 915f, 14f, 900f, 30f), steam, 20f, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperRight);
            Hud.Label(new Rect(width - 615f, height - 40f, 600f, 30f), NetVersion.Current, 18f, new Color(1f, 1f, 1f, 0.6f), TextAnchor.LowerRight);
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
            Hud.Fill(panel, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.8f), 16f);
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
                    Hud.Label(new Rect(x + 24f, y, width - 190f, row - 8f), friend.Name + state, 24f, Color.white, TextAnchor.MiddleLeft, shadow: false);
                    GUI.enabled = joinable;
                    if (Button(new Rect(x + width - 144f, y, 120f, row - 8f), "JOIN", small: true)) _connection.JoinFriend(friend.Lobby);
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
            if (Button(new Rect(x + width - 144f, y, 120f, 48f), "JOIN", small: true)) _connection.JoinOffline(_lanAddress);
            y += 60f;
            Hud.Label(new Rect(x + 24f, y, width - 48f, 50f), "The host's address on your network (localhost for a second copy on this PC).",
                17f, new Color(1f, 1f, 1f, 0.65f), TextAnchor.UpperLeft, wrap: true, shadow: false);
        }

        // ------------------------------------------------------------------ pause

        private void DrawPauseMenu()
        {
            float width = Hud.Width, height = Hud.Height;
            Hud.Fill(new Rect(0f, 0f, width, height), new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.66f));
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
                        if (!Button(Row(wide, ref y, 440f), Dev.DevIsland.DestinationNames[i].ToUpperInvariant(), centred: true)) continue;
                        SetPause(false);
                        Dev.DevIsland.Travel((Dev.Destination)i);
                        return;
                    }
                    if (Button(Row(wide, ref y, 440f), "BACK", centred: true)) _page = Page.Main;
                    return;
                }
                case Page.ConfirmLeave:
                case Page.ConfirmQuit:
                {
                    bool quit = _page == Page.ConfirmQuit;
                    float y = Header(quit ? "QUIT THE GAME?" : host ? "END THE SESSION?" : "LEAVE THE SESSION?",
                        host ? "Everyone playing with you is sent back to the menu." : null, 2, ButtonWidth, out _);
                    if (Button(Row(x, ref y), "YES", centred: true))
                    {
                        SetPause(false);
                        _connection.Leave();
                        if (quit) Application.Quit();
                        return;
                    }
                    if (Button(Row(x, ref y), "NO", centred: true)) _page = Page.Main;
                    return;
                }
            }

            bool travel = Dev.DevIsland.Instance != null;
            bool story = host && Story.StoryDirector.Instance != null;
            int players = host ? nm.ServerManager.Clients.Count : nm.ClientManager.Clients.Count;
            string info = (host ? "Host" : "Client") + "   " + _connection.Mode + "   " + players + (players == 1 ? " player" : " players");
            if (!SteamLobbyService.InLobby && SteamBootstrap.IsReady)
                info += "\nOffline session: friends can't join it. Leave and press PLAY to host through Steam.";
            int rows = 5 + (SteamLobbyService.InLobby ? 1 : 0) + (travel ? 1 : 0) + (story ? 1 : 0);
            float top = Header("PAUSED", info, rows, ButtonWidth, out _);

            if (Button(Row(x, ref top), "RESUME", centred: true, primary: true)) SetPause(false);
            if (SteamLobbyService.InLobby && Button(Row(x, ref top), "INVITE FRIENDS", centred: true)) _page = Page.Invite;
            if (Button(Row(x, ref top), "CHARACTER", centred: true)) AvatarCustomizer.Open();
            if (Button(Row(x, ref top), GameDisplay.IsFullscreen ? "WINDOWED" : "FULLSCREEN", centred: true)) GameDisplay.Toggle();
            if (travel && Button(Row(x, ref top), "TRAVEL", centred: true)) _page = Page.Travel;
            if (story && Button(Row(x, ref top), "RESTART STORY", centred: true))
            {
                SetPause(false);
                DevCommands.Execute("story reset");
                return;
            }
            if (Button(Row(x, ref top), "MAIN MENU", centred: true)) _page = Page.ConfirmLeave;
            if (Button(Row(x, ref top), "QUIT", centred: true)) _page = Page.ConfirmQuit;
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
            Hud.Fill(panel, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.85f), 16f);
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
                Hud.Label(new Rect(panel.x + 24f, y, panelWidth - 200f, row - 8f), friend.InGame ? friend.Name + "  (playing)" : friend.Name, 24f, Color.white, TextAnchor.MiddleLeft, shadow: false);
                bool invited = SteamLobbyService.RecentlyInvited(friend.Id);
                GUI.enabled = !invited;
                if (Button(new Rect(panel.xMax - 164f, y, 140f, row - 8f), invited ? "SENT" : "INVITE", small: true)) SteamLobbyService.Invite(friend.Id);
                GUI.enabled = true;
                y += row;
            }
            Hud.Label(new Rect(panel.x + 24f, y, panelWidth - 48f, 50f), "They accept it in Steam; the game must already be running on their PC.",
                17f, new Color(1f, 1f, 1f, 0.65f), TextAnchor.UpperLeft, wrap: true, shadow: false);
            y = panel.yMax - 76f;
            float half = (panelWidth - 48f - ButtonGap) * 0.5f;
            GUI.enabled = SteamBootstrap.OverlayAvailable;
            if (Button(new Rect(panel.x + 24f, y, half, 56f), "STEAM OVERLAY", centred: true, small: true)) _connection.InviteFriends();
            GUI.enabled = true;
            if (Button(new Rect(panel.x + 24f + half + ButtonGap, y, half, 56f), "BACK", centred: true, small: true)) _page = Page.Main;
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

        private void DrawGuide()
        {
            const float width = 470f, height = 330f;
            var panel = new Rect(Hud.Width - width - 24f, 24f, width, height);
            Hud.Fill(panel, new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, 0.85f), 16f);
            Hud.Label(new Rect(panel.x + 24f, panel.y + 14f, width - 48f, 40f), "BEACH GUIDE", 30f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            float y = panel.y + 64f;
            GuideRow(panel, ref y, "WASD", "Move", "Shift", "Sprint");
            GuideRow(panel, ref y, "E", "Rescue / use", "Space", "Jump");
            GuideRow(panel, ref y, "Mouse", "Aim / fire", "R", "Reload");
            GuideRow(panel, ref y, "F", "Inspect gun", "1-6", "Inventory");
            GuideRow(panel, ref y, "G", "Drop (hold: throw)", "Z / C", "Skin");
            y += 10f;
            GuideRow(panel, ref y, "Tab", "Close guide", "Esc", "Pause");
            GuideRow(panel, ref y, "F11", "Fullscreen", "Shift+Tab", "Steam");
        }

        private static void GuideRow(Rect panel, ref float y, string keyA, string whatA, string keyB, string whatB)
        {
            float half = (panel.width - 48f) * 0.5f;
            var white = new Color(1f, 1f, 1f, 0.92f);
            Hud.Label(new Rect(panel.x + 24f, y, half, 32f), $"<color=#ffd24a>{keyA}</color>  {whatA}", 20f, white, TextAnchor.MiddleLeft, shadow: false);
            Hud.Label(new Rect(panel.x + 24f + half + 10f, y, half, 32f), $"<color=#ffd24a>{keyB}</color>  {whatB}", 20f, white, TextAnchor.MiddleLeft, shadow: false);
            y += 34f;
        }

        // ------------------------------------------------------------------ parts

        /// <summary>The next button's box in a column, moving <paramref name="y"/> down past it.</summary>
        private static Rect Row(float x, ref float y, float width = ButtonWidth)
        {
            var r = new Rect(x, y, width, ButtonHeight);
            y += ButtonHeight + ButtonGap;
            return r;
        }

        /// <summary>
        /// A wide dark button with heavy white lettering. Under the mouse it gets a white frame and nudges right
        /// (hover and click sounds included); <paramref name="primary"/> is the coral one you'd press first.
        /// </summary>
        private bool Button(Rect r, string label, bool centred = false, bool primary = false, bool selected = false, bool small = false)
        {
            bool enabled = GUI.enabled;
            bool hover = enabled && Hud.Hovered(r);
            if (Event.current.type == EventType.Repaint)
            {
                Rect box = hover && !centred && !small ? new Rect(r.x + 8f, r.y, r.width, r.height) : r;
                float radius = Mathf.Min(14f, r.height * 0.25f);
                Color fill = primary ? new Color(Hud.Coral.r, Hud.Coral.g, Hud.Coral.b, hover ? 1f : 0.9f)
                    : small ? new Color(Hud.Teal.r * 0.6f, Hud.Teal.g * 0.6f, Hud.Teal.b * 0.6f, hover ? 1f : 0.85f)
                    : new Color(Hud.Ink.r, Hud.Ink.g, Hud.Ink.b, hover || selected ? 0.9f : 0.6f);
                if (!enabled) fill.a *= 0.4f;
                Hud.Fill(box, fill, radius);
                if (hover || selected) Hud.Frame(box, Color.white, 3f, radius);
                Color text = !enabled ? new Color(1f, 1f, 1f, 0.45f) : hover && !primary && !small ? Hud.Sand : Color.white;
                Rect words = centred || small ? box : new Rect(box.x + 24f, box.y, box.width - 36f, box.height);
                Hud.Label(words, label, small ? 22f : 30f, text, centred || small ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, heavy: true, shadow: false);

                if (hover)
                {
                    string id = label + "@" + Mathf.RoundToInt(r.x) + "," + Mathf.RoundToInt(r.y);
                    _hoveredThisFrame = id;
                    if (_hoveredButton != id) BeachAudio.PlayLocal(BeachAudio.MenuHover, 0.45f);
                }
            }
            bool pressed = GUI.Button(Hud.Px(r), GUIContent.none, GUIStyle.none);
            if (pressed) BeachAudio.PlayLocal(BeachAudio.MenuSelect, 0.7f);
            return pressed;
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
        }
    }
}
