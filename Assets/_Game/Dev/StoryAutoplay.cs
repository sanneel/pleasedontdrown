using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World.Water;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// Plays the story by itself, for automated tests of the whole flow: <c>-pdd-autoplay [last beat]</c> on a host
    /// (default 1.10 = all of chapter 1). It does what a player would, through the same console commands and
    /// interactions, only faster on its feet (it teleports where a player would walk): swims out to whoever is in
    /// trouble, tows them in, does CPR, knocks the robber down, picks lost things up and hands them in, takes the
    /// keys and rides the jet ski across. The log gets one line per beat and a last line starting
    /// <c>[Autoplay] RESULT PASS</c> or <c>[Autoplay] RESULT FAIL</c>, then the game quits.
    /// <c>-pdd-timescale 2</c> runs the clock faster. <c>-pdd-autoshots</c> also saves pictures to Screenshots/Autoplay
    /// as it goes (run without -nographics): through the player's eyes, of the player from outside, of the beach and
    /// of whoever is swimming, so the animations can be looked at afterwards.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class StoryAutoplay : MonoBehaviour
    {
        private const float BeatLimit = 300f;       // game seconds one beat may take before the run counts as stuck
        private static readonly string[] Beats =
        {
            "1.1", "1.2", "1.3", "1.4", "1.5", "1.6", "1.7", "1.8", "1.9", "1.10", "2.1", "2.2", "2.3", "2.4", "2.5", "2.6", "2.7", "end"
        };

        private readonly List<string> _errors = new();
        private readonly List<string> _warnings = new();
        private readonly List<string> _beatTimes = new();
        private int _errorCount;
        private bool _shots;
        private Camera _shotCamera;
        private float _nextPeriodic;
        private readonly Dictionary<string, int> _shotCounts = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!DevLaunchArgs.Has("-pdd-autoplay") && !DevLaunchArgs.Has("-pdd-crew")) return;
            var go = new GameObject("StoryAutoplay");
            DontDestroyOnLoad(go);
            go.AddComponent<StoryAutoplay>();
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            _errorCount++;
            string first = message.Length > 220 ? message.Substring(0, 220) : message;
            if (_errors.Count < 12 && !_errors.Contains(first)) _errors.Add(first);
        }

        private static void Say(string text) => Debug.Log($"[Autoplay] {text}");

        private void Warn(string text)
        {
            Debug.Log($"[Autoplay] WARN {text}");
            if (_warnings.Count < 20 && !_warnings.Contains(text)) _warnings.Add(text);
        }

        private static void Exec(string command)
        {
            Debug.Log($"[Autoplay] > {command}");
            DevCommands.Execute(command);
        }

        private IEnumerator Start()
        {
            if (DevLaunchArgs.Has("-pdd-crew"))
            {
                while (PlayerHub.Local == null || StoryDirector.Instance == null) yield return null;
                DevCommands.Output += line => Debug.Log($"[Console] {line}");
                bool observedWave = false;
                while (true)
                {
                    if (!observedWave && TsunamiState.Age > 20f && TsunamiState.Age < 40f)
                    {
                        observedWave = true;
                        Say($"client sees synchronized tsunami: age {TsunamiState.Age:F2}, dock height {WaterSurface.HeightAt(32f, -8f):F2}");
                    }
                    if (StoryDirector.Instance.BeatId == "1.10") yield return RideToIsland2(PlayerHub.Local);
                    yield return new WaitForSeconds(0.3f);
                }
            }
            string last = DevLaunchArgs.Value("-pdd-autoplay");
            if (string.IsNullOrEmpty(last) || Array.IndexOf(Beats, last) < 0) last = "1.10";
            if (float.TryParse(DevLaunchArgs.Value("-pdd-timescale"), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
                Time.timeScale = Mathf.Clamp(scale, 0.5f, 4f);
            DevCommands.Output += line => Debug.Log($"[Console] {line}");
            _shots = DevLaunchArgs.Has("-pdd-autoshots");

            float deadline = Time.realtimeSinceStartup + 60f;
            while ((PlayerHub.Local == null || StoryDirector.Instance == null || string.IsNullOrEmpty(StoryDirector.Instance.BeatId)) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (PlayerHub.Local == null || StoryDirector.Instance == null || string.IsNullOrEmpty(StoryDirector.Instance.BeatId))
            {
                yield return Finish(false, "the session or the story never started");
                yield break;
            }
            Say($"playing the story up to the end of beat {last} (time scale {Time.timeScale:0.#})");
            yield return new WaitForSeconds(1f);

            string beat = null;
            float beatStart = Time.time, started = Time.time, nextStatus = Time.time + 20f;
            while (true)
            {
                StoryDirector story = StoryDirector.Instance;
                if (story == null || PlayerHub.Local == null)
                {
                    yield return Finish(false, "the session ended");
                    yield break;
                }
                string now = story.BeatId;
                if (now != beat)
                {
                    if (beat != null) _beatTimes.Add($"{beat} {Time.time - beatStart:0}s");
                    Say($"beat {now} (after {Time.time - started:0} s): {story.Objective}");
                    beat = now;
                    beatStart = Time.time;
                    if (Array.IndexOf(Beats, beat) > Array.IndexOf(Beats, last)) break;
                    _nextPeriodic = Time.time + 2f;
                }
                if (_shots && Time.time > _nextPeriodic)
                {
                    _nextPeriodic = Time.time + 14f;
                    string stamp = $"{beat.Replace('.', '_')}_t{Time.time - started:000}";
                    Later(() =>
                    {
                        Shoot(stamp + "_eyes", Camera.main);
                        ShootPlayer(stamp + "_me");
                        ShootBeach(stamp + "_beach");
                        ShootSwimmer(stamp + "_swimmer");
                    });
                }
                if (Time.time - beatStart > BeatLimit)
                {
                    DumpState();
                    yield return Finish(false, $"stuck in beat {beat} for {BeatLimit:0} s (\"{story.Objective}\" {story.Progress}/{story.Goal})");
                    yield break;
                }
                if (Time.time > nextStatus)
                {
                    nextStatus = Time.time + 20f;
                    Say($"... beat {beat} for {Time.time - beatStart:0} s: \"{story.Objective}\" {story.Progress}/{story.Goal}, ${Economy.Money}, me at {PlayerHub.Local.transform.position:F0}");
                }
                yield return Act(beat);
                yield return new WaitForSeconds(0.3f);
            }
            var stats = StoryDirector.Instance.ChapterStats;
            yield return Finish(true, $"reached beat {beat} in {Time.time - started:0} game seconds; ${Economy.Money}; " +
                                      $"this chapter so far: rescued {stats.rescued}, lost {stats.lost}, returned {stats.returned}");
        }

        private IEnumerator Finish(bool passed, string detail)
        {
            Say($"beats: {string.Join(", ", _beatTimes)}");
            foreach (string w in _warnings) Say($"warning: {w}");
            foreach (string e in _errors) Say($"error in the log: {e}");
            bool clean = passed && _errorCount == 0;
            Say($"RESULT {(clean ? "PASS" : "FAIL")}: {detail}; {_errorCount} errors logged, {_warnings.Count} warnings");
            yield return new WaitForSecondsRealtime(1.5f);
            Application.Quit(clean ? 0 : 1);
        }

        private void DumpState()
        {
            PlayerHub me = PlayerHub.Local;
            Say($"state: me at {me.transform.position:F1}, seat {(me.Motor.Seat != null ? me.Motor.Seat.DisplayName : "-")}, holding {(me.Hands.HeldItem != null ? me.Hands.HeldItem.DisplayName : "-")}");
            foreach (VictimBrain v in VictimBrain.All)
                Say($"  tourist {v.Name}: {v.State}, ashore {v.IsAshore}, held {v.Item.IsHeld}, at {v.transform.position:F1}");
            foreach (StoryNpc n in StoryNpc.All)
                if (n.Role != NpcRole.Guest || n.MaxHealth > 0)
                    Say($"  npc {n.Name} ({n.Role}): pose {n.Pose}, hp {n.Health}/{n.MaxHealth}, at {n.transform.position:F1}");
            foreach (Item i in Item.All)
                if (i.GetComponent<LostItem>() != null || i.DisplayName.Contains("Keys"))
                    Say($"  item {i.DisplayName}: held {i.IsHeld}, at {i.transform.position:F1}");
            foreach (Vehicle v in Vehicle.All)
                Say($"  vehicle {v.DisplayName}: at {v.transform.position:F1}, driver {(v.Driver != null ? v.Driver.DisplayName : "-")}, locked {v.IsLocked}");
        }

        // ------------------------------------------------------------------ what a player would do next

        private IEnumerator Act(string beat)
        {
            PlayerHub me = PlayerHub.Local;
            bool island2 = me.transform.position.z < -120f;
            if (beat == "1.10") { yield return RideToIsland2(me); yield break; }
            if (beat == "1.3") { yield return TalkRental(me); yield break; }
            if (beat == "1.4")
            {
                me.Motor.Teleport(new Vector3(2f, 2f, 32f));
                me.Look.LookAt(new Vector3(20f, 1f, -30f));
                yield return new WaitForSeconds(1f);
                yield break;
            }
            if (beat == "1.9")
            {
                if (Carries(me, "Fleet Key Case")) yield return TalkRental(me);
                else foreach (Item item in Item.All)
                    if (item.DisplayName == "Fleet Key Case" && !item.IsHeld) { yield return PickUp(me, item); break; }
                yield break;
            }

            // Someone in trouble comes first.
            foreach (VictimBrain v in VictimBrain.All)
                if (v != null && v.IsSpawned && v.State.NeedsHelp())
                {
                    yield return Rescue(me, v);
                    yield break;
                }

            // A robber (or pirate) on his feet: knock him down.
            foreach (StoryNpc n in StoryNpc.All)
                if (n != null && n.MaxHealth > 0 && n.Health > 0 && n.Role is NpcRole.Robber or NpcRole.Pirate)
                {
                    Exec($"goto {FirstWord(n.Name)}");
                    yield return new WaitForSeconds(0.3f);
                    Exec("whack 1");
                    yield return new WaitForSeconds(0.5f);
                    Tagged("robber", me);
                    yield break;
                }

            // The jet ski keys, when they're lying about.
            if (!island2 && !Carries(me, "Jet Ski Keys"))
                foreach (Item i in Item.All)
                    if (!i.IsHeld && i.DisplayName == "Jet Ski Keys" && i.transform.position.z > -120f && i.transform.position.x > -150f)
                    {
                        yield return PickUp(me, i);
                        yield break;
                    }

            // Lost things: pick them up, and hand them in when the pockets fill or nothing more is lying about.
            if (!island2 && me.Motor.Seat == null)
            {
                int carried = 0, pockets = 0;
                foreach (Item i in Item.All)
                    if (i.Holder == me)
                    {
                        pockets++;
                        if (i.GetComponent<LostItem>() != null) carried++;
                    }
                Item loose = null;
                foreach (Item i in Item.All)
                    if (!i.IsHeld && i.GetComponent<LostItem>() != null && i.transform.position.z > -120f && i.transform.position.x > -150f)
                        loose = i;
                if (loose != null && pockets < PlayerHands.SlotCount)
                {
                    yield return PickUp(me, loose);
                    yield break;
                }
                if (carried > 0)
                {
                    yield return HandIn(me, carried);
                    yield break;
                }
            }

            if (beat == "1.8") yield return WadeToFalseAlarm(me);
            if (beat == "1.10") yield return RideToIsland2(me);
        }

        private IEnumerator TalkRental(PlayerHub me)
        {
            foreach (StoryNpc npc in StoryNpc.All)
                if (npc.Name == "Milo" && npc.IsTalkable)
                {
                    me.Motor.Teleport(npc.transform.position + Vector3.back * 1.5f + Vector3.up * 0.2f);
                    me.Look.LookAt(npc.HeadPosition);
                    yield return new WaitForSeconds(0.4f);
                    npc.OnInteract(me);
                    yield return new WaitForSeconds(0.5f);
                    break;
                }
        }

        /// <summary>The false alarm: walk up to whoever is screaming in the shallows (the marker is on them).</summary>
        private IEnumerator WadeToFalseAlarm(PlayerHub me)
        {
            Vector3? marker = StoryDirector.Instance.MarkerPosition;
            if (marker == null) yield break;
            Vector3 at = marker.Value - Vector3.up * 2.1f;
            Vector3 stand = at + Shoreward(at) * 1.6f;
            float ground = Shore.GroundHeightAt(stand + Vector3.up * 3f);
            if (!float.IsNaN(ground)) stand.y = ground + 0.2f;
            me.Motor.Teleport(stand);
            me.Look.LookAt(at + Vector3.up * 1.5f);
            yield return new WaitForSeconds(1f);
            Tagged("falsealarm", me);
        }

        private static string FirstWord(string name)
        {
            int space = name.IndexOf(' ');
            return space > 0 ? name.Substring(0, space) : name;
        }

        private static bool Carries(PlayerHub me, string itemName)
        {
            foreach (Item i in Item.All)
                if (i.Holder == me && i.DisplayName == itemName) return true;
            return false;
        }

        private static int CountCarried(PlayerHub me, string itemName)
        {
            int count = 0;
            foreach (Item i in Item.All)
                if (i.Holder == me && i.DisplayName == itemName) count++;
            return count;
        }

        private IEnumerator PickUp(PlayerHub me, Item item)
        {
            string itemName = item.DisplayName;
            // Next to this very one (several things can share a name), looking at it.
            Vector3 at = item.transform.position;
            float ground = Shore.GroundHeightAt(at + Vector3.up * 3f);
            Vector3 stand = at + Vector3.forward * 1.2f;
            stand.y = Shore.WaterDepthAt(stand) > 1.3f && WaterSurface.Exists ? WaterSurface.HeightAt(stand) - 1.2f : (float.IsNaN(ground) ? at.y : ground) + 0.2f;
            me.Motor.Teleport(stand);
            me.Look.LookAt(at);
            yield return new WaitForSeconds(0.4f);
            // "grab" takes the nearest thing of that name, which may be this one's twin lying beside it: either counts.
            int before = CountCarried(me, itemName);
            Exec($"grab {itemName.Substring(itemName.LastIndexOf(' ') + 1)}");
            float until = Time.time + 5f;
            while (Time.time < until && item != null && !item.IsHeld && CountCarried(me, itemName) <= before) yield return null;
            if (item != null && !item.IsHeld && CountCarried(me, itemName) <= before) Warn($"couldn't pick up the {itemName} at {item.transform.position:F1}");
            yield return new WaitForSeconds(0.3f);
        }

        private static int CarriedLost(PlayerHub me)
        {
            int n = 0;
            foreach (Item i in Item.All)
                if (i.Holder == me && i.GetComponent<LostItem>() != null) n++;
            return n;
        }

        /// <summary>To the Lost &amp; Found counter, look at it, press Interact.</summary>
        private IEnumerator HandIn(PlayerHub me, int carried)
        {
            LostAndFound counter = LostAndFound.Instance;
            if (counter == null)
            {
                Warn("no Lost & Found in the scene");
                yield break;
            }
            // Stand outside the hut, by the counter: try the four sides, take the first where the counter can be aimed at.
            Vector3 at = counter.transform.position;
            foreach (Vector3 side in new[] { Vector3.right, Vector3.forward, Vector3.back, Vector3.left })
            {
                Vector3 stand = at + side * 2.2f;
                float ground = Shore.GroundHeightAt(stand + Vector3.up * 3f);
                if (float.IsNaN(ground)) continue;
                stand.y = ground + 0.2f;
                me.Motor.Teleport(stand);
                me.Look.LookAt(at);
                yield return new WaitForSeconds(0.5f);
                Exec("use");
                yield return new WaitForSeconds(0.8f);
                if (CarriedLost(me) < carried) yield break;
            }
            Warn($"the Lost & Found counter at {at:F1} couldn't be used by looking at it from any side; handing in directly");
            counter.OnInteract(me);
            yield return new WaitForSeconds(0.8f);
        }

        private static Vector3 Shoreward(Vector3 at) => at.z < -120f ? Vector3.back : Vector3.forward;

        /// <summary>Out to them, grab, tow to the beach in short hops, lay them down, CPR.</summary>
        private IEnumerator Rescue(PlayerHub me, VictimBrain v)
        {
            bool mine = v.Item.IsHeld && v.Item.Holder == me;
            if (!mine && !v.IsAshore && !v.Item.IsHeld)
            {
                // Right next to this very tourist (two can share a name), treading water.
                Vector3 beside = v.transform.position + Vector3.forward * 1.3f;
                beside.y = WaterSurface.Exists ? WaterSurface.HeightAt(beside) - 1.2f : beside.y;
                me.Motor.Teleport(beside);
                me.Look.LookAt(v.transform.position);
                yield return new WaitForSeconds(0.5f);
                Exec("grab tourist");
                float until = Time.time + 3f;
                while (Time.time < until && v != null && !v.Item.IsHeld) yield return null;
                if (v == null) yield break;
                if (!v.Item.IsHeld)
                {
                    Warn($"couldn't grab {v.Name} ({v.State}) at {v.transform.position:F1} from {me.transform.position:F1}");
                    yield break;
                }
                mine = v.Item.Holder == me;
                yield return new WaitForSeconds(0.4f);
                Tagged("tow", me);
            }
            if (mine)
            {
                // Tow them in: a few metres at a time, like swimming, until we stand on dry sand.
                Vector3 dir = Shoreward(me.transform.position);
                for (int hop = 0; hop < 60 && v != null && v.Item.IsHeld; hop++)
                {
                    Vector3 p = me.transform.position + dir * 4f;
                    float depth = Shore.WaterDepthAt(p + Vector3.up * 2f);
                    float ground = Shore.GroundHeightAt(p + Vector3.up * 5f);
                    p.y = depth > 1.3f && WaterSurface.Exists ? WaterSurface.HeightAt(p) - 1.2f : (float.IsNaN(ground) ? p.y : ground + 0.2f);
                    me.Motor.Teleport(p);
                    yield return new WaitForSeconds(0.3f);
                    if (depth < 0.05f) break;
                }
                yield return new WaitForSeconds(0.5f);
                if (v != null && v.Item.IsHeld && v.Item.Holder == me)
                {
                    Exec("drop");
                    yield return new WaitForSeconds(1.2f);
                }
            }
            if (v != null && v.State == VictimState.Unconscious && !v.Item.IsHeld)
            {
                if ((v.transform.position - me.transform.position).sqrMagnitude > 2f * 2f)
                {
                    Vector3 beside = v.transform.position + Vector3.forward * 1.6f;
                    float ground = Shore.GroundHeightAt(beside + Vector3.up * 3f);
                    if (!float.IsNaN(ground)) beside.y = ground + 0.2f;
                    me.Motor.Teleport(beside);
                    me.Look.LookAt(v.transform.position);
                    yield return new WaitForSeconds(0.4f);
                }
                Exec("cpr 8");
                yield return new WaitForSeconds(0.7f);
                Tagged("cpr", me);
                yield return new WaitForSeconds(1.3f);
            }
        }

        /// <summary>Onto the robber's jet ski and south to the hotel island, steering at it.</summary>
        private IEnumerator RideToIsland2(PlayerHub me)
        {
            if (me.Motor.Seat == null)
            {
                Vehicle ski = null;
                foreach (Vehicle v in Vehicle.All)
                    if (v.DisplayName.StartsWith("Rental Jet Ski") && !v.IsLocked && v.Driver == null && v.transform.position.z > -120f)
                        ski = v;
                if (ski == null)
                {
                    Warn("no jet ski on island 1");
                    yield break;
                }
                Vector3 boarding = ski.transform.position + ski.transform.right * 1.6f;
                boarding.y = WaterSurface.HeightAt(boarding) - 1.0f;
                me.Motor.Teleport(boarding);
                me.Look.LookAt(ski.transform.position + Vector3.up * 0.6f);
                yield return new WaitForSeconds(0.6f);
                Exec("use");
                yield return new WaitForSeconds(1f);
                if (me.Motor.Seat == null)
                {
                    Warn($"couldn't get on the {ski.DisplayName} by looking at it (prompt: {ski.GetPrompt(me)}); getting on directly");
                    ski.OnInteract(me);
                    yield return new WaitForSeconds(1f);
                }
                yield break;
            }
            Vehicle ride = me.Motor.Seat;
            Vector3 to = StoryDirector.Instance.Island2Arrival - ride.transform.position;
            to.y = 0f;
            float angle = Vector3.SignedAngle(ride.transform.forward, to, Vector3.up);
            float steer = Mathf.Clamp(angle / 35f, -1f, 1f);
            float throttle = Mathf.Abs(angle) > 100f ? 0.5f : 1f;
            DevCommands.Execute(string.Format(CultureInfo.InvariantCulture, "drive 0.7 {0:0.00} {1:0.00}", steer, throttle));
            Tagged("ride", me, 3);
        }

        // ------------------------------------------------------------------ pictures

        /// <summary>The first few times something happens: through the player's eyes and from outside.</summary>
        private void Tagged(string tag, PlayerHub me, int most = 2)
        {
            if (!_shots) return;
            _shotCounts.TryGetValue(tag, out int n);
            if (n >= most) return;
            _shotCounts[tag] = n + 1;
            string shot = $"{tag}{n + 1}";
            Later(() =>
            {
                Shoot(shot + "_eyes", Camera.main);
                ShootPlayer(shot + "_me");
            });
        }

        private readonly List<Action> _pendingShots = new();

        /// <summary>
        /// Pictures are taken at the end of the frame (this script's LateUpdate runs after the others), so hands,
        /// bodies and the camera are where this frame put them: a picture taken straight after a teleport showed the
        /// first-person hands still at the old place.
        /// </summary>
        private void Later(Action shoot)
        {
            if (_shots) _pendingShots.Add(shoot);
        }

        private void LateUpdate()
        {
            if (_pendingShots.Count == 0) return;
            foreach (Action shoot in _pendingShots) shoot();
            _pendingShots.Clear();
        }

        private Camera ShotCamera(Vector3 from, Vector3 at)
        {
            if (_shotCamera == null)
            {
                _shotCamera = new GameObject("AutoplayCamera").AddComponent<Camera>();
                DontDestroyOnLoad(_shotCamera.gameObject);
                _shotCamera.enabled = false;
                _shotCamera.fieldOfView = 55f;
                _shotCamera.nearClipPlane = 0.1f;
                _shotCamera.farClipPlane = 1500f;
            }
            _shotCamera.transform.position = from;
            _shotCamera.transform.LookAt(at);
            return _shotCamera;
        }

        /// <summary>The local player from a few metres away (our own body is normally only a shadow to us).</summary>
        private void ShootPlayer(string shot)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null) return;
            var avatar = me.GetComponentInChildren<PlayerAvatar>();
            if (avatar != null) avatar.SetLocal(false);
            Vector3 chest = me.transform.position + Vector3.up * 1.1f;
            Vector3 flat = Vector3.ProjectOnPlane(me.Head.forward, Vector3.up).normalized;
            Shoot(shot, ShotCamera(chest + flat * 3.4f + Vector3.Cross(Vector3.up, flat) * 1.6f + Vector3.up * 0.9f, chest));
            if (avatar != null) avatar.SetLocal(true);
        }

        /// <summary>Island 1's beach and the sea in front of it, from above the station.</summary>
        private void ShootBeach(string shot)
        {
            if (PlayerHub.Local == null || PlayerHub.Local.transform.position.z < -120f) return;
            Shoot(shot, ShotCamera(new Vector3(0f, 9f, 10f), new Vector3(0f, 0f, -22f)));
        }

        /// <summary>Whoever is swimming along right now, from the side at the waterline.</summary>
        private void ShootSwimmer(string shot)
        {
            StoryNpc best = null;
            foreach (StoryNpc n in StoryNpc.All)
                if (n != null && n.IsSwimming && n.IsMoving && n.transform.position.z > -120f && n.transform.position.x > -150f) best = n;
            if (best == null) return;
            Vector3 p = best.transform.position;
            float surface = WaterSurface.Exists ? WaterSurface.HeightAt(p) : p.y + 1.4f;
            Vector3 at = new Vector3(p.x, surface - 0.1f, p.z);
            Shoot(shot, ShotCamera(at + best.transform.right * 3.2f + Vector3.up * 0.9f, at));
        }

        private void Shoot(string shot, Camera camera)
        {
            if (!_shots || camera == null) return;
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "Autoplay"));
            Directory.CreateDirectory(folder);
            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGBHalf);
            var image = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture before = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(folder, shot + ".jpg"), image.EncodeToJPG(85));
            }
            catch (Exception e)
            {
                Warn($"picture {shot} failed: {e.Message}");
                _shots = false;
            }
            finally
            {
                RenderTexture.active = before;
                rt.Release();
                Destroy(rt);
                Destroy(image);
            }
        }
    }
}
