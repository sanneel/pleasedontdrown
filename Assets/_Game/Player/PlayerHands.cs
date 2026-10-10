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
    /// Six inventory slots (1-6 / mouse wheel). The selected slot is what's in your hands; small things (a life
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
        public const int SlotCount = 6;

        [SerializeField] private PlayerHub _hub;
        [SerializeField] private Transform _head;

        // Holding and throwing are How to Fish's (its tuning measured from the game; our own code).
        [Header("Holding (local physics)")]
        [Tooltip("Plain things float this far straight ahead of the eyes.")]
        [SerializeField] private float _holdDistance = 1.5f;
        [Tooltip("Velocity toward the hold point per metre off it (and turning, per radian).")]
        [SerializeField] private float _holdSpeed = 15f;
        [SerializeField] private float _holdRotateSpeed = 15f;
        [Tooltip("Share of our own (smoothed) velocity the held thing gets on top.")]
        [SerializeField] private float _carryVelocityShare = 0.75f;
        [SerializeField] private float _carryVelocitySmoothing = 0.05f;
        [Tooltip("Picked up: the steering eases in at this rate (per second).")]
        [SerializeField] private float _pickUpRate = 2f;
        [SerializeField] private float _maxSpeedWhileTouching = 10f;
        [SerializeField] private float _maxTurnWhileTouching = 5f;
        [Tooltip("Stuck this far behind (after the first second): back to the hold point.")]
        [SerializeField] private float _unstuckDistance = 2f;
        [SerializeField] private float _unstuckAfter = 1f;
        [Tooltip("Held things bob with the head, more while sprinting.")]
        [SerializeField] private float _sprintBob = 1.5f;

        [Header("Throwing (hold Drop)")]
        [Tooltip("Throw speed at full charge (m/s, added to how it's moving).")]
        [SerializeField] private float _maxThrowSpeed = 10f;
        [Tooltip("Seconds of holding Drop to full charge.")]
        [SerializeField] private float _chargeTime = 0.5f;
        [Tooltip("Released before this share of the charge: just dropped.")]
        [SerializeField] private float _throwThreshold = 0.3f;
        [Tooltip("Pulled back toward you while charging (m).")]
        [SerializeField] private float _chargePullback = 0.2f;

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
        private float _dropForce;                 // 0..1 while Drop is held
        private Vector3 _carryVelocity, _carryVelocityRef;
        private float _bobShare = 1f;
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
        private Avatars.AvatarAnimator _animator; // remote players: where their lips are (drinking)
        public float EatProgress01 => _eatProgress;
        public bool IsCharging => _chargeSource != ChargeSource.None && Charge01 > 0f;
        public bool IsPreparingThrow => _chargeSource != ChargeSource.None;

        public float Charge01 { get; private set; }
        /// <summary>The item we last threw, when, and how hard (first-person hands follow it out briefly).</summary>
        public Item LastThrown { get; private set; }
        public float LastThrowTime { get; private set; } = -10f;
        public float LastThrowCharge { get; private set; }

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
            BeachAudio.PlayLocal(BeachAudio.Equip, 0.45f);
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
            _dropForce = 0f;
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
            BeachAudio.PlayLocal(BeachAudio.Pickup, 0.65f);
            Invalidate();
            ResetHoldState();
        }

        /// <summary>Throw with a charge from 0 (lob) to 1 (full power). Queued until the host confirms the pickup.</summary>
        public void Throw(float charge) => RequestRelease(Mathf.Clamp01(charge));

        private Combat.PlayerCombat _combat;

        /// <summary>Owner: our hands are busy being knocked out, carried or fired from a cannon.</summary>
        public bool HandsTaken()
        {
            if (_combat == null) _combat = GetComponent<Combat.PlayerCombat>();
            return (_combat != null && _combat.IsDazed) || PlayerCarry.IsCarried(_hub) || Fun.HumanCannon.IsInside(_hub) || Fun.HumanCannon.IsPushing(_hub);
        }

        /// <summary>Owner: whatever is in our hands goes in a free pocket (it stays ours), else it's dropped.</summary>
        public void Stow()
        {
            Item held = HeldItem;
            if (held == null) return;
            CancelCharge();
            StopEating();
            if (held.Pocketable)
            {
                int free = FirstFreeSlot();
                if (free >= 0)
                {
                    SelectSlot(free);
                    return;
                }
            }
            Drop();
        }

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
            if (charge < 0f && item.TryGetComponent(out Rescue.VictimBody victim) && victim.LayDown(_hub))
            {
                // A person, put down on land: laid on their back on the sand in front of us, ready for CPR.
                velocity = Vector3.zero;
                spin = Vector3.zero;
            }
            else
            {
                // Let go: it carries on as it was moving in our hands (a gun: as we were moving); a throw adds up to
                // 10 m/s straight where we look, whatever it weighs.
                Rigidbody body = item.Sync.Body;
                bool live = !body.isKinematic;
                velocity = live ? body.linearVelocity : playerVelocity;
                spin = live ? body.angularVelocity : Vector3.zero;
                if (charge > 0f)
                    velocity += AimTransform.forward * (charge * _maxThrowSpeed * item.ThrowStrength);
                // A basketball thrown at a hoop goes on a proper arc (Fun.BasketballHoop.TryAssist).
                if (charge > 0f && item.DisplayName == Fun.BasketballHoop.BallName && live &&
                    Fun.BasketballHoop.TryAssist(body.position, AimTransform.forward, charge, body.linearDamping, out Vector3 shot))
                {
                    velocity = shot;
                    spin = Vector3.Cross(new Vector3(shot.x, 0f, shot.z).normalized, Vector3.up) * 8f; // backspin
                }
            }

            StopEating();
            if (charge >= 0f)
            {
                LastThrown = item;
                LastThrowTime = Time.time;
                LastThrowCharge = charge;
            }
            item.Release(_hub, velocity, spin);
            if (charge > 0f) Fun.ThrownImpact.Arm(item, _hub); // whoever it hits gets bonked
            BeachAudio.PlayLocal(charge >= 0f ? BeachAudio.Throw : BeachAudio.Drop, 0.6f);
            Invalidate();
            ResetHoldState();
            bool jumpShot = charge > 0f && item.DisplayName == Fun.BasketballHoop.BallName;
            if (jumpShot)
            {
                // A basketball: a jump shot. Both arms go up and the shooter hops as the ball leaves the hands.
                _hub.Gesture(AvatarGesture.JumpShot);
                if (_hub.Motor != null && _hub.Motor.IsGrounded && !_hub.Motor.IsSwimming) _hub.Motor.AddImpulse(Vector3.up * 3.4f);
            }
            else if (charge >= 0f) _hub.Gesture(AvatarGesture.Throw);
            else if (_chargeAnnounced) _hub.Gesture(AvatarGesture.ChargeEnd);
            _chargeAnnounced = false;
        }

        private void CancelCharge()
        {
            if (_chargeAnnounced) _hub.Gesture(AvatarGesture.ChargeEnd);
            _chargeAnnounced = false;
            _chargeSource = ChargeSource.None;
            Charge01 = 0f;
            _dropForce = 0f;
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
            if (food.BurpAfter > 0f) _hub.BurpLater(food.BurpAfter);
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
            DevCommands.Register("usetool", "", "Use the held tool (pistol, defibrillator) as if pressing Primary.", _ =>
            {
                if (HeldItem != null && HeldItem.TryGetComponent(out Combat.IHeldTool t)) t.Use(_hub);
                else DevCommands.Print("not holding a tool");
            }, cheat: true, owner: this);
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
            DevCommands.Unregister("usetool", this);
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

            // Hands that can't hold anything (knocked out cold, carried off by someone, stuffed in the cannon): what
            // was in them goes in a pocket, or falls. (It used to stay floating where the hands had been: a gun up
            // in the air over somebody sliding about on the sand.)
            if (HandsTaken())
            {
                Stow();
                return;
            }

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

            // Guns handle their own trigger, sights and reload (Combat/Weapon); here only hold Drop to throw them.
            if (held.TryGetComponent(out Combat.Weapon _))
            {
                if (_chargeSource == ChargeSource.None && GameInput.Drop.WasPressedThisFrame()) BeginCharge(ChargeSource.Drop);
                if (_chargeSource == ChargeSource.None) return;
            }

            // Tools (defibrillator): Primary uses them; hold Drop to throw them instead.
            if (_chargeSource == ChargeSource.None && GameInput.Primary.WasPressedThisFrame() && held.TryGetComponent(out Combat.IHeldTool tool))
            {
                if (held.IsConfirmedHolder(_hub)) tool.Use(_hub);
                return;
            }

            // Hold Drop to throw: the charge builds over half a second (the item pulls back toward you); let go
            // before 30% of it and it's just dropped.
            if (_chargeSource == ChargeSource.None && GameInput.Drop.WasPressedThisFrame()) BeginCharge(ChargeSource.Drop);
            if (_chargeSource == ChargeSource.None)
                return;

            _dropForce = Mathf.Clamp01((Time.time - _chargeStart) / _chargeTime);
            Charge01 = _dropForce >= _throwThreshold ? _dropForce : 0f;
            if (!_chargeAnnounced && Charge01 > 0f)
            {
                _chargeAnnounced = true; // others see the wind-up
                _hub.Gesture(AvatarGesture.ChargeStart);
            }

            InputActionReleased(out bool released);
            if (!released)
                return;
            _chargeSource = ChargeSource.None;
            float force = _dropForce >= _throwThreshold ? _dropForce : 0f;
            _dropForce = 0f;
            if (force <= 0f) Drop();
            else Throw(force);
        }

        private void ReadSlotInput()
        {
            if (Fun.BarSeat.MenuOpen) return; // sitting at a bar: the number keys order from its menu
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

            // Steering eases in after pickup (half a second), and carries a share of our own velocity.
            _holdPercent = Mathf.Min(1f, _holdPercent + _pickUpRate * dt);
            _carryVelocity = Vector3.SmoothDamp(_carryVelocity, _hub.Motor.Velocity, ref _carryVelocityRef, _carryVelocitySmoothing, Mathf.Infinity, dt);
            GetHoldTarget(item, out Vector3 target, out Quaternion targetRotation);

            // Fallen far behind (stuck on something) after the first second: back to the hold point.
            if (Time.time - _heldSince > _unstuckAfter && (target - body.position).sqrMagnitude > _unstuckDistance * _unstuckDistance)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = target;
                return;
            }

            // Toward the hold point at 15x the distance per second (and turning likewise), plus 75% of our velocity;
            // slower while it's bumping into something, so it slides along instead of fighting.
            Vector3 velocity = (target - body.position) * (_holdSpeed * _holdPercent) + _carryVelocity * _carryVelocityShare;
            if (item.Sync.IsTouching && velocity.sqrMagnitude > _maxSpeedWhileTouching * _maxSpeedWhileTouching)
                velocity = velocity.normalized * _maxSpeedWhileTouching;
            Vector3 turn = AngularVelocityTowards(body.rotation, targetRotation) * (_holdRotateSpeed * _holdPercent);
            if (item.Sync.IsTouching && turn.sqrMagnitude > _maxTurnWhileTouching * _maxTurnWhileTouching)
                turn = turn.normalized * _maxTurnWhileTouching;
            body.linearVelocity = velocity;
            body.angularVelocity = turn;
        }

        private void LateUpdate()
        {
            bool eating = IsLocal ? IsEating : _hub.Avatar != null && _hub.Avatar.RemoteEating;
            _eatBlend = Mathf.MoveTowards(_eatBlend, eating ? 1f : 0f, Time.deltaTime * 4f);
            bool sprinting = _hub.Motor != null && _hub.Motor.IsSprinting;
            _bobShare = Mathf.Lerp(_bobShare, sprinting ? _sprintBob : 1f, Mathf.Min(1f, 10f * Time.deltaTime));

            // Pocketed items ride along with us, hidden (so they drop right here if we leave).
            Vector3 hip = transform.position + Vector3.up * 0.9f;
            int active = ActiveSlot;
            for (int i = 0; i < SlotCount; i++)
            {
                Item pocketed = i == active ? null : SlotItem(i);
                if (pocketed != null && pocketed.Sync.Body.isKinematic) pocketed.PlaceInHand(hip, pocketed.transform.rotation);
            }

            Item item = HeldItem;
            if (item == null) return;
            if (IsLocal)
            {
                // Guns sit rigidly in view (their own pose adds aim, sway and recoil).
                if (item.RigidInHand && item.Sync.Body.isKinematic)
                {
                    GetHoldTarget(item, out Vector3 at, out Quaternion turned);
                    item.PlaceInHand(at, turned);
                }
                return;
            }

            // Remote holders: glue the held item to their (synced) head.
            GetHoldTarget(item, out Vector3 target, out Quaternion rotation);
            float k = 1f - Mathf.Exp(-30f * Time.deltaTime);
            Vector3 current = item.transform.position;
            Vector3 position = (current - target).sqrMagnitude > 4f ? target : Vector3.Lerp(current, target, k);
            item.PlaceInHand(position, Quaternion.Slerp(item.transform.rotation, rotation, k));
        }

        private void GetHoldTarget(Item item, out Vector3 position, out Quaternion rotation)
        {
            Transform aim = IsLocal ? AimTransform : _head;
            if (item.TryGetComponent(out Fun.BasketballDribble basketball))
            {
                basketball.GetHoldTarget(_hub, aim, out position, out rotation);
                return;
            }
            // Somebody else's gun or bat: held by their body (third person), not floated in front of their eyes the
            // way our own first-person view model is (that put guns up beside the head, pointing at the sky).
            if (!IsLocal && item.RigidInHand && item.GripRight != null)
            {
                if (_rig == null) _rig = GetComponentInChildren<Avatars.AvatarRig>();
                Quaternion flat = Quaternion.Euler(0f, aim.eulerAngles.y, 0f);
                bool rigged = _rig != null && _rig[Avatars.AvatarRig.Bone.UpperArmR] != null;
                Vector3 shoulderR = rigged ? _rig[Avatars.AvatarRig.Bone.UpperArmR].position : transform.position + flat * new Vector3(0.17f, 1.38f, 0f);
                Vector3 shoulderL = rigged ? _rig[Avatars.AvatarRig.Bone.UpperArmL].position : transform.position + flat * new Vector3(-0.17f, 1.38f, 0f);
                float reach = rigged ? _rig.UpperArmLength + _rig.ForearmLength + _rig.HandLength * 0.5f : 0.6f;
                ThirdPersonHold(item, shoulderR, shoulderL, reach, aim.forward, out position, out rotation, rigged ? _rig : null);
                return;
            }
            item.GetHoldPose(_hub, out Vector3 holdOffset, out Quaternion holdRotation, out float pitchFollow);
            if (IsLocal && !item.HasCustomHoldPose && !item.RigidInHand && _eatBlend <= 0f)
            {
                // Plain things (How to Fish): straight ahead of the eyes at the hold distance, turning with the view,
                // bobbing with the head (more while sprinting), pulled back a little while a throw charges.
                Quaternion view = aim.rotation;
                float back = _chargePullback * Mathf.SmoothStep(0f, 1f, _dropForce);
                Vector3 bob = _hub.Look != null ? _hub.Look.BobPosition * _bobShare : Vector3.zero;
                position = _head.position + view * (new Vector3(0f, 0f, _holdDistance - back) + bob);
                rotation = view * holdRotation;
                return;
            }
            if (!IsLocal) holdOffset = Vector3.Scale(holdOffset, _remoteHoldScale);
            // Eating: up to the mouth, with a little bob per bite.
            if (_eatBlend > 0f)
            {
                float chew = IsLocal && IsEating ? Mathf.Sin(Time.time * 15f) * 0.012f : 0f;
                float e = Mathf.SmoothStep(0f, 1f, _eatBlend);
                Edible drink = item.GetComponent<Edible>();
                if (drink != null && drink.Drink)
                {
                    // A bottle goes up bottom first, its mouth at the lips, tipping further as it empties.
                    Quaternion tipped = Quaternion.Euler(-(108f + 22f * (IsLocal ? _eatProgress : 0.5f)), 0f, 0f);
                    Vector3 lip = Vector3.Scale(drink.Lip, item.transform.lossyScale);
                    holdRotation = Quaternion.Slerp(holdRotation, tipped, e);
                    // (Right at the lips, not out where food is held up: the neck goes into the mouth, just under the view.
                    // Seen from outside, at the avatar's own lips, wherever its head is.)
                    Vector3 lips = new(0.015f, -0.1f, 0.1f);
                    if (!IsLocal)
                    {
                        if (_animator == null) _animator = GetComponentInChildren<Avatars.AvatarAnimator>();
                        if (_animator != null) lips = aim.InverseTransformPoint(_animator.Lips);
                    }
                    holdOffset = Vector3.Lerp(holdOffset, lips - tipped * lip + new Vector3(0f, chew * 0.3f, 0f), e);
                }
                else holdOffset = Vector3.Lerp(holdOffset, _mouthOffset + new Vector3(0f, chew, 0f), e);
            }
            // Wind-up: pull the item back (and a little down) while a throw charges.
            float pull = Mathf.SmoothStep(0f, 1f, IsLocal ? _dropForce : Charge01);
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
        /// How the hands hold the item in our hands (world space), How to Fish style: a palm position, finger and
        /// palm directions and a finger pose per hand. Both hands on the near sides of boxes, spread over a ball,
        /// one hand cupped under small things or hooked over the rim of a ring, under the back and knees of a person.
        /// </summary>
        public GripKind GetGrip(out HandGrip left, out HandGrip right) => GetGrip(HeldItem, out left, out right);

        /// <summary>The same for any item (hands that stay with a thrown item for a moment use this).</summary>
        public GripKind GetGrip(Item item, out HandGrip left, out HandGrip right)
        {
            left = right = default;
            if (item == null) return GripKind.None;
            if (item.TryGetComponent(out Fun.BasketballDribble dribble))
                return dribble.GetGrips(_hub, out left, out right);
            // Hand-placed grips on the item win (fingers along the grip's forward, palm toward its down).
            if (item.GripRight != null || item.GripLeft != null)
            {
                if (item.GripRight != null) right = new HandGrip(item.GripRight.position, item.GripRight.forward, -item.GripRight.up, item.GripPose);
                if (item.GripLeft != null) left = new HandGrip(item.GripLeft.position, item.GripLeft.forward, -item.GripLeft.up, item.GripPoseLeft);
                // Seen from outside: the left hand on the forend where the arm reaches (long guns' grips are far out).
                if (!IsLocal && left.Active && item.RigidInHand)
                {
                    if (_rig == null) _rig = GetComponentInChildren<Avatars.AvatarRig>();
                    if (_rig != null && _rig[Avatars.AvatarRig.Bone.UpperArmL] != null)
                        left.Point = ReachableLeftGrip(left.Point, item.transform.forward, _rig[Avatars.AvatarRig.Bone.UpperArmL].position,
                            _rig.UpperArmLength + _rig.ForearmLength + _rig.HandLength * 0.5f);
                }
                // A gun being worked (reloading, the bolt): the hands follow its moves.
                if (item.TryGetComponent(out Combat.Weapon gun))
                {
                    if (gun.HandOverride(true, out HandGrip r)) right = r;
                    if (gun.HandOverride(false, out HandGrip l)) left = l;
                }
                return right.Active && left.Active ? GripKind.TwoHands : GripKind.OneHand;
            }

            Transform frame = IsLocal ? AimTransform : _head;
            Vector3 up = Vector3.up;
            Vector3 side = Vector3.ProjectOnPlane(frame.right, up);
            side = side.sqrMagnitude > 1e-4f ? side.normalized : frame.right;
            Vector3 fwd = Vector3.Cross(side, up); // horizontal forward

            if (item.Grip == ItemGrip.Person)
            {
                Transform body = item.transform;
                Vector3 a = body.position + body.up * 0.24f - up * 0.15f; // under the shoulders
                Vector3 b = body.position - body.up * 0.32f - up * 0.15f; // under the knees
                bool aRight = Vector3.Dot(a - b, side) > 0f;
                right = new HandGrip(aRight ? a : b, fwd, up, HandPose.Carry);
                left = new HandGrip(aRight ? b : a, fwd, up, HandPose.Carry);
                return GripKind.Person;
            }

            Bounds bounds = item.VisualBounds;
            Vector3 c = bounds.center, e = bounds.extents;
            float halfWidth = Mathf.Abs(side.x) * e.x + Mathf.Abs(side.y) * e.y + Mathf.Abs(side.z) * e.z;
            Vector3 toUs = Vector3.ProjectOnPlane(frame.position - c, up);
            toUs = toUs.sqrMagnitude > 1e-4f ? toUs.normalized : -fwd;
            float depth = Mathf.Abs(toUs.x) * e.x + Mathf.Abs(toUs.z) * e.z;
            ItemGrip style = item.Grip != ItemGrip.Auto ? item.Grip
                : bounds.size.x < 0.36f && bounds.size.y < 0.36f && bounds.size.z < 0.36f ? ItemGrip.OneHand : ItemGrip.TwoHands;

            if (style == ItemGrip.OneHand)
            {
                if (halfWidth > 0.2f)
                {
                    // A ring: palm on top of the tube at its near-right edge, fingers curled round it.
                    Vector3 rim = c + side * (halfWidth * 0.84f) + toUs * (depth * 0.45f) + up * 0.04f;
                    right = new HandGrip(rim, (fwd - side * 0.25f).normalized, -up, HandPose.LooseFist);
                }
                else
                {
                    // Small things sit in the palm, fingers wrapped round.
                    Vector3 under = c - up * (e.y * 0.92f) + toUs * (depth * 0.12f);
                    right = new HandGrip(under, (fwd - side * 0.2f).normalized, up, HandPose.Cup);
                }
                return GripKind.OneHand;
            }

            bool round = item.GetComponentInChildren<SphereCollider>() != null;
            if (round)
            {
                // A ball: palms on its sides, fingers spread up and over it.
                Vector3 l = c - side * (halfWidth * 0.97f) + toUs * (depth * 0.28f);
                Vector3 r = c + side * (halfWidth * 0.97f) + toUs * (depth * 0.28f);
                Vector3 over = (up * 0.55f - toUs * 0.6f).normalized;
                left = new HandGrip(l, over, (c - l).normalized, HandPose.BallGrip);
                right = new HandGrip(r, over, (c - r).normalized, HandPose.BallGrip);
                return GripKind.TwoHands;
            }

            // A box: palms flat on the sides near the front corners (outside the silhouette, so we see them),
            // fingers pointing away along the sides.
            Vector3 near = toUs * (depth * 0.7f) + up * (e.y * 0.3f);
            Vector3 along = (-toUs * 0.85f - up * 0.35f).normalized;
            left = new HandGrip(c - side * (halfWidth + 0.012f) + near, along, side, HandPose.BoxGrip);
            right = new HandGrip(c + side * (halfWidth + 0.012f) + near, along, -side, HandPose.BoxGrip);
            return GripKind.TwoHands;
        }

        private Avatars.AvatarRig _rig;

        /// <summary>How far up or down a held gun follows where its holder looks (third person).</summary>
        public const float ThirdPersonMaxPitch = 45f, LongGunMaxPitch = 18f;

        /// <summary>
        /// A front grip the left arm can actually reach: slid back along the gun (you can hold a forend anywhere
        /// along it) until it's within <paramref name="armReach"/> of the left shoulder.
        /// </summary>
        public static Vector3 ReachableLeftGrip(Vector3 grip, Vector3 along, Vector3 leftShoulder, float armReach)
        {
            for (int i = 0; i < 6; i++)
            {
                float over = Vector3.Distance(grip, leftShoulder) - armReach * 0.95f;
                if (over <= 0.003f) break;
                grip -= along * over;
            }
            return grip;
        }

        /// <summary>
        /// Where a gun (or a bat, a tool) goes in somebody's hands seen from outside, so that its right grip lands on
        /// a natural hand spot: a long gun shouldered (the hand just in front of the right shoulder), a pistol held out
        /// in front at chest height, anything else carried low at the side, pointing where they look (pitch limited).
        /// The arms then reach the grips by IK. Shared with the editor's gun-hold check (ReviewCapture), which renders
        /// every gun this way and fails if a hand can't reach its grip.
        /// </summary>
        public static void ThirdPersonHold(Item item, Vector3 rightShoulder, Vector3 leftShoulder, float armReach, Vector3 lookForward,
                                           out Vector3 position, out Quaternion rotation, Avatars.AvatarRig rig = null)
        {
            float yaw = Mathf.Atan2(lookForward.x, lookForward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(lookForward.y, -1f, 1f)) * Mathf.Rad2Deg, -ThirdPersonMaxPitch, ThirdPersonMaxPitch);
            bool gun = item.TryGetComponent(out Combat.Weapon _);
            bool longGun = Shoulders(item);
            if (longGun) pitch = Mathf.Clamp(pitch, -LongGunMaxPitch, LongGunMaxPitch); // tipped further, the stock hits the (big) head
            Quaternion look = Quaternion.Euler(gun ? pitch : 0f, yaw, 0f);
            rotation = gun ? look : Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-60f, 0f, 0f);
            Transform t = item.transform;
            Vector3 gripLocal = Quaternion.Inverse(t.rotation) * (item.GripRight.position - t.position);
            if (longGun)
            {
                // Shouldered: the butt of the stock on the right shoulder and the gun pivoting there as they look up
                // and down, pulled back a little if the arm couldn't reach the grip, then slid forward until the
                // butt rests on the skin (not in it). The hands land wherever the grips are from there.
                Vector3 pocket = rightShoulder + Quaternion.Euler(0f, yaw, 0f) * StockPocket;
                position = pocket + rotation * new Vector3(0f, 0f, StockBack(item));
                Vector3 hand = position + rotation * gripLocal;
                float over = Vector3.Distance(hand, rightShoulder) - armReach * 0.9f;
                if (over > 0f) position -= rotation * Vector3.forward * over;
                position += rotation * Vector3.forward * ShoulderedStock.PushOut(item, rig, position, rotation);
                return;
            }
            Vector3 held = gun ? rightShoulder + look * new Vector3(-0.15f, -0.06f, 0.47f)        // pistol held out
                : rightShoulder + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.02f, -0.42f, 0.22f); // low at the side
            // The grip in the item's own frame, then the item placed so that grip sits in the hand.
            position = held - rotation * gripLocal;
            if (item.GripLeft == null) return;
            // Two hands: slide it back along the barrel until the left hand reaches its grip too (long guns' front
            // grips are far out), but never so far that the right hand is jammed into the shoulder.
            Vector3 leftLocal = Quaternion.Inverse(t.rotation) * (item.GripLeft.position - t.position);
            Vector3 along = rotation * Vector3.forward;
            for (int i = 0; i < 4; i++)
            {
                float over = Vector3.Distance(position + rotation * leftLocal, leftShoulder) - armReach * 0.92f;
                float room = Vector3.Dot(position + rotation * gripLocal - rightShoulder, along) - 0.1f;
                if (over <= 0.005f || room <= 0f) break;
                position -= along * Mathf.Min(over, room);
            }
            // A short gun's stock stays out of the body too.
            if (gun) position += along * ShoulderedStock.PushOut(item, rig, position, rotation);
        }

        /// <summary>Where a long gun's butt goes, from the right shoulder joint (right, up, forward; facing frame), before it's slid out onto the skin.</summary>
        public static readonly Vector3 StockPocket = new(0.035f, 0.08f, 0f);

        /// <summary>How far the back of the item (the end of a stock) is behind its origin, along its forward.</summary>
        public static float StockBack(Item item)
        {
            if (StockBacks.TryGetValue(item, out float known)) return known;
            Transform t = item.transform;
            float back = 0f;
            foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out Renderer shown) || !shown.enabled) continue;
                Bounds b = filter.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = new((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Vector3 local = Quaternion.Inverse(t.rotation) * (filter.transform.TransformPoint(corner) - t.position);
                    back = Mathf.Max(back, -local.z);
                }
            }
            StockBacks[item] = back;
            return back;
        }

        /// <summary>A two-handed gun (they all have a stock), held up in the shoulder seen from outside.</summary>
        public static bool Shoulders(Item item)
        {
            if (item == null || item.GripLeft == null || item.GripRight == null) return false;
            if (!LongGuns.TryGetValue(item, out bool shouldered))
                LongGuns[item] = shouldered = item.TryGetComponent(out Combat.Weapon _);
            return shouldered;
        }

        private static readonly System.Collections.Generic.Dictionary<Item, float> StockBacks = new();
        private static readonly System.Collections.Generic.Dictionary<Item, bool> LongGuns = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            StockBacks.Clear();
            LongGuns.Clear();
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
