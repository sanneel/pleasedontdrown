using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// A prop in a character's hand that the animation knows nothing about (Sandy's giant grabber claw): it sits at the
    /// hand bone and points the way the character faces, tipped down a little; <see cref="Swing"/> sweeps it out in
    /// front (a grab). Looks only, on every machine.
    /// </summary>
    public sealed class HeldProp : MonoBehaviour
    {
        public Transform Hand;
        public Transform Body;
        [Tooltip("Degrees below level while held.")] public float Pitch = 25f;
        /// <summary>0 held at rest, 1 thrust out level in a grab.</summary>
        public float Swing;
        private float _shown;

        private void LateUpdate()
        {
            if (Hand == null || Body == null) { Destroy(gameObject); return; }
            _shown = Mathf.MoveTowards(_shown, Swing, Time.deltaTime * 4f);
            transform.SetPositionAndRotation(Hand.position,
                Body.rotation * Quaternion.Euler(Mathf.Lerp(Pitch, -5f, _shown), Mathf.Lerp(18f, 0f, _shown), 0f));
        }
    }
}
