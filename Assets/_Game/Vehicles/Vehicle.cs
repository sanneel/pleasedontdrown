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
        [Tooltip("Back seats for riders (hips; forward = the vehicle's forward). Children GripLeft / GripRight: their hands.")]
        [SerializeField] private Transform[] _backSeats;
        [Tooltip("Riders sit astride (legs either side): a banana, a jet ski's back seat.")]
        [SerializeField] private bool _straddle;
        [Tooltip("Hands rest open on the float rather than closing around handlebars.")]
        [SerializeField] private bool _restHands;
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
        private readonly SyncVar<PlayerHub> _rider1 = new SyncVar<PlayerHub>();
        private readonly SyncVar<PlayerHub> _rider2 = new SyncVar<PlayerHub>();
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

        /// <summary>The vehicle this player is on, driving or on a back seat.</summary>
        public static Vehicle RideOf(PlayerHub player)
        {
            if (player == null) return null;
            // Our own player: what our own machine says (off at once on getting off, before the host has caught up).
            if (player == PlayerHub.Local && player.Motor != null) return player.Motor.Seat;
            foreach (Vehicle v in _all)
                if (v.SeatIndexOf(player) >= 0) return v;
            return null;
        }

        /// <summary>Host: aboard any vehicle by the synced seats (not a machine's own view of itself).</summary>
        private static bool AboardAny(PlayerHub player)
        {
            foreach (Vehicle v in _all)
                if (v.SeatIndexOf(player) >= 0) return true;
            return false;
        }

        public int BackSeatCount => _backSeats == null ? 0 : Mathf.Min(_backSeats.Length, 2);
        public bool Straddle => _straddle;
        public bool RestHands => _restHands;

        /// <summary>Who sits on back seat <paramref name="i"/> (0 or 1).</summary>
        public PlayerHub Rider(int i) => i == 0 ? _rider1.Value : i == 1 ? _rider2.Value : null;

        private SyncVar<PlayerHub> RiderVar(int i) => i == 0 ? _rider1 : _rider2;

        /// <summary>0 driving, 1.. a back seat, -1 not on it.</summary>
        public int SeatIndexOf(PlayerHub player)
        {
            if (player == null) return -1;
            if (_driver.Value == player) return 0;
            for (int i = 0; i < BackSeatCount; i++)
                if (Rider(i) == player) return i + 1;
            return -1;
        }

        public bool IsAboard(PlayerHub player) => SeatIndexOf(player) >= 0;

        /// <summary>Everyone on it: the driver first, then the riders.</summary>
        public List<PlayerHub> Aboard()
        {
            var list = new List<PlayerHub>();
            if (_driver.Value != null) list.Add(_driver.Value);
            for (int i = 0; i < BackSeatCount; i++)
                if (Rider(i) != null) list.Add(Rider(i));
            return list;
        }

        private Transform SeatTransform(int index) => index <= 0 ? (_seat != null ? _seat : transform) : _backSeats[index - 1];

        private int FreeBackSeat()
        {
            for (int i = 0; i < BackSeatCount; i++)
                if (Rider(i) == null) return i;
            return -1;
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
            _rider1.OnChange += (prev, next, asServer) => OnRiderChanged(prev, next, asServer, 1);
            _rider2.OnChange += (prev, next, asServer) => OnRiderChanged(prev, next, asServer, 2);
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

        public bool CanInteract(PlayerHub player) => _driver.Value == null || IsAboard(player) || FreeBackSeat() >= 0 && !_locked.Value;

        public string GetPrompt(PlayerHub player)
        {
            if (IsAboard(player)) return $"Get off the {_displayName}";
            if (_driver.Value != null && FreeBackSeat() >= 0)
                return _thrust > 0f ? $"Hop on the back of the {_displayName}" : $"Ride the {_displayName} (seat {FreeBackSeat() + 2})";
            if (_locked.Value) return string.IsNullOrEmpty(_lockedReason.Value) ? $"{_displayName} (locked)" : _lockedReason.Value;
            if (!string.IsNullOrEmpty(_keyItem) && !HasKey(player)) return $"{_displayName}: needs the {_keyItem.ToLowerInvariant()}";
            return _thrust > 0f ? $"Drive the {_displayName}" : $"Ride the {_displayName}"; // towed things (the banana boat) are ridden
        }

        public void OnInteract(PlayerHub player)
        {
            if (IsAboard(player))
            {
                ExitNow();
                return;
            }
            bool backSeat = _driver.Value != null;
            if (_locked.Value || (!backSeat && !string.IsNullOrEmpty(_keyItem) && !HasKey(player)))
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
            if (backSeat) EnterBackServer(player);
            else EnterServer(player);
        }

        [ServerRpc(RequireOwnership = false)]
        private void EnterBackServer(PlayerHub player, NetworkConnection caller = null)
        {
            if (player == null || player.Owner != caller || _locked.Value || AboardAny(player)) return;
            if ((player.transform.position - transform.position).sqrMagnitude > 6f * 6f) return;
            int seat = FreeBackSeat();
            if (seat < 0) return;
            RiderVar(seat).Value = player;
            Debug.Log($"[Vehicle] {player.DisplayName} rides on the back of the {_displayName} (seat {seat + 2})");
        }

        /// <summary>Host: one rider (not the driver) gets off.</summary>
        [Server]
        public void ServerKickRider(PlayerHub rider)
        {
            for (int i = 0; i < BackSeatCount; i++)
                if (rider != null && Rider(i) == rider)
                {
                    Debug.Log($"[Vehicle] {rider.DisplayName} got off the back of the {_displayName}");
                    RiderVar(i).Value = null;
                }
        }

        /// <summary>Host: everybody off (the driver and the riders).</summary>
        [Server]
        public void ServerKickAll()
        {
            for (int i = 0; i < BackSeatCount; i++) RiderVar(i).Value = null;
            ServerKickDriver();
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

        /// <summary>
        /// Getting off, on our own machine: off at once (into the water beside it, the view gliding down), not after
        /// the host has answered (a round trip of sitting there on a friend's machine), then the host is told. The
        /// seat change coming back finds us already off.
        /// </summary>
        private void ExitNow()
        {
            PlayerHub local = PlayerHub.Local;
            int index = SeatIndexOf(local);
            if (local != null && local.Motor != null && local.Motor.Seat == this && index >= 0)
            {
                LocalGetOff(local, SeatTransform(index));
                if (index == 0)
                {
                    // The engine cuts out here and now (this machine is simulating it while we drive).
                    _throttle = 0f;
                    _brakeUntil = Time.time + 2.5f;
                }
            }
            _exitSentAt = Time.time;
            ExitServer();
        }

        private float _exitSentAt = float.NegativeInfinity;

        /// <summary>Off locally but the host still has us aboard (the message got lost): ask again.</summary>
        private void ResendExitIfIgnored()
        {
            PlayerHub local = PlayerHub.Local;
            if (local == null || local.Motor == null || local.Motor.Seat == this || !IsAboard(local)) return;
            if (Time.time - _exitSentAt < 1f) return;
            _exitSentAt = Time.time;
            ExitServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ExitServer(NetworkConnection caller = null)
        {
            PlayerHub driver = _driver.Value;
            if (driver != null && driver.Owner == caller)
            {
                ServerKickDriver();
                return;
            }
            for (int i = 0; i < BackSeatCount; i++)
                if (Rider(i) != null && Rider(i).Owner == caller) ServerKickRider(Rider(i));
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
            if (locked) ServerKickAll();
        }

        /// <summary>Host: drive to a point by itself (pirates arriving). Null stops.</summary>
        [Server]
        public void ServerAutopilot(Vector3? target, float speed = 8f)
        {
            if (target.HasValue) ServerKickAll(); // nobody rides along on autopilot
            _autopilotTarget = target;
            _autopilotSpeed = speed;
            if (target.HasValue && Owner.IsValid) RemoveOwnership();
        }

        /// <summary>Host: put it somewhere (story setup, teleports). Whoever sat on it gets off first.</summary>
        [Server]
        public void ServerPlace(Vector3 position, float yaw)
        {
            ServerKickAll();
            if (Owner.IsValid) RemoveOwnership();
            _rb.position = position;
            _rb.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.SetPositionAndRotation(position, _rb.rotation);
            if (!_rb.isKinematic) _rb.linearVelocity = _rb.angularVelocity = Vector3.zero;
        }

        private void OnDriverChanged(PlayerHub prev, PlayerHub next, bool asServer)
        {
            // Once per machine: a host in its server callback (FishNet hands a host's client-side callback
            // prev == next, so "the driver who just got off" was never seen there and the host sat frozen on the seat
            // until CheckSeat gave up), everybody else in the client one.
            if (asServer != IsServerStarted) return;
            // Seated bodies don't bump into their own vehicle. Getting off, they stay ghosts to it until they are
            // really clear of the hull, so a hull still drifting (or bobbing on a wave) can't trap, shove or launch
            // them while they swim away.
            if (prev != null && prev != next) StartCoroutine(RestoreCollisionWhenClear(prev));
            SetIgnore(next, true);
            _lastYaw = transform.eulerAngles.y;

            PlayerHub local = PlayerHub.Local;
            if (local != null && prev == local && local.Motor != null && local.Motor.Seat == this && !IsAboard(local))
                LocalGetOff(local, SeatTransform(0));
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

        /// <summary>
        /// Off beside the hull, on the side we're looking to: straight into the water at swimming depth (or onto the
        /// sand, the seabed or a deck), drifting on with it a little and a small push away so we don't start in its
        /// path. The view glides down from the seat instead of jumping.
        /// </summary>
        private void LocalGetOff(PlayerHub local, Transform seat)
        {
            Vector3 eyeBefore = local.Look != null && local.Look.Camera != null ? local.Look.Camera.transform.position : local.Head.position;
            Vector3 feet = ExitPosition(seat);
            bool inWater = WaterSurface.Exists && WaterSurface.HeightAt(feet) - feet.y > 0.6f;
            Vector3 away = Vector3.ProjectOnPlane(feet - transform.position, Vector3.up);
            Vector3 drift = _rb != null && !_rb.isKinematic ? Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up) * (inWater ? 0.15f : 0.3f) : Vector3.zero;
            drift = Vector3.ClampMagnitude(drift, 3f);
            Vector3 push = away.sqrMagnitude > 1e-4f ? away.normalized * (inWater ? 0.8f : 1.2f) : Vector3.zero;
            local.Motor.SetSeat(null, feet, drift + push);
            if (local.Look != null) local.Look.GlideFrom(eyeBefore);
            PlayerHud.ShowToast($"Off the {_displayName}.", 1.5f);
        }

        /// <summary>A back seat changed hands: the same as the driver's seat, without the driving.</summary>
        private void OnRiderChanged(PlayerHub prev, PlayerHub next, bool asServer, int seat)
        {
            if (asServer != IsServerStarted) return; // once per machine (see OnDriverChanged)
            if (prev != null && prev != next && !IsAboard(prev)) StartCoroutine(RestoreCollisionWhenClear(prev));
            SetIgnore(next, true);
            PlayerHub local = PlayerHub.Local;
            if (local != null && prev == local && local.Motor != null && local.Motor.Seat == this && !IsAboard(local))
                LocalGetOff(local, SeatTransform(seat));
            if (local != null && next == local)
            {
                PlayerHands hands = local.Hands;
                if (hands != null && hands.HeldItem != null && hands.HeldItem.Pocketable)
                {
                    int free = hands.FirstFreeSlot();
                    if (free >= 0) hands.SelectSlot(free);
                    else hands.Drop();
                }
                local.Motor.SetSeat(this, Vector3.zero);
                _lastYaw = transform.eulerAngles.y;
                PlayerHud.ShowToast($"Hold on tight! <b>[{GameInput.KeyLabel(GameInput.Interact)}]</b> to get off.", 4f);
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
            while (player != null && !IsAboard(player) && Time.time - started < 8f)
            {
                clearFor = TouchesHull(player) ? 0f : clearFor + 0.2f;
                if (clearFor >= 0.5f) break;
                yield return wait;
            }
            if (player != null && !IsAboard(player)) SetIgnore(player, false);
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
        public Vector3 SafeExitPosition() => ExitPosition(SeatTransform(0));

        private readonly Collider[] _exitOverlaps = new Collider[32];

        /// <summary>
        /// Where the driver gets off: beside the seat (in the water for a jet ski, on the deck for the boat), else the
        /// other side, behind, in front, then further out all round, whichever is free first, so nobody ends up
        /// inside the dock, a rock or a hull. Our own body, whatever we carry and this vehicle's riders don't count.
        /// </summary>
        private Vector3 ExitPosition(Transform seat)
        {
            Vector3 right = Vector3.ProjectOnPlane(seat.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(seat.forward, Vector3.up).normalized;
            Vector3 start = seat.position + Vector3.up * 0.2f;
            PlayerHub local = PlayerHub.Local;
            // The side we're looking to first.
            Transform eye = local != null && local.Look != null && local.Look.Camera != null ? local.Look.Camera.transform : null;
            Vector3 side = eye != null && Vector3.Dot(eye.forward, right) < 0f ? -right : right;

            var candidates = new List<Vector3>
            {
                start + side * 1.3f, start - side * 1.3f, start - forward * 2.2f, start + forward * 2.4f
            };
            foreach (float distance in new[] { 2f, 3f, 4.5f, 6f, 8f })
                for (int i = 0; i < 12; i++)
                    candidates.Add(start + Quaternion.Euler(0f, i * 30f, 0f) * side * distance);

            foreach (Vector3 spot in candidates)
            {
                Vector3 feet = Settle(spot, local);
                if (ExitFree(feet, local))
                    return feet;
            }
            // Nowhere free at all (packed in by the dock, rocks and people): straight up off the seat. We stay a
            // ghost to this hull until clear of it, so we drop through it into the water and swim out, never stuck.
            Debug.LogWarning($"[Vehicle] no free spot to get off the {_displayName}: dropping through it");
            return seat.position + Vector3.up * 0.3f;
        }

        /// <summary>
        /// The right height for a spot beside the seat: in deep water, low enough to be swimming straight away (not
        /// dropped from seat height into it); else standing on whatever is under it (sand, the seabed, a deck).
        /// </summary>
        private Vector3 Settle(Vector3 spot, PlayerHub local)
        {
            float ground = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(spot + Vector3.up * 1.2f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (local != null && hit.collider.transform.IsChildOf(local.transform)) continue;
                if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue; // a ball, a crate
                ground = Mathf.Max(ground, hit.point.y);
            }
            float water = WaterSurface.Exists ? WaterSurface.HeightAt(spot) : float.NegativeInfinity;
            if (water > ground + 0.3f) spot.y = Mathf.Max(ground + 0.02f, water - SwimDepth);
            else if (!float.IsNegativeInfinity(ground)) spot.y = ground + 0.02f;
            return spot;
        }

        private const float SwimDepth = 1.35f; // feet this far under the surface: swimming (the motor starts at 1.25)

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

        /// <summary>
        /// Where this player's hands go: the handlebars for the driver, a back seat's own grips (the banana's handles,
        /// the driver's waist on a jet ski) for a rider.
        /// </summary>
        public bool GetGrips(PlayerHub player, out HandGrip left, out HandGrip right)
        {
            int index = SeatIndexOf(player);
            if (index <= 0 && !_straddle) return GetHandlebars(out left, out right);
            left = right = default;
            Transform seat = SeatTransform(Mathf.Max(0, index));
            Transform gl = index <= 0 ? _gripLeft : seat.Find("GripLeft"), gr = index <= 0 ? _gripRight : seat.Find("GripRight");
            if (gl == null || gr == null) return false;
            // A tight fist round the handle (a loose one left the fingers splayed over it like a claw).
            left = new HandGrip(gl.position, gl.forward, -gl.up, HandPose.Fist);
            right = new HandGrip(gr.position, gr.forward, -gr.up, HandPose.Fist);
            return true;
        }

        /// <summary>World handlebar grips (fingers forward over the bar, palms down).</summary>
        public bool GetHandlebars(out HandGrip left, out HandGrip right)
        {
            left = right = default;
            if (_gripLeft == null || _gripRight == null) return false;
            HandPose pose = _restHands ? HandPose.Flat : HandPose.LooseFist;
            left = new HandGrip(_gripLeft.position, _gripLeft.forward, -_gripLeft.up, pose);
            right = new HandGrip(_gripRight.position, _gripRight.forward, -_gripRight.up, pose);
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
            float yaw = transform.eulerAngles.y;
            for (int index = 0; index <= BackSeatCount; index++)
            {
                PlayerHub rider = index == 0 ? _driver.Value : Rider(index - 1);
                if (rider == null) continue;
                // Already off on our own machine (ExitNow), the host not caught up yet: not back onto the seat.
                if (rider == PlayerHub.Local && rider.Motor != null && rider.Motor.Seat != this) continue;
                Vector3 feet = SeatTransform(index).position - Vector3.up * 0.5f;
                rider.transform.position = feet;
                if (rider.TryGetComponent(out Rigidbody body) && body.isKinematic) body.position = feet;
                if (turnView && rider == PlayerHub.Local && rider.Look != null)
                    rider.Look.AddYaw(Mathf.DeltaAngle(_lastYaw, yaw)); // the view turns with the vehicle
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
            if (driver != null && driver == PlayerHub.Local && driver.IsOwner && driver.Motor != null && driver.Motor.Seat == this)
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
            DevCommands.Register("getoff", "", "Get off the vehicle you're on, as if pressing Interact (automated tests).", _ =>
            {
                PlayerHub me = PlayerHub.Local;
                Vehicle v = me != null && me.Motor != null && me.Motor.Seat != null ? me.Motor.Seat : SeatOf(me);
                if (v == null) throw new System.InvalidOperationException("not on anything");
                v.ExitNow();
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
            DevCommands.Unregister("getoff", this);
            DevCommands.Unregister("vehicles", this);
        }

        private void Update()
        {
            PlayerHub driver = _driver.Value;
            // The driver left the game: free the seat.
            if (IsServerInitialized && !ReferenceEquals(driver, null) && (driver == null || !driver.IsSpawned))
                _driver.Value = null;
            for (int i = 0; IsServerInitialized && i < BackSeatCount; i++)
            {
                PlayerHub rider = Rider(i);
                if (!ReferenceEquals(rider, null) && (rider == null || !rider.IsSpawned)) RiderVar(i).Value = null;
            }
            // A rider: Interact always means "get off" too.
            PlayerHub me = PlayerHub.Local;
            if (me != null && me != driver && SeatIndexOf(me) > 0 && me.Motor != null && me.Motor.Seat == this && GameInput.GameplayActive
                && GameInput.Interact.WasPressedThisFrame() && Time.time > _nextEnterRequest)
            {
                _nextEnterRequest = Time.time + 0.5f;
                ExitNow();
            }
            // Our driver: Interact always means "get off" (whatever the crosshair is on).
            if (driver != null && driver == PlayerHub.Local && driver.Motor != null && driver.Motor.Seat == this && GameInput.GameplayActive
                && GameInput.Interact.WasPressedThisFrame() && Time.time > _nextEnterRequest) // not the same press that just got us on
            {
                _nextEnterRequest = Time.time + 0.5f;
                ExitNow();
            }
            ResendExitIfIgnored();
            GlueDriver(true);
        }
    }
}
