namespace PleaseDontDrown.Player
{
    /// <summary>
    /// God mode (ESC menu, your own lifeguard only): endless air and stamina, never hungry, never knocked about.
    /// Fly goes with it: fly through everything (the noclip), Jump up, Crouch down, Sprint faster.
    /// </summary>
    public static class GodMode
    {
        public static bool On { get; private set; }

        public static bool Flying => PlayerHub.Local != null && PlayerHub.Local.Motor != null && PlayerHub.Local.Motor.Noclip;

        public static void Toggle()
        {
            On = !On;
            if (!On && Flying) ToggleFly();
        }

        public static void ToggleFly()
        {
            PlayerMotor motor = PlayerHub.Local != null ? PlayerHub.Local.Motor : null;
            if (motor == null) return;
            if (!motor.Noclip && !On) On = true;
            motor.SetNoclip(!motor.Noclip);
        }
    }
}
