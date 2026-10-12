using System;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Creatures;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Skull Cove lives before you get there (host). Its lookout parrots and the guards on the lane, the jungle's guards
    /// and monkeys, the river's alligators, Old Snapper asleep in its pond and Iron Jaw Jack in the tavern are put on the
    /// island while the team is still out at sea (nobody sees them appear) and they stay: each mission is about the ones
    /// already there, and nobody vanishes once it's done. A failed mission puts its group back as it was (the team is
    /// back at the mission's start then, away from them). Groups:
    ///  I3Lookouts (3.1: the night watch, sleepers and lantern guards) · I3Tavern (3.4) · I3Jungle (3.5) · I3River (3.6) · I3Snapper (3.7, gone once beaten).
    /// </summary>
    public partial class StoryDirector
    {
        private sealed class Resident
        {
            public string Group;
            public Func<GameObject> Make;
            public GameObject Body;
        }

        private readonly List<Resident> _residents = new();

        /// <summary>Host: someone who lives on the island (made now, and again by <see cref="ResetResidents"/>).</summary>
        private void AddResident(string group, Func<GameObject> make)
        {
            var r = new Resident { Group = group, Make = make };
            r.Body = MakeResident(r);
            _residents.Add(r);
        }

        private GameObject MakeResident(Resident r)
        {
            GameObject body = r.Make();
            if (body != null) _spawnedActors.Remove(body); // not a mission's: a mission's cleanup doesn't take it
            return body;
        }

        /// <summary>Host: the living bodies of a group, of type <typeparamref name="T"/>.</summary>
        private List<T> ResidentsOf<T>(string group) where T : Component
        {
            var list = new List<T>();
            foreach (Resident r in _residents)
                if (r.Group == group && r.Body != null && r.Body.TryGetComponent(out T t)) list.Add(t);
            return list;
        }

        /// <summary>Host: a group back as it was at the start (calm, healed, at home): despawned and made again.</summary>
        private void ResetResidents(string group)
        {
            foreach (Resident r in _residents)
            {
                if (r.Group != group) continue;
                RemoveBody(r);
                r.Body = MakeResident(r);
            }
        }

        /// <summary>Host: a group is gone for good (Old Snapper beaten): it isn't made again. What's left of it is an
        /// ordinary story actor now (cleared with the rest when the story moves on).</summary>
        private void ForgetResidents(string group)
        {
            foreach (Resident r in _residents)
                if (r.Group == group && r.Body != null) _spawnedActors.Add(r.Body);
            _residents.RemoveAll(r => r.Group == group);
        }

        private void RemoveBody(Resident r)
        {
            if (r.Body == null) return;
            if (r.Body.TryGetComponent(out StoryNpc npc)) npc.ServerStopFighting();
            if (r.Body.TryGetComponent(out Creature creature)) creature.ServerReleaseAll();
            if (r.Body.TryGetComponent(out NetworkObject nob) && nob.IsSpawned) Despawn(r.Body);
            r.Body = null;
        }

        /// <summary>Host: everyone who lives on the islands goes (the story restarts or jumps).</summary>
        private void CleanupResidents()
        {
            foreach (Resident r in _residents) RemoveBody(r);
            _residents.Clear();
        }

        /// <summary>The current beat comes before <paramref name="id"/>.</summary>
        private bool BeatBefore(string id)
        {
            int at = _beats.FindIndex(b => b.Id == id);
            return at < 0 || _beatIndex < at;
        }

        /// <summary>Host: Skull Cove's people and animals (once; again after the story restarts or jumps).</summary>
        private void PopulateIsland3()
        {
            if (_residents.Exists(r => r.Group.StartsWith("I3"))) return;
            // The night watch between the mountain and the THIEF's shack (Skull Cove is always at night): pirates asleep
            // round the campfire and by the porches, two guards walking the lane with lanterns. (The lookout parrots'
            // perches stand empty: the birds sleep at night.)
            for (int i = 0; i < 12; i++)
            {
                int k = i;
                if (NightPoint($"Sleeper_{k}") is not { } spot) break;
                AddResident("I3Lookouts", () => Sleeper(k, spot).gameObject);
            }
            if (Island3Point("GuardA_0") is { } a0 && Island3Point("GuardA_1") is { } a1)
                AddResident("I3Lookouts", () => NightGuard("Bosun Barnaby", 0, a0.position, a1.position).gameObject);
            if (Island3Point("GuardB_0") is { } b0 && Island3Point("GuardB_1") is { } b1)
                AddResident("I3Lookouts", () => NightGuard("Squint", 2, b1.position, b0.position).gameObject);

            // The THIEF at home on his porch (he lives here: when you get to his shack he's there to let you in).
            if (Island3Point("Thief") is { } shack)
                AddResident("I3Thief", () =>
                {
                    _thief = SpawnNpc("THIEF", NpcRole.Guide, RobberLook, shack.position, shack.eulerAngles.y + 180f);
                    return _thief.gameObject;
                });

            // The tavern's champion, waiting for someone to ask about the treasure.
            GameObject floor = GameObject.Find("Environment/Island3/PirateTavern/RoomFloor");
            if (floor != null)
            {
                AddResident("I3Tavern", () =>
                    SpawnNpc("Iron Jaw Jack", NpcRole.Pirate, PirateLookOf(AvatarLook.Bodies.PirateFat), floor.transform.position + Vector3.forward * 2f, 180f, 160).gameObject);
                // The regulars round the ring, mugs up, waiting for a fight to bet on (they cheer the cage fight, 3.4).
                Vector3 ring = floor.transform.position + Vector3.forward * 2f;
                for (int i = 0; i < TavernRegulars.Length; i++)
                {
                    int k = i;
                    AddResident("I3Tavern", () => TavernRegular(k, ring).gameObject);
                }
            }

            // A drunk staggering between the tavern and the campfire, singing (too drunk to notice anyone sneaking by).
            if (NightPoint("Campfire") is { } fire && floor != null)
                AddResident("I3Locals", () => Drunk(fire.position, floor.transform.position).gameObject);

            // The jungle between the west road and the river: three guards on their beats, three monkeys.
            string[] names = { "Mangy Mo", "Long Tom", "Grog" };
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                if (Island3Point($"JungleGuard_{k}_0") is { } p0 && Island3Point($"JungleGuard_{k}_1") is { } p1)
                    AddResident("I3Jungle", () => SpawnGuard(names[k], k + 1, p0.position, 0f, new[] { p0.position, p1.position }).gameObject);
                if (Island3Point($"MonkeyHome_{k}") is { } home)
                    AddResident("I3Jungle", () => SpawnCreature(CreatureKind.Monkey, home.position, 0f, "Monkey")?.gameObject);
            }

            // The river: two alligators.
            for (int i = 0; i < 2; i++)
                if (Island3Point($"Gator_{i}") is { } g)
                    AddResident("I3River", () => SpawnCreature(CreatureKind.Alligator, g.position, g.eulerAngles.y, "Alligator")?.gameObject);

            // The Pirate King on his throne in the palace, two guards at his side (3.7b; only once part C has built the
            // palace: Environment/Island3/Story/PalaceThrone).
            if (BeatBefore("3.8") && GameObject.Find(Island3Story + "PalaceThrone") is { } throne)
            {
                AddResident("I3Palace", () => PirateKing(throne.transform).gameObject);
                for (int i = 0; i < 2; i++)
                {
                    int k = i;
                    AddResident("I3Palace", () => PalaceGuard(k, throne.transform).gameObject);
                }
            }

            // Old Snapper, asleep in its pond under the crag falls until someone comes for the gold (not once beaten).
            if (BeatBefore("3.8") && Island3Point("SnapperPond") is { } pond)
                AddResident("I3Snapper", () =>
                {
                    Creature boss = SpawnCreature(CreatureKind.OldSnapper, pond.position, pond.eulerAngles.y, "Old Snapper");
                    boss?.ServerSleep();
                    return boss != null ? boss.gameObject : null;
                });
            Debug.Log($"[Story] Skull Cove populated: {_residents.Count} residents");
        }
    }
}
