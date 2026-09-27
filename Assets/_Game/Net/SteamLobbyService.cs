using System;
using PleaseDontDrown.Core;
using Steamworks;
using UnityEngine;

namespace PleaseDontDrown.Net
{
    /// <summary>
    /// Steam lobby lifecycle only: create (host), join (invite / friend list / +connect_lobby), leave.
    /// It never touches the network transport; <see cref="ConnectionService"/> reacts to its events.
    /// </summary>
    public class SteamLobbyService : MonoBehaviour
    {
        private const string HostIdKey = "pdd_host";
        private const string VersionKey = "pdd_version";

        public static CSteamID CurrentLobby { get; private set; } = CSteamID.Nil;
        public static bool InLobby => CurrentLobby != CSteamID.Nil;

        /// <summary>We created a lobby and own it.</summary>
        public event Action<CSteamID> HostLobbyReady;
        /// <summary>We joined someone else's lobby. Args: lobby, owner.</summary>
        public event Action<CSteamID, CSteamID> JoinedLobby;
        public event Action<string> LobbyFailed;

        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;
        private bool _creating;

        private void Start()
        {
            if (!SteamBootstrap.IsReady)
                return;

            _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(OnJoinRequested);

            // Launched from a Steam invite while the game was closed: "+connect_lobby <id>".
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong lobbyId))
                    Join(new CSteamID(lobbyId));
            }
        }

        public bool CreateLobby(int maxPlayers)
        {
            if (!SteamBootstrap.IsReady || InLobby || _creating)
                return false;
            _creating = true;
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers);
            return true;
        }

        public void Join(CSteamID lobby)
        {
            if (!SteamBootstrap.IsReady)
                return;
            if (InLobby)
                Leave();
            SteamMatchmaking.JoinLobby(lobby);
        }

        public void Leave()
        {
            if (InLobby && SteamBootstrap.IsReady)
                SteamMatchmaking.LeaveLobby(CurrentLobby);
            CurrentLobby = CSteamID.Nil;
            _creating = false;
        }

        public void OpenInviteOverlay()
        {
            if (InLobby && SteamBootstrap.IsReady)
                SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
        }

        public static bool IsMember(CSteamID user)
        {
            if (!InLobby || !SteamBootstrap.IsReady)
                return false;
            int count = SteamMatchmaking.GetNumLobbyMembers(CurrentLobby);
            for (int i = 0; i < count; i++)
            {
                if (SteamMatchmaking.GetLobbyMemberByIndex(CurrentLobby, i) == user)
                    return true;
            }
            return false;
        }

        private void OnLobbyCreated(LobbyCreated_t result)
        {
            _creating = false;
            if (result.m_eResult != EResult.k_EResultOK)
            {
                LobbyFailed?.Invoke($"Could not create lobby: {result.m_eResult}");
                return;
            }

            CurrentLobby = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(CurrentLobby, HostIdKey, SteamBootstrap.LocalId.m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(CurrentLobby, VersionKey, Application.version);
            HostLobbyReady?.Invoke(CurrentLobby);
        }

        private void OnLobbyEntered(LobbyEnter_t result)
        {
            var lobby = new CSteamID(result.m_ulSteamIDLobby);
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobby);

            // The host also receives this right after creating; that path is handled in OnLobbyCreated.
            if (owner == SteamBootstrap.LocalId)
                return;

            if (result.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                LobbyFailed?.Invoke($"Could not join lobby: {(EChatRoomEnterResponse)result.m_EChatRoomEnterResponse}");
                return;
            }

            string hostVersion = SteamMatchmaking.GetLobbyData(lobby, VersionKey);
            if (!string.IsNullOrEmpty(hostVersion) && hostVersion != Application.version)
            {
                SteamMatchmaking.LeaveLobby(lobby);
                LobbyFailed?.Invoke($"Version mismatch: host has {hostVersion}, you have {Application.version}");
                return;
            }

            CurrentLobby = lobby;
            JoinedLobby?.Invoke(lobby, owner);
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t request) => Join(request.m_steamIDLobby);

        private void OnDestroy()
        {
            Leave();
            _lobbyCreated?.Dispose();
            _lobbyEntered?.Dispose();
            _joinRequested?.Dispose();
        }
    }
}
