using System;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// "Customize your lifeguard": body, skin, hair, clothes, hat, glasses and extras, with a live 3D preview that
    /// you can spin by dragging. The look is saved locally and worn in every session (synced by PlayerHub).
    /// IMGUI for the prototype, like the other menus.
    /// </summary>
    public class AvatarCustomizer : MonoBehaviour
    {
        private const string PrefsKey = "pdd.avatar.look";
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
        private Texture2D _white;
        private GUIStyle _title, _label, _value, _button;
        private Vector2 _scroll;

        /// <summary>The look this player wears (saved between sessions).</summary>
        public static AvatarLook LocalLook
        {
            get
            {
                if (_localLook == null)
                {
                    string saved = PlayerPrefs.GetString(PrefsKey, "");
                    _localLook = ulong.TryParse(saved, out ulong packed) ? AvatarLook.Unpack(packed) : AvatarLook.Lifeguard;
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

            _texture = new RenderTexture(400, 520, 24) { name = "AvatarPreview", antiAliasing = 4 };
            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(_stage.transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.targetTexture = _texture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.55f, 0.8f, 0.95f);
            _camera.fieldOfView = 30f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 12f;
            _white = Texture2D.whiteTexture;
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

        private void OnGUI()
        {
            if (!_open) return;
            EnsureStyles();
            float w = Mathf.Min(900f, Screen.width - 40f), h = Mathf.Min(600f, Screen.height - 40f);
            var area = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 14f, area.y + 10f, area.width - 28f, area.height - 20f));
            GUILayout.Label("CUSTOMIZE YOUR LIFEGUARD", _title);
            GUILayout.BeginHorizontal();

            // Preview (drag to spin).
            float ph = h - 110f, pw = ph * 400f / 520f;
            Rect preview = GUILayoutUtility.GetRect(pw, ph, GUILayout.Width(pw), GUILayout.Height(ph));
            if (_texture != null) GUI.DrawTexture(preview, _texture, ScaleMode.ScaleToFit, false);
            Event e = Event.current;
            if (e.type == EventType.MouseDrag && preview.Contains(e.mousePosition))
            {
                _orbit -= e.delta.x * 0.6f;
                e.Use();
            }
            GUILayout.Space(16f);

            GUILayout.BeginVertical();
            _scroll = GUILayout.BeginScrollView(_scroll);
            AvatarLook look = LocalLook;
            bool changed = false;
            changed |= Row("Body", AvatarLook.BuildNames[look.Build], ref look.Build, 4);
            changed |= Row("Figure", AvatarLook.FigureNames[look.Figure % 2], ref look.Figure, 2);
            changed |= Row("Height", AvatarLook.HeightNames[look.Height], ref look.Height, 4);
            changed |= ColorRow("Skin", ref look.Skin, AvatarLook.SkinTones);
            changed |= EnumRow("Hair", ref look.Hair);
            changed |= ColorRow("Hair colour", ref look.HairColor, AvatarLook.HairColors);
            changed |= EnumRow("Top", ref look.Top);
            changed |= ColorRow("Top colour", ref look.TopColor, AvatarLook.ClothColors);
            changed |= EnumRow("Shorts", ref look.Bottom);
            changed |= ColorRow("Shorts colour", ref look.BottomColor, AvatarLook.ClothColors);
            changed |= EnumRow("Hat", ref look.Hat);
            changed |= ColorRow("Hat colour", ref look.HatColor, AvatarLook.ClothColors);
            changed |= EnumRow("Glasses", ref look.Glasses);
            changed |= EnumRow("Facial hair", ref look.Face);
            changed |= Toggle("Whistle", ref look.Extras, AvatarExtras.Whistle);
            changed |= Toggle("Sunscreen nose", ref look.Extras, AvatarExtras.Sunscreen);
            changed |= Toggle("Arm floaties", ref look.Extras, AvatarExtras.Floaties);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Randomize", _button, GUILayout.Height(32f)))
            {
                look = AvatarLook.Random(new System.Random(Environment.TickCount));
                changed = true;
            }
            if (GUILayout.Button("Station uniform", _button, GUILayout.Height(32f)))
            {
                look = AvatarLook.Lifeguard;
                changed = true;
            }
            if (GUILayout.Button("Wave", _button, GUILayout.Height(32f)) && _animator != null)
                _animator.Play(AvatarGesture.Wave);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Done", _button, GUILayout.Width(140f), GUILayout.Height(32f)))
                SetOpen(false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (changed) Set(look);
        }

        private bool Row(string label, string value, ref byte field, int count)
        {
            int dir = Arrows(label, value, null);
            if (dir == 0) return false;
            field = (byte)((field + dir + count) % count);
            return true;
        }

        private bool EnumRow<T>(string label, ref T field) where T : Enum
        {
            Array values = Enum.GetValues(typeof(T));
            int index = Array.IndexOf(values, field);
            int dir = Arrows(label, Nicify(field.ToString()), null);
            if (dir == 0) return false;
            field = (T)values.GetValue((index + dir + values.Length) % values.Length);
            return true;
        }

        private bool ColorRow(string label, ref byte field, Color[] palette)
        {
            int dir = Arrows(label, $"{field + 1} / {palette.Length}", palette[field % palette.Length]);
            if (dir == 0) return false;
            field = (byte)((field + dir + palette.Length) % palette.Length);
            return true;
        }

        private bool Toggle(string label, ref AvatarExtras extras, AvatarExtras flag)
        {
            bool on = (extras & flag) != 0;
            int dir = Arrows(label, on ? "Yes" : "No", null);
            if (dir == 0) return false;
            extras ^= flag;
            return true;
        }

        /// <summary>One "label  &lt;  value  &gt;" row. Returns -1, 0 or +1.</summary>
        private int Arrows(string label, string value, Color? swatch)
        {
            int dir = 0;
            GUILayout.BeginHorizontal(GUILayout.Height(28f));
            GUILayout.Label(label, _label, GUILayout.Width(130f));
            if (GUILayout.Button("<", _button, GUILayout.Width(34f), GUILayout.Height(26f))) dir = -1;
            Rect r = GUILayoutUtility.GetRect(170f, 26f, GUILayout.Width(170f));
            if (swatch.HasValue)
            {
                Color c = GUI.color;
                GUI.color = swatch.Value;
                GUI.DrawTexture(new Rect(r.x + 6f, r.y + 4f, 40f, r.height - 8f), _white);
                GUI.color = c;
                GUI.Label(new Rect(r.x + 50f, r.y, r.width - 50f, r.height), value, _value);
            }
            else GUI.Label(r, value, _value);
            if (GUILayout.Button(">", _button, GUILayout.Width(34f), GUILayout.Height(26f))) dir = 1;
            GUILayout.EndHorizontal();
            return dir;
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

        private void EnsureStyles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            _value = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
        }
    }
}
