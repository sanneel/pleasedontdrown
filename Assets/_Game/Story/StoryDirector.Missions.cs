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
    /// Missions (host): a job that can be failed and tried again (for Sandy's jobs, Docs/20 phase 7). While one runs,
    /// everyone going down (or <see cref="ServerFailMission"/>) fails it:
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
                if (go != null && go.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) Despawn(go);
                _spawnedActors.RemoveAt(i);
            }
        }


        // ------------------------------------------------------------------ console

        private void RegisterMissionCommands() =>
            DevCommands.Register("fail", "[reason]", "Fail the running mission (it restarts).", args =>
                CheatServer("fail:" + (args.Length > 0 ? string.Join(" ", args) : "Test"), 0), cheat: true, owner: this);

        private void UnregisterMissionCommands() => DevCommands.Unregister("fail", this);

        /// <summary>Host: the mission test commands; true if <paramref name="what"/> was one.</summary>
        private bool ServerMissionCheat(string what, int value, PlayerHub by)
        {
            if (!what.StartsWith("fail:")) return false;
            ServerFailMission(what.Substring(5));
            return true;
        }
    }
}
