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
        // Only a really big swing throws the riders off: the jet ski towing it swung FlingSweep degrees round within
        // FlingWindow seconds (a full turnaround) while the banana goes faster than FlingSpeed m/s. A quick jink or an
        // ordinary hard turn doesn't (it used to, on every turn: a 1.2 s turn at full lock swings it ~90 degrees). (The jet
        // ski's heading, not the banana's: lagging on its rope, the banana's own swing is small and wobbly.)
        private const float FlingSpeed = 6f, FlingSweep = 170f, FlingWindow = 4f;

        private readonly SyncVar<NetworkObject> _towedBy = new SyncVar<NetworkObject>();
        private Rigidbody _body;
        private float _nextThink, _nextFling, _unmannedSince = -1f;
        private Vector3 _lastPos;
        private float _lastYaw, _speed, _yawRate;
        private float _turnedTotal, _lastTugYaw = float.NaN; // the jet ski's heading, unwrapped (degrees)
        private readonly System.Collections.Generic.Queue<(float time, float turned)> _headings = new();

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
            float turned = Mathf.DeltaAngle(_lastYaw, yaw);
            _yawRate = Mathf.Lerp(_yawRate, turned / dt, 0.2f);
            // How far round the jet ski has swung in the last few seconds (the biggest swing either way in the window).
            Vehicle towing = Tug;
            float tugYaw = towing != null ? towing.transform.eulerAngles.y : float.NaN;
            float tugTurned = towing != null && !float.IsNaN(_lastTugYaw) ? Mathf.DeltaAngle(_lastTugYaw, tugYaw) : 0f;
            _lastTugYaw = tugYaw;
            if (towing == null || Mathf.Abs(tugTurned) > 30f) // not towed, or snapped round (put back on the water): not a turn
            {
                tugTurned = 0f;
                _headings.Clear();
            }
            _turnedTotal += tugTurned;
            _headings.Enqueue((Time.time, _turnedTotal));
            while (_headings.Count > 0 && Time.time - _headings.Peek().time > FlingWindow) _headings.Dequeue();
            float lowest = _turnedTotal, highest = _turnedTotal;
            foreach ((float _, float t) in _headings)
            {
                lowest = Mathf.Min(lowest, t);
                highest = Mathf.Max(highest, t);
            }
            float sweep = highest - lowest;
            _lastPos = p;
            _lastYaw = yaw;

            // Swung right round at speed: everyone on the banana goes flying (the back riders furthest: they're on
            // the end of the whip).
            var riders = _vehicle != null ? _vehicle.Aboard() : null;
            if (riders != null && riders.Count > 0 && sweep >= FlingSweep && _speed > FlingSpeed && Time.time > _nextFling)
            {
                _nextFling = Time.time + 4f;
                _headings.Clear();
                Vector3 outward = -transform.right * Mathf.Sign(_yawRate);
                for (int i = 0; i < riders.Count; i++)
                {
                    PlayerHub rider = riders[i];
                    if (rider == _vehicle.Driver) _vehicle.ServerKickDriver();
                    else _vehicle.ServerKickRider(rider);
                    FlingTarget(rider.Owner, outward * (6f + i * 1.5f) + Vector3.up * (5f + i));
                    FlungObservers(rider.DisplayName, p + Vector3.up);
                    Debug.Log($"[Banana] {rider.DisplayName} flung off at {_speed:F1} m/s, turning {_yawRate:F0} deg/s, swung {sweep:F0} deg round in {FlingWindow:F0} s");
                }
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
