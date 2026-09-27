using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// M0 stand-in player: a capsule you can walk around with, synced by FishNet's NetworkTransform
    /// (client authoritative). Replaced by the real first-person player in M1.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class DebugCapsulePlayer : NetworkBehaviour
    {
        [SerializeField] private float _walkSpeed = 5f;
        [SerializeField] private float _jumpSpeed = 5f;
        [SerializeField] private float _lookSensitivity = 0.12f;
        [SerializeField] private float _eyeHeight = 0.7f;
        [SerializeField] private TextMesh _nameTag;
        [SerializeField] private Renderer _body;

        private readonly SyncVar<string> _displayName = new SyncVar<string>();

        private CharacterController _controller;
        private Transform _camera;
        private float _pitch;
        private float _verticalVelocity;

        public static DebugCapsulePlayer Local { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _displayName.OnChange += (_, next, asServer) =>
            {
                if (_nameTag != null) _nameTag.text = next;
                if (!asServer) Debug.Log($"[Player] owner {OwnerId} is now called '{next}'");
            };
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            Debug.Log($"[Player] spawned for owner {OwnerId} (mine: {IsOwner}) at {transform.position}");

            // Stable per-connection color so players can tell each other apart.
            if (_body != null)
                _body.material.color = Color.HSVToRGB(Mathf.Repeat(OwnerId * 0.2718f, 1f), 0.65f, 0.95f);

            if (!IsOwner)
                return;

            Local = this;
            SetNameServer(SteamBootstrap.LocalName);
            if (_nameTag != null)
                _nameTag.gameObject.SetActive(false);

            var cam = new GameObject("PlayerCamera") { tag = "MainCamera" };
            cam.transform.SetParent(transform, false);
            cam.transform.localPosition = new Vector3(0f, _eyeHeight, 0f);
            cam.AddComponent<Camera>().nearClipPlane = 0.05f;
            cam.AddComponent<AudioListener>();
            _camera = cam.transform;
            SceneCameras.SetMenuCameraActive(false);
            Cursor.lockState = CursorLockMode.Locked;
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (Local != this)
                return;
            Local = null;
            SceneCameras.SetMenuCameraActive(true);
            Cursor.lockState = CursorLockMode.None;
        }

        [ServerRpc]
        private void SetNameServer(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = $"Lifeguard {OwnerId}";
            _displayName.Value = displayName.Length > 24 ? displayName.Substring(0, 24) : displayName;
        }

        private void Update()
        {
            if (_nameTag != null && _nameTag.gameObject.activeSelf && Camera.main != null)
                _nameTag.transform.rotation = Camera.main.transform.rotation;

            if (!IsOwner || _camera == null)
                return;

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null)
                return;

            if (kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 look = mouse.delta.ReadValue() * _lookSensitivity;
                transform.Rotate(0f, look.x, 0f);
                _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);
                _camera.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }

            Vector2 move = Vector2.zero;
            if (kb.wKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed) move.y -= 1f;
            if (kb.dKey.isPressed) move.x += 1f;
            if (kb.aKey.isPressed) move.x -= 1f;
            Vector3 horizontal = (transform.right * move.x + transform.forward * move.y).normalized * _walkSpeed;

            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
                if (kb.spaceKey.wasPressedThisFrame)
                    _verticalVelocity = _jumpSpeed;
            }
            else
            {
                _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            }

            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);

            // Fell off the world during testing: pop back to spawn height.
            if (transform.position.y < -20f)
                _controller.Move(Vector3.up * 25f);
        }
    }
}
