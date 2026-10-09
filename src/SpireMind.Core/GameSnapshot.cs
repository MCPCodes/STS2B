using System.Text.Json.Nodes;

namespace SpireMind.Core;

/// <summary>
/// One thing the bot is allowed to do right now. Ids are short and only valid for the
/// snapshot they came from, e.g. "play:2>e0" (hand card 2 at enemy 0), "end_turn",
/// "map:3", "reward:1", "skip".
/// </summary>
public sealed record LegalAction(string Id, string Label);

/// <summary>
/// What the bot sees: the current screen, the facts it needs to decide, and the full
/// list of legal actions. The game side builds these; the bridge stamps the version.
/// </summary>
public sealed class GameSnapshot
{
    /// <summary>Bumped by <see cref="SnapshotVersioner"/> whenever state or actions change.</summary>
    public long Version { get; set; }

    /// <summary>combat, map, rewards, card_reward, shop, rest, event, treasure, card_select,
    /// character_select, menu, game_over, or busy while animations and enemy turns play out.</summary>
    public required string Screen { get; init; }

    /// <summary>True when the local player has a choice to make and <see cref="Actions"/> is usable.</summary>
    public bool AwaitingDecision { get; init; }

    /// <summary>Short human-readable reason when not awaiting a decision ("enemy turn", "teammate choosing").</summary>
    public string? Waiting { get; init; }

    /// <summary>Screen-specific facts: hand, enemies, map nodes, shop stock and so on.</summary>
    public JsonObject State { get; init; } = new();

    public IReadOnlyList<LegalAction> Actions { get; init; } = Array.Empty<LegalAction>();

    public static GameSnapshot Busy(string screen, string why) =>
        new() { Screen = screen, AwaitingDecision = false, Waiting = why };

    public JsonObject ToJson()
    {
        var actions = new JsonArray();
        foreach (var a in Actions)
            actions.Add(new JsonObject { ["id"] = a.Id, ["label"] = a.Label });

        var json = new JsonObject
        {
            ["version"] = Version,
            ["screen"] = Screen,
            ["awaiting_decision"] = AwaitingDecision,
        };
        if (Waiting is not null) json["waiting"] = Waiting;
        json["state"] = State.DeepClone();
        json["actions"] = actions;
        return json;
    }
}

public sealed record ActionResult(bool Ok, string? Error = null)
{
    public static readonly ActionResult Success = new(true);
    public static ActionResult Fail(string error) => new(false, error);
}
