using System.Collections;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The human cannon on the beach, on a wheeled carriage. Climb in (Interact), or carry a friend over and load
    /// them in. Whoever is inside aims while the fuse burns: the cannon swings round on its wheels to where they
    /// look and the barrel tips up or down; click to fire early. BOOM, and they sail out over the sea (the
    /// cannonball judge rates the splash). The one inside flies themselves on their own machine; the host keeps one
    /// lifeguard in the cannon at a time and everyone sees it turn, hears the bang and sees the smoke.
    /// </summary>
    public class HumanCannon : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _turret;    // turns on the wheels (yaw)
        [SerializeField] private Transform _barrel;    // tips up and down (pitch)
        [SerializeField] private Transform _mouth;     // forward = where it fires
        [SerializeField] private Transform[] _wheels;  // spin round their local x as the cannon turns
        [SerializeField] private float[] _wheelRadii;
        [SerializeField] private float _power = 23f;
        [SerializeField] private AudioSource _audio;

        private const float MaxTurn = 80f, MinPitch = 15f, MaxPitch = 60f, DefaultPitch = 35f, FuseTime = 4f;

        private readonly SyncVar<NetworkObject> _occupant = new SyncVar<NetworkObject>();
        private readonly SyncVar<float> _yaw = new SyncVar<float>();
        private readonly SyncVar<float> _pitch = new SyncVar<float>(DefaultPitch);
        private float _shownYaw, _shownPitch = DefaultPitch;
        private bool _aimingHere;
        private float _sentAt, _serverBusySince;

        public bool Busy => _occupant.Value != null;

        // ------------------------------------------------------------------ interaction (the player's machine)

        public bool CanInteract(PlayerHub player) => !Busy && !_aimingHere && player != null && player.Motor != null && player.Motor.Seat == null
                                                     && !PlayerCarry.IsCarried(player);

        public string GetPrompt(PlayerHub player)
        {
            PlayerHub carried = PlayerCarry.CarriedBy(player);
            return carried != null ? $"Load {carried.DisplayName} into the cannon" : "Climb into the cannon";
        }

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner || Busy) return;
            PlayerHub carried = PlayerCarry.CarriedBy(player);
            if (carried != null) LoadServer(carried.NetworkObject);
            else ClimbServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ClimbServer(NetworkConnection caller = null)
        {
            PlayerHub player = HubOf(caller);
            if (player == null || !ServerTake(player)) return;
            InTarget(caller, null);
        }

        [ServerRpc(RequireOwnership = false)]
        private void LoadServer(NetworkObject target, NetworkConnection caller = null)
        {
            PlayerHub loader = HubOf(caller);
            PlayerHub carried = target != null ? target.GetComponent<PlayerHub>() : null;
            PlayerCarry carry = PlayerCarry.Of(carried);
            if (loader == null || carry == null || carry.Carrier != loader || !ServerTake(carried)) return;
            carry.ServerHandOver();
            InTarget(carried.Owner, loader.DisplayName);
            LoadedObservers(loader.DisplayName, carried.DisplayName);
            Debug.Log($"[Cannon] {loader.DisplayName} loads {carried.DisplayName}");
        }

        [Server]
        private bool ServerTake(PlayerHub player)
        {
            if (Busy && Time.time - _serverBusySince < FuseTime + 4f) return false; // (a lost occupant frees it in time)
            _occupant.Value = player.NetworkObject;
            _serverBusySince = Time.time;
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void FreeServer() => _occupant.Value = null;

        [ObserversRpc]
        private void LoadedObservers(string loader, string loaded)
        {
            PlayerHud.ShowToast($"<b>{loader}</b> stuffs <b>{loaded}</b> into the cannon!", 2.5f);
            FloatingText.Spawn(_mouth.position + Vector3.up * 0.4f, "IN YOU GO!", new Color(1f, 0.85f, 0.3f), 1.2f, 1.4f);
        }

        [TargetRpc]
        private void InTarget(NetworkConnection target, string loadedBy)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || me.Motor == null) return;
            PlayerCarry.Of(me)?.StopHolding(); // out of the loader's arms, into the barrel
            StartCoroutine(Fire(me, loadedBy));
        }

        // ------------------------------------------------------------------ inside: aim, then fly (the occupant's machine)

        private IEnumerator Fire(PlayerHub player, string loadedBy)
        {
            _aimingHere = true;
            PlayerHud.ShowToast(loadedBy != null
                ? $"<b>{loadedBy}</b> put you in the cannon! Look where you want to fly, click to fire!"
                : "Look where you want to fly, click to fire! (it goes off by itself soon)", FuseTime);
            FuseServer();
            float start = Time.time;
            while (player != null && Time.time - start < FuseTime)
            {
                Transform view = player.Look != null && player.Look.Camera != null ? player.Look.Camera.transform : player.Head;
                Vector3 f = view.forward;
                float baseYaw = transform.eulerAngles.y;
                _shownYaw = Mathf.Clamp(Mathf.DeltaAngle(baseYaw, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg), -MaxTurn, MaxTurn);
                _shownPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg + 10f, MinPitch, MaxPitch);
                Pose(Time.deltaTime);
                if (Time.time - _sentAt > 0.1f)
                {
                    _sentAt = Time.time;
                    AimServer(_shownYaw, _shownPitch);
                }
                player.Motor.Teleport(_mouth.position - _mouth.forward * 1.2f - Vector3.up * 1.4f); // tucked in the barrel
                player.Motor.Stun(0.2f);
                if (Time.time - start > 0.6f && GameInput.GameplayActive && (GameInput.Primary.WasPressedThisFrame() || GameInput.Jump.WasPressedThisFrame()))
                    break;
                yield return null;
            }
            AimServer(_shownYaw, _shownPitch);
            if (player != null)
            {
                player.Motor.Teleport(_mouth.position + _mouth.forward * 0.6f - Vector3.up * 1.0f);
                player.Motor.AddImpulse(_mouth.forward * _power);
                BoomServer();
                Debug.Log($"[Cannon] {player.DisplayName} fired: turned {_shownYaw:F0}, barrel up {_shownPitch:F0} deg");
            }
            _aimingHere = false;
            yield return new WaitForSeconds(1f);
            FreeServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void AimServer(float yaw, float pitch)
        {
            _yaw.Value = Mathf.Clamp(yaw, -MaxTurn, MaxTurn);
            _pitch.Value = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        // ------------------------------------------------------------------ turning on the wheels (every machine)

        private void Update()
        {
            if (_aimingHere) return; // the one inside poses it straight from their aim
            float dt = Time.deltaTime;
            _shownYaw = Mathf.MoveTowards(_shownYaw, _yaw.Value, 120f * dt);
            _shownPitch = Mathf.MoveTowards(_shownPitch, _pitch.Value, 90f * dt);
            Pose(dt);
        }

        private float _posedYaw;

        private void Pose(float dt)
        {
            if (_turret == null) return;
            float turned = Mathf.DeltaAngle(_posedYaw, _shownYaw);
            _posedYaw = _shownYaw;
            _turret.localRotation = Quaternion.Euler(0f, _shownYaw, 0f);
            if (_barrel != null) _barrel.localRotation = Quaternion.Euler(-_shownPitch, 0f, 0f);
            if (_wheels == null || Mathf.Abs(turned) < 1e-4f) return;
            // Turning on the spot: each wheel rolls as far as it travels round the pivot (the two sides opposite ways).
            for (int i = 0; i < _wheels.Length; i++)
            {
                if (_wheels[i] == null) continue;
                float radius = _wheelRadii != null && i < _wheelRadii.Length ? _wheelRadii[i] : 0.4f;
                float roll = turned * _wheels[i].localPosition.x / radius;
                _wheels[i].localRotation *= Quaternion.Euler(-roll, 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ the bang

        [ServerRpc(RequireOwnership = false)]
        private void FuseServer() => FuseObservers();

        [ObserversRpc]
        private void FuseObservers()
        {
            if (_audio != null) _audio.PlayOneShot(FunSounds.Swish, 0.6f);
        }

        [ServerRpc(RequireOwnership = false)]
        private void BoomServer() => BoomObservers();

        [ObserversRpc]
        private void BoomObservers()
        {
            if (_audio != null)
            {
                _audio.PlayOneShot(Core.ProceduralAudio.Shot(Combat.GunSound.Shotgun), 1f);
                _audio.PlayOneShot(FunSounds.Whoop, 0.8f);
            }
            FloatingText.Spawn(_mouth.position + Vector3.up * 0.5f, "BOOM!", new Color(1f, 0.55f, 0.2f), 1.6f, 1.4f);
            for (int i = 0; i < 6; i++)
                FloatingText.Spawn(_mouth.position + Random.insideUnitSphere * 0.5f, "~", new Color(0.85f, 0.85f, 0.85f, 0.8f), 2f, 1.2f); // puffs of smoke
        }

        private static PlayerHub HubOf(NetworkConnection conn)
        {
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p.Owner == conn) return p;
            return null;
        }
    }
}
