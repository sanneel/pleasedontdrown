using PleaseDontDrown.World.Water;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Look
{
    /// <summary>
    /// The game's picture: turns post-processing on for whichever camera is looking, and blends in the "underwater"
    /// volume (teal grade, dark vignette, a wobbling lens, soft focus) while the camera is under the surface.
    /// The day volume, the sky, the sun and the fog are baked into the scene by GameSceneBuilder.Look.
    /// </summary>
    public class LookDirector : MonoBehaviour
    {
        [SerializeField] private Volume _underwater;
        [SerializeField] private float _blendSpeed = 5f;
        [SerializeField] private float _wobble = 0.11f;
        [SerializeField] private float _wobbleSpeed = 1.7f;

        private LensDistortion _lens;
        private float _weight;
        private Camera _prepared;

        /// <summary>Switches on post-processing (bloom, tonemapping, vignette...) and edge smoothing on a camera.</summary>
        public static void Prepare(Camera camera)
        {
            if (camera == null) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
        }

        private void Start()
        {
            if (_underwater != null)
            {
                _underwater.profile.TryGet(out _lens); // its intensity is animated in LateUpdate
                _underwater.weight = 0f;
            }
            DynamicGI.UpdateEnvironment(); // sky-lit ambient and reflections from the sky material
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null && cam != _prepared)
            {
                _prepared = cam;
                Prepare(cam);
            }
            if (_underwater == null) return;

            float target = UnderwaterFx.CameraUnderwater ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, target, _blendSpeed * Time.unscaledDeltaTime);
            _underwater.weight = _weight;
            if (_lens != null && _weight > 0f)
                _lens.intensity.value = Mathf.Sin(Time.time * _wobbleSpeed) * _wobble;
        }
    }
}
