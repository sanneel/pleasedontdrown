using System.Collections;
using System.Reflection;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>Opt-in offline host check of the actual escort retries and stall fallback.</summary>
    public class HutWalkReview : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (DevLaunchArgs.Has("-pdd-hut-review")) new GameObject("Hut walk review").AddComponent<HutWalkReview>();
        }

        private IEnumerator Start()
        {
            float ready = Time.realtimeSinceStartup + 60;
            while ((PlayerHub.Local == null || StoryDirector.Instance == null || LoveHut.Instance == null) && Time.realtimeSinceStartup < ready)
                yield return null;
            if (PlayerHub.Local == null || StoryDirector.Instance == null || LoveHut.Instance == null)
            { Debug.LogError("[HutReview] FAIL: session did not start"); Application.Quit(1); yield break; }
            yield return new WaitForSeconds(1);
            var story = StoryDirector.Instance; var hut = LoveHut.Instance; var hero = PlayerHub.Local;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            for (int scenario = 0; scenario < 4; scenario++)
            {
                Vector3 door = hut.Outside.position;
                Vector3 origin = door + hut.Outside.forward * (scenario == 2 ? 35 : 8);
                if (scenario == 3)
                {
                    float closest = float.MaxValue;
                    for (int x = -45; x <= 45; x += 3)
                        for (int z = -15; z <= 3; z++)
                        {
                            Vector3 p = new(x, 0, z);
                            float depth = Shore.WaterDepthAt(p);
                            if (depth < .3f || depth > .7f || (p - door).sqrMagnitude >= closest) continue;
                            float sand = Shore.GroundHeightAt(p + Vector3.up * 3);
                            if (float.IsNaN(sand)) continue;
                            closest = (p - door).sqrMagnitude; origin = new Vector3(x, sand, z);
                        }
                    if (closest == float.MaxValue) { Debug.LogError("[HutReview] FAIL: no knee-deep test location"); Application.Quit(1); yield break; }
                    Debug.Log($"[HutReview] shallow origin {origin}, water depth {Shore.WaterDepthAt(origin):F2}m");
                }
                var npc = (StoryNpc)typeof(StoryDirector).GetMethod("SpawnNpc", flags).Invoke(story,
                    new object[] { "Escort review", NpcRole.Guest, AvatarLook.RandomTourist(37, 1), origin, 0f, 0 });
                npc.ServerTeleport(origin, 0, true);
                npc.ServerWalkWith(hero.GetComponent<Rigidbody>());
                hut.ServerLead(hero, npc);
                // Disable movement to reproduce an actor blocked indefinitely, then separately a route ending early.
                npc.enabled = scenario == 3;
                var walk = (IEnumerator)typeof(StoryDirector).GetMethod("WalkToHut", flags).Invoke(story, new object[] { npc, door, hut.Outside.eulerAngles.y });
                float start = Time.time;
                float nextHeadway = start;
                while (walk.MoveNext())
                {
                    if (scenario == 1) npc.ServerStop();
                    if (scenario == 2 && Time.time >= nextHeadway)
                    {
                        nextHeadway = Time.time + 2;
                        npc.ServerTeleport(npc.transform.position + (door - npc.transform.position).normalized * .35f, 0, true);
                        npc.ServerMoveTo(door, 1.7f, water: true); // teleport intentionally stops a route; keep this deadline case active
                    }
                    yield return walk.Current;
                    if (Time.time - start > 22) { Debug.LogError("[HutReview] FAIL: escort exceeded deadline"); Application.Quit(1); yield break; }
                }
                yield return new WaitForFixedUpdate();
                float elapsed = Time.time - start;
                float distance = Vector3.Distance(npc.transform.position, door);
                float follow = Vector3.Distance(hero.transform.position, npc.transform.position);
                npc.ServerWalkWith(null); hut.ServerLead(hero, null);
                Debug.Log($"[HutReview] case {scenario}: {elapsed:F2}s, actor-door {distance:F2}m, player-actor {follow:F2}m");
                float allowedDistance = scenario == 3 ? 1.7f : .2f;
                float deadline = scenario == 0 ? 5.5f : scenario == 1 ? 2f : 21f;
                if (distance > allowedDistance || follow > 4 || elapsed > deadline || (scenario == 2 && elapsed < 19.5f))
                { Debug.LogError("[HutReview] FAIL: fallback or player follow"); Application.Quit(1); yield break; }
                FishNet.InstanceFinder.ServerManager.Despawn(npc.NetworkObject);
                yield return new WaitForSeconds(.3f);
            }
            Debug.Log("[HutReview] PASS: stall fallback, two retries, 20-second deadline, shallow-water departure and following lifeguard.");
            Application.Quit(0);
        }
    }
}
