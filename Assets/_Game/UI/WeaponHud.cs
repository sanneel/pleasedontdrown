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
        private static Color _hitColour = Color.white;
        private static float _hitSize = 1f, _hitLength = 0.18f;
        private Texture2D _scope;
        private int _scopeSize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _hitAt = -10f;

        /// <summary>One of our bullets hit something that counts.</summary>
        public static void ShowHitMarker() => ShowHitMarker(false, false);

        /// <summary>
        /// The host confirmed our hit: a headshot marks yellow and bigger, a kill red, biggest and longest (a plain hit
        /// stays white). A plain confirmation doesn't shrink a headshot or kill marker still showing.
        /// </summary>
        public static void ShowHitMarker(bool head, bool kill)
        {
            bool showingBig = Time.unscaledTime - _hitAt < _hitLength && _hitSize > 1f;
            if (!head && !kill && showingBig) return;
            _hitAt = Time.unscaledTime;
            _hitColour = kill ? new Color(1f, 0.22f, 0.18f) : head ? new Color(1f, 0.85f, 0.2f) : Color.white;
            _hitSize = kill ? 1.6f : head ? 1.3f : 1f;
            _hitLength = kill ? 0.45f : head ? 0.3f : 0.18f;
        }

        // Drawn on the HUD canvas (no IMGUI): see HudCanvas.
        private void OnEnable() => HudCanvas.Add(this, HudCanvas.OrderWeapon, DrawHud);

        private void OnDisable() => HudCanvas.Remove(this);

        private void OnDestroy()
        {
            if (_scope != null) Destroy(_scope);
        }

        private void DrawHud()
        {
            Weapon gun = Weapon.Local;
            if (gun == null || !GameInput.GameplayActive) return;
            float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;

            if (Weapon.LocalScoped) DrawScope(cx, cy);
            else if (gun.Aim < 0.5f) DrawCrosshair(gun, cx, cy);

            // Hit marker: four short ticks round the middle (popping out a little, bigger and coloured for a headshot / kill).
            float since = Time.unscaledTime - _hitAt;
            if (since < _hitLength)
            {
                float t = since / _hitLength;
                var colour = new Color(_hitColour.r, _hitColour.g, _hitColour.b, 1f - t * t);
                float s = Hud.Scale * _hitSize * (1f + 0.25f * (1f - t));
                for (int i = 0; i < 4; i++)
                    Hud.PixelBox(new Rect(cx + 9f * s, cy - 1.5f * s, 12f * s, 3f * s), colour, 45f + 90f * i, new Vector2(cx, cy));
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
            var colour = new Color(1f, 1f, 1f, 0.85f * fade);
            float len = 11f * Hud.Scale, w = Mathf.Max(2f, 3f * Hud.Scale);
            Hud.PixelBox(new Rect(cx - w * 0.5f, cy - gap - len, w, len), colour);
            Hud.PixelBox(new Rect(cx - w * 0.5f, cy + gap, w, len), colour);
            Hud.PixelBox(new Rect(cx - gap - len, cy - w * 0.5f, len, w), colour);
            Hud.PixelBox(new Rect(cx + gap, cy - w * 0.5f, len, w), colour);
        }

        private void DrawScope(float cx, float cy)
        {
            int size = Screen.height;
            if (_scope == null || _scopeSize != size) BuildScope(size);
            float left = cx - size * 0.5f;
            Hud.PixelBox(new Rect(0f, 0f, left + 1f, Screen.height), Color.black);
            Hud.PixelBox(new Rect(left + size - 1f, 0f, Screen.width - left - size + 1f, Screen.height), Color.black);
            Hud.PixelPicture(new Rect(left, 0f, size, size), _scope, Color.white);
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
            const float width = 396f, height = 310f;
            var area = new Rect(Hud.Width - width - Hud.Margin, Hud.Height * 0.5f - height * 0.5f, width, height);
            Hud.Surface(area, 0.94f, Hud.Teal);
            Hud.Label(new Rect(area.x + 22f, area.y + 14f, width - 44f, 22f), "EQUIPMENT", 15f, Hud.Teal, TextAnchor.MiddleLeft, heavy: true, shadow: false);
            Hud.FitLabel(new Rect(area.x + 22f, area.y + 38f, width - 44f, 40f), gun.DisplayName, 28f, Hud.Paper, heavy: true);
            Hud.Fill(new Rect(area.x + 22f, area.y + 88f, width - 44f, 1f), new Color(1f, 1f, 1f, 0.14f));
            string pellets = gun.Pellets > 1 ? $" x{gun.Pellets}" : "";
            string text = $"Damage <b>{gun.Damage}{pellets}</b>   ({gun.Tier.Name})\n" +
                          $"{(gun.FullAuto ? "Full auto" : "Single shots")},  {Mathf.RoundToInt(60f / Mathf.Max(0.01f, gun.Interval))} rounds a minute\n\n" +
                          $"Sight: {gun.Sight.Name}\nBarrel: {gun.Barrel.Name}" +
                          (gun.HasLaser ? "\nLaser sight" : "") + (gun.HasExtendedMagazine ? "\nExtended magazine" : "");
            Hud.Label(new Rect(area.x + 22f, area.y + 104f, width - 44f, height - 124f), text, 20f, Hud.Muted, TextAnchor.UpperLeft, wrap: true, shadow: false);
        }
    }
}
