namespace SpireMind.Core.Brain;

/// <summary>Whether a snapshot's screen means a run is in progress.</summary>
public static class RunScreens
{
    // Screens that only exist during a run. "menu" and "game_over" are outside one; anything
    // else (a modal, which can also be up at the menu, an error, an unknown screen) says nothing.
    private static readonly HashSet<string> During = new(StringComparer.Ordinal)
    {
        "combat", "hand_select", "map", "rewards", "card_reward", "card_select", "shop", "rest", "event", "treasure", "crystal_sphere",
    };

    /// <summary>True on a run screen, false at the menu or game over, null when the screen
    /// doesn't tell (a modal, an error, an unknown screen), so the caller keeps its last answer.</summary>
    public static bool? InRun(string? screen) => screen switch
    {
        { } s when During.Contains(s) => true,
        "menu" or "game_over" => false,
        _ => null,
    };
}
