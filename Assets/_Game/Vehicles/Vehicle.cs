using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Vehicles
{
    /// <summary>
    /// Something you sit on and drive over the water: the jet ski (needs its keys in your inventory) and the pirate
    /// boat (driven by the host's autopilot until you take it).
    ///
    /// Physics authority works like items (<see cref="ItemSync"/>): whoever drives simulates it and streams it, so
    /// steering feels instant; everyone else follows. The driver is glued to the seat on every machine, their view
    /// turns with the vehicle, and their body sits with both hands on the handlebars.
    /// Controls: W/S throttle and reverse, A/D steer, E to get off.
    /// </summary>
    [RequireComponent(typeof(ItemSync))]
    public class Vehicle : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private string _displayName = "Jet Ski";
        [Tooltip("Where the driver's hips go; forward = the vehicle's forward.")]
        [SerializeField] private Transform _seat;
        [SerializeField] private Transform _gripLeft;
        [SerializeField] private Transform _gripRight;
        [SerializeField] private Transform _thrustPoint;
        [Tooltip("Needed in the driver's inventory to start it (item display name). Empty = no key.")]
        [SerializeField] private string _keyItem = "";
        [Header("Driving")]
        [SerializeField] private float _thrust = 11f;
        [SerializeField] private float _reverseThrust = 4f;
        [SerializeField] private float _maxSpeed = 17f;
        [SerializeField] private float _turnRate = 1.7f;
        [SerializeField] private float _sideGrip = 2.4f;
        [SerializeField] private float _uprightStrength = 14f;
        [SerializeField] private AudioSource _engineAudio;

        private static readonly List<Vehicle> _all = new();

        private readonly SyncVar<PlayerHub> _driver = new SyncVar<PlayerHub>();
        private readonly SyncVar<bool> _locked = new SyncVar<bool>();
        private readonly SyncVar<string> _lockedReason = new SyncVar<string>();

        private ItemSync _sync;
        private Rigidbody _rb;
        private Collider[] _colliders;
        private float _lastYaw;
        private float _throttle;
        private Vector3? _autopilotTarget;
        private float _autopilotSpeed;
        private float _nextEnterRequest;
        private float _scriptedUntil = float.NegativeInfinity;
        private Vector2 _scriptedInput;

        public static IReadOnlyList<Vehicle> All => _all;
        public string DisplayName => _displayName;
        public PlayerHub Driver => _driver.Value;
        public bool IsLocked => _locked.Value;
        public Rigidbody Body => _rb;
        public float Speed => _rb != null ? _rb.linearVelocity.magnitude : 0f;
        /// <summary>Host: raised when someone gets on.</summary>
        public static event System.Action<Vehicle, PlayerHub> ServerDriverEntered;

        /// <summary>The vehicle this player is driving, if any.</summary>
        public static Vehicle SeatOf(PlayerHub player)
        {
            if (player == null) return null;
            foreach (Vehicle v in _all)
                if (v._driver.Value == player) return v;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            ServerDriverEntered = null;
        }

        private void Awake()
        {
            _sync = GetComponent<ItemSync>();
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _driver.OnChange += OnDriverChanged;
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _all.Add(this);
            _lastYaw = transform.eulerAngles.y;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _all.Remove(this);
        }

        // ------------------------------------------------------------------ getting on and off

        public bool CanInteract(PlayerHub player) => _driver.Value == null || _driver.Value == player;

        public string GetPrompt(PlayerHub player)
        {
            if (_driver.Value == player) return $"Get off the {_displayName}";
            if (_locked.Value) return string.IsNullOrEmpty(_lockedReason.Value) ? $"{_displayName} (locked)" : _lockedReason.Value;
            if (!string.IsNullOrEmpty(_keyItem) && !HasKey(player)) return $"{_displayName}: needs the {_keyItem.ToLowerInvariant()}";
            return $"Drive the {_displayName}";
        }

        public void OnInteract(PlayerHub player)
        {
            if (_driver.Value == player)
            {
                ExitServer();
                return;
            }
            if (_locked.Value || (!string.IsNullOrEmpty(_keyItem) && !HasKey(player)))
            {
                PlayerHud.ShowToast(GetPrompt(player), 2.5f);
                return;
            }
            if (player.Hands != null && player.Hands.HeldItem != null && !player.Hands.HeldItem.Pocketable)
            {
                PlayerHud.ShowToast($"Put the {player.Hands.HeldItem.DisplayName} down first.", 2.5f);
                return;
            }
            if (Time.time < _nextEnterRequest) return;
            _nextEnterRequest = Time.time + 0.5f;
            EnterServer(player);
        }

        private bool HasKey(PlayerHub player)
        {
            if (string.IsNullOrEmpty(_keyItem)) return true;
            foreach (Item item in Item.All)
                if (item.Holder == player && string.Equals(item.DisplayName, _keyItem, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        [ServerRpc(RequireOwnership = false)]
        private void EnterServer(PlayerHub player, NetworkConnection caller = null)
        {
            if (player == null || player.Owner != caller || _driver.Value != null || _locked.Value) return;
            if ((player.transform.position - transform.position).sqrMagnitude > 6f * 6f || SeatOf(player) != null) return;
            if (!HasKey(player)) return;
            _autopilotTarget = null;
            _driver.Value = player;
            GiveOwnership(caller); // the driver simulates it
            Debug.Log($"[Vehicle] {player.DisplayName} drives the {_displayName}");
            ServerDriverEntered?.Invoke(this, player);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ExitServer(NetworkConnection caller = null)
        {
            PlayerHub driver = _driver.Value;
            if (driver == null || driver.Owner != caller) return;
            ServerKickDriver();
        }

        /// <summary>Host: the driver gets off (or left the game).</summary>
        [Server]
        public void ServerKickDriver()
        {
            if (_driver.Value == null) return;
            Debug.Log($"[Vehicle] {_driver.Value.DisplayName} got off the {_displayName}");
            _driver.Value = null;
        }

        /// <summary>Host: lock or unlock (the pirate boat can't be taken while pirates are on it).</summary>
        [Server]
        public void ServerSetLocked(bool locked, string reason = null)
        {
            _locked.Value = locked;
            _lockedReason.Value = reason ?? string.Empty;
            if (locked) ServerKickDriver();
        }

        /// <summary>Host: drive to a point by itself (pirates arriving). Null stops.</summary>
        [Server]
        public void ServerAutopilot(Vector3? target, float speed = 8f)
        {
            _autopilotTarget = target;
            _autopilotSpeed = speed;
            if (target.HasValue && Owner.IsValid) RemoveOwnership();
        }

        /// <summary>Host: put it somewhere (story setup, teleports).</summary>
        [Server]
        public void ServerPlace(Vector3 position, float yaw)
        {
            if (Owner.IsValid) RemoveOwnership();
            _rb.position = position;
            _rb.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(position, _rb.rotation);
            if (!_rb.isKinematic) _rb.linearVelocity = _rb.angularVelocity = Vector3.zero;
        }

        private void OnDriverChanged(PlayerHub prev, PlayerHub next, bool asServer)
        {
            if (asServer && IsClientStarted) return; // a host handles it once, as a client
            // Seated bodies don't bump into their own vehicle.
            SetIgnore(prev, false);
            SetIgnore(next, true);
            _lastYaw = transform.eulerAngles.y;

            PlayerHub local = PlayerHub.Local;
            if (local != null && prev == local)
            {
                local.Motor.SetSeat(null, ExitPosition());
                PlayerHud.ShowToast($"Off the {_displayName}.", 1.5f);
            }
            if (local != null && next == local)
            {
                // Both hands on the handlebars: whatever small thing we held goes in a pocket.
                PlayerHands hands = local.Hands;
                if (hands != null && hands.HeldItem != null && hands.HeldItem.Pocketable && hands.FirstFreeSlot() is var free && free >= 0)
                    hands.SelectSlot(free);
                local.Motor.SetSeat(this, Vector3.zero);
                PlayerHud.ShowToast($"<b>W/S</b> throttle, <b>A/D</b> steer, <b>[{GameInput.KeyLabel(GameInput.Interact)}]</b> to get off.", 5f);
            }
        }

        private void SetIgnore(PlayerHub player, bool ignore)
        {
            if (player == null || player.BodyCollider == null) return;
            foreach (Collider c in _colliders)
                if (c != null) Physics.IgnoreCollision(c, player.BodyCollider, ignore);
        }

        private readonly Collider[] _exitOverlaps = new Collider[16];

        /// <summary>
        /// Where the driver gets off: beside the seat (in the water for a jet ski, on the deck for the boat), or the
        /// other side, behind, in front, on top, whichever is free first, so nobody ends up inside the dock or a hull.
        /// </summary>
        private Vector3 ExitPosition()
        {
            Transform seat = _seat != null ? _seat : transform;
            Vector3 right = Vector3.ProjectOnPlane(seat.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(seat.forward, Vector3.up).normalized;
            Vector3 up = Vector3.up * 0.2f;
            Vector3[] candidates =
            {
                seat.position + right * 1.2f + up, seat.position - right * 1.2f + up, seat.position - forward * 2f + up,
                seat.position + forward * 2.2f + up, seat.position + right * 2f + up, seat.position - right * 2f + up,
                seat.position + Vector3.up * 1.2f
            };
            Collider mine = PlayerHub.Local != null ? PlayerHub.Local.BodyCollider : null;
            foreach (Vector3 feet in candidates)
            {
                int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * 0.4f, feet + Vector3.up * 1.45f, 0.35f, _exitOverlaps, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = false;
                for (int i = 0; i < count && !blocked; i++)
                    blocked = _exitOverlaps[i] != mine;
                if (!blocked) return feet;
            }
            return candidates[candidates.Length - 1];
        }

        /// <summary>World handlebar grips (fingers forward over the bar, palms down).</summary>
        public bool GetHandlebars(out HandGrip left, out HandGrip right)
        {
            left = right = default;
            if (_gripLeft == null || _gripRight == null) return false;
            left = new HandGrip(_gripLeft.position, _gripLeft.forward, -_gripLeft.up, HandPose.LooseFist);
            right = new HandGrip(_gripRight.position, _gripRight.forward, -_gripRight.up, HandPose.LooseFist);
            return true;
        }

        /// <summary>Where the seated driver's feet go (their root), so the hips land on the seat.</summary>
        public Vector3 DriverFeetPosition => (_seat != null ? _seat.position : transform.position) - Vector3.up * 0.5f;

        // ------------------------------------------------------------------ every frame

        private void LateUpdate()
        {
            PlayerHub driver = _driver.Value;
            float yaw = transform.eulerAngles.y;
            if (driver != null)
            {
                // Glue the driver to the seat (their own machine and everyone else's).
                driver.transform.position = DriverFeetPosition;
                if (driver == PlayerHub.Local && driver.Look != null)
                    driver.Look.AddYaw(Mathf.DeltaAngle(_lastYaw, yaw)); // the view turns with the vehicle
            }
            _lastYaw = yaw;

            if (_engineAudio != null)
            {
                bool running = driver != null || _autopilotTarget.HasValue;
                if (running && !_engineAudio.isPlaying)
                {
                    _engineAudio.clip = ProceduralAudio.Engine;
                    _engineAudio.loop = true;
                    _engineAudio.Play();
                }
                else if (!running && _engineAudio.isPlaying) _engineAudio.Stop();
                _engineAudio.pitch = 0.8f + Mathf.Clamp01(Speed / _maxSpeed) * 1.1f;
            }
        }

        private void FixedUpdate()
        {
            PlayerHub driver = _driver.Value;
            _sync.KeepAwake = driver != null || _autopilotTarget.HasValue; // stream while it's going somewhere
            _sync.KeepAuthority = driver != null; // the driver's machine keeps simulating it
            if (_rb.isKinematic || !_sync.IsSimulator) return;

            float throttle = 0f, steer = 0f;
            if (driver != null && driver == PlayerHub.Local && driver.IsOwner)
            {
                Vector2 input = Time.time < _scriptedUntil ? _scriptedInput : GameInput.GameplayActive ? GameInput.Move.ReadValue<Vector2>() : Vector2.zero;
                throttle = input.y;
                steer = input.x;
            }
            else if (driver == null && _autopilotTarget.HasValue && IsServerInitialized)
            {
                Vector3 to = _autopilotTarget.Value - _rb.position;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance < 2.5f)
                {
                    _autopilotTarget = null;
                    _rb.linearVelocity *= 0.5f;
                }
                else
                {
                    float angle = Vector3.SignedAngle(Flat(transform.forward), to, Vector3.up);
                    steer = Mathf.Clamp(angle / 35f, -1f, 1f);
                    float wanted = Mathf.Min(_autopilotSpeed, distance * 0.6f);
                    throttle = Mathf.Clamp((wanted - Vector3.Dot(_rb.linearVelocity, Flat(transform.forward))) * 0.5f, -1f, 1f);
                }
            }
            Drive(throttle, steer);
        }

        private void Drive(float throttle, float steer)
        {
            _throttle = Mathf.MoveTowards(_throttle, throttle, Time.fixedDeltaTime * 3f);
            Vector3 thrustAt = _thrustPoint != null ? _thrustPoint.position : _rb.position - transform.forward * 1f;
            bool inWater = WaterSurface.Exists && WaterSurface.DepthOf(thrustAt) > -0.15f;
            Vector3 forward = Flat(transform.forward);
            Vector3 right = Flat(transform.right);
            Vector3 v = _rb.linearVelocity;
            float forwardSpeed = Vector3.Dot(v, forward);

            if (inWater)
            {
                float push = _throttle >= 0f ? _throttle * _thrust : _throttle * _reverseThrust;
                if (forwardSpeed > _maxSpeed && push > 0f) push = 0f;
                _rb.AddForce(forward * push, ForceMode.Acceleration);
                // Hull grip: the water kills sideways sliding (less at speed: a little drift is fun).
                float side = Vector3.Dot(v, right);
                _rb.AddForce(-right * side * _sideGrip, ForceMode.Acceleration);
                // Steering needs some speed (or a bit of throttle), and flips when reversing.
                float authority = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / 3f + Mathf.Abs(_throttle) * 0.3f) * (forwardSpeed < -0.5f ? -1f : 1f);
                float wantYaw = steer * _turnRate * authority;
                _rb.AddTorque(Vector3.up * ((wantYaw - _rb.angularVelocity.y) * 4f), ForceMode.Acceleration);
                // Lean into turns a little, nose up under throttle.
                Vector3 lean = transform.forward * (-steer * 0.25f * Mathf.Clamp01(forwardSpeed / _maxSpeed));
                _rb.AddTorque(lean * _uprightStrength, ForceMode.Acceleration);
            }

            // Stay upright (it's a toy, not a simulator).
            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);
            _rb.AddTorque(tilt * _uprightStrength - new Vector3(_rb.angularVelocity.x, 0f, _rb.angularVelocity.z) * 3f, ForceMode.Acceleration);
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _autopilotTarget = null;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("drive", "<seconds> [steer -1..1] [throttle]", "Drive the vehicle you're on, on autopilot (automated tests).", args =>
            {
                Vehicle v = SeatOf(PlayerHub.Local);
                if (v == null) throw new System.InvalidOperationException("not driving anything");
                v._scriptedUntil = Time.time + DevCommands.ParseFloat(args, 0);
                v._scriptedInput = new Vector2(args.Length > 1 ? DevCommands.ParseFloat(args, 1) : 0f, args.Length > 2 ? DevCommands.ParseFloat(args, 2) : 1f);
            }, cheat: true, owner: this);
            DevCommands.Register("vehicles", "", "List vehicles.", _ =>
            {
                foreach (Vehicle v in _all)
                    DevCommands.Print($"  {v._displayName,-12} at {v.transform.position:F1} speed {v.Speed:F1}  driver {(v.Driver != null ? v.Driver.DisplayName : "-")}  sim {v._sync.AuthorityLabel}{(v.IsLocked ? "  locked" : "")}");
            }, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("drive", this);
            DevCommands.Unregister("vehicles", this);
        }

        private void Update()
        {
            PlayerHub driver = _driver.Value;
            // The driver left the game: free the seat.
            if (IsServerInitialized && !ReferenceEquals(driver, null) && (driver == null || !driver.IsSpawned))
                _driver.Value = null;
            // Our driver: Interact always means "get off" (whatever the crosshair is on).
            if (driver != null && driver == PlayerHub.Local && GameInput.GameplayActive && GameInput.Interact.WasPressedThisFrame())
                ExitServer();
        }
    }
}
