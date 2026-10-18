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
    /// fighter. The weapon in their hands (flintlock pistol, musket, cutlass) and how it moves is drawn by
    /// <see cref="NpcWeaponView"/> from the synced gun state; what makes a fight fair and readable is all here:
    ///
    ///  * Turns: at most two fighters close in on one lifeguard at a time (one for a cage fighter, a duel), and only
    ///    one of them winds up a blow at a time; the rest circle well back (4.5 m), spread out, waiting. Gunmen take
    ///    turns too (two aiming at one lifeguard). Few shouts: one pirate every few seconds, not all of them at once.
    ///  * Fist fighters square up (<see cref="FightStance"/>): fists up at the chin while closing in or circling,
    ///    and a blow is wound up first (<see cref="NpcWeapon.WindUp"/>), the fist drawn back past the cheek: the tell.
    ///    They turn toward the target only so fast, so stepping back or round them makes it miss.
    ///  * The guard blocks blows from the front (<see cref="FighterProfile.Guard"/>; a hook or an uppercut gets through
    ///    more often): a blocked blow does nothing, the lifeguard's arm bounces off (their next punch waits a moment,
    ///    <see cref="PlayerCombat.ServerStun"/>) and it's answered with a quick counter. Two blows landing in a row
    ///    make them cover up and back off a moment. A flinch doesn't drop the guard; after their own swing it is down:
    ///    that's the moment to hit them (a jab then staggers them). So mashing the button into a raised guard gets you
    ///    countered; timing, hooks and uppercuts, combos and coming in from the side win.
    ///  * They square up a moment at arm's length before the first blow and between blows. Winding up, the other
    ///    hand still guards (jabs mostly bounce off); a heavy blow early in the wind-up stops it, late in it they swing
    ///    through. Stepping back out of the blow leaves them open. A blow that lands rocks the lifeguard a moment (no
    ///    punching straight back). Enough damage at once (a combo) knocks them over; they get back up.
    ///  * Gunmen keep their distance, need a clear line, aim with a lag (a moving lifeguard is hard to hit, more so
    ///    far away), fire short bursts, reload, and move to a new spot now and then. Too close, they bash you.
    ///  * Cage fighters guard almost every blow from the front; they throw combos and slip back out.
    /// Every fighter keeps a tally (swings, hits, misses, shots, blocks) printed when it ends, for tuning.
    /// </summary>
    public partial class StoryNpc
    {
        private enum FightState : byte { Idle, Approach, Hold, Close, WindUp, Recover, Aim, Reload, Reposition, Down }

        private const float SeeRange = 40f, LoseRange = 45f;
        private const float RingRadius = 4.5f, OrbitDegreesPerSecond = 14f, Elbow = 1.7f; // Elbow: room kept between fighters
        private const float StrafeSpeed = 2.1f, CloseSpeed = 4.2f, BackOffSpeed = 2.4f;

        // Who swings / shoots at whom right now (host). Fighters ask for a turn; most two per lifeguard.
        private static readonly Dictionary<PlayerHub, List<StoryNpc>> MeleeTurns = new(), ShotTurns = new();
        private static float _nextFightShout; // one pirate's shout at a time across a fight

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
        // The guard (host): up this frame (blows from the front can be blocked); covering up after two blows in a row.
        private bool _guardUp, _lastHitCommitted, _lastHitHeavy;
        private float _coverUntil, _lastLandedOnUs = -10f, _nextSwingAt;
        private bool _squaredUp; // already at arm's length with this lifeguard (the first blow waits a moment)
        // Every machine: the fists up or a blow being drawn back (fist fighters), and when that wind-up started.
        private readonly FishNet.Object.Synchronizing.SyncVar<FightStance> _stance = new();
        private float _windUpShownAt = -10f, _windUpShownFor = 0.5f, _blockShownAt = -10f;

        // What's in their hand and what they're doing with it, on every machine (NpcWeaponView draws it).
        private readonly FishNet.Object.Synchronizing.SyncVar<NpcWeaponKind> _weaponKind = new();
        private readonly FishNet.Object.Synchronizing.SyncVar<bool> _armed = new();
        private readonly FishNet.Object.Synchronizing.SyncVar<NpcGunState> _gunState = new();
        private readonly FishNet.Object.Synchronizing.SyncVar<Vector3> _aimAt = new();
        private NpcWeaponView _weaponView;

        /// <summary>What a gunman is doing with the gun (the arms and the gun follow it on every machine).</summary>
        public enum NpcGunState : byte { Lowered, Aiming, Reloading }

        public bool IsFighting => _fighting;
        public FighterProfile Fighter => _fighter;
        /// <summary>Every machine: the weapon this character fights with (only meaningful while <see cref="IsArmed"/>).</summary>
        public NpcWeaponKind WeaponKind => _weaponKind.Value;
        public bool IsArmed => _armed.Value;
        public NpcGunState GunState => _gunState.Value;
        /// <summary>Every machine: where the gun points while <see cref="GunState"/> is Aiming.</summary>
        public Vector3 AimPoint => _aimAt.Value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFightStatics()
        {
            MeleeTurns.Clear();
            ShotTurns.Clear();
            _nextFightShout = 0f;
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
            _coverUntil = 0f;
        }

        [Server]
        public void ServerStopFighting()
        {
            if (!_fighting) return;
            LogTally("stopped");
            _fighting = false;
            _armed.Value = _guarding && _guardFights; // a guard keeps carrying his gun
            SetGunState(NpcGunState.Lowered, Vector3.zero);
            _fightGoal = null;
            _foe = null;
            ReleaseTurns(this);
            _guardUp = false;
            SetStance(FightStance.None);
        }

        // ------------------------------------------------------------------ thinking (host, every frame)

        /// <summary>Host: one step of the fight. Called from the brain instead of the role's own behaviour.</summary>
        private void FightThink(float dt)
        {
            _guardUp = false; // (each step below raises it where the fists are up)
            FightStep(dt);
            bool fists = _fighter.Weapon.Kind == NpcWeaponKind.Fists && !IsDefeated && _pose.Value == AvatarPose.Normal;
            SetStance(!fists ? FightStance.None : _fight == FightState.WindUp ? FightStance.WindUp : _guardUp ? FightStance.Guard : FightStance.None);
        }

        private void SetStance(FightStance stance)
        {
            if (_stance.Value != stance) _stance.Value = stance;
        }

        private void FightStep(float dt)
        {
            if (IsDefeated)
            {
                if (_fight != FightState.Idle) LogTally("down for good");
                _fight = FightState.Idle;
                SetGunState(NpcGunState.Lowered, Vector3.zero);
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
                _guardUp = _fight != FightState.Recover && _fighter.Style != FightStyle.Drunk; // a flinch, not an opening
                return;
            }

            if (RivalThink(dt)) return; // fighting another character (a grudge, a bar brawl: StoryNpc.Rivals)
            PickFoe();
            if (_foe == null)
            {
                SetGunState(NpcGunState.Lowered, Vector3.zero);
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
                GestureObservers(AvatarGesture.Punch, FacePoint(_foe.transform, _foe.Head.position, _foe.Head.forward));
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
                    SetGunState(NpcGunState.Reloading, Vector3.zero);
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
            SetGunState(NpcGunState.Lowered, Vector3.zero);
            if (distance > 9f || !see)
            {
                Approach();
                return;
            }
            _guardUp = distance < RingRadius + 1.5f; // squared up
            if (Time.time < _coverUntil)
            {
                // Covering up after two blows in a row: guard high, half a step back, no swing for a moment.
                _fight = FightState.Hold;
                Goal(p - dir * 0.5f, BackOffSpeed);
                return;
            }
            int maxSwinging = _fighter.Style == FightStyle.Brawler ? 1 : 2;
            if (TakeTurn(MeleeTurns, _foe, maxSwinging))
            {
                // Our turn: close in and swing as soon as they are in reach and roughly in front (unless the other one
                // is already winding up: one blow at a time, the second waits just out of reach, guard up).
                float reach = w.Reach * (_fighter.Style == FightStyle.Drunk ? 0.9f : 1f);
                // Coming in (not a counter): square up a moment at arm's length before the first blow (walking straight
                // into a swing left them wide open to a flurry).
                if (distance > reach * 1.8f) _squaredUp = false;
                else if (!_squaredUp && distance <= reach * 1.25f)
                {
                    _squaredUp = true;
                    if (_comboChain == 0) _nextSwingAt = Mathf.Max(_nextSwingAt, Time.time + GuardPause() * 0.7f);
                }
                if (Time.time < _nextSwingAt)
                {
                    // Back in the guard a moment between blows (they used to swing, recover and swing again at once, so
                    // the guard was never up when it mattered): a step round them at arm's length, fists up.
                    _fight = FightState.Close;
                    Vector3 round = Vector3.Cross(Vector3.up, dir) * (_orbitSign * 0.35f);
                    Goal(Spaced(_foe.transform.position - dir * reach * 1.05f + round), StrafeSpeed);
                    return;
                }
                bool otherSwinging = OtherWindingUp(_foe);
                if (!otherSwinging && distance <= reach * 0.95f && Vector3.Angle(transform.forward, dir) < 35f)
                {
                    StartWindUp(w);
                    return;
                }
                _fight = FightState.Close;
                Goal(Spaced(_foe.transform.position - dir * reach * (otherSwinging ? 1.3f : 0.75f)), _fighter.Style == FightStyle.Drunk ? 2.6f : CloseSpeed);
                return;
            }
            // Waiting for a turn: circle well back, spread out, facing them (and now and then egging the others on).
            _fight = FightState.Hold;
            Orbit(RingRadius, dt);
            if (_fighter.Style == FightStyle.Pirate && Time.time >= _nextFightShout && Random.value < 0.4f * dt)
            {
                _nextFightShout = Time.time + Random.Range(4f, 7f);
                ShoutObservers(PirateShouts[Random.Range(0, PirateShouts.Length)], false);
            }
        }

        /// <summary>
        /// Where a punch at someone's head lands: on the side of the head facing us, a little under the eyes (aimed at the
        /// eyes themselves, a blow from the side went through the goofy lifeguard's big head to reach his face).
        /// </summary>
        private Vector3 FacePoint(Transform body, Vector3 eyes, Vector3 facing)
        {
            AvatarRig rig = body.GetComponentInChildren<AvatarRig>();
            float half = rig != null && rig.HeadHalfWidth > 0.02f ? rig.HeadHalfWidth : 0.1f;
            Vector3 fwd = Vector3.ProjectOnPlane(facing, Vector3.up);
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : body.forward;
            Vector3 centre = eyes - fwd * (half * 0.8f);
            Vector3 toUs = Vector3.ProjectOnPlane(HeadPosition - centre, Vector3.up);
            toUs = toUs.sqrMagnitude > 1e-4f ? toUs.normalized : fwd;
            return centre + toUs * (half + 0.04f) - Vector3.up * 0.04f;
        }

        /// <summary>Someone else on this lifeguard is winding up a blow right now.</summary>
        private bool OtherWindingUp(PlayerHub foe)
        {
            if (!MeleeTurns.TryGetValue(foe, out List<StoryNpc> list)) return false;
            foreach (StoryNpc n in list)
                if (n != null && n != this && n._fight == FightState.WindUp) return true;
            return false;
        }

        /// <summary>A spot nudged clear of the other fighters on their feet (they bunched up into one heap).</summary>
        private Vector3 Spaced(Vector3 spot)
        {
            foreach (StoryNpc other in _all)
            {
                if (other == this || other == null || !other._fighting || other.IsDefeated) continue;
                Vector3 d = spot - other.transform.position;
                d.y = 0f;
                float m = d.magnitude;
                if (m < Elbow) spot += (m > 0.01f ? d / m : transform.right) * (Elbow - m);
            }
            return spot;
        }

        private void ThinkGun(NpcWeapon w, Vector3 p, Vector3 dir, float distance, bool see, float dt)
        {
            if (!see || distance > w.MaxRange)
            {
                ReleaseTurn(ShotTurns, this);
                SetGunState(NpcGunState.Lowered, Vector3.zero);
                Approach();
                return;
            }
            if (_ammo <= 0)
            {
                // A flintlock is loaded by hand after every shot: the moment to rush them.
                ReleaseTurn(ShotTurns, this);
                SetGunState(NpcGunState.Reloading, Vector3.zero);
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

            Vector3 chest = _foe.transform.position + Vector3.up * 1.15f;
            if (!TakeTurn(ShotTurns, _foe, 2))
            {
                // Someone else has the shot: drift sideways to a new angle, gun up and on them.
                SetGunState(NpcGunState.Aiming, chest);
                Orbit(Mathf.Clamp(distance, w.MinRange, w.MaxRange * 0.7f), dt);
                return;
            }
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
            SetGunState(NpcGunState.Aiming, _aimPoint);
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
            SetStance(_fighter.Weapon.Kind == NpcWeaponKind.Fists ? FightStance.WindUp : FightStance.None);
            // The arm goes back now and the jab plays out as the blow lands (the gesture is the punch we have).
            _gestureAt = Mathf.Max(Time.time, _fightUntil - 0.2f);
            _fightGoal = null;
            WindUpObservers(windUp);
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
                    combat.ServerStun(PlayerCombat.HitStun); // rocked: no punching straight back through it
                }
            }
            if (landed) _landed++;
            else
            {
                _missed++;
                if (!StrayBlow(w)) WhiffObservers(); // (or it lands on whoever else is in front)
            }
            float recover = w.Recover * (_fighter.Style == FightStyle.Drunk ? 1.5f : 1f) * (landed ? 1f : 1.25f); // a miss leaves them open longer
            SetFight(FightState.Recover, recover);
        }

        private void AfterRecover()
        {
            // A cage fighter often follows up; the others give someone else a go.
            bool combo = _fighter.Style == FightStyle.Brawler && _comboChain < 2 && Random.value < 0.35f + _fighter.Skill * 0.3f;
            _comboChain = combo ? _comboChain + 1 : 0;
            if (!combo) _nextSwingAt = Time.time + GuardPause();
            if (!combo && _fighter.Style != FightStyle.Brawler) ReleaseTurn(MeleeTurns, this);
            SetFight(FightState.Idle, 0f);
        }

        /// <summary>How long a fighter stays in the guard before the next blow.</summary>
        private float GuardPause() => _fighter.Style switch
        {
            FightStyle.Brawler => Random.Range(0.35f, 0.7f),
            FightStyle.Drunk => Random.Range(0.8f, 1.6f),
            _ => Random.Range(0.6f, 1.2f)
        };

        private void Fire(NpcWeapon w, float distance)
        {
            _shots++;
            _ammo--;
            Vector3 muzzle = Muzzle;
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
            Collider hitCollider = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _brainHits[i];
                if (h.distance >= best || IsOwnCollider(h.collider)) continue;
                best = h.distance;
                end = h.point;
                hitCollider = h.collider;
                hitPlayer = h.collider.GetComponentInParent<PlayerHub>();
            }
            if (hitPlayer != null && hitPlayer.TryGetComponent(out PlayerCombat combat) && !combat.IsDowned)
            {
                hitFoe = true;
                _shotsLanded++;
                combat.ServerDamage(w.ShotDamage, Name, muzzle);
                combat.ServerKnockback(dir * 1.5f, Name);
            }
            else if (hitPlayer == null) StrayShot(hitCollider, w.ShotDamage, end, dir); // another pirate, an animal
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
            if (_lastHitBlocked || _lastHitCommitted || Time.time < _staggerFreeUntil) return false;
            // A jab into the guard is shrugged off (it used to stagger them, and the guard came down with it: a masher
            // stun-locked anyone); a heavy blow, or anything that catches them open after a swing, rocks them.
            if (!_lastHitHeavy && _fight != FightState.Recover && _fighter.Style != FightStyle.Drunk) return false;
            _staggerFreeUntil = Time.time + (_fighter.Style == FightStyle.Brawler ? 1.5f : 1f);
            return true;
        }

        /// <summary>Host: a fighter is hit. Returns the damage it really takes (a guard soaks most of it).</summary>
        private int FightOnHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 direction)
        {
            _lastHitBlocked = false;
            _lastHitCommitted = false;
            _lastHitHeavy = damage >= 35; // a hook, an uppercut, an overhand (a jab is 25)
            if (!_fighting || IsDefeated) return damage;
            // Turn on whoever hit us (mostly).
            if (attacker != null && attacker != _foe && (_foe == null || Random.value < 0.6f)) _foe = attacker;

            // The guard: with the fists up, a blow from the front is often blocked (a heavy one, a hook or an uppercut,
            // gets through more often). Never mid-swing, and not after their own swing: that's the opening.
            bool brawler = _fighter.Style == FightStyle.Brawler;
            Vector3 from = attacker != null ? attacker.transform.position - transform.position : -direction;
            from.y = 0f;
            bool front = Vector3.Angle(transform.forward, from) < 65f;
            // (Winding up, the other hand still guards the chin: jabs mostly bounce off it. Step back from the blow, or
            // break through with a hook or an uppercut.)
            float guard = _fight == FightState.WindUp ? _fighter.Guard * 0.9f : _guardUp ? _fighter.Guard : 0f;
            if (guard > 0f && front && kind is DamageKind.Punch or DamageKind.Blade && attacker != null &&
                Random.value < guard * (_lastHitHeavy ? 0.8f : 1f))
            {
                _blocked++;
                _lastHitBlocked = true;
                BlockObservers(HeadPosition);
                if (attacker.TryGetComponent(out PlayerCombat theirs)) theirs.ServerStun(_lastHitHeavy ? PlayerCombat.BlockedRecoil + 0.15f : PlayerCombat.BlockedRecoil, blocked: true); // their arm bounces off (a big swing further)
                // Blocking sets up a quick counter: the next swing comes at once and fast.
                if (_foe == attacker && _fight is not (FightState.WindUp or FightState.Down) && TakeTurn(MeleeTurns, attacker, brawler ? 1 : 2))
                {
                    _comboChain = 1;
                    _coverUntil = 0f;
                    _nextSwingAt = 0f;
                    SetFight(FightState.Idle, 0f);
                }
                return 0;
            }

            // Hit mid wind-up: a heavy blow early in it stops it; a jab doesn't, and late in it they swing through.
            // (Not a cage fighter's: they take it and swing anyway.)
            if (_fight == FightState.WindUp)
            {
                float wound = (Time.time - _fightStart) / Mathf.Max(0.01f, _fightUntil - _fightStart);
                if (!brawler && _lastHitHeavy && wound < 0.6f && Time.time >= _staggerFreeUntil)
                {
                    _gestureAt = -1f;
                    SetFight(FightState.Recover, 0.4f);
                }
                else _lastHitCommitted = true;
            }
            // Two blows landing in a row from the front: they cover up and back off a moment (a drunk just takes it).
            else if (front && _fighter.Style == FightStyle.Pirate && Time.time - _lastLandedOnUs < 1f) _coverUntil = Time.time + 0.9f;
            if (front) _lastLandedOnUs = Time.time;

            // Enough at once knocks them over (they get back up if there's any fight left in them). A hook or an uppercut
            // that catches them open after their own swing counts double: the timed big one puts them on the sand.
            if (Time.time - _poiseSince > 1.3f)
            {
                _poiseSince = Time.time;
                _poiseDamage = 0f;
            }
            _poiseDamage += damage * (_lastHitHeavy && _fight == FightState.Recover ? 2f : 1f);
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
            Transform facing = RivalValid() ? _rival.transform : _foe != null ? _foe.transform : null;
            if (facing != null)
            {
                Vector3 face = facing.position - transform.position;
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
            // Drift toward the slot round the near side, not straight through the lifeguard, clear of the others.
            Goal(Spaced(spot), StrafeSpeed);
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
            Vector3 muzzle = Muzzle;
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
        private void WindUpObservers(float seconds)
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.KnifeSwish, 0.35f);
            if (_weaponView != null) _weaponView.OnWindUp(seconds);
            _windUpShownAt = Time.time;
            _windUpShownFor = seconds;
        }

        /// <summary>Every machine: how far a fist fighter has drawn the blow back (all the way as the punch starts, 0.2 s
        /// before it lands).</summary>
        private float WindUpDrawn => Mathf.Clamp01((Time.time - _windUpShownAt) / Mathf.Max(0.08f, _windUpShownFor - 0.2f));

        /// <summary>Every machine: the stance (fists up, a blow drawn back), for the animator and tests.</summary>
        public FightStance Stance => _stance.Value;

        [ObserversRpc]
        private void WhiffObservers()
        {
            if (_audio != null) _audio.PlayOneShot(BeachAudio.PunchSwoosh, 0.6f);
        }

        [ObserversRpc]
        private void BlockObservers(Vector3 at)
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Thump, 0.9f);
            // (Once in a while, not on every blocked jab of a flurry: a stack of words over one head.)
            if (Time.time - _blockShownAt < 1.2f) return;
            _blockShownAt = Time.time;
            UI.FloatingText.Spawn(at + Vector3.up * 0.3f, "BLOCKED", new Color(0.7f, 0.85f, 1f), 0.8f, 0.8f);
        }

        [ObserversRpc]
        private void ShotObservers(Vector3 muzzle, Vector3 end, byte sound, bool hit)
        {
            AudioSource.PlayClipAtPoint(ProceduralAudio.Shot((GunSound)sound), muzzle, 1f);
            // From the gun this machine draws (it can sit a little off the host's): the flash, a cloud of black powder
            // smoke and the ball's tracer (with its puff, hole and crack where it lands).
            Vector3 from = _weaponView != null && _weaponView.Muzzle is { } drawn && (drawn - muzzle).sqrMagnitude < 1f ? drawn : muzzle;
            if (_weaponView != null) _weaponView.OnShot();
            Vector3 path = end - from;
            float length = path.magnitude;
            if (length < 0.05f) return;
            Vector3 dir = path / length;
            ProjectileSystem fx = ProjectileSystem.Instance;
            fx.MuzzleFlash(from, dir, 2.6f, false);
            fx.GunSmoke(from, dir, (GunSound)sound == GunSound.Rifle ? 2.1f : 1.6f);
            fx.Fire(null, null, false, from, from, new[] { dir }, 140f, 0f, ProjectileKind.Bullet, 0, 0f, length + 0.4f);
        }

        // ------------------------------------------------------------------ the weapon in hand

        /// <summary>Where this fighter's shots come from: the drawn gun's muzzle, or about there without one.</summary>
        private Vector3 Muzzle => _weaponView != null && _weaponView.Muzzle is { } m
            ? m
            : HeadPosition - Vector3.up * 0.25f + transform.forward * 0.45f;

        /// <summary>Host: what the gun is doing, synced only when it really changes (the aim point moves every frame).</summary>
        private void SetGunState(NpcGunState state, Vector3 aim)
        {
            if (_gunState.Value != state) _gunState.Value = state;
            if (state != NpcGunState.Aiming) return;
            if ((_aimAt.Value - aim).sqrMagnitude > 0.12f * 0.12f) _aimAt.Value = aim;
        }

        /// <summary>Every machine: draw the weapon once there's one to hold.</summary>
        private void UpdateWeaponView()
        {
            if (_weaponView != null || !_armed.Value || _weaponKind.Value == NpcWeaponKind.Fists) return;
            if (_animator == null || _rig == null) return;
            _weaponView = gameObject.AddComponent<NpcWeaponView>();
            _weaponView.Init(this, _animator, _rig);
        }
    }
}
