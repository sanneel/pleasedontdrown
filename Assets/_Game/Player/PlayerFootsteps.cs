using PleaseDontDrown.Core;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Footstep sounds for every player on every machine. The local player steps on the camera-bob footfalls;
    /// other players step every stride of distance they cover on the ground. The surface underfoot (sand, wood,
    /// rock) or shallow water picks the sound.
    /// </summary>
    public class PlayerFootsteps : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _remoteStride = 1.5f;
        [SerializeField] private float _volume = 0.55f;

        private readonly RaycastHit[] _hits = new RaycastHit[4];
        private Vector3 _lastPosition;
        private float _distance;
        private bool _subscribed;
        private float _nextSwimStroke;
        private bool _submerged;

        private void Start() => _lastPosition = transform.position;

        private void Update()
        {
            if (_hub.IsOwner)
            {
                UpdateLocalSwimming();
                if (!_subscribed && _hub.Look != null)
                {
                    _hub.Look.Step += OnLocalStep;
                    _subscribed = true;
                }
                return;
            }

            // Remote players: estimate footfalls from how far they moved while near the ground.
            Vector3 p = transform.position;
            Vector3 delta = p - _lastPosition;
            _lastPosition = p;
            float flat = new Vector2(delta.x, delta.z).magnitude;
            if (flat > 3f || Mathf.Abs(delta.y) > 0.3f) return; // teleport, jump or fall
            if (!TryGround(out Collider ground)) return;
            _distance += flat;
            if (_distance >= _remoteStride)
            {
                _distance = 0f;
                Play(ground);
            }
        }

        private void UpdateLocalSwimming()
        {
            if (_audio == null || _hub.Motor == null) return;
            bool underwater = WaterSurface.Exists && _hub.Head != null &&
                              WaterSurface.DepthOf(_hub.Head.position) > 0.14f;
            if (underwater != _submerged)
            {
                _submerged = underwater;
                _audio.PlayOneShot(underwater ? BeachAudio.Dive : BeachAudio.Emerge, 0.35f);
            }
            if (!_hub.Motor.IsSwimming) return;
            Vector3 v = _hub.Motor.Velocity;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (speed < 0.6f || Time.time < _nextSwimStroke) return;
            _nextSwimStroke = Time.time + Mathf.Lerp(1f, 0.62f, Mathf.Clamp01(speed / 5f));
            _audio.pitch = Random.Range(0.91f, 1.08f);
            _audio.PlayOneShot(BeachAudio.SwimStroke, Mathf.Min(0.8f, _volume * 0.95f));
        }

        private void OnDestroy()
        {
            if (_subscribed && _hub != null && _hub.Look != null)
                _hub.Look.Step -= OnLocalStep;
        }

        private void OnLocalStep()
        {
            if (_hub.Motor.IsGrounded && !_hub.Motor.IsSwimming)
                Play(_hub.Motor.GroundCollider);
        }

        private void Play(Collider ground)
        {
            if (_audio == null) return;
            bool wading = WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0.12f;
            AudioClip clip = wading ? ProceduralAudio.WaterStep : BeachAudio.Footstep(SurfaceType.Of(ground));
            _audio.pitch = Random.Range(0.9f, 1.1f);
            _audio.PlayOneShot(clip, _volume * (_hub.Motor != null && _hub.Motor.IsSprinting ? 1.2f : 1f));
        }

        private bool TryGround(out Collider ground)
        {
            ground = null;
            int count = Physics.RaycastNonAlloc(transform.position + Vector3.up * 0.3f, Vector3.down, _hits, 0.55f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].collider == _hub.BodyCollider || _hits[i].distance >= best) continue;
                best = _hits[i].distance;
                ground = _hits[i].collider;
            }
            return ground != null;
        }
    }
}
