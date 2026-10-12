using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The map screen, as the mod's MapReader shows it (docs/screens.md, "Map"): where the player
/// stands, where the run can go next, and the rest of the act in a compact form. The state's
/// keys in order: <c>run</c>, <c>player</c> (without potions), <c>potions</c>, <c>boss</c>,
/// <c>at</c>, <c>next</c>, <c>map</c>. The actions: <c>map:&lt;n&gt;</c> for <c>next[n]</c>, then the
/// potion ids. Everything it shows is in the run's <see cref="MapState"/>; the screen itself
/// holds nothing.
/// <para>
/// A director opens it when a room is finished (<c>run.Push(new MapScreen())</c> from its
/// AfterRoom) and has to be an <see cref="IMapDirector"/>: <c>map:n</c> enters the point
/// (<see cref="MapState.Enter"/>), closes every screen and hands the room to
/// <see cref="IMapDirector.EnterRoom"/>.
/// </para>
/// <para>
/// Not imitated, because the arena cannot be in those states: the map opened by hand in a room
/// that is not finished, travel not enabled yet, drawing mode, co-op votes, a second boss, the
/// quest marker of Spoils Map and Fur Coat, and the <c>ancient</c> key, which the game shows only
/// on the first map of a second or third act.
/// </para>
/// </summary>
public sealed class MapScreen : RunScreen
{
    /// <summary>The fixed text of <c>state.map.key</c>.</summary>
    public const string MapKey =
        "rows go upward by row number; each node is cCOL:TYPE^CHILD_COLS (children are in the next row); " +
        "@ marks you; (quest) marks a quest target. next[].ahead: min-max count of each room type over the " +
        "routes from that node to the boss, itself included.";

    /// <summary>The kinds counted along the routes ahead, in the order they are written.</summary>
    private static readonly MapPointType[] Tally =
        [MapPointType.Monster, MapPointType.Elite, MapPointType.Unknown, MapPointType.Shop, MapPointType.RestSite, MapPointType.Treasure];

    public override string Name => "map";

    /// <summary>The word for a kind of point in <c>at</c>, <c>next[].type</c>, <c>visited</c> and <c>rows</c>.</summary>
    public static string TypeWord(MapPointType type) => type switch
    {
        MapPointType.Monster => "monster",
        MapPointType.Elite => "elite",
        MapPointType.RestSite => "rest",
        MapPointType.Shop => "shop",
        MapPointType.Treasure => "treasure",
        MapPointType.Unknown => "unknown",
        MapPointType.Boss => "boss",
        MapPointType.Ancient => "ancient",
        _ => type.ToString().ToLowerInvariant(),
    };

    /// <summary>The words for a kind of point in an action's label ("Go to Rest Site at (3,11)").</summary>
    public static string TypeLabel(MapPointType type) => type switch
    {
        MapPointType.Monster => "Monster",
        MapPointType.Elite => "Elite",
        MapPointType.RestSite => "Rest Site",
        MapPointType.Shop => "Shop",
        MapPointType.Treasure => "Treasure",
        MapPointType.Unknown => "Unknown (?)",
        MapPointType.Boss => "Boss",
        MapPointType.Ancient => "Ancient",
        _ => TypeWord(type),
    };

    public override GameSnapshot Snapshot(Run run)
    {
        var part = run.State.Part<MapState>();
        var map = part.Map;
        var next = part.Next(run);
        if (part.Visited.Count == 0 && map.Start.Type == MapPointType.Ancient)
            throw new NotSupportedException("The arena does not show a map with nothing visited: it needs the title of the act's Ancient, and only a second or third act opens that way.");

        var state = new JsonObject
        {
            ["run"] = run.RunJson(),
            ["player"] = run.PlayerJson(PlayerKeys.NoPotions),
            ["potions"] = RunPotions.Describe(run),
        };
        MapState.AddBoss(state, run);
        if (part.Current is { } here) state["at"] = $"{here} {TypeWord(map.Point(here)!.Type)}";

        var ahead = new Dictionary<MapPoint, (int[] Min, int[] Max)>(ReferenceEqualityComparer.Instance);
        var options = new JsonArray();
        for (var n = 0; n < next.Count; n++)
        {
            var option = new JsonObject { ["n"] = n, ["at"] = next[n].Coord.ToString(), ["type"] = TypeWord(next[n].Type) };
            var summary = Ahead(next[n], ahead);
            if (summary.Length > 0) option["ahead"] = summary;
            options.Add(option);
        }
        state["next"] = options;
        state["map"] = Compact(part, next);

        // Standing on the boss there is nowhere to go: the act ends through the boss's rewards,
        // and a map opened there only waits to be closed. A director never opens it there.
        if (next.Count == 0)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = "nowhere left to travel on this map; close the map to continue", State = state };

        var actions = new List<LegalAction>();
        for (var n = 0; n < next.Count; n++)
            actions.Add(new LegalAction(string.Create(CultureInfo.InvariantCulture, $"map:{n}"), $"Go to {TypeLabel(next[n].Type)} at {next[n].Coord}"));
        actions.AddRange(RunPotions.Legal(run));
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public override ActionResult Execute(Run run, string actionId)
    {
        if (RunPotions.TryExecute(run, actionId, out var potion)) return potion;

        if (!actionId.StartsWith("map:", StringComparison.Ordinal)
            || !int.TryParse(actionId.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return ActionResult.Fail($"Unknown map action '{actionId}'.");

        var part = run.State.Part<MapState>();
        var next = part.Next(run);
        if (n < 0 || n >= next.Count) return ActionResult.Fail($"There is no map option {n}.");
        if (run.Director is not IMapDirector director)
            throw new InvalidOperationException($"{run.Director.GetType().Name} cannot open the rooms of a map: it is not an IMapDirector.");

        var arrival = part.Enter(run, next[n].Coord);
        run.CloseAll();
        director.EnterRoom(run, arrival);
        return ActionResult.Success;
    }

    public override RunScreen Clone(CloneMap map) => new MapScreen();

    // ---------- state.map ----------

    /// <summary>
    /// The compact map: the fixed key, the visited points oldest first, and row by row every
    /// point that still matters: the one the player stands on, everything that can be reached
    /// from the travel options by following the paths, and the boss while it is not behind the
    /// player. Visited points other than the current one and branches out of reach are left out.
    /// </summary>
    private static JsonObject Compact(MapState part, IReadOnlyList<MapPoint> next)
    {
        var map = part.Map;
        var json = new JsonObject { ["key"] = MapKey };
        if (part.Visited.Count > 0)
            json["visited"] = string.Join(", ", part.Visited.Select(coord => $"{coord} {TypeWord(map.Point(coord)!.Type)}"));

        var shown = new HashSet<MapPoint>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<MapPoint>(next);
        while (queue.Count > 0)
        {
            var point = queue.Dequeue();
            if (!shown.Add(point)) continue;
            foreach (var child in point.Children)
                if (!shown.Contains(child)) queue.Enqueue(child);
        }
        var here = part.CurrentPoint;
        if (here is not null) shown.Add(here);
        if (map.Boss.Row >= (here?.Row ?? 0)) shown.Add(map.Boss);

        var rows = new JsonObject();
        foreach (var row in shown.GroupBy(p => p.Row).OrderBy(g => g.Key))
        {
            var text = new StringBuilder();
            foreach (var point in row.OrderBy(p => p.Col))
            {
                if (text.Length > 0) text.Append(' ');
                if (ReferenceEquals(point, here)) text.Append('@');
                text.Append('c').Append(point.Col.ToString(CultureInfo.InvariantCulture)).Append(':').Append(TypeWord(point.Type));
                if (point.Children.Count > 0)
                    text.Append('^').AppendJoin(',', point.Children.Select(c => c.Coord).Order().Select(c => c.Col.ToString(CultureInfo.InvariantCulture)));
            }
            rows[row.Key.ToString(CultureInfo.InvariantCulture)] = text.ToString();
        }
        json["rows"] = rows;
        return json;
    }

    // ---------- next[].ahead ----------

    /// <summary>
    /// "monster 3-5, elite 1-2, rest 1": for each counted kind the fewest and the most rooms of
    /// that kind on a route from the point to the end of the grid, the point itself included.
    /// Each kind is counted by itself, so the two numbers of different kinds can come from
    /// different routes. A kind that no route holds is left out; the boss and the Ancient are
    /// never counted, so the boss has no summary.
    /// </summary>
    private static string Ahead(MapPoint from, Dictionary<MapPoint, (int[] Min, int[] Max)> memo)
    {
        var (min, max) = Counts(from, memo);
        var parts = new List<string>();
        for (var i = 0; i < Tally.Length; i++)
        {
            if (max[i] == 0) continue;
            var kind = TypeWord(Tally[i]);
            parts.Add(min[i] == max[i]
                ? string.Create(CultureInfo.InvariantCulture, $"{kind} {max[i]}")
                : string.Create(CultureInfo.InvariantCulture, $"{kind} {min[i]}-{max[i]}"));
        }
        return string.Join(", ", parts);
    }

    private static (int[] Min, int[] Max) Counts(MapPoint point, Dictionary<MapPoint, (int[] Min, int[] Max)> memo)
    {
        if (memo.TryGetValue(point, out var known)) return known;

        var min = new int[Tally.Length];
        var max = new int[Tally.Length];
        if (point.Children.Count > 0)
        {
            Array.Fill(min, int.MaxValue);
            foreach (var child in point.Children)
            {
                var (childMin, childMax) = Counts(child, memo);
                for (var i = 0; i < Tally.Length; i++)
                {
                    min[i] = Math.Min(min[i], childMin[i]);
                    max[i] = Math.Max(max[i], childMax[i]);
                }
            }
        }

        var self = Array.IndexOf(Tally, point.Type);
        if (self >= 0)
        {
            min[self]++;
            max[self]++;
        }
        memo[point] = (min, max);
        return (min, max);
    }
}
