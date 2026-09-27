using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
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
        private bool _held;
        private bool _sending;
        private float _stillTime;
        private float _lastAuthorityRequest = float.NegativeInfinity;

        // Latest state received from the simulator (used while following).
        private Vector3 _netPosition;
        private Quaternion _netRotation = Quaternion.identity;
        private Vector3 _netVelocity;
        private Vector3 _netAngularVelocity;
        private float _netTime;

        public Rigidbody Body => _rb;
        /// <summary>True on the one machine that runs physics for this body.</summary>
        public bool IsSimulator => Owner.IsValid ? IsOwner : IsServerInitialized;
        public string AuthorityLabel => Owner.IsValid ? $"client {Owner.ClientId}" : "host";

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
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
            Refresh();
            if (prevOwner.IsValid || Owner.IsValid) // skip the initial "nobody -> nobody" at spawn
                Debug.Log($"[Item] {name} simulator -> {AuthorityLabel}");
        }

        public override void OnOwnershipClient(NetworkConnection prevOwner)
        {
            base.OnOwnershipClient(prevOwner);
            Refresh();
        }

        /// <summary>Held items are positioned by the holder's hands on every machine; physics is off.</summary>
        public void SetHeld(bool held)
        {
            if (_held == held) return;
            _held = held;
            Refresh();
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
            bool simulate = IsSimulator && !_held;
            bool wasKinematic = _rb.isKinematic;
            _rb.interpolation = _held ? RigidbodyInterpolation.None : RigidbodyInterpolation.Interpolate;
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
                CaptureFollowTarget();
            }
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

            bool moving = _rb.linearVelocity.sqrMagnitude > _restSpeed * _restSpeed
                          || _rb.angularVelocity.sqrMagnitude > _restAngularSpeed * _restAngularSpeed;
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

            float ahead = Mathf.Min(Time.time - _netTime, _maxExtrapolation);
            Vector3 target = _netPosition + _netVelocity * ahead;
            Quaternion targetRotation = _netRotation;
            float spin = _netAngularVelocity.magnitude;
            if (spin > 0.01f)
                targetRotation = Quaternion.AngleAxis(spin * Mathf.Rad2Deg * ahead, _netAngularVelocity / spin) * _netRotation;

            // MovePosition on a kinematic body also pushes dynamic bodies it runs into (e.g. a thrown crate on the host).
            float k = 1f - Mathf.Exp(-_followSharpness * Time.fixedDeltaTime);
            _rb.MovePosition(Vector3.Lerp(_rb.position, target, k));
            _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRotation, k));
        }
    }
}
