using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Carrying, pockets, throwing and eating.
    ///
    /// Four inventory slots (1-4 / mouse wheel). The selected slot is what's in your hands; small things (a life
    /// ring, a ball, coconuts) wait hidden in the other slots, big ones (crates, people) have to be put down before
    /// you can switch. Which item sits in which slot lives on the items (host-owned), the selected slot on PlayerHub,
    /// so everyone agrees on what you hold.
    ///
    /// Local player: the held item stays a live rigidbody and is steered to a hold point in front of the camera
    /// with velocities every physics step (glides in after pickup, lags with weight, slides along walls instead of
    /// clipping through them, gets unstuck if it falls far behind). Tap Drop to let go, hold Drop (or Primary) to
    /// charge a throw; the item pulls back while charging. A throw before the host confirmed the pickup is queued.
    /// Hold Secondary with food in hand to eat it.
    ///
    /// Remote players: the held item is glued in front of their head each frame; pocketed items ride along, hidden.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        public const int SlotCount = 4;

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

        [Header("Seen by others")]
        [Tooltip("Remote players' held items sit closer to their body than in first person, so their arms reach them.")]
        [SerializeField] private Vector3 _remoteHoldScale = new(0.9f, 0.85f, 0.66f);

        [Header("Eating")]
        [SerializeField] private Vector3 _mouthOffset = new(0.03f, -0.13f, 0.3f);
        [SerializeField] private float _biteInterval = 0.42f;

        private enum ChargeSource { None, Primary, Drop }

        public enum GripKind { None, OneHand, TwoHands, Person }

        private ChargeSource _chargeSource;
        private float _chargeStart;
        private float _holdPercent;
        private float _heldSince;
        private float _stuckTime;
        private float? _queuedThrow;   // charge (0..1), or -1 for a plain drop
        private float _queuedAt;
        private bool _chargeAnnounced;
        private int _localSlot;
        private Item _lastHeld;
        private float _eatProgress;
        private float _nextBite;
        private float _eatBlend;
        private float _scriptedEatUntil = float.NegativeInfinity;

        private readonly Item[] _slots = new Item[SlotCount];
        private int _slotsFrame = -1;

        /// <summary>The item in your hands (the selected slot).</summary>
        public Item HeldItem => SlotItem(ActiveSlot);
        /// <summary>Selected slot: predicted for our own player, synced for everyone else.</summary>
        public int ActiveSlot => IsLocal ? _localSlot : _hub.SyncedActiveSlot;
        /// <summary>Holding food to the mouth.</summary>
        public bool IsEating { get; private set; }
        public float EatProgress01 => _eatProgress;
        public bool IsCharging => _chargeSource != ChargeSource.None && Charge01 > 0f;
        public float Charge01 { get; private set; }

        private bool IsLocal => _hub.IsOwner;

        // ------------------------------------------------------------------ slots

        /// <summary>What's in a slot right now (items know their holder and slot; cached per frame).</summary>
        public Item SlotItem(int slot)
        {
            if (slot < 0 || slot >= SlotCount) return null;
            if (_slotsFrame != Time.frameCount) RebuildSlots();
            return _slots[slot];
        }

        private void RebuildSlots()
        {
            _slotsFrame = Time.frameCount;
            System.Array.Clear(_slots, 0, SlotCount);
            foreach (Item item in Item.All)
            {
                if (item.Holder != _hub) continue;
                int slot = item.Slot;
                if (slot >= 0 && slot < SlotCount && _slots[slot] == null) _slots[slot] = item;
            }
        }

        /// <summary>Call when an item's holder or slot changed (so the per-frame cache is rebuilt now).</summary>
        internal void Invalidate() => _slotsFrame = -1;

        public int FirstFreeSlot()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                int slot = (ActiveSlot + i) % SlotCount;
                if (SlotItem(slot) == null) return slot;
            }
            return -1;
        }

        /// <summary>Switch hands to another slot. Refused while holding something too big to pocket.</summary>
        public bool SelectSlot(int slot)
        {
            if (!IsLocal || slot < 0 || slot >= SlotCount || slot == _localSlot) return false;
            Item current = HeldItem;
            if (current != null && !current.Pocketable)
            {
                PlayerHud.ShowToast($"Hands full: put the {current.DisplayName} down first ([{GameInput.KeyLabel(GameInput.Drop)}])", 2.5f);
                return false;
            }
            StopEating();
            CancelCharge();
            _localSlot = slot;
            _hub.RequestActiveSlot(slot);
            OnActiveSlotChanged();
            return true;
        }

        /// <summary>The selected slot changed (ours, or a remote player's synced one): items move between hands and pockets.</summary>
        internal void OnActiveSlotChanged()
        {
            Invalidate();
            foreach (Item item in Item.All)
                if (item.Holder == _hub) item.RefreshHeldState();
            ResetHoldState();
        }

        // ------------------------------------------------------------------ called by Item

        internal void OnItemGained(Item item)
        {
            Invalidate();
            if (item == HeldItem) ResetHoldState();
        }

        internal void OnItemLost(Item item)
        {
            Invalidate();
            if (item == _lastHeld) ResetHoldState();
        }

        private void ResetHoldState()
        {
            _chargeSource = ChargeSource.None;
            Charge01 = 0f;
            _holdPercent = 0f;
            _heldSince = Time.time;
            _stuckTime = 0f;
            _queuedThrow = null;
            _lastHeld = HeldItem;
            StopEating();
        }

        // ------------------------------------------------------------------ actions (local player)

        public void TryPickUp(Item item)
        {
            if (!IsLocal || item == null || item.IsHeld)
                return;
            Item current = HeldItem;
            if (current != null)
            {
                // Pocket what we hold if we can, otherwise swap it for the new thing.
                int free = current.Pocketable ? FirstFreeSlot() : -1;
                if (free >= 0) SelectSlot(free);
                else Drop();
            }
            item.RequestPickUp(_hub, ActiveSlot);
            Invalidate();
            ResetHoldState();
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

            StopEating();
            item.Release(_hub, velocity, spin);
            Invalidate();
            ResetHoldState();
            if (charge >= 0f) _hub.Gesture(AvatarGesture.Throw);
            else if (_chargeAnnounced) _hub.Gesture(AvatarGesture.ChargeEnd);
            _chargeAnnounced = false;
        }

        private void CancelCharge()
        {
            if (_chargeAnnounced) _hub.Gesture(AvatarGesture.ChargeEnd);
            _chargeAnnounced = false;
            _chargeSource = ChargeSource.None;
            Charge01 = 0f;
        }

        private Transform AimTransform => _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _head;

        // ------------------------------------------------------------------ eating

        private void StopEating()
        {
            if (!IsEating) return;
            IsEating = false;
            if (IsLocal) _hub.Gesture(AvatarGesture.EatStop);
        }

        private void UpdateEating(Item item)
        {
            Edible food = item != null ? item.GetComponent<Edible>() : null;
            bool pressed = GameInput.Secondary.IsPressed() || Time.time < _scriptedEatUntil;
            bool wants = food != null && pressed && _chargeSource == ChargeSource.None && item.IsConfirmedHolder(_hub);
            if (!wants)
            {
                StopEating();
                _eatProgress = Mathf.Max(0f, _eatProgress - Time.deltaTime * 0.5f);
                return;
            }
            if (!IsEating)
            {
                IsEating = true;
                _nextBite = Time.time + _biteInterval * 0.6f;
                _hub.Gesture(AvatarGesture.EatStart);
            }
            _eatProgress += Time.deltaTime / Mathf.Max(0.2f, food.Seconds);
            if (Time.time >= _nextBite)
            {
                _nextBite = Time.time + _biteInterval;
                food.PlayBite();
            }
            if (_eatProgress < 1f) return;

            _eatProgress = 0f;
            StopEating();
            if (_hub.Vitals != null) _hub.Vitals.Eat(food.Food);
            PlayerHud.ShowToast($"Mmm, {item.DisplayName.ToLowerInvariant()}.", 2f);
            food.Consume(_hub);
            Invalidate();
        }

        // ------------------------------------------------------------------ commands

        private void OnEnable()
        {
            if (!IsLocal) return;
            DevCommands.Register("grab", "[name]", "Pick up the item you look at, or the nearest one (optionally matching a name, e.g. 'grab tourist').",
                args => GrabCommand(args.Length > 0 ? args[0] : null), owner: this);
            DevCommands.Register("throw", "[charge 0..1]", "Throw the held item.", args => Throw(args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 1f), owner: this);
            DevCommands.Register("drop", "", "Drop the held item.", _ => Drop(), owner: this);
            DevCommands.Register("eat", "[seconds]", "Hold 'eat' on autopilot (automated tests).", args =>
                _scriptedEatUntil = Time.time + (args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 2.5f), cheat: true, owner: this);
            DevCommands.Register("slot", "<1-4>", "Select an inventory slot.", args => SelectSlot(Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)) - 1), owner: this);
            DevCommands.Register("inventory", "", "What's in each slot.", _ =>
            {
                for (int i = 0; i < SlotCount; i++)
                    DevCommands.Print($"  {i + 1}{(i == ActiveSlot ? "*" : " ")} {(SlotItem(i) != null ? SlotItem(i).DisplayName : "-")}");
            }, owner: this);
        }

        private void OnDisable()
        {
            DevCommands.Unregister("grab", this);
            DevCommands.Unregister("throw", this);
            DevCommands.Unregister("drop", this);
            DevCommands.Unregister("slot", this);
            DevCommands.Unregister("eat", this);
            DevCommands.Unregister("inventory", this);
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
            if (HeldItem != _lastHeld) ResetHoldState(); // something new in our hands (pickup, slot, host decision)
            if (!IsLocal)
                return;

            if (GameInput.GameplayActive) ReadSlotInput();

            Item held = HeldItem;
            if (held == null)
                return;

            if (_queuedThrow.HasValue)
            {
                if (held.IsConfirmedHolder(_hub)) RequestRelease(_queuedThrow.Value);
                else if (Time.time - _queuedAt > 1f) _queuedThrow = null; // host never confirmed; forget it
                return;
            }

            if (!GameInput.GameplayActive)
            {
                CancelCharge();
                StopEating();
                return;
            }

            UpdateEating(held);
            if (IsEating || HeldItem == null) return;

            if (_chargeSource == ChargeSource.None)
            {
                if (GameInput.Primary.WasPressedThisFrame()) BeginCharge(ChargeSource.Primary);
                else if (GameInput.Drop.WasPressedThisFrame()) BeginCharge(ChargeSource.Drop);
            }

            if (_chargeSource == ChargeSource.None)
                return;

            float heldFor = Time.time - _chargeStart;
            // Drop only starts charging after a tap's worth of time, so a quick tap is a plain drop.
            float chargeTime = _chargeSource == ChargeSource.Drop ? heldFor - _tapTime : heldFor;
            Charge01 = Mathf.Clamp01(chargeTime / _chargeTime);
            if (!_chargeAnnounced && Charge01 > 0.05f)
            {
                _chargeAnnounced = true; // others see the wind-up
                _hub.Gesture(AvatarGesture.ChargeStart);
            }

            InputActionReleased(out bool released);
            if (!released)
                return;
            ChargeSource source = _chargeSource;
            _chargeSource = ChargeSource.None;
            if (source == ChargeSource.Drop && heldFor < _tapTime) Drop();
            else Throw(Charge01);
        }

        private void ReadSlotInput()
        {
            for (int i = 0; i < SlotCount; i++)
                if (GameInput.Slots[i].WasPressedThisFrame())
                {
                    SelectSlot(i);
                    return;
                }
            float scroll = GameInput.SlotScroll.ReadValue<float>();
            if (Mathf.Abs(scroll) > 0.01f && _chargeSource == ChargeSource.None)
                SelectSlot((_localSlot + (scroll < 0f ? 1 : SlotCount - 1)) % SlotCount);
        }

        private void BeginCharge(ChargeSource source)
        {
            _chargeSource = source;
            _chargeStart = Time.time;
            Charge01 = 0f;
            _chargeAnnounced = false;
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
            bool eating = IsLocal ? IsEating : _hub.Avatar != null && _hub.Avatar.RemoteEating;
            _eatBlend = Mathf.MoveTowards(_eatBlend, eating ? 1f : 0f, Time.deltaTime * 4f);

            // Pocketed items ride along with us, hidden (so they drop right here if we leave).
            Vector3 hip = transform.position + Vector3.up * 0.9f;
            int active = ActiveSlot;
            for (int i = 0; i < SlotCount; i++)
            {
                Item pocketed = i == active ? null : SlotItem(i);
                if (pocketed != null && pocketed.Sync.Body.isKinematic) pocketed.PlaceInHand(hip, pocketed.transform.rotation);
            }

            // Remote holders: glue the held item to their (synced) head.
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
            if (!IsLocal) holdOffset = Vector3.Scale(holdOffset, _remoteHoldScale);
            // Eating: up to the mouth, with a little bob per bite.
            if (_eatBlend > 0f)
            {
                float chew = IsLocal && IsEating ? Mathf.Sin(Time.time * 15f) * 0.012f : 0f;
                holdOffset = Vector3.Lerp(holdOffset, _mouthOffset + new Vector3(0f, chew, 0f), Mathf.SmoothStep(0f, 1f, _eatBlend));
            }
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

        /// <summary>
        /// Where the hands go on the held item, in world space: both hands just outside the sides of bigger things,
        /// one hand under small ones or on the rim of a ring, and under the back and knees of a person.
        /// </summary>
        public GripKind GetGrips(out Vector3 left, out Vector3 right)
        {
            left = right = Vector3.zero;
            Item item = HeldItem;
            if (item == null) return GripKind.None;
            Transform frame = IsLocal ? AimTransform : _head;
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.ProjectOnPlane(frame.right, up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : frame.right;

            if (item.Grip == ItemGrip.Person)
            {
                Transform body = item.transform;
                Vector3 a = body.position + body.up * 0.24f - up * 0.14f; // under the shoulders
                Vector3 b = body.position - body.up * 0.32f - up * 0.14f; // under the knees
                bool aRight = Vector3.Dot(a - b, side) > 0f;
                right = aRight ? a : b;
                left = aRight ? b : a;
                return GripKind.Person;
            }

            Bounds bounds = item.VisualBounds;
            Vector3 c = bounds.center, e = bounds.extents;
            float halfWidth = Mathf.Abs(side.x) * e.x + Mathf.Abs(side.y) * e.y + Mathf.Abs(side.z) * e.z;
            Vector3 toUs = Vector3.ProjectOnPlane(frame.position - c, up);
            toUs = toUs.sqrMagnitude > 1e-4f ? toUs.normalized : -frame.forward;
            float depth = Mathf.Abs(toUs.x) * e.x + Mathf.Abs(toUs.z) * e.z;
            ItemGrip style = item.Grip != ItemGrip.Auto ? item.Grip
                : bounds.size.x < 0.36f && bounds.size.y < 0.36f && bounds.size.z < 0.36f ? ItemGrip.OneHand : ItemGrip.TwoHands;
            if (style == ItemGrip.OneHand)
            {
                right = halfWidth > 0.2f
                    ? c + side * (halfWidth * 0.85f) + toUs * (depth * 0.5f)                       // the rim of a ring
                    : c + side * (halfWidth * 0.6f + 0.02f) - up * (e.y * 0.55f) + toUs * (depth * 0.2f); // cupped under its side
                left = right;
                return GripKind.OneHand;
            }
            // On the near corners, just outside the sides: the hands wrap round the front of the item where we can see them.
            Vector3 near = toUs * (depth * 0.8f) + up * (e.y * 0.1f);
            left = c - side * (halfWidth + 0.02f) + near;
            right = c + side * (halfWidth + 0.02f) + near;
            return GripKind.TwoHands;
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
