using PleaseDontDrown.Core;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
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
        private GUIStyle _title, _button;

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
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _button ??= new GUIStyle(GUI.skin.button) { fontSize = 18 };
            var area = new Rect((Screen.width - 420f) * 0.5f, (Screen.height - 260f) * 0.5f, 420f, 260f);
            GUI.color = new Color(0.12f, 0.07f, 0.2f, 0.9f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(area.x + 20f, area.y + 14f, area.width - 40f, area.height - 28f));
            GUILayout.Label("Travel to...", _title);
            GUILayout.Space(8f);
            for (int i = 0; i < DevIsland.DestinationNames.Length; i++)
            {
                if ((Destination)i == _here) continue;
                if (GUILayout.Button(DevIsland.DestinationNames[i], _button, GUILayout.Height(44f)))
                {
                    SetOpen(false);
                    DevIsland.Travel((Destination)i);
                }
                GUILayout.Space(4f);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Stay here (Esc)", GUILayout.Height(28f))) SetOpen(false);
            GUILayout.EndArea();
        }
    }
}
