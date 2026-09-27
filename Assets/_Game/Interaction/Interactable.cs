using System.Collections.Generic;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Interaction
{
    /// <summary>
    /// Implement on any component (networked or not) that reacts when a player uses an <see cref="Interactable"/>.
    /// </summary>
    public interface IInteractionHandler
    {
        bool CanInteract(PlayerHub player);
        string GetPrompt(PlayerHub player);
        /// <summary>Runs on the interacting player's machine. Networked handlers send a ServerRpc from here.</summary>
        void OnInteract(PlayerHub player);
    }

    /// <summary>
    /// The "look at it and press E" part of a world object: owns the colliders that can be targeted,
    /// the hover outline, and forwards the interaction to the <see cref="IInteractionHandler"/> on the same object.
    /// </summary>
    [DisallowMultipleComponent]
    public class Interactable : MonoBehaviour
    {
        [Tooltip("Colliders that count as looking at this object. Empty = all child colliders.")]
        [SerializeField] private Collider[] _colliders;
        [Tooltip("Renderers that get the hover outline. Empty = all child renderers.")]
        [SerializeField] private Renderer[] _outlineRenderers;
        [SerializeField] private float _maxDistance = 2.8f;

        private static readonly Dictionary<Collider, Interactable> ByCollider = new();
        private static int _outlineLayer = -1;

        private readonly Dictionary<Renderer, int> _originalLayers = new();
        private IInteractionHandler _handler;
        private bool _outlined;

        public float MaxDistance => _maxDistance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ByCollider.Clear();
            _outlineLayer = -1;
        }

        public static Interactable FromCollider(Collider col) =>
            col != null && ByCollider.TryGetValue(col, out Interactable i) ? i : null;

        private void Awake()
        {
            _handler = GetComponent<IInteractionHandler>();
            if (_handler == null)
                Debug.LogWarning($"[Interact] {name} has no IInteractionHandler.", this);
            if (_colliders == null || _colliders.Length == 0)
                _colliders = GetComponentsInChildren<Collider>(true);
            if (_outlineRenderers == null || _outlineRenderers.Length == 0)
                _outlineRenderers = GetComponentsInChildren<Renderer>(true);
            if (_outlineLayer < 0)
                _outlineLayer = LayerMask.NameToLayer("Outlined");
        }

        private void OnEnable()
        {
            foreach (Collider c in _colliders)
                if (c != null) ByCollider[c] = this;
        }

        private void OnDisable()
        {
            foreach (Collider c in _colliders)
                if (c != null) ByCollider.Remove(c);
            SetOutlined(false);
        }

        public bool CanInteract(PlayerHub player) => _handler != null && isActiveAndEnabled && _handler.CanInteract(player);

        public string GetPrompt(PlayerHub player) => _handler?.GetPrompt(player) ?? string.Empty;

        public void Interact(PlayerHub player)
        {
            if (CanInteract(player))
                _handler.OnInteract(player);
        }

        /// <summary>Moves the visuals onto the "Outlined" layer, which a URP RenderObjects pass draws with an outline shader.</summary>
        public void SetOutlined(bool outlined)
        {
            if (outlined == _outlined || _outlineLayer < 0)
                return;
            _outlined = outlined;
            foreach (Renderer r in _outlineRenderers)
            {
                if (r == null) continue;
                if (outlined)
                {
                    _originalLayers[r] = r.gameObject.layer;
                    r.gameObject.layer = _outlineLayer;
                }
                else if (_originalLayers.TryGetValue(r, out int layer))
                {
                    r.gameObject.layer = layer;
                }
            }
        }
    }
}
