using PleaseDontDrown.Player;

namespace PleaseDontDrown.Items
{
    /// <summary>
    /// An item that's used where it lies (the defibrillator, put down by a casualty): while it can be, E on it uses it
    /// instead of picking it up (<see cref="Item.OnInteract"/>); the item offers picking it up on its other button then.
    /// </summary>
    public interface IGroundUse
    {
        bool CanUseOnGround(PlayerHub player);
        string GroundUsePrompt(PlayerHub player);
        /// <summary>Runs on the player's machine (networked ones send a ServerRpc from here).</summary>
        void UseOnGround(PlayerHub player);
    }
}
