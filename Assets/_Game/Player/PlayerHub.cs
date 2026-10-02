using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.UI;
using UnityEngine;

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
        [SerializeField] private PlayerAvatar _avatar;
        [SerializeField] private PlayerVitals _vitals;
        [SerializeField] private Transform _head;
        [SerializeField] private TextMesh _nameTag;

        private readonly SyncVar<string> _displayName = new SyncVar<string>();
        private readonly SyncVar<ulong> _avatarLook = new SyncVar<ulong>();
        private readonly SyncVar<byte> _activeSlot = new SyncVar<byte>();
        private FirstPersonArms _arms;

        private static readonly List<PlayerHub> _all = new();

        public static PlayerHub Local { get; private set; }
        public static IReadOnlyList<PlayerHub> All => _all;
        public static event Action<PlayerHub> LocalPlayerChanged;
        /// <summary>Host: a player's body now exists (the host's own first, then each friend who joins).</summary>
        public static event Action<PlayerHub> ServerJoined;

        public string DisplayName => string.IsNullOrEmpty(_displayName.Value) ? $"Lifeguard {OwnerId}" : _displayName.Value;
        public PlayerMotor Motor => _motor;
        public PlayerLook Look => _look;
        public PlayerInteractor Interactor => _interactor;
        public PlayerHands Hands => _hands;
        public PlayerAvatar Avatar => _avatar;
        public PlayerVitals Vitals => _vitals;
        /// <summary>Selected inventory slot as the host knows it (our own player predicts; see PlayerHands).</summary>
        public int SyncedActiveSlot => _activeSlot.Value;
        public FirstPersonArms Arms => _arms;
        public Transform Head => _head;
        public AvatarLook AvatarLook => AvatarLook.Unpack(_avatarLook.Value);
        /// <summary>The body capsule (items ignore it while this player holds or has just thrown them).</summary>
        public Collider BodyCollider { get; private set; }


        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            Local = null;
            LocalPlayerChanged = null;
            ServerJoined = null;
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
            _avatarLook.OnChange += OnLookChanged;
            _activeSlot.OnChange += OnActiveSlotChanged;
            BodyCollider = GetComponent<CapsuleCollider>();
            // Owner-only systems start off; OnStartClient enables them for the local player.
            _motor.enabled = _look.enabled = _interactor.enabled = false;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _all.Add(this);
            _nameTag.color = Color.HSVToRGB(Mathf.Repeat(OwnerId * 0.2718f + 0.05f, 1f), 0.6f, 0.95f);
            _avatar.ApplyLook(AvatarLook); // already synced for a late joiner; the uniform otherwise
            Debug.Log($"[Player] spawned for owner {OwnerId} (mine: {IsOwner}) at {transform.position}");

            if (IsOwner)
            {
                SetupLocal();
            }
            else
            {
                // Other players: their machine simulates them; here the body is a kinematic capsule the
                // NetworkTransform moves (we still collide with it). Facing lives on the synced head, so the body stays unrotated.
                var body = GetComponent<Rigidbody>();
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
                PlayerLook.ResetBodyRotation(transform);
                _nameTag.gameObject.SetActive(true);
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerJoined?.Invoke(this);
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
            DevCommands.Unregister("wave", this);
            AvatarCustomizer.LookChanged -= SetLook;
            Local = null;
            GameInput.LocalPlayerExists = false;
            GameInput.Apply();
            SceneCameras.SetMenuCameraActive(true);
            LocalPlayerChanged?.Invoke(null);
        }

        private void SetupLocal()
        {
            Local = this;
            _avatar.SetLocal(true); // we only see our own body's shadow...
            _nameTag.gameObject.SetActive(false);

            var camGo = new GameObject("PlayerCamera") { tag = "MainCamera" };
            camGo.transform.SetParent(_head, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            var muffle = camGo.AddComponent<AudioLowPassFilter>(); // switched on underwater by UnderwaterFx
            muffle.cutoffFrequency = 700f;
            muffle.enabled = false;

            _arms = camGo.AddComponent<FirstPersonArms>(); // ...and our arms
            _arms.Init(this, cam);
            _look.Attach(cam);
            _motor.enabled = _look.enabled = _interactor.enabled = true;
            _hands.RefreshLocal();
            DevCommands.Register("spawn", "<item> [count]", "Spawn items in front of you (see 'spawn list').", SpawnCommand, cheat: true, owner: this);
            DevCommands.Register("wave", "", "Wave (also the V key).", _ => Gesture(AvatarGesture.Wave), owner: this);
            SetLook(AvatarCustomizer.LocalLook);
            AvatarCustomizer.LookChanged += SetLook;

            SceneCameras.SetMenuCameraActive(false);
            GameInput.LocalPlayerExists = true;
            GameInput.Apply();
            SetNameServer(SteamBootstrap.LocalName);
            LocalPlayerChanged?.Invoke(this);
        }

        // ------------------------------------------------------------------ inventory

        /// <summary>Owner: tell everyone which slot is in our hands.</summary>
        public void RequestActiveSlot(int slot)
        {
            if (IsOwner) SetActiveSlotServer((byte)Mathf.Clamp(slot, 0, PlayerHands.SlotCount - 1));
        }

        [ServerRpc]
        private void SetActiveSlotServer(byte slot) => _activeSlot.Value = slot;

        private void OnActiveSlotChanged(byte prev, byte next, bool asServer)
        {
            if (!IsOwner && _hands != null) _hands.OnActiveSlotChanged(); // our own player already switched locally
        }

        // ------------------------------------------------------------------ looks & gestures

        /// <summary>Owner: wear this look (shown right away here, synced to everyone).</summary>
        public void SetLook(AvatarLook look)
        {
            if (!IsOwner) return;
            _avatar.ApplyLook(look);
            if (_arms != null) _arms.Build(look);
            SetLookServer(look.Pack());
        }

        [ServerRpc]
        private void SetLookServer(ulong packed) => _avatarLook.Value = AvatarLook.Unpack(packed).Pack(); // normalised

        private void OnLookChanged(ulong prev, ulong next, bool asServer)
        {
            if (asServer && IsClientStarted) return; // a host applies it once, as a client
            AvatarLook look = AvatarLook.Unpack(next);
            _avatar.ApplyLook(look);
            if (_arms != null) _arms.Build(look);
        }

        /// <summary>Owner: play a gesture (throw, reach, wave, punch...) here and on everyone else's screen.</summary>
        public void Gesture(AvatarGesture gesture, Vector3 point = default)
        {
            if (!IsOwner) return;
            _avatar.OnGesture(gesture, point);
            if (_arms != null) _arms.Play(gesture, point);
            GestureServer(gesture, point);
        }

        [ServerRpc]
        private void GestureServer(AvatarGesture gesture, Vector3 point) => GestureObservers(gesture, point);

        [ObserversRpc(ExcludeOwner = true)]
        private void GestureObservers(AvatarGesture gesture, Vector3 point) => _avatar.OnGesture(gesture, point);

        /// <summary>This player pressed a chest for CPR (called on every machine; see VictimBrain).</summary>
        public void ShowPump(Vector3 chest)
        {
            _avatar.OnPump(chest);
            if (_arms != null) _arms.OnPump(chest);
            if (IsOwner && Look != null) Look.KneelOver(chest, true);
        }

        /// <summary>The other CPR steps: a rescue breath at the mouth, or a punch to the face (every machine).</summary>
        public void ShowCprAction(Rescue.CprStep step, Vector3 point)
        {
            AvatarGesture gesture = step == Rescue.CprStep.Breath ? AvatarGesture.Breath : AvatarGesture.Punch;
            _avatar.OnCprGesture(gesture, point);
            if (_arms != null)
            {
                if (step == Rescue.CprStep.Punch) _arms.PlaySlap(point);
                else _arms.Play(gesture, point);
            }
            // We stay down beside them through the whole of CPR (the chest is 30 cm below the face, lying on the back).
            if (IsOwner && Look != null) Look.KneelOver(point - Vector3.up * 0.05f, false);
            // Mouth-to-mouth in first person: our view goes right down to their lips and back.
            if (IsOwner && step == Rescue.CprStep.Breath && Look != null) Look.LeanIn(point, 1.1f);
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

        private void Update()
        {
            if (IsOwner && GameInput.GameplayActive && GameInput.Emote.WasPressedThisFrame())
                Gesture(AvatarGesture.Wave);
        }

        private void LateUpdate()
        {
            if (IsOwner) return;
            // Remote players: the name tag faces our camera.
            Camera cam = Camera.main;
            if (cam == null || !_nameTag.gameObject.activeSelf) return;
            _nameTag.transform.rotation = cam.transform.rotation;
            // Same size on screen up close (see StoryNpc's name tag).
            _nameTag.transform.localScale = Vector3.one * Mathf.Clamp(Vector3.Distance(cam.transform.position, _nameTag.transform.position) / 8f, 0.22f, 1f);
        }
    }
}
