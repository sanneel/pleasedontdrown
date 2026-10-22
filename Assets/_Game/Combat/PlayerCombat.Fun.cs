using System.Collections.Generic;
using FishNet.Object;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Fun;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// Friendly fire that's fun: three quick punches on another lifeguard (or a hard throw to the head) knock them
    /// flat, seeing stars: they drop, birds tweet, their googly eyes spin and they can't move for a moment. Thrown
    /// things that hit people go BONK. And a jump off the tower into the sea gets a cannonball score.
    /// </summary>
    public partial class PlayerCombat
    {
        private const float ComboWindow = 3f, ComboDaze = 2.5f, BonkDazeImpact = 9f;

        private float _dazedUntil = float.NegativeInfinity;
        private bool _wasDazed;
        private float _dazeLength = 1f;
        private readonly Dictionary<PlayerHub, (int count, float time)> _combo = new();
        // Cannonball: where the feet were last frame against the water.
        private float _lastAboveWater = float.NegativeInfinity;
        private float _nextCannonball;

        /// <summary>Knocked flat and seeing stars (every machine).</summary>
        public bool IsDazed => Time.time < _dazedUntil;
        /// <summary>0 .. 1 through the daze (for the fall and getting up).</summary>
        public float Daze01 => IsDazed ? 1f - (_dazedUntil - Time.time) / _dazeLength : 1f;

        // ------------------------------------------------------------------ punches and the daze

        [Server]
        private void ServerCountCombo(PlayerHub other, PlayerCombat combat)
        {
            _combo.TryGetValue(other, out var c);
            c = Time.time - c.time < ComboWindow ? (c.count + 1, Time.time) : (1, Time.time);
            _combo[other] = c;
            if (c.count < 3 || combat.IsDazed) return;
            _combo[other] = (0, Time.time);
            combat.ServerDaze(ComboDaze, _hub.DisplayName, "punched you silly");
        }

        /// <summary>Host: knock this lifeguard flat for a while.</summary>
        [Server]
        public void ServerDaze(float seconds, string byWhom, string how) => DazeObservers(seconds, byWhom ?? string.Empty, how ?? string.Empty);

        [ObserversRpc]
        private void DazeObservers(float seconds, string byWhom, string how)
        {
            _dazeLength = Mathf.Max(0.5f, seconds);
            _dazedUntil = Time.time + _dazeLength;
            if (_audio != null)
            {
                _audio.PlayOneShot(FunSounds.SlideDown, 0.8f);
                _audio.PlayOneShot(FunSounds.Tweet, 0.6f);
            }
            Vector3 head = _hub.Head != null ? _hub.Head.position : transform.position + Vector3.up * 1.6f;
            AvatarRig rig = _hub.Avatar != null ? _hub.Avatar.Rig : null;
            if (rig != null)
            {
                if (rig.TryGetComponent(out AvatarFunny funny)) funny.Dizzy(_dazeLength);
                if (!IsOwner) DizzyStars.Spawn(rig[AvatarRig.Bone.Head], _dazeLength);
            }
            if (IsOwner)
            {
                if (_hub.Motor != null) _hub.Motor.Stun(_dazeLength);
            }
        }

        // ------------------------------------------------------------------ thrown things hitting people

        /// <summary>Thrower's machine (it simulates the flight): what they threw hit somebody.</summary>
        public void ReportBonk(NetworkObject target, Vector3 point, float impact)
        {
            if (!IsOwner || target == null) return;
            BonkServer(target, point, impact);
        }

        [ServerRpc]
        private void BonkServer(NetworkObject target, Vector3 point, float impact)
        {
            if (target == null || (target.transform.position - point).sqrMagnitude > 3f * 3f) return;
            impact = Mathf.Clamp(impact, 0f, 30f);
            if (target.TryGetComponent(out PlayerHub other) && other != _hub && other.TryGetComponent(out PlayerCombat combat))
            {
                Vector3 away = other.transform.position - point;
                away.y = 0f;
                combat.ServerKnockback(away.normalized * Mathf.Min(6f, impact * 0.4f) + Vector3.up * 1.5f, _hub.DisplayName);
                if (impact > BonkDazeImpact && !combat.IsDazed) combat.ServerDaze(1.8f, _hub.DisplayName, "bonked you on the head");
            }
            else if (target.TryGetComponent(out StoryNpc npc) && npc != null) { } // (the bonk is heard: no shouted words)
            BonkObservers(point, impact);
        }

        [ObserversRpc]
        private void BonkObservers(Vector3 point, float impact)
        {
            AudioSource.PlayClipAtPoint(ProceduralAudio.Bonk, point, 1f);
            if (impact > BonkDazeImpact) AudioSource.PlayClipAtPoint(FunSounds.Boing, point, 0.7f);
        }

        // ------------------------------------------------------------------ owner: the daze view and the cannonball

        private void UpdateFun()
        {
            if (!IsOwner) return;
            PlayerLook look = _hub.Look;
            if (look != null)
            {
                // Knocked flat: the view tips over and drops to the sand, sways, then gets back up.
                float k = 0f;
                if (IsDazed)
                {
                    float t = Daze01;
                    k = t < 0.15f ? t / 0.15f : t > 0.8f ? (1f - t) / 0.2f : 1f;
                    k = k * k * (3f - 2f * k);
                }
                // Down (health gone): lying on the sand, the view on its side and low, breathing slowly.
                float down = DownedViewK;
                if (down > k)
                {
                    look.ExtraRoll = down * (70f + Mathf.Sin(Time.time * 1.3f) * 3f);
                    look.ExtraDrop = down * 1.3f;
                }
                else
                {
                    look.ExtraRoll = k * (55f + Mathf.Sin(Time.time * 3.1f) * 10f);
                    look.ExtraDrop = k * 1.1f;
                }
                if (IsDazed && _hub.Motor != null) _hub.Motor.Stun(0.1f);
                // The daze over: getting up off the sand takes a moment (AvatarAnimator.GetUpTime) before we can run.
                if (_wasDazed && !IsDazed && _hub.Motor != null) _hub.Motor.Stun(0.9f);
                _wasDazed = IsDazed;
            }
            UpdateCannonball();
        }

        private void UpdateCannonball()
        {
            PlayerMotor motor = _hub.Motor;
            if (motor == null || !WaterSurface.Exists || motor.Seat != null) return;
            Vector3 feet = transform.position;
            float surface = WaterSurface.HeightAt(feet);
            if (feet.y > surface + 0.25f)
            {
                _lastAboveWater = Time.time;
                return;
            }
            // Just went in, falling fast (off the tower deck, the dock, a rock): rate the splash.
            float vy = motor.Velocity.y;
            if (Time.time - _lastAboveWater > 0.1f || vy > -8f || Time.time < _nextCannonball) return;
            if (Shore.WaterDepthAt(feet + Vector3.up * 2f) < 0.8f) return; // landing in the shallows isn't a dive
            _nextCannonball = Time.time + 2f;
            float score = Mathf.Lerp(5f, 10f, Mathf.InverseLerp(8f, 15f, -vy)) + Random.Range(-0.4f, 0.4f);
            CannonballServer(feet, Mathf.Clamp(score, 1f, 10f));
        }

        [ServerRpc]
        private void CannonballServer(Vector3 at, float score) => CannonballObservers(at, Mathf.Clamp(score, 0f, 10f));

        [ObserversRpc]
        private void CannonballObservers(Vector3 at, float score)
        {
            float surface = WaterSurface.Exists ? WaterSurface.HeightAt(at) : at.y;
            var p = new Vector3(at.x, surface, at.z);
            SplashFx.Spawn(p, 1.4f);
            for (int i = 0; i < 4; i++)
                SplashFx.Spawn(p + Quaternion.Euler(0f, i * 90f + 45f, 0f) * Vector3.forward * 0.8f, 0.7f);
            AudioSource.PlayClipAtPoint(score >= 8f ? FunSounds.Cheer : FunSounds.Whoop, p, 0.8f);
        }
    }
}
