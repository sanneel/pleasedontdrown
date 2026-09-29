using System;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>How a tool (a gun) moves in the hands: sway, tilt, trailing the view, sprint pose, drawing it.</summary>
    [Serializable]
    public struct ToolFeel
    {
        [Tooltip("Tilt (degrees) when strafing, at full strafe.")] public float Tilt;
        public bool CanLookAround;
        [Tooltip("How far the gun trails behind turning the view (degrees): x = pitch, y = yaw, z = roll with the yaw.")] public Vector3 MaxLook;
        [Tooltip("Share of each turn of the view the gun trails by.")] public float LookSpeed;
        [Tooltip("Sideways / vertical slide from turning the view.")] public float SwayPosition;
        [Tooltip("Turn from turning the view (per axis).")] public Vector3 SwayRotation;
        public float MaxSwayPosition;
        public float MaxSwayRotation;
        [Tooltip("Lift while falling / dip while rising.")] public float FallForce;
        [Tooltip("Lowered, turned pose while sprinting (camera space, metres / degrees).")] public Vector3 SprintPosition;
        public Vector3 SprintRotation;
        [Tooltip("Smoothing time into / out of the sprint pose.")] public float SprintTime;
        [Tooltip("Where it starts when drawn (camera space, metres / degrees): it comes up from here.")] public Vector3 DrawPosition;
        public Vector3 DrawRotation;

        /// <summary>A pistol-like default (used when a gun has nothing set).</summary>
        public static ToolFeel Default => new()
        {
            Tilt = -10f, CanLookAround = true, MaxLook = new Vector3(2f, 8f, 5f), LookSpeed = 0.25f,
            SwayPosition = 1f, SwayRotation = new Vector3(500f, 250f, 250f), MaxSwayPosition = 0.1f, MaxSwayRotation = 25f,
            FallForce = 1e-5f, SprintPosition = new Vector3(-0.1f, -0.1f, 0f), SprintRotation = new Vector3(30f, -35f, 25f),
            SprintTime = 0.15f, DrawPosition = new Vector3(0.15f, -0.5f, 0f), DrawRotation = new Vector3(90f, 0f, 0f)
        };
    }

    /// <summary>
    /// The motion of a tool held up in first person (a gun), How to Fish style (the structure and tuning measured from
    /// the game; our own code, see Docs):
    ///
    /// A target pose is the sum of offsets: the hip / aimed position, the head bob (a share of the camera's), a tilt
    /// into strafes, sway from turning the view (slide and turn, clamped), a lift when falling, a slow breath, the
    /// sprint pose, and the draw motion. The whole target also trails the view when you turn (it swings round the
    /// eye by up to a few degrees). The gun itself doesn't sit on the target: it chases it on springs, like a mass
    /// on a joint (sideways / forward 150, up-down 500, damping 5 plus drag 10; the turn 1000 with drag 10, and
    /// soft limits of 1-3° beyond which a 250 spring pulls harder). Recoil kicks a second, stiff spring (the gun's
    /// own numbers) whose offset feeds the target, so a shot jumps the gun back and up and the soft spring carries
    /// the rest. Both run at 100 steps a second, as in the original, with the drawn pose interpolated.
    /// Aiming down the sights damps the sway, bob, breath and tilt to the sights' share.
    /// </summary>
    public sealed class HeldToolMotion
    {
        private const float Step = 0.01f;           // 100 Hz, as in the original
        private const float Drag = 10f;             // both rigs: 10 linear and angular drag
        private static readonly Vector3 SwaySpring = new(150f, 500f, 150f);
        private const float SwayDamper = 5f;
        private const float SwayTurnSpring = 1000f;
        private static readonly Vector3 SwayTurnLimit = new(1f, 3f, 3f); // degrees
        private const float SwayTurnLimitSpring = 250f;
        private const float BobShare = 0.1f, SprintBobShare = 0.5f, SidewaysBob = 1.5f, SprintBobRate = 25f;
        private const float TiltTime = 0.3f;
        private const float BreathRate = 0.7f, BreathHeight = 0.005f;
        private const float DrawTime = 0.4f;

        public ToolFeel Feel = ToolFeel.Default;

        // Inputs, set each frame by the gun.
        public Vector3 BasePosition;                // hip pose (camera space)
        public Vector3 AimOffset;                   // added while aiming (blended by the gun)
        public float AimSwayShare = 1f;             // the sights' sway multiplier when aiming
        public bool Aiming;

        // The recoil spring (the gun's numbers, stiffer while aiming).
        public float RecoilPositionSpring = 10000f, RecoilPositionDamper, RecoilRotationSpring = 1000f, RecoilRotationDamper;

        // Rigs: current and previous step (for interpolation).
        private Vector3 _pos, _prevPos, _vel;
        private Quaternion _rot = Quaternion.identity, _prevRot = Quaternion.identity;
        private Vector3 _angVel;
        private Vector3 _recoilPos, _recoilVel;
        private Quaternion _recoilRot = Quaternion.identity;
        private Vector3 _recoilAngVel;
        private float _accumulator;
        private Vector3 _targetPos, _lastTargetPos;
        private Quaternion _targetRot = Quaternion.identity;

        // Frame state.
        private Vector3 _lookRot;                   // the target trailing the view (degrees)
        private Vector2 _swayPos;
        private Vector3 _swayRot;
        private Vector2 _bob;
        private float _sprintShare;
        private float _tilt, _tiltVelocity;
        private float _fall;
        private float _breath;
        private bool _breathIn = true;
        private float _aimShare = 1f;
        private Vector3 _sprintPos, _sprintPosVelocity, _sprintRot, _sprintRotVelocity;
        private float _draw = 1f;

        /// <summary>Where the gun is now (camera space) and how it's turned (relative to its hip pose).</summary>
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; } = Quaternion.identity;

        /// <summary>Freshly in the hands: drawn up from the draw pose, springs at rest.</summary>
        public void Draw()
        {
            _draw = 0f;
            _lookRot = Vector3.zero;
            _swayPos = Vector2.zero;
            _swayRot = Vector3.zero;
            _sprintPos = _sprintRot = _sprintPosVelocity = _sprintRotVelocity = Vector3.zero;
            _tilt = _tiltVelocity = 0f;
            _recoilPos = _recoilVel = _recoilAngVel = Vector3.zero;
            _recoilRot = Quaternion.identity;
            _vel = _angVel = Vector3.zero;
            _pos = _prevPos = _targetPos = _lastTargetPos = BasePosition + Feel.DrawPosition;
            _rot = _prevRot = _targetRot = Quaternion.Euler(Feel.DrawRotation);
            _accumulator = 0f;
            Position = _pos;
            Rotation = _rot;
        }

        /// <summary>A shot: the recoil spring is kicked (camera-space metres and degrees, applied in its own frame).</summary>
        public void Kick(Vector3 position, Vector3 rotation)
        {
            _recoilRot *= Quaternion.Euler(rotation);
            _recoilPos += position;
        }

        /// <summary>A shot also swings the trailing a little (up by the view's climb, sideways by its kick).</summary>
        public void KickLook(Vector2 recoil)
        {
            _lookRot += new Vector3(-recoil.y, recoil.x, 0f);
        }

        /// <param name="lookDelta">How far the view turned this frame (degrees): x = pitch (+ down), y = yaw.</param>
        /// <param name="cameraBob">The camera's head bob this frame (camera space).</param>
        public void Update(float dt, Vector2 lookDelta, Vector3 cameraBob, Vector2 moveInput, bool sprinting, bool grounded,
            float verticalSpeed, float cameraRollTime)
        {
            dt = Mathf.Max(dt, 1e-5f);
            float aimTarget = Aiming ? AimSwayShare : 1f;
            _aimShare = Mathf.Lerp(_aimShare, aimTarget, Mathf.Min(1f, 50f * dt));

            Breathe(dt);

            // Sway from turning the view: slides against the turn and twists, clamped.
            Vector2 turn = new Vector2(-lookDelta.y, lookDelta.x) * 0.0001f / dt;
            _swayPos = Vector2.ClampMagnitude(turn * (Feel.SwayPosition * _aimShare), Feel.MaxSwayPosition);
            _swayRot = Vector3.ClampMagnitude(new Vector3(-turn.y * Feel.SwayRotation.x, turn.x * Feel.SwayRotation.y, -turn.x * Feel.SwayRotation.z) * _aimShare,
                Feel.MaxSwayRotation);

            // A share of the head bob (more while sprinting), and a tilt into strafes.
            _sprintShare = Mathf.Lerp(_sprintShare, sprinting ? 1f : 0f, Mathf.Min(1f, SprintBobRate * dt));
            float bob = Mathf.Lerp(BobShare, SprintBobShare, _sprintShare);
            _bob = new Vector2(cameraBob.x * bob * SidewaysBob, cameraBob.y * bob) * _aimShare;
            float tiltTarget = moveInput.x * Feel.Tilt;
            _tilt = Mathf.SmoothDamp(_tilt, tiltTarget * _aimShare, ref _tiltVelocity, tiltTarget == 0f ? cameraRollTime : TiltTime, Mathf.Infinity, dt);

            // Falling lifts it (a touch).
            _fall = grounded ? 0f : -verticalSpeed * Feel.FallForce / dt;

            // Trailing the view: accumulates with turning, up to its limits; not while aiming.
            _lookRot += new Vector3(lookDelta.x, lookDelta.y, 0f) * Feel.LookSpeed;
            _lookRot = new Vector3(Mathf.Clamp(_lookRot.x, -Feel.MaxLook.x, Feel.MaxLook.x), Mathf.Clamp(_lookRot.y, -Feel.MaxLook.y, Feel.MaxLook.y), 0f);
            _lookRot.z = Feel.MaxLook.y > 0f ? _lookRot.y / Feel.MaxLook.y * -Feel.MaxLook.z : 0f;
            if (!Feel.CanLookAround || Aiming) _lookRot = Vector3.zero;

            // Sprint pose.
            Vector3 sprintPos = sprinting && grounded ? Feel.SprintPosition : Vector3.zero;
            Vector3 sprintRot = sprinting && grounded ? Feel.SprintRotation : Vector3.zero;
            _sprintPos = Vector3.SmoothDamp(_sprintPos, sprintPos, ref _sprintPosVelocity, Mathf.Max(0.01f, Feel.SprintTime), Mathf.Infinity, dt);
            _sprintRot = Vector3.SmoothDamp(_sprintRot, sprintRot, ref _sprintRotVelocity, Mathf.Max(0.01f, Feel.SprintTime), Mathf.Infinity, dt);

            // Drawn up from the draw pose (eased out).
            _draw = Mathf.MoveTowards(_draw, 1f, dt / DrawTime);
            float drawn = Mathf.Sin(_draw * Mathf.PI * 0.5f);
            Vector3 drawPos = Vector3.Lerp(Feel.DrawPosition, Vector3.zero, drawn);
            Vector3 drawRot = Vector3.Lerp(Feel.DrawRotation, Vector3.zero, drawn);

            // Physics steps (100 Hz), the target rebuilt each step from the frame's offsets and the recoil spring.
            _accumulator += dt;
            int steps = 0;
            while (_accumulator >= Step && steps < 12)
            {
                _accumulator -= Step;
                steps++;
                BuildTarget(drawPos, drawRot);
                Simulate();
            }
            if (steps == 12) _accumulator = 0f;

            float alpha = Mathf.Clamp01(_accumulator / Step);
            Position = Vector3.Lerp(_prevPos, _pos, alpha);
            Rotation = Quaternion.Slerp(_prevRot, _rot, alpha);
        }

        private void Breathe(float dt)
        {
            _breath = Mathf.MoveTowards(_breath, _breathIn ? 1f : 0f, BreathRate * dt);
            if (_breath >= 1f) _breathIn = false;
            else if (_breath <= 0f) _breathIn = true;
        }

        private void BuildTarget(Vector3 drawPos, Vector3 drawRot)
        {
            float breath = Mathf.SmoothStep(0f, BreathHeight, _breath) * _aimShare;
            Vector3 recoilEuler = WrapEuler(_recoilRot.eulerAngles);
            Vector3 local = new Vector3(_bob.x, _bob.y, 0f) + _recoilRot * _recoilPos + (Vector3)_swayPos + _sprintPos + AimOffset
                            + new Vector3(0f, _fall + breath, 0f) + drawPos + BasePosition;
            Vector3 euler = new Vector3(0f, 0f, _tilt) + recoilEuler + _swayRot + _sprintRot + drawRot;
            Quaternion trail = Quaternion.Euler(_lookRot);
            _lastTargetPos = _targetPos;
            _targetPos = trail * local;
            _targetRot = trail * Quaternion.Euler(euler);
        }

        private void Simulate()
        {
            const float h = Step;
            float drag = Mathf.Max(0f, 1f - Drag * h);

            // The recoil spring pulls back to rest (implicit, like a joint drive).
            _recoilVel = SpringStep(_recoilVel, -_recoilPos, Vector3.zero, RecoilPositionSpring, RecoilPositionDamper, h) * drag;
            _recoilPos += _recoilVel * h;
            Vector3 recoilError = -RotationVector(_recoilRot);
            _recoilAngVel = SpringStep(_recoilAngVel, recoilError, Vector3.zero, RecoilRotationSpring, RecoilRotationDamper, h) * drag;
            _recoilRot = Integrate(_recoilRot, _recoilAngVel, h);

            // The gun chases the target.
            _prevPos = _pos;
            _prevRot = _rot;
            Vector3 targetVel = (_targetPos - _lastTargetPos) / h;
            Vector3 error = _targetPos - _pos;
            _vel = new Vector3(
                SpringAxis(_vel.x, error.x, targetVel.x, SwaySpring.x, SwayDamper, h),
                SpringAxis(_vel.y, error.y, targetVel.y, SwaySpring.y, SwayDamper, h),
                SpringAxis(_vel.z, error.z, targetVel.z, SwaySpring.z, SwayDamper, h)) * drag;
            _pos += _vel * h;

            // Turn: toward the target's, harder beyond the soft limits.
            Quaternion offset = Quaternion.Inverse(_targetRot) * _rot;
            Vector3 off = WrapEuler(offset.eulerAngles);
            Vector3 beyond = new Vector3(Excess(off.x, SwayTurnLimit.x), Excess(off.y, SwayTurnLimit.y), Excess(off.z, SwayTurnLimit.z));
            Vector3 limitTorque = _targetRot * (-beyond * (Mathf.Deg2Rad * SwayTurnLimitSpring));
            Vector3 turnError = RotationVector(_targetRot * Quaternion.Inverse(_rot));
            _angVel = SpringStep(_angVel + limitTorque * h, turnError, Vector3.zero, SwayTurnSpring, 0f, h) * drag;
            _rot = Integrate(_rot, _angVel, h);
        }

        private static float Excess(float value, float limit) => value > limit ? value - limit : value < -limit ? value + limit : 0f;

        /// <summary>One implicit step of a spring-damper toward <paramref name="error"/> (as PhysX drives do), per axis.</summary>
        private static Vector3 SpringStep(Vector3 v, Vector3 error, Vector3 targetVel, float k, float c, float h) =>
            (v + h * (k * error + c * targetVel)) / (1f + h * c + h * h * k);

        private static float SpringAxis(float v, float error, float targetVel, float k, float c, float h) =>
            (v + h * (k * error + c * targetVel)) / (1f + h * c + h * h * k);

        /// <summary>Axis * angle (radians) of a rotation.</summary>
        private static Vector3 RotationVector(Quaternion q)
        {
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return Vector3.zero;
            if (angle > 180f) angle -= 360f;
            return axis * (angle * Mathf.Deg2Rad);
        }

        private static Quaternion Integrate(Quaternion q, Vector3 angularVelocity, float h)
        {
            float speed = angularVelocity.magnitude;
            if (speed < 1e-7f) return q;
            return Quaternion.AngleAxis(speed * h * Mathf.Rad2Deg, angularVelocity / speed) * q;
        }

        private static Vector3 WrapEuler(Vector3 e) =>
            new(Mathf.DeltaAngle(0f, e.x), Mathf.DeltaAngle(0f, e.y), Mathf.DeltaAngle(0f, e.z));
    }
}
