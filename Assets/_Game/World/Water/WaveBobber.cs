using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>Decoration that rides the waves in place (swim-zone buoys, floating junk). No physics, no network.</summary>
    public class WaveBobber : MonoBehaviour
    {
        [SerializeField] private float _heightOffset;
        [SerializeField, Range(0f, 1f)] private float _tilt = 0.7f;

        private Vector3 _anchor;
        private Quaternion _baseRotation;

        private void Start()
        {
            _anchor = transform.position;
            _baseRotation = transform.rotation;
        }

        private void LateUpdate()
        {
            if (!WaterSurface.Exists) return;
            Vector3 normal = Vector3.Lerp(Vector3.up, WaterSurface.NormalAt(_anchor), _tilt);
            transform.SetPositionAndRotation(
                new Vector3(_anchor.x, WaterSurface.HeightAt(_anchor) + _heightOffset, _anchor.z),
                Quaternion.FromToRotation(Vector3.up, normal) * _baseRotation);
        }
    }
}
