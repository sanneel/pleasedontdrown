using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Mouse / stick look. Yaw and pitch both live on the head (which is network-synced, so others see where you
    /// look); the physics body never rotates. Owner only.
    ///
    /// Camera feel is How to Fish's (tuning measured from the game; our own code): a head bob that follows two
    /// curves through each step cycle (sideways once, up-down twice), eased in with a short smoothing and a delayed
    /// follow; a 1° roll into strafes; a pitch that follows how fast we rise or fall; the view climbing with recoil
    /// (it stays up: you pull it back down); and the field of view easing to the sights' when aiming, with mouse
    /// sensitivity scaled to match.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class PlayerLook : MonoBehaviour
    {
        private const string SensitivityKey = "pdd.look.sensitivity";
        private const string FovKey = "pdd.look.fov";

        [SerializeField] private Transform _head;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private float _stickDegreesPerSecond = 180f;

        [Header("Tilt")]
        [SerializeField] private float _strafeRoll = 1f;          // degrees at full strafe
        [SerializeField] private float _rollSmoothTime = 0.15f;
        [SerializeField] private float _fallPitch = 0.25f;        // degrees per m/s of vertical speed
        [SerializeField] private float _fallPitchSmoothTime = 0.08f;

        [Header("Head bob")]
        [SerializeField] private float _bobCyclesPerSecond = 3f;  // at full speed; half that when slow
        [SerializeField] private float _bobMinSpeed = 0.1f;       // speed fraction below which it stops
        [SerializeField] private float _bobFullAt = 7f;           // speed fraction scale (as in the original: the bob stays small)
        [SerializeField] private float _bobSideways = 0.5f;
        [SerializeField] private float _bobUpDown = 0.75f;
        [SerializeField] private float _bobSmoothTime = 0.08f;
        [SerializeField] private float _bobFollowRate = 7.5f;
        [SerializeField] private float _stepAtHeight = -0.02f;    // a footstep sounds when the bob dips below this

        private Camera _camera;
        private float _yaw;
        private float _pitch;
        private float _roll, _rollVelocity;
        private float _fallTilt, _fallTiltVelocity;
        private float _bobTime;
        private bool _stepArmed;
        private Vector3 _bob, _bobVelocity, _delayedBob;
        private Vector2 _recoilTarget, _recoilCurrent;   // x = yaw right, y = pitch up (degrees)
        private float _aimFov, _aimSmoothTime = 0.1f;
        private int _aimFrame = -10;
        private float _fov, _fovVelocity;

        private static AnimationCurve _bobSidewaysCurve, _bobUpDownCurve;

        public Camera Camera => _camera;
        public float Sensitivity { get; private set; }
        public float BaseFov { get; private set; }
        public float Yaw => _yaw;
        public float Pitch => _pitch;
        /// <summary>Horizontal facing, used for movement.</summary>
        public Quaternion YawRotation => Quaternion.Euler(0f, _yaw, 0f);
        /// <summary>How far the view turned this frame (degrees): x = pitch (+ down), y = yaw (+ right). Held guns sway from it.</summary>
        public Vector2 LookDelta { get; private set; }
        /// <summary>The head bob this frame (camera space, before the delayed follow): held guns bob along with it.</summary>
        public Vector3 BobPosition => _bob;
        /// <summary>How quickly the strafe roll settles (held guns level out at the same pace).</summary>
        public float RollSmoothTime => _rollSmoothTime;
        /// <summary>Current field of view relative to the base one (mouse sensitivity follows it).</summary>
        public float ZoomSensitivity => _fov > 1f ? _fov / Mathf.Max(1f, BaseFov) : 1f;

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
            BaseFov = PlayerPrefs.GetFloat(FovKey, 74f);
            _fov = BaseFov;
            BuildBobCurves();
        }

        /// <summary>
        /// One step cycle of head bob (as in the original): sideways out and back once, up and down twice with a
        /// sharp dip at each footfall.
        /// </summary>
        private static void BuildBobCurves()
        {
            if (_bobSidewaysCurve != null) return;
            _bobSidewaysCurve = new AnimationCurve(
                new Keyframe(0f, 0f, 6.43f, 6.43f),
                new Keyframe(0.4223f, 1f, 0f, 0f),
                new Keyframe(0.8523f, -1f, 0f, 0f),
                new Keyframe(1f, 0f, 5.916f, 5.916f));
            _bobUpDownCurve = new AnimationCurve(
                new Keyframe(0f, 0f, -8.153f, -8.153f),
                new Keyframe(0.1504f, -1f, 0f, 0f),
                new Keyframe(0.4210f, 1f, 0f, 0f),
                new Keyframe(0.6808f, -1f, 0.0427f, 0.0427f),
                new Keyframe(0.8509f, 1f, -0.0362f, -0.0362f),
                new Keyframe(1f, 0f, -7.130f, -7.130f));
        }

        private void OnEnable()
        {
            DevCommands.Register("sens", "<degrees per pixel>", "Mouse sensitivity (default 0.1).", args =>
            {
                Sensitivity = Mathf.Clamp(DevCommands.ParseFloat(args, 0), 0.01f, 1f);
                PlayerPrefs.SetFloat(SensitivityKey, Sensitivity);
                DevCommands.Print($"sensitivity = {Sensitivity}");
            }, owner: this);
            DevCommands.Register("fov", "<degrees>", "Field of view (default 74).", args =>
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

        private Vector3 _leanPoint;
        private float _leanStart = -10f, _leanLength;

        /// <summary>
        /// Lean the view right in to a point and back (mouth-to-mouth: the camera goes down to their lips, holds, and
        /// comes back up). The look direction itself doesn't change.
        /// </summary>
        public void LeanIn(Vector3 worldPoint, float seconds)
        {
            _leanPoint = worldPoint;
            _leanStart = Time.time;
            _leanLength = Mathf.Max(0.3f, seconds);
        }

        /// <summary>0..1..0 over the lean: quick in, hold, back out.</summary>
        private float LeanWeight()
        {
            float t = (Time.time - _leanStart) / _leanLength;
            if (t < 0f || t > 1f) return 0f;
            float inOut = t < 0.3f ? t / 0.3f : t > 0.75f ? (1f - t) / 0.25f : 1f;
            return Mathf.SmoothStep(0f, 1f, inOut);
        }

        /// <summary>Gun recoil: the view climbs (x right, y up, degrees) quickly and stays there.</summary>
        public void AddRecoil(Vector2 kick) => _recoilTarget += kick;

        /// <summary>
        /// Aiming down the sights: the field of view eases to <paramref name="fov"/> (smoothing time
        /// <paramref name="smoothTime"/>, the sights' own). Call every frame while aiming.
        /// </summary>
        public void SetAimFov(float fov, float smoothTime)
        {
            _aimFov = fov;
            _aimSmoothTime = Mathf.Max(0.01f, smoothTime);
            _aimFrame = Time.frameCount;
        }

        private bool Aiming => Time.frameCount - _aimFrame <= 1;

        /// <summary>Players' bodies never rotate; facing lives on the head.</summary>
        public static void ResetBodyRotation(Transform body)
        {
            if (body.TryGetComponent(out Rigidbody rb))
                rb.rotation = Quaternion.identity;
            body.rotation = Quaternion.identity;
        }

        private void Update()
        {
            LookDelta = Vector2.zero;
            if (!GameInput.GameplayActive)
                return;

            Vector2 mouse = GameInput.LookMouse.ReadValue<Vector2>() * Sensitivity;
            Vector2 stick = GameInput.LookStick.ReadValue<Vector2>() * (_stickDegreesPerSecond * Time.deltaTime);
            // Zoomed in (aiming): turn slower, in step with the field of view.
            Vector2 look = (mouse + stick) * ZoomSensitivity;
            LookDelta = new Vector2(-look.y, look.x);

            // Recoil: the climb catches up with where the kicks put it (a quarter of the way per 1/100 s), and stays.
            if (_recoilTarget.x != 0f || _recoilTarget.y != 0f)
            {
                Vector2 before = _recoilCurrent;
                _recoilCurrent = Vector2.Lerp(_recoilCurrent, _recoilTarget, Mathf.Min(1f, 25f * Time.deltaTime));
                look += _recoilCurrent - before;
            }

            _yaw = Mathf.Repeat(_yaw + look.x, 360f);
            _pitch = Mathf.Clamp(_pitch - look.y, -88f, 88f);
            ApplyHead();
        }

        private void LateUpdate()
        {
            if (_camera == null)
                return;
            float dt = Mathf.Max(Time.deltaTime, 1e-5f);

            // Field of view: eases to the sights' when aiming (at their pace), back to the base one otherwise.
            bool aiming = Aiming;
            _fov = Mathf.SmoothDamp(_fov, aiming ? _aimFov : BaseFov, ref _fovVelocity, aiming ? _aimSmoothTime : 0.1f, Mathf.Infinity, dt);
            _camera.fieldOfView = _fov;

            // Roll into strafes; pitch with the vertical speed (up when falling, down a touch when rising).
            _roll = Mathf.SmoothDamp(_roll, -_motor.StrafeInput * _strafeRoll, ref _rollVelocity, _rollSmoothTime, Mathf.Infinity, dt);
            float vy = _motor.IsSwimming ? 0f : _motor.Velocity.y;
            _fallTilt = Mathf.SmoothDamp(_fallTilt, vy * _fallPitch, ref _fallTiltVelocity, _fallPitchSmoothTime, Mathf.Infinity, dt);

            HeadBob(dt);
            _delayedBob = Vector3.Lerp(_delayedBob, _bob, Mathf.Min(1f, _bobFollowRate * dt));

            _camera.transform.localPosition = _delayedBob;
            _camera.transform.localRotation = Quaternion.Euler(_fallTilt, 0f, _roll);

            // Leaning in (mouth-to-mouth): the eye comes down over their face (they lie on their back, face up) to just
            // above the lips, looking down at them, head tipped a little, then back up.
            float lean = LeanWeight();
            if (lean > 0f)
            {
                Vector3 eye = _camera.transform.position;
                Vector3 toward = Vector3.ProjectOnPlane(_leanPoint - eye, Vector3.up);
                toward = toward.sqrMagnitude > 1e-4f ? toward.normalized : Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
                Vector3 close = _leanPoint + Vector3.up * 0.19f - toward * 0.05f;
                Quaternion face = Quaternion.LookRotation(_leanPoint - close, toward) * Quaternion.Euler(0f, 0f, 15f);
                _camera.transform.SetPositionAndRotation(Vector3.Lerp(eye, close, lean), Quaternion.Slerp(_camera.transform.rotation, face, lean));
            }
        }

        /// <summary>
        /// The step cycle runs at 1.5..3 cycles a second (with speed), the bob follows the two curves, sized by the
        /// speed, eased in and out; a footstep sounds each time it dips below the step height.
        /// </summary>
        private void HeadBob(float dt)
        {
            float moving = _motor.SpeedFraction;
            bool bobbing = moving > _bobMinSpeed && _motor.IsGrounded && !_motor.IsSwimming;
            Vector3 target = Vector3.zero;
            if (bobbing)
            {
                float amount = Mathf.Clamp01(moving / Mathf.Max(_bobFullAt, _bobMinSpeed + 0.01f));
                float rate = Mathf.Lerp(_bobCyclesPerSecond * 0.5f, _bobCyclesPerSecond, amount);
                _bobTime = Mathf.Repeat(_bobTime + dt * rate, 1f);
                target = new Vector3(_bobSidewaysCurve.Evaluate(_bobTime) * _bobSideways, _bobUpDownCurve.Evaluate(_bobTime) * _bobUpDown, 0f) * amount;
            }
            else
            {
                _bobTime = 0f;
                _stepArmed = false;
            }
            _bob = Vector3.SmoothDamp(_bob, target, ref _bobVelocity, _bobSmoothTime, Mathf.Infinity, dt);
            if (!bobbing) return;
            if (_bob.y > _stepAtHeight) _stepArmed = true;
            if (_stepArmed && _bob.y <= _stepAtHeight)
            {
                _stepArmed = false;
                Step?.Invoke(); // a foot came down
            }
        }
    }
}
