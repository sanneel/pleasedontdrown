using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// Every machine: a stolen sun lounger or phone the Kleptopus flings at a lifeguard, flying an arc from its arm to
    /// where it will land (the host decides who it hit when it lands; this is the look of it). Thud or splash at the end.
    /// </summary>
    public class ThrownProp : MonoBehaviour
    {
        private static Material _white, _blue, _black;
        private Vector3 _from, _to;
        private float _flight, _started;
        private Vector3 _spin;

        public static void Launch(Vector3 from, Vector3 to, float flight, bool lounger)
        {
            var go = new GameObject(lounger ? "FlyingLounger" : "FlyingPhone");
            var prop = go.AddComponent<ThrownProp>();
            prop._from = from;
            prop._to = to;
            prop._flight = Mathf.Max(0.2f, flight);
            prop._started = Time.time;
            prop._spin = Random.onUnitSphere * (lounger ? 360f : 900f);
            if (lounger)
            {
                Piece(go.transform, new Vector3(0f, 0f, 0f), new Vector3(0.7f, 0.08f, 1.9f), Paint(ref _white, Color.white));
                for (int i = 0; i < 4; i++)
                    Piece(go.transform, new Vector3(0f, 0.05f, -0.75f + i * 0.5f), new Vector3(0.72f, 0.02f, 0.18f), Paint(ref _blue, new Color(0.15f, 0.45f, 0.85f)));
                Piece(go.transform, new Vector3(0f, 0.3f, -0.85f), new Vector3(0.7f, 0.6f, 0.08f), _white);
            }
            else
            {
                Piece(go.transform, Vector3.zero, new Vector3(0.18f, 0.03f, 0.36f), Paint(ref _black, new Color(0.06f, 0.06f, 0.08f)));
                Piece(go.transform, new Vector3(0f, 0.017f, 0f), new Vector3(0.15f, 0.005f, 0.3f), Paint(ref _blue, new Color(0.15f, 0.45f, 0.85f)));
            }
            go.transform.position = from;
        }

        private static Material Paint(ref Material cached, Color colour)
        {
            if (cached != null) return cached;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            cached = new Material(shader != null ? shader : Shader.Find("Standard")) { color = colour };
            if (cached.HasProperty("_BaseColor")) cached.SetColor("_BaseColor", colour);
            return cached;
        }

        private static void Piece(Transform parent, Vector3 local, Vector3 scale, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void Update()
        {
            float k = (Time.time - _started) / _flight;
            if (k >= 1f)
            {
                bool water = WaterSurface.Exists && Rescue.Shore.WaterDepthAt(_to + Vector3.up) > 0.3f;
                if (water) SplashFx.Spawn(new Vector3(_to.x, WaterSurface.HeightAt(_to), _to.z), 1.2f);
                else FloatingText.Spawn(_to + Vector3.up * 0.5f, "THUD!", new Color(1f, 0.6f, 0.3f), 1f, 0.8f);
                Destroy(gameObject);
                return;
            }
            // A high lob: you can see it coming and step out of the way.
            Vector3 p = Vector3.Lerp(_from, _to, k) + Vector3.up * (4f * k * (1f - k) * Mathf.Max(4f, Vector3.Distance(_from, _to) * 0.3f));
            transform.position = p;
            transform.Rotate(_spin * Time.deltaTime, Space.World);
        }
    }
}
