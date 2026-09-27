using PleaseDontDrown.Core;
using PleaseDontDrown.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Temporary IMGUI main/pause menu. Always shown when not in a session; Esc toggles it in-game.
    /// Replaced by the real menus later.
    /// </summary>
    public class DevConnectMenu : MonoBehaviour
    {
        [SerializeField] private ConnectionService _connection;

        private bool _pauseOpen;
        private string _lanAddress = "localhost";
        private GUIStyle _title;

        private void OnEnable() => GameInput.ToggleMenu.performed += OnToggleMenu;

        private void OnDisable()
        {
            GameInput.ToggleMenu.performed -= OnToggleMenu;
            SetPause(false);
        }

        private void OnToggleMenu(InputAction.CallbackContext _)
        {
            if (DevConsole.IsOpen || AvatarCustomizer.IsOpen || !_connection.IsActive) return; // Esc closes those first
            SetPause(!_pauseOpen);
        }

        private void SetPause(bool open)
        {
            if (open == _pauseOpen) return;
            _pauseOpen = open;
            if (open) GameInput.PushUI();
            else GameInput.PopUI();
        }

        private void Update()
        {
            // Session ended while paused (host left, kicked...): drop the pause blocker.
            if (_pauseOpen && !_connection.IsActive)
                SetPause(false);
        }

        private void OnGUI()
        {
            bool active = _connection.IsActive;
            if ((active && !_pauseOpen) || AvatarCustomizer.IsOpen)
                return;

            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(20, 20, 360, 480), GUI.skin.box);
            GUILayout.Label("PLEASE DON'T DROWN", _title);
            GUILayout.Label(active ? "Paused" : $"Prototype build {NetVersion.Current}");
            GUILayout.Space(6);
            GUILayout.Label(SteamBootstrap.IsReady ? $"Steam: {SteamBootstrap.LocalName}" : "Steam: not running (offline only)");

            if (!active)
            {
                GUI.enabled = SteamBootstrap.IsReady;
                if (GUILayout.Button("Host (Steam, friends only)", GUILayout.Height(32))) _connection.HostSteam();
                GUI.enabled = true;
                GUILayout.Label("Join: accept a Steam invite, or right-click a friend > Join Game.");
                GUILayout.Space(8);
                if (GUILayout.Button("Play solo (local host)", GUILayout.Height(28))) _connection.HostOffline();
                GUILayout.BeginHorizontal();
                _lanAddress = GUILayout.TextField(_lanAddress, GUILayout.Width(180));
                if (GUILayout.Button("Join local/LAN")) _connection.JoinOffline(_lanAddress);
                GUILayout.EndHorizontal();
            }
            else
            {
                var nm = _connection.NetworkManager;
                string role = nm.IsServerStarted ? "Host" : "Client";
                int players = nm.IsServerStarted ? nm.ServerManager.Clients.Count : nm.ClientManager.Clients.Count;
                GUILayout.Label($"{role} · {_connection.Mode} · players: {players}");
                if (!nm.IsServerStarted)
                    GUILayout.Label($"Ping: {nm.TimeManager.RoundTripTime} ms");
                if (SteamLobbyService.InLobby)
                    GUILayout.Label($"Lobby: {SteamLobbyService.CurrentLobby.m_SteamID}");
                GUILayout.Space(6);
                if (GUILayout.Button("Resume", GUILayout.Height(30))) SetPause(false);
                if (_connection.Mode == ConnectionMode.Steam && GUILayout.Button("Invite friends", GUILayout.Height(28)))
                    _connection.InviteFriends();
                if (GUILayout.Button(nm.IsServerStarted ? "End session" : "Leave", GUILayout.Height(28)))
                {
                    SetPause(false);
                    _connection.Leave();
                }
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Customize your lifeguard", GUILayout.Height(28))) AvatarCustomizer.Open();
            GUILayout.Space(8);
            GUILayout.Label("<color=#aaaaaa>WASD move · Shift sprint · Ctrl crouch · Space jump · E use · ` console</color>",
                new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });

            if (!string.IsNullOrEmpty(_connection.LastError))
                GUILayout.Label($"<color=#ff8080>{_connection.LastError}</color>", new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true });
            GUILayout.EndArea();
        }
    }
}
