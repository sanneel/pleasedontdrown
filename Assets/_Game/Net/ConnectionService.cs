using System;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using PleaseDontDrown.Core;
using Steamworks;
using UnityEngine;
using SteamTransport = FishySteamworks.FishySteamworks;

namespace PleaseDontDrown.Net
{
    public enum ConnectionMode
    {
        None,
        /// <summary>Solo: host + client over localhost (identical code path to multiplayer).</summary>
        Offline,
        /// <summary>Steam P2P via a friends-only lobby.</summary>
        Steam
    }

    /// <summary>
    /// The only place that starts or stops a session. Multipass holds two transports:
    /// Tugboat (localhost, solo/LAN testing) and FishySteamworks (online).
    /// </summary>
    public class ConnectionService : MonoBehaviour
    {
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private Multipass _multipass;
        [SerializeField] private SteamLobbyService _lobby;
        [SerializeField, Range(1, 4)] private int _maxPlayers = 4;

        public static ConnectionService Instance { get; private set; }
        public ConnectionMode Mode { get; private set; }
        public string LastError { get; private set; } = string.Empty;
        public NetworkManager NetworkManager => _networkManager;
        public bool IsActive => _networkManager.IsServerStarted || _networkManager.IsClientStarted;

        public event Action SessionEnded;

        private int _tugboatIndex = -1;
        private int _steamIndex = -1;

        private void Awake()
        {
            Instance = this;
            for (int i = 0; i < _multipass.Transports.Count; i++)
            {
                if (_multipass.Transports[i] is Tugboat) _tugboatIndex = i;
                else if (_multipass.Transports[i] is SteamTransport) _steamIndex = i;
            }
            if (_tugboatIndex < 0 || _steamIndex < 0)
                Debug.LogError("[Net] Multipass must contain both Tugboat and FishySteamworks.");

            _lobby.HostLobbyReady += OnHostLobbyReady;
            _lobby.JoinedLobby += OnJoinedLobby;
            _lobby.LobbyFailed += OnLobbyFailed;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnRemoteConnectionState(FishNet.Connection.NetworkConnection conn, RemoteConnectionStateArgs args) =>
            Debug.Log($"[Net] Remote client {conn.ClientId} {args.ConnectionState} (transport {args.TransportIndex})");

        private void OnDestroy()
        {
            if (_lobby != null)
            {
                _lobby.HostLobbyReady -= OnHostLobbyReady;
                _lobby.JoinedLobby -= OnJoinedLobby;
                _lobby.LobbyFailed -= OnLobbyFailed;
            }
            if (_networkManager != null)
            {
                _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }
        }

        // ---------- Public API ----------

        public bool HostOffline()
        {
            if (IsActive) return false;
            Mode = ConnectionMode.Offline;
            var tugboat = (Tugboat)_multipass.GetTransport(_tugboatIndex);
            tugboat.SetClientAddress("localhost");
            _multipass.SetClientTransport(_tugboatIndex);
            if (!_multipass.StartConnection(true, _tugboatIndex) || !_multipass.StartConnection(false, _tugboatIndex))
                return Fail("Could not start local host.");
            return true;
        }

        /// <summary>Joins a Tugboat host on this PC or LAN. Handy for testing two game instances without Steam.</summary>
        public bool JoinOffline(string address = "localhost")
        {
            if (IsActive) return false;
            Mode = ConnectionMode.Offline;
            ((Tugboat)_multipass.GetTransport(_tugboatIndex)).SetClientAddress(address);
            _multipass.SetClientTransport(_tugboatIndex);
            if (!_multipass.StartConnection(false, _tugboatIndex))
                return Fail($"Could not connect to {address}.");
            return true;
        }

        /// <summary>Creates a friends-only Steam lobby; the server starts once Steam confirms it.</summary>
        public bool HostSteam()
        {
            if (IsActive) return false;
            if (!SteamBootstrap.IsReady) return Fail("Steam is not running.");
            Mode = ConnectionMode.Steam;
            return _lobby.CreateLobby(_maxPlayers) || Fail("Could not request a Steam lobby.");
        }

        public void InviteFriends() => _lobby.OpenInviteOverlay();

        public void Leave()
        {
            if (_networkManager.IsClientStarted)
                _multipass.StopConnection(false);
            if (_networkManager.IsServerStarted)
            {
                _multipass.StopConnection(true, _tugboatIndex);
                _multipass.StopConnection(true, _steamIndex);
            }
            _lobby.Leave();
            Mode = ConnectionMode.None;
        }

        // ---------- Steam lobby → transport ----------

        private void OnHostLobbyReady(CSteamID lobby)
        {
            var steam = (SteamTransport)_multipass.GetTransport(_steamIndex);
            steam.SetMaximumClients(_maxPlayers);
            _multipass.SetClientTransport(_steamIndex);
            // With the server running, FishySteamworks routes the host's own client through its in-memory "client host" socket.
            if (!_multipass.StartConnection(true, _steamIndex) || !_multipass.StartConnection(false, _steamIndex))
            {
                _lobby.Leave();
                Fail("Could not start Steam host.");
            }
        }

        private void OnJoinedLobby(CSteamID lobby, CSteamID owner)
        {
            if (IsActive)
                Leave();
            Mode = ConnectionMode.Steam;
            var steam = (SteamTransport)_multipass.GetTransport(_steamIndex);
            steam.SetClientAddress(owner.m_SteamID.ToString());
            _multipass.SetClientTransport(_steamIndex);
            if (!_multipass.StartConnection(false, _steamIndex))
            {
                _lobby.Leave();
                Fail("Could not connect to the host over Steam.");
            }
        }

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            Debug.Log($"[Net] Local client {args.ConnectionState} ({Mode})");
            if (args.ConnectionState != LocalConnectionState.Stopped)
                return;
            // Host quit, we were kicked, or we left: make sure the lobby and server are closed too.
            if (_networkManager.IsServerStarted)
            {
                _multipass.StopConnection(true, _tugboatIndex);
                _multipass.StopConnection(true, _steamIndex);
            }
            _lobby.Leave();
            Mode = ConnectionMode.None;
            SessionEnded?.Invoke();
        }

        private void OnLobbyFailed(string message)
        {
            Mode = ConnectionMode.None;
            Fail(message);
        }

        private bool Fail(string message)
        {
            LastError = message;
            Debug.LogWarning($"[Net] {message}");
            return false;
        }
    }
}
