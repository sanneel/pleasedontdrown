using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Carrying and throwing.
    ///
    /// Local player: the held item stays a live rigidbody and is steered to a hold point in front of the camera
    /// with velocities every physics step (glides in after pickup, lags with weight, slides along walls instead of
    /// clipping through them, gets unstuck if it falls far behind). Tap Drop to let go, hold Drop (or Primary) to
    /// charge a throw; the item pulls back while charging. A throw before the host confirmed the pickup is queued.
    ///
    /// Remote players: the item is glued to their head each frame.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private Transform _head;

        [Header("Holding (local physics)")]
        [SerializeField] private float _holdSpeed = 22f;
        [SerializeField] private float _holdRotateSpeed = 14f;
        [SerializeField] private float _pickUpGlideTime = 0.2f;
        [SerializeField] private float _maxSpeedWhileTouching = 3f;
        [SerializeField] private float _unstuckDistance = 1.6f;

        [Header("Throwing")]
        [SerializeField] private float _minThrowSpeed = 3f;
        [SerializeField] private float _maxThrowSpeed = 15f;
        [SerializeField] private float _chargeTime = 0.75f;
        [Tooltip("Releasing Drop faster than this just drops the item.")]
        [SerializeField] private float _tapTime = 0.18f;
        [SerializeField] private float _chargePullback = 0.28f;

        private enum ChargeSource { None, Primary, Drop }

        private ChargeSource _chargeSource;
        private float _chargeStart;
        private float _holdPercent;
        private float _heldSince;
        private float _stuckTime;
        private float? _queuedThrow;   // charge (0..1), or -1 for a plain drop
        private float _queuedAt;

        public Item HeldItem { get; private set; }
        public bool IsCharging => _chargeSource != ChargeSource.None && Charge01 > 0f;
        public float Charge01 { get; private set; }

        private bool IsLocal => _hub.IsOwner;

        // ------------------------------------------------------------------ called by Item

        internal void OnItemGained(Item item)
        {
            if (HeldItem == item) return;
            HeldItem = item;
            ResetHoldState();
        }

        internal void OnItemLost(Item item)
        {
            if (HeldItem != item) return;
            HeldItem = null;
            ResetHoldState();
        }

        private void ResetHoldState()
        {
            _chargeSource = ChargeSource.None;
            Charge01 = 0f;
            _holdPercent = 0f;
            _heldSince = Time.time;
            _stuckTime = 0f;
            _queuedThrow = null;
        }

        // ------------------------------------------------------------------ actions (local player)

        public void TryPickUp(Item item)
        {
            if (!IsLocal || item == null || item == HeldItem || item.IsHeld)
                return;
            if (HeldItem != null)
                Drop();
            HeldItem = item;
            ResetHoldState();
            item.RequestPickUp(_hub);
        }

        /// <summary>Throw with a charge from 0 (lob) to 1 (full power). Queued until the host confirms the pickup.</summary>
        public void Throw(float charge) => RequestRelease(Mathf.Clamp01(charge));

        public void Drop() => RequestRelease(-1f);

        private void RequestRelease(float charge)
        {
            Item item = HeldItem;
            if (item == null) return;
            if (!item.IsConfirmedHolder(_hub))
            {
                _queuedThrow = charge; // the host hasn't answered yet; do it the moment it does
                _queuedAt = Time.time;
                return;
            }

            Vector3 playerVelocity = _hub.Motor.Velocity;
            Vector3 velocity;
            Vector3 spin;
            if (charge < 0f)
            {
                velocity = playerVelocity + AimTransform.forward * 0.8f;
                spin = Vector3.zero;
            }
            else
            {
                // Light things fly far, heavy things plop. Clamp so a beach ball doesn't become a missile.
                float massScale = Mathf.Clamp(Mathf.Sqrt(3f / Mathf.Max(0.1f, item.Mass)), 0.35f, 1.3f);
                float speed = Mathf.Lerp(_minThrowSpeed, _maxThrowSpeed, charge) * massScale * item.ThrowStrength;
                Vector3 direction = (AimTransform.forward + Vector3.up * 0.08f).normalized;
                velocity = direction * speed + playerVelocity;
                spin = Random.insideUnitSphere * (2f + 6f * charge);
            }

            HeldItem = null;
            ResetHoldState();
            item.Release(_hub, velocity, spin);
        }

        private Transform AimTransform => _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _head;

        // ------------------------------------------------------------------ commands

        private void OnEnable()
        {
            if (!IsLocal) return;
            DevCommands.Register("grab", "[name]", "Pick up the item you look at, or the nearest one (optionally matching a name, e.g. 'grab tourist').",
                args => GrabCommand(args.Length > 0 ? args[0] : null), owner: this);
            DevCommands.Register("throw", "[charge 0..1]", "Throw the held item.", args => Throw(args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 1f), owner: this);
            DevCommands.Register("drop", "", "Drop the held item.", _ => Drop(), owner: this);
        }

        private void OnDisable()
        {
            DevCommands.Unregister("grab", this);
            DevCommands.Unregister("throw", this);
            DevCommands.Unregister("drop", this);
        }

        /// <summary>The hub calls this once it knows we're the local player (commands register only then).</summary>
        internal void RefreshLocal()
        {
            OnDisable();
            OnEnable();
        }

        // ------------------------------------------------------------------ input

        private void Update()
        {
            if (!IsLocal || HeldItem == null)
                return;

            if (_queuedThrow.HasValue)
            {
                if (HeldItem.IsConfirmedHolder(_hub)) RequestRelease(_queuedThrow.Value);
                else if (Time.time - _queuedAt > 1f) _queuedThrow = null; // host never confirmed; forget it
                return;
            }

            if (!GameInput.GameplayActive)
            {
                _chargeSource = ChargeSource.None;
                Charge01 = 0f;
                return;
            }

            if (_chargeSource == ChargeSource.None)
            {
                if (GameInput.Primary.WasPressedThisFrame()) BeginCharge(ChargeSource.Primary);
                else if (GameInput.Drop.WasPressedThisFrame()) BeginCharge(ChargeSource.Drop);
            }

            if (_chargeSource == ChargeSource.None)
                return;

            float held = Time.time - _chargeStart;
            // Drop only starts charging after a tap's worth of time, so a quick tap is a plain drop.
            float chargeTime = _chargeSource == ChargeSource.Drop ? held - _tapTime : held;
            Charge01 = Mathf.Clamp01(chargeTime / _chargeTime);

            InputActionReleased(out bool released);
            if (!released)
                return;
            ChargeSource source = _chargeSource;
            _chargeSource = ChargeSource.None;
            if (source == ChargeSource.Drop && held < _tapTime) Drop();
            else Throw(Charge01);
        }

        private void BeginCharge(ChargeSource source)
        {
            _chargeSource = source;
            _chargeStart = Time.time;
            Charge01 = 0f;
        }

        private void InputActionReleased(out bool released) =>
            released = _chargeSource == ChargeSource.Primary ? GameInput.Primary.WasReleasedThisFrame() : GameInput.Drop.WasReleasedThisFrame();

        // ------------------------------------------------------------------ holding

        private void FixedUpdate()
        {
            Item item = HeldItem;
            if (!IsLocal || item == null) return;
            Rigidbody body = item.Sync.Body;
            if (body.isKinematic) return;
            float dt = Time.fixedDeltaTime;

            _holdPercent = Mathf.Min(1f, _holdPercent + dt / _pickUpGlideTime);
            float ease = Mathf.SmoothStep(0f, 1f, _holdPercent);
            GetHoldTarget(item, out Vector3 target, out Quaternion targetRotation);

            // Stuck behind something far from the hands for a moment: pop it back.
            Vector3 toTarget = target - body.position;
            if (Time.time - _heldSince > 0.5f && toTarget.sqrMagnitude > _unstuckDistance * _unstuckDistance)
            {
                _stuckTime += dt;
                if (_stuckTime > 0.25f)
                {
                    body.position = target;
                    body.rotation = targetRotation;
                    body.linearVelocity = _hub.Motor.Velocity;
                    _stuckTime = 0f;
                    return;
                }
            }
            else
            {
                _stuckTime = 0f;
            }

            // Heavier things follow more lazily.
            float follow = _holdSpeed / (1f + item.Mass / 15f);
            Vector3 velocity = toTarget * (follow * ease) + _hub.Motor.Velocity;
            if (item.Sync.IsTouching && velocity.sqrMagnitude > _maxSpeedWhileTouching * _maxSpeedWhileTouching)
                velocity = velocity.normalized * _maxSpeedWhileTouching; // slide along walls instead of fighting them
            body.linearVelocity = velocity;
            body.angularVelocity = AngularVelocityTowards(body.rotation, targetRotation) * (_holdRotateSpeed * ease);
        }

        private void LateUpdate()
        {
            // Remote holders: glue the item to their (synced) head.
            Item item = HeldItem;
            if (IsLocal || item == null) return;
            GetHoldTarget(item, out Vector3 target, out Quaternion rotation);
            float k = 1f - Mathf.Exp(-30f * Time.deltaTime);
            Vector3 current = item.transform.position;
            Vector3 position = (current - target).sqrMagnitude > 4f ? target : Vector3.Lerp(current, target, k);
            item.PlaceInHand(position, Quaternion.Slerp(item.transform.rotation, rotation, k));
        }

        private void GetHoldTarget(Item item, out Vector3 position, out Quaternion rotation)
        {
            Transform aim = IsLocal ? AimTransform : _head;
            item.GetHoldPose(_hub, out Vector3 holdOffset, out Quaternion holdRotation, out float pitchFollow);
            // Wind-up: pull the item back (and a little down) while a throw charges.
            float pull = Mathf.SmoothStep(0f, 1f, Charge01);
            Vector3 offset = holdOffset + new Vector3(0.05f, -0.06f, -_chargePullback) * pull;
            Quaternion windUp = Quaternion.Euler(-25f * pull, 0f, 0f);
            if (pitchFollow >= 0.999f)
            {
                position = aim.TransformPoint(offset);
                rotation = aim.rotation * holdRotation * windUp;
                return;
            }
            // Big things (a person) only partly follow looking up and down, so they don't end up under your feet.
            Vector3 f = aim.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            Quaternion frame = Quaternion.Euler(pitch * pitchFollow, yaw, 0f);
            position = aim.position + frame * offset;
            rotation = frame * holdRotation * windUp;
        }

        /// <summary>Angular velocity (rad/s per unit speed) that turns <paramref name="from"/> toward <paramref name="to"/>.</summary>
        private static Vector3 AngularVelocityTowards(Quaternion from, Quaternion to)
        {
            Quaternion delta = to * Quaternion.Inverse(from);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(axis.x) || angle < 0.01f) return Vector3.zero;
            if (angle > 180f) angle -= 360f;
            return axis * (angle * Mathf.Deg2Rad);
        }

        private void GrabCommand(string filter)
        {
            bool Matches(Item i) => filter == null ||
                                    i.DisplayName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    i.name.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) >= 0;
            Item target = null;
            if (_hub.Interactor != null && _hub.Interactor.Current != null)
                target = _hub.Interactor.Current.GetComponent<Item>();
            if (target != null && !Matches(target)) target = null;
            if (target == null)
            {
                float range = filter != null ? 6f : 4f;
                float best = range * range;
                foreach (Item i in Item.All)
                {
                    float d = (i.transform.position - transform.position).sqrMagnitude;
                    if (!i.IsHeld && d < best && Matches(i)) { best = d; target = i; }
                }
            }
            if (target == null) DevCommands.Print($"nothing{(filter != null ? $" matching '{filter}'" : "")} to grab nearby");
            else TryPickUp(target);
        }
    }
}
