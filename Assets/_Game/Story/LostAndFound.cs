using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Items;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// Sandy's Lost &amp; Found counter. Press Interact with lost things in your hands or pockets: they're handed in
    /// and the team gets the reward. The story hears about each one (stolen goods, evidence).
    /// </summary>
    public class LostAndFound : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _payPoint;

        /// <summary>Host: an item was handed in by a player (item info is copied: the item is gone by now).</summary>
        public static event Action<HandedIn> ServerHandedIn;

        public struct HandedIn
        {
            public PlayerHub By;
            public string Owner;
            public string Kind;
            public bool Stolen;
            public bool Evidence;
            public int Reward;
        }

        public static LostAndFound Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerHandedIn = null;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private static List<LostItem> Carried(PlayerHub player)
        {
            var list = new List<LostItem>();
            foreach (Item item in Item.All)
                if (item.Holder == player && item.TryGetComponent(out LostItem lost))
                    list.Add(lost);
            return list;
        }

        public bool CanInteract(PlayerHub player) => true;

        public string GetPrompt(PlayerHub player)
        {
            List<LostItem> carried = Carried(player);
            if (carried.Count == 0) return "Lost & Found (bring things tourists lost)";
            int reward = 0;
            foreach (LostItem l in carried) reward += l.Reward;
            return carried.Count == 1 ? $"Hand in {carried[0].Item.DisplayName} (+${reward})" : $"Hand in {carried.Count} lost things (+${reward})";
        }

        public void OnInteract(PlayerHub player)
        {
            if (Carried(player).Count == 0)
            {
                PlayerHud.ShowToast("Nothing to hand in. Tourists drop wallets, phones and sunglasses on the beach and in the water.", 3.5f);
                return;
            }
            HandInServer(player);
        }

        [ServerRpc(RequireOwnership = false)]
        private void HandInServer(PlayerHub player, NetworkConnection caller = null)
        {
            if (player == null || player.Owner != caller) return;
            if ((player.transform.position - transform.position).sqrMagnitude > 6f * 6f) return;
            Vector3 where = _payPoint != null ? _payPoint.position : transform.position + Vector3.up * 1.4f;
            foreach (LostItem lost in Carried(player))
            {
                var info = new HandedIn
                {
                    By = player, Owner = lost.OwnerName, Kind = lost.Kind, Stolen = lost.IsStolen, Evidence = lost.IsEvidence, Reward = lost.Reward
                };
                Debug.Log($"[LostAndFound] {player.DisplayName} handed in {lost.Item.DisplayName} (+${info.Reward}{(info.Stolen ? ", stolen" : "")}{(info.Evidence ? ", evidence" : "")})");
                lost.Item.ServerForceDrop();
                Despawn(lost.gameObject);
                if (Economy.Instance != null) Economy.Instance.ServerAdd(info.Reward, "lost & found", where);
                where += Vector3.up * 0.35f;
                ServerHandedIn?.Invoke(info);
            }
        }
    }
}
