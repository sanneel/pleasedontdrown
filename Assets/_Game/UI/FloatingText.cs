using UnityEngine;

namespace PleaseDontDrown.UI
{
    /// <summary>World-space text that pops up, drifts upward, faces the camera and fades out ("DING!", "+$350").</summary>
    public class FloatingText : MonoBehaviour
    {
        private TextMesh _text;
        private Color _color;
        private float _age;
        private float _lifetime;
        private float _rise;

        public static void Spawn(Vector3 position, string text, Color color, float scale = 1f, float lifetime = 1.4f)
        {
            var go = new GameObject("FloatingText");
            go.transform.position = position;
            var mesh = go.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            mesh.text = text;
            mesh.fontSize = 96;
            mesh.characterSize = 0.035f * scale;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.fontStyle = FontStyle.Bold;
            mesh.color = color;

            var ft = go.AddComponent<FloatingText>();
            ft._text = mesh;
            ft._color = color;
            ft._lifetime = lifetime;
            ft._rise = 0.9f;
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            float t = _age / _lifetime;
            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            transform.position += Vector3.up * (_rise * (1f - t) * Time.deltaTime);
            // Pop in, then shrink a little and fade over the last half.
            float pop = t < 0.12f ? Mathf.SmoothStep(0.4f, 1.15f, t / 0.12f) : Mathf.Lerp(1.15f, 0.9f, (t - 0.12f) / 0.88f);
            transform.localScale = Vector3.one * pop;
            _text.color = new Color(_color.r, _color.g, _color.b, _color.a * Mathf.Clamp01((1f - t) * 2f));

            Camera cam = Camera.main;
            if (cam != null)
                transform.rotation = cam.transform.rotation;
        }
    }
}
