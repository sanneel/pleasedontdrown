using System;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// "Customize your lifeguard": body, skin, hair, clothes, hat, glasses and extras, with a live 3D preview that
    /// you can spin by dragging. The look is saved locally and worn in every session (synced by PlayerHub).
    /// Drawn with <see cref="Hud"/> on its 1080-high sheet, like the other menus.
    /// </summary>
    public class AvatarCustomizer : MonoBehaviour
    {
        private const string PrefsKey = "pdd.avatar.look";
        private const string GoofyKey = "pdd.avatar.goofy"; // set once a saved classic look has been moved onto the goofy body
        private static readonly Vector3 StagePosition = new(0f, -300f, 0f);

        private static AvatarCustomizer _instance;
        private static AvatarLook? _localLook;

        private bool _open;
        private GameObject _stage;
        private AvatarRig _rig;
        private AvatarAnimator _animator;
        private Camera _camera;
        private RenderTexture _texture;
        private float _orbit;

        /// <summary>The look this player wears (saved between sessions).</summary>
        public static AvatarLook LocalLook
        {
            get
            {
                if (_localLook == null)
                {
                    string saved = PlayerPrefs.GetString(PrefsKey, "");
                    AvatarLook look = ulong.TryParse(saved, out ulong packed) ? AvatarLook.Unpack(packed) : AvatarLook.Lifeguard;
                    // Everyone becomes the goofy lifeguard once (keeping their colours); CHARACTER switches back.
                    if (PlayerPrefs.GetInt(GoofyKey, 0) == 0)
                    {
                        PlayerPrefs.SetInt(GoofyKey, 1);
                        if (look.Body == 0) look.Body = AvatarLook.Bodies.Goofy;
                        PlayerPrefs.SetString(PrefsKey, look.Pack().ToString());
                        PlayerPrefs.Save();
                    }
                    _localLook = look;
                }
                return _localLook.Value;
            }
        }

        public static event Action<AvatarLook> LookChanged;
        public static bool IsOpen => _instance != null && _instance._open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null;
            _localLook = null;
            LookChanged = null;
        }

        public static void Open()
        {
            if (_instance != null) _instance.SetOpen(true);
        }

        public static void Close()
        {
            if (_instance != null) _instance.SetOpen(false);
        }

        private void Awake()
        {
            _instance = this;
            useGUILayout = false; // drawn with fixed boxes: no layout pass needed
            DevCommands.Register("customize", "", "Open the lifeguard customization panel.", _ => SetOpen(true), owner: this);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            DevCommands.Unregister("customize", this);
            if (_open) GameInput.PopUI();
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
            if (_stage != null) Destroy(_stage);
        }

        private void Update()
        {
            if (_open && GameInput.ToggleMenu.WasPressedThisFrame()) SetOpen(false);
        }

        private void SetOpen(bool open)
        {
            if (open == _open) return;
            _open = open;
            if (open)
            {
                GameInput.PushUI();
                EnsureStage();
                _rig.Build(LocalLook);
                _stage.SetActive(true);
                _animator.Play(AvatarGesture.Wave);
            }
            else
            {
                GameInput.PopUI();
                if (_stage != null) _stage.SetActive(false);
            }
        }

        private void Set(AvatarLook look)
        {
            _localLook = look;
            PlayerPrefs.SetString(PrefsKey, look.Pack().ToString());
            PlayerPrefs.Save();
            if (_rig != null) _rig.Build(look);
            LookChanged?.Invoke(look);
        }

        // ------------------------------------------------------------------ preview stage

        private void EnsureStage()
        {
            if (_stage != null) return;
            _stage = new GameObject("AvatarPreviewStage");
            _stage.transform.position = StagePosition;
            var avatar = new GameObject("PreviewAvatar");
            avatar.transform.SetParent(_stage.transform, false);
            _rig = avatar.AddComponent<AvatarRig>();
            _animator = avatar.AddComponent<AvatarAnimator>();
            _animator.Rig = _rig;
            _animator.Motion = new AvatarMotion { FacingYaw = 0f, Grounded = true };

            _texture = new RenderTexture(800, 1040, 24) { name = "AvatarPreview", antiAliasing = 4 }; // sharp at 4K too
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_stage.transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.targetTexture = _texture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.55f, 0.8f, 0.95f);
            _camera.fieldOfView = 30f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 12f;
        }

        private void LateUpdate()
        {
            if (!_open || _camera == null) return;
            Vector3 center = StagePosition + Vector3.up * 0.95f;
            // Yaw 0 = in front of the character (it faces +z), a little above.
            _camera.transform.position = center + Quaternion.Euler(-9f, _orbit, 0f) * new Vector3(0f, 0f, 4.2f);
            _camera.transform.LookAt(center);
        }

        // ------------------------------------------------------------------ panel

        private const float PanelWidth = 1300f, PanelHeight = 880f;
        private const float RowHeight = 37f, LabelWidth = 196f, ChipSize = 28f, ChipGap = 5f;

        private void OnGUI()
        {
            if (!_open) return;
            Hud.Dim();
            // On a narrow window the whole panel shrinks to fit rather than running off the side.
            float fit = Mathf.Min(1f, (Hud.Width - 40f) / PanelWidth);
            Matrix4x4 before = GUI.matrix;
            if (fit < 1f) GUIUtility.ScaleAroundPivot(new Vector2(fit, fit), new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            var panel = new Rect((Hud.Width - PanelWidth) * 0.5f, (Hud.Height - PanelHeight) * 0.5f, PanelWidth, PanelHeight);
            Hud.Panel(panel, 0.92f);
            Hud.Label(new Rect(panel.x + 36f, panel.y + 18f, 900f, 56f), "YOUR LIFEGUARD", 44f, Color.white, TextAnchor.MiddleLeft, heavy: true, shadow: false);

            // Preview (drag to spin).
            var preview = new Rect(panel.x + 36f, panel.y + 96f, 470f, 611f);
            Hud.Fill(preview, new Color(0.55f, 0.8f, 0.95f), 16f);
            if (_texture != null) Hud.Picture(preview, _texture, Color.white);
            Hud.Label(new Rect(preview.x, preview.yMax + 6f, preview.width, 28f), "Drag to turn", 18f, new Color(1f, 1f, 1f, 0.6f), shadow: false);
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && Hud.Hovered(preview))
            {
                _orbit -= e.delta.x * 0.6f;
                e.Use();
            }

            AvatarLook look = LocalLook;
            bool changed = false;
            float x = panel.x + 540f, y = panel.y + 92f;
            bool goofy = look.IsGoofy;
            if (Arrows(x, ref y, "CHARACTER", goofy ? "Goofy" : "Classic") != 0)
            {
                look.Body = goofy ? (byte)0 : AvatarLook.Bodies.Goofy;
                goofy = !goofy;
                changed = true;
            }
            if (goofy)
            {
                // The goofy lifeguard: shape and face first (that's the fun), then colours and things to wear.
                if (Arrows(x, ref y, "HEAD", AvatarLook.HeadSizeName(look.HeadSize)) != 0)
                {
                    look.HeadSize = look.HeadSize == AvatarLook.SmallHead ? (byte)0 : AvatarLook.SmallHead;
                    changed = true;
                }
                changed |= Row(x, ref y, "BELLY", AvatarLook.BellyNames[look.Belly & 3], ref look.Belly, 4);
                changed |= Row(x, ref y, "NOSE", AvatarLook.NoseNames[look.Nose & 3], ref look.Nose, 4);
                changed |= Row(x, ref y, "EYES", AvatarLook.EyeNames[look.Eyes & 3], ref look.Eyes, 4);
                changed |= Row(x, ref y, "TEETH", AvatarLook.TeethNames[look.Teeth & 3], ref look.Teeth, 4);
                changed |= ColorRow(x, ref y, "SKIN", ref look.Skin, AvatarLook.SkinTones);
                changed |= ColorRow(x, ref y, "HAIR COLOUR", ref look.HairColor, AvatarLook.HairColors);
                changed |= ColorRow(x, ref y, "TOP COLOUR", ref look.TopColor, AvatarLook.ClothColors);
                changed |= ColorRow(x, ref y, "SHORTS COLOUR", ref look.BottomColor, AvatarLook.ClothColors);
                changed |= HatRow(x, ref y, ref look.Hat);
                changed |= ColorRow(x, ref y, "HAT COLOUR", ref look.HatColor, AvatarLook.ClothColors);
                changed |= EnumRow(x, ref y, "GLASSES", ref look.Glasses);
                changed |= EnumRow(x, ref y, "FACIAL HAIR", ref look.Face);
                changed |= Toggle(x, ref y, "SUNSCREEN NOSE", ref look.Extras, AvatarExtras.Sunscreen);
                changed |= Toggle(x, ref y, "ARM FLOATIES", ref look.Extras, AvatarExtras.Floaties);
            }
            else
            {
                changed |= Row(x, ref y, "BODY", AvatarLook.BuildNames[look.Build], ref look.Build, 4);
                changed |= Row(x, ref y, "FIGURE", AvatarLook.FigureNames[look.Figure % 2], ref look.Figure, 2);
                changed |= Row(x, ref y, "HEIGHT", AvatarLook.HeightNames[look.Height], ref look.Height, 4);
                changed |= ColorRow(x, ref y, "SKIN", ref look.Skin, AvatarLook.SkinTones);
                changed |= EnumRow(x, ref y, "HAIR", ref look.Hair);
                changed |= ColorRow(x, ref y, "HAIR COLOUR", ref look.HairColor, AvatarLook.HairColors);
                changed |= EnumRow(x, ref y, "TOP", ref look.Top);
                changed |= ColorRow(x, ref y, "TOP COLOUR", ref look.TopColor, AvatarLook.ClothColors);
                changed |= EnumRow(x, ref y, "SHORTS", ref look.Bottom);
                changed |= ColorRow(x, ref y, "SHORTS COLOUR", ref look.BottomColor, AvatarLook.ClothColors);
                changed |= HatRow(x, ref y, ref look.Hat);
                changed |= ColorRow(x, ref y, "HAT COLOUR", ref look.HatColor, AvatarLook.ClothColors);
                changed |= EnumRow(x, ref y, "GLASSES", ref look.Glasses);
                changed |= EnumRow(x, ref y, "FACIAL HAIR", ref look.Face);
                changed |= Toggle(x, ref y, "WHISTLE", ref look.Extras, AvatarExtras.Whistle);
                changed |= Toggle(x, ref y, "SUNSCREEN NOSE", ref look.Extras, AvatarExtras.Sunscreen);
                changed |= Toggle(x, ref y, "ARM FLOATIES", ref look.Extras, AvatarExtras.Floaties);
            }

            float by = panel.yMax - 84f, bx = panel.x + 36f;
            if (Hud.Button(new Rect(bx, by, 220f, 56f), "RANDOM", centred: true, small: true))
            {
                AvatarLook random = AvatarLook.Random(new System.Random(Environment.TickCount));
                if (!goofy) random.Body = 0;
                look = random;
                changed = true;
            }
            if (Hud.Button(new Rect(bx + 232f, by, 238f, 56f), "UNIFORM", centred: true, small: true))
            {
                look = goofy ? AvatarLook.Lifeguard : AvatarLook.ClassicLifeguard;
                changed = true;
            }
            if (Hud.Button(new Rect(bx + 510f, by, 160f, 56f), "WAVE", centred: true, small: true) && _animator != null)
                _animator.Play(AvatarGesture.Wave);
            bool done = Hud.Button(new Rect(panel.xMax - 276f, by, 240f, 56f), "DONE", centred: true, primary: true);
            GUI.matrix = before;

            if (changed) Set(look);
            if (done) SetOpen(false);
        }

        private static bool Row(float x, ref float y, string label, string value, ref byte field, int count)
        {
            int dir = Arrows(x, ref y, label, value);
            if (dir == 0) return false;
            field = (byte)((field + dir + count) % count);
            return true;
        }

        /// <summary>Only the snug hats (AvatarLook.Hats).</summary>
        private static bool HatRow(float x, ref float y, ref HatStyle hat)
        {
            HatStyle[] hats = AvatarLook.Hats;
            int index = Mathf.Max(0, Array.IndexOf(hats, hat));
            int dir = Arrows(x, ref y, "HAT", EnumValues<HatStyle>.Names[Array.IndexOf(EnumValues<HatStyle>.All, hats[index])]);
            if (dir == 0) return false;
            hat = hats[(index + dir + hats.Length) % hats.Length];
            return true;
        }

        private static bool EnumRow<T>(float x, ref float y, string label, ref T field) where T : Enum
        {
            T[] values = EnumValues<T>.All;
            int index = Array.IndexOf(values, field);
            int dir = Arrows(x, ref y, label, EnumValues<T>.Names[Mathf.Max(0, index)]);
            if (dir == 0) return false;
            field = values[(index + dir + values.Length) % values.Length];
            return true;
        }

        /// <summary>The palette as chips to click: the one worn has a white frame.</summary>
        private static bool ColorRow(float x, ref float y, string label, ref byte field, Color[] palette)
        {
            Hud.Label(new Rect(x, y, LabelWidth, RowHeight), label, 20f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            bool changed = false;
            for (int i = 0; i < palette.Length; i++)
            {
                var chip = new Rect(x + LabelWidth + i * (ChipSize + ChipGap), y + (RowHeight - ChipSize) * 0.5f, ChipSize, ChipSize);
                if (!Hud.Chip(chip, palette[i], i == field % palette.Length) || i == field) continue;
                field = (byte)i;
                changed = true;
            }
            y += RowHeight;
            return changed;
        }

        private static bool Toggle(float x, ref float y, string label, ref AvatarExtras extras, AvatarExtras flag)
        {
            bool on = (extras & flag) != 0;
            Hud.Label(new Rect(x, y, LabelWidth, RowHeight), label, 20f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            bool pressed = Hud.Button(new Rect(x + LabelWidth, y + 3f, 110f, RowHeight - 6f), on ? "YES" : "NO", centred: true, small: true, selected: on);
            y += RowHeight;
            if (pressed) extras ^= flag;
            return pressed;
        }

        /// <summary>One "label  &lt;  value  &gt;" row. Returns -1, 0 or +1.</summary>
        private static int Arrows(float x, ref float y, string label, string value)
        {
            const float arrow = 44f, wide = 300f;
            int dir = 0;
            Hud.Label(new Rect(x, y, LabelWidth, RowHeight), label, 20f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            float bx = x + LabelWidth;
            if (Hud.Button(new Rect(bx, y + 3f, arrow, RowHeight - 6f), "<", centred: true, small: true)) dir = -1;
            Hud.Label(new Rect(bx + arrow, y, wide, RowHeight), value, 24f, Color.white, heavy: true, shadow: false);
            if (Hud.Button(new Rect(bx + arrow + wide, y + 3f, arrow, RowHeight - 6f), ">", centred: true, small: true)) dir = 1;
            y += RowHeight;
            return dir;
        }

        /// <summary>An enum's values and their names as shown ("CapBackwards" = "Cap backwards"), made once.</summary>
        private static class EnumValues<T> where T : Enum
        {
            public static readonly T[] All = (T[])Enum.GetValues(typeof(T));
            public static readonly string[] Names = Array.ConvertAll(All, v => Nicify(v.ToString()));
        }

        private static string Nicify(string name)
        {
            var sb = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
                sb.Append(i > 0 ? char.ToLowerInvariant(name[i]) : name[i]);
            }
            return sb.ToString();
        }
    }
}
