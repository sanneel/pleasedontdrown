using PleaseDontDrown.Avatars;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>Little yellow stars circling a knocked-out head for a few seconds, then gone.</summary>
    public class DizzyStars : MonoBehaviour
    {
        private const int Count = 5;
        private static Mesh _star;

        private Transform _head;
        private float _until;
        private readonly Transform[] _stars = new Transform[Count];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _star = null;

        public static void Spawn(Transform head, float seconds)
        {
            if (head == null || AvatarRig.SharedMaterial == null) return;
            var go = new GameObject("DizzyStars");
            var stars = go.AddComponent<DizzyStars>();
            stars._head = head;
            stars._until = Time.time + seconds;
            if (_star == null)
            {
                var kit = new AvatarMeshKit();
                kit.SetBone(0, Matrix4x4.identity);
                // A chunky four-pointed star: two crossed spindles and a bright middle.
                kit.Ellipsoid(Vector3.zero, new Vector3(0.07f, 0.022f, 0.022f), new Color(1f, 0.88f, 0.2f), null, 6, 4);
                kit.Ellipsoid(Vector3.zero, new Vector3(0.022f, 0.07f, 0.022f), new Color(1f, 0.88f, 0.2f), null, 6, 4);
                kit.Ellipsoid(Vector3.zero, Vector3.one * 0.03f, new Color(1f, 0.97f, 0.6f), null, 8, 6);
                _star = kit.ToMesh("DizzyStar", new[] { Matrix4x4.identity }, flat: true);
            }
            for (int i = 0; i < Count; i++)
            {
                var s = new GameObject("Star").transform;
                s.SetParent(go.transform, false);
                s.gameObject.AddComponent<MeshFilter>().sharedMesh = _star;
                s.gameObject.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
                stars._stars[i] = s;
            }
            stars.LateUpdate();
        }

        private void LateUpdate()
        {
            if (_head == null || Time.time > _until)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 centre = _head.position + Vector3.up * 0.45f * _head.lossyScale.y;
            float r = 0.32f * _head.lossyScale.x;
            for (int i = 0; i < Count; i++)
            {
                float a = Time.time * 4f + i * Mathf.PI * 2f / Count;
                _stars[i].position = centre + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a * 2f + i) * 0.04f, Mathf.Sin(a) * r);
                _stars[i].rotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg * 2f, 20f);
            }
        }
    }
}
