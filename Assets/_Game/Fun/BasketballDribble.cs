using PleaseDontDrown.Audio;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>Right-hand dribble on the ground; gather the actual ball in both hands for jumps and shots.</summary>
    [DefaultExecutionOrder(30)] // hands place the item at 0, ball moves at 30, avatar grips refresh at 40, IK at 50
    public class BasketballDribble : MonoBehaviour
    {
        [SerializeField] private Item _item;
        [SerializeField] private Transform _visual;
        [SerializeField] private AudioSource _audio;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private PlayerHub _holder;
        private Vector3 _lastHolderPosition;
        private float _speed, _blend, _phase, _spin;
        public const float Radius = .12f;

        private static void State(PlayerHub holder, out bool moving, out bool airborne, out bool shooting)
        {
            moving = airborne = shooting = false;
            if (holder == null) return;
            var motor = holder.Motor;
            if (holder.IsOwner && motor != null && motor.enabled)
            {
                moving = motor.HorizontalSpeed > 1.1f;
                airborne = !motor.IsGrounded && !motor.IsSwimming && !motor.IsClimbing && !motor.Noclip && motor.Seat == null;
                shooting = holder.Hands != null && holder.Hands.IsPreparingThrow;
            }
            else if (holder.Avatar != null && holder.Avatar.Animator != null)
            {
                AvatarMotion motion = holder.Avatar.Animator.Motion;
                moving = new Vector2(motion.Velocity.x, motion.Velocity.z).magnitude > 1.1f;
                airborne = (!motion.Grounded || Mathf.Abs(motion.Velocity.y) > 2.5f) && !motion.Swimming && !motion.Climbing && !motion.Seated;
                shooting = holder.Avatar.RemoteCharging;
            }
        }

        public static Vector3 HoldOffset(bool moving, bool airborne, bool shooting) => airborne || shooting
            ? new Vector3(0f, .04f, .50f) : moving ? new Vector3(.24f, -.06f, .65f) : new Vector3(0f, -.06f, .55f);

        // Keep the first-person bounce inside the view. Looking down still shows its full ground contact;
        // remote players always see the full physical-height bounce.
        public static Vector3 VisibleDribbleCentre(Camera camera, Vector3 centre)
        {
            if (camera == null) return centre;
            Vector3 viewport = camera.WorldToViewportPoint(centre);
            if (viewport.z <= .05f) return centre;
            float halfHeight = viewport.z * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f);
            float lowest = .035f + (Radius + .015f) / (2f * halfHeight);
            if (viewport.y >= lowest) return centre;
            viewport.y = lowest;
            return camera.ViewportToWorldPoint(viewport);
        }

        public static Vector3 HoldCentre(AvatarRig rig, Vector3 eye, Quaternion facing, bool moving, bool airborne, bool shooting)
        {
            if (rig == null || !rig.IsBuilt) return eye + facing * HoldOffset(moving, airborne, shooting);
            Transform r = rig[AvatarRig.Bone.UpperArmR], l = rig[AvatarRig.Bone.UpperArmL];
            Vector3 ahead = facing * Vector3.forward, side = facing * Vector3.right;
            float reach = rig.UpperArmLength + rig.ForearmLength;
            if (moving && !airborne && !shooting)
                return r.position + ahead * (reach * .6f) + side * .08f - Vector3.up * .04f;
            return (l.position + r.position) * .5f + ahead * (reach * (airborne || shooting ? 1.14f : 1.08f)) +
                Vector3.up * (reach * (airborne || shooting ? .40f : .12f));
        }

        public void GetHoldTarget(PlayerHub holder, Transform aim, out Vector3 position, out Quaternion rotation)
        {
            State(holder, out bool moving, out bool airborne, out bool shooting);
            if (holder != null && holder.IsOwner)
            {
                rotation = aim.rotation;
                position = aim.TransformPoint(HoldOffset(moving, airborne, shooting));
                return;
            }
            Vector3 f = aim.forward;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
            rotation = Quaternion.Euler(Mathf.Clamp(pitch, -45f, 45f) * .25f, yaw, 0f);
            position = HoldCentre(holder != null && holder.Avatar != null ? holder.Avatar.Rig : null, aim.position,
                Quaternion.Euler(0f, yaw, 0f), moving, airborne, shooting);
        }

        public PlayerHands.GripKind GetGrips(PlayerHub holder, out HandGrip left, out HandGrip right)
        {
            State(holder, out bool moving, out bool airborne, out bool shooting);
            bool twoHands = !moving || airborne || shooting;
            Transform frame = holder != null && holder.Head != null ? holder.Head : transform;
            Vector3 ahead = Vector3.ProjectOnPlane(frame.forward, Vector3.up).normalized;
            if (ahead.sqrMagnitude < .01f) ahead = Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, ahead);
            Vector3 held = transform.TransformPoint(_restPosition);
            Vector3 centre = _visual != null ? _visual.position : transform.position;
            BallGrips(held, centre, ahead, side, twoHands, twoHands ? 0f : _blend, out left, out right);
            return twoHands ? PlayerHands.GripKind.TwoHands : PlayerHands.GripKind.OneHand;
        }

        /// <summary>Shared with visual review: use the rendered sphere, including its bounce, as the contact surface.</summary>
        public static void BallGrips(Vector3 held, Vector3 centre, Vector3 ahead, Vector3 side, bool twoHands,
            float dribbleBlend, out HandGrip left, out HandGrip right)
        {
            left = default;
            HandGrip Surface(Vector3 outward) => new(centre + outward.normalized * (Radius + .004f),
                Vector3.ProjectOnPlane(ahead + Vector3.up * .35f, outward).normalized, -outward.normalized, HandPose.BallGrip);
            if (twoHands)
            {
                right = Surface(side * .8f - Vector3.up * .35f - ahead * .48f);
                left = Surface(-side * .9f - Vector3.up * .15f - ahead * .4f);
                return;
            }
            HandGrip cup = Surface(side * .5f - Vector3.up * .65f - ahead * .55f);
            // Follow the ball while pushing its top, then wait above it instead of reaching down to the sand.
            Vector3 push = centre + Vector3.up * (Radius + .004f);
            push.y = Mathf.Max(push.y, held.y + Radius + .004f - .28f);
            right = new HandGrip(Vector3.Lerp(cup.Point, push, dribbleBlend),
                Vector3.Lerp(cup.Fingers, ahead, dribbleBlend).normalized,
                Vector3.Lerp(cup.Palm, Vector3.down, dribbleBlend).normalized,
                HandPose.Lerp(HandPose.BallGrip, new HandPose(.3f, .22f, .15f), dribbleBlend));
        }

        // Retained for older editor court reviews. Live hands use BallGrips against the actual rendered sphere.
        public static HandGrip RightPushGrip(Vector3 held, HandGrip rest, Vector3 ahead, Vector3 side, float blend, float phase)
        {
            float tri = 1f - Mathf.Abs(2f * Mathf.Repeat(phase, 1f) - 1f);
            return new HandGrip(Vector3.Lerp(rest.Point, held + Vector3.up * (.12f - .28f * tri * tri), blend),
                ahead, Vector3.down, HandPose.BallGrip);
        }

        public static Vector3 BounceCentre(Vector3 held, Vector3 ahead, Vector3 side, float depth, float blend, float phase)
        {
            float tri = 1f - Mathf.Abs(2f * Mathf.Repeat(phase, 1f) - 1f);
            return held + (Vector3.down * depth + side * .10f + ahead * .08f) * (tri * tri * blend);
        }

        private void Awake()
        {
            if (_item == null) _item = GetComponent<Item>();
            if (_visual == null) _visual = transform.Find("Model_basketball");
            if (_visual == null) return;
            _restPosition = _visual.localPosition;
            _restRotation = _visual.localRotation;
        }

        private void ResetBounce()
        {
            _blend = _phase = 0f;
            _visual.localPosition = _restPosition;
            _visual.localRotation = _restRotation;
        }

        private void LateUpdate()
        {
            if (_visual == null || _item == null) return;
            float dt = Mathf.Max(Time.deltaTime, .0001f);
            PlayerHub holder = _item.Holder;
            bool inHands = holder != null && holder.Hands != null && holder.Hands.HeldItem == _item;
            if (!inHands) { ResetBounce(); _holder = null; _speed = 0f; return; }
            if (holder != _holder) { _holder = holder; _lastHolderPosition = holder.transform.position; _speed = 0f; }
            Vector3 moved = (holder.transform.position - _lastHolderPosition) / dt;
            _lastHolderPosition = holder.transform.position;
            if (moved.sqrMagnitude > 900f) moved = Vector3.zero;
            _speed = Mathf.Lerp(_speed, new Vector2(moved.x, moved.z).magnitude, 1f - Mathf.Exp(-8f * dt));
            State(holder, out bool moving, out bool airborne, out bool shooting);
            // Immediately gather at takeoff, at the apex, while falling, and for shot charging.
            if (airborne || shooting || !moving) { ResetBounce(); return; }
            Vector3 held = transform.TransformPoint(_restPosition);
            bool ground = Physics.Raycast(held, Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.attachedRigidbody != _item.Sync.Body
                && (!WaterSurface.Exists || WaterSurface.HeightAt(hit.point) < hit.point.y + .05f);
            bool dribbling = ground && Vehicles.Vehicle.RideOf(holder) == null;
            _blend = Mathf.MoveTowards(_blend, dribbling ? 1f : 0f, dt * (dribbling ? 4f : 8f));
            if (_blend <= 0f) { ResetBounce(); return; }
            float before = _phase;
            _phase += dt * Mathf.Lerp(1.7f, 2.8f, Mathf.InverseLerp(1f, 7f, _speed));
            float depth = ground ? Mathf.Clamp(hit.distance - Radius, 0f, 1.6f) : 0f;
            Transform frame = holder.Head != null ? holder.Head : holder.transform;
            Vector3 ahead = Vector3.ProjectOnPlane(frame.forward, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, ahead);
            Vector3 bounce = BounceCentre(held, ahead, side, depth, _blend, _phase);
            if (holder.IsOwner && holder.Look != null) bounce = VisibleDribbleCentre(holder.Look.Camera, bounce);
            _visual.position = bounce;
            _spin += _speed * dt * 220f;
            _visual.rotation = Quaternion.AngleAxis(_spin, side) * transform.rotation * _restRotation;
            if (Mathf.Floor(before + .5f) != Mathf.Floor(_phase + .5f) && _blend > .5f && _audio != null)
            { _audio.pitch = Random.Range(.92f, 1.08f); _audio.PlayOneShot(FunSounds.BallBounce, .7f); }
        }
    }
}
