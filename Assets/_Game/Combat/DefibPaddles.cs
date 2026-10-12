using PleaseDontDrown.Avatars;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// The defibrillator's two paddles (every machine). They sit in their places on the case's lid (carried, it's just
    /// the case). A shock, given with the case on the ground beside the casualty (<see cref="Defibrillator"/>): the
    /// lifeguard kneels, a paddle in each hand on a cable from the case, both go down on the chest, the shock, back up
    /// and onto the lid again (<see cref="UseFrom"/>, <see cref="PlayShock"/>).
    /// The hands follow <see cref="GetGrips"/> (PlayerHands asks for them, see <see cref="ActiveFor"/>) and the paddles
    /// follow the hands, so the two never part. The paddle is the model's own (Resources/Props/DefibPaddle.glb, split
    /// off the case by ArtSource/Tools/split_defibrillator.py): origin at the middle of its contact plate, handle up (+y).
    /// </summary>
    [DefaultExecutionOrder(200)] // after the third-person animator (50) and the first-person arms (100)
    public class DefibPaddles : MonoBehaviour
    {
        private const float PaddleScale = 1.35f;     // the model's paddles are toy-sized next to hands (7 cm)
        private const float HandleTop = 0.05f * PaddleScale; // the palm on the handle: this far above the plate
        // On the lid (model space, as the original model had them) and where their cables leave the case.
        private static readonly Vector3 RestRight = new(-0.12f, 0.056f, 0.09f), RestLeft = new(0.12f, 0.056f, 0.09f);
        private static readonly Vector3 PortRight = new(-0.03f, 0.065f, 0.03f), PortLeft = new(0.03f, 0.065f, 0.03f);

        // The shock, seconds after it starts: down to just over the chest, onto it, the shock, back up, back to hold.
        public const float ShockAt = 0.5f;
        private const float HoverEnd = 0.35f, PressEnd = 0.5f, LiftStart = 0.85f, LiftEnd = 1.2f, Done = 1.55f;

        private Item _item;
        private Transform _model;
        private Vector3 _modelRestPos;
        private Quaternion _modelRestRot;
        private Transform _paddleR, _paddleL;
        private LineRenderer _cableR, _cableL;
        private Renderer[] _caseRenderers;
        private bool _parked;
        private PlayerHub _user;           // who's giving a shock with them now (the case on the ground)
        private static readonly System.Collections.Generic.List<DefibPaddles> All = new();
        private float _shockStart = float.NegativeInfinity;
        private Vector3 _chest;
        private Light _flash;

        private static GameObject _paddlePrefab;

        private void Awake()
        {
            _item = GetComponent<Item>();
            foreach (Transform child in transform)
                if (child.name.StartsWith("Model_")) _model = child;
            if (_model == null) _model = transform;
            _modelRestPos = _model.localPosition;
            _modelRestRot = _model.localRotation;
            _caseRenderers = _model.GetComponentsInChildren<Renderer>();
            if (_paddlePrefab == null) _paddlePrefab = Resources.Load<GameObject>("Props/DefibPaddle");
            _paddleR = MakePaddle("PaddleR", out _cableR);
            _paddleL = MakePaddle("PaddleL", out _cableL);
            Park();
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>Paddles this player is giving a shock with right now (their hands go on them), if any.</summary>
        public static DefibPaddles ActiveFor(PlayerHub player)
        {
            if (player == null) return null;
            foreach (DefibPaddles p in All)
                if (p._user == player && p.InUse) return p;
            return null;
        }

        private bool InUse => _user != null && Time.time - _shockStart <= Done;

        /// <summary>This player takes the paddles off the case for a shock (then <see cref="PlayShock"/>).</summary>
        public void UseFrom(PlayerHub user) => _user = user;

        private Transform MakePaddle(string name, out LineRenderer cable)
        {
            Transform paddle = _paddlePrefab != null ? Instantiate(_paddlePrefab).transform : GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            paddle.name = name;
            foreach (Collider c in paddle.GetComponentsInChildren<Collider>()) Destroy(c);
            paddle.SetParent(_model, false);
            paddle.localScale = Vector3.one * PaddleScale;

            var go = new GameObject(name + "Cable");
            go.transform.SetParent(transform, false);
            cable = go.AddComponent<LineRenderer>();
            cable.positionCount = 12;
            cable.useWorldSpace = true;
            cable.widthMultiplier = 0.012f;
            cable.numCapVertices = 2;
            cable.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cable.sharedMaterial = CableMaterial;
            cable.enabled = false;
            return paddle;
        }

        private static Material _cableMaterial;
        private static Material CableMaterial
        {
            get
            {
                if (_cableMaterial != null) return _cableMaterial;
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _cableMaterial = new Material(shader) { name = "DefibCable", color = new Color(0.08f, 0.08f, 0.09f) };
                if (_cableMaterial.HasProperty("_Smoothness")) _cableMaterial.SetFloat("_Smoothness", 0.35f);
                return _cableMaterial;
            }
        }

        /// <summary>Back in their places on the lid; the case back in the item.</summary>
        private void Park()
        {
            _model.SetLocalPositionAndRotation(_modelRestPos, _modelRestRot);
            _paddleR.SetLocalPositionAndRotation(RestRight, Quaternion.identity);
            _paddleL.SetLocalPositionAndRotation(RestLeft, Quaternion.identity);
            _cableR.enabled = _cableL.enabled = false;
            SetCaseVisible(true);
            _shockStart = float.NegativeInfinity;
            _user = null;
            _parked = true;
        }

        private void SetCaseVisible(bool visible)
        {
            foreach (Renderer r in _caseRenderers)
                if (r != null) r.enabled = visible;
        }

        // ------------------------------------------------------------------ the shock

        /// <summary>A shock on this chest starts now (the holder's machine at once, everyone else when told).</summary>
        public void PlayShock(Vector3 chest)
        {
            _chest = chest;
            _shockStart = Time.time;
        }

        // ------------------------------------------------------------------ where the hands go

        /// <summary>
        /// The hands on the paddle handles (world): held up in front, plates facing ahead, ready; during a shock down
        /// onto the chest, plates flat on it either side of the breastbone.
        /// </summary>
        public PlayerHands.GripKind GetGrips(PlayerHub holder, out HandGrip left, out HandGrip right)
        {
            right = Grip(holder, true);
            left = Grip(holder, false);
            return PlayerHands.GripKind.TwoHands;
        }

        private HandGrip Grip(PlayerHub holder, bool rightHand)
        {
            float side = rightHand ? 1f : -1f;
            Transform frame = holder == null ? transform
                : holder.IsOwner && holder.Look != null && holder.Look.Camera != null ? holder.Look.Camera.transform : holder.Head;
            Vector3 fwd = frame.forward, up = frame.up, across = frame.right;
            // Ready: in front of the chest, plates ahead, handles toward us (palms push forward on them).
            Vector3 ready = frame.position + fwd * 0.46f - up * 0.22f + across * (side * 0.16f);
            var hold = new HandGrip(ready, (up - across * (side * 0.25f)).normalized, (fwd - up * 0.35f).normalized, HandPose.Fist);

            float t = Time.time - _shockStart;
            if (t < 0f || t > Done) return hold;
            // On the chest: flat, handles up, one a little toward the head, one toward the hip.
            Vector3 flatFwd = Vector3.ProjectOnPlane(fwd, Vector3.up);
            flatFwd = flatFwd.sqrMagnitude > 1e-4f ? flatFwd.normalized : Vector3.forward;
            Vector3 flatAcross = Vector3.Cross(Vector3.up, flatFwd);
            Vector3 plate = _chest + flatAcross * (side * 0.1f) + flatFwd * (side * 0.04f);
            var press = new HandGrip(plate + Vector3.up * HandleTop, flatFwd, Vector3.down, HandPose.Fist);
            var hover = press;
            hover.Point += Vector3.up * 0.14f;

            if (t < HoverEnd) return Blend(hold, hover, t / HoverEnd);
            if (t < PressEnd) return Blend(hover, press, (t - HoverEnd) / (PressEnd - HoverEnd));
            if (t < LiftStart)
            {
                // The shock: a jolt through the arms.
                float since = t - ShockAt;
                if (since >= 0f && since < 0.12f) press.Point += Vector3.up * (0.02f * Mathf.Sin(since / 0.12f * Mathf.PI));
                return press;
            }
            if (t < LiftEnd) return Blend(press, hover, (t - LiftStart) / (LiftEnd - LiftStart));
            return Blend(hover, hold, (t - LiftEnd) / (Done - LiftEnd));
        }

        private static HandGrip Blend(HandGrip a, HandGrip b, float k)
        {
            k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            return new HandGrip(Vector3.Lerp(a.Point, b.Point, k), Vector3.Slerp(a.Fingers, b.Fingers, k).normalized,
                Vector3.Slerp(a.Palm, b.Palm, k).normalized, b.Pose);
        }

        // ------------------------------------------------------------------ drawing them

        private void LateUpdate()
        {
            // Carried, or lying there unused: the paddles on the lid.
            if (!InUse || (_item != null && _item.IsHeld))
            {
                if (!_parked) Park();
                return;
            }
            _parked = false;
            PlayerHub user = _user;
            bool firstPerson = user.IsOwner;
            AvatarRig rig = user.Avatar != null ? user.Avatar.Rig : null;
            // The cables come out of the case where it lies.
            Vector3 portR = _model.TransformPoint(PortRight), portL = _model.TransformPoint(PortLeft);
            GetGrips(user, out HandGrip left, out HandGrip right);
            Place(_paddleR, _cableR, right, true, user, rig, firstPerson, portR);
            Place(_paddleL, _cableL, left, false, user, rig, firstPerson, portL);
            UpdateFlash();
        }

        private void Place(Transform paddle, LineRenderer cable, HandGrip grip, bool rightHand, PlayerHub holder, AvatarRig rig,
            bool firstPerson, Vector3 port)
        {
            // Where the palm really is (the hands lag and stretch a little behind their grips): the handle goes there.
            Vector3 palm = grip.Point;
            if (firstPerson)
            {
                if (holder.Arms != null && holder.Arms.TryGetPalm(rightHand, out Vector3 p)) palm = p;
            }
            else if (rig != null && rig.Hand(rightHand) is { } hand && hand.Hand != null)
                palm = hand.Hand.TransformPoint(hand.PalmContact);
            Vector3 handleUp = -grip.Palm;
            Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(grip.Fingers, handleUp), handleUp);
            paddle.SetPositionAndRotation(palm - handleUp * HandleTop, rotation);

            // The cable: from the case down in a sag and up into the back of the paddle.
            cable.enabled = true;
            Vector3 a = port, b = paddle.position + handleUp * (HandleTop * 0.3f);
            float sag = 0.25f + 0.15f * Vector3.Distance(a, b);
            Vector3 mid = (a + b) * 0.5f - Vector3.up * sag;
            for (int i = 0; i < cable.positionCount; i++)
            {
                float t = i / (cable.positionCount - 1f);
                cable.SetPosition(i, Vector3.Lerp(Vector3.Lerp(a, mid, t), Vector3.Lerp(mid, b, t), t));
            }
        }

        // A blue-white flash between the plates at the shock.
        private void UpdateFlash()
        {
            float since = Time.time - _shockStart - ShockAt;
            bool on = since >= 0f && since < 0.12f;
            if (on && _flash == null)
            {
                var go = new GameObject("ShockFlash");
                go.transform.SetParent(transform, false);
                _flash = go.AddComponent<Light>();
                _flash.type = LightType.Point;
                _flash.color = new Color(0.65f, 0.85f, 1f);
                _flash.range = 1.6f;
            }
            if (_flash == null) return;
            _flash.enabled = on;
            if (!on) return;
            _flash.transform.position = (_paddleR.position + _paddleL.position) * 0.5f + Vector3.up * 0.1f;
            _flash.intensity = 2.2f * (1f - since / 0.12f);
        }
    }
}
