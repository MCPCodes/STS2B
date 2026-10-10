namespace SpireMind.Arena;

/// <summary>
/// The arena's own numbers. Everything else about a fight is the game's (v0.107.1) and lives
/// with the content it belongs to.
/// </summary>
public static class Tuning
{
    /// <summary>
    /// The arena's one house rule: a fight that is not won by the end of this turn (the enemy
    /// turn included) is lost, so that stalling can neither win nor run for ever. The game
    /// itself has no turn limit.
    /// </summary>
    public const int MaxTurns = 40;
}
