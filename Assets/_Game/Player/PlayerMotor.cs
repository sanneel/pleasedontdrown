using System;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Physics-body first-person movement (owner only; FishNet's NetworkTransform syncs the position).
    /// The player is a real rigidbody capsule, so it shoves crates, kicks balls, can be knocked back and
    /// ridden on by joints later. Velocity is shaped each physics step: eased walk/sprint speed, ground
    /// acceleration and braking, air control that keeps momentum, extra gravity for a snappy jump,
    /// coyote time + jump buffering, sliding off steep slopes and a small step-up assist.
    /// The body never rotates; facing comes from <see cref="PlayerLook.YawRotation"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public partial class PlayerMotor : MonoBehaviour
    {
        [Header("Speed (m/s)")]
        [SerializeField] private float _walkSpeed = 4.6f;
        [SerializeField] private float _sprintSpeed = 7.4f;
        [SerializeField] private float _crouchSpeed = 2.3f;
        [Tooltip("Seconds to ease between walk / sprint / crouch speed.")]
        [SerializeField] private float _speedEaseTime = 0.18f;

        [Header("Acceleration (m/s²)")]
        [SerializeField] private float _groundAccel = 55f;
        [SerializeField] private float _groundBrake = 40f;
        [SerializeField] private float _airAccel = 11f;

        [Header("Jump & gravity")]
        [SerializeField] private float _jumpHeight = 1.15f;
        [Tooltip("Added on top of Physics.gravity while airborne (snappier arcs).")]
        [SerializeField] private float _extraGravity = 14f;
        [SerializeField] private float _coyoteTime = 0.12f;
        [SerializeField] private float _jumpBuffer = 0.14f;

        [Header("Ground")]
        [SerializeField] private float _maxWalkAngle = 48f;
        [SerializeField] private float _groundProbe = 0.14f;
        [SerializeField] private float _slideAccel = 16f;
        [SerializeField] private float _maxStepHeight = 0.36f;

        [Header("Crouch")]
        [SerializeField] private float _standHeight = 1.8f;
        [SerializeField] private float _crouchHeight = 1.15f;
        [SerializeField] private float _crouchSharpness = 12f;
        [Tooltip("Eye distance below the top of the capsule.")]
        [SerializeField] private float _eyeBelowTop = 0.15f;
        [SerializeField] private Transform _head;

        private Rigidbody _rb;
        private CapsuleCollider _capsule;
        private PlayerLook _look;
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        // Input (read in Update, used in FixedUpdate).
        private Vector2 _moveInput;
        private bool _sprintHeld;
        private bool _crouchHeld;
        private float _lastJumpPressedTime = float.NegativeInfinity;
        private Vector2 _scriptedInput;
        private bool _scriptedSprint;
        private float _scriptedUntil = float.NegativeInfinity;

        private float _maxSpeed;
        private float _maxSpeedVelocity;
        private float _lastGroundedTime = float.NegativeInfinity;
        private float _lastJumpTime = float.NegativeInfinity;
        private float _controlLossUntil = float.NegativeInfinity;
        private float _height;
        private Vector3 _groundNormal = Vector3.up;
        private Vector3 _spawnPoint;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public float HorizontalSpeed { get; private set; }
        public float StrafeInput => _moveInput.x;
        public Collider GroundCollider { get; private set; }
        public Vector3 Velocity => Noclip || _rb == null ? Vector3.zero : _rb.linearVelocity;
        public bool Noclip { get; set; }
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>Set by PlayerVitals: starving lifeguards are slower.</summary>
        public float HungerSpeedScale { get; set; } = 1f;
        /// <summary>Set by PlayerVitals: hungry lifeguards get their breath back slowly.</summary>
        public float StaminaRefillScale { get; set; } = 1f;
        private Quaternion Facing => _look != null ? _look.YawRotation : Quaternion.identity;
        private float TotalGravity => -Physics.gravity.y + _extraGravity;

        /// <summary>Fired when touching ground after falling; argument is the downward speed at impact.</summary>
        public event Action<float> Landed;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _look = GetComponent<PlayerLook>();
            _hands = GetComponent<PlayerHands>();
            _height = _standHeight;
            _maxSpeed = _walkSpeed;
            ApplyHeight();
        }

        private void OnEnable()
        {
            _spawnPoint = transform.position;
            _rb.isKinematic = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            DevCommands.Register("noclip", "", "Fly through everything.", _ =>
            {
                Noclip = !Noclip;
                _rb.isKinematic = Noclip;
                _rb.linearVelocity = Vector3.zero;
                DevCommands.Print($"noclip {(Noclip ? "ON" : "OFF")}");
            }, cheat: true, owner: this);
            DevCommands.Register("speed", "<multiplier>", "Movement speed multiplier (1 = normal).", args =>
            {
                SpeedMultiplier = Mathf.Clamp(DevCommands.ParseFloat(args, 0), 0.1f, 10f);
                DevCommands.Print($"speed x{SpeedMultiplier}");
            }, cheat: true, owner: this);
            DevCommands.Register("tp", "<x> <y> <z> | spawn", "Teleport yourself.", args =>
            {
                Teleport(args.Length > 0 && args[0] == "spawn"
                    ? _spawnPoint
                    : new Vector3(DevCommands.ParseFloat(args, 0), DevCommands.ParseFloat(args, 1), DevCommands.ParseFloat(args, 2)));
            }, cheat: true, owner: this);
            DevCommands.Register("walk", "<seconds> [sprint]", "Walk forward on autopilot (automated tests).", args =>
            {
                _scriptedUntil = Time.time + DevCommands.ParseFloat(args, 0);
                _scriptedInput = Vector2.up;
                _scriptedSprint = args.Length > 1 && args[1] == "sprint";
            }, cheat: true, owner: this);
            DevCommands.Register("contacts", "", "Toggle logging of what your body touches.", _ =>
            {
                _logContacts = !_logContacts;
                DevCommands.Print($"contact logging {(_logContacts ? "ON" : "OFF")}");
            }, owner: this);
            DevCommands.Register("knock", "<x> <y> <z>", "Knock yourself back (test impulses).", args =>
                AddImpulse(new Vector3(DevCommands.ParseFloat(args, 0), DevCommands.ParseFloat(args, 1), DevCommands.ParseFloat(args, 2))),
                cheat: true, owner: this);
            RegisterSwimCommands();
        }

        private void OnDisable()
        {
            DevCommands.Unregister("noclip", this);
            DevCommands.Unregister("speed", this);
            DevCommands.Unregister("tp", this);
            DevCommands.Unregister("walk", this);
            DevCommands.Unregister("knock", this);
            DevCommands.Unregister("contacts", this);
            UnregisterSwimCommands();
        }

        public void Teleport(Vector3 position)
        {
            _rb.position = position;
            transform.position = position;
            if (!_rb.isKinematic)
                _rb.linearVelocity = Vector3.zero;
            _climbing = false;
        }

        /// <summary>The vehicle we're sitting on (the vehicle glues us to its seat), or null.</summary>
        public Vehicles.Vehicle Seat { get; private set; }

        /// <summary>Sit on a vehicle (body goes kinematic, eyes drop to sitting height) or get off at <paramref name="exitPosition"/>.</summary>
        public void SetSeat(Vehicles.Vehicle vehicle, Vector3 exitPosition)
        {
            if (Seat == vehicle) return;
            Seat = vehicle;
            _climbing = false;
            if (vehicle != null)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.interpolation = RigidbodyInterpolation.None;
                IsGrounded = true;
                IsCrouching = false;
                _height = _standHeight;
                ApplyHeight();
                if (_head != null) _head.localPosition = new Vector3(0f, 1.22f, 0f); // sitting eye height
                return;
            }
            _rb.isKinematic = Noclip;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            ApplyHeight();
            Teleport(exitPosition);
        }

        /// <summary>Knockback / explosion / boat hit: an instant velocity change plus a moment of reduced control.</summary>
        public void AddImpulse(Vector3 velocityChange)
        {
            if (_rb.isKinematic) return;
            _rb.linearVelocity += velocityChange;
            _controlLossUntil = Time.time + Mathf.Clamp(velocityChange.magnitude * 0.05f, 0.15f, 0.8f);
            IsGrounded = false;
            if (velocityChange.y > 0.5f)
                _lastJumpTime = Time.time; // airborne like a jump, so ground snapping doesn't eat the launch
        }

        // ------------------------------------------------------------------ input (per frame)

        private void Update()
        {
            bool scripted = Time.time < _scriptedUntil;
            _moveInput = scripted ? _scriptedInput
                : GameInput.GameplayActive ? Vector2.ClampMagnitude(GameInput.Move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            _sprintHeld = GameInput.Sprint.IsPressed() || (scripted && _scriptedSprint);
            _crouchHeld = GameInput.Crouch.IsPressed();
            if (GameInput.Jump.WasPressedThisFrame())
                _lastJumpPressedTime = Time.time;

            UpdateBreath(Time.deltaTime);

            if (Seat != null) _moveInput = Vector2.zero; // W/S/A/D drive the vehicle instead
            if (!Noclip && Seat == null && transform.position.y < -25f)
                Teleport(_spawnPoint);
        }

        // ------------------------------------------------------------------ physics step

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            if (Seat != null)
                return; // the vehicle carries us
            if (Noclip)
            {
                FlyNoclip(dt);
                return;
            }
            if (UpdateClimb())
                return;

            UpdateWater();
            if (IsSwimming)
            {
                SwimMove(dt);
                return;
            }
            _rb.useGravity = true;

            bool wasGrounded = IsGrounded;
            float fallSpeed = -_rb.linearVelocity.y;
            GroundCheck();
            UpdateCrouch(dt);

            // Eased target speed.
            IsSprinting = _sprintHeld && _moveInput.y > 0.1f && !IsCrouching;
            float targetSpeed = IsCrouching ? _crouchSpeed : IsSprinting ? _sprintSpeed : _walkSpeed;
            _maxSpeed = Mathf.SmoothDamp(_maxSpeed, targetSpeed, ref _maxSpeedVelocity, _speedEaseTime, Mathf.Infinity, dt);
            float speed = _maxSpeed * SpeedMultiplier * HungerSpeedScale * WadeFactor * CarryFactor(0.015f);
            Vector3 wish = Facing * new Vector3(_moveInput.x, 0f, _moveInput.y) * speed;

            Vector3 v = _rb.linearVelocity;
            var horizontal = new Vector3(v.x, 0f, v.z);
            float control = Time.time < _controlLossUntil ? 0.1f : 1f;
            bool hasInput = wish.sqrMagnitude > 0.01f;

            if (IsGrounded)
            {
                float rate = (hasInput ? _groundAccel : _groundBrake) * control;
                horizontal = Vector3.MoveTowards(horizontal, wish, rate * dt);
            }
            else if (hasInput)
            {
                // Air control steers toward the wish but never brakes you below your current momentum.
                Vector3 steered = Vector3.MoveTowards(horizontal, wish, _airAccel * control * dt);
                horizontal = steered.sqrMagnitude < horizontal.sqrMagnitude && Vector3.Dot(horizontal, wish) > 0f
                    ? steered.normalized * horizontal.magnitude
                    : steered;
            }

            // Waist-deep or more: Jump climbs a ledge in front (dock, rock) if there is one.
            if (WaterDepthAtFeet > 0.5f && Time.time - _lastJumpPressedTime <= _jumpBuffer && TryStartClimb())
            {
                _lastJumpPressedTime = float.NegativeInfinity;
                return;
            }

            float vy = v.y;
            bool jumped = false;
            bool canJump = Time.time - _lastGroundedTime <= _coyoteTime && !IsCrouching && control >= 1f;
            if (canJump && Time.time - _lastJumpPressedTime <= _jumpBuffer)
            {
                vy = Mathf.Sqrt(2f * TotalGravity * _jumpHeight);
                _lastJumpPressedTime = _lastGroundedTime = float.NegativeInfinity;
                _lastJumpTime = Time.time;
                IsGrounded = false;
                jumped = true;
            }

            Vector3 velocity;
            if (IsGrounded && !jumped)
            {
                // Follow the ground: project onto the slope and press down lightly so we don't skip off bumps.
                velocity = Vector3.ProjectOnPlane(horizontal, _groundNormal) - _groundNormal * 0.5f;
            }
            else
            {
                if (!jumped)
                    vy -= _extraGravity * dt;
                velocity = new Vector3(horizontal.x, vy, horizontal.z);
                if (_onSteepSlope)
                    velocity += Vector3.ProjectOnPlane(Vector3.down, _groundNormal).normalized * (_slideAccel * dt);
            }

            _rb.linearVelocity = velocity;
            HorizontalSpeed = horizontal.magnitude;

            if (IsGrounded && hasInput)
                TryStepUp(wish.normalized);

            if (!wasGrounded && IsGrounded && fallSpeed > 3f)
                Landed?.Invoke(fallSpeed);
        }

        // ------------------------------------------------------------------ ground

        private bool _onSteepSlope;

        private void GroundCheck()
        {
            float radius = _capsule.radius * 0.95f;
            Vector3 origin = _rb.position + Vector3.up * (radius + 0.05f);
            bool hit = CastIgnoringSelf(origin, radius, Vector3.down, 0.05f + _groundProbe, out RaycastHit ground);
            bool justJumped = Time.time - _lastJumpTime < 0.2f && _rb.linearVelocity.y > 0.5f;

            float minNormalY = Mathf.Cos(_maxWalkAngle * Mathf.Deg2Rad);
            _onSteepSlope = hit && ground.normal.y < minNormalY;
            IsGrounded = hit && !justJumped && !_onSteepSlope;
            _groundNormal = hit ? ground.normal : Vector3.up;
            GroundCollider = hit ? ground.collider : null;
            if (IsGrounded)
                _lastGroundedTime = Time.time;
        }

        /// <summary>Walking into a low ledge (dock edge, step, crate lip): pop up onto it if there's room.</summary>
        private void TryStepUp(Vector3 direction)
        {
            float radius = _capsule.radius;
            Vector3 feet = _rb.position;
            if (!RaycastIgnoringSelf(feet + Vector3.up * 0.06f, direction, radius + 0.2f, out RaycastHit low) || low.normal.y > 0.5f)
                return; // nothing blocking at ankle height (or it's already a walkable slope)

            Vector3 above = feet + direction * (radius + 0.12f) + Vector3.up * (_maxStepHeight + 0.05f);
            if (!RaycastIgnoringSelf(above, Vector3.down, _maxStepHeight + 0.05f, out RaycastHit top) || top.normal.y < 0.7f)
                return;
            float rise = top.point.y - feet.y;
            if (rise <= 0.02f || rise > _maxStepHeight)
                return;

            Vector3 raised = feet + Vector3.up * (rise + 0.02f);
            if (Physics.CheckCapsule(raised + Vector3.up * (radius + 0.02f), raised + Vector3.up * (_height - radius), radius * 0.9f,
                    ~0, QueryTriggerInteraction.Ignore) && !OnlySelfOverlaps(raised))
                return;
            _rb.position = raised;
        }

        private bool OnlySelfOverlaps(Vector3 feet)
        {
            float radius = _capsule.radius * 0.9f;
            Collider[] overlaps = Physics.OverlapCapsule(feet + Vector3.up * (radius + 0.02f), feet + Vector3.up * (_height - radius), radius, ~0, QueryTriggerInteraction.Ignore);
            foreach (Collider c in overlaps)
                if (!IsSelfOrHeld(c)) return false;
            return true;
        }

        private bool IsSelfOrHeld(Collider c)
        {
            if (c == _capsule) return true;
            Item held = _hands != null ? _hands.HeldItem : null;
            return held != null && held.OwnsCollider(c); // includes a carried tourist's dangling legs
        }

        private bool CastIgnoringSelf(Vector3 origin, float radius, Vector3 direction, float distance, out RaycastHit best)
        {
            int count = Physics.SphereCastNonAlloc(origin, radius, direction, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
            return PickClosest(count, out best);
        }

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
            return PickClosest(count, out best);
        }

        private bool PickClosest(int count, out RaycastHit best)
        {
            best = default;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (IsSelfOrHeld(h.collider) || h.distance <= 0f && h.point == Vector3.zero) continue;
                if (h.distance < bestDistance)
                {
                    bestDistance = h.distance;
                    best = h;
                }
            }
            return bestDistance < float.MaxValue;
        }

        // ------------------------------------------------------------------ crouch

        private void UpdateCrouch(float dt)
        {
            bool wantCrouch = _crouchHeld;
            if (!wantCrouch && IsCrouching && !HasHeadroom(_standHeight))
                wantCrouch = true; // stay down under low ceilings
            IsCrouching = wantCrouch;

            float target = IsCrouching ? _crouchHeight : _standHeight;
            if (Mathf.Abs(_height - target) > 0.001f)
            {
                _height = Mathf.Lerp(_height, target, 1f - Mathf.Exp(-_crouchSharpness * dt));
                ApplyHeight();
            }
        }

        private bool HasHeadroom(float height)
        {
            float radius = _capsule.radius * 0.95f;
            Vector3 bottom = _rb.position + Vector3.up * (radius + 0.05f);
            float distance = height - radius * 2f - 0.05f;
            return distance <= 0f || !CastIgnoringSelf(bottom, radius, Vector3.up, distance, out _);
        }

        private void ApplyHeight()
        {
            _capsule.height = _height;
            _capsule.center = Vector3.up * (_height * 0.5f);
            if (_head != null)
                _head.localPosition = new Vector3(0f, _height - _eyeBelowTop, 0f);
        }

        // ------------------------------------------------------------------ pushing items

        private static bool _logContacts;

        private void OnCollisionEnter(Collision collision)
        {
            if (_logContacts)
                Debug.Log($"[Player] touched {collision.collider.name} (body: {(collision.rigidbody != null ? collision.rigidbody.name + (collision.rigidbody.isKinematic ? ", kinematic" : "") : "static")})");
            ClaimIfItem(collision);
        }

        // Contacts with a kinematic (not-ours) item often last a single step, so claim on Enter as well as Stay.
        private void OnCollisionStay(Collision collision) => ClaimIfItem(collision);

        /// <summary>Bumping an item someone else simulates: ask for it, so the next steps shove it for real.</summary>
        private static void ClaimIfItem(Collision collision)
        {
            Item item = Item.FromCollider(collision.collider);
            if (item != null && !item.Sync.IsSimulator && collision.rigidbody == item.Sync.Body)
                item.Sync.RequestAuthority();
        }

        // ------------------------------------------------------------------ noclip

        private void FlyNoclip(float dt)
        {
            Transform view = _head != null ? _head : transform;
            float up = (GameInput.Jump.IsPressed() ? 1f : 0f) - (GameInput.Crouch.IsPressed() ? 1f : 0f);
            float speed = (GameInput.Sprint.IsPressed() ? 25f : 10f) * SpeedMultiplier;
            _rb.MovePosition(_rb.position + (view.forward * _moveInput.y + view.right * _moveInput.x + Vector3.up * up) * (speed * dt));
            IsGrounded = false;
            HorizontalSpeed = 0f;
        }
    }
}
