using System;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// First-person movement on a CharacterController (owner only; FishNet's NetworkTransform syncs the result).
    /// Walk / sprint / crouch / jump with coyote time and jump buffering, plus an impulse API for knockbacks.
    /// The root sits at the player's feet.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speed (m/s)")]
        [SerializeField] private float _walkSpeed = 4.6f;
        [SerializeField] private float _sprintSpeed = 7.4f;
        [SerializeField] private float _crouchSpeed = 2.3f;

        [Header("Acceleration (m/s²)")]
        [SerializeField] private float _groundAccel = 60f;
        [SerializeField] private float _airAccel = 12f;

        [Header("Jump")]
        [SerializeField] private float _jumpHeight = 1.15f;
        [SerializeField] private float _gravity = 24f;
        [SerializeField] private float _coyoteTime = 0.12f;
        [SerializeField] private float _jumpBuffer = 0.12f;

        [Header("Crouch")]
        [SerializeField] private float _standHeight = 1.8f;
        [SerializeField] private float _crouchHeight = 1.15f;
        [SerializeField] private float _crouchSharpness = 12f;
        [Tooltip("Eye distance below the top of the capsule.")]
        [SerializeField] private float _eyeBelowTop = 0.15f;
        [SerializeField] private Transform _head;

        private CharacterController _controller;
        private Vector2 _scriptedInput;
        private bool _scriptedSprint;
        private float _scriptedUntil = float.NegativeInfinity;
        private Vector3 _velocity;
        private Vector3 _externalVelocity;
        private float _lastGroundedTime = float.NegativeInfinity;
        private float _lastJumpPressedTime = float.NegativeInfinity;
        private float _height;
        private Vector3 _spawnPoint;

        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public float HorizontalSpeed { get; private set; }
        /// <summary>Current world velocity (thrown items inherit it).</summary>
        public Vector3 Velocity => Noclip ? Vector3.zero : _velocity + _externalVelocity;
        public bool Noclip { get; set; }

        [Header("Pushing items")]
        [SerializeField] private float _pushStrength = 1.3f;
        public float SpeedMultiplier { get; set; } = 1f;

        /// <summary>Fired when touching ground after falling; argument is the downward speed at impact.</summary>
        public event Action<float> Landed;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _height = _standHeight;
            ApplyHeight();
        }

        private void OnEnable()
        {
            _spawnPoint = transform.position;
            DevCommands.Register("noclip", "", "Fly through everything.", _ =>
            {
                Noclip = !Noclip;
                _controller.enabled = !Noclip;
                _velocity = Vector3.zero;
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
        }

        private void OnDisable()
        {
            DevCommands.Unregister("noclip", this);
            DevCommands.Unregister("speed", this);
            DevCommands.Unregister("tp", this);
            DevCommands.Unregister("walk", this);
        }

        public void Teleport(Vector3 position)
        {
            bool was = _controller.enabled;
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = was && !Noclip;
            _velocity = _externalVelocity = Vector3.zero;
        }

        /// <summary>Knockback / explosion / boat hit. Upward part launches, horizontal part slides and decays.</summary>
        public void AddImpulse(Vector3 velocityChange)
        {
            if (velocityChange.y > 0f)
                _velocity.y = Mathf.Max(_velocity.y, velocityChange.y);
            _externalVelocity += new Vector3(velocityChange.x, 0f, velocityChange.z);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector2 input = GameInput.GameplayActive ? Vector2.ClampMagnitude(GameInput.Move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            bool scripted = Time.time < _scriptedUntil;
            if (scripted) input = _scriptedInput;

            if (Noclip)
            {
                FlyNoclip(input, dt);
                return;
            }

            bool wasGrounded = IsGrounded;
            IsGrounded = _controller.isGrounded;
            if (IsGrounded) _lastGroundedTime = Time.time;
            if (GameInput.Jump.WasPressedThisFrame()) _lastJumpPressedTime = Time.time;

            UpdateCrouch(dt);

            IsSprinting = (GameInput.Sprint.IsPressed() || (scripted && _scriptedSprint)) && input.y > 0.1f && !IsCrouching;
            float speed = (IsCrouching ? _crouchSpeed : IsSprinting ? _sprintSpeed : _walkSpeed) * SpeedMultiplier;
            Vector3 wish = (transform.right * input.x + transform.forward * input.y) * speed;

            var horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, wish, (IsGrounded ? _groundAccel : _airAccel) * dt);

            float vertical = _velocity.y;
            if (IsGrounded && vertical < 0f)
                vertical = -2f; // keeps the controller pressed onto slopes and steps

            bool canJump = Time.time - _lastGroundedTime <= _coyoteTime && !IsCrouching;
            if (canJump && Time.time - _lastJumpPressedTime <= _jumpBuffer)
            {
                vertical = Mathf.Sqrt(2f * _gravity * _jumpHeight);
                _lastJumpPressedTime = _lastGroundedTime = float.NegativeInfinity;
            }
            else
            {
                vertical -= _gravity * dt;
            }

            _externalVelocity = Vector3.MoveTowards(_externalVelocity, Vector3.zero, (IsGrounded ? 18f : 3f) * dt);

            float fallSpeed = -vertical;
            CollisionFlags flags = _controller.Move((horizontal + Vector3.up * vertical + _externalVelocity) * dt);
            if ((flags & CollisionFlags.Above) != 0 && vertical > 0f)
                vertical = 0f;

            _velocity = horizontal + Vector3.up * vertical;
            HorizontalSpeed = horizontal.magnitude;

            if (!wasGrounded && _controller.isGrounded && fallSpeed > 3f)
                Landed?.Invoke(fallSpeed);

            if (transform.position.y < -25f)
                Teleport(_spawnPoint);
        }

        /// <summary>
        /// Walking into a loose item pushes it (kicking the beach ball). If another machine simulates it,
        /// ask for authority first; the push lands a round trip later.
        /// </summary>
        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            Rigidbody body = hit.collider.attachedRigidbody;
            if (body == null || hit.moveDirection.y < -0.5f) // standing on top of it
                return;
            var sync = body.GetComponent<Items.ItemSync>();
            if (sync == null)
                return;
            if (!sync.IsSimulator)
            {
                sync.RequestAuthority();
                return;
            }
            if (body.isKinematic)
                return;

            var dir = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
            if (dir.sqrMagnitude < 1e-4f)
                return;
            dir.Normalize();

            // Set the velocity instead of adding force: this callback runs every frame, but forces only apply on the
            // next physics step, so AddForce would stack up many times between steps (a beach ball at 27 m/s...).
            float desired = Mathf.Max(HorizontalSpeed, 1.5f) * _pushStrength / Mathf.Max(1f, body.mass / 6f);
            Vector3 v = body.linearVelocity;
            float along = Vector3.Dot(v, dir);
            if (along >= desired)
                return;
            v += dir * (desired - along);
            if (body.mass < 1f)
                v.y = Mathf.Max(v.y, desired * 0.3f); // light things pop up a little when kicked
            body.linearVelocity = v;
        }

        private void UpdateCrouch(float dt)
        {
            bool wantCrouch = GameInput.Crouch.IsPressed();
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
            float radius = _controller.radius * 0.95f;
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.05f);
            float distance = height - radius * 2f - 0.05f;
            return !Physics.SphereCast(bottom, radius, Vector3.up, out _, distance, ~0, QueryTriggerInteraction.Ignore)
                   || distance <= 0f;
        }

        private void ApplyHeight()
        {
            _controller.height = _height;
            _controller.center = Vector3.up * (_height * 0.5f);
            if (_head != null)
                _head.localPosition = new Vector3(0f, _height - _eyeBelowTop, 0f);
        }

        private void FlyNoclip(Vector2 input, float dt)
        {
            Transform view = _head != null ? _head : transform;
            float up = (GameInput.Jump.IsPressed() ? 1f : 0f) - (GameInput.Crouch.IsPressed() ? 1f : 0f);
            float speed = (GameInput.Sprint.IsPressed() ? 25f : 10f) * SpeedMultiplier;
            transform.position += (view.forward * input.y + view.right * input.x + Vector3.up * up) * (speed * dt);
            IsGrounded = false;
            HorizontalSpeed = 0f;
        }
    }
}
