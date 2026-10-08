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

    /// <summary>What a beach tourist is up to (host bookkeeping, used by BeachCrowd and NpcWatch).</summary>
    public enum NpcActivity : byte { None, Sunbathe, Stroll, Wade, Swim, Chat, GoSwim, Return }

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
        private bool _swimPose;
        private GameObject _bag, _disguise;
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
        private float _turnTime;
        private float _progressBest;       // closest it has come to the current waypoint...
        private float _progressSince;      // ...and when that last improved
        private int _replans;
        private bool _allowWater;
        private float _teleportedAt = -10f;
        private string _gaveUp;
        private Rigidbody _walksWith;
        private float _nextUnstick;
        private readonly RaycastHit[] _sweepHits = new RaycastHit[12];
        private readonly Collider[] _overlapHits = new Collider[12];

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
        public Vector3 HeadPosition => _pose.Value switch
        {
            AvatarPose.Down or AvatarPose.Lie or AvatarPose.LieFront => transform.position + Vector3.up * 0.3f,
            AvatarPose.Sit => transform.position + Vector3.up * 1f - transform.forward * SitBack,
            AvatarPose.SitChair => transform.position + Vector3.up * 1.35f,
            AvatarPose.Kneel => transform.position + Vector3.up * 1.3f,
            _ => transform.position + Vector3.up * 1.75f
        };
        /// <summary>Treading water (in water deeper than it can stand in).</summary>
        public bool IsSwimming => WaterSurface.Exists && WaterSurface.HeightAt(transform.position) - transform.position.y > SwimAbove;

        /// <summary>
        /// Swimming once the surface is this far above the feet. Walkers keep their feet on the bottom until the water is
        /// <see cref="SwimDepth"/> deep, so the swim pose starts just before they lift off (not while still walking).
        /// </summary>
        private const float SwimAbove = 1.3f;
        public bool IsMoving => _moveTarget.HasValue;
        public Vector3 MoveTarget => _moveTarget ?? transform.position;
        public bool IsUpright => Upright(_pose.Value);

        /// <summary>The navmesh area under the station's buildings (Editor/GameSceneBuilder NavKeepOut).</summary>
        public const int BuildingArea = 3;

        /// <summary>
        /// Host: keep out of the buildings (the beach crowd: a tourist's stroll doesn't go up the tower stairs or in
        /// through the hut). Story characters leave it off: Sandy works in the hut.
        /// </summary>
        public bool StayOutOfBuildings { get; set; }

        private int AreaMask => StayOutOfBuildings ? NavMesh.AllAreas & ~(1 << BuildingArea) : NavMesh.AllAreas;
        /// <summary>Where the drawn body faces (degrees): it follows the character's turning with a little lag.</summary>
        public float DrawnYaw => _animator != null ? _animator.BodyYaw : transform.eulerAngles.y;
        public Vehicle Ride => _ride;
        /// <summary>Host: what a beach tourist is doing (set by BeachCrowd).</summary>
        public NpcActivity Activity { get; set; }
        /// <summary>Host: a route ended; true = arrived, false = gave up (blocked for good).</summary>
        public event Action<StoryNpc, bool> ServerRouteEnded;
        public bool ServerTeleportedSince(float time) => _teleportedAt >= time;

        /// <summary>Host (tests): the last route it gave up on, once.</summary>
        public bool TakeGaveUp(out string why)
        {
            why = _gaveUp;
            _gaveUp = null;
            return why != null;
        }
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
            _characterBodies.Clear();
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
            _role.OnChange += (_, _, _) => ShowDisguise();
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
                    float back = pose == AvatarPose.Sit ? -SitBack : 0f; // sitting on a towel: behind the feet (see SitBack)
                    _bodyCollider.direction = 1;
                    _bodyCollider.center = new Vector3(0f, h * 0.5f, back);
                    _bodyCollider.height = h;
                    _bodyCollider.radius = 0.3f;
                    if (_headCollider != null) _headCollider.center = new Vector3(0f, h - 0.1f, back);
                    break;
                default:
                    // In the water only the head is out: the body ends at the surface (feet hang SwimDepth below it),
                    // so a swimmer passes under the dock without their collider sticking up through the deck.
                    float tall = _swimPose ? 1.6f : 1.8f;
                    _bodyCollider.direction = 1;
                    _bodyCollider.center = new Vector3(0f, tall * 0.5f, 0f);
                    _bodyCollider.height = tall;
                    _bodyCollider.radius = 0.32f;
                    if (_headCollider != null) _headCollider.center = new Vector3(0f, tall - 0.18f, 0f);
                    break;
            }
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _all.Add(this);
            _lastPosition = transform.position;
            _lookYaw = transform.eulerAngles.y;
            if (_animator != null)
            {
                // Each tourist moves in their own way (the same on every machine), fidgets when idle, and is only
                // posed now and then while nobody can see them.
                _animator.Seed = ObjectId + 1;
                _animator.Lively = true;
                _animator.CullWhenHidden = true;
                _animator.Breaststroke = true; // holidaymakers: heads up, hands out in front
            }
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
        /// <param name="water">May walk into (and swim through) deep water: beach tourists going for a swim. Story
        /// characters leave it off, so a robber never runs into the sea.</param>
        [Server]
        public void ServerMoveTo(Vector3 target, float speed, bool water = false)
        {
            _moveTarget = target;
            _moveSpeed = Mathf.Max(0.1f, speed);
            _allowWater = water;
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
            _progressBest = float.MaxValue;
            _progressSince = Time.time;
            if (!_moveTarget.HasValue) return;
            _navPath ??= new NavMeshPath();
            Vector3 target = _moveTarget.Value;
            // Swimming from water to water: straight there if nothing's in the way (the navmesh lies on the seabed and
            // would lead under the dock).
            if (Shore.WaterDepthAt(transform.position + Vector3.up * 0.1f) > 1.1f && Shore.WaterDepthAt(target + Vector3.up * 0.1f) > 1.1f)
            {
                if (SwimLineClear(transform.position, target))
                {
                    _path.Add(target);
                    return;
                }
                // Something in the way (a buoy, a moored jet ski, the dock's corner): swim round it through one open
                // water point off to the side, never along the seabed navmesh (that leads under the dock).
                if (SwimDetour(transform.position, target, out Vector3 via))
                {
                    _path.Add(via);
                    _path.Add(target);
                    return;
                }
            }
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit from, 10f, AreaMask) &&
                NavMesh.SamplePosition(target, out NavMeshHit to, 10f, AreaMask) &&
                NavMesh.CalculatePath(from.position, to.position, AreaMask, _navPath) &&
                _navPath.status != NavMeshPathStatus.PathInvalid)
            {
                Vector3[] corners = _navPath.corners;
                for (int i = 1; i < corners.Length; i++) _path.Add(corners[i]);
                // Arrive exactly where asked (a path to somewhere unreachable ends at its closest point).
                if (_navPath.status == NavMeshPathStatus.PathComplete && _path.Count > 0) _path[_path.Count - 1] = target;
            }
            if (_path.Count == 0) _path.Add(target);
        }

        private static readonly RaycastHit[] _lineHits = new RaycastHit[16];

        /// <summary>Can a swimmer go straight from a to b, head above water (no dock, post, rock or buoy in between)?</summary>
        public static bool SwimLineClear(Vector3 a, Vector3 b)
        {
            if (!WaterSurface.Exists) return true;
            Vector3 from = new Vector3(a.x, WaterSurface.HeightAt(a) - 0.35f, a.z), to = new Vector3(b.x, WaterSurface.HeightAt(b) - 0.35f, b.z);
            Vector3 d = to - from;
            float length = d.magnitude;
            if (length < 0.05f) return true;
            int n = Physics.SphereCastNonAlloc(from, 0.4f, d / length, _lineHits, length, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _lineHits[i].collider;
                if (IsGround(c) || c is TerrainCollider) continue;
                // Floating things (buoys, a parked jet ski, crates) block a swimmer just like the dock: the same rule
                // the movement sweep uses, so a line judged clear here is one the swimmer can really swim. Other
                // characters are steered round on the way instead.
                Rigidbody body = c.attachedRigidbody;
                if (body != null && IsCharacter(body)) continue;
                if (body != null && _lineHits[i].distance <= 0f) continue; // already touching it: swim off, don't freeze
                return false;
            }
            return true;
        }

        /// <summary>An open-water point beside the straight line that both halves of the trip can swim to.</summary>
        public static bool SwimDetour(Vector3 from, Vector3 to, out Vector3 via)
        {
            Vector3 d = to - from;
            d.y = 0f;
            via = default;
            if (d.sqrMagnitude < 0.01f) return false;
            Vector3 side = Vector3.Cross(Vector3.up, d.normalized);
            foreach (float along in new[] { 0.5f, 0.35f, 0.65f })
                foreach (float off in new[] { 3f, -3f, 5.5f, -5.5f, 8f, -8f })
                {
                    Vector3 p = from + d * along + side * off;
                    if (Shore.WaterDepthAt(p + Vector3.up * 0.1f) < 1.6f || !OpenSky(p)) continue;
                    if (!SwimLineClear(from, p) || !SwimLineClear(p, to)) continue;
                    via = new Vector3(p.x, from.y, p.z);
                    return true;
                }
            return false;
        }

        /// <summary>Open water above this spot (not under a dock or a pier).</summary>
        public static bool OpenSky(Vector3 p) =>
            !WaterSurface.Exists || !Physics.Raycast(new Vector3(p.x, WaterSurface.HeightAt(p) - 0.6f, p.z), Vector3.up, 6f, ~0, QueryTriggerInteraction.Ignore);

        /// <summary>The nearest walkable spot (outside walls and trunks), same height handling as walking.</summary>
        public static Vector3 OnNavMesh(Vector3 position, float radius = 3f)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas)) return position;
            return new Vector3(hit.position.x, position.y, hit.position.z);
        }

        [Server]
        public void ServerStop()
        {
            _moveTarget = null;
            _path.Clear();
        }

        /// <summary>Host: someone walking with us (hand in hand): never a wall in our way.</summary>
        [Server] public void ServerWalkWith(Rigidbody body) => _walksWith = body;

        /// <summary>Host: turn toward a point (null: face whoever is near).</summary>
        [Server] public void ServerFace(Vector3? point) => _facePoint = point;

        [Server]
        public void ServerTeleport(Vector3 position, float yaw, bool keepExact = false)
        {
            _moveTarget = null;
            _path.Clear();
            _ride = null;
            _teleportedAt = Time.time;
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
            OnSpeak(0.8f, cry ? null : text);
        }

        /// <summary>Host: say something (a speech line over the head; the mouth moves while it's up).</summary>
        [Server]
        public void ServerSay(string text, float seconds = 0f)
        {
            if (seconds <= 0f) seconds = Mathf.Clamp(1.2f + text.Length * 0.055f, 1.8f, 4.5f);
            SayObservers(text, seconds);
        }

        [ObserversRpc]
        private void SayObservers(string text, float seconds)
        {
            FloatingText.Spawn(HeadPosition + Vector3.up * 0.4f, text, SpeechColor, 0.75f, seconds);
            OnSpeak(seconds * 0.8f, text, 0.5f); // beach chatter: a murmur next to the story's own lines
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

        /// <summary>Every machine: mouth moves for a while (a dialogue line or a shout), and <paramref name="text"/> is babbled out loud.</summary>
        public void OnSpeak(float seconds, string text = null, float gain = 1f)
        {
            _talkUntil = Mathf.Max(_talkUntil, Time.time + seconds);
            if (text == null || _audio == null) return;
            // Each person has their own voice: the kind from who they are, the exact pitch from their id.
            int id = ObjectId;
            bool feminine = _rig != null && _rig.Look.Feminine;
            (int register, float pitch) = _role.Value switch
            {
                NpcRole.Guide => (2, 1.03f),
                NpcRole.Robber => (0, 0.88f),
                NpcRole.Pirate => (0, 0.8f + (id % 5) * 0.03f),
                _ => ((feminine ? 2 : 0) + (id & 1), 0.93f + (id * 7 % 15) * 0.01f)
            };
            Audio.SpeechVoice.On(gameObject, _audio).Speak(text, register, pitch, gain);
        }

        // ------------------------------------------------------------------ damage (host)

        public bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction)
        {
            if (_maxHealth.Value <= 0 || _health.Value <= 0) return false; // can't be hurt / already down
            _health.Value = Mathf.Max(0, _health.Value - damage);
            Vector3 push = new Vector3(direction.x, 0f, direction.z).normalized;
            _knock = push * (kind == DamageKind.Bullet ? 2f : 3.5f);
            _staggerUntil = Time.time + (kind == DamageKind.Bullet ? 0.35f : 0.6f);
            HitObservers(point, push, _health.Value, kind == DamageKind.Bullet);
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
        private void HitObservers(Vector3 point, Vector3 push, int healthLeft, bool bullet)
        {
            if (_animator != null) _animator.Flinch(push);
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
            UpdateDizzy();
            // Idle: turn the head (and eventually the body) toward the nearest player.
            float yaw = transform.eulerAngles.y;
            var flatSpeed = new Vector2(_velocity.x, _velocity.z).magnitude;
            // (Only someone close and roughly in front: a lifeguard walking past behind two tourists chatting doesn't
            // spin them round.)
            if (flatSpeed < 0.3f && _pose.Value is AvatarPose.Normal or AvatarPose.Scared && NearestPlayer(4.5f) is { } near)
            {
                Vector3 to = near.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Angle(transform.forward, to) < 110f) yaw = Quaternion.LookRotation(to).eulerAngles.y;
            }
            // (On the move the body is drawn the way it is really turned, at once: the legs walk where the body faces.)
            _lookYaw = Mathf.LerpAngle(_lookYaw, yaw, 1f - Mathf.Exp(-(flatSpeed > 0.3f ? 18f : 6f) * Time.deltaTime));
            float submerged = WaterSurface.Exists ? WaterSurface.HeightAt(transform.position) - transform.position.y : 0f;
            bool wasSwimming = _swimPose;
            _swimPose = _swimPose ? submerged > SwimAbove - 0.12f : submerged > SwimAbove; // no flicker on a wave
            bool swimming = _swimPose;
            if (swimming != wasSwimming) FitColliders(_pose.Value);
            _animator.Motion = new AvatarMotion
            {
                Velocity = _pose.Value == AvatarPose.Normal || _pose.Value == AvatarPose.Scared ? _velocity : Vector3.zero,
                FacingYaw = _lookYaw,
                Grounded = true,
                Swimming = swimming && _ride == null,
                Pose = _pose.Value,
                Mood = _mood.Value,
                Talking = Time.time < _talkUntil,
                Seated = false,
                LookBack = _role.Value == NpcRole.Robber && _pose.Value == AvatarPose.Normal && !swimming
            };
            // Leading a lifeguard by the hand (the beach hut gag): her right hand back in his.
            if (LoveHut.HandHold(transform, out LoveHut.HandGripPoint hold))
            {
                AvatarMotion m = _animator.Motion;
                m.Holding = true;
                m.GripRight = new HandGrip(hold.Point, hold.Toward, -transform.right, HandPose.LooseFist);
                _animator.Motion = m;
            }
        }

        private void UpdateNameTag()
        {
            if (_nameTag == null) return;
            PlayerHub local = PlayerHub.Local;
            Camera cam = Camera.main;
            bool show = local != null && cam != null && (local.transform.position - transform.position).sqrMagnitude < 9f * 9f && !string.IsNullOrEmpty(_name.Value);
            if (_nameTag.gameObject.activeSelf != show) _nameTag.gameObject.SetActive(show);
            if (!show) return;
            Vector3 at = HeadPosition + Vector3.up * 0.45f;
            _nameTag.transform.SetPositionAndRotation(at, cam.transform.rotation);
            // The same size on screen from arm's length out to a few metres (full size beyond that): a world-sized
            // label is a billboard in your face when you stand next to someone.
            _nameTag.transform.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(cam.transform.position, at) / TagFullSizeAt, 0.22f, 1f);
            // The words only change with the name or a hit.
            int health = _maxHealth.Value > 0 ? _health.Value : -1;
            if (health == _tagHealth && ReferenceEquals(_tagName, _name.Value)) return;
            _tagHealth = health;
            _tagName = _name.Value;
            _nameTag.text = health > 0
                ? $"{Name}  {new string('●', health)}{new string('○', Mathf.Max(0, _maxHealth.Value - health))}"
                : Name;
        }

        private const float TagFullSizeAt = 8f;

        // Knocked out: three little stars circle the head.
        private Transform[] _stars;

        private void UpdateDizzy()
        {
            bool down = _pose.Value == AvatarPose.Down && _maxHealth.Value > 0 && _ride == null;
            if (!down && _stars == null) return;
            if (_stars == null)
            {
                _stars = new Transform[5];
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                for (int i = 0; i < _stars.Length; i++)
                {
                    var go = new GameObject("DizzyStar");
                    go.transform.SetParent(transform, false);
                    var mesh = go.AddComponent<TextMesh>();
                    mesh.font = font;
                    go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                    mesh.text = "*";
                    mesh.fontSize = 96;
                    mesh.characterSize = 0.035f;
                    mesh.fontStyle = FontStyle.Bold;
                    mesh.anchor = TextAnchor.MiddleCenter;
                    mesh.color = new Color(1f, 0.88f, 0.25f);
                    _stars[i] = go.transform;
                }
            }
            Camera cam = Camera.main;
            for (int i = 0; i < _stars.Length; i++)
            {
                Transform star = _stars[i];
                if (star.gameObject.activeSelf != down) star.gameObject.SetActive(down);
                if (!down) continue;
                float a = Time.time * 2.6f + i * (Mathf.PI * 2f / _stars.Length);
                Vector3 head = _rig != null && _rig.IsBuilt ? _rig[AvatarRig.Bone.Head].position : HeadPosition;
                star.position = head + new Vector3(Mathf.Cos(a) * 0.32f, 0.3f + Mathf.Sin(a * 2f) * 0.04f, Mathf.Sin(a) * 0.32f);
                if (cam != null) star.rotation = cam.transform.rotation;
            }
        }
        private int _tagHealth = int.MinValue;
        private string _tagName;

        public static PlayerHub NearestPlayer(Vector3 from, float range)
        {
            PlayerHub best = null;
            float bestSq = range * range;
            IReadOnlyList<PlayerHub> players = PlayerHub.All;
            for (int i = 0; i < players.Count; i++)
            {
                PlayerHub p = players[i];
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

            Unstick();
            p = transform.position;
            if (_moveTarget.HasValue && _pose.Value is AvatarPose.Normal or AvatarPose.Scared)
                FollowPath(p, dt);
            if (!_moveTarget.HasValue && _facePoint.HasValue)
                Face(_facePoint.Value - p, dt, 5f);
            // Begging on the knees: to the face of whoever caught him, not to the sand behind him.
            else if (!_moveTarget.HasValue && _pose.Value == AvatarPose.Kneel && NearestPlayer(8f) is { } catcher)
                Face(catcher.transform.position - p, dt, 4f);
            Separate(dt);
            // Shoved off course (two swimmers meeting, someone walking into them): they turn the way they are really
            // going instead of sliding along sideways, then pick their route up again.
            Vector3 went = transform.position - p;
            went.y = 0f;
            if (_moveTarget.HasValue && Upright(_pose.Value) && went.sqrMagnitude > 0.35f * 0.35f * dt * dt &&
                Vector3.Dot(transform.forward, went.normalized) < 0.55f)
                Face(went, dt, 12f);
            // Spawned in the air or into a dune: onto the ground. (Not someone placed on a seat: feet on a stool's footrest.)
            if (!_moveTarget.HasValue && _pose.Value != AvatarPose.SitChair)
            {
                Vector3 here = transform.position, grounded = Grounded(here);
                if (grounded.y != here.y) transform.position = grounded;
            }
        }

        /// <summary>Don't stand inside each other (a gang of pirates spreads out around their target).</summary>
        private void Separate(float dt)
        {
            if (!Upright(_pose.Value)) return;
            Vector3 p = transform.position, push = Vector3.zero;
            foreach (StoryNpc other in _all)
            {
                if (other == this || other._ride != null || !Upright(other._pose.Value)) continue;
                Vector3 d = p - other.transform.position;
                d.y = 0f;
                float distance = d.magnitude;
                if (distance < 0.85f)
                    push += (distance > 0.01f ? d / distance : Random.insideUnitSphere) * (0.85f - distance);
            }
            push.y = 0f;
            if (push.sqrMagnitude > 1e-6f) transform.position = Grounded(MoveChecked(p, Vector3.ClampMagnitude(push, 1f) * Mathf.Min(1f, dt * 6f)));
        }

        private void EndRoute(bool arrived, string why = null)
        {
            _moveTarget = null;
            _path.Clear();
            if (!arrived)
            {
                _gaveUp = why;
                Debug.Log($"[Npc] {Name} gave up walking: {why}");
            }
            ServerRouteEnded?.Invoke(this, arrived);
        }

        private void FollowPath(Vector3 p, float dt)
        {
            if (_path.Count == 0) PlanPath();
            Vector3 waypoint = _path[Mathf.Min(_pathIndex, _path.Count - 1)];
            Vector3 to = waypoint - p;
            to.y = 0f;
            float distance = to.magnitude;
            bool last = _pathIndex >= _path.Count - 1;
            // Corners are passed a little early (no stop-and-turn at each one); the destination is reached exactly.
            if (distance < (last ? 0.15f : Mathf.Clamp(_moveSpeed * 0.25f, 0.2f, 0.6f)))
            {
                if (++_pathIndex >= _path.Count) EndRoute(true); // arrived (or as close as the navmesh gets)
                _progressBest = float.MaxValue;
                _progressSince = Time.time;
                return;
            }

            // Ease into the destination instead of stopping dead.
            // (Not a swimmer: the end of a leg is only where they turn, and the next leg starts at once.)
            float speed = last && !IsSwimming ? Mathf.Min(_moveSpeed, 0.6f + distance * 1.5f) : _moveSpeed;
            Vector3 dir = Avoid(p, to / distance);
            // People go the way they face: turned away from where the route leads (setting off, a sharp corner, pushed
            // round by someone), they turn on the spot first and only pick up speed as they come round. Nobody slides
            // off backwards or sideways. (A swimmer keeps some way on: they turn in an arc.)
            // (Judged against the route itself, not the swerve round someone in the way: two people passing each
            // other must not stop to re-aim every frame.)
            Vector3 route = to / distance;
            float facing = Vector3.Dot(transform.forward, route);
            float go = Mathf.Clamp01((facing - 0.15f) / 0.6f);
            bool turning = go < 0.25f;
            _turnTime = turning ? _turnTime + dt : 0f;
            // Someone running flat out (the robber dodging a lifeguard) swings round in a quick arc instead of stopping
            // to turn on the spot each time he changes his mind.
            bool sprinting = speed > 4f && !IsSwimming;
            speed *= IsSwimming ? Mathf.Lerp(0.3f, 1f, go) : sprinting ? Mathf.Lerp(0.4f, 1f, go) : go;
            Vector3 step = dir * Mathf.Min(distance, speed * dt);
            Vector3 moved = MoveChecked(p, step);
            Vector3 next = Grounded(moved);
            // Story characters don't wander into deep water (the robber would drown on us); beach tourists may.
            if (_allowWater || Shore.WaterDepthAt(next + Vector3.up * 0.1f) < 0.5f || Shore.WaterDepthAt(p) >= 0.5f)
                transform.position = next;
            else
            {
                EndRoute(false, "the way goes into deep water");
                return;
            }
            // Turn toward where we're heading (toward the next corner when close to this one, so turns are rounded).
            Vector3 heading = step;
            if (!last && distance < 1f)
            {
                Vector3 after = _path[_pathIndex + 1] - p;
                after.y = 0f;
                if (after.sqrMagnitude > 1e-4f) heading = Vector3.Lerp(after.normalized, step.normalized, distance);
            }
            Face(turning ? route : heading, dt, turning ? (sprinting ? 18f : 11f) : sprinting ? 12f : 8f);

            // Pinned against something the path didn't know about (a player, a tourist, a parked jet ski): plan again,
            // then step aside, and only then give up (whoever sent it picks something else to do).
            // (Turning to set off isn't being stuck; turning for ever, pushed round and round by someone, is.)
            bool progressed = (turning && _turnTime < 0.6f) || (new Vector2(moved.x - p.x, moved.z - p.z)).sqrMagnitude > step.sqrMagnitude * 0.09f;
            _stuckTime = progressed ? 0f : _stuckTime + dt;
            // Also stuck: moving but not getting any closer (pushed back and forth, circling a corner).
            if (distance < _progressBest - 0.25f)
            {
                _progressBest = distance;
                _progressSince = Time.time;
            }
            if (Time.time - _progressSince > 2.5f + 1.5f / Mathf.Max(0.3f, _moveSpeed))
            {
                _stuckTime = 1f;
                _progressBest = distance;
                _progressSince = Time.time;
            }
            if (_stuckTime > 0.7f && _moveTarget.HasValue)
            {
                // Blocked right next to the destination (someone standing on it): that's close enough.
                Vector3 left = _moveTarget.Value - p;
                left.y = 0f;
                if (left.magnitude < 1.5f)
                {
                    EndRoute(true);
                    return;
                }
                _replans++;
                // A swimmer gives up sooner: open water always has somewhere else to go, and treading water in front
                // of a buoy for half a minute looks broken.
                if (_replans > (IsSwimming ? 3 : 6)) EndRoute(false, $"blocked at {p:F1} on the way to {_moveTarget.Value:F1}");
                else if (_replans % 2 == 1) PlanPath();
                else SideStep(p, step);
                _stuckTime = 0f;
            }
        }

        /// <summary>
        /// Walking into someone: veer off to the side they're not on (two people meeting head-on both keep right), more
        /// the closer and more squarely in front they are.
        /// </summary>
        private Vector3 Avoid(Vector3 p, Vector3 dir)
        {
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            Vector3 avoid = Vector3.zero;
            foreach (StoryNpc other in _all)
            {
                if (other == this || other._ride != null || !Upright(other._pose.Value)) continue;
                Vector3 d = other.transform.position - p;
                d.y = 0f;
                float distance = d.magnitude;
                if (distance > 1.8f || distance < 0.01f) continue;
                float ahead = Vector3.Dot(d / distance, dir);
                if (ahead < 0.2f) continue;
                float side = Vector3.Dot(d, right);
                float away = Mathf.Abs(side) < 0.05f ? 1f : -Mathf.Sign(side); // dead ahead: keep right
                avoid += right * (away * ahead * (1.8f - distance) / 1.8f);
            }
            if (avoid.sqrMagnitude < 1e-6f) return dir;
            Vector3 steered = dir + avoid * 1.6f;
            steered.y = 0f;
            return steered.sqrMagnitude > 1e-6f ? steered.normalized : dir;
        }

        /// <summary>Blocked: put a detour point to one side (whichever is free) at the front of the route.</summary>
        private void SideStep(Vector3 p, Vector3 step)
        {
            Vector3 forward = step.sqrMagnitude > 1e-8f ? step.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward);
            foreach (float sign in Random.value < 0.5f ? new[] { 1f, -1f } : new[] { -1f, 1f })
                for (float d = 0.8f; d <= 2.4f; d += 0.8f)
                {
                    Vector3 dir = (side * sign + forward * 0.3f).normalized;
                    if (ObstacleAhead(p, dir, d, out _)) continue;
                    Vector3 detour = p + dir * d;
                    if (NavMesh.SamplePosition(detour, out NavMeshHit hit, 0.6f, AreaMask)) detour = new Vector3(hit.position.x, detour.y, hit.position.z);
                    _path.Insert(Mathf.Min(_pathIndex, _path.Count), detour);
                    return;
                }
            PlanPath();
        }

        /// <summary>Standing inside something solid (spawned there, knocked into it): step out of it.</summary>
        private void Unstick()
        {
            if (_bodyCollider == null || Time.time < _nextUnstick || !Upright(_pose.Value)) return;
            _nextUnstick = Time.time + 0.2f;
            Vector3 p = transform.position;
            int n = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * 0.55f, p + Vector3.up * 1.5f, 0.24f, _overlapHits, ~0, QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Collider c = _overlapHits[i];
                if (c == _bodyCollider || c == _headCollider || c.attachedRigidbody != null || IsGround(c) || c is TerrainCollider) continue;
                if (c.bounds.max.y <= p.y + StepHeight) continue; // a step being climbed (dock deck), not a wall
                if (Physics.ComputePenetration(_bodyCollider, p, transform.rotation, c, c.transform.position, c.transform.rotation, out Vector3 dir, out float depth))
                    push += new Vector3(dir.x, 0f, dir.z) * depth;
            }
            if (push.sqrMagnitude > 1e-6f) transform.position = Grounded(p + Vector3.ClampMagnitude(push * 1.05f, 0.5f));
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
                if (IsGround(c) || (body != null && (body == _rigidbody || body == _walksWith || (_ride != null && body == _ride.Body)))) continue; // ourselves, our boat, our partner
                if (body == null && c.bounds.max.y <= p.y + StepHeight) continue; // a step up (dock deck, kerb): Grounded climbs it
                if (body != null && IsCharacter(body)) continue;                   // other characters: steered round (Avoid)
                bestDistance = h.distance;
                best = h;
            }
            return bestDistance < float.MaxValue;
        }

        /// <summary>Static edges up to this high are stepped onto (a dock deck from low sand is ~0.5 m).</summary>
        private const float StepHeight = 0.6f;

        private static readonly Dictionary<Collider, bool> _groundColliders = new();
        private static readonly Dictionary<Rigidbody, bool> _characterBodies = new();

        internal static bool IsCharacter(Rigidbody body)
        {
            if (!_characterBodies.TryGetValue(body, out bool yes)) _characterBodies[body] = yes = body.GetComponent<StoryNpc>() != null;
            return yes;
        }

        /// <summary>The island terrain (dunes, seabed slopes) is walked on, never a wall.</summary>
        private static bool IsGround(Collider c)
        {
            if (!_groundColliders.TryGetValue(c, out bool ground))
                _groundColliders[c] = ground = c.GetComponent<World.Water.Seabed>() != null;
            return ground;
        }

        private static bool Upright(AvatarPose pose) => pose is AvatarPose.Normal or AvatarPose.Scared or AvatarPose.HandsUp;

        /// <summary>
        /// Sitting on a towel, the hips are this far behind the feet (the character's root): lying on the back, on the
        /// belly and sitting up all share one root, so turning over or getting up never makes the body jump.
        /// </summary>
        public const float SitBack = AvatarAnimator.SitBack;

        /// <summary>Host (tests): joints where they can't be (stretched limbs, head under the hips while standing, feet off the ground).</summary>
        public bool BodyProblem(out string what)
        {
            what = null;
            if (_rig == null || !_rig.IsBuilt) return false;
            Transform hips = _rig[AvatarRig.Bone.Hips], head = _rig[AvatarRig.Bone.Head];
            if (float.IsNaN(hips.position.x) || float.IsNaN(head.position.x)) what = "NaN joint positions";
            else if (Upright(_pose.Value) && !_swimPose && head.position.y < hips.position.y + 0.25f * _rig.Scale)
                what = $"head {hips.position.y - head.position.y:F2} m below the hips while upright";
            else
            {
                float arm = (_rig.UpperArmLength + _rig.ForearmLength + _rig.HandLength) * 1.2f + 0.02f;
                float leg = (_rig.ThighLength + _rig.ShinLength) * 1.2f + 0.02f;
                (AvatarRig.Bone a, AvatarRig.Bone b, float max, string limb)[] limbs =
                {
                    (AvatarRig.Bone.UpperArmL, AvatarRig.Bone.HandL, arm, "left arm"), (AvatarRig.Bone.UpperArmR, AvatarRig.Bone.HandR, arm, "right arm"),
                    (AvatarRig.Bone.ThighL, AvatarRig.Bone.FootL, leg, "left leg"), (AvatarRig.Bone.ThighR, AvatarRig.Bone.FootR, leg, "right leg")
                };
                foreach (var (a, b, max, limb) in limbs)
                {
                    float d = Vector3.Distance(_rig[a].position, _rig[b].position);
                    if (d > max)
                    {
                        what = $"{limb} stretched to {d:F2} m (max {max:F2})";
                        break;
                    }
                }
                if (what == null && Upright(_pose.Value) && !_swimPose && _ride == null)
                {
                    float lowest = Mathf.Min(_rig[AvatarRig.Bone.FootL].position.y, _rig[AvatarRig.Bone.FootR].position.y) - transform.position.y;
                    if (lowest > 0.35f) what = $"both feet {lowest:F2} m off the ground";
                    else if (lowest < -0.2f) what = $"a foot {-lowest:F2} m into the ground";
                }
            }
            return what != null;
        }

        private void Face(Vector3 direction, float dt, float rate)
        {
            direction.y = 0f;
            // (Any length will do: a walker's step in one frame is a few millimetres at a high frame rate, and when
            // steps that small were ignored here nobody turned toward where they were going: they walked off
            // sideways or backwards.)
            float length = direction.magnitude;
            if (length < 1e-7f) return;
            Quaternion want = Quaternion.LookRotation(direction / length);
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
            ShowDisguise();
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
            if (_bag == null) _bag = RobberBag.Create();
            RobberBag.Wear(_bag, _rig);
            _bag.SetActive(true);
        }

        /// <summary>The thief looks like one: a black beanie and a bandana over his face, made to fit this body's head.</summary>
        private void ShowDisguise()
        {
            // (His own model is already dressed for it: sunglasses and a headband.)
            bool wear = _role.Value == NpcRole.Robber && _rig != null && _rig.IsBuilt && _rig.Look.Body != AvatarLook.Bodies.Robber;
            if (_disguise != null) Destroy(_disguise); // the head may have changed size with the look
            _disguise = wear ? RobberDisguise.Create(_rig) : null;
        }
    }
}
