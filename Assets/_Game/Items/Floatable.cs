using System.Collections.Generic;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// Something a person in the water can hang onto (life ring, raft, rescue board). A struggling tourist near a
    /// floating one grabs it, keeps their head up and calms down.
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Floatable : MonoBehaviour
    {
        private static readonly List<Floatable> _all = new();

        private Item _item;

        public Item Item => _item;

        /// <summary>Floating on the water and not in anyone's hands.</summary>
        public bool IsAvailable => !_item.IsHeld && WaterSurface.Exists && Mathf.Abs(WaterSurface.DepthOf(transform.position)) < 0.6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all.Clear();

        private void Awake() => _item = GetComponent<Item>();
        private void OnEnable() => _all.Add(this);
        private void OnDisable() => _all.Remove(this);

        /// <summary>The closest available float within <paramref name="radius"/> (horizontal distance), or null.</summary>
        public static Floatable FindNear(Vector3 position, float radius)
        {
            Floatable best = null;
            float bestSq = radius * radius;
            foreach (Floatable f in _all)
            {
                Vector3 d = f.transform.position - position;
                d.y = 0f;
                if (d.sqrMagnitude < bestSq && f.IsAvailable)
                {
                    bestSq = d.sqrMagnitude;
                    best = f;
                }
            }
            return best;
        }
    }
}
