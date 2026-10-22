using System.Collections.Generic;
using PleaseDontDrown.Core;
using PleaseDontDrown.World;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// The casings' particle system tells this where each spent case hits the world: a clink on wood or stone, a soft
    /// tick on sand, louder the faster it lands (the last little rolls are silent).
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class CasingSounds : MonoBehaviour
    {
        private ParticleSystem _ps;
        private readonly List<ParticleCollisionEvent> _events = new();
        private float _last;
        private int _variant;

        private void Awake() => _ps = GetComponent<ParticleSystem>();

        private void OnParticleCollision(GameObject other)
        {
            int n = _ps.GetCollisionEvents(other, _events);
            for (int i = 0; i < n; i++)
            {
                float speed = _events[i].velocity.magnitude;
                if (speed < 0.6f || Time.time - _last < 0.02f) continue;
                _last = Time.time;
                Collider c = _events[i].colliderComponent as Collider;
                SurfaceKind surface = c != null ? SurfaceType.Of(c) : SurfaceKind.Sand;
                float volume = Mathf.Clamp01(speed / 3.5f) * (surface == SurfaceKind.Sand ? 0.3f : 0.5f);
                AudioSource.PlayClipAtPoint(ProceduralAudio.CasingDrop(surface, _variant++), _events[i].intersection, volume);
            }
        }
    }
}
