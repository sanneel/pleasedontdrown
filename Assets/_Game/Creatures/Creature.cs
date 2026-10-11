using System.Collections.Generic;
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
    public enum CreatureKind : byte { None, Parrot, Monkey, Alligator, OldSnapper, Kleptopus }

    /// <summary>
    /// One body part of a creature: where a hit lands decides how much it counts (a turtle's shell next to nothing,
    /// its head a lot). The host picks the part nearest the point the shooter's machine says it hit.
    /// </summary>
    public class CreatureZone : MonoBehaviour
    {
        public string Part = "Body";
        public float Multiplier = 1f;
        public Collider Collider;
    }

    /// <summary>
    /// The island creatures (plan C6), one networked prefab for all of them (Resources/Creature): the kind is synced
    /// and every machine builds the body for it (greybox now; the Tripo models later). The host thinks for it
    /// (Creature.Parrot / .Monkey / .Alligator / .Snapper) and moves it (NetworkTransform). Health with hit zones,
    /// an alert meter like the guards', and for bosses a boss bar (<see cref="BossFight"/>).
    /// </summary>
    public partial class Creature : NetworkBehaviour, IDamageable
    {
        [SerializeField] private AudioSource _audio;
        [Tooltip("Paints for the greybox bodies (see CreaturePaint).")]
        [SerializeField] private Material[] _paints = System.Array.Empty<Material>();

        private readonly SyncVar<CreatureKind> _kind = new SyncVar<CreatureKind>();
        private readonly SyncVar<int> _health = new SyncVar<int>();
        private readonly SyncVar<int> _maxHealth = new SyncVar<int>();
        // 0..100 suspicion, 255 alarmed (sentries); what the body is doing (every machine animates from it).
        private readonly SyncVar<byte> _alert = new SyncVar<byte>();
        private readonly SyncVar<CreatureAct> _act = new SyncVar<CreatureAct>();
        private readonly SyncVar<string> _displayName = new SyncVar<string>();

        private static readonly List<Creature> _all = new();
        public static IReadOnlyList<Creature> All => _all;

        /// <summary>Host: a creature died / gave up (who did it, if anyone).</summary>
        public static event System.Action<Creature, PlayerHub> ServerKilled;

        private Vector3 _home;
        private float _actUntil;
        private bool _setUp;
        private Transform _model;
        private readonly List<CreatureZone> _zones = new();
        private readonly RaycastHit[] _hits = new RaycastHit[10];

        public CreatureKind Kind => _kind.Value;
        public string DisplayName => !string.IsNullOrEmpty(_displayName.Value) ? _displayName.Value : Kind switch
        {
            CreatureKind.OldSnapper => "Old Snapper",
            CreatureKind.Kleptopus => "Kleptopus",
            CreatureKind.Alligator => "Alligator",
            CreatureKind.Monkey => "Monkey",
            CreatureKind.Parrot => "Parrot",
            _ => "Creature"
        };
        public int Health => _health.Value;
        public int MaxHealth => _maxHealth.Value;
        public float Health01 => _maxHealth.Value > 0 ? _health.Value / (float)_maxHealth.Value : 0f;
        public bool IsDead => _act.Value == CreatureAct.Dead;
        public CreatureAct Act => _act.Value;
        public float Alert => _alert.Value == StoryNpc.AlertAlarmed ? 1f : _alert.Value / 100f;
        public bool IsAlarmed => _alert.Value == StoryNpc.AlertAlarmed;
        /// <summary>Where a marker or the alert sign goes.</summary>
        public Vector3 TopPosition => transform.position + Vector3.up * TopHeight;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            ServerKilled = null;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _all.Add(this);
            _kind.OnChange += (_, kind, _) => BuildBody(kind);
            BuildBody(_kind.Value);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _all.Remove(this);
            Stealth.Unregister(this);
            ServerReleaseGrab();
        }

        // ------------------------------------------------------------------ host API

        /// <summary>Host: become a <paramref name="kind"/> living round <paramref name="home"/>.</summary>
        [Server]
        public void ServerSetup(CreatureKind kind, Vector3 home, string displayName = null, int health = 0)
        {
            _home = home;
            _kind.Value = kind;
            _displayName.Value = displayName ?? string.Empty;
            int hp = health > 0 ? health : kind switch
            {
                CreatureKind.Parrot => 10,
                CreatureKind.Monkey => 40,
                CreatureKind.Alligator => 180,
                CreatureKind.OldSnapper => 900,
                CreatureKind.Kleptopus => 1100,
                _ => 50
            };
            if (kind is CreatureKind.OldSnapper or CreatureKind.Kleptopus) hp = BossFight.Scaled(hp);
            _maxHealth.Value = _health.Value = hp;
            _act.Value = CreatureAct.Idle;
            _alert.Value = 0;
            _setUp = true;
            BuildBody(kind);
            switch (kind)
            {
                case CreatureKind.Parrot: ParrotSetup(); break;
                case CreatureKind.Monkey: MonkeySetup(); break;
                case CreatureKind.Alligator: GatorSetup(); break;
                case CreatureKind.OldSnapper: SnapperSetup(); break;
                case CreatureKind.Kleptopus: KleptopusSetup(); break;
            }
            Debug.Log($"[Creature] {DisplayName} ({kind}, {hp} hp) at {transform.position:F1}");
        }

        /// <summary>Host (missions restarting): calm again, back home, healed.</summary>
        [Server]
        public void ServerReset()
        {
            if (IsDead) return;
            ServerReleaseGrab();
            _health.Value = _maxHealth.Value;
            _alert.Value = 0;
            _eyes?.Reset();
            transform.position = _home;
            SetAct(CreatureAct.Idle, 0.5f);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (IsServerInitialized && _setUp && !IsDead)
            {
                switch (_kind.Value)
                {
                    case CreatureKind.Parrot: ParrotThink(dt); break;
                    case CreatureKind.Monkey: MonkeyThink(dt); break;
                    case CreatureKind.Alligator: GatorThink(dt); break;
                    case CreatureKind.OldSnapper: SnapperThink(dt); break;
                    case CreatureKind.Kleptopus: KleptopusThink(dt); break;
                }
            }
            if (IsServerInitialized && _setUp && _kind.Value == CreatureKind.Monkey) HatPickup();
            AnimateBody(dt);
            UpdateGrabbedLocal(dt);
        }

        private void SetAct(CreatureAct act, float seconds)
        {
            if (_act.Value != act) _act.Value = act;
            _actUntil = Time.time + seconds;
        }

        private bool ActDone => Time.time >= _actUntil;

        // ------------------------------------------------------------------ moving (host)

        /// <summary>Walk/swim toward a point at <paramref name="speed"/>, turning to face the way; false once there.</summary>
        private bool MoveToward(Vector3 target, float speed, float dt, float turnRate = 360f, bool water = false, bool land = true)
        {
            Vector3 p = transform.position;
            Vector3 to = target - p;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance < 0.15f) return false;
            Vector3 dir = to / distance;
            Turn(dir, turnRate, dt);
            // Step along where the body faces (it can't slide sideways), slower while still turning.
            float facing = Mathf.Clamp01(Vector3.Dot(transform.forward, dir));
            Vector3 step = transform.forward * Mathf.Min(distance, speed * dt * Mathf.Lerp(0.25f, 1f, facing));
            Vector3 next = p + step;
            if (Blocked(p, step)) return true;
            float depth = Shore.WaterDepthAt(next + Vector3.up * 2f);
            if (!water && depth > 0.6f) return true;  // won't go into deep water
            if (!land && depth < 0.35f) return true;   // won't come out of it
            transform.position = Settle(next);
            return true;
        }

        /// <summary>On the ground, or at the water's surface (swimmers ride a little low).</summary>
        private Vector3 Settle(Vector3 p)
        {
            float ground = Shore.GroundHeightAt(p + Vector3.up * 2f);
            float surface = WaterSurface.Exists ? WaterSurface.HeightAt(p) : float.NegativeInfinity;
            float y = float.IsNaN(ground) ? p.y : ground;
            if (surface - SwimDepth > y) y = surface - SwimDepth;
            return new Vector3(p.x, y, p.z);
        }

        private bool Blocked(Vector3 p, Vector3 step)
        {
            float length = step.magnitude + BodyRadius;
            if (length < 1e-4f) return false;
            Vector3 from = p + Vector3.up * Mathf.Max(0.35f, TopHeight * 0.5f);
            int count = Physics.RaycastNonAlloc(from, step.normalized, _hits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _hits[i].collider;
                if (c.attachedRigidbody != null) continue; // people, things, ourselves: not walls
                if (Vector3.Angle(_hits[i].normal, Vector3.up) < 50f) continue; // a slope, not a wall
                return true;
            }
            return false;
        }

        private void Turn(Vector3 direction, float degreesPerSecond, float dt)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-6f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction.normalized), degreesPerSecond * dt);
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static PlayerHub NearestStanding(Vector3 from, float range, System.Func<PlayerHub, bool> filter = null)
        {
            PlayerHub best = null;
            float bestSq = range * range;
            foreach (PlayerHub p in PlayerHub.All)
            {
                if (p == null || !p.TryGetComponent(out PlayerCombat c) || c.IsDowned) continue;
                if (filter != null && !filter(p)) continue;
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = p;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------ damage (host)

        public bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction)
        {
            if (!_setUp || IsDead || _health.Value <= 0) return false;
            CreatureZone zone = ZoneAt(point);
            float multiplier = ZoneMultiplier(zone, kind, point, direction);
            int dealt = Mathf.Max(multiplier > 0.01f ? 1 : 0, Mathf.RoundToInt(damage * multiplier));
            HitReaction(kind, attacker, zone, dealt, point, damage);
            if (dealt <= 0)
            {
                HitObservers(point, 0, zone != null ? zone.Part : "Body");
                return true;
            }
            _health.Value = Mathf.Max(0, _health.Value - dealt);
            if (_boss != null && _maxHealth.Value > 0) _boss.UpdatePhase(Health01);
            HitObservers(point, dealt, zone != null ? zone.Part : "Body");
            if (_health.Value <= 0) Die(attacker);
            return true;
        }

        private CreatureZone ZoneAt(Vector3 point)
        {
            // The nearest part; a point inside several (the head tucked in the shell) belongs to the smallest.
            CreatureZone best = null;
            float bestScore = float.MaxValue;
            foreach (CreatureZone z in _zones)
            {
                if (z == null || z.Collider == null || !z.Collider.enabled) continue;
                float d = (z.Collider.ClosestPoint(point) - point).sqrMagnitude;
                Vector3 size = z.Collider.bounds.size;
                float score = d + (size.x * size.y * size.z) * 1e-4f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = z;
                }
            }
            return best;
        }

        private void Die(PlayerHub by)
        {
            ServerReleaseGrab();
            SetAct(CreatureAct.Dead, 0f);
            _alert.Value = 0;
            Stealth.Unregister(this);
            Debug.Log($"[Creature] {DisplayName} is beaten{(by != null ? " by " + by.DisplayName : "")}");
            OnDied(by);
            ServerKilled?.Invoke(this, by);
        }

        [ObserversRpc]
        private void HitObservers(Vector3 point, int dealt, string part)
        {
            if (_audio != null) _audio.PlayOneShot(dealt <= 0 ? Core.ProceduralAudio.Thump : Core.ProceduralAudio.Bonk, 1f);
            string text = dealt <= 0 ? (part == "Shell" ? "CLANK!" : "NO EFFECT") : part == "Head" && dealt >= 20 ? $"WEAK SPOT! -{dealt}" : $"-{dealt}";
            Color colour = dealt <= 0 ? new Color(0.7f, 0.8f, 0.95f) : part == "Head" ? new Color(1f, 0.85f, 0.2f) : new Color(1f, 0.6f, 0.3f);
            FloatingText.Spawn(point + Vector3.up * 0.3f, text, colour, dealt >= 20 ? 1.2f : 0.9f, 1f);
        }

        [ObserversRpc]
        private void ShoutObservers(string text, bool loud)
        {
            FloatingText.Spawn(TopPosition + Vector3.up * 0.3f, text, loud ? new Color(1f, 0.35f, 0.3f) : new Color(0.55f, 1f, 0.45f), loud ? 1.1f : 0.85f, 2.2f);
            if (_audio == null) return;
            AudioClip clip = _kind.Value == CreatureKind.Parrot ? Audio.ParrotSounds.Squawk : Core.ProceduralAudio.Thump;
            if (clip != null) _audio.PlayOneShot(clip, loud ? 1f : 0.7f);
        }

        // ------------------------------------------------------------------ per kind (hooks the partial files fill in)

        private float TopHeight => _kind.Value switch
        {
            CreatureKind.Parrot => 0.5f,
            CreatureKind.Monkey => 1f,
            CreatureKind.Alligator => 0.7f,
            CreatureKind.OldSnapper => 2.4f,
            CreatureKind.Kleptopus => 4.2f,
            _ => 1f
        };

        private float BodyRadius => _kind.Value switch
        {
            CreatureKind.Alligator => 1f,
            CreatureKind.OldSnapper => 2.2f,
            CreatureKind.Kleptopus => 2f,
            _ => 0.3f
        };

        private float SwimDepth => _kind.Value switch
        {
            CreatureKind.Alligator => 0.45f,
            CreatureKind.OldSnapper => 0.9f,
            CreatureKind.Kleptopus => 1.1f,
            _ => 0.2f
        };

        private float ZoneMultiplier(CreatureZone zone, DamageKind kind, Vector3 point, Vector3 direction)
        {
            if (_kind.Value == CreatureKind.OldSnapper) return SnapperZoneMultiplier(zone, kind, direction);
            if (_kind.Value == CreatureKind.Kleptopus) return KleptopusZoneMultiplier(zone);
            if (_kind.Value == CreatureKind.Alligator && zone != null && zone.Part == "Back") return 0.5f; // armoured back
            return zone != null ? zone.Multiplier : 1f;
        }

        private void HitReaction(DamageKind kind, PlayerHub attacker, CreatureZone zone, int dealt, Vector3 point, int damage)
        {
            switch (_kind.Value)
            {
                case CreatureKind.Kleptopus: KleptopusHit(attacker, zone, damage, kind); break;
                case CreatureKind.Monkey: MonkeyHit(attacker, point); break;
                case CreatureKind.Alligator: GatorHit(attacker, dealt); break;
                case CreatureKind.OldSnapper: SnapperHit(attacker, zone, dealt); break;
                case CreatureKind.Parrot: ParrotHit(attacker); break;
            }
        }

        private void OnDied(PlayerHub by)
        {
            switch (_kind.Value)
            {
                case CreatureKind.Monkey: MonkeyDied(); break;
                case CreatureKind.OldSnapper: SnapperDied(by); break;
                case CreatureKind.Kleptopus: KleptopusDied(by); break;
                case CreatureKind.Parrot: ParrotDied(); break;
            }
        }
    }

    /// <summary>What a creature's body is doing (synced: every machine poses it from this).</summary>
    public enum CreatureAct : byte
    {
        Idle, Move, Run, Perch, Fly, Stare, Alarm,
        Sneak, Grab, Carry, Taunt,
        Lurk, WindUp, Lunge, Bite, Hold,
        Rear, Gasp, Slam, Retreat,
        Dead
    }
}
