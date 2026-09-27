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

        private static SteamBootstrap _instance;

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

            Debug.Log(IsReady
                ? $"[Steam] Ready as {LocalName} ({LocalId})"
                : "[Steam] Not available (is the Steam client running?). Offline play still works.");
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
            if (IsReady)
            {
                IsReady = false;
                SteamAPI.Shutdown();
            }
        }
    }
}
