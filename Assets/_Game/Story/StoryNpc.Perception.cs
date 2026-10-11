using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Player;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Guards (host), plan C5: a pirate on watch walks a patrol, stops at each point to look about, and watches with a
    /// cone of sight (<see cref="Perception"/>). Something half-seen or a quiet noise: they stop and stare, then come
    /// over to look. Sure: "INTRUDER!", the alarm (<see cref="Stealth.ServerSpotted"/>), nearby guards join in, and
    /// an armed guard fights. Everyone sees the meter over their head ("?" filling, then "!") through <see cref="Alert"/>.
    /// </summary>
    public partial class StoryNpc : ISentry
    {
        private enum GuardState : byte { Patrol, Pause, Stare, Investigate, Alarmed }

        // 0..100: suspicion in percent; AlertAlarmed: raised the alarm.
        public const byte AlertAlarmed = 255;
        private readonly SyncVar<byte> _alert = new SyncVar<byte>();

        private bool _guarding;
        private Perception _eyes;
        private GuardState _guard;
        private readonly List<Vector3> _patrol = new();
        private int _patrolIndex;
        private float _guardUntil, _lookAroundYaw;
        private FighterProfile _guardFighter;
        private bool _guardFights;
        private string _alarmReason;
        private bool _lookAbout = true;

        /// <summary>Every machine: 0..1 how suspicious this guard is; 1 and <see cref="IsAlarmed"/> once it raised the alarm.</summary>
        public float Alert => _alert.Value == AlertAlarmed ? 1f : _alert.Value / 100f;
        public bool IsAlarmed => _alert.Value == AlertAlarmed;
        public bool IsGuarding => _guarding;
        public Vector3 SentryPosition => HeadPosition;

        /// <summary>
        /// Host: stand guard along <paramref name="patrol"/> (one point: stand there and look about). Once alarmed an
        /// armed guard (<paramref name="fighter"/> given) fights; otherwise they stand pointing and shouting.
        /// </summary>
        [Server]
        public void ServerGuard(IList<Vector3> patrol, FighterProfile? fighter = null, SightSettings? sight = null, bool lookAbout = true)
        {
            _lookAbout = lookAbout;
            _lookAroundYaw = transform.eulerAngles.y;
            _guarding = true;
            _patrol.Clear();
            if (patrol != null) _patrol.AddRange(patrol);
            if (_patrol.Count == 0) _patrol.Add(transform.position);
            _patrolIndex = 0;
            _guardFights = fighter.HasValue;
            if (fighter.HasValue) _guardFighter = fighter.Value;
            _eyes = new Perception(sight ?? SightSettings.Guard, IsOwnCollider);
            _alert.Value = 0;
            _alarmReason = null;
            SetGuard(GuardState.Pause, Random.Range(0.5f, 2f));
            Stealth.Register(this);
        }

        [Server]
        public void ServerStopGuarding()
        {
            if (!_guarding) return;
            _guarding = false;
            Stealth.Unregister(this);
            _alert.Value = 0;
        }

        /// <summary>Host (missions restarting): calm again, back to the patrol.</summary>
        [Server]
        public void ServerCalmDown()
        {
            if (!_guarding) return;
            _eyes.Reset();
            _alert.Value = 0;
            _alarmReason = null;
            if (_fighting) ServerStopFighting();
            SetGuard(GuardState.Pause, 1f);
        }

        public void ServerHear(Vector3 at, NoiseKind kind, PlayerHub by)
        {
            if (!_guarding || _eyes == null || _eyes.Alarmed || IsDefeated) return;
            float distance = Vector3.Distance(at, HeadPosition);
            switch (kind)
            {
                case NoiseKind.Gunshot when distance > Stealth.SuppressedShot + 0.5f:
                    // Only a loud shot carries this far: everyone on the island knows.
                    Alarm(by, at, "heard a gunshot");
                    return;
                case NoiseKind.Gunshot:
                    _eyes.Notice(at, 0.55f, by);
                    break;
                case NoiseKind.Alarm:
                    Alarm(by, at, "heard the alarm");
                    return;
                case NoiseKind.Fight:
                    _eyes.Notice(at, 0.45f, by);
                    break;
                default: // footsteps
                    if (Stealth.IsDisguised(by)) return; // a pirate walking by is nothing new
                    _eyes.Notice(at, 0.1f, by);
                    break;
            }
            if (_guard is GuardState.Patrol or GuardState.Pause) SetGuard(GuardState.Stare, Random.Range(1.2f, 2f));
        }

        // ------------------------------------------------------------------ thinking (host, from BrainUpdate)

        private void GuardThink(float dt)
        {
            if (IsDefeated)
            {
                ServerStopGuarding();
                return;
            }
            Vector3 p = transform.position;
            if (_guard != GuardState.Alarmed && _eyes.Tick(HeadPosition, transform.forward, dt))
            {
                Alarm(_eyes.Suspect, _eyes.Interest ?? p, "spotted you");
                return;
            }
            byte shown = _eyes.Alarmed ? AlertAlarmed : (byte)Mathf.RoundToInt(_eyes.Suspicion * 100f);
            if (Mathf.Abs(shown - _alert.Value) >= 3 || (shown == 0) != (_alert.Value == 0) || shown == AlertAlarmed) _alert.Value = shown;

            bool suspicious = _eyes.Suspicion >= _eyes.Sight.SuspiciousAt && _eyes.Interest.HasValue && Time.time - _eyes.InterestAt < 6f;
            switch (_guard)
            {
                case GuardState.Alarmed:
                    if (!_guardFights && _eyes.Suspect != null) ServerFace(_eyes.Suspect.transform.position);
                    return;
                case GuardState.Patrol:
                case GuardState.Pause:
                    if (suspicious)
                    {
                        ServerStop();
                        SetGuard(GuardState.Stare, Random.Range(1f, 1.6f));
                        if (Random.value < 0.6f) ShoutObservers(Random.value < 0.5f ? "Eh? Who's there?" : "Hm?", false);
                        break;
                    }
                    Patrol(p, dt);
                    break;
                case GuardState.Stare:
                    // Stop and look straight at it; still there after a moment, go and see.
                    if (_eyes.Interest.HasValue) ServerFace(_eyes.Interest.Value);
                    TurnToward((_eyes.Interest ?? p + transform.forward) - p, 200f, dt);
                    if (Time.time < _guardUntil) break;
                    if (_eyes.Suspicion >= _eyes.Sight.SuspiciousAt && _eyes.Interest.HasValue)
                    {
                        ServerMoveTo(OnNavMesh(_eyes.Interest.Value, 2f), run: false);
                        SetGuard(GuardState.Investigate, 9f);
                    }
                    else ResumePatrol();
                    break;
                case GuardState.Investigate:
                    // Walking over; at the spot (or bored), a look round and back to the beat.
                    if (_eyes.Interest.HasValue && Time.time - _eyes.InterestAt < 0.5f && !_moveTarget.HasValue)
                        ServerMoveTo(OnNavMesh(_eyes.Interest.Value, 2f), run: false);
                    if (_eyes.Suspicion < 0.08f || Time.time > _guardUntil)
                    {
                        if (Random.value < 0.7f) ShoutObservers(Random.value < 0.5f ? "Must've been the wind." : "Bah. Monkeys.", false);
                        ResumePatrol();
                    }
                    break;
            }
        }

        private void Patrol(Vector3 p, float dt)
        {
            if (_guard == GuardState.Pause)
            {
                // Looking about: a slow turn one way, then the other.
                TurnToward(Quaternion.Euler(0f, _lookAroundYaw, 0f) * Vector3.forward, 60f, dt);
                if (Time.time < _guardUntil) return;
                if (_patrol.Count <= 1)
                {
                    if (_lookAbout) _lookAroundYaw += Random.Range(60f, 140f) * (Random.value < 0.5f ? -1f : 1f);
                    SetGuard(GuardState.Pause, Random.Range(2.5f, 4.5f));
                    return;
                }
                _patrolIndex = (_patrolIndex + 1) % _patrol.Count;
                ServerFace(null);
                ServerMoveTo(_patrol[_patrolIndex], run: false);
                SetGuard(GuardState.Patrol, 30f);
                return;
            }
            if (_moveTarget.HasValue && Time.time < _guardUntil) return;
            // There (or stuck): stop and look about.
            ServerStop();
            _lookAroundYaw = transform.eulerAngles.y + (_lookAbout ? Random.Range(-70f, 70f) : 0f);
            SetGuard(GuardState.Pause, Random.Range(2f, 4f));
        }

        private void ResumePatrol()
        {
            ServerStop();
            ServerFace(null);
            _lookAroundYaw = transform.eulerAngles.y;
            SetGuard(GuardState.Pause, Random.Range(1f, 2f));
        }

        private void SetGuard(GuardState state, float seconds)
        {
            _guard = state;
            _guardUntil = Time.time + seconds;
        }

        /// <summary>Host: sure now. Shout, set the others off, report it, fight if armed.</summary>
        private void Alarm(PlayerHub who, Vector3 at, string reason)
        {
            if (_guard == GuardState.Alarmed) return;
            _eyes.ForceAlarm(who, at);
            _alert.Value = AlertAlarmed;
            _alarmReason = reason;
            SetGuard(GuardState.Alarmed, 0f);
            ServerStop();
            _mood.Value = AvatarMood.Angry;
            ShoutObservers(reason == "spotted you" ? "INTRUDER! Over here!" : "ALARM! To arms!", true);
            GestureObservers(AvatarGesture.Wave, at);
            Stealth.RaiseSpotted(Name, who, reason);
            // The shout carries: the other guards near enough are on their feet too.
            Stealth.ServerNoise(HeadPosition, Stealth.AlarmShout, who, NoiseKind.Alarm);
            if (_guardFights && who != null) ServerFight(_guardFighter, transform.position);
        }

        /// <summary>Host: hit by a lifeguard while on watch: that settles it.</summary>
        private void GuardOnHit(PlayerHub attacker)
        {
            if (_guarding && _guard != GuardState.Alarmed && attacker != null) Alarm(attacker, attacker.transform.position, "was attacked");
        }
    }
}
