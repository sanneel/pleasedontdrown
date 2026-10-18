using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Characters hurting each other, and dying (host):
    ///
    ///  * Killed or knocked out: fists and blades knock people out (K.O., stars round the head); a bullet or an
    ///    animal's bite that takes the last of their health kills them (no stars, they lie still for good). Story
    ///    people the plot still needs (Sandy, the receptionist, the THIEF, anyone you can talk to) only ever get
    ///    knocked out.
    ///  * Stray blows and shots: a fighter's swing that misses its lifeguard lands on whoever else stands in front,
    ///    a pirate's ball hits whoever is in the way (another pirate, an animal).
    ///  * Grudges: a fighter hit by another character often turns on them for a while (a drunk always does); a bar
    ///    full of drunks with no lifeguard close picks fights among itself.
    ///  * Held: an alligator has them in its jaws; the animal moves them, their own legs and brain wait.
    /// </summary>
    public partial class StoryNpc
    {
        private const float GrudgeSeconds = 10f, BarBrawlSeconds = 14f, RivalLoseRange = 20f;
        private static readonly string[] GrudgeShouts = { "OI! WATCH IT!", "YOU HIT ME?!", "RIGHT, THAT'S IT!", "WRONG GUY, MATE!" };
        private static readonly string[] DrunkPicks = { "YOU LOOKIN' AT ME?!", "YOU SPILLED MY DRINK!", "C'MERE YOU!", "SAY THAT AGAIN!" };

        private readonly SyncVar<bool> _dead = new SyncVar<bool>();
        private StoryNpc _rival;
        private float _rivalUntil, _nextRivalLook;
        private bool _held;

        /// <summary>Killed (shot, bitten), not just knocked out: they won't get up again.</summary>
        public bool IsDead => _dead.Value;
        /// <summary>The story still needs them: they can be knocked out but never killed, and animals leave them alone.</summary>
        public bool IsEssential => _role.Value is NpcRole.Guide or NpcRole.Receptionist or NpcRole.Robber || _talkable.Value;
        /// <summary>Host: the character this one is fighting instead of a lifeguard (null: none).</summary>
        public StoryNpc Rival => RivalValid() ? _rival : null;
        /// <summary>Host: in an animal's jaws.</summary>
        public bool IsHeld => _held;

        // ------------------------------------------------------------------ host API

        /// <summary>
        /// Host: hit by another character (<paramref name="by"/>) or an animal (null). Same as a lifeguard's hit, plus
        /// the grudge. False if it didn't count (can't be hurt, already down).
        /// </summary>
        [Server]
        public bool ServerHitBy(int damage, DamageKind kind, StoryNpc by, Vector3 point, Vector3 direction)
        {
            if (by == this || IsDefeated) return false;
            if (_maxHealth.Value <= 0)
            {
                // A tourist caught by a stray swing: shocked, unhurt.
                if (Random.value < 0.5f) ShoutObservers(Random.value < 0.5f ? "HEY!" : "OW! WATCH IT!", false);
                return false;
            }
            if (!ServerTakeHit(damage, kind, null, point, direction)) return false;
            if (by != null && !IsDefeated && !by.IsDefeated) Grudge(by);
            return true;
        }

        /// <summary>Host: in an animal's jaws at <paramref name="at"/> (called every frame it holds them).</summary>
        [Server]
        public void ServerHold(Vector3 at, float yaw)
        {
            if (!_held)
            {
                _held = true;
                ServerStop();
                _fightGoal = null;
                _rival = null;
                ReleaseTurns(this);
                if (!IsDefeated) _mood.Value = AvatarMood.Scared;
            }
            transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Host: the animal let go.</summary>
        [Server]
        public void ServerLetGo() => _held = false;

        // ------------------------------------------------------------------ dying (from ServerTakeHit)

        /// <summary>A gun or an animal finishes people off; fists and blades only knock them out.</summary>
        private bool KillsOutright(DamageKind kind) => kind is DamageKind.Bullet or DamageKind.Bite && !IsEssential;

        private void Die()
        {
            _dead.Value = true;
            _rival = null;
            _shoutText = null;
        }

        // ------------------------------------------------------------------ grudges

        private void Grudge(StoryNpc by)
        {
            if (!_fighting)
            {
                if (Random.value < 0.6f) ShoutObservers(GrudgeShouts[Random.Range(0, GrudgeShouts.Length)], false);
                return;
            }
            if (_rival == by)
            {
                _rivalUntil = Time.time + GrudgeSeconds;
                return;
            }
            // A drunk always swings back; a pirate mostly takes it out on whoever hit him, sometimes shrugs it off.
            if (_fighter.Style != FightStyle.Drunk && Random.value > 0.6f)
            {
                if (Random.value < 0.5f) ShoutObservers("Watch it, you idiot!", false);
                return;
            }
            StartRivalry(by, GrudgeSeconds, GrudgeShouts[Random.Range(0, GrudgeShouts.Length)]);
        }

        private void StartRivalry(StoryNpc other, float seconds, string shout)
        {
            _rival = other;
            _rivalUntil = Time.time + seconds;
            ReleaseTurns(this);
            if (_fight is not (FightState.WindUp or FightState.Recover or FightState.Down)) SetFight(FightState.Idle, 0f);
            _moveTarget = null;
            _mood.Value = AvatarMood.Angry;
            if (shout != null) ShoutObservers(shout, false);
            Debug.Log($"[Fight] {Name} turns on {other.Name}");
        }

        private bool RivalValid() =>
            _rival != null && !_rival.IsDefeated && _rival._ride == null && !_rival._held && Time.time < _rivalUntil &&
            (_rival.transform.position - transform.position).sqrMagnitude < RivalLoseRange * RivalLoseRange;

        /// <summary>A drunk with no lifeguard close by sometimes picks a fight with another drunk (a bar brawl).</summary>
        private void LookForBrawl()
        {
            if (_fighter.Style != FightStyle.Drunk || Time.time < _nextRivalLook) return;
            _nextRivalLook = Time.time + Random.Range(1.5f, 3f);
            if (NearestStandingPlayer(transform.position, 6f) != null || Random.value > 0.35f) return;
            StoryNpc best = null;
            float bestSq = 9f * 9f;
            foreach (StoryNpc other in _all)
            {
                if (other == this || other == null || !other._fighting || other.IsDefeated || other._held) continue;
                if (other._fighter.Style != FightStyle.Drunk || other._pose.Value != AvatarPose.Normal) continue;
                float d = (other.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = other;
                }
            }
            if (best == null) return;
            StartRivalry(best, BarBrawlSeconds, DrunkPicks[Random.Range(0, DrunkPicks.Length)]);
            if (best._rival == null) best.StartRivalry(this, BarBrawlSeconds, null);
        }

        /// <summary>Host: start a fight with another character now (a bar brawl), for <paramref name="seconds"/> or until one is down.</summary>
        [Server]
        public void ServerBrawlWith(StoryNpc other, float seconds, string shout)
        {
            if (other == null || other == this || IsDefeated || other.IsDefeated || !_fighting) return;
            StartRivalry(other, seconds, shout);
        }

        // ------------------------------------------------------------------ fighting a rival (from FightThink)

        /// <summary>Host: one step of a fight with another character. True while there is one (the lifeguards wait).</summary>
        private bool RivalThink(float dt)
        {
            if (_rival == null) LookForBrawl();
            if (_rival == null) return false;
            if (!RivalValid())
            {
                if (_rival != null && _rival.IsDefeated && Random.value < 0.7f)
                    ShoutObservers(_rival.IsDead ? "...oops." : "AND STAY DOWN!", false);
                _rival = null;
                _gestureAt = -1f;
                if (_fight is FightState.WindUp or FightState.Recover or FightState.Close) SetFight(FightState.Idle, 0f);
                return false;
            }

            NpcWeapon w = _fighter.Weapon;
            bool drunk = _fighter.Style == FightStyle.Drunk;
            Vector3 p = transform.position;
            Vector3 to = _rival.transform.position - p;
            to.y = 0f;
            float distance = to.magnitude;
            Vector3 dir = distance > 0.01f ? to / distance : transform.forward;
            SetGunState(NpcGunState.Lowered, Vector3.zero); // up close: the gun is a club
            _guardUp = distance < 3.5f && _fight != FightState.Recover; // squared up (down again after a swing)

            if (_gestureAt >= 0f && Time.time >= _gestureAt)
            {
                _gestureAt = -1f;
                GestureObservers(AvatarGesture.Punch, FacePoint(_rival.transform, _rival.HeadPosition, _rival.transform.forward));
            }
            switch (_fight)
            {
                case FightState.WindUp:
                    _fightGoal = null;
                    TurnToward(dir, WindUpTurnRate, dt);
                    if (Time.time >= _fightUntil) RivalStrike(w, drunk);
                    return true;
                case FightState.Recover:
                    Goal(p - dir * 0.6f, BackOffSpeed);
                    if (Time.time < _fightUntil) return true;
                    SetFight(FightState.Idle, 0f);
                    _nextSwingAt = Time.time + GuardPause();
                    return true;
                case FightState.Reload:
                    if (Time.time < _fightUntil) return true;
                    _ammo = Mathf.Max(1, w.Magazine);
                    break;
            }
            float reach = w.Reach * (drunk ? 0.9f : 1f);
            if (Time.time < _nextSwingAt)
            {
                // A moment in the guard between blows, at arm's length.
                _fight = FightState.Close;
                Goal(_rival.transform.position - dir * reach * 1.05f, StrafeSpeed);
                return true;
            }
            if (distance <= reach * 0.95f && Vector3.Angle(transform.forward, dir) < 35f)
            {
                StartWindUp(w);
                return true;
            }
            _fight = FightState.Close;
            Goal(_rival.transform.position - dir * reach * 0.75f, drunk ? 2.6f : CloseSpeed);
            return true;
        }

        private void RivalStrike(NpcWeapon w, bool drunk)
        {
            bool landed = false;
            if (RivalValid())
            {
                Vector3 to = _rival.transform.position - transform.position;
                float dy = Mathf.Abs(to.y);
                to.y = 0f;
                landed = to.magnitude <= w.Reach + 0.35f && dy < 1.6f && Vector3.Angle(transform.forward, to) <= 55f;
                if (landed)
                {
                    Vector3 push = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
                    StoryNpc rival = _rival;
                    rival.ServerHitBy(w.MeleeDamage, BlowKind(w), this, rival.HeadPosition - Vector3.up * 0.4f, push);
                    _rivalUntil = Time.time + GrudgeSeconds;
                }
            }
            if (landed) _landed++;
            else
            {
                _missed++;
                WhiffObservers();
            }
            SetFight(FightState.Recover, w.Recover * (drunk ? 1.5f : 1f) * (landed ? 1f : 1.25f));
        }

        private static DamageKind BlowKind(NpcWeapon w) => w.Kind == NpcWeaponKind.Cutlass ? DamageKind.Blade : DamageKind.Punch;

        // ------------------------------------------------------------------ stray blows and shots (from Strike / Fire)

        /// <summary>Host: a blow that missed its lifeguard lands on whoever else stands right in front. True if it hit someone.</summary>
        private bool StrayBlow(NpcWeapon w)
        {
            StoryNpc best = null;
            float bestDistance = w.Reach + 0.3f;
            Vector3 p = transform.position;
            foreach (StoryNpc other in _all)
            {
                if (other == this || other == null || other.IsDefeated || other._ride != null || !Upright(other._pose.Value)) continue;
                Vector3 to = other.transform.position - p;
                float dy = Mathf.Abs(to.y);
                to.y = 0f;
                float d = to.magnitude;
                if (d > bestDistance || dy > 1.6f || Vector3.Angle(transform.forward, to) > 40f) continue;
                bestDistance = d;
                best = other;
            }
            if (best == null) return false;
            Vector3 push = best.transform.position - p;
            push.y = 0f;
            best.ServerHitBy(w.MeleeDamage, BlowKind(w), this, best.HeadPosition - Vector3.up * 0.4f, push.sqrMagnitude > 0.01f ? push.normalized : transform.forward);
            return true;
        }

        /// <summary>Host: our shot hit <paramref name="c"/>, not a lifeguard: another character or an animal takes it.</summary>
        private bool StrayShot(Collider c, int damage, Vector3 point, Vector3 direction)
        {
            if (c == null) return false;
            StoryNpc npc = c.GetComponentInParent<StoryNpc>();
            if (npc != null) return npc != this && npc.ServerHitBy(damage, DamageKind.Bullet, this, point, direction);
            return DamageUtil.Find(c) is { } thing && thing.ServerTakeHit(damage, DamageKind.Bullet, null, point, direction);
        }
    }
}
