using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PleaseDontDrown.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Frame-cost measurements for automated runs: console <c>perf &lt;seconds&gt; [label] [raw]</c> logs frame time
    /// percentiles plus the main engine stages (scripts, physics, animation, rendering, GC) as
    /// <c>[Perf] ...</c> lines; with <c>raw</c> it also writes a Profiler binary log to <c>Logs/Perf/&lt;label&gt;.raw</c>
    /// (development builds) that <c>Editor/PerfReport.AnalyzeBatch</c> turns into a per-function table.
    /// </summary>
    public class PerfProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("PerfProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<PerfProbe>();
        }

        // Stage markers worth a column. Names that don't exist in this player read 0.
        private static readonly (string label, ProfilerCategory category, string marker)[] Stages =
        {
            ("main", ProfilerCategory.Internal, "Main Thread"),
            ("update", ProfilerCategory.Scripts, "Update.ScriptRunBehaviourUpdate"),
            ("late", ProfilerCategory.Scripts, "PreLateUpdate.ScriptRunBehaviourLateUpdate"),
            ("fixedScripts", ProfilerCategory.Scripts, "FixedUpdate.ScriptRunBehaviourFixedUpdate"),
            ("physics", ProfilerCategory.Physics, "FixedUpdate.PhysicsFixedUpdate"),
            ("anim", ProfilerCategory.Animation, "PreLateUpdate.DirectorUpdateAnimationBegin"),
            ("gui", ProfilerCategory.Gui, "PostLateUpdate.PlayerUpdateCanvases"),
            ("imgui", ProfilerCategory.Gui, "GUI.Repaint"),
            ("render", ProfilerCategory.Render, "PostLateUpdate.FinishFrameRendering"),
            ("present", ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
            ("gc", ProfilerCategory.Memory, "GC.Collect"),
        };

        private static readonly (string label, ProfilerCategory category, string counter)[] Counters =
        {
            ("batches", ProfilerCategory.Render, "Batches Count"),
            ("setpass", ProfilerCategory.Render, "SetPass Calls Count"),
            ("draws", ProfilerCategory.Render, "Draw Calls Count"),
            ("tris", ProfilerCategory.Render, "Triangles Count"),
            ("shadowCasters", ProfilerCategory.Render, "Shadow Casters Count"),
            ("gcAlloc", ProfilerCategory.Memory, "GC Allocated In Frame"),
            ("bodies", ProfilerCategory.Physics, "Active Dynamic Bodies"),
            ("kinematic", ProfilerCategory.Physics, "Active Kinematic Bodies"),
        };

        private bool _busy;

        private void OnEnable() =>
            DevCommands.Register("perf", "<seconds> [label] [raw]", "Measure frame cost for a while and log [Perf] lines (raw: also a Profiler log).", args =>
            {
                float seconds = args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 5f;
                string label = args.Length > 1 ? args[1] : "perf";
                bool raw = args.Length > 2 && args[2] == "raw";
                if (_busy) { DevCommands.Print("perf already running"); return; }
                StartCoroutine(Measure(seconds, label, raw));
            }, owner: this);

        private void OnDisable()
        {
            DevCommands.Unregister("perf", this);
            DevCommands.Unregister("bodycheck", this);
        }

        private void Start() =>
            DevCommands.Register("bodycheck", "<seconds> [label]", "Watch every hand for a while: log [BodyCheck] how often one was inside somebody else.", args =>
                StartCoroutine(BodyCheck(args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 5f, args.Length > 1 ? args[1] : "bodycheck")), owner: this);

        /// <summary>Hands found more than 2 cm inside another character (after everybody's LateUpdate).</summary>
        private IEnumerator BodyCheck(float seconds, string label)
        {
            int frames = 0, badFrames = 0, handsChecked = 0, inside = 0;
            float worst = 0f;
            string worstWho = "";
            var endOfFrame = new WaitForEndOfFrame();
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return endOfFrame;
                frames++;
                bool bad = false;
                foreach (Avatars.AvatarRig rig in Avatars.BodySpace.Rigs)
                {
                    if (rig == null || !rig.IsBuilt || !rig.gameObject.activeInHierarchy) continue;
                    if (rig.Renderer == null || !rig.Renderer.isVisible) continue; // (off screen: posed 4 times a second, nobody sees it)
                    foreach (bool right in new[] { false, true })
                    {
                        Transform hand = rig[right ? Avatars.AvatarRig.Bone.HandR : Avatars.AvatarRig.Bone.HandL];
                        if (hand == null) continue;
                        handsChecked++;
                        Vector3 palm = hand.position - hand.up * (rig.HandLength * 0.5f), moved = palm;
                        if (!Avatars.BodySpace.PushOut(ref moved, 0f, rig)) continue;
                        float depth = Vector3.Distance(palm, moved);
                        if (depth < 0.02f) continue;
                        inside++;
                        bad = true;
                        if (depth > worst) { worst = depth; worstWho = $"{rig.name} {(right ? "right" : "left")} hand"; }
                    }
                }
                if (bad) badFrames++;
            }
            Debug.Log($"[BodyCheck] {label}: {frames} frames, {handsChecked} hands checked, {inside} inside somebody (in {badFrames} frames), worst {worst * 100f:F1} cm ({worstWho})");
        }

        private IEnumerator Measure(float seconds, string label, bool raw)
        {
            _busy = true;
            var stages = Stages.Select(s => ProfilerRecorder.StartNew(s.category, s.marker, 1)).ToArray();
            var counters = Counters.Select(c => ProfilerRecorder.StartNew(c.category, c.counter, 1)).ToArray();
            var stageSum = new double[Stages.Length];
            var stageMax = new double[Stages.Length];
            var counterSum = new double[Counters.Length];
            var frames = new List<float>();
            var timing = new FrameTiming[1];
            double gpuSum = 0, renderThreadSum = 0, mainSum = 0;
            int timed = 0;

            string rawPath = null;
            if (raw && Profiler.supported)
            {
                string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Perf"));
                Directory.CreateDirectory(folder);
                rawPath = Path.Combine(folder, label + ".raw");
                Profiler.maxUsedMemory = 512 * 1024 * 1024;
                Profiler.logFile = rawPath;
                Profiler.enableBinaryLog = true;
                Profiler.enabled = true;
            }

            yield return null; // the recorders fill from the next frame
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                for (int i = 0; i < stages.Length; i++)
                {
                    double ms = stages[i].Valid ? stages[i].LastValue / 1e6 : 0;
                    stageSum[i] += ms;
                    if (ms > stageMax[i]) stageMax[i] = ms;
                }
                for (int i = 0; i < counters.Length; i++)
                    counterSum[i] += counters[i].Valid ? counters[i].LastValue : 0;
                // GPU and render thread (needs Frame Timing Stats in the player settings).
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0)
                {
                    gpuSum += timing[0].gpuFrameTime;
                    renderThreadSum += timing[0].cpuRenderThreadFrameTime;
                    mainSum += timing[0].cpuMainThreadFrameTime;
                    timed++;
                }
            }

            if (rawPath != null)
            {
                Profiler.enabled = false;
                Profiler.enableBinaryLog = false;
                Profiler.logFile = "";
            }
            foreach (var r in stages) r.Dispose();
            foreach (var r in counters) r.Dispose();

            int n = Mathf.Max(1, frames.Count);
            frames.Sort();
            float P(float q) => frames.Count == 0 ? 0f : frames[Mathf.Clamp(Mathf.RoundToInt(q * (frames.Count - 1)), 0, frames.Count - 1)];
            var line = new StringBuilder();
            line.Append($"[Perf] {label}: {frames.Count} frames, avg {frames.Average():F2} ms ({1000f / frames.Average():F0} fps), p50 {P(0.5f):F2}, p95 {P(0.95f):F2}, p99 {P(0.99f):F2}, max {P(1f):F2}");
            Debug.Log(line.ToString());
            line.Clear().Append($"[Perf] {label} stages avg/max ms:");
            for (int i = 0; i < Stages.Length; i++) line.Append($" {Stages[i].label} {stageSum[i] / n:F2}/{stageMax[i]:F1}");
            Debug.Log(line.ToString());
            line.Clear().Append($"[Perf] {label} counters avg:");
            for (int i = 0; i < Counters.Length; i++) line.Append($" {Counters[i].label} {counterSum[i] / n:F0}");
            Debug.Log(line.ToString());
            Debug.Log(timed > 0
                ? $"[Perf] {label} timing: gpu {gpuSum / timed:F2} ms, render thread {renderThreadSum / timed:F2} ms, main thread {mainSum / timed:F2} ms ({timed} frames)"
                : $"[Perf] {label} timing: no GPU timings (Frame Timing Stats off, or batch mode)");
            Debug.Log($"[Perf] {label} scene: {Census()}");
            if (rawPath != null) Debug.Log($"[Perf] {label} profiler log: {rawPath}");
            _busy = false;
        }

        /// <summary>What's alive right now: the things that cost every frame.</summary>
        private static string Census()
        {
            int renderers = 0, visible = 0, skinned = 0, skinnedVisible = 0, shadowCasting = 0;
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                renderers++;
                if (r.isVisible) visible++;
                if (r is SkinnedMeshRenderer) { skinned++; if (r.isVisible) skinnedVisible++; }
                if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) shadowCasting++;
            }
            int lights = 0, shadowLights = 0;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (!l.isActiveAndEnabled) continue;
                lights++;
                if (l.shadows != LightShadows.None) shadowLights++;
            }
            int cameras = Camera.allCamerasCount;
            int bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Count(b => !b.IsSleeping() && !b.isKinematic);
            int colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None).Length;
            int audio = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a => a.isPlaying);
            int behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Length;
            int particles = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Count(p => p.isPlaying);
            return $"renderers {renderers} (visible {visible}), skinned {skinned} (visible {skinnedVisible}), shadow casters {shadowCasting}, " +
                   $"lights {lights} (shadowed {shadowLights}), cameras {cameras}, awake bodies {bodies}, colliders {colliders}, " +
                   $"playing audio {audio}, behaviours {behaviours}, particles {particles}";
        }
    }
}
