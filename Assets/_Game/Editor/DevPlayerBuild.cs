using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// A development Windows build of the Game scene exactly as it is (no scene rebuild first, unlike
    /// <see cref="GameSceneBuilder.BuildPlayerBatch"/>): the console cheats work, the scene file is left alone.
    /// Batch: -executeMethod PleaseDontDrown.Editor.DevPlayerBuild.Build [-pdd-out path\to.exe]
    /// <see cref="BuildRelease"/> is the one for players.
    /// </summary>
    public static class DevPlayerBuild
    {
        private const string Scene = "Assets/_Game/Scenes/Game.unity";
        private const string DefaultExe = "Builds/Win64/PleaseDontDrown.exe";
        private const string DefaultReleaseExe = "Builds/Release/PleaseDontDrown.exe";

        [MenuItem("PLEASE DON'T DROWN/Dev build (scene as is)")]
        public static void Build() => Build(release: false);

        /// <summary>
        /// The build for players: no development player (profiler hooks, debug checks, the "Development Build" mark;
        /// cheat commands are off), data packed with LZ4HC (about half the download, quicker to load from a slow
        /// disk), and no stack trace captured for ordinary log lines (the game logs a lot, each trace costs).
        /// Batch: -executeMethod PleaseDontDrown.Editor.DevPlayerBuild.BuildRelease [-pdd-out path\to.exe]
        /// </summary>
        [MenuItem("PLEASE DON'T DROWN/Release build (scene as is)")]
        public static void BuildRelease() => Build(release: true);

        private static void Build(bool release)
        {
            string[] cl = Environment.GetCommandLineArgs();
            int outAt = Array.IndexOf(cl, "-pdd-out");
            string exe = outAt >= 0 && outAt + 1 < cl.Length ? cl[outAt + 1] : release ? DefaultReleaseExe : DefaultExe;
            StackTraceLogType logTrace = PlayerSettings.GetStackTraceLogType(LogType.Log);
            StackTraceLogType warningTrace = PlayerSettings.GetStackTraceLogType(LogType.Warning);
            if (release)
            {
                PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            }
            UnityEditor.Build.Reporting.BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = exe,
                    target = BuildTarget.StandaloneWindows64,
                    options = release ? BuildOptions.CompressWithLz4HC : BuildOptions.Development
                });
            }
            finally
            {
                PlayerSettings.SetStackTraceLogType(LogType.Log, logTrace);
                PlayerSettings.SetStackTraceLogType(LogType.Warning, warningTrace);
            }
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            if (ok) File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(exe)!, "steam_appid.txt"), true);
            Debug.Log($"[Build] {(release ? "release" : "dev")} player {(ok ? "OK" : "FAILED")}: {exe} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors)");
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
