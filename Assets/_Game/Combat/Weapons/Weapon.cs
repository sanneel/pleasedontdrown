using System.Collections;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// A gun you hold (pistol, SMG, shotgun, rifle, sniper). Everything about handling it happens on the holder's
    /// machine for an instant feel: fire rate, semi/full auto, spread, magazine and reload, aiming down the sights
    /// (zoom, scope), recoil (the view climbs, the gun kicks on springs), sway, sprint pose and inspecting. Shots are
    /// real projectiles (<see cref="ProjectileSystem"/>): the host checks each shot (holder, fire rate, ammo) and
    /// passes it on to everyone else, who fly their own copy; the shooter's copy reports hits, which the host checks
    /// again before applying damage.
    /// Parts (sight, barrel, laser, extended magazine, better rounds) are bought at the shop and synced as small
    /// numbers; every part's model is already on the gun and just switched on.
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Weapon : NetworkBehaviour, IHeldTool, IHoldPose
    {
        public enum PartKind : byte { Sight, Barrel, Laser, Magazine, Rounds }

        /// <summary>A part the shop can offer for this gun.</summary>
        public struct PartOffer
        {
            public PartKind Kind;
            public byte Index;
            public string Name;
            public int Price;
            public bool Fitted;
        }

        [Header("Handling")]
        [SerializeField] private string _kind = "Pistol";
        [SerializeField] private GunSound _sound;
        [SerializeField] private ProjectileKind _projectile;
        [SerializeField] private bool _fullAuto;
        [Tooltip("Seconds between shots.")]
        [SerializeField] private float _interval = 0.2f;
        [SerializeField] private int _pellets = 1;
        [Tooltip("Cone half-angle in degrees from the hip / aiming.")]
        [SerializeField] private float _hipSpread = 2f;
        [SerializeField] private float _aimSpread = 0.2f;
        [SerializeField] private float _speed = 180f;
        [SerializeField] private float _gravity = 4f;
        [Tooltip("Velocity change given to loose things it hits (m/s).")]
        [SerializeField] private float _force = 3f;
        [Tooltip("How far its bullets fly (m).")]
        [SerializeField] private float _range = 250f;
        [Tooltip("Shoves the shooter backwards (m/s), e.g. the shotgun.")]
        [SerializeField] private float _shooterPush;
        [SerializeField] private float _flashSize = 1f;

        [Header("Magazine")]
        [SerializeField] private int _magazine = 12;
        [SerializeField] private int _extendedMagazine = 18;
        [SerializeField] private float _reloadTime = 1.5f;
        [Tooltip("How far into the reload the magazine counts as full (dropping it before that loses the reload).")]
        [Range(0f, 1f)] [SerializeField] private float _refillAt = 0.85f;

        [Header("Recoil")]
        [Tooltip("View climb per shot in degrees: x = random sideways range, y = up.")]
        [SerializeField] private Vector2 _climb = new(0.5f, 1.4f);
        [Tooltip("Gun kick in the hands (m): x = random sideways, y = up, z = back.")]
        [SerializeField] private Vector3 _kick = new(0.004f, 0.01f, 0.045f);
        [Tooltip("Gun twist in the hands (degrees): x = random roll/yaw, y = muzzle up.")]
        [SerializeField] private Vector2 _kickTurn = new(2f, 8f);
        [SerializeField] private float _springPosition = 230f, _dampPosition = 21f, _springRotation = 260f, _dampRotation = 23f;
        [Tooltip("Recoil while aiming, relative to the hip.")]
        [SerializeField] private float _aimRecoil = 0.55f;

        [Header("Pose")]
        [SerializeField] private Vector3 _sprintOffset = new(-0.04f, -0.07f, -0.06f);
        [SerializeField] private Vector3 _sprintEuler = new(12f, -38f, 14f);

        [Header("Parts (index 0 is what the gun comes with)")]
        [SerializeField] private WeaponSight[] _sights = { new() };
        [SerializeField] private WeaponBarrel[] _barrels = { new() };
        [SerializeField] private WeaponTier[] _tiers = { new() };
        [SerializeField] private GameObject _laser;
        [SerializeField] private int _laserPrice = 120;
        [SerializeField] private GameObject _magazineModel;
        [SerializeField] private GameObject _extendedMagazineModel;
        [SerializeField] private int _extendedMagazinePrice = 150;

        [Header("Moving bits")]
        [SerializeField] private Transform _ejectPort;
        [Tooltip("Slides back on each shot (a pistol's slide, a shotgun's pump).")]
        [SerializeField] private Transform _slide;
        [SerializeField] private float _slideTravel = 0.03f;
        [SerializeField] private AudioSource _audio;

        private readonly SyncVar<byte> _sight = new();
        private readonly SyncVar<byte> _barrel = new();
        private readonly SyncVar<byte> _tier = new();
        private readonly SyncVar<bool> _extended = new();
        private readonly SyncVar<bool> _laserOn = new();
        private readonly SyncVar<byte> _ammo = new();

        private Item _item;
        private Renderer[] _renderers;
        private LineRenderer _laserBeam;
        private Transform _laserDot;
        private Vector3 _slideRest;
        private float _slideKick;
        private readonly RaycastHit[] _hits = new RaycastHit[12];

        // The holder's machine.
        private int _localAmmo;
        private float _nextShot;
        private bool _queued;
        private bool _reloading;
        private bool _refilled;
        private bool _wantsReload;
        private float _reloadStart;
        private float _aim, _aimVelocity;
        private bool _wasAiming;
        private float _sprint;
        private float _equip;
        private float _inspectStart = -10f;
        private float _wall, _wallVelocity;
        private Vector2 _sway;
        private Vector3 _kickPosition, _kickPositionVelocity, _kickRotation, _kickRotationVelocity;
        private int _scriptShots;
        private bool _scriptAim;
        private Coroutine _reloadSounds;

        // The host.
        private float _lastServerShot = -10f;
        private int _hitBudget;

        /// <summary>The gun the local player has in their hands (null when none).</summary>
        public static Weapon Local { get; private set; }
        /// <summary>Looking through a scope: the gun and arms hide and the HUD draws the scope.</summary>
        public static bool LocalScoped => Local != null && Local.IsScopedIn;

        public string Kind => _kind;
        public string DisplayName => _item != null ? _item.DisplayName : _kind;
        public string UseLabel => "Shoot";
        public int Ammo => Local == this ? _localAmmo : _ammo.Value;
        public int MagazineSize => _extended.Value ? _extendedMagazine : _magazine;
        public int Damage => Tier.Damage;
        public int Pellets => _pellets;
        public bool FullAuto => _fullAuto;
        public float Interval => _interval;
        public bool IsReloading => _reloading;
        public float ReloadProgress => _reloading ? Mathf.Clamp01((Time.time - _reloadStart) / _reloadTime) : 0f;
        public float Aim => _aim;
        public bool IsScopedIn { get; private set; }
        public bool IsInspecting => Time.time - _inspectStart < InspectSeconds;
        public WeaponSight Sight => _sights[Mathf.Min(_sight.Value, _sights.Length - 1)];
        public WeaponBarrel Barrel => _barrels[Mathf.Min(_barrel.Value, _barrels.Length - 1)];
        public WeaponTier Tier => _tiers[Mathf.Min(_tier.Value, _tiers.Length - 1)];
        public bool HasLaser => _laserOn.Value;
        public bool HasExtendedMagazine => _extended.Value;
        /// <summary>Current cone (degrees) for the crosshair.</summary>
        public float CurrentSpread { get; private set; }

        private const float InspectSeconds = 2.4f;

        private Transform Muzzle => Barrel.Muzzle != null ? Barrel.Muzzle : _barrels[0].Muzzle != null ? _barrels[0].Muzzle : transform;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Init()
        {
            Local = null;
            DevCommands.Register("fire", "[shots]", "Pull the trigger of the gun in your hands (automated tests).", args =>
            {
                if (Local == null) { DevCommands.Print("not holding a gun"); return; }
                Local._scriptShots += args.Length > 0 ? Mathf.Max(1, Mathf.RoundToInt(DevCommands.ParseFloat(args, 0))) : 1;
            }, cheat: true);
            DevCommands.Register("aim", "<0|1>", "Aim down the sights (automated tests).", args =>
            {
                if (Local != null) Local._scriptAim = args.Length == 0 || DevCommands.ParseFloat(args, 0) > 0.5f;
            }, cheat: true);
            DevCommands.Register("reload", "", "Reload the gun in your hands.", _ => { if (Local != null) Local._wantsReload = true; });
            DevCommands.Register("inspect", "", "Look the gun in your hands over (the F key).", _ => { if (Local != null) Local._inspectStart = Time.time; });
            DevCommands.Register("fit", "<part>", "Host: fit a part to the gun in your hands for free (e.g. 'fit scope').", args =>
            {
                if (Local == null) { DevCommands.Print("not holding a gun"); return; }
                if (!Local.IsServerInitialized) { DevCommands.Print("host only"); return; }
                string name = string.Join(" ", args);
                if (!Local.ServerFitByName(name))
                    DevCommands.Print($"no part '{name}'; this gun takes: {string.Join(", ", Local.Offers().ConvertAll(o => o.Name))}");
            }, cheat: true);
            DevCommands.Register("gun", "", "The gun in your hands: ammo, parts, state.", _ =>
            {
                if (Local == null) { DevCommands.Print("not holding a gun"); return; }
                Weapon w = Local;
                DevCommands.Print($"{w.DisplayName}: {w._localAmmo}/{w.MagazineSize} (host says {w._ammo.Value}), damage {w.Damage} x{w._pellets}, " +
                                  $"sight {w.Sight.Name}, barrel {w.Barrel.Name}, laser {w.HasLaser}, aim {w._aim:F2}, reloading {w._reloading}, " +
                                  $"projectiles in flight {ProjectileSystem.LiveCount}");
            });
        }

        private void Awake()
        {
            _item = GetComponent<Item>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            if (_slide != null) _slideRest = _slide.localPosition;
            _sight.OnChange += (_, _, _) => ApplyParts();
            _barrel.OnChange += (_, _, _) => ApplyParts();
            _extended.OnChange += (_, _, _) => ApplyParts();
            _laserOn.OnChange += (_, _, _) => ApplyParts();
            ApplyParts();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _ammo.Value = (byte)MagazineSize;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyParts();
        }

        private void OnDisable()
        {
            if (Local == this) EndLocal();
        }

        // ------------------------------------------------------------------ per frame

        private void Update()
        {
            PlayerHub holder = _item.Holder;
            bool mine = holder != null && holder == PlayerHub.Local && !_item.IsStowed;
            if (mine && Local != this) BeginLocal();
            else if (!mine && Local == this) EndLocal();
            if (Local == this) LocalUpdate(holder, Mathf.Min(Time.deltaTime, 0.1f));
        }

        private void LateUpdate()
        {
            // Everyone: the slide snaps back on each shot, the laser shines where it points.
            _slideKick = Mathf.MoveTowards(_slideKick, 0f, Time.deltaTime / 0.09f);
            if (_slide != null) _slide.localPosition = _slideRest + Vector3.back * (_slideTravel * Mathf.SmoothStep(0f, 1f, _slideKick));
            UpdateLaser();
        }

        private void BeginLocal()
        {
            if (Local != null) Local.EndLocal();
            Local = this;
            _localAmmo = _ammo.Value;
            _equip = 0f;
            _aim = _aimVelocity = 0f;
            _sprint = 0f;
            _reloading = _queued = false;
            _wantsReload = _localAmmo == 0;
            _kickPosition = _kickPositionVelocity = _kickRotation = _kickRotationVelocity = Vector3.zero;
            _inspectStart = -10f;
            _scriptShots = 0;
        }

        private void EndLocal()
        {
            if (Local == this) Local = null;
            if (_reloading && !_refilled) _wantsReload = true;
            _reloading = false;
            _scriptAim = false;
            _aim = 0f;
            if (IsScopedIn) SetScoped(false);
            if (_reloadSounds != null) StopCoroutine(_reloadSounds);
        }

        private void LocalUpdate(PlayerHub holder, float dt)
        {
            PlayerMotor motor = holder.Motor;
            bool canAct = GameInput.GameplayActive && _item.IsConfirmedHolder(holder) && (holder.Hands == null || !holder.Hands.IsCharging);
            bool scripted = _scriptShots > 0 && Time.time >= _nextShot && !_reloading;
            bool fireDown = canAct && (GameInput.Primary.WasPressedThisFrame() || scripted);
            bool fireHeld = canAct && GameInput.Primary.IsPressed();
            bool aimHeld = (canAct && GameInput.Secondary.IsPressed()) || _scriptAim;
            bool reload = canAct && GameInput.Reload.WasPressedThisFrame();
            bool inspect = canAct && GameInput.Inspect.WasPressedThisFrame();
            if (scripted && fireDown) _scriptShots--;

            _equip = Mathf.MoveTowards(_equip, 1f, dt / 0.3f);

            // Reload: the magazine counts as full near the end of the motion.
            if (_reloading)
            {
                float u = ReloadProgress;
                if (!_refilled && u >= _refillAt)
                {
                    _localAmmo = MagazineSize;
                    _refilled = true;
                }
                if (Time.time - _reloadStart >= _reloadTime) _reloading = false;
            }

            // Aim down the sights.
            bool aiming = aimHeld && !_reloading && _equip > 0.6f;
            _aim = Mathf.SmoothDamp(_aim, aiming ? 1f : 0f, ref _aimVelocity, Mathf.Max(0.03f, Sight.AimTime), Mathf.Infinity, dt);
            if (aiming != _wasAiming) PlayLocal(aiming ? ProceduralAudio.AimIn : ProceduralAudio.AimOut, 0.35f);
            _wasAiming = aiming;
            if (holder.Look != null) holder.Look.SetZoom(Sight.AimFov, Ease(_aim));
            bool sprinting = motor != null && motor.IsSprinting && motor.IsGrounded && motor.HorizontalSpeed > 1.5f;
            _sprint = Mathf.MoveTowards(_sprint, sprinting && !aiming && !_reloading ? 1f : 0f, dt * 5f);

            if (inspect && !_reloading && _aim < 0.1f) _inspectStart = Time.time;
            if (fireHeld || aiming || _reloading) _inspectStart = -10f;

            // Trigger.
            bool trigger = fireDown || (_fullAuto && fireHeld);
            if ((trigger || _queued) && _equip > 0.8f && _sprint < 0.5f)
            {
                if (Time.time >= _nextShot && !_reloading)
                {
                    _queued = false;
                    if (_localAmmo > 0) Fire(holder);
                    else if (fireDown)
                    {
                        PlayLocal(ProceduralAudio.DryFire, 0.8f);
                        _wantsReload = true;
                    }
                }
                else if (fireDown && !_reloading && _nextShot - Time.time < 0.2f)
                {
                    _queued = true; // pressed a moment early: fire as soon as it's ready
                }
            }
            if ((reload || _wantsReload) && !_reloading && _localAmmo < MagazineSize && Time.time >= _nextShot) StartReload();
            if (_wantsReload && (_reloading || _localAmmo >= MagazineSize)) _wantsReload = false;

            // Spread for the crosshair: wider moving, in the air and from the hip; the laser tightens the hip.
            float hip = _hipSpread * (HasLaser ? 0.7f : 1f);
            float moving = motor != null ? Mathf.Clamp01(motor.HorizontalSpeed / 5f) : 0f;
            float air = motor != null && !motor.IsGrounded && !motor.IsSwimming ? 1f : 0f;
            CurrentSpread = Mathf.Lerp(hip, _aimSpread, Ease(_aim)) * (1f + moving * 0.6f + air);

            // Sway with the mouse (much less when aiming), a slow breath while aiming.
            Vector2 look = canAct ? GameInput.LookMouse.ReadValue<Vector2>() : Vector2.zero;
            float swayScale = Mathf.Lerp(1f, Sight.AimSway, _aim);
            Vector2 swayTarget = Vector2.ClampMagnitude(-look * (0.0012f * swayScale), 0.035f);
            _sway = Vector2.Lerp(_sway, swayTarget, 1f - Mathf.Exp(-10f * dt));

            // Pull the gun back from walls instead of pushing it through them.
            float wallTarget = 0f;
            Transform cam = ViewOf(holder);
            if (Physics.SphereCast(cam.position, 0.07f, cam.forward, out RaycastHit wallHit, 0.95f, ~0, QueryTriggerInteraction.Ignore) &&
                wallHit.collider != holder.BodyCollider && !_item.OwnsCollider(wallHit.collider))
                wallTarget = Mathf.Clamp(0.95f - wallHit.distance, 0f, 0.45f);
            _wall = Mathf.SmoothDamp(_wall, wallTarget, ref _wallVelocity, 0.06f, Mathf.Infinity, dt);

            StepSprings(dt);
            bool scoped = Sight.Scope && _aim > 0.9f;
            if (scoped != IsScopedIn) SetScoped(scoped);
        }

        private void StepSprings(float dt)
        {
            float stiff = Mathf.Lerp(1f, 1.4f, _aim);
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt * 240f), 1, 24);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                _kickPositionVelocity += (-_kickPosition * (_springPosition * stiff) - _kickPositionVelocity * _dampPosition) * h;
                _kickPosition += _kickPositionVelocity * h;
                _kickRotationVelocity += (-_kickRotation * (_springRotation * stiff) - _kickRotationVelocity * _dampRotation) * h;
                _kickRotation += _kickRotationVelocity * h;
            }
        }

        private void SetScoped(bool scoped)
        {
            IsScopedIn = scoped;
            bool hide = scoped || _item.IsStowed;
            foreach (Renderer r in _renderers)
                if (r != null) r.forceRenderingOff = hide;
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        private static Transform ViewOf(PlayerHub holder) =>
            holder.Look != null && holder.Look.Camera != null ? holder.Look.Camera.transform : holder.Head;

        // ------------------------------------------------------------------ holding (IHoldPose)

        public void GetHoldPose(PlayerHub holder, out Vector3 offset, out Quaternion rotation, out float pitchFollow)
        {
            pitchFollow = 1f;
            Vector3 hip = _item.HoldOffset;
            Quaternion baseRotation = _item.HoldRotation;
            if (Local != this || holder != PlayerHub.Local)
            {
                offset = hip;
                rotation = baseRotation;
                return;
            }

            // Aimed: the sight's eye point sits straight ahead of the eye.
            Vector3 aimed = hip;
            WeaponSight sight = Sight;
            if (sight.EyePoint != null)
                aimed = new Vector3(0f, 0f, sight.EyeDistance) - baseRotation * transform.InverseTransformPoint(sight.EyePoint.position);
            float a = Ease(_aim);
            Vector3 position = Vector3.Lerp(hip, aimed, a);
            Quaternion rot = baseRotation;

            // Raise it into view when taken out; lower and turn it while sprinting.
            float down = 1f - Ease(_equip);
            position += new Vector3(0.02f, -0.22f, -0.08f) * down;
            float sprint = Ease(_sprint);
            position += _sprintOffset * sprint;
            rot = Quaternion.Euler(_sprintEuler * sprint + new Vector3(35f * down, 0f, 0f)) * rot;

            // Reload: tip it over to the side and bring it in, then back.
            if (_reloading)
            {
                float u = ReloadProgress;
                float r = Mathf.Sin(Mathf.Clamp01(u * 1.15f) * Mathf.PI);
                r = Mathf.Sqrt(Mathf.Max(0f, r));
                position += new Vector3(-0.04f, -0.06f, -0.06f) * r;
                rot = Quaternion.Euler(12f * r, 8f * r, 40f * r) * rot;
            }

            // Inspect: turn it to look at the side, then the other side.
            float since = Time.time - _inspectStart;
            if (since < InspectSeconds)
            {
                float u = since / InspectSeconds;
                float show = Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI);
                float turn = Mathf.Lerp(-55f, 35f, Ease(Mathf.Clamp01((u - 0.15f) / 0.7f)));
                position += new Vector3(-0.12f, 0.05f, -0.1f) * show;
                rot = Quaternion.Euler(-10f * show, turn * show, -25f * show) * rot;
            }

            // Too close to a wall: pull back and point down.
            position += new Vector3(0f, -0.05f, -1f) * _wall;
            rot = Quaternion.Euler(60f * _wall, 0f, 0f) * rot;

            // Sway and recoil.
            position += new Vector3(_sway.x, _sway.y, 0f) + _kickPosition;
            rot = Quaternion.Euler(_kickRotation + new Vector3(_sway.y * 250f, -_sway.x * 250f, _sway.x * 400f)) * rot;

            offset = position;
            rotation = rot;
        }

        // ------------------------------------------------------------------ firing

        public void Use(PlayerHub holder)
        {
            if (Local == this) _scriptShots++;
        }

        private void Fire(PlayerHub holder)
        {
            Transform view = ViewOf(holder);
            Vector3 origin = view.position, forward = view.forward;
            int seed = Random.Range(1, int.MaxValue);
            float spread = CurrentSpread;
            Vector3[] directions = ProjectileSystem.SpreadDirections(forward, spread, seed, _pellets);
            ProjectileSystem.Instance.Fire(this, holder, true, origin, Muzzle.position, directions, _speed, _gravity, _projectile, Damage, 0f, _range);

            _localAmmo--;
            _nextShot = Time.time + _interval;
            FireEffects();

            // Recoil: the view climbs for good; the gun jumps on its springs and settles.
            WeaponBarrel barrel = Barrel;
            float scale = Mathf.Lerp(1f, _aimRecoil, Ease(_aim));
            if (holder.Look != null)
                holder.Look.AddRecoil(new Vector2(Random.Range(-_climb.x, _climb.x), _climb.y) * (barrel.ClimbScale * scale));
            float side = Random.Range(-1f, 1f);
            _kickPosition += new Vector3(side * _kick.x, _kick.y, -_kick.z) * (barrel.KickScale * scale);
            _kickRotation += new Vector3(-_kickTurn.y, side * _kickTurn.x, -side * _kickTurn.x) * (barrel.KickScale * scale);
            if (_shooterPush > 0f && holder.Motor != null) holder.Motor.AddImpulse(-forward * _shooterPush);
            holder.Gesture(AvatarGesture.Shoot);
            if (_localAmmo <= 0) _wantsReload = true;

            ShootServer(TimeManager.Tick, origin, forward, seed, spread);
        }

        [ServerRpc(RequireOwnership = false)]
        private void ShootServer(uint tick, Vector3 origin, Vector3 forward, int seed, float spread, NetworkConnection caller = null)
        {
            PlayerHub holder = _item.Holder;
            if (holder == null || holder.Owner != caller) return;
            if (Time.time - _lastServerShot < _interval * 0.6f || _ammo.Value == 0) return;
            if ((origin - holder.Head.position).sqrMagnitude > 2.5f * 2.5f) return;
            _lastServerShot = Time.time;
            _ammo.Value--;
            _hitBudget = Mathf.Min(_hitBudget + _pellets, _pellets * 3);
            ShotObservers(tick, origin, forward.normalized, seed, Mathf.Clamp(spread, 0f, _hipSpread * 3f), caller.ClientId);
        }

        [ObserversRpc]
        private void ShotObservers(uint tick, Vector3 origin, Vector3 forward, int seed, float spread, int shooterId)
        {
            if (LocalConnection != null && LocalConnection.ClientId == shooterId) return; // fired already
            float late = (float)TimeManager.TicksToTime(TimeManager.Tick > tick ? TimeManager.Tick - tick : 0u);
            Vector3[] directions = ProjectileSystem.SpreadDirections(forward, spread, seed, _pellets);
            ProjectileSystem.Instance.Fire(this, _item.Holder, false, origin, Muzzle.position, directions, _speed, _gravity, _projectile, Damage, late, _range);
            FireEffects();
        }

        private void FireEffects()
        {
            WeaponBarrel barrel = Barrel;
            Transform muzzle = Muzzle;
            ProjectileSystem.Instance.MuzzleFlash(muzzle.position, muzzle.forward, _flashSize, barrel.Suppressed);
            if (_ejectPort != null) StartCoroutine(Eject(_sound == GunSound.Shotgun ? 0.32f : 0.02f));
            if (_audio != null)
            {
                _audio.pitch = Random.Range(0.94f, 1.06f);
                _audio.PlayOneShot(barrel.Suppressed ? ProceduralAudio.GunshotSuppressed : ProceduralAudio.Shot(_sound), barrel.Suppressed ? 0.7f : 1f);
            }
            if (_sound == GunSound.Shotgun || _sound == GunSound.Sniper) StartCoroutine(Cycle());
            else _slideKick = 1f;
        }

        private IEnumerator Eject(float delay)
        {
            if (delay > 0.03f) yield return new WaitForSeconds(delay);
            if (_ejectPort != null) ProjectileSystem.Instance.EjectCasing(_ejectPort.position, _ejectPort.right, _ejectPort.up, _sound == GunSound.Shotgun);
        }

        /// <summary>Pump or bolt: work the action a moment after the shot.</summary>
        private IEnumerator Cycle()
        {
            yield return new WaitForSeconds(Mathf.Min(0.25f, _interval * 0.35f));
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Rack, 0.6f);
            _slideKick = 1f;
        }

        // ------------------------------------------------------------------ hits

        /// <summary>One of our own projectiles hit something (shooter's machine only).</summary>
        internal void OnProjectileHit(RaycastHit hit, Vector3 direction, int damage)
        {
            PlayerHub holder = _item.Holder;
            if (DamageUtil.Find(hit.collider) is Component target && target.TryGetComponent(out NetworkObject nob))
            {
                HitServer(nob, hit.point, direction);
                WeaponHud.ShowHitMarker();
            }
            else if (hit.collider.GetComponentInParent<PlayerHub>() is { } player && player != holder)
            {
                HitPlayerServer(player, direction);
                WeaponHud.ShowHitMarker();
            }
            else if (Item.FromCollider(hit.collider) is { } item && !item.IsHeld)
            {
                // Knock loose things (and people lying about) around: we take over their physics for a moment.
                item.Sync.RequestAuthority();
                if (!item.Sync.Body.isKinematic)
                    item.Sync.Body.AddForceAtPosition(direction * (_force * Mathf.Max(0.3f, damage / 30f)), hit.point, ForceMode.VelocityChange);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void HitServer(NetworkObject target, Vector3 point, Vector3 direction, NetworkConnection caller = null)
        {
            PlayerHub holder = _item.Holder;
            if (target == null || holder == null || holder.Owner != caller || _hitBudget <= 0) return;
            if ((target.transform.position - holder.transform.position).sqrMagnitude > (_range + 5f) * (_range + 5f)) return;
            _hitBudget--;
            if (target.GetComponent<IDamageable>() is { } damageable)
                damageable.ServerTakeHit(Damage, DamageKind.Bullet, holder, point, direction.normalized);
        }

        [ServerRpc(RequireOwnership = false)]
        private void HitPlayerServer(PlayerHub target, Vector3 direction, NetworkConnection caller = null)
        {
            PlayerHub holder = _item.Holder;
            if (target == null || holder == null || holder.Owner != caller || _hitBudget <= 0) return;
            _hitBudget--;
            // No health on players: a hit knocks them back, harder for heavier rounds.
            Vector3 flat = new Vector3(direction.x, 0f, direction.z).normalized;
            if (target.TryGetComponent(out PlayerCombat combat))
                combat.ServerKnockback(flat * Mathf.Clamp(Damage / 12f, 1.5f, 7f) + Vector3.up * 1.5f, holder.DisplayName);
        }

        // ------------------------------------------------------------------ reload

        private void StartReload()
        {
            _reloading = true;
            _refilled = false;
            _reloadStart = Time.time;
            _wantsReload = _queued = false;
            _inspectStart = -10f;
            if (_reloadSounds != null) StopCoroutine(_reloadSounds);
            _reloadSounds = StartCoroutine(ReloadSounds());
            ReloadServer();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ReloadServer(NetworkConnection caller = null)
        {
            PlayerHub holder = _item.Holder;
            if (holder == null || holder.Owner != caller) return;
            _ammo.Value = (byte)MagazineSize;
            ReloadObservers(caller.ClientId);
        }

        [ObserversRpc]
        private void ReloadObservers(int shooterId)
        {
            if (LocalConnection != null && LocalConnection.ClientId == shooterId) return;
            StartCoroutine(ReloadSounds());
        }

        private IEnumerator ReloadSounds()
        {
            yield return new WaitForSeconds(_reloadTime * 0.15f);
            PlayLocal(ProceduralAudio.MagOut, 0.7f);
            yield return new WaitForSeconds(_reloadTime * 0.45f);
            PlayLocal(ProceduralAudio.MagIn, 0.8f);
            yield return new WaitForSeconds(_reloadTime * 0.22f);
            PlayLocal(ProceduralAudio.Rack, 0.6f);
            _slideKick = 1f;
        }

        private void PlayLocal(AudioClip clip, float volume)
        {
            if (_audio != null && clip != null) _audio.PlayOneShot(clip, volume);
        }

        // ------------------------------------------------------------------ parts

        private void ApplyParts()
        {
            for (int i = 0; i < _sights.Length; i++)
                if (_sights[i].Model != null) _sights[i].Model.SetActive(i == _sight.Value);
            for (int i = 0; i < _barrels.Length; i++)
                if (_barrels[i].Model != null) _barrels[i].Model.SetActive(i == _barrel.Value);
            if (_laser != null) _laser.SetActive(_laserOn.Value);
            if (_extendedMagazineModel != null) _extendedMagazineModel.SetActive(_extended.Value);
            if (_magazineModel != null) _magazineModel.SetActive(!_extended.Value || _extendedMagazineModel == null);
            if (IsScopedIn) SetScoped(true); // newly shown parts hide too
        }

        /// <summary>What the shop can sell for this gun (and what's fitted).</summary>
        public List<PartOffer> Offers()
        {
            var list = new List<PartOffer>();
            for (int i = 1; i < _sights.Length; i++)
                list.Add(new PartOffer { Kind = PartKind.Sight, Index = (byte)i, Name = _sights[i].Name, Price = _sights[i].Price, Fitted = _sight.Value == i });
            for (int i = 1; i < _barrels.Length; i++)
                list.Add(new PartOffer { Kind = PartKind.Barrel, Index = (byte)i, Name = _barrels[i].Name, Price = _barrels[i].Price, Fitted = _barrel.Value == i });
            if (_laser != null)
                list.Add(new PartOffer { Kind = PartKind.Laser, Name = "Laser sight", Price = _laserPrice, Fitted = _laserOn.Value });
            if (_extendedMagazine > _magazine)
                list.Add(new PartOffer { Kind = PartKind.Magazine, Name = $"Extended magazine ({_extendedMagazine})", Price = _extendedMagazinePrice, Fitted = _extended.Value });
            int next = _tier.Value + 1;
            if (next < _tiers.Length)
                list.Add(new PartOffer { Kind = PartKind.Rounds, Index = (byte)next, Name = $"{_tiers[next].Name} ({_tiers[next].Damage} damage)", Price = _tiers[next].Price });
            return list;
        }

        public int PriceOf(PartKind kind, byte index) => kind switch
        {
            PartKind.Sight => index < _sights.Length ? _sights[index].Price : -1,
            PartKind.Barrel => index < _barrels.Length ? _barrels[index].Price : -1,
            PartKind.Laser => _laser != null ? _laserPrice : -1,
            PartKind.Magazine => _extendedMagazine > _magazine ? _extendedMagazinePrice : -1,
            PartKind.Rounds => index == _tier.Value + 1 && index < _tiers.Length ? _tiers[index].Price : -1,
            _ => -1
        };

        public bool IsFitted(PartKind kind, byte index) => kind switch
        {
            PartKind.Sight => _sight.Value == index,
            PartKind.Barrel => _barrel.Value == index,
            PartKind.Laser => _laserOn.Value,
            PartKind.Magazine => _extended.Value,
            PartKind.Rounds => _tier.Value >= index,
            _ => false
        };

        /// <summary>Host: fit a part (the shop has taken the money).</summary>
        [Server]
        public void ServerFit(PartKind kind, byte index)
        {
            switch (kind)
            {
                case PartKind.Sight: if (index < _sights.Length) _sight.Value = index; break;
                case PartKind.Barrel: if (index < _barrels.Length) _barrel.Value = index; break;
                case PartKind.Laser: _laserOn.Value = true; break;
                case PartKind.Magazine: _extended.Value = true; break;
                case PartKind.Rounds: if (index < _tiers.Length) _tier.Value = index; break;
            }
        }

        /// <summary>Host: fit a part by (part of) its name, for the 'fit' test command. False if none matches.</summary>
        [Server]
        public bool ServerFitByName(string name)
        {
            foreach (PartOffer offer in Offers())
                if (offer.Name.ToLowerInvariant().Contains(name.ToLowerInvariant()))
                {
                    ServerFit(offer.Kind, offer.Index);
                    return true;
                }
            return false;
        }

        private void UpdateLaser()
        {
            bool on = _laser != null && _laserOn.Value && _laser.activeInHierarchy && _item.IsHeld && !_item.IsStowed && !IsScopedIn;
            if (!on)
            {
                if (_laserBeam != null) _laserBeam.enabled = false;
                if (_laserDot != null) _laserDot.gameObject.SetActive(false);
                return;
            }
            if (_laserBeam == null)
            {
                _laserBeam = new GameObject("LaserBeam").AddComponent<LineRenderer>();
                _laserBeam.transform.SetParent(transform, false);
                _laserBeam.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
                _laserBeam.startColor = new Color(1f, 0.15f, 0.1f, 0.55f);
                _laserBeam.endColor = new Color(1f, 0.15f, 0.1f, 0.1f);
                _laserBeam.widthMultiplier = 0.004f;
                _laserBeam.positionCount = 2;
                _laserBeam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(dot.GetComponent<Collider>());
                dot.name = "LaserDot";
                dot.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.1f, 0.05f) };
                dot.transform.localScale = Vector3.one * 0.025f;
                _laserDot = dot.transform;
            }
            Transform origin = _laser.transform;
            Vector3 end = origin.position + origin.forward * 40f;
            int count = Physics.RaycastNonAlloc(origin.position, origin.forward, _hits, 40f, ~0, QueryTriggerInteraction.Ignore);
            float best = 40f;
            bool found = false;
            PlayerHub holder = _item.Holder;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (h.distance >= best || _item.OwnsCollider(h.collider) || (holder != null && h.collider == holder.BodyCollider)) continue;
                best = h.distance;
                end = h.point;
                found = true;
            }
            _laserBeam.enabled = true;
            _laserBeam.SetPosition(0, origin.position);
            _laserBeam.SetPosition(1, end);
            _laserDot.gameObject.SetActive(found);
            _laserDot.position = end;
        }
    }
}
