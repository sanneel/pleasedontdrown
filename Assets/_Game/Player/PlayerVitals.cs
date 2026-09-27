using PleaseDontDrown.Core;
using PleaseDontDrown.UI;
using UnityEngine;

namespace PleaseDontDrown.Player
{
    /// <summary>
    /// Food meter (owner only). It empties slowly over a shift, faster while sprinting or swimming. Hungry lifeguards
    /// get their breath back slowly; starving ones also move slower. Eat coconuts (shake a palm) to fill it up.
    /// </summary>
    public class PlayerVitals : MonoBehaviour
    {
        [SerializeField] private PlayerHub _hub;
        [Tooltip("Seconds for a full meter to run empty while taking it easy.")]
        [SerializeField] private float _secondsToEmpty = 720f;
        [SerializeField] private float _activeDrain = 1.8f;
        [SerializeField] private float _hungryBelow = 0.25f;

        private bool _warned;

        public float Food01 { get; private set; } = 0.8f;
        public bool Hungry => Food01 < _hungryBelow;
        public bool Starving => Food01 <= 0.001f;
        public float LastAteTime { get; private set; } = -100f;

        private void OnEnable() => DevCommands.Register("food", "[0..1]", "Show or set your food meter.", args =>
        {
            if (args.Length > 0) Food01 = Mathf.Clamp01(DevCommands.ParseFloat(args, 0));
            DevCommands.Print($"food {Food01:P0}{(Hungry ? " (hungry)" : "")}");
        }, cheat: true, owner: this);

        private void OnDisable() => DevCommands.Unregister("food", this);

        public void Eat(float amount)
        {
            Food01 = Mathf.Min(1f, Food01 + amount);
            LastAteTime = Time.time;
            _warned = Hungry;
            if (_hub.Motor != null) _hub.Motor.RestoreStamina();
        }

        private void Update()
        {
            if (!_hub.IsOwner) return;
            PlayerMotor motor = _hub.Motor;
            bool active = motor != null && (motor.IsSprinting || motor.IsSwimming);
            Food01 = Mathf.Max(0f, Food01 - Time.deltaTime / _secondsToEmpty * (active ? _activeDrain : 1f));

            if (motor != null)
            {
                motor.StaminaRefillScale = Starving ? 0.25f : Hungry ? 0.55f : 1f;
                motor.HungerSpeedScale = Starving ? 0.82f : 1f;
            }

            if (Hungry && !_warned)
            {
                _warned = true;
                PlayerHud.ShowToast("<b>You're getting hungry.</b> Shake a palm tree for coconuts, then hold [RMB] to eat one.", 6f);
            }
            else if (!Hungry) _warned = false;
        }
    }
}
