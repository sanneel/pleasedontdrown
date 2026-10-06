using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Core
{
    /// <summary>
    /// The player's picture settings (options menu): quality (shadows and ambient occlusion), edge smoothing, vsync, a
    /// frame cap and the render scale. Kept in PlayerPrefs. High is the picture the game is made for: it keeps the
    /// pipeline asset's own shadows and ambient occlusion. The changes go on a copy of the pipeline asset, so playing
    /// in the editor never edits the project's settings.
    /// </summary>
    public static class PictureSettings
    {
        public enum Level { Low, Medium, High }
        public enum Smoothing { Off, Fxaa, Smaa }

        private const string QualityKey = "pdd.gfx.quality";
        private const string SmoothingKey = "pdd.gfx.aa";
        private const string VSyncKey = "pdd.gfx.vsync";
        private const string CapKey = "pdd.gfx.cap";
        private const string ScaleKey = "pdd.gfx.scale";

        public const Level DefaultQuality = Level.High;
        public const Smoothing DefaultSmoothing = Smoothing.Smaa;
        public const bool DefaultVSync = true;
        public const float DefaultRenderScale = 1f, MinRenderScale = 0.5f, MaxRenderScale = 1f;

        /// <summary>Frames per second the cap can be set to when vsync is off; 0 is no cap.</summary>
        public static readonly int[] FrameCaps = { 30, 60, 120, 144, 165, 240, 0 };
        public const int DefaultFrameCap = 6;

        // Low and Medium; High keeps the pipeline asset's own values.
        private static readonly float[] ShadowDistance = { 60f, 100f };
        private static readonly int[] ShadowResolution = { 1024, 2048 };

        private static bool _loaded;
        private static Level _quality;
        private static Smoothing _smoothing;
        private static bool _vSync;
        private static int _frameCap;
        private static float _renderScale;

        private static RenderPipelineAsset _qualityPipeline;
        private static UniversalRenderPipelineAsset _source, _copy;
        private static readonly List<ScriptableRendererFeature> _occlusion = new();
        private static readonly List<bool> _occlusionWasActive = new();

        /// <summary>Goes up on every change, so cameras know to pick up the new edge smoothing.</summary>
        public static int Version { get; private set; }

        public static Level Quality
        {
            get { Load(); return _quality; }
            set { Load(); _quality = value; Save(QualityKey, (int)value); ApplyPipeline(); }
        }

        public static Smoothing AntiAliasing
        {
            get { Load(); return _smoothing; }
            set { Load(); _smoothing = value; Save(SmoothingKey, (int)value); }
        }

        public static bool VSync
        {
            get { Load(); return _vSync; }
            set { Load(); _vSync = value; Save(VSyncKey, value ? 1 : 0); ApplyFrameRate(); }
        }

        /// <summary>Index into <see cref="FrameCaps"/>.</summary>
        public static int FrameCap
        {
            get { Load(); return _frameCap; }
            set { Load(); _frameCap = Mathf.Clamp(value, 0, FrameCaps.Length - 1); Save(CapKey, _frameCap); ApplyFrameRate(); }
        }

        /// <summary>The 3D picture's resolution against the screen's (the HUD stays sharp).</summary>
        public static float RenderScale
        {
            get { Load(); return _renderScale; }
            set
            {
                Load();
                _renderScale = Mathf.Clamp(value, MinRenderScale, MaxRenderScale);
                PlayerPrefs.SetFloat(ScaleKey, _renderScale);
                ApplyPipeline();
            }
        }

        public static void ResetAll()
        {
            Quality = DefaultQuality;
            AntiAliasing = DefaultSmoothing;
            VSync = DefaultVSync;
            FrameCap = DefaultFrameCap;
            RenderScale = DefaultRenderScale;
            PlayerPrefs.Save();
        }

        /// <summary>Edge smoothing on a camera, as chosen.</summary>
        public static void ApplyTo(UniversalAdditionalCameraData camera)
        {
            camera.antialiasing = AntiAliasing switch
            {
                Smoothing.Off => AntialiasingMode.None,
                Smoothing.Fxaa => AntialiasingMode.FastApproximateAntialiasing,
                _ => AntialiasingMode.SubpixelMorphologicalAntiAliasing,
            };
            camera.antialiasingQuality = AntialiasingQuality.High;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _loaded = false;
            _qualityPipeline = null;
            _source = _copy = null;
            _occlusion.Clear();
            _occlusionWasActive.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyAtStart()
        {
            ApplyFrameRate();
            ApplyPipeline();
#if UNITY_EDITOR
            Application.quitting -= Restore;
            Application.quitting += Restore;
#endif
        }

        private static void Save(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            Version++;
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            _quality = (Level)Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, (int)DefaultQuality), 0, (int)Level.High);
            _smoothing = (Smoothing)Mathf.Clamp(PlayerPrefs.GetInt(SmoothingKey, (int)DefaultSmoothing), 0, (int)Smoothing.Smaa);
            _vSync = PlayerPrefs.GetInt(VSyncKey, DefaultVSync ? 1 : 0) == 1;
            _frameCap = Mathf.Clamp(PlayerPrefs.GetInt(CapKey, DefaultFrameCap), 0, FrameCaps.Length - 1);
            _renderScale = Mathf.Clamp(PlayerPrefs.GetFloat(ScaleKey, DefaultRenderScale), MinRenderScale, MaxRenderScale);
        }

        private static void ApplyFrameRate()
        {
            Load();
            QualitySettings.vSyncCount = _vSync ? 1 : 0;
            int cap = FrameCaps[_frameCap];
            Application.targetFrameRate = _vSync || cap <= 0 ? -1 : cap;
        }

        private static void ApplyPipeline()
        {
            Load();
            if (_copy == null)
            {
                _source = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (_source == null) return;
                _qualityPipeline = QualitySettings.renderPipeline;
                _copy = Object.Instantiate(_source);
                _copy.name = _source.name;
                QualitySettings.renderPipeline = _copy;

                foreach (ScriptableRendererData data in _source.rendererDataList)
                {
                    if (data == null) continue;
                    foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                        if (feature is ScreenSpaceAmbientOcclusion)
                        {
                            _occlusion.Add(feature);
                            _occlusionWasActive.Add(feature.isActive);
                        }
                }
            }

            bool high = _quality == Level.High;
            int i = (int)_quality;
            _copy.shadowDistance = high ? _source.shadowDistance : ShadowDistance[i];
            _copy.shadowCascadeCount = high ? _source.shadowCascadeCount : 2;
            _copy.mainLightShadowmapResolution = high ? _source.mainLightShadowmapResolution : ShadowResolution[i];
            _copy.renderScale = _renderScale;
            for (int f = 0; f < _occlusion.Count; f++)
                _occlusion[f].SetActive(high && _occlusionWasActive[f]);
        }

#if UNITY_EDITOR
        /// <summary>Leaving play mode: the project's own pipeline asset and renderer features go back as they were.</summary>
        private static void Restore()
        {
            for (int f = 0; f < _occlusion.Count; f++)
                if (_occlusion[f] != null) _occlusion[f].SetActive(_occlusionWasActive[f]);
            if (_copy != null)
            {
                QualitySettings.renderPipeline = _qualityPipeline;
                Object.Destroy(_copy);
            }
            ResetStatics();
        }
#endif
    }
}
