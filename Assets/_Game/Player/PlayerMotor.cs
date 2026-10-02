using System;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Physics-body first-person movement (owner only; FishNet's NetworkTransform syncs the position).
    /// The player is a real rigidbody capsule, so it shoves crates, kicks balls and can be knocked back.
    ///
    /// The movement model is How to Fish's (its tuning measured from the game; our own code): every 1/100 s the
    /// horizontal velocity keeps 90% of itself and gains 10% of the wished speed (the same on the ground and in
    /// the air), capped at the current speed; walk 5, sprint 7.5 m/s with a 0.1 s eased change between them;
    /// gravity plus 20 m/s² extra all the time, a 9 m/s jump with 0.2 s coyote time; slopes over 45° push you
    /// off; crouching shortens the capsule to 85% (and tucks the legs up while airborne). The constants are
    /// per-100 Hz-step in the original and converted here to our physics rate.
    /// The body never rotates; facing comes from <see cref="PlayerLook.YawRotation"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    public partial class PlayerMotor : MonoBehaviour
    {
        [Header("Speed (m/s)")]
        [SerializeField] private float _walkSpeed = 5f;
        [SerializeField] private float _sprintSpeed = 7.5f;
        [Tooltip("Crouching walk speed, relative to walking.")]
        [SerializeField] private float _crouchSpeedScale = 0.65f;
        [Tooltip("Speed change per second between walk / sprint / crouch (eased).")]
        [SerializeField] private float _speedChangeRate = 10f;

        [Header("Acceleration (per 1/100 s step, as in the original)")]
        [Tooltip("Share of the wished speed added each step.")]
        [SerializeField] private float _accelPerStep = 0.1f;
        [Tooltip("Share of the velocity kept each step.")]
        [SerializeField] private float _keepPerStep = 0.9f;

        [Header("Jump & gravity")]
        [SerializeField] private float _jumpSpeed = 9f;
        [Tooltip("Pulled down this much on top of Physics.gravity, all the time (m/s²).")]
        [SerializeField] private float _extraGravity = 20f;
        [SerializeField] private float _coyoteTime = 0.2f;
        [Tooltip("A press this early (before landing) still jumps: one physics step, so no press is lost between frames.")]
        [SerializeField] private float _jumpBuffer = 0.05f;
        /// <summary>Swimming keeps its own, longer press window (unchanged by the walking rework).</summary>
        private const float SwimJumpBuffer = 0.14f;
        [SerializeField] private float _jumpRepeatDelay = 0.2f;

        [Header("Ground")]
        [SerializeField] private float _maxWalkAngle = 45f;
        [SerializeField] private float _groundProbe = 0.2f;
        [Tooltip("Sideways push off slopes steeper than the walk angle (m/s).")]
        [SerializeField] private float _slipPush = 2f;
        [SerializeField] private float _maxStepHeight = 0.36f;

        [Header("Crouch")]
        [SerializeField] private float _standHeight = 1.7f;
        [Tooltip("Crouched height, relative to standing.")]
        [SerializeField] private float _crouchHeightScale = 0.85f;
        [Tooltip("Crouch blend per second (eased in).")]
        [SerializeField] private float _crouchRate = 7.5f;
        [Tooltip("Eye distance below the top of the capsule.")]
        [SerializeField] private float _eyeBelowTop = 0.1f;
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

        private float _maxSpeed;                 // current speed cap (eases between walk / sprint / crouch)
        private float _speedFrom, _speedTo, _speedBlend = 1f;
        private Vector3 _moveVelocity;           // the horizontal velocity the movement model keeps
        private float _crouchBlend;              // 0 standing .. 1 crouched
        private bool _hasJumped;
        private float _slipSpeed;                // how much of the speed is sliding down a steep slope (the camera doesn't bob)
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
        public Vector2 MoveInput => _moveInput;
        /// <summary>Horizontal speed as a share of the current top speed (0..1), 0 while sliding off a slope: drives the head bob.</summary>
        public float SpeedFraction { get; private set; }
        /// <summary>Set by the gun in our hands: no sprinting while aiming down the sights.</summary>
        public bool AimBlocksSprint { get; set; }
        public Collider GroundCollider { get; private set; }
        public Vector3 Velocity => Noclip || _rb == null ? Vector3.zero : _rb.linearVelocity;
        public bool Noclip { get; set; }
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>Set by PlayerVitals: starving lifeguards are slower.</summary>
        public float HungerSpeedScale { get; set; } = 1f;
        /// <summary>Set by PlayerVitals: hungry lifeguards get their breath back slowly.</summary>
        public float StaminaRefillScale { get; set; } = 1f;
        private Quaternion Facing => _look != null ? _look.YawRotation : Quaternion.identity;
        private float CrouchHeight => _standHeight * _crouchHeightScale;

        /// <summary>Fired when touching ground after falling; argument is the downward speed at impact.</summary>
        public event Action<float> Landed;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _look = GetComponent<PlayerLook>();
            _hands = GetComponent<PlayerHands>();
            _height = _standHeight;
            _maxSpeed = _speedFrom = _speedTo = _walkSpeed;
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
            DevCommands.Register("jump", "", "Press jump on autopilot (automated tests).", _ => _lastJumpPressedTime = Time.time, cheat: true, owner: this);
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
            DevCommands.Unregister("jump", this);
            DevCommands.Unregister("knock", this);
            DevCommands.Unregister("contacts", this);
            UnregisterSwimCommands();
        }

        public void Teleport(Vector3 position)
        {
            // A climb cut short (teleported mid-climb) would leave the body kinematic for good.
            if (_climbing && Seat == null) _rb.isKinematic = Noclip;
            _rb.position = position;
            transform.position = position;
            if (!_rb.isKinematic)
                _rb.linearVelocity = Vector3.zero;
            _moveVelocity = Vector3.zero;
            _climbing = false;
        }

        /// <summary>The vehicle we're sitting on (the vehicle glues us to its seat), or null.</summary>
        public Vehicles.Vehicle Seat { get; private set; }
        private float _seatLostSince = float.PositiveInfinity;

        /// <summary>Sit on a vehicle (body goes kinematic, eyes drop to sitting height) or get off at <paramref name="exitPosition"/>.</summary>
        public void SetSeat(Vehicles.Vehicle vehicle, Vector3 exitPosition, Vector3 exitVelocity = default)
        {
            if (Seat == vehicle) return;
            Seat = vehicle;
            _seatLostSince = float.PositiveInfinity;
            _climbing = false;
            // Whatever the walk/swim was doing stops here: no stale speed (head bob and footsteps while sitting),
            // no half-done jump, crouch or hop carried over to the other side.
            _moveVelocity = Vector3.zero;
            SpeedFraction = HorizontalSpeed = 0f;
            IsSprinting = IsCrouching = false;
            _crouchBlend = 0f;
            _hasJumped = false;
            _lastJumpPressedTime = float.NegativeInfinity;
            _hopUntil = float.NegativeInfinity;
            _controlLossUntil = float.NegativeInfinity;
            _height = _standHeight;
            if (vehicle != null)
            {
                if (!_rb.isKinematic) _rb.linearVelocity = _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _rb.useGravity = false;
                _rb.interpolation = RigidbodyInterpolation.None;
                IsGrounded = true;
                IsSwimming = false;
                _seatedAt = Time.time;
                ApplyHeight();
                if (_head != null) _head.localPosition = new Vector3(0f, 1.22f, 0f); // sitting eye height
                return;
            }
            _rb.isKinematic = Noclip;
            _rb.useGravity = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
            IsGrounded = false;
            ApplyHeight();
            Teleport(exitPosition);
            PlayerLook.ResetBodyRotation(transform);
            UpdateWater(); // in the water or on a deck from the first step, not a frame of falling
            if (!_rb.isKinematic && exitVelocity != Vector3.zero)
            {
                _rb.linearVelocity = exitVelocity;
                _moveVelocity = new Vector3(exitVelocity.x, 0f, exitVelocity.z);
            }
            Debug.Log($"[Player] got off at {exitPosition:F2} (swimming {IsSwimming}, depth {WaterDepthAtFeet:F2})");
        }

        /// <summary>
        /// Every step while seated: the seat must still exist and still have us as its driver. If the vehicle was
        /// despawned, disabled, or its driver changed without telling us (a lost message), we get off by ourselves
        /// after half a second instead of staying frozen on nothing.
        /// </summary>
        private void CheckSeat()
        {
            Vehicles.Vehicle seat = Seat;
            PlayerHub me = _hub != null ? _hub : (_hub = GetComponent<PlayerHub>());
            bool valid = seat != null && seat.isActiveAndEnabled && seat.IsSpawned && (me == null || seat.Driver == me || seat.Driver == null && Time.time - _seatedAt < 1f);
            if (valid)
            {
                _seatLostSince = float.PositiveInfinity;
                return;
            }
            if (float.IsPositiveInfinity(_seatLostSince)) _seatLostSince = Time.time;
            if (Time.time - _seatLostSince < 0.5f) return;
            Vector3 spot = seat != null ? seat.SafeExitPosition() : transform.position + Vector3.up * 1.5f;
            Debug.LogWarning("[Player] seat lost (vehicle gone or no longer ours): getting off by ourselves");
            SetSeat(null, spot);
        }

        private PlayerHub _hub;
        private float _seatedAt;

        /// <summary>
        /// Last line of defence against "I can't move": out of a seat, not climbing, not in noclip, the body must be
        /// a dynamic, upright rigidbody with a sane capsule. Anything else is repaired (and logged).
        /// </summary>
        private void EnsureMobile()
        {
            if (Seat != null || _climbing || Noclip) return;
            if (_rb.isKinematic)
            {
                Debug.LogWarning("[Player] body was left kinematic outside a seat/climb: fixed");
                _rb.isKinematic = false;
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            if ((_rb.constraints & RigidbodyConstraints.FreezePosition) != 0)
                _rb.constraints = RigidbodyConstraints.FreezeRotation;
            if (!_capsule.enabled) _capsule.enabled = true;
            if (_height < CrouchHeight - 0.01f || _height > _standHeight + 0.01f)
            {
                _height = _standHeight;
                ApplyHeight();
            }
            if (float.IsNaN(_rb.position.x) || float.IsNaN(_moveVelocity.x))
            {
                _moveVelocity = Vector3.zero;
                Teleport(_spawnPoint);
            }
        }

        /// <summary>
        /// A small shove that the movement soaks up (How to Fish's knockback, e.g. a shotgun's kick): added to the
        /// moving velocity, so it fades out in the normal way.
        /// </summary>
        public void Knockback(Vector3 velocityChange)
        {
            if (_rb.isKinematic) return;
            _moveVelocity += new Vector3(velocityChange.x, 0f, velocityChange.z);
            _rb.linearVelocity += velocityChange;
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
            {
                CheckSeat();
                if (Seat != null) return; // the vehicle carries us
            }
            EnsureMobile();
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
                Vector3 swum = _rb.linearVelocity;
                _moveVelocity = new Vector3(swum.x, 0f, swum.z); // walking out of the water carries on from here
                return;
            }
            _rb.useGravity = true;

            bool wasGrounded = IsGrounded;
            float fallSpeed = -_rb.linearVelocity.y;
            GroundCheck();
            UpdateCrouch(dt);

            // Sprinting: holding sprint and moving (any direction), on the ground (or already sprinting when leaving
            // it), never while aiming down the sights.
            bool moving = _moveInput.sqrMagnitude > 0.0001f;
            IsSprinting = _sprintHeld && moving && (IsGrounded || IsSprinting) && !AimBlocksSprint;
            UpdateSpeedCap(dt);
            float speed = _maxSpeed * SpeedMultiplier * HungerSpeedScale * WadeFactor * CarryFactor(0.015f);

            // Waist-deep or more: Jump climbs a ledge in front (dock, rock) if there is one.
            if (WaterDepthAtFeet > 0.5f && Time.time - _lastJumpPressedTime <= _jumpBuffer && TryStartClimb())
            {
                _lastJumpPressedTime = float.NegativeInfinity;
                return;
            }

            Vector3 v = _rb.linearVelocity;
            bool knocked = Time.time < _controlLossUntil;
            Vector3 input = _moveInput.sqrMagnitude > 1f ? (Vector3)_moveInput.normalized : (Vector3)_moveInput;
            Vector3 wish = Facing * new Vector3(input.x, 0f, input.y);
            if (knocked)
            {
                // Knocked back: the shove carries us (movement takes over again when it's spent).
                _moveVelocity = new Vector3(v.x, 0f, v.z);
            }
            else
            {
                // The movement model, per 1/100 s step: keep 90%, add 10% of the wished speed, cap at the top speed.
                float steps = dt / 0.01f;
                _moveVelocity.y = 0f;
                _moveVelocity *= Mathf.Pow(_keepPerStep, steps);
                _moveVelocity += wish * (_accelPerStep * speed * steps);
                _moveVelocity = Vector3.ClampMagnitude(_moveVelocity, speed);
            }
            Vector3 horizontal = _moveVelocity;
            _slipSpeed = 0f;
            if (_onSteepSlope && !knocked)
            {
                // Too steep to stand on: pushed off it, away from the slope.
                Vector3 away = new Vector3(_groundNormal.x, 0f, _groundNormal.z);
                if (away.sqrMagnitude > 1e-4f)
                {
                    horizontal += away.normalized * _slipPush;
                    _slipSpeed = _slipPush;
                }
            }

            // Jump: straight up at 9 m/s, from the ground or just after running off it (not from a too-steep slope).
            float vy = v.y;
            bool canJump = !_hasJumped && Time.time - _lastGroundedTime <= _coyoteTime && !_onSteepSlope && !knocked;
            if (canJump && Time.time - _lastJumpPressedTime <= _jumpBuffer)
            {
                vy = _jumpSpeed;
                _hasJumped = true;
                _lastJumpPressedTime = float.NegativeInfinity;
                _lastJumpTime = Time.time;
                IsGrounded = false;
            }
            else
            {
                vy -= _extraGravity * dt; // extra gravity, on the ground too (keeps us pressed to it)
            }

            _rb.linearVelocity = new Vector3(horizontal.x, vy, horizontal.z);
            HorizontalSpeed = new Vector2(horizontal.x, horizontal.z).magnitude;
            SpeedFraction = _slipSpeed > 0f ? 0f : new Vector2(_moveVelocity.x, _moveVelocity.z).magnitude / Mathf.Max(0.01f, _maxSpeed);

            if (IsGrounded && moving && !knocked)
                TryStepUp(wish.normalized);

            if (!wasGrounded && IsGrounded && fallSpeed > 3f)
                Landed?.Invoke(fallSpeed);
        }

        /// <summary>The top speed eases (over 1/10 s) between walking, sprinting and crouch-walking.</summary>
        private void UpdateSpeedCap(float dt)
        {
            float target = IsSprinting ? _sprintSpeed : _walkSpeed;
            if (IsCrouching && IsGrounded) target *= _crouchSpeedScale;
            if (!Mathf.Approximately(_speedTo, target))
            {
                _speedFrom = _maxSpeed;
                _speedTo = target;
                _speedBlend = 0f;
            }
            _speedBlend = Mathf.MoveTowards(_speedBlend, 1f, _speedChangeRate * dt);
            _maxSpeed = Mathf.Lerp(_speedFrom, _speedTo, Mathf.SmoothStep(0f, 1f, _speedBlend));
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
            {
                _lastGroundedTime = Time.time;
                if (Time.time >= _lastJumpTime + _jumpRepeatDelay) _hasJumped = false;
            }
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

        /// <summary>
        /// Crouching (held), or airborne for more than 1/10 s (the legs tuck up, so ledges are easier to land on).
        /// On the ground the head comes down; in the air the feet come up and the head stays where it is.
        /// </summary>
        private void UpdateCrouch(float dt)
        {
            bool airborne = !IsGrounded && Time.time > _lastGroundedTime + 0.1f;
            bool wantCrouch = _crouchHeld || airborne;
            if (!wantCrouch && IsCrouching && !HasHeadroom(_standHeight))
                wantCrouch = true; // stay down under low ceilings
            IsCrouching = wantCrouch;

            _crouchBlend = Mathf.MoveTowards(_crouchBlend, IsCrouching ? 1f : 0f, _crouchRate * dt);
            float eased = _crouchBlend * _crouchBlend; // eases in (slow start, quick finish)
            float target = Mathf.Lerp(_standHeight, CrouchHeight, eased);
            if (Mathf.Abs(_height - target) > 0.0005f)
            {
                float change = _height - target;
                _height = target;
                if (!IsGrounded)
                {
                    // Keep the top (and the eyes) still: move the feet.
                    Vector3 p = _rb.position + Vector3.up * change;
                    _rb.position = p;
                    transform.position = p;
                }
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
