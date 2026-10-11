using PleaseDontDrown.Combat;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// Animals and people other than the lifeguards (host), and animals dying:
    ///
    ///  * Prey: the alligator goes for a pirate by the water as readily as for a lifeguard (whoever is closer), grabs
    ///    him and drags him off biting; Old Snapper's bite catches a pirate in the way. A bite that takes the last of
    ///    their health kills them. Story people the plot needs and tourists who can't be hurt are left alone.
    ///    Shooting the alligator enough makes it let go (same as when it holds a lifeguard).
    ///  * Death: a killed animal cries out, flops over and stays dead (no getting up); a small one (parrot, monkey,
    ///    alligator) sinks out of sight after a while and is gone. Bosses stay where they fell.
    /// </summary>
    public partial class Creature
    {
        private const float NpcBite = 10f;
        private const float CorpseStays = 25f, CorpseSinks = 4f;

        private StoryNpc _npcPrey;
        private float _diedAt = -1f;

        /// <summary>A character an animal may go for: on its feet, can be hurt, not someone the story still needs.</summary>
        private static bool IsNpcPrey(StoryNpc n) =>
            n != null && n.MaxHealth > 0 && !n.IsDefeated && !n.IsEssential && n.Ride == null && !n.IsHeld && n.IsUpright;

        private static StoryNpc NearestNpcPrey(Vector3 from, float range, System.Func<StoryNpc, bool> filter = null)
        {
            StoryNpc best = null;
            float bestSq = range * range;
            foreach (StoryNpc n in StoryNpc.All)
            {
                if (!IsNpcPrey(n) || (filter != null && !filter(n))) continue;
                float d = (n.transform.position - from).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = n;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ the alligator with a pirate in its jaws

        private void GrabNpc(StoryNpc npc)
        {
            _grabbed.Value = npc.NetworkObject;
            _struggle = 0;
            _friendsDamage = 0;
            _holdUntil = Time.time + GatorHoldSeconds;
            _nextBite = Time.time + 0.2f;
            SetAct(CreatureAct.Hold, GatorHoldSeconds);
            npc.ServerHold(npc.transform.position, npc.transform.eulerAngles.y);
            npc.ServerShout(Random.value < 0.5f ? "AAAH! GATOR!" : "IT'S GOT MY LEG!", true);
            GrabObservers(npc.NetworkObject);
            Debug.Log($"[Creature] {DisplayName} grabbed {npc.Name}");
        }

        private void HoldNpc(StoryNpc npc, float dt)
        {
            if (npc == null || npc.IsDefeated)
            {
                LetGo(null); // dead (or out cold) in its jaws: it lets the body go
                return;
            }
            if (Time.time > _holdUntil)
            {
                LetGo(null);
                return;
            }
            // Drag toward deeper water, rolling, biting now and then; the pirate goes where the jaws go.
            Vector3 p = transform.position;
            Vector3 deeper = p + (p - npc.transform.position).normalized * 3f;
            MoveToward(Vector3.Lerp(deeper, _home, 0.3f), 1.3f, dt, 60f, water: true, land: true);
            Vector3 jaws = transform.position + transform.forward * 1.7f;
            float surface = WaterSurface.Exists ? WaterSurface.HeightAt(jaws) : jaws.y;
            float ground = Shore.GroundHeightAt(jaws + Vector3.up * 2f);
            jaws.y = Mathf.Max(float.IsNaN(ground) ? jaws.y : ground, surface - 1.4f);
            npc.ServerHold(jaws, transform.eulerAngles.y + 180f);
            if (Time.time < _nextBite) return;
            _nextBite = Time.time + 0.5f;
            npc.ServerHitBy(Mathf.RoundToInt(NpcBite), DamageKind.Bite, null, npc.HeadPosition, transform.forward);
            if (!npc.IsDefeated && Random.value < 0.3f) npc.ServerShout(Random.value < 0.5f ? "GET IT OFF ME!" : "SHOOT IT! SHOOT IT!", true);
        }

        /// <summary>Host: a pirate let go (still alive): out of the water and away from it, fast.</summary>
        private void LetGoOfNpc(StoryNpc npc)
        {
            npc.ServerLetGo();
            if (npc.IsDefeated) return;
            Vector3 p = npc.transform.position;
            Vector3 away = p - transform.position;
            away.y = 0f;
            npc.ServerShout("RUN!", true);
            npc.ServerMoveTo(p + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * 10f, run: true);
        }

        /// <summary>Host: a big bite (Old Snapper) catches any pirate in front of the jaws as well.</summary>
        private void BiteNpcs(Vector3 p, Vector3 jaws, float radius, float angle, int damage)
        {
            // (A copy: a bite that kills changes nothing in the list, but stay safe against anyone despawning.)
            foreach (StoryNpc n in new System.Collections.Generic.List<StoryNpc>(StoryNpc.All))
            {
                if (!IsNpcPrey(n)) continue;
                Vector3 to = n.transform.position - p;
                to.y = 0f;
                if (Flat(n.transform.position, jaws) > radius || Vector3.Angle(transform.forward, to) > angle) continue;
                n.ServerHitBy(damage, DamageKind.Bite, null, n.HeadPosition, to.normalized);
            }
        }

        // ------------------------------------------------------------------ dying

        /// <summary>Host (from Die): the cry and the "KILLED!" on every machine; small animals are cleared away later.</summary>
        private void ServerDeathFx()
        {
            _diedAt = Time.time;
            DiedObservers(Kind is CreatureKind.Parrot or CreatureKind.Monkey or CreatureKind.Alligator);
        }

        [FishNet.Object.ObserversRpc]
        private void DiedObservers(bool small)
        {
            if (_audio != null)
            {
                _audio.PlayOneShot(Core.ProceduralAudio.Thump, 1f);
                AudioClip cry = _kind.Value == CreatureKind.Parrot ? Audio.ParrotSounds.Squawk : Core.ProceduralAudio.Crunch;
                if (cry != null) _audio.PlayOneShot(cry, 0.8f);
            }
            if (small) FloatingText.Spawn(TopPosition + Vector3.up * 0.2f, "KILLED!", new Color(0.9f, 0.15f, 0.12f), 1.1f, 1.4f);
        }

        /// <summary>Host, every frame: a small dead animal lies a while, sinks out of sight and is gone.</summary>
        private void CorpseUpdate(float dt)
        {
            if (!IsServerInitialized || !IsDead || _diedAt < 0f) return;
            if (_kind.Value is not (CreatureKind.Parrot or CreatureKind.Monkey or CreatureKind.Alligator)) return;
            if (_kind.Value == CreatureKind.Monkey && _hatLying.Value) return; // the stolen hat lies by it: wait till it's picked up
            float since = Time.time - _diedAt;
            if (since < CorpseStays) return;
            transform.position += Vector3.down * (1.2f / CorpseSinks) * dt;
            if (since < CorpseStays + CorpseSinks) return;
            _diedAt = -1f;
            Despawn(gameObject);
        }
    }
}
