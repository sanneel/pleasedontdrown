using PleaseDontDrown.Core;
using PleaseDontDrown.World;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>Surface footsteps, accepted jumps, weighted landings and swimming on each client.</summary>
    public class PlayerFootsteps : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _remoteStride = 1.5f;
        [SerializeField] private float _volume = 0.55f;

        private readonly RaycastHit[] _hits = new RaycastHit[4];
        private Vector3 _lastPosition;
        private float _distance, _nextSwimStroke, _nextTransition, _nextFootstep, _remoteFallSpeed;
        private bool _submerged, _waterInitialized, _positionInitialized, _remoteGrounded;
        private PlayerMotor _motor;
        private PlayerLook _look;
        private AudioSource _waterAudio;
        private SurfaceKind _remoteSurface;

        private void Start()
        {
            if (_audio == null) return;
            _audio.dopplerLevel = 0f;
            // Transitions have their own source so a footstep never retunes a splash in flight.
            _waterAudio = gameObject.AddComponent<AudioSource>();
            _waterAudio.playOnAwake = false;
            _waterAudio.dopplerLevel = 0f;
            _waterAudio.rolloffMode = _audio.rolloffMode;
            _waterAudio.minDistance = _audio.minDistance;
            _waterAudio.maxDistance = _audio.maxDistance;
            _waterAudio.spatialBlend = _audio.spatialBlend;
        }

        private void Update()
        {
            if (_hub == null || _audio == null) return;
            if (_hub.IsOwner)
            {
                _audio.spatialBlend = 0f;
                if (_waterAudio != null) _waterAudio.spatialBlend = 0f;
                if (_motor == null && _hub.Motor != null)
                {
                    _motor = _hub.Motor;
                    _motor.Jumped += OnJump;
                    _motor.Landed += OnLand;
                }
                if (_look == null && _hub.Look != null)
                {
                    _look = _hub.Look;
                    _look.Step += OnLocalStep;
                }
                if (_motor == null || _motor.Noclip || _motor.Seat != null) return;
                UpdateLocalSwimming();
                return;
            }

            Vector3 p = transform.position;
            if (!_positionInitialized)
            {
                _positionInitialized = true;
                _lastPosition = p;
                _remoteGrounded = TryGround(out Collider initialGround);
                _remoteSurface = SurfaceType.Of(initialGround);
                return;
            }
            Vector3 delta = p - _lastPosition;
            _lastPosition = p;
            float flat = new Vector2(delta.x, delta.z).magnitude;
            if (delta.sqrMagnitude > 9f || Time.deltaTime <= 0f)
            {
                _distance = _remoteFallSpeed = 0f;
                _remoteGrounded = false;
                return;
            }
            float depth = WaterSurface.Exists ? WaterSurface.DepthOf(p) : 0f;
            if (depth > 1f)
            {
                Stroke(flat / Time.deltaTime, false);
                _remoteGrounded = false;
                _remoteFallSpeed = _distance = 0f;
                return;
            }
            bool grounded = TryGround(out Collider ground);
            if (grounded) _remoteSurface = SurfaceType.Of(ground);
            float verticalSpeed = delta.y / Time.deltaTime;
            if (!grounded) _remoteFallSpeed = Mathf.Max(_remoteFallSpeed, -verticalSpeed);
            if (_remoteGrounded && !grounded && verticalSpeed > 3f)
                PlayClip(depth > 0.12f ? BeachAudio.Wade : BeachAudio.Jump(_remoteSurface), _volume * 0.65f);
            if (!_remoteGrounded && grounded && _remoteFallSpeed > 3f)
                Land(ground, _remoteFallSpeed);
            _remoteGrounded = grounded;
            if (!grounded) { _distance = 0f; return; }
            _remoteFallSpeed = 0f;
            _distance += flat;
            if (_distance >= _remoteStride)
            {
                _distance %= _remoteStride;
                PlayStep(ground);
            }
        }

        private void UpdateLocalSwimming()
        {
            float depth = WaterSurface.Exists && _hub.Head != null ? WaterSurface.DepthOf(_hub.Head.position) : -1f;
            bool underwater = depth > (_submerged ? 0.025f : 0.2f);
            if (!_waterInitialized)
            {
                _waterInitialized = true;
                _submerged = underwater;
            }
            if (underwater != _submerged && Time.time >= _nextTransition)
            {
                _submerged = underwater;
                _nextTransition = Time.time + 0.5f;
                if (_waterAudio != null)
                {
                    // The recordings have natural tails. A new transition replaces the old tail.
                    _waterAudio.Stop();
                    _waterAudio.clip = underwater ? BeachAudio.Dive : BeachAudio.Emerge;
                    _waterAudio.volume = 0.45f;
                    _waterAudio.Play();
                }
            }
            if (!_motor.IsSwimming) { _nextSwimStroke = Time.time; return; }
            Vector3 v = _motor.Velocity;
            if (_motor.MoveInput.sqrMagnitude < 0.01f && (!_submerged || Mathf.Abs(v.y) < 1.3f)) return;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (_submerged) speed = Mathf.Max(speed, Mathf.Abs(v.y));
            Stroke(speed, _submerged);
        }

        private void Stroke(float speed, bool underwater)
        {
            if (speed < 0.65f || Time.time < _nextSwimStroke) return;
            float effort = Mathf.Clamp01(speed / 5f);
            _nextSwimStroke = Time.time + Mathf.Lerp(1.12f, 0.78f, effort) + Random.Range(-0.045f, 0.045f);
            PlayClip(BeachAudio.SwimStroke, _volume * Mathf.Lerp(0.35f, 0.65f, effort) * (underwater ? 0.45f : 1f));
        }

        private void OnDisable()
        {
            if (_look != null) _look.Step -= OnLocalStep;
            if (_motor != null) { _motor.Jumped -= OnJump; _motor.Landed -= OnLand; }
            _look = null;
            _motor = null;
            _waterInitialized = _positionInitialized = false;
            _distance = _remoteFallSpeed = 0f;
            if (_waterAudio != null) _waterAudio.Stop();
            if (_audio != null) _audio.Stop();
        }

        private void OnJump()
        {
            if (_motor == null || _motor.Noclip) return;
            bool wet = WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0.12f;
            PlayClip(wet ? BeachAudio.Wade : BeachAudio.Jump(SurfaceType.Of(_motor.GroundCollider)), _volume * 0.7f);
            _nextFootstep = Time.time + 0.18f;
        }

        private void OnLand(float speed)
        {
            if (_motor == null || _motor.Noclip || _motor.IsSwimming) return;
            Land(_motor.GroundCollider, speed);
        }

        private void Land(Collider ground, float speed)
        {
            bool wet = WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0.12f;
            float weight = Mathf.InverseLerp(3f, 15f, speed);
            PlayClip(wet ? BeachAudio.WaterImpact(weight * 0.7f) : BeachAudio.Land(SurfaceType.Of(ground)), _volume * Mathf.Lerp(0.65f, 1.35f, weight));
            _nextFootstep = Time.time + 0.2f;
        }

        private void OnLocalStep()
        {
            if (_motor != null && !_motor.Noclip && _motor.IsGrounded && !_motor.IsSwimming)
                PlayStep(_motor.GroundCollider);
        }

        private void PlayStep(Collider ground)
        {
            if (Time.time < _nextFootstep) return;
            _nextFootstep = Time.time + 0.12f;
            bool wading = WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0.12f;
            float effort = _motor != null && _motor.IsSprinting ? 1.15f : _motor != null && _motor.IsCrouching ? 0.55f : 1f;
            PlayClip(wading ? BeachAudio.Wade : BeachAudio.Footstep(SurfaceType.Of(ground)), _volume * effort * (wading ? 0.55f : 1f));
        }

        private void PlayClip(AudioClip clip, float gain)
        {
            if (_audio == null) return;
            // Keep source pitch fixed: retuning PlayOneShot also retunes its still-playing tails.
            _audio.pitch = 1f;
            _audio.PlayOneShot(clip, gain * Random.Range(0.94f, 1.02f));
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
