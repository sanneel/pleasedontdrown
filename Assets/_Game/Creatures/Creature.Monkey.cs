using FishNet.Object.Synchronizing;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// Jungle monkey (host), island 3: potters about its patch, spots a lifeguard in the pirate disguise, creeps up
    /// behind, snatches the hat (disguise gone: the pirates see you for what you are) and runs off with it to sit
    /// and jeer out of reach. Hit it and it drops the hat (walk over it to put it back on) and bolts. Shooting it is
    /// allowed, quietly: a loud shot carries to every pirate (Stealth), and its screeches carry a bit too.
    /// </summary>
    public partial class Creature
    {
        private readonly SyncVar<bool> _monkeyHasHat = new SyncVar<bool>();
        private readonly SyncVar<bool> _hatLying = new SyncVar<bool>();
        private readonly SyncVar<Vector3> _hatAt = new SyncVar<Vector3>();

        private PlayerHub _mark, _stolenFrom;
        private Vector3 _runTo;
        private float _nextHatSteal;

        /// <summary>Host (tests): this monkey has someone's hat / dropped it where it lies.</summary>
        public bool HasHat => _monkeyHasHat.Value;
        public bool HatLying => _hatLying.Value;

        private void MonkeySetup()
        {
            _monkeyHasHat.Value = false;
            _hatLying.Value = false;
            _nextHatSteal = Time.time + 3f;
            SetAct(CreatureAct.Idle, Random.Range(1f, 3f));
        }

        private void MonkeyThink(float dt)
        {
            Vector3 p = transform.position;
            switch (_act.Value)
            {
                case CreatureAct.Idle:
                case CreatureAct.Move:
                {
                    // A disguised lifeguard close by: go for the hat.
                    if (!_monkeyHasHat.Value && Time.time >= _nextHatSteal &&
                        NearestStanding(p, 10f, Stealth.IsDisguised) is { } mark && Flat(mark.transform.position, _home) < 22f)
                    {
                        _mark = mark;
                        SetAct(CreatureAct.Sneak, 8f);
                        break;
                    }
                    if (_act.Value == CreatureAct.Move && MoveToward(_runTo, 1.4f, dt)) break;
                    if (!ActDone) break;
                    // Potter: a few steps somewhere round home, a sit, another few steps.
                    Vector2 r = Random.insideUnitCircle * 7f;
                    _runTo = _home + new Vector3(r.x, 0f, r.y);
                    SetAct(Random.value < 0.6f ? CreatureAct.Move : CreatureAct.Idle, Random.Range(2f, 5f));
                    break;
                }
                case CreatureAct.Sneak:
                {
                    // Round behind them at a creep, then a dash for the head.
                    if (_mark == null || !Stealth.IsDisguised(_mark) || ActDone)
                    {
                        SetAct(CreatureAct.Idle, 2f);
                        break;
                    }
                    Vector3 target = _mark.transform.position;
                    float distance = Flat(target, p);
                    if (distance > 3f)
                    {
                        Vector3 behind = target - (_mark.Head != null ? Vector3.ProjectOnPlane(_mark.Head.forward, Vector3.up).normalized : Vector3.forward) * 2f;
                        MoveToward(behind, 2.6f, dt, 400f);
                        break;
                    }
                    SetAct(CreatureAct.Grab, 2f);
                    ShoutObservers("Ook?", false);
                    break;
                }
                case CreatureAct.Grab:
                {
                    if (_mark == null || ActDone)
                    {
                        SetAct(CreatureAct.Idle, 2f);
                        break;
                    }
                    Vector3 target = _mark.transform.position;
                    MoveToward(target, 6.5f, dt, 720f);
                    if (Flat(target, transform.position) > 1.1f) break;
                    // Got it.
                    if (_mark.TryGetComponent(out PlayerCombat combat) && combat.IsDisguised)
                    {
                        combat.ServerSetDisguise(false);
                        _monkeyHasHat.Value = true;
                        _stolenFrom = _mark;
                        ShoutObservers("OOK OOK! AAH-AAH!", true);
                        HatTakenObservers(_mark.NetworkObject);
                        Debug.Log($"[Creature] {DisplayName} stole {_mark.DisplayName}'s hat");
                    }
                    RunAway(p);
                    break;
                }
                case CreatureAct.Carry:
                    if (MoveToward(_runTo, 6.2f, dt, 540f) && !ActDone) break;
                    SetAct(CreatureAct.Taunt, Random.Range(3f, 6f));
                    break;
                case CreatureAct.Taunt:
                    // Sits and jeers; off again if anyone comes close.
                    if (NearestStanding(p, 6f) is { } chaser) RunAway(p, chaser);
                    else if (ActDone)
                    {
                        if (Random.value < 0.5f) ShoutObservers(Random.value < 0.5f ? "Ooh ooh AAH!" : "*blows a raspberry*", false);
                        SetAct(CreatureAct.Taunt, Random.Range(3f, 6f));
                    }
                    break;
                case CreatureAct.Run:
                    if (MoveToward(_runTo, 6.2f, dt, 540f) && !ActDone) break;
                    SetAct(CreatureAct.Idle, Random.Range(2f, 4f));
                    break;
            }
        }

        /// <summary>Off at a run, away from whoever is nearest, to somewhere 20-28 m off on land.</summary>
        private void RunAway(Vector3 p, PlayerHub from = null)
        {
            from ??= NearestStanding(p, 40f);
            Vector3 away = from != null ? p - from.transform.position : Random.insideUnitSphere;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
            Vector3 best = p;
            for (int i = 0; i < 10; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, Random.Range(-70f, 70f), 0f) * away;
                Vector3 candidate = p + dir * Random.Range(20f, 28f);
                if (Shore.WaterDepthAt(candidate + Vector3.up * 3f) > 0.3f) continue;
                best = candidate;
                break;
            }
            _runTo = best;
            SetAct(_monkeyHasHat.Value ? CreatureAct.Carry : CreatureAct.Run, 9f);
        }

        private void MonkeyHit(PlayerHub attacker, Vector3 point)
        {
            // Screeches: anyone near hears a fight.
            Stealth.ServerNoise(transform.position, Stealth.FightNoise * 0.8f, attacker, NoiseKind.Fight);
            if (_monkeyHasHat.Value) DropHat();
            if (_health.Value > 0) RunAway(transform.position, attacker);
        }

        private void MonkeyDied()
        {
            if (_monkeyHasHat.Value) DropHat();
        }

        private void DropHat()
        {
            _monkeyHasHat.Value = false;
            Vector3 p = transform.position + transform.forward * 0.6f;
            float ground = Shore.GroundHeightAt(p + Vector3.up * 2f);
            _hatAt.Value = new Vector3(p.x, float.IsNaN(ground) ? p.y : ground, p.z);
            _hatLying.Value = true;
            _nextHatSteal = Time.time + 25f;
            ShoutObservers("EEK!", false);
        }

        /// <summary>Host, every frame (alive or not): a lifeguard without the disguise walks over the dropped hat: it's back on.</summary>
        private void HatPickup()
        {
            if (!_hatLying.Value) return;
            foreach (PlayerHub p in PlayerHub.All)
            {
                if (p == null || !p.TryGetComponent(out PlayerCombat c) || c.IsDowned || c.IsDisguised) continue;
                if (Flat(p.transform.position, _hatAt.Value) > 1.4f || Mathf.Abs(p.transform.position.y - _hatAt.Value.y) > 2f) continue;
                c.ServerSetDisguise(true);
                _hatLying.Value = false;
                HatBackObservers(p.NetworkObject);
                return;
            }
        }

        [FishNet.Object.ObserversRpc]
        private void HatTakenObservers(FishNet.Object.NetworkObject victim)
        {
            if (victim != null && victim.IsOwner) UI.PlayerHud.ShowToast("<b>A monkey stole your hat!</b> Your disguise is gone: get it back.", 4f, UI.ToastKind.Warning);
        }

        [FishNet.Object.ObserversRpc]
        private void HatBackObservers(FishNet.Object.NetworkObject who)
        {
            if (who != null && who.IsOwner) UI.PlayerHud.ShowToast("Hat back on. You're a pirate again.", 3f, UI.ToastKind.Good);
        }
    }
}
