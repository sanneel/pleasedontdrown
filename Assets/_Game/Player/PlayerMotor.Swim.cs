using PleaseDontDrown.Core;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Swimming and diving. Deep enough water switches the motor into swim mode:
    /// at the surface you float head-up and swim flat; with your head under you swim where you look.
    /// Crouch dives, Jump rises (or hops out at the surface to climb onto docks).
    /// Air drains while your head is under; at zero you're forced up until you catch your breath.
    /// Sprint-swimming drains stamina.
    /// </summary>
    public partial class PlayerMotor
    {
        [Header("Swimming")]
        [SerializeField] private float _swimEnterDepth = 1.25f;
        [SerializeField] private float _swimExitDepth = 1.05f;
        [Tooltip("Feet depth when floating at the surface (eyes ~0.3 m above the water).")]
        [SerializeField] private float _floatFeetDepth = 1.32f;
        [SerializeField] private float _swimSpeed = 3f;
        [SerializeField] private float _swimSprintSpeed = 4.7f;
        [SerializeField] private float _swimAccel = 7f;
        [SerializeField] private float _surfaceHopSpeed = 4.8f;

        [Header("Breath & stamina")]
        [SerializeField] private float _airSeconds = 25f;
        [SerializeField] private float _airRefillPerSecond = 0.25f;
        [SerializeField] private float _staminaDrainPerSecond = 0.14f;
        [SerializeField] private float _staminaRefillPerSecond = 0.2f;

        private PlayerHands _hands;
        private float _hopUntil = float.NegativeInfinity;
        private float _scriptedDiveUntil = float.NegativeInfinity;
        private bool _gasping;

        public bool IsSwimming { get; private set; }
        public bool IsHeadUnderwater { get; private set; }
        /// <summary>How far the feet are below the water surface (negative on dry land).</summary>
        public float WaterDepthAtFeet { get; private set; }
        public float Air01 { get; private set; } = 1f;
        public float Stamina01 { get; private set; } = 1f;
        public bool IsOutOfBreath => _gasping;

        private float WadeFactor => Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(0.15f, _swimEnterDepth, WaterDepthAtFeet));

        private void RegisterSwimCommands()
        {
            DevCommands.Register("dive", "<seconds>", "Hold 'dive' on autopilot (automated tests).", args =>
                _scriptedDiveUntil = Time.time + DevCommands.ParseFloat(args, 0), cheat: true, owner: this);
            DevCommands.Register("climb", "", "Try to climb out onto the ledge in front (automated tests).", _ =>
                DevCommands.Print(IsSwimming && TryStartClimb() ? "climbing" : "no ledge in reach"), cheat: true, owner: this);
            DevCommands.Register("breath", "", "Show air and stamina.", _ =>
                DevCommands.Print($"air {Air01:P0}, stamina {Stamina01:P0}, swimming {IsSwimming}, head under {IsHeadUnderwater}, feet depth {WaterDepthAtFeet:F2} m"),
                owner: this);
        }

        private void UnregisterSwimCommands()
        {
            DevCommands.Unregister("dive", this);
            DevCommands.Unregister("climb", this);
            DevCommands.Unregister("breath", this);
        }

        private void UpdateWater()
        {
            if (!WaterSurface.Exists)
            {
                IsSwimming = IsHeadUnderwater = false;
                WaterDepthAtFeet = -10f;
                return;
            }
            Vector3 feet = transform.position;
            WaterDepthAtFeet = WaterSurface.HeightAt(feet) - feet.y;
            bool inHop = Time.time < _hopUntil;
            IsSwimming = !inHop && (IsSwimming ? WaterDepthAtFeet > _swimExitDepth : WaterDepthAtFeet > _swimEnterDepth);
            Vector3 head = _head != null ? _head.position : feet + Vector3.up * 1.65f;
            IsHeadUnderwater = WaterSurface.DepthOf(head) > 0.03f;
        }

        private void UpdateBreath(float dt)
        {
            if (IsHeadUnderwater)
            {
                Air01 = Mathf.Max(0f, Air01 - dt / _airSeconds);
                if (Air01 <= 0f && !_gasping)
                {
                    _gasping = true;
                    Debug.Log("[Player] out of air, surfacing");
                }
            }
            else
            {
                Air01 = Mathf.Min(1f, Air01 + _airRefillPerSecond * dt);
            }
            if (_gasping && Air01 >= 0.3f)
                _gasping = false;

            if (IsSwimming && IsSprinting)
                Stamina01 = Mathf.Max(0f, Stamina01 - _staminaDrainPerSecond * dt);
            else
                Stamina01 = Mathf.Min(1f, Stamina01 + _staminaRefillPerSecond * dt);
        }

        // ------------------------------------------------------------------ climbing out of the water

        [Header("Climb out")]
        [SerializeField] private float _climbMaxRise = 2.3f;
        [SerializeField] private float _climbDuration = 0.45f;

        private bool _climbing;
        private float _climbStart;
        private Vector3 _climbFrom;
        private Vector3 _climbTo;

        public bool IsClimbing => _climbing;

        /// <summary>Looks for a walkable top just in front of us, within reach, with room to stand.</summary>
        private bool TryStartClimb()
        {
            Vector3 feet = transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float radius = _controller.radius;
            Vector3 probe = feet + forward * (radius + 0.45f) + Vector3.up * (_climbMaxRise + 0.3f);

            if (!Physics.SphereCast(probe, 0.2f, Vector3.down, out RaycastHit hit, _climbMaxRise + 0.3f, ~0, QueryTriggerInteraction.Ignore))
                return false;
            float rise = hit.point.y - feet.y;
            if (rise < 0.4f || rise > _climbMaxRise || hit.normal.y < 0.7f)
                return false;

            Vector3 top = new Vector3(hit.point.x, hit.point.y + 0.03f, hit.point.z) + forward * 0.15f;
            Vector3 bottom = top + Vector3.up * (radius + 0.05f);
            Vector3 upper = top + Vector3.up * (_standHeight - radius);
            if (Physics.CheckCapsule(bottom, upper, radius * 0.9f, ~0, QueryTriggerInteraction.Ignore))
                return false; // no room to stand up there

            _climbing = true;
            _climbStart = Time.time;
            _climbFrom = feet;
            _climbTo = top;
            _controller.enabled = false;
            _velocity = _externalVelocity = Vector3.zero;
            return true;
        }

        /// <summary>Up first, then over the edge. Returns true while the climb is running.</summary>
        private bool UpdateClimb()
        {
            if (!_climbing) return false;
            float t = Mathf.Clamp01((Time.time - _climbStart) / _climbDuration);
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.6f));
            float over = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.45f) / 0.55f));
            var p = new Vector3(Mathf.Lerp(_climbFrom.x, _climbTo.x, over), Mathf.Lerp(_climbFrom.y, _climbTo.y + 0.05f, up), Mathf.Lerp(_climbFrom.z, _climbTo.z, over));
            transform.position = p;
            if (t >= 1f)
            {
                _climbing = false;
                _controller.enabled = !Noclip;
                IsSwimming = false;
            }
            return true;
        }

        private float CarryFactor()
        {
            var item = _hands != null ? _hands.HeldItem : null;
            return item == null ? 1f : 1f / (1f + item.Mass * 0.06f);
        }

        private void SwimMove(Vector2 input, bool scripted, float dt)
        {
            IsGrounded = false;
            IsCrouching = false;
            if (Mathf.Abs(_height - _standHeight) > 0.001f)
            {
                _height = _standHeight;
                ApplyHeight();
            }

            bool dive = (GameInput.Crouch.IsPressed() || Time.time < _scriptedDiveUntil) && !_gasping;
            bool rise = GameInput.Jump.IsPressed();
            bool sprintHeld = GameInput.Sprint.IsPressed() || (scripted && _scriptedSprint);
            IsSprinting = sprintHeld && input.sqrMagnitude > 0.01f && Stamina01 > 0.02f;

            // At the surface, Jump climbs out onto a ledge in front of you (dock, rock, boat), or does a little hop.
            if (!IsHeadUnderwater && GameInput.Jump.WasPressedThisFrame() && Time.time > _hopUntil + 0.3f)
            {
                if (TryStartClimb())
                    return;
                _hopUntil = Time.time + 0.45f;
                _velocity.y = _surfaceHopSpeed;
            }

            Transform view = _head != null ? _head : transform;
            Vector3 wish = IsHeadUnderwater
                ? view.forward * input.y + view.right * input.x        // underwater: swim where you look
                : transform.forward * input.y + transform.right * input.x; // surface: stay level
            if (dive) wish += Vector3.down;
            if (rise && IsHeadUnderwater) wish += Vector3.up;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            float speed = (IsSprinting ? _swimSprintSpeed : _swimSpeed) * SpeedMultiplier * CarryFactor();
            Vector3 target = wish * speed;

            bool verticalIntent = dive || (rise && IsHeadUnderwater);
            if (_gasping && IsHeadUnderwater)
            {
                target.y = 2.2f; // out of air: kick for the surface
            }
            else if (!verticalIntent)
            {
                // Natural buoyancy: settle at floating depth, riding the waves.
                float targetY = WaterSurface.HeightAt(transform.position) - _floatFeetDepth;
                target.y = Mathf.Clamp((targetY - transform.position.y) * 2.2f, -1.5f, 1.8f);
            }

            // Hitting the water fast (jumping off the dock) brakes harder than normal swimming.
            float accel = _velocity.sqrMagnitude > speed * speed * 2.5f ? _swimAccel * 2f : _swimAccel;
            if (Time.time < _hopUntil)
            {
                var flat = Vector3.MoveTowards(new Vector3(_velocity.x, 0f, _velocity.z), new Vector3(target.x, 0f, target.z), accel * dt);
                _velocity = new Vector3(flat.x, _velocity.y - _gravity * dt, flat.z);
            }
            else
            {
                _velocity = Vector3.MoveTowards(_velocity, target, accel * dt);
            }

            _externalVelocity = Vector3.MoveTowards(_externalVelocity, Vector3.zero, 8f * dt);
            _controller.Move((_velocity + _externalVelocity) * dt);
            HorizontalSpeed = new Vector2(_velocity.x, _velocity.z).magnitude;
        }
    }
}
