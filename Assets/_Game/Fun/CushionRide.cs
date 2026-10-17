using System.Collections.Generic;
using FishNet;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using PleaseDontDrown.Vehicles;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The cushion ride on the hotel island (Docs/16, replaces the banana boat there): an inflatable cushion with two
    /// hotel guests sitting in it, waiting at the cushion jetty. A lifeguard drives a jet ski up to its nose and it
    /// hitches on (a rope, like the banana boat's). Round every buoy of the course in order and back past the first:
    /// $40 per guest still aboard. Swing the jet ski right round at speed and the guests are flung off: they're guests
    /// in trouble in the water then (rescue them). New guests climb in when the cushion is back at the jetty.
    /// The host runs the guests, the course and the pay; whoever simulates the cushion pulls it along.
    /// </summary>
    public class CushionRide : NetworkBehaviour
    {
        [SerializeField] private Vehicle _vehicle;
        [SerializeField] private Transform _nose;
        [SerializeField] private LineRenderer _rope;
        [SerializeField] private float _ropeLength = 7f;
        [Tooltip("Where the two guests sit (children of the cushion).")]
        [SerializeField] private Transform[] _riderSpots = System.Array.Empty<Transform>();
        [Tooltip("The course: buoys to go round, in order.")]
        [SerializeField] private Transform[] _buoys = System.Array.Empty<Transform>();
        [Tooltip("At the jetty: guests climb in when the cushion is near here and still.")]
        [SerializeField] private Transform _home;
        [SerializeField] private StoryNpc _npcPrefab;
        [SerializeField] private int _payPerGuest = 40;

        // A buoy counts once the cushion passes this close; a fling is the jet ski swung this far round this fast.
        private const float BuoyReach = 14f, FlingSpeed = 6f, FlingSweep = 150f, FlingWindow = 3.5f;

        private readonly SyncVar<NetworkObject> _towedBy = new SyncVar<NetworkObject>();
        private readonly SyncVar<int> _nextBuoy = new SyncVar<int>();
        private Rigidbody _body;
        private readonly StoryNpc[] _riders = new StoryNpc[2];
        private float _nextThink, _boardAt = -1f, _unmannedSince = -1f, _nextFling;
        private Vector3 _lastPos;
        private float _speed, _turnedTotal, _lastTugYaw = float.NaN;
        private readonly Queue<(float time, float turned)> _headings = new();
        private bool _lapStarted;

        private static readonly string[] Cheers = { "WHEEE!", "FASTER!", "Woo-hoo!", "This is the best day of my holiday!", "Ha ha ha!" };
        private static readonly string[] Screams = { "AAAAAH!", "TOO FAST!", "MY DRINK!", "NOT THE HAIR!" };

        public static CushionRide Instance { get; private set; }
        /// <summary>Guests sitting in the cushion now (host).</summary>
        public int RidersAboard
        {
            get
            {
                int n = 0;
                foreach (StoryNpc r in _riders) if (r != null && r.IsSpawned) n++;
                return n;
            }
        }
        /// <summary>The buoy the course goes to next (everyone: drawn as a marker).</summary>
        public Transform NextBuoy => _buoys.Length > 0 ? _buoys[Mathf.Clamp(_nextBuoy.Value, 0, _buoys.Length - 1)] : null;
        public bool IsTowed => _towedBy.Value != null;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private Vehicle Tug => _towedBy.Value != null ? _towedBy.Value.GetComponent<Vehicle>() : null;

        private static Vector3 TailOf(Vehicle tug) => tug.transform.position - tug.transform.forward * 1.4f + Vector3.up * 0.3f;

        private static bool IsJetSki(Vehicle v) => v != null && v.DisplayName.IndexOf("Jet Ski", System.StringComparison.OrdinalIgnoreCase) >= 0;

        // ------------------------------------------------------------------ the pull (whoever simulates the cushion)

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

        // ------------------------------------------------------------------ host

        [Server]
        private void ServerWatch()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 p = transform.position;
            _speed = Mathf.Lerp(_speed, new Vector2(p.x - _lastPos.x, p.z - _lastPos.z).magnitude / dt, 0.2f);
            _lastPos = p;

            // How far round the jet ski has swung in the last few seconds.
            Vehicle towing = Tug;
            float tugYaw = towing != null ? towing.transform.eulerAngles.y : float.NaN;
            float tugTurned = towing != null && !float.IsNaN(_lastTugYaw) ? Mathf.DeltaAngle(_lastTugYaw, tugYaw) : 0f;
            _lastTugYaw = tugYaw;
            if (towing == null || Mathf.Abs(tugTurned) > 30f)
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
            if (highest - lowest >= FlingSweep && _speed > FlingSpeed && Time.time > _nextFling && RidersAboard > 0)
            {
                _nextFling = Time.time + 4f;
                _headings.Clear();
                ServerFling($"swung {highest - lowest:F0} deg round at {_speed:F1} m/s");
            }

            if (Time.time < _nextThink) return;
            _nextThink = Time.time + 0.25f;
            Hitching();
            Boarding();
            Course();
        }

        private void Hitching()
        {
            Vehicle tug = Tug;
            if (tug == null)
            {
                foreach (Vehicle v in Vehicle.All)
                    if (v != _vehicle && IsJetSki(v) && v.Driver != null && (TailOf(v) - _nose.position).sqrMagnitude < 4.5f * 4.5f)
                    {
                        _towedBy.Value = v.NetworkObject;
                        _unmannedSince = -1f;
                        HitchObservers(true, v.Driver.DisplayName);
                        Debug.Log($"[Cushion] hitched to {v.name} ({v.Driver.DisplayName}), {RidersAboard} guests aboard");
                        break;
                    }
                return;
            }
            if (tug.Driver == null) { if (_unmannedSince < 0f) _unmannedSince = Time.time; }
            else _unmannedSince = -1f;
            if ((_unmannedSince > 0f && Time.time - _unmannedSince > 6f) || (TailOf(tug) - _nose.position).sqrMagnitude > 30f * 30f)
            {
                _towedBy.Value = null;
                HitchObservers(false, "");
            }
        }

        /// <summary>Back at the jetty and still, with a lifeguard about: guests climb into the empty places.</summary>
        private void Boarding()
        {
            if (_home == null || _npcPrefab == null || RidersAboard >= _riderSpots.Length) { _boardAt = -1f; return; }
            bool home = (transform.position - _home.position).sqrMagnitude < 14f * 14f && _speed < 1.2f;
            if (!home || StoryNpc.NearestPlayer(transform.position, 45f) == null) { _boardAt = -1f; return; }
            if (_boardAt < 0f) _boardAt = Time.time + 3f;
            if (Time.time < _boardAt) return;
            _boardAt = -1f;
            ServerFillUp();
        }

        /// <summary>Host: guests in every empty place now (the 'cushiontest' command too).</summary>
        [Server]
        public void ServerFillUp()
        {
            for (int i = 0; i < _riders.Length && i < _riderSpots.Length; i++)
            {
                if (_riders[i] != null && _riders[i].IsSpawned) continue;
                Transform spot = _riderSpots[i];
                int seed = Random.Range(1, int.MaxValue);
                AvatarLook look = AvatarLook.RandomTourist(seed, i % 2);
                StoryNpc npc = Instantiate(_npcPrefab, spot.position, spot.rotation);
                InstanceFinder.ServerManager.Spawn(npc.gameObject);
                npc.ServerSetup(VictimBrain.RandomName(look.Feminine), NpcRole.Guest, look);
                npc.ServerSetPose(AvatarPose.SitChair);
                npc.ServerRide(_vehicle, transform.InverseTransformPoint(spot.position));
                // Sitting inside it: their bodies would shove the cushion under the water (it simulates here, on the host).
                foreach (Collider mine in GetComponentsInChildren<Collider>())
                    foreach (Collider theirs in npc.GetComponentsInChildren<Collider>())
                        Physics.IgnoreCollision(mine, theirs);
                _riders[i] = npc;
            }
            _nextBuoy.Value = 0;
            _lapStarted = false;
            Debug.Log($"[Cushion] {RidersAboard} guests climbed in");
        }

        private void Course()
        {
            if (_buoys.Length == 0 || Tug == null || RidersAboard == 0) return;
            Transform buoy = _buoys[Mathf.Clamp(_nextBuoy.Value, 0, _buoys.Length - 1)];
            Vector3 d = buoy.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > BuoyReach * BuoyReach) return;
            if (_nextBuoy.Value == 0 && _lapStarted)
            {
                // Round the course and back past the first buoy: a tour.
                int guests = RidersAboard;
                int pay = guests * _payPerGuest;
                if (Story.Economy.Instance != null) Story.Economy.Instance.ServerAdd(pay, "cushion ride", transform.position + Vector3.up * 2f);
                LapObservers(guests, pay);
                Debug.Log($"[Cushion] tour done: {guests} guests, +${pay}");
                foreach (StoryNpc r in _riders) if (r != null && r.IsSpawned) r.ServerShout(Cheers[Random.Range(0, Cheers.Length)], false);
            }
            _lapStarted = true;
            _nextBuoy.Value = (_nextBuoy.Value + 1) % _buoys.Length;
            Debug.Log($"[Cushion] buoy passed, next {_nextBuoy.Value}");
        }

        /// <summary>Host: the guests go flying into the sea; each is a guest in trouble in the water now.</summary>
        [Server]
        public void ServerFling(string why)
        {
            Vector3 outward = -transform.right * (Random.value < 0.5f ? 1f : -1f);
            for (int i = 0; i < _riders.Length; i++)
            {
                StoryNpc npc = _riders[i];
                _riders[i] = null;
                if (npc == null || !npc.IsSpawned) continue;
                Vector3 at = npc.transform.position + outward * (3f + 1.5f * i) + transform.forward * Random.Range(-1f, 1f);
                at.y = WaterSurface.Exists ? WaterSurface.HeightAt(at) - 0.4f : at.y;
                var profile = new TouristProfile
                {
                    Figure = AvatarLook.Unpack(npc.LookPacked).Feminine ? 1 : 0, Look = npc.LookPacked, Name = npc.Name,
                    SecondsToUnconscious = 25f, BleedSeconds = 60f, Shouts = Screams[Random.Range(0, Screams.Length)] + "|HELP!"
                };
                FlungObservers(npc.transform.position + Vector3.up);
                npc.ServerRide(null, Vector3.zero);
                Despawn(npc.gameObject);
                if (RescueService.Instance != null)
                {
                    RescueService.Instance.SpawnVictim(at, Random.Range(0f, 360f), VictimState.Distressed, 40f, 0.9f, profile);
                    RescueService.Instance.ServerAnnounce($"<color=#ffd060><b>SPLASH!</b></color> {profile.Name} flew off the cushion. Get them out!");
                }
                Debug.Log($"[Cushion] {profile.Name} flung off ({why})");
            }
            _nextBuoy.Value = 0;
            _lapStarted = false;
        }

        [ObserversRpc]
        private void HitchObservers(bool hitched, string driver)
        {
            if (hitched) PlayerHud.ShowToast($"The cushion is hitched to <b>{driver}</b>'s jet ski. Round the buoys: $40 a guest!", 3f);
            else PlayerHud.ShowToast("The cushion is untied.", 2f);
        }

        [ObserversRpc]
        private void LapObservers(int guests, int pay) =>
            PlayerHud.ShowToast($"Cushion tour done: {guests} happy guest{(guests == 1 ? "" : "s")}, +${pay}", 3f);

        [ObserversRpc]
        private void FlungObservers(Vector3 at) => AudioSource.PlayClipAtPoint(FunSounds.SlideDown, at, 0.9f);

        // ------------------------------------------------------------------ console

        public override void OnStartServer()
        {
            base.OnStartServer();
            // The guests' ride: lifeguards tow it with the jet ski, they don't sit in it.
            if (_vehicle != null) _vehicle.ServerSetLocked(true, "The cushion ride: tow it with the jet ski ($40 a guest)");
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("cushiontest", "[fling]", "Put yourself on a jet ski hitched to the cushion with two guests aboard (fling: throw them off now).", args =>
                TestServer(args.Length > 0 && args[0] == "fling"), cheat: true, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("cushiontest", this);
        }

        [ServerRpc(RequireOwnership = false)]
        private void TestServer(bool fling, FishNet.Connection.NetworkConnection caller = null)
        {
            if (!DevCommands.CheatsAllowed) return;
            if (fling)
            {
                ServerFling("cushiontest fling");
                return;
            }
            if (RidersAboard < _riderSpots.Length) ServerFillUp();
            // The nearest free jet ski goes just ahead of the cushion's nose; the caller sits on it.
            Vehicle ski = null;
            foreach (Vehicle v in Vehicle.All)
                if (IsJetSki(v) && !v.IsLocked && v.Driver == null && (ski == null || (v.transform.position - transform.position).sqrMagnitude < (ski.transform.position - transform.position).sqrMagnitude))
                    ski = v;
            if (ski == null) return;
            Vector3 at = _nose.position + transform.forward * (_ropeLength * 0.6f);
            ski.ServerPlace(at, transform.eulerAngles.y);
            PlayerHub by = null;
            foreach (PlayerHub h in PlayerHub.All) if (h.Owner == caller) by = h;
            if (by != null) MountTarget(caller, ski.NetworkObject);
            Debug.Log($"[Cushion] cushiontest: {ski.name} placed at {at:F1} for {by?.DisplayName}");
        }

        [TargetRpc]
        private void MountTarget(FishNet.Connection.NetworkConnection target, NetworkObject ski)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || ski == null || !ski.TryGetComponent(out Vehicle v)) return;
            StartCoroutine(MountSoon(me, v));
        }

        private System.Collections.IEnumerator MountSoon(PlayerHub me, Vehicle v)
        {
            yield return new WaitForSeconds(0.4f);
            if (me == null || v == null) yield break;
            me.Motor.Teleport(v.transform.position + Vector3.up * 1.2f);
            yield return new WaitForSeconds(0.2f);
            v.OnInteract(me);
        }
    }
}
