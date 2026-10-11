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
    ///  3.1 sneak in past the parrots (seen = restart) · 3.2 the THIEF: disguises, "buy suppressed guns" ·
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
            Check("3.1 starts on Skull Cove", _beat.Value == "3.1" && (me.transform.position - Island3Arrival).sqrMagnitude < 40f * 40f);
            float waited = Time.time; // the radio lines, then the mission
            while (!MissionRunning && Time.time - waited < 25f) yield return null;
            yield return new WaitForSeconds(0.3f);
            Transform thief = Island3Point("Thief");
            int parrots = 0;
            foreach (Creature c in Creature.All) if (c.Kind == CreatureKind.Parrot && !c.IsDead) parrots++;
            Check("3.1 lookout parrots on their perches", parrots >= 4, $"{parrots}");
            int alarms = 0;
            void Alarmed(string who, PlayerHub whom, string why) => alarms++;
            Stealth.ServerSpotted += Alarmed;
            yield return new WaitForSeconds(12f); // standing at the landing: out of their sight
            Stealth.ServerSpotted -= Alarmed;
            Check("3.1 the landing is out of the lookouts' sight", alarms == 0, $"{alarms} alarms in 12 s");
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

        // ------------------------------------------------------------------ 3.1 sneak in

        private IEnumerator SkullCove()
        {
            SetChapter("Chapter 3: Skull Cove");
            EnsureOnIsland3();
            DisguiseEveryone(false);
            TitleObservers("CHAPTER 3", "Skull Cove");
            yield return new WaitForSeconds(2f);
            yield return Say(_sandy, "(on the radio) Skull Cove. The pirates' island. Their parrots squawk at any stranger they see.");
            yield return Say(_sandy, "(on the radio) Sneak in. Walk, don't run. Keep buildings and trees between you and those birds.");
            Transform thief = Island3Point("Thief");
            if (thief == null) yield break;
            Vector3 start = Island3Arrival;
            yield return RunStealthMission("Sneak in", start, () => ArrivalSneak(thief.position));
        }

        private IEnumerator ArrivalSneak(Vector3 goal)
        {
            int first = _spawnedActors.Count;
            for (int i = 0; i < 6; i++)
                if (Island3Point($"Perch_{i}") is { } perch) SpawnCreature(CreatureKind.Parrot, perch.position, perch.eulerAngles.y, "Lookout parrot")?.ServerLookout();
            if (Island3Point("GuardA_0") is { } a0 && Island3Point("GuardA_1") is { } a1)
                SpawnGuard("Bosun Barnaby", 0, a0.position, 0f, new[] { a0.position, a1.position });
            if (Island3Point("GuardB_0") is { } b0 && Island3Point("GuardB_1") is { } b1)
                SpawnGuard("Squint", 2, b1.position, 0f, new[] { b1.position, b0.position });
            Marker("THIEF's shack", goal + Vector3.up * 1.5f);
            MissionObjective("Sneak to the THIEF's shack at the end of the lane. Don't let the parrots see you.");
            yield return Reach(goal, 3.5f);
            CleanupActorsFrom(first); // in: the lookouts and their guards are done with
            NoMarker();
            TitleObservers("IN", "Nobody saw a thing");
            yield return new WaitForSeconds(1.5f);
        }

        // ------------------------------------------------------------------ 3.2 the THIEF

        private IEnumerator TheThief()
        {
            EnsureOnIsland3();
            Transform at = Island3Point("Thief");
            if (at == null) yield break;
            if (_thief == null || !_thief.IsSpawned)
                _thief = SpawnNpc("THIEF", NpcRole.Guide, RobberLook, at.position, at.eulerAngles.y + 180f);
            SetObjective("Talk to the THIEF");
            Marker("THIEF", _thief.NetworkObject);
            yield return WaitTalk(_thief, "Talk to the THIEF");
            _thief.ServerSetTalkable(false);
            NoMarker();
            _thief.ServerSetMood(AvatarMood.Scared);
            yield return Say(_thief, "YOU?! The lifeguards from the tsunami... Fine. I owe you. You pulled me out of that wave.");
            _thief.ServerSetMood(AvatarMood.Neutral);
            yield return Say(_thief, "These pirates robbed the hotel's casino. The money's in a cave behind the big waterfall, down south.");
            yield return Say(_thief, "Looking like THAT you won't last a minute. Here. Hats. Eye patches. Now you're pirates.");
            DisguiseEveryone(true);
            TitleObservers("DISGUISED", "Pirates take you for one of theirs. Parrots too.");
            yield return Say(_thief, "One more thing. Buy guns. SUPPRESSED guns. A bang on this island and every pirate comes running.");
            yield return Say(_thief, "The stall by the dock sells them. A pistol AND a shotgun, and a silencer on each. Trust me.");
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
            yield return RunMission("Cage fight", start, () => CageFightBody(floor.position));
        }

        private IEnumerator CageFightBody(Vector3 floor)
        {
            StoryNpc champ = SpawnNpc("Iron Jaw Jack", NpcRole.Pirate, PirateLookOf(AvatarLook.Bodies.PirateFat), floor + Vector3.forward * 2f, 180f, 160);
            yield return Say(champ, "Fresh meat! Nobody asks about the treasure without going a round with Iron Jaw Jack.");
            TitleObservers("CAGE FIGHT", "Fists only. He guards: hit him after he swings.");
            champ.ServerFight(FighterProfile.Brawler(0.75f), champ.transform.position);
            while (!champ.IsDefeated)
            {
                MissionObjective("Beat Iron Jaw Jack. Step back from his swings, then hit him.");
                yield return new WaitForSeconds(0.25f);
            }
            champ.ServerStopFighting();
            yield return new WaitForSeconds(1.5f);
            yield return Say(champ, "Hrrk... you hit like a kraken. Fine. The gold? In the mountain, behind the falls. Through the jungle, over the river...");
            yield return Say(champ, "...past old Snapper's pond under the crag falls, then the big falls on the south cliffs. Don't wake the turtle.");
            StartCoroutine(RemoveLater(champ, 30f));
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
            yield return RunStealthMission("The jungle", start.position + Vector3.up * 0.3f, () => JungleBody(bank.position));
        }

        private IEnumerator JungleBody(Vector3 bank)
        {
            int first = _spawnedActors.Count;
            DisguiseEveryone(true);
            string[] names = { "Mangy Mo", "Long Tom", "Grog" };
            for (int i = 0; i < 3; i++)
            {
                if (Island3Point($"JungleGuard_{i}_0") is { } p0 && Island3Point($"JungleGuard_{i}_1") is { } p1)
                    SpawnGuard(names[i], i + 1, p0.position, 0f, new[] { p0.position, p1.position });
                if (Island3Point($"MonkeyHome_{i}") is { } home) SpawnCreature(CreatureKind.Monkey, home.position, 0f, "Monkey");
            }
            Marker("River", bank + Vector3.up * 1.5f);
            MissionObjective("Reach the river. Keep your hat on: the pirates know a lifeguard's face. A loud shot and you're caught.");
            yield return Reach(bank, 4.5f);
            CleanupActorsFrom(first);
            NoMarker();
        }

        // ------------------------------------------------------------------ 3.6 the river

        private IEnumerator River()
        {
            EnsureOnIsland3();
            Transform a = Island3Point("RiverBankA"), b = Island3Point("RiverBankB");
            if (a == null || b == null) yield break;
            yield return RunMission("The river", a.position, () => RiverBody(b.position));
        }

        private IEnumerator RiverBody(Vector3 far)
        {
            int first = _spawnedActors.Count;
            for (int i = 0; i < 2; i++)
                if (Island3Point($"Gator_{i}") is { } g) SpawnCreature(CreatureKind.Alligator, g.position, Random.Range(0f, 360f), "Alligator");
            Marker("Rope swing", far + Vector3.up * 2f);
            MissionObjective("Swing across on the rope (look at it, press E). The river's full of alligators.");
            yield return Reach(far, 4f);
            CleanupActorsFrom(first);
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
            yield return Reach(pond.position, 22f);
            NoMarker();
            Vector3 start = up != null ? up.position : StoryNpc.NearestPlayer(pond.position, 30f)?.transform.position ?? pond.position;
            yield return RunMission("Old Snapper", start, () => SnapperBody(pond.position, pond.eulerAngles.y));
        }

        private IEnumerator SnapperBody(Vector3 at, float yaw)
        {
            Creature boss = SpawnCreature(CreatureKind.OldSnapper, at, yaw, "Old Snapper");
            if (boss == null) yield break;
            TitleObservers("OLD SNAPPER", "Its shell stops bullets. Hit the head when it gasps after a bite.");
            while (!boss.IsDead)
            {
                MissionObjective($"Beat Old Snapper (phase {boss.BossPhase}/{boss.BossPhases}). Bullets bounce off the shell: shoot the head when it gasps.");
                yield return new WaitForSeconds(0.25f);
            }
            TitleObservers("OLD SNAPPER IS BEATEN", "The way south is clear");
            yield return new WaitForSeconds(3f);
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
