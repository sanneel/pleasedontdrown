using System.Collections.Generic;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>The parrots' perches (palm crowns, the beach hut's roof) and where they circle when they fly.</summary>
    public class ParrotFlock : MonoBehaviour
    {
        [SerializeField] private Transform[] _perches;
        [SerializeField] private Vector3 _skyCentre;

        private readonly HashSet<Transform> _taken = new();

        public Vector3 SkyCentre => _skyCentre;

        /// <summary>A free perch (not <paramref name="except"/>), a nearby one more likely; null when all are taken.</summary>
        public Transform TakePerch(Transform except, Vector3 from)
        {
            Transform best = null;
            float bestScore = float.MaxValue;
            foreach (Transform perch in _perches)
            {
                if (perch == null || perch == except || _taken.Contains(perch)) continue;
                float score = Vector3.Distance(perch.position, from) + Random.Range(0f, 25f);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = perch;
                }
            }
            if (best != null) _taken.Add(best);
            return best;
        }

        /// <summary>The free perch right here (where the scene put the bird), else any free one.</summary>
        public Transform TakePerchAt(Vector3 at)
        {
            foreach (Transform perch in _perches)
                if (perch != null && !_taken.Contains(perch) && (perch.position - at).sqrMagnitude < 0.04f)
                {
                    _taken.Add(perch);
                    return perch;
                }
            return TakePerch(null, at);
        }

        public void Release(Transform perch)
        {
            if (perch != null) _taken.Remove(perch);
        }
    }
}
