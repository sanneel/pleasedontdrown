using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Fun
{
    /// <summary>
    /// The human cannon on the beach, on a wheeled carriage.
    /// <list type="bullet">
    /// <item>Push it (Interact): take the push bar at the back and walk it round the beach; it turns with you and the
    /// barrel tips up and down with your look. Click to fire whoever is inside; Interact again to let go.</item>
    /// <item>Carry a friend over and load them in (Interact): you take the push bar and decide when they fly.</item>
    /// <item>Climb in yourself (Secondary): with nobody pushing, you aim (look) and fire (click) yourself, or the
    /// fuse does it; Jump climbs back out. With somebody pushing, they fire you.</item>
    /// </list>
    /// Whoever is inside sticks out of the muzzle, head and hands up, for everyone (their own view looks out of
    /// it). BOOM, and they sail out over the sea (the cannonball judge rates the splash). The pusher moves the cannon
    /// on their own machine and the host passes it on; the one inside flies themselves on theirs.
    /// </summary>
    public class HumanCannon : NetworkBehaviour, IInteractionHandler, IInteractionSecondary
    {
        [SerializeField] private Transform _turret;    // turns on the wheels (yaw)
        [SerializeField] private Transform _barrel;    // tips up and down (pitch)
        [SerializeField] private Transform _mouth;     // forward = where it fires
        [SerializeField] private Transform[] _wheels;  // spin round their local x as the cannon turns
        [SerializeField] private float[] _wheelRadii;
        [SerializeField] private float _power = 23f;
        [SerializeField] private AudioSource _audio;

        private const float MaxTurn = 80f, MinPitch = 15f, MaxPitch = 60f, DefaultPitch = 35f, FuseTime = 4f;
        // The push bar (turret space): where the hands go, and where the pusher's feet are.
        private static readonly Vector3 BarLeft = new(-0.24f, 0.93f, -1.9f), BarRight = new(0.24f, 0.93f, -1.9f);
        private const float PushBack = 2.4f; // from the cannon's middle to the pusher's feet

        private readonly SyncVar<NetworkObject> _occupant = new();
        private readonly SyncVar<NetworkObject> _pusher = new();
        private readonly SyncVar<float> _yaw = new();                      // the turret on the carriage
        private readonly SyncVar<float> _pitch = new(DefaultPitch);        // the barrel
        private readonly SyncVar<Vector3> _spot = new();                   // the carriage on the beach (pushed about)
        private readonly SyncVar<float> _heading = new();
        private float _shownYaw, _shownPitch = DefaultPitch, _posedYaw;
        private bool _insideHere, _launchNow;
        private float _sentAt, _serverBusySince;
        // Pushing it, on the pusher's machine.
        private PlayerHub _pushingHere;
        private float _pushingSince, _pushHeading;
        private Collider[] _colliders;
        private bool _devFire;

        /// <summary>Tests: as if the fire button were clicked (by the pusher, or the one inside on their own).</summary>
        public void PressFire() => _devFire = true;

        private bool FirePressed()
        {
            bool pressed = _devFire || (GameInput.GameplayActive && GameInput.Primary.WasPressedThisFrame());
            _devFire = false;
            return pressed;
        }

        public bool Busy => _occupant.Value != null;
        private bool Pushed => _pusher.Value != null;

        private static readonly List<HumanCannon> All = new();
        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        /// <summary>Is this lifeguard sitting in a cannon right now?</summary>
        public static bool IsInside(PlayerHub player) => Holding(player) != null;

        /// <summary>Is this lifeguard pushing a cannon about (hands on its push bar)?</summary>
        public static bool IsPushing(PlayerHub player)
        {
            if (player == null) return false;
            foreach (HumanCannon c in All)
                if (c != null && c._pusher.Value != null && c._pusher.Value == player.NetworkObject) return true;
            return false;
        }

        private static HumanCannon Holding(PlayerHub player)
        {
            if (player == null) return null;
            foreach (HumanCannon c in All)
                if (c != null && c._occupant.Value != null && c._occupant.Value == player.NetworkObject) return c;
            return null;
        }

        /// <summary>
        /// Where somebody inside a cannon is drawn: up the barrel, head out of the muzzle (every machine). The body's
        /// up runs out along the barrel, its front faces the sky side of it.
        /// </summary>
        public static bool InBarrel(PlayerHub player, out Vector3 muzzle, out Quaternion rotation)
        {
            HumanCannon c = Holding(player);
            if (c == null || c._mouth == null)
            {
                muzzle = default;
                rotation = Quaternion.identity;
                return false;
            }
            muzzle = c._mouth.position;
            rotation = Quaternion.LookRotation(c._barrel.up, c._mouth.forward);
            return true;
        }

        /// <summary>The pusher's hands on the push bar (fingers over it, palms down).</summary>
        public static bool PushGrips(PlayerHub player, out Avatars.HandGrip left, out Avatars.HandGrip right)
        {
            foreach (HumanCannon c in All)
                if (c != null && player != null && c._pusher.Value != null && c._pusher.Value == player.NetworkObject && c._turret != null)
                {
                    Vector3 fingers = c._turret.forward - c._turret.up * 0.6f;
                    left = new Avatars.HandGrip(c._turret.TransformPoint(BarLeft), fingers, -c._turret.up, Avatars.HandPose.Fist);
                    right = new Avatars.HandGrip(c._turret.TransformPoint(BarRight), fingers, -c._turret.up, Avatars.HandPose.Fist);
                    return true;
                }
            left = right = default;
            return false;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _spot.Value = transform.position;
            _heading.Value = transform.eulerAngles.y;
        }

        // ------------------------------------------------------------------ interaction (the player's machine)

        private static bool Free(PlayerHub player) => player != null && player.Motor != null && player.Motor.Seat == null && !PlayerCarry.IsCarried(player);

        public bool CanInteract(PlayerHub player)
        {
            if (!Free(player) || _insideHere) return false;
            if (IsPushing(player)) return true;                                   // let go
            if (PlayerCarry.CarriedBy(player) != null) return !Busy;              // load them in
            return !Pushed && !IsInside(player);                                  // take the push bar
        }

        public string GetPrompt(PlayerHub player)
        {
            if (IsPushing(player)) return "Let go of the cannon";
            PlayerHub carried = PlayerCarry.CarriedBy(player);
            return carried != null ? $"Load {carried.DisplayName} into the cannon" : "Push the cannon";
        }

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner) return;
            if (IsPushing(player))
            {
                StopPushing();
                return;
            }
            PlayerHub carried = PlayerCarry.CarriedBy(player);
            if (carried != null)
            {
                if (!Busy) LoadServer(carried.NetworkObject);
            }
            else if (!Pushed) PushServer();
        }

        public bool CanSecondary(PlayerHub player) => Free(player) && !Busy && !_insideHere && !IsPushing(player) && PlayerCarry.CarriedBy(player) == null;

        public string GetSecondaryPrompt(PlayerHub player) => "Climb into the cannon";

        public void OnSecondary(PlayerHub player)
        {
            if (player == null || !player.IsOwner || Busy) return;
            ClimbServer();
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
            // Whoever loaded them takes the push bar (unless somebody already has it): they say when.
            if (!Pushed)
            {
                _pusher.Value = loader.NetworkObject;
                PushTarget(caller);
            }
        }

        [Server]
        private bool ServerTake(PlayerHub player)
        {
            // (A lost occupant frees it in time when nobody's pushing; one being pushed waits for the pusher.)
            if (Busy && (Pushed || Time.time - _serverBusySince < FuseTime + 6f)) return false;
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
            if (!IsInside(PlayerHub.Local)) // (not right in the face of the one looking out of the muzzle)
                FloatingText.Spawn(_mouth.position + Vector3.up * 0.4f, "IN YOU GO!", new Color(1f, 0.85f, 0.3f), 1.2f, 1.4f);
        }

        [TargetRpc]
        private void InTarget(NetworkConnection target, string loadedBy)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || me.Motor == null) return;
            PlayerCarry.Of(me)?.StopHolding(); // out of the loader's arms, into the barrel
            StartCoroutine(Inside(me, loadedBy));
        }

        // ------------------------------------------------------------------ pushing it about (the pusher's machine)

        [ServerRpc(RequireOwnership = false)]
        private void PushServer(NetworkConnection caller = null)
        {
            PlayerHub player = HubOf(caller);
            if (player == null || Pushed || IsInside(player)) return;
            _pusher.Value = player.NetworkObject;
            Debug.Log($"[Cannon] {player.DisplayName} takes the push bar");
            PushTarget(caller);
        }

        [ServerRpc(RequireOwnership = false)]
        private void LetGoServer(NetworkConnection caller = null)
        {
            PlayerHub player = HubOf(caller);
            if (player != null && _pusher.Value == player.NetworkObject) _pusher.Value = null;
        }

        [TargetRpc]
        private void PushTarget(NetworkConnection target)
        {
            PlayerHub me = PlayerHub.Local;
            if (me == null || me.Motor == null) return;
            if (me.Hands != null) me.Hands.Stow(); // both hands on the bar
            _pushingHere = me;
            _pushingSince = Time.time;
            _pushHeading = transform.eulerAngles.y + _shownYaw;
            // Straight behind it, facing where it points (the turret's turn goes into the carriage).
            _shownYaw = 0f;
            Quaternion facing = Quaternion.Euler(0f, _pushHeading, 0f);
            transform.rotation = facing;
            Pose(0f);
            me.Motor.Teleport(OnGround(transform.position - facing * Vector3.forward * PushBack) + Vector3.up * 0.05f);
            if (me.Look != null) me.Look.LookAt(_mouth.position + facing * Vector3.forward * 12f + Vector3.up * 3f);
            IgnoreCollisions(me, true);
            PlayerHud.ShowToast(Busy
                ? $"Your friend's in! Walk to roll it, look to aim, {Key(GameInput.Primary)} <b>FIRE!</b>   {Key(GameInput.Interact)} let go"
                : $"Walk to roll the cannon, look to aim   {Key(GameInput.Primary)} fire   {Key(GameInput.Interact)} let go", 5f);
        }

        private static string Key(UnityEngine.InputSystem.InputAction action) => $"<color=#ffd24a>[{GameInput.KeyLabel(action)}]</color>";

        private void StopPushing()
        {
            if (_pushingHere != null) IgnoreCollisions(_pushingHere, false);
            _pushingHere = null;
            LetGoServer();
        }

        private void IgnoreCollisions(PlayerHub player, bool ignore)
        {
            _colliders ??= GetComponentsInChildren<Collider>();
            if (player == null || player.BodyCollider == null) return;
            foreach (Collider c in _colliders)
                if (c != null) Physics.IgnoreCollision(player.BodyCollider, c, ignore);
        }

        private void UpdatePushing(float dt)
        {
            PlayerHub me = _pushingHere;
            bool stillOurs = Time.time - _pushingSince < 1f || (me != null && _pusher.Value == me.NetworkObject);
            if (me == null || !stillOurs || me.Motor == null || me.Motor.Seat != null || me.Motor.IsSwimming || PlayerCarry.IsCarried(me))
            {
                StopPushing();
                return;
            }
            // The carriage swings round in front of us as we turn, and rolls along as we walk.
            Transform view = me.Look != null && me.Look.Camera != null ? me.Look.Camera.transform : me.Head;
            Vector3 look = view.forward;
            Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up);
            if (flat.sqrMagnitude > 1e-4f)
                _pushHeading = Mathf.MoveTowardsAngle(_pushHeading, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, 110f * dt);
            Quaternion facing = Quaternion.Euler(0f, _pushHeading, 0f);
            Vector3 feet = me.transform.position;
            Vector3 spot = feet + facing * Vector3.forward * PushBack;
            spot.y = Ground(spot, transform.position.y);
            if (WaterSurface.Exists && WaterSurface.HeightAt(spot) - spot.y > 0.5f)
            {
                PlayerHud.ShowToast("The cannon won't roll into the sea.", 2f);
                StopPushing();
                return;
            }
            Roll(spot - transform.position);
            transform.SetPositionAndRotation(spot, facing);
            _shownYaw = Mathf.MoveTowards(_shownYaw, 0f, 120f * dt);
            _shownPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg + 18f, MinPitch, MaxPitch);
            Pose(dt);
            if (Time.time - _sentAt > 0.07f)
            {
                _sentAt = Time.time;
                PushStateServer(spot, _pushHeading, _shownPitch);
            }
            if (Time.time - _pushingSince > 0.4f)
            {
                if (FirePressed()) FireServer();
                // (Let go from anywhere: aiming high, the bar isn't what we're looking at.)
                else if (GameInput.GameplayActive && GameInput.Interact.WasPressedThisFrame()) StopPushing();
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void PushStateServer(Vector3 spot, float heading, float pitch, NetworkConnection caller = null)
        {
            PlayerHub player = HubOf(caller);
            if (player == null || _pusher.Value != player.NetworkObject) return;
            _spot.Value = spot;
            _heading.Value = heading;
            _yaw.Value = 0f;
            _pitch.Value = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        /// <summary>The pusher clicked: whoever is inside goes (an empty cannon just clicks).</summary>
        [ServerRpc(RequireOwnership = false)]
        private void FireServer(NetworkConnection caller = null)
        {
            PlayerHub player = HubOf(caller);
            if (player == null || _pusher.Value != player.NetworkObject) return;
            PlayerHub inside = _occupant.Value != null ? _occupant.Value.GetComponent<PlayerHub>() : null;
            if (inside == null)
            {
                ClickObservers();
                return;
            }
            Debug.Log($"[Cannon] {player.DisplayName} fires {inside.DisplayName}");
            LaunchTarget(inside.Owner);
        }

        [TargetRpc]
        private void LaunchTarget(NetworkConnection target) => _launchNow = true;

        [ObserversRpc]
        private void ClickObservers()
        {
            if (_audio != null) _audio.PlayOneShot(FunSounds.BallBounce, 0.5f);
            FloatingText.Spawn(_mouth.position + Vector3.up * 0.3f, "*click* (empty!)", new Color(1f, 1f, 1f, 0.9f), 0.8f, 1.2f);
        }

        /// <summary>The sand (or deck) under a point, not the cannon itself or anybody standing about.</summary>
        private float Ground(Vector3 at, float fallback)
        {
            _colliders ??= GetComponentsInChildren<Collider>();
            float best = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(at + Vector3.up * 3f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<PlayerHub>() != null) continue;
                if (hit.collider.attachedRigidbody != null && !hit.collider.attachedRigidbody.isKinematic) continue; // a ball, a crate
                best = Mathf.Max(best, hit.point.y);
            }
            return float.IsNegativeInfinity(best) ? fallback : best;
        }

        private Vector3 OnGround(Vector3 at)
        {
            at.y = Ground(at, transform.position.y);
            return at;
        }

        private void Roll(Vector3 moved)
        {
            if (_wheels == null) return;
            float along = Vector3.Dot(moved, transform.forward);
            if (Mathf.Abs(along) < 1e-5f) return;
            for (int i = 0; i < _wheels.Length; i++)
            {
                if (_wheels[i] == null) continue;
                float radius = _wheelRadii != null && i < _wheelRadii.Length ? _wheelRadii[i] : 0.4f;
                _wheels[i].localRotation *= Quaternion.Euler(along / radius * Mathf.Rad2Deg, 0f, 0f);
            }
        }

        // ------------------------------------------------------------------ inside: aim, then fly (the occupant's machine)

        private IEnumerator Inside(PlayerHub player, string loadedBy)
        {
            _insideHere = true;
            _launchNow = false;
            bool onOurOwn = loadedBy == null && !Pushed;
            PlayerHud.ShowToast(loadedBy != null
                ? $"<b>{loadedBy}</b> stuffed you in the cannon! They decide when you fly..."
                : Pushed ? "In you go! Whoever's pushing decides when you fly..."
                : $"Look where you want to fly, {Key(GameInput.Primary)} fire! (it goes off by itself soon)   {Key(GameInput.Jump)} climb out", FuseTime);
            if (onOurOwn) FuseServer();
            float start = Time.time, fuseFrom = Time.time;
            bool climbOut = false;
            while (player != null)
            {
                if (Pushed)
                {
                    // Somebody has the push bar: they aim it and they fire. We just look out of the muzzle.
                    fuseFrom = Time.time;
                    if (_launchNow) break;
                }
                else
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
                    bool canAct = Time.time - start > 0.6f && GameInput.GameplayActive;
                    if (Time.time - start > 0.6f && FirePressed()) break;
                    if (canAct && GameInput.Jump.WasPressedThisFrame())
                    {
                        climbOut = true;
                        break;
                    }
                    if (onOurOwn && Time.time - fuseFrom > FuseTime) break;
                }
                player.Motor.Teleport(_mouth.position - _mouth.forward * 1.2f - Vector3.up * 1.4f); // tucked in the barrel
                player.Motor.Stun(0.2f);
                if (player.Look != null) player.Look.ViewFrom(_mouth.position + _mouth.forward * 0.25f + _barrel.up * 0.12f);
                yield return null;
            }
            if (player != null)
            {
                if (climbOut)
                    player.Motor.Teleport(OnGround(transform.position + transform.right * 1.6f) + Vector3.up * 0.1f);
                else
                {
                    if (!Pushed) AimServer(_shownYaw, _shownPitch);
                    player.Motor.Teleport(_mouth.position + _mouth.forward * 0.6f - Vector3.up * 1.0f);
                    player.Motor.AddImpulse(_mouth.forward * _power);
                    BoomServer();
                    Debug.Log($"[Cannon] {player.DisplayName} fired: barrel up {_mouth.forward.y * 90f:F0}ish deg, toward {_mouth.forward}");
                }
            }
            _insideHere = false;
            _launchNow = false;
            yield return new WaitForSeconds(climbOut ? 0.2f : 1f);
            FreeServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void AimServer(float yaw, float pitch)
        {
            if (Pushed) return; // the pusher aims
            _yaw.Value = Mathf.Clamp(yaw, -MaxTurn, MaxTurn);
            _pitch.Value = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        // ------------------------------------------------------------------ turning on the wheels (every machine)

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_pushingHere != null)
            {
                UpdatePushing(dt);
                return;
            }
            // Pushed about by somebody else: the carriage glides after where they've rolled it.
            if (_spot.Value != Vector3.zero)
            {
                float k = 1f - Mathf.Exp(-12f * dt);
                Vector3 next = Vector3.Lerp(transform.position, _spot.Value, k);
                if ((next - transform.position).sqrMagnitude > 25f) next = _spot.Value; // a long way: just be there
                Roll(next - transform.position);
                transform.SetPositionAndRotation(next, Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, _heading.Value, 0f), k));
            }
            if (_insideHere && !Pushed) return; // the one inside poses it straight from their aim
            _shownYaw = Mathf.MoveTowards(_shownYaw, _yaw.Value, 120f * dt);
            _shownPitch = Mathf.MoveTowards(_shownPitch, _pitch.Value, 90f * dt);
            Pose(dt);
        }

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
