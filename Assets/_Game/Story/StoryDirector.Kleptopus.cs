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
    /// Chapter 2's boss (Docs/16-Plan-Islands-2-3.md): things go missing from the guests on the beach, the receptionist
    /// comes out to the team, the trail of wet sucker marks leads under the hotel pier: the Kleptopus. A mission (all
    /// down restarts it). Beaten: the stolen things spill out to hand back and the hotel pays $300.
    /// </summary>
    public partial class StoryDirector
    {
        private static readonly string[] TheftLines =
        {
            "My sunglasses! Something WET took my sunglasses!", "Has anyone seen my watch? It was RIGHT HERE.",
            "My phone! It just... slithered away!", "Who keeps taking the sun loungers?!"
        };

        /// <summary>Where the Kleptopus lives: deep water just off the hotel pier's far end.</summary>
        private static Vector3 KleptopusLair()
        {
            GameObject pier = GameObject.Find("HotelDock");
            Vector3 end = pier != null ? pier.transform.position + pier.transform.forward * 11f : new Vector3(-2f, 0f, -199f);
            Vector3 side = pier != null ? pier.transform.right : Vector3.right;
            Vector3 best = end + side * 6f;
            float bestDepth = -1f;
            foreach (float s in new[] { 6f, -6f, 8f, -8f })
                foreach (float f in new[] { 0f, 3f, 6f })
                {
                    Vector3 p = end + side * s + (pier != null ? pier.transform.forward : Vector3.forward) * f;
                    float d = Shore.WaterDepthAt(new Vector3(p.x, 1f, p.z));
                    if (d > bestDepth && d < 8f)
                    {
                        bestDepth = d;
                        best = p;
                    }
                }
            float y = WaterSurface.Exists ? WaterSurface.HeightAt(best) - 1.1f : -1f;
            return new Vector3(best.x, y, best.z);
        }

        private IEnumerator Kleptopus()
        {
            _island = _island2;
            EnsureOnIsland2();
            SetObjective("Something on the beach is stealing from the guests...");
            // Guests on the beach cry out about their things, one after another.
            BeachCrowd crowd = BeachCrowd.Nearest(Island2SpawnPoint);
            for (int i = 0; i < 3; i++)
            {
                StoryNpc guest = crowd != null ? crowd.Borrow(i % 2 == 0 ? BeachCrowd.Activity.Sunbathe : BeachCrowd.Activity.Stroll, Island2SpawnPoint, 70f) : null;
                if (guest != null)
                {
                    guest.ServerSetMood(AvatarMood.Angry);
                    guest.ServerShout(TheftLines[i % TheftLines.Length], true);
                    Marker("!", guest.NetworkObject);
                }
                yield return new WaitForSeconds(3f);
                if (guest != null) crowd.Return(guest);
            }
            NoMarker();

            // Marisol comes out to the team.
            Vector3 home = _receptionist.transform.position;
            float homeYaw = _receptionist.transform.eulerAngles.y;
            PlayerHub near = StoryNpc.NearestPlayer(home, 400f);
            if (near != null)
            {
                Vector3 to = near.transform.position + (home - near.transform.position).normalized * 2f;
                _receptionist.ServerMoveTo(StoryNpc.OnNavMesh(to), run: true);
                float started = Time.time;
                while (_receptionist.IsMoving && Time.time - started < 25f) yield return null;
                _receptionist.ServerFace(near.transform.position);
            }
            _receptionist.ServerSetMood(AvatarMood.Scared);
            yield return Say(_receptionist, "Lifeguards! Watches, phones, sunglasses... the guests' things keep vanishing!");
            yield return Say(_receptionist, "And there's a trail of little wet circles. Suckers. Leading under the pier.");
            _receptionist.ServerSetMood(AvatarMood.Neutral);
            yield return Say(_receptionist, $"Bring our guests' things back and the hotel pays you ${Creature.KleptopusReward}. Please hurry, the reviews!");
            _receptionist.ServerMoveTo(home, run: false);
            StartCoroutine(FaceWhenHome(_receptionist, home, homeYaw));

            Vector3 lair = KleptopusLair();
            SetObjective("Find the thief under the pier");
            Marker("The pier", lair + Vector3.up * 2f);
            while (StoryNpc.NearestPlayer(lair, 16f) == null) yield return new WaitForSeconds(0.25f);
            NoMarker();
            PlayerHub first = StoryNpc.NearestPlayer(lair, 16f);
            Vector3 start = first != null ? first.transform.position : lair;
            yield return RunMission("The Kleptopus", start, () => KleptopusFight(lair));
            _receptionist.ServerSetMood(AvatarMood.Happy);
            yield return Say(_receptionist, "(over the speaker) A GIANT OCTOPUS?! ...in a PANAMA?! The hotel thanks you. Generously!");
            yield return Say(_receptionist, "(over the speaker) Any of our guests' things you find on the beach, please hand them in.");
        }

        private IEnumerator FaceWhenHome(StoryNpc npc, Vector3 home, float yaw)
        {
            float started = Time.time;
            while (npc != null && npc.IsMoving && Time.time - started < 30f) yield return null;
            if (npc != null) npc.ServerTeleport(home, yaw, keepExact: true);
        }

        private IEnumerator KleptopusFight(Vector3 lair)
        {
            Creature boss = SpawnCreature(CreatureKind.Kleptopus, lair, 180f, "The Kleptopus");
            if (boss == null) yield break;
            TitleObservers("THE KLEPTOPUS", "Shoot the tentacles to free the guests. Its eyes go wide: shoot the head!");
            while (boss != null && !boss.IsDead)
            {
                MissionObjective(boss.BossPhase switch
                {
                    1 => "Shoot the tentacle dragging a guest, then rescue them. Shoot the head when its eyes go wide.",
                    2 => "Ink! If it snatches a pistol, punch the tentacle by the pier to get it back.",
                    _ => "Dodge the flying loot! Shoot the head when its eyes glow."
                });
                yield return new WaitForSeconds(0.25f);
            }
            TitleObservers("MISSION COMPLETE", $"The Kleptopus is beaten! +${Creature.KleptopusReward}");
            yield return new WaitForSeconds(3f);
            StartCoroutine(RemoveLater(boss, 25f));
        }

        // ------------------------------------------------------------------ automated check (host)

        /// <summary>
        /// Host: the Kleptopus's rules against the host's own lifeguard on island 2 (it moves you to the pier). Logs
        /// "[Check] PASS/FAIL" per rule and "[Check] DONE".
        /// </summary>
        private IEnumerator KleptoCheck(PlayerHub me)
        {
            _checkFails = 0;
            PlayerCombat combat = me.GetComponent<PlayerCombat>();
            Vector3 lair = KleptopusLair();
            GameObject pier = GameObject.Find("HotelDock");
            Vector3 deck = pier != null ? pier.transform.position + Vector3.up * 0.6f : lair + Vector3.back * 8f;
            me.Motor.Teleport(deck);
            yield return new WaitForSeconds(1f);
            Creature boss = SpawnCreature(CreatureKind.Kleptopus, lair, 180f, "The Kleptopus");
            Check("the Kleptopus spawns at its lair", boss != null, $"{lair:F1}, water depth {Shore.WaterDepthAt(lair + Vector3.up * 2f):F1}");
            if (boss == null) yield break;
            int victimsBefore = VictimBrain.All.Count;
            try
            {
                // Phase 1: an arm drags a sunbather; shooting it lets go and opens the head.
                float t0 = Time.time;
                int arm = -1;
                while (arm < 0 && Time.time - t0 < 15f)
                {
                    for (int i = 0; i < 2; i++) if (boss.Dragged(i) != null && boss.Arm(i) == Creature.ArmState.Drag) arm = i;
                    yield return null;
                }
                Check("an arm drags a sunbather", arm >= 0, $"{Time.time - t0:F1} s");
                if (arm >= 0)
                {
                    StoryNpc dragged = boss.Dragged(arm);
                    t0 = Time.time; // out into deep water
                    while (dragged != null && Shore.WaterDepthAt(dragged.transform.position + Vector3.up * 2f) < 1.3f && Time.time - t0 < 20f) yield return null;
                    Transform tip = null;
                    foreach (CreatureZone z in boss.GetComponentsInChildren<CreatureZone>())
                        if (z.Part == "Arm" + arm) tip = z.transform;
                    for (int i = 0; i < 2 && boss.Dragged(arm) != null; i++)
                        ((IDamageable)boss).ServerTakeHit(30, DamageKind.Bullet, me, tip != null ? tip.position : boss.transform.position, Vector3.down);
                    yield return null;
                    Check("shooting the arm lets go", boss.Dragged(arm) == null);
                    Check("it shrieks with the head open", boss.BossOpen);
                    Check("the guest is in the water, to rescue", VictimBrain.All.Count > victimsBefore, $"victims {victimsBefore} -> {VictimBrain.All.Count}");
                }

                // Phase 2: hit the head down to 60%: ink and a stolen pistol, punched back.
                Transform head = null;
                foreach (CreatureZone z in boss.GetComponentsInChildren<CreatureZone>())
                    if (z.Part == "Head") head = z.transform;
                int guard = 0;
                while (boss.Health01 > 0.62f && guard++ < 200 && head != null)
                    ((IDamageable)boss).ServerTakeHit(40, DamageKind.Bullet, me, head.position, Vector3.forward);
                Check("phase 2 at 66%", boss.BossPhase == 2, $"hp {boss.Health}/{boss.MaxHealth}");
                me.Motor.Teleport(deck);
                DevCommands.Execute("spawn Pistol");
                yield return new WaitForSeconds(1f);
                DevCommands.Execute("grab Pistol");
                yield return new WaitForSeconds(1f);
                me.Hands.HeldItem?.GetComponent<Weapon>()?.ServerFitByName("suppressor");
                bool holding = me.Hands.HeldItem != null && me.Hands.HeldItem.GetComponent<Weapon>() != null;
                Check("(set-up) holding a suppressed pistol", holding);
                t0 = Time.time;
                bool inked = false;
                while (!boss.HoldsGun && Time.time - t0 < 25f)
                {
                    if (Creature.InkUntil > Time.time) inked = true;
                    me.Motor.Teleport(deck);
                    yield return null;
                }
                Check("ink clouds the view", inked);
                Check("it snatches the pistol", boss.HoldsGun && (me.Hands.HeldItem == null || me.Hands.HeldItem.GetComponent<Weapon>() == null), $"{Time.time - t0:F1} s");
                int stealArm = boss.Arm(0) == Creature.ArmState.Steal ? 0 : 1;
                Transform armTip = null;
                foreach (CreatureZone z in boss.GetComponentsInChildren<CreatureZone>())
                    if (z.Part == "Arm" + stealArm) armTip = z.transform;
                ((IDamageable)boss).ServerTakeHit(30, DamageKind.Bullet, me, armTip != null ? armTip.position : boss.transform.position, Vector3.forward);
                Check("a bullet doesn't make it drop it", boss.HoldsGun);
                ((IDamageable)boss).ServerTakeHit(Damage.Punch, DamageKind.Punch, me, armTip != null ? armTip.position : boss.transform.position, Vector3.forward);
                yield return new WaitForSeconds(0.5f);
                Check("a punch gets it back", !boss.HoldsGun);
                Weapon back = null;
                foreach (Item i in Item.All)
                    if (!i.IsHeld && i.TryGetComponent(out Weapon w) && (i.transform.position - lair).sqrMagnitude < 30f * 30f) back = w;
                Check("the same pistol, suppressor still on", back != null && back.Barrel.Suppressed);

                // Phase 3: eyes glow (open) before a throw; a lounger lands where you stood.
                guard = 0;
                while (boss.Health01 > 0.3f && guard++ < 200 && head != null)
                    ((IDamageable)boss).ServerTakeHit(40, DamageKind.Bullet, me, head.position, Vector3.forward);
                Check("phase 3 at 33%", boss.BossPhase == 3);
                combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
                t0 = Time.time;
                bool glowed = false;
                int hpBefore = combat.Health;
                while (Time.time - t0 < 12f && combat.Health >= hpBefore)
                {
                    if (boss.BossOpen) glowed = true;
                    me.Motor.Teleport(deck);
                    yield return null;
                }
                Check("eyes glow before a throw", glowed);
                Check("a thrown lounger or phone hurts someone standing still", combat.Health < hpBefore, $"{hpBefore} -> {combat.Health}");

                // Beaten: loot spills out, the hotel pays.
                int money = Economy.Money;
                guard = 0;
                while (!boss.IsDead && guard++ < 300 && head != null)
                    ((IDamageable)boss).ServerTakeHit(60, DamageKind.Bullet, me, head.position, Vector3.forward);
                yield return new WaitForSeconds(1.5f);
                Check("the Kleptopus can be beaten", boss.IsDead);
                Check($"the hotel pays ${Creature.KleptopusReward}", Economy.Money >= money + Creature.KleptopusReward, $"${money} -> ${Economy.Money}");
                int loot = 0;
                foreach (Item i in Item.All)
                    if (!i.IsHeld && i.TryGetComponent(out LostItem l) && l.IsStolen && (i.transform.position - lair).sqrMagnitude < 40f * 40f) loot++;
                Check("stolen things spill out to hand back", loot >= 4, $"{loot}");
            }
            finally
            {
                if (boss != null) { boss.ServerReleaseAll(); Remove(boss); }
                combat.ServerStandUp(PlayerCombat.MaxHealth, 0f);
                Debug.Log($"[Check] DONE: {(_checkFails == 0 ? "ALL PASS" : _checkFails + " FAILED")}");
            }
        }
    }
}
