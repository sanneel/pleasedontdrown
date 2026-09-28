using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World.Water;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Story
{
    public enum NpcRole : byte { Guide, Receptionist, Robber, Pirate, Bystander, Guest }

    /// <summary>
    /// A story character: Sandy, the receptionist, the robber, pirates, a guest shouting for help.
    ///
    /// The host moves it (a <c>NetworkTransform</c> carries position and facing); every machine animates the
    /// procedural body from that movement, the same way remote players are animated, plus a synced pose (knocked
    /// down, begging, scared...), mood and "talking". Press Interact to talk when it has something to say.
    /// Robbers and pirates can be punched and shot (<see cref="IDamageable"/>); their behaviour lives in
    /// StoryNpc.Brains.cs. The look is an <see cref="AvatarLook"/> today and a generated model later.
    /// </summary>
    public partial class StoryNpc : NetworkBehaviour, IInteractionHandler, IDamageable
    {
        [SerializeField] private AvatarRig _rig;
        [SerializeField] private AvatarAnimator _animator;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private TextMesh _nameTag;
        [SerializeField] private float _walkSpeed = 1.5f;
        [SerializeField] private float _runSpeed = 5.9f;

        private static readonly List<StoryNpc> _all = new();

        private readonly SyncVar<string> _name = new SyncVar<string>();
        private readonly SyncVar<ulong> _look = new SyncVar<ulong>();
        private readonly SyncVar<NpcRole> _role = new SyncVar<NpcRole>();
        private readonly SyncVar<AvatarPose> _pose = new SyncVar<AvatarPose>();
        private readonly SyncVar<AvatarMood> _mood = new SyncVar<AvatarMood>();
        private readonly SyncVar<bool> _talkable = new SyncVar<bool>();
        private readonly SyncVar<string> _talkPrompt = new SyncVar<string>();
        private readonly SyncVar<int> _health = new SyncVar<int>();
        private readonly SyncVar<int> _maxHealth = new SyncVar<int>();
        private readonly SyncVar<bool> _hasBag = new SyncVar<bool>();

        // Every machine.
        private Vector3 _lastPosition;
        private Vector3 _velocity;
        private float _talkUntil;
        private float _lookYaw;
        private GameObject _bag;
        private float _nextTalkRequest;
        private CapsuleCollider _bodyCollider;
        private SphereCollider _headCollider;
        private Rigidbody _rigidbody;

        // Host.
        private Vector3? _moveTarget;
        private float _moveSpeed;
        private Vector3? _facePoint;
        private Vehicle _ride;
        private Vector3 _rideLocal;
        private float _staggerUntil;
        private Vector3 _knock;
        private bool _setUp;   // placed by whoever spawned it (until then it stays exactly where the scene put it)
        // Walking a navmesh path (host).
        private readonly List<Vector3> _path = new();
        private int _pathIndex;
        private NavMeshPath _navPath;
        private float _stuckTime;
        private int _replans;
        private readonly RaycastHit[] _sweepHits = new RaycastHit[12];

        public static IReadOnlyList<StoryNpc> All => _all;
        /// <summary>Host: a player pressed Interact on this character.</summary>
        public static event Action<StoryNpc, PlayerHub> ServerTalked;
        /// <summary>Host: health reached zero.</summary>
        public static event Action<StoryNpc, PlayerHub> ServerDefeated;

        public string Name => string.IsNullOrEmpty(_name.Value) ? "Someone" : _name.Value;
        public NpcRole Role => _role.Value;
        public AvatarLook Look => AvatarLook.Unpack(_look.Value);
        public ulong LookPacked => _look.Value;
        public AvatarPose Pose => _pose.Value;
        public int Health => _health.Value;
        public int MaxHealth => _maxHealth.Value;
        public bool IsDefeated => _maxHealth.Value > 0 && _health.Value <= 0;
        public bool IsTalkable => _talkable.Value;
        public Vector3 HeadPosition => _pose.Value switch
        {
            AvatarPose.Down or AvatarPose.Lie or AvatarPose.LieFront => transform.position + Vector3.up * 0.3f,
            AvatarPose.Sit => transform.position + Vector3.up * 1f,
            AvatarPose.SitChair => transform.position + Vector3.up * 1.35f,
            AvatarPose.Kneel => transform.position + Vector3.up * 1.3f,
            _ => transform.position + Vector3.up * 1.75f
        };
        /// <summary>Treading water (in water deeper than it can stand in).</summary>
        public bool IsSwimming => WaterSurface.Exists && WaterSurface.HeightAt(transform.position) - transform.position.y > 1.1f;
        public bool IsMoving => _moveTarget.HasValue;
        /// <summary>Host debugging: where along its route it is.</summary>
        public string PathInfo => _moveTarget.HasValue ? $"waypoint {_pathIndex + 1}/{_path.Count}" : "";

        public Color SpeechColor => _role.Value switch
        {
            NpcRole.Guide => new Color(1f, 0.78f, 0.55f),
            NpcRole.Receptionist => new Color(0.7f, 0.85f, 1f),
            NpcRole.Robber => new Color(0.85f, 0.85f, 0.85f),
            NpcRole.Pirate => new Color(1f, 0.5f, 0.4f),
            _ => new Color(1f, 1f, 0.75f)
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            _groundColliders.Clear();
            ServerTalked = null;
            ServerDefeated = null;
        }

        private void Awake()
        {
            _look.OnChange += (_, next, _) => ApplyLook(next);
            _name.OnChange += (_, next, _) =>
            {
                if (_nameTag != null) _nameTag.text = next;
                if (!string.IsNullOrEmpty(next)) gameObject.name = $"Npc_{next}";
            };
            _hasBag.OnChange += (_, next, _) => ShowBag(next);
            _pose.OnChange += (_, next, _) => FitColliders(next);
            _bodyCollider = GetComponent<CapsuleCollider>();
            _headCollider = GetComponent<SphereCollider>();
            _rigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>Lying people are long and low, sitting ones short: the colliders follow (walk past, punch the right spot).</summary>
        private void FitColliders(AvatarPose pose)
        {
            if (_bodyCollider == null) return;
            switch (pose)
            {
                case AvatarPose.Down:
                case AvatarPose.Lie:
                case AvatarPose.LieFront:
                    _bodyCollider.direction = 2;
                    _bodyCollider.center = new Vector3(0f, 0.18f, -0.45f);
                    _bodyCollider.height = 1.8f;
                    _bodyCollider.radius = 0.2f;
                    if (_headCollider != null) _headCollider.center = new Vector3(0f, 0.2f, pose == AvatarPose.LieFront ? 0.35f : -1.3f);
                    break;
                case AvatarPose.Sit:
                case AvatarPose.Kneel:
                case AvatarPose.SitChair:
                    float h = pose == AvatarPose.Sit ? 0.95f : 1.35f;
                    _bodyCollider.direction = 1;
                    _bodyCollider.center = new Vector3(0f, h * 0.5f, 0f);
                    _bodyCollider.height = h;
                    _bodyCollider.radius = 0.3f;
                    if (_headCollider != null) _headCollider.center = new Vector3(0f, h - 0.1f, 0f);
                    break;
                default:
                    _bodyCollider.direction = 1;
                    _bodyCollider.center = new Vector3(0f, 0.9f, 0f);
                    _bodyCollider.height = 1.8f;
                    _bodyCollider.radius = 0.32f;
                    if (_headCollider != null) _headCollider.center = new Vector3(0f, 1.62f, 0f);
                    break;
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _all.Add(this);
            _lastPosition = transform.position;
            _lookYaw = transform.eulerAngles.y;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _all.Remove(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyLook(_look.Value);
            if (_nameTag != null) _nameTag.text = Name;
            ShowBag(_hasBag.Value);
            FitColliders(_pose.Value);
        }

        // ------------------------------------------------------------------ host API

        /// <summary>Host: who this is and what they look like.</summary>
        [Server]
        public void ServerSetup(string displayName, NpcRole role, AvatarLook look, int health = 0)
        {
            _setUp = true;
            _name.Value = displayName;
            _role.Value = role;
            _look.Value = look.Pack();
            _maxHealth.Value = health;
            _health.Value = health;
            _hasBag.Value = role == NpcRole.Robber;
            _talkable.Value = role is NpcRole.Guide or NpcRole.Receptionist;
            _pose.Value = AvatarPose.Normal;
            _mood.Value = AvatarMood.Neutral;
            ServerSetupBrain();
        }

        [Server] public void ServerSetTalkable(bool talkable, string prompt = null)
        {
            _talkable.Value = talkable;
            _talkPrompt.Value = prompt ?? string.Empty;
        }

        [Server] public void ServerSetPose(AvatarPose pose) => _pose.Value = pose;
        [Server] public void ServerSetMood(AvatarMood mood) => _mood.Value = mood;
        [Server] public void ServerSetHealth(int health, int max) { _maxHealth.Value = max; _health.Value = health; }
        [Server] public void ServerSetBag(bool hasBag) => _hasBag.Value = hasBag;

        /// <summary>Host: walk (or run) to a point on land, then stop.</summary>
        [Server]
        public void ServerMoveTo(Vector3 target, bool run = false) => ServerMoveTo(target, run ? _runSpeed : _walkSpeed);

        /// <summary>Host: move at a given speed (m/s), e.g. a slow swim. Walks around obstacles on the navmesh.</summary>
        [Server]
        public void ServerMoveTo(Vector3 target, float speed)
        {
            _moveTarget = target;
            _moveSpeed = Mathf.Max(0.1f, speed);
            _replans = 0;
            PlanPath();
        }

        /// <summary>
        /// Route to the destination on the baked navmesh (around walls, palms and counters, through doorways).
        /// Without a navmesh there it goes straight, and the wall check still stops it at walls.
        /// </summary>
        private void PlanPath()
        {
            _path.Clear();
            _pathIndex = 0;
            _stuckTime = 0f;
            if (!_moveTarget.HasValue) return;
            _navPath ??= new NavMeshPath();
            Vector3 target = _moveTarget.Value;
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit from, 10f, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(target, out NavMeshHit to, 10f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _navPath) &&
                _navPath.status != NavMeshPathStatus.PathInvalid)
            {
                Vector3[] corners = _navPath.corners;
                for (int i = 1; i < corners.Length; i++) _path.Add(corners[i]);
                // Arrive exactly where asked (a path to somewhere unreachable ends at its closest point).
                if (_navPath.status == NavMeshPathStatus.PathComplete && _path.Count > 0) _path[_path.Count - 1] = target;
            }
            if (_path.Count == 0) _path.Add(target);
        }

        /// <summary>The nearest walkable spot (outside walls and trunks), same height handling as walking.</summary>
        public static Vector3 OnNavMesh(Vector3 position, float radius = 3f)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas)) return position;
            return new Vector3(hit.position.x, position.y, hit.position.z);
        }

        [Server] public void ServerStop() => _moveTarget = null;

        /// <summary>Host: turn toward a point (null: face whoever is near).</summary>
        [Server] public void ServerFace(Vector3? point) => _facePoint = point;

        [Server]
        public void ServerTeleport(Vector3 position, float yaw, bool keepExact = false)
        {
            _moveTarget = null;
            _path.Clear();
            _ride = null;
            if (!keepExact) position = Grounded(OnNavMesh(position, 2f)); // never inside a wall or a trunk
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Host: stand on a vehicle's deck (pirates arriving), local to the vehicle. Null gets off.</summary>
        [Server]
        public void ServerRide(Vehicle vehicle, Vector3 local)
        {
            _ride = vehicle;
            _rideLocal = local;
            _moveTarget = null;
        }

        /// <summary>Host: play a gesture on every machine.</summary>
        [Server]
        public void ServerGesture(AvatarGesture gesture, Vector3 point = default) => GestureObservers(gesture, point);

        [ObserversRpc]
        private void GestureObservers(AvatarGesture gesture, Vector3 point)
        {
            if (_animator != null) _animator.Play(gesture, point);
        }

        /// <summary>Host: a shout above the head everyone sees (and hears).</summary>
        [Server]
        public void ServerShout(string text, bool cry = false) => ShoutObservers(text, cry);

        [ObserversRpc]
        private void ShoutObservers(string text, bool cry)
        {
            FloatingText.Spawn(HeadPosition + Vector3.up * 0.4f, text, new Color(1f, 0.95f, 0.75f), 0.9f, 1.6f);
            if (cry && _audio != null) _audio.PlayOneShot(ProceduralAudio.Cry(_rig != null && _rig.Look.Feminine ? 3 : 1), 1f);
            OnSpeak(0.8f);
        }

        // ------------------------------------------------------------------ talking

        public bool CanInteract(PlayerHub player) => _talkable.Value && _pose.Value != AvatarPose.Down;

        public string GetPrompt(PlayerHub player) => !string.IsNullOrEmpty(_talkPrompt.Value) ? _talkPrompt.Value : $"Talk to {Name}";

        public void OnInteract(PlayerHub player)
        {
            if (Time.time < _nextTalkRequest) return;
            _nextTalkRequest = Time.time + 0.6f;
            TalkServer(player);
        }

        [ServerRpc(RequireOwnership = false)]
        private void TalkServer(PlayerHub player, NetworkConnection caller = null)
        {
            if (player == null || player.Owner != caller || !_talkable.Value) return;
            if ((player.transform.position - transform.position).sqrMagnitude > 5f * 5f) return;
            Debug.Log($"[Npc] {player.DisplayName} talks to {Name}");
            ServerTalked?.Invoke(this, player);
        }

        /// <summary>Every machine: mouth moves for a while (a dialogue line or a shout).</summary>
        public void OnSpeak(float seconds) => _talkUntil = Mathf.Max(_talkUntil, Time.time + seconds);

        // ------------------------------------------------------------------ damage (host)

        public bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction)
        {
            if (_maxHealth.Value <= 0 || _health.Value <= 0) return false; // can't be hurt / already down
            _health.Value = Mathf.Max(0, _health.Value - damage);
            Vector3 push = new Vector3(direction.x, 0f, direction.z).normalized;
            _knock = push * (kind == DamageKind.Bullet ? 2f : 3.5f);
            _staggerUntil = Time.time + (kind == DamageKind.Bullet ? 0.35f : 0.6f);
            HitObservers(point, _health.Value, kind == DamageKind.Bullet);
            OnServerHit(attacker);
            if (_health.Value <= 0)
            {
                _moveTarget = null;
                _pose.Value = AvatarPose.Down;
                _mood.Value = AvatarMood.Hurt;
                Debug.Log($"[Npc] {Name} is down ({kind} by {(attacker != null ? attacker.DisplayName : "?")})");
                ServerDefeated?.Invoke(this, attacker);
            }
            return true;
        }

        [ObserversRpc]
        private void HitObservers(Vector3 point, int healthLeft, bool bullet)
        {
            if (_audio != null) _audio.PlayOneShot(bullet ? ProceduralAudio.Bonk : ProceduralAudio.Punch, 1f);
            string text = healthLeft <= 0 ? "K.O.!" : bullet ? "OUCH!" : Random.value < 0.5f ? "OW!" : "OOF!";
            FloatingText.Spawn(HeadPosition + Vector3.up * 0.3f, text, new Color(1f, 0.6f, 0.3f), healthLeft <= 0 ? 1.4f : 0.9f, 1.2f);
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (IsServerInitialized) ServerUpdate(dt);

            // Everyone: animate from how the body moved.
            Vector3 position = transform.position;
            Vector3 raw = (position - _lastPosition) / dt;
            if (raw.sqrMagnitude > 900f) raw = Vector3.zero;
            _velocity = Vector3.Lerp(_velocity, raw, 1f - Mathf.Exp(-10f * dt));
            _lastPosition = position;
            UpdateAnimation();
            UpdateNameTag();
        }

        private void UpdateAnimation()
        {
            if (_animator == null || _rig == null || !_rig.IsBuilt) return;
            // Idle: turn the head (and eventually the body) toward the nearest player.
            float yaw = transform.eulerAngles.y;
            var flatSpeed = new Vector2(_velocity.x, _velocity.z).magnitude;
            if (flatSpeed < 0.3f && _pose.Value is AvatarPose.Normal or AvatarPose.Scared && NearestPlayer(6f) is { } near)
            {
                Vector3 to = near.transform.position - transform.position;
                if (to.sqrMagnitude > 0.01f) yaw = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z)).eulerAngles.y;
            }
            _lookYaw = Mathf.LerpAngle(_lookYaw, yaw, 1f - Mathf.Exp(-6f * Time.deltaTime));
            bool swimming = WaterSurface.Exists && WaterSurface.HeightAt(transform.position) - transform.position.y > 1.1f;
            _animator.Motion = new AvatarMotion
            {
                Velocity = _pose.Value == AvatarPose.Normal || _pose.Value == AvatarPose.Scared ? _velocity : Vector3.zero,
                FacingYaw = _lookYaw,
                Grounded = true,
                Swimming = swimming && _ride == null,
                Sprinting = flatSpeed > 4.5f,
                Pose = _pose.Value,
                Mood = _mood.Value,
                Talking = Time.time < _talkUntil,
                Seated = false
            };
        }

        private void UpdateNameTag()
        {
            if (_nameTag == null) return;
            PlayerHub local = PlayerHub.Local;
            Camera cam = Camera.main;
            bool show = local != null && cam != null && (local.transform.position - transform.position).sqrMagnitude < 9f * 9f && !string.IsNullOrEmpty(_name.Value);
            if (_nameTag.gameObject.activeSelf != show) _nameTag.gameObject.SetActive(show);
            if (!show) return;
            _nameTag.transform.position = HeadPosition + Vector3.up * 0.45f;
            _nameTag.transform.rotation = cam.transform.rotation;
            _nameTag.text = _maxHealth.Value > 0 && _health.Value > 0
                ? $"{Name}  {new string('●', _health.Value)}{new string('○', Mathf.Max(0, _maxHealth.Value - _health.Value))}"
                : Name;
        }

        public static PlayerHub NearestPlayer(Vector3 from, float range)
        {
            PlayerHub best = null;
            float bestSq = range * range;
            foreach (PlayerHub p in PlayerHub.All)
            {
                float d = (p.transform.position - from).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best = p;
                }
            }
            return best;
        }

        private PlayerHub NearestPlayer(float range) => NearestPlayer(transform.position, range);

        // ------------------------------------------------------------------ host movement

        private void ServerUpdate(float dt)
        {
            if (!_setUp) return;
            if (_ride != null)
            {
                // Standing on a moving deck.
                transform.position = _ride.transform.TransformPoint(_rideLocal);
                transform.rotation = Quaternion.Euler(0f, _ride.transform.eulerAngles.y, 0f);
                return;
            }

            BrainUpdate(dt);

            Vector3 p = transform.position;
            if (Time.time < _staggerUntil)
            {
                // Knocked back by a hit, sliding to a stop (against a wall, not into it).
                transform.position = Grounded(MoveChecked(p, _knock * dt));
                _knock = Vector3.MoveTowards(_knock, Vector3.zero, dt * 8f);
                return;
            }

            if (_moveTarget.HasValue && _pose.Value is AvatarPose.Normal or AvatarPose.Scared)
                FollowPath(p, dt);
            if (!_moveTarget.HasValue && _facePoint.HasValue)
                Face(_facePoint.Value - p, dt, 5f);
            Separate(dt);
            // Spawned in the air or into a dune: onto the ground. (Not someone placed on a seat: feet on a stool's footrest.)
            if (!_moveTarget.HasValue && _pose.Value != AvatarPose.SitChair) transform.position = Grounded(transform.position);
        }

        /// <summary>Don't stand inside each other (a gang of pirates spreads out around their target).</summary>
        private void Separate(float dt)
        {
            if (!IsUpright(_pose.Value)) return;
            Vector3 p = transform.position, push = Vector3.zero;
            foreach (StoryNpc other in _all)
            {
                if (other == this || other._ride != null || !IsUpright(other._pose.Value)) continue;
                Vector3 d = p - other.transform.position;
                d.y = 0f;
                float distance = d.magnitude;
                if (distance < 0.85f)
                    push += (distance > 0.01f ? d / distance : Random.insideUnitSphere) * (0.85f - distance);
            }
            push.y = 0f;
            if (push.sqrMagnitude > 1e-6f) transform.position = Grounded(MoveChecked(p, Vector3.ClampMagnitude(push, 1f) * Mathf.Min(1f, dt * 6f)));
        }

        private void FollowPath(Vector3 p, float dt)
        {
            if (_path.Count == 0) PlanPath();
            Vector3 waypoint = _path[Mathf.Min(_pathIndex, _path.Count - 1)];
            Vector3 to = waypoint - p;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance < 0.2f)
            {
                if (++_pathIndex >= _path.Count)
                {
                    _moveTarget = null; // arrived (or as close as the navmesh gets)
                    _path.Clear();
                }
                return;
            }

            Vector3 step = to / distance * Mathf.Min(distance, _moveSpeed * dt);
            Vector3 moved = MoveChecked(p, step);
            Vector3 next = Grounded(moved);
            // Story characters don't wander into deep water (the robber would drown on us).
            if (Shore.WaterDepthAt(next + Vector3.up * 0.1f) < 0.5f || Shore.WaterDepthAt(p) >= 0.5f)
                transform.position = next;
            else
            {
                _moveTarget = null;
                _path.Clear();
            }
            Face(step, dt, 10f);

            // Pinned against something the path didn't know about (a player, a parked jet ski): plan again, then give up.
            bool progressed = (new Vector2(moved.x - p.x, moved.z - p.z)).sqrMagnitude > step.sqrMagnitude * 0.09f;
            _stuckTime = progressed ? 0f : _stuckTime + dt;
            if (_stuckTime > 0.8f && _moveTarget.HasValue)
            {
                if (++_replans > 3)
                {
                    _moveTarget = null;
                    _path.Clear();
                }
                else PlanPath();
            }
        }

        /// <summary>
        /// Moves by <paramref name="step"/> unless something is in the way, sliding along it: walls (buildings,
        /// counters, trunks, dock posts, rocks) and things that move (lifeguards, crates, parked jet skis, tourists,
        /// other characters). The ground itself and triggers don't count. The capsule starts 0.45 m up, so steps and
        /// kerbs are walked over (low things lying about are avoided by the navmesh instead: they carve it).
        /// </summary>
        private Vector3 MoveChecked(Vector3 p, Vector3 step)
        {
            for (int i = 0; i < 3; i++)
            {
                float distance = step.magnitude;
                if (distance < 1e-5f) return p;
                Vector3 dir = step / distance;
                if (!ObstacleAhead(p, dir, distance, out RaycastHit hit)) return p + step;
                float free = Mathf.Max(0f, hit.distance - 0.02f);
                p += dir * free;
                Vector3 normal = new Vector3(hit.normal.x, 0f, hit.normal.z);
                if (normal.sqrMagnitude < 1e-4f) return p;
                step = Vector3.ProjectOnPlane(dir * (distance - free), normal.normalized); // slide along the wall
            }
            return p;
        }

        private bool ObstacleAhead(Vector3 p, Vector3 dir, float distance, out RaycastHit best)
        {
            const float radius = 0.28f;
            Vector3 bottom = p + Vector3.up * (0.45f + radius), top = p + Vector3.up * (1.95f - radius);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, dir, _sweepHits, distance + 0.02f, ~0, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _sweepHits[i];
                if (h.distance <= 0f || h.distance >= bestDistance) continue; // already overlapping: let them walk out of it
                Collider c = h.collider;
                Rigidbody body = c.attachedRigidbody;
                if (IsGround(c) || (body != null && (body == _rigidbody || (_ride != null && body == _ride.Body)))) continue; // ourselves, our boat
                bestDistance = h.distance;
                best = h;
            }
            return bestDistance < float.MaxValue;
        }

        private static readonly Dictionary<Collider, bool> _groundColliders = new();

        /// <summary>The island terrain (dunes, seabed slopes) is walked on, never a wall.</summary>
        private static bool IsGround(Collider c)
        {
            if (!_groundColliders.TryGetValue(c, out bool ground))
                _groundColliders[c] = ground = c.GetComponent<World.Water.Seabed>() != null;
            return ground;
        }

        private static bool IsUpright(AvatarPose pose) => pose is AvatarPose.Normal or AvatarPose.Scared or AvatarPose.HandsUp;

        private void Face(Vector3 direction, float dt, float rate)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) return;
            Quaternion want = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-rate * dt));
        }

        /// <summary>Feet on the ground, or treading water with the head out where it's too deep to stand.</summary>
        private static Vector3 Grounded(Vector3 p)
        {
            // Swimmers look for the seabed just above themselves, so a dock overhead isn't mistaken for the ground.
            bool swimming = WaterSurface.Exists && WaterSurface.HeightAt(p) - p.y > 1f;
            // (Walkers probe from just above their knees: from 3 m up, the ceiling of a hut they stand in looked like the ground.)
            float ground = Shore.GroundHeightAt(p + Vector3.up * (swimming ? 0.9f : 0.7f));
            if (float.IsNaN(ground)) return p;
            if (WaterSurface.Exists)
            {
                float surface = WaterSurface.HeightAt(p);
                if (surface - ground > SwimDepth)
                {
                    p.y = surface - SwimDepth;
                    return p;
                }
            }
            p.y = ground;
            return p;
        }

        /// <summary>Feet this far under the surface when swimming (mouth just out of the water).</summary>
        private const float SwimDepth = 1.4f;

        // ------------------------------------------------------------------ looks

        private void ApplyLook(ulong packed)
        {
            if (_rig == null || packed == 0) return;
            AvatarLook look = AvatarLook.Unpack(packed);
            if (_rig.IsBuilt && _rig.Look.Equals(look)) return;
            _rig.Build(look);
            ShowBag(_hasBag.Value);
        }

        /// <summary>The robber's backpack: a lumpy bag on his back (bursts open when he goes down).</summary>
        private void ShowBag(bool show)
        {
            if (!show)
            {
                if (_bag != null) _bag.SetActive(false);
                return;
            }
            if (_rig == null || !_rig.IsBuilt) return;
            if (_bag == null)
            {
                var kit = new AvatarMeshKit();
                kit.SetBone(0, Matrix4x4.identity);
                Color canvas = new Color(0.36f, 0.27f, 0.18f), strap = new Color(0.16f, 0.12f, 0.08f);
                kit.Ellipsoid(Vector3.zero, new Vector3(0.17f, 0.22f, 0.11f), canvas, segments: 12, rings: 8);
                kit.Ellipsoid(new Vector3(0f, 0.15f, -0.02f), new Vector3(0.15f, 0.07f, 0.1f), canvas * 0.85f, segments: 10, rings: 6);
                kit.Box(new Vector3(0f, -0.02f, -0.11f), new Vector3(0.16f, 0.12f, 0.04f), canvas * 0.9f);
                foreach (float side in new[] { -1f, 1f })
                    kit.Box(new Vector3(0.1f * side, 0.05f, 0.1f), new Vector3(0.03f, 0.34f, 0.02f), strap);
                _bag = new GameObject("Bag");
                _bag.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("RobberBag", new[] { Matrix4x4.identity });
                _bag.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
            }
            _bag.transform.SetParent(_rig[AvatarRig.Bone.Chest], false);
            _bag.transform.localPosition = new Vector3(0f, 0.05f, -0.22f) * _rig.Scale;
            _bag.transform.localRotation = Quaternion.identity;
            _bag.SetActive(true);
        }
    }
}
