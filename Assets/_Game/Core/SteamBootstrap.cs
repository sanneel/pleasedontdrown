using System;
using Steamworks;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// Initializes Steamworks once, pumps its callbacks and shuts it down on exit.
    /// If Steam isn't running the game still works, just offline only.
    /// Lives on its own root GameObject so DontDestroyOnLoad never drags other systems along.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class SteamBootstrap : MonoBehaviour
    {
        public static bool IsReady { get; private set; }
        public static CSteamID LocalId => IsReady ? SteamUser.GetSteamID() : CSteamID.Nil;
        public static string LocalName => IsReady ? SteamFriends.GetPersonaName() : Environment.UserName;
        public static bool OverlayAvailable => IsReady && SteamUtils.IsOverlayEnabled();
        public static bool IsOverlayOpen => _instance != null && _instance._overlayActive;

        private static SteamBootstrap _instance;
        private Callback<GameOverlayActivated_t> _overlayCallback;
        private bool _overlayActive;

        private void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (DevLaunchArgs.Has("-pdd-nosteam"))
            {
                Debug.Log("[Steam] Skipped (-pdd-nosteam).");
                return;
            }
            if (!Packsize.Test())
            {
                Debug.LogError("[Steam] Packsize test failed: wrong Steamworks.NET build for this platform.");
                return;
            }
            if (!DllCheck.Test())
            {
                Debug.LogError("[Steam] DllCheck failed: steam_api64.dll is missing or outdated.");
                return;
            }

            try
            {
                IsReady = SteamAPI.Init();
            }
            catch (DllNotFoundException e)
            {
                Debug.LogError($"[Steam] steam_api64.dll not found: {e.Message}");
            }

            if (IsReady) _overlayCallback = Callback<GameOverlayActivated_t>.Create(OnOverlayChanged);

            Debug.Log(IsReady
                ? $"[Steam] Ready as {LocalName} ({LocalId})"
                : "[Steam] Not available (is the Steam client running?). Offline play still works.");
        }

        public static bool OpenFriendsOverlay()
        {
            if (!OverlayAvailable) return false;
            SteamFriends.ActivateGameOverlay("friends");
            return true;
        }

        private void OnOverlayChanged(GameOverlayActivated_t data)
        {
            bool open = data.m_bActive != 0;
            if (open == _overlayActive) return;
            _overlayActive = open;
            if (open) GameInput.PushUI();
            else GameInput.PopUI();
            Debug.Log(open ? "[Steam] Overlay opened" : "[Steam] Overlay closed");
        }

        private void Update()
        {
            if (IsReady)
                SteamAPI.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (_instance != this)
                return;
            _instance = null;
            if (_overlayActive) { _overlayActive = false; GameInput.PopUI(); }
            _overlayCallback?.Dispose();
            _overlayCallback = null;
            if (IsReady)
            {
                IsReady = false;
                SteamAPI.Shutdown();
            }
        }
    }
}
