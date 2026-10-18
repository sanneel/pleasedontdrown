using System.Collections;
using System.Collections.Generic;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Stealth, creatures and bosses in the story (host): spawning creatures (Resources/Creature), failing a stealth
    /// mission when a sentry raises the alarm, a pirate knocked into the water when a boss changes phase, and the test
    /// missions and checks for the console (stealthtest, creature, bosstest, disguise, threatcheck).
    /// </summary>
    public partial class StoryDirector
    {
        private Creature _creaturePrefab;
        // While true, a sentry raising the alarm fails the running mission ("A parrot saw you!").
        private bool _failOnSpotted;

        private void StartThreats()
        {
            Stealth.ServerSpotted += OnSpotted;
            Creature.ServerBossPhase += OnBossPhase;
        }

        private void StopThreats()
        {
            Stealth.ServerSpotted -= OnSpotted;
            Creature.ServerBossPhase -= OnBossPhase;
            _failOnSpotted = false;
        }

        private void OnSpotted(string sentry, PlayerHub who, string reason)
        {
            if (!_failOnSpotted || !MissionRunning) return;
            string you = who != null && PlayerHub.All.Count > 1 ? who.DisplayName : "you";
            ServerFailMission($"{sentry} {reason.Replace("you", you)}!");
        }

        /// <summary>Host: a boss's new phase. Old Snapper's second knocks a pirate guard into the water: rescue him.</summary>
        private void OnBossPhase(Creature boss, int phase)
        {
            if (boss.Kind != CreatureKind.OldSnapper || phase != 2) return;
            StartCoroutine(PirateOverboard(boss.transform.position));
        }

        private IEnumerator PirateOverboard(Vector3 near)
        {
            yield return new WaitForSeconds(0.8f);
            RescueService rescue = RescueService.Instance;
            if (rescue == null) yield break;
            // The deepest water within 14 m of the turtle; with none, he lands on the bank out cold.
            Vector3 best = near + Vector3.forward * 6f;
            float bestDepth = -100f;
            for (int i = 0; i < 24; i++)
            {
                Vector3 p = near + Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward * Random.Range(7f, 14f);
                float d = Shore.WaterDepthAt(p + Vector3.up * 3f);
                if (d > bestDepth && d < 6f)
                {
                    bestDepth = d;
                    best = p;
                }
            }
            TouristProfile profile = TouristProfile.Default;
            profile.Name = "Gully the pirate";
            profile.Figure = 0;
            profile.Look = PirateLook(3).Pack();
            profile.SecondsToUnconscious = 25f;
            profile.Shouts = "HELP! I can't swim!|Arrr, help me!|Glub... HELP!";
            bool inWater = bestDepth > Shore.DeepDepth;
            float ground = Shore.GroundHeightAt(best + Vector3.up * 5f);
            best.y = inWater && WaterSurface.Exists ? WaterSurface.HeightAt(best) - 0.4f : (float.IsNaN(ground) ? best.y : ground + 0.5f);
            VictimBrain v = rescue.SpawnVictim(best, 0f, inWater ? VictimState.Distressed : VictimState.Unconscious, inWater ? 30f : 0f, inWater ? 1f : 0f, profile);
            if (v == null) yield break;
            TitleObservers("MAN OVERBOARD!", "Old Snapper knocked a pirate into the water. Save him!");
            Marker("Pirate in trouble", v.NetworkObject);
            Debug.Log($"[Boss] a pirate was knocked into the water at {best:F1} (depth {bestDepth:F1})");
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>Host: a creature of <paramref name="kind"/> living round <paramref name="at"/>.</summary>
        public Creature SpawnCreature(CreatureKind kind, Vector3 at, float yaw, string name = null, int health = 0)
        {
            if (_creaturePrefab == null) _creaturePrefab = Resources.Load<Creature>("Creature");
            if (_creaturePrefab == null)
            {
                Debug.LogError("[Story] no Resources/Creature prefab (PLEASE DON'T DROWN > Build creature prefab)");
                return null;
            }
            Creature c = Instantiate(_creaturePrefab, at, Quaternion.Euler(0f, yaw, 0f));
            Spawn(c.gameObject);
            c.ServerSetup(kind, at, name, health);
            _spawnedActors.Add(c.gameObject);
            return c;
        }

        /// <summary>Host: a pirate on watch at <paramref name="at"/>, walking the patrol (or standing and looking about).</summary>
        private StoryNpc SpawnGuard(string name, int look, Vector3 at, float yaw, IList<Vector3> patrol, NpcWeaponKind weapon = NpcWeaponKind.Pistol, bool lookAbout = true)
        {
            StoryNpc guard = SpawnNpc(name, NpcRole.Pirate, PirateLook(look), at, yaw, 100);
            guard.ServerGuard(patrol, FighterProfile.Pirate(weapon), lookAbout: lookAbout);
            return guard;
        }

        private static Vector3 Ground(Vector3 p)
        {
            float g = Shore.GroundHeightAt(p + Vector3.up * 20f);
            return float.IsNaN(g) ? p : new Vector3(p.x, g, p.z);
        }

        // ------------------------------------------------------------------ console

        private void RegisterThreatCommands()
        {
            DevCommands.Register("stealthtest", "[guards] [parrots]", "Mission test: sneak past guards and parrots to the flag; spotted restarts it.", args =>
                CheatServer($"stealthtest:{(args.Length > 1 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 1)) : 2)}",
                    args.Length > 0 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) : 3), cheat: true, owner: this);
            DevCommands.Register("creature", "<parrot|monkey|alligator|snapper> [count]", "Spawn island 3 creatures in front of you.", args =>
                CheatServer("creature:" + (args.Length > 0 ? args[0].ToLowerInvariant() : "monkey"),
                    args.Length > 1 ? Mathf.RoundToInt(DevCommands.ParseFloat(args, 1)) : 1), cheat: true, owner: this);
            DevCommands.Register("bosstest", "[snapper|kleptopus]", "Mission test: a boss fight (Old Snapper in front of you; the Kleptopus at the hotel pier).", args =>
                CheatServer("bosstest:" + (args.Length > 0 ? args[0].ToLowerInvariant() : "snapper"), 0), cheat: true, owner: this);
            DevCommands.Register("kleptocheck", "", "Host: automated checks of the Kleptopus at the hotel pier (log: [Check]).", _ =>
                CheatServer("kleptocheck", 0), cheat: true, owner: this);
            DevCommands.Register("chapter3check", "", "Host: plays chapter 3 through with your lifeguard and checks each beat (log: [Check]).", _ =>
                CheatServer("chapter3check", 0), cheat: true, owner: this);
            DevCommands.Register("disguise", "[on|off]", "Put the pirate disguise on or take it off.", args =>
                CheatServer("disguise", args.Length > 0 && args[0] == "off" ? 0 : 1), cheat: true, owner: this);
            DevCommands.Register("threatcheck", "", "Host: automated checks of guards, parrots, monkeys, alligators and Old Snapper (log: [Check]).", _ =>
                CheatServer("threatcheck", 0), cheat: true, owner: this);
            DevCommands.Register("creatures", "", "List creatures and guards.", _ =>
            {
                foreach (Creature c in Creature.All)
                    DevCommands.Print($"  {c.DisplayName,-14} {c.Kind,-10} {c.Act,-8} hp {c.Health}/{c.MaxHealth} alert {c.Alert:P0}{(c.IsBoss ? $" phase {c.BossPhase}{(c.BossOpen ? " OPEN" : "")}" : "")} at {c.transform.position:F1}");
                foreach (StoryNpc n in StoryNpc.All)
                    if (n.IsGuarding) DevCommands.Print($"  guard {n.Name,-12} alert {n.Alert:P0}{(n.IsAlarmed ? " ALARMED" : "")} at {n.transform.position:F1}");
            }, owner: this);
        }

        private void UnregisterThreatCommands()
        {
            foreach (string c in new[] { "stealthtest", "creature", "bosstest", "kleptocheck", "chapter3check", "disguise", "threatcheck", "creatures" })
                DevCommands.Unregister(c, this);
        }

        /// <summary>Host: the threat test commands; true if <paramref name="what"/> was one.</summary>
        private bool ServerThreatCheat(string what, int value, PlayerHub by)
        {
            if (what == "disguise")
            {
                if (by != null && by.TryGetComponent(out PlayerCombat c)) c.ServerSetDisguise(value != 0);
                return true;
            }
            if (what == "threatcheck")
            {
                if (by != null) StartCoroutine(ThreatCheck(by));
                return true;
            }
            if (what == "chapter3check")
            {
                if (by != null) by.StartCoroutine(Chapter3Check(by)); // not ours: jumping to 3.1 stops all of ours
                return true;
            }
            if (what == "kleptocheck")
            {
                if (by != null) StartCoroutine(KleptoCheck(by));
                return true;
            }
            if (by == null) return false;
            Vector3 flat = Vector3.ProjectOnPlane(by.Head.forward, Vector3.up).normalized;
            if (flat.sqrMagnitude < 0.01f) flat = Vector3.forward;
            float yaw = Quaternion.LookRotation(-flat).eulerAngles.y;
            if (what.StartsWith("creature:"))
            {
                CreatureKind kind = what.Substring(9) switch
                {
                    "parrot" => CreatureKind.Parrot,
                    "alligator" or "gator" => CreatureKind.Alligator,
                    "snapper" => CreatureKind.OldSnapper,
                    "kleptopus" or "octopus" => CreatureKind.Kleptopus,
                    _ => CreatureKind.Monkey
                };
                for (int i = 0; i < Mathf.Clamp(value, 1, 8); i++)
                {
                    // Monkeys live in island 3's jungle only: the test spawns them at their homes there, wherever you are.
                    if (kind == CreatureKind.Monkey && Island3Point($"MonkeyHome_{i % 3}") is { } monkeyHome)
                    {
                        SpawnCreature(kind, Ground(monkeyHome.position + Quaternion.Euler(0f, i * 97f, 0f) * Vector3.forward * (i / 3) * 2f), yaw);
                        continue;
                    }
                    Vector3 at = by.transform.position + Quaternion.Euler(0f, (i - (value - 1) * 0.5f) * 20f, 0f) * flat * (kind == CreatureKind.OldSnapper ? 14f : 7f);
                    at = kind == CreatureKind.Parrot ? Ground(at) + Vector3.up * 3f : Ground(at);
                    if (kind == CreatureKind.Alligator && WaterSurface.Exists) at.y = Mathf.Max(at.y, WaterSurface.HeightAt(at) - 0.45f);
                    SpawnCreature(kind, at, yaw);
                }
                return true;
            }
            if (what.StartsWith("stealthtest:"))
            {
                if (MissionRunning) return true;
                Vector3 start = by.transform.position;
                int guards = Mathf.Clamp(value, 0, 8), parrots = Mathf.Clamp(int.Parse(what.Substring(12)), 0, 8);
                StartCoroutine(RunStealthMission("Sneak in", start, () => StealthTest(start, flat, guards, parrots)));
                return true;
            }
            if (what.StartsWith("bosstest"))
            {
                if (MissionRunning) return true;
                if (what.EndsWith("kleptopus") || what.EndsWith("octopus"))
                {
                    Vector3 lair = KleptopusLair();
                    GameObject pier = GameObject.Find("HotelDock");
                    Vector3 deck = pier != null ? pier.transform.position + Vector3.up * 0.6f : lair + Vector3.back * 8f;
                    TeleportObservers(deck);
                    StartCoroutine(RunMission("The Kleptopus", deck, () => KleptopusFight(lair)));
                    return true;
                }
                Vector3 start = by.transform.position;
                StartCoroutine(RunMission("Old Snapper", start, () => BossTest(start, flat)));
                return true;
            }
            return false;
        }

        /// <summary>A mission where any sentry raising the alarm fails it.</summary>
        private IEnumerator RunStealthMission(string title, Vector3 start, System.Func<IEnumerator> body, System.Action onRetry = null)
        {
            _failOnSpotted = true;
            try { yield return RunMission(title, start, body, onRetry); }
            finally { _failOnSpotted = false; }
        }

        private IEnumerator StealthTest(Vector3 start, Vector3 forward, int guards, int parrots)
        {
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            Vector3 goal = Ground(start + forward * 45f);
            TitleObservers("Sneak in", "Get to the flag without being seen");
            Marker("Flag", goal + Vector3.up);
            string[] names = { "Lookout Lou", "Squint", "Old Bones", "Peg", "Mags", "Hook", "Bilge", "Tar" };
            for (int i = 0; i < guards; i++)
            {
                // Each walks a beat across the way in, at its own distance, some quick some slow.
                float along = 12f + i * (30f / Mathf.Max(1, guards));
                Vector3 a = Ground(start + forward * along - side * 9f), b = Ground(start + forward * along + side * 9f);
                SpawnGuard(names[i % names.Length], i, i % 2 == 0 ? a : b, Quaternion.LookRotation(i % 2 == 0 ? side : -side).eulerAngles.y,
                    i % 3 == 2 ? new[] { a } : new[] { a, b });
            }
            for (int i = 0; i < parrots; i++)
            {
                Vector3 perch = Ground(start + forward * (18f + i * 12f) + side * (i % 2 == 0 ? -6f : 6f)) + Vector3.up * 3.5f;
                SpawnCreature(CreatureKind.Parrot, perch, Random.Range(0f, 360f), "Parrot");
            }
            while (true)
            {
                bool there = false;
                foreach (PlayerHub p in PlayerHub.All)
                    if (p != null && Vector3.Distance(p.transform.position, goal) < 3f) there = true;
                MissionObjective("Reach the flag unseen. Crouch and walk slowly; keep out of their sight.");
                if (there) break;
                yield return new WaitForSeconds(0.2f);
            }
            NoMarker();
            TitleObservers("MISSION COMPLETE", "Nobody saw a thing");
            yield return new WaitForSeconds(2f);
        }

        private IEnumerator BossTest(Vector3 start, Vector3 forward)
        {
            Vector3 at = Ground(start + forward * 16f);
            if (WaterSurface.Exists) at.y = Mathf.Max(at.y, WaterSurface.HeightAt(at) - 0.9f);
            Creature boss = SpawnCreature(CreatureKind.OldSnapper, at, Quaternion.LookRotation(-forward).eulerAngles.y, "Old Snapper");
            if (boss == null) yield break;
            TitleObservers("OLD SNAPPER", "Its shell stops bullets. Hit the head when it gasps after a bite.");
            while (boss != null && !boss.IsDead)
            {
                MissionObjective($"Beat Old Snapper (phase {boss.BossPhase}/{boss.BossPhases})");
                yield return new WaitForSeconds(0.25f);
            }
            TitleObservers("MISSION COMPLETE", "Old Snapper is beaten");
            yield return new WaitForSeconds(3f);
            Remove(boss);
        }

        // ------------------------------------------------------------------ automated checks (host)

        private int _checkFails;

        private void Check(string what, bool ok, string detail = "")
        {
            if (!ok) _checkFails++;
            Debug.Log($"[Check] {(ok ? "PASS" : "FAIL")}: {what}{(string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")")}");
        }

        /// <summary>
        /// Host: runs the stealth, creature and boss rules against the host's own lifeguard and logs PASS/FAIL per rule,
        /// then "[Check] DONE". Teleports the lifeguard about; run it on an empty bit of island 1.
        /// </summary>
        private IEnumerator ThreatCheck(PlayerHub me)
        {
            _checkFails = 0;
            PlayerCombat combat = me.GetComponent<PlayerCombat>();
            Vector3 origin = me.transform.position;
            Vector3 f = Vector3.ProjectOnPlane(me.Head.forward, Vector3.up).normalized;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            int spotted = 0;
            void Count(string s, PlayerHub w, string r) => spotted++;
            Stealth.ServerSpotted += Count;
            var made = new List<GameObject>();
            void Clear()
            {
                foreach (GameObject go in made)
                    if (go != null)
                    {
                        _spawnedActors.Remove(go);
                        if (go.TryGetComponent(out FishNet.Object.NetworkObject nob) && nob.IsSpawned) Despawn(go);
                    }
                made.Clear();
                spotted = 0;
            }
            void Stand(Vector3 p) { if (me.Motor != null) me.Motor.Teleport(p + Vector3.up * 0.2f); }

            try
            {
                // 1. A guard looking straight at you from 10 m, in the open: alarm within 4 s.
                combat.ServerSetDisguise(false);
                Stand(origin);
                Vector3 guardAt = Ground(origin + f * 10f);
                StoryNpc g = SpawnGuard("Check Guard", 1, guardAt, Quaternion.LookRotation(-f).eulerAngles.y, new[] { guardAt }, lookAbout: false);
                made.Add(g.gameObject);
                float t0 = Time.time;
                while (spotted == 0 && Time.time - t0 < 4f) yield return null;
                Check("guard facing you at 10 m spots you", spotted > 0, $"{Time.time - t0:F1} s");
                Clear();

                // 2. The same guard facing away: nothing in 5 s.
                Stand(origin);
                g = SpawnGuard("Check Guard", 1, guardAt, Quaternion.LookRotation(f).eulerAngles.y, new[] { guardAt }, lookAbout: false);
                made.Add(g.gameObject);
                yield return new WaitForSeconds(5f);
                Check("guard facing away doesn't", spotted == 0, $"alert {g.Alert:P0}");

                // 3. A loud shot behind its back: alarm. 4. a suppressed one 10 m off: no.
                Stealth.ServerNoise(origin + Vector3.up, Stealth.SuppressedShot, me, NoiseKind.Gunshot);
                yield return new WaitForSeconds(0.3f);
                Check("suppressed shot 10 m away isn't heard", spotted == 0);
                Stealth.ServerNoise(origin + Vector3.up, Stealth.LoudShot, me, NoiseKind.Gunshot);
                yield return new WaitForSeconds(0.3f);
                Check("loud shot is heard: alarm", spotted > 0 && g.IsAlarmed);
                Clear();

                // 5. Disguised in plain view at 10 m: no alarm in 5 s.
                combat.ServerSetDisguise(true);
                Stand(origin);
                g = SpawnGuard("Check Guard", 1, guardAt, Quaternion.LookRotation(-f).eulerAngles.y, new[] { guardAt }, lookAbout: false);
                made.Add(g.gameObject);
                yield return new WaitForSeconds(5f);
                Check("disguised lifeguard isn't spotted", spotted == 0, $"alert {g.Alert:P0}");
                Clear();

                // 6. A parrot on a perch 8 m off: spots you undisguised, not disguised.
                Vector3 perch = Ground(origin + f * 8f) + Vector3.up * 3f;
                combat.ServerSetDisguise(false);
                Stand(origin);
                Creature parrot = SpawnCreature(CreatureKind.Parrot, perch, Quaternion.LookRotation(-f).eulerAngles.y, "Check Parrot");
                made.Add(parrot.gameObject);
                t0 = Time.time;
                while (spotted == 0 && Time.time - t0 < 5f) yield return null;
                Check("parrot spots an undisguised lifeguard", spotted > 0, $"{Time.time - t0:F1} s");
                Clear();
                combat.ServerSetDisguise(true);
                parrot = SpawnCreature(CreatureKind.Parrot, perch, Quaternion.LookRotation(-f).eulerAngles.y, "Check Parrot");
                made.Add(parrot.gameObject);
                yield return new WaitForSeconds(4f);
                Check("parrot is fooled by the disguise", spotted == 0);
                Clear();

                // 7. A monkey steals the hat; hit it, it drops it; walk over it, it's back on.
                combat.ServerSetDisguise(true);
                Stand(origin);
                Creature monkey = SpawnCreature(CreatureKind.Monkey, Ground(origin + f * 5f), 0f, "Check Monkey");
                made.Add(monkey.gameObject);
                t0 = Time.time;
                while (combat.IsDisguised && Time.time - t0 < 15f) yield return null;
                Check("monkey steals the disguise hat", !combat.IsDisguised && monkey.HasHat, $"{Time.time - t0:F1} s");
                yield return new WaitForSeconds(1f);
                ((IDamageable)monkey).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, monkey.TopPosition, f);
                yield return null;
                Check("hit monkey drops the hat", monkey.HatLying && !monkey.HasHat);
                Vector3 hat = monkey.transform.position;
                foreach (Transform t in monkey.GetComponentsInChildren<Transform>(true))
                    if (t.name == "PirateHat" && t.parent == monkey.transform) hat = t.position;
                Stand(hat);
                t0 = Time.time;
                while (!combat.IsDisguised && Time.time - t0 < 2f) yield return null;
                Check("walking over the hat puts it back on", combat.IsDisguised);
                Clear();

                // 8. An alligator in the water: grabs a lifeguard wading; five punches break free.
                combat.ServerSetDisguise(false);
                Vector3 wade = FindWater(origin, 0.3f, 0.8f);
                Vector3 deep = FindWater(wade, 1.2f, 3f);
                if (deep == wade) Check("found water for the alligator", false, "no water near");
                else
                {
                    Stand(wade);
                    Vector3 gatorAt = wade + (deep - wade).normalized * 6f;
                    gatorAt = Ground(gatorAt);
                    if (WaterSurface.Exists) gatorAt.y = Mathf.Max(gatorAt.y, WaterSurface.HeightAt(gatorAt) - 0.45f);
                    Creature gator = SpawnCreature(CreatureKind.Alligator, gatorAt, Quaternion.LookRotation(wade - gatorAt).eulerAngles.y, "Check Gator");
                    made.Add(gator.gameObject);
                    t0 = Time.time;
                    while (gator.Grabbed == null && Time.time - t0 < 10f) { me.Motor.Teleport(wade + Vector3.up * 0.2f); yield return null; }
                    Check("alligator grabs someone wading", gator.Grabbed == me, $"{Time.time - t0:F1} s");
                    int healthBefore = combat.Health;
                    yield return new WaitForSeconds(1.2f);
                    Check("held lifeguard gets bitten", combat.Health < healthBefore, $"{healthBefore} -> {combat.Health}");
                    for (int i = 0; i < 5 && gator.Grabbed != null; i++)
                    {
                        ((IDamageable)gator).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, gator.TopPosition, f);
                        yield return new WaitForSeconds(0.15f);
                    }
                    Check("five punches break free", gator.Grabbed == null);
                }
                Clear();
                combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);

                // 9. Old Snapper: the shell takes next to nothing, the open head a lot; phases at 60% and 30%.
                Stand(origin);
                Creature boss = SpawnCreature(CreatureKind.OldSnapper, Ground(origin + f * 30f), Quaternion.LookRotation(-f).eulerAngles.y, "Old Snapper");
                made.Add(boss.gameObject);
                yield return null;
                int phaseEvents = 0;
                void Phase(Creature c, int p) { if (c == boss) phaseEvents++; }
                Creature.ServerBossPhase += Phase;
                int before = boss.Health;
                Vector3 shellTop = boss.transform.position + Vector3.up * 2.2f;
                ((IDamageable)boss).ServerTakeHit(40, DamageKind.Bullet, me, shellTop, Vector3.down);
                int shellLoss = before - boss.Health;
                Check("shell shrugs off a bullet", shellLoss <= 3, $"-{shellLoss}");
                Transform head = null;
                foreach (CreatureZone z in boss.GetComponentsInChildren<CreatureZone>())
                    if (z.Part == "Head") head = z.transform;
                // Wait for it to bite at nothing? It needs someone close: stand in front of it, let it bite, then shoot the head.
                Stand(boss.transform.position + boss.transform.forward * 5.5f);
                t0 = Time.time;
                while (!boss.BossOpen && Time.time - t0 < 8f)
                {
                    me.Motor.Teleport(boss.transform.position + boss.transform.forward * 5.5f + Vector3.up * 0.2f);
                    yield return null;
                }
                Check("it bites, then gasps with the weak spot open", boss.BossOpen, $"{Time.time - t0:F1} s");
                combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
                before = boss.Health;
                if (head != null) ((IDamageable)boss).ServerTakeHit(40, DamageKind.Bullet, me, head.position, f);
                int headLoss = before - boss.Health;
                Check("open head takes 2.5x", headLoss >= 90, $"-{headLoss}");
                Stand(origin);
                // Down to 50%, then 20%: phase 2 (a pirate overboard), phase 3.
                int max = boss.MaxHealth;
                // (Shell hits do almost nothing, so wear it down through its head: 0.6x with the window shut.)
                int guard = 0;
                while (boss.Health > max / 2 && guard++ < 200 && head != null)
                    ((IDamageable)boss).ServerTakeHit(60, DamageKind.Bullet, me, head.position, f);
                yield return new WaitForSeconds(0.3f);
                Check("phase 2 at 60%", boss.BossPhase >= 2, $"phase {boss.BossPhase}, hp {boss.Health}/{max}");
                guard = 0;
                while (boss.Health > max / 5 && guard++ < 300 && head != null)
                    ((IDamageable)boss).ServerTakeHit(60, DamageKind.Bullet, me, head.position, f);
                Check("phase 3 at 30%", boss.BossPhase >= 3, $"phase {boss.BossPhase}");
                Check("phase events fired", phaseEvents >= 2, $"{phaseEvents}");
                yield return new WaitForSeconds(1.5f);
                bool pirate = false;
                foreach (VictimBrain v in VictimBrain.All) if (v.Name.Contains("pirate")) pirate = true;
                Check("phase 2 knocks a pirate into the water", pirate);
                guard = 0;
                while (!boss.IsDead && guard++ < 300 && head != null)
                    ((IDamageable)boss).ServerTakeHit(60, DamageKind.Bullet, me, head.position, f);
                Check("Old Snapper can be beaten", boss.IsDead);
                Creature.ServerBossPhase -= Phase;
                Clear();
                foreach (VictimBrain v in new List<VictimBrain>(VictimBrain.All))
                    if (v != null && v.Name.Contains("pirate") && v.TryGetComponent(out FishNet.Object.NetworkObject nob) && nob.IsSpawned) Despawn(v.gameObject);
            }
            finally
            {
                Stealth.ServerSpotted -= Count;
                combat.ServerSetDisguise(false);
                Clear();
                Stand(origin);
                NoMarker();
                Debug.Log($"[Check] DONE: {(_checkFails == 0 ? "ALL PASS" : _checkFails + " FAILED")}");
            }
        }

        /// <summary>The nearest water <paramref name="min"/>..<paramref name="max"/> m deep, searching outward from a point.</summary>
        private static Vector3 FindWater(Vector3 from, float min, float max)
        {
            for (float r = 0f; r < 80f; r += 1.5f)
                for (int i = 0; i < 24; i++)
                {
                    Vector3 p = from + Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward * r;
                    float d = Shore.WaterDepthAt(new Vector3(p.x, (WaterSurface.Exists ? WaterSurface.HeightAt(p) : 0f) + 0.5f, p.z));
                    if (d >= min && d <= max)
                    {
                        float ground = Shore.GroundHeightAt(p + Vector3.up * 5f);
                        return new Vector3(p.x, float.IsNaN(ground) ? p.y : ground, p.z);
                    }
                }
            return from;
        }
    }
}
