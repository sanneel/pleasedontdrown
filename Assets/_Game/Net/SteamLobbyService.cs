using System;
using System.Collections.Generic;
using PleaseDontDrown.Core;
using Steamworks;
using UnityEngine;

namespace PleaseDontDrown.Net
{
    /// <summary>
    /// Steam lobby lifecycle only: create (host), join, leave, invite, and the friends list around it.
    /// Joins asked for from outside the game (an accepted invite, "Join Game" in the Steam friends list, a
    /// "+connect_lobby" launch) are raised as <see cref="JoinRequested"/>: <see cref="ConnectionService"/> ends the
    /// current session first. While in a lobby, rich presence tells friends how to join us.
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
        /// <summary>The player wants to join this lobby (invite, friends list, launch argument).</summary>
        public event Action<CSteamID> JoinRequested;

        /// <summary>A Steam friend who is online, and whether they are in this game (same build) with a lobby to join.</summary>
        public readonly struct Friend
        {
            public readonly CSteamID Id;
            public readonly string Name;
            /// <summary>Running this game (their rich presence carries our version key).</summary>
            public readonly bool InGame;
            public readonly bool SameVersion;
            /// <summary>Their lobby, if they are in one we could join; else Nil.</summary>
            public readonly CSteamID Lobby;

            public Friend(CSteamID id, string name, bool inGame, bool sameVersion, CSteamID lobby)
            {
                Id = id;
                Name = name;
                InGame = inGame;
                SameVersion = sameVersion;
                Lobby = lobby;
            }
        }

        private static readonly List<Friend> _friends = new();
        private static float _friendsRefreshedAt = float.NegativeInfinity;
        private static readonly Dictionary<ulong, float> _invitedAt = new();

        private Callback<LobbyCreated_t> _lobbyCreated;
        private Callback<LobbyEnter_t> _lobbyEntered;
        private Callback<GameLobbyJoinRequested_t> _joinRequested;
        private Callback<GameRichPresenceJoinRequested_t> _presenceJoinRequested;
        private Callback<LobbyChatUpdate_t> _membersChanged;
        private bool _creating;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CurrentLobby = CSteamID.Nil;
            _friends.Clear();
            _friendsRefreshedAt = float.NegativeInfinity;
            _invitedAt.Clear();
        }

        private void Start()
        {
            if (!SteamBootstrap.IsReady)
                return;

            _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
            _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => JoinRequested?.Invoke(r.m_steamIDLobby));
            _presenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(OnPresenceJoinRequested);
            _membersChanged = Callback<LobbyChatUpdate_t>.Create(_ => UpdatePresence());
            SteamFriends.ClearRichPresence();

            // Launched from a Steam invite while the game was closed: "+connect_lobby <id>".
            if (TryParseConnect(string.Join(" ", Environment.GetCommandLineArgs()), out CSteamID lobby))
                JoinRequested?.Invoke(lobby);
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
            UpdatePresence();
        }

        public void OpenInviteOverlay()
        {
            if (InLobby && SteamBootstrap.IsReady)
                SteamFriends.ActivateGameOverlayInviteDialog(CurrentLobby);
        }

        /// <summary>Sends a Steam invite to our lobby (works without the overlay); the friend accepts it in Steam.</summary>
        public static bool Invite(CSteamID friend)
        {
            if (!InLobby || !SteamBootstrap.IsReady || !SteamMatchmaking.InviteUserToLobby(CurrentLobby, friend))
                return false;
            _invitedAt[friend.m_SteamID] = Time.unscaledTime;
            return true;
        }

        /// <summary>Invited in the last half minute (so the menu can say so instead of offering it again).</summary>
        public static bool RecentlyInvited(CSteamID friend) =>
            _invitedAt.TryGetValue(friend.m_SteamID, out float at) && Time.unscaledTime - at < 30f;

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

        /// <summary>
        /// Online friends, those in this game first (refreshed every few seconds, so menus can call it each frame).
        /// With the development app id (480, Spacewar) friends playing the real Spacewar show as not in this game:
        /// only our own rich presence marks a friend as playing it.
        /// </summary>
        public static IReadOnlyList<Friend> Friends()
        {
            if (!SteamBootstrap.IsReady)
            {
                _friends.Clear();
                return _friends;
            }
            if (Time.unscaledTime - _friendsRefreshedAt < 3f)
                return _friends;
            _friendsRefreshedAt = Time.unscaledTime;

            _friends.Clear();
            AppId_t app = SteamUtils.GetAppID();
            int count = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (int i = 0; i < count; i++)
            {
                CSteamID id = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                if (SteamFriends.GetFriendPersonaState(id) == EPersonaState.k_EPersonaStateOffline)
                    continue;
                bool sameApp = SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t game) && game.m_gameID.AppID() == app;
                string version = string.Empty;
                if (sameApp)
                {
                    SteamFriends.RequestFriendRichPresence(id); // arrives for the next refresh
                    version = SteamFriends.GetFriendRichPresence(id, VersionKey);
                }
                bool inGame = !string.IsNullOrEmpty(version);
                CSteamID lobby = inGame && game.m_steamIDLobby.IsValid() ? game.m_steamIDLobby : CSteamID.Nil;
                _friends.Add(new Friend(id, SteamFriends.GetFriendPersonaName(id), inGame, version == NetVersion.Current, lobby));
            }
            _friends.Sort((a, b) => a.InGame != b.InGame ? (a.InGame ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return _friends;
        }

        /// <summary>Finds "+connect_lobby &lt;id&gt;" in a launch line or a rich presence connect string.</summary>
        public static bool TryParseConnect(string text, out CSteamID lobby)
        {
            lobby = CSteamID.Nil;
            if (string.IsNullOrEmpty(text))
                return false;
            string[] parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == "+connect_lobby" && ulong.TryParse(parts[i + 1], out ulong id) && id != 0)
                {
                    lobby = new CSteamID(id);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Rich presence while in a lobby: "connect" gives friends a Join Game entry in the Steam friends list,
        /// the player group puts everyone of one session together there, the version key marks this game.
        /// </summary>
        private void UpdatePresence()
        {
            if (!SteamBootstrap.IsReady)
                return;
            if (!InLobby)
            {
                SteamFriends.ClearRichPresence();
                return;
            }
            string id = CurrentLobby.m_SteamID.ToString();
            int members = SteamMatchmaking.GetNumLobbyMembers(CurrentLobby);
            int limit = SteamMatchmaking.GetLobbyMemberLimit(CurrentLobby);
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + id);
            SteamFriends.SetRichPresence("status", $"Lifeguarding on the beach ({members}/{limit})");
            SteamFriends.SetRichPresence(VersionKey, NetVersion.Current);
            SteamFriends.SetRichPresence("steam_player_group", id);
            SteamFriends.SetRichPresence("steam_player_group_size", members.ToString());
        }

        private void OnPresenceJoinRequested(GameRichPresenceJoinRequested_t request)
        {
            if (TryParseConnect(request.m_rgchConnect, out CSteamID lobby))
                JoinRequested?.Invoke(lobby);
            else
                Debug.LogWarning($"[Steam] Join request without a lobby: '{request.m_rgchConnect}'");
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
            SteamMatchmaking.SetLobbyData(CurrentLobby, VersionKey, NetVersion.Current);
            UpdatePresence();
            Debug.Log($"[Steam] Lobby {CurrentLobby.m_SteamID} created: friends can join");
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
            if (!string.IsNullOrEmpty(hostVersion) && hostVersion != NetVersion.Current)
            {
                SteamMatchmaking.LeaveLobby(lobby);
                LobbyFailed?.Invoke($"Version mismatch: host has {hostVersion}, you have {NetVersion.Current}");
                return;
            }

            CurrentLobby = lobby;
            UpdatePresence();
            Debug.Log($"[Steam] Joined lobby {lobby.m_SteamID} of {SteamFriends.GetFriendPersonaName(owner)}");
            JoinedLobby?.Invoke(lobby, owner);
        }

        private void OnDestroy()
        {
            Leave();
            _lobbyCreated?.Dispose();
            _lobbyEntered?.Dispose();
            _joinRequested?.Dispose();
            _presenceJoinRequested?.Dispose();
            _membersChanged?.Dispose();
        }
    }
}
