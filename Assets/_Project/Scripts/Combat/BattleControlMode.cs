namespace Tigerverse.Combat
{
    /// <summary>
    /// Active control scheme during the battle phase. The local player toggles
    /// between these with the A button on the right XR controller (or M in
    /// the editor).
    ///
    /// Scribble — DEFAULT. Player aims with the left joystick (a reticle hovers
    ///            in front of their monster). Voice commands fire the named
    ///            move as a projectile in the aimed direction. Damage only
    ///            on actual hit. Per-player cooldown prevents voice-spam.
    /// Artist  — Joystick translates the local monster around the arena (XZ
    ///            plane) so it can dodge incoming projectiles. Voice commands
    ///            are intentionally ignored in this mode.
    ///
    /// Trainer locomotion (XR continuous move + flat-screen WASD) is locked
    /// in BOTH modes by BattleControlModeManager.Configure — only the
    /// monster moves, never the player.
    /// </summary>
    public enum BattleControlMode
    {
        Scribble = 0,
        Artist   = 1,
    }
}
