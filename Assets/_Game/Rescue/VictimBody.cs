using System;
using PleaseDontDrown.Avatars;
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
        [Tooltip("The cartoon body; its arms and legs follow the physics limbs.")]
        [SerializeField] private AvatarRig _avatar;
        [Tooltip("Spring bones (feminine figures): kicked by CPR compressions and breaths.")]
        [SerializeField] private AvatarJiggle _jiggle;
        [Tooltip("Torso-space height of the hips (where the leg joints are).")]
        [SerializeField] private float _hipY = -0.3f;
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

        private float _dunk;               // 0..1, how far under the current dunk pushes the head
        private float _nextDunkAt;
        private float _dunkUntil;
        private float _nextCryAt;
        private float _nextSplashAt;
        private float _squishAt = float.NegativeInfinity;
        private float _nextFloatScan;
        private float _nextWadeScan;
        private bool _wading;
        private bool _walkingAway;
        private Vector3 _wadeDirection = Vector3.forward;
        private float _groundY;
        private int _voice;
        private float _breathAt = float.NegativeInfinity;
        private float _punchAt = float.NegativeInfinity;
        private float _punchSide = 1f;
        private bool _punchLanded = true;
        private GameObject _stump;
        private float _nextBleed;
        private float _zapAt = float.NegativeInfinity;
        private float _layDownUntil = float.NegativeInfinity;
        private float _nextLyingScan;
        private Collider[] _allColliders = Array.Empty<Collider>();
        private readonly System.Collections.Generic.HashSet<Collider> _passThrough = new(); // players' bodies we let walk over us

        /// <summary>Where the mouth and nose are: underwater here = no air.</summary>
        public Vector3 HeadPosition => transform.TransformPoint(_headLocal);
        /// <summary>Middle of the chest, where CPR hands go.</summary>
        public Vector3 ChestPoint => transform.position + transform.forward * 0.17f + transform.up * 0.12f;
        /// <summary>The mouth (rescue breaths).</summary>
        public Vector3 MouthPoint => transform.TransformPoint(_headLocal + new Vector3(0f, -0.03f, 0.13f));
        public bool IsBuilt => _avatar != null && _avatar.IsBuilt;
        /// <summary>The float (life ring...) this person is holding onto, if any. Found on every machine.</summary>
        public Floatable HeldFloat { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _sync = GetComponent<ItemSync>();
            _item = GetComponent<Item>();
            _brain = GetComponent<VictimBrain>();
            _buoyancy = GetComponent<Buoyancy>();
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
            var colliders = new System.Collections.Generic.List<Collider>(GetComponentsInChildren<Collider>(true));
            foreach (Limb limb in _limbs) colliders.AddRange(limb.Body.GetComponentsInChildren<Collider>(true));
            colliders.RemoveAll(c => c.isTrigger);
            _allColliders = colliders.ToArray();
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

        /// <summary>Same look on every machine (the host picks it from the seed): body, clothes, hat, glasses, floaties, voice.</summary>
        public void ApplyLooks(AvatarLook look, int seed)
        {
            // Voices 0-1 are the lower ones, 2-3 the higher ones.
            _voice = look.Feminine ? 2 + Mathf.Abs(seed % 2) : Mathf.Abs(seed % 2);
            if (_avatar == null) return;
            _avatar.Build(look);
            // Hips of the cartoon body on the torso's hip joints.
            _avatar.transform.localPosition = new Vector3(0f, _hipY - _avatar.HipHeight, 0f);
            _avatar.transform.localRotation = Quaternion.identity;
        }

        /// <summary>A CPR compression: a squish and a thump (local visual, every machine).</summary>
        public void Pump()
        {
            _squishAt = Time.time;
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Thump, 0.9f);
            // Pushed into the chest (lying on the back, the chest faces the body's forward), then bouncing back and
            // swinging about loosely for a moment.
            if (_jiggle != null) _jiggle.Shake(-transform.forward * 1.5f, transform.right * 0.9f + transform.up * 0.4f, 2.4f, 1.3f);
        }

        /// <summary>A rescue breath, mouth to mouth: a kiss, and the chest rises (local visual, every machine).</summary>
        public void RescueBreath()
        {
            _breathAt = Time.time;
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Kiss, 0.9f);
            if (_jiggle != null) _jiggle.Shake(transform.forward * 0.7f, transform.right * 0.3f, 1.6f, 1.2f);
            FloatingText.Spawn(HeadPosition + Vector3.up * 0.35f, "MMMPPPH", new Color(1f, 0.6f, 0.75f), 0.7f, 1.1f);
        }

        /// <summary>How long after the press the hand lands (the swing: Player.FirstPersonArms.PlaySlap, the avatar's punch).</summary>
        public const float SlapLands = 0.2f;

        /// <summary>
        /// Slapped awake (men's CPR): when the hand lands the head snaps away from it with a smack.
        /// <paramref name="push"/> is the way the hand is travelling (world), zero for either side.
        /// </summary>
        public void Punched(Vector3 push = default)
        {
            _punchAt = Time.time + SlapLands;
            _punchLanded = false;
            Transform head = _avatar != null && _avatar.IsBuilt ? _avatar[AvatarRig.Bone.Head] : null;
            _punchSide = push != Vector3.zero && head != null ? (Vector3.Dot(head.right, push) < 0f ? 1f : -1f) : Random.value < 0.5f ? -1f : 1f;
        }

        /// <summary>Defibrillator shock: the whole body jumps.</summary>
        public void Zapped()
        {
            _zapAt = Time.time;
            _squishAt = Time.time;
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Zap, 1f);
            if (_jiggle != null) _jiggle.Shake(transform.forward * 1.2f, transform.right * 0.8f, 2f, 1f);
            FloatingText.Spawn(ChestPoint + Vector3.up * 0.4f, "BZZZT!", new Color(0.6f, 0.9f, 1f), 1.3f, 1.2f);
            if (!_rb.isKinematic) _rb.AddForce(Vector3.up * 2.5f, ForceMode.VelocityChange);
        }

        /// <summary>
        /// Put down with a plain drop (G) on land: laid on the back on the sand in front of the lifeguard, across
        /// their view (head to their right), ready for CPR. Called by the holder's hands just before letting go.
        /// Returns false in the water (dropped there, they just float).
        /// </summary>
        public bool LayDown(PlayerHub holder)
        {
            if (holder == null) return false;
            Vector3 feet = holder.transform.position;
            Vector3 ahead = Vector3.ProjectOnPlane(holder.Head != null ? holder.Head.forward : holder.transform.forward, Vector3.up);
            ahead = ahead.sqrMagnitude > 1e-4f ? ahead.normalized : Vector3.forward;
            Vector3 spot = feet + ahead * 0.95f;
            if (Shore.WaterDepthAt(spot + Vector3.up * 0.3f) > 0.3f) return false;
            float ground = Shore.GroundHeightAt(spot + Vector3.up * 1.5f);
            spot.y = (float.IsNaN(ground) ? feet.y : ground) + 0.17f;
            Quaternion onBack = Quaternion.LookRotation(Vector3.up, Vector3.Cross(Vector3.up, ahead)); // chest up, head to the right

            Quaternion turn = onBack * Quaternion.Inverse(_rb.rotation);
            foreach (Limb limb in _limbs)
            {
                // Limbs come along, turned with the torso, so the joints don't yank.
                Vector3 local = limb.Body.position - _rb.position;
                limb.Body.position = spot + turn * local;
                limb.Body.rotation = turn * limb.Body.rotation;
                limb.Body.transform.SetPositionAndRotation(limb.Body.position, limb.Body.rotation);
                if (!limb.Body.isKinematic) limb.Body.linearVelocity = limb.Body.angularVelocity = Vector3.zero;
            }
            _rb.position = spot;
            _rb.rotation = onBack;
            transform.SetPositionAndRotation(spot, onBack);
            if (!_rb.isKinematic) _rb.linearVelocity = _rb.angularVelocity = Vector3.zero;
            _lastRootPosition = spot;
            _layDownUntil = Time.time + 1.5f;
            return true;
        }

        /// <summary>Host (simulator): move the body somewhere (a hospital bed), limbs follow.</summary>
        public void ServerPlaceAt(Vector3 position, Quaternion rotation)
        {
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            if (!_rb.isKinematic) _rb.linearVelocity = _rb.angularVelocity = Vector3.zero;
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
                offset = new Vector3(0.42f, -0.48f, 1.0f);
                rotation = Quaternion.LookRotation(Vector3.up, Vector3.back);
                pitchFollow = 0f;
            }
            else
            {
                // Carried across the arms, face up, head to the right.
                offset = new Vector3(0.05f, -0.64f, 0.62f); // close to the chest, in both arms
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
            UpdatePassThrough();

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

            if (!state.IsStruggling() || _item.IsHeld || _brain.IsSilent)
                return; // the silent ones just slip under

            // Calling for help (only with the head out of the water).
            if (!headUnder && state != VictimState.Drowning && Time.time >= _nextCryAt && HeldFloat == null)
            {
                _nextCryAt = Time.time + Random.Range(2.8f, 5.5f);
                if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Cry(_voice), 0.9f);
                string[] own = _brain.Shouts; // story gags: their own thing to yell, read a bit longer
                if (own.Length > 0 && Random.value < 0.85f)
                    FloatingText.Spawn(HeadPosition + Vector3.up * 0.45f, own[Random.Range(0, own.Length)], new Color(1f, 0.95f, 0.8f), 0.75f, 2f);
                else if (Random.value < 0.6f)
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
            if (_avatar == null) return;
            float eyes = state is VictimState.Unconscious or VictimState.Lost ? 0.1f
                : state is VictimState.Panicking or VictimState.Drowning ? 1.45f : state == VictimState.Injured ? 0.5f : 1f;
            float mouth = state switch
            {
                VictimState.Panicking => 0.75f + 0.25f * Mathf.Sin(Time.time * 11f),
                VictimState.Drowning => 0.9f,
                VictimState.Distressed => 0.45f,
                VictimState.Saved => 0.55f,
                VictimState.Injured => 0.6f + 0.2f * Mathf.Sin(Time.time * 7f),
                _ => 0.05f
            };
            float brows = state.IsStruggling() || state == VictimState.Injured ? -1f : state == VictimState.Saved ? 0.2f : 0f;
            if (_brain.IsSilent && state.IsStruggling()) { eyes = 1.2f; mouth = 0.3f; brows = -0.4f; } // glassy, quiet
            _avatar.SetExpression(eyes, mouth, brows);
        }

        /// <summary>The cartoon body's arms and legs follow the simulated limbs (elbows and knees bend by mood).</summary>
        private void LateUpdate()
        {
            if (_avatar == null || !_avatar.IsBuilt) return;
            VictimState state = _brain.State;
            float armBend, legBend;
            if (!state.IsConscious()) { armBend = 8f; legBend = 6f; }
            else if (_item.IsHeld) { armBend = 25f; legBend = 30f; }
            else if (state.IsStruggling()) { armBend = 30f + 15f * Mathf.Sin(Time.time * 5f); legBend = 35f + 20f * Mathf.Sin(Time.time * 4f); }
            else if (_wading || _walkingAway) { armBend = 18f; legBend = 22f; }
            else { armBend = 15f; legBend = 4f; }

            foreach (Limb limb in _limbs)
            {
                AvatarRig.Bone upper = limb.Kind switch
                {
                    LimbKind.ArmL => AvatarRig.Bone.UpperArmL, LimbKind.ArmR => AvatarRig.Bone.UpperArmR,
                    LimbKind.LegL => AvatarRig.Bone.ThighL, _ => AvatarRig.Bone.ThighR
                };
                AvatarRig.Bone lower = upper + 1;
                _avatar[upper].rotation = limb.Body.transform.rotation; // both hang down their local -Y at rest
                _avatar[lower].localRotation = limb.IsArm ? Quaternion.Euler(-armBend, 0f, 0f) : Quaternion.Euler(legBend, 0f, 0f);
            }

            // Hands: limp when out cold, clawing at the water in a panic, reaching open-handed while going under.
            HandPose hands = state switch
            {
                VictimState.Panicking => HandPose.Lerp(HandPose.Wave, HandPose.Cup, 0.5f + 0.5f * Mathf.Sin(Time.time * 9f)),
                VictimState.Drowning => HandPose.Wave,
                VictimState.Distressed => HandPose.Wave,
                VictimState.Saved => HandPose.Wave,
                _ => HandPose.Relaxed
            };
            _avatar.LeftHand?.Pose(hands);
            _avatar.RightHand?.Pose(hands);

            // Punched: the head snaps aside and wobbles back.
            float sincePunch = Time.time - _punchAt;
            if (sincePunch >= 0f && !_punchLanded)
            {
                _punchLanded = true;
                if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Punch, 1f);
                FloatingText.Spawn(HeadPosition + Vector3.up * 0.3f, "SLAP!", new Color(1f, 0.85f, 0.25f), 1.1f, 1f);
            }
            if (sincePunch >= 0f && sincePunch < 0.6f)
            {
                float k = Mathf.Exp(-sincePunch * 7f) * Mathf.Cos(sincePunch * 22f);
                _avatar[AvatarRig.Bone.Head].localRotation = _avatar.RestRotation(AvatarRig.Bone.Head) * Quaternion.Euler(0f, 55f * k * _punchSide, 20f * k * _punchSide);
            }
            else if (state == VictimState.Unconscious)
                _avatar[AvatarRig.Bone.Head].localRotation = _avatar.RestRotation(AvatarRig.Bone.Head) * Quaternion.Euler(-12f, 0f, 0f); // chin up (airway open)
            else
                _avatar[AvatarRig.Bone.Head].localRotation = _avatar.RestRotation(AvatarRig.Bone.Head);

            // Shark bite: the left leg is gone below the knee. The shin and foot shrink to nothing at the knee, where a
            // raw stump shows instead, and it bleeds until the infirmary has seen to it.
            bool lost = _brain.HasLostLeg;
            _avatar[AvatarRig.Bone.ShinL].localScale = lost ? Vector3.one * 0.001f : Vector3.one;
            if (lost && _stump == null) _stump = BuildStump();
            if (_stump != null && _stump.activeSelf != lost) _stump.SetActive(lost);
            if (lost && state is not (VictimState.Saved or VictimState.Lost) && Time.time >= _nextBleed)
            {
                _nextBleed = Time.time + 0.14f;
                BloodFx.Bleed(_avatar[AvatarRig.Bone.ShinL].position);
            }
        }

        /// <summary>What the shark left at the knee: torn flesh, ragged skin, the end of the bone.</summary>
        private GameObject BuildStump()
        {
            float s = _avatar.Scale, r = 0.066f * s;
            var kit = new AvatarMeshKit();
            kit.SetBone(0, Matrix4x4.identity);
            var flesh = new Color(0.55f, 0.04f, 0.05f);
            var dark = new Color(0.33f, 0.02f, 0.03f);
            kit.Ellipsoid(new Vector3(0f, 0.012f, 0f), new Vector3(r, 0.04f * s, r), flesh, null, 12, 6);
            kit.Ellipsoid(new Vector3(0f, -0.012f * s, 0f), new Vector3(r * 0.8f, 0.03f * s, r * 0.8f), dark, null, 10, 6);
            for (int i = 0; i < 7; i++) // ragged flaps hanging round the edge
            {
                float a = i * (Mathf.PI * 2f / 7f) + 0.3f;
                float hang = (0.035f + 0.02f * ((i * 37) % 5) / 4f) * s;
                kit.Ellipsoid(new Vector3(Mathf.Cos(a) * r * 0.86f, -hang * 0.6f, Mathf.Sin(a) * r * 0.86f), new Vector3(0.02f * s, hang, 0.011f * s), i % 2 == 0 ? flesh : dark,
                    Quaternion.Euler(Mathf.Sin(a) * 14f, -a * Mathf.Rad2Deg, -Mathf.Cos(a) * 14f), 6, 4);
            }
            kit.Frustum(new Vector3(0f, -0.07f * s, 0f), 0.016f * s, 0.02f * s, 0.07f * s, new Color(0.93f, 0.9f, 0.82f), null, null, 8); // the bone
            var stump = new GameObject("Stump");
            stump.AddComponent<MeshFilter>().sharedMesh = kit.ToMesh("Stump", new[] { Matrix4x4.identity });
            stump.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
            // On the thigh, at the knee (the shin bone's own place): it swings with the leg.
            Transform shin = _avatar[AvatarRig.Bone.ShinL];
            stump.transform.SetParent(_avatar[AvatarRig.Bone.ThighL], false);
            stump.transform.localPosition = shin.localPosition;
            return stump;
        }

        private void UpdateSquish()
        {
            if (_visual == null) return;
            float t = (Time.time - _squishAt) / 0.28f;
            float k = t < 1f ? Mathf.Sin(t * Mathf.PI) * (1f - t) : 0f;
            // A rescue breath puffs the chest up for a moment.
            float b = (Time.time - _breathAt) / 0.9f;
            float rise = b < 1f ? Mathf.Sin(b * Mathf.PI) * 0.07f : 0f;
            // Zap: a quick shiver.
            float z = Time.time - _zapAt;
            float shiver = z < 0.45f ? Mathf.Sin(z * 90f) * 0.04f * (1f - z / 0.45f) : 0f;
            _visual.localScale = new Vector3(1f + 0.12f * k + rise * 0.5f + shiver, 1f - 0.16f * k + shiver, 1f + 0.12f * k + rise);
        }

        // ------------------------------------------------------------------ physics step (every machine)

        private void FixedUpdate()
        {
            FollowTeleports();
            VictimState state = _brain.State;
            PoseLimbs(state);

            bool struggling = state.IsStruggling() && !_item.IsHeld;
            bool recovered = _brain.HasBeenRescued && !_brain.HasLostLeg && (state is VictimState.Fine or VictimState.Saved);
            _sync.KeepAwake = struggling || (recovered && (_wading || _walkingAway || transform.up.y < 0.92f));
            if (_buoyancy != null)
                _buoyancy.Density = state.IsConscious() ? _consciousDensity : _unconsciousDensity;

            if (_rb.isKinematic || _item.IsHeld)
                return; // someone else simulates the torso, or a lifeguard's hands steer it

            _mode = "-";
            if (Time.time < _layDownUntil)
            {
                _mode = "laydown";
                KeepOnBack(); // just put down: settle on the back
            }
            else if (struggling && WaterSurface.Exists)
            {
                _mode = "swim";
                Swim(state);
            }
            else if (state is VictimState.Fine or VictimState.Saved)
            {
                _mode = "fine";
                if (recovered) StandAndWalk(_wading || _walkingAway);
                else if (_wading) WadeAshore();
                else SitUp();
            }
            else if (!state.IsConscious() && !_brain.IsAshore && WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0.2f)
            {
                _mode = "topple";
                Topple(); // (not in the shallows: CPR needs them on their back)
            }
            else if ((!state.IsConscious() || state == VictimState.Injured) && _rb.linearVelocity.sqrMagnitude < 4f)
            {
                // Passed out (or holding a bitten leg) on the sand: flat on the back and staying put, ready for CPR.
                // (Sitting, on the side or face down: all rolled onto the back the same way.)
                _mode = "onback";
                KeepOnBack();
            }
        }

        /// <summary>
        /// Lying on land: roll onto the back if face down or on the side, and don't spin or slide about (a nudge
        /// from someone kneeling by them used to turn them round mid-CPR).
        /// </summary>
        private void KeepOnBack()
        {
            Vector3 chest = transform.forward;
            Vector3 axis = Vector3.Cross(chest, Vector3.up);
            float error = Vector3.Angle(chest, Vector3.up) * Mathf.Deg2Rad;
            if (axis.sqrMagnitude < 1e-4f) axis = chest.y < 0f ? transform.up : Vector3.zero; // face down: roll over along the spine
            // Strong: the floppy limbs (an arm or a leg underneath) wedge the body, so a gentle push leaves them on the side.
            Vector3 torque = axis.normalized * Mathf.Min(error, 1.6f) * 70f;
            Vector3 w = _rb.angularVelocity;
            _rb.AddTorque(torque - w * 12f - Vector3.Project(w, Vector3.up) * 10f, ForceMode.Acceleration); // extra brake on turning round
            if (error > 0.35f)
            {
                // Lift a touch while rolling over, so the torso isn't pinned by its own limbs.
                float ground = Shore.GroundHeightAt(_rb.position + Vector3.up * 0.5f);
                if (float.IsNaN(ground)) ground = _rb.position.y - 0.17f;
                float lift = Mathf.Clamp((ground + 0.3f - _rb.position.y) * 30f - _rb.linearVelocity.y * 6f, 0f, 14f);
                _rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
            }
            if (error < 0.5f)
            {
                Vector3 v = _rb.linearVelocity;
                _rb.AddForce(-new Vector3(v.x, 0f, v.z) * 6f, ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// Someone lying on the sand (out cold, being given CPR): the lifeguards' feet walk over them instead of
        /// kicking them about. Checked every machine, from the synced state.
        /// </summary>
        private void UpdatePassThrough()
        {
            if (Time.time < _nextLyingScan) return;
            _nextLyingScan = Time.time + 0.25f;
            VictimState state = _brain.State;
            bool lying = !_item.IsHeld && _brain.IsAshore && transform.forward.y > 0.4f &&
                         (!state.IsConscious() || state == VictimState.Injured || Time.time < _layDownUntil);
            foreach (PlayerHub player in PlayerHub.All)
            {
                if (player == null) continue;
                // Every solid part of the lifeguard (body capsule, feet, head...), not just the main capsule.
                foreach (Collider body in player.GetComponentsInChildren<Collider>())
                {
                    if (body == null || body.isTrigger) continue;
                    if (lying)
                    {
                        // Re-applied every scan (a holder's own collision handling may have switched it back on).
                        foreach (Collider c in _allColliders)
                            if (c != null) Physics.IgnoreCollision(c, body, true);
                        _passThrough.Add(body);
                    }
                    else if (_passThrough.Remove(body) && _item.Holder != player)
                    {
                        foreach (Collider c in _allColliders)
                            if (c != null) Physics.IgnoreCollision(c, body, false);
                    }
                }
            }
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
            if (_brain.IsSilent && HeldFloat == null) depth = _floatDepth + 0.22f + _dunkDepth * _dunk; // mouth at the waterline, head tipped back
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
            else if (state == VictimState.Panicking && !_brain.IsSilent)
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
                _walkingAway = false;
                return;
            }

            Vector3 p = transform.position;
            float depth = Shore.WaterDepthAt(p);
            bool recovered = _brain.HasBeenRescued && !_brain.HasLostLeg;
            _wading = depth > 0.12f && depth < Shore.DeepDepth + 0.4f;
            _walkingAway = recovered && depth <= 0.12f && depth > -0.85f;
            if (!_wading && !recovered) return;

            float ground = Shore.GroundHeightAt(p + Vector3.up * 2f);
            if (!float.IsNaN(ground)) _groundY = ground;
            else if (_wading) _groundY = WaterSurface.HeightAt(p) - depth;

            // Head inland, up the beach slope. Keep the last direction where the beach levels out.
            float dx = Shore.GroundHeightAt(p + Vector3.right * 1.5f + Vector3.up * 2f) -
                       Shore.GroundHeightAt(p + Vector3.left * 1.5f + Vector3.up * 2f);
            float dz = Shore.GroundHeightAt(p + Vector3.forward * 1.5f + Vector3.up * 2f) -
                       Shore.GroundHeightAt(p + Vector3.back * 1.5f + Vector3.up * 2f);
            var uphill = new Vector3(dx, 0f, dz);
            if (!float.IsNaN(uphill.x) && !float.IsNaN(uphill.z) && uphill.sqrMagnitude > 1e-5f)
                _wadeDirection = uphill.normalized;
        }

        private void WadeAshore() => StandAndWalk(true);

        /// <summary>Lift a rescued person off the sand, right the torso, then let them walk inland.</summary>
        private void StandAndWalk(bool moving)
        {
            Vector3 p = _rb.position;
            Vector3 v = _rb.linearVelocity;
            float lift = Mathf.Clamp((_groundY + 1.2f - p.y) * 65f - v.y * 17f + 10f, -30f, 55f);
            _rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);

            Vector3 tilt = Vector3.Cross(transform.up, Vector3.up);
            if (tilt.sqrMagnitude < 0.001f && transform.up.y < 0f)
                tilt = transform.right; // an upside-down body needs a direction to start rolling
            float turn = Vector3.SignedAngle(transform.forward, _wadeDirection, Vector3.up) * Mathf.Deg2Rad;
            _rb.AddTorque(tilt * 78f + Vector3.up * (turn * 22f) - _rb.angularVelocity * 14f, ForceMode.Acceleration);

            // They walk the way they face: turn toward the beach first, then set off (no shuffling off backwards or sideways).
            Vector3 ahead = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float go = Mathf.Clamp01((Vector3.Dot(ahead, _wadeDirection) - 0.3f) / 0.5f);
            Vector3 target = moving ? _wadeDirection * ((_wading ? 1.1f : 1.35f) * go) : Vector3.zero;
            Vector3 horizontal = new(v.x, 0f, v.z);
            _rb.AddForce((target - horizontal) * 8f, ForceMode.Acceleration);
        }

        /// <summary>Unrescued beachgoers can stay seated on the sand.</summary>
        private void SitUp()
        {
            if (_rb.linearVelocity.sqrMagnitude > 9f) return;
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
                    if (_brain.IsAshore && !held)
                    {
                        // Out cold on the sand: a soft pull to lying flat (legs straight, arms a little out), so the
                        // limbs don't fold up under the body and wedge it onto its side.
                        SetDrive(limb, 70f, 7f);
                        SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 22f, 0f) : LegPose(limb, 2f, 5f));
                    }
                    else SetDrive(limb, 0f, 3f); // limp
                    continue;
                }

                if (held)
                {
                    // Held: arms along the body, legs a little bent, a bit stiff.
                    SetDrive(limb, 90f, 8f);
                    SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 18f, 5f) : LegPose(limb, 15f, 6f));
                    continue;
                }

                if (_brain.IsSilent && state.IsStruggling() && !onFloat)
                {
                    // Instinctive drowning response: upright, arms out to the sides pressing down on the water,
                    // no waving, no kicking to speak of. Looks like treading water; it isn't.
                    SetDrive(limb, limb.IsArm ? 300f : 200f, 20f);
                    SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 80f + 12f * Mathf.Sin(t * 2.2f + ph), 10f) : LegPose(limb, 6f + 4f * Mathf.Sin(t * 1.5f + ph), 4f));
                    continue;
                }
                if (state == VictimState.Injured)
                {
                    // Lying there clutching the leg, the other one twitching.
                    SetDrive(limb, 200f, 18f);
                    SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 25f, 55f + 10f * Mathf.Sin(t * 5f + ph)) : LegPose(limb, 15f + 10f * Mathf.Sin(t * 3f + ph), 8f));
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
                    case VictimState.Saved when _wading || _walkingAway:
                    case VictimState.Fine when _wading || _walkingAway:
                        // Alternating steps and arm swings while leaving the water.
                        SetDrive(limb, limb.IsArm ? 360f : 650f, 28f);
                        float stride = Mathf.Sin(t * (_wading ? 5f : 7f) + (limb.Side > 0f ? 0f : Mathf.PI));
                        if (limb.IsArm)
                            SetTarget(limb.Joint, state == VictimState.Saved && limb.Kind == LimbKind.ArmR
                                ? ArmPose(limb, 125f + 15f * Mathf.Sin(t * 7f), 10f)
                                : ArmPose(limb, 14f, -30f * stride));
                        else
                            SetTarget(limb.Joint, LegPose(limb, 30f * stride, 5f));
                        break;
                    case VictimState.Saved when _brain.HasBeenRescued && !_brain.HasLostLeg:
                        // A short standing wave after reaching dry sand.
                        SetDrive(limb, limb.IsArm ? 340f : 600f, 28f);
                        SetTarget(limb.Joint, limb.IsArm
                            ? limb.Kind == LimbKind.ArmR ? ArmPose(limb, 120f + 12f * Mathf.Sin(t * 6f), 8f) : ArmPose(limb, 12f, 0f)
                            : LegPose(limb, 3f, 5f));
                        break;
                    case VictimState.Fine when _brain.HasBeenRescued && !_brain.HasLostLeg:
                        // Upright idle until they head back to their sunbed.
                        SetDrive(limb, limb.IsArm ? 320f : 600f, 28f);
                        SetTarget(limb.Joint, limb.IsArm
                            ? ArmPose(limb, 12f, 3f * Mathf.Sin(t * 1.7f + ph))
                            : LegPose(limb, 2f, 5f));
                        break;
                    case VictimState.Saved:
                        SetDrive(limb, limb.IsArm ? 360f : 440f, 28f);
                        SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 160f + 12f * Mathf.Sin(t * 8f + ph), 10f) : LegPose(limb, 85f, 12f));
                        break;
                    default:
                        SetDrive(limb, limb.IsArm ? 280f : 440f, 28f);
                        SetTarget(limb.Joint, limb.IsArm ? ArmPose(limb, 22f, -35f) : LegPose(limb, 85f, 12f));
                        break;                }
            }
        }

        // ------------------------------------------------------------------ debugging

        public string DebugState => (_wading || _walkingAway ? $"walking toward {_wadeDirection:F2} (ground {_groundY:F2})  " : "") +
                                    $"mode {_mode}{(_rb.isKinematic ? " kinematic" : "")}{(_rb.IsSleeping() ? " asleep" : "")} chest {transform.forward:F2}";

        private string _mode = "-"; // which physics branch ran last (tests)

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
