using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// A fighter's weapon in their hands (every machine): the Tripo flintlock pistol, musket or cutlass
    /// (Resources/NpcWeapons, made by ArtSource/Tools/tripo_prepare_weapons.py; the origin is the middle of the grip,
    /// +Z the barrel / blade).
    ///
    /// Held exactly the way the players' guns are seen from outside (<see cref="PlayerHands.ThirdPersonHold"/>): the
    /// weapon is placed from the right shoulder (a pistol held out in front at chest height, the musket's butt in the
    /// shoulder pocket, the cutlass low at the side), pointing where the pirate looks, and the hands reach its grips by
    /// arm IK, with the players' finger poses (<c>WeaponGripAuthoring</c>). Where they look: at the lifeguard while
    /// aiming (<see cref="StoryNpc.GunState"/>, the synced aim point), a little down at the ground ahead otherwise and
    /// while reloading. On top: the gun's kick, and the cutlass raised over the shoulder for the wind-up and slashed
    /// down. Knocked down or swimming, the weapon stays in the hand.
    /// </summary>
    [DefaultExecutionOrder(195)] // Update: after StoryNpc set the body's motion. LateUpdate: after the arms (AvatarAnimator 50, AvatarRig 190).
    public class NpcWeaponView : MonoBehaviour
    {
        private static readonly GameObject[] Prefabs = new GameObject[4];
        private static readonly bool[] Loaded = new bool[4];

        // The players' finger poses (WeaponGripAuthoring): the pistol and the rifle hand, the support hand under a
        // fore-end, and the knife's fist.
        private static readonly HandPose PistolHand = new(0.74f, 0.3f, 0f)
            { Index = 0.27f, Middle = 0.7f, Ring = 0.76f, Pinky = 0.8f, IndexLift = 20f, IndexBend = new Vector3(-12f, 28f, 0f) };
        private static readonly HandPose RifleHand = new(0.74f, 0.18f, 0f)
            { Index = 0.28f, Middle = 0.7f, Ring = 0.76f, Pinky = 0.8f, IndexLift = 18f, IndexBend = new Vector3(-12f, 28f, 0f) };
        private static readonly HandPose SupportHand = new(0.52f, 0.68f, 0.04f)
            { Index = 0.46f, Middle = 0.5f, Ring = 0.54f, Pinky = 0.59f, ThumbSwing = 65f };
        private static readonly HandPose BladeHand = new(0.92f, 0.7f, 0f);

        /// <summary>How far down they look with the weapon when they aren't aiming it (degrees).</summary>
        private const float LoweredPitch = 28f;

        private StoryNpc _npc;
        private AvatarAnimator _animator;
        private AvatarRig _rig;

        private NpcWeaponKind _kind;
        private Transform _model, _muzzle, _gripRight, _gripLeft;
        private HandPose _rightPose;
        private float _stockBack, _scale = 1f;

        /// <summary>
        /// The pirates are stylised with big hands: a real-size flintlock (42 cm) vanished in the fist like a toy. The
        /// weapons are drawn bigger than life to match them.
        /// </summary>
        private static float ScaleFor(NpcWeaponKind kind) => kind switch
        {
            NpcWeaponKind.Pistol => 1.5f,
            NpcWeaponKind.Musket => 1.1f,
            _ => 1.15f
        };

        // Held this frame (placed in Update, the arms reach it), and how it sits in the hand for when nothing holds it.
        private bool _held;
        private Vector3 _inHandPosition;
        private Quaternion _inHandRotation = Quaternion.identity;
        private bool _hasInHand;

        private float _raise, _kick;
        private Vector3 _aim;
        private bool _wasAiming;
        private StoryNpc.NpcGunState _lastState;
        private float _swingAt = -1f, _swingWindUp;

        public bool Shown => _model != null && _model.gameObject.activeSelf;

        /// <summary>Where shots come out (null without a gun in hand).</summary>
        public Vector3? Muzzle => Shown && _muzzle != null ? _muzzle.position : null;

        public void Init(StoryNpc npc, AvatarAnimator animator, AvatarRig rig)
        {
            _npc = npc;
            _animator = animator;
            _rig = rig;
        }

        private static GameObject PrefabFor(NpcWeaponKind kind)
        {
            int i = (int)kind;
            if (i <= 0 || i >= Prefabs.Length) return null;
            if (!Loaded[i])
            {
                Loaded[i] = true;
                string file = kind switch
                {
                    NpcWeaponKind.Pistol => "flintlock_pistol",
                    NpcWeaponKind.Musket => "flintlock_musket",
                    _ => "cutlass"
                };
                Prefabs[i] = Resources.Load<GameObject>("NpcWeapons/" + file);
            }
            return Prefabs[i];
        }

        private bool Show(NpcWeaponKind kind, bool show)
        {
            if (show && (_model == null || _kind != kind))
            {
                if (_model != null) Destroy(_model.gameObject);
                _model = null;
                _kind = kind;
                _hasInHand = false;
                GameObject prefab = PrefabFor(kind);
                if (prefab == null) return false;
                _model = Instantiate(prefab).transform;
                _model.name = "Weapon_" + prefab.name;
                _scale = ScaleFor(kind);
                _model.localScale = Vector3.one * _scale;
                foreach (Renderer r in _model.GetComponentsInChildren<Renderer>(true)) r.allowOcclusionWhenDynamic = false;
                foreach (Collider c in _model.GetComponentsInChildren<Collider>(true)) Destroy(c);
                _muzzle = FindChild(_model, "Muzzle") ?? FindChild(_model, "Tip");
                AddGrips();
                _stockBack = 0f;
                foreach (MeshFilter f in _model.GetComponentsInChildren<MeshFilter>())
                {
                    if (f.sharedMesh == null) continue;
                    Bounds b = f.sharedMesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        Vector3 corner = new((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                        _stockBack = Mathf.Max(_stockBack, -(Quaternion.Inverse(_model.rotation) * (f.transform.TransformPoint(corner) - _model.position)).z);
                    }
                }
            }
            if (_model != null && _model.gameObject.activeSelf != show) _model.gameObject.SetActive(show);
            return show && _model != null;
        }

        /// <summary>
        /// The grips, as the players' guns have them (GameSceneBuilder.HandGrips + WeaponGripAuthoring, in the model's
        /// own units, so they grow with it): the right palm against the right side of the grip, fingers along it and
        /// wrapped round; the left hand under the musket's fore-end; the cutlass in a fist, the blade past the thumb.
        /// </summary>
        private void AddGrips()
        {
            Transform Node(string name, Vector3 at, Quaternion rotation)
            {
                var t = new GameObject(name).transform;
                t.SetParent(_model, false);
                t.localPosition = at;
                t.localRotation = rotation;
                return t;
            }

            _gripLeft = null;
            if (_kind == NpcWeaponKind.Cutlass)
            {
                _gripRight = Node("GripRight", new Vector3(0.017f, 0f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.right));
                _rightPose = BladeHand;
                return;
            }
            bool pistol = _kind == NpcWeaponKind.Pistol;
            // (The flintlock's origin is the middle of its handle: the palm goes on its right side, a little low and back.)
            Vector3 right = pistol ? new Vector3(0.022f, -0.03f, -0.02f) : new Vector3(0.024f, -0.012f, 0f);
            float slant = pistol ? 12f : 8f, wrap = pistol ? 15f : 25f;
            Quaternion orientation = Quaternion.Euler(slant, 0f, 0f) * Quaternion.LookRotation(Vector3.forward, Vector3.right);
            Vector3 pivot = right - orientation * Vector3.up * 0.016f;
            Quaternion turn = Quaternion.AngleAxis(wrap, orientation * Vector3.left);
            _gripRight = Node("GripRight", pivot + turn * (right - pivot), turn * orientation);
            _rightPose = pistol ? PistolHand : RifleHand;
            if (pistol) return;
            Transform fore = FindChild(_model, "GripLeft");
            Vector3 under = fore != null ? fore.localPosition : new Vector3(0f, -0.03f, 0.4f);
            fore = fore != null ? fore : Node("GripLeft", under, Quaternion.identity);
            fore.localPosition = under + new Vector3(-0.022f, 0f, 0f);
            fore.localRotation = Quaternion.LookRotation(new Vector3(1f, 0f, 0.28f).normalized, Vector3.down);
            _gripLeft = fore;
        }

        private void OnDestroy()
        {
            if (_model != null) Destroy(_model.gameObject);
        }

        // ------------------------------------------------------------------ events (from StoryNpc's RPCs)

        /// <summary>A shot: the gun kicks up and back.</summary>
        public void OnShot() => _kick = 1f;

        /// <summary>A blow is being wound up, landing in <paramref name="seconds"/>.</summary>
        public void OnWindUp(float seconds)
        {
            if (_kind != NpcWeaponKind.Cutlass) return;
            _swingAt = Time.time;
            _swingWindUp = Mathf.Max(0.15f, seconds);
        }

        /// <summary>True when the weapon plays this gesture itself (the cutlass slash, the gun's kick) instead of the body's.</summary>
        public bool PlaysGesture(AvatarGesture gesture) => Shown && _held &&
            (gesture == AvatarGesture.Shoot || (gesture == AvatarGesture.Punch && _kind == NpcWeaponKind.Cutlass));

        // ------------------------------------------------------------------ held (before the arms are posed)

        private void Update()
        {
            _held = false;
            if (_npc == null || _animator == null || _rig == null || !_rig.IsBuilt) return;
            bool armed = _npc.IsArmed && _npc.WeaponKind != NpcWeaponKind.Fists;
            if (!Show(_npc.WeaponKind, armed)) return;

            float dt = Time.deltaTime;
            AvatarMotion m = _animator.Motion;
            StoryNpc.NpcGunState state = _npc.GunState;
            bool aiming = state == StoryNpc.NpcGunState.Aiming;
            if (aiming && !_wasAiming && _npc.TryGetComponent(out AudioSource audio))
                audio.PlayOneShot(ProceduralAudio.DryFire, 0.8f); // the hammer cocked: a shot is coming
            if (state == StoryNpc.NpcGunState.Reloading && _lastState != state && _npc.TryGetComponent(out AudioSource a2))
                a2.PlayOneShot(ProceduralAudio.Rack, 0.5f);
            _wasAiming = aiming;
            _lastState = state;

            _raise = Mathf.MoveTowards(_raise, aiming ? 1f : 0f, dt * (aiming ? 4.5f : 2.5f));
            _kick = Mathf.MoveTowards(_kick, 0f, dt * 6f);
            Vector3 aimAt = _npc.AimPoint;
            _aim = _aim == Vector3.zero || (aimAt - _aim).sqrMagnitude > 25f ? aimAt : Vector3.Lerp(_aim, aimAt, 1f - Mathf.Exp(-14f * dt));

            bool canHold = m.Pose == AvatarPose.Normal && !m.Swimming && !m.Seated && !m.CarryingPerson;
            if (!canHold) return;
            Transform upperR = _rig[AvatarRig.Bone.UpperArmR], upperL = _rig[AvatarRig.Bone.UpperArmL];
            if (upperR == null || upperL == null) return;
            Vector3 shoulderR = upperR.position, shoulderL = upperL.position;
            float reach = _rig.UpperArmLength + _rig.ForearmLength + _rig.HandLength * 0.5f;

            // Where they look: at the lifeguard while aiming, a little down at the ground ahead otherwise.
            Vector3 ahead = Quaternion.Euler(LoweredPitch, m.FacingYaw, 0f) * Vector3.forward;
            Vector3 atTarget = _aim != Vector3.zero ? (_aim - shoulderR).normalized : ahead;
            Vector3 look = Vector3.Slerp(ahead, atTarget, Mathf.SmoothStep(0f, 1f, _raise));

            Hold(look, shoulderR, shoulderL, reach, out Vector3 position, out Quaternion rotation);
            if (_kind == NpcWeaponKind.Cutlass) Swing(shoulderR, m.FacingYaw, ref position, ref rotation);
            else if (_kick > 0f)
            {
                // The kick: up and back about the grip.
                Vector3 grip = position + rotation * GripLocal;
                rotation *= Quaternion.Euler(-14f * _kick, 0f, 0f);
                position = grip - rotation * GripLocal + rotation * new Vector3(0f, 0.01f, -0.05f) * _kick;
            }
            _model.SetPositionAndRotation(position, rotation);
            _held = true;

            m.Holding = true;
            m.TwoHanded = _gripLeft != null;
            m.Shouldered = _kind == NpcWeaponKind.Musket;
            m.LookPitch = -Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg;
            m.GripRight = new HandGrip(_gripRight.position, _gripRight.forward, -_gripRight.up, _rightPose);
            m.GripLeft = _gripLeft != null
                ? new HandGrip(PlayerHands.ReachableLeftGrip(_gripLeft.position, _model.forward, shoulderL, reach), _gripLeft.forward, -_gripLeft.up, SupportHand)
                : default;
            _animator.Motion = m;
        }

        /// <summary>The right grip in the model's frame (scaled, unrotated).</summary>
        private Vector3 GripLocal => Quaternion.Inverse(_model.rotation) * (_gripRight.position - _model.position);

        /// <summary>
        /// <see cref="PlayerHands.ThirdPersonHold"/>, for a model instead of an item: a long gun's butt in the shoulder
        /// pocket, pivoting there (pulled back if the arm can't reach its grip); a pistol held out in front at chest
        /// height; anything else carried low at the side. The pitch is limited as the players' is.
        /// </summary>
        private void Hold(Vector3 lookForward, Vector3 rightShoulder, Vector3 leftShoulder, float armReach, out Vector3 position, out Quaternion rotation)
        {
            float yaw = Mathf.Atan2(lookForward.x, lookForward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(lookForward.y, -1f, 1f)) * Mathf.Rad2Deg, -PlayerHands.ThirdPersonMaxPitch, PlayerHands.ThirdPersonMaxPitch);
            bool gun = _kind != NpcWeaponKind.Cutlass, longGun = _kind == NpcWeaponKind.Musket;
            if (longGun) pitch = Mathf.Clamp(pitch, -PlayerHands.LongGunMaxPitch, PlayerHands.LongGunMaxPitch);
            Quaternion look = Quaternion.Euler(gun ? pitch : 0f, yaw, 0f);
            // (The players' knife is tipped 60 degrees up; a cutlass that long would cross the face: it points ahead and up a little.)
            rotation = gun ? look : Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-25f, 0f, 0f);
            _model.rotation = rotation; // (GripLocal reads the model's frame)
            Vector3 gripLocal = GripLocal;
            if (longGun)
            {
                Vector3 pocket = rightShoulder + Quaternion.Euler(0f, yaw, 0f) * PlayerHands.StockPocket;
                position = pocket + rotation * new Vector3(0f, 0f, _stockBack + 0.03f); // (+ a little: the stock rests on the coat)
                Vector3 hand = position + rotation * gripLocal;
                float over = Vector3.Distance(hand, rightShoulder) - armReach * 0.9f;
                if (over > 0f) position -= rotation * Vector3.forward * over;
                return;
            }
            Vector3 held = gun ? rightShoulder + look * new Vector3(-0.15f, -0.06f, 0.47f)        // pistol held out
                : rightShoulder + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.08f, -0.44f, 0.22f); // low at the side
            position = held - rotation * gripLocal;
        }

        /// <summary>The cutlass blow: raised back over the right shoulder for the wind-up, slashed across and down, back.</summary>
        private void Swing(Vector3 shoulderR, float yaw, ref Vector3 position, ref Quaternion rotation)
        {
            if (_swingAt < 0f) return;
            Quaternion flat = Quaternion.Euler(0f, yaw, 0f);
            Vector3 gripLocal = GripLocal;
            Vector3 readyGrip = position + rotation * gripLocal;
            Quaternion ready = rotation;
            Quaternion raised = flat * Quaternion.Euler(-120f, 25f, 0f);
            Vector3 raisedGrip = shoulderR + flat * new Vector3(0.16f, 0.16f, -0.02f);
            Quaternion cut = flat * Quaternion.Euler(38f, -50f, 0f);
            Vector3 cutGrip = shoulderR + flat * new Vector3(-0.22f, -0.38f, 0.42f);
            float since = Time.time - _swingAt;
            const float slash = 0.16f, back = 0.45f;
            Vector3 grip;
            if (since < _swingWindUp)
            {
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(since / Mathf.Min(_swingWindUp, 0.35f)));
                grip = Vector3.Lerp(readyGrip, raisedGrip, t);
                rotation = Quaternion.Slerp(ready, raised, t);
            }
            else if (since < _swingWindUp + slash)
            {
                float t = (since - _swingWindUp) / slash;
                t = t * t * (3f - 2f * t);
                grip = Vector3.Lerp(raisedGrip, cutGrip, t);
                rotation = Quaternion.Slerp(raised, cut, t);
            }
            else if (since < _swingWindUp + slash + back)
            {
                float t = Mathf.SmoothStep(0f, 1f, (since - _swingWindUp - slash) / back);
                grip = Vector3.Lerp(cutGrip, readyGrip, t);
                rotation = Quaternion.Slerp(cut, ready, t);
            }
            else
            {
                _swingAt = -1f;
                return;
            }
            position = grip - rotation * gripLocal;
        }

        // ------------------------------------------------------------------ in the hand when nothing holds it

        private void LateUpdate()
        {
            if (!Shown || _rig == null || !_rig.IsBuilt) return;
            HandBones hand = _rig.Hand(true);
            Transform bone = hand != null ? hand.Hand : _rig[AvatarRig.Bone.HandR];
            if (bone == null) return;
            if (_held)
            {
                // Remember how it sits in the hand, for when they're knocked down or swimming.
                _inHandPosition = Quaternion.Inverse(bone.rotation) * (_model.position - bone.position);
                _inHandRotation = Quaternion.Inverse(bone.rotation) * _model.rotation;
                _hasInHand = true;
                return;
            }
            if (!_hasInHand)
            {
                _inHandPosition = Vector3.zero;
                _inHandRotation = Quaternion.identity;
            }
            _model.SetPositionAndRotation(bone.position + bone.rotation * _inHandPosition, bone.rotation * _inHandRotation);
        }

        private static Transform FindChild(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
