using System.Text.Json.Nodes;

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

    // Screens that belong to another room, or to leaving one: the map is where the bot picks
    // the next room, and an act's first room after a boss is an event.
    private static readonly HashSet<string> LeftFight = new(StringComparer.Ordinal)
    {
        "map", "shop", "rest", "event", "crystal_sphere", "menu", "game_over",
    };

    /// <summary>True on a run screen, false at the menu or game over, null when the screen
    /// doesn't tell (a modal, an error, an unknown screen), so the caller keeps its last answer.</summary>
    public static bool? InRun(string? screen) => screen switch
    {
        { } s when During.Contains(s) => true,
        "menu" or "game_over" => false,
        _ => null,
    };

    /// <summary>
    /// The elite or boss room the bot is in after <paramref name="snapshot"/>, given the one it
    /// was in before (<paramref name="current"/>): <see cref="BrainSettings.EliteRoom"/>,
    /// <see cref="BrainSettings.BossRoom"/> or null. Only combat (and game over) snapshots name
    /// their room (run.room, or room), so a fight room holds through the room's own later screens
    /// (rewards, card picks, a hand or card select, treasure, a modal) until a snapshot names
    /// another room or shows a screen of leaving it (map, shop, rest, event, crystal_sphere, menu,
    /// game_over).
    /// </summary>
    public static string? FightRoom(string? current, JsonObject snapshot)
    {
        var screen = Str(snapshot["screen"]);
        if (screen is not null && LeftFight.Contains(screen))
            return null;
        if (snapshot["in_run"] is JsonValue flag && flag.TryGetValue<bool>(out var inRun) && !inRun)
            return null;
        var state = snapshot["state"] as JsonObject;
        var room = Str(state?["run"]?["room"]) ?? Str(state?["room"]);
        if (room is null)
            return current;
        return string.Equals(room, BrainSettings.EliteRoom, StringComparison.OrdinalIgnoreCase) ? BrainSettings.EliteRoom
            : string.Equals(room, BrainSettings.BossRoom, StringComparison.OrdinalIgnoreCase) ? BrainSettings.BossRoom
            : null;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
