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
        [Tooltip("Where the item sits relative to the holder's head (x right, y up, z forward).")]
        [SerializeField] private Vector3 _holdOffset = new Vector3(0.3f, -0.32f, 0.75f);
        [SerializeField] private Vector3 _holdEuler;
        [Tooltip("Multiplier on throw speed (after mass scaling).")]
        [SerializeField] private float _throwStrength = 1f;
        [SerializeField] private float _pickupRange = 3.2f;

        private readonly SyncVar<PlayerHub> _holder = new SyncVar<PlayerHub>();

        private static readonly List<Item> _all = new();

        private ItemSync _sync;
        private Collider[] _colliders;
        private PlayerHub _predictedHolder;
        private bool _releasePending;

        public static IReadOnlyList<Item> All => _all;
        public string DisplayName => _displayName;
        public ItemSync Sync => _sync;
        public Vector3 HoldOffset => _holdOffset;
        public Quaternion HoldRotation => Quaternion.Euler(_holdEuler);
        public float ThrowStrength => _throwStrength;
        public float Mass => _sync.Body.mass;

        /// <summary>Holder as seen on this machine: our own prediction wins until the host answers.</summary>
        public PlayerHub Holder => _releasePending ? null : _predictedHolder != null ? _predictedHolder : _holder.Value;
        public bool IsHeld => Holder != null;

        /// <summary>The host agreed this player holds it and we own its physics, so it can be thrown.</summary>
        public bool IsConfirmedHolder(PlayerHub player) => !_releasePending && _holder.Value == player && IsOwner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
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
            _colliders = GetComponentsInChildren<Collider>(true);
            _holder.OnChange += OnHolderChanged;
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

        public string GetPrompt(PlayerHub player) =>
            player.Hands != null && player.Hands.HeldItem != null ? $"Swap for {_displayName}" : $"Pick up {_displayName}";

        public void OnInteract(PlayerHub player) => player.Hands.TryPickUp(this);

        // ------------------------------------------------------------------ pick up

        /// <summary>Called by the local player's hands. Shows the item in hand right away and asks the host.</summary>
        public void RequestPickUp(PlayerHub player)
        {
            _predictedHolder = player;
            _releasePending = false;
            ApplyHeldState();
            PickUpServer(player);
        }

        [ServerRpc(RequireOwnership = false)]
        private void PickUpServer(PlayerHub player, NetworkConnection caller = null)
        {
            bool valid = player != null && player.Owner == caller && _holder.Value == null &&
                         (player.transform.position - transform.position).sqrMagnitude < (_pickupRange + 2f) * (_pickupRange + 2f);
            if (!valid)
            {
                PickUpRejected(caller);
                return;
            }

            // One item per player: anything else they held on the host falls to the ground.
            foreach (Item other in _all.ToArray())
                if (other != this && other._holder.Value == player)
                    other.ServerForceDrop();

            _holder.Value = player;
            GiveOwnership(caller);
            Debug.Log($"[Item] {_displayName} picked up by {player.DisplayName}");
        }

        [TargetRpc]
        private void PickUpRejected(NetworkConnection target)
        {
            PlayerHub predicted = _predictedHolder;
            _predictedHolder = null;
            if (predicted != null && predicted.Hands != null)
                predicted.Hands.OnItemLost(this);
            ApplyHeldState();
            Debug.Log($"[Item] pickup of {_displayName} rejected");
        }

        // ------------------------------------------------------------------ release / throw

        /// <summary>Called by the holder's hands (local owner). Physics starts here immediately; the host is told.</summary>
        public void Release(PlayerHub holder, Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            _predictedHolder = null;
            _releasePending = true;
            ApplyHeldState();

            Rigidbody body = _sync.Body;
            transform.SetPositionAndRotation(position, rotation);
            body.position = position;
            body.rotation = rotation;
            if (!body.isKinematic)
            {
                body.linearVelocity = velocity;
                body.angularVelocity = angularVelocity;
            }
            StartCoroutine(IgnoreHolderBriefly(holder));
            ReleaseServer(position, rotation, velocity);
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

        private IEnumerator IgnoreHolderBriefly(PlayerHub holder)
        {
            var controller = holder != null ? holder.GetComponent<CharacterController>() : null;
            if (controller == null) yield break;
            foreach (Collider c in _colliders) Physics.IgnoreCollision(c, controller, true);
            yield return new WaitForSeconds(0.35f);
            if (controller == null) yield break;
            foreach (Collider c in _colliders)
                if (c != null) Physics.IgnoreCollision(c, controller, false);
        }

        // ------------------------------------------------------------------ state

        private void OnHolderChanged(PlayerHub prev, PlayerHub next, bool asServer)
        {
            PlayerHub shownBefore = _predictedHolder != null ? _predictedHolder : prev;
            if (next == null) _releasePending = false;
            if (next != null) _predictedHolder = null; // confirmed, or someone else won the race

            PlayerHub now = Holder;
            if (shownBefore != null && shownBefore != now && shownBefore.Hands != null) shownBefore.Hands.OnItemLost(this);
            if (prev != null && prev != now && prev != shownBefore && prev.Hands != null) prev.Hands.OnItemLost(this);
            if (now != null && now.Hands != null) now.Hands.OnItemGained(this);
            ApplyHeldState();
        }

        private void ApplyHeldState()
        {
            bool held = IsHeld;
            foreach (Collider c in _colliders)
                if (c != null) c.enabled = !held;
            _sync.SetHeld(held);
        }

        /// <summary>Hands call this every frame while holding (on every machine).</summary>
        public void PlaceInHand(Vector3 position, Quaternion rotation) => transform.SetPositionAndRotation(position, rotation);
    }
}
