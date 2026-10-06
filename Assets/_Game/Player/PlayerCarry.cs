using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Picking up another lifeguard: look at them and press Interact and you scoop them up in your arms (they lie
    /// back and enjoy the ride). Tap Drop to put them down, hold Drop and let go to throw them (or click to fling them
    /// at once), or carry them to the human cannon and load them in. Mash Jump to wriggle free.
    /// The host decides who carries whom; the carried lifeguard's own machine keeps their body in the carrier's arms.
    /// </summary>
    [DefaultExecutionOrder(-10)] // before the avatar animator, so the body is in the arms when it's posed
    public class PlayerCarry : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private PlayerHub _hub;

        private const float MaxReach = 3.2f, ThrowCharge = 0.6f, MaxCarry = 25f;
        private const int WrigglesToEscape = 6;

        private static readonly List<PlayerCarry> All = new();
        private readonly SyncVar<NetworkObject> _carrier = new SyncVar<NetworkObject>();
        private float _letGoUntil = -1f; // owner: just let go, don't snap back into the arms while the sync catches up
        private float _carriedSince, _dropHeldSince = -1f, _wriggleWindow;
        private int _wriggles;
        private Collider _ignoring;

        public PlayerHub Hub => _hub;
        /// <summary>Who has us in their arms (null: our own feet).</summary>
        public PlayerHub Carrier => _carrier.Value != null ? _carrier.Value.GetComponent<PlayerHub>() : null;

        public static PlayerCarry Of(PlayerHub hub) => hub != null ? hub.GetComponent<PlayerCarry>() : null;
        public static bool IsCarried(PlayerHub hub) => Of(hub)?.Carrier != null;

        /// <summary>The lifeguard <paramref name="carrier"/> has in their arms, or null.</summary>
        public static PlayerHub CarriedBy(PlayerHub carrier)
        {
            if (carrier == null) return null;
            foreach (PlayerCarry c in All)
                if (c != null && c._carrier.Value != null && c._carrier.Value == carrier.NetworkObject) return c._hub;
            return null;
        }

        /// <summary>Where a carried lifeguard's feet go: across the carrier's arms, lying back, head to the left.</summary>
        /// <summary>Where the one carried lies, from the carrier's feet (turned with the carrier's look).</summary>
        public static readonly Vector3 HoldOffset = new(0.17f, 1.04f, 0.04f);

        public static Vector3 HoldPoint(PlayerHub carrier, out float yaw)
        {
            yaw = carrier.Head != null ? carrier.Head.eulerAngles.y : carrier.transform.eulerAngles.y;
            // (Fireman's carry: their hips on our right shoulder, facing behind us; AvatarPose.Carried.)
            return carrier.transform.position + Quaternion.Euler(0f, yaw, 0f) * HoldOffset;
        }

        private void Awake()
        {
            if (_hub == null) _hub = GetComponent<PlayerHub>();
            _carrier.OnChange += (previous, next, _) => IgnoreCarrier(next);
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public override void OnStartClient()
        {
            base.OnStartClient();
            if (!IsOwner) return;
            DevCommands.Register("carry", "", "Pick up the nearest other lifeguard (automated tests).", _ =>
            {
                PlayerHub best = null;
                foreach (PlayerHub p in PlayerHub.All)
                    if (p != null && p != _hub && (best == null || (p.transform.position - transform.position).sqrMagnitude < (best.transform.position - transform.position).sqrMagnitude))
                        best = p;
                PlayerCarry target = Of(best);
                if (target == null || !target.CanInteract(_hub)) { DevCommands.Print("nobody to pick up"); return; }
                target.OnInteract(_hub);
                DevCommands.Print($"picking up {best.DisplayName}");
            }, owner: this);
            DevCommands.Register("yeet", "[speed]", "Throw whoever you carry where you look (automated tests).", args =>
            {
                PlayerHub carried = CarriedBy(_hub);
                if (carried == null) { DevCommands.Print("not carrying anybody"); return; }
                float speed = args.Length > 0 ? DevCommands.ParseFloat(args, 0) : 13f;
                Release(carried, _hub.Head.forward * speed + Vector3.up * 4f, true);
            }, owner: this);
            DevCommands.Register("cannon", "", "Climb into the nearest cannon, or load whoever you carry (automated tests).", _ =>
            {
                Fun.HumanCannon best = null;
                foreach (Fun.HumanCannon c in FindObjectsByType<Fun.HumanCannon>(FindObjectsSortMode.None))
                    if (best == null || (c.transform.position - transform.position).sqrMagnitude < (best.transform.position - transform.position).sqrMagnitude)
                        best = c;
                if (best == null || !best.CanInteract(_hub)) { DevCommands.Print("no cannon free"); return; }
                DevCommands.Print(best.GetPrompt(_hub));
                best.OnInteract(_hub);
            }, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("carry", this);
            DevCommands.Unregister("yeet", this);
            DevCommands.Unregister("cannon", this);
        }

        // ------------------------------------------------------------------ picking somebody up (the carrier's machine)

        public bool CanInteract(PlayerHub player) =>
            player != null && player != _hub && Carrier == null && CarriedBy(_hub) == null && Vehicles.Vehicle.RideOf(_hub) == null
            && player.Motor != null && player.Motor.Seat == null && !IsCarried(player) && CarriedBy(player) == null
            && (player.Hands == null || player.Hands.HeldItem == null);

        public string GetPrompt(PlayerHub player) => $"Pick up {_hub.DisplayName}";

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner) return;
            PickUpServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void PickUpServer(NetworkConnection caller = null)
        {
            PlayerHub carrier = HubOf(caller);
            if (carrier == null || !CanInteract(carrier)) return;
            if (Vector3.Distance(carrier.transform.position, transform.position) > MaxReach + 1f) return;
            _carrier.Value = carrier.NetworkObject;
            _carriedSince = Time.time;
            PickedUpObservers(carrier.NetworkObject, carrier.DisplayName, _hub.DisplayName);
            Debug.Log($"[Carry] {carrier.DisplayName} picks up {_hub.DisplayName}");
        }

        [ObserversRpc]
        private void PickedUpObservers(NetworkObject carrierObject, string carrier, string carried)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == _hub)
                PlayerHud.ShowToast($"<b>{carrier}</b> picked you up! Mash [{GameInput.KeyLabel(GameInput.Jump)}] to wriggle free.", 3f);
            else if (me != null && me.NetworkObject == carrierObject)
                PlayerHud.ShowToast($"You've got <b>{carried}</b>! Tap [{GameInput.KeyLabel(GameInput.Drop)}] to put them down, hold it to throw, or load them into the cannon.", 4f);
            FloatingText.Spawn(transform.position + Vector3.up * 2.2f, "UPSY-DAISY!", new Color(1f, 0.9f, 0.5f), 1f, 1.4f);
        }

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (IsServerInitialized) ServerCheck();
            if (!IsOwner || _hub == null) return;
            if (Carrier != null && Time.time >= _letGoUntil) UpdateWriggle();
            PlayerHub carried = CarriedBy(_hub);
            if (carried != null) UpdateCarrying(carried);
            else _dropHeldSince = -1f;
        }

        private void FixedUpdate()
        {
            // Our own machine keeps our body in the carrier's arms (everyone else sees it through the network).
            if (!IsOwner || _hub == null || _hub.Motor == null) return;
            PlayerHub carrier = Carrier;
            if (carrier == null || Time.time < _letGoUntil) return;
            _hub.Motor.Stun(0.25f);
            _hub.Motor.Teleport(HoldPoint(carrier, out _));
        }

        private void LateUpdate()
        {
            // Everyone else: in the arms exactly, not a network-delay behind them.
            if (IsOwner) return;
            PlayerHub carrier = Carrier;
            if (carrier != null) transform.position = HoldPoint(carrier, out _);
        }

        /// <summary>Carrier: tap Drop to put them down, hold it to throw; Primary flings them at once.</summary>
        private void UpdateCarrying(PlayerHub carried)
        {
            if (!GameInput.GameplayActive) return;
            Transform aim = _hub.Look != null && _hub.Look.Camera != null ? _hub.Look.Camera.transform : _hub.Head;
            Vector3 forward = aim.forward;
            if (GameInput.Primary.WasPressedThisFrame())
            {
                Release(carried, forward * 13f + Vector3.up * 4f, true);
                return;
            }
            if (GameInput.Drop.WasPressedThisFrame()) _dropHeldSince = Time.time;
            if (_dropHeldSince >= 0f && GameInput.Drop.WasReleasedThisFrame())
            {
                float charge = Mathf.Clamp01((Time.time - _dropHeldSince) / ThrowCharge);
                _dropHeldSince = -1f;
                Vector3 flat = new Vector3(forward.x, 0f, forward.z).normalized;
                if (charge < 0.3f) Release(carried, flat * 1.5f, false);
                else Release(carried, forward * (5f + 9f * charge) + Vector3.up * (2.5f + 2.5f * charge), true);
            }
        }

        private static void Release(PlayerHub carried, Vector3 velocity, bool thrown) => Of(carried)?.ReleaseServer(velocity, thrown);

        [ServerRpc(RequireOwnership = false)]
        private void ReleaseServer(Vector3 velocity, bool thrown, NetworkConnection caller = null)
        {
            PlayerHub carrier = Carrier;
            if (carrier == null || carrier.Owner != caller) return;
            ServerLetGo(Vector3.ClampMagnitude(velocity, 20f), thrown ? $"{carrier.DisplayName} throws {_hub.DisplayName}!" : null);
        }

        /// <summary>Host: out of the arms, at the hold point, with this velocity (and a shout if it was a throw).</summary>
        [Server]
        public void ServerLetGo(Vector3 velocity, string shout)
        {
            PlayerHub carrier = Carrier;
            if (carrier == null) return;
            Vector3 at = HoldPoint(carrier, out _);
            _carrier.Value = null;
            LetGoTarget(Owner, at, velocity);
            if (shout != null) ThrownObservers(shout, at);
            Debug.Log($"[Carry] {carrier.DisplayName} lets go of {_hub.DisplayName} ({velocity.magnitude:F1} m/s)");
        }

        /// <summary>Host: out of the arms, and our own machine takes it from here (the cannon loads us).</summary>
        [Server]
        public void ServerHandOver() => _carrier.Value = null;

        [TargetRpc]
        private void LetGoTarget(NetworkConnection target, Vector3 at, Vector3 velocity)
        {
            if (_hub == null || _hub.Motor == null) return;
            StopHolding();
            _hub.Motor.Teleport(at);
            if (velocity.sqrMagnitude > 0.01f) _hub.Motor.AddImpulse(velocity);
        }

        /// <summary>Owner: stop keeping the body in the arms right now (the host's word follows).</summary>
        public void StopHolding() => _letGoUntil = Time.time + 1f;

        [ObserversRpc]
        private void ThrownObservers(string shout, Vector3 at)
        {
            FloatingText.Spawn(at + Vector3.up * 1.2f, "YEEEET!", new Color(1f, 0.6f, 0.2f), 1.5f, 1.4f);
            PlayerHud.ShowToast(shout, 2f);
            AudioSource.PlayClipAtPoint(FunSounds.Whoop, at, 0.9f);
        }

        // ------------------------------------------------------------------ wriggling free (the carried one's machine)

        private void UpdateWriggle()
        {
            if (!GameInput.GameplayActive || !GameInput.Jump.WasPressedThisFrame()) return;
            if (Time.time > _wriggleWindow) _wriggles = 0;
            _wriggleWindow = Time.time + 1.2f;
            if (++_wriggles < WrigglesToEscape) return;
            _wriggles = 0;
            WriggleServer();
        }

        [ServerRpc]
        private void WriggleServer()
        {
            PlayerHub carrier = Carrier;
            if (carrier == null) return;
            Vector3 back = -(Quaternion.Euler(0f, carrier.Head.eulerAngles.y, 0f) * Vector3.forward);
            ServerLetGo(back * 2f + Vector3.up * 3f, null);
            WriggledObservers(_hub.DisplayName, carrier.DisplayName);
        }

        [ObserversRpc]
        private void WriggledObservers(string carried, string carrier)
        {
            PlayerHud.ShowToast($"<b>{carried}</b> wriggled free from <b>{carrier}</b>!", 2f);
            FloatingText.Spawn(transform.position + Vector3.up * 2f, "LET ME GO!", new Color(1f, 0.5f, 0.5f), 1f, 1.3f);
        }

        private void IgnoreCarrier(NetworkObject carrier)
        {
            Collider mine = _hub != null ? _hub.BodyCollider : null;
            if (_ignoring != null && mine != null) Physics.IgnoreCollision(mine, _ignoring, false);
            _ignoring = null;
            if (carrier != null) _letGoUntil = -1f; // picked up (again): into the arms straight away
            if (carrier == null || mine == null) return;
            PlayerHub hub = carrier.GetComponent<PlayerHub>();
            if (hub == null || hub.BodyCollider == null) return;
            Physics.IgnoreCollision(mine, hub.BodyCollider, true);
            _ignoring = hub.BodyCollider;
            _carriedSince = Time.time;
        }

        /// <summary>Host, from <see cref="PlayerHub"/>'s connection list: the lifeguard this connection plays.</summary>
        private static PlayerHub HubOf(NetworkConnection conn)
        {
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p.Owner == conn) return p;
            return null;
        }

        /// <summary>Host: carried too long, or the carrier left or got on a vehicle: put them down.</summary>
        [Server]
        private void ServerCheck()
        {
            if (ReferenceEquals(_carrier.Value, null)) return;
            PlayerHub carrier = Carrier;
            if (carrier == null || !carrier.IsSpawned || Time.time - _carriedSince > MaxCarry || Vehicles.Vehicle.RideOf(carrier) != null)
            {
                if (carrier == null) { _carrier.Value = null; LetGoTarget(Owner, transform.position + Vector3.up * 0.5f, Vector3.zero); }
                else ServerLetGo(Vector3.up * 2f, null);
            }
        }
    }
}
