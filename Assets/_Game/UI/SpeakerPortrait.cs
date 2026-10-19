using PleaseDontDrown.Avatars;
using PleaseDontDrown.Story;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// A live picture of whoever is talking, for the dialogue's name plate (StoryHud): a small camera in front of their
    /// face, on only while their line is up and at half the frame rate. Speech bubbles are hidden from it (they float
    /// over the head, in the top of its picture).
    /// </summary>
    public sealed class SpeakerPortrait : MonoBehaviour
    {
        private const int Size = 256;
        private const float Distance = 0.85f, Fov = 26f;
        /// <summary>The camera stands this many head-heights out in front of the face (a person's head: 0.85 m from it).</summary>
        private const float HeadsAway = 1.5f;
        private static SpeakerPortrait _instance;

        private Camera _camera;
        private RenderTexture _texture;
        private StoryNpc _target;
        private int _askedFrame = -10;
        private bool _ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>
        /// The picture of <paramref name="npc"/>'s face (null until it has one). Ask every frame it's on the screen; the
        /// camera stops a moment after nobody asks.
        /// </summary>
        public static Texture Of(StoryNpc npc)
        {
            if (npc == null || Application.isBatchMode) return null;
            if (_instance == null)
            {
                var go = new GameObject("SpeakerPortrait");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<SpeakerPortrait>();
            }
            if (_instance._target != npc) _instance._ready = false; // (the last speaker's face isn't theirs)
            _instance._target = npc;
            _instance._askedFrame = Time.frameCount;
            return _instance._ready ? _instance._texture : null;
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
        }

        private void BeforeCamera(ScriptableRenderContext _, Camera camera)
        {
            if (camera == _camera) FloatingText.HideBubbles(true);
        }

        private void AfterCamera(ScriptableRenderContext _, Camera camera)
        {
            if (camera == _camera) FloatingText.HideBubbles(false);
        }

        private void LateUpdate()
        {
            bool wanted = _target != null && _target.isActiveAndEnabled && Time.frameCount - _askedFrame <= 2;
            if (!wanted)
            {
                if (_camera != null) _camera.enabled = false;
                _ready = false;
                return;
            }
            if (_camera == null) Build();
            // Half the frame rate is plenty for a face this size.
            bool render = (Time.frameCount & 1) == 0 || !_ready;
            _camera.enabled = render;
            if (!render) return;

            // In front of their face, looking at it a touch from above: the face fills the picture, shoulders under it.
            // (Out by the head's own size: at a person's distance the goofy lifeguard's big head showed only his nose.)
            AvatarRig rig = _target.Rig;
            bool built = rig != null && rig.IsBuilt && rig.HeadTop > 0f;
            Vector3 head = built ? rig[AvatarRig.Bone.Head].position + Vector3.up * (rig.HeadTop * 0.4f) : _target.HeadPosition;
            float distance = built ? Mathf.Max(Distance * 0.8f, rig.HeadFront + rig.HeadTop * HeadsAway) : Distance;
            Vector3 facing = _target.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.001f) facing = Vector3.forward;
            facing.Normalize();
            Vector3 eye = head + facing * distance + Vector3.up * 0.06f;
            _camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(head - Vector3.up * 0.05f - eye));
            _ready = true;
        }

        private void Build()
        {
            _texture = new RenderTexture(Size, Size, 16) { name = "SpeakerPortrait", antiAliasing = 2 };
            var camGo = new GameObject("PortraitCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.targetTexture = _texture;
            _camera.fieldOfView = Fov;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 120f;
            _camera.depth = -10f; // before the player's own view each frame
            _camera.cullingMask &= ~(1 << 5); // (not the UI layer)
            _camera.enabled = false;
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
        }

        private void OnDestroy()
        {
            if (_texture != null)
            {
                _texture.Release();
                Destroy(_texture);
            }
            if (_instance == this) _instance = null;
        }
    }
}
