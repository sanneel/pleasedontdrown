using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// Makes the scope on the gun in your hands something you can see through: a small camera looks from your eye
    /// through the glass at the back of the scope and its picture is shown on that glass, so the lens shows what is
    /// really behind it, magnified (like clear glass with a lens in it), instead of a dark disc. Only for the local player's own gun, and not while
    /// the eye is at the scope (then the whole screen is the scope picture). Starts itself.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after the gun and the view have been placed for this frame
    public sealed class ScopeLens : MonoBehaviour
    {
        private const int Size = 384;

        private Camera _camera;
        private RenderTexture _view;
        private Material _material;
        private Mesh _disc;
        private GameObject _glass;
        private Weapon _gun;
        private Transform _scope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            if (Application.isBatchMode) return; // nothing is drawn in a headless run
            var go = new GameObject("ScopeLens");
            DontDestroyOnLoad(go);
            go.AddComponent<ScopeLens>();
        }

        private void LateUpdate()
        {
            Weapon gun = Weapon.Local;
            Transform scope = gun != null && gun.Sight.Scope && gun.Sight.EyePoint != null ? gun.Sight.EyePoint.parent : null;
            if (scope != _scope || gun != _gun) Fit(gun, scope);
            if (_glass == null) return;
            // Eye at the scope: the gun isn't drawn at all and the screen shows the scope picture.
            bool show = _scope != null && _scope.gameObject.activeInHierarchy && !gun.IsScopedIn;
            if (_glass.activeSelf != show) _glass.SetActive(show);
            if (_camera.enabled != show) _camera.enabled = show;
            Camera eye = Camera.main;
            if (!show || eye == null) return;

            // Look from the player's own eye through the middle of the glass: what shows in the lens is what is
            // really behind it (as through clear glass), only magnified. The near plane starts past the muzzle, so
            // the gun and the hands aren't in the picture.
            Vector3 centre = _glass.transform.position, from = eye.transform.position;
            Vector3 through = centre - from;
            float distance = through.magnitude;
            if (distance < 0.05f) return;
            float across = 2f * Mathf.Atan(_glass.transform.lossyScale.x * 1.04f / distance) * Mathf.Rad2Deg; // how big the glass looks
            float zoom = Mathf.Clamp(eye.fieldOfView / Mathf.Max(1f, gun.Sight.AimFov) * 0.6f, 1.4f, 3f);
            _camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(through, eye.transform.up));
            _camera.fieldOfView = Mathf.Clamp(across / zoom, 0.2f, 60f);
            _camera.nearClipPlane = distance + 1.1f;
            // Where each point of the glass lies in that view (unmagnified): the shader looks the picture up there.
            Matrix4x4 lens = Matrix4x4.Perspective(Mathf.Clamp(across, 0.5f, 120f), 1f, 0.05f, 100f) * _camera.worldToCameraMatrix;
            _material.SetMatrix(LensView, lens);
        }

        private static readonly int LensView = Shader.PropertyToID("_LensView");

        /// <summary>Put the glass and the camera on this gun's scope (or take them off).</summary>
        private void Fit(Weapon gun, Transform scope)
        {
            _gun = gun;
            _scope = scope;
            if (scope == null)
            {
                if (_glass != null) _glass.SetActive(false);
                if (_camera != null) _camera.enabled = false;
                if (_glass != null) _glass.transform.SetParent(transform, false);
                return;
            }
            if (_glass == null && !Build()) return;

            // The scope's own lenses say where its ends are and how wide the glass is (the eye point is at the back).
            Transform rear = scope.Find("RearLens");
            Vector3 back = scope.InverseTransformPoint(gun.Sight.EyePoint.position);
            float radius = rear != null ? rear.localScale.x * 0.5f : 0.024f;
            // A gun whose model has its own scope (the sniper's: the fitted one is hidden): the glass goes on that
            // eyepiece, which ends 4 cm behind the eye point (Editor/WeaponArtPolish's sniper optic).
            bool ownScope = rear != null && rear.TryGetComponent(out Renderer rearGlass) && !rearGlass.enabled;
            _glass.transform.SetParent(scope, false);
            _glass.transform.localPosition = ownScope ? new Vector3(back.x, back.y + 0.002f, back.z - 0.045f) : new Vector3(back.x, back.y, back.z - 0.004f);
            if (ownScope) radius = 0.0235f;
            _glass.transform.localRotation = Quaternion.identity;
            _glass.transform.localScale = Vector3.one * radius;
            _glass.layer = gun.gameObject.layer;
        }

        private bool Build()
        {
            var shader = Resources.Load<Shader>("ScopeLens");
            if (shader == null)
            {
                Debug.LogWarning("[Scope] Resources/ScopeLens.shader is missing: scopes keep their dark glass");
                enabled = false;
                return false;
            }
            Release(); // the last gun took the glass with it when it was destroyed: start clean
            _view = new RenderTexture(Size, Size, 16) { name = "ScopeView", antiAliasing = 2 };
            _material = new Material(shader) { name = "ScopeLensView", mainTexture = _view };

            var camGo = new GameObject("ScopeCamera");
            camGo.transform.SetParent(transform, false);
            _camera = camGo.AddComponent<Camera>();
            _camera.targetTexture = _view;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 1500f;
            _camera.depth = -10f; // before the player's own view each frame
            _camera.enabled = false;
            UniversalAdditionalCameraData data = _camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            // A round pane of unit radius facing back toward the eye (-z), the picture the right way up on it.
            const int n = 28;
            var vertices = new Vector3[n + 1];
            var uv = new Vector2[n + 1];
            var triangles = new int[n * 3];
            vertices[0] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                vertices[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                uv[i + 1] = new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + (i + 1) % n;
                triangles[i * 3 + 2] = 1 + i;
            }
            _disc = new Mesh { name = "ScopeGlass", vertices = vertices, uv = uv, triangles = triangles };
            _disc.RecalculateBounds();
            _glass = new GameObject("ScopeGlassView");
            _glass.AddComponent<MeshFilter>().sharedMesh = _disc;
            var renderer = _glass.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return true;
        }

        private void OnDestroy() => Release();

        private void Release()
        {
            if (_camera != null) Destroy(_camera.gameObject);
            if (_view != null)
            {
                _view.Release();
                Destroy(_view);
            }
            if (_material != null) Destroy(_material);
            if (_disc != null) Destroy(_disc);
            if (_glass != null) Destroy(_glass);
        }
    }
}
