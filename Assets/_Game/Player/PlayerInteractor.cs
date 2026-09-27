using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Looks straight out of the camera for an <see cref="Interactable"/>, outlines it, exposes the prompt for the HUD,
    /// and triggers it on the Interact input. Owner only.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private float _rayDistance = 4f;
        [SerializeField] private LayerMask _mask = ~0;

        private Interactable _current;

        public Interactable Current => _current;
        public string CurrentPrompt { get; private set; }

        private void Update()
        {
            Interactable found = null;
            Camera cam = _hub.Look.Camera;
            if (cam != null && GameInput.GameplayActive)
            {
                Transform view = cam.transform;
                // The ray starts inside our own capsule; Unity ignores colliders a ray begins inside of.
                if (Physics.Raycast(view.position, view.forward, out RaycastHit hit, _rayDistance, _mask, QueryTriggerInteraction.Ignore))
                {
                    Interactable candidate = Interactable.FromCollider(hit.collider);
                    if (candidate != null && hit.distance <= candidate.MaxDistance && candidate.CanInteract(_hub))
                        found = candidate;
                }
            }

            if (found != _current)
            {
                if (_current != null) _current.SetOutlined(false);
                _current = found;
                if (_current != null) _current.SetOutlined(true);
            }

            CurrentPrompt = _current != null ? _current.GetPrompt(_hub) : null;

            if (_current != null && GameInput.Interact.WasPressedThisFrame())
                _current.Interact(_hub);
        }

        private void OnDisable()
        {
            if (_current != null) _current.SetOutlined(false);
            _current = null;
            CurrentPrompt = null;
        }
    }
}
