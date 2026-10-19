using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.Story;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Vehicles
{
    /// <summary>
    /// A fuel pump on a dock. E fills every fuel-using vehicle within reach from the team's wallet, as much as the
    /// team can pay for.
    /// </summary>
    public class FuelPump : NetworkBehaviour, IInteractionHandler
    {
        [SerializeField] private float _pricePerLitre = 1f;
        [SerializeField] private float _reach = 14f;

        public bool CanInteract(PlayerHub player) => true;

        public string GetPrompt(PlayerHub player)
        {
            float missing = 0f;
            foreach (Vehicle v in Vehicle.All)
                if (InReach(v)) missing += v.FuelCapacity - v.Fuel;
            return missing < 0.5f ? "Fuel pump (every tank is full)" : $"Fill up the tanks (${Mathf.CeilToInt(missing * _pricePerLitre)})";
        }

        public void OnInteract(PlayerHub player) => RefuelServer(player);

        private bool InReach(Vehicle v) =>
            v != null && v.UsesFuel && (v.transform.position - transform.position).sqrMagnitude <= _reach * _reach;

        [ServerRpc(RequireOwnership = false)]
        private void RefuelServer(PlayerHub player, NetworkConnection caller = null)
        {
            if (player == null || player.Owner != caller || Economy.Instance == null) return;
            if ((player.transform.position - transform.position).sqrMagnitude > 8f * 8f) return;
            float bought = 0f;
            foreach (Vehicle v in Vehicle.All)
            {
                if (!InReach(v)) continue;
                float wanted = v.FuelCapacity - v.Fuel;
                int cost = Mathf.CeilToInt(wanted * _pricePerLitre);
                if (wanted < 0.5f || cost <= 0) continue;
                float affordable = Mathf.Min(wanted, Economy.Money / _pricePerLitre);
                int pay = Mathf.CeilToInt(affordable * _pricePerLitre);
                if (pay <= 0 || !Economy.Instance.ServerSpend(pay, "fuel")) continue;
                bought += v.ServerRefuel(affordable);
            }
            Told(caller, bought);
        }

        [TargetRpc]
        private void Told(NetworkConnection target, float litres)
        {
            if (litres > 0.5f) PlayerHud.ShowToast($"Filled up {Mathf.RoundToInt(litres)} litres.", 2.5f, ToastKind.Good);
            else PlayerHud.ShowToast("Nothing to fill, or the team can't pay for fuel.", 3f, ToastKind.Warning);
        }
    }
}
