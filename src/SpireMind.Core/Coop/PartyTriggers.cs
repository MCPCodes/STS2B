using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SpireMind.Core.Coop;

/// <summary>A planning session one snapshot calls for: its key ("A2-start"), kind (act_start,
/// mid or boss) and act, and a short reason for the log ("event, ancient").</summary>
public sealed record PartyDue(string Key, string Kind, int Act, string Why);

/// <summary>
/// Decides from one snapshot whether the party's planning session is due, from fields the
/// readers already emit: state.run.act, state.ancient on an Ancient's event, state.at on the map
/// ("(c,r) type") and state.map.rows, whose row strings hold nodes like "c3:boss".
/// <list type="bullet">
/// <item>act_start: an Ancient's event, or the act's map before its first room (no at yet).</item>
/// <item>mid: the map once the row reached is at least half the boss row, while more than two
/// rows are left before it.</item>
/// <item>boss: the map from two rows before the boss row, where the next pick is normally the rest
/// site, so Mend and Smith calls come in time.</item>
/// </list>
/// The second boss is ignored. Pure: settled keys and resets are <see cref="PartyBot"/>'s.
/// </summary>
public static class PartyTriggers
{
    public const string ActStart = "act_start";
    public const string MidAct = "mid";
    public const string Boss = "boss";
    public const string Manual = "manual";

    public static string StartKey(int act) => $"A{act.ToString(CultureInfo.InvariantCulture)}-start";
    public static string MidKey(int act) => $"A{act.ToString(CultureInfo.InvariantCulture)}-mid";
    public static string BossKey(int act) => $"A{act.ToString(CultureInfo.InvariantCulture)}-boss";

    /// <summary>The session <paramref name="snapshot"/> (a snapshot's JSON: screen and state)
    /// calls for, or null.</summary>
    public static PartyDue? Due(JsonObject snapshot) =>
        snapshot["screen"] is JsonValue v && v.TryGetValue<string>(out var screen) && snapshot["state"] is JsonObject state
            ? Due(screen, state)
            : null;

    /// <summary>The session a snapshot with this screen and state calls for, or null.</summary>
    public static PartyDue? Due(string screen, JsonObject state)
    {
        if (Act(state) is not { } act)
            return null;
        switch (screen)
        {
            case "event":
                return state["ancient"] is JsonValue a && a.TryGetValue<bool>(out var ancient) && ancient
                    ? new PartyDue(StartKey(act), ActStart, act, "event, ancient")
                    : null;
            case "map":
                if (state["at"] is null)
                    return new PartyDue(StartKey(act), ActStart, act, "map, before the first room");
                if (Row(state) is not { } r || BossRow(state) is not { } bossRow || r >= bossRow)
                    return null;
                if (r >= bossRow - 2)
                    return new PartyDue(BossKey(act), Boss, act, $"map, row {r} of {bossRow}");
                if (2 * r >= bossRow)
                    return new PartyDue(MidKey(act), MidAct, act, $"map, row {r} of {bossRow}");
                return null;
            default:
                return null;
        }
    }

    /// <summary>state.run.act, or null.</summary>
    public static int? Act(JsonObject state) =>
        state["run"]?["act"] is JsonValue v && v.TryGetValue<int>(out var act) && act > 0 ? act : null;

    /// <summary>The row of state.at ("(3,7) monster" gives 7), or null when it doesn't read.</summary>
    public static int? Row(JsonObject state) =>
        state["at"] is JsonValue v && v.TryGetValue<string>(out var at) && AtPattern().Match(at) is { Success: true } m
        && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : null;

    /// <summary>The lowest row key of state.map.rows whose string holds a boss node, or null.</summary>
    public static int? BossRow(JsonObject state)
    {
        if (state["map"]?["rows"] is not JsonObject rows)
            return null;
        int? best = null;
        foreach (var (key, value) in rows)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var row))
                continue;
            if (value is JsonValue v && v.TryGetValue<string>(out var text) && text.Contains(":boss", StringComparison.Ordinal)
                && (best is null || row < best))
                best = row;
        }
        return best;
    }

    /// <summary>The order of a session key's kind within an act: start, mid, boss.</summary>
    public static int Rank(string kind) => kind switch
    {
        ActStart => 0,
        MidAct => 1,
        Boss => 2,
        _ => -1,
    };

    private static readonly Regex AtRegex = new(@"^\((\d+),(\d+)\)", RegexOptions.CultureInvariant);

    private static Regex AtPattern() => AtRegex;
}
