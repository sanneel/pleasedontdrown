using System;
using System.Collections;
using FishNet.Authenticating;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using PleaseDontDrown.Core;
using Steamworks;
using UnityEngine;

namespace PleaseDontDrown.Net
{
    /// <summary>Client → host right after connecting.</summary>
    public struct HelloBroadcast : IBroadcast
    {
        public string Version;
    }

    /// <summary>Host → client: accepted, or why not.</summary>
    public struct HelloResponse : IBroadcast
    {
        public bool Accepted;
        public string Reason;
    }

    /// <summary>
    /// Server-side gate for every connection (Steam and local):
    ///  1. the client must send a matching <see cref="NetVersion"/> within a few seconds,
    ///  2. in Steam mode, the client must also be a member of our Steam lobby.
    /// The host's own client goes through the same handshake.
    /// </summary>
    public class LobbyAuthenticator : Authenticator
    {
        [SerializeField] private float _timeout = 6f;
        [Tooltip("How long to wait for Steam to report a new lobby member before rejecting.")]
        [SerializeField] private float _membershipTimeout = 5f;

        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        /// <summary>Raised on the client when the host refuses us (e.g. version mismatch).</summary>
        public static event Action<string> Rejected;

        public override void InitializeOnce(NetworkManager networkManager)
        {
            base.InitializeOnce(networkManager);
            networkManager.ServerManager.RegisterBroadcast<HelloBroadcast>(OnHello, requireAuthentication: false);
            networkManager.ClientManager.RegisterBroadcast<HelloResponse>(OnHelloResponse);
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        }

        // ------------------------------------------------------------------ client

        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState == LocalConnectionState.Started)
                NetworkManager.ClientManager.Broadcast(new HelloBroadcast { Version = NetVersion.Current });
        }

        private void OnHelloResponse(HelloResponse response, Channel channel)
        {
            if (response.Accepted) return;
            Debug.LogWarning($"[Auth] Host refused us: {response.Reason}");
            Rejected?.Invoke(response.Reason);
        }

        // ------------------------------------------------------------------ server

        public override void OnRemoteConnection(NetworkConnection connection)
        {
            base.OnRemoteConnection(connection);
            StartCoroutine(TimeoutIfSilent(connection));
        }

        private IEnumerator TimeoutIfSilent(NetworkConnection connection)
        {
            float deadline = Time.unscaledTime + _timeout;
            while (Time.unscaledTime < deadline)
            {
                if (!connection.IsActive || connection.IsAuthenticated) yield break;
                yield return null;
            }
            if (connection.IsActive && !connection.IsAuthenticated)
                Reject(connection, "no handshake received");
        }

        private void OnHello(NetworkConnection connection, HelloBroadcast hello, Channel channel)
        {
            if (connection.IsAuthenticated) return;

            if (hello.Version != NetVersion.Current)
            {
                Reject(connection, $"version mismatch: host has {NetVersion.Current}, you have {hello.Version}. Use the same build.");
                return;
            }

            bool needsLobby = !connection.IsLocalClient && ConnectionService.Instance != null && ConnectionService.Instance.Mode == ConnectionMode.Steam;
            if (needsLobby)
                StartCoroutine(AuthenticateSteamMember(connection));
            else
                Accept(connection);
        }

        private IEnumerator AuthenticateSteamMember(NetworkConnection connection)
        {
            if (!ulong.TryParse(connection.GetAddress(), out ulong id) || id == 0)
            {
                Reject(connection, "no valid SteamID");
                yield break;
            }
            var steamId = new CSteamID(id);
            float deadline = Time.unscaledTime + _membershipTimeout;
            // Lobby membership can arrive a moment after the P2P connection does.
            while (!SteamLobbyService.IsMember(steamId) && Time.unscaledTime < deadline)
                yield return null;

            if (connection.IsActive && SteamLobbyService.IsMember(steamId)) Accept(connection);
            else Reject(connection, "not a member of the host's Steam lobby");
        }

        private void Accept(NetworkConnection connection)
        {
            NetworkManager.ServerManager.Broadcast(connection, new HelloResponse { Accepted = true }, requireAuthenticated: false);
            OnAuthenticationResult?.Invoke(connection, true);
        }

        private void Reject(NetworkConnection connection, string reason)
        {
            Debug.LogWarning($"[Auth] Rejected connection {connection.ClientId}: {reason}");
            // Send the reason before the kick so the client can show it.
            NetworkManager.ServerManager.Broadcast(connection, new HelloResponse { Accepted = false, Reason = reason }, requireAuthenticated: false);
            OnAuthenticationResult?.Invoke(connection, false);
        }
    }
}
