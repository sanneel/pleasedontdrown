using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// Makes the scope on the gun in your hands something you can see through: a small camera looks down the scope
    /// and its picture is shown on the glass at the back, so from the hip the lens shows what the gun points at
    /// (magnified, with the crosshair) instead of a dark disc. Only for the local player's own gun, and not while
    /// the eye is at the scope (then the whole screen is the scope picture). Starts itself.
    /// </summary>
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
            if (show) _camera.fieldOfView = Mathf.Clamp(gun.Sight.AimFov * 0.7f, 6f, 40f);
        }

        /// <summary>Put the glass and the camera on this gun's scope (or take them off).</summary>
        private void Fit(Weapon gun, Transform scope)
        {
            _gun = gun;
            _scope = scope;
            if (scope == null)
            {
                if (_glass != null) _glass.SetActive(false);
                if (_camera != null)
                {
                    _camera.enabled = false;
                    _camera.transform.SetParent(transform, false);
                }
                if (_glass != null) _glass.transform.SetParent(transform, false);
                return;
            }
            if (_glass == null && !Build()) return;

            // The scope's own lenses say where its ends are and how wide the glass is (the eye point is at the back).
            Transform rear = scope.Find("RearLens"), front = scope.Find("FrontLens");
            Vector3 back = scope.InverseTransformPoint(gun.Sight.EyePoint.position);
            float radius = rear != null ? rear.localScale.x * 0.5f : 0.024f;
            float length = front != null ? front.localPosition.z - back.z : 0.24f;
            // A gun whose model has its own scope (the sniper's: the fitted one is hidden): the glass goes on that
            // eyepiece, which ends 4 cm behind the eye point (Editor/WeaponArtPolish's sniper optic).
            bool ownScope = rear != null && rear.TryGetComponent(out Renderer rearGlass) && !rearGlass.enabled;
            _glass.transform.SetParent(scope, false);
            _glass.transform.localPosition = ownScope ? new Vector3(back.x, back.y + 0.002f, back.z - 0.045f) : new Vector3(back.x, back.y, back.z - 0.004f);
            if (ownScope) radius = 0.0235f;
            _glass.transform.localRotation = Quaternion.identity;
            _glass.transform.localScale = Vector3.one * radius;
            _glass.layer = gun.gameObject.layer;
            _camera.transform.SetParent(scope, false);
            // Out past the muzzle, on the scope's line: the gun's own front sight and barrel aren't in the picture.
            _camera.transform.localPosition = new Vector3(back.x, back.y, back.z + Mathf.Max(length + 0.03f, 0.95f));
            _camera.transform.localRotation = Quaternion.identity;
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
