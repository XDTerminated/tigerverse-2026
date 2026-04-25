namespace Tigerverse.Core
{
    /// <summary>
    /// High-level game state machine for Tigerverse VR.
    /// Title -> Lobby -> DrawWait -> Hatch -> PreBattleReveal -> Battle -> Result.
    /// </summary>
    public enum AppState
    {
        Title,
        Lobby,
        DrawWait,
        Hatch,
        PreBattleReveal,
        Battle,
        Result
    }
}
