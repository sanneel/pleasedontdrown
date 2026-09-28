using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>A glowing pad: E takes you to its target (the player moves themselves, so nothing is sent).</summary>
    public class TeleportPad : MonoBehaviour, IInteractionHandler
    {
        [SerializeField] private string _label = "Teleport";
        [SerializeField] private bool _toDevIsland = true;

        public bool CanInteract(PlayerHub player) => DevIsland.Instance != null;
        public string GetPrompt(PlayerHub player) => _label;

        public void OnInteract(PlayerHub player)
        {
            if (player != PlayerHub.Local || DevIsland.Instance == null) return;
            DevIsland.Teleport(player, _toDevIsland ? DevIsland.Instance.Arrival : DevIsland.Instance.Home);
        }
    }
}
