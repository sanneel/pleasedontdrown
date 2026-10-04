using PleaseDontDrown.Audio;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// A basketball's bounces and rim clangs, heard on every machine. Collision callbacks only happen where the ball
    /// is simulated, so this listens to how it moves instead: a sudden turn upward is a bounce, a hard knock near a
    /// hoop's rim is a clang.
    /// </summary>
    public class BallSounds : MonoBehaviour
    {
        [SerializeField] private AudioSource _audio;

        private Vector3 _last, _lastVelocity;
        private bool _primed;
        private float _quietUntil;

        private Items.Item _item;

        private void Awake() => _item = GetComponent<Items.Item>();

        private void FixedUpdate()
        {
            if (_item != null && _item.IsHeld)
            {
                _primed = false; // swung about in a hand: not bouncing
                _lastVelocity = Vector3.zero;
                return;
            }
            Vector3 p = transform.position;
            float dt = Time.fixedDeltaTime;
            if (!_primed)
            {
                _primed = true;
                _last = p;
                _quietUntil = Time.time + 0.12f; // the first steps after a throw set the speed, they're no bounce
                return;
            }
            Vector3 v = (p - _last) / dt;
            _last = p;
            Vector3 change = v - _lastVelocity;
            _lastVelocity = v;
            if (_audio == null || Time.time < _quietUntil || change.magnitude < 2.2f || change.magnitude > 40f) return; // (a teleport isn't a bounce)
            bool rim = false;
            foreach (Collider c in Physics.OverlapSphere(p, 0.2f, ~0, QueryTriggerInteraction.Ignore))
                if (c.name.StartsWith("Rim")) rim = true;
            float loud = Mathf.Clamp01(change.magnitude / 9f);
            _audio.pitch = Random.Range(0.92f, 1.08f);
            _audio.PlayOneShot(rim ? FunSounds.Rim : FunSounds.BallBounce, rim ? 0.5f + loud * 0.5f : 0.3f + loud * 0.7f);
            _quietUntil = Time.time + 0.08f;
        }
    }
}
