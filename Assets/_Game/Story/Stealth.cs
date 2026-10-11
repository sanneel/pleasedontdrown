using System;
using System.Collections.Generic;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    public enum NoiseKind : byte { Footsteps, Fight, Gunshot, Alarm }

    /// <summary>Anything that watches and listens for lifeguards (pirate guards, parrot sentries).</summary>
    public interface ISentry
    {
        /// <summary>Host: a noise of <paramref name="kind"/> at <paramref name="at"/>, made by <paramref name="by"/> (or nobody).</summary>
        void ServerHear(Vector3 at, NoiseKind kind, PlayerHub by);
        Vector3 SentryPosition { get; }
    }

    /// <summary>
    /// Stealth (host), Docs/16-Plan-Islands-2-3.md C5. Sentries (<see cref="ISentry"/>) look with a cone of sight and
    /// listen for noises. What they see fills a suspicion meter (faster close up, in plain view, when you run; slowly or
    /// not at all in a pirate disguise); full, they raise the alarm (<see cref="ServerSpotted"/>: a stealth mission
    /// fails, guards come at you). A loud gunshot is heard ~60 m off and raises the alarm at once; a suppressed one
    /// carries ~4 m; running feet a few metres.
    /// </summary>
    public static class Stealth
    {
        public const float LoudShot = 60f, SuppressedShot = 4f, Running = 7f, Walking = 2.5f, FightNoise = 12f, AlarmShout = 28f;

        private static readonly List<ISentry> Sentries = new();

        /// <summary>Host: a sentry raised the alarm: who saw/heard whom, and why ("A parrot saw you!").</summary>
        public static event Action<string, PlayerHub, string> ServerSpotted;

        /// <summary>Host (tests): every noise made, and how many sentries could hear it.</summary>
        public static event Action<Vector3, NoiseKind, float, int> ServerNoiseMade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Sentries.Clear();
            ServerSpotted = null;
            ServerNoiseMade = null;
        }

        public static void Register(ISentry sentry)
        {
            if (!Sentries.Contains(sentry)) Sentries.Add(sentry);
        }

        public static void Unregister(ISentry sentry) => Sentries.Remove(sentry);

        /// <summary>Host: something made a noise <paramref name="radius"/> m loud here.</summary>
        public static void ServerNoise(Vector3 at, float radius, PlayerHub by, NoiseKind kind)
        {
            int heard = 0;
            for (int i = Sentries.Count - 1; i >= 0; i--)
            {
                ISentry s = Sentries[i];
                if (s == null || s is UnityEngine.Object o && o == null)
                {
                    Sentries.RemoveAt(i);
                    continue;
                }
                if ((s.SentryPosition - at).sqrMagnitude > radius * radius) continue;
                heard++;
                s.ServerHear(at, kind, by);
            }
            ServerNoiseMade?.Invoke(at, kind, radius, heard);
        }

        public static void RaiseSpotted(string sentry, PlayerHub who, string reason)
        {
            Debug.Log($"[Stealth] ALARM: {sentry} {reason} ({(who != null ? who.DisplayName : "nobody in particular")})");
            ServerSpotted?.Invoke(sentry, who, reason);
        }

        /// <summary>Is this lifeguard wearing the pirate disguise (host and every machine)?</summary>
        public static bool IsDisguised(PlayerHub p) => p != null && p.TryGetComponent(out PlayerCombat c) && c.IsDisguised;
    }

    /// <summary>How a sentry sees: how far, how wide, how quick to catch on.</summary>
    [Serializable]
    public struct SightSettings
    {
        public float Range;          // full sight distance (m)
        public float HalfAngle;      // half the cone (degrees)
        public float CloseRange;     // this close, seen even a bit outside the cone (m)
        public float FillPerSecond;  // suspicion per second for someone in plain view at mid range
        public float DecayPerSecond; // suspicion lost per second with nobody in sight
        public float DisguiseFactor; // multiplies the fill for a disguised lifeguard (0: a disguise fools it completely)
        public float SuspiciousAt;   // from here the sentry turns to look / comes over

        public static SightSettings Guard => new()
        {
            Range = 22f, HalfAngle = 55f, CloseRange = 3.5f, FillPerSecond = 0.8f, DecayPerSecond = 0.18f,
            DisguiseFactor = 0.08f, SuspiciousAt = 0.3f
        };

        /// <summary>A parrot on a perch or circling: sharp eyes, wide view, quick to squawk; a disguise fools it.</summary>
        public static SightSettings Parrot => new()
        {
            Range = 18f, HalfAngle = 75f, CloseRange = 5f, FillPerSecond = 1.15f, DecayPerSecond = 0.22f,
            DisguiseFactor = 0f, SuspiciousAt = 0.25f
        };
    }

    /// <summary>
    /// One sentry's eyes and suspicion (host). <see cref="Tick"/> it every frame with where its eyes are and which way
    /// they look; it fills on what it sees and reports the moment it is sure (<see cref="Alarmed"/>).
    /// </summary>
    public class Perception
    {
        public SightSettings Sight;
        public float Suspicion { get; private set; }
        public bool Alarmed { get; private set; }
        public PlayerHub Suspect { get; private set; }
        /// <summary>Where the sentry last saw or heard something worth a look.</summary>
        public Vector3? Interest { get; private set; }
        public float InterestAt { get; private set; } = -100f;

        private readonly RaycastHit[] _hits = new RaycastHit[10];
        private readonly Func<Collider, bool> _isOwn;

        public Perception(SightSettings sight, Func<Collider, bool> isOwnCollider)
        {
            Sight = sight;
            _isOwn = isOwnCollider;
        }

        public void Reset()
        {
            Suspicion = 0f;
            Alarmed = false;
            Suspect = null;
            Interest = null;
        }

        /// <summary>Host: raise the alarm now (heard a gunshot, a friend shouted, got hit).</summary>
        public void ForceAlarm(PlayerHub who, Vector3 at)
        {
            Suspicion = 1f;
            Alarmed = true;
            if (who != null) Suspect = who;
            Interest = at;
            InterestAt = Time.time;
        }

        /// <summary>Host: something worth a look (a quiet noise): a bump in suspicion and a place to look at.</summary>
        public void Notice(Vector3 at, float amount, PlayerHub who = null)
        {
            if (Alarmed) return;
            Suspicion = Mathf.Min(0.95f, Suspicion + amount);
            Interest = at;
            InterestAt = Time.time;
            if (who != null && Suspect == null) Suspect = who;
        }

        /// <summary>Host, every frame. True the frame the sentry becomes sure (raise the alarm).</summary>
        public bool Tick(Vector3 eye, Vector3 forward, float dt)
        {
            if (Alarmed) return false;
            float best = 0f;
            PlayerHub bestWho = null;
            foreach (PlayerHub p in PlayerHub.All)
            {
                float rate = SeeRate(p, eye, forward);
                if (rate > best)
                {
                    best = rate;
                    bestWho = p;
                }
            }
            if (best > 0f)
            {
                Suspicion = Mathf.Min(1f, Suspicion + best * dt);
                Suspect = bestWho;
                Interest = bestWho.transform.position;
                InterestAt = Time.time;
            }
            else Suspicion = Mathf.Max(0f, Suspicion - Sight.DecayPerSecond * dt);
            if (Suspicion < 1f) return false;
            Alarmed = true;
            return true;
        }

        /// <summary>How fast seeing <paramref name="p"/> from here fills the meter (0: not seen).</summary>
        public float SeeRate(PlayerHub p, Vector3 eye, Vector3 forward)
        {
            if (p == null || !p.TryGetComponent(out PlayerCombat combat) || combat.IsDowned) return 0f;
            Vector3 body = p.transform.position + Vector3.up * 1.1f;
            Vector3 to = body - eye;
            float distance = to.magnitude;
            if (distance > Sight.Range || distance < 0.01f) return 0f;
            float angle = Vector3.Angle(forward, to);
            bool close = distance < Sight.CloseRange;
            if (angle > Sight.HalfAngle && !(close && angle < Sight.HalfAngle * 1.6f)) return 0f;
            // Something solid in the way of both the head and the chest: hidden.
            Vector3 head = p.Head != null ? p.Head.position : p.transform.position + Vector3.up * 1.6f;
            if (!Clear(eye, body, p) && !Clear(eye, head, p)) return 0f;

            // Plain view close up fills fast; at the edge of sight or of the cone, slowly.
            float nearness = Mathf.Lerp(2.2f, 0.35f, Mathf.InverseLerp(Sight.CloseRange, Sight.Range, distance));
            float centred = Mathf.Lerp(1f, 0.45f, Mathf.InverseLerp(Sight.HalfAngle * 0.3f, Sight.HalfAngle, angle));
            float speed = combat.ServerSpeed;
            float movement = speed > 4.5f ? 1.5f : speed < 2.2f ? 0.65f : 1f; // running catches the eye; creeping doesn't
            float rate = Sight.FillPerSecond * nearness * centred * movement;
            // In the disguise you're one of them at any distance; only standing right in their face for a while makes them wonder.
            if (combat.IsDisguised) rate = close ? rate * Sight.DisguiseFactor * 3f : 0f;
            return rate;
        }

        private bool Clear(Vector3 from, Vector3 to, PlayerHub target)
        {
            Vector3 d = to - from;
            float length = d.magnitude;
            int count = Physics.RaycastNonAlloc(from, d / length, _hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _hits[i].collider;
                if (_isOwn != null && _isOwn(c)) continue;
                if (c.GetComponentInParent<PlayerHub>() == target) continue;
                Rigidbody body = c.attachedRigidbody;
                if (body != null && (body.GetComponent<StoryNpc>() != null || body.GetComponent<PlayerHub>() != null)) continue; // people don't hide people
                if (body != null && !body.isKinematic && body.mass < 30f) continue; // small loose things
                if (c.GetComponentInParent<Creatures.Creature>() != null) continue;
                // Leaves and fronds: you can hide behind a bush, but not see through it either way. (Trees' trunks block.)
                return false;
            }
            return true;
        }
    }
}
