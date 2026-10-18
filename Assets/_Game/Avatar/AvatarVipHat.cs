using UnityEngine;
using Bone = PleaseDontDrown.Avatars.AvatarRig.Bone;

namespace PleaseDontDrown.Avatars
{
    /// <summary>
    /// A gold panama hat with a black band, put on a character at run time (the hotel's VIP guests, the director).
    /// Sized from the rig's measured head (any body, generated or code-built), so it sits on the hair instead of
    /// floating over it. Rebuilt with the body.
    /// </summary>
    public sealed class AvatarVipHat : MonoBehaviour
    {
        private static readonly Color Gold = new(0.93f, 0.7f, 0.24f), Band = new(0.06f, 0.05f, 0.05f);
        private static Mesh _shared;
        private AvatarRig _rig;
        private GameObject _hat;

        /// <summary>Puts the hat on <paramref name="rig"/> (or takes it off) and keeps it on through rebuilds.</summary>
        public static void Set(AvatarRig rig, bool on)
        {
            if (rig == null) return;
            var hat = rig.GetComponent<AvatarVipHat>();
            if (!on)
            {
                if (hat != null) Destroy(hat);
                return;
            }
            if (hat == null) hat = rig.gameObject.AddComponent<AvatarVipHat>();
            hat._rig = rig;
            hat.Wear();
        }

        private void OnEnable()
        {
            if (_rig == null) _rig = GetComponent<AvatarRig>();
            if (_rig != null) _rig.Rebuilt += Wear;
        }

        private void OnDisable()
        {
            if (_rig != null) _rig.Rebuilt -= Wear;
        }

        // Lying down (out cold, carried, on a bed) the brim would cut into the sand: the hat is off until they're up.
        private void LateUpdate()
        {
            if (_hat == null || _rig == null || !_rig.IsBuilt) return;
            bool upright = Vector3.Dot(_rig[Bone.Head].up, Vector3.up) > 0.55f;
            if (_hat.activeSelf != upright) _hat.SetActive(upright);
        }

        private void OnDestroy()
        {
            if (_hat != null) Destroy(_hat);
        }

        private void Wear()
        {
            if (_hat != null) Destroy(_hat);
            _hat = null;
            if (_rig == null || !_rig.IsBuilt || _rig[Bone.Head] == null || _rig.HeadTop <= 0f) return;
            _hat = new GameObject("VipHat");
            _hat.transform.SetParent(_rig[Bone.Head], false);
            // The mesh is made for a 1 m head and scaled to this one (one shared mesh for every VIP).
            float r = _rig.HeadHalfWidth * 1.02f;
            _hat.transform.localPosition = new Vector3(0f, _rig.HeadTop - r, -_rig.HeadBack + r);
            _hat.transform.localScale = Vector3.one * r;
            _hat.AddComponent<MeshFilter>().sharedMesh = SharedMesh();
            _hat.AddComponent<MeshRenderer>().sharedMaterial = AvatarRig.SharedMaterial;
        }

        /// <summary>The hat around a skull of radius 1 centred on the origin (y up, the face toward +z).</summary>
        private static Mesh SharedMesh()
        {
            if (_shared != null) return _shared;
            var kit = new AvatarMeshKit();
            kit.SetBone(0, Matrix4x4.identity);
            // The crown covers the skull down to a little above the brow; the brim sticks out all round.
            const float rim = 0.3f;
            float cover = Mathf.Acos(rim / 1.1f) * Mathf.Rad2Deg;
            kit.Dome(Vector3.zero, new Vector3(1.05f, 1.1f, 1.07f), cover, Gold, null, 22);
            const float band = rim + 0.08f;
            float around = 1.05f * Mathf.Sqrt(1f - band / 1.1f * (band / 1.1f)); // the crown's radius at the band
            kit.Torus(new Vector3(0f, band, 0f), around * 1.01f, 0.06f, Band, null, new Vector2(1f, 1.02f), 24);
            kit.Disc(new Vector3(0f, rim, 0f), 1.5f, 0.06f, Gold, null, 0f, new Vector2(1f, 1.04f), 28);
            _shared = kit.ToMesh("VipHat", new[] { Matrix4x4.identity });
            return _shared;
        }
    }
}
