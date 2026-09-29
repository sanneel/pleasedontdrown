using System.Collections.Generic;
using System.Text;
using PleaseDontDrown.Items;
using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>
    /// Pictures of items for the hotbar (How to Fish's inventory icons): the item as it looks right now (its skin,
    /// the parts fitted to it) rendered once, from the side, tilted up to the right, on a clear background.
    /// Icons are cached by look, so changing a skin or fitting a scope makes a new one.
    /// </summary>
    public sealed class ItemIcons : MonoBehaviour
    {
        private const int Size = 128;
        private const int Layer = 31;           // nothing else is drawn on it
        private static ItemIcons _instance;

        private readonly Dictionary<string, Texture> _icons = new();
        private readonly Dictionary<Item, (string key, float at)> _keys = new();
        private readonly Queue<Item> _pending = new();
        private readonly HashSet<Item> _queued = new();
        private Camera _camera;
        private Transform _stage;

        /// <summary>The item's icon, or null while it's still being made (asks for it).</summary>
        public static Texture Get(Item item)
        {
            if (item == null) return null;
            if (_instance == null)
            {
                var go = new GameObject("ItemIcons") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<ItemIcons>();
            }
            return _instance.Lookup(item);
        }

        private Texture Lookup(Item item)
        {
            string key = KeyOf(item);
            if (_icons.TryGetValue(key, out Texture icon)) return icon;
            if (_queued.Add(item)) _pending.Enqueue(item);
            return null;
        }

        private string KeyOf(Item item)
        {
            if (_keys.TryGetValue(item, out var cached) && Time.unscaledTime - cached.at < 0.5f) return cached.key;
            var sb = new StringBuilder(item.DisplayName);
            if (item.TryGetComponent(out ItemSkin skin)) sb.Append('#').Append(skin.Skin);
            foreach (MeshRenderer r in item.GetComponentsInChildren<MeshRenderer>(false))
                if (Shown(r)) sb.Append('|').Append(r.name);
            string key = sb.ToString();
            _keys[item] = (key, Time.unscaledTime);
            return key;
        }

        private static bool Shown(Renderer r)
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) return false;
            string n = r.name.ToLowerInvariant();
            return !n.Contains("flash") && !n.Contains("beam") && !n.Contains("laserdot");
        }

        private void LateUpdate()
        {
            // One icon per frame at most (each is a small render).
            while (_pending.Count > 0)
            {
                Item item = _pending.Dequeue();
                _queued.Remove(item);
                if (item == null) continue;
                string key = KeyOf(item);
                if (_icons.ContainsKey(key)) continue;
                _icons[key] = Render(item);
                break;
            }
        }

        private Texture Render(Item item)
        {
            if (_camera == null)
            {
                _stage = new GameObject("IconStage").transform;
                _stage.SetParent(transform, false);
                _stage.position = new Vector3(0f, -2000f, 0f);
                _camera = new GameObject("IconCamera").AddComponent<Camera>();
                _camera.transform.SetParent(transform, false);
                _camera.enabled = false;
                _camera.orthographic = true;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _camera.cullingMask = 1 << Layer;
                _camera.nearClipPlane = 0.01f;
                _camera.farClipPlane = 20f;
                var light = new GameObject("IconLight").AddComponent<Light>();
                light.transform.SetParent(transform, false);
                light.type = LightType.Directional;
                light.cullingMask = 1 << Layer;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            }

            // A copy of what shows of the item: side on (the muzzle / blade to the right), tilted up.
            var copy = new GameObject("IconCopy").transform;
            copy.SetParent(_stage, false);
            Matrix4x4 toItem = item.transform.worldToLocalMatrix;
            Bounds bounds = default;
            bool any = false;
            foreach (MeshRenderer r in item.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (!Shown(r) || !r.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) continue;
                var part = new GameObject(r.name) { layer = Layer };
                part.transform.SetParent(copy, false);
                Matrix4x4 m = toItem * r.transform.localToWorldMatrix;
                part.transform.localPosition = m.GetColumn(3);
                part.transform.localRotation = m.rotation;
                part.transform.localScale = m.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = r.sharedMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                any = true;
            }
            copy.localRotation = Quaternion.Euler(0f, 0f, 32f) * Quaternion.Euler(0f, -90f, 0f);
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>())
            {
                if (bounds.size == Vector3.zero) bounds = r.bounds;
                else bounds.Encapsulate(r.bounds);
            }

            var target = new RenderTexture(Size, Size, 16, RenderTextureFormat.ARGB32) { name = "Icon_" + item.DisplayName, antiAliasing = 4 };
            if (any)
            {
                float extent = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.12f;
                _camera.orthographicSize = Mathf.Max(0.02f, extent);
                _camera.transform.SetPositionAndRotation(bounds.center - Vector3.forward * 5f, Quaternion.identity);
                _camera.targetTexture = target;
                _camera.Render();
                _camera.targetTexture = null;
            }
            Destroy(copy.gameObject);
            return target;
        }

        private void OnDestroy()
        {
            foreach (Texture t in _icons.Values)
                if (t != null) Destroy(t);
        }
    }
}
