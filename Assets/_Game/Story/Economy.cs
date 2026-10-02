using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Core;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The team's shared wallet (host). Rescues pay automatically; Lost & Found, the shop and the story add and spend.
    /// Everyone sees "+$60" pop up where the money was earned.
    /// </summary>
    public class Economy : NetworkBehaviour
    {
        [Header("Pay per rescue")]
        [SerializeField] private int _payRescue = 60;
        [SerializeField] private int _payRevive = 80;
        [SerializeField] private int _payDefib = 150;
        [SerializeField] private int _payHospital = 200;
        [Tooltip("What the rival company bills the team for every tourist it has to airlift (never more than the team has).")]
        [SerializeField] private int _airliftBill = 30;
        [SerializeField] private AudioSource _audio;

        private readonly SyncVar<int> _money = new SyncVar<int>();

        public static Economy Instance { get; private set; }
        public static int Money => Instance != null ? Instance._money.Value : 0;
        /// <summary>Host: after every change (the story saves it).</summary>
        public static event System.Action<int> ServerChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerChanged = null;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            VictimBrain.ServerEvent += OnVictimEvent;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            VictimBrain.ServerEvent -= OnVictimEvent;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            DevCommands.Register("money", "[amount]", "Show the team's money, or add some (cheat).", args =>
            {
                if (args.Length == 0)
                {
                    DevCommands.Print($"${Money}");
                    return;
                }
                if (!DevCommands.CheatsAllowed) throw new System.InvalidOperationException("cheats are off");
                CheatServer(Mathf.RoundToInt(DevCommands.ParseFloat(args, 0)));
            }, owner: this);
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            DevCommands.Unregister("money", this);
        }

        private void OnVictimEvent(VictimBrain victim, VictimEvent e, PlayerHub credit)
        {
            int pay = e switch
            {
                VictimEvent.Saved => _payRescue,
                VictimEvent.SelfRescue => _payRescue / 2,
                VictimEvent.Revived => _payRevive,
                VictimEvent.Zapped => _payDefib,
                VictimEvent.Hospitalized => _payHospital,
                _ => 0
            };
            if (pay > 0) ServerAdd(pay, e == VictimEvent.Zapped ? "defibrillator" : e.ToString().ToLowerInvariant(), victim.Body.HeadPosition + Vector3.up);
            // Lost to the rival company: they send the bill for the airlift.
            if (e == VictimEvent.Lost && _airliftBill > 0 && _money.Value > 0)
            {
                int bill = Mathf.Min(_airliftBill, _money.Value);
                ServerAdd(-bill, "airlift bill", victim.Body.HeadPosition + Vector3.up);
                if (RescueService.Instance != null)
                    RescueService.Instance.ServerAnnounce($"<color=#ff9080>The rival company bills you ${bill} for the airlift.</color>");
            }
        }

        /// <summary>Host: earn money (shown where it happened).</summary>
        [Server]
        public void ServerAdd(int amount, string reason, Vector3 where)
        {
            if (amount == 0) return;
            _money.Value = Mathf.Max(0, _money.Value + amount);
            Debug.Log($"[Money] {(amount > 0 ? "+" : "")}{amount} ({reason}) = {_money.Value}");
            PaidObservers(amount, where);
            ServerChanged?.Invoke(_money.Value);
        }

        /// <summary>Host: pay for something. False (and nothing spent) if the team can't afford it.</summary>
        [Server]
        public bool ServerSpend(int amount, string reason)
        {
            if (amount < 0 || _money.Value < amount) return false;
            _money.Value -= amount;
            Debug.Log($"[Money] -{amount} ({reason}) = {_money.Value}");
            ServerChanged?.Invoke(_money.Value);
            return true;
        }

        /// <summary>Host: set directly (loading a save).</summary>
        [Server]
        public void ServerSet(int amount) => _money.Value = Mathf.Max(0, amount);

        [ServerRpc(RequireOwnership = false)]
        private void CheatServer(int amount, NetworkConnection caller = null)
        {
            if (!DevCommands.CheatsAllowed) return;
            PlayerHub p = null;
            foreach (PlayerHub h in PlayerHub.All)
                if (h.Owner == caller) p = h;
            ServerAdd(amount, "cheat", p != null ? p.Head.position + p.Head.forward * 1.5f : Vector3.zero);
        }

        [ObserversRpc]
        private void PaidObservers(int amount, Vector3 where)
        {
            if (where != Vector3.zero)
                FloatingText.Spawn(where, amount > 0 ? $"+${amount}" : $"-${-amount}", amount > 0 ? new Color(0.45f, 1f, 0.45f) : new Color(1f, 0.5f, 0.4f), 1.1f, 1.8f);
            if (_audio != null && amount > 0) _audio.PlayOneShot(ProceduralAudio.Cash, 0.7f);
        }
    }
}
