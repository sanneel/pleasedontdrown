using FishNet.Object;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Creatures
{
    /// <summary>
    /// A shark (host-moved, NetworkTransform). Circles with its fin out of the water; when the story sends it at a
    /// tourist it charges, takes a leg (<see cref="VictimBrain.ServerBite"/>) and swims off. Shoot it and it flees.
    /// </summary>
    public class Shark : NetworkBehaviour, IDamageable
    {
        [SerializeField] private Transform _tail;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _cruiseSpeed = 3f;
        [SerializeField] private float _chargeSpeed = 9f;
        [SerializeField] private float _depth = 0.55f;
        [SerializeField] private int _health = 100;

        private enum Mode { Circle, Charge, Leave }

        private Mode _mode;
        private Vector3 _center;
        private float _radius = 12f;
        private float _angle;
        private VictimBrain _target;
        private Vector3 _leaveTo;
        private float _leaveUntil;

        public bool IsDone => _mode == Mode.Leave && Time.time > _leaveUntil;
        /// <summary>Host: the shark bit someone.</summary>
        public static event System.Action<Shark, VictimBrain> ServerBit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerBit = null;

        [Server]
        public void ServerCircle(Vector3 center, float radius)
        {
            _mode = Mode.Circle;
            _center = center;
            _radius = radius;
            _angle = Random.Range(0f, 360f);
        }

        [Server]
        public void ServerAttack(VictimBrain target)
        {
            _target = target;
            _mode = Mode.Charge;
            FinObservers();
        }

        [ObserversRpc]
        private void FinObservers()
        {
            FloatingText.Spawn(transform.position + Vector3.up * 1.2f, "!!!", new Color(1f, 0.3f, 0.25f), 1.4f, 1.5f);
            PlayerHud.ShowToast("<color=#ff7060><b>SHARK!</b></color>", 3f);
        }

        private void Update()
        {
            // Everyone: wag the tail (faster when charging).
            if (_tail != null)
                _tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * (_mode == Mode.Charge ? 14f : 5f)) * 25f, 0f);
            if (!IsServerInitialized) return;

            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            Vector3 target;
            float speed;
            switch (_mode)
            {
                case Mode.Charge when _target != null && _target.State != VictimState.Lost:
                    target = _target.transform.position;
                    speed = _chargeSpeed;
                    if ((new Vector3(target.x - p.x, 0f, target.z - p.z)).sqrMagnitude < 1.4f * 1.4f)
                    {
                        _target.ServerBite();
                        ServerBit?.Invoke(this, _target);
                        BiteObservers(target);
                        Leave(p + (p - target).normalized * 45f);
                    }
                    break;
                case Mode.Leave:
                    target = _leaveTo;
                    speed = _chargeSpeed * 0.8f;
                    if (Time.time > _leaveUntil + 10f) Despawn(gameObject);
                    break;
                default:
                    _mode = Mode.Circle;
                    _angle += _cruiseSpeed / Mathf.Max(2f, _radius) * Mathf.Rad2Deg * dt;
                    float a = _angle * Mathf.Deg2Rad;
                    target = _center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _radius;
                    speed = _cruiseSpeed;
                    break;
            }

            Vector3 to = target - p;
            to.y = 0f;
            Vector3 step = to.sqrMagnitude > 1e-4f ? to.normalized * Mathf.Min(to.magnitude, speed * dt) : Vector3.zero;
            Vector3 next = p + step;
            // Swim just under the surface so the fin shows; never over land.
            if (Shore.WaterDepthAt(next) < 1.2f && _mode != Mode.Charge) next = p;
            next.y = (WaterSurface.Exists ? WaterSurface.HeightAt(next) : -0.35f) - _depth;
            transform.position = next;
            if (step.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(step), 1f - Mathf.Exp(-6f * dt));
        }

        private void Leave(Vector3 to)
        {
            _mode = Mode.Leave;
            _target = null;
            _leaveTo = to;
            _leaveUntil = Time.time + 6f;
        }

        [ObserversRpc]
        private void BiteObservers(Vector3 at)
        {
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Crunch, 1f);
            SplashFx.Spawn(new Vector3(at.x, WaterSurface.Exists ? WaterSurface.HeightAt(at) : at.y, at.z), 0.5f);
        }

        public bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction)
        {
            if (_mode == Mode.Leave) return false;
            _health -= damage;
            HitObservers(point, _health <= 0);
            if (_health <= 0) Leave(transform.position + new Vector3(direction.x, 0f, direction.z).normalized * 50f);
            return true;
        }

        [ObserversRpc]
        private void HitObservers(Vector3 point, bool fled) => FloatingText.Spawn(point + Vector3.up * 0.5f, fled ? "IT FLED!" : "HIT!", new Color(1f, 0.6f, 0.3f), 1f, 1f);
    }
}
