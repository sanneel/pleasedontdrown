using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    public enum HudIcon { Food, Stamina, Air }

    /// <summary>
    /// What every screen element is drawn with. The HUD and menus are laid out on a sheet 1080 units high (the sizes
    /// How to Fish's HUD uses: 100-unit inventory slots, 60-unit buttons) and scaled to the window, so they take the
    /// same share of the screen at any resolution. Text is set at its real pixel size, so it stays sharp.
    /// </summary>
    public static class Hud
    {
        public const float Sheet = 1080f;

        public static readonly Color Coral = new(0.98f, 0.47f, 0.34f);
        public static readonly Color Teal = new(0.27f, 0.79f, 0.77f);
        public static readonly Color Sand = new(1f, 0.93f, 0.72f);
        public static readonly Color Ink = new(0.03f, 0.1f, 0.15f);

        // Where the bottom-centre block sits, for everything that stacks above it.
        public const float SlotSize = 100f, SlotGap = 8f, SlotBottom = 24f, SlotLift = 20f;
        /// <summary>The top of the hotbar (a lifted slot included), in sheet units.</summary>
        public static float HotbarTop => Height - SlotBottom - SlotSize - SlotLift;

        public static float Scale => Mathf.Max(0.45f, Screen.height / Sheet);
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;
        public static Vector2 Mouse => Event.current.mousePosition / Scale;

        private static Font _heavy, _bold;
        private static readonly Dictionary<long, GUIStyle> _styles = new();
        private static readonly Dictionary<HudIcon, Texture2D> _icons = new();
        private static readonly Regex _colorTags = new("</?color[^>]*>", RegexOptions.Compiled);
        // Shadow text for labels with colour tags, so the same label isn't stripped again every frame.
        private const int PlainTextCap = 64;
        private static readonly Dictionary<string, string> _plainText = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _styles.Clear();
            _plainText.Clear();
            _icons.Clear();
            _heavy = _bold = null;
            _hoverId = 0;
            _hoverFrame = -10;
        }

        public static Rect Px(Rect sheet) => new(sheet.x * Scale, sheet.y * Scale, sheet.width * Scale, sheet.height * Scale);

        public static bool Hovered(Rect sheet) => sheet.Contains(Mouse);

        /// <summary>A text style at <paramref name="size"/> sheet units (cached per pixel size).</summary>
        public static GUIStyle Style(float size, TextAnchor anchor = TextAnchor.MiddleCenter, bool heavy = false, bool wrap = false)
        {
            int px = Mathf.Max(8, Mathf.RoundToInt(size * Scale));
            long key = (uint)px | ((long)anchor << 12) | (heavy ? 1L << 20 : 0L) | (wrap ? 1L << 21 : 0L);
            if (_styles.TryGetValue(key, out GUIStyle style)) return style;
            if (_heavy == null) _heavy = Resources.Load<Font>("Fonts/Roboto-Black");
            if (_bold == null) _bold = Resources.Load<Font>("Fonts/Roboto-Bold");
            style = new GUIStyle
            {
                font = heavy ? _heavy : _bold, fontSize = px, alignment = anchor, richText = true, wordWrap = wrap,
                clipping = TextClipping.Overflow
            };
            style.normal.textColor = Color.white;
            _styles[key] = style;
            return style;
        }

        /// <summary>Text with a soft drop shadow (the shadow is what keeps it readable over sand and sky).</summary>
        public static void Label(Rect sheet, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter,
            bool heavy = false, bool wrap = false, bool shadow = true)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(text) || color.a <= 0.005f) return;
            GUIStyle style = Style(size, anchor, heavy, wrap);
            Rect r = Px(sheet);
            if (shadow)
            {
                float d = Mathf.Max(1f, size * 0.07f * Scale);
                style.normal.textColor = new Color(0f, 0f, 0f, 0.6f * color.a);
                GUI.Label(new Rect(r.x + d, r.y + d, r.width, r.height), WithoutColorTags(text), style);
            }
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        /// <summary>The text with its colour tags taken out (a shadow is one colour). Remembers the last few.</summary>
        private static string WithoutColorTags(string text)
        {
            if (text.IndexOf("<color", System.StringComparison.Ordinal) < 0) return text;
            if (_plainText.TryGetValue(text, out string plain)) return plain;
            if (_plainText.Count >= PlainTextCap) _plainText.Clear();
            plain = _colorTags.Replace(text, "");
            _plainText[text] = plain;
            return plain;
        }

        public static float TextWidth(string text, float size, bool heavy = false) =>
            Style(size, TextAnchor.MiddleLeft, heavy).CalcSize(new GUIContent(text)).x / Scale;

        /// <summary>A filled box, corners rounded by <paramref name="radius"/>.</summary>
        public static void Fill(Rect sheet, Color color, float radius = 0f)
        {
            if (Event.current.type != EventType.Repaint || color.a <= 0.005f) return;
            GUI.DrawTexture(Px(sheet), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius * Scale);
        }

        /// <summary>Only the outline of a box.</summary>
        public static void Frame(Rect sheet, Color color, float thickness, float radius = 0f)
        {
            if (Event.current.type != EventType.Repaint || color.a <= 0.005f) return;
            GUI.DrawTexture(Px(sheet), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, Mathf.Max(1f, thickness * Scale), radius * Scale);
        }

        public static void Picture(Rect sheet, Texture picture, Color tint)
        {
            if (Event.current.type != EventType.Repaint || picture == null) return;
            GUI.DrawTexture(Px(sheet), picture, ScaleMode.ScaleToFit, true, 0f, tint, 0f, 0f);
        }

        /// <summary>A rounded bar filled from the left.</summary>
        public static void Bar(Rect sheet, float value, Color fill)
        {
            Fill(sheet, new Color(0f, 0f, 0f, 0.5f), sheet.height * 0.5f);
            value = Mathf.Clamp01(value);
            if (value <= 0f) return;
            GUI.BeginGroup(Px(new Rect(sheet.x, sheet.y, sheet.width * value, sheet.height)));
            Fill(new Rect(0f, 0f, sheet.width, sheet.height), fill, sheet.height * 0.5f);
            GUI.EndGroup();
        }

        /// <summary>
        /// A vitals square (How to Fish's health / fullness boxes): dark box, the level rising in colour from the
        /// bottom, a white picture on top. It blinks when <paramref name="warn"/>.
        /// </summary>
        public static void Gauge(Rect sheet, float value, Color fill, HudIcon icon, bool warn, float alpha = 1f)
        {
            if (alpha <= 0.01f) return;
            float radius = sheet.width * 0.2f;
            Fill(sheet, new Color(0f, 0f, 0f, 0.5f * alpha), radius);
            value = Mathf.Clamp01(value);
            if (warn) fill = Color.Lerp(fill, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));
            fill.a *= alpha;
            float h = sheet.height * value;
            if (h > 0.5f)
            {
                GUI.BeginGroup(Px(new Rect(sheet.x, sheet.yMax - h, sheet.width, h)));
                Fill(new Rect(0f, h - sheet.height, sheet.width, sheet.height), fill, radius);
                GUI.EndGroup();
            }
            float inset = sheet.width * 0.16f;
            Picture(new Rect(sheet.x + inset, sheet.y + inset, sheet.width - inset * 2f, sheet.height - inset * 2f), Icon(icon), new Color(1f, 1f, 1f, 0.95f * alpha));
        }

        // ------------------------------------------------------------------ controls (menus and panels)

        private static int _hoverId, _hoverFrame = -10;

        /// <summary>A dark rounded panel for a menu page or a window.</summary>
        public static void Panel(Rect sheet, float alpha = 0.85f) => Fill(sheet, new Color(Ink.r, Ink.g, Ink.b, alpha), 16f);

        /// <summary>The whole screen darkened behind a window.</summary>
        public static void Dim(float alpha = 0.66f) => Fill(new Rect(0f, 0f, Width, Height), new Color(Ink.r, Ink.g, Ink.b, alpha));

        /// <summary>
        /// A wide dark button with heavy white lettering. Under the mouse it gets a white frame and nudges right
        /// (hover and click sounds included); <paramref name="primary"/> is the coral one you'd press first,
        /// <paramref name="small"/> the teal one for a row inside a panel.
        /// </summary>
        public static bool Button(Rect r, string label, bool centred = false, bool primary = false, bool selected = false, bool small = false)
        {
            bool enabled = GUI.enabled;
            bool hover = enabled && Hovered(r);
            if (Event.current.type == EventType.Repaint)
            {
                Rect box = hover && !centred && !small ? new Rect(r.x + 8f, r.y, r.width, r.height) : r;
                float radius = Mathf.Min(14f, r.height * 0.25f);
                Color fill = primary ? new Color(Coral.r, Coral.g, Coral.b, hover ? 1f : 0.9f)
                    : small ? new Color(Teal.r * 0.6f, Teal.g * 0.6f, Teal.b * 0.6f, hover ? 1f : 0.85f)
                    : new Color(Ink.r, Ink.g, Ink.b, hover || selected ? 0.9f : 0.6f);
                if (!enabled) fill.a *= 0.4f;
                Fill(box, fill, radius);
                if (hover || selected) Frame(box, Color.white, 3f, radius);
                Color text = !enabled ? new Color(1f, 1f, 1f, 0.45f) : hover && !primary && !small ? Sand : Color.white;
                Rect words = centred || small ? box : new Rect(box.x + 24f, box.y, box.width - 36f, box.height);
                Label(words, label, small ? 22f : 30f, text, centred || small ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, heavy: true, shadow: false);
                if (hover) HoverSound(System.HashCode.Combine(label, Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y)));
            }
            bool pressed = GUI.Button(Px(r), GUIContent.none, GUIStyle.none);
            if (pressed) Core.BeachAudio.PlayLocal(Core.BeachAudio.MenuSelect, 0.7f);
            return pressed;
        }

        /// <summary>A colour to pick: a rounded chip, framed in white when it's the chosen one.</summary>
        public static bool Chip(Rect r, Color color, bool selected)
        {
            if (Event.current.type == EventType.Repaint)
            {
                bool hover = Hovered(r);
                Rect box = hover || selected ? new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f) : r;
                Fill(box, color, 8f);
                if (selected) Frame(box, Color.white, 3f, 8f);
                else if (hover) Frame(box, new Color(1f, 1f, 1f, 0.6f), 2f, 8f);
                if (hover) HoverSound(System.HashCode.Combine(color, Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y)));
            }
            bool pressed = GUI.Button(Px(r), GUIContent.none, GUIStyle.none);
            if (pressed) Core.BeachAudio.PlayLocal(Core.BeachAudio.MenuSelect, 0.7f);
            return pressed;
        }

        /// <summary>One tick as the mouse comes onto a control (not while it stays there).</summary>
        private static void HoverSound(int id)
        {
            if (_hoverId != id || Time.frameCount > _hoverFrame + 2) Core.BeachAudio.PlayLocal(Core.BeachAudio.MenuHover, 0.45f);
            _hoverId = id;
            _hoverFrame = Time.frameCount;
        }

        /// <summary>A rounded track with a coral fill and a white knob; click or drag anywhere along it.</summary>
        public static float Slider(Rect r, float value, float min, float max)
        {
            const float knob = 26f, track = 10f;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            Rect px = Px(r);
            float FromMouse() => Mathf.Lerp(min, max, Mathf.InverseLerp(px.x + knob * 0.5f * Scale, px.xMax - knob * 0.5f * Scale, e.mousePosition.x));
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (e.button == 0 && px.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        value = FromMouse();
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        value = FromMouse();
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        PlayerPrefs.Save();
                        e.Use();
                    }
                    break;
                case EventType.Repaint:
                {
                    float t = Mathf.InverseLerp(min, max, value);
                    float cx = Mathf.Lerp(r.x + knob * 0.5f, r.xMax - knob * 0.5f, t), cy = r.y + r.height * 0.5f;
                    bool active = GUIUtility.hotControl == id || Hovered(r);
                    Fill(new Rect(r.x, cy - track * 0.5f, r.width, track), new Color(1f, 1f, 1f, 0.18f), track * 0.5f);
                    Fill(new Rect(r.x, cy - track * 0.5f, cx - r.x, track), Coral, track * 0.5f);
                    float size = active ? knob + 4f : knob;
                    Fill(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), Color.white, size * 0.5f);
                    break;
                }
            }
            return value;
        }

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + 2.70158f * t * t * t + 1.70158f * t * t;
        }

        // ------------------------------------------------------------------ pictures

        /// <summary>Small white pictures for the vitals, drawn in code (drumstick, lightning bolt, bubbles).</summary>
        public static Texture2D Icon(HudIcon icon)
        {
            if (_icons.TryGetValue(icon, out Texture2D texture) && texture != null) return texture;
            const int n = 64, samples = 4;
            texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "HudIcon_" + icon, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                        {
                            // p: 0..1 across, y up.
                            var p = new Vector2((x + (sx + 0.5f) / samples) / n, (y + (sy + 0.5f) / samples) / n);
                            if (Inside(icon, p)) hits++;
                        }
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(255 * hits / (samples * samples)));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            _icons[icon] = texture;
            return texture;
        }

        private static readonly Vector2[] Bolt =
        {
            new(0.60f, 0.98f), new(0.20f, 0.44f), new(0.45f, 0.44f), new(0.36f, 0.02f), new(0.80f, 0.58f), new(0.54f, 0.58f)
        };

        private static bool Inside(HudIcon icon, Vector2 p)
        {
            switch (icon)
            {
                case HudIcon.Food: // a drumstick: the meat top right, the bone down to the left
                    return Disc(p, new Vector2(0.60f, 0.62f), 0.29f) || Capsule(p, new Vector2(0.44f, 0.46f), new Vector2(0.20f, 0.22f), 0.065f) ||
                           Disc(p, new Vector2(0.13f, 0.25f), 0.09f) || Disc(p, new Vector2(0.23f, 0.14f), 0.09f);
                case HudIcon.Stamina:
                    return InPolygon(p, Bolt);
                default: // bubbles
                    return Ring(p, new Vector2(0.38f, 0.36f), 0.27f, 0.07f) || Ring(p, new Vector2(0.72f, 0.70f), 0.17f, 0.06f) ||
                           Disc(p, new Vector2(0.30f, 0.84f), 0.08f);
            }
        }

        private static bool Disc(Vector2 p, Vector2 c, float r) => (p - c).sqrMagnitude <= r * r;

        private static bool Ring(Vector2 p, Vector2 c, float r, float thickness)
        {
            float d = (p - c).magnitude;
            return d <= r && d >= r - thickness;
        }

        private static bool Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + ab * t)).sqrMagnitude <= r * r;
        }

        private static bool InPolygon(Vector2 p, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i], b = polygon[j];
                if (a.y > p.y != b.y > p.y && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }
}
