using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>A knock when this body hits something hard enough (coconuts go "bonk", heads included).</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ImpactSound : MonoBehaviour
    {
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _minSpeed = 2.2f;

        private float _next;

        private void OnCollisionEnter(Collision collision)
        {
            float speed = collision.relativeVelocity.magnitude;
            if (speed < _minSpeed || Time.time < _next || _audio == null) return;
            _next = Time.time + 0.15f;
            _audio.PlayOneShot(ProceduralAudio.Bonk, Mathf.Clamp01(speed / 9f));
        }
    }
}
