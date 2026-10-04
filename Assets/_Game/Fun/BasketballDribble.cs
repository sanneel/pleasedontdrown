using PleaseDontDrown.Audio;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// Walk or run with the basketball in your hands and you dribble it: the ball (its look only; the real one stays
    /// in your hands, so nothing about catching, shooting or the network changes) drops out of your hand to the sand
    /// beside you and bounces back up, faster the faster you go, with the bounce sound on every bounce. Every machine
    /// does it for every lifeguard holding a ball. Standing still, you just hold it.
    /// </summary>
    [DefaultExecutionOrder(60)] // after the hands have put the ball where it's held this frame
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

        private void Awake()
        {
            if (_visual == null) return;
            _restPosition = _visual.localPosition;
            _restRotation = _visual.localRotation;
        }

        private void LateUpdate()
        {
            if (_visual == null || _item == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            PlayerHub holder = _item.Holder;
            bool inHands = holder != null && holder.Hands != null && holder.Hands.HeldItem == _item;
            if (holder != _holder)
            {
                _holder = holder;
                if (holder != null) _lastHolderPosition = holder.transform.position;
                _speed = 0f;
            }

            float verticalSpeed = 0f;
            if (inHands)
            {
                Vector3 moved = (holder.transform.position - _lastHolderPosition) / dt;
                _lastHolderPosition = holder.transform.position;
                if (moved.sqrMagnitude > 900f) moved = Vector3.zero; // a teleport
                _speed = Mathf.Lerp(_speed, new Vector2(moved.x, moved.z).magnitude, 1f - Mathf.Exp(-8f * dt));
                verticalSpeed = moved.y;
            }

            // Where the bounce lands: the ground under the ball (not water: no dribbling while wading).
            Vector3 held = _item.transform.TransformPoint(_restPosition);
            bool ground = Physics.Raycast(held, Vector3.down, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore)
                          && hit.collider.attachedRigidbody != _item.Sync.Body
                          && (!WaterSurface.Exists || WaterSurface.HeightAt(hit.point) < hit.point.y + 0.05f);
            bool dribbling = inHands && ground && _speed > 1.1f && Mathf.Abs(verticalSpeed) < 2f && Vehicles.Vehicle.RideOf(holder) == null;
            _blend = Mathf.MoveTowards(_blend, dribbling ? 1f : 0f, dt * (dribbling ? 3f : 6f));
            if (_blend <= 0f)
            {
                _visual.localPosition = _restPosition;
                _visual.localRotation = _restRotation;
                _phase = 0f;
                return;
            }

            // Down to the sand and back up to the hand: quick at the bottom (the bounce), slow at the top.
            float rate = Mathf.Lerp(1.7f, 2.8f, Mathf.InverseLerp(1f, 7f, _speed));
            float before = _phase;
            _phase += dt * rate;
            float u = Mathf.Repeat(_phase, 1f);
            float tri = 1f - Mathf.Abs(2f * u - 1f);
            float drop = tri * tri; // rounded at the hand (it eases in and out of the palm), a sharp turn at the bounce
            float depth = ground ? Mathf.Clamp(hit.distance - 0.12f, 0f, 1.5f) : 0f;
            Vector3 side = holder != null ? Vector3.ProjectOnPlane(holder.Head != null ? holder.Head.right : holder.transform.right, Vector3.up).normalized : Vector3.zero;
            Vector3 ahead = holder != null ? Vector3.ProjectOnPlane(holder.Head != null ? holder.Head.forward : holder.transform.forward, Vector3.up).normalized : Vector3.zero;
            // Bounced a little out to the right and ahead, so it doesn't hit your feet.
            Vector3 offset = Vector3.down * (depth * drop) + side * (0.14f * drop) + ahead * (0.12f * drop);
            _visual.position = held + offset * _blend;
            _spin += _speed * dt * 220f;
            _visual.rotation = Quaternion.AngleAxis(_spin, side) * _item.transform.rotation * _restRotation;

            // The bounce: once per dribble, as it touches the sand.
            if (Mathf.Floor(before + 0.5f) != Mathf.Floor(_phase + 0.5f) && _blend > 0.5f && _audio != null)
            {
                _audio.pitch = Random.Range(0.92f, 1.08f);
                _audio.PlayOneShot(FunSounds.BallBounce, 0.7f);
            }
        }
    }
}
