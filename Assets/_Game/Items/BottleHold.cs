using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// A bottle sits in the hand like one: low and to the right of the view, upright, not floated out in front of
    /// the eyes the way plain things are (the hand covered the beer). Drinking from it tips it up to the lips
    /// (<see cref="PlayerHands"/>).
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class BottleHold : MonoBehaviour, IHoldPose
    {
        [SerializeField] private Vector3 _offset = new(0.2f, -0.24f, 0.42f);
        [Tooltip("Someone else's bottle, seen from outside: down in the hand by the belly, from the eyes (before " +
                 "PlayerHands shrinks remote holds toward the body).")]
        [SerializeField] private Vector3 _remoteOffset = new(0.26f, -0.74f, 0.5f);
        [SerializeField] private Vector3 _euler = new(-6f, 0f, -4f);

        public void GetHoldPose(PlayerHub holder, out Vector3 offset, out Quaternion rotation, out float pitchFollow)
        {
            offset = holder != null && !holder.IsOwner ? _remoteOffset : _offset;
            rotation = Quaternion.Euler(_euler);
            pitchFollow = 1f;
        }
    }
}
