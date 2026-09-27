using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// Watches a point on an object and splashes when it drops through the water surface fast enough.
    /// Works on every machine from the visible motion, so remote players and followed items splash too.
    /// </summary>
    public class SurfaceCrossing : MonoBehaviour
    {
        [SerializeField] private Vector3 _localPoint;
        [SerializeField] private float _minDownSpeed = 2.5f;
        [SerializeField] private float _fullStrengthSpeed = 10f;

        private Vector3 _previous;
        private float _previousDepth;
        private bool _hasPrevious;
        private float _cooldownUntil;

        private void OnEnable() => _hasPrevious = false;

        private void LateUpdate()
        {
            if (!WaterSurface.Exists || Time.deltaTime <= 0f) return;
            Vector3 p = transform.TransformPoint(_localPoint);
            float depth = WaterSurface.DepthOf(p);

            if (_hasPrevious && _previousDepth <= 0f && depth > 0f && Time.time > _cooldownUntil)
            {
                float downSpeed = (_previous.y - p.y) / Time.deltaTime;
                // Ignore teleports (huge jumps) and gentle wading.
                if (downSpeed > _minDownSpeed && (p - _previous).sqrMagnitude < 25f)
                {
                    SplashFx.Spawn(new Vector3(p.x, WaterSurface.HeightAt(p), p.z), downSpeed / _fullStrengthSpeed);
                    _cooldownUntil = Time.time + 0.4f;
                }
            }

            _previous = p;
            _previousDepth = depth;
            _hasPrevious = true;
        }
    }
}
