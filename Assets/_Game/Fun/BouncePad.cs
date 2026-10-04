using PleaseDontDrown.Audio;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// Trampolines and the diving board: land on the trigger over the mat and up you go (plus a push along
    /// <see cref="_push"/> for the board). Every machine sees the mat squash and hears the boing; only the lifeguard's
    /// own machine launches them (it simulates their body). Loose things bounce too.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BouncePad : MonoBehaviour
    {
        [SerializeField] private float _launch = 13f;
        [SerializeField] private Vector3 _push = Vector3.zero;    // local direction and size of an extra shove
        [SerializeField] private Transform _squash;               // the mat/board to squash and spring back
        [SerializeField] private AudioSource _audio;

        private float _squashAmount;
        private float _lastBounce;
        private Vector3 _squashScale = Vector3.one;

        private void Awake()
        {
            GetComponent<Collider>().isTrigger = true;
            if (_squash != null) _squashScale = _squash.localScale;
        }

        private void OnTriggerStay(Collider other) => Bounce(other);
        private void OnTriggerEnter(Collider other) => Bounce(other);

        private void Bounce(Collider other)
        {
            if (Time.time - _lastBounce < 0.25f) return;
            PlayerHub hub = other.GetComponentInParent<PlayerHub>();
            if (hub != null)
            {
                if (hub.Motor == null || hub.Motor.Seat != null) return;
                bool mine = hub.IsOwner;
                float vy = mine ? hub.Motor.Velocity.y : 0f;
                if (mine && vy > 1f) return; // already on the way up
                _lastBounce = Time.time;
                Feedback();
                if (!mine) return;
                // Higher the harder you landed (a little), and a jump pressed on the mat goes higher still.
                float launch = _launch + Mathf.Clamp(-vy * 0.25f, 0f, 4f);
                hub.Motor.AddImpulse(Vector3.up * (launch - vy) + transform.TransformDirection(_push));
                return;
            }
            Rigidbody body = other.attachedRigidbody;
            if (body != null && !body.isKinematic && body.linearVelocity.y < 0.5f)
            {
                _lastBounce = Time.time;
                body.linearVelocity = new Vector3(body.linearVelocity.x, _launch * 0.7f, body.linearVelocity.z) + transform.TransformDirection(_push) * 0.5f;
                Feedback();
            }
        }

        private void Feedback()
        {
            _squashAmount = 1f;
            if (_audio != null)
            {
                _audio.pitch = Random.Range(0.9f, 1.15f);
                _audio.PlayOneShot(FunSounds.Boing, 0.8f);
            }
        }

        private void Update()
        {
            if (_squash == null || _squashAmount <= 0f) return;
            _squashAmount = Mathf.Max(0f, _squashAmount - Time.deltaTime * 3f);
            float s = Mathf.Sin(_squashAmount * 14f) * _squashAmount * 0.25f;
            _squash.localScale = new Vector3(_squashScale.x * (1f + s * 0.3f), _squashScale.y * (1f - s), _squashScale.z * (1f + s * 0.3f));
        }
    }
}
