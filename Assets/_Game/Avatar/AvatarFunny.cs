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
        public static readonly float[] HeadScales = { 1f, 1.22f, 1.5f, 0.8f };

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

        /// <summary>Seeing stars: the pupils roll round and round for this long.</summary>
        public void Dizzy(float seconds) => _dizzyUntil = Time.time + seconds;

        public void SetShadowsOnly(bool shadowsOnly)
        {
            _shadowsOnly = shadowsOnly;
            foreach (GameObject part in _parts)
                if (part != null && part.TryGetComponent(out MeshRenderer r))
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
                skin.sharedMaterial = _material;
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
                var eye = GooglyEye.Create(head, at, body.GooglyRadius * eyeScale, pupil, rest);
                _eyes.Add(eye);
                _parts.Add(eye.Root);
            }
            AvatarFunnyWear.Dress(rig, body, look, eyeScale, _parts);
            SetShadowsOnly(_shadowsOnly);
        }

        private void ClearParts()
        {
            foreach (GameObject part in _parts)
                if (part != null) Kill(part);
            _parts.Clear();
            _eyes.Clear();
            if (_rig != null && _rig.IsBuilt) _rig[AvatarRig.Bone.Head].localScale = Vector3.one;
            _headScale = 1f;
        }

        private void LateUpdate()
        {
            if (_body == null || _rig == null || !_rig.IsBuilt) return;
            if (!Mathf.Approximately(_headScale, 1f)) _rig[AvatarRig.Bone.Head].localScale = Vector3.one * _headScale;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            if (dt <= 0f) return;
            bool dizzy = Time.time < _dizzyUntil;
            foreach (GooglyEye eye in _eyes)
                if (eye.Root != null) eye.Step(dt, dizzy);
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

            public static GooglyEye Create(Transform head, Vector3 paintedFront, float radius, float pupilShare, Vector2 rest)
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
