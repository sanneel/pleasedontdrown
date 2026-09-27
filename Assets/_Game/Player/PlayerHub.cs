using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// The player's central component. Owns identity (name), wires up the local-only systems
    /// (motor, look, interactor, camera) for the owning client, and the remote-only visuals for everyone else.
    /// Other systems find players through <see cref="Local"/> and <see cref="All"/>.
    /// </summary>
    public class PlayerHub : NetworkBehaviour
    {
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private PlayerLook _look;
        [SerializeField] private PlayerInteractor _interactor;
        [SerializeField] private PlayerHands _hands;
        [SerializeField] private Transform _head;
        [SerializeField] private Transform _body;
        [Tooltip("Hidden for the local player (they still cast shadows).")]
        [SerializeField] private Renderer[] _selfHiddenRenderers;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private TextMesh _nameTag;

        private readonly SyncVar<string> _displayName = new SyncVar<string>();

        private static readonly List<PlayerHub> _all = new();

        public static PlayerHub Local { get; private set; }
        public static IReadOnlyList<PlayerHub> All => _all;
        public static event Action<PlayerHub> LocalPlayerChanged;

        public string DisplayName => string.IsNullOrEmpty(_displayName.Value) ? $"Lifeguard {OwnerId}" : _displayName.Value;
        public PlayerMotor Motor => _motor;
        public PlayerLook Look => _look;
        public PlayerInteractor Interactor => _interactor;
        public PlayerHands Hands => _hands;
        public Transform Head => _head;

        private float _standingHeadY;
        private float _standingBodyScaleY;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            Local = null;
            LocalPlayerChanged = null;
            DevCommands.Register("players", "", "List connected players.", _ =>
            {
                foreach (PlayerHub p in _all)
                    DevCommands.Print($"  #{p.OwnerId}  {p.DisplayName}{(p == Local ? "  (you)" : "")}  at {p.transform.position:F1}");
            });
        }

        /// <summary>Display name for a connection (host-side lookups in RPCs).</summary>
        public static string NameOf(NetworkConnection conn)
        {
            foreach (PlayerHub p in _all)
                if (p.Owner == conn) return p.DisplayName;
            return conn != null ? $"player {conn.ClientId}" : "someone";
        }

        private void Awake()
        {
            _displayName.OnChange += OnNameChanged;
            _standingHeadY = _head.localPosition.y;
            _standingBodyScaleY = _body.localScale.y;
            // Owner-only systems start off; OnStartClient enables them for the local player.
            _motor.enabled = _look.enabled = _interactor.enabled = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _all.Add(this);
            if (_bodyRenderer != null)
                _bodyRenderer.material.color = Color.HSVToRGB(Mathf.Repeat(OwnerId * 0.2718f + 0.05f, 1f), 0.6f, 0.95f);
            Debug.Log($"[Player] spawned for owner {OwnerId} (mine: {IsOwner}) at {transform.position}");

            if (IsOwner) SetupLocal();
            else _nameTag.gameObject.SetActive(true);
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Items.Item.ServerDropAllHeldBy(this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            _all.Remove(this);
            if (Local != this) return;

            DevCommands.Unregister("spawn", this);
            Local = null;
            GameInput.LocalPlayerExists = false;
            GameInput.Apply();
            SceneCameras.SetMenuCameraActive(true);
            LocalPlayerChanged?.Invoke(null);
        }

        private void SetupLocal()
        {
            Local = this;
            foreach (Renderer r in _selfHiddenRenderers)
                if (r != null) r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            _nameTag.gameObject.SetActive(false);

            var camGo = new GameObject("PlayerCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(_head, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            var muffle = camGo.AddComponent<AudioLowPassFilter>(); // switched on underwater by UnderwaterFx
            muffle.cutoffFrequency = 700f;
            muffle.enabled = false;

            _look.Attach(cam);
            _motor.enabled = _look.enabled = _interactor.enabled = true;
            _hands.RefreshLocal();
            DevCommands.Register("spawn", "<item> [count]", "Spawn items in front of you (see 'spawn list').", SpawnCommand, cheat: true, owner: this);

            SceneCameras.SetMenuCameraActive(false);
            GameInput.LocalPlayerExists = true;
            GameInput.Apply();
            SetNameServer(SteamBootstrap.LocalName);
            LocalPlayerChanged?.Invoke(this);
        }

        [ServerRpc]
        private void SetNameServer(string displayName)
        {
            displayName = (displayName ?? string.Empty).Trim();
            if (displayName.Length == 0) displayName = $"Lifeguard {OwnerId}";
            _displayName.Value = displayName.Length > 24 ? displayName.Substring(0, 24) : displayName;
        }

        private void SpawnCommand(string[] args)
        {
            Items.ItemCatalog catalog = GameContent.Items;
            if (catalog == null) throw new InvalidOperationException("no item catalog in this scene");
            if (args.Length == 0 || args[0] == "list")
            {
                foreach (Items.Item item in catalog.Items)
                    DevCommands.Print($"  {item.DisplayName}");
                return;
            }
            if (catalog.Find(args[0]) == null) throw new ArgumentException($"no item called '{args[0]}'");
            int count = args.Length > 1 ? Mathf.Clamp((int)DevCommands.ParseFloat(args, 1), 1, 20) : 1;
            Vector3 spot = _head.position + _head.forward * 2f;
            SpawnItemServer(args[0], spot, count);
        }

        [ServerRpc]
        private void SpawnItemServer(string itemName, Vector3 position, int count)
        {
            Items.Item prefab = GameContent.Items != null ? GameContent.Items.Find(itemName) : null;
            if (prefab == null || !DevCommands.CheatsAllowed) return;
            count = Mathf.Clamp(count, 1, 20);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = position + Vector3.up * (0.7f * i) + UnityEngine.Random.insideUnitSphere * 0.1f;
                Items.Item spawned = Instantiate(prefab, p, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
                Spawn(spawned.gameObject);
            }
            Debug.Log($"[Item] {DisplayName} spawned {count}x {prefab.DisplayName}");
        }

        private void OnNameChanged(string prev, string next, bool asServer)
        {
            _nameTag.text = next;
            if (!asServer) Debug.Log($"[Player] owner {OwnerId} is now called '{next}'");
        }

        private void LateUpdate()
        {
            if (IsOwner) return;

            // Remote players: the synced head height drives the body (crouching), the name tag faces our camera.
            float crouch = Mathf.InverseLerp(_standingHeadY, _standingHeadY * 0.62f, _head.localPosition.y);
            Vector3 s = _body.localScale;
            _body.localScale = new Vector3(s.x, Mathf.Lerp(_standingBodyScaleY, _standingBodyScaleY * 0.65f, crouch), s.z);
            _body.localPosition = new Vector3(0f, _body.localScale.y, 0f);

            Camera cam = Camera.main;
            if (cam != null && _nameTag.gameObject.activeSelf)
                _nameTag.transform.rotation = cam.transform.rotation;
        }
    }
}
