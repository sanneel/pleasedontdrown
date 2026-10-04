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
        private BoxCollider _hull;
        private readonly RaycastHit[] _overhead = new RaycastHit[8];
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
            Transform hull = transform.Find("Hull");
            _hull = hull != null ? hull.GetComponent<BoxCollider>() : null;
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
            return _thrust > 0f ? $"Drive the {_displayName}" : $"Ride the {_displayName}"; // towed things (the banana boat) are ridden
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

        /// <summary>
        /// Host: the driver gets off (or left the game). The engine cuts out; the hull coasts to a stop quickly (it
        /// doesn't keep ploughing on and run over the one who just jumped off), and the simulation goes back to the
        /// host a moment later, once the ex-driver's machine has streamed the stop.
        /// </summary>
        [Server]
        public void ServerKickDriver()
        {
            if (_driver.Value == null) return;
            Debug.Log($"[Vehicle] {_driver.Value.DisplayName} got off the {_displayName}");
            _driver.Value = null;
            _throttle = 0f;
            if (!_rb.isKinematic) _rb.linearVelocity *= 0.3f;
            StopBrakingOwner(); // the ex-driver's machine brakes too, if it's the simulator
        }

        [ObserversRpc]
        private void StopBrakingOwner()
        {
            _brakeUntil = Time.time + 2.5f;
            _throttle = 0f;
        }

        private float _brakeUntil = float.NegativeInfinity;

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
            if (target.HasValue) ServerKickDriver(); // nobody rides along on autopilot
            _autopilotTarget = target;
            _autopilotSpeed = speed;
            if (target.HasValue && Owner.IsValid) RemoveOwnership();
        }

        /// <summary>Host: put it somewhere (story setup, teleports). Whoever sat on it gets off first.</summary>
        [Server]
        public void ServerPlace(Vector3 position, float yaw)
        {
            ServerKickDriver();
            if (Owner.IsValid) RemoveOwnership();
            _rb.position = position;
            _rb.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(position, _rb.rotation);
            if (!_rb.isKinematic) _rb.linearVelocity = _rb.angularVelocity = Vector3.zero;
        }

        private void OnDriverChanged(PlayerHub prev, PlayerHub next, bool asServer)
        {
            if (asServer && IsClientStarted) return; // a host handles it once, as a client
            // Seated bodies don't bump into their own vehicle. Getting off, they stay ghosts to it until they are
            // really clear of the hull, so a hull still drifting (or bobbing on a wave) can't trap, shove or launch
            // them while they swim away.
            if (prev != null && prev != next) StartCoroutine(RestoreCollisionWhenClear(prev));
            SetIgnore(next, true);
            _lastYaw = transform.eulerAngles.y;

            PlayerHub local = PlayerHub.Local;
            if (local != null && prev == local && local.Motor != null && local.Motor.Seat == this)
            {
                // Off beside the hull, drifting with it a little and a small push away, so we don't start in its path.
                Vector3 feet = ExitPosition();
                Vector3 away = Vector3.ProjectOnPlane(feet - transform.position, Vector3.up);
                Vector3 drift = _rb != null && !_rb.isKinematic ? Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up) * 0.3f : Vector3.zero;
                Vector3 push = away.sqrMagnitude > 1e-4f ? away.normalized * 1.2f : Vector3.zero;
                local.Motor.SetSeat(null, feet, drift + push);
                PlayerHud.ShowToast($"Off the {_displayName}.", 1.5f);
            }
            if (local != null && next == local)
            {
                // Both hands on the handlebars: whatever small thing we held goes in a pocket; if every pocket is
                // full it's dropped (a dangling item would keep shoving the hull from the inside).
                PlayerHands hands = local.Hands;
                if (hands != null && hands.HeldItem != null && hands.HeldItem.Pocketable)
                {
                    int free = hands.FirstFreeSlot();
                    if (free >= 0) hands.SelectSlot(free);
                    else hands.Drop();
                }
                local.Motor.SetSeat(this, Vector3.zero);
                GameInput.Rebind[] keys = GameInput.Rebindable; // forward, back, left, right come first
                string Key(int i) => GameInput.KeyLabel(keys[i].Action, keys[i].Binding);
                PlayerHud.ShowToast($"<b>{Key(0)}/{Key(1)}</b> throttle, <b>{Key(2)}/{Key(3)}</b> steer, <b>[{GameInput.KeyLabel(GameInput.Interact)}]</b> to get off.", 5f);
            }
        }

        private readonly HashSet<PlayerHub> _ghosts = new();

        /// <summary>
        /// Collision with the ex-driver comes back only once their body has been clear of every hull collider for
        /// half a second (checked five times a second), or after 8 s at most. Never while they sit on it again.
        /// </summary>
        private System.Collections.IEnumerator RestoreCollisionWhenClear(PlayerHub player)
        {
            float clearFor = 0f;
            float started = Time.time;
            var wait = new WaitForSeconds(0.2f);
            while (player != null && _driver.Value != player && Time.time - started < 8f)
            {
                clearFor = TouchesHull(player) ? 0f : clearFor + 0.2f;
                if (clearFor >= 0.5f) break;
                yield return wait;
            }
            if (player != null && _driver.Value != player) SetIgnore(player, false);
        }

        private bool TouchesHull(PlayerHub player)
        {
            if (player.BodyCollider is not CapsuleCollider body) return false;
            Vector3 feet = player.transform.position;
            float r = body.radius + 0.15f;
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * r, feet + Vector3.up * Mathf.Max(r, body.height - r), r,
                _exitOverlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (_exitOverlaps[i].transform.IsChildOf(transform)) return true;
            return false;
        }

        private void SetIgnore(PlayerHub player, bool ignore)
        {
            if (player == null || player.BodyCollider == null) return;
            if (ignore) _ghosts.Add(player); else _ghosts.Remove(player);
            foreach (Collider c in _colliders)
                if (c != null) Physics.IgnoreCollision(c, player.BodyCollider, ignore);
        }

        /// <summary>A swimmer may climb up onto this vehicle only while it's still and they're solid to it.</summary>
        public bool AllowsClimbOnto(PlayerHub player) =>
            player != null && !_ghosts.Contains(player) && _driver.Value == null && Speed < 0.8f;

        /// <summary>Where to put someone who has to get off right now (their seat vanished): a free spot beside it.</summary>
        public Vector3 SafeExitPosition() => ExitPosition();

        private readonly Collider[] _exitOverlaps = new Collider[32];

        /// <summary>
        /// Where the driver gets off: beside the seat (in the water for a jet ski, on the deck for the boat), else the
        /// other side, behind, in front, then further out all round, whichever is free first, so nobody ends up
        /// inside the dock, a rock or a hull. Our own body, whatever we carry and this vehicle's riders don't count.
        /// </summary>
        private Vector3 ExitPosition()
        {
            Transform seat = _seat != null ? _seat : transform;
            Vector3 right = Vector3.ProjectOnPlane(seat.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(seat.forward, Vector3.up).normalized;
            Vector3 start = seat.position + Vector3.up * 0.2f;
            PlayerHub local = PlayerHub.Local;

            var candidates = new List<Vector3>
            {
                start + right * 1.3f, start - right * 1.3f, start - forward * 2.2f, start + forward * 2.4f
            };
            foreach (float distance in new[] { 2f, 3f, 4.5f, 6f, 8f })
                for (int i = 0; i < 12; i++)
                    candidates.Add(start + Quaternion.Euler(0f, i * 30f, 0f) * right * distance);

            foreach (Vector3 feet in candidates)
                if (ExitFree(feet, local))
                    return feet;
            // Nowhere free at all (packed in by the dock, rocks and people): straight up off the seat. We stay a
            // ghost to this hull until clear of it, so we drop through it into the water and swim out, never stuck.
            Debug.LogWarning($"[Vehicle] no free spot to get off the {_displayName}: dropping through it");
            return seat.position + Vector3.up * 0.3f;
        }

        private bool ExitFree(Vector3 feet, PlayerHub local)
        {
            const float radius = 0.4f;
            int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * (radius + 0.05f), feet + Vector3.up * 1.6f, radius,
                _exitOverlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = _exitOverlaps[i];
                if (local != null && c.transform.IsChildOf(local.transform)) continue; // ourselves
                Rigidbody body = c.attachedRigidbody;
                if (local != null && body != null && body.TryGetComponent(out Item item) && item.Holder == local) continue; // what we carry
                return false;
            }
            // Something solid between the seat and the spot (a dock post, a wall): not through it.
            Vector3 from = (_seat != null ? _seat.position : transform.position) + Vector3.up * 0.3f;
            Vector3 to = feet + Vector3.up * 0.5f;
            foreach (RaycastHit hit in Physics.RaycastAll(from, to - from, (to - from).magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (local != null && hit.collider.transform.IsChildOf(local.transform)) continue;
                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.TryGetComponent(out Item carried) && carried.Holder == local) continue;
                return false;
            }
            return true;
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

        /// <summary>
        /// Glue the driver to the seat (their own machine and everyone else's) and turn their view with the hull.
        /// Done in Update, right after the physics interpolation moved the hull, so every LateUpdate (camera, hands,
        /// body) this frame already sees the rider on the seat: gluing later made the view lag a frame and judder.
        /// Again in LateUpdate so a remote rider's NetworkTransform smoothing can't pull them off the seat.
        /// </summary>
        private void GlueDriver(bool turnView)
        {
            PlayerHub driver = _driver.Value;
            float yaw = transform.eulerAngles.y;
            if (driver != null)
            {
                Vector3 feet = DriverFeetPosition;
                driver.transform.position = feet;
                if (driver.TryGetComponent(out Rigidbody body) && body.isKinematic) body.position = feet;
                if (turnView && driver == PlayerHub.Local && driver.Look != null)
                    driver.Look.AddYaw(Mathf.DeltaAngle(_lastYaw, yaw)); // the view turns with the vehicle
            }
            if (turnView) _lastYaw = yaw;
        }

        private void LateUpdate()
        {
            PlayerHub driver = _driver.Value;
            GlueDriver(false);

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

            // Just abandoned: the hull slows to a stop instead of gliding on (over the one who jumped off).
            if (driver == null && !_autopilotTarget.HasValue && Time.time < _brakeUntil)
            {
                Vector3 flat = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
                _rb.AddForce(-flat * 1.8f, ForceMode.Acceleration);
                _rb.AddTorque(Vector3.up * (-_rb.angularVelocity.y * 3f), ForceMode.Acceleration);
            }

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

            // Wedged under a dock: the water lifts the hull against the planks and it sticks there. While something solid
            // is right over it, press it down under the edge so the engine (or the waves) can take it out.
            if (inWater && UnderSomething()) _rb.AddForce(Vector3.down * 7f, ForceMode.Acceleration);

            // Stay upright (it's a toy, not a simulator).
            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);
            _rb.AddTorque(tilt * _uprightStrength - new Vector3(_rb.angularVelocity.x, 0f, _rb.angularVelocity.z) * 3f, ForceMode.Acceleration);
        }

        /// <summary>Is there something fixed (a dock, a rock) just above the hull's top, at its nose, middle or tail?</summary>
        private bool UnderSomething()
        {
            if (_hull == null) return false;
            Transform t = _hull.transform;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 top = t.TransformPoint(_hull.center + new Vector3(0f, _hull.size.y * 0.5f, _hull.size.z * 0.4f * i));
                int count = Physics.RaycastNonAlloc(top - transform.up * 0.05f, Vector3.up, _overhead, 0.35f, ~0, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                {
                    Collider c = _overhead[h].collider;
                    if (c.attachedRigidbody == null && !c.transform.IsChildOf(transform)) return true;
                }
            }
            return false;
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
            if (driver != null && driver == PlayerHub.Local && GameInput.GameplayActive && GameInput.Interact.WasPressedThisFrame()
                && Time.time > _nextEnterRequest) // not the same press that just got us on
            {
                _nextEnterRequest = Time.time + 0.5f;
                ExitServer();
            }
            GlueDriver(true);
        }
    }
}
