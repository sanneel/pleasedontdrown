using System.Collections;
using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Chapter 3, Skull Cove (Docs/16-Plan-Islands-2-3.md): no pay here, you're undercover. The places are
    /// "Environment/Island3/Story/..." (GameSceneBuilder.Island3Story).
    ///  3.1 at night: land under the mountain, climb the lookout, sneak past the sleeping pirates (seen = restart) · 3.2 the THIEF: disguises, "buy suppressed guns" ·
    ///  3.3 the gun stall: a pistol and a shotgun, every gun suppressed · 3.4 the tavern's cage fighter ·
    ///  3.5 the jungle (monkeys take hats; a loud shot = caught) · 3.6 the rope swing over the alligators' river ·
    ///  3.7 Old Snapper at the crag falls · 3.8 the gold vault behind those falls, in the heart of the mountain.
    /// </summary>
    public partial class StoryDirector
    {
        private const string Island3Story = "Environment/Island3/Story/";
        private StoryNpc _thief;

        private static Transform Island3Point(string name)
        {
            GameObject go = GameObject.Find(Island3Story + name);
            if (go == null) Debug.LogWarning($"[Story] island 3 has no '{name}' (rebuild island three)");
            return go != null ? go.transform : null;
        }

        private static ShopCounter GunStall => GameObject.Find(Island3Story + "Island3GunShop/Counter")?.GetComponent<ShopCounter>();

        private static Vector3 Island3Arrival => GameObject.Find("Environment/Island3/Island3Arrival") is { } a ? a.transform.position : new Vector3(110f, 0.5f, -903f);

        /// <summary>The THIEF's line (over the radio when he isn't here: a resumed save).</summary>
        private IEnumerator ThiefSays(string text) =>
            _thief != null && _thief.IsSpawned ? Say(_thief, text) : Say("THIEF", "(on the radio) " + text, new Color(1f, 0.75f, 0.45f));

        /// <summary>Resuming chapter 3 (or skipping to a beat): everyone over to the pirates' dock.</summary>
        private void EnsureOnIsland3(Vector3? at = null)
        {
            Vector3 dock = at ?? Island3Arrival;
            bool anyFar = false;
            foreach (PlayerHub p in PlayerHub.All)
                if ((p.transform.position - dock).sqrMagnitude > 230f * 230f) anyFar = true;
            if (anyFar) TeleportObservers(dock);
            if (_beatIndex >= 0 && _beats[_beatIndex].Id.CompareTo("3.3") >= 0 && GunStall != null) GunStall.ServerSetAvailable(true);
            PopulateIsland3();
        }

        private void DisguiseEveryone(bool on)
        {
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p.TryGetComponent(out PlayerCombat c)) c.ServerSetDisguise(on);
        }

        /// <summary>Wait until a lifeguard is within <paramref name="radius"/> m of the point.</summary>
        private static IEnumerator Reach(Vector3 point, float radius)
        {
            while (StoryNpc.NearestPlayer(point, radius) == null) yield return new WaitForSeconds(0.2f);
        }

        // ------------------------------------------------------------------ automated run-through (host)

        private IEnumerator WaitBeat(string id, float seconds)
        {
            float t0 = Time.time;
            while (_beat.Value != id && Time.time - t0 < seconds) yield return null;
        }

        /// <summary>
        /// Host: plays chapter 3 from 3.1 to the end with the host's lifeguard (teleporting about, talking, buying, hitting)
        /// and checks each beat finishes. Logs "[Check] PASS/FAIL" and "[Check] DONE".
        /// </summary>
        private IEnumerator Chapter3Check(PlayerHub me)
        {
            _checkFails = 0;
            PlayerCombat combat = me.GetComponent<PlayerCombat>();
            void Stand(Vector3 p) => me.Motor.Teleport(p + Vector3.up * 0.3f);
            int index = _beats.FindIndex(b => b.Id == "3.1");
            StartAt(index);
            yield return new WaitForSeconds(1f);
            Check("3.1 starts at the dock under the mountain", _beat.Value == "3.1" && (me.transform.position - NightDock).sqrMagnitude < 25f * 25f,
                $"{Vector3.Distance(me.transform.position, NightDock):F1} m off");
            Check("3.1 it's night on Skull Cove", World.IslandNight.NightAt(me.transform.position) > 0.95f);
            Transform lookout = NightPoint("Lookout");
            yield return new WaitForSeconds(6f);
            if (lookout != null) Stand(lookout.position);
            float waited = Time.time; // the radio lines, then the mission
            while (!MissionRunning && Time.time - waited < 40f) yield return null;
            yield return new WaitForSeconds(0.5f);
            Transform thief = Island3Point("Thief");
            var sleepers = new List<StoryNpc>();
            foreach (StoryNpc n in StoryNpc.All) if (n.IsFastAsleep) sleepers.Add(n);
            Check("3.1 pirates asleep in the village", sleepers.Count >= 6, $"{sleepers.Count}");
            int alarms = 0;
            void Alarmed(string who, PlayerHub whom, string why) => alarms++;
            Stealth.ServerSpotted += Alarmed;
            yield return new WaitForSeconds(10f); // on the tower: out of everyone's sight
            // Footsteps running past a sleeper wake him; he looks about, sees nobody, sleeps again.
            if (sleepers.Count > 0)
            {
                StoryNpc s0 = sleepers[0];
                for (int i = 0; i < 6 && s0.IsFastAsleep; i++)
                {
                    Stealth.ServerNoise(s0.transform.position + Vector3.right * 4f, Stealth.Running, me, NoiseKind.Footsteps);
                    yield return new WaitForSeconds(0.5f);
                }
                Check("3.1 running near a sleeper wakes him", !s0.IsFastAsleep);
                float t0 = Time.time;
                while (!s0.IsFastAsleep && Time.time - t0 < 15f) yield return null;
                Check("3.1 seeing nobody, he goes back to sleep", s0.IsFastAsleep, $"{Time.time - t0:F1} s");
            }
            Stealth.ServerSpotted -= Alarmed;
            Check("3.1 nobody sees you on the tower", alarms == 0, $"{alarms} alarms");
            if (thief != null) Stand(thief.position);
            yield return WaitBeat("3.2", 8f);
            Check("3.1 reaching the shack unseen moves on", _beat.Value == "3.2");

            float t = Time.time;
            while ((_thief == null || !_thief.IsTalkable) && Time.time - t < 5f) yield return null;
            if (_thief != null)
            {
                Stand(_thief.transform.position + _thief.transform.forward * 1.5f);
                _talks.Add((_thief, me));
            }
            yield return WaitBeat("3.3", 40f);
            Check("3.2 the THIEF talks, then on to the guns", _beat.Value == "3.3");
            Check("3.2 everyone is disguised", combat.IsDisguised);

            ShopCounter stall = GunStall;
            Check("3.3 the gun stall is open", stall != null && stall.CanInteract(me));
            if (stall != null) Stand(stall.HandOverPoint + Vector3.back * 1.5f);
            yield return new WaitForSeconds(1f);
            foreach (string gun in new[] { "Pistol", "Shotgun" })
            {
                DevCommands.Execute("spawn " + gun);
                yield return new WaitForSeconds(0.8f);
                DevCommands.Execute("grab " + gun);
                yield return new WaitForSeconds(0.8f);
                me.Hands.HeldItem?.GetComponent<Weapon>()?.ServerFitByName("suppressor");
                yield return new WaitForSeconds(0.3f);
            }
            yield return WaitBeat("3.4", 10f);
            Check("3.3 a suppressed pistol and shotgun moves on", _beat.Value == "3.4", $"guns {TeamGuns()}");

            Transform floor = GameObject.Find("Environment/Island3/PirateTavern/RoomFloor")?.transform;
            if (floor != null) Stand(floor.position);
            t = Time.time;
            StoryNpc champ = null;
            while (champ == null && Time.time - t < 20f)
            {
                foreach (StoryNpc n in StoryNpc.All)
                    if (n.Name == "Iron Jaw Jack" && n.IsFighting) champ = n;
                yield return null;
            }
            Check("3.4 the cage fighter steps up", champ != null);
            for (int i = 0; champ != null && !champ.IsDefeated && i < 40; i++)
            {
                ((IDamageable)champ).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, champ.HeadPosition, me.Head.forward);
                combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
                yield return new WaitForSeconds(0.3f);
            }
            yield return WaitBeat("3.5", 25f);
            Check("3.4 beating him moves on", _beat.Value == "3.5");

            Transform jungle = Island3Point("JungleStart"), bankA = Island3Point("RiverBankA"), bankB = Island3Point("RiverBankB");
            if (jungle != null) Stand(jungle.position);
            yield return new WaitForSeconds(2f);
            int monkeys = 0, guards = 0;
            foreach (Creature c in Creature.All) if (c.Kind == CreatureKind.Monkey) monkeys++;
            foreach (StoryNpc n in StoryNpc.All) if (n.IsGuarding) guards++;
            Check("3.5 jungle guards and monkeys", monkeys >= 3 && guards >= 3, $"{guards} guards, {monkeys} monkeys");
            if (bankA != null) Stand(bankA.position);
            yield return WaitBeat("3.6", 8f);
            Check("3.5 reaching the river moves on", _beat.Value == "3.6");

            yield return new WaitForSeconds(1f);
            if (bankA != null) Stand(bankA.position);
            yield return new WaitForSeconds(0.5f);
            DevCommands.Execute("swing");
            yield return new WaitForSeconds(2.5f);
            Check("3.6 the rope swing lands on the far bank", bankB != null && Vector3.Distance(me.transform.position, bankB.position) < 4f,
                bankB != null ? $"{Vector3.Distance(me.transform.position, bankB.position):F1} m off" : "");
            yield return WaitBeat("3.7", 8f);
            Check("3.6 across the river moves on", _beat.Value == "3.7");

            Transform pond = Island3Point("SnapperPond"), up = Island3Point("UpRiver");
            if (up != null) Stand(up.position);
            t = Time.time;
            Creature boss = null;
            while (boss == null && Time.time - t < 10f)
            {
                foreach (Creature c in Creature.All)
                    if (c.Kind == CreatureKind.OldSnapper && !c.IsDead) boss = c;
                yield return null;
            }
            Check("3.7 Old Snapper waits at the pond", boss != null && pond != null && Vector3.Distance(boss.transform.position, pond.position) < 8f);
            if (pond != null && up != null) Stand(pond.position + (up.position - pond.position).normalized * 12f); // close enough to wake it
            t = Time.time;
            while (boss != null && boss.IsDormant && Time.time - t < 20f) yield return null; // asleep until the fight starts
            Check("3.7 it wakes when you come for it", boss != null && !boss.IsDormant);
            Transform head = null;
            if (boss != null)
                foreach (CreatureZone z in boss.GetComponentsInChildren<CreatureZone>())
                    if (z.Part == "Head") head = z.transform;
            for (int i = 0; boss != null && !boss.IsDead && head != null && i < 400; i++)
                ((IDamageable)boss).ServerTakeHit(60, DamageKind.Bullet, me, head.position, me.Head.forward);
            yield return WaitBeat("3.8", 15f);
            Check("3.7 beating Old Snapper moves on", _beat.Value == "3.8");

            Transform mouth = Island3Point("CaveMouth"), ledge = Island3Point("CaveLedge");
            if (mouth != null) Stand(mouth.position);
            yield return new WaitForSeconds(1.5f);
            if (ledge != null) Stand(ledge.position);
            t = Time.time;
            while (_chapter.Value != "End of chapter 3" && Time.time - t < 20f) yield return null;
            Check("3.8 the gold vault ends the chapter", _chapter.Value == "End of chapter 3");
            Debug.Log($"[Check] DONE: {(_checkFails == 0 ? "ALL PASS" : _checkFails + " FAILED")}");
        }

        // ------------------------------------------------------------------ 3.1 sneak in (at night)

        /// <summary>The night arrival's places (GameSceneBuilder.Island3Night): the dock under the crags, the lookout...</summary>
        private static Transform NightPoint(string name) => GameObject.Find("Environment/Island3/NightArrival/" + name)?.transform;

        /// <summary>Where the team lands: the little dock on the mountain side (the old pirate dock if it isn't built).</summary>
        private static Vector3 NightDock => NightPoint("Dock") is { } dock ? dock.position : Island3Arrival;

        private static readonly string[] SleeperNames = { "Snorin' Sam", "Drowsy Dan", "Old Wheeze", "Barnacle Bess", "Nodding Ned", "Grumbles", "Lazy Lars", "Mumbling Mick" };

        // Sneaking in is run like a boss fight (How to Fish): introduced, a bar at the bottom that empties as you close
        // in on the THIEF's shack, a clock. Seen, too slow or everyone down: back to the landing, the lookouts reset.
        private const float SneakInSeconds = 300f, ShackReach = 3.5f;

        private IEnumerator SkullCove()
        {
            SetChapter("Chapter 3: Skull Cove");
            EnsureOnIsland3(NightDock); // the sleepers and the watch are there already: they came on the island as you set sail
            DisguiseEveryone(false);
            TitleObservers("CHAPTER 3", "Skull Cove, by night");
            yield return new WaitForSeconds(2f);
            yield return Say(_sandy, "(on the radio) Skull Cove. You came in round the back, under the mountain. Nobody watches this side.");
            Transform lookout = NightPoint("Lookout"); // the stair tower was taken out (Docs/18 C1): then straight to the sneak
            if (lookout != null)
            {
                yield return Say(_sandy, "(on the radio) See that old pirate tower against the cliff? Climb it. From up there you'll see the whole village.");
                SetObjective("Climb the lookout tower against the cliff");
                Marker("Lookout", lookout.position + Vector3.up * 1.5f);
                yield return Reach(lookout.position, 4f);
                NoMarker();
                yield return Say(_sandy, "(on the radio) Look down there. Lanterns in the lane: two on watch. And round the campfire... asleep. Half the village is.");
                yield return Say(_sandy, "(on the radio) Sleepers can't see you, but they hear. Don't run near them, and don't walk right past their ears.");
            }
            Transform thief = Island3Point("Thief");
            if (thief == null) yield break;
            Marker("THIEF's shack", thief.position + Vector3.up * 1.5f);
            yield return Say(_sandy, "(on the radio) The THIEF's shack is the last one, past the square. Go quietly. If anyone sees you, it's over.");
            // Run like a boss fight (How to Fish): the card and the sting, then the bar empties as you close in.
            // Seen, too slow or everyone down: back to the foot of the tower, the sleepers and the watch reset.
            Transform foot = NightPoint("TowerFoot");
            Vector3 start = foot != null ? foot.position : NightDock;
            BossIntroObservers("SKULL COVE", "Sleepers hear you. Lanterns see you. Get to the THIEF without waking the village.");
            yield return new WaitForSeconds(1.5f);
            yield return RunStealthMission("Sneak in", start, () => ArrivalSneak(start, thief.position), () => ResetResidents("I3Lookouts"));
        }

        private IEnumerator ArrivalSneak(Vector3 landing, Vector3 goal)
        {
            float from = Mathf.Max(ShackReach + 1f, Flat(landing, goal));
            float started = Time.time;
            Marker("THIEF's shack", goal + Vector3.up * 1.5f);
            while (true)
            {
                float nearest = float.MaxValue;
                foreach (PlayerHub p in PlayerHub.All)
                    if (p != null) nearest = Mathf.Min(nearest, Flat(p.transform.position, goal));
                if (nearest <= ShackReach) break;
                float left = SneakInSeconds - (Time.time - started);
                if (left <= 0f)
                {
                    ServerFailMission("Too slow: the pirates changed the watch");
                    yield break;
                }
                ServerBossBar("Skull Cove's lookouts", (nearest - ShackReach) / (from - ShackReach), left, SneakInSeconds);
                MissionObjective("Sneak to the THIEF's shack at the far end of the village. Walk, don't run; keep clear of the sleepers and the lanterns.");
                yield return new WaitForSeconds(0.2f);
            }
            HideBossBar();
            NoMarker();
            // The THIEF lives here: at his door the disguises go straight on.
            DisguiseEveryone(true);
            TitleObservers("IN", "Nobody woke up. The THIEF throws you hats and eye patches: you're pirates now");
            yield return ThiefSays("Psst! Lifeguards?! Quick, put those on! Pirates take you for crew in them. Parrots too.");
            yield return new WaitForSeconds(0.5f);
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>A guard on the night watch: walks his beat with a lantern and sees less far than by day.</summary>
        private StoryNpc NightGuard(string name, int look, Vector3 a, Vector3 b)
        {
            StoryNpc guard = SpawnNpc(name, NpcRole.Pirate, PirateLook(look), a, 0f, 100);
            guard.ServerGuard(new[] { a, b }, FighterProfile.Pirate(NpcWeaponKind.Pistol), StoryNpc.NightGuard);
            guard.ServerSetLantern(true);
            return guard;
        }

        /// <summary>A pirate asleep at one of the night arrival's spots (round the campfire, by the porches).</summary>
        private StoryNpc Sleeper(int i, Transform spot)
        {
            StoryNpc sleeper = SpawnNpc(SleeperNames[i % SleeperNames.Length], NpcRole.Pirate, PirateLook(i + 3), spot.position, spot.eulerAngles.y, 100);
            sleeper.ServerTeleport(spot.position, spot.eulerAngles.y, keepExact: true);
            sleeper.ServerSleep(FighterProfile.Pirate(i % 2 == 0 ? NpcWeaponKind.Cutlass : NpcWeaponKind.Fists));
            return sleeper;
        }

        // ------------------------------------------------------------------ 3.2 the THIEF

        private IEnumerator TheThief()
        {
            EnsureOnIsland3(); // he lives on his porch (Skull Cove's residents)
            if (_thief == null || !_thief.IsSpawned) yield break;
            DisguiseEveryone(true); // a resumed save: he handed the disguises over at the door
            SetObjective("Talk to the THIEF");
            Marker("THIEF", _thief.NetworkObject);
            yield return WaitTalk(_thief, "Talk to the THIEF");
            _thief.ServerSetTalkable(false);
            NoMarker();
            _thief.ServerSetMood(AvatarMood.Scared);
            yield return Say(_thief, "YOU?! The lifeguards from the tsunami... Fine. I owe you. You pulled me out of that wave.");
            _thief.ServerSetMood(AvatarMood.Neutral);
            yield return Say(_thief, "These pirates robbed the hotel's casino. The money's inside the mountain, behind the crag falls.");
            yield return Say(_thief, "Keep those hats on. To the pirates you're crew now, and the parrots won't look twice.");
            yield return Say(_thief, "One more thing. Buy guns. SUPPRESSED guns. A bang on this island and every pirate comes running.");
            yield return Say(_thief, "The stall down by the pirates' dock sells them. A pistol AND a shotgun, and a silencer on each. Trust me.");
        }

        // ------------------------------------------------------------------ 3.3 the gun stall

        private static (bool pistol, bool shotgun, int loud) TeamGuns()
        {
            bool pistol = false, shotgun = false;
            int loud = 0;
            foreach (Item item in Item.All)
            {
                if (!item.IsHeld || !item.TryGetComponent(out Weapon w)) continue;
                if (item.DisplayName == "Pistol") pistol = true;
                if (item.DisplayName == "Shotgun") shotgun = true;
                if (!w.Barrel.Suppressed) loud++;
            }
            return (pistol, shotgun, loud);
        }

        private IEnumerator GunStallBeat()
        {
            EnsureOnIsland3();
            DisguiseEveryone(true);
            ShopCounter stall = GunStall;
            if (stall != null) stall.ServerSetAvailable(true);
            // No pay on this island (you're undercover): the THIEF tops the wallet up so nobody is stuck.
            const int need = 250 + 500 + 250 + 250;
            if (Economy.Money < need && Economy.Instance != null)
            {
                yield return ThiefSays("Short on money? Here. My savings. Don't ask where they came from.");
                Economy.Instance.ServerAdd(need - Economy.Money, "the THIEF's savings", stall != null ? stall.HandOverPoint : Island3Arrival);
            }
            while (true)
            {
                (bool pistol, bool shotgun, int loud) = TeamGuns();
                int done = (pistol ? 1 : 0) + (shotgun ? 1 : 0) + (pistol && shotgun && loud == 0 ? 1 : 0);
                string line = !pistol || !shotgun
                    ? $"Buy a {(pistol ? "" : "pistol")}{(!pistol && !shotgun ? " and a " : "")}{(shotgun ? "" : "shotgun")} at the gun stall by the dock"
                    : $"Fit a suppressor on every gun ({loud} still loud): hold the gun at the stall and buy one";
                SetObjective(line, done, 3);
                if (stall != null) Marker("Gun stall", stall.HandOverPoint);
                if (pistol && shotgun && loud == 0) break;
                yield return new WaitForSeconds(0.4f);
            }
            NoMarker();
            yield return You("Armed. And quiet.");
        }

        // ------------------------------------------------------------------ 3.4 the cage fight

        private IEnumerator CageFight()
        {
            EnsureOnIsland3();
            Transform floor = GameObject.Find("Environment/Island3/PirateTavern/RoomFloor")?.transform;
            if (floor == null) yield break;
            SetObjective("The tavern. Somebody there knows the way to the money.");
            Marker("Tavern", floor.position + Vector3.up * 2f);
            yield return Reach(floor.position, 7f);
            NoMarker();
            PlayerHub first = StoryNpc.NearestPlayer(floor.position, 8f);
            Vector3 start = first != null ? first.transform.position : floor.position;
            yield return RunMission("Cage fight", start, CageFightBody, () => ResetResidents("I3Tavern"));
        }

        private IEnumerator CageFightBody()
        {
            // The tavern's champion has been in there all along (Skull Cove's residents): he steps up.
            List<StoryNpc> jack = ResidentsOf<StoryNpc>("I3Tavern");
            if (jack.Count == 0) yield break;
            StoryNpc champ = jack[0];
            yield return Say(champ, "Fresh meat! Nobody asks about the treasure without going a round with Iron Jaw Jack.");
            BossIntroObservers("IRON JAW JACK", "Fists only. He guards: hit him after he swings.");
            champ.ServerFight(FighterProfile.Brawler(0.75f), champ.transform.position);
            while (!champ.IsDefeated)
            {
                MissionObjective("Beat Iron Jaw Jack. Step back from his swings, then hit him.");
                yield return new WaitForSeconds(0.25f);
            }
            champ.ServerStopFighting();
            yield return new WaitForSeconds(1.5f);
            yield return Say(champ, "Hrrk... you hit like a kraken. Fine. The gold? In the mountain, behind the falls. Through the jungle, over the river...");
            yield return Say(champ, "...to old Snapper's pond under the crag falls. The gold's behind those falls, inside the mountain. Don't wake the turtle.");
            // He stays in his tavern, nursing his jaw.
        }

        // ------------------------------------------------------------------ 3.5 the jungle

        private IEnumerator Jungle()
        {
            EnsureOnIsland3();
            Transform start = Island3Point("JungleStart"), bank = Island3Point("RiverBankA");
            if (start == null || bank == null) yield break;
            SetObjective("Through the jungle to the river");
            Marker("Jungle", start.position + Vector3.up * 2f);
            yield return Reach(start.position, 10f);
            NoMarker();
            yield return RunStealthMission("The jungle", start.position + Vector3.up * 0.3f, () => JungleBody(bank.position), () => ResetResidents("I3Jungle"));
        }

        private IEnumerator JungleBody(Vector3 bank)
        {
            // Its guards and monkeys live there (Skull Cove's residents); a lost hat comes back with the next try.
            DisguiseEveryone(true);
            Marker("River", bank + Vector3.up * 1.5f);
            MissionObjective("Reach the river. Keep your hat on: the pirates know a lifeguard's face. A loud shot and you're caught.");
            yield return Reach(bank, 4.5f);
            NoMarker();
        }

        // ------------------------------------------------------------------ 3.6 the river

        private IEnumerator River()
        {
            EnsureOnIsland3();
            Transform a = Island3Point("RiverBankA"), b = Island3Point("RiverBankB");
            if (a == null || b == null) yield break;
            yield return RunMission("The river", a.position, () => RiverBody(b.position), () => ResetResidents("I3River"));
        }

        private IEnumerator RiverBody(Vector3 far)
        {
            // The alligators live in the river (Skull Cove's residents).
            Marker("Rope swing", far + Vector3.up * 2f);
            MissionObjective("Swing across on the rope (look at it, press E). The river's full of alligators.");
            yield return Reach(far, 4f);
            NoMarker();
            yield return You("Not today, lizards.");
        }

        // ------------------------------------------------------------------ 3.7 Old Snapper

        private IEnumerator SnapperBeat()
        {
            EnsureOnIsland3();
            Transform pond = Island3Point("SnapperPond"), up = Island3Point("UpRiver");
            if (pond == null) yield break;
            SetObjective("Upriver to the crag falls");
            Marker("The falls", pond.position + Vector3.up * 3f);
            // It has been lying in its pond all along, low in the water like a rock (Skull Cove's residents).
            yield return Reach(pond.position, 22f);
            NoMarker();
            Vector3 start = up != null ? up.position : StoryNpc.NearestPlayer(pond.position, 30f)?.transform.position ?? pond.position;
            yield return RunMission("Old Snapper", start, SnapperBody, () => ResetResidents("I3Snapper"));
        }

        private IEnumerator SnapperBody()
        {
            List<Creature> found = ResidentsOf<Creature>("I3Snapper");
            if (found.Count == 0) yield break;
            Creature boss = found[0];
            boss.ServerWake(); // it rises out of the pond...
            BossIntroObservers("OLD SNAPPER", "Its shell stops bullets. Hit the head when it gasps after a bite.");
            while (!boss.IsDead)
            {
                MissionObjective($"Beat Old Snapper (phase {boss.BossPhase}/{boss.BossPhases}). Bullets bounce off the shell: shoot the head when it gasps.");
                yield return new WaitForSeconds(0.25f);
            }
            TitleObservers("OLD SNAPPER IS BEATEN", "The way into the mountain is clear");
            yield return new WaitForSeconds(3f);
            ForgetResidents("I3Snapper"); // beaten for good: not back after a reload of this chapter
            StartCoroutine(RemoveLater(boss, 40f));
        }

        // ------------------------------------------------------------------ 3.8 the gold vault

        private IEnumerator MoneyCave()
        {
            EnsureOnIsland3();
            Transform mouth = Island3Point("CaveMouth"), ledge = Island3Point("CaveLedge");
            if (mouth == null || ledge == null) yield break;
            SetObjective("The gold: swim in behind the crag falls, into the mountain");
            Marker("Behind the falls", mouth.position + Vector3.up * 3f);
            yield return Reach(mouth.position, 10f);
            Marker("The gold", ledge.position + Vector3.up * 1.5f);
            SetObjective("Up the ramp and through the tunnel to the heart of the mountain");
            yield return Reach(ledge.position, 6f);
            NoMarker();
            TitleObservers("THE PIRATES' GOLD", "A mountain of it. And the casino's money: every dollar.");
            yield return new WaitForSeconds(2f);
            yield return ThiefSays("You found it?! ...I mean: well done. Leave a sack for the THIEF?");
            SetChapter("End of chapter 3");
            SetObjective("To be continued...");
            yield return ChapterReport("CHAPTER 3 COMPLETE");
        }
    }
}
