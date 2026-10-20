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

            if (CageRingCentre() is { } ringAt) Stand(ringAt + Vector3.back * 1.5f);
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
            yield return WaitBeat("3.7b", 15f);
            Check("3.7 beating Old Snapper moves on", _beat.Value == "3.7b");

            // 3.7b: into the sewer, up the ladder into the castle, the Pirate King's fight in the throne room.
            Transform sewer = Island3Point("SewerEntrance"), throne = Island3Point("PalaceThrone");
            Check("3.7b the sewer and the throne room exist", sewer != null && throne != null);
            if (sewer != null) Stand(sewer.position);
            yield return new WaitForSeconds(1.5f);
            Check("3.7b the sewer marker moves on to the palace", _markerLabel.Value == "The palace");
            if (throne != null) Stand(throne.position + throne.forward * 6f);
            t = Time.time;
            while (!MissionRunning && Time.time - t < 10f) yield return null;
            StoryNpc king = null;
            foreach (StoryNpc n in StoryNpc.All) if (n != null && n.Name == "The Pirate King") king = n;
            Check("3.7b the Pirate King is in his throne room", king != null && throne != null && (king.transform.position - throne.position).magnitude < 4f);
            Check("3.7b he wears his own model", king != null && king.Look.Body == AvatarLook.Bodies.PirateKing);
            for (int i = 0; king != null && !king.IsDefeated && i < 40; i++)
            {
                ((IDamageable)king).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, king.HeadPosition, me.Head.forward);
                yield return new WaitForSeconds(0.2f);
            }
            yield return WaitBeat("3.8", 25f);
            Check("3.7b beating the Pirate King moves on", _beat.Value == "3.8");

            Transform mouth = Island3Point("CaveMouth"), ledge = Island3Point("CaveLedge");
            if (mouth != null) Stand(mouth.position);
            yield return new WaitForSeconds(1.5f);
            if (ledge != null) Stand(ledge.position);
            yield return WaitBeat("3.9", 20f);
            Check("3.8 reaching the gold moves on", _beat.Value == "3.9");

            // 3.9: the four sacks in the vault, carried to the boat (here: carried by the test, put on the deck).
            yield return new WaitForSeconds(1f);
            Check("3.9 four money sacks in the vault", _sacks.Count == GoldSacks && ledge != null &&
                _sacks.TrueForAll(s => s != null && (s.transform.position - ledge.position).magnitude < 6f), $"{_sacks.Count}");
            Check("3.9 the pirate boat waits at the night dock", _pirateBoat != null && (_pirateBoat.transform.position - NightBerth).magnitude < 4f);
            Check("3.9 the THIEF minds the boat", _thief != null && _thief.IsSpawned && (_thief.transform.position - NightDock).magnitude < 6f);
            if (_sacks.Count > 0 && _sacks[0] != null)
            {
                Item first = _sacks[0];
                Stand(first.transform.position + Vector3.back * 0.8f);
                yield return new WaitForSeconds(0.5f);
                DevCommands.Execute("grab Money Sack"); // (as a player would: the nearest one)
                t = Time.time;
                while (!_sacks.Exists(s => s != null && s.IsHeld) && Time.time - t < 3f) yield return null;
                yield return new WaitForSeconds(0.5f); // (the host confirms the pickup before we run off with it)
                Check("3.9 a sack can be picked up", _sacks.Exists(s => s != null && s.IsHeld));
                Stand(NightDock);
                yield return new WaitForSeconds(1.2f);
                Check("3.9 carrying one, the marker shows the boat", _markerLabel.Value == "Boat", _markerLabel.Value);
                me.Hands.Drop();
                yield return new WaitForSeconds(0.5f);
            }
            int before = _cargo.Value;
            foreach (Item sack in _sacks.ToArray())
            {
                if (sack == null || sack.IsHeld) continue;
                Vector3 deck = _pirateBoat.transform.TransformPoint(new Vector3(0f, 1.6f, 1.2f));
                sack.Sync.Body.position = deck;
                sack.transform.position = deck;
                sack.Sync.Body.linearVelocity = Vector3.zero;
                yield return new WaitForSeconds(0.7f);
            }
            yield return WaitBeat("3.10", 25f);
            Check("3.9 sacks on the deck are stowed, four moves on", _beat.Value == "3.10" && before < GoldSacks, $"cargo {_cargo.Value}");

            // 3.10: sail home (the test puts the boat off the hotel beach), the THIEF and a sack are gone, Marisol counts.
            yield return new WaitForSeconds(1f);
            Check("3.10 the THIEF rides on the boat", _thief != null && _thief.IsSpawned && (_thief.transform.position - _pirateBoat.transform.position).magnitude < 4f);
            Vector3 stop = _pirateLanding.position + _pirateLanding.forward * 13f;
            int money = Economy.Money;
            _pirateBoat.ServerPlace(stop + _pirateLanding.forward * 30f, _pirateLanding.eulerAngles.y + 180f);
            yield return new WaitForSeconds(0.5f);
            _pirateBoat.ServerPlace(stop, _pirateLanding.eulerAngles.y);
            Stand(_pirateLanding.position);
            t = Time.time;
            while (_markerLabel.Value != "Marisol" && _markerLabel.Value != "On the deck" && Time.time - t < 40f) yield return null;
            Check("3.10 the THIEF slipped away with one sack", (_thief == null || !_thief.IsSpawned) && _cargo.Value == GoldSacks - 1, $"cargo {_cargo.Value}");
            Check("3.10 Marisol comes down to count it", _receptionist != null && (_receptionist.transform.position - _pirateLanding.position).magnitude < 8f);
            t = Time.time;
            while (_markerLabel.Value != "On the deck" && Time.time - t < 60f) yield return null;
            Check("3.10 the team gets a finder's fee", Economy.Money >= money + FindersFee, $"${Economy.Money - money}");
            Item tag = null;
            foreach (Item i in Item.All) if (i.DisplayName == "Lost & Found Tag") tag = i;
            Check("3.10 Sandy's tag lies on the deck", tag != null && (tag.transform.position - _pirateBoat.transform.position).magnitude < 4f);
            if (tag != null) Stand(tag.transform.position + Vector3.back * 0.5f);
            t = Time.time;
            while (_chapter.Value != "End of chapter 3" && Time.time - t < 60f) yield return null;
            Check("3.10 finding the tag ends the chapter", _chapter.Value == "End of chapter 3");
            Debug.Log($"[Check] DONE: {(_checkFails == 0 ? "ALL PASS" : _checkFails + " FAILED")}");
        }

        // ------------------------------------------------------------------ 3.1 sneak in (at night)

        /// <summary>The night arrival's places (GameSceneBuilder.Island3Night): the dock under the crags, the lookout...</summary>
        private static Transform NightPoint(string name) => GameObject.Find("Environment/Island3/NightArrival/" + name)?.transform;

        /// <summary>Where the team lands: the little dock on the mountain side (the old pirate dock if it isn't built).</summary>
        private static Vector3 NightDock => NightPoint("Dock") is { } dock ? dock.position : Island3Arrival;

        private static readonly string[] TavernRegulars = { "One-Tooth Tess", "Bilge Bob", "Rum Rita", "Patch", "Salty Sven" };
        private static readonly string[] TavernCheers = { "HIT 'IM!", "Jack! Jack! Jack!", "Two doubloons on the big one!", "Ooooh!", "Get up, ya landlubber!", "FIGHT! FIGHT!" };
        private static readonly string[] DrunkSongs = { "Yo ho ho... hic!", "Fifteen men on a dead man's... where was I?", "Who moved the island?", "Hic! Lovely night for it.", "The moon's got two of it..." };

        /// <summary>A tavern regular round the ring (I3Tavern): standing about with a mug, facing the fight.</summary>
        private StoryNpc TavernRegular(int i, Vector3 ring)
        {
            float a = (i * 72f + 20f) * Mathf.Deg2Rad;
            Vector3 at = ring + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3.4f;
            StoryNpc npc = SpawnNpc(TavernRegulars[i], NpcRole.Pirate, PirateLook(i + 5), at, 0f, 100);
            npc.ServerFace(ring);
            npc.ServerSetMood(AvatarMood.Happy);
            return npc;
        }

        /// <summary>The village drunk (I3Locals): staggers from the campfire to the tavern and back, singing.</summary>
        private StoryNpc Drunk(Vector3 fire, Vector3 tavern)
        {
            StoryNpc npc = SpawnNpc("Wobbly Will", NpcRole.Pirate, PirateLook(9), fire + Vector3.right * 3f, 0f, 100);
            npc.ServerSetDrunk(true);
            StartCoroutine(Stagger(npc, fire + Vector3.right * 3f, tavern + Vector3.back * 4f));
            return npc;
        }

        private System.Collections.IEnumerator Stagger(StoryNpc npc, Vector3 a, Vector3 b)
        {
            bool toB = true;
            while (npc != null && !npc.IsDefeated)
            {
                if (!npc.IsFighting)
                {
                    Vector3 target = (toB ? b : a) + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));
                    npc.ServerMoveTo(target, 1.1f);
                    if (Random.value < 0.6f) npc.ServerShout(DrunkSongs[Random.Range(0, DrunkSongs.Length)]);
                    toB = !toB;
                }
                yield return new WaitForSeconds(Random.Range(9f, 14f));
            }
        }

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

        /// <summary>The middle of the cage-fight ring (C7's Octagon, on the grass beside the tavern), or in the tavern
        /// before it existed.</summary>
        public static Vector3? CageRingCentre()
        {
            Transform ring = GameObject.Find("Environment/Island3/Octagon")?.transform;
            if (ring != null) return ring.position;
            Transform floor = GameObject.Find("Environment/Island3/PirateTavern/RoomFloor")?.transform;
            return floor != null ? floor.position + Vector3.forward * 2f : null;
        }

        private IEnumerator CageFight()
        {
            EnsureOnIsland3();
            if (CageRingCentre() is not { } ring) yield break;
            SetObjective("The fighting ring by the tavern. Somebody there knows the way to the money.");
            Marker("Ring", ring + Vector3.up * 2f);
            yield return Reach(ring, 7f);
            NoMarker();
            PlayerHub first = StoryNpc.NearestPlayer(ring, 8f);
            Vector3 start = first != null ? first.transform.position : ring;
            yield return RunMission("Cage fight", start, CageFightBody, () => ResetResidents("I3Tavern"));
        }

        private IEnumerator CageFightBody()
        {
            // The tavern's champion has been in there all along (Skull Cove's residents): he steps up.
            StoryNpc champ = ResidentsOf<StoryNpc>("I3Tavern").Find(n => n.Name == "Iron Jaw Jack");
            if (champ == null) yield break;
            yield return Say(champ, "Fresh meat! Nobody asks about the treasure without going a round with Iron Jaw Jack.");
            BossIntroObservers("IRON JAW JACK", "Fists only. LMB jab, RMB hook, crouch + LMB uppercut. Jab, jab, hook: combo.");
            champ.ServerFight(FighterProfile.Brawler(0.75f), champ.transform.position);
            float nextCheer = 0f;
            while (!champ.IsDefeated)
            {
                if (Time.time > nextCheer)
                {
                    // The regulars cheer the fight on.
                    nextCheer = Time.time + Random.Range(2f, 3.5f);
                    List<StoryNpc> crowd = ResidentsOf<StoryNpc>("I3Tavern");
                    crowd.Remove(champ);
                    if (crowd.Count > 0)
                    {
                        StoryNpc fan = crowd[Random.Range(0, crowd.Count)];
                        fan.ServerShout(TavernCheers[Random.Range(0, TavernCheers.Length)]);
                        fan.ServerFace(champ.transform.position);
                    }
                }
                MissionObjective("Beat Iron Jaw Jack: dodge his swings, then jab, jab, hook (RMB) or uppercut (crouch + LMB).");
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

        // ------------------------------------------------------------------ 3.7b the sewer, the palace and the Pirate King

        /// <summary>
        /// Behind the back waterfall a sewer runs to the palace; the Pirate King and his two guards are there. Beat him
        /// (fists or a blade put him down, he's needed alive) and he tells you where the gold is (3.8). The places are
        /// part C's: Environment/Island3/Story/SewerEntrance (the way in, behind the back waterfall) and PalaceThrone
        /// (in front of the throne, facing the room). Until they exist this beat is skipped.
        /// </summary>
        private IEnumerator PirateKingBeat()
        {
            EnsureOnIsland3();
            Transform sewer = GameObject.Find(Island3Story + "SewerEntrance")?.transform;
            Transform throne = GameObject.Find(Island3Story + "PalaceThrone")?.transform;
            if (sewer == null || throne == null)
            {
                Debug.Log("[Story] 3.7b skipped: no SewerEntrance / PalaceThrone on island 3 yet (part C builds them)");
                yield break;
            }
            if (ResidentsOf<StoryNpc>("I3Palace").Count == 0) // (the palace went up after the island was populated)
            {
                AddResident("I3Palace", () => PirateKing(throne).gameObject);
                for (int i = 0; i < 2; i++)
                {
                    int k = i;
                    AddResident("I3Palace", () => PalaceGuard(k, throne).gameObject);
                }
            }
            SetObjective("Behind the back waterfall: the sewer to the palace");
            Marker("The sewer", sewer.position + Vector3.up * 2f);
            yield return Reach(sewer.position, 4f);
            SetObjective("Follow the sewer up into the palace");
            Marker("The palace", throne.position + Vector3.up * 2f);
            yield return Reach(throne.position, 9f);
            NoMarker();
            PlayerHub first = StoryNpc.NearestPlayer(throne.position, 10f);
            yield return RunMission("The Pirate King", first != null ? first.transform.position : throne.position, PirateKingBody, () => ResetResidents("I3Palace"));
        }

        private IEnumerator PirateKingBody()
        {
            List<StoryNpc> court = ResidentsOf<StoryNpc>("I3Palace");
            StoryNpc king = court.Find(n => n.Name == "The Pirate King");
            if (king == null) yield break;
            yield return Say(king, "Lifeguards? In MY palace? Through the SEWER? Guards! ...Oh, forget it. I'll do it meself.");
            BossIntroObservers("THE PIRATE KING", "His guards first, or him: he's slow but he hits like a cannon.");
            king.ServerSetPose(Avatars.AvatarPose.Normal);
            foreach (StoryNpc n in court)
                n.ServerFight(n == king ? FighterProfile.Pirate(NpcWeaponKind.Cutlass) : FighterProfile.Pirate(NpcWeaponKind.Fists), n.transform.position);
            while (!king.IsDefeated)
            {
                MissionObjective("Beat the Pirate King (put him down: he knows where the gold is)");
                yield return new WaitForSeconds(0.25f);
            }
            foreach (StoryNpc n in court) n.ServerStopFighting();
            yield return new WaitForSeconds(1.5f);
            yield return Say(king, "Arr... enough! Ye want the casino's gold? It's not in me palace, ye fools.");
            yield return Say(king, "It's in the heart of the mountain. Behind the crag falls: swim in, up the ramp, through the tunnel.");
        }

        private StoryNpc PirateKing(Transform throne)
        {
            StoryNpc king = SpawnNpc("The Pirate King", NpcRole.Pirate, PirateLookOf(AvatarLook.Bodies.PirateKing), throne.position, throne.eulerAngles.y, 300);
            king.ServerSetMood(AvatarMood.Angry);
            return king;
        }

        private StoryNpc PalaceGuard(int i, Transform throne)
        {
            Vector3 at = throne.position + throne.right * (i == 0 ? -2.2f : 2.2f) + throne.forward * 1.2f;
            StoryNpc guard = SpawnNpc(i == 0 ? "Crown Guard Krusty" : "Crown Guard Moss", NpcRole.Pirate, PirateLook(i + 2), at, throne.eulerAngles.y, 120);
            return guard;
        }

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
            // On to 3.9: the casino's money sacks down to the pirate boat.
        }
    }
}
