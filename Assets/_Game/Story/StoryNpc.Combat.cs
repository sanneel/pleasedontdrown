using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Fighting characters (host): pirates with fists, cutlasses, pistols or muskets; drunks; the tavern's cage
    /// fighter. Logic only for now: the blows use the punch / shoot gestures we have, the real moves and weapon
    /// models come with the pirates' own meshes. What makes a fight fair and readable is all here:
    ///
    ///  * Turns: at most two fighters swing at one lifeguard at a time (one for a cage fighter, a duel); the rest
    ///    circle at a few metres, facing them, waiting. Gunmen take turns too (two aiming at one lifeguard).
    ///  * Every blow is wound up first (<see cref="NpcWeapon.WindUp"/>) and turns toward the target only so fast,
    ///    so stepping back or round them makes it miss. It lands only if they are still in reach and in front.
    ///  * Hitting a fighter mid wind-up stops the blow. Enough damage at once knocks them over; they get back up.
    ///  * Gunmen keep their distance, need a clear line, aim with a lag (a moving lifeguard is hard to hit, more so
    ///    far away), fire short bursts, reload, and move to a new spot now and then. Too close, they bash you.
    ///  * Cage fighters guard: blows from the front are mostly blocked; they throw combos and slip back out.
    /// Every fighter keeps a tally (swings, hits, misses, shots, blocks) printed when it ends, for tuning.
    /// </summary>
    public partial class StoryNpc
    {
        private enum FightState : byte { Idle, Approach, Hold, Close, WindUp, Recover, Aim, Reload, Reposition, Down }

        private const float SeeRange = 40f, LoseRange = 45f;
        private const float RingRadius = 3.4f, OrbitDegreesPerSecond = 18f;
        private const float StrafeSpeed = 2.1f, CloseSpeed = 4.2f, BackOffSpeed = 2.4f;

        // Who swings / shoots at whom right now (host). Fighters ask for a turn; most two per lifeguard.
        private static readonly Dictionary<PlayerHub, List<StoryNpc>> MeleeTurns = new(), ShotTurns = new();

        private bool _fighting;
        private FighterProfile _fighter;
        private Vector3 _fightHome;
        private PlayerHub _foe;
        private FightState _fight;
        private float _fightUntil, _fightStart;
        private float _gestureAt = -1f;
        private int _ammo, _burstLeft, _burstsSinceMove;
        private Vector3 _aimPoint, _aimVelocity;
        private float _ringAngle, _orbitSign = 1f;
        private Vector3? _fightGoal;
        private float _fightGoalSpeed;
        private float _nextReplan, _knockedUntil, _poiseDamage, _poiseSince, _comboChain;
        // Tally for tuning.
        private int _swings, _landed, _missed, _shots, _shotsLanded, _blocked, _knockdowns;

        // What's in their hand, on every machine (the weapon models hook onto this when the pirates' meshes arrive).
        private readonly FishNet.Object.Synchronizing.SyncVar<NpcWeaponKind> _weaponKind = new();
        private readonly FishNet.Object.Synchronizing.SyncVar<bool> _armed = new();

        public bool IsFighting => _fighting;
        public FighterProfile Fighter => _fighter;
        /// <summary>Every machine: the weapon this character fights with (only meaningful while <see cref="IsArmed"/>).</summary>
        public NpcWeaponKind WeaponKind => _weaponKind.Value;
        public bool IsArmed => _armed.Value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFightStatics()
        {
            MeleeTurns.Clear();
            ShotTurns.Clear();
        }

        // ------------------------------------------------------------------ host API

        /// <summary>Host: fight lifeguards with this profile; with nobody about, go back to <paramref name="home"/>.</summary>
        [Server]
        public void ServerFight(FighterProfile profile, Vector3 home)
        {
            _fighting = true;
            _fighter = profile;
            _weaponKind.Value = profile.Weapon.Kind;
            _armed.Value = true;
            _fightHome = home;
            _ammo = Mathf.Max(1, profile.Weapon.Magazine);
            _fight = FightState.Idle;
            _ringAngle = Random.Range(0f, 360f);
            _orbitSign = Random.value < 0.5f ? -1f : 1f;
            _swings = _landed = _missed = _shots = _shotsLanded = _blocked = _knockdowns = 0;
            _aggressive = false;
            _fleeing = false;
        }

        [Server]
        public void ServerStopFighting()
        {
            if (!_fighting) return;
            LogTally("stopped");
            _fighting = false;
            _armed.Value = false;
            _fightGoal = null;
            _foe = null;
            ReleaseTurns(this);
        }

        // ------------------------------------------------------------------ thinking (host, every frame)

        /// <summary>Host: one step of the fight. Called from the brain instead of the role's own behaviour.</summary>
        private void FightThink(float dt)
        {
            if (IsDefeated)
            {
                if (_fight != FightState.Idle) LogTally("down for good");
                _fight = FightState.Idle;
                _fightGoal = null;
                ReleaseTurns(this);
                return;
            }
            if (_pose.Value == AvatarPose.Down)
            {
                _fightGoal = null;
                if (Time.time < _knockedUntil) return;
                _pose.Value = AvatarPose.Normal; // back on their feet
                _mood.Value = AvatarMood.Angry;
                SetFight(FightState.Recover, 0.5f);
                ShoutObservers(_fighter.Style == FightStyle.Drunk ? "I'm fine... I'm FINE!" : "Is that all?!", false);
            }
            if (_pose.Value != AvatarPose.Normal) return;
            if (Time.time < _staggerUntil)
            {
                if (_fight == FightState.WindUp) SetFight(FightState.Recover, 0.35f); // interrupted
                _fightGoal = null;
                return;
            }

            PickFoe();
            if (_foe == null)
            {
                ReleaseTurns(this);
                _fightGoal = null;
                _fight = FightState.Idle;
                if ((transform.position - _fightHome).sqrMagnitude > 4f && !_moveTarget.HasValue && Time.time >= _nextReplan)
                {
                    _nextReplan = Time.time + 1f;
                    ServerMoveTo(_fightHome, run: false);
                }
                return;
            }

            if (_gestureAt >= 0f && Time.time >= _gestureAt)
            {
                _gestureAt = -1f;
                GestureObservers(AvatarGesture.Punch, _foe.Head.position);
            }

            NpcWeapon w = _fighter.Weapon;
            Vector3 p = transform.position;
            Vector3 to = _foe.transform.position - p;
            to.y = 0f;
            float distance = to.magnitude;
            Vector3 dir = distance > 0.01f ? to / distance : transform.forward;

            // Mid-swing or mid-recovery: see it through.
            switch (_fight)
            {
                case FightState.WindUp:
                    _fightGoal = null;
                    TurnToward(dir, WindUpTurnRate, dt);
                    if (Time.time >= _fightUntil) Strike();
                    return;
                case FightState.Recover:
                    // Step back a little, facing them (a cage fighter slips right out of reach).
                    float back = _fighter.Style == FightStyle.Brawler ? 1.2f : 0.6f;
                    Goal(p - dir * back, BackOffSpeed);
                    if (Time.time < _fightUntil) return;
                    AfterRecover();
                    return;
                case FightState.Reload:
                    if (distance < 3f) Goal(p - dir * 1.5f, BackOffSpeed);
                    else _fightGoal = null;
                    TurnToward(dir, 240f, dt);
                    if (Time.time < _fightUntil) return;
                    _ammo = Mathf.Max(1, w.Magazine);
                    SetFight(FightState.Idle, 0f);
                    return;
            }

            bool see = CanSee(_foe);
            bool gunReach = w.Ranged && distance >= w.MinRange * 0.55f;
            if (gunReach) ThinkGun(w, p, dir, distance, see, dt);
            else ThinkMelee(w, p, dir, distance, see, dt);
        }

        private float WindUpTurnRate => _fighter.Weapon.Kind == NpcWeaponKind.Cutlass ? 140f : _fighter.Style == FightStyle.Brawler ? 260f : 190f;

        private void ThinkMelee(NpcWeapon w, Vector3 p, Vector3 dir, float distance, bool see, float dt)
        {
            if (distance > 9f || !see)
            {
                Approach();
                return;
            }
            int maxSwinging = _fighter.Style == FightStyle.Brawler ? 1 : 2;
            if (TakeTurn(MeleeTurns, _foe, maxSwinging))
            {
                // Our turn: close in and swing as soon as they are in reach and roughly in front.
                float reach = w.Reach * (_fighter.Style == FightStyle.Drunk ? 0.9f : 1f);
                if (distance <= reach * 0.95f && Vector3.Angle(transform.forward, dir) < 35f)
                {
                    StartWindUp(w);
                    return;
                }
                _fight = FightState.Close;
                Goal(_foe.transform.position - dir * reach * 0.75f, _fighter.Style == FightStyle.Drunk ? 2.6f : CloseSpeed);
                return;
            }
            // Waiting for a turn: circle at a few metres, facing them (and egging the others on).
            _fight = FightState.Hold;
            Orbit(RingRadius, dt);
            if (_fighter.Style == FightStyle.Pirate && Random.value < 0.25f * dt)
                ShoutObservers(PirateShouts[Random.Range(0, PirateShouts.Length)], false);
        }

        private void ThinkGun(NpcWeapon w, Vector3 p, Vector3 dir, float distance, bool see, float dt)
        {
            if (!see || distance > w.MaxRange)
            {
                ReleaseTurn(ShotTurns, this);
                Approach();
                return;
            }
            if (_ammo <= 0)
            {
                ReleaseTurn(ShotTurns, this);
                SetFight(FightState.Reload, w.Reload * Random.Range(0.9f, 1.15f));
                return;
            }
            if (distance < w.MinRange)
                Goal(p - dir * 2f, BackOffSpeed); // too close for comfort: back off while aiming
            else if (_fight == FightState.Reposition)
            {
                if (_fightGoal.HasValue && (FlatDelta(_fightGoal.Value, p) > 0.5f) && Time.time < _fightUntil) return;
                SetFight(FightState.Idle, 0f);
            }
            else if (_fight != FightState.Aim) _fightGoal = null;

            if (!TakeTurn(ShotTurns, _foe, 2))
            {
                // Someone else has the shot: drift sideways to a new angle.
                Orbit(Mathf.Clamp(distance, w.MinRange, w.MaxRange * 0.7f), dt);
                return;
            }
            Vector3 chest = _foe.transform.position + Vector3.up * 1.15f;
            if (_fight != FightState.Aim)
            {
                SetFight(FightState.Aim, (_burstLeft > 0 ? w.BurstGap : w.AimTime) * Mathf.Lerp(1.25f, 0.8f, _fighter.Skill));
                if (_burstLeft <= 0)
                {
                    _burstLeft = w.Burst;
                    _aimPoint = chest + Random.insideUnitSphere * 1.5f; // the first look is rough
                    _aimVelocity = Vector3.zero;
                }
            }
            // The aim trails the lifeguard: it catches up over a fraction of a second (better shots catch up faster).
            float lag = Mathf.Lerp(0.38f, 0.16f, _fighter.Skill);
            _aimPoint = Vector3.SmoothDamp(_aimPoint, chest, ref _aimVelocity, lag, Mathf.Infinity, dt);
            TurnToward(_aimPoint - transform.position, 300f, dt);
            if (Time.time < _fightUntil) return;
            if (AllyInLine(chest))
            {
                Reposition(w, dir, distance);
                return;
            }
            Fire(w, distance);
            _burstLeft--;
            if (_burstLeft > 0)
            {
                SetFight(FightState.Aim, w.BurstGap);
                return;
            }
            _burstsSinceMove++;
            ReleaseTurn(ShotTurns, this);
            if (_burstsSinceMove >= 2 || Random.value < 0.3f) Reposition(w, dir, distance);
            else SetFight(FightState.Recover, w.FollowUpAim * Random.Range(0.8f, 1.3f));
        }

        // ------------------------------------------------------------------ blows and shots

        private void StartWindUp(NpcWeapon w)
        {
            _swings++;
            float windUp = w.WindUp * (_fighter.Style == FightStyle.Drunk ? 1.4f : 1f) * (_comboChain > 0 ? 0.75f : 1f);
            SetFight(FightState.WindUp, windUp);
            // The arm goes back now and the jab plays out as the blow lands (the gesture is the punch we have).
            _gestureAt = Mathf.Max(Time.time, _fightUntil - 0.2f);
            _fightGoal = null;
            WindUpObservers();
        }

        private void Strike()
        {
            NpcWeapon w = _fighter.Weapon;
            bool landed = false;
            if (_foe != null && _foe.TryGetComponent(out PlayerCombat combat) && !combat.IsDowned)
            {
                Vector3 p = transform.position;
                Vector3 to = _foe.transform.position - p;
                float dy = Mathf.Abs(to.y);
                to.y = 0f;
                float reach = w.Reach + 0.35f; // to the edge of their body
                landed = to.magnitude <= reach && dy < 1.6f && Vector3.Angle(transform.forward, to) <= 55f &&
                         LineClear(HeadPosition - Vector3.up * 0.4f, _foe.transform.position + Vector3.up * 1.1f, _foe);
                if (landed)
                {
                    Vector3 push = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
                    combat.ServerKnockback(push * w.Knock + Vector3.up * 2.5f, Name);
                    combat.ServerDamage(w.MeleeDamage, Name, p);
                }
            }
            if (landed) _landed++;
            else
            {
                _missed++;
                WhiffObservers();
            }
            float recover = w.Recover * (_fighter.Style == FightStyle.Drunk ? 1.5f : 1f) * (landed ? 1f : 1.25f); // a miss leaves them open longer
            SetFight(FightState.Recover, recover);
        }

        private void AfterRecover()
        {
            // A cage fighter often follows up; the others give someone else a go.
            bool combo = _fighter.Style == FightStyle.Brawler && _comboChain < 2 && Random.value < 0.35f + _fighter.Skill * 0.3f;
            _comboChain = combo ? _comboChain + 1 : 0;
            if (!combo && _fighter.Style != FightStyle.Brawler) ReleaseTurn(MeleeTurns, this);
            SetFight(FightState.Idle, 0f);
        }

        private void Fire(NpcWeapon w, float distance)
        {
            _shots++;
            _ammo--;
            Vector3 muzzle = HeadPosition - Vector3.up * 0.25f + transform.forward * 0.45f;
            Vector3 aim = _aimPoint - muzzle;
            // Misses grow with distance and with how fast the lifeguard moves (sideways most of all).
            Vector3 foeVelocity = _foe.Motor != null ? _foe.Motor.Velocity : Vector3.zero;
            float across = Vector3.ProjectOnPlane(foeVelocity, aim.normalized).magnitude;
            float spread = w.Spread * (1f + distance / 15f) * (1f + across / 4.5f) * Mathf.Lerp(1.4f, 0.7f, _fighter.Skill);
            Quaternion cone = Quaternion.AngleAxis(Random.Range(0f, 360f), aim) * Quaternion.AngleAxis(Random.Range(0f, spread), Vector3.Cross(aim, Vector3.up).normalized);
            Vector3 dir = cone * aim.normalized;
            Vector3 end = muzzle + dir * w.MaxRange * 1.3f;
            bool hitFoe = false;
            int count = Physics.RaycastNonAlloc(muzzle, dir, _brainHits, w.MaxRange * 1.3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            PlayerHub hitPlayer = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _brainHits[i];
                if (h.distance >= best || IsOwnCollider(h.collider)) continue;
                best = h.distance;
                end = h.point;
                hitPlayer = h.collider.GetComponentInParent<PlayerHub>();
            }
            if (hitPlayer != null && hitPlayer.TryGetComponent(out PlayerCombat combat) && !combat.IsDowned)
            {
                hitFoe = true;
                _shotsLanded++;
                combat.ServerDamage(w.ShotDamage, Name, muzzle);
                combat.ServerKnockback(dir * 1.5f, Name);
            }
            GestureObservers(AvatarGesture.Shoot, _aimPoint);
            ShotObservers(muzzle, end, (byte)w.Sound, hitFoe);
        }

        private void Reposition(NpcWeapon w, Vector3 dir, float distance)
        {
            _burstsSinceMove = 0;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (Random.value < 0.5f ? -1f : 1f);
            float want = Mathf.Clamp(distance, w.MinRange + 1f, w.PreferredRange + 3f);
            Vector3 spot = _foe.transform.position - dir * want + side * Random.Range(3f, 5.5f);
            spot = OnNavMesh(spot, 2f);
            Goal(spot, StrafeSpeed * 1.3f);
            SetFight(FightState.Reposition, 2.5f);
        }

        // ------------------------------------------------------------------ getting hit (from ServerTakeHit)

        private bool _lastHitBlocked;
        private float _staggerFreeUntil;

        /// <summary>
        /// Host: does this hit make them stumble? Not a blocked one, and not again straight after the last stumble
        /// (mashing punches mustn't lock a fighter in place: they take the damage and keep coming).
        /// </summary>
        private bool FightStaggers()
        {
            if (!_fighting) return true;
            if (_lastHitBlocked || Time.time < _staggerFreeUntil) return false;
            _staggerFreeUntil = Time.time + (_fighter.Style == FightStyle.Brawler ? 1.5f : 1f);
            return true;
        }

        /// <summary>Host: a fighter is hit. Returns the damage it really takes (a guard soaks most of it).</summary>
        private int FightOnHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 direction)
        {
            _lastHitBlocked = false;
            if (!_fighting || IsDefeated) return damage;
            // Turn on whoever hit us (mostly).
            if (attacker != null && attacker != _foe && (_foe == null || Random.value < 0.6f)) _foe = attacker;

            // A cage fighter's guard: a blow from the front, unless they're mid-swing, is mostly blocked.
            bool brawler = _fighter.Style == FightStyle.Brawler;
            if (brawler && kind is DamageKind.Punch or DamageKind.Blade && _fight != FightState.WindUp && attacker != null)
            {
                Vector3 from = attacker.transform.position - transform.position;
                from.y = 0f;
                if (Vector3.Angle(transform.forward, from) < 60f && Random.value < 0.45f + _fighter.Skill * 0.3f)
                {
                    _blocked++;
                    _lastHitBlocked = true;
                    BlockObservers(HeadPosition);
                    // Blocking sets up a quick counter: the next swing comes at once and fast.
                    if (_foe == attacker && TakeTurn(MeleeTurns, attacker, 1) && _fight != FightState.Down)
                    {
                        _comboChain = 1;
                        SetFight(FightState.Idle, 0f);
                    }
                    return Mathf.Max(1, Mathf.RoundToInt(damage * 0.2f));
                }
            }

            // Hit mid wind-up: the blow is lost (not a cage fighter's: they take it and swing anyway).
            // (Only a hit that makes them stumble: straight after one they swing through the next.)
            if (_fight == FightState.WindUp && damage >= 8 && !brawler && Time.time >= _staggerFreeUntil)
            {
                _gestureAt = -1f;
                SetFight(FightState.Recover, 0.4f);
            }

            // Enough at once knocks them over (they get back up if there's any fight left in them).
            if (Time.time - _poiseSince > 1.3f)
            {
                _poiseSince = Time.time;
                _poiseDamage = 0f;
            }
            _poiseDamage += damage;
            if (_poiseDamage >= _fighter.Poise && _health.Value - damage > 0)
            {
                _poiseDamage = 0f;
                _knockdowns++;
                _knockedUntil = Time.time + Random.Range(1.6f, 2.3f) * (_fighter.Style == FightStyle.Drunk ? 1.6f : 1f);
                _pose.Value = AvatarPose.Down;
                _mood.Value = AvatarMood.Hurt;
                _fightGoal = null;
                _moveTarget = null;
                SetFight(FightState.Down, 0f);
                ReleaseTurns(this);
            }
            return damage;
        }

        // ------------------------------------------------------------------ moving (host, from ServerUpdate)

        /// <summary>Host: steer straight for the fight goal, facing the foe. False when not steering (route walking).</summary>
        private bool FightMove(float dt)
        {
            if (!_fighting || !_fightGoal.HasValue || _pose.Value != AvatarPose.Normal) return false;
            _moveTarget = null;
            Vector3 p = transform.position;
            Vector3 to = _fightGoal.Value - p;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance > 0.08f)
            {
                Vector3 step = to / distance * Mathf.Min(distance, _fightGoalSpeed * dt);
                Vector3 next = p + step;
                // Fighters don't wade out after a lifeguard in deep water (they'd drown: it's a lifeguard game).
                if (Shore.WaterDepthAt(next + Vector3.up * 2f) < 1.1f)
                    transform.position = Grounded(MoveChecked(p, step));
            }
            if (_foe != null)
            {
                Vector3 face = _foe.transform.position - transform.position;
                TurnToward(face, _fight == FightState.WindUp ? WindUpTurnRate : 360f, dt);
            }
            return true;
        }

        private void Goal(Vector3 point, float speed)
        {
            _fightGoal = point;
            _fightGoalSpeed = speed;
        }

        private void Approach()
        {
            _fight = FightState.Approach;
            _fightGoal = null;
            if (Time.time < _nextReplan && _moveTarget.HasValue) return;
            _nextReplan = Time.time + 0.5f;
            Vector3 p = transform.position;
            Vector3 to = _foe.transform.position - p;
            to.y = 0f;
            ServerMoveTo(_foe.transform.position - to.normalized * 1.5f, run: true);
        }

        /// <summary>Circle the foe at <paramref name="radius"/>, each fighter at its own angle, drifting slowly round.</summary>
        private void Orbit(float radius, float dt)
        {
            _ringAngle += _orbitSign * OrbitDegreesPerSecond * dt;
            Vector3 spot = _foe.transform.position + Quaternion.Euler(0f, _ringAngle, 0f) * Vector3.forward * radius;
            // Drift toward the slot round the near side, not straight through the lifeguard.
            Goal(spot, StrafeSpeed);
        }

        private void TurnToward(Vector3 direction, float degreesPerSecond, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;
            Quaternion want = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, degreesPerSecond * dt);
        }

        private void SetFight(FightState state, float seconds)
        {
            _fight = state;
            _fightStart = Time.time;
            _fightUntil = Time.time + seconds;
        }

        // ------------------------------------------------------------------ targets, sight, turns

        private void PickFoe()
        {
            Vector3 p = transform.position;
            if (_foe != null)
            {
                bool gone = !_foe.TryGetComponent(out PlayerCombat c) || c.IsDowned || (_foe.transform.position - p).sqrMagnitude > LoseRange * LoseRange;
                if (!gone)
                {
                    // Stick with them unless someone else is much closer.
                    PlayerHub nearest = NearestStandingPlayer(p, SeeRange);
                    if (nearest == null || nearest == _foe ||
                        (nearest.transform.position - p).sqrMagnitude * 4f > (_foe.transform.position - p).sqrMagnitude) return;
                }
                ReleaseTurns(this);
            }
            _foe = NearestStandingPlayer(p, SeeRange);
        }

        private bool CanSee(PlayerHub who) =>
            LineClear(HeadPosition, who.transform.position + Vector3.up * 1.4f, who);

        /// <summary>Nothing solid between (characters and the target don't count; walls, rocks, crates do).</summary>
        private bool LineClear(Vector3 from, Vector3 to, PlayerHub target)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.05f) return true;
            int count = Physics.RaycastNonAlloc(from, d / length, _brainHits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _brainHits[i].collider;
                if (IsOwnCollider(c)) continue;
                if (target != null && c.GetComponentInParent<PlayerHub>() != null) continue; // a lifeguard isn't a wall
                Rigidbody body = c.attachedRigidbody;
                if (body != null && IsCharacter(body)) continue;
                if (body != null && !body.isKinematic && body.mass < 30f) continue; // small loose things
                return false;
            }
            return true;
        }

        /// <summary>Another character between our gun and the lifeguard.</summary>
        private bool AllyInLine(Vector3 target)
        {
            Vector3 muzzle = HeadPosition - Vector3.up * 0.25f;
            Vector3 d = target - muzzle;
            float length = d.magnitude;
            int count = Physics.SphereCastNonAlloc(muzzle, 0.3f, d / length, _brainHits, length - 0.5f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = _brainHits[i].collider.attachedRigidbody;
                if (body != null && body != _rigidbody && IsCharacter(body)) return true;
            }
            return false;
        }

        private bool IsOwnCollider(Collider c) => c.attachedRigidbody != null && c.attachedRigidbody == _rigidbody;

        private bool TakeTurn(Dictionary<PlayerHub, List<StoryNpc>> turns, PlayerHub foe, int max)
        {
            if (!turns.TryGetValue(foe, out List<StoryNpc> list)) turns[foe] = list = new List<StoryNpc>();
            list.RemoveAll(n => n == null || !n._fighting || n.IsDefeated || n._foe != foe);
            if (list.Contains(this)) return true;
            if (list.Count >= max) return false;
            list.Add(this);
            return true;
        }

        private static void ReleaseTurn(Dictionary<PlayerHub, List<StoryNpc>> turns, StoryNpc npc)
        {
            foreach (List<StoryNpc> list in turns.Values) list.Remove(npc);
        }

        private static void ReleaseTurns(StoryNpc npc)
        {
            ReleaseTurn(MeleeTurns, npc);
            ReleaseTurn(ShotTurns, npc);
        }

        private static float FlatDelta(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        /// <summary>Host: print the tally so far (tuning).</summary>
        public void ServerLogTally(string why) => LogTally(why);

        private void LogTally(string why) =>
            Debug.Log($"[Fight] {Name} ({_fighter.Weapon.Kind}, {_fighter.Style}) {why}: swings {_swings}, landed {_landed}, missed {_missed}; " +
                      $"shots {_shots}, landed {_shotsLanded}; blocked {_blocked}, knocked down {_knockdowns}");

        // ------------------------------------------------------------------ every machine: sounds only (looks come later)

        [ObserversRpc]
        private void WindUpObservers()
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.KnifeSwish, 0.35f);
        }

        [ObserversRpc]
        private void WhiffObservers()
        {
            if (_audio != null) _audio.PlayOneShot(BeachAudio.PunchSwoosh, 0.6f);
        }

        [ObserversRpc]
        private void BlockObservers(Vector3 at)
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Thump, 0.9f);
            UI.FloatingText.Spawn(at + Vector3.up * 0.3f, "BLOCKED", new Color(0.7f, 0.85f, 1f), 0.8f, 0.8f);
        }

        [ObserversRpc]
        private void ShotObservers(Vector3 muzzle, Vector3 end, byte sound, bool hit)
        {
            AudioSource.PlayClipAtPoint(ProceduralAudio.Shot((GunSound)sound), muzzle, 1f);
            if (!hit) AudioSource.PlayClipAtPoint(ProceduralAudio.BulletImpact, end, 0.6f);
        }
    }
}
