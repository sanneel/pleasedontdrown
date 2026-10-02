using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// What a gun adds to the screen while you hold one: a crosshair that opens up with the spread, the scope picture
    /// when looking through a scope, a hit marker, and the gun's card while inspecting. No ammo counter and no reload
    /// bar (How to Fish shows neither): the gun in your hands tells you.
    /// </summary>
    public class WeaponHud : MonoBehaviour
    {
        private static float _hitAt = -10f;
        private Texture2D _scope;
        private int _scopeSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _hitAt = -10f;

        /// <summary>One of our bullets hit something that counts.</summary>
        public static void ShowHitMarker() => _hitAt = Time.unscaledTime;

        private void Awake() => useGUILayout = false; // drawn with fixed boxes: no layout pass needed

        private void OnDestroy()
        {
            if (_scope != null) Destroy(_scope);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return; // nothing here takes input
            Weapon gun = Weapon.Local;
            if (gun == null || !GameInput.GameplayActive) return;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;

            if (Weapon.LocalScoped) DrawScope(cx, cy);
            else if (gun.Aim < 0.5f) DrawCrosshair(gun, cx, cy);

            // Hit marker: four short ticks round the middle.
            float since = Time.unscaledTime - _hitAt;
            if (since < 0.18f)
            {
                GUI.color = new Color(1f, 1f, 1f, 1f - since / 0.18f);
                for (int i = 0; i < 4; i++)
                {
                    Matrix4x4 keep = GUI.matrix;
                    GUIUtility.RotateAroundPivot(45f + 90f * i, new Vector2(cx, cy));
                    GUI.DrawTexture(new Rect(cx + 9f * Hud.Scale, cy - 1.5f * Hud.Scale, 12f * Hud.Scale, 3f * Hud.Scale), Texture2D.whiteTexture);
                    GUI.matrix = keep;
                }
                GUI.color = Color.white;
            }

            if (gun.IsInspecting) DrawCard(gun);
        }

        private void DrawCrosshair(Weapon gun, float cx, float cy)
        {
            // Lines sit where the edge of the spread cone lands on screen.
            Camera cam = Player.PlayerHub.Local != null && Player.PlayerHub.Local.Look != null ? Player.PlayerHub.Local.Look.Camera : null;
            float fov = cam != null ? cam.fieldOfView : 80f;
            float gap = Mathf.Tan(gun.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * cy;
            gap = Mathf.Clamp(gap, 4f * Hud.Scale, Screen.height * 0.25f) + 3f * Hud.Scale;
            float fade = 1f - gun.Aim * 2f;
            GUI.color = new Color(1f, 1f, 1f, 0.85f * fade);
            float len = 11f * Hud.Scale, w = Mathf.Max(2f, 3f * Hud.Scale);
            GUI.DrawTexture(new Rect(cx - w * 0.5f, cy - gap - len, w, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - w * 0.5f, cy + gap, w, len), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - gap - len, cy - w * 0.5f, len, w), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + gap, cy - w * 0.5f, len, w), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void DrawScope(float cx, float cy)
        {
            int size = Screen.height;
            if (_scope == null || _scopeSize != size) BuildScope(size);
            float left = cx - size * 0.5f;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, left + 1f, Screen.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(left + size - 1f, 0f, Screen.width - left - size + 1f, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(left, 0f, size, size), _scope);
        }

        /// <summary>Black all round a round lens, fine crosshairs with thicker outer posts.</summary>
        private void BuildScope(int size)
        {
            if (_scope != null) Destroy(_scope);
            _scopeSize = size;
            int n = Mathf.Min(size, 1024);
            _scope = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[n * n];
            float r = n * 0.46f, c = n * 0.5f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = x - c + 0.5f, dy = y - c + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    byte alpha = (byte)(Mathf.Clamp01((d - r) / 3f) * 255f);
                    float edge = Mathf.Clamp01(1f - Mathf.Abs(d - r) / (n * 0.04f)) * 0.5f; // dark rim inside the lens
                    alpha = (byte)Mathf.Max(alpha, edge * 255f);
                    float thin = n * 0.0012f + 0.5f, thick = n * 0.006f;
                    bool post = Mathf.Abs(dx) > n * 0.12f || Mathf.Abs(dy) > n * 0.12f;
                    float half = post ? thick : thin;
                    if (d < r && ((Mathf.Abs(dx) < half && Mathf.Abs(dy) > 2f) || (Mathf.Abs(dy) < half && Mathf.Abs(dx) > 2f))) alpha = 255;
                    pixels[y * n + x] = new Color32(0, 0, 0, alpha);
                }
            _scope.SetPixels32(pixels);
            _scope.Apply(false, true); // only ever drawn: no copy kept in memory
        }

        /// <summary>The gun's card, on the right at eye height.</summary>
        private void DrawCard(Weapon gun)
        {
            const float width = 380f, height = 250f;
            var area = new Rect(Hud.Width - width - 25f, Hud.Height * 0.5f - height * 0.5f, width, height);
            Hud.Fill(area, new Color(0.02f, 0.06f, 0.09f, 0.7f), 16f);
            Hud.Label(new Rect(area.x + 20f, area.y + 12f, width - 40f, 40f), gun.DisplayName, 30f, Color.white, TextAnchor.MiddleLeft, heavy: true);
            string pellets = gun.Pellets > 1 ? $" x{gun.Pellets}" : "";
            string text = $"Damage <b>{gun.Damage}{pellets}</b>   ({gun.Tier.Name})\n" +
                          $"{(gun.FullAuto ? "Full auto" : "Single shots")},  {Mathf.RoundToInt(60f / Mathf.Max(0.01f, gun.Interval))} rounds a minute\n\n" +
                          $"Sight: {gun.Sight.Name}\nBarrel: {gun.Barrel.Name}" +
                          (gun.HasLaser ? "\nLaser sight" : "") + (gun.HasExtendedMagazine ? "\nExtended magazine" : "");
            Hud.Label(new Rect(area.x + 20f, area.y + 58f, width - 40f, height - 70f), text, 20f, new Color(1f, 1f, 1f, 0.92f), TextAnchor.UpperLeft, wrap: true, shadow: false);
        }
    }
}
