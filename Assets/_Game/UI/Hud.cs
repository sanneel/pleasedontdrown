using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    public enum HudIcon { Food, Stamina, Air, Health, Check, Alert, Info }

    /// <summary>
    /// What every screen element is drawn with. The HUD and menus are laid out on a sheet 1080 units high (the sizes
    /// How to Fish's HUD uses: 100-unit inventory slots, 60-unit buttons) and scaled to the window, so they take the
    /// same share of the screen at any resolution. Text is set at its real pixel size, so it stays sharp.
    /// </summary>
    public static class Hud
    {
        public const float Sheet = 1080f;

        // A single coastal rescue palette for menus, combat, story and the HUD.
        public static readonly Color Coral = new(1f, 0.38f, 0.28f);
        public static readonly Color Teal = new(0.32f, 0.88f, 0.81f);
        public static readonly Color Sand = new(1f, 0.91f, 0.70f);
        public static readonly Color Ink = new(0.025f, 0.075f, 0.11f);
        public static readonly Color Paper = new(0.96f, 0.97f, 0.94f);
        public static readonly Color Muted = new(0.69f, 0.79f, 0.79f);
        public static readonly Color Danger = new(1f, 0.43f, 0.36f);
        public const float Margin = 28f;
        public static float BossTop => Width >= 1800f ? Margin : 220f;

        // Where the bottom-centre block sits, for everything that stacks above it.
        public const float SlotSize = 100f, SlotGap = 8f, SlotBottom = 24f, SlotLift = 20f;
        /// <summary>The top of the hotbar (a lifted slot included), in sheet units.</summary>
        public static float HotbarTop => Height - SlotBottom - SlotSize - SlotLift;

        // Keep the full layout visible on 4:3 and narrow windows as well as 16:9. The in-game HUD also takes the
        // player's HUD SIZE (Options), but never so big that it's narrower than 1240 units (a 4:3 screen); the menus
        // don't take it (they'd run off a small screen).
        public static float Scale
        {
            get
            {
                float fit = Mathf.Max(0.1f, Mathf.Min(Screen.height / Sheet, Screen.width / 1440f));
                return Retained ? fit * Mathf.Min(HudSize, Mathf.Max(1f, Screen.width / (fit * 1240f))) : fit;
            }
        }

        private const string HudSizeKey = "pdd.ui.hudsize";
        public const float MinHudSize = 0.85f, MaxHudSize = 1.3f;
        private static float _hudSize = -1f;

        /// <summary>How big the in-game HUD is drawn (Options: HUD SIZE), 1 = as designed. Kept in PlayerPrefs.</summary>
        public static float HudSize
        {
            get
            {
                if (_hudSize < 0f) _hudSize = Mathf.Clamp(PlayerPrefs.GetFloat(HudSizeKey, 1f), MinHudSize, MaxHudSize);
                return _hudSize;
            }
            set
            {
                _hudSize = Mathf.Clamp(value, MinHudSize, MaxHudSize);
                PlayerPrefs.SetFloat(HudSizeKey, _hudSize);
            }
        }
        public static float Width => Screen.width / Scale;
        public static float Height => Screen.height / Scale;
        public static Vector2 Mouse
        {
            get
            {
                if (!Retained && Event.current != null) return Event.current.mousePosition / Scale;
                var mouse = UnityEngine.InputSystem.Mouse.current;
                Vector2 p = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
                return new Vector2(p.x, Screen.height - p.y) / Scale;
            }
        }

        /// <summary>
        /// True while <see cref="HudCanvas"/> runs the in-game HUD: everything below then draws on its canvas instead
        /// of through IMGUI (menus, drawn in OnGUI, still use IMGUI).
        /// </summary>
        internal static bool Retained;

        /// <summary>Drawing now: the HUD canvas pass, or IMGUI's repaint event.</summary>
        private static bool Painting => Retained || Event.current != null && Event.current.type == EventType.Repaint;

        private static Font _heavy, _bold, _display;

        /// <summary>The faces: Roboto Bold and Black for everything you read, Lilita One for the logo and titles.</summary>
        private enum Face : byte { Bold, Heavy, Display }
        private static readonly Dictionary<long, GUIStyle> _styles = new();
        private static readonly Dictionary<HudIcon, Texture2D> _icons = new();
        private static readonly Dictionary<int, Texture2D> _arcs = new();
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
            _arcs.Clear();
            _heavy = _bold = _display = null;
            _hudSize = -1f;
            _hoverId = 0;
            _hoverFrame = -10;
            _eased.Clear();
        }

        private static Font HudFont(bool heavy) => FontOf(heavy ? Face.Heavy : Face.Bold);

        private static Font FontOf(Face face)
        {
            switch (face)
            {
                case Face.Display:
                    if (_display == null) _display = Resources.Load<Font>("Fonts/LilitaOne-Regular"); // (OFL: Fonts/LilitaOne-OFL.txt)
                    return _display != null ? _display : FontOf(Face.Heavy);
                case Face.Heavy:
                    if (_heavy == null) _heavy = Resources.Load<Font>("Fonts/Roboto-Black");
                    return _heavy;
                default:
                    if (_bold == null) _bold = Resources.Load<Font>("Fonts/Roboto-Bold");
                    return _bold;
            }
        }

        private static Face FaceOf(bool heavy) => heavy ? Face.Heavy : Face.Bold;

        private static int PixelSize(float size) => Mathf.Max(8, Mathf.RoundToInt(size * Scale));

        public static Rect Px(Rect sheet) => new(sheet.x * Scale, sheet.y * Scale, sheet.width * Scale, sheet.height * Scale);

        public static bool Hovered(Rect sheet) => sheet.Contains(Mouse);

        /// <summary>A text style at <paramref name="size"/> sheet units (cached per pixel size).</summary>
        public static GUIStyle Style(float size, TextAnchor anchor = TextAnchor.MiddleCenter, bool heavy = false, bool wrap = false, bool richText = true) =>
            StyleOf(size, anchor, FaceOf(heavy), wrap, richText);

        private static GUIStyle StyleOf(float size, TextAnchor anchor, Face face, bool wrap, bool richText)
        {
            int px = PixelSize(size);
            long key = (uint)px | ((long)anchor << 12) | ((long)face << 20) | (wrap ? 1L << 22 : 0L) | (richText ? 1L << 23 : 0L);
            if (_styles.TryGetValue(key, out GUIStyle style)) return style;
            style = new GUIStyle
            {
                font = FontOf(face), fontSize = px, alignment = anchor, richText = richText, wordWrap = wrap,
                clipping = TextClipping.Clip
            };
            style.normal.textColor = Color.white;
            _styles[key] = style;
            return style;
        }

        /// <summary>Text with a soft drop shadow (the shadow is what keeps it readable over sand and sky).</summary>
        public static void Label(Rect sheet, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter,
            bool heavy = false, bool wrap = false, bool shadow = true, bool richText = true) =>
            Text(sheet, text, size, color, anchor, FaceOf(heavy), wrap, shadow, richText);

        /// <summary>A title in the display face (Lilita One: the logo, menu headings, chapter and boss cards), outlined.</summary>
        public static void Title(Rect sheet, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, bool wrap = false) =>
            Text(sheet, text, size, color, anchor, Face.Display, wrap, true, false);

        /// <summary>A one-line title shrunk to fit <paramref name="rect"/> if it's too long.</summary>
        public static void FitTitle(Rect rect, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter, float minimum = 20f)
        {
            if (!Painting || string.IsNullOrEmpty(text)) return;
            float width = TitleWidth(text, size);
            if (width > rect.width) size = Mathf.Max(minimum, size * rect.width / width);
            Title(rect, text, size, color, anchor);
        }

        public static float TitleWidth(string text, float size) => WidthOf(text, size, Face.Display);

        private static void Text(Rect sheet, string text, float size, Color color, TextAnchor anchor, Face face, bool wrap, bool shadow, bool richText)
        {
            if (!Painting || string.IsNullOrEmpty(text) || color.a <= 0.005f) return;
            Rect r = Px(sheet);
            // Text sits straight on the game (no dark boxes behind it): a dark outline keeps it readable on bright sand
            // and sky. (`shadow` only makes it a little heavier now.) Dark letters, on a coral button or a key chip, get
            // none: a black outline round black letters turned them into a blob.
            bool dark = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f < 0.35f;
            float d = dark ? 0f : Mathf.Max(1f, size * (shadow ? 0.07f : 0.055f) * Scale);
            var outline = new Color(0.02f, 0.04f, 0.06f, 0.85f * color.a);
            if (Retained)
            {
                HudCanvas.Text(r, text, FontOf(face), PixelSize(size), anchor, wrap, richText, color, d, outline);
                return;
            }
            GUIStyle style = StyleOf(size, anchor, face, wrap, richText);
            string plain = WithoutColorTags(text);
            style.normal.textColor = outline;
            if (d > 0f)
                foreach (Vector2 o in Outline)
                    GUI.Label(new Rect(r.x + o.x * d, r.y + o.y * d, r.width, r.height), plain, style);
            style.normal.textColor = color;
            GUI.Label(r, text, style);
        }

        private static readonly Vector2[] Outline =
        {
            new(-1f, -1f), new(1f, -1f), new(-1f, 1f), new(1f, 1f), new(0f, 1.3f), new(0f, -1f), new(1.2f, 0f), new(-1.2f, 0f)
        };

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

        private static readonly GUIContent Measured = new();
        private static readonly TextGenerator Measure = new();

        public static float TextWidth(string text, float size, bool heavy = false) => WidthOf(text, size, FaceOf(heavy));

        private static float WidthOf(string text, float size, Face face)
        {
            if (Retained) return Measure.GetPreferredWidth(text, Generation(size, face, true, 0f)) / Scale;
            Measured.text = text;
            return StyleOf(size, TextAnchor.MiddleLeft, face, false, true).CalcSize(Measured).x / Scale;
        }

        public static float TextHeight(string text, float width, float size, bool heavy = false, bool richText = true)
        {
            if (Retained) return Measure.GetPreferredHeight(text, Generation(size, FaceOf(heavy), richText, Mathf.Max(1f, width * Scale))) / Scale;
            Measured.text = text;
            return Style(size, TextAnchor.UpperLeft, heavy, true, richText).CalcHeight(Measured, Mathf.Max(1f, width * Scale)) / Scale;
        }

        /// <summary>How the HUD canvas lays text out (the same font and pixel size as the IMGUI styles).</summary>
        private static TextGenerationSettings Generation(float size, Face face, bool richText, float wrapWidth) => new()
        {
            font = FontOf(face), fontSize = PixelSize(size), richText = richText, scaleFactor = 1f, lineSpacing = 1f,
            textAnchor = TextAnchor.UpperLeft, color = Color.white, pivot = new Vector2(0.5f, 0.5f), updateBounds = true,
            horizontalOverflow = wrapWidth > 0f ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow,
            verticalOverflow = VerticalWrapMode.Overflow, generationExtents = new Vector2(wrapWidth, 0f), generateOutOfBounds = true,
        };

        /// <summary>Single-line labels keep their padding when a name or binding is unusually long.</summary>
        public static void FitLabel(Rect rect, string text, float size, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft, bool heavy = false, float minimum = 16f)
        {
            if (!Painting || string.IsNullOrEmpty(text)) return;
            float width = TextWidth(text, size, heavy);
            if (width > rect.width) size = Mathf.Max(minimum, size * rect.width / width);
            Label(rect, text, size, color, anchor, heavy, shadow: false);
        }

        /// <summary>
        /// Where in-game information sits. Draws nothing now: no dark boxes over the game, the text carries itself
        /// with its outline (see <see cref="Label"/>). Menus that need a backdrop use <see cref="Panel"/>.
        /// </summary>
        public static void Surface(Rect rect, float alpha = 0.9f, Color? accent = null)
        {
        }

        /// <summary>A filled box, corners rounded by <paramref name="radius"/>.</summary>
        public static void Fill(Rect sheet, Color color, float radius = 0f)
        {
            if (!Painting || color.a <= 0.005f) return;
            if (Retained) HudCanvas.Box(Px(sheet), color, radius * Scale, 0f);
            else GUI.DrawTexture(Px(sheet), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius * Scale);
        }

        /// <summary>Only the outline of a box.</summary>
        public static void Frame(Rect sheet, Color color, float thickness, float radius = 0f)
        {
            if (!Painting || color.a <= 0.005f) return;
            if (Retained) HudCanvas.Box(Px(sheet), color, radius * Scale, Mathf.Max(1f, thickness * Scale));
            else GUI.DrawTexture(Px(sheet), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, Mathf.Max(1f, thickness * Scale), radius * Scale);
        }

        public static void Picture(Rect sheet, Texture picture, Color tint)
        {
            if (!Painting || picture == null) return;
            if (Retained) HudCanvas.Picture(Fit(Px(sheet), picture), picture, tint);
            else GUI.DrawTexture(Px(sheet), picture, ScaleMode.ScaleToFit, true, 0f, tint, 0f, 0f);
        }

        private static Material _round;

        /// <summary>A picture cut to a circle (the speaker's portrait), filling <paramref name="sheet"/>.</summary>
        public static void RoundPicture(Rect sheet, Texture picture, Color tint)
        {
            if (!Painting || picture == null) return;
            if (!Retained)
            {
                Rect px = Px(sheet);
                GUI.DrawTexture(px, picture, ScaleMode.StretchToFill, false, 0f, tint, 0f, px.width * 0.5f);
                return;
            }
            if (_round == null)
            {
                Shader shader = Resources.Load<Shader>("HudRound");
                if (shader == null) return;
                _round = new Material(shader) { name = "HudRound", hideFlags = HideFlags.HideAndDontSave };
            }
            HudCanvas.Picture(Px(sheet), picture, tint, 0f, _round);
        }

        /// <summary>
        /// A number in a thin frame with what it counts under it (the pause screen's log and the chapter report):
        /// the number in the title face, the label in small capitals.
        /// </summary>
        public static void StatTile(Rect r, string number, string label, Color color, float alpha = 1f)
        {
            if (alpha <= 0.005f) return;
            Frame(r, new Color(1f, 1f, 1f, 0.26f * alpha), 1.5f, 12f);
            float big = Mathf.Min(46f, r.height * 0.47f);
            FitTitle(new Rect(r.x + 8f, r.y + r.height * 0.08f, r.width - 16f, big * 1.18f), number, big, new Color(color.r, color.g, color.b, color.a * alpha));
            Label(new Rect(r.x, r.yMax - r.height * 0.36f, r.width, r.height * 0.26f), label, 16f, new Color(Muted.r, Muted.g, Muted.b, alpha), TextAnchor.MiddleCenter, heavy: true, shadow: false);
        }

        /// <summary>A rounded bar filled from the left.</summary>
        public static void Bar(Rect sheet, float value, Color fill)
        {
            Fill(sheet, new Color(Ink.r, Ink.g, Ink.b, 0.9f * fill.a), sheet.height * 0.5f); // (fades with its colour)
            value = Mathf.Clamp01(value);
            if (value > 0f)
            {
                var filled = new Rect(sheet.x + 2f, sheet.y + 2f, Mathf.Max(0f, sheet.width - 4f) * value, Mathf.Max(0f, sheet.height - 4f));
                Fill(filled, fill, Mathf.Min(filled.width, filled.height) * 0.5f);
            }
            Frame(sheet, new Color(Paper.r, Paper.g, Paper.b, 0.28f), 1f, sheet.height * 0.5f);
        }

        /// <summary>
        /// A vitals square (How to Fish's health / fullness boxes): dark box, the level rising in colour from the
        /// bottom, a white picture on top. It blinks when <paramref name="warn"/>.
        /// </summary>
        public static void Gauge(Rect sheet, float value, Color fill, HudIcon icon, bool warn, float alpha = 1f)
        {
            if (alpha <= 0.01f) return;
            float radius = sheet.width * 0.2f;
            Fill(sheet, new Color(Ink.r, Ink.g, Ink.b, 0.82f * alpha), radius);
            Frame(sheet, new Color(Paper.r, Paper.g, Paper.b, 0.26f * alpha), 1.5f, radius);
            value = Mathf.Clamp01(value);
            if (warn) fill = Color.Lerp(fill, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f));
            fill.a *= alpha;
            float h = sheet.height * value;
            if (h > 0.5f && Retained)
                Fill(new Rect(sheet.x, sheet.yMax - h, sheet.width, h), fill, Mathf.Min(radius, h * 0.5f)); // (no clipping on the canvas)
            else if (h > 0.5f)
            {
                GUI.BeginGroup(Px(new Rect(sheet.x, sheet.yMax - h, sheet.width, h)));
                Fill(new Rect(0f, h - sheet.height, sheet.width, sheet.height), fill, radius);
                GUI.EndGroup();
            }
            float inset = sheet.width * 0.16f;
            Picture(new Rect(sheet.x + inset, sheet.y + inset, sheet.width - inset * 2f, sheet.height - inset * 2f), Icon(icon), new Color(1f, 1f, 1f, 0.95f * alpha));
        }

        /// <summary>
        /// A round vital, the way How to Fish shows them: no box, just a thin white ring round a white picture. The
        /// ring is the meter, running out anticlockwise from the top; what was just lost shows in coral for a moment
        /// (<paramref name="trail"/>, easing down after <paramref name="value"/>). Low, the picture takes the warning
        /// colour and pulses.
        /// </summary>
        public static void Vital(Rect sheet, float value, float trail, HudIcon icon, Color warnColor, bool low, float alpha = 1f)
        {
            if (alpha <= 0.01f || !Painting) return;
            value = Mathf.Clamp01(value);
            trail = Mathf.Clamp01(Mathf.Max(trail, value));
            const float thick = 0.075f; // of the diameter
            var shadow = new Rect(sheet.x + 1.5f, sheet.y + 2f, sheet.width, sheet.height);
            Fill(sheet, new Color(Ink.r, Ink.g, Ink.b, 0.48f * alpha), sheet.width * 0.5f);
            Arc(shadow, 0f, 1f, thick * 1.25f, new Color(0f, 0f, 0f, 0.35f * alpha));
            Arc(sheet, 0f, 1f, thick, new Color(Paper.r, Paper.g, Paper.b, 0.26f * alpha));
            // (Full, the ring closes at the top; it empties from there round to the right.)
            if (trail > value + 0.002f) Arc(sheet, 1f - trail, 1f - value, thick, new Color(Coral.r, Coral.g, Coral.b, alpha)); // just lost
            if (value > 0.002f) Arc(sheet, 1f - value, 1f, thick, new Color(Teal.r, Teal.g, Teal.b, alpha));
            float pulse = low ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 9f) : 0f;
            Color tint = low ? Color.Lerp(warnColor, Color.white, pulse * 0.35f) : Color.white;
            tint.a = alpha;
            float inset = sheet.width * (0.2f - 0.02f * pulse);
            var inner = new Rect(sheet.x + inset, sheet.y + inset, sheet.width - inset * 2f, sheet.height - inset * 2f);
            Picture(new Rect(inner.x + 1.5f, inner.y + 2f, inner.width, inner.height), Icon(icon), new Color(0f, 0f, 0f, 0.35f * alpha));
            Picture(inner, Icon(icon), tint);
        }

        /// <summary>
        /// A ring segment in a square, from <paramref name="from"/> to <paramref name="to"/> of the way round
        /// (0 = the top, going clockwise), <paramref name="thickness"/> a share of the diameter.
        /// </summary>
        public static void Arc(Rect sheet, float from, float to, float thickness, Color color)
        {
            if (!Painting || color.a <= 0.005f) return;
            from = Mathf.Clamp01(from);
            to = Mathf.Clamp01(to);
            if (to - from <= 0.001f) return;
            Texture2D arc = ArcTexture(to - from, thickness);
            Rect px = Px(sheet);
            if (Retained)
            {
                HudCanvas.Picture(px, arc, color, from * 360f);
                return;
            }
            Matrix4x4 before = GUI.matrix;
            if (from > 0.0005f) GUIUtility.RotateAroundPivot(from * 360f, px.center);
            GUI.DrawTexture(px, arc, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
            GUI.matrix = before;
        }

        /// <summary>A white ring segment from the top clockwise (cached per 1/240 of a turn and thickness).</summary>
        private static Texture2D ArcTexture(float length, float thickness)
        {
            int steps = Mathf.Clamp(Mathf.RoundToInt(length * 240f), 1, 240);
            int thick = Mathf.Clamp(Mathf.RoundToInt(thickness * 400f), 4, 200);
            int key = steps | (thick << 9);
            if (_arcs.TryGetValue(key, out Texture2D texture) && texture != null) return texture;
            const int n = 128;
            float outer = n * 0.5f - 1f, inner = outer - thick / 400f * n, end = steps / 240f * Mathf.PI * 2f;
            texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "HudArc", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f; // texture y runs up
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float band = Mathf.Clamp01(outer - r + 0.5f) * Mathf.Clamp01(r - inner + 0.5f);
                    float angle = Mathf.Atan2(dx, dy); // 0 at the top, clockwise positive
                    if (angle < 0f) angle += Mathf.PI * 2f;
                    float edge = steps >= 240 ? 1f : Mathf.Clamp01((end - angle) * r + 0.5f) * Mathf.Clamp01(angle * r + 0.5f);
                    pixels[y * n + x] = new Color32(255, 255, 255, (byte)(255f * band * edge));
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            _arcs[key] = texture;
            return texture;
        }

        /// <summary>The part of <paramref name="px"/> a picture fills keeping its shape (IMGUI's ScaleToFit).</summary>
        private static Rect Fit(Rect px, Texture picture)
        {
            float aspect = picture.width / (float)Mathf.Max(1, picture.height);
            if (px.width / Mathf.Max(0.001f, px.height) > aspect)
            {
                float w = px.height * aspect;
                return new Rect(px.x + (px.width - w) * 0.5f, px.y, w, px.height);
            }
            float h = px.width / aspect;
            return new Rect(px.x, px.y + (px.height - h) * 0.5f, px.width, h);
        }

        /// <summary>
        /// A plain box in screen pixels (the crosshair, the scope's black sides), turned <paramref name="angle"/>
        /// degrees clockwise about <paramref name="pivot"/> (screen pixels; default its own middle).
        /// </summary>
        public static void PixelBox(Rect px, Color color, float angle = 0f, Vector2? pivot = null)
        {
            if (!Painting || color.a <= 0.005f) return;
            if (Retained)
            {
                HudCanvas.Box(Turned(px, angle, pivot), color, 0f, 0f, angle);
                return;
            }
            Matrix4x4 before = GUI.matrix;
            if (angle != 0f) GUIUtility.RotateAroundPivot(angle, pivot ?? px.center);
            GUI.DrawTexture(px, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, 0f);
            GUI.matrix = before;
        }

        /// <summary>A picture stretched over a rect in screen pixels.</summary>
        public static void PixelPicture(Rect px, Texture picture, Color tint)
        {
            if (!Painting || picture == null) return;
            if (Retained) HudCanvas.Picture(px, picture, tint);
            else GUI.DrawTexture(px, picture, ScaleMode.StretchToFill, true, 0f, tint, 0f, 0f);
        }

        /// <summary>Where <paramref name="px"/> ends up turned about <paramref name="pivot"/> (it is then also turned about its own middle).</summary>
        private static Rect Turned(Rect px, float angle, Vector2? pivot)
        {
            if (angle == 0f || pivot == null) return px;
            float a = angle * Mathf.Deg2Rad, cos = Mathf.Cos(a), sin = Mathf.Sin(a);
            Vector2 d = px.center - pivot.Value; // screen y runs down, so this turns clockwise on screen
            Vector2 c = pivot.Value + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
            return new Rect(c.x - px.width * 0.5f, c.y - px.height * 0.5f, px.width, px.height);
        }

        // ------------------------------------------------------------------ controls (menus and panels)

        private static int _hoverId, _hoverFrame = -10;

        /// <summary>A dark rounded panel for a menu page or a window.</summary>
        public static void Panel(Rect sheet, float alpha = 0.85f)
        {
            Surface(sheet, alpha);
            Fill(new Rect(sheet.x + 28f, sheet.y + 1f, Mathf.Max(0f, Mathf.Min(64f, sheet.width - 56f)), 3f),
                new Color(Teal.r, Teal.g, Teal.b, alpha), 1.5f);
        }

        /// <summary>The whole screen darkened behind a window.</summary>
        public static void Dim(float alpha = 0.66f) => Fill(new Rect(0f, 0f, Width, Height), new Color(Ink.r, Ink.g, Ink.b, alpha));

        /// <summary>
        /// A wide dark button with heavy white lettering. Under the mouse it eases into its hover look: a teal bar grows
        /// up its left edge, the words slide right and turn sand (hover and click sounds included);
        /// <paramref name="primary"/> is the coral one you'd press first (dark letters, no outline),
        /// <paramref name="small"/> the teal one for a row inside a panel.
        /// </summary>
        public static bool Button(Rect r, string label, bool centred = false, bool primary = false, bool selected = false, bool small = false)
        {
            bool enabled = GUI.enabled;
            bool hover = enabled && Hovered(r);
            if (Event.current.type == EventType.Repaint)
            {
                int id = System.HashCode.Combine(label, Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y));
                float h = Ease(id, hover || selected && !small);
                Rect box = r;
                float radius = Mathf.Min(12f, r.height * 0.22f);
                Color fill = primary ? Color.Lerp(new Color(Coral.r, Coral.g, Coral.b, 0.94f), new Color(1f, 0.5f, 0.4f, 1f), h)
                    : small ? Color.Lerp(new Color(0.055f, 0.27f, 0.29f, 0.92f), new Color(0.08f, 0.36f, 0.38f, 1f), h)
                    : Color.Lerp(new Color(0.04f, 0.13f, 0.17f, 0.82f), new Color(0.07f, 0.23f, 0.27f, 0.97f), h);
                if (selected && !primary) fill = new Color(0.09f, 0.33f, 0.35f, 0.97f);
                if (!enabled) fill = new Color(0.08f, 0.15f, 0.18f, 0.7f);
                Fill(box, fill, radius);
                float lit = primary ? 0f : selected ? 1f : h;
                Frame(box, Color.Lerp(new Color(Paper.r, Paper.g, Paper.b, enabled ? 0.2f : 0.1f), new Color(Teal.r, Teal.g, Teal.b, 0.95f), lit),
                    1f + lit, radius);
                if (!primary && !small && h > 0.01f)
                {
                    // The bar up the left edge, growing from the middle.
                    float bar = (box.height - 18f) * h;
                    Fill(new Rect(box.x + 7f, box.center.y - bar * 0.5f, 4f, bar), new Color(Teal.r, Teal.g, Teal.b, h), 2f);
                }
                Color text = !enabled ? new Color(1f, 1f, 1f, 0.45f) : primary ? Ink : Color.Lerp(Paper, Sand, h);
                float slide = primary || small ? 0f : 8f * h;
                Rect words = new Rect(box.x + (centred || small ? 14f : 24f) + slide, box.y, box.width - (centred || small ? 28f : 48f), box.height);
                FitLabel(words, label, small ? 21f : 27f, text, centred || small ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft, heavy: true);
                if (hover) HoverSound(id);
            }
            bool pressed = GUI.Button(Px(r), GUIContent.none, GUIStyle.none);
            if (pressed) Core.BeachAudio.PlayLocal(Core.BeachAudio.MenuSelect, 0.7f);
            return pressed;
        }

        /// <summary>
        /// A key drawn as a little keycap: a light rounded key with its edge showing below, the dark letter on it
        /// (no outline). <paramref name="height"/> sets the size; returns its width.
        /// </summary>
        public static float KeyCap(float x, float y, string key, float height = 32f, float alpha = 1f)
        {
            float letters = height * 0.53f;
            float width = Mathf.Max(height, TextWidth(key, letters, heavy: true) + height * 0.5f);
            var cap = new Rect(x, y, width, height);
            float radius = height * 0.22f;
            Fill(new Rect(cap.x, cap.y + height * 0.09f, cap.width, cap.height), new Color(0f, 0f, 0f, 0.45f * alpha), radius);
            Fill(cap, new Color(Paper.r, Paper.g, Paper.b, 0.96f * alpha), radius);
            Label(cap, key, letters, new Color(Ink.r, Ink.g, Ink.b, alpha), heavy: true, shadow: false);
            return width;
        }

        /// <summary>A row of keycaps, each with what it does beside it, centred on <paramref name="center"/>.</summary>
        public static void KeyHints(float center, float y, IReadOnlyList<(string key, string what)> hints, float alpha = 1f, float size = 18f)
        {
            float cap = size * 1.45f, gap = size * 0.4f, spacing = size * 1.4f, total = 0f;
            for (int i = 0; i < hints.Count; i++)
                total += Mathf.Max(cap, TextWidth(hints[i].key, cap * 0.53f, heavy: true) + cap * 0.5f) + gap + TextWidth(hints[i].what, size) + (i > 0 ? spacing : 0f);
            float x = center - total * 0.5f;
            var words = new Color(Paper.r, Paper.g, Paper.b, alpha);
            for (int i = 0; i < hints.Count; i++)
            {
                if (i > 0) x += spacing;
                x += KeyCap(x, y, hints[i].key, cap, alpha) + gap;
                float w = TextWidth(hints[i].what, size);
                Label(new Rect(x, y - 2f, w + 6f, cap + 2f), hints[i].what, size, words, TextAnchor.MiddleLeft, shadow: false);
                x += w;
            }
        }

        private static readonly Dictionary<int, (float value, int frame)> _eased = new();

        /// <summary>
        /// 0..1 easing toward <paramref name="on"/> for the control <paramref name="id"/> (a hover, a highlight),
        /// about a tenth of a second each way. Controls not drawn for a while start again from 0.
        /// </summary>
        public static float Ease(int id, bool on)
        {
            float v = _eased.TryGetValue(id, out var e) && Time.frameCount - e.frame < 30 ? e.value : 0f;
            if (e.frame != Time.frameCount)
                v = Mathf.MoveTowards(v, on ? 1f : 0f, Mathf.Min(0.1f, Time.unscaledDeltaTime) * 9f);
            if (_eased.Count > 512) _eased.Clear();
            _eased[id] = (v, Time.frameCount);
            return Mathf.SmoothStep(0f, 1f, v);
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
                    Fill(new Rect(r.x, cy - track * 0.5f, r.width, track), new Color(Paper.r, Paper.g, Paper.b, 0.22f), track * 0.5f);
                    Fill(new Rect(r.x, cy - track * 0.5f, cx - r.x, track), Coral, track * 0.5f);
                    float size = active ? knob + 4f : knob;
                    Fill(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), Paper, size * 0.5f);
                    Frame(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), Ink, 2f, size * 0.5f);
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
            const int n = 96, samples = 4;
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

        private static readonly Vector2[] Heart = { new(0.11f, 0.60f), new(0.50f, 0.10f), new(0.89f, 0.60f), new(0.50f, 0.66f) };

        private static bool Inside(HudIcon icon, Vector2 p)
        {
            switch (icon)
            {
                case HudIcon.Food: // an apple: two round halves with a dip at the top, a stalk and a leaf (a drumstick
                                   // inside the ring read as a magnifying glass)
                    bool body = (Disc(p, new Vector2(0.37f, 0.45f), 0.27f) || Disc(p, new Vector2(0.63f, 0.45f), 0.27f) ||
                                 Disc(p, new Vector2(0.5f, 0.3f), 0.22f)) && !Disc(p, new Vector2(0.5f, 0.84f), 0.13f);
                    return body || Capsule(p, new Vector2(0.5f, 0.66f), new Vector2(0.55f, 0.88f), 0.035f) ||
                           Capsule(p, new Vector2(0.6f, 0.8f), new Vector2(0.78f, 0.87f), 0.06f);
                case HudIcon.Stamina:
                    return InPolygon(p, Bolt);
                case HudIcon.Health: // a heart: two round tops and a point at the bottom
                    return Disc(p, new Vector2(0.32f, 0.64f), 0.22f) || Disc(p, new Vector2(0.68f, 0.64f), 0.22f) || InPolygon(p, Heart);
                case HudIcon.Check: // a tick
                    return Capsule(p, new Vector2(0.22f, 0.50f), new Vector2(0.42f, 0.29f), 0.085f) ||
                           Capsule(p, new Vector2(0.42f, 0.29f), new Vector2(0.80f, 0.71f), 0.085f);
                case HudIcon.Alert: // an exclamation mark
                    return Capsule(p, new Vector2(0.5f, 0.80f), new Vector2(0.5f, 0.43f), 0.09f) || Disc(p, new Vector2(0.5f, 0.2f), 0.095f);
                case HudIcon.Info: // a small i
                    return Disc(p, new Vector2(0.5f, 0.78f), 0.095f) || Capsule(p, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.2f), 0.085f);
                default: // bubbles
                    return Ring(p, new Vector2(0.38f, 0.36f), 0.28f, 0.11f) || Ring(p, new Vector2(0.73f, 0.71f), 0.18f, 0.09f) ||
                           Disc(p, new Vector2(0.30f, 0.84f), 0.09f);
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
