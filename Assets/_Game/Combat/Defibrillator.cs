using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Core;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Combat
{
    /// <summary>
    /// The electric shock (hotel first-aid station). Hold it and press Primary on an unconscious tourist who's out of
    /// deep water: it brings them back, and it's the only thing that works once their heart has stopped (flatline).
    /// </summary>
    [RequireComponent(typeof(Item))]
    public class Defibrillator : NetworkBehaviour, IHeldTool
    {
        [SerializeField] private AudioSource _audio;
        [SerializeField] private float _reach = 2.8f;
        [SerializeField] private float _recharge = 2f;

        private Item _item;
        private float _nextUse;
        private float _lastServerUse = float.NegativeInfinity;

        public string UseLabel => "Shock (on an unconscious tourist)";

        private void Awake() => _item = GetComponent<Item>();

        public void Use(PlayerHub holder)
        {
            if (holder == null) return;
            if (Time.time < _nextUse)
            {
                PlayerHud.ShowToast("Charging...", 1f);
                return;
            }
            VictimBrain target = FindTarget(holder);
            if (target == null)
            {
                PlayerHud.ShowToast("Nobody unconscious in front of you to shock.", 2f);
                return;
            }
            _nextUse = Time.time + _recharge;
            holder.Gesture(AvatarGesture.Zap, target.Body.ChestPoint);
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Zap, 0.8f);
            ZapServer(target);
        }

        private VictimBrain FindTarget(PlayerHub holder)
        {
            Transform view = holder.Look != null && holder.Look.Camera != null ? holder.Look.Camera.transform : holder.Head;
            VictimBrain best = null;
            float bestScore = float.MaxValue;
            foreach (VictimBrain v in VictimBrain.All)
            {
                if (v.State != VictimState.Unconscious || v.Item.IsHeld) continue;
                Vector3 to = v.Body.ChestPoint - view.position;
                float distance = to.magnitude;
                if (distance > _reach) continue;
                float angle = Vector3.Angle(view.forward, to);
                if (angle > 50f) continue;
                float score = angle + distance * 10f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = v;
                }
            }
            return best;
        }

        [ServerRpc(RequireOwnership = false)]
        private void ZapServer(VictimBrain victim, NetworkConnection caller = null)
        {
            PlayerHub holder = _item.Holder;
            if (victim == null || holder == null || holder.Owner != caller || Time.time - _lastServerUse < _recharge * 0.8f) return;
            if ((victim.transform.position - holder.transform.position).sqrMagnitude > (_reach + 1.5f) * (_reach + 1.5f)) return;
            _lastServerUse = Time.time;
            if (!victim.ServerDefib(holder))
                Tell(caller, $"{victim.Name} has to be unconscious and out of deep water.");
            ZapObservers(caller.ClientId);
        }

        [ObserversRpc]
        private void ZapObservers(int userId)
        {
            if (LocalConnection != null && LocalConnection.ClientId == userId) return;
            if (_audio != null) _audio.PlayOneShot(ProceduralAudio.Zap, 0.8f);
        }

        [TargetRpc]
        private void Tell(NetworkConnection target, string text) => PlayerHud.ShowToast(text, 2.5f);
    }
}
