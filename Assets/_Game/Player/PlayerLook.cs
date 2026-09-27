using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Mouse / stick look (yaw on the body, pitch on the head, which remote players see via the synced head),
    /// plus camera feel: sprint FOV kick, head bob and a landing dip. Owner only.
    /// </summary>
    [DefaultExecutionOrder(-10)] // rotate before PlayerMotor reads transform.forward
    public class PlayerLook : MonoBehaviour
    {
        private const string SensitivityKey = "pdd.look.sensitivity";
        private const string FovKey = "pdd.look.fov";

        [SerializeField] private Transform _head;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private float _stickDegreesPerSecond = 170f;
        [SerializeField] private float _sprintFovBoost = 7f;
        [SerializeField] private float _bobAmplitude = 0.035f;
        [SerializeField] private float _bobStrideLength = 1.1f;
        [SerializeField] private float _landDipPerSpeed = 0.012f;

        private Camera _camera;
        private float _pitch;
        private float _fovBoost;
        private float _bobPhase;
        private float _bobWeight;
        private float _dip;
        private float _dipVelocity;

        public Camera Camera => _camera;
        public float Sensitivity { get; private set; }
        public float BaseFov { get; private set; }

        public void Attach(Camera cam)
        {
            _camera = cam;
            _camera.fieldOfView = BaseFov;
        }

        private void Awake()
        {
            Sensitivity = PlayerPrefs.GetFloat(SensitivityKey, 0.1f);
            BaseFov = PlayerPrefs.GetFloat(FovKey, 80f);
        }

        private void OnEnable()
        {
            _motor.Landed += OnLanded;
            DevCommands.Register("sens", "<degrees per pixel>", "Mouse sensitivity (default 0.1).", args =>
            {
                Sensitivity = Mathf.Clamp(DevCommands.ParseFloat(args, 0), 0.01f, 1f);
                PlayerPrefs.SetFloat(SensitivityKey, Sensitivity);
                DevCommands.Print($"sensitivity = {Sensitivity}");
            }, owner: this);
            DevCommands.Register("fov", "<degrees>", "Field of view (default 80).", args =>
            {
                BaseFov = Mathf.Clamp(DevCommands.ParseFloat(args, 0), 60f, 110f);
                PlayerPrefs.SetFloat(FovKey, BaseFov);
                DevCommands.Print($"fov = {BaseFov}");
            }, owner: this);
            DevCommands.Register("lookat", "<x> <y> <z>", "Aim the camera at a world point.", args =>
            {
                var target = new Vector3(DevCommands.ParseFloat(args, 0), DevCommands.ParseFloat(args, 1), DevCommands.ParseFloat(args, 2));
                LookAt(target);
            }, cheat: true, owner: this);
        }

        public void LookAt(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - _head.position;
            var flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            _pitch = Mathf.Clamp(-Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg, -88f, 88f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void OnDisable()
        {
            _motor.Landed -= OnLanded;
            DevCommands.Unregister("sens", this);
            DevCommands.Unregister("fov", this);
            DevCommands.Unregister("lookat", this);
        }

        private void Update()
        {
            if (!GameInput.GameplayActive)
                return;

            Vector2 mouse = GameInput.LookMouse.ReadValue<Vector2>() * Sensitivity;
            Vector2 stick = GameInput.LookStick.ReadValue<Vector2>() * (_stickDegreesPerSecond * Time.deltaTime);
            Vector2 look = mouse + stick;

            transform.Rotate(0f, look.x, 0f, Space.Self);
            _pitch = Mathf.Clamp(_pitch - look.y, -88f, 88f);
            _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void LateUpdate()
        {
            if (_camera == null)
                return;
            float dt = Time.deltaTime;

            float targetBoost = _motor.IsSprinting && _motor.HorizontalSpeed > 1f ? _sprintFovBoost : 0f;
            _fovBoost = Mathf.Lerp(_fovBoost, targetBoost, 1f - Mathf.Exp(-8f * dt));
            _camera.fieldOfView = BaseFov + _fovBoost;

            // Bob advances with distance walked, so it matches footsteps at any speed.
            float moving = _motor.IsGrounded ? Mathf.Clamp01(_motor.HorizontalSpeed / 4f) : 0f;
            _bobWeight = Mathf.Lerp(_bobWeight, moving, 1f - Mathf.Exp(-10f * dt));
            _bobPhase += _motor.HorizontalSpeed * dt / _bobStrideLength * Mathf.PI;
            float bobY = Mathf.Abs(Mathf.Sin(_bobPhase)) * _bobAmplitude * _bobWeight;
            float bobX = Mathf.Sin(_bobPhase) * _bobAmplitude * 0.5f * _bobWeight;

            // Landing dip: a critically damped spring back to zero.
            _dip = Mathf.SmoothDamp(_dip, 0f, ref _dipVelocity, 0.12f);

            _camera.transform.localPosition = new Vector3(bobX, bobY - _dip, 0f);
        }

        private void OnLanded(float impactSpeed) => _dip = Mathf.Min(0.25f, impactSpeed * _landDipPerSpeed);
    }
}
