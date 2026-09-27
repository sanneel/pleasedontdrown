using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// A palm you can shake (Interact on the trunk): it sways and rustles for everyone, and if it has coconuts one
    /// drops from the crown (host-spawned, a normal item: carry it, pocket it, throw it, eat it). Coconuts grow back.
    /// </summary>
    public class PalmTree : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private int _maxCoconuts = 3;
        [SerializeField] private float _regrowSeconds = 50f;
        [Tooltip("Where coconuts drop from, above the trunk's foot.")]
        [SerializeField] private float _crownHeight = 5.4f;
        [SerializeField] private Transform _model;
        [SerializeField] private AudioSource _audio;
        [SerializeField] private string _coconutItem = "Coconut";

        private readonly SyncVar<int> _coconuts = new SyncVar<int>(3);
        private float _nextGrow;
        private float _lastShakeServer = -10f;
        private float _shakeAt = -10f;
        private Quaternion _modelRest;
        private Vector3 _shakeAxis = Vector3.right;

        private void Awake()
        {
            if (_model != null) _modelRest = _model.localRotation;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _coconuts.Value = _maxCoconuts;
        }

        public bool CanInteract(PlayerHub player) => true;

        public string GetPrompt(PlayerHub player) => _coconuts.Value switch
        {
            0 => "Shake the palm (no coconuts right now)",
            1 => "Shake the palm (1 coconut left)",
            _ => $"Shake the palm ({_coconuts.Value} coconuts)"
        };

        public void OnInteract(PlayerHub player)
        {
            Shake(player != null ? player.transform.position : transform.position);
            ShakeServer(player != null ? player.transform.position : transform.position);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ShakeServer(Vector3 from, NetworkConnection caller = null)
        {
            if (Time.time - _lastShakeServer < 0.9f) return;
            _lastShakeServer = Time.time;
            ShakeObservers(from, caller != null ? caller.ClientId : -1);
            if (_coconuts.Value <= 0) return;

            Item prefab = GameContent.Items != null ? GameContent.Items.Find(_coconutItem) : null;
            if (prefab == null) return;
            _coconuts.Value--;
            if (_nextGrow < Time.time) _nextGrow = Time.time + _regrowSeconds;
            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(0.4f, 0.9f);
            Vector3 spot = transform.position + Vector3.up * _crownHeight + new Vector3(ring.x, 0f, ring.y);
            Item coconut = Instantiate(prefab, spot, Random.rotation);
            Spawn(coconut.gameObject);
            if (!coconut.Sync.Body.isKinematic)
                coconut.Sync.Body.linearVelocity = new Vector3(ring.x, 0.5f, ring.y) * 1.5f;
            Debug.Log($"[World] a coconut fell from {name} ({_coconuts.Value} left)");
        }

        [ObserversRpc]
        private void ShakeObservers(Vector3 from, int shakerId)
        {
            if (LocalConnection != null && LocalConnection.ClientId == shakerId) return; // they already saw it
            Shake(from);
        }

        private void Shake(Vector3 from)
        {
            _shakeAt = Time.time;
            Vector3 away = transform.position - from;
            away.y = 0f;
            _shakeAxis = away.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, away.normalized) : Vector3.right;
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Rustle, 0.9f);
        }

        private void Update()
        {
            if (IsServerInitialized && _coconuts.Value < _maxCoconuts && Time.time >= _nextGrow)
            {
                _coconuts.Value++;
                _nextGrow = Time.time + _regrowSeconds;
            }

            if (_model == null) return;
            float t = Time.time - _shakeAt;
            float angle = t < 1.6f ? 5f * Mathf.Sin(t * 13f) * Mathf.Exp(-2.6f * t) : 0f;
            Vector3 axis = _model.parent != null ? _model.parent.InverseTransformDirection(_shakeAxis) : _shakeAxis;
            _model.localRotation = Quaternion.AngleAxis(angle, axis) * _modelRest;
        }
    }
}
