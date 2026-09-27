using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Carrying and throwing. Runs on every machine (it glues the held item to the holder's head),
    /// but only the owner reads input: hold Primary to charge a throw, release to throw, Drop to let go.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private Transform _head;
        [SerializeField] private float _minThrowSpeed = 3f;
        [SerializeField] private float _maxThrowSpeed = 15f;
        [SerializeField] private float _chargeTime = 0.7f;
        [SerializeField] private float _holdSmoothing = 30f;

        private float _chargeStart;

        public Item HeldItem { get; private set; }
        public bool IsCharging { get; private set; }
        public float Charge01 { get; private set; }

        private bool IsLocal => _hub.IsOwner;

        // ------------------------------------------------------------------ called by Item

        internal void OnItemGained(Item item)
        {
            if (HeldItem == item) return;
            HeldItem = item;
            IsCharging = false;
        }

        internal void OnItemLost(Item item)
        {
            if (HeldItem != item) return;
            HeldItem = null;
            IsCharging = false;
        }

        // ------------------------------------------------------------------ actions (local player)

        public void TryPickUp(Item item)
        {
            if (!IsLocal || item == null || item == HeldItem || item.IsHeld)
                return;
            if (HeldItem != null)
                Drop();
            HeldItem = item;
            item.RequestPickUp(_hub);
        }

        /// <summary>Throw with a charge from 0 (lob) to 1 (full power). Ignored until the host confirms the pickup.</summary>
        public void Throw(float charge)
        {
            Item item = HeldItem;
            if (item == null || !item.IsConfirmedHolder(_hub)) return;

            // Light things fly far, heavy things plop. Clamp so a beach ball doesn't become a missile.
            float massScale = Mathf.Clamp(Mathf.Sqrt(3f / Mathf.Max(0.1f, item.Mass)), 0.35f, 1.3f);
            float speed = Mathf.Lerp(_minThrowSpeed, _maxThrowSpeed, Mathf.Clamp01(charge)) * massScale * item.ThrowStrength;
            Transform aim = AimTransform;
            Vector3 direction = (aim.forward + Vector3.up * 0.08f).normalized;
            Vector3 spin = Random.insideUnitSphere * (2f + 6f * charge);
            ReleaseHeld(direction * speed + _hub.Motor.Velocity, spin);
        }

        public void Drop()
        {
            Item item = HeldItem;
            if (item == null || !item.IsConfirmedHolder(_hub)) return;
            ReleaseHeld(_hub.Motor.Velocity + AimTransform.forward * 0.8f, Vector3.zero);
        }

        private void ReleaseHeld(Vector3 velocity, Vector3 angularVelocity)
        {
            Item item = HeldItem;
            HeldItem = null;
            IsCharging = false;
            GetHoldPose(item, out Vector3 position, out Quaternion rotation);
            item.Release(_hub, position, rotation, velocity, angularVelocity);
        }

        private Transform AimTransform => _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _head;

        // ------------------------------------------------------------------ frame updates

        private void OnEnable()
        {
            if (!IsLocal) return;
            DevCommands.Register("grab", "", "Pick up the item you look at (or the nearest one).", _ => GrabCommand(), owner: this);
            DevCommands.Register("throw", "[charge 0..1]", "Throw the held item.", args => Throw(args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 1f), owner: this);
            DevCommands.Register("drop", "", "Drop the held item.", _ => Drop(), owner: this);
        }

        private void OnDisable()
        {
            DevCommands.Unregister("grab", this);
            DevCommands.Unregister("throw", this);
            DevCommands.Unregister("drop", this);
        }

        /// <summary>The hub enables hands after deciding local/remote, so commands register only for the local player.</summary>
        internal void RefreshLocal()
        {
            OnDisable();
            OnEnable();
        }

        private void Update()
        {
            if (!IsLocal || HeldItem == null || !GameInput.GameplayActive)
            {
                IsCharging = false;
                return;
            }

            if (GameInput.Primary.WasPressedThisFrame())
            {
                IsCharging = true;
                _chargeStart = Time.time;
            }
            if (IsCharging)
            {
                Charge01 = Mathf.Clamp01((Time.time - _chargeStart) / _chargeTime);
                if (GameInput.Primary.WasReleasedThisFrame())
                    Throw(Charge01);
            }
            if (GameInput.Drop.WasPressedThisFrame())
                Drop();
        }

        private void LateUpdate()
        {
            Item item = HeldItem;
            if (item == null) return;
            GetHoldPose(item, out Vector3 target, out Quaternion rotation);
            // A little lag gives held things weight; snap if we fell far behind (teleports, spawns).
            float k = 1f - Mathf.Exp(-_holdSmoothing * Time.deltaTime);
            Vector3 current = item.transform.position;
            Vector3 position = (current - target).sqrMagnitude > 4f ? target : Vector3.Lerp(current, target, k);
            item.PlaceInHand(position, Quaternion.Slerp(item.transform.rotation, rotation, k));
        }

        private void GetHoldPose(Item item, out Vector3 position, out Quaternion rotation)
        {
            position = _head.TransformPoint(item.HoldOffset);
            rotation = _head.rotation * item.HoldRotation;
        }

        private void GrabCommand()
        {
            Item target = null;
            if (_hub.Interactor != null && _hub.Interactor.Current != null)
                target = _hub.Interactor.Current.GetComponent<Item>();
            if (target == null)
            {
                float best = 3.5f * 3.5f;
                foreach (Item i in Item.All)
                {
                    float d = (i.transform.position - transform.position).sqrMagnitude;
                    if (!i.IsHeld && d < best) { best = d; target = i; }
                }
            }
            if (target == null) DevCommands.Print("nothing to grab within 3.5 m");
            else TryPickUp(target);
        }
    }
}
