using PleaseDontDrown.Interaction;
using PleaseDontDrown.Player;
using UnityEngine;

namespace PleaseDontDrown.World
{
    /// <summary>
    /// Island 3's sewer (GameSceneBuilder.Island3Palace): the ladder at the tunnel's end climbs up through the drain into
    /// the castle yard, and the grate in the yard climbs down into the tunnel. Press E: you come out at the other end
    /// (the shaft between is too narrow to climb by hand), and the grate swings open for a moment (on the climber's
    /// screen: it's only a lid, nothing to sync). Movement is the player's own, so the teleport is local.
    /// </summary>
    public sealed class SewerLadder : MonoBehaviour, IInteractionHandler
    {
        [SerializeField] private Transform _to;
        [SerializeField] private string _prompt = "Climb up the ladder";
        [Tooltip("The grate over the drain (swings up while someone goes through).")]
        [SerializeField] private Transform _grate;

        private static float _openUntil = -1f;
        private Quaternion _grateShut;
        private bool _hasGrate;

        public bool CanInteract(PlayerHub player) => player != null && _to != null && player.Motor != null && player.Motor.Seat == null;

        public string GetPrompt(PlayerHub player) => _prompt;

        public void OnInteract(PlayerHub player)
        {
            if (player == null || !player.IsOwner || _to == null) return;
            _openUntil = Time.time + 2.5f;
            player.Motor.Teleport(_to.position);
            player.Look.LookAt(_to.position + _to.forward * 4f + Vector3.up * 1.4f);
        }

        private void Awake()
        {
            _hasGrate = _grate != null;
            if (_hasGrate) _grateShut = _grate.localRotation;
        }

        private void Update()
        {
            if (!_hasGrate) return;
            // Open (up on its hinge) while anyone just went through, then down again.
            Quaternion want = Time.time < _openUntil ? _grateShut * Quaternion.Euler(0f, 0f, 75f) : _grateShut;
            _grate.localRotation = Quaternion.RotateTowards(_grate.localRotation, want, 220f * Time.deltaTime);
        }
    }
}
