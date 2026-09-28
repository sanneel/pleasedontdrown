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
        // Play(): a Steam lobby was asked for; if Steam can't make one, play offline instead of failing.
        private bool _offlineIfLobbyFails;
        // A friend's lobby to join once the current session has shut down.
        private CSteamID _pendingJoin = CSteamID.Nil;

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

            // Tests use their own port so they can never join a game someone is playing on this PC.
            if (ushort.TryParse(DevLaunchArgs.Value("-pdd-port"), out ushort port))
            {
                ((Tugboat)_multipass.GetTransport(_tugboatIndex)).SetPort(port);
                Debug.Log($"[Net] Local port {port}");
            }
            LobbyAuthenticator.Rejected += OnRejected;

            _lobby.HostLobbyReady += OnHostLobbyReady;
            _lobby.JoinedLobby += OnJoinedLobby;
            _lobby.LobbyFailed += OnLobbyFailed;
            _lobby.JoinRequested += JoinFriend;
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
        }

        private void OnRemoteConnectionState(FishNet.Connection.NetworkConnection conn, RemoteConnectionStateArgs args) =>
            Debug.Log($"[Net] Remote client {conn.ClientId} {args.ConnectionState} (transport {args.TransportIndex})");

        private void OnRejected(string reason) => Fail($"Host refused the connection: {reason}");

        private void OnDestroy()
        {
            LobbyAuthenticator.Rejected -= OnRejected;
            if (_lobby != null)
            {
                _lobby.HostLobbyReady -= OnHostLobbyReady;
                _lobby.JoinedLobby -= OnJoinedLobby;
                _lobby.LobbyFailed -= OnLobbyFailed;
                _lobby.JoinRequested -= JoinFriend;
            }
            if (_networkManager != null)
            {
                _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
                _networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
            }
        }

        // ---------- Public API ----------

        /// <summary>
        /// The menu's Play: with Steam running, a friends-only Steam session (friends join from their Steam friends
        /// list, an invite, or the menu's list of friends playing); otherwise, or if Steam can't make a lobby, offline.
        /// </summary>
        public bool Play()
        {
            if (IsActive || _offlineIfLobbyFails) return false; // a lobby is already on its way
            LastError = string.Empty;
            if (!SteamBootstrap.IsReady) return HostOffline();
            _offlineIfLobbyFails = true;
            if (HostSteam()) return true;
            _offlineIfLobbyFails = false;
            return HostOffline();
        }

        /// <summary>Joins a friend's Steam lobby, ending the current session first if there is one.</summary>
        public void JoinFriend(CSteamID lobby)
        {
            if (!SteamBootstrap.IsReady || lobby == CSteamID.Nil || lobby == SteamLobbyService.CurrentLobby) return;
            LastError = string.Empty;
            if (!IsActive)
            {
                _lobby.Join(lobby);
                return;
            }
            // Leaving is finished when the local client reports Stopped (OnClientConnectionState joins then).
            _pendingJoin = lobby;
            Leave();
            if (!IsActive) JoinPending();
        }

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
            bool offlineIfFails = _offlineIfLobbyFails;
            _offlineIfLobbyFails = false;
            if (IsActive)
            {
                _lobby.Leave(); // something else started meanwhile (a LAN join)
                return;
            }
            var steam = (SteamTransport)_multipass.GetTransport(_steamIndex);
            steam.SetMaximumClients(_maxPlayers);
            _multipass.SetClientTransport(_steamIndex);
            // With the server running, FishySteamworks routes the host's own client through its in-memory "client host" socket.
            if (!_multipass.StartConnection(true, _steamIndex) || !_multipass.StartConnection(false, _steamIndex))
            {
                _lobby.Leave();
                Fail("Could not start Steam host.");
                if (offlineIfFails) HostOffline();
            }
        }

        private void OnJoinedLobby(CSteamID lobby, CSteamID owner)
        {
            if (IsActive)
            {
                // Still in a session (should have ended first): end it, then come back to this lobby.
                _pendingJoin = lobby;
                Leave();
                if (!IsActive) JoinPending();
                return;
            }
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
            JoinPending();
        }

        private void JoinPending()
        {
            if (_pendingJoin == CSteamID.Nil) return;
            CSteamID lobby = _pendingJoin;
            _pendingJoin = CSteamID.Nil;
            _lobby.Join(lobby);
        }

        private void OnLobbyFailed(string message)
        {
            Mode = ConnectionMode.None;
            if (_offlineIfLobbyFails && !IsActive)
            {
                _offlineIfLobbyFails = false;
                Fail($"{message}. Playing offline: friends can't join this time.");
                HostOffline();
                return;
            }
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
