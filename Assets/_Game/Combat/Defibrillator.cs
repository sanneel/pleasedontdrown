using System.Collections;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// The electric shock (hotel first-aid station): it brings back an unconscious tourist who's out of deep water, and
    /// it's the only thing that works once their heart has stopped (flatline). Carry it to them, put it down beside
    /// them, then E on it (the user, 2026-10-12): the lifeguard kneels, takes the two paddles off the case
    /// (<see cref="DefibPaddles"/>, cables back to it), presses them on the chest and the shock goes through when they
    /// touch (<see cref="DefibPaddles.ShockAt"/> after the press); the paddles go back on the case. While someone lies
    /// close enough, picking it up again is the other button.
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Defibrillator : NetworkBehaviour, IHeldTool, IGroundUse, IInteractionSecondary
    {
        [SerializeField] private AudioSource _audio;
        [Tooltip("How far from the case on the ground the casualty's chest can be (the cables' length).")]
        [SerializeField] private float _cable = 2.2f;
        [SerializeField] private float _recharge = 2f;
        [Tooltip("How close to the casualty's chest the lifeguard has to stand (they kneel down beside them).")]
        [SerializeField] private float _beside = 2f;

        private Item _item;
        private DefibPaddles _paddles;
        private float _nextUse;
        private float _lastServerUse = float.NegativeInfinity;

        public string UseLabel => "Put it down by an unconscious tourist, then E on it";

        private void Awake()
        {
            _item = GetComponent<Item>();
            _paddles = GetComponent<DefibPaddles>() ?? gameObject.AddComponent<DefibPaddles>();
        }

        private void OnEnable() =>
            DevCommands.Register("defib", "", "Press E on the nearest defibrillator lying on the ground (tests).", _ =>
            {
                PlayerHub me = PlayerHub.Local;
                Defibrillator nearest = null;
                foreach (Defibrillator d in FindObjectsByType<Defibrillator>(FindObjectsSortMode.None))
                    if (me != null && !d._item.IsHeld && (nearest == null ||
                        (d.transform.position - me.transform.position).sqrMagnitude < (nearest.transform.position - me.transform.position).sqrMagnitude))
                        nearest = d;
                if (nearest == null) { DevCommands.Print("no defibrillator on the ground"); return; }
                DevCommands.Print($"defibrillator: {(nearest.CanUseOnGround(me) ? nearest.GroundUsePrompt(me) : "nobody to shock here")}");
                if (nearest.CanUseOnGround(me)) nearest.UseOnGround(me);
            }, cheat: true, owner: this);

        private void OnDisable() => DevCommands.Unregister("defib", this);

        // Held: it's carried, not used (the shock is given from the ground).
        public void Use(PlayerHub holder) => PlayerHud.ShowToast("Put the defibrillator down next to them, then press E on it.", 2.5f);

        // ------------------------------------------------------------------ on the ground: E

        public bool CanUseOnGround(PlayerHub player) => player != null && !_item.IsHeld && Casualty(player) != null;

        public string GroundUsePrompt(PlayerHub player)
        {
            VictimBrain who = Casualty(player);
            if (who == null) return string.Empty;
            if (!Beside(player, who)) return $"Stand next to {who.Name} to shock";
            return Time.time < _nextUse ? "Defibrillator charging..." : $"Shock {who.Name}";
        }

        public void UseOnGround(PlayerHub player)
        {
            if (Time.time < _nextUse)
            {
                PlayerHud.ShowToast("Charging...", 1f);
                return;
            }
            VictimBrain target = Casualty(player);
            if (target == null) return;
            if (!Beside(player, target))
            {
                PlayerHud.ShowToast($"Stand next to {target.Name}, then press E on the defibrillator.", 2f);
                return;
            }
            _nextUse = Time.time + _recharge;
            Show(player, target);
            ZapServer(target, player);
        }

        /// <summary>The unconscious tourist the cables reach (nearest the case), with the player close by.</summary>
        private VictimBrain Casualty(PlayerHub player)
        {
            if (player == null || (player.transform.position - transform.position).sqrMagnitude > 3.5f * 3.5f) return null;
            VictimBrain best = null;
            float bestDistance = _cable;
            foreach (VictimBrain v in VictimBrain.All)
            {
                if (v.State != VictimState.Unconscious || v.Item.IsHeld) continue;
                float distance = Vector3.Distance(v.Body.ChestPoint, transform.position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = v;
                }
            }
            return best;
        }

        /// <summary>Standing by them (they kneel down beside them: further off, the body would slide over to them).</summary>
        private bool Beside(PlayerHub player, VictimBrain victim)
        {
            Vector3 to = victim.Body.ChestPoint - player.transform.position;
            to.y = 0f;
            return to.magnitude <= _beside;
        }

        /// <summary>Kneel, the paddles off the case onto the chest, the whine (every machine).</summary>
        private void Show(PlayerHub user, VictimBrain target)
        {
            _paddles.UseFrom(user);
            user.ShowZap(_paddles, target.Body.ChestPoint, target.Body.HeadPosition, target.Body.KneelReach);
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Zap, 0.8f); // (its whine rises while the paddles go down)
        }

        [ServerRpc(RequireOwnership = false)]
        private void ZapServer(VictimBrain victim, PlayerHub user, NetworkConnection caller = null)
        {
            if (victim == null || user == null || user.Owner != caller || _item.IsHeld || Time.time - _lastServerUse < _recharge * 0.8f) return;
            if (Vector3.Distance(victim.Body.ChestPoint, transform.position) > _cable + 0.8f) return;
            if ((user.transform.position - transform.position).sqrMagnitude > 5f * 5f) return;
            _lastServerUse = Time.time;
            ZapObservers(caller.ClientId, victim, user);
            StartCoroutine(ShockWhenOn(victim, user, caller));
        }

        // The paddles reach the chest a moment after the press: the shock goes through then.
        private IEnumerator ShockWhenOn(VictimBrain victim, PlayerHub user, NetworkConnection caller)
        {
            yield return new WaitForSeconds(DefibPaddles.ShockAt);
            if (victim == null || user == null) yield break;
            if (!victim.ServerDefib(user))
                Tell(caller, $"{victim.Name} has to be unconscious and out of deep water.");
        }

        [ObserversRpc]
        private void ZapObservers(int userId, VictimBrain victim, PlayerHub user)
        {
            if (LocalConnection != null && LocalConnection.ClientId == userId) return;
            if (user != null && victim != null) Show(user, victim);
        }

        [TargetRpc]
        private void Tell(NetworkConnection target, string text) => PlayerHud.ShowToast(text, 2.5f);

        // ------------------------------------------------------------------ picking it up again (E shocks while someone's there)

        public bool CanSecondary(PlayerHub player) => CanUseOnGround(player) && player.Hands != null;

        public string GetSecondaryPrompt(PlayerHub player) => "Pick up the defibrillator";

        public void OnSecondary(PlayerHub player) => player.Hands.TryPickUp(_item);
    }
}
