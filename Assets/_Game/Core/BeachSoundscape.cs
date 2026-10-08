using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Core
{
    /// <summary>Quiet surf and occasional birds on each client's beach.</summary>
    public sealed class BeachSoundscape : MonoBehaviour
    {
        private AudioSource _surf, _bird, _underwater;
        private float _nextBird;

        private void Awake()
        {
            _surf = gameObject.AddComponent<AudioSource>();
            _surf.clip = BeachAudio.Surf;
            _surf.loop = true;
            _surf.playOnAwake = false;
            _surf.spatialBlend = 0f;
            _surf.volume = 0f;
            _surf.Play();
            _underwater = gameObject.AddComponent<AudioSource>();
            _underwater.clip = Audio.WaterFoley.UnderwaterLoop;
            _underwater.loop = true;
            _underwater.playOnAwake = false;
            _underwater.spatialBlend = 0f;
            _underwater.volume = 0f;
            _underwater.Play();
            _bird = gameObject.AddComponent<AudioSource>();
            _bird.playOnAwake = false;
            _bird.spatialBlend = 0f;
            _bird.volume = 0.5f;
            _nextBird = Time.unscaledTime + UnityEngine.Random.Range(9f, 16f);
        }

        private void Update()
        {
            bool active = PlayerHub.Local != null;
            bool submerged = active && PlayerHub.Local.Motor != null && PlayerHub.Local.Motor.IsHeadUnderwater;
            float target = active && !submerged ? 0.32f : 0f;
            _surf.volume = Mathf.MoveTowards(_surf.volume, target, Time.unscaledDeltaTime * 0.15f);
            _underwater.volume = Mathf.MoveTowards(_underwater.volume, submerged ? 0.18f : 0f, Time.unscaledDeltaTime * 0.35f);
            if (!active || submerged || Time.unscaledTime < _nextBird) return;
            _bird.pitch = UnityEngine.Random.Range(0.86f, 1.18f);
            _bird.PlayOneShot(Audio.ParrotSounds.Squawk, UnityEngine.Random.Range(0.12f, 0.22f)); // distant parrots, not gulls
            _nextBird = Time.unscaledTime + UnityEngine.Random.Range(12f, 26f);
        }
    }
}
