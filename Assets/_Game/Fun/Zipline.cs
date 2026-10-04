using System.Collections;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The zipline from the lifeguard tower's deck out over the sea: grab the handle (Interact) and whizz down the
    /// cable, faster and faster, and let go at the far post into deep water. The lifeguard's own machine carries
    /// them; everyone else sees them fly by (their position is synced like any walk).
    /// </summary>
    public class Zipline : MonoBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _start;    // the cable's top end
        [SerializeField] private Transform _end;      // the far post's eye
        [SerializeField] private Transform _handle;   // rides the cable (back at the start between rides)
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _maxSpeed = 15f;

        private bool _busy;
        private Vector3 _handleHome;

        private void Awake()
        {
            if (_handle != null) _handleHome = _handle.position;
        }

        public bool CanInteract(PlayerHub player) => !_busy && player != null && player.Motor != null && player.Motor.Seat == null;
        public string GetPrompt(PlayerHub player) => "Ride the zipline";

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner || _busy) return;
            StartCoroutine(Ride(player));
        }

        private IEnumerator Ride(PlayerHub player)
        {
            _busy = true;
            PlayerHud.ShowToast("WHEEEEE!", 1.5f);
            if (_audio != null) _audio.PlayOneShot(FunSounds.Whoop, 0.9f);
            Vector3 a = _start.position, b = _end.position;
            float length = Vector3.Distance(a, b);
            Vector3 dir = (b - a) / length;
            float along = 0f, speed = 3f;
            const float hang = 2.05f; // from the cable down to the feet
            while (along < length - 0.5f && player != null)
            {
                float dt = Time.fixedDeltaTime;
                speed = Mathf.Min(_maxSpeed, speed + 9f * dt);
                along += speed * dt;
                Vector3 on = a + dir * along;
                if (_handle != null) _handle.position = on;
                player.Motor.Teleport(on - Vector3.up * hang);
                player.Motor.Stun(0.2f);
                if (_audio != null && Random.value < 0.04f) _audio.PlayOneShot(FunSounds.Swish, 0.3f);
                yield return new WaitForFixedUpdate();
            }
            if (player != null)
            {
                // Let go: on out over the water with the speed it had.
                player.Motor.AddImpulse(dir * speed * 0.6f + Vector3.up * 1.5f);
                PlayerHud.ShowToast("...and let go!", 1.2f);
            }
            yield return new WaitForSeconds(0.8f);
            // The handle slides back up to the tower for the next one.
            float t = 0f;
            Vector3 from = _handle != null ? _handle.position : a;
            while (_handle != null && t < 1f)
            {
                t += Time.deltaTime / 2f;
                _handle.position = Vector3.Lerp(from, _handleHome, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            _busy = false;
        }
    }
}
