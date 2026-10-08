using System.Text;
using System.Collections.Generic;
using PleaseDontDrown.Story;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>World-space popups and speaker-following dialogue bubbles.</summary>
    public class FloatingText : MonoBehaviour
    {
        private TextMesh _text;
        private SpriteRenderer _bubble;
        private SpriteRenderer _tail;
        private StoryNpc _speaker;
        private float _bubbleHeight;
        private Color _color;
        private float _age, _lifetime, _rise;
        private static readonly Dictionary<int, FloatingText> ActiveSpeech = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSpeech() => ActiveSpeech.Clear();

        // Ordinary rewards, impacts and notifications retain their original presentation.
        public static void Spawn(Vector3 position, string text, Color color, float scale = 1f, float lifetime = 1.4f, bool speechCloud = false)
        {
            if (speechCloud) { SpawnBubble(position, null, text, scale, lifetime); return; }
            var go = new GameObject("FloatingText");
            go.transform.position = position;
            TextMesh mesh = AddText(go, text, color, 0.035f * scale);
            var ft = go.AddComponent<FloatingText>();
            ft._text = mesh;
            ft._color = color;
            ft._lifetime = Mathf.Max(0.1f, lifetime);
            ft._rise = 0.9f;
        }

        /// <summary>A readable bubble that remains over this speaker for its full line.</summary>
        public static void SpawnSpeech(StoryNpc speaker, string text, float lifetime, float scale = 1f)
        {
            if (speaker == null) return;
            int id = speaker.GetInstanceID();
            if (ActiveSpeech.TryGetValue(id, out FloatingText previous) && previous != null)
                Destroy(previous.gameObject);
            SpawnBubble(speaker.HeadPosition, speaker, text, scale, lifetime);
        }

        private static void SpawnBubble(Vector3 position, StoryNpc speaker, string text, float scale, float lifetime)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var go = new GameObject("SpeechBubble");
            go.transform.position = position;
            string wrapped = Wrap(text, 23);
            float size = 0.02f * scale;
            TextMesh mesh = AddText(go, wrapped, new Color(0.12f, 0.16f, 0.19f), size);
            mesh.lineSpacing = 1.08f;
            // TextMesh widths vary with the glyphs. Use its generated bounds so both
            // narrow and broad lines receive consistent padding.
            Bounds glyphs = go.GetComponent<MeshRenderer>().localBounds;
            float width = Mathf.Max(1.1f * scale, glyphs.size.x + 0.38f * scale);
            float height = Mathf.Max(0.5f * scale, glyphs.size.y + 0.32f * scale);

            var bg = new GameObject("RoundedBackground");
            bg.transform.SetParent(go.transform, false);
            bg.transform.localPosition = new Vector3(glyphs.center.x, glyphs.center.y, 0.025f);
            var bubble = bg.AddComponent<SpriteRenderer>();
            bubble.sprite = BubbleSprite;
            bubble.drawMode = SpriteDrawMode.Sliced;
            bubble.size = new Vector2(width, height);
            bubble.color = new Color(1f, 0.98f, 0.91f, 0.97f);
            var tip = new GameObject("Tail");
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(glyphs.center.x - 0.13f * scale, glyphs.center.y - height * 0.5f - 0.09f * scale, 0.027f);
            var tail = tip.AddComponent<SpriteRenderer>();
            tail.sprite = TailSprite;
            tail.color = bubble.color;
            tip.transform.localScale = Vector3.one * 0.22f * scale;

            var ft = go.AddComponent<FloatingText>();
            ft._text = mesh;
            ft._bubble = bubble;
            ft._tail = tail;
            ft._speaker = speaker;
            ft._bubbleHeight = height;
            ft._color = mesh.color;
            ft._lifetime = Mathf.Max(0.2f, lifetime);
            if (speaker != null)
            {
                ActiveSpeech[speaker.GetInstanceID()] = ft;
                go.transform.position = speaker.HeadPosition + Vector3.up * (height * 0.5f + 0.28f);
            }
        }

        private static TextMesh AddText(GameObject go, string text, Color color, float characterSize)
        {
            var mesh = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            mesh.text = text;
            mesh.fontSize = 96;
            mesh.characterSize = characterSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.fontStyle = FontStyle.Bold;
            mesh.color = color;
            return mesh;
        }

        private static Sprite _bubbleSprite, _tailSprite;
        private static Sprite BubbleSprite
        {
            get
            {
                if (_bubbleSprite != null) return _bubbleSprite;
                var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                texture.name = "SpeechBubbleRounded";
                texture.filterMode = FilterMode.Bilinear;
                for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float dx = Mathf.Max(0f, Mathf.Abs(x - 31.5f) - 20f);
                    float dy = Mathf.Max(0f, Mathf.Abs(y - 31.5f) - 20f);
                    float alpha = Mathf.Clamp01(12f - Mathf.Sqrt(dx * dx + dy * dy));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
                texture.Apply();
                _bubbleSprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 64f), new Vector2(0.5f, 0.5f), 64f, 0, SpriteMeshType.FullRect, new Vector4(13f, 13f, 13f, 13f));
                return _bubbleSprite;
            }
        }

        private static Sprite TailSprite
        {
            get
            {
                if (_tailSprite != null) return _tailSprite;
                var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                texture.name = "SpeechBubbleTail";
                texture.filterMode = FilterMode.Bilinear;
                for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((y * 0.5f - Mathf.Abs(x - 15.5f)) * 2f)));
                texture.Apply();
                _tailSprite = Sprite.Create(texture, new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
                return _tailSprite;
            }
        }

        private static string Wrap(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var words = value.Trim().Split(' ');
            var result = new StringBuilder();
            int line = 0;
            foreach (string word in words)
            {
                if (line > 0 && line + word.Length + 1 > max) { result.Append('\n'); line = 0; }
                else if (line > 0) { result.Append(' '); line++; }
                result.Append(word); line += word.Length;
            }
            return result.ToString();
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            float t = _age / _lifetime;
            if (t >= 1f || (_speaker != null && !_speaker.isActiveAndEnabled)) { Destroy(gameObject); return; }
            if (_speaker != null) transform.position = _speaker.HeadPosition + Vector3.up * (_bubbleHeight * 0.5f + 0.28f);
            else transform.position += Vector3.up * (_rise * (1f - t) * Time.deltaTime);
            float pop = t < 0.12f ? Mathf.SmoothStep(0.4f, 1.08f, t / 0.12f) : Mathf.Lerp(1.08f, 1f, Mathf.Clamp01((t - 0.12f) / 0.2f));
            transform.localScale = Vector3.one * pop;
            float fade = Mathf.Clamp01((1f - t) * 4f);
            _text.color = new Color(_color.r, _color.g, _color.b, _color.a * fade);
            if (_bubble != null) _bubble.color = new Color(1f, 0.98f, 0.91f, 0.97f * fade);
            if (_tail != null) _tail.color = new Color(1f, 0.98f, 0.91f, 0.97f * fade);
            Camera cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }

        private void OnDestroy()
        {
            if (_speaker != null && ActiveSpeech.TryGetValue(_speaker.GetInstanceID(), out FloatingText current) && current == this)
                ActiveSpeech.Remove(_speaker.GetInstanceID());
        }
    }
}
