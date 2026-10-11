using System.Collections;
using System.Collections.Generic;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Creatures;
using PleaseDontDrown.Player;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Test commands for killing and knocking out (host): <c>preytest</c> puts an alligator in the water by the
    /// nearest shore with two pirates on the bank; <c>killcheck</c> checks the rules automatically (log: [Check]):
    /// fists knock out, bullets kill, story people only get knocked out, animals die and are cleared away, drunks
    /// brawl with each other, the alligator kills a pirate.
    /// </summary>
    public partial class StoryDirector
    {
        private void RegisterKillCommands()
        {
            DevCommands.Register("preytest", "", "An alligator by the nearest shore with two pirates on the bank (watch it hunt them).", _ =>
                CheatServer("preytest", 0), cheat: true, owner: this);
            DevCommands.Register("killcheck", "", "Host: automated checks of killing, knocking out and fights between characters (log: [Check]).", _ =>
                CheatServer("killcheck", 0), cheat: true, owner: this);
        }

        private void UnregisterKillCommands()
        {
            DevCommands.Unregister("preytest", this);
            DevCommands.Unregister("killcheck", this);
        }

        /// <summary>Host: the kill test commands; true if <paramref name="what"/> was one.</summary>
        private bool ServerKillCheat(string what, PlayerHub by)
        {
            if (by == null) return false;
            if (what == "preytest")
            {
                PreyScene(by.transform.position, out _, out _);
                return true;
            }
            if (what == "killcheck")
            {
                StartCoroutine(KillCheck(by));
                return true;
            }
            return false;
        }

        /// <summary>An alligator in the water off the nearest shore, two pirates (who can be hurt) standing at the edge.</summary>
        private bool PreyScene(Vector3 near, out Creature gator, out List<StoryNpc> pirates)
        {
            gator = null;
            pirates = new List<StoryNpc>();
            Vector3 edge = FindWater(near, 0.25f, 0.6f);
            Vector3 deep = FindWater(edge, 1.2f, 3f);
            if (edge == near || deep == edge) return false;
            Vector3 out_ = (deep - edge).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, out_);
            for (int i = 0; i < 2; i++)
            {
                Vector3 at = edge + side * (i == 0 ? -1.2f : 1.2f);
                StoryNpc pirate = SpawnNpc(i == 0 ? "Bilge Rat" : "Old Tom", NpcRole.Pirate, PirateLook(i + 2), at, Quaternion.LookRotation(out_).eulerAngles.y, 100);
                pirates.Add(pirate);
            }
            Vector3 gatorAt = Ground(edge + out_ * 7f);
            if (WaterSurface.Exists) gatorAt.y = Mathf.Max(gatorAt.y, WaterSurface.HeightAt(gatorAt) - 0.45f);
            gator = SpawnCreature(CreatureKind.Alligator, gatorAt, Quaternion.LookRotation(-out_).eulerAngles.y, "Hungry Gator");
            return gator != null;
        }

        private IEnumerator KillCheck(PlayerHub me)
        {
            _checkFails = 0;
            Vector3 origin = me.transform.position;
            Vector3 f = Vector3.ProjectOnPlane(me.Head.forward, Vector3.up).normalized;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, f);
            var made = new List<Component>();
            void Hit(StoryNpc n, int damage, DamageKind kind, int times)
            {
                for (int i = 0; i < times && !n.IsDefeated; i++) ((IDamageable)n).ServerTakeHit(damage, kind, me, n.HeadPosition, f);
            }

            // 1. Fists knock out, a gun kills, the story's own people are only ever knocked out.
            StoryNpc boxer = SpawnNpc("Check Boxer", NpcRole.Pirate, PirateLook(0), origin + f * 5f - side * 2f, 0f, 100);
            StoryNpc target = SpawnNpc("Check Target", NpcRole.Pirate, PirateLook(1), origin + f * 5f, 0f, 100);
            StoryNpc thief = SpawnNpc("Check Thief", NpcRole.Robber, RobberLook, origin + f * 5f + side * 2f, 0f, 100);
            made.Add(boxer);
            made.Add(target);
            made.Add(thief);
            yield return null;
            Hit(boxer, Damage.Punch, DamageKind.Punch, 10);
            Check("punches knock a pirate out (not dead)", boxer.IsDefeated && !boxer.IsDead);
            Hit(target, 22, DamageKind.Bullet, 10);
            Check("bullets kill a pirate", target.IsDefeated && target.IsDead);
            Hit(thief, 22, DamageKind.Bullet, 10);
            Check("the story's thief is only knocked out by bullets", thief.IsDefeated && !thief.IsDead);

            // 2. An animal shot dead stays dead, then is cleared away.
            Creature parrot = SpawnCreature(CreatureKind.Parrot, Ground(origin - f * 6f) + Vector3.up * 3f, 0f, "Check Parrot");
            yield return null;
            ((IDamageable)parrot).ServerTakeHit(40, DamageKind.Bullet, me, parrot.TopPosition, f);
            Check("a shot parrot is dead", parrot.IsDead);

            // 3. Drunks with no lifeguard about brawl with each other (the lifeguard steps well out of their sight).
            Vector3 bar = origin - f * 22f;
            me.Motor.Teleport(Ground(origin + f * 45f) + Vector3.up * 0.2f);
            var drunks = new List<StoryNpc>();
            for (int i = 0; i < 3; i++)
            {
                StoryNpc d = SpawnNpc($"Check Drunk {i + 1}", NpcRole.Pirate, PirateLook(i), bar + Quaternion.Euler(0f, i * 120f, 0f) * Vector3.forward * 1.6f, i * 120f + 180f, 60);
                d.ServerFight(FighterProfile.Drunk(), bar);
                drunks.Add(d);
                made.Add(d);
            }
            float t0 = Time.time;
            bool brawled = false;
            while (Time.time - t0 < 25f && !brawled)
            {
                foreach (StoryNpc d in drunks) if (d.Health < 60) brawled = true;
                yield return new WaitForSeconds(0.25f);
            }
            Check("drunks pick fights with each other and land blows", brawled, $"{Time.time - t0:F1} s");
            foreach (StoryNpc d in drunks) d.ServerStopFighting();

            // 4. A stray pirate ball hurts another pirate.
            StoryNpc shot = SpawnNpc("Check Bystander", NpcRole.Pirate, PirateLook(3), origin + f * 8f, 0f, 100);
            made.Add(shot);
            yield return null;
            int before = shot.Health;
            shot.ServerHitBy(22, DamageKind.Bullet, boxer, shot.HeadPosition, f);
            Check("a pirate's shot hurts another pirate", shot.Health == before - 22, $"{before} -> {shot.Health}");
            foreach (Component c in made)
                if (c != null) Remove(c);
            made.Clear();

            // 5. The alligator hunts a pirate on the bank, and its bites kill him.
            me.Motor.Teleport(origin + Vector3.up * 0.2f);
            if (!PreyScene(origin, out Creature gator, out List<StoryNpc> pirates))
                Check("found a shore for the alligator", false, "no water near");
            else
            {
                made.Add(gator);
                foreach (StoryNpc p in pirates) made.Add(p);
                // Keep the lifeguard well away (the alligator would rather have the closer one).
                Vector3 away = origin + (origin - gator.transform.position).normalized * 40f;
                me.Motor.Teleport(Ground(away) + Vector3.up * 0.2f);
                t0 = Time.time;
                bool held = false;
                while (Time.time - t0 < 20f && !held)
                {
                    foreach (StoryNpc p in pirates) if (p.IsHeld) held = true;
                    yield return null;
                }
                Check("the alligator grabs a pirate by the water", held, $"{Time.time - t0:F1} s");
                t0 = Time.time;
                bool killed = false;
                while (held && Time.time - t0 < 10f && !killed)
                {
                    foreach (StoryNpc p in pirates) if (p.IsDead) killed = true;
                    yield return new WaitForSeconds(0.2f);
                }
                Check("its bites kill the pirate", killed, $"{Time.time - t0:F1} s");
            }

            // 2b. The dead parrot is gone by now (or nearly: give it the rest of the time).
            t0 = Time.time;
            while (parrot != null && Time.time - t0 < 35f) yield return new WaitForSeconds(0.5f);
            Check("the dead parrot's body is cleared away", parrot == null);

            foreach (Component c in made)
                if (c != null) Remove(c);
            me.Motor.Teleport(origin + Vector3.up * 0.2f);
            Debug.Log($"[Check] DONE: {(_checkFails == 0 ? "ALL PASS" : _checkFails + " FAILED")}");
        }
    }
}
