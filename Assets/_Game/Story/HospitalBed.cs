using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// The hotel infirmary's bed. Carry a shark-bite victim here and press Interact: the bleeding stops and they're
    /// saved (the doctor takes it from here).
    /// </summary>
    public class HospitalBed : NetworkBehaviour, IInteractionHandler
    {
        [Tooltip("Where the patient lies (up = their front, forward = toward their head).")]
        [SerializeField] private Transform _patientPoint;

        private static VictimBrain Carried(PlayerHub player)
        {
            var held = player != null && player.Hands != null ? player.Hands.HeldItem : null;
            return held != null && held.TryGetComponent(out VictimBrain v) && v.HasLostLeg && v.State != VictimState.Saved && v.State != VictimState.Lost ? v : null;
        }

        public bool CanInteract(PlayerHub player) => Carried(player) != null;

        public string GetPrompt(PlayerHub player)
        {
            VictimBrain v = Carried(player);
            return v != null ? $"Put {v.Name} on the hospital bed ({v.BleedSecondsLeft:F0}s left)" : "Hospital bed";
        }

        public void OnInteract(PlayerHub player)
        {
            VictimBrain v = Carried(player);
            if (v != null) AdmitServer(player, v);
        }

        [ServerRpc(RequireOwnership = false)]
        private void AdmitServer(PlayerHub player, VictimBrain victim, NetworkConnection caller = null)
        {
            if (player == null || victim == null || player.Owner != caller) return;
            if ((player.transform.position - transform.position).sqrMagnitude > 5f * 5f) return;
            Transform at = _patientPoint != null ? _patientPoint : transform;
            // Lying on the back: the torso's up points at the ceiling... the body's up (head direction) along the bed.
            Quaternion lying = Quaternion.LookRotation(Vector3.up, at.forward);
            victim.ServerHospitalize(at.position + Vector3.up * 0.25f, lying, player);
        }
    }
}
