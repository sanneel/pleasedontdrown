using System.Collections.Generic;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// Island 3 (Skull Cove) is always at night (Docs/16-Plan-Islands-2-3.md); the other islands keep their sunny day.
    /// Each machine blends its own picture by where its camera is: on island 3 and its waters full night, fading back to
    /// day over the channel. Night is a moon (the sun light turned round, pale blue and dim), a dark banded sky with
    /// stars, a dark sea, low blue ambient light and a thick dark sea mist that hides the hotel island over the water.
    /// The island's own lights (lantern posts, the campfire, the lookout) only burn while it's night; the campfire
    /// flickers. Nothing in the scene or the assets changes: the sky and sea get runtime copies of their materials.
    /// </summary>
    public class IslandNight : MonoBehaviour
    {
        // Island 3's footprint (GameSceneBuilder.Island3: centre (20, -900), half size 95 x 118) and how far out the
        // night reaches: full night inside, gone this far beyond the coast.
        private static readonly Vector2 Centre = new(20f, -900f), Half = new(95f, 118f);
        private const float FullNightOut = 40f, DayOut = 190f;
        // Island 2 (the hotel) at night only for the gala (Docs/16): its footprint and how far the night reaches from it.
        private static readonly Vector2 HotelCentre = new(-5f, -268f), HotelHalf = new(95f, 62f);
        private const float HotelFullNightOut = 60f, HotelDayOut = 230f, HotelFadeSeconds = 5f;

        private struct SkyLook { public Color Top, Horizon, Bottom, Sun, Cloud, CloudShade; public float SunIntensity, SunRadius, SunGlow, Stars; }
        private struct SeaLook { public Color Shallow, Deep, Sky, Ripple, Foam; }

        private static readonly SkyLook NightSky = new()
        {
            Top = new Color(0.012f, 0.02f, 0.06f), Horizon = new Color(0.035f, 0.055f, 0.11f), Bottom = new Color(0.035f, 0.055f, 0.11f),
            Sun = new Color(0.82f, 0.86f, 1f), Cloud = new Color(0.16f, 0.18f, 0.27f), CloudShade = new Color(0.06f, 0.07f, 0.12f),
            SunIntensity = 2.2f, SunRadius = 1.7f, SunGlow = 0.35f, Stars = 1f
        };
        private static readonly SeaLook NightSea = new()
        {
            Shallow = new Color(0.02f, 0.08f, 0.13f), Deep = new Color(0f, 0.02f, 0.05f), Sky = new Color(0.06f, 0.09f, 0.18f),
            Ripple = new Color(0.3f, 0.4f, 0.55f), Foam = new Color(0.4f, 0.45f, 0.55f)
        };
        private static readonly Color NightFog = new(0.035f, 0.055f, 0.11f);
        private const float NightFogDensity = 0.0075f;
        private static readonly Color NightMoon = new(0.55f, 0.66f, 1f);
        private const float NightMoonIntensity = 0.36f;
        private static readonly Quaternion MoonRotation = Quaternion.Euler(48f, 205f, 0f);
        private static readonly Color NightAmbientSky = new(0.09f, 0.115f, 0.22f), NightAmbientEquator = new(0.05f, 0.06f, 0.1f), NightAmbientGround = new(0.025f, 0.025f, 0.035f);

        /// <summary>0 day .. 1 night, for this machine's camera (anyone may read it: sounds, sentries' sight...).</summary>
        public static float Night { get; private set; }
        /// <summary>The gala is on: island 2 goes to night too (set on every machine from the story's synced state).</summary>
        public static bool HotelGala { get; set; }
        private static float _hotelBlend; // the gala's night fading in and out (0..1)

        private Light _sun;
        private Color _daySunColor;
        private float _daySunIntensity;
        private Quaternion _daySunRotation;
        private Material _sky, _sea;
        private SkyLook _daySky;
        private SeaLook _daySea;
        private Color _dayFog, _dayAmbientSky, _dayAmbientEquator, _dayAmbientGround;
        private float _dayFogDensity;
        private readonly List<Light> _lights = new();
        private readonly List<Light> _flicker = new();
        private float _applied = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            Night = 0f;
            HotelGala = false;
            _hotelBlend = 0f;
            if (GameObject.Find("Environment/Island3") == null) return; // not the game scene
            new GameObject("IslandNight").AddComponent<IslandNight>();
        }

        private void Start()
        {
            _sun = RenderSettings.sun;
            if (_sun != null)
            {
                _daySunColor = _sun.color;
                _daySunIntensity = _sun.intensity;
                _daySunRotation = _sun.transform.rotation;
            }
            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_TopColor"))
            {
                _sky = new Material(RenderSettings.skybox) { name = "Sky (night blend)" };
                RenderSettings.skybox = _sky;
                _daySky = new SkyLook
                {
                    Top = _sky.GetColor("_TopColor"), Horizon = _sky.GetColor("_HorizonColor"), Bottom = _sky.GetColor("_BottomColor"),
                    Sun = _sky.GetColor("_SunColor"), Cloud = _sky.GetColor("_CloudColor"), CloudShade = _sky.GetColor("_CloudShade"),
                    SunIntensity = _sky.GetFloat("_SunIntensity"), SunRadius = _sky.GetFloat("_SunRadius"), SunGlow = _sky.GetFloat("_SunGlow"),
                    Stars = _sky.HasProperty("_Stars") ? _sky.GetFloat("_Stars") : 0f
                };
            }
            // The sea: every renderer that draws with the ocean material gets one shared runtime copy.
            Material ocean = null;
            var renderers = new List<Renderer>();
            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material m = r.sharedMaterial;
                if (m == null || !m.HasProperty("_ShallowColor") || !m.HasProperty("_DeepColor")) continue;
                if (ocean != null && m != ocean) continue;
                ocean = m;
                renderers.Add(r);
            }
            if (ocean != null)
            {
                _sea = new Material(ocean) { name = "Ocean (night blend)" };
                foreach (Renderer r in renderers) r.sharedMaterial = _sea;
                _daySea = new SeaLook
                {
                    Shallow = _sea.GetColor("_ShallowColor"), Deep = _sea.GetColor("_DeepColor"), Sky = _sea.GetColor("_SkyColor"),
                    Ripple = _sea.GetColor("_RippleColor"), Foam = _sea.HasProperty("_FoamColor") ? _sea.GetColor("_FoamColor") : Color.white
                };
            }
            _dayFog = RenderSettings.fogColor;
            _dayFogDensity = RenderSettings.fogDensity;
            _dayAmbientSky = RenderSettings.ambientSkyColor;
            _dayAmbientEquator = RenderSettings.ambientEquatorColor;
            _dayAmbientGround = RenderSettings.ambientGroundColor;
            // The island's own lights: they burn at night only.
            Transform island = GameObject.Find("Environment/Island3")?.transform;
            if (island != null)
                foreach (Light l in island.GetComponentsInChildren<Light>(true))
                {
                    if (l.type == LightType.Directional) continue;
                    _lights.Add(l);
                    if (l.name.Contains("Campfire")) _flicker.Add(l);
                }
        }

        private void LateUpdate()
        {
            _hotelBlend = Mathf.MoveTowards(_hotelBlend, HotelGala ? 1f : 0f, Time.deltaTime / HotelFadeSeconds);
            Camera cam = Camera.main;
            float k = cam != null ? NightAt(cam.transform.position) : _applied < 0f ? 0f : _applied;
            Night = k;
            if (!Mathf.Approximately(k, _applied)) Apply(k);
            // Underwater has its own fog (UnderwaterFx); above it, the night's (or the day's).
            if (!UnderwaterFx.CameraUnderwater && k > 0.001f)
            {
                RenderSettings.fogColor = Color.Lerp(_dayFog, NightFog, k);
                RenderSettings.fogDensity = Mathf.Lerp(_dayFogDensity, NightFogDensity, k);
            }
            else if (!UnderwaterFx.CameraUnderwater && _applied > 0.001f)
            {
                RenderSettings.fogColor = _dayFog;
                RenderSettings.fogDensity = _dayFogDensity;
            }
            if (k > 0.3f)
                foreach (Light l in _flicker)
                    if (l != null) l.intensity = 2.6f * (0.82f + 0.18f * Mathf.PerlinNoise(Time.time * 7f, l.GetInstanceID() * 0.13f));
        }

        /// <summary>1 on island 3 and close round it, 0 out past the channel (and the same round island 2 during the gala).</summary>
        public static float NightAt(Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(p.x - Centre.x) - Half.x), dz = Mathf.Max(0f, Mathf.Abs(p.z - Centre.y) - Half.y);
            float out_ = Mathf.Sqrt(dx * dx + dz * dz);
            float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullNightOut, DayOut, out_));
            if (_hotelBlend <= 0f) return k;
            dx = Mathf.Max(0f, Mathf.Abs(p.x - HotelCentre.x) - HotelHalf.x);
            dz = Mathf.Max(0f, Mathf.Abs(p.z - HotelCentre.y) - HotelHalf.y);
            float hotel = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HotelFullNightOut, HotelDayOut, Mathf.Sqrt(dx * dx + dz * dz)));
            return Mathf.Max(k, hotel * _hotelBlend);
        }

        private void Apply(float k)
        {
            _applied = k;
            if (_sun != null)
            {
                _sun.color = Color.Lerp(_daySunColor, NightMoon, k);
                _sun.intensity = Mathf.Lerp(_daySunIntensity, NightMoonIntensity, k);
                _sun.transform.rotation = Quaternion.Slerp(_daySunRotation, MoonRotation, k);
            }
            if (_sky != null)
            {
                _sky.SetColor("_TopColor", Color.Lerp(_daySky.Top, NightSky.Top, k));
                _sky.SetColor("_HorizonColor", Color.Lerp(_daySky.Horizon, NightSky.Horizon, k));
                _sky.SetColor("_BottomColor", Color.Lerp(_daySky.Bottom, NightSky.Bottom, k));
                _sky.SetColor("_SunColor", Color.Lerp(_daySky.Sun, NightSky.Sun, k));
                _sky.SetColor("_CloudColor", Color.Lerp(_daySky.Cloud, NightSky.Cloud, k));
                _sky.SetColor("_CloudShade", Color.Lerp(_daySky.CloudShade, NightSky.CloudShade, k));
                _sky.SetFloat("_SunIntensity", Mathf.Lerp(_daySky.SunIntensity, NightSky.SunIntensity, k));
                _sky.SetFloat("_SunRadius", Mathf.Lerp(_daySky.SunRadius, NightSky.SunRadius, k));
                _sky.SetFloat("_SunGlow", Mathf.Lerp(_daySky.SunGlow, NightSky.SunGlow, k));
                if (_sky.HasProperty("_Stars")) _sky.SetFloat("_Stars", Mathf.Lerp(_daySky.Stars, NightSky.Stars, k));
            }
            if (_sea != null)
            {
                _sea.SetColor("_ShallowColor", Color.Lerp(_daySea.Shallow, NightSea.Shallow, k));
                _sea.SetColor("_DeepColor", Color.Lerp(_daySea.Deep, NightSea.Deep, k));
                _sea.SetColor("_SkyColor", Color.Lerp(_daySea.Sky, NightSea.Sky, k));
                _sea.SetColor("_RippleColor", Color.Lerp(_daySea.Ripple, NightSea.Ripple, k));
                if (_sea.HasProperty("_FoamColor")) _sea.SetColor("_FoamColor", Color.Lerp(_daySea.Foam, NightSea.Foam, k));
            }
            RenderSettings.ambientSkyColor = Color.Lerp(_dayAmbientSky, NightAmbientSky, k);
            RenderSettings.ambientEquatorColor = Color.Lerp(_dayAmbientEquator, NightAmbientEquator, k);
            RenderSettings.ambientGroundColor = Color.Lerp(_dayAmbientGround, NightAmbientGround, k);
            bool lit = k > 0.3f;
            foreach (Light l in _lights)
                if (l != null && l.enabled != lit) l.enabled = lit;
        }
    }
}
