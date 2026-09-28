using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// What a gun adds to the screen while you hold one: ammo counter and reload bar, a crosshair that opens up with
    /// the spread, the scope picture when looking through a scope, a hit marker, and the gun's card while inspecting.
    /// </summary>
    public class WeaponHud : MonoBehaviour
    {
        private static float _hitAt = -10f;
        private GUIStyle _big, _small, _card;
        private Texture2D _scope;
        private int _scopeSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _hitAt = -10f;

        /// <summary>One of our bullets hit something that counts.</summary>
        public static void ShowHitMarker() => _hitAt = Time.unscaledTime;

        private void OnGUI()
        {
            Weapon gun = Weapon.Local;
            if (gun == null || !GameInput.GameplayActive) return;
            EnsureStyles();
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
                    GUI.DrawTexture(new Rect(cx + 7f, cy - 1f, 9f, 2f), Texture2D.whiteTexture);
                    GUI.matrix = keep;
                }
                GUI.color = Color.white;
            }

            // Ammo, bottom right.
            float x = Screen.width - 250f, y = Screen.height - 150f;
            string ammo = gun.Ammo == 0 && !gun.IsReloading
                ? $"<color=#ff6b5a>{gun.Ammo}</color>"
                : gun.Ammo <= Mathf.Max(1, gun.MagazineSize / 4) ? $"<color=#ffd24a>{gun.Ammo}</color>" : gun.Ammo.ToString();
            Shadowed(new Rect(x, y, 220f, 56f), $"{ammo}<size=22> / {gun.MagazineSize}</size>", _big);
            string mode = gun.FullAuto ? "AUTO" : gun.Pellets > 1 ? "PUMP" : "SEMI";
            Shadowed(new Rect(x, y + 52f, 220f, 22f), $"{gun.DisplayName}  <color=#bbbbbb>{mode}</color>", _small);
            if (gun.IsReloading)
            {
                Rect bar = new Rect(cx - 70f, cy + 40f, 140f, 6f);
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.85f, 0.3f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * gun.ReloadProgress, bar.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
                Shadowed(new Rect(cx - 70f, cy + 48f, 140f, 22f), "RELOADING", _small);
            }
            else if (gun.Ammo == 0)
            {
                Shadowed(new Rect(cx - 120f, cy + 40f, 240f, 22f), $"<b>[{GameInput.KeyLabel(GameInput.Reload)}]</b> reload", _small);
            }

            if (gun.IsInspecting) DrawCard(gun);
        }

        private void DrawCrosshair(Weapon gun, float cx, float cy)
        {
            // Lines sit where the edge of the spread cone lands on screen.
            Camera cam = Player.PlayerHub.Local != null && Player.PlayerHub.Local.Look != null ? Player.PlayerHub.Local.Look.Camera : null;
            float fov = cam != null ? cam.fieldOfView : 80f;
            float gap = Mathf.Tan(gun.CurrentSpread * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * cy;
            gap = Mathf.Clamp(gap, 4f, Screen.height * 0.25f) + 3f;
            float fade = 1f - gun.Aim * 2f;
            GUI.color = new Color(1f, 1f, 1f, 0.85f * fade);
            const float len = 8f, w = 2f;
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
            _scope.Apply();
        }

        private void DrawCard(Weapon gun)
        {
            var area = new Rect(40f, Screen.height * 0.5f - 110f, 300f, 220f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            string pellets = gun.Pellets > 1 ? $" x{gun.Pellets}" : "";
            string text = $"<b><size=20>{gun.DisplayName}</size></b>\n" +
                          $"Damage <b>{gun.Damage}{pellets}</b>   ({gun.Tier.Name})\n" +
                          $"Magazine <b>{gun.MagazineSize}</b>   {(gun.FullAuto ? "full auto" : "single shots")}\n" +
                          $"{Mathf.RoundToInt(60f / Mathf.Max(0.01f, gun.Interval))} rounds a minute\n\n" +
                          $"Sight: {gun.Sight.Name}\nBarrel: {gun.Barrel.Name}" +
                          (gun.HasLaser ? "\nLaser sight" : "") + (gun.HasExtendedMagazine ? "\nExtended magazine" : "");
            GUI.Label(new Rect(area.x + 14f, area.y + 10f, area.width - 28f, area.height - 20f), text, _card);
        }

        private void Shadowed(Rect r, string text, GUIStyle style)
        {
            Color c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 2f, r.y + 2f, r.width, r.height), StripColors(text), style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        private static string StripColors(string s) => System.Text.RegularExpressions.Regex.Replace(s, "</?color[^>]*>", "");

        private void EnsureStyles()
        {
            if (_big != null) return;
            _big = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight, richText = true, normal = { textColor = Color.white } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperRight, richText = true, normal = { textColor = Color.white } };
            _card = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true, wordWrap = true, normal = { textColor = Color.white } };
        }
    }
}
