using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// Alligator (host), island 3's river and pond: lurks low in the water, eyes up. A lifeguard in or near the water
    /// gets stalked; close enough, it opens its jaws and hisses (the warning: get out of the way), then lunges. If the
    /// lunge lands it grabs and drags you toward deeper water with a death roll, biting as it goes. Punch it to break
    /// free (you, mashing) or your friends hurt it enough to let go; it gives up after a few seconds anyway.
    /// </summary>
    public partial class Creature
    {
        private const float GatorReach = 1.6f, GatorBite = 7f, GatorHoldSeconds = 6f;
        private const int GatorStruggle = 5, GatorFriendsDamage = 30;

        private readonly SyncVar<NetworkObject> _grabbed = new SyncVar<NetworkObject>();

        private PlayerHub _prey;
        private Vector3 _lungeDir;
        private float _nextBite, _holdUntil, _nextGatorThink;
        private int _struggle, _friendsDamage;

        /// <summary>Every machine: the lifeguard in its jaws (null: nobody).</summary>
        public PlayerHub Grabbed => _grabbed.Value != null ? _grabbed.Value.GetComponent<PlayerHub>() : null;
        /// <summary>Owner of the grabbed lifeguard: punches landed so far / needed.</summary>
        public int Struggle => _struggle;

        private void GatorSetup()
        {
            _grabbed.Value = null;
            SetAct(CreatureAct.Lurk, 1f);
        }

        /// <summary>In or right by the water (the bank counts: it comes a few steps out for you).</summary>
        private static bool ByWater(Vector3 p) =>
            Shore.WaterDepthAt(p + Vector3.up * 0.5f) > 0.25f ||
            Shore.WaterDepthAt(p + Vector3.up * 0.5f + Vector3.right * 2.5f) > 0.6f || Shore.WaterDepthAt(p + Vector3.up * 0.5f - Vector3.right * 2.5f) > 0.6f ||
            Shore.WaterDepthAt(p + Vector3.up * 0.5f + Vector3.forward * 2.5f) > 0.6f || Shore.WaterDepthAt(p + Vector3.up * 0.5f - Vector3.forward * 2.5f) > 0.6f;

        private void GatorThink(float dt)
        {
            Vector3 p = transform.position;
            switch (_act.Value)
            {
                case CreatureAct.Lurk:
                case CreatureAct.Idle:
                case CreatureAct.Move:
                {
                    if (Time.time >= _nextGatorThink)
                    {
                        _nextGatorThink = Time.time + 0.3f;
                        _prey = NearestStanding(p, 14f, h => ByWater(h.transform.position));
                        // A pirate by the water will do too, whichever is closer (Creature.Prey).
                        _npcPrey = NearestNpcPrey(p, 14f, n => ByWater(n.transform.position));
                        if (_prey != null && _npcPrey != null)
                        {
                            if (Flat(_npcPrey.transform.position, p) < Flat(_prey.transform.position, p)) _prey = null;
                            else _npcPrey = null;
                        }
                    }
                    Transform quarry = _prey != null ? _prey.transform : _npcPrey != null ? _npcPrey.transform : null;
                    if (quarry != null)
                    {
                        SetAct(CreatureAct.Move, 0.5f);
                        Vector3 target = quarry.position;
                        float distance = Flat(target, p);
                        Vector3 to = target - p;
                        to.y = 0f;
                        if (distance < 4.2f && Vector3.Angle(transform.forward, to) < 30f)
                        {
                            // The warning: jaws wide, a hiss. Get out of the way.
                            SetAct(CreatureAct.WindUp, 0.75f);
                            ShoutObservers("HSSSS!", false);
                            break;
                        }
                        MoveToward(target, distance > 7f ? 2.2f : 1.5f, dt, 90f, water: true, land: true);
                        if (Shore.WaterDepthAt(transform.position + Vector3.up) < -0.6f) transform.position = p; // stays by the water
                        break;
                    }
                    // Drift about home, low and slow.
                    if (Flat(p, _home) > 5f) MoveToward(_home, 0.8f, dt, 60f, water: true, land: true);
                    else if (Random.value < 0.2f * dt) Turn(Random.insideUnitSphere, 40f, 1f);
                    if (_act.Value != CreatureAct.Lurk) SetAct(CreatureAct.Lurk, 1f);
                    break;
                }
                case CreatureAct.WindUp:
                    if (_prey != null) Turn(_prey.transform.position - p, 120f, dt);
                    else if (_npcPrey != null) Turn(_npcPrey.transform.position - p, 120f, dt);
                    if (!ActDone) break;
                    _lungeDir = transform.forward;
                    SetAct(CreatureAct.Lunge, 0.32f);
                    break;
                case CreatureAct.Lunge:
                {
                    Vector3 step = _lungeDir * 10f * dt;
                    if (!Blocked(p, step)) transform.position = Settle(p + step);
                    // Snaps shut on whoever is right in front of the jaws.
                    Vector3 jaws = transform.position + transform.forward * 1.9f + Vector3.up * 0.3f;
                    PlayerHub caught = NearestStanding(jaws, GatorReach);
                    if (caught != null && Vector3.Angle(transform.forward, caught.transform.position - transform.position) < 50f)
                    {
                        Grab(caught);
                        break;
                    }
                    StoryNpc caughtNpc = NearestNpcPrey(jaws, GatorReach);
                    if (caughtNpc != null && Vector3.Angle(transform.forward, caughtNpc.transform.position - transform.position) < 50f)
                    {
                        GrabNpc(caughtNpc);
                        break;
                    }
                    if (ActDone)
                    {
                        WhiffObservers();
                        SetAct(CreatureAct.Retreat, 1.4f);
                    }
                    break;
                }
                case CreatureAct.Hold:
                    Hold(dt);
                    break;
                case CreatureAct.Retreat:
                    // A missed lunge (or got hit off): back off a little toward the water, then try again.
                    Vector3 home = p + (_home - p).normalized * 2f;
                    MoveToward(home, 1.2f, dt, 120f, water: true, land: true);
                    if (ActDone) SetAct(CreatureAct.Lurk, 1f);
                    break;
            }
        }

        private void Grab(PlayerHub who)
        {
            _grabbed.Value = who.NetworkObject;
            _struggle = 0;
            _friendsDamage = 0;
            _holdUntil = Time.time + GatorHoldSeconds;
            _nextBite = Time.time + 0.2f;
            SetAct(CreatureAct.Hold, GatorHoldSeconds);
            GrabObservers(who.NetworkObject);
            Debug.Log($"[Creature] {DisplayName} grabbed {who.DisplayName}");
        }

        private void Hold(float dt)
        {
            if (_grabbed.Value != null && _grabbed.Value.TryGetComponent(out StoryNpc npc))
            {
                HoldNpc(npc, dt); // a pirate in its jaws (Creature.Prey)
                return;
            }
            PlayerHub held = Grabbed;
            PlayerCombat combat = held != null ? held.GetComponent<PlayerCombat>() : null;
            if (combat == null || combat.IsDowned)
            {
                LetGo(null);
                return;
            }
            if (Time.time > _holdUntil)
            {
                LetGo("It let go");
                return;
            }
            // Drag toward deeper water, rolling, biting now and then.
            Vector3 p = transform.position;
            Vector3 deeper = p + (p - held.transform.position).normalized * 3f;
            MoveToward(Vector3.Lerp(deeper, _home, 0.3f), 1.3f, dt, 60f, water: true, land: true);
            if (Time.time >= _nextBite)
            {
                _nextBite = Time.time + 0.5f;
                combat.ServerDamage(Mathf.RoundToInt(GatorBite), DisplayName, p);
            }
        }

        private void GatorHit(PlayerHub attacker, int dealt)
        {
            if (_act.Value == CreatureAct.Hold)
            {
                PlayerHub held = Grabbed;
                if (attacker != null && attacker == held)
                {
                    _struggle++;
                    StruggleObservers(held.NetworkObject, _struggle, GatorStruggle);
                    if (_struggle >= GatorStruggle) LetGo("You broke free!");
                }
                else
                {
                    _friendsDamage += dealt;
                    if (_friendsDamage >= GatorFriendsDamage) LetGo("It let go!");
                }
                return;
            }
            // Hit while stalking or winding up: the lunge is off, it backs away a moment.
            if (_act.Value is CreatureAct.WindUp or CreatureAct.Move && dealt >= 15) SetAct(CreatureAct.Retreat, 1.2f);
        }

        private void LetGo(string why)
        {
            NetworkObject held = _grabbed.Value;
            _grabbed.Value = null;
            if (held != null && why != null) ReleasedObservers(held, why);
            if (held != null && held.TryGetComponent(out PlayerCombat combat))
                combat.ServerKnockback((held.transform.position - transform.position).normalized * 4f + Vector3.up * 3f, null);
            if (held != null && held.TryGetComponent(out StoryNpc npc)) LetGoOfNpc(npc);
            SetAct(CreatureAct.Retreat, 2.5f);
        }

        /// <summary>Host: let go of whoever it holds (it died, the mission restarted).</summary>
        public void ServerReleaseGrab()
        {
            if (!IsServerInitialized || _grabbed.Value == null) return;
            if (_grabbed.Value.TryGetComponent(out StoryNpc npc)) npc.ServerLetGo();
            _grabbed.Value = null;
        }

        /// <summary>Every machine: the held lifeguard's own copy keeps them in the jaws (they can still punch).</summary>
        private void UpdateGrabbedLocal(float dt)
        {
            NetworkObject held = _grabbed.Value;
            if (held == null || !held.IsOwner || !held.TryGetComponent(out PlayerHub me) || me.Motor == null) return;
            Vector3 jaws = transform.position + transform.forward * 1.7f;
            float surface = WaterSurface.Exists ? WaterSurface.HeightAt(jaws) : jaws.y;
            float ground = Shore.GroundHeightAt(jaws + Vector3.up * 2f);
            jaws.y = Mathf.Max(float.IsNaN(ground) ? jaws.y : ground, surface - 1.1f);
            me.Motor.Stun(0.25f);
            me.Motor.Teleport(jaws);
        }

        [ObserversRpc]
        private void GrabObservers(NetworkObject who)
        {
            if (_audio != null) _audio.PlayOneShot(Core.ProceduralAudio.Crunch, 1f);
            FloatingText.Spawn(TopPosition + Vector3.up * 0.4f, "CHOMP!", new Color(1f, 0.3f, 0.25f), 1.3f, 1.4f);
            if (who != null && who.IsOwner) PlayerHud.ShowToast($"<b>The {DisplayName.ToLowerInvariant()} has you!</b> Punch it to break free!", 3f);
        }

        [ObserversRpc]
        private void StruggleObservers(NetworkObject who, int done, int needed)
        {
            if (who != null && who.IsOwner) PlayerHud.ShowToast($"Break free! {done}/{needed}", 1f);
        }

        [ObserversRpc]
        private void ReleasedObservers(NetworkObject who, string why)
        {
            if (who != null && who.IsOwner) PlayerHud.ShowToast(why, 2f);
        }

        [ObserversRpc]
        private void WhiffObservers()
        {
            if (_audio != null) _audio.PlayOneShot(Core.ProceduralAudio.Thump, 0.8f);
            SplashFx.Spawn(transform.position + transform.forward * 1.8f, 0.6f);
        }
    }
}
