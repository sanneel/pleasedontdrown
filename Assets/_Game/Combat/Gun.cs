using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// A hitscan pistol (held item). Primary shoots where you look: the shooter picks the target (instant feel),
    /// the host checks it was plausible (holding the gun, in range, roughly where they aimed) and applies the damage;
    /// everyone sees the tracer and hears the bang.
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Gun : NetworkBehaviour, IHeldTool
    {
        [SerializeField] private Transform _muzzle;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private int _damage = 1;
        [SerializeField] private float _range = 70f;
        [SerializeField] private float _interval = 0.32f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Item _item;
        private float _nextShot;
        private float _lastServerShot;
        private static Material _tracerMaterial;

        public string UseLabel => "Shoot";

        private void Awake() => _item = GetComponent<Item>();

        public void Use(PlayerHub holder)
        {
            if (Time.time < _nextShot || holder == null) return;
            _nextShot = Time.time + _interval;
            Transform view = holder.Look != null && holder.Look.Camera != null ? holder.Look.Camera.transform : holder.Head;
            Vector3 origin = view.position, dir = view.forward;
            Vector3 end = origin + dir * _range;
            NetworkObject target = null;
            if (FirstHit(holder, origin, dir, out RaycastHit hit))
            {
                end = hit.point;
                if (DamageUtil.Find(hit.collider) is Component c && c.TryGetComponent(out NetworkObject nob)) target = nob;
            }
            holder.Gesture(AvatarGesture.Shoot);
            Vector3 muzzle = MuzzlePosition;
            ShowShot(muzzle, end, target != null);
            ShootServer(origin, dir, end, target);
        }

        private Vector3 MuzzlePosition => _muzzle != null ? _muzzle.position : transform.position + transform.forward * 0.2f;

        private bool FirstHit(PlayerHub holder, Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            int count = Physics.RaycastNonAlloc(origin, dir, _hits, _range, ~0, QueryTriggerInteraction.Ignore);
            best = default;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == holder.BodyCollider || _item.OwnsCollider(h.collider) || h.distance >= bestDistance) continue;
                bestDistance = h.distance;
                best = h;
            }
            return bestDistance < float.MaxValue;
        }

        [ServerRpc(RequireOwnership = false)]
        private void ShootServer(Vector3 origin, Vector3 dir, Vector3 end, NetworkObject target, NetworkConnection caller = null)
        {
            PlayerHub holder = _item.Holder;
            if (holder == null || holder.Owner != caller || Time.time - _lastServerShot < _interval * 0.7f) return;
            if ((origin - holder.Head.position).sqrMagnitude > 3f * 3f) return;
            _lastServerShot = Time.time;
            bool hit = false;
            if (target != null)
            {
                // Plausible: in range, and near the line they shot along (targets move a little between machines).
                Vector3 toTarget = target.transform.position + Vector3.up - origin;
                float along = Vector3.Dot(toTarget, dir.normalized);
                float off = Vector3.Cross(dir.normalized, toTarget).magnitude;
                if (along > 0f && along < _range + 2f && off < 2.5f && target.GetComponent<IDamageable>() is { } damageable)
                    hit = damageable.ServerTakeHit(_damage, DamageKind.Bullet, holder, end, dir);
            }
            ShotObservers(end, hit, caller.ClientId);
        }

        [ObserversRpc]
        private void ShotObservers(Vector3 end, bool hit, int shooterId)
        {
            if (LocalConnection != null && LocalConnection.ClientId == shooterId) return; // shown when fired
            ShowShot(MuzzlePosition, end, hit);
        }

        private void ShowShot(Vector3 from, Vector3 to, bool hit)
        {
            if (_audio != null)
            {
                _audio.pitch = Random.Range(0.92f, 1.08f);
                _audio.PlayOneShot(ProceduralAudio.Gunshot, 1f);
            }
            var go = new GameObject("Tracer");
            var line = go.AddComponent<LineRenderer>();
            if (_tracerMaterial == null) _tracerMaterial = new Material(Shader.Find("Sprites/Default")); // always in builds, vertex coloured
            line.sharedMaterial = _tracerMaterial;
            line.startColor = new Color(1f, 0.95f, 0.6f);
            line.endColor = new Color(1f, 0.8f, 0.4f, 0.4f);
            line.widthMultiplier = 0.03f;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            Destroy(go, 0.06f);
            if (hit) FloatingText.Spawn(to + Vector3.up * 0.3f, "BANG!", new Color(1f, 0.5f, 0.3f), 0.8f, 0.7f);
        }
    }
}
