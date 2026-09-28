using PleaseDontDrown.Core;
using PleaseDontDrown.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.UI
{
    /// <summary>Start screen, pause menu, and quick in-game guide.</summary>
    public class DevConnectMenu : MonoBehaviour
    {
        [SerializeField] private ConnectionService _connection;

        private bool _pauseOpen;
        private bool _guideOpen;
        private string _lanAddress = "localhost";
        private Vector2 _scroll;
        private int _windowWidth = 1280;
        private int _windowHeight = 720;
        private GUIStyle _title;
        private GUIStyle _subtitle;
        private GUIStyle _body;
        private GUIStyle _small;
        private GUIStyle _button;
        private string _hoveredButton;
        private string _hoveredThisFrame;

        private static readonly Color Coral = new(0.98f, 0.47f, 0.34f);
        private static readonly Color Teal = new(0.27f, 0.79f, 0.77f);
        private static readonly Color Dark = new(0.035f, 0.13f, 0.19f, 0.94f);

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
            if (DevConsole.IsOpen || AvatarCustomizer.IsOpen || SteamBootstrap.IsOverlayOpen || !_connection.IsActive) return;
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
                ToggleFullscreen();

            if (_pauseOpen && !_connection.IsActive)
                SetPause(false);
            if (!_connection.IsActive)
                _guideOpen = false;
        }

        private void ToggleFullscreen()
        {
            if (Screen.fullScreen)
                Screen.SetResolution(_windowWidth, _windowHeight, FullScreenMode.Windowed);
            else
            {
                _windowWidth = Screen.width;
                _windowHeight = Screen.height;
                Resolution desktop = Screen.currentResolution;
                Screen.SetResolution(desktop.width, desktop.height, FullScreenMode.FullScreenWindow);
            }
        }

        private void OnGUI()
        {
            bool active = _connection.IsActive;
            if (AvatarCustomizer.IsOpen || (active && !_pauseOpen && !_guideOpen))
                return;

            BuildStyles();
            if (Event.current.type == EventType.Repaint) _hoveredThisFrame = null;
            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Max(0.65f, Mathf.Min(Screen.width / 1280f, Screen.height / 720f));
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            float height = Screen.height / scale;

            if (_guideOpen && !_pauseOpen)
            {
                DrawGuide(width);
                _hoveredButton = null;
                GUI.matrix = previous;
                return;
            }

            DrawRect(new Rect(0f, 0f, width, height), new Color(0.01f, 0.08f, 0.13f, 0.74f));
            float panelWidth = Mathf.Min(480f, width - 30f);
            float panelHeight = active ? Mathf.Min(650f, height - 30f) : Mathf.Min(535f, height - 30f);
            Rect panel = new((width - panelWidth) * 0.5f, (height - panelHeight) * 0.5f, panelWidth, panelHeight);
            DrawRect(panel, Dark);
            DrawRect(new Rect(panel.x, panel.y, panel.width, 6f), Coral);

            GUILayout.BeginArea(new Rect(panel.x + 30f, panel.y + 24f, panel.width - 60f, panel.height - 44f));
            GUILayout.Label("PLEASE DON'T DROWN", _title);
            GUILayout.Label(active ? "PAUSED" : "ISLAND 1  /  LIFEGUARD START", _subtitle);
            GUILayout.Space(16f);
            _scroll = GUILayout.BeginScrollView(_scroll, false, false);

            if (!active) DrawStartMenu();
            else DrawPauseMenu();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (Event.current.type == EventType.Repaint) _hoveredButton = _hoveredThisFrame;
            GUI.matrix = previous;
        }

        private void DrawStartMenu()
        {
            GUILayout.Label("Rescue tourists. Return lost items. Find out what is happening on the beach.", _body);
            GUILayout.Space(16f);
            if (Button("PLAY", Coral, 48f)) _connection.Play();
            GUILayout.Label(SteamBootstrap.IsReady
                ? "Steam: " + SteamBootstrap.LocalName + "  |  Your Steam friends can join you."
                : "Steam isn't running: offline only. Start Steam to play with friends.", _small);
            DrawFriendsPlaying();
            DrawOverlayButton();
            GUILayout.Space(10f);

            GUILayout.Label("JOIN LOCAL / LAN GAME", _subtitle);
            GUILayout.BeginHorizontal();
            _lanAddress = GUILayout.TextField(_lanAddress, GUILayout.Height(34f));
            if (Button("JOIN", Teal, 34f, 94f)) _connection.JoinOffline(_lanAddress);
            GUILayout.EndHorizontal();
            GUILayout.Space(12f);

            if (Button("CUSTOMIZE LIFEGUARD", Color.white, 36f)) AvatarCustomizer.Open();
            if (Button(Screen.fullScreen ? "WINDOWED  /  F11" : "FULLSCREEN  /  F11", Color.white, 36f))
                ToggleFullscreen();

            if (!string.IsNullOrEmpty(_connection.LastError))
                GUILayout.Label(_connection.LastError, _small);
            GUILayout.Space(5f);
            GUILayout.Label("Move: WASD   Rescue / use: E   Pause: Esc   Guide: Tab", _small);
        }

        private void DrawPauseMenu()
        {
            var nm = _connection.NetworkManager;
            string role = nm.IsServerStarted ? "Host" : "Client";
            int players = nm.IsServerStarted ? nm.ServerManager.Clients.Count : nm.ClientManager.Clients.Count;
            GUILayout.Label(role + "  /  " + _connection.Mode + "  /  " + players + " players", _body);
            GUILayout.Space(12f);
            if (Button("RESUME", Coral, 48f)) SetPause(false);
            if (Button(Screen.fullScreen ? "WINDOWED  /  F11" : "FULLSCREEN  /  F11", Color.white, 36f))
                ToggleFullscreen();
            if (SteamLobbyService.InLobby)
            {
                if (SteamBootstrap.OverlayAvailable && Button("INVITE FRIENDS  /  STEAM", Teal, 36f))
                    _connection.InviteFriends();
                DrawInviteList();
            }
            else if (SteamBootstrap.IsReady)
                GUILayout.Label("Offline session: friends can't join it. Leave and press PLAY to host through Steam.", _small);
            DrawOverlayButton();

            if (Dev.DevIsland.Instance != null)
            {
                GUILayout.Space(12f);
                GUILayout.Label("TRAVEL", _subtitle);
                for (int i = 0; i < Dev.DevIsland.DestinationNames.Length; i++)
                {
                    if (!Button(Dev.DevIsland.DestinationNames[i], Color.white, 30f)) continue;
                    SetPause(false);
                    Dev.DevIsland.Travel((Dev.Destination)i);
                }
            }
            if (nm.IsServerStarted && Story.StoryDirector.Instance != null)
            {
                GUILayout.Space(10f);
                if (Button("RESTART STORY FROM CHAPTER 1", Color.white, 32f))
                {
                    SetPause(false);
                    DevCommands.Execute("story reset");
                }
            }

            GUILayout.Space(12f);
            if (Button("CUSTOMIZE LIFEGUARD", Color.white, 34f)) AvatarCustomizer.Open();
            if (Button(nm.IsServerStarted ? "END SESSION" : "LEAVE SESSION", Color.white, 34f))
            {
                SetPause(false);
                _connection.Leave();
            }
            GUILayout.Space(8f);
            GUILayout.Label("WASD move  |  E use  |  R reload  |  F inspect  |  Tab guide", _small);
        }

        /// <summary>Friends in this game with a session to join (a Steam invite or their friends list works too).</summary>
        private void DrawFriendsPlaying()
        {
            if (!SteamBootstrap.IsReady) return;
            bool any = false;
            foreach (SteamLobbyService.Friend friend in SteamLobbyService.Friends())
            {
                if (!friend.InGame) break; // sorted: the ones in this game come first
                if (!any)
                {
                    GUILayout.Space(8f);
                    GUILayout.Label("FRIENDS PLAYING", _subtitle);
                    any = true;
                }
                GUILayout.BeginHorizontal();
                string state = !friend.SameVersion ? "  (other build)" : friend.Lobby == Steamworks.CSteamID.Nil ? "  (in the menu)" : string.Empty;
                GUILayout.Label(friend.Name + state, _body, GUILayout.Height(32f));
                GUI.enabled = friend.SameVersion && friend.Lobby != Steamworks.CSteamID.Nil;
                if (Button("JOIN", Teal, 32f, 94f)) _connection.JoinFriend(friend.Lobby);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Online Steam friends to invite: Steam sends them the invite, no overlay needed.</summary>
        private void DrawInviteList()
        {
            if (!SteamBootstrap.IsReady) return;
            var friends = SteamLobbyService.Friends();
            GUILayout.Space(8f);
            GUILayout.Label("INVITE A FRIEND", _subtitle);
            if (friends.Count == 0)
            {
                GUILayout.Label("No Steam friends online right now.", _small);
                return;
            }
            int shown = 0;
            foreach (SteamLobbyService.Friend friend in friends)
            {
                if (SteamLobbyService.IsMember(friend.Id)) continue;
                if (++shown > 12) break;
                GUILayout.BeginHorizontal();
                GUILayout.Label(friend.InGame ? friend.Name + "  (playing)" : friend.Name, _body, GUILayout.Height(30f));
                bool invited = SteamLobbyService.RecentlyInvited(friend.Id);
                GUI.enabled = !invited;
                if (Button(invited ? "SENT" : "INVITE", Teal, 30f, 94f)) SteamLobbyService.Invite(friend.Id);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("They accept it in Steam; the game must already be running on their PC.", _small);
        }

        private void DrawOverlayButton()
        {
            if (!SteamBootstrap.IsReady) return;
            GUI.enabled = SteamBootstrap.OverlayAvailable;
            if (Button("OPEN STEAM FRIENDS  /  SHIFT+TAB", Teal, 34f)) SteamBootstrap.OpenFriendsOverlay();
            GUI.enabled = true;
            if (!SteamBootstrap.OverlayAvailable)
                GUILayout.Label("No Steam overlay: start the game from your Steam library (Games > Add a Non-Steam Game).", _small);
        }

        private void DrawGuide(float width)
        {
            Rect panel = new(width - 380f, 18f, 360f, 255f);
            DrawRect(panel, Dark);
            DrawRect(new Rect(panel.x, panel.y, panel.width, 5f), Teal);
            GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 18f, panel.width - 40f, panel.height - 28f));
            GUILayout.Label("BEACH GUIDE", _subtitle);
            GUILayout.Space(8f);
            GUILayout.Label("WASD  Move       Shift  Sprint", _body);
            GUILayout.Label("E  Rescue / interact       Space  Jump", _body);
            GUILayout.Label("Mouse  Aim / fire       R  Reload", _body);
            GUILayout.Label("F  Inspect gun       1-4  Inventory", _body);
            GUILayout.Space(8f);
            GUILayout.Label("Tab  Close guide       Esc  Pause", _small);
            GUILayout.Label("F11  Fullscreen       Shift+Tab  Steam", _small);
            GUILayout.EndArea();
        }

        private void BuildStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _title.normal.textColor = Color.white;
            _subtitle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            _subtitle.normal.textColor = Teal;
            _body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            _body.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true };
            _small.normal.textColor = new Color(0.76f, 0.88f, 0.9f);
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter, margin = new RectOffset(0, 0, 3, 3)
            };
        }

        private bool Button(string label, Color color, float height, float width = 0f)
        {
            Color before = GUI.backgroundColor;
            GUI.backgroundColor = color;
            bool pressed = width > 0f
                ? GUILayout.Button(label, _button, GUILayout.Width(width), GUILayout.Height(height))
                : GUILayout.Button(label, _button, GUILayout.Height(height));
            GUI.backgroundColor = before;
            if (Event.current.type == EventType.Repaint && GUI.enabled &&
                GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition))
            {
                _hoveredThisFrame = label;
                if (_hoveredButton != label)
                    BeachAudio.PlayLocal(BeachAudio.MenuHover, 0.45f);
            }
            if (pressed) BeachAudio.PlayLocal(BeachAudio.MenuSelect, 0.7f);
            return pressed;
        }

        private static void DrawRect(Rect rect, Color color)
        {
            Color before = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = before;
        }
    }
}