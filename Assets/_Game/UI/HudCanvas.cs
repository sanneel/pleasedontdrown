using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Draws the in-game HUD without IMGUI. The HUD code stays immediate-mode (each frame it calls
    /// <see cref="Hud.Label"/>, <see cref="Hud.Fill"/>... in order), but while it runs those calls land here: the
    /// n-th thing drawn reuses the n-th piece of a uGUI canvas and only touches what changed. So a frame costs a few
    /// comparisons per element instead of IMGUI's layout + repaint passes and its nine text meshes per label; a label
    /// is rebuilt only when its text, size or colour changes. Menus stay IMGUI (they only cost anything while open)
    /// and draw on top of this canvas.
    /// HUD scripts register with <see cref="Add"/> in OnEnable (lower order is drawn first, underneath).
    /// </summary>
    [DefaultExecutionOrder(32000)] // after every LateUpdate: cameras have moved, markers land where things are drawn
    public sealed class HudCanvas : MonoBehaviour
    {
        // Draw order of the HUD scripts (underneath first).
        public const int OrderScreenTint = -100, OrderWorldMarkers = 0, OrderPlayer = 20, OrderStory = 30, OrderWeapon = 40, OrderFade = 100;

        private struct Drawer
        {
            public Object Owner;
            public int Order;
            public Action Draw;
        }

        private static readonly List<Drawer> Drawers = new();
        private static HudCanvas _instance;

        private readonly List<Node> _nodes = new();
        private int _used;
        private Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Drawers.Clear();
            _instance = null;
            Rounded.Clear();
        }

        /// <summary>Draws <paramref name="draw"/> every frame until <see cref="Remove"/> (or the owner is destroyed).</summary>
        public static void Add(Object owner, int order, Action draw)
        {
            if (!Application.isPlaying) return;
            Ensure();
            Remove(owner);
            int at = Drawers.Count;
            while (at > 0 && Drawers[at - 1].Order > order) at--;
            Drawers.Insert(at, new Drawer { Owner = owner, Order = order, Draw = draw });
        }

        public static void Remove(Object owner)
        {
            for (int i = Drawers.Count - 1; i >= 0; i--)
                if (Drawers[i].Owner == owner) Drawers.RemoveAt(i);
        }

        private static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("HudCanvas");
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; // 1 unit = 1 screen pixel, like Hud.Px
            canvas.sortingOrder = -100;
            _instance = go.AddComponent<HudCanvas>();
            _instance._root = go.transform;
        }

        private void LateUpdate()
        {
            _used = 0;
            Hud.Retained = true;
            try
            {
                for (int i = 0; i < Drawers.Count; i++)
                {
                    Drawer d = Drawers[i];
                    if (d.Owner == null)
                    {
                        Drawers.RemoveAt(i--);
                        continue;
                    }
                    try { d.Draw(); }
                    catch (Exception e) { Debug.LogException(e, d.Owner); }
                }
            }
            finally
            {
                Hud.Retained = false;
            }
            for (int i = _used; i < _nodes.Count; i++) _nodes[i].Hide();
        }

        // ------------------------------------------------------------------ what Hud draws with, while Retained

        internal static void Text(Rect px, string text, Font font, int fontSize, TextAnchor anchor, bool wrap, bool rich,
            Color color, float outline, Color outlineColor)
        {
            Node n = _instance.Next(Kind.Text);
            n.Place(px, 0f);
            Text t = n.Text;
            if (t.font != font) t.font = font;
            t.fontSize = fontSize; // (uGUI's setters ignore unchanged values)
            t.alignment = anchor;
            t.supportRichText = rich;
            t.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            t.color = AsImgui(color);
            t.text = text;
            n.Outline.Set(outline, AsImgui(outlineColor));
        }

        /// <summary>A box with rounded corners (<paramref name="thickness"/> 0: filled, else only its outline). Pixels.</summary>
        internal static void Box(Rect px, Color color, float radius, float thickness, float angle = 0f)
        {
            Node n = _instance.Next(Kind.Box);
            n.Place(px, angle);
            Image image = n.Box;
            int r = Mathf.Clamp(Mathf.RoundToInt(radius), 0, 64), th = thickness <= 0f ? 0 : Mathf.Clamp(Mathf.RoundToInt(thickness), 1, 32);
            Sprite sprite = r == 0 && th == 0 ? null : RoundedSprite(r, th);
            if (image.sprite != sprite)
            {
                image.sprite = sprite;
                image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
                image.fillCenter = true;
            }
            image.color = AsImgui(color);
        }

        /// <summary>
        /// A picture stretched over <paramref name="px"/>, turned <paramref name="angle"/> degrees clockwise about its
        /// middle; <paramref name="material"/> draws it another way (a round cut: Hud.RoundPicture).
        /// </summary>
        internal static void Picture(Rect px, Texture texture, Color tint, float angle = 0f, Material material = null)
        {
            Node n = _instance.Next(Kind.Picture);
            n.Place(px, angle);
            RawImage image = n.Picture;
            if (image.texture != texture) image.texture = texture;
            if (image.material != (material != null ? material : image.defaultMaterial)) image.material = material;
            image.color = AsImgui(tint);
        }

        /// <summary>
        /// The project renders in linear colour, and the canvas blends see-through colours in linear space where IMGUI
        /// blended them as stored: a 26% white frame came out twice as bright, a dark slot washed out. This alpha gives
        /// the IMGUI look back: exact for black (1 - (1 - a)^2.2) and for white over dark (a^2.2), in between by how
        /// light the colour is.
        /// </summary>
        private static Color AsImgui(Color c)
        {
            if (c.a >= 0.999f || c.a <= 0.001f) return c;
            float light = Mathf.Clamp01(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
            c.a = Mathf.Lerp(1f - Mathf.Pow(1f - c.a, 2.2f), Mathf.Pow(c.a, 2.2f), light);
            return c;
        }

        private Node Next(Kind kind)
        {
            if (_used == _nodes.Count) _nodes.Add(new Node(_root));
            Node n = _nodes[_used++];
            n.Use(kind);
            return n;
        }

        // ------------------------------------------------------------------ rounded boxes

        private static readonly Dictionary<int, Sprite> Rounded = new();

        /// <summary>
        /// A white rounded box (or ring, <paramref name="thickness"/> > 0) cut for 9-slicing: the corners stay
        /// <paramref name="radius"/> pixels round at any size.
        /// </summary>
        private static Sprite RoundedSprite(int radius, int thickness)
        {
            int key = radius | (thickness << 8);
            if (Rounded.TryGetValue(key, out Sprite sprite) && sprite != null) return sprite;
            int border = Mathf.Max(radius, thickness) + 2, size = border * 2 + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "HudBox", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            float inner = Mathf.Max(0f, radius - thickness);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float cover = Coverage(p, size, 0f, radius);
                    if (thickness > 0) cover *= 1f - Coverage(p, size, thickness, inner);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255f * cover));
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Rounded[key] = sprite;
            return sprite;
        }

        /// <summary>How much of the pixel at <paramref name="p"/> is inside the square inset by <paramref name="inset"/>, corners <paramref name="radius"/> round.</summary>
        private static float Coverage(Vector2 p, int size, float inset, float radius)
        {
            float half = size * 0.5f - inset;
            Vector2 q = new Vector2(Mathf.Abs(p.x - size * 0.5f), Mathf.Abs(p.y - size * 0.5f)) - new Vector2(half - radius, half - radius);
            float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            return Mathf.Clamp01(0.5f - outside);
        }

        // ------------------------------------------------------------------ one drawn thing

        private enum Kind : byte { None, Text, Box, Picture }

        /// <summary>
        /// A slot on the canvas. It keeps one graphic of each kind it has been used as and shows the one asked for;
        /// position, size and turn are only written when they change (a write dirties the canvas).
        /// </summary>
        private sealed class Node
        {
            private readonly RectTransform _rect;
            private Kind _kind;
            private Graphic _shown;
            private Vector2 _position, _size;
            private float _angle;
            private Text _text;
            private HudOutline _outline;
            private Image _box;
            private RawImage _picture;

            public Node(Transform root)
            {
                var go = new GameObject("HudNode", typeof(RectTransform));
                _rect = (RectTransform)go.transform;
                _rect.SetParent(root, false);
                _rect.anchorMin = _rect.anchorMax = new Vector2(0f, 1f); // from the top left, like the screen rects
                _rect.pivot = new Vector2(0.5f, 0.5f);
                _size = new Vector2(-1f, -1f);
            }

            public Text Text => _text;
            public HudOutline Outline => _outline;
            public Image Box => _box;
            public RawImage Picture => _picture;

            public void Use(Kind kind)
            {
                if (_kind == kind) return;
                Graphic show = kind switch
                {
                    Kind.Text => _text != null ? _text : CreateText(),
                    Kind.Box => _box != null ? _box : _box = Create<Image>("Box"),
                    _ => _picture != null ? _picture : _picture = Create<RawImage>("Picture"),
                };
                if (_shown != null) _shown.enabled = false;
                show.enabled = true;
                _shown = show;
                _kind = kind;
            }

            public void Hide()
            {
                if (_kind == Kind.None) return;
                if (_shown != null) _shown.enabled = false;
                _shown = null;
                _kind = Kind.None;
            }

            /// <summary><paramref name="px"/>: screen pixels from the top left. <paramref name="angle"/>: degrees clockwise.</summary>
            public void Place(Rect px, float angle)
            {
                var position = new Vector2(px.x + px.width * 0.5f, -(px.y + px.height * 0.5f));
                if (position != _position) _rect.anchoredPosition = _position = position;
                if (px.size != _size) _rect.sizeDelta = _size = px.size;
                if (!Mathf.Approximately(angle, _angle)) _rect.localRotation = Quaternion.Euler(0f, 0f, -(_angle = angle));
            }

            private Text CreateText()
            {
                _text = Create<Text>("Text");
                _text.verticalOverflow = VerticalWrapMode.Overflow;
                _text.lineSpacing = 1f;
                _outline = _text.gameObject.AddComponent<HudOutline>();
                return _text;
            }

            private T Create<T>(string name) where T : Graphic
            {
                var go = new GameObject(name, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(_rect, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var graphic = go.AddComponent<T>();
                graphic.raycastTarget = false;
                graphic.enabled = false;
                return graphic;
            }
        }
    }
}
