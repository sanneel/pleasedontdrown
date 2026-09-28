using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Mouse / stick look. Yaw and pitch both live on the head (which is network-synced, so others see where you
    /// look); the physics body never rotates. Also owns camera feel: sprint FOV kick, strafe roll, a stepped
    /// head bob and a landing dip. Owner only.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class PlayerLook : MonoBehaviour
    {
        private const string SensitivityKey = "pdd.look.sensitivity";
        private const string FovKey = "pdd.look.fov";

        [SerializeField] private Transform _head;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private float _stickDegreesPerSecond = 180f;

        [Header("Camera feel")]
        [SerializeField] private float _sprintFovBoost = 7f;
        [SerializeField] private float _strafeRoll = 1.4f;
        [SerializeField] private float _bobVertical = 0.045f;
        [SerializeField] private float _bobHorizontal = 0.03f;
        [SerializeField] private float _walkStride = 1.5f;
        [SerializeField] private float _landDipPerSpeed = 0.014f;

        private Camera _camera;
        private float _yaw;
        private float _pitch;
        private float _fovBoost;
        private float _roll;
        private float _stridePhase;
        private float _bobWeight;
        private float _dip;
        private float _dipVelocity;
        private Vector2 _recoilTarget, _recoilCurrent;   // x = yaw right, y = pitch up (degrees)
        private float _zoomFov, _zoomWeight;
        private int _zoomFrame = -10;

        public Camera Camera => _camera;
        public float Sensitivity { get; private set; }
        public float BaseFov { get; private set; }
        public float Yaw => _yaw;
        /// <summary>Horizontal facing, used for movement.</summary>
        public Quaternion YawRotation => Quaternion.Euler(0f, _yaw, 0f);

        /// <summary>Raised on each footfall of the camera bob (for footstep sounds).</summary>
        public event System.Action Step;

        public void Attach(Camera cam)
        {
            _camera = cam;
            _camera.fieldOfView = BaseFov;
            // Take the spawn facing from the body, then keep the body unrotated (it's a physics capsule).
            // Reset the rigidbody too: with interpolation it writes its own rotation back onto the transform.
            _yaw = transform.eulerAngles.y;
            ResetBodyRotation(transform);
            ApplyHead();
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
                LookAt(new Vector3(DevCommands.ParseFloat(args, 0), DevCommands.ParseFloat(args, 1), DevCommands.ParseFloat(args, 2))),
                cheat: true, owner: this);
        }

        private void OnDisable()
        {
            _motor.Landed -= OnLanded;
            DevCommands.Unregister("sens", this);
            DevCommands.Unregister("fov", this);
            DevCommands.Unregister("lookat", this);
        }

        public void LookAt(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - _head.position;
            var flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 1e-6f)
                _yaw = Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
            _pitch = Mathf.Clamp(-Mathf.Atan2(dir.y, flat.magnitude) * Mathf.Rad2Deg, -88f, 88f);
            ApplyHead();
        }

        private void ApplyHead() => _head.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);

        /// <summary>Turn the view (e.g. with the vehicle we're driving).</summary>
        public void AddYaw(float degrees)
        {
            _yaw += degrees;
            ApplyHead();
        }

        /// <summary>Gun recoil: the view climbs (x right, y up, degrees) quickly and stays there.</summary>
        public void AddRecoil(Vector2 kick) => _recoilTarget += kick;

        /// <summary>Aiming down the sights: blend the view towards <paramref name="fov"/>. Call every frame while it applies.</summary>
        public void SetZoom(float fov, float weight)
        {
            _zoomFov = fov;
            _zoomWeight = Mathf.Clamp01(weight);
            _zoomFrame = Time.frameCount;
        }

        private float ZoomWeight => Time.frameCount - _zoomFrame <= 1 ? _zoomWeight : 0f;

        /// <summary>Players' bodies never rotate; facing lives on the head.</summary>
        public static void ResetBodyRotation(Transform body)
        {
            if (body.TryGetComponent(out Rigidbody rb))
                rb.rotation = Quaternion.identity;
            body.rotation = Quaternion.identity;
        }

        private void Update()
        {
            if (!GameInput.GameplayActive)
                return;

            Vector2 mouse = GameInput.LookMouse.ReadValue<Vector2>() * Sensitivity;
            Vector2 stick = GameInput.LookStick.ReadValue<Vector2>() * (_stickDegreesPerSecond * Time.deltaTime);
            Vector2 look = mouse + stick;
            // Zoomed in: turn slower, so the same hand movement covers the same part of the picture.
            float zoom = ZoomWeight;
            if (zoom > 0f) look *= Mathf.Lerp(1f, _zoomFov / Mathf.Max(1f, BaseFov), zoom);

            // Recoil eases in over a few frames.
            Vector2 before = _recoilCurrent;
            _recoilCurrent = Vector2.Lerp(_recoilCurrent, _recoilTarget, 1f - Mathf.Exp(-25f * Time.deltaTime));
            look += _recoilCurrent - before;
            if ((_recoilTarget - _recoilCurrent).sqrMagnitude < 1e-6f) _recoilTarget = _recoilCurrent = Vector2.zero;

            _yaw = Mathf.Repeat(_yaw + look.x, 360f);
            _pitch = Mathf.Clamp(_pitch - look.y, -88f, 88f);
            ApplyHead();
        }

        private void LateUpdate()
        {
            if (_camera == null)
                return;
            float dt = Time.deltaTime;

            float zoom = ZoomWeight;
            float targetBoost = _motor.IsSprinting && _motor.HorizontalSpeed > 1f && zoom < 0.1f ? _sprintFovBoost : 0f;
            _fovBoost = Mathf.Lerp(_fovBoost, targetBoost, 1f - Mathf.Exp(-8f * dt));
            _camera.fieldOfView = Mathf.Lerp(BaseFov + _fovBoost, _zoomFov, zoom);

            // Lean a touch into strafes.
            _roll = Mathf.Lerp(_roll, -_motor.StrafeInput * _strafeRoll, 1f - Mathf.Exp(-6f * dt));

            // Bob: one full cycle per two steps, driven by distance walked so it matches any speed.
            bool walking = _motor.IsGrounded && !_motor.IsSwimming && _motor.HorizontalSpeed > 0.4f;
            _bobWeight = Mathf.Lerp(_bobWeight, walking ? Mathf.Clamp01(_motor.HorizontalSpeed / 4.5f) : 0f, 1f - Mathf.Exp(-9f * dt));
            float previousPhase = _stridePhase;
            if (walking)
                _stridePhase += _motor.HorizontalSpeed * dt / _walkStride;
            if (Mathf.Floor(_stridePhase) > Mathf.Floor(previousPhase))
                Step?.Invoke(); // a foot came down
            float step = _stridePhase * Mathf.PI;
            float bobY = -Mathf.Abs(Mathf.Sin(step)) * _bobVertical * _bobWeight;       // dips on each footfall
            float bobX = Mathf.Sin(step * 0.5f) * _bobHorizontal * _bobWeight;         // sways once per two steps

            _dip = Mathf.SmoothDamp(_dip, 0f, ref _dipVelocity, 0.14f);

            _camera.transform.localPosition = new Vector3(bobX, bobY - _dip, 0f);
            _camera.transform.localRotation = Quaternion.Euler(_dip * 12f, 0f, _roll);
        }

        private void OnLanded(float impactSpeed) => _dip = Mathf.Min(0.3f, impactSpeed * _landDipPerSpeed);
    }
}
