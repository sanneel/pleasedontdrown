using System;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Rescue
{
    /// <summary>
    /// The physical tourist: a torso body (the networked <see cref="Item"/>) with four floppy limbs on joints.
    ///
    /// Limbs are simulated locally on every machine and only the torso is synced, so dangling, splashing and
    /// sprawling look natural everywhere at no bandwidth cost. Joint drives pose the limbs from the victim's
    /// state: waving for help, thrashing, treading water, reaching up while going under, limp when unconscious,
    /// sitting on the sand once saved. Where the torso is simulated (host, or whoever last touched it), this
    /// also swims it: keeps the head up, dunks under when panicking, lets go when unconscious.
    /// </summary>
    [RequireComponent(typeof(VictimBrain), typeof(ItemSync))]
    public class VictimBody : MonoBehaviour, IHoldPose
    {
        private enum LimbKind { ArmL, ArmR, LegL, LegR }

        private sealed class Limb
        {
            public LimbKind Kind;
            public Rigidbody Body;
            public ConfigurableJoint Joint;
            public Buoyancy Buoyancy;
            public float Phase;
            public float Side;          // +1 right, -1 left
            public bool IsArm;
            public float Spring = -1f;  // current drive, to avoid re-assigning it every step
        }

        [SerializeField] private Transform _visual;
        [SerializeField] private Transform[] _eyes;
        [SerializeField] private Transform _mouth;
        [SerializeField] private Renderer[] _shirt;
        [SerializeField] private Renderer[] _shorts;
        [SerializeField] private Renderer[] _skin;
        [SerializeField] private Renderer[] _hair;
        [SerializeField] private GameObject[] _floaties;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private Vector3 _headLocal = new Vector3(0f, 0.52f, 0f);

        [Header("Swimming (where the torso is simulated)")]
        [Tooltip("Torso centre below the surface while treading water (keeps the head out).")]
        [SerializeField] private float _floatDepth = 0.28f;
        [SerializeField] private float _dunkDepth = 0.8f;
        [SerializeField] private float _floatSpring = 32f;
        [SerializeField] private float _floatDamping = 10f;
        [SerializeField] private float _uprightStrength = 60f;
        [SerializeField] private float _uprightDamping = 14f;
        [SerializeField] private float _consciousDensity = 0.95f;
        [Tooltip("Unconscious people sink, slowly.")]
        [SerializeField] private float _unconsciousDensity = 1.04f;

        [Header("Held onto a float (life ring)")]
        [SerializeField] private float _floatGrabRadius = 1.6f;
        [SerializeField] private float _floatHoldDistance = 0.5f;

        private static readonly string[] LimbNames = { "ArmL", "ArmR", "LegL", "LegR" };
        private static Transform _limbContainer;

        private Rigidbody _rb;
        private ItemSync _sync;
        private Item _item;
        private VictimBrain _brain;
        private Buoyancy _buoyancy;
        private Limb[] _limbs = Array.Empty<Limb>();
        private Vector3 _lastRootPosition;
        private MaterialPropertyBlock _props;

        private float _dunk;               // 0..1, how far under the current dunk pushes the head
        private float _nextDunkAt;
        private float _dunkUntil;
        private float _nextCryAt;
        private float _nextSplashAt;
        private float _squishAt = float.NegativeInfinity;
        private float _nextFloatScan;
        private float _nextWadeScan;
        private bool _wading;
        private Vector3 _wadeDirection = Vector3.forward;
        private float _groundY;
        private int _voice;

        /// <summary>Where the mouth and nose are: underwater here = no air.</summary>
        public Vector3 HeadPosition => transform.TransformPoint(_headLocal);
        /// <summary>The float (life ring...) this person is holding onto, if any. Found on every machine.</summary>
        public Floatable HeldFloat { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _sync = GetComponent<ItemSync>();
            _item = GetComponent<Item>();
            _brain = GetComponent<VictimBrain>();
            _buoyancy = GetComponent<Buoyancy>();
            _props = new MaterialPropertyBlock();
            _rb.solverIterations = 12;
            _rb.solverVelocityIterations = 4;
            BuildLimbs();
        }

        private void Start()
        {
            // Out of the torso's hierarchy: moving the torso transform (followers, remote holders) must not drag the
            // limbs rigidly; they hang on their joints instead.
            if (_limbContainer == null) _limbContainer = new GameObject("TouristLimbs").transform;
            foreach (Limb limb in _limbs)
            {
                limb.Body.transform.SetParent(_limbContainer, true);
                limb.Body.name = $"{name}_{limb.Kind}";
            }
            _lastRootPosition = _rb.position;
        }

        private void OnEnable()
        {
            foreach (Limb limb in _limbs)
                if (limb.Body != null) limb.Body.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            foreach (Limb limb in _limbs)
                if (limb.Body != null) limb.Body.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (Limb limb in _limbs)
                if (limb.Body != null) Destroy(limb.Body.gameObject);
        }

        // ------------------------------------------------------------------ looks

        private static readonly Color[] Shirts =
        {
            new(1f, 0.35f, 0.55f), new(0.2f, 0.85f, 0.75f), new(1f, 0.6f, 0.1f), new(0.55f, 0.35f, 0.95f),
            new(0.3f, 0.9f, 0.3f), new(1f, 0.9f, 0.2f), new(0.2f, 0.55f, 1f), new(0.95f, 0.25f, 0.2f)
        };
        private static readonly Color[] Skins =
        {
            new(1f, 0.82f, 0.7f), new(1f, 0.62f, 0.55f) /* sunburnt */, new(0.87f, 0.68f, 0.5f),
            new(0.68f, 0.48f, 0.33f), new(0.45f, 0.3f, 0.2f), new(0.95f, 0.72f, 0.6f)
        };
        private static readonly Color[] Hairs =
        {
            new(0.15f, 0.1f, 0.07f), new(0.45f, 0.28f, 0.12f), new(0.9f, 0.78f, 0.45f), new(0.6f, 0.6f, 0.62f), new(0.75f, 0.3f, 0.12f)
        };

        /// <summary>Same seed, same tourist on every machine: shirt, shorts, skin, hair, arm floaties, voice.</summary>
        public void ApplyLooks(int seed)
        {
            var rng = new System.Random(seed);
            Color shirt = Shirts[rng.Next(Shirts.Length)];
            Color shorts = Color.Lerp(Shirts[rng.Next(Shirts.Length)], new Color(0.15f, 0.2f, 0.35f), 0.55f);
            Color skin = Skins[rng.Next(Skins.Length)];
            Color hair = Hairs[rng.Next(Hairs.Length)];
            Paint(_shirt, shirt);
            Paint(_shorts, shorts);
            Paint(_skin, skin);
            Paint(_hair, hair);
            bool floaties = rng.NextDouble() < 0.35;
            foreach (GameObject f in _floaties)
                if (f != null) f.SetActive(floaties);
            _voice = rng.Next(4);
        }

        private void Paint(Renderer[] renderers, Color color)
        {
            _props.SetColor("_BaseColor", color);
            _props.SetColor("_Color", color);
            foreach (Renderer r in renderers)
                if (r != null) r.SetPropertyBlock(_props);
        }

        /// <summary>A CPR compression: a squish and a thump (local visual, every machine).</summary>
        public void Pump()
        {
            _squishAt = Time.time;
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Thump, 0.9f);
        }

        public void PlayCough()
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Cough);
        }

        // ------------------------------------------------------------------ holding

        public void GetHoldPose(PlayerHub holder, out Vector3 offset, out Quaternion rotation, out float pitchFollow)
        {
            // Decided from the holder's synced position, so every machine agrees on the pose.
            Vector3 feet = holder != null ? holder.transform.position : transform.position;
            bool towing = WaterSurface.Exists && WaterSurface.HeightAt(feet) - feet.y > 1.1f;
            if (towing)
            {
                // Lifeguard tow: on their back at the surface, a little ahead and to the right (keeps your view clear).
                offset = new Vector3(0.45f, -0.5f, 1.25f);
                rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
                pitchFollow = 0f;
            }
            else
            {
                // Carried across the arms, face up, head to the right.
                offset = new Vector3(0.05f, -0.85f, 0.95f);
                rotation = Quaternion.LookRotation(Vector3.up, Vector3.right);
                pitchFollow = 0.3f;
            }
        }

        // ------------------------------------------------------------------ per frame (every machine)

        private void Update()
        {
            VictimState state = _brain.State;
            bool headUnder = WaterSurface.Exists && WaterSurface.DepthOf(HeadPosition) > 0.02f;
            UpdateFace(state);
            UpdateSquish();

            if (Time.time >= _nextFloatScan)
            {
                _nextFloatScan = Time.time + 0.3f;
                HeldFloat = state.IsStruggling() && !_item.IsHeld ? Floatable.FindNear(transform.position, _floatGrabRadius) : null;
            }
            if (Time.time >= _nextWadeScan)
            {
                _nextWadeScan = Time.time + 0.3f;
                SenseWading(state);
            }

            if (!state.IsStruggling() || _item.IsHeld)
                return;

            // Calling for help (only with the head out of the water).
            if (!headUnder && state != VictimState.Drowning && Time.time >= _nextCryAt && HeldFloat == null)
            {
                _nextCryAt = Time.time + Random.Range(2.8f, 5.5f);
                if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Cry(_voice), 0.9f);
                if (Random.value < 0.6f)
                    FloatingText.Spawn(HeadPosition + Vector3.up * 0.45f, state == VictimState.Panicking ? "HELP!!" : "help!",
                        new Color(1f, 0.95f, 0.8f), 0.7f, 1.1f);
            }

            // Thrashing splashes.
            if (state != VictimState.Distressed && HeldFloat == null && Time.time >= _nextSplashAt && WaterSurface.Exists)
            {
                _nextSplashAt = Time.time + (state == VictimState.Panicking ? Random.Range(0.35f, 0.9f) : Random.Range(1.2f, 2.4f));
                Limb arm = _limbs.Length > 1 ? _limbs[Random.Range(0, 2)] : null;
                Vector3 hand = arm != null ? arm.Body.transform.TransformPoint(0f, -0.55f, 0f) : transform.position;
                if (Mathf.Abs(WaterSurface.DepthOf(hand)) < 0.35f)
                    SplashFx.Spawn(new Vector3(hand.x, WaterSurface.HeightAt(hand), hand.z), state == VictimState.Panicking ? 0.25f : 0.12f);
            }
        }

        private void UpdateFace(VictimState state)
        {
            float eye = state is VictimState.Unconscious or VictimState.Lost ? 0.15f
                : state is VictimState.Panicking or VictimState.Drowning ? 1.4f : 1f;
            foreach (Transform e in _eyes)
                if (e != null) e.localScale = new Vector3(0.035f, 0.035f * eye, 0.02f);
            if (_mouth != null)
            {
                float open = state.IsStruggling() ? 2.4f : state == VictimState.Saved ? 1.6f : 0.6f;
                _mouth.localScale = new Vector3(0.06f, 0.02f * open, 0.02f);
            }
        }

        private void UpdateSquish()
        {
            if (_visual == null) return;
            float t = (Time.time - _squishAt) / 0.28f;
            float k = t < 1f ? Mathf.Sin(t * Mathf.PI) * (1f - t) : 0f;
            _visual.localScale = new Vector3(1f + 0.12f * k, 1f - 0.16f * k, 1f + 0.12f * k);
        }

        // ------------------------------------------------------------------ physics step (every machine)

        private void FixedUpdate()
        {
            FollowTeleports();
            VictimState state = _brain.State;
            PoseLimbs(state);

            bool struggling = state.IsStruggling() && !_item.IsHeld;
            _sync.KeepAwake = struggling;
            if (_buoyancy != null)
                _buoyancy.Density = state.IsConscious() ? _consciousDensity : _unconsciousDensity;

            if (_rb.isKinematic || _item.IsHeld)
                return; // someone else simulates the torso, or a lifeguard's hands steer it

            if (struggling && WaterSurface.Exists)
                Swim(state);
            else if (state is VictimState.Fine or VictimState.Saved)
            {
                if (_wading) WadeAshore();
                else SitUp();
            }
            else if (!state.IsConscious() && !_brain.IsAshore && WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0.2f)
                Topple(); // (not in the shallows: CPR needs them on their back)
        }

        /// <summary>Unconscious in the water: slowly tip over face-down instead of sinking like a statue.</summary>
        private void Topple()
        {
            Vector3 tilt = Vector3.Cross(transform.forward, Vector3.down);
            _rb.AddTorque(tilt * 6f - _rb.angularVelocity * 1f, ForceMode.Acceleration);
        }

        /// <summary>The torso jumped (snap, unstuck pop, teleport): bring the limbs along instead of stretching the joints.</summary>
        private void FollowTeleports()
        {
            Vector3 root = _rb.position;
            Vector3 delta = root - _lastRootPosition;
            _lastRootPosition = root;
            if (delta.sqrMagnitude < 1f) return;
            foreach (Limb limb in _limbs)
            {
                limb.Body.position += delta;
                limb.Body.transform.position = limb.Body.position;
                limb.Body.linearVelocity = _rb.isKinematic ? Vector3.zero : _rb.linearVelocity;
            }
        }

        /// <summary>Tread water with the head out; panicking dunks under now and then, drowning mostly stays under.</summary>
        private void Swim(VictimState state)
        {
            Vector3 p = _rb.position;
            Vector3 v = _rb.linearVelocity;
            float surface = WaterSurface.HeightAt(p);
            UpdateDunk(state);

            float depth = HeldFloat != null ? _floatDepth - 0.12f : _floatDepth + _dunkDepth * _dunk;
            float targetY = surface - depth;
            float lift = Mathf.Clamp((targetY - p.y) * _floatSpring - v.y * _floatDamping, -24f, 24f);
            if (p.y > surface + 0.2f) lift = Mathf.Min(lift, 0f); // don't fly out of the water
            _rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);

            float upright = state == VictimState.Drowning ? _uprightStrength * 0.4f : _uprightStrength;
            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);
            _rb.AddTorque(tilt * upright - _rb.angularVelocity * _uprightDamping, ForceMode.Acceleration);

            if (HeldFloat != null)
            {
                // Hang on to the ring: drift to it and stay there.
                Vector3 to = HeldFloat.transform.position - p;
                to.y = 0f;
                float distance = to.magnitude;
                if (distance > 0.01f)
                {
                    Vector3 pull = to / distance * ((distance - _floatHoldDistance) * 8f) - new Vector3(v.x, 0f, v.z) * 4f;
                    _rb.AddForce(Vector3.ClampMagnitude(pull, 12f), ForceMode.Acceleration);
                }
            }
            else if (state == VictimState.Panicking)
            {
                // Thrashing about.
                float t = Time.time;
                var shove = new Vector3(Mathf.PerlinNoise(t * 0.9f, 3.1f) - 0.5f, 0f, Mathf.PerlinNoise(7.3f, t * 0.9f) - 0.5f);
                _rb.AddForce(shove * 10f, ForceMode.Acceleration);
                _rb.AddTorque(Vector3.up * ((Mathf.PerlinNoise(t * 1.3f, 9.7f) - 0.5f) * 12f), ForceMode.Acceleration);
            }
        }

        private void UpdateDunk(VictimState state)
        {
            float t = Time.time;
            float target = 0f;
            if (HeldFloat == null && state == VictimState.Panicking)
            {
                if (t >= _nextDunkAt && t >= _dunkUntil)
                {
                    float panic = _brain.Panic01;
                    _dunkUntil = t + Random.Range(0.8f, 1.6f);
                    _nextDunkAt = _dunkUntil + Random.Range(2.2f, 4.5f) * (1.3f - panic * 0.4f);
                }
                target = t < _dunkUntil ? 1f : 0f;
            }
            else if (HeldFloat == null && state == VictimState.Drowning)
            {
                // Mostly under; brief, weak pops up for air.
                if (t >= _nextDunkAt)
                {
                    _dunkUntil = t + Random.Range(0.5f, 0.9f);    // here: time spent UP
                    _nextDunkAt = _dunkUntil + Random.Range(2.5f, 4f);
                }
                target = t < _dunkUntil ? 0.1f : 1f;
            }
            _dunk = Mathf.MoveTowards(_dunk, target, Time.fixedDeltaTime * 2.5f);
        }

        /// <summary>
        /// Saved / fine but still standing in water: walk out toward shallower water (every machine decides
        /// "wading" the same way from the torso position, so the walking legs show everywhere).
        /// </summary>
        private void SenseWading(VictimState state)
        {
            if (!(state is VictimState.Fine or VictimState.Saved) || _item.IsHeld || !WaterSurface.Exists)
            {
                _wading = false;
                return;
            }
            Vector3 p = transform.position;
            float depth = Shore.WaterDepthAt(p);
            _wading = depth > 0.12f && depth < Shore.DeepDepth + 0.4f;
            if (!_wading || _rb.isKinematic) return;

            // Where the simulator walks: up the seabed slope (ground heights, not water depth: waves would swamp the slope).
            _groundY = WaterSurface.HeightAt(p) - depth;
            float dx = Shore.GroundHeightAt(p + Vector3.right * 1.5f) - Shore.GroundHeightAt(p + Vector3.left * 1.5f);
            float dz = Shore.GroundHeightAt(p + Vector3.forward * 1.5f) - Shore.GroundHeightAt(p + Vector3.back * 1.5f);
            var uphill = new Vector3(dx, 0f, dz);
            if (!float.IsNaN(uphill.x) && !float.IsNaN(uphill.z) && uphill.sqrMagnitude > 1e-5f) _wadeDirection = uphill.normalized;
        }

        private void WadeAshore()
        {
            Vector3 p = _rb.position;
            Vector3 v = _rb.linearVelocity;
            // Stand: hold the torso at standing height over the seabed (the legs are too floppy to carry it).
            float lift = Mathf.Clamp((_groundY + 1.2f - p.y) * 60f - v.y * 16f + 10f, -30f, 50f); // feet just clear of the sand
            _rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);
            float turn = Vector3.SignedAngle(transform.forward, _wadeDirection, Vector3.up) * Mathf.Deg2Rad;
            _rb.AddTorque(tilt * 60f + Vector3.up * (turn * 8f) - _rb.angularVelocity * 12f, ForceMode.Acceleration);
            Vector3 walk = (_wadeDirection * 1.2f - new Vector3(v.x, 0f, v.z)) * 6f;
            _rb.AddForce(walk, ForceMode.Acceleration);
        }

        /// <summary>Saved or fine on land: sit up (the legs are posed forward).</summary>
        private void SitUp()
        {
            if (_rb.linearVelocity.sqrMagnitude > 9f) return; // flying through the air: let physics have it
            bool inWater = WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0.3f;
            if (inWater) return;
            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);
            _rb.AddTorque(tilt * 28f - _rb.angularVelocity * 8f, ForceMode.Acceleration);
        }

        // ------------------------------------------------------------------ limbs

        private void BuildLimbs()
        {
            var limbs = new System.Collections.Generic.List<Limb>();
            for (int i = 0; i < LimbNames.Length; i++)
            {
                Transform t = transform.Find(LimbNames[i]);
                if (t == null || !t.TryGetComponent(out Rigidbody body)) continue;
                var limb = new Limb
                {
                    Kind = (LimbKind)i,
                    Body = body,
                    Buoyancy = t.GetComponent<Buoyancy>(),
                    IsArm = i < 2,
                    Side = i % 2 == 0 ? -1f : 1f,
                    Phase = i * 1.7f + Random.value * 6.28f
                };
                body.solverIterations = 12;
                body.solverVelocityIterations = 4;
                body.maxAngularVelocity = 25f;
                limb.Joint = CreateJoint(limb, t.localPosition);
                limbs.Add(limb);
            }
            _limbs = limbs.ToArray();

            // Parts of the same person don't collide with each other.
            Collider[] all = GetComponentsInChildren<Collider>(true);
            for (int a = 0; a < all.Length; a++)
                for (int b = a + 1; b < all.Length; b++)
                    Physics.IgnoreCollision(all[a], all[b], true);
        }

        /// <summary>
        /// Arms: the primary axis raises the arm sideways (the one asymmetric limit: out to overhead, barely inward).
        /// Legs: the primary axis swings forward/back (kick forward, barely backward). Rest pose = hanging straight down.
        /// </summary>
        private ConfigurableJoint CreateJoint(Limb limb, Vector3 anchorInTorso)
        {
            var joint = limb.Body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = _rb;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = anchorInTorso;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;

            if (limb.IsArm)
            {
                joint.axis = Vector3.forward;       // sideways raise
                joint.secondaryAxis = Vector3.right; // forward / back swing
                SetPrimaryRange(joint, limb.Side > 0f ? -15f : -175f, limb.Side > 0f ? 175f : 15f);
                joint.angularYLimit = new SoftJointLimit { limit = 115f };
                joint.angularZLimit = new SoftJointLimit { limit = 35f };
            }
            else
            {
                joint.axis = Vector3.right;           // forward / back swing (forward = negative)
                joint.secondaryAxis = Vector3.forward; // sideways spread
                SetPrimaryRange(joint, -115f, 25f);
                joint.angularYLimit = new SoftJointLimit { limit = 40f };
                joint.angularZLimit = new SoftJointLimit { limit = 15f };
            }
            return joint;
        }

        /// <summary>Allowed rotation about the primary axis, in the same sense as the poses below.</summary>
        private static void SetPrimaryRange(ConfigurableJoint joint, float min, float max)
        {
            // Joint space runs opposite to the local rotation we ask for (see SetTarget), so the range flips.
            joint.lowAngularXLimit = new SoftJointLimit { limit = -max };
            joint.highAngularXLimit = new SoftJointLimit { limit = -min };
        }

        /// <summary>Drive the limb toward a rotation relative to the torso (identity = hanging straight down).</summary>
        private static void SetTarget(ConfigurableJoint joint, Quaternion relative)
        {
            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion toJoint = Quaternion.LookRotation(forward, up);
            joint.targetRotation = Quaternion.Inverse(toJoint) * Quaternion.Inverse(relative) * toJoint;
        }

        private static void SetDrive(Limb limb, float spring, float damper)
        {
            if (Mathf.Abs(limb.Spring - spring) < 0.5f) return;
            limb.Spring = spring;
            limb.Joint.slerpDrive = new JointDrive { positionSpring = spring, positionDamper = damper, maximumForce = float.MaxValue };
        }

        /// <param name="raise">Degrees out to the side (0 = down, 90 = horizontal, 180 = straight up).</param>
        /// <param name="swing">Degrees forward (positive) or back.</param>
        private static Quaternion ArmPose(Limb limb, float raise, float swing) =>
            Quaternion.AngleAxis(raise * limb.Side, Vector3.forward) * Quaternion.AngleAxis(-swing, Vector3.right);

        /// <param name="lift">Degrees forward (90 = sitting, legs straight out).</param>
        /// <param name="spread">Degrees out to the side.</param>
        private static Quaternion LegPose(Limb limb, float lift, float spread) =>
            Quaternion.AngleAxis(-lift, Vector3.right) * Quaternion.AngleAxis(spread * limb.Side, Vector3.forward);

        private void PoseLimbs(VictimState state)
        {
            float t = Time.time;
            bool held = _item.IsHeld;
            bool onFloat = HeldFloat != null;
            foreach (Limb limb in _limbs)
            {
                if (limb.Buoyancy != null) limb.Buoyancy.Density = state.IsConscious() ? 0.97f : 1.02f;
                float ph = limb.Phase;
                if (!state.IsConscious())
                {
                    SetDrive(limb, 0f, 3f); // limp
                    continue;
                }

                if (held)
                {
                    // Held: arms along the body, legs a little bent, a bit stiff.
                    SetDrive(limb, 90f, 8f);
                    SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 18f, 5f) : LegPose(limb, 15f, 6f));
                    continue;
                }

                switch (state)
                {
                    case VictimState.Distressed when onFloat:
                    case VictimState.Panicking when onFloat:
                    case VictimState.Drowning when onFloat:
                        // Hugging the ring, lazy kicks.
                        SetDrive(limb, limb.IsArm ? 320f : 240f, 20f);
                        SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 55f, 70f) : LegPose(limb, 20f + 12f * Mathf.Sin(t * 2f + ph), 10f));
                        break;
                    case VictimState.Distressed:
                        SetDrive(limb, limb.IsArm ? 400f : 520f, 24f);
                        if (limb.Kind == LimbKind.ArmR)
                            SetTarget(limb.Joint, ArmPose(limb, 150f + 18f * Mathf.Sin(t * 7f), 10f)); // waving
                        else if (limb.IsArm)
                            SetTarget(limb.Joint, ArmPose(limb, 65f + 25f * Mathf.Sin(t * 3f + ph), 20f + 20f * Mathf.Sin(t * 3f)));
                        else
                            SetTarget(limb.Joint, LegPose(limb, 25f + 22f * Mathf.Sin(t * 2.6f + ph), 12f)); // treading water
                        break;
                    case VictimState.Panicking:
                        SetDrive(limb, limb.IsArm ? 640f : 720f, 20f);
                        if (limb.IsArm)
                            SetTarget(limb.Joint, ArmPose(limb, 115f + 55f * Mathf.Sin(t * 9f + ph), 30f + 55f * Mathf.Sin(t * 6.3f + ph * 1.7f)));
                        else
                            SetTarget(limb.Joint, LegPose(limb, 35f + 38f * Mathf.Sin(t * 8f + ph), 15f + 10f * Mathf.Sin(t * 5f + ph)));
                        break;
                    case VictimState.Drowning:
                        // Reaching up for the surface, getting weaker.
                        SetDrive(limb, limb.IsArm ? 220f : 160f, 16f);
                        if (limb.IsArm)
                            SetTarget(limb.Joint, ArmPose(limb, 155f + 12f * Mathf.Sin(t * 4f + ph), 25f + 25f * Mathf.Sin(t * 4.7f + ph)));
                        else
                            SetTarget(limb.Joint, LegPose(limb, 20f + 15f * Mathf.Sin(t * 3f + ph), 10f));
                        break;
                    case VictimState.Saved when _wading:
                    case VictimState.Fine when _wading:
                        // Walking out of the water (arms up while celebrating).
                        SetDrive(limb, limb.IsArm ? 300f : 520f, 24f);
                        float stride = Mathf.Sin(t * 6f + (limb.Side > 0f ? 0f : Mathf.PI));
                        if (limb.IsArm)
                            SetTarget(limb.Joint, state == VictimState.Saved ? ArmPose(limb, 160f + 12f * Mathf.Sin(t * 8f + ph), 10f) : ArmPose(limb, 10f, -25f * stride));
                        else
                            SetTarget(limb.Joint, LegPose(limb, 25f * stride, 4f));
                        break;
                    case VictimState.Saved:
                        // Arms up, legs out: sitting on the sand, celebrating.
                        SetDrive(limb, limb.IsArm ? 360f : 440f, 28f);
                        SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 160f + 12f * Mathf.Sin(t * 8f + ph), 10f) : LegPose(limb, 85f, 12f));
                        break;
                    default:
                        // Fine: sitting, leaning back on the hands.
                        SetDrive(limb, limb.IsArm ? 280f : 440f, 28f);
                        SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 22f, -35f) : LegPose(limb, 85f, 12f));
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ debugging

        public string DebugState => _wading ? $"wading toward {_wadeDirection:F2} (ground {_groundY:F2})" : "";

        /// <summary>Where each limb points in torso space (checks that poses and joint limits agree).</summary>
        public string DescribeLimbs()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Limb limb in _limbs)
            {
                Vector3 down = transform.InverseTransformDirection(-limb.Body.transform.up);
                sb.Append($"  {limb.Kind}: points {down:F2} (spring {limb.Spring:F0})\n");
            }
            return sb.ToString();
        }
    }
}
