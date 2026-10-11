using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// A character's health over their head (every machine), only once they're hurt: a slim bar that pops in on a hit,
    /// drops at once, with a white chunk for the damage just done draining after it, and fades out a few seconds after
    /// the last hit. Green when healthy, through yellow to red. Gone when they're down. (It replaced the row of pips
    /// after the name.)
    /// </summary>
    public class NpcHealthBar : MonoBehaviour
    {
        private const float Width = 0.75f, Height = 0.075f, Border = 0.014f;
        private const float ShowFor = 3.5f, FadeFor = 0.6f, ChipDelay = 0.35f, ChipSpeed = 0.9f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Mesh _quad;
        private static Material _material;

        private StoryNpc _npc;
        private Transform _back, _chip, _fill;
        private Renderer _backR, _chipR, _fillR;
        private MaterialPropertyBlock _block;
        private float _shown, _chipAmount = 1f, _hitAt = -10f, _pop;

        public static NpcHealthBar Attach(StoryNpc npc)
        {
            var go = new GameObject("HealthBar");
            var bar = go.AddComponent<NpcHealthBar>();
            bar._npc = npc;
            bar.Build();
            return bar;
        }

        /// <summary>A hit just landed: show the bar (the chip holds where the health was, then drains).</summary>
        public void Hit(int damage)
        {
            if (Time.time - _hitAt > ShowFor + FadeFor) _chipAmount = Mathf.Clamp01(Fraction + damage / (float)Mathf.Max(1, _npc.MaxHealth));
            _hitAt = Time.time;
            _pop = 1f;
        }

        private float Fraction => _npc != null && _npc.MaxHealth > 0 ? Mathf.Clamp01(_npc.Health / (float)_npc.MaxHealth) : 0f;

        private void Build()
        {
            if (_quad == null)
            {
                _quad = new Mesh { name = "HealthBarQuad" };
                // Anchored at its left edge, so scaling x empties it from the right.
                _quad.vertices = new[] { new Vector3(0f, -0.5f, 0f), new Vector3(1f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0.5f, 0f) };
                _quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                _quad.RecalculateBounds();
            }
            if (_material == null) _material = new Material(Shader.Find("Sprites/Default")) { name = "HealthBar" }; // always in builds
            _block = new MaterialPropertyBlock();
            _back = Part("Back", 0, out _backR);
            _chip = Part("Chip", 1, out _chipR);
            _fill = Part("Fill", 2, out _fillR);
            _back.localPosition = new Vector3(-Width * 0.5f - Border, 0f, 0f);
            _back.localScale = new Vector3(Width + Border * 2f, Height + Border * 2f, 1f);
            _chip.localPosition = _fill.localPosition = new Vector3(-Width * 0.5f, 0f, -0.002f);
            gameObject.SetActive(true);
        }

        private Transform Part(string name, int order, out Renderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = _quad;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.sortingOrder = 30000 + order; // (in front of the world's other see-through things, back to front among themselves)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.allowOcclusionWhenDynamic = false;
            renderer = r;
            return go.transform;
        }

        private void LateUpdate()
        {
            if (_npc == null)
            {
                Destroy(gameObject);
                return;
            }
            float since = Time.time - _hitAt;
            float f = Fraction;
            bool alive = _npc.MaxHealth > 0 && _npc.Health > 0 && _npc.Pose != Avatars.AvatarPose.Down;
            float alpha = !alive ? 0f : since < ShowFor ? 1f : 1f - (since - ShowFor) / FadeFor;
            _shown = Mathf.MoveTowards(_shown, Mathf.Clamp01(alpha), Time.deltaTime * 8f);
            Camera cam = Camera.main;
            bool visible = _shown > 0.01f && cam != null;
            SetShown(visible);
            if (!visible) return;

            if (since > ChipDelay) _chipAmount = Mathf.MoveTowards(_chipAmount, f, ChipSpeed * Time.deltaTime);
            _chipAmount = Mathf.Max(_chipAmount, f);
            _pop = Mathf.MoveTowards(_pop, 0f, Time.deltaTime * 5f);

            Vector3 at = _npc.HeadPosition + Vector3.up * 0.66f;
            float distance = Vector3.Distance(cam.transform.position, at);
            // Readable from a few metres to across a fight: about the same size on screen up close, growing slowly further out.
            float scale = Mathf.Clamp(distance / 8f, 0.28f, 2.6f) * (1f + 0.18f * _pop);
            transform.SetPositionAndRotation(at, cam.transform.rotation);
            transform.localScale = Vector3.one * scale;

            _fill.localScale = new Vector3(Width * Mathf.Max(f, 0.001f), Height, 1f);
            _chip.localScale = new Vector3(Width * Mathf.Max(_chipAmount, 0.001f), Height, 1f);
            Color fill = f > 0.5f ? Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.35f, 0.9f, 0.35f), (f - 0.5f) * 2f)
                : Color.Lerp(new Color(0.95f, 0.18f, 0.15f), new Color(1f, 0.85f, 0.2f), f * 2f);
            Paint(_backR, new Color(0.05f, 0.05f, 0.06f, 0.7f * _shown));
            Paint(_chipR, new Color(1f, 1f, 1f, 0.9f * _shown));
            Paint(_fillR, new Color(fill.r, fill.g, fill.b, _shown));
        }

        private void Paint(Renderer r, Color c)
        {
            _block.SetColor(ColorId, c);
            r.SetPropertyBlock(_block);
        }

        private void SetShown(bool shown)
        {
            if (_backR.enabled == shown) return;
            _backR.enabled = _chipR.enabled = _fillR.enabled = shown;
        }
    }
}
