using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Rescue
{
    /// <summary>Board at the station: press Interact to throw a tourist into the sea for practice (until the director arrives).</summary>
    public class DrillBoard : MonoBehaviour, IInteractionHandler
    {
        public bool CanInteract(PlayerHub player) => RescueService.Instance != null;

        public string GetPrompt(PlayerHub player)
        {
            int n = RescueService.InTroubleCount;
            return n == 0 ? "Start a rescue drill" : $"Start another drill ({n} in the water)";
        }

        public void OnInteract(PlayerHub player) => RescueService.Instance.RequestDrill();
    }
}
