using System.Collections;
using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Vehicles;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Chapter 4, Finders Keepers (Docs/Prompt-Shako-Chapter-4-Finale.md): Sandy's island, the head office of the rival
    /// lifeguard company. The places are "Environment/Island4/Story/..." (GameSceneBuilder.Island4).
    ///  4.1 sail there on the sea chart, pay the landing fee at the jetty · 4.2 the rescue race against the company's
    ///  lifeguards (save 3 before they take 3) · 4.3 the warehouse: five Lost &amp; Found tags, past security · 4.4 the
    ///  Blackout Berry bar: the pills from the beach, the barman and his regulars fight · 4.5 the THIEF, cornered at the
    ///  helipad, spills it all.
    /// Island 4's own people (the toll guards, the tower's lookout, the bar) are put there while the team is still out
    /// at sea, like Skull Cove's (groups I4Jetty, I4Bar).
    /// </summary>
    public partial class StoryDirector
    {
        private const int LandingFee = 50, RaceGoal = 3, WarehouseTags = 5;
        private static readonly string[] RivalNames = { "FK Chad", "FK Brad", "FK Tad", "FK Kyle", "FK Troy", "FK Brent", "FK Lance", "FK Duke" };

        /// <summary>The company's lifeguards: the players' own goofy lifeguard in orange and white, an orange visor.</summary>
        public static AvatarLook RivalLook(int i)
        {
            AvatarLook look = AvatarLook.Lifeguard;
            look.TopColor = 3;      // orange
            look.BottomColor = 1;   // white
            look.Hat = HatStyle.Visor;
            look.HatColor = 3;
            look.Skin = (byte)(i * 3 % AvatarLook.SkinTones.Length);
            look.HairColor = (byte)(i * 5 % 6);
            look.Extras = AvatarExtras.Whistle | AvatarExtras.Sunscreen;
            return look;
        }

        /// <summary>The company's warehouse security: the same lifeguards in black.</summary>
        public static AvatarLook SecurityLook(int i)
        {
            AvatarLook look = RivalLook(i + 3);
            look.TopColor = 14;     // black
            look.BottomColor = 14;
            look.Hat = HatStyle.Cap;
            look.HatColor = 14;
            look.Extras = AvatarExtras.None;
            return look;
        }

        /// <summary>A character standing exactly on a builder's point (SpawnNpc grounds from 3 m above, which lands anyone
        /// under a roof on top of it: the smoothie bar's canopy, the lifeguard tower's hut).</summary>
        private StoryNpc SpawnAtPoint(string displayName, NpcRole role, AvatarLook look, Transform point, int health = 0, float turn = 0f)
        {
            StoryNpc npc = SpawnNpc(displayName, role, look, point.position, point.eulerAngles.y + turn, health);
            npc.ServerTeleport(point.position, point.eulerAngles.y + turn, keepExact: true);
            return npc;
        }

        private static Vector3 I4(string name, Vector3 fallback) => Island4Point(name) is { } t ? t.position : fallback;

        /// <summary>Resuming chapter 4 (or skipping to a beat): everyone over to the island 4 jetty, the boat tied up there.</summary>
        private void EnsureOnIsland4()
        {
            Vector3 arrival = Island4Arrival;
            bool anyFar = false;
            foreach (PlayerHub p in PlayerHub.All)
                if ((p.transform.position - arrival).sqrMagnitude > 230f * 230f) anyFar = true;
            if (anyFar) TeleportObservers(arrival);
            BerthAtIsland4();
            PopulateIsland4();
        }

        private void BerthAtIsland4()
        {
            if (_pirateBoat == null || Island4Point("BoatBerth") is not { } berth) return;
            if ((_pirateBoat.transform.position - berth.position).sqrMagnitude < 12f * 12f) return;
            _pirateBoat.ServerAutopilot(null);
            _pirateBoat.ServerKickAll();
            _pirateBoat.ServerPlace(berth.position, berth.eulerAngles.y);
            Mooring.Tie(_pirateBoat, berth.position, berth.eulerAngles.y);
            _pirateBoat.ServerSetLocked(false);
        }

        /// <summary>Host: island 4's people (once; again after the story restarts or jumps).</summary>
        private void PopulateIsland4()
        {
            if (_residents.Exists(r => r.Group.StartsWith("I4"))) return;
            for (int i = 0; i < 2; i++)
            {
                int k = i;
                if (Island4Point($"TollGuard_{k}") is { } at)
                    AddResident("I4Jetty", () =>
                    {
                        StoryNpc guard = SpawnAtPoint(RivalNames[k], NpcRole.Rival, RivalLook(k), at);
                        guard.ServerSetMood(AvatarMood.Happy);
                        return guard.gameObject;
                    });
            }
            if (Island4Point("RivalLookout") is { } lookout)
                AddResident("I4Jetty", () => SpawnAtPoint(RivalNames[2], NpcRole.Rival, RivalLook(2), lookout).gameObject);
            // The smoothie bar: the barman behind his counter, three regulars in front of it, glassy-eyed.
            if (Island4Point("Barman") is { } barman)
                AddResident("I4Bar", () =>
                {
                    StoryNpc b = SpawnAtPoint("Smoothie Steve", NpcRole.Rival, RivalLook(6), barman, 160);
                    b.ServerSetMood(AvatarMood.Happy);
                    return b.gameObject;
                });
            for (int i = 0; i < 3; i++)
            {
                int k = i;
                if (Island4Point($"BarRegular_{k + (k > 0 ? 1 : 0)}") is { } stool)
                    AddResident("I4Bar", () =>
                    {
                        StoryNpc r = SpawnAtPoint(BarRegularNames[k], NpcRole.Guest, GuestLook(4400 + k * 17, 0), stool, 3 * Damage.Punch, 180f);
                        r.ServerSetMood(AvatarMood.Hurt); // (the Blackout Berry's look)
                        return r.gameObject;
                    });
            }
            Debug.Log($"[Story] Finders Keepers populated: {_residents.FindAll(r => r.Group.StartsWith("I4")).Count} residents");
        }

        private static readonly string[] BarRegularNames = { "Woozy Wendell", "Blank Barry", "Sleepy Stu" };

        // ------------------------------------------------------------------ 4.1 arrival

        private IEnumerator FindersArrival()
        {
            SetChapter("Chapter 4: Finders Keepers");
            _island = Island4Setup;
            ResetChapterStats();
            TitleObservers("CHAPTER 4", "Finders Keepers");
            Transform berth = Island4Point("BoatBerth"), toll = Island4Point("TollBooth");
            if (berth == null || toll == null) yield break;
            // From the hotel island (where chapter 3 ended), on the pirates' boat; a resumed save starts there too.
            bool onIsland4 = false;
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && (p.transform.position - Island4Arrival).sqrMagnitude < 230f * 230f) onIsland4 = true;
            if (!onIsland4)
            {
                if (_island2Spawn != null && !TeamOnIsland2() && !AnyAtSea()) EnsureOnIsland2();
                // The boat at the hotel beach, in deep enough water (not left on the sand, not left at Skull Cove).
                if (_pirateBoat != null && (_pirateBoat.transform.position - berth.position).sqrMagnitude > 200f * 200f) MooredAtHotel();
            }
            if (_pirateBoat != null) _pirateBoat.ServerSetLocked(false);
            PopulateIsland4(); // its people take their places while you're out at sea
            yield return new WaitForSeconds(2.5f);
            yield return Say(_receptionist, "(on the radio) That island on the chart... the buoys out east are orange. Follow the orange.");
            SetObjective("Sail to the island on the sea chart: follow the orange buoys east");
            Marker("Finders Keepers", berth.position + Vector3.up * 3f);
            // There by boat (or travel pad): someone near the jetty.
            Vector3 jetty = I4("JettyHead", berth.position);
            while (StoryNpc.NearestPlayer(jetty, 45f) == null && StoryNpc.NearestPlayer(Island4Arrival, 30f) == null)
                yield return new WaitForSeconds(0.4f);
            if (_pirateBoat != null && _pirateBoat.Driver == null) BerthAtIsland4();
            else StartCoroutine(TieUpWhenLeft(berth));
            // The toll guards wave you down: a landing fee.
            List<StoryNpc> guards = ResidentsOf<StoryNpc>("I4Jetty");
            StoryNpc chad = guards.Find(n => n.Name == RivalNames[0]);
            if (chad == null) yield break;
            chad.ServerShout("PRIVATE BEACH! That'll be $50.", false);
            SetObjective("A toll on the jetty: talk to the guard");
            Marker("Toll", chad.NetworkObject);
            yield return WaitTalk(chad, "Talk to FK Chad");
            chad.ServerSetTalkable(false);
            NoMarker();
            yield return Say(chad, "Welcome to Finders Keepers! Private beach. Landing fee's fifty. Per boat. Per person. Per... vibe.");
            if (Economy.Instance != null && Economy.Instance.ServerSpend(LandingFee, "landing fee"))
                yield return Say(chad, "Pleasure doing business! Everything on this island was lost by somebody. Now it's ours.");
            else
            {
                yield return Say(chad, "No money? We'll put it on your bill. We LOVE bills.");
            }
            yield return You("Finders Keepers... like the tag. Like the airlift company that keeps billing us.");
            yield return Say(chad, "Lifeguards? Cute. Enjoy the beach. Don't touch the tourists: they're our tourists.");
            SetObjective("On to the island");
            Marker("The beach", Island4Arrival + Vector3.up * 2f);
            yield return Reach(Island4Arrival + Vector3.right * 4f, 6f);
            NoMarker();
        }

        private static bool AnyAtSea()
        {
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && Vehicle.SeatOf(p) != null) return true;
            return false;
        }

        /// <summary>The boat they came on, tied up at the jetty once its driver gets off near it.</summary>
        private IEnumerator TieUpWhenLeft(Transform berth)
        {
            float until = Time.time + 120f;
            while (_pirateBoat != null && Time.time < until)
            {
                if (_pirateBoat.Driver == null && (_pirateBoat.transform.position - berth.position).sqrMagnitude < 40f * 40f)
                {
                    BerthAtIsland4();
                    yield break;
                }
                yield return new WaitForSeconds(1f);
            }
        }

        // ------------------------------------------------------------------ 4.2 the rescue race

        private readonly List<VictimBrain> _raceTourists = new();
        private readonly List<StoryNpc> _raceRivals = new();
        private readonly Dictionary<StoryNpc, Vector3> _rivalAim = new();
        private int _raceYours, _raceTheirs;

        /// <summary>Host (tests): the race's score.</summary>
        public (int yours, int theirs) RaceScore => (_raceYours, _raceTheirs);

        private IEnumerator RescueRace()
        {
            EnsureOnIsland4();
            _island = Island4Setup;
            Transform beach = Island4Point("RaceBeach");
            if (beach == null) yield break;
            SetObjective("The lagoon beach: the company's lifeguards are on duty");
            Marker("Lagoon", beach.position + Vector3.up * 2f);
            yield return Reach(beach.position, 14f);
            NoMarker();
            yield return RunMission("Finders Keepers vs you", beach.position, RaceBody, ResetRace);
            HideBossBar();
        }

        private void ResetRace()
        {
            foreach (VictimBrain v in _raceTourists)
                if (v != null && v.IsSpawned) Despawn(v.gameObject);
            _raceTourists.Clear();
            _raceRivals.Clear();
            _rivalAim.Clear();
            _raceYours = _raceTheirs = 0;
        }

        private IEnumerator RaceBody()
        {
            ResetRace();
            Vector3 start = I4("RaceStart", Island4Arrival);
            for (int i = 0; i < 3; i++)
            {
                StoryNpc rival = SpawnNpc(RivalNames[3 + i], NpcRole.Rival, RivalLook(3 + i), start + new Vector3(i * 1.4f, 0f, -i * 1.2f), 230f);
                _raceRivals.Add(rival);
            }
            yield return Say(_raceRivals[0], "Oh, you want to RACE? Every tourist out there is a paying customer. Finders keepers!");
            BossIntroObservers("FINDERS KEEPERS vs YOU", "Save 3 tourists before their lifeguards grab 3");
            var counted = new HashSet<VictimBrain>();
            float nextSpawn = Time.time + 2f;
            int spawned = 0;
            while (_raceYours < RaceGoal)
            {
                _raceTourists.RemoveAll(v => v == null || !v.IsSpawned);
                // Keep two in trouble at a time.
                int inTrouble = _raceTourists.FindAll(v => v.State.NeedsHelp() && !counted.Contains(v)).Count;
                if (inTrouble < 2 && Time.time > nextSpawn)
                {
                    VictimBrain v = SpawnStoryTourist(Island4Setup, Profile(Island4Setup, spawned % 2), 2f, 7f);
                    if (v != null) { _raceTourists.Add(v); spawned++; }
                    nextSpawn = Time.time + Random.Range(3f, 6f);
                }
                foreach (VictimBrain v in _raceTourists)
                {
                    if (counted.Contains(v)) continue;
                    if (_rescued.Contains(v) || v.State is VictimState.Saved || v.HasBeenRescued && v.IsAshore)
                    {
                        counted.Add(v);
                        _raceYours++;
                        RescueService.Instance?.ServerAnnounce($"<color=#7dffa8><b>YOURS!</b></color> {v.Name} is safe. You {_raceYours} - {_raceTheirs} Finders Keepers");
                    }
                }
                StoryNpc grabbed = RivalsSwim(counted);
                if (grabbed != null && _raceTheirs >= RaceGoal)
                {
                    ServerFailMission("Finders Keepers grabbed three. Try again!");
                    yield return null;
                }
                ServerBossBar("FINDERS KEEPERS vs YOU", 1f - _raceYours / (float)RaceGoal);
                MissionObjective($"Save {RaceGoal} before their lifeguards grab {RaceGoal}:  you {_raceYours}  -  {_raceTheirs} them", _raceYours, RaceGoal);
                yield return new WaitForSeconds(0.25f);
            }
            ServerBossBar("FINDERS KEEPERS vs YOU", 0f);
            foreach (StoryNpc r in _raceRivals) if (r != null) r.ServerMoveTo(start, run: false);
            TitleObservers("YOU WIN THE RACE", $"You {_raceYours} - {_raceTheirs} Finders Keepers");
            yield return new WaitForSeconds(2f);
            yield return Say(_raceRivals.Count > 0 ? _raceRivals[0] : null, "Not FAIR! Those were OUR customers! I'm telling the boss!");
            yield return You("The boss. Who's the boss?");
        }

        /// <summary>
        /// Host: each rival swims at the nearest tourist in trouble nobody has hold of; one who gets there first takes
        /// them (<see cref="VictimBrain.ServerTakenByRival"/>: "LOST TO COMPETITION", the report's count, the company's
        /// bill, towed off). Returns the rival who took someone this step (or null).
        /// </summary>
        private StoryNpc RivalsSwim(HashSet<VictimBrain> counted)
        {
            StoryNpc took = null;
            var claimed = new HashSet<VictimBrain>();
            foreach (StoryNpc rival in _raceRivals)
            {
                if (rival == null || rival.IsDefeated) continue;
                VictimBrain best = null;
                float bestSq = float.MaxValue;
                foreach (VictimBrain v in _raceTourists)
                {
                    if (v == null || counted.Contains(v) || claimed.Contains(v) || v.Item.IsHeld || v.IsAshore || !v.State.NeedsHelp()) continue;
                    float d = (v.transform.position - rival.transform.position).sqrMagnitude;
                    if (d < bestSq) { bestSq = d; best = v; }
                }
                if (best == null) continue;
                claimed.Add(best);
                if (bestSq < 1.8f * 1.8f && best.ServerTakenByRival(rival.Name))
                {
                    counted.Add(best);
                    _raceTheirs++;
                    took = rival;
                    rival.ServerShout("FINDERS KEEPERS!", false);
                    RescueService.Instance?.ServerAnnounce($"<color=#ff9080><b>THEIRS!</b></color> You {_raceYours} - {_raceTheirs} Finders Keepers"); // (VictimBrain says who took whom)
                    continue;
                }
                // Swim at them (a little slower than a lifeguard sprinting: it's a race, not a rout). A new route only when
                // they've drifted or the rival stopped (each one is a path search).
                if (!rival.IsMoving || !_rivalAim.TryGetValue(rival, out Vector3 aim) || (aim - best.transform.position).sqrMagnitude > 2f * 2f)
                {
                    _rivalAim[rival] = best.transform.position;
                    rival.ServerMoveTo(best.transform.position, Shore.WaterDepthAt(rival.transform.position + Vector3.up * 0.1f) > 1.1f ? 2.6f : 3.4f, water: true);
                }
            }
            return took;
        }

        // ------------------------------------------------------------------ 4.3 the warehouse

        private readonly List<Item> _evidence = new();
        private readonly HashSet<Item> _evidenceFound = new();

        /// <summary>Host (tests): tags found in the warehouse.</summary>
        public int EvidenceFound => _evidenceFound.Count;

        private static readonly string[] TagReadings =
        {
            "\"Wallet. Lost by: Bernie, hotel beach. Sold back to: Bernie, $200.\"",
            "\"Sunglasses, pink. Lost by: Lola. Sold back to: Lola. Twice.\"",
            "\"Phone. Lost: island 1, after the 'free sample' pills. Airlift billed: $30.\"",
            "\"Casino chips, 2 sacks. Delivered by: the pirates. Paid: 10%.\"",
            "\"Gold watch. Handed in by: the lifeguards! Tip paid: $35. Sold for: $900.\" ...That was OUR find."
        };

        private IEnumerator Warehouse()
        {
            EnsureOnIsland4();
            Transform door = Island4Point("WarehouseDoor");
            if (door == null) yield break;
            SetObjective("The big hangar: FINDERS KEEPERS. What's in there?");
            Marker("Warehouse", door.position + Vector3.up * 3f);
            yield return Reach(door.position, 6f);
            NoMarker();
            yield return You("It's... everything. Every wallet, phone and pair of sunglasses anybody ever 'lost'. Even the casino's chips.");
            DisguiseEveryone(false);
            yield return RunStealthMission("The warehouse", door.position, WarehouseBody, ClearEvidence);
            foreach (StoryNpc n in new List<StoryNpc>(_warehouseGuards)) Remove(n);
            _warehouseGuards.Clear();
        }

        private readonly List<StoryNpc> _warehouseGuards = new();

        /// <summary>Security on its beat between the towers: bored, eyes on the floor ahead (a guard's usual 22 m and 55° saw the
        /// whole hangar at once).</summary>
        private static SightSettings WarehouseSight => new()
        {
            Range = 12f, HalfAngle = 42f, CloseRange = 2.5f, FillPerSecond = 0.55f, DecayPerSecond = 0.3f,
            DisguiseFactor = 0.08f, SuspiciousAt = 0.35f
        };
        private static readonly string[] SecurityNames = { "FK Security Dwayne", "FK Security Rex", "FK Security Moe", "FK Security Vince" };

        private void ClearEvidence()
        {
            foreach (Item t in _evidence)
                if (t != null && t.IsSpawned) Despawn(t.gameObject);
            _evidence.Clear();
            _evidenceFound.Clear();
            _warehouseGuards.Clear();
        }

        private IEnumerator WarehouseBody()
        {
            ClearEvidence();
            for (int i = 0; i < SecurityNames.Length; i++)
            {
                if (Island4Point($"WarehouseGuard_{i}_0") is not { } a || Island4Point($"WarehouseGuard_{i}_1") is not { } b) continue;
                StoryNpc guard = SpawnNpc(SecurityNames[i], NpcRole.Rival, SecurityLook(i), a.position, a.eulerAngles.y, 100);
                guard.ServerGuard(new[] { a.position, b.position }, FighterProfile.Pirate(NpcWeaponKind.Fists), WarehouseSight);
                _warehouseGuards.Add(guard);
            }
            for (int i = 0; i < WarehouseTags; i++)
            {
                if (Island4Point($"Evidence_{i}") is not { } spot) continue;
                Item tag = SpawnItem("LostTag", spot.position);
                if (tag == null) continue;
                _evidence.Add(tag);
                _spawnedActors.Add(tag.gameObject); // (a failed try takes them away with its guards)
            }
            int read = 0;
            while (_evidenceFound.Count < WarehouseTags)
            {
                Item next = null;
                float best = float.MaxValue;
                foreach (Item t in _evidence)
                {
                    if (t == null) continue;
                    if (t.IsHeld) _evidenceFound.Add(t);
                    if (_evidenceFound.Contains(t)) continue;
                    PlayerHub near = StoryNpc.NearestPlayer(t.transform.position, 60f);
                    float d = near != null ? (near.transform.position - t.transform.position).sqrMagnitude : 0f;
                    if (d < best) { best = d; next = t; }
                }
                if (_evidenceFound.Count > read)
                {
                    read = _evidenceFound.Count;
                    StartCoroutine(You(TagReadings[(read - 1) % TagReadings.Length]));
                }
                if (next != null && _markerTarget.Value != next.NetworkObject) Marker("Tag", next.NetworkObject);
                MissionObjective($"Find the Lost & Found tags among the towers ({_evidenceFound.Count}/{WarehouseTags}). Security mustn't see you: crouch, keep to the towers.",
                    _evidenceFound.Count, WarehouseTags);
                yield return new WaitForSeconds(0.25f);
            }
            NoMarker();
            TitleObservers("EVIDENCE", "Lost, found, sold back. All of it.");
            yield return new WaitForSeconds(2f);
            yield return You("Every tag says the same thing. Lost... found... sold back. Who signs these? 'S.'");
        }

        // ------------------------------------------------------------------ 4.4 Blackout Berry

        private IEnumerator BlackoutBerry()
        {
            EnsureOnIsland4();
            Transform front = Island4Point("SmoothieBar");
            if (front == null) yield break;
            SetObjective("The smoothie bar on the beach: 'Blackout Berry'?");
            Marker("Smoothie bar", front.position + Vector3.up * 2.5f);
            yield return Reach(front.position, 6f);
            NoMarker();
            List<StoryNpc> bar = ResidentsOf<StoryNpc>("I4Bar");
            StoryNpc steve = bar.Find(n => n.Name == "Smoothie Steve");
            if (steve == null) yield break;
            yield return Say(steve, "Welcome! Try a Blackout Berry: our special. One sip and you forget where you put your wallet!");
            yield return You("Those little pills in the smoothies... that's the stuff the guy was handing out on the first beach!");
            steve.ServerSetMood(AvatarMood.Angry);
            yield return Say(steve, "...Nobody was supposed to remember that. LADS!");
            yield return RunMission("Blackout Berry", front.position, BarBrawlBody, () => ResetResidents("I4Bar"));
            ForgetResidents("I4Bar"); // (beaten: not back after a reload)
        }

        private IEnumerator BarBrawlBody()
        {
            List<StoryNpc> bar = ResidentsOf<StoryNpc>("I4Bar");
            if (bar.Count == 0) yield break;
            BossIntroObservers("BLACKOUT BERRY", "The barman and his regulars. Fists: jab, jab, hook.");
            Vector3 front = I4("SmoothieBar", bar[0].transform.position);
            foreach (StoryNpc n in bar)
            {
                n.ServerSetPose(AvatarPose.Normal);
                n.ServerFight(n.Name == "Smoothie Steve" ? FighterProfile.Brawler(0.6f) : FighterProfile.Drunk(), front);
            }
            while (true)
            {
                int down = 0;
                foreach (StoryNpc n in bar) if (n == null || n.IsDefeated) down++;
                MissionObjective($"Bar fight! Knock them out ({down}/{bar.Count})", down, bar.Count);
                if (down >= bar.Count) break;
                yield return new WaitForSeconds(0.25f);
            }
            foreach (StoryNpc n in bar) if (n != null) n.ServerStopFighting();
            TitleObservers("BAR CLEARED", "Last orders");
            yield return new WaitForSeconds(1.5f);
            StoryNpc steve = bar.Find(n => n != null && n.Name == "Smoothie Steve");
            if (steve != null) yield return Say(steve, "Ow. Fine! The berries are for the tourists. They nap, they drop their stuff, the THIEF collects. Ask HIM. He's at the helipad.");
        }

        // ------------------------------------------------------------------ 4.5 the THIEF talks

        private IEnumerator ThiefTalks()
        {
            EnsureOnIsland4();
            Transform pad = Island4Point("Helipad");
            if (pad == null) yield break;
            if (_thief == null || !_thief.IsSpawned) _thief = SpawnNpc("THIEF", NpcRole.Guide, RobberLook, pad.position, pad.eulerAngles.y);
            else _thief.ServerTeleport(pad.position, pad.eulerAngles.y);
            _thief.ServerSetMood(AvatarMood.Scared);
            _thief.ServerSetPose(AvatarPose.Scared);
            _thief.ServerKeepShouting("I DIDN'T DO IT! ...THIS time.", pad.position + pad.forward * 6f, 4f);
            SetObjective("The THIEF! Corner him at the helipad");
            Marker("THIEF", _thief.NetworkObject);
            yield return WaitTalk(_thief, "Talk to the THIEF");
            _thief.ServerKeepShouting(null, pad.position + pad.forward * 6f);
            _thief.ServerFace(null);
            _thief.ServerSetTalkable(false);
            NoMarker();
            _thief.ServerSetPose(AvatarPose.Normal);
            yield return ThiefSays("Okay okay OKAY! Don't hit me! I'll tell you everything. For free! ...Mostly.");
            yield return ThiefSays("It's SANDY. Sweet old Sandy from the Lost & Found. She runs ALL of it.");
            yield return ThiefSays("Finders Keepers Rescue Co.? Hers. Every tourist you don't save, her chopper picks up, and she bills the family.");
            yield return ThiefSays("The pills on the beach? Her Blackout Berry. Tourists nap, drop their wallets, and I... collect.");
            yield return ThiefSays("You hand the 'lost' things in at her kiosk, she tips you, and sells them back to the owners. TWICE.");
            yield return ThiefSays("And the pirates? Delivery boys. The casino's gold was coming HERE.");
            yield return You("And the fourth sack?");
            yield return ThiefSays("...Commission?");
            _thief.ServerSetMood(AvatarMood.Neutral);
            yield return ThiefSays("She's up in the villa on the cliff. Under the giant golden statue of herself. Can't miss it. She'd hate it if you missed it.");
        }

        // ------------------------------------------------------------------ automated run-through (host)

        /// <summary>
        /// Host: plays chapter 4 (4.1 on) with the host's lifeguard and checks each beat finishes. "[Check] PASS/FAIL",
        /// then "[Check] DONE".
        /// </summary>
        private IEnumerator Chapter4Check(PlayerHub me)
        {
            _checkFails = 0;
            void Stand(Vector3 p) => me.Motor.Teleport(p + Vector3.up * 0.3f);
            string[] points = { "Island4Arrival", "BoatBerth", "JettyHead", "TollBooth", "TollGuard_0", "RivalLookout", "RaceBeach", "RaceStart", "DropOff",
                "WarehouseDoor", "WarehouseInside", "Evidence_0", "Evidence_4", "WarehouseGuard_0_0", "WarehouseGuard_3_1", "SmoothieBar", "Barman",
                "Helipad", "HeliPark", "SandyVilla", "SandyStatue", "SandyStairsTop", "SandyJetty", "SandyJettyEnd", "KrakenLair", "HeliDrop",
                "PoliceDock", "PoliceBoatBerth", "CastWave", "Route_0", "Route_5" };
            int missing = 0;
            foreach (string p in points) if (GameObject.Find(Island4Story + p) == null) { missing++; Debug.Log($"[Check] missing point {p}"); }
            Check("island 4: every beat's point exists", missing == 0, $"{points.Length - missing}/{points.Length}");
            Check("island 4: its navmesh is loaded", UnityEngine.AI.NavMesh.SamplePosition(Island4Arrival, out _, 3f, UnityEngine.AI.NavMesh.AllAreas));

            int index = _beats.FindIndex(b => b.Id == "4.1");
            StartAt(index);
            yield return new WaitForSeconds(4f);
            Check("4.1 starts chapter 4", _beat.Value == "4.1" && _chapter.Value.StartsWith("Chapter 4"), _chapter.Value);
            Check("4.1 the boat is free to take", _pirateBoat != null && !_pirateBoat.IsLocked);
            Check("4.1 island 4's people are there", ResidentsOf<StoryNpc>("I4Jetty").Count >= 3 && ResidentsOf<StoryNpc>("I4Bar").Count >= 4,
                $"{ResidentsOf<StoryNpc>("I4Jetty").Count} jetty, {ResidentsOf<StoryNpc>("I4Bar").Count} bar");
            Stand(I4("JettyHead", Island4Arrival));
            float t = Time.time;
            StoryNpc chad = null;
            while (Time.time - t < 20f && (chad == null || !chad.IsTalkable))
            {
                chad = ResidentsOf<StoryNpc>("I4Jetty").Find(n => n.Name == RivalNames[0]);
                yield return null;
            }
            Check("4.1 a toll guard on the jetty wants a word", chad != null && chad.IsTalkable);
            int money = Economy.Money;
            if (chad != null) { Stand(chad.transform.position + chad.transform.forward * 1.5f); _talks.Add((chad, me)); }
            t = Time.time;
            while (_markerLabel.Value != "The beach" && Time.time - t < 40f) yield return null;
            Check("4.1 the landing fee is paid (or billed)", Economy.Money == money - LandingFee || money < LandingFee, $"${money} -> ${Economy.Money}");
            Stand(Island4Arrival + Vector3.right * 4f);
            yield return WaitBeat("4.2", 10f);
            Check("4.1 past the jetty moves on", _beat.Value == "4.2");

            Transform beach = Island4Point("RaceBeach");
            if (beach != null) Stand(beach.position);
            t = Time.time;
            while (!MissionRunning && Time.time - t < 20f) yield return null;
            yield return new WaitForSeconds(9f);
            Check("4.2 three company lifeguards race you", _raceRivals.Count == 3);
            Check("4.2 tourists in trouble past the drop-off", _raceTourists.Exists(v => v != null && v.State.NeedsHelp()), $"{_raceTourists.Count}");
            Check("4.2 the mission bar is up", BossBarTitle == "FINDERS KEEPERS vs YOU");
            // Let them win first: three grabbed fails the race, and it starts again.
            t = Time.time;
            bool failed = false;
            while (Time.time - t < 120f && !failed)
            {
                if (_missionFailReason != null) failed = true;
                foreach (VictimBrain v in _raceTourists)
                    if (v != null && v.State.NeedsHelp() && !v.Item.IsHeld)
                        foreach (StoryNpc r in _raceRivals) if (r != null) { r.ServerTeleport(v.transform.position + Vector3.right * 1f, 270f, keepExact: true); break; }
                yield return new WaitForSeconds(0.5f);
            }
            Check("4.2 three grabbed by them fails the race (it starts again)", failed);
            t = Time.time;
            while (_raceRivals.Count == 0 || _raceTheirs > 0 && Time.time - t < 15f) yield return null;
            yield return new WaitForSeconds(4f);
            // Now win it: each tourist in trouble pulled out onto the lagoon's shelf.
            t = Time.time;
            while (_beat.Value == "4.2" && Time.time - t < 150f)
            {
                foreach (StoryNpc r in _raceRivals) if (r != null) r.ServerTeleport(I4("RaceStart", Island4Arrival), 230f); // (kept on the beach)
                foreach (VictimBrain v in _raceTourists.ToArray())
                {
                    if (v == null || !v.State.NeedsHelp() || v.Item.IsHeld) continue;
                    Vector3 beside = v.transform.position + Vector3.right * 1.2f;
                    beside.y = World.Water.WaterSurface.HeightAt(beside) - 1.2f;
                    me.Motor.Teleport(beside);
                    me.Look.LookAt(v.transform.position);
                    yield return new WaitForSeconds(0.4f);
                    DevCommands.Execute("grab tourist");
                    yield return new WaitForSeconds(0.8f);
                    for (int hop = 0; hop < 12 && v != null && v.Item.IsHeld; hop++)
                    {
                        Vector3 p = me.transform.position + Vector3.right * 3f;
                        float depth = Shore.WaterDepthAt(p + Vector3.up * 2f);
                        p.y = depth > 1.3f ? World.Water.WaterSurface.HeightAt(p) - 1.2f : Shore.GroundHeightAt(p + Vector3.up * 3f) + 0.2f;
                        me.Motor.Teleport(p);
                        yield return new WaitForSeconds(0.3f);
                    }
                    if (v != null && v.Item.IsHeld) DevCommands.Execute("drop");
                    yield return new WaitForSeconds(1f);
                    if (v != null && v.State == VictimState.Unconscious) { DevCommands.Execute("cpr 8"); yield return new WaitForSeconds(2f); }
                    break;
                }
                yield return new WaitForSeconds(0.5f);
            }
            Check("4.2 saving three wins the race", _beat.Value == "4.3", $"you {_raceYours} - {_raceTheirs} them");

            Transform door = Island4Point("WarehouseDoor");
            if (door != null) Stand(door.position);
            t = Time.time;
            while (!MissionRunning && Time.time - t < 20f) yield return null;
            yield return new WaitForSeconds(1f);
            Check("4.3 five tags and four guards in the warehouse", _evidence.Count == WarehouseTags && _warehouseGuards.Count == 4, $"{_evidence.Count} tags, {_warehouseGuards.Count} guards");
            Check("4.3 security walks its beats", _warehouseGuards.TrueForAll(g => g != null && g.IsGuarding));
            _failOnSpotted = false; // (the test teleports about in plain sight: it checks the tags, not its sneaking)
            foreach (Item tag in _evidence.ToArray())
            {
                if (tag == null) continue;
                Stand(tag.transform.position + Vector3.back * 0.7f);
                me.Look.LookAt(tag.transform.position);
                yield return new WaitForSeconds(0.4f);
                DevCommands.Execute("grab Lost");
                yield return new WaitForSeconds(1.2f); // (the host confirms the pickup before we move on)
            }
            yield return WaitBeat("4.4", 20f);
            Check("4.3 five tags found moves on", _beat.Value == "4.4", $"{_evidenceFound.Count} found");

            Transform bar = Island4Point("SmoothieBar");
            if (bar != null) Stand(bar.position);
            t = Time.time;
            while (!MissionRunning && Time.time - t < 30f) yield return null;
            List<StoryNpc> fighters = ResidentsOf<StoryNpc>("I4Bar");
            Check("4.4 the barman and his regulars fight", fighters.Count == 4 && fighters.TrueForAll(n => n.IsFighting), $"{fighters.Count}");
            PlayerCombat combat = me.GetComponent<PlayerCombat>();
            for (int round = 0; round < 60 && MissionRunning; round++)
            {
                foreach (StoryNpc n in fighters)
                    if (n != null && !n.IsDefeated) ((IDamageable)n).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, n.HeadPosition, me.Head.forward);
                if (combat != null) combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
                yield return new WaitForSeconds(0.3f);
            }
            yield return WaitBeat("4.5", 25f);
            Check("4.4 the bar cleared moves on", _beat.Value == "4.5");

            t = Time.time;
            while ((_thief == null || !_thief.IsTalkable) && Time.time - t < 8f) yield return null;
            Check("4.5 the THIEF is cornered at the helipad", _thief != null && (_thief.transform.position - I4("Helipad", Vector3.zero)).magnitude < 4f);
            if (_thief != null) { Stand(_thief.transform.position + _thief.transform.forward * 1.5f); _talks.Add((_thief, me)); }
            t = Time.time;
            while (_beat.Value == "4.5" && Time.time - t < 90f) yield return null;
            Check("4.5 his story told moves on", _beat.Value == "end" || _beats.FindIndex(b => b.Id == _beat.Value) > _beats.FindIndex(b => b.Id == "4.5"), _beat.Value);
            yield return Chapter4CheckBoss(me);
            Debug.Log($"[Check] DONE: {(_checkFails == 0 ? "ALL PASS" : _checkFails + " FAILED")}");
        }
    }
}
