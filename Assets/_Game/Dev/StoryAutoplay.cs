using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Creatures;
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
            "1.1", "1.2", "1.3", "1.4", "1.5", "1.9", "1.10", "2.1", "2.2", "2.3", "2.4", "2.5", "2.5b", "2.5c", "2.6", "2.7",
            "3.1", "3.2", "3.3", "3.4", "3.5", "3.6", "3.7", "3.7b", "3.8", "3.9", "3.10", "4.1", "4.2", "4.3", "4.4", "4.5", "4.6", "4.7", "4.8", "4.9", "end"
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
                    if (beat == "2.5c") _galaFundSince = Time.time;
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
            if (me.Motor.Seat != null && beat.StartsWith("2.") && beat != "2.7")
            {
                Exec("getoff"); // arrived on the jet ski: on foot from here
                yield return new WaitForSeconds(0.8f);
                yield break;
            }
            if (beat.StartsWith("3.")) { yield return Chapter3(me, beat); yield break; }
            if (beat.StartsWith("4.")) { yield return Chapter4(me, beat); yield break; }
            if (beat == "2.1") { yield return TalkToMarked(me); yield break; }
            if (beat == "2.2" && !Carries(me, "Pistol")) { yield return BuyAt(me, "Pistol"); yield break; }
            if (beat == "2.7") { yield return Beat27(me); yield break; }
            if (beat == "2.5c" && StoryDirector.Instance.MarkerLabel == "Director") { yield return TalkToMarked(me); yield break; }
            if (beat == "2.6" && StoryDirector.Instance.MarkerLabel == "Gala" && StoryDirector.Instance.MarkerPosition is { } gala)
            {
                Stand(me, gala - Vector3.up * 1.5f + Vector3.right * 1.5f);
                yield return new WaitForSeconds(0.5f);
                yield break;
            }

            // Someone in trouble comes first.
            foreach (VictimBrain v in VictimBrain.All)
                if (v != null && v.IsSpawned && v.State.NeedsHelp())
                {
                    yield return Rescue(me, v);
                    yield break;
                }

            // A boss up: shoot it in the head (as if with the pistol; a guest it drags comes free when it's hurt).
            if (ActiveBoss() is { } boss)
            {
                yield return HitBoss(me, boss);
                yield break;
            }

            // A robber (or pirate) on his feet: knock him down.
            foreach (StoryNpc n in StoryNpc.All)
                if (n != null && n.MaxHealth > 0 && n.Health > 0 && (n.Role is NpcRole.Robber or NpcRole.Pirate || (n.Role == NpcRole.Guest && n.IsFighting)) && n.Ride == null)
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

            // The gala fund: the beach shift pays as it goes; after a minute of it the test tops the wallet up (galamoney).
            if (beat == "2.5c" && StoryDirector.Instance.GalaGoal > 0 && Time.time - _galaFundSince > 60f && Economy.Money < StoryDirector.Instance.GalaGoal)
            {
                Say($"gala fund: ${Economy.Money} earned in a minute of shift, topping up to ${StoryDirector.Instance.GalaGoal}");
                Exec($"galamoney {StoryDirector.Instance.GalaGoal - Economy.Money}");
                yield return new WaitForSeconds(1f);
            }
            if (beat == "1.8") yield return WadeToFalseAlarm(me);
            if (beat == "1.10") yield return RideToIsland2(me);
            // Sent somewhere (the Kleptopus's pier): go where the marker is.
            if (beat == "2.5b" && !StoryDirector.MissionRunning && StoryDirector.Instance.MarkerLabel == "The pier" &&
                StoryDirector.Instance.MarkerPosition is { } there)
            {
                Vector3 at = there - Vector3.up * 2f;
                at.y = WaterSurface.Exists && Shore.WaterDepthAt(at + Vector3.up * 2f) > 1.3f ? WaterSurface.HeightAt(at) - 1.2f : at.y;
                me.Motor.Teleport(at + Vector3.back * 6f);
                yield return new WaitForSeconds(1f);
            }
        }

        private float _galaFundSince = float.MaxValue;

        /// <summary>2.7: talk to the director for the yacht keys (marked), then sail.</summary>
        private IEnumerator Beat27(PlayerHub me)
        {
            if (StoryDirector.Instance.MarkerLabel == "Director") { yield return TalkToMarked(me); yield break; }
            yield return SailToSkullCove(me);
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

        private static Vector3 Shoreward(Vector3 at) => at.x > 640f ? Vector3.left : at.x > 380f ? Vector3.right : at.z < -120f ? Vector3.back : Vector3.forward; // (island 4: its lagoon is to the east)

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
            if (mine && v.HasLostLeg)
            {
                yield return ToHospitalBed(me); // bitten: the infirmary, not the beach
                yield break;
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

        // ------------------------------------------------------------------ chapters 2 and 3

        private static void Stand(PlayerHub me, Vector3 p) => me.Motor.Teleport(p + Vector3.up * 0.3f);

        private static Transform StoryPoint(string name) => GameObject.Find("Environment/Island3/Story/" + name)?.transform;

        /// <summary>Whoever the story waits to talk to (talkable, the marker over them): in front of them, Interact.</summary>
        private IEnumerator TalkToMarked(PlayerHub me)
        {
            Vector3? marker = StoryDirector.Instance.MarkerPosition;
            if (marker == null) yield break; // not marked yet: wait (anyone talkable elsewhere isn't who the story means)
            StoryNpc best = null;
            foreach (StoryNpc n in StoryNpc.All)
                if (n != null && n.IsTalkable && (n.transform.position - marker.Value).sqrMagnitude < 6f * 6f && (best == null ||
                    (n.transform.position - marker.Value).sqrMagnitude < (best.transform.position - marker.Value).sqrMagnitude))
                    best = n;
            if (best == null) yield break;
            Stand(me, best.transform.position + best.transform.forward * 1.5f);
            me.Look.LookAt(best.HeadPosition);
            yield return new WaitForSeconds(0.4f);
            Say($"talking to {best.Name} at {Vector3.Distance(me.transform.position, best.transform.position):F1} m");
            best.OnInteract(me);
            yield return new WaitForSeconds(1f);
        }

        /// <summary>To the nearest open counter selling <paramref name="item"/>, buy it.</summary>
        private IEnumerator BuyAt(PlayerHub me, string item)
        {
            ShopCounter shop = null;
            foreach (ShopCounter c in FindObjectsByType<ShopCounter>(FindObjectsSortMode.None))
                if (c.IsOpen && c.ProductIndex(item) >= 0 && (shop == null ||
                    (c.transform.position - me.transform.position).sqrMagnitude < (shop.transform.position - me.transform.position).sqrMagnitude))
                    shop = c;
            if (shop == null) yield break;
            Vector3 at = shop.HandOverPoint;
            Vector3 stand = at - shop.transform.forward * 1.4f;
            float ground = Shore.GroundHeightAt(stand + Vector3.up * 3f);
            if (!float.IsNaN(ground)) stand.y = ground;
            Stand(me, stand);
            me.Look.LookAt(at);
            yield return new WaitForSeconds(0.5f);
            Exec($"buy {shop.ProductIndex(item) + 1}");
            yield return new WaitForSeconds(1f);
            Exec($"grab {item}");
            yield return new WaitForSeconds(0.6f);
        }

        /// <summary>Carrying a shark-bite victim: to the infirmary bed, Interact.</summary>
        private IEnumerator ToHospitalBed(PlayerHub me)
        {
            HospitalBed bed = FindFirstObjectByType<HospitalBed>();
            if (bed == null)
            {
                Warn("no hospital bed in the scene");
                yield break;
            }
            Stand(me, bed.transform.position + bed.transform.right * 1.2f);
            me.Look.LookAt(bed.transform.position);
            yield return new WaitForSeconds(0.5f);
            Exec("use");
            yield return new WaitForSeconds(0.8f);
            if (bed.CanInteract(me))
            {
                Warn("couldn't use the hospital bed by looking at it; admitting directly");
                bed.OnInteract(me);
                yield return new WaitForSeconds(0.8f);
            }
        }

        /// <summary>A boss fighting right now (awake, alive), or null.</summary>
        private static Creature ActiveBoss()
        {
            foreach (Creature c in Creature.All)
                if (c != null && c.IsBoss && !c.IsDead && !c.IsDormant) return c;
            return null;
        }

        /// <summary>Host: a few pistol shots into the boss's head (from where a player would stand).</summary>
        private IEnumerator HitBoss(PlayerHub me, Creature boss)
        {
            Transform head = null;
            foreach (CreatureZone z in boss.GetComponentsInChildren<CreatureZone>())
                if (z.Part == "Head") head = z.transform;
            if (head == null) yield break;
            for (int i = 0; i < 4 && !boss.IsDead; i++)
            {
                ((IDamageable)boss).ServerTakeHit(60, DamageKind.Bullet, me, head.position, (head.position - me.Head.position).normalized);
                yield return new WaitForSeconds(0.15f);
            }
            if (me.TryGetComponent(out PlayerCombat combat) && combat.IsDowned) combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
        }

        /// <summary>Onto the pirate boat and south to Skull Cove, steering at the night landing under its mountain.</summary>
        private IEnumerator SailToSkullCove(PlayerHub me)
        {
            // Skull Cove's night landing: the little dock under the mountain, on its north side (the pirates' own dock
            // on the east shore if the night arrival isn't built).
            Transform cove = GameObject.Find("Environment/Island3/NightArrival/Dock")?.transform;
            bool north = cove != null;
            if (cove == null) cove = GameObject.Find("Environment/Island3/Island3Arrival")?.transform;
            if (cove == null) yield break;
            if (me.Motor.Seat == null)
            {
                Vehicle boat = null;
                foreach (Vehicle v in Vehicle.All)
                    if ((v.DisplayName == "Yacht" || v.DisplayName == "Pirate Boat") && !v.IsLocked && v.Driver == null) boat = v;
                if (boat == null) yield break;
                Vector3 boarding = boat.transform.position + boat.transform.right * 2.5f;
                boarding.y = WaterSurface.Exists ? WaterSurface.HeightAt(boarding) - 1.0f : boarding.y;
                me.Motor.Teleport(boarding);
                me.Look.LookAt(boat.transform.position + Vector3.up * 0.8f);
                yield return new WaitForSeconds(0.6f);
                Exec("use");
                yield return new WaitForSeconds(1f);
                if (me.Motor.Seat == null) boat.OnInteract(me);
                yield return new WaitForSeconds(1f);
                yield break;
            }
            Vehicle ride = me.Motor.Seat;
            // The hotel island lies between its beach and Skull Cove: round its west end (x -160 .. 200, z -220 .. -480).
            // Skull Cove's dock is on its east shore (the island is x -75 .. 115, z -1020 .. -780): come in from the east.
            Vector3 p = ride.transform.position;
            Vector3 target = cove.position;
            if (p.z > -300f && p.z < -170f && p.x > -120f) target = new Vector3(p.x - 25f, 0f, -150f); // by the hotel beach (the marina): out to sea first
            else if (p.z > -500f && p.x > -205f) target = new Vector3(-225f, 0f, p.z - 40f);
            else if (p.z > -500f) target = new Vector3(-225f, 0f, -530f);
            else if (!north && p.x < 150f && p.z > -890f) target = new Vector3(165f, 0f, Mathf.Min(p.z, -740f) - 25f);
            else if (!north && p.x < 150f) target = new Vector3(165f, 0f, cove.position.z);
            else if (north) target = cove.position + Vector3.forward * 6f; // straight in from the open sea to the north
            Vector3 to = target - p;
            to.y = 0f;
            float angle = Vector3.SignedAngle(ride.transform.forward, to, Vector3.up);
            DevCommands.Execute(string.Format(CultureInfo.InvariantCulture, "drive 0.7 {0:0.00} {1:0.00}",
                Mathf.Clamp(angle / 35f, -1f, 1f), Mathf.Abs(angle) > 100f ? 0.5f : 1f));
            Tagged("sail", me, 3);
        }

        /// <summary>Skull Cove, beat by beat (what chapter3check does, as a player would).</summary>
        private IEnumerator Chapter3(PlayerHub me, string beat)
        {
            if (beat == "3.10") { yield return Beat310(me); yield break; }
            if (me.Motor.Seat != null) Exec("getoff");
            PlayerCombat combat = me.GetComponent<PlayerCombat>();
            if (combat != null && combat.IsDowned) combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
            switch (beat)
            {
                case "3.1":
                    // Up the lookout tower (the sneak starts once you've looked down at the village), then straight to the
                    // THIEF's door.
                    Transform landing = GameObject.Find("Environment/Island3/NightArrival/Lookout")?.transform
                                        ?? GameObject.Find("Environment/Island3/Island3Arrival")?.transform;
                    if (!StoryDirector.MissionRunning)
                    {
                        if (landing != null && (me.transform.position - landing.position).sqrMagnitude > 3f * 3f) Stand(me, landing.position);
                        yield return new WaitForSeconds(1f);
                        yield break;
                    }
                    if (StoryPoint("Thief") is { } thief) Stand(me, thief.position);
                    yield return new WaitForSeconds(2f);
                    break;
                case "3.2":
                    yield return TalkToMarked(me);
                    break;
                case "3.3":
                    foreach (string gun in new[] { "Pistol", "Shotgun" })
                    {
                        Item have = null;
                        foreach (Item i in Item.All)
                            if (i.Holder == me && i.DisplayName == gun) have = i;
                        if (have == null)
                        {
                            Exec($"spawn {gun}");
                            yield return new WaitForSeconds(0.8f);
                            Exec($"grab {gun}");
                            yield return new WaitForSeconds(0.8f);
                        }
                        else Exec($"slot {SlotOf(me, have) + 1}");
                        yield return new WaitForSeconds(0.3f);
                        me.Hands.HeldItem?.GetComponent<Weapon>()?.ServerFitByName("suppressor");
                        yield return new WaitForSeconds(0.3f);
                    }
                    break;
                case "3.4":
                    if (StoryDirector.CageRingCentre() is { } ring && (me.transform.position - ring).sqrMagnitude > 4f * 4f) Stand(me, ring + Vector3.back * 1.5f);
                    foreach (StoryNpc n in StoryNpc.All)
                        if (n != null && n.Name == "Iron Jaw Jack" && n.IsFighting && !n.IsDefeated)
                            for (int k = 0; k < 6; k++)
                            {
                                ((IDamageable)n).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, n.HeadPosition, me.Head.forward);
                                yield return new WaitForSeconds(0.2f);
                            }
                    break;
                case "3.5":
                    if (StoryDirector.MissionRunning && StoryPoint("RiverBankA") is { } bank) Stand(me, bank.position);
                    else if (StoryPoint("JungleStart") is { } jungle) Stand(me, jungle.position);
                    yield return new WaitForSeconds(1.5f);
                    break;
                case "3.6":
                    if (StoryPoint("RiverBankA") is { } a && (me.transform.position - a.position).sqrMagnitude > 3f * 3f) Stand(me, a.position);
                    yield return new WaitForSeconds(0.5f);
                    Exec("swing");
                    yield return new WaitForSeconds(3f);
                    break;
                case "3.7":
                    if (ActiveBoss() is { } snapper) { yield return HitBoss(me, snapper); break; }
                    if (StoryPoint("SnapperPond") is { } pond && StoryPoint("UpRiver") is { } up)
                        Stand(me, pond.position + (up.position - pond.position).normalized * 12f);
                    yield return new WaitForSeconds(1f);
                    break;
                case "3.7b":
                    // Into the sewer behind the back waterfall, up the ladder into the castle, then the throne room's fight
                    // (fists put the king down: he's needed alive).
                    if (StoryDirector.MissionRunning)
                    {
                        foreach (StoryNpc n in StoryNpc.All)
                            if (n != null && n.Name == "The Pirate King" && !n.IsDefeated)
                            {
                                Exec("goto King");
                                yield return new WaitForSeconds(0.3f);
                                Exec("whack 2");
                                yield return new WaitForSeconds(0.6f);
                                break;
                            }
                        break;
                    }
                    if (StoryDirector.Instance.MarkerLabel == "The sewer" && StoryPoint("SewerEntrance") is { } sewer)
                    {
                        Stand(me, sewer.position);
                        yield return new WaitForSeconds(1f);
                        break;
                    }
                    if (StoryDirector.Instance.MarkerLabel == "The palace" && StoryPoint("PalaceThrone") is { } throne)
                    {
                        // The sewer's ladder: E at its foot comes out in the castle yard; then in through the keep's door.
                        World.SewerLadder ladder = null;
                        foreach (World.SewerLadder l in FindObjectsByType<World.SewerLadder>(FindObjectsSortMode.None))
                            if (l.name == "SewerLadder") ladder = l;
                        if (ladder != null)
                        {
                            Stand(me, ladder.transform.position + ladder.transform.forward * 1.2f); // (it faces down the tunnel)
                            me.Look.LookAt(ladder.transform.position + Vector3.up * 1.3f);
                            yield return new WaitForSeconds(0.5f);
                            ladder.OnInteract(me);
                            yield return new WaitForSeconds(1f);
                        }
                        Stand(me, throne.position + throne.forward * 6f);
                        yield return new WaitForSeconds(1f);
                    }
                    break;
                case "3.8":
                    Transform mouth = StoryPoint("CaveMouth"), ledge = StoryPoint("CaveLedge");
                    if (mouth != null) Stand(me, mouth.position);
                    yield return new WaitForSeconds(1.5f);
                    if (ledge != null) Stand(me, ledge.position);
                    yield return new WaitForSeconds(2f);
                    break;
                case "3.9":
                    yield return CarrySack(me);
                    break;
            }
        }

        /// <summary>3.9: a money sack from the vault onto the pirate boat's deck (picked up, stood on the deck, put down).</summary>
        private IEnumerator CarrySack(PlayerHub me)
        {
            Vehicle boat = null;
            foreach (Vehicle v in Vehicle.All) if (v.DisplayName == "Pirate Boat") boat = v;
            if (boat == null) yield break;
            Item held = me.Hands.HeldItem;
            if (held == null || held.DisplayName != "Money Sack")
            {
                Item sack = null;
                foreach (Item i in Item.All)
                    if (i.DisplayName == "Money Sack" && !i.IsHeld) sack = i;
                if (sack == null) { yield return new WaitForSeconds(0.5f); yield break; }
                yield return PickUp(me, sack);
                yield break;
            }
            // On the deck behind the mast, facing the bow: put it down in front of you.
            Stand(me, boat.transform.TransformPoint(new Vector3(-0.3f, 1.0f, -0.6f)));
            me.Look.LookAt(boat.transform.TransformPoint(new Vector3(-0.3f, 0.9f, 1.4f)));
            yield return new WaitForSeconds(0.6f);
            Exec("drop");
            yield return new WaitForSeconds(1.2f);
            Tagged("sack", me, 2);
        }

        /// <summary>3.10: drive the pirate boat home round the hotel island's west end, then the tag on its deck.</summary>
        private IEnumerator Beat310(PlayerHub me)
        {
            StoryDirector story = StoryDirector.Instance;
            Vehicle boat = null;
            foreach (Vehicle v in Vehicle.All) if (v.DisplayName == "Pirate Boat") boat = v;
            if (boat == null) yield break;
            if (story.MarkerLabel == "Hotel beach" && story.MarkerPosition is { } stop)
            {
                if (me.Motor.Seat == null)
                {
                    if (boat.IsLocked) { yield return new WaitForSeconds(0.5f); yield break; }
                    Vector3 boarding = boat.transform.position + boat.transform.right * 2.5f;
                    boarding.y = WaterSurface.Exists ? WaterSurface.HeightAt(boarding) - 1.0f : boarding.y;
                    me.Motor.Teleport(boarding);
                    me.Look.LookAt(boat.transform.position + Vector3.up * 0.8f);
                    yield return new WaitForSeconds(0.6f);
                    Exec("use");
                    yield return new WaitForSeconds(1f);
                    if (me.Motor.Seat == null) boat.OnInteract(me);
                    yield return new WaitForSeconds(1f);
                    yield break;
                }
                // Skull Cove lies south of the hotel island (x -160 .. 200, z -220 .. -480), the beach on its north side.
                Vector3 p = me.Motor.Seat.transform.position, target = stop;
                if (p.z < -235f && p.x > -205f) target = new Vector3(-228f, 0f, p.z < -490f ? -470f : p.z + 25f);
                else if (p.z < -235f) target = new Vector3(-228f, 0f, -150f);
                Vector3 to = target - p;
                to.y = 0f;
                float angle = Vector3.SignedAngle(me.Motor.Seat.transform.forward, to, Vector3.up);
                DevCommands.Execute(string.Format(CultureInfo.InvariantCulture, "drive 0.7 {0:0.00} {1:0.00}",
                    Mathf.Clamp(angle / 35f, -1f, 1f), Mathf.Abs(angle) > 100f || to.magnitude < 50f ? 0.5f : 1f));
                Tagged("sailhome", me, 3);
                yield break;
            }
            if (me.Motor.Seat != null) { Exec("getoff"); yield return new WaitForSeconds(0.8f); yield break; }
            if (story.MarkerLabel == "Marisol" && story.MarkerPosition is { } marisol)
            {
                Stand(me, marisol - Vector3.up * 2f + Vector3.right * 2f);
                yield return new WaitForSeconds(1f);
                yield break;
            }
            if (story.MarkerLabel == "On the deck")
            {
                foreach (Item i in Item.All)
                    if (i.DisplayName == "Lost & Found Tag" && !i.IsHeld)
                    {
                        Stand(me, i.transform.position + Vector3.back * 0.6f);
                        yield return new WaitForSeconds(0.5f);
                        me.Hands.TryPickUp(i);
                        yield return new WaitForSeconds(1f);
                        Tagged("tag", me, 1);
                        break;
                    }
            }
            yield return new WaitForSeconds(0.5f);
        }

        private static Transform Island4Point(string name) => GameObject.Find("Environment/Island4/Story/" + name)?.transform;

        /// <summary>Finders Keepers, beat by beat (what chapter4check does, as a player would).</summary>
        private IEnumerator Chapter4(PlayerHub me, string beat)
        {
            StoryDirector story = StoryDirector.Instance;
            PlayerCombat combat = me.GetComponent<PlayerCombat>();
            if (combat != null && combat.IsDowned) combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
            // The final boss: kept on its feet (a player would dodge the claw and the flying loot; the test just stands there).
            if (combat != null && beat == "4.7" && StoryDirector.MissionRunning) combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
            if (beat == "4.1" && story.MarkerLabel == "Finders Keepers") { yield return SailToIsland4(me); yield break; }
            if (me.Motor.Seat != null) { Exec("getoff"); yield return new WaitForSeconds(0.8f); yield break; }
            // Someone in trouble off the lagoon: out to them first.
            foreach (VictimBrain v in VictimBrain.All)
                if (v != null && v.IsSpawned && v.State.NeedsHelp() && v.transform.position.x > 380f)
                {
                    yield return Rescue(me, v);
                    yield break;
                }
            // Anyone fighting us: knock them down.
            foreach (StoryNpc n in StoryNpc.All)
                if (n != null && n.IsFighting && !n.IsDefeated && n.MaxHealth > 0)
                {
                    Stand(me, n.transform.position + n.transform.forward * 1.3f); // (names like "FK Security Rex" share their first word)
                    me.Look.LookAt(n.HeadPosition);
                    yield return new WaitForSeconds(0.3f);
                    Exec("whack 2");
                    yield return new WaitForSeconds(0.5f);
                    Tagged("barfight", me);
                    yield break;
                }
            switch (beat)
            {
                case "4.1":
                    if (story.MarkerLabel == "Toll") { yield return TalkToMarked(me); Tagged("toll", me, 1); }
                    else if (story.MarkerLabel == "The beach" && story.MarkerPosition is { } beach) Stand(me, beach - Vector3.up * 2f + Vector3.right * 4f);
                    yield return new WaitForSeconds(0.6f);
                    break;
                case "4.2":
                    if (!StoryDirector.MissionRunning && Island4Point("RaceBeach") is { } race) Stand(me, race.position);
                    yield return new WaitForSeconds(0.8f);
                    break;
                case "4.3":
                    if (!StoryDirector.MissionRunning)
                    {
                        if (Island4Point("WarehouseDoor") is { } door) Stand(me, door.position);
                        yield return new WaitForSeconds(1f);
                        break;
                    }
                    // A tag no guard is near or looking at (a careful player waits for the guard to walk on).
                    foreach (Item i in Item.All)
                    {
                        if (i.DisplayName != "Lost & Found Tag" || i.IsHeld || i.transform.position.x < 380f) continue;
                        bool watched = false;
                        foreach (StoryNpc n in StoryNpc.All)
                        {
                            if (n == null || !n.IsGuarding) continue;
                            Vector3 to = i.transform.position - n.transform.position;
                            to.y = 0f;
                            if (to.magnitude < 5f || to.magnitude < 18f && Vector3.Dot(n.transform.forward, to.normalized) > 0.3f) watched = true;
                        }
                        if (watched) continue;
                        yield return PickUp(me, i);
                        Tagged("evidence", me, 2);
                        // Back out of the door between tags.
                        if (Island4Point("WarehouseDoor") is { } outside) Stand(me, outside.position);
                        break;
                    }
                    yield return new WaitForSeconds(0.4f);
                    break;
                case "4.4":
                    if (Island4Point("SmoothieBar") is { } bar && (me.transform.position - bar.position).sqrMagnitude > 4f * 4f) Stand(me, bar.position);
                    yield return new WaitForSeconds(0.8f);
                    break;
                case "4.5":
                    yield return TalkToMarked(me);
                    Tagged("thief", me, 1);
                    break;
                case "4.6":
                    yield return TalkToMarked(me);
                    Tagged("sandy", me, 1);
                    break;
                case "4.7":
                    if (ActiveBoss() is { } grabby) { yield return HitBoss(me, grabby); Tagged("grabby", me, 2); break; }
                    if (!StoryDirector.MissionRunning && Island4Point("SandyJetty") is { } sj) Stand(me, sj.position);
                    yield return new WaitForSeconds(0.8f);
                    break;
                case "4.8":
                    yield return new WaitForSeconds(0.5f);
                    break;
                case "4.9":
                    // Along to wherever the ending is (the police dock, then the beach with everyone on it).
                    if (StoryDirector.Instance.MarkerPosition is { } m && (me.transform.position - m).sqrMagnitude > 10f * 10f) Stand(me, m - Vector3.up * 2f + Vector3.back * 4f);
                    Tagged("ending", me, 3);
                    yield return new WaitForSeconds(1f);
                    break;
            }
        }

        /// <summary>4.1: onto the pirate boat at the hotel beach and east along the orange buoys to the island 4 jetty.</summary>
        private IEnumerator SailToIsland4(PlayerHub me)
        {
            Transform berth = Island4Point("BoatBerth");
            if (berth == null) yield break;
            if (me.Motor.Seat == null)
            {
                Vehicle boat = null;
                foreach (Vehicle v in Vehicle.All) if (v.DisplayName == "Pirate Boat" && !v.IsLocked && v.Driver == null) boat = v;
                if (boat == null) { yield return new WaitForSeconds(0.5f); yield break; }
                Vector3 boarding = boat.transform.position + boat.transform.right * 2.5f;
                boarding.y = WaterSurface.Exists ? WaterSurface.HeightAt(boarding) - 1.0f : boarding.y;
                me.Motor.Teleport(boarding);
                me.Look.LookAt(boat.transform.position + Vector3.up * 0.8f);
                yield return new WaitForSeconds(0.6f);
                Exec("use");
                yield return new WaitForSeconds(1f);
                if (me.Motor.Seat == null) boat.OnInteract(me);
                yield return new WaitForSeconds(1f);
                yield break;
            }
            // The next buoy east of us (the route runs west to east), then the jetty's head.
            Vector3 p = me.Motor.Seat.transform.position, target = berth.position;
            for (int i = 0; i < 6; i++)
                if (Island4Point($"Route_{i}") is { } r && r.position.x > p.x + 12f) { target = r.position; break; }
            Vector3 to = target - p;
            to.y = 0f;
            float angle = Vector3.SignedAngle(me.Motor.Seat.transform.forward, to, Vector3.up);
            DevCommands.Execute(string.Format(CultureInfo.InvariantCulture, "drive 0.7 {0:0.00} {1:0.00}",
                Mathf.Clamp(angle / 35f, -1f, 1f), Mathf.Abs(angle) > 100f || to.magnitude < 40f ? 0.5f : 1f));
            Tagged("sail4", me, 3);
        }

        private static int SlotOf(PlayerHub me, Item item)
        {
            for (int s = 0; s < PlayerHands.SlotCount; s++)
                if (me.Hands.SlotItem(s) == item) return s;
            return 0;
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
