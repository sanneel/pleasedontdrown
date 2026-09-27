using PleaseDontDrown.Core;
using PleaseDontDrown.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Temporary IMGUI connection menu for M0 testing. F1 toggles it while in a session.
    /// Replaced by the real main menu later.
    /// </summary>
    public class DevConnectMenu : MonoBehaviour
    {
        [SerializeField] private ConnectionService _connection;

        private bool _visibleInSession;
        private string _lanAddress = "localhost";
        private GUIStyle _title;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                _visibleInSession = !_visibleInSession;
                Cursor.lockState = _visibleInSession ? CursorLockMode.None : CursorLockMode.Locked;
            }
        }

        private void OnGUI()
        {
            bool active = _connection.IsActive;
            if (active && !_visibleInSession)
            {
                GUI.Label(new Rect(10, 10, 400, 22), "F1: connection menu");
                return;
            }

            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(20, 20, 360, 420), GUI.skin.box);
            GUILayout.Label("PLEASE DON'T DROWN", _title);
            GUILayout.Label("M0 network test build");
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
                if (_connection.Mode == ConnectionMode.Steam && GUILayout.Button("Invite friends", GUILayout.Height(28)))
                    _connection.InviteFriends();
                if (GUILayout.Button("Leave", GUILayout.Height(28)))
                {
                    _visibleInSession = false;
                    _connection.Leave();
                }
            }

            if (!string.IsNullOrEmpty(_connection.LastError))
            {
                GUILayout.Space(6);
                GUILayout.Label($"<color=#ff8080>{_connection.LastError}</color>");
            }
            GUILayout.EndArea();
        }
    }
}
