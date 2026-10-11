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

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)] // (after DevCommands clears its list)
        private static void Register()
        {
            On = false;
            Core.DevCommands.Register("god", "[on|off]", "God mode for your lifeguard (as in the ESC menu): nothing hurts you (tests, filming).", args =>
            {
                bool want = args.Length == 0 ? !On : args[0] != "off";
                if (want != On) Toggle();
                Core.DevCommands.Print("god mode " + (On ? "on" : "off"));
            }, cheat: true);
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
