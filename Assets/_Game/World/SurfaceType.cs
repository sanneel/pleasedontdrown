using UnityEngine;

namespace PleaseDontDrown.World
{
    public enum SurfaceKind { Sand, Wood, Rock }

    /// <summary>Tags a collider's material for footsteps and impacts. Untagged ground counts as sand.</summary>
    public class SurfaceType : MonoBehaviour
    {
        [SerializeField] private SurfaceKind _kind = SurfaceKind.Sand;

        public SurfaceKind Kind => _kind;

        public static SurfaceKind Of(Collider collider)
        {
            if (collider == null) return SurfaceKind.Sand;
            SurfaceType tag = collider.GetComponentInParent<SurfaceType>();
            return tag != null ? tag.Kind : SurfaceKind.Sand;
        }
    }
}
