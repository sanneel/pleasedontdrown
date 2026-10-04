using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.Vehicles;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The banana boat, towed by the lifeguards' jet ski: one lifeguard drives the ski, another sits on the banana
    /// behind it. Drive the ski up to the banana's nose and it hitches on (a rope from the ski's tail). Turn too hard
    /// at speed and whoever is on the banana goes flying into the sea. Whichever machine simulates the banana pulls it
    /// along the rope; the host decides hitching and who gets flung off.
    /// </summary>
    public class BananaBoat : NetworkBehaviour
    {
        [SerializeField] private Vehicle _vehicle;
        [SerializeField] private Transform _nose;
        [SerializeField] private LineRenderer _rope;
        [SerializeField] private float _ropeLength = 6f;

        public const string TugName = "Lifeguard Jet Ski";
        private const float FlingSpeed = 6f, FlingTurn = 55f; // m/s and degrees a second

        private readonly SyncVar<NetworkObject> _towedBy = new SyncVar<NetworkObject>();
        private Rigidbody _body;
        private float _nextThink, _nextFling, _unmannedSince = -1f;
        private Vector3 _lastPos;
        private float _lastYaw, _speed, _yawRate;

        private void Awake() => _body = GetComponent<Rigidbody>();

        private Vehicle Tug => _towedBy.Value != null ? _towedBy.Value.GetComponent<Vehicle>() : null;

        /// <summary>The point on the jet ski the rope is tied to: its tail.</summary>
        private static Vector3 TailOf(Vehicle tug) => tug.transform.position - tug.transform.forward * 1.4f + Vector3.up * 0.3f;

        // ------------------------------------------------------------------ the pull (whoever simulates the banana)

        private void FixedUpdate()
        {
            Vehicle tug = Tug;
            if (tug != null && _body != null && !_body.isKinematic)
            {
                Vector3 rope = TailOf(tug) - _nose.position;
                rope.y = 0f;
                float distance = rope.magnitude;
                if (distance > _ropeLength)
                {
                    Vector3 dir = rope / distance;
                    float closing = Vector3.Dot(_body.linearVelocity, dir);
                    _body.AddForceAtPosition(dir * ((distance - _ropeLength) * 9f - closing * 0.8f + 2f) * _body.mass, _nose.position, ForceMode.Force);
                }
                // Swing round to follow the rope.
                Vector3 forward = new Vector3(transform.forward.x, 0f, transform.forward.z);
                if (distance > 0.5f && forward.sqrMagnitude > 0.01f)
                    _body.AddTorque(Vector3.up * (Vector3.SignedAngle(forward, rope, Vector3.up) * 0.05f), ForceMode.Acceleration);
            }
            if (IsServerInitialized) ServerWatch();
        }

        private void LateUpdate()
        {
            if (_rope == null) return;
            Vehicle tug = Tug;
            _rope.enabled = tug != null;
            if (tug == null) return;
            Vector3 a = _nose.position, b = TailOf(tug);
            float slack = Mathf.Clamp01(1f - Vector3.Distance(a, b) / _ropeLength) * 0.8f;
            for (int i = 0; i < _rope.positionCount; i++)
            {
                float t = i / (_rope.positionCount - 1f);
                _rope.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * (0.15f + slack)));
            }
        }

        // ------------------------------------------------------------------ host: hitching, flinging the rider off

        [Server]
        private void ServerWatch()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 p = transform.position;
            _speed = Mathf.Lerp(_speed, new Vector2(p.x - _lastPos.x, p.z - _lastPos.z).magnitude / dt, 0.2f);
            float yaw = transform.eulerAngles.y;
            _yawRate = Mathf.Lerp(_yawRate, Mathf.DeltaAngle(_lastYaw, yaw) / dt, 0.2f);
            _lastPos = p;
            _lastYaw = yaw;

            // Too sharp a turn too fast: the banana rider goes flying.
            PlayerHub rider = _vehicle != null ? _vehicle.Driver : null;
            if (rider != null && _speed > FlingSpeed && Mathf.Abs(_yawRate) > FlingTurn && Time.time > _nextFling)
            {
                _nextFling = Time.time + 4f;
                Vector3 outward = -transform.right * Mathf.Sign(_yawRate);
                _vehicle.ServerKickDriver();
                FlingTarget(rider.Owner, outward * 6f + Vector3.up * 5f);
                FlungObservers(rider.DisplayName, p + Vector3.up);
                Debug.Log($"[Banana] {rider.DisplayName} flung off at {_speed:F1} m/s, turning {_yawRate:F0} deg/s");
            }

            if (Time.time < _nextThink) return;
            _nextThink = Time.time + 0.25f;
            Vehicle tug = Tug;
            if (tug == null)
            {
                // Hitch: the lifeguards' jet ski with somebody on it, its tail up against our nose. (Not the robber's:
                // the story rides that one to the hotel island, and a banana in tow would come along.)
                foreach (Vehicle v in Vehicle.All)
                    if (v != null && v != _vehicle && v.DisplayName == TugName && v.Driver != null && (TailOf(v) - _nose.position).sqrMagnitude < 4.5f * 4.5f)
                    {
                        _towedBy.Value = v.NetworkObject;
                        _unmannedSince = -1f;
                        HitchObservers(true, v.Driver.DisplayName);
                        break;
                    }
            }
            else
            {
                // Unhitch: the jet ski left empty a while, or yanked impossibly far (teleported).
                if (tug.Driver == null) { if (_unmannedSince < 0f) _unmannedSince = Time.time; }
                else _unmannedSince = -1f;
                if ((_unmannedSince > 0f && Time.time - _unmannedSince > 6f) || (TailOf(tug) - _nose.position).sqrMagnitude > 30f * 30f)
                {
                    _towedBy.Value = null;
                    HitchObservers(false, "");
                }
            }
        }

        [TargetRpc]
        private void FlingTarget(NetworkConnection target, Vector3 velocity)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || me.Motor == null) return;
            StartCoroutine(FlingSoon(me, velocity));
        }

        private System.Collections.IEnumerator FlingSoon(PlayerHub me, Vector3 velocity)
        {
            yield return new WaitForSeconds(0.15f); // off the seat first
            if (me != null && me.Motor.Seat == null) me.Motor.AddImpulse(velocity);
        }

        [ObserversRpc]
        private void FlungObservers(string who, Vector3 at)
        {
            FloatingText.Spawn(at + Vector3.up * 0.8f, "WAAAAAH!", new Color(1f, 0.95f, 0.4f), 1.3f, 1.6f);
            PlayerHud.ShowToast($"<b>{who}</b> flew off the banana!", 2f);
            AudioSource.PlayClipAtPoint(FunSounds.SlideDown, at, 0.9f);
        }

        [ObserversRpc]
        private void HitchObservers(bool hitched, string driver)
        {
            if (hitched) PlayerHud.ShowToast($"The banana boat is hitched to <b>{driver}</b>'s jet ski. Someone hop on the banana!", 2.5f);
            else PlayerHud.ShowToast("The banana boat is untied.", 2f);
            FloatingText.Spawn(_nose.position + Vector3.up * 0.6f, hitched ? "HITCHED!" : "UNTIED", new Color(1f, 0.95f, 0.4f), 1f, 1.5f);
        }
    }
}
