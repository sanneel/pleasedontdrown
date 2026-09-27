using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using PleaseDontDrown.World.Water;
using UnityEngine;

namespace PleaseDontDrown.Rescue
{
    /// <summary>Things everyone should see or hear about a tourist (sent by the host).</summary>
    public enum VictimEvent : byte { InTrouble, Panicking, GoingUnder, Unconscious, Calmed, Saved, Revived, Lost, SelfRescue }

    /// <summary>
    /// A tourist's mind, run by the host: panic, air, the drowning state machine, and what counts as rescued.
    ///
    ///  * In deep water panic rises (faster with the head under). Past 50 they're Panicking and dunk under now and then.
    ///  * Air drains while panicking/drowning (faster with the head under). Low air = Drowning, no air = Unconscious.
    ///  * Being held by a lifeguard or hanging onto a float calms them and lets them breathe.
    ///  * Reaching the shallows (or the dock) while conscious = Saved. Unconscious ones need CPR on land:
    ///    lifeguards pump with the Secondary button (placeholder for the M6 timing minigame) before the condition runs out.
    ///  * Condition runs out = Lost ("airlifted by the rival lifeguard company"), no hard fail.
    /// Everyone else reads the synced state; <see cref="VictimBody"/> turns it into motion on every machine.
    /// </summary>
    [RequireComponent(typeof(Item), typeof(VictimBody))]
    public class VictimBrain : NetworkBehaviour, IInteractionSecondary
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
        [SerializeField] private int _cprPumpsNeeded = 15;
        [SerializeField] private float _cprDecayPerSecond = 0.04f;
        [SerializeField] private float _cprReach = 3f;

        [Header("After")]
        [SerializeField] private float _celebrateSeconds = 4f;
        [Tooltip("Saved tourists sit on the sand this long, then go back to their sunbed (despawn).")]
        [SerializeField] private float _leaveAfterSeconds = 45f;

        private static readonly string[] Names =
        {
            "Kevin", "Brenda", "Gary", "Linda", "Chad", "Doug", "Tina", "Hank", "Rita", "Bernie",
            "Gloria", "Steve", "Pam", "Duncan", "Marge", "Otto", "Deb", "Lars", "Yolanda", "Nigel"
        };

        private static readonly List<VictimBrain> _all = new();

        private readonly SyncVar<string> _name = new SyncVar<string>();
        private readonly SyncVar<int> _seed = new SyncVar<int>();
        private readonly SyncVar<VictimState> _state = new SyncVar<VictimState>(VictimState.Fine);
        private readonly SyncVar<float> _air = new SyncVar<float>(1f, new SyncTypeSettings(0.2f));
        private readonly SyncVar<float> _panic = new SyncVar<float>(0f, new SyncTypeSettings(0.2f));
        private readonly SyncVar<float> _condition = new SyncVar<float>(1f, new SyncTypeSettings(0.25f));
        private readonly SyncVar<float> _cpr = new SyncVar<float>(0f, new SyncTypeSettings(0.1f));
        private readonly SyncVar<bool> _ashore = new SyncVar<bool>();

        private Item _item;
        private VictimBody _body;

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

        public static IReadOnlyList<VictimBrain> All => _all;
        public string Name => string.IsNullOrEmpty(_name.Value) ? "Tourist" : _name.Value;
        public VictimState State => _state.Value;
        public float Air01 => _air.Value;
        public float Panic01 => _panic.Value / 100f;
        public float Condition01 => _condition.Value;
        public float Cpr01 => _cpr.Value;
        public bool IsAshore => _ashore.Value;
        public float ConditionSecondsLeft => _condition.Value * _conditionSeconds;
        public Item Item => _item;
        public VictimBody Body => _body;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _all.Clear();

        private void Awake()
        {
            _item = GetComponent<Item>();
            _body = GetComponent<VictimBody>();
            _name.OnChange += (_, next, _) =>
            {
                _item.SetDisplayName(string.IsNullOrEmpty(next) ? "Tourist" : next);
                if (!string.IsNullOrEmpty(next)) gameObject.name = $"Tourist_{next}";
            };
            _seed.OnChange += (_, next, _) => _body.ApplyLooks(next);
            _state.OnChange += OnStateChanged;
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
            _body.ApplyLooks(_seed.Value);
        }

        /// <summary>Host: set up a freshly spawned tourist.</summary>
        [Server]
        public void ServerSetup(string displayName, int seed, VictimState state, float panic, float air)
        {
            _name.Value = displayName;
            _seed.Value = seed;
            _panic.Value = Mathf.Clamp(panic, 0f, 100f);
            _air.Value = Mathf.Clamp01(air);
            _condition.Value = 1f;
            _autoState = false;
            SetState(state);
            if (state.NeedsHelp()) _troubleSince = Time.time;
        }

        public static string RandomName() => Names[Random.Range(0, Names.Length)];

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
                _name.Value = RandomName();
                _seed.Value = Random.Range(1, int.MaxValue);
                if (_deep)
                {
                    _troubleSince = Time.time;
                    SetState(VictimState.Distressed, VictimEvent.InTrouble);
                }
            }

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
                    UpdatePanic(dt, held, onFloat);
                    UpdateAir(dt, supported);
                    UpdateStruggleState(supported);
                    break;

                case VictimState.Unconscious:
                    bool cprActive = Time.time - _lastPumpTime < 1.5f;
                    _condition.Value = Mathf.Max(0f, _condition.Value - dt / _conditionSeconds * (cprActive ? _cprSlowdown : 1f));
                    if (!cprActive && _cpr.Value > 0f)
                        _cpr.Value = Mathf.Max(0f, _cpr.Value - _cprDecayPerSecond * dt);
                    if (_condition.Value <= 0f)
                        Lost();
                    break;

                case VictimState.Saved:
                    Breathe(dt);
                    if (Time.time - _stateSince > _celebrateSeconds)
                        SetState(VictimState.Fine);
                    break;

                case VictimState.Lost:
                    if (Time.time - _stateSince > 3f)
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
                _condition.Value = 1f;
                _cpr.Value = 0f;
                _cprHelpers.Clear();
                SetState(VictimState.Unconscious, VictimEvent.Unconscious);
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

        private void Rescued(bool held)
        {
            // Credit whoever brought them in (holding them now, or let go of them moments ago).
            PlayerHub rescuer = held ? _item.Holder : Time.time - _lastHeldTime < 8f ? _lastHolder : null;
            if (held) _item.ServerForceDrop(); // put down in the shallows
            string took = _troubleSince >= 0f ? FormatTime(Time.time - _troubleSince) : "?";
            _panic.Value = 0f;
            if (rescuer != null)
            {
                SetState(VictimState.Saved, VictimEvent.Saved, $"{Name} was saved by {rescuer.DisplayName}! ({took})");
                Debug.Log($"[Victim] {Name} saved by {rescuer.DisplayName} after {took}");
            }
            else
            {
                string how = _body.HeldFloat != null ? " hanging onto a float" : "";
                SetState(VictimState.Saved, VictimEvent.SelfRescue, $"{Name} made it back{how}. ({took})");
                Debug.Log($"[Victim] {Name} made it back on their own{how} after {took}");
            }
        }

        private void Lost()
        {
            SetState(VictimState.Lost, VictimEvent.Lost, $"{Name} was airlifted by the rival lifeguard company.");
            Debug.Log($"[Victim] {Name} lost (condition ran out)");
        }

        private void SetState(VictimState next, VictimEvent? announce = null, string text = null)
        {
            if (_state.Value == next) return;
            Debug.Log($"[Victim] {Name}: {_state.Value} -> {next} (air {_air.Value:P0}, panic {_panic.Value:F0})");
            _state.Value = next;
            _stateSince = Time.time;
            if (announce.HasValue)
                EventObservers(announce.Value, text ?? string.Empty);
        }

        private static string FormatTime(float seconds) => $"{(int)seconds / 60}:{(int)seconds % 60:00}";

        // ------------------------------------------------------------------ CPR (placeholder until the M6 minigame)

        public bool CanSecondary(PlayerHub player) => _state.Value == VictimState.Unconscious && _ashore.Value && !_item.IsHeld;

        public string GetSecondaryPrompt(PlayerHub player) => $"CPR on {Name} (keep tapping)  {_cpr.Value * 100f:F0}%";

        public void OnSecondary(PlayerHub player)
        {
            _body.Pump(); // feel it right away; the host counts it
            if (player != null) player.ShowPump(_body.ChestPoint);
            CprPumpServer();
        }

        /// <summary>For tests: pump as the local player.</summary>
        public void RequestPump() => OnSecondary(PlayerHub.Local);

        [ServerRpc(RequireOwnership = false)]
        private void CprPumpServer(NetworkConnection caller = null)
        {
            if (_state.Value != VictimState.Unconscious || !_ashore.Value || _item.IsHeld || caller == null) return;
            PlayerHub pumper = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p.Owner == caller) pumper = p;
            if (pumper == null || (pumper.transform.position - transform.position).sqrMagnitude > _cprReach * _cprReach) return;
            if (_lastPumpBy.TryGetValue(caller.ClientId, out float last) && Time.time - last < 0.12f) return;
            _lastPumpBy[caller.ClientId] = Time.time;

            _lastPumpTime = Time.time;
            if (!_cprHelpers.Contains(pumper.DisplayName)) _cprHelpers.Add(pumper.DisplayName);
            _cpr.Value = Mathf.Min(1f, _cpr.Value + 1f / _cprPumpsNeeded);
            PumpObservers(caller.ClientId);
            if (_cpr.Value < 1f) return;

            string took = _troubleSince >= 0f ? FormatTime(Time.time - _troubleSince) : "?";
            _air.Value = 0.4f;
            _panic.Value = 0f;
            SetState(VictimState.Saved, VictimEvent.Revived, $"{Name} was revived! CPR by {string.Join(" & ", _cprHelpers)} ({took})");
            Debug.Log($"[Victim] {Name} revived by {string.Join(", ", _cprHelpers)} after {took}");
        }

        [ObserversRpc]
        private void PumpObservers(int pumperId)
        {
            if (LocalConnection != null && LocalConnection.ClientId == pumperId) return; // already squished locally
            _body.Pump();
            foreach (PlayerHub p in PlayerHub.All)
                if (p.OwnerId == pumperId) p.ShowPump(_body.ChestPoint);
        }

        // ------------------------------------------------------------------ announcements (every machine)

        [ObserversRpc]
        private void EventObservers(VictimEvent e, string text)
        {
            Vector3 above = _body.HeadPosition + Vector3.up * 0.6f;
            switch (e)
            {
                case VictimEvent.Panicking:
                    FloatingText.Spawn(above, "PANIC!", new Color(1f, 0.55f, 0.2f), 0.9f);
                    break;
                case VictimEvent.GoingUnder:
                    FloatingText.Spawn(above, "blub...", new Color(0.6f, 0.85f, 1f), 0.8f);
                    PlayerHud.ShowToast($"{Name} is going under!");
                    break;
                case VictimEvent.Unconscious:
                    FloatingText.Spawn(above, "!!!", new Color(1f, 0.3f, 0.25f), 1.2f);
                    PlayerHud.ShowToast($"<color=#ff7060><b>{Name} is unconscious!</b></color> Get them onto the sand for CPR.", 5f);
                    break;
                case VictimEvent.Calmed:
                    FloatingText.Spawn(above, "phew", new Color(0.7f, 1f, 0.7f), 0.7f);
                    break;
                case VictimEvent.Saved:
                case VictimEvent.SelfRescue:
                    FloatingText.Spawn(above, "SAVED!", new Color(0.4f, 1f, 0.45f), 1.4f, 2f);
                    PlayerHud.ShowToast($"<color=#80ff80>{text}</color>", 5f);
                    _body.PlayCough();
                    break;
                case VictimEvent.Revived:
                    FloatingText.Spawn(above, "REVIVED!", new Color(0.4f, 1f, 0.45f), 1.4f, 2f);
                    PlayerHud.ShowToast($"<color=#80ff80>{text}</color>", 5f);
                    _body.PlayCough();
                    break;
                case VictimEvent.Lost:
                    FloatingText.Spawn(above, "LOST TO COMPETITION", new Color(1f, 0.3f, 0.25f), 1.1f, 2.5f);
                    PlayerHud.ShowToast($"<color=#ff7060>{text}</color>", 5f);
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
                case "state":
                    var s = (VictimState)Mathf.Clamp((int)value, 0, (int)VictimState.Lost);
                    if (s == VictimState.Unconscious) { _air.Value = 0f; _condition.Value = 1f; _cpr.Value = 0f; _cprHelpers.Clear(); }
                    if (s.NeedsHelp() && _troubleSince < 0f) _troubleSince = Time.time;
                    SetState(s);
                    break;
            }
        }

        public string Describe() =>
            $"{Name,-8} {State,-11} air {Air01:P0}  panic {_panic.Value:F0}  " +
            (State == VictimState.Unconscious ? $"condition {ConditionSecondsLeft:F0}s  cpr {Cpr01:P0}  " : "") +
            $"{(IsAshore ? "ashore" : $"depth {Shore.WaterDepthAt(transform.position):F1} m")}  sim: {_item.Sync.AuthorityLabel}" +
            (_item.IsHeld ? $"  held by {_item.Holder.DisplayName}" : "") + (_body.HeldFloat != null ? "  on a float" : "") +
            (_body.DebugState.Length > 0 ? "  " + _body.DebugState : "") +
            $"  at {transform.position:F1}";
    }
}
