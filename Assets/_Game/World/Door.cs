using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// A hinged door (Interact to open/close). It swings away from whoever opens it, so it never shoves you
    /// back out. Open/closed is host state; the opener sees it move immediately.
    /// </summary>
    public class Door : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _hinge;
        [SerializeField] private float _openAngle = 100f;
        [SerializeField] private float _swingSpeed = 6f;
        [SerializeField] private AudioSource _audio;

        // 0 closed, +1 / -1 open to either side.
        private readonly SyncVar<sbyte> _state = new SyncVar<sbyte>();
        private sbyte _predicted;
        private float _predictedUntil;
        private float _angle;
        private Quaternion _rest;
        private Vector3 _leafLocal = Vector3.left;

        public bool IsOpen => State != 0;
        private sbyte State => Time.time < _predictedUntil ? _predicted : _state.Value;

        private void Awake()
        {
            _rest = _hinge.localRotation;
            // Which way the leaf sticks out from the hinge (its middle, in hinge space).
            Renderer leaf = _hinge.GetComponentInChildren<Renderer>();
            if (leaf != null)
            {
                Vector3 local = _hinge.InverseTransformPoint(leaf.bounds.center);
                local.y = 0f;
                if (local.sqrMagnitude > 1e-4f) _leafLocal = local.normalized;
            }
            _state.OnChange += OnStateChanged;
        }

        public bool CanInteract(PlayerHub player) => true;

        public string GetPrompt(PlayerHub player) => IsOpen ? "Close door" : "Open door";

        public void OnInteract(PlayerHub player)
        {
            sbyte next = IsOpen ? (sbyte)0 : SideAwayFrom(player != null ? player.transform.position : transform.position);
            _predicted = next;
            _predictedUntil = Time.time + 0.6f;
            SetServer(next);
        }

        /// <summary>+1 or -1: the swing that moves the door away from someone standing at <paramref name="position"/>.</summary>
        private sbyte SideAwayFrom(Vector3 position)
        {
            Quaternion closed = (_hinge.parent != null ? _hinge.parent.rotation : Quaternion.identity) * _rest;
            Vector3 leaf = Vector3.ProjectOnPlane(closed * _leafLocal, Vector3.up);
            Vector3 swing = Vector3.Cross(Vector3.up, leaf); // where a positive angle moves the door's free edge
            return Vector3.Dot(swing, position - _hinge.position) > 0f ? (sbyte)-1 : (sbyte)1;
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetServer(sbyte state) => _state.Value = (sbyte)Mathf.Clamp(state, -1, 1);

        private void OnStateChanged(sbyte prev, sbyte next, bool asServer)
        {
            if (!asServer || !IsClientStarted) PlaySound(next != 0); // once per machine
        }

        private void PlaySound(bool open)
        {
            if (_audio != null) _audio.PlayOneShot(open ? ProceduralAudio.Creak : ProceduralAudio.Shut, 0.8f);
        }

        private void Update()
        {
            float target = State * _openAngle;
            float before = _angle;
            _angle = Mathf.Lerp(_angle, target, 1f - Mathf.Exp(-_swingSpeed * Time.deltaTime));
            if (Mathf.Abs(_angle - before) > 1e-4f || Mathf.Abs(_angle - target) > 0.01f)
                _hinge.localRotation = _rest * Quaternion.Euler(0f, _angle, 0f);
        }
    }
}
