using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Rescue
{
    /// <summary>How deep the water is under a point: the gap between the wave surface and the static ground below.</summary>
    public static class Shore
    {
        /// <summary>Water shallower than this counts as "ashore": an adult stands up here.</summary>
        public const float StandDepth = 0.9f;
        /// <summary>Deeper than this, a non-swimmer is in trouble. The gap to <see cref="StandDepth"/> stops waves flipping them.</summary>
        public const float DeepDepth = 1.4f;

        private static readonly RaycastHit[] _hits = new RaycastHit[16];

        /// <summary>Water depth at (x, z) in metres. Negative on dry land, large in open water with no ground found.</summary>
        public static float WaterDepthAt(Vector3 point)
        {
            if (!WaterSurface.Exists) return -10f;
            float surface = WaterSurface.HeightAt(point);
            // Start just above the point, so a dock over someone's head isn't mistaken for the ground under them.
            float top = point.y + 0.5f;
            int count = Physics.RaycastNonAlloc(new Vector3(point.x, top, point.z), Vector3.down, _hits, 60f, ~0, QueryTriggerInteraction.Ignore);
            float ground = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                // Only the static world counts as ground (not items, players or other tourists).
                if (_hits[i].collider.attachedRigidbody != null) continue;
                ground = Mathf.Max(ground, _hits[i].point.y);
            }
            return float.IsNegativeInfinity(ground) ? 50f : surface - ground;
        }

        public static bool IsAshore(Vector3 point) => WaterDepthAt(point) < StandDepth;

        /// <summary>Height of the static ground under a point (not the waves), or NaN if there's none.</summary>
        public static float GroundHeightAt(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.5f, Vector3.down, _hits, 60f, ~0, QueryTriggerInteraction.Ignore);
            float ground = float.NaN;
            for (int i = 0; i < count; i++)
                if (_hits[i].collider.attachedRigidbody == null && !(ground >= _hits[i].point.y))
                    ground = _hits[i].point.y;
            return ground;
        }
    }
}
