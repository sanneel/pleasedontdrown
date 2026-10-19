using System.Collections;
using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Chapter 4's finale (Docs/Prompt-Shako-Chapter-4-Finale.md):
    ///  4.6 Sandy in her villa under her statue: she thanks you for the lost things (she sold them back), changes into
    ///      her boss look (Bodies.SandyBoss), "SANDY / Owner, Finders Keepers Rescue Co.", and runs for her jetty ·
    ///  4.7 the final boss, one mission with one bar in three phases: (1) the airlift: her helicopter drops tourists into
    ///      the deep water off her jetty and her lifeguards swim for them, save four before the clock runs out; (2) Grabby,
    ///      the Kleptopus's big orange cousin under the jetty, dragging the dropped tourists under; (3) Sandy herself on
    ///      the jetty's end with the giant grabber claw (it flings you into the sea; the swim-up ramps bring you back),
    ///      until she's knocked off the end into the deep water ·
    ///  4.8 Please don't drown: Sandy can't swim. Bring her in, CPR (the kiss of life on Sandy: the team has opinions);
    ///      she comes round and gives up.
    /// </summary>
    public partial class StoryDirector
    {
        private const int AirliftGoal = 4, SandyHealth = 300;
        private const float AirliftSeconds = 100f;
        private FkHelicopter _heli;
        private Creature _grabby;
        private VictimBrain _sandyInWater;
        private int _bossPhase;
        private int _bossCheckpoint = 1; // (a failed try starts again at the phase it failed in, not the airlift)

        /// <summary>Host (tests): which phase of the final boss is on (0: none).</summary>
        public int FinalBossPhase => _bossPhase;

        public static AvatarLook SandyBossLook
        {
            get
            {
                AvatarLook look = SandyLook;
                look.Body = AvatarLook.Bodies.SandyBoss;
                return look;
            }
        }

        private FkHelicopter Helicopter => _heli != null ? _heli : _heli = Object.FindFirstObjectByType<FkHelicopter>();

        /// <summary>Host: what a finished (or skipped) fight leaves behind: the claw in her hand, a drowning Sandy.</summary>
        private void ClearFinaleLeftovers()
        {
            if (_sandy != null) _sandy.ServerStopFighting(); // (the claw goes with the fight)
            if (_sandyInWater != null && _sandyInWater.IsSpawned) Despawn(_sandyInWater.gameObject);
            _sandyInWater = null;
        }

        private void ParkHelicopter()
        {
            if (Helicopter != null && Island4Point("HeliPark") is { } park) Helicopter.ServerPark(park.position, park.eulerAngles.y);
        }

        // ------------------------------------------------------------------ 4.6 Sandy

        private IEnumerator SandyReveal()
        {
            EnsureOnIsland4();
            ClearFinaleLeftovers();
            Transform villa = Island4Point("SandyVilla"), top = Island4Point("SandyStairsTop");
            if (villa == null || top == null || _sandy == null) yield break;
            _sandy.ServerSetup("Sandy", NpcRole.Guide, SandyLook);
            _sandy.ServerTeleport(villa.position, villa.eulerAngles.y, keepExact: true);
            _sandy.ServerSetPose(AvatarPose.Normal);
            _sandy.ServerSetMood(AvatarMood.Happy);
            SetObjective("Up the cliff path to the villa under the golden statue");
            Marker("Sandy", _sandy.NetworkObject);
            yield return WaitTalk(_sandy, "Talk to Sandy");
            _sandy.ServerSetTalkable(false);
            NoMarker();
            yield return Say(_sandy, "Oh! My favourite lifeguards! Come in, come in, dears. Mind the gold.");
            yield return Say(_sandy, "I wanted to say thank you. For every lost thing you ever handed in. Every wallet. Every watch.");
            yield return Say(_sandy, "I sold them all back to their owners. Twice! Finders keepers, dear. It's the law of the beach.");
            yield return You("The pills. The airlift bills. The pirates. The THIEF. It was you. All of it.");
            _sandy.ServerSetMood(AvatarMood.Angry);
            yield return Say(_sandy, "Twenty years at that kiosk! Do you know what a Lost & Found earns? NOTHING.");
            yield return Say(_sandy, "Do you know what Finders Keepers earns? EVERYTHING. And you lot keep SAVING my customers!");
            // The real Sandy.
            _sandy.ServerSetup("Sandy", NpcRole.Guide, SandyBossLook);
            _sandy.ServerSetTalkable(false);
            _sandy.ServerSetMood(AvatarMood.Angry);
            BossIntroObservers("SANDY", "Owner, Finders Keepers Rescue Co.");
            yield return new WaitForSeconds(1.2f);
            _sandy.ServerShout("CHOPPER! GRABBY! Mummy's got VISITORS!", false);
            _sandy.ServerMoveTo(top.position, run: true);
            yield return new WaitForSeconds(1.5f);
        }

        // ------------------------------------------------------------------ 4.7 the final boss

        private IEnumerator FinalBoss()
        {
            EnsureOnIsland4();
            Transform top = Island4Point("SandyStairsTop"), end = Island4Point("SandyJettyEnd");
            if (top == null || end == null || _sandy == null) yield break;
            _sandy.ServerSetup("Sandy", NpcRole.Guide, SandyBossLook);
            _sandy.ServerSetTalkable(false);
            // She's off down the stairs to her jetty; you follow.
            _sandy.ServerMoveTo(end.position, run: true);
            SetObjective("After her! Down the stairs to her private jetty");
            Marker("Sandy", _sandy.NetworkObject);
            float t0 = Time.time;
            while (Time.time - t0 < 25f && (_sandy.transform.position - end.position).sqrMagnitude > 2f * 2f) yield return new WaitForSeconds(0.3f);
            _sandy.ServerTeleport(end.position, end.eulerAngles.y, keepExact: true);
            yield return Reach(Island4Point("SandyJetty") is { } j ? j.position : end.position, 14f);
            NoMarker();
            _bossCheckpoint = 1;
            yield return RunMission("Sandy", top.position, FinalBossBody, ResetFinalBoss);
            HideBossBar();
            _bossPhase = 0;
        }

        private void ResetFinalBoss()
        {
            ResetRace();
            if (_grabby != null && _grabby.IsSpawned) { _grabby.ServerReleaseAll(); Despawn(_grabby.gameObject); }
            _grabby = null;
            ParkHelicopter();
            if (_sandy != null && Island4Point("SandyJettyEnd") is { } end)
            {
                _sandy.ServerStopFighting();
                _sandy.ServerSetHealth(0, 0);
                _sandy.ServerTeleport(end.position, end.eulerAngles.y, keepExact: true);
                _sandy.ServerSetPose(AvatarPose.Normal);
            }
            _bossPhase = 0;
        }

        private void BossBar(float phaseDone, float timeLeft = 0f, float total = 0f)
        {
            // One bar for the whole fight: each phase takes a third.
            float left = 1f - ((_bossPhase - 1) + Mathf.Clamp01(phaseDone)) / 3f;
            ServerBossBar("SANDY", left, timeLeft, total);
        }

        private IEnumerator FinalBossBody()
        {
            ResetRace();
            Transform drop = Island4Point("HeliDrop"), end = Island4Point("SandyJettyEnd"), jetty = Island4Point("SandyJetty"), lair = Island4Point("KrakenLair");
            if (drop == null || end == null || jetty == null || lair == null) yield break;
            var counted = new HashSet<VictimBrain>();
            int dropped = 0;
            float nextDrop = Time.time + 4f;
            if (_bossCheckpoint >= 3) goto Phase3;
            if (_bossCheckpoint == 2) goto Phase2;

            // ---- phase 1: the airlift
            _bossPhase = 1;
            BossBar(0f);
            yield return Say(_sandy, "Every tourist that hits the water is a customer! And every customer gets a BILL!");
            if (Helicopter != null) Helicopter.ServerFlyTo(drop.position, drop.eulerAngles.y, 14f);
            for (int i = 0; i < 3; i++)
            {
                // Already in the water beside the jetty (diving off its deck, they asked for a path down to the seabed).
                Vector3 swim = end.position + new Vector3(-3f - i * 2f, 0f, 6.5f + (i % 2) * 1.5f);
                swim.y = (WaterSurface.Exists ? WaterSurface.HeightAt(swim) : 0f) - 1.2f;
                StoryNpc rival = SpawnNpc(RivalNames[5 + i % 3], NpcRole.Rival, RivalLook(5 + i), swim, 90f);
                rival.ServerTeleport(swim, 90f, keepExact: true);
                _raceRivals.Add(rival);
            }
            TitleObservers("PHASE 1: THE AIRLIFT", $"Save {AirliftGoal} before the clock runs out. Her lifeguards will try to grab them.");
            float started = Time.time;
            while (_raceYours < AirliftGoal)
            {
                float left = AirliftSeconds - (Time.time - started);
                if (left <= 0f) { ServerFailMission("Time's up: Finders Keepers billed them all"); yield return null; }
                _raceTourists.RemoveAll(v => v == null || !v.IsSpawned);
                int inWater = _raceTourists.FindAll(v => v.State.NeedsHelp() && !counted.Contains(v)).Count;
                if (inWater < 3 && Time.time > nextDrop && Helicopter != null && Helicopter.Arrived)
                {
                    VictimBrain v = DropTourist(Helicopter.transform.position, dropped);
                    if (v != null) { _raceTourists.Add(v); dropped++; }
                    nextDrop = Time.time + Random.Range(4f, 6.5f);
                }
                CountRescues(counted, "Saved");
                RivalsSwim(counted);
                BossBar(_raceYours / (float)AirliftGoal, left, AirliftSeconds);
                MissionObjective($"The airlift! Pull the dropped tourists out of the sea onto the jetty: {_raceYours}/{AirliftGoal} (her lifeguards {_raceTheirs})", _raceYours, AirliftGoal);
                yield return new WaitForSeconds(0.25f);
            }
            foreach (StoryNpc r in _raceRivals) if (r != null) Remove(r);
            _raceRivals.Clear();
            _bossCheckpoint = 2;

        Phase2:
            // ---- phase 2: Grabby
            _bossPhase = 2;
            BossBar(0f);
            if (Helicopter != null && !Helicopter.EngineOn) Helicopter.ServerFlyTo(drop.position, drop.eulerAngles.y, 14f); // (a retry: back out)
            yield return Say(_sandy, "GRABBY! Mummy needs you, sweetie!");
            Vector3 at = lair.position;
            at.y = WaterSurface.Exists ? WaterSurface.HeightAt(at) - 1.1f * Creature.CousinSize : at.y;
            _grabby = SpawnCreature(CreatureKind.Kleptopus, at, 0f, "Grabby", 650); // (ServerSetup scales a boss for the team size)
            if (_grabby != null) _grabby.ServerMakeCousin(end.position + Vector3.up * 0.6f);
            BossIntroObservers("GRABBY", "The Kleptopus's big cousin. Shoot the arm dragging a tourist under; shoot the head when its eyes go wide.");
            nextDrop = Time.time + 3f;
            while (_grabby != null && !_grabby.IsDead)
            {
                _raceTourists.RemoveAll(v => v == null || !v.IsSpawned);
                int inWater = _raceTourists.FindAll(v => v.State.NeedsHelp()).Count;
                if (inWater < 2 && Time.time > nextDrop && Helicopter != null && Helicopter.Arrived)
                {
                    VictimBrain v = DropTourist(Helicopter.transform.position, dropped++);
                    if (v != null) _raceTourists.Add(v);
                    nextDrop = Time.time + Random.Range(7f, 10f);
                }
                CountRescues(counted, null);
                BossBar(1f - _grabby.Health01);
                MissionObjective(_grabby.BossPhase switch
                {
                    1 => "GRABBY drags tourists under: shoot the arm to free them, then pull them out. Shoot the head when its eyes go wide.",
                    2 => "Ink! Keep the tourists away from its arms. Head shots when it shrieks.",
                    _ => "It throws her loot! Shoot the head when its eyes glow."
                });
                yield return new WaitForSeconds(0.25f);
            }
            if (_grabby != null) StartCoroutine(RemoveLater(_grabby, 3f)); // (soon: its own boss bar would sit over Sandy's)
            _grabby = null;
            if (Helicopter != null && Island4Point("HeliPark") is { } park) Helicopter.ServerFlyTo(park.position + Vector3.up * 0.01f, park.eulerAngles.y, 12f);
            yield return Say(_sandy, "GRABBY! Oh, you've upset him. Fine. If you want something done...");

            _bossCheckpoint = 3;
        Phase3:
            // ---- phase 3: Sandy and the claw
            _bossPhase = 3;
            BossBar(0f);
            _sandy.ServerSetHealth(SandyHealth, SandyHealth);
            BossIntroObservers("SANDY", "The giant grabber from the Lost & Found. Knock her off her jetty!");
            // The claw is her weapon (NpcWeaponKind.Claw, Combat/NpcWeapon): drawn back for the tell, a lunge, and whoever
            // it catches is flung off the side of the jetty into the sea.
            FighterProfile claw = FighterProfile.Claw(0.8f);
            claw.WindUpShout = "MINE!";
            claw.LandShout = "FINDERS KEEPERS!";
            _sandy.ServerFight(claw, end.position);
            while (!_sandy.IsDefeated)
            {
                BossBar(1f - _sandy.Health / (float)SandyHealth);
                MissionObjective($"Knock Sandy off her jetty! Dodge the claw: it flings you into the sea (swim back up the ramps).");
                yield return new WaitForSeconds(0.2f);
            }
            _sandy.ServerStopFighting();
            BossBar(1f);
            _bossPhase = 4;
        }

        /// <summary>A tourist out of the helicopter's door: falls into the sea under it, in trouble.</summary>
        private VictimBrain DropTourist(Vector3 heli, int n)
        {
            if (RescueService.Instance == null) return null;
            TouristProfile profile = Profile(Island4Setup, n % 2);
            profile.SecondsToUnconscious = 30f;
            profile.Shouts = "AAAAH!|I PAID FOR THIS TOUR!|WHY IS THE HELICOPTER EMPTYING?!";
            Vector3 spot = heli + new Vector3(Random.Range(-4f, 4f), 0f, Random.Range(-4f, 4f));
            spot.y = (WaterSurface.Exists ? WaterSurface.HeightAt(spot) : 0f) - 0.4f;
            VictimBrain v = RescueService.Instance.SpawnVictim(spot, Random.Range(0f, 360f), VictimState.Distressed, 40f, 1f, profile);
            if (v != null && WaterSurface.Exists) RescueService.Instance.ServerCallout(spot + Vector3.up * 2f, "SPLASH!", false);
            return v;
        }

        /// <summary>Tourists out of the water (on the jetty or the beach under the cliff) count for the team.</summary>
        private void CountRescues(HashSet<VictimBrain> counted, string word)
        {
            foreach (VictimBrain v in _raceTourists)
            {
                if (v == null || counted.Contains(v)) continue;
                if (!(_rescued.Contains(v) || v.State is VictimState.Saved || v.HasBeenRescued && v.IsAshore)) continue;
                counted.Add(v);
                _raceYours++;
                if (word != null) RescueService.Instance?.ServerAnnounce($"<color=#7dffa8><b>{word.ToUpperInvariant()}!</b></color> {v.Name}: {_raceYours}/{AirliftGoal}");
            }
        }

        // ------------------------------------------------------------------ 4.8 please don't drown

        private IEnumerator SandyCantSwim()
        {
            EnsureOnIsland4();
            if (_sandy != null) _sandy.ServerStopFighting();
            Transform end = Island4Point("SandyJettyEnd"), villa = Island4Point("SandyVilla");
            if (end == null || _sandy == null || RescueService.Instance == null) yield break;
            // Off the end of her jetty, into the deep water: the real Sandy goes; a drowning Sandy is in the sea.
            Vector3 splash = end.position + end.forward * -7f + Vector3.right * 2f; // (the jetty's end looks back inland: out past it)
            splash.y = (WaterSurface.Exists ? WaterSurface.HeightAt(splash) : 0f) - 0.4f;
            _sandy.ServerShout("WAAAH!", true);
            yield return new WaitForSeconds(0.6f);
            if (villa != null) _sandy.ServerTeleport(villa.position, villa.eulerAngles.y, keepExact: true); // (out of sight)
            RescueService.Instance.ServerCallout(splash + Vector3.up * 2f, "SPLASH!", true);
            TitleObservers("SANDY CAN'T SWIM", "Please don't drown");
            var lines = new List<string> { "I CAN'T SWIM!", "HELP! I'M RICH!", "I'LL PAY YOU! I'LL PAY YOU BACK! ...SOME OF IT!" };
            while (true)
            {
                _sandyInWater = SpawnSandyInWater(splash, lines);
                if (_sandyInWater == null) yield break;
                SetObjective("Sandy can't swim! Bring her in, then CPR");
                Marker("Sandy", _sandyInWater.NetworkObject);
                bool kissed = false, joked = false;
                while (_sandyInWater != null && _sandyInWater.IsSpawned && _sandyInWater.State != VictimState.Lost)
                {
                    if (_howRescued.TryGetValue(_sandyInWater, out VictimEvent how) && how is VictimEvent.Revived or VictimEvent.Zapped or VictimEvent.SelfRescue ||
                        _sandyInWater.State == VictimState.Saved) break;
                    if (!joked && _sandyInWater.State == VictimState.Unconscious && _sandyInWater.IsAshore && !_sandyInWater.Item.IsHeld)
                    {
                        joked = true;
                        SetObjective("CPR! Compressions, then the kiss of life");
                        StartCoroutine(CprBanter());
                    }
                    if (!kissed && _sandyInWater.CprCount > 0) kissed = true;
                    yield return new WaitForSeconds(0.25f);
                }
                if (_sandyInWater != null && _sandyInWater.IsSpawned && _sandyInWater.State != VictimState.Lost) break;
                // Lost: the company's helicopter isn't coming for HER. She bobs up again, yelling (try again).
                TitleObservers("SHE'S SINKING", "Try again: Sandy's back up, splashing");
                yield return new WaitForSeconds(2f);
            }
            NoMarker();
            Vector3 lay = _sandyInWater.transform.position;
            float ground = Shore.GroundHeightAt(lay + Vector3.up * 2f);
            if (!float.IsNaN(ground)) lay.y = ground;
            Despawn(_sandyInWater.gameObject);
            _sandyInWater = null;
            // She comes round where she lay, sitting up, soaked.
            _sandy.ServerSetup("Sandy", NpcRole.Guide, SandyBossLook);
            _sandy.ServerTeleport(lay, end.eulerAngles.y, keepExact: true);
            _sandy.ServerSetPose(AvatarPose.Sit);
            _sandy.ServerSetMood(AvatarMood.Hurt);
            _sandy.ServerSetTalkable(false);
            yield return Say(_sandy, "*cough* *COUGH* ...Did... did one of you just KISS me?");
            yield return You("Lifeguard's code. Everybody gets saved. Even you. Don't make it weird.");
            yield return Say(_sandy, "Twenty years... and I never learned to swim. Who needs to swim when you OWN the lifeguards?");
            _sandy.ServerSetMood(AvatarMood.Neutral);
            yield return Say(_sandy, "Fine. FINE. I give up. Finders keepers... losers weepers.");
        }

        private VictimBrain SpawnSandyInWater(Vector3 at, List<string> lines)
        {
            TouristProfile profile = TouristProfile.Default;
            profile.Name = "Sandy";
            profile.Look = SandyBossLook.Pack();
            profile.Figure = 1;
            profile.SecondsToUnconscious = 9f;  // she can't swim: under in seconds
            profile.ConditionSeconds = 150f;    // ...and a long while to bring her back
            profile.Shouts = string.Join("|", lines);
            return RescueService.Instance.SpawnVictim(at, 0f, VictimState.Distressed, 80f, 0.7f, profile);
        }

        // ------------------------------------------------------------------ automated run-through (host)

        /// <summary>chapter4check, 4.6 to 4.8 (after 4.5 has moved on).</summary>
        private IEnumerator Chapter4CheckBoss(PlayerHub me)
        {
            void Stand(Vector3 p) => me.Motor.Teleport(p + Vector3.up * 0.3f);
            PlayerCombat combat = me.GetComponent<PlayerCombat>();
            yield return WaitBeat("4.6", 10f);
            Transform villa = Island4Point("SandyVilla"), end = Island4Point("SandyJettyEnd"), jetty = Island4Point("SandyJetty");
            yield return new WaitForSeconds(1f);
            Check("4.6 Sandy waits in her villa", _beat.Value == "4.6" && villa != null && (_sandy.transform.position - villa.position).magnitude < 2f);
            Stand(_sandy.transform.position + _sandy.transform.forward * 1.5f);
            _talks.Add((_sandy, me));
            float t = Time.time;
            while (_sandy.Look.Body != AvatarLook.Bodies.SandyBoss && Time.time - t < 60f) yield return null;
            Check("4.6 she changes into her boss look", _sandy.Look.Body == AvatarLook.Bodies.SandyBoss);
            yield return WaitBeat("4.7", 20f);
            Check("4.6 she runs for her jetty: on to the boss", _beat.Value == "4.7");

            if (jetty != null) Stand(jetty.position);
            t = Time.time;
            while (!MissionRunning && Time.time - t < 40f) yield return null;
            Check("4.7 the final boss starts by her jetty", MissionRunning && end != null && (_sandy.transform.position - end.position).magnitude < 3f);
            t = Time.time;
            while (_raceTourists.Count == 0 && Time.time - t < 40f) yield return null;
            Check("4.7 phase 1: the helicopter flies out, her lifeguards dive in", Helicopter != null && Helicopter.EngineOn && _raceRivals.Count == 3);
            Check("4.7 phase 1: tourists drop out of the helicopter", _raceTourists.Count > 0, $"{_raceTourists.Count}");
            Check("4.7 phase 1 is on a clock", BossBarTimeTotal > 0f && BossBarTitle == "SANDY");
            // Pull each one out onto the jetty's head (the rivals kept back on the deck).
            t = Time.time;
            while (_bossPhase == 1 && Time.time - t < 150f)
            {
                foreach (StoryNpc r in _raceRivals) if (r != null && jetty != null) r.ServerTeleport(jetty.position, 90f, keepExact: true);
                foreach (VictimBrain v in _raceTourists.ToArray())
                {
                    if (v == null || !v.State.NeedsHelp() || v.Item.IsHeld || v.IsAshore) continue;
                    Vector3 beside = v.transform.position + Vector3.right * 1.2f;
                    beside.y = WaterSurface.HeightAt(beside) - 1.2f;
                    me.Motor.Teleport(beside);
                    me.Look.LookAt(v.transform.position);
                    yield return new WaitForSeconds(0.4f);
                    DevCommands.Execute("grab tourist");
                    yield return new WaitForSeconds(0.8f);
                    if (v != null && v.Item.IsHeld && end != null) { Stand(end.position + end.forward * -2f); yield return new WaitForSeconds(0.8f); DevCommands.Execute("drop"); }
                    yield return new WaitForSeconds(1.2f);
                    if (v != null && v.State == VictimState.Unconscious) { DevCommands.Execute("cpr 8"); yield return new WaitForSeconds(2.5f); }
                    break;
                }
                yield return new WaitForSeconds(0.4f);
            }
            Check("4.7 saving four ends the airlift", _bossPhase == 2, $"saved {_raceYours}, phase {_bossPhase}");
            t = Time.time;
            while (_grabby == null && Time.time - t < 15f) yield return null;
            Check("4.7 phase 2: Grabby, the Kleptopus's big orange cousin", _grabby != null && _grabby.IsCousin && _grabby.transform.localScale.x > 1.5f);
            bool dragged = false;
            t = Time.time;
            while (_grabby != null && !dragged && Time.time - t < 40f)
            {
                for (int a = 0; a < 2; a++) if (_grabby.DraggedVictim(a) != null) dragged = true;
                yield return new WaitForSeconds(0.2f);
            }
            Check("4.7 it drags a tourist in the water under", dragged);
            Transform head = null;
            if (_grabby != null)
                foreach (CreatureZone z in _grabby.GetComponentsInChildren<CreatureZone>())
                    if (z.Part == "Head") head = z.transform;
            for (int i = 0; _grabby != null && !_grabby.IsDead && head != null && i < 600; i++)
            {
                ((IDamageable)_grabby).ServerTakeHit(40, DamageKind.Bullet, me, head.position, me.Head.forward);
                if (i % 10 == 0) yield return null;
            }
            t = Time.time;
            while (_bossPhase != 3 && Time.time - t < 20f) yield return null;
            Check("4.7 beating Grabby brings on phase 3", _bossPhase == 3);
            yield return new WaitForSeconds(1f);
            Check("4.7 phase 3: Sandy fights on her jetty, the claw in her hand", _sandy.IsFighting && _sandy.MaxHealth > 0 && _sandy.IsArmed && _sandy.WeaponKind == NpcWeaponKind.Claw);
            float knocked = combat != null ? combat.LastKnockedTime : 0f;
            Stand(_sandy.transform.position + _sandy.transform.forward * 2f);
            t = Time.time;
            while (combat != null && combat.LastKnockedTime == knocked && Time.time - t < 15f)
            {
                Stand(_sandy.transform.position + _sandy.transform.forward * 2f);
                yield return new WaitForSeconds(0.3f);
            }
            Check("4.7 the claw flings you off the jetty", combat != null && combat.LastKnockedTime != knocked);
            for (int i = 0; i < 80 && !_sandy.IsDefeated; i++)
            {
                ((IDamageable)_sandy).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, _sandy.HeadPosition, me.Head.forward);
                if (combat != null) combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
                yield return new WaitForSeconds(0.2f);
            }
            yield return WaitBeat("4.8", 20f);
            Check("4.7 knocking her down ends the fight", _beat.Value == "4.8");

            t = Time.time;
            while ((_sandyInWater == null || !_sandyInWater.IsSpawned) && Time.time - t < 10f) yield return null;
            Check("4.8 Sandy is in the deep water and can't swim", _sandyInWater != null && !_sandyInWater.IsAshore);
            for (int tries = 0; tries < 3 && _beat.Value == "4.8"; tries++)
            {
                VictimBrain s = _sandyInWater;
                if (s == null) { yield return new WaitForSeconds(1f); continue; }
                Vector3 beside = s.transform.position + Vector3.right * 1.2f;
                beside.y = WaterSurface.HeightAt(beside) - 1.2f;
                me.Motor.Teleport(beside);
                me.Look.LookAt(s.transform.position);
                yield return new WaitForSeconds(0.5f);
                DevCommands.Execute("grab Sandy");
                yield return new WaitForSeconds(1f);
                if (end != null) Stand(end.position + end.forward * -2f);
                yield return new WaitForSeconds(0.8f);
                DevCommands.Execute("drop");
                t = Time.time;
                while (s != null && s.IsSpawned && s.State != VictimState.Unconscious && Time.time - t < 15f) yield return null;
                for (int k = 0; k < 12 && s != null && s.IsSpawned && s.State == VictimState.Unconscious; k++)
                {
                    Stand(s.transform.position + Vector3.forward * 1.4f);
                    me.Look.LookAt(s.transform.position);
                    DevCommands.Execute("cpr 8");
                    yield return new WaitForSeconds(2f);
                }
                yield return new WaitForSeconds(1f);
            }
            t = Time.time;
            while (_beat.Value == "4.8" && Time.time - t < 40f) yield return null;
            Check("4.8 CPR brings her round and she gives up", _beat.Value != "4.8", _beat.Value);
            Check("4.8 she's sitting up, soaked, in her boss look", _sandy.Pose == AvatarPose.Sit || _beat.Value != "4.8");
            yield return Chapter4CheckEnding(me);
        }

        private IEnumerator CprBanter()
        {
            yield return You("She needs the kiss of life. ...Somebody has to do it.");
            yield return You("Not it. NOT IT. I called not it on the boat!");
            yield return You("Fine. For the lifeguard's code. Don't look.");
        }
    }
}
