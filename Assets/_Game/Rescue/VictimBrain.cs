using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Combat;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;
using Random = UnityEngine.Random;

namespace PleaseDontDrown.Rescue
{
    /// <summary>Things everyone should see or hear about a tourist (sent by the host).</summary>
    public enum VictimEvent : byte
    {
        InTrouble, Panicking, GoingUnder, Unconscious, Calmed, Saved, Revived, Lost, SelfRescue,
        Flatline, Zapped, Bitten, InjuredAshore, Hospitalized, Collapsed,
        /// <summary>A rival company's lifeguard got to them first (island 4): lost, like <see cref="Lost"/>, and billed.</summary>
        TakenByRival
    }

    /// <summary>How a tourist behaves in trouble (story beats and island difficulty set this; drills use the default).</summary>
    [Serializable]
    public struct TouristProfile
    {
        [Tooltip("-1 decided by the seed, 0 man, 1 woman.")]
        public int Figure;
        [Tooltip("Seconds from 'help!' to passing out if nobody holds them. 0 = the classic panic/air model.")]
        public float SecondsToUnconscious;
        [Tooltip("Seconds of condition once unconscious (the CPR window). 0 = default.")]
        public float ConditionSeconds;
        [Tooltip("Unconscious this long = no pulse: CPR stops working, only the defibrillator helps. 0 = never.")]
        public float FlatlineAfter;
        [Tooltip("Drowns without a sound: no waving, no shouting (someone else has to raise the alarm).")]
        public bool Silent;
        [Tooltip("Seconds a shark-bite victim can bleed before they're lost.")]
        public float BleedSeconds;
        [Tooltip("Collapses once out of the water even if still awake: always needs CPR (so nobody misses that part).")]
        public bool NeedsCpr;
        public string Name;
        [Tooltip("Packed AvatarLook to wear (a beach swimmer turning into this tourist). 0 = from the seed.")]
        public ulong Look;
        [Tooltip("What they shout instead of 'help!' while in trouble, separated by '|' (story gags). Empty = 'help!'.")]
        public string Shouts;

        public static TouristProfile Default => new() { Figure = -1, BleedSeconds = 60f };
    }

    /// <summary>
    /// A tourist's mind, run by the host: panic, air, the drowning state machine, and what counts as rescued.
    ///
    ///  * Classic model (drills): in deep water panic rises (faster with the head under); past 50 they're Panicking
    ///    and dunk under now and then; air drains while panicking/drowning. Low air = Drowning, no air = Unconscious.
    ///  * Timer model (story, <see cref="TouristProfile.SecondsToUnconscious"/>): air runs out in exactly that many
    ///    seconds unless someone holds them; the state follows the air (distressed, panicking, drowning, out).
    ///  * Being held by a lifeguard or hanging onto a float calms them and lets them breathe.
    ///  * Reaching the shallows (or the dock) while conscious = Saved. Unconscious ones need CPR on land (Secondary):
    ///    chest compressions (for a man a slap in the face after every 5, and again).
    ///    Someone out too long may flatline: then only the defibrillator brings them back.
    ///  * Shark bite: a leg is gone and they bleed. On land they need a hospital bed before the bleeding runs out.
    ///  * Condition or bleeding runs out = Lost ("airlifted by the rival lifeguard company"), no hard fail.
    /// Everyone else reads the synced state; <see cref="VictimBody"/> turns it into motion on every machine.
    /// </summary>
    [RequireComponent(typeof(Item), typeof(VictimBody))]
    public class VictimBrain : NetworkBehaviour, IInteractionSecondary, IDamageable
    {
        [Header("Panic (0-100)")]
        [SerializeField] private float _panicRise = 1.2f;
        [SerializeField] private float _panicRiseHeadUnder = 6f;
        [SerializeField] private float _panicFallHeld = 30f;
        [SerializeField] private float _panicFallFloat = 12f;
        [SerializeField] private float _panickingAt = 50f;
        [SerializeField] private float _calmBelow = 30f;

        [Header("Air (0-1)")]
        [SerializeField] private float _drainPanicking = 1f / 90f;
        [SerializeField] private float _drainDrowning = 1f / 30f;
        [Tooltip("Extra drain while the head is actually under.")]
        [SerializeField] private float _drainHeadUnder = 1f / 45f;
        [SerializeField] private float _refill = 0.12f;
        [SerializeField] private float _drowningBelowAir = 0.35f;

        [Header("Unconscious")]
        [SerializeField] private float _conditionSeconds = 90f;
        [Tooltip("Condition drains this much slower while someone is doing CPR.")]
        [SerializeField] private float _cprSlowdown = 0.3f;
        [SerializeField] private float _cprDecayPerSecond = 0.04f;
        [SerializeField] private float _cprReach = 3f;
        [Header("CPR steps (revive progress per press)")]
        [SerializeField] private int _compressionsPerSet = 5;
        [SerializeField] private float _compressGain = 0.05f;
        [Tooltip("A woman gets no slap between sets: each compression brings her round this much (as fast as a man's set with its slap).")]
        [SerializeField] private float _womanCompressGain = 0.09f;
        [SerializeField] private float _punchGain = 0.2f;

        [Header("After")]
        [SerializeField] private float _celebrateSeconds = 4f;
        [Tooltip("Saved tourists sit on the sand this long, then go back to their sunbed (despawn).")]
        [SerializeField] private float _leaveAfterSeconds = 45f;

        private static readonly string[] MaleNames =
            { "Kevin", "Gary", "Chad", "Doug", "Hank", "Bernie", "Steve", "Duncan", "Otto", "Lars", "Nigel", "Rick", "Barry", "Todd" };
        private static readonly string[] FemaleNames =
            { "Brenda", "Linda", "Tina", "Rita", "Gloria", "Pam", "Marge", "Deb", "Yolanda", "Stacy", "Carla", "Jenny", "Donna", "Lola" };

        private static readonly List<VictimBrain> _all = new();

        private readonly SyncVar<string> _name = new SyncVar<string>();
        private readonly SyncVar<int> _seed = new SyncVar<int>();
        private readonly SyncVar<ulong> _look = new SyncVar<ulong>();
        private readonly SyncVar<VictimState> _state = new SyncVar<VictimState>(VictimState.Fine);
        private readonly SyncVar<float> _air = new SyncVar<float>(1f, new SyncTypeSettings(0.2f));
        private readonly SyncVar<float> _panic = new SyncVar<float>(0f, new SyncTypeSettings(0.2f));
        private readonly SyncVar<float> _condition = new SyncVar<float>(1f, new SyncTypeSettings(0.25f));
        private readonly SyncVar<float> _conditionTotal = new SyncVar<float>(90f);
        private readonly SyncVar<float> _cpr = new SyncVar<float>(0f, new SyncTypeSettings(0.1f));
        private readonly SyncVar<CprStep> _cprStep = new SyncVar<CprStep>(CprStep.Compress);
        private readonly SyncVar<byte> _cprCount = new SyncVar<byte>();
        private readonly SyncVar<bool> _ashore = new SyncVar<bool>();
        private readonly SyncVar<bool> _rescued = new SyncVar<bool>();
        private readonly SyncVar<bool> _silent = new SyncVar<bool>();
        private readonly SyncVar<bool> _flatline = new SyncVar<bool>();
        private readonly SyncVar<bool> _legLost = new SyncVar<bool>();
        private readonly SyncVar<float> _bleedLeft = new SyncVar<float>(0f, new SyncTypeSettings(0.25f));
        private readonly SyncVar<string> _shouts = new SyncVar<string>();
        private readonly SyncVar<bool> _knockedOut = new SyncVar<bool>();
        private readonly SyncVar<bool> _dead = new SyncVar<bool>();
        private int _health = 100; // (host: shots)
        private string[] _shoutLines = System.Array.Empty<string>();

        private Item _item;
        private VictimBody _body;
        private AvatarLook _appliedLook;

        // Host-only bookkeeping.
        private float _stateSince;
        private float _troubleSince = -1f;
        private float _nextSense;
        private bool _headUnder;
        private bool _deep;
        private PlayerHub _lastHolder;
        private float _lastHeldTime = float.NegativeInfinity;
        private float _lastPumpTime = float.NegativeInfinity;
        private readonly Dictionary<int, float> _lastPumpBy = new();
        private readonly List<string> _cprHelpers = new();
        private bool _autoState;
        private float _drownSeconds;       // timer model when > 0
        private float _flatlineAfter;
        private float _bleedSeconds = 60f;
        private bool _hospitalized;
        private bool _needsCpr;
        private bool _takenByRival;        // lost to a rival lifeguard (ServerTakenByRival), not to the clock

        public static IReadOnlyList<VictimBrain> All => _all;
        /// <summary>Host: a tourist was saved, revived, lost... (with whoever gets the credit, if anyone).</summary>
        public static event Action<VictimBrain, VictimEvent, PlayerHub> ServerEvent;

        public string Name => string.IsNullOrEmpty(_name.Value) ? "Tourist" : _name.Value;
        public VictimState State => _state.Value;
        public float Air01 => _air.Value;
        public float Panic01 => _panic.Value / 100f;
        public float Condition01 => _condition.Value;
        public float Cpr01 => _cpr.Value;
        public CprStep NextCprStep => _cprStep.Value;
        /// <summary>How far into the current CPR step (0 = none done yet) and how many it takes.</summary>
        public int CprCount => _cprCount.Value;
        public int CompressionsPerSet => _compressionsPerSet;
        public bool IsAshore => _ashore.Value;
        public bool HasBeenRescued => _rescued.Value;
        public float ConditionSecondsLeft => _condition.Value * _conditionTotal.Value;
        public bool IsSilent => _silent.Value;
        public bool IsFlatlined => _flatline.Value;
        public bool HasLostLeg => _legLost.Value;
        /// <summary>Knocked out in a fight on land (a bar brawler): carry them to the infirmary's bed.</summary>
        public bool IsKnockedOut => _knockedOut.Value;
        /// <summary>Shot dead: a limp body on the sand (or floating) for a while, then gone.</summary>
        public bool IsDead => _dead.Value;
        public float BleedSecondsLeft => _bleedLeft.Value;
        /// <summary>What they shout in trouble instead of "help!" (empty = the usual).</summary>
        public string[] Shouts => _shoutLines;
        public bool IsFemale => Look.Feminine;
        public AvatarLook Look => _look.Value != 0 ? AvatarLook.Unpack(_look.Value) : AvatarLook.RandomTourist(_seed.Value);
        public Item Item => _item;
        public VictimBody Body => _body;
        private string They => IsFemale ? "her" : "him";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _all.Clear();
            ServerEvent = null;
        }

        private void Awake()
        {
            _item = GetComponent<Item>();
            _body = GetComponent<VictimBody>();
            _name.OnChange += (_, next, _) =>
            {
                _item.SetDisplayName(string.IsNullOrEmpty(next) ? "Tourist" : next);
                if (!string.IsNullOrEmpty(next)) gameObject.name = $"Tourist_{next}";
            };
            _seed.OnChange += (_, _, _) => ApplyLook();
            _look.OnChange += (_, _, _) => ApplyLook();
            _state.OnChange += OnStateChanged;
            _shouts.OnChange += (_, next, _) =>
                _shoutLines = string.IsNullOrEmpty(next) ? System.Array.Empty<string>() : next.Split('|');
        }

        private void ApplyLook()
        {
            AvatarLook look = Look;
            if (_body.IsBuilt && look.Equals(_appliedLook)) return;
            _appliedLook = look;
            _body.ApplyLooks(look, _seed.Value);
        }

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _all.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _all.Remove(this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            // Spawned without ServerSetup (e.g. the 'spawn' cheat): the first update picks a name and works out
            // the state from where they are.
            _autoState = true;
            _stateSince = Time.time;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            _item.SetDisplayName(Name);
            ApplyLook();
        }

        /// <summary>Host: set up a freshly spawned tourist.</summary>
        [Server]
        public void ServerSetup(string displayName, int seed, VictimState state, float panic, float air) =>
            ServerSetup(displayName, seed, state, panic, air, TouristProfile.Default);

        /// <summary>Host: set up a freshly spawned tourist with a behaviour profile (story tourists, island difficulty).</summary>
        [Server]
        public void ServerSetup(string displayName, int seed, VictimState state, float panic, float air, TouristProfile profile)
        {
            AvatarLook look = profile.Look != 0 ? AvatarLook.Unpack(profile.Look) : AvatarLook.RandomTourist(seed, profile.Figure);
            _seed.Value = seed;
            _look.Value = look.Pack();
            _name.Value = !string.IsNullOrEmpty(profile.Name) ? profile.Name : !string.IsNullOrEmpty(displayName) ? displayName : RandomName(look.Feminine);
            _silent.Value = profile.Silent;
            _shouts.Value = profile.Shouts ?? string.Empty;
            _drownSeconds = Mathf.Max(0f, profile.SecondsToUnconscious);
            _flatlineAfter = Mathf.Max(0f, profile.FlatlineAfter);
            _bleedSeconds = profile.BleedSeconds > 0f ? profile.BleedSeconds : 60f;
            _needsCpr = profile.NeedsCpr;
            _conditionTotal.Value = profile.ConditionSeconds > 0f ? profile.ConditionSeconds : _conditionSeconds;
            _panic.Value = Mathf.Clamp(panic, 0f, 100f);
            _air.Value = Mathf.Clamp01(air);
            _condition.Value = 1f;
            _autoState = false;
            SetState(state);
            if (state.NeedsHelp()) _troubleSince = Time.time;
        }


        public static string RandomName() => RandomName(Random.value < 0.5f);
        public static string RandomName(bool female) => female ? FemaleNames[Random.Range(0, FemaleNames.Length)] : MaleNames[Random.Range(0, MaleNames.Length)];

        // ------------------------------------------------------------------ host simulation

        private void Update()
        {
            if (!IsServerInitialized) return;
            float dt = Time.deltaTime;

            Sense();
            PlayerHub holder = _item.Holder;
            if (holder != null)
            {
                _lastHolder = holder;
                _lastHeldTime = Time.time;
            }
            bool held = holder != null;
            bool onFloat = _body.HeldFloat != null;
            bool supported = held || onFloat;
            bool ashore = _ashore.Value;

            if (_autoState)
            {
                _autoState = false;
                int seed = Random.Range(1, int.MaxValue);
                _seed.Value = seed;
                AvatarLook look = AvatarLook.RandomTourist(seed);
                _look.Value = look.Pack();
                _name.Value = RandomName(look.Feminine);
                _conditionTotal.Value = _conditionSeconds;
                if (_deep)
                {
                    _troubleSince = Time.time;
                    SetState(VictimState.Distressed, VictimEvent.InTrouble);
                }
            }

            UpdateBleeding(dt);

            switch (_state.Value)
            {
                case VictimState.Fine:
                    Breathe(dt);
                    _panic.Value = Mathf.Max(0f, _panic.Value - 20f * dt);
                    if (_deep && !held)
                    {
                        _panic.Value = Mathf.Max(_panic.Value, 25f);
                        _troubleSince = Time.time;
                        SetState(VictimState.Distressed, VictimEvent.InTrouble);
                    }
                    else if (_leaveAfterSeconds > 0f && !held && Time.time - _stateSince > _leaveAfterSeconds && _troubleSince >= 0f)
                    {
                        Debug.Log($"[Victim] {Name} went back to their sunbed");
                        Despawn(gameObject);
                    }
                    break;

                case VictimState.Distressed:
                case VictimState.Panicking:
                case VictimState.Drowning:
                    if (ashore)
                    {
                        Rescued(held);
                        break;
                    }
                    if (_drownSeconds > 0f)
                    {
                        UpdateTimedDrowning(dt, supported);
                        break;
                    }
                    UpdatePanic(dt, held, onFloat);
                    UpdateAir(dt, supported);
                    UpdateStruggleState(supported);
                    break;

                case VictimState.Unconscious:
                    bool cprActive = Time.time - _lastPumpTime < 1.5f && !_flatline.Value;
                    _condition.Value = Mathf.Max(0f, _condition.Value - dt / Mathf.Max(1f, _conditionTotal.Value) * (cprActive ? _cprSlowdown : 1f));
                    if (!cprActive && _cpr.Value > 0f)
                        _cpr.Value = Mathf.Max(0f, _cpr.Value - _cprDecayPerSecond * dt);
                    if (_flatlineAfter > 0f && !_flatline.Value && Time.time - _stateSince > _flatlineAfter)
                    {
                        _flatline.Value = true;
                        _cpr.Value = 0f;
                        Announce(VictimEvent.Flatline, $"{Name} has no pulse! CPR won't work now: use the defibrillator.");
                    }
                    if (_condition.Value <= 0f)
                        Lost();
                    break;

                case VictimState.Injured:
                    Breathe(dt);
                    _panic.Value = Mathf.Max(0f, _panic.Value - 10f * dt);
                    if (_deep && !held)
                        SetState(VictimState.Panicking, VictimEvent.InTrouble); // back in deep water with one leg
                    break;

                case VictimState.Saved:
                    Breathe(dt);
                    if (Time.time - _stateSince > _celebrateSeconds)
                        SetState(VictimState.Fine);
                    break;

                case VictimState.Lost:
                    // (Taken by a rival: towed off at once. Airlifted: a moment to see it. Shot: the body stays a while.)
                    if (Time.time - _stateSince > (_dead.Value ? DeadBodySeconds : _takenByRival ? 1.2f : 3f))
                        Despawn(gameObject);
                    break;
            }
        }

        private void Sense()
        {
            _headUnder = WaterSurface.Exists && WaterSurface.DepthOf(_body.HeadPosition) > 0.1f; // mouth and nose under, not just a wave slap
            if (Time.time < _nextSense) return;
            _nextSense = Time.time + 0.2f;
            float depth = Shore.WaterDepthAt(transform.position);
            bool ashore = depth < Shore.StandDepth;
            _deep = depth > Shore.DeepDepth;
            if (_ashore.Value != ashore) _ashore.Value = ashore;
        }

        private void Breathe(float dt) => _air.Value = Mathf.Min(1f, _air.Value + _refill * dt);

        private void UpdatePanic(float dt, bool held, bool onFloat)
        {
            float p = _panic.Value;
            if (held) p -= _panicFallHeld * dt;
            else if (onFloat) p -= _panicFallFloat * dt;
            else p += (_panicRise + (_headUnder ? _panicRiseHeadUnder : 0f)) * dt;
            _panic.Value = Mathf.Clamp(p, 0f, 100f);
        }

        private void UpdateAir(float dt, bool supported)
        {
            if (supported)
            {
                Breathe(dt);
                return;
            }
            float drain = _state.Value switch
            {
                VictimState.Drowning => _drainDrowning,
                VictimState.Panicking => _drainPanicking,
                _ => 0f
            };
            if (_headUnder) drain += _drainHeadUnder;
            _air.Value = Mathf.Max(0f, _air.Value - drain * dt);
        }

        private void UpdateStruggleState(bool supported)
        {
            VictimState s = _state.Value;
            float air = _air.Value, panic = _panic.Value;
            if (air <= 0f)
            {
                PassOut();
            }
            else if (s == VictimState.Drowning)
            {
                if (supported && air > 0.6f)
                    SetState(panic >= _panickingAt ? VictimState.Panicking : VictimState.Distressed, VictimEvent.Calmed);
            }
            else if (air < _drowningBelowAir)
            {
                SetState(VictimState.Drowning, VictimEvent.GoingUnder);
            }
            else if (s == VictimState.Distressed && panic >= _panickingAt)
            {
                SetState(VictimState.Panicking, VictimEvent.Panicking);
            }
            else if (s == VictimState.Panicking && panic < _calmBelow)
            {
                SetState(VictimState.Distressed, VictimEvent.Calmed);
            }
        }

        /// <summary>
        /// Story timer: out of air in exactly <see cref="_drownSeconds"/> unless someone holds them (or they hang on
        /// a float), and the state follows the air: needs help, panicking, going under, out cold.
        /// </summary>
        private void UpdateTimedDrowning(float dt, bool supported)
        {
            float air = _air.Value;
            air = supported ? Mathf.Min(1f, air + dt / (_drownSeconds * 0.5f)) : Mathf.Max(0f, air - dt / _drownSeconds);
            _air.Value = air;
            _panic.Value = supported ? Mathf.Max(0f, _panic.Value - _panicFallHeld * dt) : Mathf.Max(_panic.Value, (1f - air) * 100f);
            if (air <= 0f)
            {
                PassOut();
                return;
            }
            VictimState target = air > 0.66f ? VictimState.Distressed : air > 0.33f ? VictimState.Panicking : VictimState.Drowning;
            if (supported && _state.Value > target) target = _state.Value; // don't flip-flop while being towed
            if (target == _state.Value) return;
            VictimEvent e = target > _state.Value ? target == VictimState.Drowning ? VictimEvent.GoingUnder : VictimEvent.Panicking : VictimEvent.Calmed;
            if (_silent.Value && e != VictimEvent.Calmed) SetState(target); // no fuss from the quiet ones
            else SetState(target, e);
        }

        private void PassOut() => PassOut(VictimEvent.Unconscious, null);

        private void PassOut(VictimEvent e, string text, PlayerHub credit = null)
        {
            _air.Value = 0f;
            _condition.Value = 1f;
            _cpr.Value = 0f;
            _cprStep.Value = CprStep.Compress;
            _cprCount.Value = 0;
            _flatline.Value = false;
            _cprHelpers.Clear();
            SetState(VictimState.Unconscious, e, text, credit);
        }

        private void UpdateBleeding(float dt)
        {
            if (!_legLost.Value || _hospitalized || _state.Value is VictimState.Lost) return;
            _bleedLeft.Value = Mathf.Max(0f, _bleedLeft.Value - dt);
            if (_bleedLeft.Value <= 0f)
            {
                SetState(VictimState.Lost, VictimEvent.Lost, $"{Name} lost too much blood and was airlifted by the rival lifeguard company.");
                Debug.Log($"[Victim] {Name} bled out");
            }
        }

        private void Rescued(bool held)
        {
            // Credit whoever brought them in (holding them now, or let go of them moments ago).
            PlayerHub rescuer = held ? _item.Holder : Time.time - _lastHeldTime < 8f ? _lastHolder : null;
            // Kicked in on a thrown ring: whoever threw it saved them.
            bool byRing = false;
            if (rescuer == null && _body.HeldFloat != null)
            {
                Item ring = _body.HeldFloat.Item;
                if (ring.LastHolder != null && Time.time - ring.ReleasedAt < 120f)
                {
                    rescuer = ring.LastHolder;
                    byRing = true;
                }
            }
            string took = _troubleSince >= 0f ? FormatTime(Time.time - _troubleSince) : "?";
            _panic.Value = 0f;
            if (_legLost.Value)
            {
                // Out of the water but still bleeding: they need a hospital bed.
                SetState(VictimState.Injured, VictimEvent.InjuredAshore, $"{Name} is out of the water but bleeding badly. Carry {They} to the infirmary!", rescuer);
                return;
            }
            if (_needsCpr)
            {
                // Swallowed half the sea: out cold the moment they're out of it. Whoever carries them keeps them.
                _needsCpr = false;
                PassOut(VictimEvent.Collapsed, $"{Name} swallowed half the sea and collapsed! Lay {They} on the sand and do CPR!", rescuer);
                return;
            }
            if (held) _item.ServerForceDrop(); // put down in the shallows
            if (rescuer != null)
            {
                SetState(VictimState.Saved, VictimEvent.Saved, byRing ? $"{Name} rode {rescuer.DisplayName}'s ring all the way in!" : $"{Name} was saved by {rescuer.DisplayName}!", rescuer);
                Debug.Log($"[Victim] {Name} saved by {rescuer.DisplayName} after {took}");
            }
            else
            {
                string how = _body.HeldFloat != null ? " hanging onto a float" : "";
                SetState(VictimState.Saved, VictimEvent.SelfRescue, $"{Name} made it back{how}.");
                Debug.Log($"[Victim] {Name} made it back on their own{how} after {took}");
            }
        }

        private void Lost()
        {
            SetState(VictimState.Lost, VictimEvent.Lost, $"{Name} was airlifted by the rival lifeguard company.");
            Debug.Log($"[Victim] {Name} lost (condition ran out)");
        }

        /// <summary>
        /// Host: a rival lifeguard (<paramref name="rival"/>: their name) grabbed this tourist before the team did. They
        /// are lost to the competition (<see cref="VictimEvent.TakenByRival"/>: "LOST TO COMPETITION" over them, the
        /// toast, the chapter report's lost count, the company's bill) and towed off (gone in a moment). False if they
        /// can't be taken any more (already saved, already lost, being carried in).
        /// </summary>
        [Server]
        public bool ServerTakenByRival(string rival)
        {
            if (_state.Value is VictimState.Lost or VictimState.Saved || _item.IsHeld || _ashore.Value) return false;
            SetState(VictimState.Lost, VictimEvent.TakenByRival, $"{rival} grabbed {Name}: lost to the competition.");
            Debug.Log($"[Victim] {Name} taken by the rival lifeguard {rival}");
            return true;
        }

        private void SetState(VictimState next, VictimEvent? announce = null, string text = null, PlayerHub credit = null)
        {
            if (_state.Value == next) return;
            Debug.Log($"[Victim] {Name}: {_state.Value} -> {next} (air {_air.Value:P0}, panic {_panic.Value:F0})");
            _state.Value = next;
            _takenByRival = announce == VictimEvent.TakenByRival;
            if (next == VictimState.Saved) _rescued.Value = true;
            _stateSince = Time.time;
            if (announce.HasValue)
                Announce(announce.Value, text ?? string.Empty, credit);
        }

        private void Announce(VictimEvent e, string text, PlayerHub credit = null)
        {
            EventObservers(e, text ?? string.Empty);
            ServerEvent?.Invoke(this, e, credit);
        }

        private static string FormatTime(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

        // ------------------------------------------------------------------ CPR

        public bool CanSecondary(PlayerHub player) => _state.Value == VictimState.Unconscious && _ashore.Value && !_item.IsHeld;

        public string GetSecondaryPrompt(PlayerHub player)
        {
            if (_flatline.Value) return $"<color=#ff7060>No pulse!</color> Use the defibrillator on {Name}";
            // Just the step: the CPR panel on the right (UI.RescueHud) counts them and shows how far along it is.
            return _cprStep.Value switch
            {
                CprStep.Punch => $"Slap {Name} awake!",
                _ => "Chest compressions"
            };
        }

        /// <summary>Each CPR step has its own button: pumps on Secondary, the slap on Primary.</summary>
        public UnityEngine.InputSystem.InputAction SecondaryAction(PlayerHub player) => _flatline.Value ? GameInput.Secondary : KeyOf(_cprStep.Value);

        public static UnityEngine.InputSystem.InputAction KeyOf(CprStep step) =>
            step == CprStep.Punch ? GameInput.Primary : GameInput.Secondary;

        private static readonly string[] Counts = { "1", "2", "3", "4", "5", "6", "7", "8", "9" };

        public void OnSecondary(PlayerHub player)
        {
            if (_flatline.Value)
            {
                PlayerHud.ShowToast($"{Name} has no pulse. Bring the <b>defibrillator</b>, put it down next to them and press E on it.", 3f, ToastKind.Warning);
                return;
            }
            CprStep step = _cprStep.Value;
            ShowCpr(player, step); // feel it right away; the host counts it
            CprServer(step);
        }

        /// <summary>For tests: the next CPR press as the local player.</summary>
        public void RequestPump() => OnSecondary(PlayerHub.Local);

        private void ShowCpr(PlayerHub player, CprStep step)
        {
            switch (step)
            {
                case CprStep.Punch:
                    // An open hand across the face, from the slapper's right to their left.
                    _body.Punched(player != null && player.Head != null ? -player.Head.right : Vector3.zero);
                    if (player != null) player.ShowCprAction(step, _body.HeadPosition);
                    break;
                default:
                    _body.Pump();
                    // Counted out loud over the chest, so everyone can see the compressions happening.
                    FloatingText.Spawn(_body.ChestPoint + Vector3.up * 0.45f, Counts[Mathf.Clamp(_cprCount.Value, 0, Counts.Length - 1)], new Color(1f, 1f, 1f, 0.95f), 0.9f, 0.55f);
                    if (player != null) player.ShowPump(_body.PressPoint, _body.HeadPosition, _body.KneelReach, _body.SideClearToward(player.transform.position));
                    break;
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void CprServer(CprStep step, NetworkConnection caller = null)
        {
            if (_state.Value != VictimState.Unconscious || !_ashore.Value || _item.IsHeld || caller == null || _flatline.Value) return;
            if (step != _cprStep.Value) return; // stale press (someone else already moved on)
            PlayerHub pumper = PlayerOf(caller);
            if (pumper == null || (pumper.transform.position - transform.position).sqrMagnitude > _cprReach * _cprReach) return;
            float minGap = step == CprStep.Compress ? 0.12f : 0.45f;
            if (_lastPumpBy.TryGetValue(caller.ClientId, out float last) && Time.time - last < minGap) return;
            _lastPumpBy[caller.ClientId] = Time.time;

            _lastPumpTime = Time.time;
            if (!_cprHelpers.Contains(pumper.DisplayName)) _cprHelpers.Add(pumper.DisplayName);
            float gain = step == CprStep.Punch ? _punchGain : IsFemale ? _womanCompressGain : _compressGain;
            _cpr.Value = Mathf.Min(1f, _cpr.Value + gain);
            AdvanceCprStep(step);
            CprObservers(step, caller.ClientId);
            if (_cpr.Value < 1f) return;

            string took = _troubleSince >= 0f ? FormatTime(Time.time - _troubleSince) : "?";
            Revive(pumper, VictimEvent.Revived, $"{Name} was revived! CPR by {string.Join(" & ", _cprHelpers)}");
            Debug.Log($"[Victim] {Name} revived by {string.Join(", ", _cprHelpers)} after {took}");
        }

        /// <summary>5 compressions, then (men) a slap, and round again.</summary>
        private void AdvanceCprStep(CprStep done)
        {
            int count = _cprCount.Value + 1;
            int needed = done == CprStep.Compress ? _compressionsPerSet : 1;
            if (count < needed)
            {
                _cprCount.Value = (byte)count;
                return;
            }
            _cprCount.Value = 0;
            _cprStep.Value = done == CprStep.Compress && !IsFemale ? CprStep.Punch : CprStep.Compress;
        }

        private void Revive(PlayerHub by, VictimEvent e, string text)
        {
            _air.Value = 0.4f;
            _panic.Value = 0f;
            _flatline.Value = false;
            _cpr.Value = 0f;
            _cprStep.Value = CprStep.Compress;
            _cprCount.Value = 0;
            SetState(_legLost.Value && !_hospitalized ? VictimState.Injured : VictimState.Saved, e, text, by);
        }

        [ObserversRpc]
        private void CprObservers(CprStep step, int pumperId)
        {
            if (LocalConnection != null && LocalConnection.ClientId == pumperId) return; // already shown locally
            PlayerHub pumper = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p.OwnerId == pumperId) pumper = p;
            ShowCpr(pumper, step);
        }

        private static PlayerHub PlayerOf(NetworkConnection conn)
        {
            foreach (PlayerHub p in PlayerHub.All)
                if (p.Owner == conn) return p;
            return null;
        }

        // ------------------------------------------------------------------ defibrillator, shark, hospital (host)

        /// <summary>Host: electric shock. Brings back anyone unconscious out of deep water, and is the only thing that works after a flatline.</summary>
        [Server]
        public bool ServerDefib(PlayerHub by)
        {
            if (_state.Value != VictimState.Unconscious || _deep || _item.IsHeld) return false;
            bool wasFlat = _flatline.Value;
            ZapObservers();
            Revive(by, VictimEvent.Zapped, wasFlat
                ? $"CLEAR! {Name}'s heart is beating again. Defibrillator by {(by != null ? by.DisplayName : "someone")}."
                : $"{Name} was shocked back awake by {(by != null ? by.DisplayName : "someone")}.");
            Debug.Log($"[Victim] {Name} defibrillated (flatlined: {wasFlat})");
            return true;
        }

        [ObserversRpc]
        private void ZapObservers() => _body.Zapped();

        /// <summary>Host: a shark took a leg. They bleed from now on and need the infirmary.</summary>
        [Server]
        public void ServerBite()
        {
            if (_legLost.Value || _state.Value is VictimState.Lost) return;
            _legLost.Value = true;
            _bleedLeft.Value = _bleedSeconds;
            if (_troubleSince < 0f) _troubleSince = Time.time;
            _panic.Value = 100f;
            if (_state.Value.IsConscious() && _state.Value != VictimState.Injured && !_ashore.Value)
            {
                _air.Value = Mathf.Min(_air.Value, 0.6f); // panicking, not calming down
                SetState(VictimState.Panicking);
            }
            Announce(VictimEvent.Bitten, $"A SHARK bit {Name}'s leg off! Get {They} out and rush {They} to the infirmary!");
            Debug.Log($"[Victim] {Name} bitten by a shark");
        }

        // ------------------------------------------------------------------ shot

        /// <summary>How long a shot-down tourist's body lies there before it goes.</summary>
        private const float DeadBodySeconds = 90f;

        /// <summary>
        /// Host: a bullet (or a blow). Slapstick, never gore: the first hit knocks them out cold (CPR still brings them
        /// round), enough of them and they're done for, a limp body. A hit in mid-air counts double.
        /// (The shove itself is the gun's: whoever simulates the body pushes it, Combat.Weapon.)
        /// </summary>
        public bool ServerTakeHit(int damage, DamageKind kind, PlayerHub attacker, Vector3 point, Vector3 direction)
        {
            if (_dead.Value || _state.Value == VictimState.Lost) return false;
            string by = attacker != null ? attacker.DisplayName : "someone";
            float ground = Shore.GroundHeightAt(transform.position + Vector3.up * 0.5f);
            bool wet = WaterSurface.Exists && WaterSurface.DepthOf(transform.position) > 0f;
            bool flying = !_item.IsHeld && !wet && (float.IsNaN(ground) || transform.position.y - ground > 0.9f);
            // (A hit in mid-air is the trick: it counts double.)
            _health -= Mathf.Max(1, damage) * (flying ? 2 : 1);
            ShotObservers(point, kind == DamageKind.Bullet);
            if (_health <= 0)
            {
                _dead.Value = true;
                _condition.Value = 0f;
                SetState(VictimState.Lost, VictimEvent.Lost, flying ? $"{by} shot {Name} out of the sky." : $"{by} shot {Name}. {(IsFemale ? "She" : "He")} won't be getting up again.");
                Debug.Log($"[Victim] {Name} shot dead by {by}{(flying ? " in mid-air" : "")}");
            }
            else if (_state.Value.IsConscious())
            {
                PassOut(VictimEvent.Collapsed, $"{by} shot {Name}! {(IsFemale ? "She" : "He")} is out cold: CPR, quick!", null);
                Debug.Log($"[Victim] {Name} shot by {by}{(flying ? " in mid-air" : "")}: out cold ({_health} left)");
            }
            return true;
        }

        /// <summary>Every machine: the hit heard where it landed. (No words over them: an "OW!" and a trickshot name popped
        /// up in yellow on every hit, the user, 2026-10-22: "make it more realistic, remove unnecessary things".)</summary>
        [ObserversRpc]
        private void ShotObservers(Vector3 point, bool bullet)
        {
            AudioSource.PlayClipAtPoint(bullet ? ProceduralAudio.BulletFlesh : ProceduralAudio.Punch, point, 1f);
        }

        /// <summary>Host: knocked out in a fight on land (a bar brawler): lying there, to be carried to the infirmary.</summary>
        [Server]
        public void ServerKnockedOut()
        {
            _knockedOut.Value = true;
            _ashore.Value = true;
            _autoState = false;
            SetState(VictimState.Injured, VictimEvent.InjuredAshore, $"{Name} is out cold after the bar fight. Carry {They} to the infirmary!");
        }

        /// <summary>Host: carried onto a hospital bed. Bleeding stops, they're saved.</summary>
        [Server]
        public void ServerHospitalize(Vector3 position, Quaternion rotation, PlayerHub by)
        {
            if ((!_legLost.Value && !_knockedOut.Value) || _hospitalized) return;
            _hospitalized = true;
            PlayerHub holder = _item.Holder;
            _item.ServerForceDrop();
            _body.ServerPlaceAt(position, rotation);
            _air.Value = 1f;
            _panic.Value = 0f;
            string who = by != null ? by.DisplayName : holder != null ? holder.DisplayName : "the lifeguards";
            if (_knockedOut.Value)
            {
                // He stays lying on the bed, sleeping it off, and is gone a moment later (getting up on the bed looked broken).
                _rescued.Value = true;
                Announce(VictimEvent.Hospitalized, $"{Name} is sleeping it off in the infirmary. Brought in by {who}.", by ?? holder);
                StartCoroutine(SleepItOff());
                return;
            }
            SetState(VictimState.Saved, VictimEvent.Hospitalized, $"{Name} is in the infirmary with {_bleedLeft.Value:F0}s to spare. Brought in by {who}!", by ?? holder);
        }

        private System.Collections.IEnumerator SleepItOff()
        {
            yield return new WaitForSeconds(3f);
            if (IsSpawned) Despawn(gameObject);
        }

        // ------------------------------------------------------------------ announcements (every machine)

        [ObserversRpc]
        private void EventObservers(VictimEvent e, string text)
        {
            Vector3 above = _body.HeadPosition + Vector3.up * 0.6f;
            switch (e)
            {
                case VictimEvent.Panicking:
                    if (!_silent.Value) FloatingText.Spawn(above, "PANIC!", new Color(1f, 0.55f, 0.2f), 0.9f);
                    break;
                case VictimEvent.GoingUnder:
                    PlayerHud.ShowToast($"{Name} is going under!", 4f, ToastKind.Warning);
                    break;
                case VictimEvent.Collapsed:
                    PlayerHud.ShowToast($"<color=#ff7060><b>{text}</b></color>", 5f);
                    break;
                case VictimEvent.Unconscious:
                    PlayerHud.ShowToast($"<color=#ff7060><b>{Name} is unconscious!</b></color> Get {They} onto the sand for CPR.", 5f);
                    break;
                case VictimEvent.Calmed:
                    break;
                case VictimEvent.Saved:
                case VictimEvent.SelfRescue:
                case VictimEvent.Hospitalized:
                    FloatingText.Spawn(above, "SAVED!", new Color(0.4f, 1f, 0.45f), 1.4f, 2f);
                    PlayerHud.ShowToast($"<color=#80ff80>{text}</color>", 5f);
                    _body.PlayCough();
                    Audio.GameMusic.Play(Audio.Jingle.Saved);
                    break;
                case VictimEvent.Revived:
                case VictimEvent.Zapped:
                    FloatingText.Spawn(above, e == VictimEvent.Zapped ? "ZAP! REVIVED!" : "REVIVED!", new Color(0.4f, 1f, 0.45f), 1.4f, 2f);
                    PlayerHud.ShowToast($"<color=#80ff80>{text}</color>", 5f);
                    _body.PlayCough();
                    Audio.GameMusic.Play(Audio.Jingle.Saved);
                    break;
                case VictimEvent.Flatline:
                    FloatingText.Spawn(above, "NO PULSE", new Color(1f, 0.25f, 0.25f), 1.2f, 2f);
                    PlayerHud.ShowToast($"<color=#ff7060><b>{text}</b></color>", 6f);
                    break;
                case VictimEvent.Bitten:
                    BloodFx.Bite(_body.transform.position);
                    FloatingText.Spawn(above, "CHOMP!", new Color(1f, 0.3f, 0.25f), 1.6f, 2f);
                    PlayerHud.ShowToast($"<color=#ff7060><b>{text}</b></color>", 7f);
                    break;
                case VictimEvent.InjuredAshore:
                    FloatingText.Spawn(above, "MY LEG!", new Color(1f, 0.6f, 0.3f), 1.1f, 2f);
                    PlayerHud.ShowToast($"<color=#ffb060>{text}</color>", 6f);
                    break;
                case VictimEvent.Lost:
                case VictimEvent.TakenByRival:
                    FloatingText.Spawn(above, "LOST TO COMPETITION", new Color(1f, 0.3f, 0.25f), 1.1f, 2.5f);
                    PlayerHud.ShowToast($"<color=#ff7060>{text}</color>", 5f);
                    Audio.GameMusic.Play(Audio.Jingle.Lost);
                    break;
            }
        }

        private void OnStateChanged(VictimState prev, VictimState next, bool asServer)
        {
            if (asServer) return;
            Debug.Log($"[Victim] {Name} is now {next}");
        }

        // ------------------------------------------------------------------ cheats (host)

        [Server]
        public void ServerCheat(string field, float value)
        {
            switch (field)
            {
                case "air": _air.Value = Mathf.Clamp01(value); break;
                case "panic": _panic.Value = Mathf.Clamp(value, 0f, 100f); break;
                case "condition": _condition.Value = Mathf.Clamp01(value); break;
                case "flatline": if (_state.Value == VictimState.Unconscious) { _flatline.Value = value > 0.5f; _cpr.Value = 0f; } break;
                case "bite": ServerBite(); break;
                case "state":
                    var s = (VictimState)Mathf.Clamp((int)value, 0, (int)VictimState.Injured);
                    if (s == VictimState.Unconscious) { PassOut(); return; }
                    if (s.NeedsHelp() && _troubleSince < 0f) _troubleSince = Time.time;
                    SetState(s);
                    break;
            }
        }

        public string Describe() =>
            $"{Name,-8} {(IsFemale ? "F" : "M")} {State,-11} air {Air01:P0}  panic {_panic.Value:F0}  " +
            (State == VictimState.Unconscious ? $"condition {ConditionSecondsLeft:F0}s  cpr {Cpr01:P0} next {_cprStep.Value}{(_flatline.Value ? " FLATLINE" : "")}  " : "") +
            (_legLost.Value ? $"leg lost, bleed {_bleedLeft.Value:F0}s  " : "") + (_silent.Value ? "silent  " : "") +
            $"{(IsAshore ? "ashore" : $"depth {Shore.WaterDepthAt(transform.position):F1} m")}  sim: {_item.Sync.AuthorityLabel}" +
            (_item.IsHeld ? $"  held by {_item.Holder.DisplayName}" : "") + (_body.HeldFloat != null ? "  on a float" : "") +
            (_body.DebugState.Length > 0 ? "  " + _body.DebugState : "") +
            $"  at {transform.position:F1}" +
            $"  {(transform.forward.y > 0.8f ? "on the back" : transform.forward.y < -0.8f ? "face down" : "on the side/upright")} yaw {transform.eulerAngles.y:F0}";
    }
}
