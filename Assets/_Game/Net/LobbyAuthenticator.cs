using System;
using System.Collections;
using FishNet.Authenticating;
using FishNet.Connection;
using Steamworks;
using UnityEngine;

namespace PleaseDontDrown.Net
{
    /// <summary>
    /// Server-side gate: in Steam mode, only members of our Steam lobby may connect.
    /// Offline (localhost) connections and the host's own client are always accepted.
    /// </summary>
    public class LobbyAuthenticator : Authenticator
    {
        [Tooltip("How long to wait for Steam to report a new lobby member before rejecting.")]
        [SerializeField] private float _membershipTimeout = 5f;

        public override event Action<NetworkConnection, bool> OnAuthenticationResult;

        public override void OnRemoteConnection(NetworkConnection connection)
        {
            base.OnRemoteConnection(connection);

            if (connection.IsLocalClient || ConnectionService.Instance == null || ConnectionService.Instance.Mode != ConnectionMode.Steam)
            {
                OnAuthenticationResult?.Invoke(connection, true);
                return;
            }
            StartCoroutine(AuthenticateSteamMember(connection));
        }

        private IEnumerator AuthenticateSteamMember(NetworkConnection connection)
        {
            if (!ulong.TryParse(connection.GetAddress(), out ulong id) || id == 0)
            {
                Debug.LogWarning($"[Auth] Rejected connection {connection.ClientId}: no valid SteamID.");
                OnAuthenticationResult?.Invoke(connection, false);
                yield break;
            }

            var steamId = new CSteamID(id);
            float deadline = Time.unscaledTime + _membershipTimeout;
            // Lobby membership can arrive a moment after the P2P connection does.
            while (!SteamLobbyService.IsMember(steamId) && Time.unscaledTime < deadline)
                yield return null;

            bool ok = connection.IsActive && SteamLobbyService.IsMember(steamId);
            if (!ok)
                Debug.LogWarning($"[Auth] Rejected Steam user {id}: not in our lobby.");
            OnAuthenticationResult?.Invoke(connection, ok);
        }
    }
}
