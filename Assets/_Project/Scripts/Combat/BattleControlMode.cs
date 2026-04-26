namespace Tigerverse.Combat
{
    /// <summary>
    /// Active control scheme during the battle phase. The local player toggles
    /// between these with the A button.
    ///
    /// Trainer  — player stands still (rotation OK, no locomotion). Voice
    ///            commands route to the local scribble's moveset.
    /// Scribble — player still stands still IRL, but the left joystick moves
    ///            their scribble around the arena (XZ plane) so it can dodge.
    ///            Voice attacks are ignored — only the trainer can call moves.
    /// </summary>
    public enum BattleControlMode
    {
        Trainer = 0,
        Scribble = 1,
    }
}
