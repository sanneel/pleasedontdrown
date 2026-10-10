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
            DevCommands.Unregister("spinwatch", this);
        }

        private void Start()
        {
            DevCommands.Register("spinwatch", "<seconds>", "On any machine (host or client): log [Spin] for characters whose drawn body turns round and round on the spot.", args =>
                StartCoroutine(SpinWatch(args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 60f)), owner: this);
            DevCommands.Register("legcheck", "<seconds>", "Log [Legs]: knees buckling and legs crossing on upright characters, per body id.", args =>
                StartCoroutine(LegCheck(args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 60f)), owner: this);
            DevCommands.Register("victimspin", "<seconds>", "Log [VictimSpin]: how much each tourist's body turns about the vertical (after CPR they stand up).", args =>
                StartCoroutine(VictimSpin(args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 20f)), owner: this);
            Register();
        }

        private IEnumerator VictimSpin(float seconds)
        {
            var last = new Dictionary<Rescue.VictimBody, float>();
            var turned = new Dictionary<Rescue.VictimBody, float>();
            var from = new Dictionary<Rescue.VictimBody, Vector3>();
            var net = new Dictionary<Rescue.VictimBody, float>();
            var trace = new Dictionary<Rescue.VictimBody, StringBuilder>();
            int tick = 0;
            float end = Time.realtimeSinceStartup + seconds;
            var wait = new WaitForSeconds(0.1f);
            while (Time.realtimeSinceStartup < end)
            {
                yield return wait;
                tick++;
                foreach (Rescue.VictimBody v in FindObjectsByType<Rescue.VictimBody>(FindObjectsSortMode.None))
                {
                    if (v.transform.up.y < 0.8f) { last.Remove(v); continue; } // only once standing
                    float yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(v.transform.forward, Vector3.up)).eulerAngles.y;
                    if (last.TryGetValue(v, out float before))
                    {
                        turned[v] = (turned.TryGetValue(v, out float t) ? t : 0f) + Mathf.Abs(Mathf.DeltaAngle(before, yaw));
                        net[v] = (net.TryGetValue(v, out float n) ? n : 0f) + Mathf.DeltaAngle(before, yaw);
                    }
                    if (tick % 5 == 0)
                    {
                        if (!trace.TryGetValue(v, out StringBuilder sb)) trace[v] = sb = new StringBuilder();
                        sb.Append($" {yaw:F0}/{v.transform.up.y:F2}/{v.GetComponent<Rigidbody>().angularVelocity.y:F1}");
                    }
                    else if (!from.ContainsKey(v)) from[v] = v.transform.position;
                    last[v] = yaw;
                }
            }
            foreach (var (v, t) in turned)
                if (v != null) Debug.Log($"[VictimSpin] {v.name}: turned {t:F0} degrees standing (net {(net.TryGetValue(v, out float n) ? n : 0f):F0}), yaw/up/spin every 0.5 s:{(trace.TryGetValue(v, out StringBuilder sb) ? sb.ToString() : "")}; moved {Vector3.Distance(from[v], v.transform.position):F2} m, at {v.transform.position:F1}");
            Debug.Log($"[VictimSpin] done ({turned.Count} standing tourists)");
        }

        /// <summary>Per body id: how often an upright, dry character's knee is bent past 0.85 or its feet cross.</summary>
        private IEnumerator LegCheck(float seconds)
        {
            var stats = new Dictionary<int, (int samples, int buckled, int crossed, float worstKnee, string worstAt)>();
            var endOfFrame = new WaitForEndOfFrame();
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return endOfFrame;
                foreach (Story.StoryNpc npc in Story.StoryNpc.All)
                {
                    if (npc == null || !npc.IsUpright || npc.IsSwimming || npc.Ride != null) continue;
                    Avatars.AvatarRig rig = npc.GetComponentInChildren<Avatars.AvatarRig>();
                    if (rig == null || !rig.IsBuilt || rig.GeneratedBody == null) continue;
                    if (!Application.isBatchMode && (rig.Renderer == null || !rig.Renderer.isVisible)) continue;
                    float knee = 1f;
                    foreach (var (thigh, foot) in new[] { (Avatars.AvatarRig.Bone.ThighL, Avatars.AvatarRig.Bone.FootL), (Avatars.AvatarRig.Bone.ThighR, Avatars.AvatarRig.Bone.FootR) })
                        knee = Mathf.Min(knee, Vector3.Distance(rig[thigh].position, rig[foot].position) / (rig.ThighLength + rig.ShinLength));
                    Transform root = rig.transform;
                    float apart = root.InverseTransformPoint(rig[Avatars.AvatarRig.Bone.FootR].position).x - root.InverseTransformPoint(rig[Avatars.AvatarRig.Bone.FootL].position).x;
                    int body = rig.GeneratedBody.Id;
                    stats.TryGetValue(body, out var st);
                    st.samples++;
                    if (st.samples == 1) st.worstKnee = 1f;
                    if (knee < 0.8f) st.buckled++;
                    if (apart < 0.03f) st.crossed++;
                    if (knee < st.worstKnee) { st.worstKnee = knee; st.worstAt = $"{npc.Name} {npc.Activity} at {npc.transform.position:F1}, moving {npc.IsMoving}"; }
                    stats[body] = st;
                }
            }
            foreach (var (body, st) in stats.OrderBy(k => k.Key))
                Debug.Log($"[Legs] body {body}: {st.samples} samples, knees buckled {100f * st.buckled / Mathf.Max(1, st.samples):F1}%, feet crossed {100f * st.crossed / Mathf.Max(1, st.samples):F1}%, worst knee {st.worstKnee:F2} ({st.worstAt})");
        }

        /// <summary>
        /// Every character's drawn body, sampled 10 times a second: over a full turn within 4 s while moving under
        /// 1.5 m is spinning. Also counts, per body id, how much the drawn bodies turn while standing still.
        /// </summary>
        private IEnumerator SpinWatch(float seconds)
        {
            var last = new Dictionary<Story.StoryNpc, (float yaw, Vector3 from, float turn, float start)>();
            var stillTurn = new Dictionary<int, (float turn, float time)>();
            int spins = 0;
            float end = Time.realtimeSinceStartup + seconds;
            var wait = new WaitForSeconds(0.1f);
            while (Time.realtimeSinceStartup < end)
            {
                yield return wait;
                foreach (Story.StoryNpc npc in Story.StoryNpc.All)
                {
                    if (npc == null || !npc.IsUpright || npc.IsSwimming) { if (npc != null) last.Remove(npc); continue; }
                    float yaw = npc.DrawnYaw;
                    Vector3 p = npc.transform.position;
                    if (!last.TryGetValue(npc, out var s)) { last[npc] = (yaw, p, 0f, Time.time); continue; }
                    float d = Mathf.Abs(Mathf.DeltaAngle(s.yaw, yaw));
                    s.turn += d;
                    int body = npc.Look.Body;
                    stillTurn.TryGetValue(body, out var st);
                    if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(s.from.x, 0f, s.from.z)) < 0.05f * (Time.time - s.start + 0.1f))
                        stillTurn[body] = (st.turn + d, st.time + 0.1f);
                    s.yaw = yaw;
                    if (Time.time - s.start >= 4f)
                    {
                        float moved = Vector3.Distance(p, s.from);
                        if (s.turn > 120f && moved < 1.5f)
                        {
                            if (s.turn > 360f) spins++;
                            Debug.Log($"[Spin] {npc.Name} (body {body}, {npc.Activity}) at {p:F1}: turned {s.turn:F0} degrees in 4 s, moved {moved:F2} m, moving {npc.IsMoving}");
                        }
                        s = (yaw, p, 0f, Time.time);
                    }
                    last[npc] = s;
                }
            }
            var line = new StringBuilder($"[Spin] done: {spins} spinning; turning while standing (deg/s) per body:");
            foreach (var (body, st) in stillTurn.OrderBy(k => k.Key)) line.Append($" {body}:{(st.time > 0f ? st.turn / st.time : 0f):F0}");
            Debug.Log(line.ToString());
        }

        private void Register() =>
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
