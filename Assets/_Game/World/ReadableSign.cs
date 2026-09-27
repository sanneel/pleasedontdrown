using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>Local-only interactable: shows its text to whoever reads it. Each read shows the next line.</summary>
    public class ReadableSign : MonoBehaviour, IInteractionHandler
    {
        [SerializeField] private string _prompt = "Read the sign";
        [SerializeField, TextArea] private string[] _lines = { "..." };

        private int _next;

        public bool CanInteract(PlayerHub player) => _lines.Length > 0;
        public string GetPrompt(PlayerHub player) => _prompt;

        public void OnInteract(PlayerHub player)
        {
            PlayerHud.ShowToast(_lines[_next]);
            _next = (_next + 1) % _lines.Length;
        }
    }
}
