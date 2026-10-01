using System;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// The window: borderless fullscreen at the monitor's own resolution, or a window in the monitor's shape.
    /// Unity remembers the last resolution in the registry, so a leftover odd size (a test run's small window, an
    /// exclusive mode the monitor doesn't have) came back on the next start as a picture with black bars: every start
    /// now sets the mode itself instead of trusting what was saved.
    /// </summary>
    public static class GameDisplay
    {
        private const string WindowedKey = "pdd.windowed";

        public static bool IsFullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplyAtStart()
        {
            if (Application.isEditor || Application.isBatchMode) return;
            bool test = false;
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("-screen-", StringComparison.OrdinalIgnoreCase)) return; // the launcher chose the window
                if (arg.StartsWith("-pdd-", StringComparison.OrdinalIgnoreCase)) test = true;
            }
            if (test) Screen.SetResolution(1280, 720, FullScreenMode.Windowed); // test runs never take over the monitor
            else if (PlayerPrefs.GetInt(WindowedKey, 0) == 1) SetWindowed();
            else SetFullscreen();
        }

        public static void Toggle()
        {
            bool windowed = IsFullscreen;
            if (windowed) SetWindowed();
            else SetFullscreen();
            PlayerPrefs.SetInt(WindowedKey, windowed ? 1 : 0);
            PlayerPrefs.Save();
        }

        private static void SetFullscreen()
        {
            Display display = Display.main;
            Screen.SetResolution(display.systemWidth, display.systemHeight, FullScreenMode.FullScreenWindow);
        }

        /// <summary>Two thirds of the monitor, same shape (1280x720 on a 1080p screen).</summary>
        private static void SetWindowed()
        {
            Display display = Display.main;
            int width = Mathf.Max(960, Mathf.RoundToInt(display.systemWidth * 2f / 3f));
            int height = Mathf.RoundToInt(width * (float)display.systemHeight / display.systemWidth);
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
        }
    }
}
