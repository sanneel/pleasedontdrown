using System;
using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
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
        /// until it finishes. The body is stepped here (not as its own coroutine), so a failure stops it mid-line.
        /// </summary>
        private IEnumerator RunMission(string title, Vector3 start, Func<IEnumerator> body)
        {
            MissionRunning = true;
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
                SetObjective("Mission failed. Trying again...");
                NoMarker();
                yield return new WaitForSeconds(FailedPause);
                CleanupActorsFrom(actorsBefore);
                TeleportObservers(start);
                yield return new WaitForSeconds(0.4f);
                attempt++;
                TitleObservers(title, $"Attempt {attempt}");
            }
            MissionRunning = false;
            _missionFailReason = null;
            Debug.Log($"[Mission] {title}: done");
        }

        /// <summary>Despawn what was spawned since <paramref name="index"/> (a failed mission's pirates, creatures, items).</summary>
        private void CleanupActorsFrom(int index)
        {
            for (int i = _spawnedActors.Count - 1; i >= index && i >= 0; i--)
            {
                GameObject go = _spawnedActors[i];
                if (go != null && go.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) Despawn(go);
                _spawnedActors.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ test missions (console)

        private void RegisterMissionCommands()
        {
            DevCommands.Register("brawltest", "[pirates]", "Mission test: fight pirates here; everyone down restarts it.", args =>
                CheatServer("brawltest", args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : 3), cheat: true, owner: this);
            DevCommands.Register("fail", "[reason]", "Fail the running mission (it restarts).", args =>
                CheatServer("fail:" + (args.Length > 0 ? string.Join(" ", args) : "Test"), 0), cheat: true, owner: this);
        }

        private void UnregisterMissionCommands()
        {
            DevCommands.Unregister("brawltest", this);
            DevCommands.Unregister("fail", this);
        }

        /// <summary>Host: the mission test commands; true if <paramref name="what"/> was one.</summary>
        private bool ServerMissionCheat(string what, int value, PlayerHub by)
        {
            if (what.StartsWith("fail:"))
            {
                ServerFailMission(what.Substring(5));
                return true;
            }
            if (what != "brawltest" || by == null) return false;
            if (MissionRunning) return true;
            Vector3 start = by.transform.position;
            int count = Mathf.Clamp(value, 1, 8);
            StartCoroutine(RunMission("Pirate brawl", start, () => BrawlTest(start, count)));
            return true;
        }

        private IEnumerator BrawlTest(Vector3 start, int count)
        {
            TitleObservers("Pirate brawl", "Take them all down");
            var pirates = new List<StoryNpc>();
            string[] names = { "One-Eye", "Barnacle", "Saltbeard", "Rusty", "Gulliver", "Mack", "Jonesy", "Cutter" };
            for (int i = 0; i < count; i++)
            {
                float angle = i * 360f / count;
                Vector3 at = start + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 9f;
                StoryNpc pirate = SpawnNpc(names[i % names.Length], NpcRole.Pirate, PirateLook(i), at, angle + 180f, 100);
                pirate.ServerAggressive(true, start);
                pirates.Add(pirate);
            }
            while (true)
            {
                int down = 0;
                foreach (StoryNpc p in pirates)
                    if (p == null || p.IsDefeated) down++;
                SetObjective("Take the pirates down", down, pirates.Count);
                if (down >= pirates.Count) break;
                yield return new WaitForSeconds(0.25f);
            }
            TitleObservers("MISSION COMPLETE", "Pirate brawl");
            yield return new WaitForSeconds(2f);
            foreach (StoryNpc p in pirates) Remove(p);
        }
    }
}
