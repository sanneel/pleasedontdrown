using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Finds what the player would use with Interact, outlines it and triggers it. Owner only.
    ///  1. Whatever is exactly under the crosshair wins (buttons, bells, signs, items).
    ///  2. Otherwise a capsule around the view line collects nearby interactables that are in plain sight,
    ///     and the one closest to the crosshair is chosen, so small, moving or bobbing items are easy to grab.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private float _rayDistance = 3.4f;
        [SerializeField] private float _grabRange = 2f;     // How to Fish: items within 2 m, a 0.5 m wide reach
        [SerializeField] private float _grabRadius = 0.5f;
        [Tooltip("Candidates further than this from the crosshair (degrees) are ignored.")]
        [SerializeField] private float _maxAngle = 14f;
        [SerializeField] private LayerMask _mask = ~0;

        private readonly Collider[] _overlaps = new Collider[32];
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private Interactable _current;

        public Interactable Current => _current;
        public string CurrentPrompt { get; private set; }
        /// <summary>Second action on the target (Secondary button), only while the hands are free.</summary>
        public string CurrentSecondaryPrompt { get; private set; }
        /// <summary>The button the second action is on right now.</summary>
        public UnityEngine.InputSystem.InputAction CurrentSecondaryAction { get; private set; }

        private void Update()
        {
            Interactable found = null;
            Camera cam = _hub.Look.Camera;
            if (cam != null && GameInput.GameplayActive && _hub.Motor.Seat == null) // driving: E only gets you off
                found = FindTarget(cam.transform);

            if (found != _current)
            {
                if (_current != null) _current.SetOutlined(false);
                _current = found;
                if (_current != null) _current.SetOutlined(true);
            }

            CurrentPrompt = _current != null ? _current.GetPrompt(_hub) : null;
            bool handsFree = _hub.Hands == null || _hub.Hands.HeldItem == null;
            CurrentSecondaryPrompt = _current != null && handsFree ? _current.GetSecondaryPrompt(_hub) : null;
            CurrentSecondaryAction = CurrentSecondaryPrompt != null ? _current.SecondaryAction(_hub) : GameInput.Secondary;

            if (_current != null && GameInput.Interact.WasPressedThisFrame())
            {
                _hub.Gesture(Avatars.AvatarGesture.Interact, TargetPoint(cam));
                _current.Interact(_hub);
            }
            else if (CurrentSecondaryPrompt != null && CurrentSecondaryAction.WasPressedThisFrame())
                _current.InteractSecondary(_hub);
        }

        /// <summary>Where on the target the hand reaches (closest point of its collider to the crosshair ray).</summary>
        private Vector3 TargetPoint(Camera cam)
        {
            if (_current == null || cam == null) return Vector3.zero;
            Transform view = cam.transform;
            if (FirstHit(view.position, view.forward, _rayDistance, out RaycastHit hit) && Interactable.FromCollider(hit.collider) == _current)
                return hit.point;
            Collider c = _current.GetComponentInChildren<Collider>();
            return c != null ? c.ClosestPoint(view.position + view.forward * 1.2f) : _current.transform.position;
        }

        private Interactable FindTarget(Transform view)
        {
            Vector3 origin = view.position;
            Vector3 forward = view.forward;

            // 1. Exactly under the crosshair.
            if (FirstHit(origin, forward, _rayDistance, out RaycastHit hit))
            {
                Interactable direct = Interactable.FromCollider(hit.collider);
                if (Usable(direct, hit.distance))
                    return direct;
            }

            // 2. Near the crosshair and visible.
            int count = Physics.OverlapCapsuleNonAlloc(origin, origin + forward * _grabRange, _grabRadius, _overlaps, _mask, QueryTriggerInteraction.Ignore);
            Interactable best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Interactable candidate = Interactable.FromCollider(_overlaps[i]);
                if (candidate == null || candidate == best) continue;

                Vector3 center = _overlaps[i].bounds.center;
                Vector3 toCenter = center - origin;
                float distance = toCenter.magnitude;
                float angle = Vector3.Angle(forward, toCenter);
                if (angle > _maxAngle || !Usable(candidate, distance) || !InPlainSight(origin, center, candidate))
                    continue;

                float score = angle + distance * 2f; // mostly "closest to the crosshair", a little "closest to me"
                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            return best;
        }

        private bool Usable(Interactable candidate, float distance) =>
            candidate != null && distance <= candidate.MaxDistance && (candidate.CanInteract(_hub) || candidate.CanSecondary(_hub));

        /// <summary>Nothing solid between the eye and the target (other than the target itself).</summary>
        private bool InPlainSight(Vector3 origin, Vector3 point, Interactable target)
        {
            Vector3 dir = point - origin;
            float distance = dir.magnitude;
            if (!FirstHit(origin, dir / distance, distance - 0.05f, out RaycastHit blocker))
                return true;
            return Interactable.FromCollider(blocker.collider) == target;
        }

        /// <summary>Closest hit that isn't our own body or the item in our hands.</summary>
        private bool FirstHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, _hits, distance, _mask, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (IsOurs(h.collider) || h.distance >= bestDistance) continue;
                bestDistance = h.distance;
                best = h;
            }
            return bestDistance < float.MaxValue;
        }

        private bool IsOurs(Collider c)
        {
            if (c == _hub.BodyCollider) return true;
            var held = _hub.Hands != null ? _hub.Hands.HeldItem : null;
            return held != null && held.OwnsCollider(c);
        }

        private void OnEnable()
        {
            DevCommands.Register("use", "", "Press Interact on whatever is targeted (automated tests).", _ =>
            {
                DevCommands.Print(_current != null ? $"using {_current.name}: {CurrentPrompt}" : "nothing targeted");
                if (_current != null) _current.Interact(_hub);
            }, cheat: true, owner: this);
            DevCommands.Register("use2", "", "Press Secondary on whatever is targeted (automated tests).", _ =>
            {
                DevCommands.Print(_current != null ? $"secondary on {_current.name}: {CurrentSecondaryPrompt ?? "(not available)"}" : "nothing targeted");
                if (_current != null) _current.InteractSecondary(_hub);
            }, cheat: true, owner: this);
            DevCommands.Register("targetdebug", "", "Explain what the interaction targeting sees right now.", _ => ExplainTargeting(), owner: this);
        }

        private void ExplainTargeting()
        {
            Camera cam = _hub.Look != null ? _hub.Look.Camera : null;
            DevCommands.Print($"camera {(cam != null ? cam.transform.position.ToString("F2") : "NONE")}, gameplay {GameInput.GameplayActive}, current {(_current != null ? _current.name : "none")}");
            if (cam == null) return;
            Vector3 origin = cam.transform.position, forward = cam.transform.forward;
            int raw = Physics.RaycastNonAlloc(origin, forward, _hits, 10f, ~0, QueryTriggerInteraction.Collide);
            DevCommands.Print($"  forward {forward:F2}, head fwd {_hub.Head.forward:F2}, mask {_mask.value}, raw hits within 10 m: {raw}" +
                              (raw > 0 ? $" (first: {_hits[0].collider.name} {_hits[0].distance:F2})" : ""));
            if (FirstHit(origin, forward, _rayDistance, out RaycastHit hit))
                DevCommands.Print($"  ray hits {hit.collider.name} at {hit.distance:F2} m (interactable: {Interactable.FromCollider(hit.collider)?.name ?? "no"})");
            else
                DevCommands.Print("  ray hits nothing");
            int count = Physics.OverlapCapsuleNonAlloc(origin, origin + forward * _grabRange, _grabRadius, _overlaps, _mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Interactable c = Interactable.FromCollider(_overlaps[i]);
                if (c == null) { DevCommands.Print($"  overlap {_overlaps[i].name}: not interactable"); continue; }
                Vector3 center = _overlaps[i].bounds.center;
                float distance = (center - origin).magnitude;
                DevCommands.Print($"  overlap {_overlaps[i].name} -> {c.name}: angle {Vector3.Angle(forward, center - origin):F1}, dist {distance:F2}/{c.MaxDistance:F1}, " +
                                  $"canUse {c.CanInteract(_hub)}, visible {InPlainSight(origin, center, c)}");
            }
        }

        private void OnDisable()
        {
            DevCommands.Unregister("use", this);
            DevCommands.Unregister("use2", this);
            DevCommands.Unregister("targetdebug", this);
            if (_current != null) _current.SetOutlined(false);
            _current = null;
            CurrentPrompt = null;
            CurrentSecondaryPrompt = null;
        }
    }
}
