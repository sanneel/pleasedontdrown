using System.Collections;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Vehicles;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The islands' own people (host), put there when the game starts: Milo at his boat dock on island 1; the hotel's
    /// director, staff and guests on island 2; Skull Cove's pirates on island 3 (asleep round the campfire, a crowd at
    /// the fire, two walking the lane, the village drunk). They're ordinary people now: anyone can be picked up and
    /// thrown. (The beach crowds are BeachCrowd's.)
    /// </summary>
    public partial class StoryDirector
    {
        [Header("Island one boats")]
        [SerializeField] private StoryNpc _rentalOperator;
        [SerializeField] private Vehicle[] _rentalFleet = System.Array.Empty<Vehicle>();
        private readonly FishNet.Object.Synchronizing.SyncVar<bool> _rentalUnlocked = new(false);
        public bool RentalUnlocked => _rentalUnlocked.Value;
        public System.Collections.Generic.IReadOnlyList<Vehicle> RentalFleet => _rentalFleet;

        private const string Island3Places = "Environment/Island3/Story/";

        private void SetRentalAccess(bool unlocked)
        {
            _rentalUnlocked.Value = unlocked;
            foreach (Vehicle ski in _rentalFleet)
                if (ski != null) ski.ServerSetLocked(!unlocked, "Milo's boat");
        }

        private void PopulateIslands()
        {
            if (_rentalOperator != null)
            {
                _rentalOperator.ServerSetup("Milo", NpcRole.Bystander, RentalLook);
                _rentalOperator.ServerSetTalkable(false);
            }
            PopulateIsland2();
            PopulateIsland3();
        }

        // ------------------------------------------------------------------ island 2: the Grand Coral

        // Lobby spot of the hotel director (in front of the reception, facing the door).
        private static readonly Vector3 DirectorSpot = new(24.5f, 1.85f, -249.6f);
        private static readonly Vector3 BellboySpot = new(22.4f, 1.85f, -241.2f), PoolAttendantSpot = new(50.5f, 1.7f, -241.5f);
        // Along the front of the hotel, from the Coral Bar's deck to the pool deck and back (clear of the carpet's ropes,
        // and on the sea side of the forecourt fountain at (2.5, -238.5)).
        private static readonly Vector3[] WaiterRoute =
            { new(-40f, 1.8f, -243f), new(-12f, 1.8f, -238.5f), new(2.5f, 1.8f, -235.2f), new(8f, 1.8f, -235.5f), new(32f, 1.8f, -235.5f), new(50f, 1.8f, -240.5f) };

        private void PopulateIsland2()
        {
            StoryNpc director = SpawnIndoors("Mr. Castellane", NpcRole.Guide, DirectorLook, DirectorSpot, 0f);
            director.ServerSetTalkable(false);
            // The staff, in the hotel's uniform: a bellhop at the door, a waitress going between the bar and the pool, a
            // pool attendant on the deck.
            Staff("Bellhop Benny", BellboySpot, 0f, AvatarLook.Bodies.HotelBellhop);
            Staff("Pool attendant Ines", PoolAttendantSpot, 0f);
            StoryNpc waiter = Staff("Waitress Paola", WaiterRoute[0], 90f);
            StartCoroutine(Rounds(waiter, WaiterRoute, 6f, 12f, 1.5f));
            SlotGuests();
            LoungerGuests();
        }

        private StoryNpc Staff(string displayName, Vector3 at, float yaw, byte body = AvatarLook.Bodies.HotelAttendantB)
        {
            AvatarLook look = GuestLook(4242, AvatarLook.Bodies.IsFeminine(body) ? 1 : 0);
            look.Body = body;
            StoryNpc npc = SpawnNpc(displayName, NpcRole.Bystander, look, at, yaw);
            npc.ServerSetTalkable(false);
            return npc;
        }

        // Guests at the casino's slot machines (GameSceneBuilder.HotelInterior: six along the outer wall, a low stool in
        // front of each): which machine (0-5), sitting facing it.
        private static readonly int[] SlotSeats = { 1, 4 };
        private const float SlotStoolX = -2.15f, SlotStoolTop = 2.16f, SlotFirstZ = -246.4f, SlotStep = 1.15f, SlotSitDrop = 0.5f;

        private void SlotGuests()
        {
            for (int k = 0; k < SlotSeats.Length; k++)
            {
                var at = new Vector3(SlotStoolX, SlotStoolTop - SlotSitDrop, SlotFirstZ - SlotSeats[k] * SlotStep);
                StoryNpc guest = SpawnIndoors(VictimBrain.RandomName(k == 0), NpcRole.Guest, GuestLook(8300 + k * 13, k == 0 ? 1 : 0), at, 270f);
                guest.ServerSetPose(AvatarPose.SitStool);
            }
        }

        // Guests sitting up on the pool's loungers, backs to the backrest (the set's -z), feet toward the pool. The sitting
        // pose's root is at the feet, the hips SitBack behind them: the hips just in front of the backrest.
        private static readonly (string set, float side)[] LoungerSeats = { ("LoungeS0", -1f), ("LoungeN2", 1f) };
        private const float LoungerSide = 1.2f, LoungerTop = 0.66f, LoungerHipsZ = -0.38f;

        private void LoungerGuests()
        {
            for (int k = 0; k < LoungerSeats.Length; k++)
            {
                GameObject set = GameObject.Find("Environment/Island2Pool/" + LoungerSeats[k].set);
                if (set == null) continue;
                Transform t = set.transform;
                Vector3 feet = t.TransformPoint(new Vector3(LoungerSeats[k].side * LoungerSide, LoungerTop, LoungerHipsZ + StoryNpc.SitBack));
                StoryNpc guest = SpawnIndoors(VictimBrain.RandomName(k == 0), NpcRole.Guest, GuestLook(9100 + k * 17, k == 0 ? 1 : 0), feet, t.eulerAngles.y);
                guest.ServerSetPose(AvatarPose.Sit);
                guest.ServerSetMood(AvatarMood.Happy);
            }
        }

        /// <summary>A character inside the hotel, exactly where asked (the navmesh snap put the lobby's people on its roof).</summary>
        private StoryNpc SpawnIndoors(string displayName, NpcRole role, AvatarLook look, Vector3 at, float yaw)
        {
            StoryNpc npc = SpawnNpc(displayName, role, look, at, yaw);
            npc.ServerTeleport(at, yaw, keepExact: true);
            return npc;
        }

        // ------------------------------------------------------------------ island 3: Skull Cove (always night)

        private static readonly string[] SleeperNames = { "Snorin' Sam", "Drowsy Dan", "Old Wheeze", "Barnacle Bess", "Nodding Ned", "Grumbles", "Lazy Lars", "Mumbling Mick" };
        private static readonly string[] FireCrowd = { "One-Tooth Tess", "Bilge Bob", "Rum Rita", "Patch", "Salty Sven" };
        private static readonly string[] DrunkSongs = { "Yo ho ho... hic!", "Fifteen men on a dead man's... where was I?", "Who moved the island?", "Hic! Lovely night for it.", "The moon's got two of it..." };

        /// <summary>The night village's places (GameSceneBuilder.Island3Night): the dock, the sleepers' spots, the campfire.</summary>
        private static Transform NightPoint(string name) => GameObject.Find("Environment/Island3/NightArrival/" + name)?.transform;

        private static Transform Island3Point(string name) => GameObject.Find(Island3Places + name)?.transform;

        private void PopulateIsland3()
        {
            int people = 0;
            // Asleep round the campfire and by the porches.
            for (int i = 0; i < 12; i++)
            {
                if (NightPoint($"Sleeper_{i}") is not { } spot) break;
                StoryNpc sleeper = SpawnNpc(SleeperNames[i % SleeperNames.Length], NpcRole.Pirate, PirateLook(i + 3), spot.position, spot.eulerAngles.y, 100);
                sleeper.ServerTeleport(spot.position, spot.eulerAngles.y, keepExact: true);
                sleeper.ServerSetPose(AvatarPose.Lie);
                people++;
            }
            // Still up at the fire, mugs in hand.
            if (NightPoint("Campfire") is { } fire)
            {
                for (int i = 0; i < FireCrowd.Length; i++)
                {
                    float a = (i * 72f + 20f) * Mathf.Deg2Rad;
                    Vector3 at = fire.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3.4f;
                    StoryNpc npc = SpawnNpc(FireCrowd[i], NpcRole.Pirate, PirateLook(i + 5), at, 0f, 100);
                    npc.ServerFace(fire.position);
                    npc.ServerSetMood(AvatarMood.Happy);
                    people++;
                }
                // The village drunk staggers between the fire and the tavern, singing.
                GameObject tavern = GameObject.Find("Environment/Island3/PirateTavern/RoomFloor");
                if (tavern != null)
                {
                    StoryNpc drunk = SpawnNpc("Wobbly Will", NpcRole.Pirate, PirateLook(9), fire.position + Vector3.right * 3f, 0f, 100);
                    drunk.ServerSetDrunk(true);
                    StartCoroutine(Rounds(drunk, new[] { fire.position + Vector3.right * 3f, tavern.transform.position + Vector3.back * 4f }, 9f, 14f, 1.1f, DrunkSongs));
                    people++;
                }
            }
            // Two walking the lane.
            if (Island3Point("GuardA_0") is { } a0 && Island3Point("GuardA_1") is { } a1)
                people += Walker("Bosun Barnaby", 0, a0.position, a1.position);
            if (Island3Point("GuardB_0") is { } b0 && Island3Point("GuardB_1") is { } b1)
                people += Walker("Squint", 2, b1.position, b0.position);
            Debug.Log($"[Story] Skull Cove: {people} pirates");
        }

        private int Walker(string name, int look, Vector3 a, Vector3 b)
        {
            StoryNpc npc = SpawnNpc(name, NpcRole.Pirate, PirateLook(look), a, 0f, 100);
            StartCoroutine(Rounds(npc, new[] { a, b }, 4f, 9f, 1.4f));
            return 1;
        }

        /// <summary>Host: walks back and forth along <paramref name="route"/>, resting at each end (and shouting a line now and then).</summary>
        private IEnumerator Rounds(StoryNpc npc, Vector3[] route, float restMin, float restMax, float speed, string[] lines = null)
        {
            int i = 0, step = 1;
            while (npc != null && npc.IsSpawned)
            {
                bool end = i == 0 || i == route.Length - 1;
                yield return new WaitForSeconds(end ? Random.Range(restMin, restMax) : 0.2f);
                if (npc == null || !npc.IsSpawned) yield break;
                if (!npc.IsUpright) continue; // knocked down or carried off: wait till they're up again
                if (i + step < 0 || i + step >= route.Length) step = -step;
                i += step;
                npc.ServerMoveTo(route[i] + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)), speed);
                if (lines != null && Random.value < 0.6f) npc.ServerShout(lines[Random.Range(0, lines.Length)]);
                float started = Time.time;
                while (npc != null && npc.IsMoving && Time.time - started < 30f) yield return new WaitForSeconds(0.3f);
            }
        }
    }
}
