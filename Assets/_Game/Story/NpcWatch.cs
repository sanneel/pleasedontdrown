using System.Collections.Generic;
using PleaseDontDrown.Core;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Host test tool: watches every story character and reports what looks wrong, so bot behaviour can be checked
    /// in automated runs (<c>-pdd-npcwatch</c> or the <c>npcwatch</c> command). Problems it looks for:
    ///  * stuck: told to go somewhere but hardly moved for 3 s (or gave up on the route);
    ///  * in a wall: the body overlaps static geometry;
    ///  * under the ground / floating: feet not on the ground while walking;
    ///  * popping: moved far faster than it can walk (outside a teleport);
    ///  * idle too long: a walker or wader that hasn't moved for 60 s, a swimmer treading water for over 16 s;
    ///  * swimming in place: on a swim route but making under 0.3 m/s for 3 s;
    ///  * broken body: the rig's joints far from where they belong (stretched limbs, a head below the hips when upright).
    /// Each problem is logged once per character per kind every 10 s, and a summary every 30 s.
    /// </summary>
    public class NpcWatch : MonoBehaviour
    {
        private sealed class Track
        {
            public Vector3 Last;
            public Vector3 Anchor;          // where it was when the "moving" timer started
            public float AnchorTime;
            public float LastMoved;
            public readonly Dictionary<string, float> Reported = new();
            public readonly Dictionary<string, float> Since = new();   // when a lasting-problem check first failed
            public readonly HashSet<string> Seen = new();
        }

        private readonly Dictionary<StoryNpc, Track> _tracks = new();
        private readonly Dictionary<string, int> _counts = new();
        private readonly Collider[] _overlaps = new Collider[16];
        private float _nextSample;
        private float _nextSummary;
        private float _until = float.MaxValue;

        public static NpcWatch Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var go = new GameObject("NpcWatch");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NpcWatch>();
            Instance.enabled = DevLaunchArgs.Has("-pdd-npcwatch");
        }

        private void Awake()
        {
            DevCommands.Register("npcwatch", "[seconds | off]", "Report stuck / clipping / broken story characters to the log (host).", args =>
            {
                if (args.Length > 0 && args[0] == "off")
                {
                    enabled = false;
                    return;
                }
                _until = args.Length > 0 ? Time.time + DevCommands.ParseFloat(args, 0) : float.MaxValue;
                _counts.Clear();
                enabled = true;
                DevCommands.Print("watching story characters");
            }, cheat: true, owner: this);
        }

        private void OnEnable() => Debug.Log("[NpcWatch] on");

        private void Update()
        {
            if (Time.time > _until)
            {
                Summary();
                enabled = false;
                return;
            }
            if (Time.time < _nextSample) return;
            float dt = Time.time - (_nextSample - 0.25f);
            _nextSample = Time.time + 0.25f;
            foreach (StoryNpc npc in StoryNpc.All)
                if (npc != null && npc.IsServerInitialized) Check(npc, Mathf.Max(0.05f, dt));
            if (Time.time >= _nextSummary)
            {
                _nextSummary = Time.time + 30f;
                Summary();
            }
        }

        private void Check(StoryNpc npc, float dt)
        {
            Vector3 p = npc.transform.position;
            if (!_tracks.TryGetValue(npc, out Track t))
            {
                _tracks[npc] = t = new Track { Last = p, Anchor = p, AnchorTime = Time.time, LastMoved = Time.time };
                return;
            }
            t.Seen.Clear();
            CheckLasting(npc, t, p, dt);
            foreach (string kind in new List<string>(t.Since.Keys))
                if (!t.Seen.Contains(kind)) t.Since.Remove(kind);
        }

        private void CheckLasting(StoryNpc npc, Track t, Vector3 p, float dt)
        {
            float step = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(t.Last.x, 0f, t.Last.z));
            if (step > 0.02f) t.LastMoved = Time.time;
            bool teleported = npc.ServerTeleportedSince(Time.time - dt - 0.05f);
            if (!teleported && step > 8f * dt + 0.3f && npc.Ride == null)
                Report(npc, t, "popping", $"moved {step:F2} m in {dt:F2} s");
            t.Last = p;

            // Stuck on a route.
            if (npc.IsMoving && npc.IsUpright)
            {
                if (Vector3.Distance(p, t.Anchor) > 0.5f || teleported)
                {
                    t.Anchor = p;
                    t.AnchorTime = Time.time;
                }
                else if (Time.time - t.AnchorTime > 3f)
                    Report(npc, t, "stuck", $"{npc.PathInfo}, target {npc.MoveTarget:F1}");
            }
            else
            {
                t.Anchor = p;
                t.AnchorTime = Time.time;
            }
            if (npc.TakeGaveUp(out string why)) Report(npc, t, "gave-up", why);

            if (npc.Activity is NpcActivity.Stroll or NpcActivity.Wade && Time.time - t.LastMoved > 60f)
                Report(npc, t, "idle", $"{npc.Activity} hasn't moved for {Time.time - t.LastMoved:F0} s");
            // Swimmers: a pause to tread water lasts 12 s at most, and a swim leg is swum at a real pace (treading
            // water "on the way" somewhere is the swimming-in-place look).
            if (npc.Activity == NpcActivity.Swim && npc.IsSwimming && !npc.IsMoving && Time.time - t.LastMoved > 16f)
                Report(npc, t, "swim-idle", $"treading water for {Time.time - t.LastMoved:F0} s");
            if (npc.IsSwimming && npc.IsMoving && npc.Ride == null && step / dt < 0.3f)
                Lasting(npc, t, "swim-slow", $"routing at {step / dt:F2} m/s ({npc.PathInfo})", 3f);

            // Feet and walls (upright people on land only: lying/sitting bodies sit on towels by design).
            bool swimming = npc.IsSwimming;
            if (npc.IsUpright && npc.Ride == null)
            {
                if (!swimming)
                {
                    float ground = Shore.GroundHeightAt(p + Vector3.up * 0.7f);
                    if (!float.IsNaN(ground))
                    {
                        if (p.y < ground - 0.15f) Lasting(npc, t, "underground", $"feet {ground - p.y:F2} m below the ground");
                        else if (p.y > ground + 0.3f) Lasting(npc, t, "floating", $"feet {p.y - ground:F2} m above the ground");
                    }
                }
                Vector3 a = p + Vector3.up * 0.8f, b = p + Vector3.up * 1.5f; // above step height
                int n = Physics.OverlapCapsuleNonAlloc(a, b, 0.2f, _overlaps, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    Collider c = _overlaps[i];
                    if (c.attachedRigidbody != null || c.GetComponent<Seabed>() != null || c is TerrainCollider) continue;
                    if (c.transform.IsChildOf(npc.transform)) continue;
                    Lasting(npc, t, "in-wall", $"overlaps {c.name}");
                    break;
                }
            }

            if (npc.BodyProblem(out string body)) Lasting(npc, t, "body", body);
        }

        /// <summary>A problem only counts once it has lasted (pose blends and a wave passing are not problems).</summary>
        private void Lasting(StoryNpc npc, Track t, string kind, string detail, float seconds = 0.75f)
        {
            t.Seen.Add(kind);
            if (!t.Since.TryGetValue(kind, out float since)) t.Since[kind] = since = Time.time;
            if (Time.time - since >= seconds) Report(npc, t, kind, detail);
        }

        private void Report(StoryNpc npc, Track t, string kind, string detail)
        {
            _counts[kind] = _counts.TryGetValue(kind, out int c) ? c + 1 : 1;
            if (t.Reported.TryGetValue(kind, out float last) && Time.time - last < 10f) return;
            t.Reported[kind] = Time.time;
            Debug.Log($"[NpcWatch] PROBLEM {kind,-11} {npc.Name} ({npc.Activity}, {npc.Pose}) at {npc.transform.position:F1}: {detail}");
        }

        private void Summary()
        {
            int upright = 0, moving = 0, swimming = 0, codeBuilt = 0;
            foreach (StoryNpc npc in StoryNpc.All)
            {
                if (npc == null) continue;
                if (Avatars.AvatarBodyLibrary.Get(npc.Look.Body) == null)
                {
                    codeBuilt++; // no generated model: a stand-in
                    if (codeBuilt <= 4) Debug.Log($"[NpcWatch] code-built: {npc.Name} ({npc.Role}) body id {npc.Look.Body}");
                }
                if (npc.IsUpright) upright++;
                if (npc.IsMoving) moving++;
                if (npc.IsSwimming) swimming++;
            }
            var parts = new List<string>();
            foreach (var kv in _counts) parts.Add($"{kv.Key} {kv.Value}");
            Debug.Log($"[NpcWatch] t={Time.time:F0}s characters {StoryNpc.All.Count} (upright {upright}, moving {moving}, swimming {swimming}, code-built {codeBuilt}); " +
                      $"problem samples: {(parts.Count == 0 ? "none" : string.Join(", ", parts))}");
        }
    }
}
