using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Missions (host): a part of the story that can be failed and tried again: a boss fight, a stealth approach, a
    /// brawl. While one runs, everyone going down (or <see cref="ServerFailMission"/>: a parrot saw you) fails it:
    /// "MISSION FAILED" with the reason, whatever the mission spawned is cleared, everyone is back on their feet at
    /// its start and it runs again from the top. Nothing is saved mid-mission, so a reload starts it over too.
    /// </summary>
    public partial class StoryDirector
    {
        private const float FailedPause = 3f;

        /// <summary>Host: a mission is running (everyone down fails it instead of everyone just getting up).</summary>
        public static bool MissionRunning { get; private set; }

        private string _missionFailReason;
        private readonly SyncVar<string> _missionTitle = new SyncVar<string>();
        private readonly SyncVar<string> _missionObjective = new SyncVar<string>();
        private readonly SyncVar<int> _missionProgress = new SyncVar<int>();
        private readonly SyncVar<int> _missionGoal = new SyncVar<int>();

        /// <summary>Host: no mission any more (finished, or the story jumped/stopped): the story's line shows again.</summary>
        private void EndMission()
        {
            MissionRunning = false;
            if (!IsServerInitialized) return;
            _missionTitle.Value = string.Empty;
            HideBossBar();
            _missionObjective.Value = string.Empty;
            _missionProgress.Value = _missionGoal.Value = 0;
        }

        /// <summary>Host: the running mission's objective (shown instead of the story's while it runs).</summary>
        private void MissionObjective(string text, int progress = 0, int goal = 0)
        {
            if (_missionObjective.Value != text) _missionObjective.Value = text;
            if (_missionProgress.Value != progress) _missionProgress.Value = progress;
            if (_missionGoal.Value != goal) _missionGoal.Value = goal;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetMissionStatics() => MissionRunning = false;

        /// <summary>Host: fail the running mission (it restarts). Ignored if none is running.</summary>
        [Server]
        public void ServerFailMission(string reason)
        {
            if (MissionRunning && _missionFailReason == null) _missionFailReason = string.IsNullOrEmpty(reason) ? "Mission failed" : reason;
        }

        /// <summary>
        /// Host: run <paramref name="body"/> as a mission starting at <paramref name="start"/>; failing it restarts it
        /// until it finishes (<paramref name="onRetry"/>: put back what lives there before each new try). The body is
        /// stepped here (not as its own coroutine), so a failure stops it mid-line.
        /// </summary>
        private IEnumerator RunMission(string title, Vector3 start, Func<IEnumerator> body, Action onRetry = null)
        {
            MissionRunning = true;
            _missionTitle.Value = title;
            int attempt = 1;
            while (true)
            {
                int actorsBefore = _spawnedActors.Count;
                _missionFailReason = null;
                foreach (PlayerHub p in PlayerHub.All)
                    if (p != null && p.TryGetComponent(out PlayerCombat c)) c.ServerStandUp(PlayerCombat.MaxHealth, 1f);
                Debug.Log($"[Mission] {title}: attempt {attempt}");

                // Step the body (and anything it yields as a nested routine) until it ends or the mission fails.
                var stack = new Stack<IEnumerator>();
                stack.Push(body());
                while (stack.Count > 0)
                {
                    if (_missionFailReason == null && PlayerCombat.AllDown) _missionFailReason = "Everyone went down";
                    if (_missionFailReason != null) break;
                    IEnumerator top = stack.Peek();
                    if (!top.MoveNext())
                    {
                        stack.Pop();
                        continue;
                    }
                    if (top.Current is IEnumerator nested)
                    {
                        stack.Push(nested);
                        continue;
                    }
                    yield return top.Current;
                }
                if (_missionFailReason == null) break; // done

                Debug.Log($"[Mission] {title} failed: {_missionFailReason}");
                TitleObservers("MISSION FAILED", _missionFailReason);
                MissionObjective("Mission failed. Trying again...");
                NoMarker();
                yield return new WaitForSeconds(FailedPause);
                CleanupActorsFrom(actorsBefore);
                onRetry?.Invoke(); // the island's own people and animals back where they were, calm again
                TeleportObservers(start);
                yield return new WaitForSeconds(0.4f);
                attempt++;
                TitleObservers(title, $"Attempt {attempt}");
            }
            EndMission();
            _missionFailReason = null;
            Debug.Log($"[Mission] {title}: done");
        }

        /// <summary>Despawn what was spawned since <paramref name="index"/> (a failed mission's pirates, creatures, items).</summary>
        private void CleanupActorsFrom(int index)
        {
            for (int i = _spawnedActors.Count - 1; i >= index && i >= 0; i--)
            {
                GameObject go = _spawnedActors[i];
                if (go != null && go.TryGetComponent(out StoryNpc npc)) npc.ServerStopFighting(); // its tally, for tuning
                if (go != null && go.TryGetComponent(out Creatures.Creature creature)) creature.ServerReleaseAll(); // people and pistols it holds
                if (go != null && go.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) Despawn(go);
                _spawnedActors.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ test missions (console)

        private void RegisterMissionCommands()
        {
            DevCommands.Register("brawltest", "[count] [fists|cutlass|pistol|musket|mix|drunk|brawler]",
                "Mission test: fighters around you; everyone down restarts it.", args =>
                    CheatServer("brawltest:" + (args.Length > 1 ? args[1].ToLowerInvariant() : "fists"),
                        args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : 3), cheat: true, owner: this);
            DevCommands.Register("fail", "[reason]", "Fail the running mission (it restarts).", args =>
                CheatServer("fail:" + (args.Length > 0 ? string.Join(" ", args) : "Test"), 0), cheat: true, owner: this);
            DevCommands.Register("fightstats", "", "Print every fighter's swings, hits, misses, shots and blocks so far (host log).", _ =>
                CheatServer("fightstats", 0), cheat: true, owner: this);
        }

        private void UnregisterMissionCommands()
        {
            DevCommands.Unregister("brawltest", this);
            DevCommands.Unregister("fail", this);
            DevCommands.Unregister("fightstats", this);
        }

        /// <summary>Host: the mission test commands; true if <paramref name="what"/> was one.</summary>
        private bool ServerMissionCheat(string what, int value, PlayerHub by)
        {
            if (what.StartsWith("fail:"))
            {
                ServerFailMission(what.Substring(5));
                return true;
            }
            if (what == "fightstats")
            {
                foreach (StoryNpc npc in StoryNpc.All)
                    if (npc != null && npc.IsFighting) npc.ServerLogTally("so far");
                return true;
            }
            if (!what.StartsWith("brawltest:") || by == null) return false;
            if (MissionRunning) return true;
            Vector3 start = by.transform.position;
            int count = Mathf.Clamp(value, 1, 8);
            string kind = what.Substring(10);
            StartCoroutine(RunMission("Brawl", start, () => BrawlTest(start, count, kind)));
            return true;
        }

        /// <summary>Who the test brawl's fighter number <paramref name="i"/> is.</summary>
        private static FighterProfile TestFighter(string kind, int i) => kind switch
        {
            "cutlass" => FighterProfile.Pirate(NpcWeaponKind.Cutlass),
            "pistol" => FighterProfile.Pirate(NpcWeaponKind.Pistol),
            "musket" => FighterProfile.Pirate(NpcWeaponKind.Musket),
            "drunk" => FighterProfile.Drunk(),
            "brawler" => FighterProfile.Brawler(),
            // A raiding party: two with blades or fists up close, the rest shooting.
            "mix" => FighterProfile.Pirate(i % 4 == 0 ? NpcWeaponKind.Cutlass : i % 4 == 1 ? NpcWeaponKind.Fists : i % 4 == 2 ? NpcWeaponKind.Pistol : NpcWeaponKind.Musket),
            _ => FighterProfile.Pirate(NpcWeaponKind.Fists)
        };

        private IEnumerator BrawlTest(Vector3 start, int count, string kind)
        {
            TitleObservers("Brawl", "Take them all down");
            var pirates = new List<StoryNpc>();
            string[] names = { "One-Eye", "Barnacle", "Saltbeard", "Rusty", "Gulliver", "Mack", "Jonesy", "Cutter" };
            for (int i = 0; i < count; i++)
            {
                FighterProfile profile = TestFighter(kind, i);
                float angle = i * 360f / count;
                float from = profile.Weapon.Ranged ? 16f : 9f;
                Vector3 at = start + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * from;
                string name = $"{names[i % names.Length]} ({profile.Weapon.Kind})";
                int health = profile.Style == FightStyle.Brawler ? 160 : profile.Style == FightStyle.Drunk ? 60 : 100;
                StoryNpc pirate = SpawnNpc(name, NpcRole.Pirate, PirateLook(i), at, angle + 180f, health);
                pirate.ServerFight(profile, start);
                pirates.Add(pirate);
            }
            while (true)
            {
                int down = 0;
                foreach (StoryNpc p in pirates)
                    if (p == null || p.IsDefeated) down++;
                MissionObjective("Take the pirates down", down, pirates.Count);
                if (down >= pirates.Count) break;
                yield return new WaitForSeconds(0.25f);
            }
            TitleObservers("MISSION COMPLETE", "Brawl");
            yield return new WaitForSeconds(2f);
            foreach (StoryNpc p in pirates)
            {
                if (p != null) p.ServerStopFighting(); // prints its tally
                Remove(p);
            }
        }
    }
}
