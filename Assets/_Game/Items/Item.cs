using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>Optional on an item: decides how it sits in the hands (a tourist is carried and towed differently from a crate).</summary>
    public interface IHoldPose
    {
        /// <param name="pitchFollow">How much the pose follows looking up/down (1 = fully, 0 = only turning).</param>
        void GetHoldPose(PlayerHub holder, out Vector3 offset, out Quaternion rotation, out float pitchFollow);
    }

    /// <summary>How hands hold an item (first-person arms and remote bodies both use it).</summary>
    public enum ItemGrip : byte { Auto, OneHand, TwoHands, Person }

    /// <summary>
    /// Anything a player can pick up, carry and throw. The host owns "who is holding it" (SyncVar);
    /// the holder is also the physics owner (<see cref="ItemSync"/>), so a throw simulates on the thrower's machine.
    ///
    /// Pickups are predicted: the item jumps into your hands immediately and the host confirms or rejects.
    /// </summary>
    [RequireComponent(typeof(ItemSync))]
    public class Item : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private string _displayName = "Thing";
        [SerializeField] private string _pickUpVerb = "Pick up";
        [Tooltip("Where the item sits relative to the holder's head (x right, y up, z forward).")]
        [SerializeField] private Vector3 _holdOffset = new Vector3(0.3f, -0.32f, 0.75f);
        [SerializeField] private Vector3 _holdEuler;
        [Tooltip("Multiplier on throw speed (after mass scaling).")]
        [SerializeField] private float _throwStrength = 1f;
        [SerializeField] private float _pickupRange = 3.2f;
        [Tooltip("How heavy it feels to carry (slows the holder). 0 = the body's mass.")]
        [SerializeField] private float _carryMass;
        [Tooltip("Auto: one hand under small things, both hands on the sides of bigger ones.")]
        [SerializeField] private ItemGrip _grip;
        [Tooltip("Small enough to keep in an inventory slot. Big things must be carried in your hands.")]
        [SerializeField] private bool _pocketable;

        private readonly SyncVar<PlayerHub> _holder = new SyncVar<PlayerHub>();
        private readonly SyncVar<byte> _slot = new SyncVar<byte>();

        private static readonly List<Item> _all = new();
        private static readonly Dictionary<Collider, Item> _byCollider = new();

        private ItemSync _sync;
        private IHoldPose _holdPose;
        private Collider[] _colliders;       // everything, including parts with their own bodies (ragdoll limbs)
        private Collider[] _bodyColliders;   // only the ones on the main body (switched off while someone else holds it)
        private PlayerHub _predictedHolder;
        private int _predictedSlot = -1;
        private bool _releasePending;
        private Renderer[] _renderers;
        private bool _hidden;

        public static IReadOnlyList<Item> All => _all;
        public string DisplayName => _displayName;
        public ItemSync Sync => _sync;
        public Vector3 HoldOffset => _holdOffset;
        public Quaternion HoldRotation => Quaternion.Euler(_holdEuler);
        public float ThrowStrength => _throwStrength;
        public float Mass => _sync.Body.mass;
        public float CarryMass => _carryMass > 0f ? _carryMass : Mass;
        public ItemGrip Grip => _grip;
        public bool Pocketable => _pocketable;

        /// <summary>Inventory slot of the holder this item is in (our prediction first).</summary>
        public int Slot => _predictedHolder != null && _predictedSlot >= 0 ? _predictedSlot : _slot.Value;

        /// <summary>Held, but in a slot that isn't selected: hidden in the holder's pockets.</summary>
        public bool IsStowed
        {
            get
            {
                PlayerHub h = Holder;
                return h != null && h.Hands != null && h.Hands.ActiveSlot != Slot;
            }
        }

        /// <summary>World box around what you see of the item (for where hands go).</summary>
        public Bounds VisualBounds
        {
            get
            {
                var bounds = new Bounds(transform.position, Vector3.zero);
                bool first = true;
                foreach (Renderer r in _renderers)
                {
                    if (r == null || !r.enabled || r is SkinnedMeshRenderer) continue;
                    if (first) { bounds = r.bounds; first = false; }
                    else bounds.Encapsulate(r.bounds);
                }
                return bounds;
            }
        }

        /// <summary>The item a collider belongs to (also finds ragdoll limbs), or null.</summary>
        public static Item FromCollider(Collider c) => c != null && _byCollider.TryGetValue(c, out Item item) ? item : null;

        public bool OwnsCollider(Collider c) => c != null && _byCollider.TryGetValue(c, out Item item) && item == this;

        /// <summary>Per-instance name (e.g. a tourist's), set on every machine by whoever knows it.</summary>
        public void SetDisplayName(string displayName) => _displayName = displayName;

        public void GetHoldPose(PlayerHub holder, out Vector3 offset, out Quaternion rotation, out float pitchFollow)
        {
            if (_holdPose != null)
            {
                _holdPose.GetHoldPose(holder, out offset, out rotation, out pitchFollow);
                return;
            }
            offset = _holdOffset;
            rotation = HoldRotation;
            pitchFollow = 1f;
        }

        /// <summary>Holder as seen on this machine: our own prediction wins until the host answers.</summary>
        public PlayerHub Holder => _releasePending ? null : _predictedHolder != null ? _predictedHolder : _holder.Value;
        public bool IsHeld => Holder != null;

        /// <summary>The host agreed this player holds it and we own its physics, so it can be thrown.</summary>
        public bool IsConfirmedHolder(PlayerHub player) => !_releasePending && _holder.Value == player && IsOwner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            _byCollider.Clear();
            DevCommands.Register("items", "", "List items, who simulates them and who holds them.", _ =>
            {
                foreach (Item i in _all)
                    DevCommands.Print($"  {i.DisplayName,-10} at {i.transform.position:F2}  sim: {i.Sync.AuthorityLabel}" +
                                      (i.IsHeld ? $"  held by {i.Holder.DisplayName}" : "") +
                                      $"  speed {i.Sync.Body.linearVelocity.magnitude:F2}");
            });
        }

        private void Awake()
        {
            _sync = GetComponent<ItemSync>();
            _holdPose = GetComponent<IHoldPose>();
            _colliders = GetComponentsInChildren<Collider>(true);
            _renderers = GetComponentsInChildren<Renderer>(true);
            _bodyColliders = System.Array.FindAll(_colliders, c => c.attachedRigidbody == _sync.Body);
            foreach (Collider c in _colliders)
                _byCollider[c] = this;
            _holder.OnChange += OnHolderChanged;
            _slot.OnChange += OnSlotChanged;
        }

        private void OnDestroy()
        {
            foreach (Collider c in _colliders)
                if (c != null && _byCollider.TryGetValue(c, out Item owner) && owner == this)
                    _byCollider.Remove(c);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _all.Add(this);
            ApplyHeldState();
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _all.Remove(this);
            PlayerHub holder = Holder;
            if (holder != null && holder.Hands != null)
                holder.Hands.OnItemLost(this);
        }

        // ------------------------------------------------------------------ interaction

        public bool CanInteract(PlayerHub player) => !IsHeld && player.Hands != null;

        public string GetPrompt(PlayerHub player)
        {
            Item current = player.Hands != null ? player.Hands.HeldItem : null;
            bool swap = current != null && (!current.Pocketable || player.Hands.FirstFreeSlot() < 0);
            return swap ? $"Swap for {_displayName}" : $"{_pickUpVerb} {_displayName}";
        }

        public void OnInteract(PlayerHub player) => player.Hands.TryPickUp(this);

        // ------------------------------------------------------------------ pick up

        /// <summary>Called by the local player's hands. Shows the item in hand right away and asks the host.</summary>
        public void RequestPickUp(PlayerHub player, int slot)
        {
            _predictedHolder = player;
            _predictedSlot = slot;
            _releasePending = false;
            ApplyHeldState();
            if (player.Hands != null) player.Hands.OnItemGained(this);
            PickUpServer(player, (byte)slot);
        }

        [ServerRpc(RequireOwnership = false)]
        private void PickUpServer(PlayerHub player, byte slot, NetworkConnection caller = null)
        {
            bool valid = player != null && player.Owner == caller && _holder.Value == null && slot < PlayerHands.SlotCount &&
                         (player.transform.position - transform.position).sqrMagnitude < (_pickupRange + 2f) * (_pickupRange + 2f);
            if (!valid)
            {
                PickUpRejected(caller);
                return;
            }

            // One item per slot: whatever they still had in that slot on the host falls to the ground.
            foreach (Item other in _all.ToArray())
                if (other != this && other._holder.Value == player && other._slot.Value == slot)
                    other.ServerForceDrop();

            _slot.Value = slot;
            _holder.Value = player;
            GiveOwnership(caller);
            Debug.Log($"[Item] {_displayName} picked up by {player.DisplayName}");
        }

        [TargetRpc]
        private void PickUpRejected(NetworkConnection target)
        {
            PlayerHub predicted = _predictedHolder;
            _predictedHolder = null;
            _predictedSlot = -1;
            if (predicted != null && predicted.Hands != null)
                predicted.Hands.OnItemLost(this);
            ApplyHeldState();
            Debug.Log($"[Item] pickup of {_displayName} rejected");
        }

        // ------------------------------------------------------------------ release / throw

        /// <summary>
        /// Called by the holder's hands (local owner). The item is already a live body in our hands, so the throw
        /// starts from exactly where it is; the host is told.
        /// </summary>
        public void Release(PlayerHub holder, Vector3 velocity, Vector3 angularVelocity)
        {
            _predictedHolder = null;
            _predictedSlot = -1;
            _releasePending = true;
            ApplyHeldState();

            Rigidbody body = _sync.Body;
            if (!body.isKinematic)
            {
                body.linearVelocity = velocity;
                body.angularVelocity = angularVelocity;
            }
            StartCoroutine(IgnoreHolderBriefly(holder));
            ReleaseServer(body.position, body.rotation, velocity);
        }

        [ServerRpc]
        private void ReleaseServer(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            _holder.Value = null;
            _sync.ServerSeedFollow(position, rotation, velocity); // no-op if the host itself threw it
            Debug.Log($"[Item] {_displayName} released at {velocity.magnitude:F1} m/s");
        }

        /// <summary>Host-side drop without the holder's cooperation (holder left, picked something else up...).</summary>
        [Server]
        public void ServerForceDrop()
        {
            if (_holder.Value == null) return;
            _holder.Value = null;
            if (Owner.IsValid) RemoveOwnership();
        }

        /// <summary>Host only: drops whatever this player was holding.</summary>
        public static void ServerDropAllHeldBy(PlayerHub player)
        {
            foreach (Item item in _all.ToArray())
                if (item._holder.Value == player)
                    item.ServerForceDrop();
        }

        /// <summary>Just-released items pass through the thrower for a moment so they don't bounce off our own body.</summary>
        private IEnumerator IgnoreHolderBriefly(PlayerHub holder)
        {
            Collider body = holder != null ? holder.BodyCollider : null;
            if (body == null) yield break;
            SetIgnoreCollision(body, true);
            yield return new WaitForSeconds(0.35f);
            if (body != null && Holder != holder) SetIgnoreCollision(body, false);
        }

        private void SetIgnoreCollision(Collider other, bool ignore)
        {
            foreach (Collider c in _colliders)
                if (c != null && other != null) Physics.IgnoreCollision(c, other, ignore);
        }

        // ------------------------------------------------------------------ state

        private void OnSlotChanged(byte prev, byte next, bool asServer)
        {
            PlayerHub holder = Holder;
            if (holder != null && holder.Hands != null) holder.Hands.OnItemGained(this);
            ApplyHeldState();
        }

        /// <summary>The holder switched slots: move between hands and pockets.</summary>
        internal void RefreshHeldState() => ApplyHeldState();

        private void OnHolderChanged(PlayerHub prev, PlayerHub next, bool asServer)
        {
            PlayerHub shownBefore = _predictedHolder != null ? _predictedHolder : prev;
            if (next == null) _releasePending = false;
            if (next != null)
            {
                _predictedHolder = null; // confirmed, or someone else won the race
                _predictedSlot = -1;
            }

            PlayerHub now = Holder;
            if (shownBefore != null && shownBefore != now && shownBefore.Hands != null) shownBefore.Hands.OnItemLost(this);
            if (prev != null && prev != now && prev != shownBefore && prev.Hands != null) prev.Hands.OnItemLost(this);
            if (now != null && now.Hands != null) now.Hands.OnItemGained(this);
            ApplyHeldState();
        }

        private void ApplyHeldState()
        {
            PlayerHub holder = Holder;
            bool held = holder != null;
            bool stowed = held && IsStowed;
            // Our own hands steer a live body (it collides with the world, but not with us);
            // everyone else sees it glued to the holder with its colliders off (dangling limbs keep theirs).
            // Pocketed items are hidden and ride along with the holder.
            bool heldLocally = held && !stowed && holder == PlayerHub.Local;
            foreach (Collider c in _bodyColliders)
                if (c != null) c.enabled = !held || heldLocally;
            if (heldLocally && holder.BodyCollider != null)
                SetIgnoreCollision(holder.BodyCollider, true);
            SetHidden(stowed);
            _sync.SetHeld(held, heldLocally);
        }

        private void SetHidden(bool hidden)
        {
            if (hidden == _hidden) return;
            _hidden = hidden;
            foreach (Renderer r in _renderers)
                if (r != null) r.forceRenderingOff = hidden;
        }

        /// <summary>Remote holders' hands glue the item to their head every frame.</summary>
        public void PlaceInHand(Vector3 position, Quaternion rotation) => transform.SetPositionAndRotation(position, rotation);
    }
}
