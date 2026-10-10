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
    /// </summary>
    public static class DevPlayerBuild
    {
        private const string Scene = "Assets/_Game/Scenes/Game.unity";
        private const string DefaultExe = "Builds/Win64/PleaseDontDrown.exe";

        [MenuItem("PLEASE DON'T DROWN/Dev build (scene as is)")]
        public static void Build()
        {
            string[] cl = Environment.GetCommandLineArgs();
            int outAt = Array.IndexOf(cl, "-pdd-out");
            string exe = outAt >= 0 && outAt + 1 < cl.Length ? cl[outAt + 1] : DefaultExe;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            if (ok) File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(exe)!, "steam_appid.txt"), true);
            Debug.Log($"[Build] dev player {(ok ? "OK" : "FAILED")}: {exe} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors)");
            if (!ok && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
