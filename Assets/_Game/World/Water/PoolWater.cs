using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.World.Water
{
    /// <summary>
    /// A pool's still water above the sea (island 2's hotel pool): inside its box <see cref="WaterSurface.HeightAt"/>
    /// answers the pool's level instead of the sea's, so swimming, diving, floating, the underwater look, drowning and
    /// rescues all work in it as in the sea. The box is the object's position (the water surface's middle) and
    /// <see cref="_size"/> (x, z); the visible surface is drawn by its own renderer.
    /// </summary>
    public class PoolWater : MonoBehaviour
    {
        [Tooltip("The water's width (x) and length (z), metres.")]
        [SerializeField] private Vector2 _size = new(16f, 10f);

        private static readonly List<PoolWater> Active = new();
        private float _minX, _maxX, _minZ, _maxZ, _level;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active.Clear();

        private void OnEnable()
        {
            Vector3 p = transform.position;
            _minX = p.x - _size.x * 0.5f; _maxX = p.x + _size.x * 0.5f;
            _minZ = p.z - _size.y * 0.5f; _maxZ = p.z + _size.y * 0.5f;
            _level = p.y;
            if (!Active.Contains(this)) Active.Add(this);
        }

        private void OnDisable() => Active.Remove(this);

        /// <summary>The level of a pool covering (x, z), if any.</summary>
        public static bool TryLevel(float x, float z, out float level)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                PoolWater w = Active[i];
                if (x >= w._minX && x <= w._maxX && z >= w._minZ && z <= w._maxZ)
                {
                    level = w._level;
                    return true;
                }
            }
            level = 0f;
            return false;
        }
    }
}
