using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>The paints the prefab carries for the greybox bodies (indexes into its material list).</summary>
    public enum CreaturePaint
    {
        ParrotGreen, ParrotRed, ParrotYellow, ParrotBlue, Fur, FurFace, GatorDark, GatorBelly, Shell, Skin, Teeth, Eye, Mouth, HatBlack,
        Purple, Suckers, Gold, Panama, Glow
    }

    /// <summary>
    /// Every machine: the creature's body, built from simple shapes for its kind. Hit zones carry their colliders;
    /// the rest is for looks, and the Tripo models draw over it where there is one (Creature.Tripo.cs). Posed each
    /// frame from <see cref="Act"/>.
    /// </summary>
    public partial class Creature
    {
        private CreatureKind _built;
        private Transform _wingL, _wingR, _jaw, _neck, _tail, _armL, _armR, _hat, _droppedHat;
        private float _open, _extend, _flapPhase, _bob, _kleptoLookYaw;
        private bool[] _slapped;

        private void BuildBody(CreatureKind kind)
        {
            if (kind == CreatureKind.None || kind == _built) return;
            if (_model != null) Destroy(_model.gameObject);
            _zones.Clear();
            _built = kind;
            _model = new GameObject("Model").transform;
            _model.SetParent(transform, false);
            switch (kind)
            {
                case CreatureKind.Parrot: BuildParrot(); break;
                case CreatureKind.Monkey: BuildMonkey(); break;
                case CreatureKind.Alligator: BuildGator(); break;
                case CreatureKind.OldSnapper: BuildSnapper(); break;
                case CreatureKind.Kleptopus: BuildKleptopus(); break;
            }
            DressTripo(kind);
            ApplyCousinLook(); // (the Kleptopus's big orange cousin: Creature.Cousin.cs)
        }

        // ------------------------------------------------------------------ shapes

        private Transform Pivot(Transform parent, string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        /// <summary>A shape; with <paramref name="zone"/> it is a hit zone (keeps its collider), otherwise looks only.</summary>
        private Transform Part(Transform parent, PrimitiveType type, string name, Vector3 local, Vector3 scale, CreaturePaint paint,
            string zone = null, float multiplier = 1f, Vector3 euler = default)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            int index = (int)paint;
            var renderer = go.GetComponent<MeshRenderer>();
            if (index < _paints.Length && _paints[index] != null) renderer.sharedMaterial = _paints[index];
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            Collider c = go.GetComponent<Collider>();
            if (zone == null) Destroy(c);
            else
            {
                CreatureZone z = go.AddComponent<CreatureZone>();
                z.Part = zone;
                z.Multiplier = multiplier;
                z.Collider = c;
                _zones.Add(z);
            }
            return go.transform;
        }

        private void BuildParrot()
        {
            // A big macaw: red body, yellow and blue wings, hooked beak. ~0.45 m long. One body zone, a generous one.
            Transform m = _model;
            Part(m, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.18f, 0f), new Vector3(0.2f, 0.18f, 0.2f), CreaturePaint.ParrotRed, "Body", 1f, new Vector3(70f, 0f, 0f));
            Part(m, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.3f, 0.13f), Vector3.one * 0.14f, CreaturePaint.ParrotRed, "Head", 2f);
            Part(m, PrimitiveType.Cube, "Beak", new Vector3(0f, 0.28f, 0.22f), new Vector3(0.05f, 0.07f, 0.06f), CreaturePaint.ParrotYellow, euler: new Vector3(25f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
                Part(m, PrimitiveType.Sphere, "Eye", new Vector3(0.055f * side, 0.32f, 0.17f), Vector3.one * 0.03f, CreaturePaint.Eye);
            Part(m, PrimitiveType.Cube, "Tail", new Vector3(0f, 0.1f, -0.26f), new Vector3(0.08f, 0.02f, 0.3f), CreaturePaint.ParrotBlue, euler: new Vector3(-15f, 0f, 0f));
            _wingL = Pivot(m, "WingL", new Vector3(-0.09f, 0.22f, 0f));
            _wingR = Pivot(m, "WingR", new Vector3(0.09f, 0.22f, 0f));
            Part(_wingL, PrimitiveType.Cube, "Feathers", new Vector3(-0.17f, 0f, -0.02f), new Vector3(0.34f, 0.02f, 0.16f), CreaturePaint.ParrotYellow);
            Part(_wingL, PrimitiveType.Cube, "Tips", new Vector3(-0.3f, 0f, -0.05f), new Vector3(0.14f, 0.021f, 0.13f), CreaturePaint.ParrotBlue);
            Part(_wingR, PrimitiveType.Cube, "Feathers", new Vector3(0.17f, 0f, -0.02f), new Vector3(0.34f, 0.02f, 0.16f), CreaturePaint.ParrotYellow);
            Part(_wingR, PrimitiveType.Cube, "Tips", new Vector3(0.3f, 0f, -0.05f), new Vector3(0.14f, 0.021f, 0.13f), CreaturePaint.ParrotBlue);
        }

        private void BuildMonkey()
        {
            // A cheeky brown monkey on all fours, ~0.7 m tall, long curled tail; the stolen hat rides on its head.
            Transform m = _model;
            Part(m, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.5f, 0f), new Vector3(0.36f, 0.3f, 0.36f), CreaturePaint.Fur, "Body", 1f, new Vector3(70f, 0f, 0f));
            Part(m, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.78f, 0.25f), Vector3.one * 0.28f, CreaturePaint.Fur, "Head", 1.5f);
            Part(m, PrimitiveType.Sphere, "Face", new Vector3(0f, 0.75f, 0.36f), new Vector3(0.2f, 0.18f, 0.1f), CreaturePaint.FurFace);
            foreach (float side in new[] { -1f, 1f })
            {
                Part(m, PrimitiveType.Sphere, "Eye", new Vector3(0.05f * side, 0.8f, 0.4f), Vector3.one * 0.04f, CreaturePaint.Eye);
                Part(m, PrimitiveType.Sphere, "Ear", new Vector3(0.15f * side, 0.82f, 0.22f), new Vector3(0.05f, 0.09f, 0.07f), CreaturePaint.FurFace);
                Part(m, PrimitiveType.Capsule, "Leg", new Vector3(0.12f * side, 0.2f, -0.12f), new Vector3(0.1f, 0.2f, 0.1f), CreaturePaint.Fur);
            }
            _armL = Pivot(m, "ArmL", new Vector3(-0.15f, 0.55f, 0.15f));
            _armR = Pivot(m, "ArmR", new Vector3(0.15f, 0.55f, 0.15f));
            Part(_armL, PrimitiveType.Capsule, "Arm", new Vector3(0f, -0.25f, 0f), new Vector3(0.09f, 0.25f, 0.09f), CreaturePaint.Fur);
            Part(_armR, PrimitiveType.Capsule, "Arm", new Vector3(0f, -0.25f, 0f), new Vector3(0.09f, 0.25f, 0.09f), CreaturePaint.Fur);
            _tail = Pivot(m, "Tail", new Vector3(0f, 0.45f, -0.28f));
            Part(_tail, PrimitiveType.Capsule, "Tail1", new Vector3(0f, 0.12f, -0.12f), new Vector3(0.06f, 0.2f, 0.06f), CreaturePaint.Fur, euler: new Vector3(-50f, 0f, 0f));
            Part(_tail, PrimitiveType.Capsule, "Tail2", new Vector3(0f, 0.38f, -0.2f), new Vector3(0.05f, 0.15f, 0.05f), CreaturePaint.Fur, euler: new Vector3(20f, 0f, 0f));
            _hat = PirateHat(m, new Vector3(0f, 0.95f, 0.24f));
            _hat.gameObject.SetActive(false);
            _droppedHat = PirateHat(transform, Vector3.zero);
            _droppedHat.gameObject.SetActive(false);
        }

        /// <summary>The disguise's black tricorn: a low crown and a wide brim.</summary>
        private Transform PirateHat(Transform parent, Vector3 local)
        {
            Transform hat = Pivot(parent, "PirateHat", local);
            Part(hat, PrimitiveType.Cylinder, "Brim", Vector3.zero, new Vector3(0.42f, 0.015f, 0.42f), CreaturePaint.HatBlack);
            Part(hat, PrimitiveType.Cylinder, "Crown", new Vector3(0f, 0.07f, 0f), new Vector3(0.24f, 0.07f, 0.24f), CreaturePaint.HatBlack);
            Part(hat, PrimitiveType.Sphere, "Skull", new Vector3(0f, 0.08f, 0.12f), Vector3.one * 0.05f, CreaturePaint.Teeth);
            return hat;
        }

        private void BuildGator()
        {
            // ~3.2 m: armoured back, long flat head with a jaw that opens, a heavy tail that sways. Low in the water.
            Transform m = _model;
            Part(m, PrimitiveType.Cube, "Body", new Vector3(0f, 0.25f, 0f), new Vector3(0.75f, 0.35f, 1.5f), CreaturePaint.GatorDark, "Body", 1f);
            Part(m, PrimitiveType.Cube, "Back", new Vector3(0f, 0.46f, 0f), new Vector3(0.6f, 0.1f, 1.4f), CreaturePaint.GatorDark, "Back", 0.5f);
            Part(m, PrimitiveType.Cube, "Belly", new Vector3(0f, 0.1f, 0f), new Vector3(0.7f, 0.06f, 1.4f), CreaturePaint.GatorBelly);
            for (int i = 0; i < 5; i++)
                Part(m, PrimitiveType.Cube, "Scute", new Vector3(0f, 0.53f, -0.6f + i * 0.3f), new Vector3(0.3f, 0.06f, 0.12f), CreaturePaint.GatorDark);
            Transform head = Pivot(m, "Head", new Vector3(0f, 0.3f, 0.75f));
            Part(head, PrimitiveType.Cube, "Snout", new Vector3(0f, 0.04f, 0.45f), new Vector3(0.45f, 0.18f, 0.9f), CreaturePaint.GatorDark, "Head", 1.6f);
            foreach (float side in new[] { -1f, 1f })
                Part(head, PrimitiveType.Sphere, "Eye", new Vector3(0.14f * side, 0.18f, 0.2f), Vector3.one * 0.09f, CreaturePaint.Eye);
            _jaw = Pivot(head, "Jaw", new Vector3(0f, -0.05f, 0.05f));
            Part(_jaw, PrimitiveType.Cube, "LowerJaw", new Vector3(0f, -0.04f, 0.42f), new Vector3(0.42f, 0.08f, 0.85f), CreaturePaint.GatorBelly);
            Part(_jaw, PrimitiveType.Cube, "Teeth", new Vector3(0f, 0.02f, 0.42f), new Vector3(0.38f, 0.04f, 0.8f), CreaturePaint.Teeth);
            Part(_jaw, PrimitiveType.Cube, "Mouth", new Vector3(0f, 0.03f, 0.4f), new Vector3(0.3f, 0.02f, 0.7f), CreaturePaint.Mouth);
            _tail = Pivot(m, "Tail", new Vector3(0f, 0.25f, -0.75f));
            Part(_tail, PrimitiveType.Cube, "Tail1", new Vector3(0f, 0f, -0.45f), new Vector3(0.5f, 0.28f, 0.9f), CreaturePaint.GatorDark, "Tail", 0.7f);
            Part(_tail, PrimitiveType.Cube, "Tail2", new Vector3(0f, 0f, -1.15f), new Vector3(0.3f, 0.2f, 0.6f), CreaturePaint.GatorDark);
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { 0.5f, -0.5f })
                    Part(m, PrimitiveType.Cube, "Leg", new Vector3(0.45f * side, 0.08f, z), new Vector3(0.22f, 0.16f, 0.25f), CreaturePaint.GatorDark);
        }

        private void BuildSnapper()
        {
            // Old Snapper: a giant alligator snapping turtle, a ~4.5 m ridged shell that takes nothing, a hooked beak on
            // a neck it pulls in. Its head only comes out to bite, and stays out gasping after one: hit it then.
            Transform m = _model;
            Part(m, PrimitiveType.Sphere, "Shell", new Vector3(0f, 1.25f, 0f), new Vector3(4.2f, 1.9f, 4.8f), CreaturePaint.Shell, "Shell", 0.05f);
            for (int i = 0; i < 3; i++)
                for (int k = 0; k < 4; k++)
                    Part(m, PrimitiveType.Cube, "Ridge", new Vector3((i - 1) * 1.05f, 2.1f - Mathf.Abs(i - 1) * 0.3f, -1.5f + k), new Vector3(0.35f, 0.4f, 0.55f), CreaturePaint.Shell,
                        euler: new Vector3(0f, 0f, (i - 1) * 25f));
            Part(m, PrimitiveType.Cube, "Plastron", new Vector3(0f, 0.55f, 0f), new Vector3(3.6f, 0.4f, 4f), CreaturePaint.Skin);
            foreach (float side in new[] { -1f, 1f })
                foreach (float z in new[] { 1.4f, -1.3f })
                    Part(m, PrimitiveType.Cylinder, "Leg", new Vector3(1.7f * side, 0.4f, z), new Vector3(0.75f, 0.45f, 0.75f), CreaturePaint.Skin, "Leg", 0.3f);
            _neck = Pivot(m, "Neck", new Vector3(0f, 1.1f, 2f));
            Part(_neck, PrimitiveType.Cylinder, "NeckSkin", new Vector3(0f, 0f, 0.3f), new Vector3(0.9f, 0.5f, 0.9f), CreaturePaint.Skin, euler: new Vector3(90f, 0f, 0f));
            Transform head = Pivot(_neck, "HeadPivot", new Vector3(0f, 0.05f, 0.85f));
            Part(head, PrimitiveType.Cube, "Head", new Vector3(0f, 0.12f, 0.35f), new Vector3(1.15f, 0.75f, 1.1f), CreaturePaint.Skin, "Head", 1f);
            Part(head, PrimitiveType.Cube, "Beak", new Vector3(0f, 0.1f, 0.98f), new Vector3(0.5f, 0.4f, 0.35f), CreaturePaint.Shell, euler: new Vector3(30f, 0f, 0f));
            foreach (float side in new[] { -1f, 1f })
                Part(head, PrimitiveType.Sphere, "Eye", new Vector3(0.45f * side, 0.38f, 0.55f), Vector3.one * 0.16f, CreaturePaint.Eye);
            _jaw = Pivot(head, "Jaw", new Vector3(0f, -0.22f, 0f));
            Part(_jaw, PrimitiveType.Cube, "LowerJaw", new Vector3(0f, -0.08f, 0.45f), new Vector3(1f, 0.22f, 1f), CreaturePaint.Skin);
            Part(_jaw, PrimitiveType.Cube, "Mouth", new Vector3(0f, 0.04f, 0.45f), new Vector3(0.8f, 0.03f, 0.85f), CreaturePaint.Mouth);
            _tail = Pivot(m, "Tail", new Vector3(0f, 0.8f, -2.3f));
            Part(_tail, PrimitiveType.Cube, "TailSkin", new Vector3(0f, 0f, -0.6f), new Vector3(0.5f, 0.35f, 1.3f), CreaturePaint.Skin);
        }

        // The Kleptopus: six arms, each a chain of segments from a root on the body to a tip; the first two can be shot.
        private const int ArmSegments = 9;
        private Transform[][] _arms;
        private Vector3[] _armRoots, _armTips;
        private Transform _glasses, _gun;
        private bool _glowShown;

        private void BuildKleptopus()
        {
            Transform m = _model;
            Part(m, PrimitiveType.Sphere, "Mantle", new Vector3(0f, 2.2f, -0.3f), new Vector3(3.2f, 3.8f, 3.4f), CreaturePaint.Purple, "Head", 1f);
            Part(m, PrimitiveType.Sphere, "Skirt", new Vector3(0f, 0.6f, 0f), new Vector3(3.8f, 1.4f, 3.8f), CreaturePaint.Purple, "Body", 0.4f);
            foreach (float side in new[] { -1f, 1f })
                Part(m, PrimitiveType.Sphere, "Eye", new Vector3(0.65f * side, 2.1f, 1.3f), new Vector3(0.7f, 0.8f, 0.5f), CreaturePaint.Teeth);
            _glasses = Part(m, PrimitiveType.Cube, "Sunglasses", new Vector3(0f, 2.15f, 1.55f), new Vector3(2.1f, 0.5f, 0.12f), CreaturePaint.Eye);
            // The panama, the gold chains and pearls: all stolen.
            Part(m, PrimitiveType.Cylinder, "HatBrim", new Vector3(0f, 4.05f, -0.3f), new Vector3(2.8f, 0.04f, 2.8f), CreaturePaint.Panama);
            Part(m, PrimitiveType.Cylinder, "HatCrown", new Vector3(0f, 4.4f, -0.3f), new Vector3(1.6f, 0.35f, 1.6f), CreaturePaint.Panama);
            Part(m, PrimitiveType.Cylinder, "HatBand", new Vector3(0f, 4.2f, -0.3f), new Vector3(1.63f, 0.08f, 1.63f), CreaturePaint.Eye);
            Part(m, PrimitiveType.Cylinder, "Chain", new Vector3(0f, 1.2f, 0f), new Vector3(3.5f, 0.06f, 3.5f), CreaturePaint.Gold);
            Part(m, PrimitiveType.Cylinder, "Chain2", new Vector3(0f, 1.0f, 0.1f), new Vector3(3.6f, 0.05f, 3.6f), CreaturePaint.Gold);
            for (int i = 0; i < 9; i++)
                Part(m, PrimitiveType.Sphere, "Pearl", Quaternion.Euler(0f, -60f + i * 15f, 0f) * new Vector3(0f, 1.45f, 1.75f), Vector3.one * 0.22f, CreaturePaint.Teeth);
            _arms = new Transform[6][];
            _armRoots = new Vector3[6];
            _armTips = new Vector3[6];
            for (int a = 0; a < 6; a++)
            {
                float angle = a < 2 ? (a == 0 ? -40f : 40f) : 90f + (a - 2) * 60f;
                _armRoots[a] = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, 0.5f, 1.6f);
                _arms[a] = new Transform[ArmSegments];
                for (int s = 0; s < ArmSegments; s++)
                {
                    float k = s / (float)(ArmSegments - 1);
                    bool zone = a < 2 && s >= ArmSegments - 5; // the working arms' outer half can be shot
                    Transform seg = Part(transform, PrimitiveType.Cylinder, "Arm" + a + "_" + s, Vector3.zero, Vector3.one,
                        s % 2 == 0 ? CreaturePaint.Purple : CreaturePaint.Suckers, zone ? "Arm" + a : null, 1f);
                    seg.localScale = Vector3.one * Mathf.Lerp(0.75f, 0.25f, k);
                    _arms[a][s] = seg;
                }
                _armTips[a] = transform.TransformPoint(_armRoots[a] * 2f + Vector3.up);
            }
            BuildTentacles();
            _gun = Part(transform, PrimitiveType.Cube, "StolenPistol", Vector3.zero, new Vector3(0.12f, 0.2f, 0.4f), CreaturePaint.HatBlack);
            _gun.gameObject.SetActive(false);
        }

        private Vector3 ArmWant(int a)
        {
            Vector3 root = transform.TransformPoint(_armRoots[a]);
            Vector3 outward = root - transform.position;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : transform.forward;
            float t = Time.time * 1.9f + a * 1.7f;
            Vector3 idle = root + outward * (2.6f + Mathf.Sin(t) * 0.7f) + Vector3.up * (1.6f + Mathf.Sin(t * 1.4f) * 1.0f) +
                           Vector3.Cross(Vector3.up, outward) * (Mathf.Cos(t * 0.8f) * 1.0f + Mathf.Sin(t * 3.1f) * 0.25f);
            if (a >= 2)
            {
                // Now and then a back arm rears up high and slaps the water.
                float ph = SlapPhase(a);
                if (ph < 0.75f) return root + outward * 1.8f + Vector3.up * 5f;
                if (ph < 1.05f) return root + outward * 3.8f + Vector3.down * 1f;
                return idle;
            }
            switch (Arm(a))
            {
                case ArmState.Reach:
                case ArmState.Drag:
                    FishNet.Object.NetworkObject target = ArmTarget(a);
                    return target != null ? target.transform.position + Vector3.up * 0.5f : idle;
                case ArmState.Steal:
                case ArmState.Throw:
                    return ArmPoint(a);
                case ArmState.Recoil:
                    return root + outward * 1.2f + Vector3.up * 0.4f;
                default:
                    return idle;
            }
        }

        private float SlapPhase(int a) => (Time.time + a * 2.9f) % (6.5f + a * 1.3f);

        private void AnimateArms(float dt)
        {
            if (_arms == null) return;
            if (_slapped == null) _slapped = new bool[_arms.Length];
            for (int a = 0; a < _arms.Length; a++)
            {
                Vector3 want = ArmWant(a);
                ArmState state = a < 2 ? Arm(a) : ArmState.Idle;
                float speed = state == ArmState.Reach ? 16f : state == ArmState.Drag ? 30f : 9f;
                if (a >= 2)
                {
                    float ph = SlapPhase(a);
                    if (ph < 0.75f) { speed = 14f; _slapped[a] = false; }
                    else if (ph < 1.05f)
                    {
                        speed = 34f;
                        if (!_slapped[a] && (_armTips[a] - want).sqrMagnitude < 1.5f)
                        {
                            _slapped[a] = true;
                            World.Water.SplashFx.Spawn(new Vector3(_armTips[a].x, World.Water.WaterSurface.Exists ? World.Water.WaterSurface.HeightAt(_armTips[a]) : _armTips[a].y, _armTips[a].z), 1.6f);
                        }
                    }
                }
                _armTips[a] = Vector3.MoveTowards(_armTips[a], want, speed * dt);
                Vector3 root = ArmRoot(a);
                Vector3 tip = _armTips[a];
                Vector3 outward = root - transform.position;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : transform.forward;
                // Out of the body, arching up over to the tip; the (hidden) segments carry the hit zones along it.
                Vector3 prev = root;
                float rootThick = 0.75f * (_tripo ? 0.7f : 1f); // slimmer, like the model's tentacles (still easy to shoot)
                for (int s = 0; s < ArmSegments; s++)
                {
                    Vector3 p = ArmCurve(root, outward, tip, (s + 1) / (float)ArmSegments);
                    Transform seg = _arms[a][s];
                    Vector3 d = p - prev;
                    float length = Mathf.Max(0.05f, d.magnitude);
                    float thick = rootThick * Mathf.Lerp(1f, 0.33f, s / (float)(ArmSegments - 1));
                    seg.SetPositionAndRotation((p + prev) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.sqrMagnitude > 1e-6f ? d / length : Vector3.up));
                    seg.localScale = new Vector3(thick, length * 0.5f + thick * 0.3f, thick);
                    prev = p;
                }
                DrawTentacle(a, root, outward, tip, rootThick);
            }
            if (_gun != null)
            {
                bool show = _holdsGun.Value;
                if (_gun.gameObject.activeSelf != show) _gun.gameObject.SetActive(show);
                int holder = Arm(0) == ArmState.Steal ? 0 : 1;
                if (show) _gun.SetPositionAndRotation(_armTips[holder] + Vector3.up * 0.25f, Quaternion.Euler(0f, Time.time * 90f, 20f));
            }
            // Eyes aglow behind the shades while it's open.
            bool glow = _bossOpen.Value;
            if (_glasses != null && (glow != _glowShown || _tripo && _glasses.GetComponent<MeshRenderer>().enabled != glow) && _paints.Length > (int)CreaturePaint.Glow)
            {
                _glowShown = glow;
                var shades = _glasses.GetComponent<MeshRenderer>();
                shades.sharedMaterial = _paints[glow ? (int)CreaturePaint.Glow : (int)CreaturePaint.Eye];
                if (_tripo) shades.enabled = glow; // the model wears its own shades: only the glow shows over them
            }
        }

        // ------------------------------------------------------------------ posing (every machine)

        private void AnimateBody(float dt)
        {
            if (_model == null) return;
            CreatureAct act = _act.Value;
            float k = 1f - Mathf.Exp(-10f * dt);
            switch (_built)
            {
                case CreatureKind.Kleptopus:
                {
                    // Bobbing in the swell, puffing up when it shrieks or inks, slumping when beaten.
                    float puff = act is CreatureAct.Gasp or CreatureAct.Slam ? 1.12f : act == CreatureAct.Rear ? 1.06f : 1f;
                    _bob = Mathf.Lerp(_bob <= 0f ? 1f : _bob, puff, k);
                    float t = Time.time;
                    bool dead = act == CreatureAct.Dead;
                    float breathe = dead ? 0f : Mathf.Sin(t * 2.3f) * 0.05f; // squash and stretch as it breathes
                    _model.localScale = new Vector3(_bob * (1f - breathe), _bob * (1f + breathe * 1.5f), _bob * (1f - breathe));
                    _model.localPosition = new Vector3(Mathf.Sin(t * 0.7f) * 0.25f, Mathf.Sin(t * 1.1f) * 0.35f - (dead ? 1.5f : 0f), 0f);
                    // It keeps an eye on the nearest lifeguard, swaying and nodding as it bobs.
                    float want = 0f;
                    float best = 60f * 60f;
                    foreach (Player.PlayerHub p in Player.PlayerHub.All)
                    {
                        if (p == null) continue;
                        Vector3 local = transform.InverseTransformPoint(p.transform.position);
                        float d = local.sqrMagnitude;
                        if (d < best) { best = d; want = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -45f, 45f); }
                    }
                    _kleptoLookYaw = Mathf.MoveTowardsAngle(_kleptoLookYaw, dead ? 0f : want, 40f * dt);
                    _model.localRotation = Quaternion.Euler(dead ? 0f : Mathf.Sin(t * 1.3f + 1f) * 4f, _kleptoLookYaw, dead ? 0f : Mathf.Sin(t * 0.9f) * 6f);
                    AnimateArms(dt);
                    break;
                }
                case CreatureKind.Parrot:
                {
                    bool flying = act is CreatureAct.Fly or CreatureAct.Alarm;
                    _flapPhase += dt * (flying ? 13f : 0f);
                    float wing = flying ? Mathf.Sin(_flapPhase) * 55f + 15f : act == CreatureAct.Dead ? 80f : 0f;
                    if (_wingL != null) _wingL.localRotation = Quaternion.Slerp(_wingL.localRotation, Quaternion.Euler(0f, 0f, -wing), flying ? 1f : k);
                    if (_wingR != null) _wingR.localRotation = Quaternion.Slerp(_wingR.localRotation, Quaternion.Euler(0f, 0f, wing), flying ? 1f : k);
                    _model.localRotation = Quaternion.Slerp(_model.localRotation, act == CreatureAct.Dead ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity, k);
                    break;
                }
                case CreatureKind.Monkey:
                {
                    bool running = act is CreatureAct.Run or CreatureAct.Carry;
                    _flapPhase += dt * (running ? 14f : act == CreatureAct.Taunt ? 9f : 3f);
                    float swing = running ? Mathf.Sin(_flapPhase) * 50f : act == CreatureAct.Taunt ? -120f + Mathf.Sin(_flapPhase) * 30f : Mathf.Sin(_flapPhase) * 6f;
                    if (_armL != null) _armL.localRotation = Quaternion.Euler(swing, 0f, act == CreatureAct.Taunt ? 30f : 0f);
                    if (_armR != null) _armR.localRotation = Quaternion.Euler(act == CreatureAct.Taunt ? swing : -swing, 0f, act == CreatureAct.Taunt ? -30f : 0f);
                    if (_tail != null) _tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 2.3f) * 25f, 0f);
                    _bob = running ? Mathf.Abs(Mathf.Sin(_flapPhase)) * 0.08f : act == CreatureAct.Taunt ? Mathf.Abs(Mathf.Sin(_flapPhase)) * 0.15f : 0f;
                    _model.localPosition = new Vector3(0f, _bob, 0f);
                    _model.localRotation = Quaternion.Slerp(_model.localRotation, act == CreatureAct.Dead ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity, k);
                    if (_hat != null) _hat.gameObject.SetActive(act is CreatureAct.Carry or CreatureAct.Taunt && _monkeyHasHat.Value);
                    if (_droppedHat != null)
                    {
                        bool lying = _hatLying.Value;
                        if (_droppedHat.gameObject.activeSelf != lying) _droppedHat.gameObject.SetActive(lying);
                        if (lying) _droppedHat.SetPositionAndRotation(_hatAt.Value + Vector3.up * 0.03f, Quaternion.Euler(0f, 30f, 8f));
                    }
                    break;
                }
                case CreatureKind.Alligator:
                {
                    float open = act switch { CreatureAct.WindUp => 35f, CreatureAct.Lunge => 45f, CreatureAct.Bite => 5f, CreatureAct.Hold => 8f, _ => 0f };
                    _open = Mathf.Lerp(_open, open, 1f - Mathf.Exp(-(act == CreatureAct.Bite ? 30f : 12f) * dt));
                    if (_jaw != null) _jaw.localRotation = Quaternion.Euler(_open, 0f, 0f);
                    float roll = act == CreatureAct.Hold ? Mathf.Sin(Time.time * 7f) * 40f : 0f; // the death roll
                    _model.localRotation = Quaternion.Slerp(_model.localRotation, act == CreatureAct.Dead ? Quaternion.Euler(0f, 0f, 170f) : Quaternion.Euler(0f, 0f, roll), k);
                    if (_tail != null) _tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * (act == CreatureAct.Move || act == CreatureAct.Lunge ? 6f : 1.5f)) * 22f, 0f);
                    break;
                }
                case CreatureKind.OldSnapper:
                {
                    // The head comes out to rear and bite and stays out gasping; otherwise it's tucked in the shell.
                    float extend = act switch { CreatureAct.Rear => 0.4f, CreatureAct.Bite => 1.6f, CreatureAct.Gasp => 1.1f, CreatureAct.Slam => 0.2f, CreatureAct.Dead => 1.2f, _ => -0.55f };
                    if (_tripo) extend = Mathf.Clamp(extend * 0.3f, -0.2f, 0.45f); // the model's neck doesn't stretch: a short lunge
                    float open = act switch { CreatureAct.Rear => 50f, CreatureAct.Bite => 4f, CreatureAct.Gasp => 30f + Mathf.Sin(Time.time * 6f) * 8f, CreatureAct.Dead => 25f, _ => 0f };
                    _extend = Mathf.Lerp(_extend, extend, 1f - Mathf.Exp(-(act == CreatureAct.Bite ? 25f : 6f) * dt));
                    _open = Mathf.Lerp(_open, open, 1f - Mathf.Exp(-(act == CreatureAct.Bite ? 30f : 8f) * dt));
                    if (_neck != null)
                    {
                        // The model's neck is short (it stretches out of the shell): it rears less far than the greybox.
                        float rear = act == CreatureAct.Rear ? 1f : 0f;
                        _neck.localPosition = new Vector3(0f, 1.1f + rear * (_tripo ? 0.3f : 0.6f), 2f + _extend);
                        _neck.localRotation = Quaternion.Euler(rear * (_tripo ? -18f : -25f), 0f, 0f);
                    }
                    if (_jaw != null) _jaw.localRotation = Quaternion.Euler(_open, 0f, 0f);
                    float slam = act == CreatureAct.Slam ? Mathf.Sin(Mathf.Clamp01((Time.time - _slamShownAt) / 0.6f) * Mathf.PI) * 1.4f : 0f;
                    if (act == CreatureAct.Slam && _lastShownAct != CreatureAct.Slam) _slamShownAt = Time.time;
                    // Asleep in its pond it lies low: only the top of its shell above the water, like a rock.
                    _sink = Mathf.MoveTowards(_sink, IsDormant ? 1.6f : 0f, dt * 0.9f);
                    _model.localPosition = new Vector3(0f, slam - _sink, 0f);
                    _model.localRotation = Quaternion.Slerp(_model.localRotation, act == CreatureAct.Dead ? Quaternion.Euler(0f, 0f, 12f) : Quaternion.identity, k);
                    if (_tail != null) _tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 1.2f) * 12f, 0f);
                    break;
                }
            }
            _lastShownAct = act;
        }

        private CreatureAct _lastShownAct;
        private float _slamShownAt;
    }
}
