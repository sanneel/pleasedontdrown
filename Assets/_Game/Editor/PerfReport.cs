using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// Turns Profiler logs written by the in-game <c>perf ... raw</c> command (Dev/PerfProbe) into text tables:
    /// batch <c>-executeMethod PleaseDontDrown.Editor.PerfReport.AnalyzeBatch -pdd-raw &lt;file.raw&gt;[,&lt;file2.raw&gt;]</c>
    /// writes <c>&lt;file&gt;.txt</c> next to each log: average self and total ms per frame for every function on the
    /// main thread, the render thread, and all job workers together.
    /// </summary>
    public static class PerfReport
    {
        public static void AnalyzeBatch()
        {
            string[] cl = Environment.GetCommandLineArgs();
            int at = Array.IndexOf(cl, "-pdd-raw");
            if (at < 0 || at + 1 >= cl.Length) { Debug.LogError("[PerfReport] -pdd-raw <file> missing"); EditorApplication.Exit(1); return; }
            try
            {
                foreach (string raw in cl[at + 1].Split(','))
                    Analyze(raw.Trim());
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[PerfReport] FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Batch: a development player of the scene as it is saved (no rebuild) for profiling, to <c>-pdd-out &lt;exe&gt;</c>
        /// (default F:/temp/claude/pdd-perfbuild). Add <c>-pdd-release</c> for a release player to compare against.
        /// </summary>
        public static void BuildBatch()
        {
            string[] cl = Environment.GetCommandLineArgs();
            int outAt = Array.IndexOf(cl, "-pdd-out");
            string exe = outAt >= 0 && outAt + 1 < cl.Length ? cl[outAt + 1] : "F:/temp/claude/pdd-perfbuild/PleaseDontDrown.exe";
            bool release = Array.IndexOf(cl, "-pdd-release") >= 0;
            bool timingWas = PlayerSettings.enableFrameTimingStats;
            PlayerSettings.enableFrameTimingStats = true; // GPU times for PerfProbe (only for this build)
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Game/Scenes/Game.unity" },
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = release ? BuildOptions.None : BuildOptions.Development |
                          (Array.IndexOf(cl, "-pdd-deep") >= 0 ? BuildOptions.EnableDeepProfilingSupport : BuildOptions.None)
            });
            PlayerSettings.enableFrameTimingStats = timingWas;
            AssetDatabase.SaveAssets();
            bool ok = report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            if (ok) File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(exe)!, "steam_appid.txt"), true);
            Debug.Log($"[PerfReport] build {report.summary.result}: {exe}");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        // -pdd-spike <ms>: frames slower than this get their call tree printed (deep profiles run slow: raise it).
        private static float SpikeMs
        {
            get
            {
                string[] cl = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(cl, "-pdd-spike");
                return at >= 0 && at + 1 < cl.Length && float.TryParse(cl[at + 1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float ms) ? ms : 12f;
            }
        }

        private class Stat { public double Self, Total; public int Calls; }

        public static void Analyze(string raw)
        {
            if (!ProfilerDriver.LoadProfile(raw, false)) throw new Exception("could not load " + raw);
            int first = ProfilerDriver.firstFrameIndex, last = ProfilerDriver.lastFrameIndex;
            int frames = last - first + 1;
            var groups = new Dictionary<string, Dictionary<string, Stat>>();
            var frameMs = new List<float>();
            var spikes = new StringBuilder();
            for (int f = first; f <= last; f++)
            {
                for (int thread = 0; thread < 256; thread++)
                {
                    using RawFrameDataView rawView = ProfilerDriver.GetRawFrameDataView(f, thread);
                    if (rawView == null || !rawView.valid) break;
                    string threadName = rawView.threadName;
                    string group = thread == 0 ? "Main Thread" : threadName == "Render Thread" ? "Render Thread" :
                        threadName.StartsWith("Job.Worker") ? "Job workers (sum)" : null;
                    if (group == null) continue;
                    if (thread == 0) frameMs.Add(rawView.frameTimeMs);
                    if (thread == 0 && rawView.frameTimeMs > SpikeMs)
                    {
                        using HierarchyFrameDataView spikeView = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                            HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnTotalTime, false);
                        spikes.AppendLine($"-- frame {f}: {rawView.frameTimeMs:F1} ms");
                        Spike(spikeView, spikeView.GetRootItemID(), 0, spikes);
                    }
                    using HierarchyFrameDataView view = ProfilerDriver.GetHierarchyFrameDataView(f, thread,
                        HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnSelfTime, false);
                    if (view == null || !view.valid) continue;
                    if (!groups.TryGetValue(group, out var stats)) groups[group] = stats = new Dictionary<string, Stat>();
                    Walk(view, view.GetRootItemID(), stats, new HashSet<string>());
                }
            }

            var text = new StringBuilder();
            frameMs.Sort();
            text.AppendLine($"{Path.GetFileName(raw)}: {frames} frames, frame avg {frameMs.DefaultIfEmpty().Average():F2} ms, " +
                            $"p50 {Pct(frameMs, 0.5f):F2}, p95 {Pct(frameMs, 0.95f):F2}, max {Pct(frameMs, 1f):F2}");
            foreach (var (group, stats) in groups.OrderBy(g => g.Key == "Main Thread" ? 0 : 1))
            {
                text.AppendLine().AppendLine($"== {group}: top self time (ms per frame, calls per frame)");
                foreach (var (name, s) in stats.OrderByDescending(p => p.Value.Self).Take(60))
                    text.AppendLine($"{s.Self / frames,8:F3} self {s.Total / frames,8:F3} total {s.Calls / (float)frames,8:F1}  {name}");
                text.AppendLine().AppendLine($"== {group}: top total time");
                foreach (var (name, s) in stats.OrderByDescending(p => p.Value.Total).Take(60))
                    text.AppendLine($"{s.Total / frames,8:F3} total {s.Self / frames,8:F3} self {s.Calls / (float)frames,8:F1}  {name}");
            }
            text.AppendLine().AppendLine("== Spikes (main thread frames over 12 ms, branches over 1 ms)").Append(spikes);
            string output = Path.ChangeExtension(raw, ".txt");
            File.WriteAllText(output, text.ToString());
            Debug.Log("[PerfReport] wrote " + output);
        }

        /// <summary>Self time per name; total time once per name per branch (so recursion isn't counted twice).</summary>
        private static void Walk(HierarchyFrameDataView view, int id, Dictionary<string, Stat> stats, HashSet<string> above)
        {
            var children = new List<int>();
            view.GetItemChildren(id, children);
            foreach (int child in children)
            {
                string name = view.GetItemName(child);
                if (!stats.TryGetValue(name, out Stat s)) stats[name] = s = new Stat();
                s.Self += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnSelfTime);
                s.Calls += (int)view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnCalls);
                bool added = above.Add(name);
                if (added) s.Total += view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                Walk(view, child, stats, above);
                if (added) above.Remove(name);
            }
        }

        private static void Spike(HierarchyFrameDataView view, int id, int depth, StringBuilder text)
        {
            if (depth > 40) return;
            var children = new List<int>();
            view.GetItemChildren(id, children);
            foreach (int child in children)
            {
                float total = view.GetItemColumnDataAsFloat(child, HierarchyFrameDataView.columnTotalTime);
                if (total < 0.25f) continue;
                text.AppendLine($"{new string(' ', depth * 2)}{total,7:F2} {view.GetItemName(child)}");
                Spike(view, child, depth + 1, text);
            }
        }

        private static float Pct(List<float> sorted, float q) =>
            sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.RoundToInt(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
    }
}
