using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// Makes the players' funny body (<see cref="AvatarLook.Bodies.Goofy"/>) each player's own. Added by
    /// <see cref="AvatarRig"/> when it wears a body with recolour data (<see cref="AvatarBody.IsFunny"/>):
    ///  * colours: skin, hair, top, shorts, teeth and nose swapped in a copy of the texture (the bake marked which
    ///    texel is which part), keeping the painted shading;
    ///  * shape: head size (head bone scale), belly and nose (shape keys from the bake), big buck teeth;
    ///  * googly eyes over the painted ones, whose pupils slosh about with every step, jump and slap;
    ///  * hats, glasses, a moustache or beard and arm floaties, fitted to this head (<see cref="AvatarFunnyWear"/>).
    /// </summary>
    [DefaultExecutionOrder(195)] // after AvatarRig (190) and every animator: the head is scaled last
    public class AvatarFunny : MonoBehaviour
    {
        /// <summary>Which part of the body a texel paints (stored in the recolour texture's alpha, times RegionStep).</summary>
        public enum Region : byte { Keep, Skin, Hair, Top, Shorts, Teeth, Nose, Count }
        public const int RegionStep = 32;

        public const string BellyRound = "BellyRound", BellyHuge = "BellyHuge", BellyFlat = "BellyFlat";
        public const string NoseBig = "NoseBig", NoseSmall = "NoseSmall";

        /// <summary>Head bone scale per <see cref="AvatarLook.HeadSize"/>.</summary>
        public static readonly float[] HeadScales = { 1f, 1f, 1f, 0.8f }; // (no big heads any more: AvatarLook.Tame)

        private static readonly Color PearlyWhite = new(0.98f, 0.97f, 0.9f), Gold = new(1f, 0.76f, 0.18f), Rotten = new(0.6f, 0.58f, 0.25f);
        private static readonly Color ClownRed = new(0.95f, 0.08f, 0.08f), SunscreenWhite = new(0.97f, 0.97f, 1f);

        // The recolour texture's own pixels, read once per body.
        private static readonly Dictionary<AvatarBody, Color32[]> _sources = new();

        private AvatarRig _rig;
        private AvatarBody _body;
        private Texture2D _texture;
        private Material _material;
        private ulong _paintedKey = ulong.MaxValue;
        private float _headScale = 1f;
        private bool _shadowsOnly;
        private readonly List<GameObject> _parts = new();
        private readonly List<GooglyEye> _eyes = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _sources.Clear();

        /// <summary>Called by the rig after it has put on a funny body.</summary>
        public static void Attach(AvatarRig rig, AvatarBody body, AvatarLook look)
        {
            if (!rig.TryGetComponent(out AvatarFunny funny)) funny = rig.gameObject.AddComponent<AvatarFunny>();
            funny.enabled = true;
            funny.Apply(rig, body, look);
        }

        /// <summary>Called by the rig when it wears anything else: nothing funny left on it.</summary>
        public static void Detach(AvatarRig rig)
        {
            if (!rig.TryGetComponent(out AvatarFunny funny)) return;
            funny.ClearParts();
            funny._body = null;
            funny.enabled = false;
        }

        private float _dizzyUntil = float.NegativeInfinity;
        private float _lidL, _lidR;

        /// <summary>How far each googly eye's lid is down (0 open .. 1 shut), from the face (AvatarRig.SetFace).</summary>
        public void SetLids(float left, float right)
        {
            _lidL = left;
            _lidR = right;
            for (int i = 0; i < _eyes.Count; i++) // (now too: review pictures run no LateUpdate)
                if (_eyes[i].Root != null) _eyes[i].Lid(i == 0 ? left : right);
        }

        /// <summary>Seeing stars: the pupils roll round and round for this long.</summary>
        public void Dizzy(float seconds) => _dizzyUntil = Time.time + seconds;

        public void SetShadowsOnly(bool shadowsOnly)
        {
            _shadowsOnly = shadowsOnly;
            // Every renderer in each part, children too: the googly eyes' pupils (and bits of hats and glasses) sit on
            // child objects, and left visible they float in front of your own first-person camera.
            foreach (GameObject part in _parts)
                if (part != null)
                    foreach (Renderer r in part.GetComponentsInChildren<Renderer>(true))
                        r.shadowCastingMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        private void Apply(AvatarRig rig, AvatarBody body, AvatarLook look)
        {
            ClearParts();
            _rig = rig;
            if (_body != body) _paintedKey = ulong.MaxValue;
            _body = body;
            Paint(look);
            SkinnedMeshRenderer skin = rig.Renderer;
            if (skin != null)
            {
                skin.sharedMaterials = AvatarRig.BodyMaterials(body, _material);
                Mesh mesh = skin.sharedMesh;
                void Shape(string name, bool on)
                {
                    int index = mesh.GetBlendShapeIndex(name);
                    if (index >= 0) skin.SetBlendShapeWeight(index, on ? 100f : 0f);
                }
                Shape(BellyRound, look.Belly == 1);
                Shape(BellyHuge, look.Belly == 2);
                Shape(BellyFlat, look.Belly == 3);
                Shape(NoseBig, look.Nose is 1 or 2);
                Shape(NoseSmall, look.Nose == 3);
            }
            _headScale = HeadScales[look.HeadSize & 3];
            rig[AvatarRig.Bone.Head].localScale = Vector3.one * _headScale; // right away too (pictures taken outside play mode)

            Transform head = rig[AvatarRig.Bone.Head];
            float eyeScale = look.Eyes == 1 ? 1.35f : 1f;
            float pupil = look.Eyes == 3 ? 0.16f : 0.42f;
            foreach (bool left in new[] { true, false })
            {
                Vector3 at = left ? body.GooglyL : body.GooglyR;
                // Cross-eyed: both pupils rest toward the nose (the left eye is on -X).
                Vector2 rest = look.Eyes == 2 ? new Vector2(left ? 0.6f : -0.6f, 0.1f) : Vector2.zero;
                var eye = GooglyEye.Create(head, at, body.GooglyRadius * eyeScale, pupil, rest, look.SkinColor);
                _eyes.Add(eye);
                _parts.Add(eye.Root);
            }
            AvatarFunnyWear.Dress(rig, body, look, eyeScale, _parts);
            _pucker = MakePucker(head, body);
            _parts.Add(_pucker.gameObject);
            if (rig.TryGetComponent(out AvatarAnimator animator)) animator.PuckerLength = PuckerReach;
            SetShadowsOnly(_shadowsOnly);
        }

        private void ClearParts()
        {
            foreach (GameObject part in _parts)
                if (part != null) { part.SetActive(false); Kill(part); }
            _parts.Clear();
            _eyes.Clear();
            _glasses.Clear();
            _glassesHead = null;
            if (_rig != null && _rig.IsBuilt) _rig[AvatarRig.Bone.Head].localScale = Vector3.one;
            _headScale = 1f;
        }

        private void LateUpdate()
        {
            if (_body == null || _rig == null || !_rig.IsBuilt) return;
            if (!Mathf.Approximately(_headScale, 1f)) _rig[AvatarRig.Bone.Head].localScale = Vector3.one * _headScale;
            PosePucker();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            bool dizzy = Time.time < _dizzyUntil;
            for (int i = 0; i < _eyes.Count; i++)
            {
                GooglyEye eye = _eyes[i];
                if (eye.Root == null) continue;
                eye.Step(dt, dizzy);
                eye.Lid(i == 0 ? _lidL : _lidR);
            }
        }

        // ------------------------------------------------------------------ the kiss: puckered lips

        /// <summary>How far the puckered lips reach out of the mouth (head space).</summary>
        private const float PuckerReach = 0.07f;
        private Transform _pucker;
        private readonly List<Transform> _glasses = new();
        private Transform _glassesHead;

        /// <summary>
        /// Glasses tipped up onto the forehead for a kiss of life (left on, the lenses went into the face of the
        /// tourist being given a breath), and back down after.
        /// </summary>
        private void PushUpGlasses(Transform head, float up)
        {
            if (head == null) return;
            if (_glassesHead != head || _glasses.Exists(g => g == null))
            {
                _glassesHead = head;
                _glasses.Clear();
                foreach (Transform child in head)
                    if (child.name.StartsWith("Blender glasses_")) _glasses.Add(child);
            }
            foreach (Transform g in _glasses)
            {
                // Turned about the temples (the arms' hinge, 12 cm behind the lenses at their height), lenses up.
                Bounds b = g.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null ? filter.sharedMesh.bounds : default;
                var pivot = new Vector3(0f, b.center.y, b.max.z - 0.12f);
                Quaternion turn = Quaternion.Euler(-55f * up, 0f, 0f);
                g.localRotation = turn;
                g.localPosition = pivot - turn * pivot + new Vector3(0f, 0.015f * up, 0f);
            }
        }
        private AvatarAnimator _animator;

        /// <summary>
        /// Big pink fish lips that push out of the mouth for a kiss (a rescue breath): they are what touches the
        /// other mouth, so the big head, nose and googly eyes stay clear of the other face instead of sinking into it.
        /// </summary>
        private static Transform MakePucker(Transform head, AvatarBody body)
        {
            var go = new GameObject("Pucker");
            go.transform.SetParent(head, false);
            go.AddComponent<MeshFilter>().sharedMesh = _puckerMesh != null ? _puckerMesh : _puckerMesh = BuildPuckerMesh();
            go.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
            go.transform.localScale = Vector3.zero;
            return go.transform;
        }

        private static Mesh _puckerMesh;

        private static Mesh BuildPuckerMesh()
        {
            var kit = new AvatarMeshKit();
            kit.SetBone(0, Matrix4x4.identity);
            var lipPink = new Color(0.93f, 0.42f, 0.5f);
            var lipDark = new Color(0.78f, 0.27f, 0.36f);
            Quaternion along = Quaternion.Euler(90f, 0f, 0f); // the kit's turned shapes stand on y: lay them along z
            // A short snout of lip from the mouth out, then the round rolled lips at its tip, and a dark little "o".
            kit.Frustum(new Vector3(0f, 0f, PuckerReach * 0.45f), 0.03f, 0.024f, PuckerReach * 0.9f, lipPink, along);
            kit.Torus(new Vector3(0f, 0f, PuckerReach * 0.92f), 0.02f, 0.012f, lipDark, along, null, 18, 6);
            kit.Disc(new Vector3(0f, 0f, PuckerReach * 0.97f), 0.011f, 0.004f, new Color(0.25f, 0.06f, 0.08f), along);
            return kit.ToMesh("Pucker", new[] { Matrix4x4.identity });
        }

        /// <summary>Every frame (and by the editor's checks): the lips out by how far into a kiss we are.</summary>
        public void PosePucker()
        {
            if (_pucker == null || _rig == null) return;
            if (_animator == null) _rig.TryGetComponent(out _animator);
            float k = _animator != null ? _animator.Kissing : 0f;
            Transform head = _rig[AvatarRig.Bone.Head], mouth = _rig[AvatarRig.Bone.Mouth];
            _pucker.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, k);
            PushUpGlasses(head, Mathf.SmoothStep(0f, 1f, k));
            if (k <= 0f || mouth == null) return;
            _pucker.localPosition = head.InverseTransformPoint(mouth.position);
            _pucker.localRotation = Quaternion.identity; // straight out of the face
        }

        private void OnDestroy()
        {
            if (_texture != null) Kill(_texture);
            if (_material != null) Kill(_material);
        }

        // Editor tools (ReviewCapture) build avatars outside play mode too.
        private static void Kill(Object thing)
        {
            if (Application.isPlaying) Destroy(thing);
            else DestroyImmediate(thing);
        }

        // ------------------------------------------------------------------ colours

        /// <summary>A copy of the body's texture with this look's colours: each part tinted, its own shading kept.</summary>
        private void Paint(AvatarLook look)
        {
            Color nose = look.Nose == 2 ? ClownRed : look.Has(AvatarExtras.Sunscreen) ? SunscreenWhite : look.SkinColor;
            Color teeth = look.Teeth switch { 2 => Gold, 3 => Rotten, _ => PearlyWhite };
            ulong key = (ulong)look.Skin | (ulong)look.HairColor << 4 | (ulong)look.TopColor << 8 | (ulong)look.BottomColor << 12 |
                        (ulong)(look.Teeth & 3) << 16 | (ulong)(look.Nose & 3) << 18 | (look.Has(AvatarExtras.Sunscreen) ? 1UL : 0UL) << 20;
            if (key == _paintedKey && _texture != null && _material != null) return;
            _paintedKey = key;

            if (!_sources.TryGetValue(_body, out Color32[] source))
                _sources[_body] = source = _body.Recolor.GetPixels32();
            int size = _body.Recolor.width;
            if (_texture == null || _texture.width != size)
            {
                if (_texture != null) Kill(_texture);
                _texture = new Texture2D(size, size, TextureFormat.RGBA32, true, false) { name = "FunnyBody", wrapMode = TextureWrapMode.Clamp };
            }

            // Per part and brightness, the colour to paint: tint x (texel brightness / the part's usual brightness).
            var tints = new Color[(int)Region.Count];
            tints[(int)Region.Skin] = look.SkinColor;
            tints[(int)Region.Hair] = look.HairTint;
            tints[(int)Region.Top] = look.TopTint;
            tints[(int)Region.Shorts] = look.BottomTint;
            tints[(int)Region.Teeth] = teeth;
            tints[(int)Region.Nose] = nose;
            var table = new Color32[(int)Region.Count * 256];
            for (int r = 1; r < (int)Region.Count; r++)
            {
                float reference = _body.RegionLuma != null && r < _body.RegionLuma.Length ? Mathf.Max(0.05f, _body.RegionLuma[r]) : 0.6f;
                for (int l = 0; l < 256; l++)
                {
                    float f = Mathf.Clamp(l / 255f / reference, 0.3f, 1.4f);
                    table[r * 256 + l] = (Color32)new Color(Mathf.Clamp01(tints[r].r * f), Mathf.Clamp01(tints[r].g * f), Mathf.Clamp01(tints[r].b * f), 1f);
                }
            }
            var pixels = new Color32[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Color32 c = source[i];
                int region = (c.a + RegionStep / 2) / RegionStep;
                if (region <= 0 || region >= (int)Region.Count)
                {
                    pixels[i] = new Color32(c.r, c.g, c.b, 255);
                    continue;
                }
                int luma = (77 * c.r + 150 * c.g + 29 * c.b) >> 8;
                pixels[i] = table[region * 256 + luma];
            }
            _texture.SetPixels32(pixels);
            _texture.Apply(true, false);
            if (_material == null) _material = new Material(_body.Material) { name = "FunnyBody" };
            _material.SetTexture("_BaseMap", _texture);
            _material.mainTexture = _texture;
        }

        // ------------------------------------------------------------------ googly eyes

        /// <summary>
        /// A googly eye on the head bone: a white dome and a black pupil that rolls about inside it, pulled down by
        /// gravity and flung about by the head's own movement (it lags, overshoots and bounces off the rim).
        /// </summary>
        private sealed class GooglyEye
        {
            public GameObject Root { get; private set; }
            private Transform _transform;
            private const float Stiffness = 55f, Damping = 2.6f, Fling = 28f;

            private Transform _pupil;
            private float _radius, _pupilRadius, _reach;
            private Vector2 _rest, _offset, _velocity;
            private Vector3 _lastPosition, _lastVelocity;
            private bool _primed;

            private static readonly Dictionary<int, Mesh> _whites = new(), _pupils = new();

            public static GooglyEye Create(Transform head, Vector3 paintedFront, float radius, float pupilShare, Vector2 rest, Color skin)
            {
                var go = new GameObject("GooglyEye");
                go.transform.SetParent(head, false);
                go.transform.localPosition = paintedFront + Vector3.back * (radius * 0.28f); // its dome stands just proud of the painted eye
                var eye = new GooglyEye { Root = go, _transform = go.transform };
                eye._radius = radius;
                eye._pupilRadius = radius * pupilShare;
                eye._reach = radius * 0.9f - eye._pupilRadius;
                eye._rest = rest;
                eye._offset = rest;
                go.AddComponent<MeshFilter>().sharedMesh = Cached(_whites, radius, () =>
                {
                    var kit = new AvatarMeshKit();
                    kit.SetBone(0, Matrix4x4.identity);
                    kit.Ellipsoid(new Vector3(0f, 0f, -radius * 0.15f), new Vector3(radius, radius, radius * 0.55f), new Color(0.98f, 0.98f, 0.97f), null, 18, 10);
                    kit.Torus(new Vector3(0f, 0f, -radius * 0.12f), radius * 0.98f, radius * 0.05f, new Color(0.15f, 0.15f, 0.17f), Quaternion.Euler(90f, 0f, 0f), null, 24, 5);
                    return kit.ToMesh("GooglyWhite", new[] { Matrix4x4.identity });
                });
                go.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
                var pupil = new GameObject("Pupil");
                pupil.transform.SetParent(go.transform, false);
                pupil.AddComponent<MeshFilter>().sharedMesh = Cached(_pupils, eye._pupilRadius, () =>
                {
                    var kit = new AvatarMeshKit();
                    kit.SetBone(0, Matrix4x4.identity);
                    kit.Ellipsoid(Vector3.zero, new Vector3(eye._pupilRadius, eye._pupilRadius, eye._pupilRadius * 0.3f), new Color(0.04f, 0.04f, 0.05f), null, 14, 6);
                    return kit.ToMesh("GooglyPupil", new[] { Matrix4x4.identity });
                });
                pupil.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
                eye._pupil = pupil.transform;
                // The lid: skin over the dome from the top down, built as far down as it is shut (Lid).
                var lid = new GameObject("Lid");
                lid.transform.SetParent(go.transform, false);
                eye._lidFilter = lid.AddComponent<MeshFilter>();
                eye._lidRenderer = lid.AddComponent<MeshRenderer>();
                eye._lidRenderer.sharedMaterial = AvatarRig.SharedMaterial;
                eye._lidRenderer.enabled = false;
                eye._skin = skin;
                eye.Place();
                return eye;
            }

            private static Mesh Cached(Dictionary<int, Mesh> cache, float size, System.Func<Mesh> make)
            {
                int key = Mathf.RoundToInt(size * 10000f);
                if (!cache.TryGetValue(key, out Mesh mesh) || mesh == null) cache[key] = mesh = make();
                return mesh;
            }

            public void Step(float dt, bool dizzy = false)
            {
                if (dizzy)
                {
                    // Round and round, both eyes the same way.
                    float a = Time.time * 11f;
                    _offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.85f;
                    _velocity = Vector2.zero;
                    Place();
                    return;
                }
                Vector3 position = _transform.position;
                if (!_primed)
                {
                    _primed = true;
                    _lastPosition = position;
                    _lastVelocity = Vector3.zero;
                    return;
                }
                Vector3 velocity = (position - _lastPosition) / dt;
                Vector3 acceleration = Vector3.ClampMagnitude((velocity - _lastVelocity) / dt, 80f);
                _lastPosition = position;
                _lastVelocity = velocity;
                // In the eye's frame the pupil feels gravity, and the head's acceleration the other way.
                Vector3 felt = _transform.InverseTransformDirection(Physics.gravity - acceleration) / 9.81f;
                var push = new Vector2(felt.x, felt.y) * Fling;
                for (int i = 0; i < 2; i++)
                {
                    float h = dt * 0.5f;
                    Vector2 force = push - Stiffness * (_offset - _rest) - Damping * _velocity;
                    _velocity += force * h;
                    _offset += _velocity * h;
                    if (_offset.magnitude > 1f)
                    {
                        // Off the rim: back inside, bouncing.
                        Vector2 n = _offset.normalized;
                        _offset = n;
                        float into = Vector2.Dot(_velocity, n);
                        if (into > 0f) _velocity -= n * into * 1.5f;
                    }
                }
                Place();
            }

            private MeshFilter _lidFilter;
            private MeshRenderer _lidRenderer;
            private Mesh _lidMesh;
            private float _lidShown = -1f;
            private Color _skin;

            /// <summary>
            /// The lid down to <paramref name="shut"/> (0 open .. 1 shut): a shell of skin just over the dome (and the
            /// pupil), from its top down to a line that sweeps down the eye, a dark lash rim along that edge.
            /// </summary>
            public void Lid(float shut)
            {
                shut = Mathf.Clamp01(shut);
                if (Mathf.Abs(shut - _lidShown) < 0.02f || _lidFilter == null) return;
                _lidShown = shut;
                _lidRenderer.enabled = shut > 0.02f;
                if (_pupil != null) _pupil.gameObject.SetActive(shut < 0.96f); // (shut right down: nothing pokes through)
                if (shut <= 0.02f) return;
                if (_lidMesh == null) _lidMesh = new Mesh { name = "GooglyLid" };
                const int rows = 8, cols = 16;
                float r = _radius * 1.04f, depth = _radius * 0.55f + _pupilRadius * 0.4f + 0.004f; // (over the pupil too)
                var center = new Vector3(0f, 0f, -_radius * 0.15f);
                float edge = r - 2f * r * Mathf.Lerp(0f, 0.98f, shut);
                var vertices = new Vector3[(rows + 1) * (cols + 1)];
                var normals = new Vector3[vertices.Length];
                var colors = new Color[vertices.Length];
                Color lash = new Color(0.12f, 0.08f, 0.07f);
                for (int j = 0; j <= rows; j++)
                {
                    float y = Mathf.Lerp(r, edge, j / (float)rows);
                    float across = Mathf.Sqrt(Mathf.Max(0f, r * r - y * y));
                    for (int i = 0; i <= cols; i++)
                    {
                        float x = Mathf.Lerp(-across, across, i / (float)cols);
                        float rho2 = (x * x + y * y) / (r * r);
                        float z = depth * Mathf.Sqrt(Mathf.Max(0f, 1f - rho2));
                        int k = j * (cols + 1) + i;
                        vertices[k] = center + new Vector3(x, y, z);
                        normals[k] = new Vector3(x / (r * r), y / (r * r), z / (depth * depth)).normalized;
                        colors[k] = j == rows ? lash : _skin;
                    }
                }
                var triangles = new int[rows * cols * 6];
                int t = 0;
                for (int j = 0; j < rows; j++)
                    for (int i = 0; i < cols; i++)
                    {
                        int a = j * (cols + 1) + i, b = a + 1, c = a + cols + 1, d = c + 1;
                        triangles[t++] = a; triangles[t++] = c; triangles[t++] = b; // (facing out toward +z)
                        triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                    }
                _lidMesh.Clear();
                _lidMesh.vertices = vertices;
                _lidMesh.normals = normals;
                _lidMesh.colors = colors;
                _lidMesh.triangles = triangles;
                _lidMesh.RecalculateBounds();
                _lidFilter.sharedMesh = _lidMesh;
            }

            private void Place()
            {
                Vector2 o = _offset * _reach;
                float rho = Mathf.Min(o.magnitude / _radius, 0.98f);
                float z = -_radius * 0.15f + _radius * 0.55f * Mathf.Sqrt(1f - rho * rho) + 0.002f;
                _pupil.localPosition = new Vector3(o.x, o.y, z);
            }
        }
    }
}
