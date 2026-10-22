using FishNet.Connection;
using FishNet.Object;
using PleaseDontDrown.Avatars;
using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using PleaseDontDrown.Rescue;
using UnityEngine;

namespace PleaseDontDrown.Story
{
    /// <summary>
    /// E picks anybody up. A beach-goer, guest, pirate or robber picked up becomes a tourist body (the same look and
    /// name) in the lifeguard's arms, so everything a tourist can go through works on them: thrown, shot out of the
    /// air, carried to the infirmary or to Sandy, CPR. Standing they're fine; knocked down, out cold; killed, a body.
    /// Talking moved to T (the secondary action). Sandy, the receptionist, the rival lifeguard, the police officer and
    /// everyone placed in the scene for the story (Milo at the rentals) only talk.
    /// </summary>
    public partial class StoryNpc : IInteractionSecondary
    {
        private float _nextPickRequest;

        private bool CanBePickedUp =>
            _role.Value is NpcRole.Bystander or NpcRole.Guest or NpcRole.Pirate or NpcRole.Robber &&
            !_talkable.Value && _ride == null && !NetworkObject.IsSceneObject; // (placed in the scene: a story character, Milo at the rentals)

        public bool CanInteract(PlayerHub player) => CanBePickedUp && player.Hands != null;

        public string GetPrompt(PlayerHub player) => $"Pick up {Name}";

        public void OnInteract(PlayerHub player)
        {
            if (Time.time < _nextPickRequest) return;
            _nextPickRequest = Time.time + 0.5f;
            PickUpServer(player);
        }

        public bool CanSecondary(PlayerHub player) => CanTalk(player);
        public string GetSecondaryPrompt(PlayerHub player) => CanTalk(player) ? TalkPrompt : null;
        public void OnSecondary(PlayerHub player) => Talk(player);
        public UnityEngine.InputSystem.InputAction SecondaryAction(PlayerHub player) => Core.GameInput.Talk;

        [ServerRpc(RequireOwnership = false)]
        private void PickUpServer(PlayerHub player, NetworkConnection caller = null)
        {
            if (player == null || player.Owner != caller || !CanBePickedUp || !IsSpawned) return;
            if ((player.transform.position - transform.position).sqrMagnitude > 4f * 4f) return;
            RescueService rescue = RescueService.Instance;
            if (rescue == null) return;

            VictimState state = _dead.Value ? VictimState.Lost : _pose.Value == AvatarPose.Down ? VictimState.Unconscious : VictimState.Fine;
            TouristProfile profile = TouristProfile.Default;
            profile.Look = _look.Value;
            profile.Name = Name;
            profile.Figure = AvatarLook.Unpack(_look.Value).Feminine ? 1 : 0;
            Vector3 at = transform.position + Vector3.up * 0.9f;
            VictimBrain body = rescue.SpawnVictim(at, transform.eulerAngles.y, state, 0f, 1f, profile);
            if (body == null) return;
            Debug.Log($"[Npc] {player.DisplayName} picks up {Name} ({state})");
            rescue.ServerGiveBody(caller, body);
            Despawn(gameObject);
        }
    }
}
