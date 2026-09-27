using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// Distributed physics authority for a networked rigidbody.
    ///
    /// Exactly one machine simulates the body: the FishNet <b>owner</b> if there is one, otherwise the host.
    /// The simulator runs real physics and streams its state every network tick while moving; everyone else keeps
    /// the body kinematic and smoothly follows (with a little extrapolation). Whoever touches an item (picks it up,
    /// throws it, bumps into it) becomes its owner, so their interaction feels instant. When the body comes to rest,
    /// it sends one final reliable state, hands authority back to the host and goes silent.
    /// Bodies that move on their own (a tourist treading water) set <see cref="KeepAwake"/>: they stream while
    /// awake and go back to the host once nobody has touched them for a few seconds.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ItemSync : NetworkBehaviour
    {
        [Header("Rest detection")]
        [SerializeField] private float _restSpeed = 0.06f;
        [SerializeField] private float _restAngularSpeed = 0.12f;
        [SerializeField] private float _restDelay = 0.5f;

        [Header("Following")]
        [SerializeField] private float _followSharpness = 22f;
        [SerializeField] private float _maxExtrapolation = 0.2f;
        [SerializeField] private float _snapDistance = 5f;

        private const float FallLimit = -30f;
        private static readonly Vector3 RescuePoint = new Vector3(0f, 2f, 16f);

        private Rigidbody _rb;
        private Buoyancy _buoyancy;
        private bool _held;
        private bool _heldLocally;
        private float _lastContactTime = float.NegativeInfinity;
        private bool _sending;
        // Follower-side "bobbing at rest": the simulator stopped sending, we bob it on the shared wave clock.
        private bool _floatIdle;
        private float _floatOffset;
        private float _stillTime;
        private float _lastAuthorityRequest = float.NegativeInfinity;
        private float _lastInteractionTime = float.NegativeInfinity;
        private const float SelfMovingHandBack = 3f;

        // Latest state received from the simulator (used while following).
        private Vector3 _netPosition;
        private Quaternion _netRotation = Quaternion.identity;
        private Vector3 _netVelocity;
        private Vector3 _netAngularVelocity;
        private float _netTime;

        public Rigidbody Body => _rb;
        public Buoyancy Buoyancy => _buoyancy;
        /// <summary>Set while the body moves by itself (a struggling swimmer): never counts as "at rest".</summary>
        public bool KeepAwake { get; set; }
        /// <summary>Touching something right now (held items use this to slide along walls instead of fighting them).</summary>
        public bool IsTouching => Time.time - _lastContactTime < 0.1f;
        /// <summary>True on the one machine that runs physics for this body.</summary>
        public bool IsSimulator => Owner.IsValid ? IsOwner : IsServerInitialized;
        public string AuthorityLabel => Owner.IsValid ? $"client {Owner.ClientId}" : "host";

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _buoyancy = GetComponent<Buoyancy>();
            _rb.isKinematic = true; // until the network decides who simulates
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative; // valid for kinematic and dynamic
            // Authority can switch while a body overlaps a player or another item; don't let depenetration launch it.
            _rb.maxDepenetrationVelocity = 3f;
            CaptureFollowTarget();
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            TimeManager.OnTick += OnTick;
            Refresh();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (TimeManager != null)
                TimeManager.OnTick -= OnTick;
        }

        public override void OnOwnershipServer(NetworkConnection prevOwner)
        {
            base.OnOwnershipServer(prevOwner);
            _lastInteractionTime = Time.time;
            Refresh();
            if (prevOwner.IsValid || Owner.IsValid) // skip the initial "nobody -> nobody" at spawn
                Debug.Log($"[Item] {name} simulator -> {AuthorityLabel}");
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            _lastInteractionTime = Time.time;
            Refresh();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Late joiner: a floating item at rest sends nothing, so start bobbing it ourselves.
            if (!IsSimulator && _buoyancy != null && _buoyancy.IsNearSurface(_rb.position))
                EnterFloatIdle(_rb.position.y - WaterSurface.HeightAt(_rb.position));
        }

        /// <summary>
        /// Held items: on the holder's machine the body stays dynamic (gravity off) and the hands steer it with
        /// velocities, so it bumps into walls and feels heavy. On every other machine it's kinematic and glued
        /// to the holder's head. Nothing is streamed while held.
        /// </summary>
        public void SetHeld(bool held, bool heldLocally = false)
        {
            if (_held == held && _heldLocally == heldLocally) return;
            bool released = _held && !held;
            _held = held;
            _lastInteractionTime = Time.time;
            _heldLocally = held && heldLocally;
            if (_buoyancy != null) _buoyancy.Suspended = held;
            Refresh();
            if (released && IsSimulator)
            {
                // Just thrown or dropped by us: start streaming right away.
                _sending = true;
                _stillTime = 0f;
            }
        }

        private void OnCollisionEnter(Collision c) => OnContact(c);
        private void OnCollisionStay(Collision c) => OnContact(c);

        private void OnContact(Collision c)
        {
            _lastContactTime = Time.time;
            if (c.rigidbody != null) _lastInteractionTime = Time.time; // pushed by a player or another body
        }

        /// <summary>Ask the host to let us simulate this body (e.g. we walked into it). Rate limited.</summary>
        public void RequestAuthority()
        {
            if (!IsSpawned || IsSimulator || _held || Time.time - _lastAuthorityRequest < 0.25f)
                return;
            _lastAuthorityRequest = Time.time;
            RequestAuthorityServer();
        }

        private void Refresh()
        {
            _rb.useGravity = !_held;
            if (_held)
            {
                _sending = false;
                _floatIdle = false;
                _rb.isKinematic = !_heldLocally;
                _rb.interpolation = _heldLocally ? RigidbodyInterpolation.Interpolate : RigidbodyInterpolation.None;
                return;
            }

            bool simulate = IsSimulator;
            bool wasKinematic = _rb.isKinematic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.isKinematic = !simulate;

            if (simulate && wasKinematic)
            {
                // We just took over: announce our state once, then rest detection decides.
                _stillTime = 0f;
                _sending = true;
            }
            else if (!simulate)
            {
                _sending = false;
                _floatIdle = false;
                CaptureFollowTarget();
            }
        }

        private void EnterFloatIdle(float offsetFromSurface)
        {
            _floatIdle = true;
            _floatOffset = offsetFromSurface;
            _netVelocity = _netAngularVelocity = Vector3.zero;
        }

        private void CaptureFollowTarget()
        {
            _netPosition = _rb.position;
            _netRotation = _rb.rotation;
            _netVelocity = _netAngularVelocity = Vector3.zero;
            _netTime = Time.time;
        }

        // ------------------------------------------------------------------ simulator side

        private void OnTick()
        {
            if (IsServerInitialized && Owner.IsValid && !Owner.IsActive)
            {
                RemoveOwnership(); // owner disconnected: the host takes over
                return;
            }
            if (!IsSimulator || _held || _rb.isKinematic)
                return;

            if (_rb.position.y < FallLimit)
            {
                // Fell out of the world: put it back on the beach instead of losing it forever.
                Debug.LogWarning($"[Item] {name} fell out of the world at {_rb.position}; returning it to the beach");
                _rb.linearVelocity = _rb.angularVelocity = Vector3.zero;
                _rb.position = RescuePoint + Random.insideUnitSphere * 0.5f;
                transform.position = _rb.position;
            }

            if (KeepAwake)
            {
                _stillTime = 0f;
                _sending = true;
                SendState(Channel.Unreliable);
                // Whoever bumped or threw a struggling swimmer hands it back to the host once it's left alone.
                if (Owner.IsValid && Time.time - _lastInteractionTime > SelfMovingHandBack)
                {
                    _lastInteractionTime = Time.time; // don't repeat while the hand-back is on its way
                    if (IsServerInitialized) RemoveOwnership();
                    else ReturnAuthorityServer(_rb.position, _rb.rotation);
                }
                return;
            }

            // Floating things bob forever; for them "at rest" means no longer drifting or spinning around.
            // (Only at the surface: something lying on the seabed rests like it would on land.)
            bool floating = _buoyancy != null && _buoyancy.InWater && _buoyancy.SubmergedFraction < 0.98f;
            Vector3 v = _rb.linearVelocity;
            Vector3 w = _rb.angularVelocity;
            bool moving = floating
                ? new Vector2(v.x, v.z).sqrMagnitude > _restSpeed * _restSpeed * 4f || Mathf.Abs(w.y) > _restAngularSpeed * 3f
                : v.sqrMagnitude > _restSpeed * _restSpeed || w.sqrMagnitude > _restAngularSpeed * _restAngularSpeed;
            if (moving)
            {
                _stillTime = 0f;
                _sending = true;
            }
            else
            {
                _stillTime += (float)TimeManager.TickDelta;
            }

            if (!_sending)
                return;

            bool resting = !moving && _stillTime >= _restDelay;
            if (resting && floating)
                SendFloatRest();
            else
                SendState(resting ? Channel.Reliable : Channel.Unreliable);
            if (!resting)
                return;

            _sending = false;
            if (!Owner.IsValid)
                return;
            if (IsServerInitialized)
                RemoveOwnership();                        // host was the owner: same machine keeps simulating
            else
                ReturnAuthorityServer(_rb.position, _rb.rotation);
        }

        private void SendState(Channel channel)
        {
            if (IsServerInitialized)
                StateObservers(_rb.position, _rb.rotation, _rb.linearVelocity, _rb.angularVelocity, channel);
            else
                StateServer(_rb.position, _rb.rotation, _rb.linearVelocity, _rb.angularVelocity, channel);
        }

        [ServerRpc(RequireOwnership = false)]
        private void StateServer(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity,
            Channel channel = Channel.Unreliable, NetworkConnection caller = null)
        {
            if (caller != Owner) return; // late packet from a previous owner
            ApplyState(position, rotation, velocity, angularVelocity);
            StateObservers(position, rotation, velocity, angularVelocity, channel);
        }

        [ObserversRpc(ExcludeOwner = true, ExcludeServer = true)]
        private void StateObservers(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity,
            Channel channel = Channel.Unreliable) => ApplyState(position, rotation, velocity, angularVelocity);

        private void SendFloatRest()
        {
            float offset = _rb.position.y - WaterSurface.HeightAt(_rb.position);
            if (IsServerInitialized)
                FloatRestObservers(_rb.position, _rb.rotation, offset);
            else
                FloatRestServer(_rb.position, _rb.rotation, offset);
        }

        [ServerRpc(RequireOwnership = false)]
        private void FloatRestServer(Vector3 position, Quaternion rotation, float offset, NetworkConnection caller = null)
        {
            if (caller != Owner) return;
            ApplyFloatRest(position, rotation, offset);
            FloatRestObservers(position, rotation, offset);
        }

        [ObserversRpc(ExcludeOwner = true, ExcludeServer = true)]
        private void FloatRestObservers(Vector3 position, Quaternion rotation, float offset) => ApplyFloatRest(position, rotation, offset);

        private void ApplyFloatRest(Vector3 position, Quaternion rotation, float offset)
        {
            if (IsSimulator) return;
            _netPosition = position;
            _netRotation = rotation;
            _netTime = Time.time;
            EnterFloatIdle(offset);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReturnAuthorityServer(Vector3 position, Quaternion rotation, NetworkConnection caller = null)
        {
            if (caller != Owner || _held) return;
            // Start host simulation exactly where the client left it, at rest.
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            RemoveOwnership();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestAuthorityServer(NetworkConnection caller = null)
        {
            if (_held || caller == null || !caller.IsActive) return;
            if (Owner.IsValid) return; // someone else is already simulating it
            GiveOwnership(caller);
        }

        // ------------------------------------------------------------------ follower side

        /// <summary>Server-side: a client released a held item here with this velocity (its owner simulates the flight).</summary>
        public void ServerSeedFollow(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            if (IsSimulator) return;
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            ApplyState(position, rotation, velocity, Vector3.zero);
        }

        private void ApplyState(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            if (IsSimulator) return;
            _floatIdle = false;
            _netPosition = position;
            _netRotation = rotation;
            _netVelocity = velocity;
            _netAngularVelocity = angularVelocity;
            _netTime = Time.time;

            if (!_held && (position - _rb.position).sqrMagnitude > _snapDistance * _snapDistance)
            {
                _rb.position = position;
                _rb.rotation = rotation;
                transform.SetPositionAndRotation(position, rotation);
            }
        }

        private void FixedUpdate()
        {
            if (_held || !_rb.isKinematic || IsSimulator)
                return;

            Vector3 target;
            Quaternion targetRotation;
            if (_floatIdle && WaterSurface.Exists)
            {
                // Bob on the shared wave clock: same waves as the simulator, zero network traffic.
                target = new Vector3(_netPosition.x, WaterSurface.HeightAt(_netPosition) + _floatOffset, _netPosition.z);
                Vector3 normal = Vector3.Lerp(Vector3.up, WaterSurface.NormalAt(_netPosition), 0.6f);
                targetRotation = Quaternion.FromToRotation(Vector3.up, normal) * _netRotation;
            }
            else
            {
                float ahead = Mathf.Min(Time.time - _netTime, _maxExtrapolation);
                target = _netPosition + _netVelocity * ahead;
                targetRotation = _netRotation;
                float spin = _netAngularVelocity.magnitude;
                if (spin > 0.01f)
                    targetRotation = Quaternion.AngleAxis(spin * Mathf.Rad2Deg * ahead, _netAngularVelocity / spin) * _netRotation;
            }

            // MovePosition on a kinematic body also pushes dynamic bodies it runs into (e.g. a thrown crate on the host).
            float k = 1f - Mathf.Exp(-_followSharpness * Time.fixedDeltaTime);
            _rb.MovePosition(Vector3.Lerp(_rb.position, target, k));
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRotation, k));
        }
    }
}
