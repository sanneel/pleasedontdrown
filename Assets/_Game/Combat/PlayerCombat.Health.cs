using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using PleaseDontDrown.Audio;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// A lifeguard's health (host decides). Pirates, bosses and creatures hurt you; at zero you go down (not dead):
    /// you lie on the sand and a friend brings you back with CPR (tap Secondary on you). Everyone down during a
    /// mission fails it (<see cref="Story.StoryDirector"/> restarts it); outside a mission everybody just gets up again.
    /// Health comes back by itself after a few quiet seconds, so only fights are dangerous.
    /// </summary>
    public partial class PlayerCombat : IInteractionSecondary
    {
        public const int MaxHealth = 100;
        private const float HitImmunity = 0.25f;     // a flurry can't take it all in one frame
        private const float RegenDelay = 6f, RegenPerSecond = 10f;
        private const float ReviveTaps = 10f;        // CPR presses to bring a friend back
        private const float ReviveDecay = 0.08f;     // progress lost per second without pressing
        private const int ReviveHealth = 35;
        private const float ReviveImmunity = 2f;
        private const float GetUpAlone = 5f;         // nobody left to help (and no mission): up again after this

        private readonly SyncVar<int> _health = new SyncVar<int>(MaxHealth);
        private readonly SyncVar<bool> _downed = new SyncVar<bool>();
        private readonly SyncVar<float> _revive01 = new SyncVar<float>();

        private float _lastDamaged = -100f, _immuneUntil, _regenCarry, _lastReviveTap = -100f, _allDownSince = -1f;
        private bool _serverGod, _sentGod;
        private float _downK;

        /// <summary>Host: someone went down / got back up.</summary>
        public static event Action<PlayerCombat> ServerWentDown, ServerGotUp;

        public int Health => _health.Value;
        public float Health01 => _health.Value / (float)MaxHealth;
        public bool IsDowned => _downed.Value;
        /// <summary>0..1: how far a friend's CPR has got.</summary>
        public float Revive01 => _revive01.Value;
        public PlayerHub Hub => _hub;
        /// <summary>Every machine: when this lifeguard last got hurt (screen flash, shake).</summary>
        public float LastHurtTime { get; private set; } = -10f;
        public float LastHurtStrength { get; private set; }

        /// <summary>Host: true if nobody is standing (and somebody is playing).</summary>
        public static bool AllDown
        {
            get
            {
                int count = 0;
                foreach (PlayerHub p in PlayerHub.All)
                {
                    if (p == null || !p.TryGetComponent(out PlayerCombat c)) continue;
                    count++;
                    if (!c.IsDowned) return false;
                }
                return count > 0;
            }
        }

        // ------------------------------------------------------------------ host: damage, going down, getting up

        /// <summary>Host: hurt this lifeguard (a pirate's fist, a bite, a bullet). Knockback is separate.</summary>
        [Server]
        public void ServerDamage(int amount, string byWhom, Vector3 from)
        {
            if (amount <= 0 || _downed.Value || _serverGod || Time.time < _immuneUntil) return;
            _immuneUntil = Time.time + HitImmunity;
            _lastDamaged = Time.time;
            int health = Mathf.Max(0, _health.Value - amount);
            _health.Value = health;
            HurtObservers(amount, from);
            if (health <= 0) ServerGoDown(byWhom);
        }

        [Server]
        private void ServerGoDown(string byWhom)
        {
            _downed.Value = true;
            _health.Value = 0;
            _revive01.Value = 0f;
            DownedObservers(byWhom ?? string.Empty);
            Debug.Log($"[Health] {_hub.DisplayName} is down{(string.IsNullOrEmpty(byWhom) ? "" : " (" + byWhom + ")")}");
            ServerWentDown?.Invoke(this);
        }

        /// <summary>Host: back on their feet with this much health (revived, mission restart, everyone got up).</summary>
        [Server]
        public void ServerStandUp(int health = MaxHealth, float immunity = ReviveImmunity)
        {
            bool wasDown = _downed.Value;
            _downed.Value = false;
            _revive01.Value = 0f;
            _health.Value = Mathf.Clamp(health, 1, MaxHealth);
            _immuneUntil = Time.time + immunity;
            _lastDamaged = Time.time;
            if (!wasDown) return;
            Debug.Log($"[Health] {_hub.DisplayName} is back up ({_health.Value})");
            UpObservers();
            ServerGotUp?.Invoke(this);
        }

        private void ServerHealthUpdate()
        {
            if (_downed.Value)
            {
                if (_revive01.Value > 0f && Time.time - _lastReviveTap > 1.5f)
                    _revive01.Value = Mathf.Max(0f, _revive01.Value - ReviveDecay * Time.deltaTime);
                return;
            }
            if (_health.Value < MaxHealth && Time.time - _lastDamaged > RegenDelay)
            {
                _regenCarry += RegenPerSecond * Time.deltaTime;
                int add = Mathf.FloorToInt(_regenCarry);
                if (add > 0)
                {
                    _regenCarry -= add;
                    _health.Value = Mathf.Min(MaxHealth, _health.Value + add);
                }
            }
        }

        /// <summary>Host, once per frame (by whichever lifeguard is first): nobody standing and no mission to fail.</summary>
        private void ServerAllDownCheck()
        {
            if (PlayerHub.All.Count == 0 || PlayerHub.All[0] != _hub) return; // one lifeguard does it for everyone
            if (!AllDown || Story.StoryDirector.MissionRunning)
            {
                _allDownSince = -1f;
                return;
            }
            if (_allDownSince < 0f) _allDownSince = Time.time;
            if (Time.time - _allDownSince < GetUpAlone) return;
            _allDownSince = -1f;
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p.TryGetComponent(out PlayerCombat c) && c.IsDowned) c.ServerStandUp(ReviveHealth);
        }

        [ServerRpc]
        private void SetGodServer(bool on) => _serverGod = on;

        // ------------------------------------------------------------------ CPR on a downed friend (rescuer's machine)

        public bool CanSecondary(PlayerHub player) =>
            IsDowned && player != null && player != _hub && player.TryGetComponent(out PlayerCombat c) && !c.IsDowned;

        public string GetSecondaryPrompt(PlayerHub player) => $"CPR on {_hub.DisplayName} ({Mathf.RoundToInt(Revive01 * 100f)}%)";

        public void OnSecondary(PlayerHub player)
        {
            if (player == null || !player.IsOwner) return;
            Vector3 chest = ChestPoint;
            player.ShowPump(chest, chest + Vector3.up * 0.1f + transform.forward * 0.4f);
            ReviveTapServer();
        }

        private Vector3 ChestPoint => transform.position + Vector3.up * 0.3f;

        [ServerRpc(RequireOwnership = false)]
        private void ReviveTapServer(NetworkConnection caller = null)
        {
            if (!_downed.Value) return;
            PlayerHub rescuer = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p.Owner == caller) rescuer = p;
            if (rescuer == null || rescuer == _hub) return;
            if (!rescuer.TryGetComponent(out PlayerCombat rc) || rc.IsDowned) return;
            if ((rescuer.transform.position - transform.position).sqrMagnitude > 3.5f * 3.5f) return;
            if (Time.time - _lastReviveTap < 0.12f) return; // no faster than a person can press
            _lastReviveTap = Time.time;
            _revive01.Value = Mathf.Min(1f, _revive01.Value + 1f / ReviveTaps);
            PumpObservers(rescuer.NetworkObject);
            if (_revive01.Value < 1f) return;
            ServerStandUp(ReviveHealth);
            RevivedObservers(rescuer.DisplayName);
            Debug.Log($"[Health] {rescuer.DisplayName} revived {_hub.DisplayName}");
        }

        [ObserversRpc]
        private void PumpObservers(NetworkObject rescuerObject)
        {
            // The rescuer's own machine already showed it.
            if (rescuerObject == null || rescuerObject.IsOwner || !rescuerObject.TryGetComponent(out PlayerHub rescuer)) return;
            Vector3 chest = ChestPoint;
            rescuer.ShowPump(chest, chest + Vector3.up * 0.1f + transform.forward * 0.4f);
        }

        // ------------------------------------------------------------------ every machine: how it looks and sounds

        [ObserversRpc]
        private void HurtObservers(int amount, Vector3 from)
        {
            LastHurtTime = Time.time;
            LastHurtStrength = Mathf.Clamp01(amount / 30f);
            if (!IsOwner || _hub.Look == null) return;
            // The view jolts away from the blow.
            Vector3 dir = transform.position - from;
            Transform view = _hub.Head != null ? _hub.Head : transform;
            float side = Vector3.Dot(dir, view.right) >= 0f ? 1f : -1f;
            _hub.Look.Shake(0.6f + LastHurtStrength * 1.4f, side);
        }

        [ObserversRpc]
        private void DownedObservers(string byWhom)
        {
            if (_audio != null) _audio.PlayOneShot(FunSounds.SlideDown, 0.9f);
            Vector3 head = _hub.Head != null ? _hub.Head.position : transform.position + Vector3.up * 1.6f;
            FloatingText.Spawn(head + Vector3.up * 0.4f, "DOWN!", new Color(1f, 0.45f, 0.35f), 1.2f, 1.8f);
            if (!IsOwner) return;
            if (_hub.Hands != null && _hub.Hands.HeldItem != null) _hub.Hands.Drop();
            PlayerHud.ShowToast(string.IsNullOrEmpty(byWhom) ? "You're down!" : $"<b>{byWhom}</b> put you down!", 3f);
        }

        [ObserversRpc]
        private void UpObservers()
        {
            Vector3 head = _hub.Head != null ? _hub.Head.position : transform.position + Vector3.up * 1.6f;
            FloatingText.Spawn(head + Vector3.up * 0.4f, "BACK UP!", new Color(0.55f, 1f, 0.6f), 1f, 1.4f);
        }

        [ObserversRpc]
        private void RevivedObservers(string rescuer)
        {
            if (_audio != null) _audio.PlayOneShot(FunSounds.Cheer, 0.6f);
            if (IsOwner) PlayerHud.ShowToast($"<b>{rescuer}</b> brought you back!", 2.5f);
        }

        /// <summary>Owner, every frame: lying on the sand while down; tell the host about god mode.</summary>
        private void UpdateHealthOwner()
        {
            if (GodMode.On != _sentGod)
            {
                _sentGod = GodMode.On;
                SetGodServer(_sentGod);
            }
            _downK = Mathf.MoveTowards(_downK, IsDowned ? 1f : 0f, Time.deltaTime / (IsDowned ? 0.35f : 0.6f));
            if (IsDowned && _hub.Motor != null) _hub.Motor.Stun(0.2f);
        }

        /// <summary>
        /// Owner (test): walk up to the nearest downed lifeguard, look at them and give CPR through the normal
        /// interaction (what a player does by hand), <paramref name="taps"/> presses.
        /// </summary>
        private System.Collections.IEnumerator CprFriend(int taps)
        {
            PlayerCombat friend = null;
            foreach (PlayerHub p in PlayerHub.All)
                if (p != null && p != _hub && p.TryGetComponent(out PlayerCombat c) && c.IsDowned) friend = c;
            if (friend == null)
            {
                DevCommands.Print("nobody is down");
                yield break;
            }
            Vector3 to = friend.transform.position - transform.position;
            to.y = 0f;
            Vector3 stand = friend.transform.position - (to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward) * 1.1f;
            _hub.Motor.Teleport(stand + Vector3.up * 0.2f);
            yield return new WaitForSeconds(0.3f);
            int done = 0;
            for (int i = 0; i < taps && friend.IsDowned; i++)
            {
                _hub.Look.LookAt(friend.ChestPoint);
                yield return new WaitForSeconds(0.2f);
                Interactable target = _hub.Interactor.Current;
                if (target != null && target.CanSecondary(_hub))
                {
                    target.InteractSecondary(_hub);
                    done++;
                }
            }
            yield return new WaitForSeconds(0.5f);
            DevCommands.Print($"cpr presses {done}/{taps}; {friend.Hub.DisplayName} {(friend.IsDowned ? $"still down ({friend.Revive01:P0})" : "is back up")}");
        }

        /// <summary>How far the view is tipped over from being down (0 standing .. 1 lying).</summary>
        private float DownedViewK => _downK * _downK * (3f - 2f * _downK);
    }
}
