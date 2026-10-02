using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Dev
{
    /// <summary>
    /// A glowing travel pad (one on each island): E opens a short list of the other islands, pick one and you're there.
    /// Players move themselves, so nothing is sent.
    /// </summary>
    public class TeleportPad : MonoBehaviour, IInteractionHandler
    {
        [Tooltip("The island this pad stands on (left out of the list).")]
        [SerializeField] private Destination _here;

        private bool _open;
        private string[] _names;

        private void Awake() => useGUILayout = false; // drawn with fixed boxes: no layout pass needed

        public bool CanInteract(PlayerHub player) => DevIsland.Instance != null;
        public string GetPrompt(PlayerHub player) => "Travel to another island";

        public void OnInteract(PlayerHub player)
        {
            if (player == PlayerHub.Local) SetOpen(true);
        }

        private void SetOpen(bool open)
        {
            if (open == _open) return;
            _open = open;
            if (open) GameInput.PushUI();
            else GameInput.PopUI();
        }

        private void OnDisable() => SetOpen(false);

        private void Update()
        {
            if (!_open) return;
            PlayerHub me = PlayerHub.Local;
            if (GameInput.ToggleMenu.WasPressedThisFrame() || me == null || (me.transform.position - transform.position).sqrMagnitude > 6f * 6f)
                SetOpen(false);
        }

        private void OnGUI()
        {
            if (!_open) return;
            const float width = 440f, row = 60f, gap = 10f;
            int count = DevIsland.DestinationNames.Length; // the other islands + "stay here"
            float height = 96f + count * (row + gap) + 14f;
            Hud.Dim(0.45f);
            var panel = new Rect((Hud.Width - width) * 0.5f, (Hud.Height - height) * 0.5f, width, height);
            Hud.Panel(panel, 0.92f);
            Hud.Label(new Rect(panel.x, panel.y + 18f, width, 56f), "TRAVEL TO", 40f, Color.white, heavy: true, shadow: false);
            float y = panel.y + 96f;
            for (int i = 0; i < DevIsland.DestinationNames.Length; i++)
            {
                if ((Destination)i == _here) continue;
                _names ??= System.Array.ConvertAll(DevIsland.DestinationNames, n => n.ToUpperInvariant());
                bool go = Hud.Button(new Rect(panel.x + 30f, y, width - 60f, row), _names[i], centred: true);
                y += row + gap;
                if (!go) continue;
                SetOpen(false);
                DevIsland.Travel((Destination)i);
                return;
            }
            if (Hud.Button(new Rect(panel.x + 30f, y, width - 60f, row), "STAY HERE", centred: true, primary: true)) SetOpen(false);
        }
    }
}
