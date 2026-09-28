using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// A plywood target on the dev island's range: shows the damage of every hit, falls over backwards when its health
    /// runs out and pops back up a moment later. Moving targets slide back and forth on a clock every machine shares,
    /// so nothing needs sending.
    /// </summary>
    public class TargetDummy : NetworkBehaviour, IDamageable
    {
        [SerializeField] private Transform _hinge;
        [SerializeField] private int _maxHealth = 100;
        [SerializeField] private float _resetSeconds = 2.5f;
        [Tooltip("Moving target: slides this far each way (local space). Zero = stands still.")]
        [SerializeField] private Vector3 _travel;
        [SerializeField] private float _period = 5f;
        [SerializeField] private AudioSource _audio;

        private readonly SyncVar<int> _health = new();
        private readonly SyncVar<bool> _down = new();
        private Vector3 _home;
        private float _fall;
        private float _upAt;

        private void Awake() => _home = transform.localPosition;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _health.Value = _maxHealth;
        }

        public bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction)
        {
            if (_down.Value) return false;
            _health.Value = Mathf.Max(0, _health.Value - damage);
            bool dead = _health.Value <= 0;
            HitObservers(damage, point, dead);
            if (dead)
            {
                Debug.Log($"[Target] {name} down ({kind} by {(attacker != null ? attacker.DisplayName : "?")})");
                _down.Value = true;
                _upAt = Time.time + _resetSeconds;
            }
            return true;
        }

        [ObserversRpc]
        private void HitObservers(int damage, Vector3 point, bool dead)
        {
            Color color = damage >= 100 ? new Color(1f, 0.3f, 0.25f) : damage >= 30 ? new Color(1f, 0.75f, 0.3f) : new Color(1f, 1f, 0.85f);
            FloatingText.Spawn(point + Vector3.up * 0.15f, dead ? $"{damage}  DOWN" : damage.ToString(), color, dead ? 1.1f : 0.8f, 0.9f);
            if (_audio != null)
            {
                _audio.pitch = dead ? 0.8f : Random.Range(1.1f, 1.3f);
                _audio.PlayOneShot(ProceduralAudio.Bonk, 0.7f);
            }
        }

        private void Update()
        {
            if (IsServerInitialized && _down.Value && Time.time >= _upAt)
            {
                _health.Value = _maxHealth;
                _down.Value = false;
            }
            _fall = Mathf.MoveTowards(_fall, _down.Value ? 85f : 0f, Time.deltaTime * (_down.Value ? 400f : 160f));
            if (_hinge != null) _hinge.localRotation = Quaternion.Euler(-_fall, 0f, 0f); // top goes away from the shooter
            if (_travel != Vector3.zero && TimeManager != null)
            {
                float t = (float)TimeManager.TicksToTime(TimeManager.Tick);
                transform.localPosition = _home + _travel * Mathf.Sin(t * Mathf.PI * 2f / Mathf.Max(0.5f, _period));
            }
        }
    }
}
