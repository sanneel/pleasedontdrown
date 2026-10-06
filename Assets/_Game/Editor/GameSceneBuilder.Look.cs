using System;
using PleaseDontDrown.Look;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PleaseDontDrown.Editor
{
    /// <summary>
    /// The picture: sky, sun, haze, ambient light, water palette and the post-processing volumes (bloom, neutral
    /// tonemapping, vignette, an underwater grade). The numbers follow what we measured in How to Fish's scenes
    /// (Reference/visuals.json: exp2 blue fog, a strong soft-shadowed sun, HDR bloom that only catches very bright
    /// things, neutral tonemapping with a little extra exposure, a light vignette); the code and shaders are ours.
    /// </summary>
    public static partial class GameSceneBuilder
    {
        private const string LookDir = "Assets/_Game/Data/Look";
        private const string SkyShaderPath = "Assets/_Game/Data/Shaders/Sky.shader";

        // How to Fish: sun intensity 2 and post exposure +0.5. Our albedos are lighter, so a touch less of both.
        private const float SunIntensity = 1.5f;
        private const float PostExposure = 0.2f;

        private static void ApplyLook(Camera menuCam, Light sun)
        {
            System.IO.Directory.CreateDirectory(LookDir);

            // ---------------------------------------------------------------- sun
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.83f);   // 5000 K-ish, like How to Fish's sun
            sun.intensity = SunIntensity;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 1f;

            // ---------------------------------------------------------------- sky, haze, ambient
            Material sky = LoadOrCreateMaterial("Sky", AssetDatabase.LoadAssetAtPath<Shader>(SkyShaderPath));
            sky.SetColor("_SunColor", new Color(1f, 0.93f, 0.72f));
            sky.SetColor("_HorizonColor", new Color(0.545f, 0.78f, 1f));
            sky.SetColor("_TopColor", new Color(0.298f, 0.405f, 1f));
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            // Exp2 fog in a sky-blue that matches the sky's horizon; UnderwaterFx keeps the same mode underwater,
            // so builds only need the one fog shader variant.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.545f, 0.78f, 1f);
            RenderSettings.fogDensity = 0.004f;
            // Sky-lit ambient from explicit colours: a saved scene has no baked skybox probe.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.68f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.55f, 0.6f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.33f, 0.28f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            // ---------------------------------------------------------------- water palette
            Material ocean = LoadOrCreateMaterial("Ocean", AssetDatabase.LoadAssetAtPath<Shader>(OceanShaderPath));
            ocean.SetColor("_ShallowColor", new Color(0.04f, 0.55f, 0.71f));   // How to Fish's shallow blue, nudged toward turquoise
            ocean.SetColor("_DeepColor", new Color(0f, 0.15f, 0.3f));
            ocean.SetColor("_SkyColor", new Color(0.55f, 0.78f, 1f));
            ocean.SetColor("_RippleColor", new Color(0.58f, 0.87f, 1f));
            ocean.SetFloat("_PixelSize", 0.2f);
            ocean.SetFloat("_DepthBands", 5f);
            ocean.SetFloat("_Shininess", 90f);
            EditorUtility.SetDirty(ocean);

            // ---------------------------------------------------------------- post-processing
            VolumeProfile day = MakeProfile($"{LookDir}/DayVolume.asset", p =>
            {
                Comp<Tonemapping>(p).mode.Override(TonemappingMode.Neutral);
                var bloom = Comp<Bloom>(p);
                bloom.threshold.Override(2f);      // only the sun, HDR clouds and glints glow
                bloom.intensity.Override(1.6f);
                bloom.scatter.Override(0.7f);
                bloom.highQualityFiltering.Override(true);
                var vignette = Comp<Vignette>(p);
                vignette.color.Override(Color.black);
                vignette.intensity.Override(0.3f);
                vignette.smoothness.Override(1f);
                Comp<ColorAdjustments>(p).postExposure.Override(PostExposure);
            });
            VolumeProfile under = MakeProfile($"{LookDir}/UnderwaterVolume.asset", p =>
            {
                var grade = Comp<ColorAdjustments>(p);
                grade.colorFilter.Override(new Color(0.55f, 0.92f, 1f));
                grade.postExposure.Override(-0.2f);
                grade.contrast.Override(8f);
                grade.saturation.Override(10f);
                var vignette = Comp<Vignette>(p);
                vignette.color.Override(new Color(0f, 0.08f, 0.14f));
                vignette.intensity.Override(0.5f);
                vignette.smoothness.Override(0.8f);
                Comp<LensDistortion>(p).intensity.Override(0f); // wobbled at runtime by LookDirector
                Comp<ChromaticAberration>(p).intensity.Override(0.15f);
                var focus = Comp<DepthOfField>(p);
                focus.mode.Override(DepthOfFieldMode.Gaussian);
                focus.gaussianStart.Override(1.5f);
                focus.gaussianEnd.Override(16f);
                focus.gaussianMaxRadius.Override(1.2f);
            });

            var look = new GameObject("Look");
            var dayVolume = new GameObject("DayVolume").AddComponent<Volume>();
            dayVolume.transform.SetParent(look.transform, false);
            dayVolume.isGlobal = true;
            dayVolume.priority = 0f;
            dayVolume.sharedProfile = day;
            var underVolume = new GameObject("UnderwaterVolume").AddComponent<Volume>();
            underVolume.transform.SetParent(look.transform, false);
            underVolume.isGlobal = true;
            underVolume.priority = 10f;
            underVolume.weight = 0f;
            underVolume.sharedProfile = under;
            SetRef(look.AddComponent<LookDirector>(), "_underwater", underVolume);

            // ---------------------------------------------------------------- the menu camera sees the sky too
            menuCam.clearFlags = CameraClearFlags.Skybox;
            LookDirector.Prepare(menuCam);
        }

        private static VolumeProfile MakeProfile(string path, Action<VolumeProfile> fill)
        {
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            fill(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>Adds an override component to the profile and stores it inside the profile asset.</summary>
        private static T Comp<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(false);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }
    }
}
