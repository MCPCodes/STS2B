using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// What entering a map point gave: where, what kind of point, and the room to open there. The
/// room is a <see cref="RoomKind"/> (the game's room type numbers): most kinds of point give
/// one kind of room; an Unknown point's room is rolled when it is entered, and the start point
/// (the act's Ancient) is an Event room.
/// </summary>
public readonly record struct MapArrival(MapCoord Coord, MapPointType PointType, RoomKind Room);

/// <summary>
/// One visited point of the act: its place, its kind, and every room entered on it. That is one
/// room, unless an event started a fight there (<see cref="MapState.AddRoom"/>): the game's
/// history entry of a map point.
/// </summary>
public sealed class MapVisit
{
    private readonly List<RoomKind> _rooms;

    internal MapVisit(MapCoord coord, MapPointType pointType, IEnumerable<RoomKind> rooms)
    {
        Coord = coord;
        PointType = pointType;
        _rooms = rooms.ToList();
    }

    public MapCoord Coord { get; }
    public MapPointType PointType { get; }

    /// <summary>The rooms entered on this point, the point's own room first.</summary>
    public IReadOnlyList<RoomKind> Rooms => _rooms;

    internal void Add(RoomKind room) => _rooms.Add(room);

    internal MapVisit Clone() => new(Coord, PointType, _rooms);
}

/// <summary>
/// What the director of a run that travels on a map has to offer besides
/// <see cref="RunDirector"/>'s own rules. The map screen calls <see cref="EnterRoom"/> when the
/// player has chosen the next point.
/// </summary>
public interface IMapDirector
{
    /// <summary>
    /// The run has entered a map point (the path, the floors and, for an Unknown point, the roll
    /// are done; every screen is closed): open its room. A Monster, Elite or Boss room takes the
    /// act's next list entry and marks the list afterwards (<see cref="MapState.MarkRoomVisited"/>).
    /// </summary>
    void EnterRoom(Run run, MapArrival arrival);

    /// <summary>
    /// A house rule of the arena (plan 5.1), off unless a director turns it on: whether the
    /// arena can show what an Unknown room was rolled to be. When it cannot (an Event room while
    /// no built event is allowed and unmet) the roll is made again from the same stream with
    /// that kind barred, as the game itself rolls when a relic bars a kind: with the Event out,
    /// a number above every bar gives the lowest kind left, a Monster room. The refused number
    /// is used up and leaves the odds alone. The game has no such rule; a run that must be the
    /// game's own answers true and ends itself as unrepresentable. When every kind is refused
    /// the roll fails with an exception, and the action that travelled is undone.
    /// </summary>
    bool CanHold(Run run, RolledRoom room) => true;
}

/// <summary>
/// The map of the act the run is in and the path taken on it: one part of the run's state
/// (<c>run.State.Part&lt;MapState&gt;()</c>), copied with the run.
/// <code>
/// var map = MapState.BeginAct(run, 0);   // a new act: its map is generated, nothing is visited
/// map.Enter(run, map.Map.Start.Coord);   // the game enters a first act's start point itself: Neow's room
/// map.Next(run)                          // where the player may go now
/// run.Push(new MapScreen());             // the screen; its map:n enters the point and calls IMapDirector.EnterRoom
/// </code>
/// The rules are the game's. Entering a point adds it to the path, sets the act floor to the
/// row plus 1, raises the total floor by one and decides the room; an Unknown point rolls its
/// room on the run's <c>unknown_map_point</c> stream against odds that grow
/// (<see cref="UnknownRoomOdds"/>), with Shop barred when the point before held a shop or when
/// every point after this one is a shop.
/// </summary>
public sealed class MapState : RunPart
{
    private ActMap? _map;
    private List<MapCoord> _visited = new();
    private List<MapVisit> _history = new();

    /// <summary>False until the first act's map is generated.</summary>
    public bool HasMap => _map is not null;

    /// <summary>The map of the current act.</summary>
    /// <exception cref="InvalidOperationException">No map was generated yet.</exception>
    public ActMap Map => _map ?? throw new InvalidOperationException("The run has no map yet: call MapState.BeginAct(run, actIndex) first.");

    /// <summary>The places visited in this act, oldest first. The last one is where the player stands.</summary>
    public IReadOnlyList<MapCoord> Visited => _visited;

    /// <summary>One entry per visited point of this act, in the same order as <see cref="Visited"/>.</summary>
    public IReadOnlyList<MapVisit> History => _history;

    /// <summary>Where the player stands, or null when nothing is visited in this act.</summary>
    public MapCoord? Current => _visited.Count > 0 ? _visited[^1] : null;

    public MapPoint? CurrentPoint => Current is { } coord ? Map.Point(coord) : null;

    // ---------- a new act ----------

    /// <summary>
    /// Sets the run up for an act as the game does: the act index, the act floor back to 0, the
    /// path cleared, the odds of an Unknown room back to their start values, and the act's map
    /// generated from its own stream ("act_1_map" for the first act). Nothing is entered: in a
    /// first act the game enters the start point at once (<see cref="Enter"/> with
    /// <c>Map.Start.Coord</c>), in a later act the map opens with the start point as its one option.
    /// </summary>
    /// <exception cref="InvalidOperationException">The run's world was not begun.</exception>
    public static MapState BeginAct(Run run, int actIndex) => BeginAct(run, actIndex, (ActMapRules?)null);

    /// <summary>The same with map rules that are not the act's own: for checks against real runs of more than one
    /// player or of an ascension level, whose maps have a row less or more elites.</summary>
    internal static MapState BeginAct(Run run, int actIndex, ActMapRules? rules) =>
        BeginAct(run, actIndex, (world, rng) => ActMapGenerator.Generate(rng, rules ?? ActMapRules.For(world.Acts[actIndex])));

    /// <summary>The same with a map made by hand, for checks: nothing is generated.</summary>
    internal static MapState BeginAct(Run run, int actIndex, ActMap map) => BeginAct(run, actIndex, (_, _) => map);

    private static MapState BeginAct(Run run, int actIndex, Func<RunWorld, GameRng, ActMap> make)
    {
        var state = run.State;
        var world = state.Part<RunWorld>();
        if (!world.IsBegun) throw new InvalidOperationException("The run's world was not begun: call RunWorld.Begin(run.State) before the first act's map.");
        if (actIndex < 0 || actIndex >= world.Acts.Count) throw new ArgumentOutOfRangeException(nameof(actIndex), $"The run has {world.Acts.Count} acts.");

        var map = state.Part<MapState>();
        state.ActIndex = actIndex;
        state.ActFloor = 0;
        map._visited.Clear();
        map._history.Clear();
        world.UnknownOdds.ResetToBase();
        map._map = make(world, world.Streams.ActMap(actIndex + 1));
        return map;
    }

    // ---------- travel ----------

    /// <summary>
    /// Where the player may go now, sorted by row and then column: the list the mod shows as
    /// <c>next</c> and numbers its <c>map:n</c> ids by. With nothing visited in the act it is the
    /// start point alone; on the last row of the grid the boss alone; on the boss nothing (the
    /// act ends through the boss's rewards); otherwise the children of the point the player
    /// stands on, or every point of the next row while a relic allows free travel.
    /// </summary>
    public IReadOnlyList<MapPoint> Next(Run run)
    {
        var map = Map;
        if (_visited.Count == 0) return [map.Start];

        var here = _visited[^1];
        if (here == map.Boss.Coord) return [];
        if (here.Row == map.Rows - 1) return [map.Boss];
        if (map.Point(here) is not { } from) return [map.Start];

        IEnumerable<MapPoint> reachable = RunHooks.ShouldAllowFreeTravel(run.Context) ? map.PointsInRow(from.Row + 1) : from.Children;
        return reachable.Distinct().OrderBy(p => p.Row).ThenBy(p => p.Col).ToList();
    }

    /// <summary>
    /// The run enters a map point, as the game does when the player's choice is in: the place
    /// joins the path, the act floor becomes the row plus 1, the streams are told the place, the
    /// room is decided (an Unknown point rolls), the visit is recorded and the total floor goes
    /// up by one. The caller then opens the room.
    /// </summary>
    /// <exception cref="InvalidOperationException">The point is not one the player may travel to now.</exception>
    public MapArrival Enter(Run run, MapCoord coord)
    {
        var options = Next(run);
        var point = options.FirstOrDefault(p => p.Coord == coord)
            ?? throw new InvalidOperationException($"The run cannot travel to {coord} now; its options are {string.Join(", ", options.Select(p => p.Coord))}.");

        var state = run.State;
        var world = state.Part<RunWorld>();
        var before = _history.Count > 0 ? _history[^1] : null;

        _visited.Add(coord);
        state.ActFloor = coord.Row + 1;
        world.Streams.MoveTo(state.ActIndex, coord.Row, coord.Col);

        var room = point.Type switch
        {
            MapPointType.Unknown => RollUnknown(run, world, point, before),
            MapPointType.Shop => RoomKind.Shop,
            MapPointType.Treasure => RoomKind.Treasure,
            MapPointType.RestSite => RoomKind.RestSite,
            MapPointType.Monster => RoomKind.Monster,
            MapPointType.Elite => RoomKind.Elite,
            MapPointType.Boss => RoomKind.Boss,
            MapPointType.Ancient => RoomKind.Event,
            _ => throw new InvalidOperationException($"The map point at {coord} has no kind."),
        };

        _history.Add(new MapVisit(coord, point.Type, [room]));
        state.TotalFloor++;
        return new MapArrival(coord, point.Type, room);
    }

    /// <summary>
    /// What an Unknown point holds. Shop is barred when a room on the point before was a shop, or
    /// when this point has children and every one of them is a shop; the relics then take away
    /// what they bar; then one number on <c>unknown_map_point</c> decides
    /// (<see cref="UnknownRoomOdds.Roll"/>).
    /// </summary>
    private static RoomKind RollUnknown(Run run, RunWorld world, MapPoint point, MapVisit? before)
    {
        var shopBarred = (before is not null && before.Rooms.Contains(RoomKind.Shop))
            || (point.Children.Count > 0 && point.Children.All(child => child.Type == MapPointType.Shop));
        var allowed = RunHooks.ModifyUnknownMapPointRoomTypes(run.Context, UnknownRoomOdds.Allowed(shopBarred));

        var rng = world.Streams.Draw(RunStream.UnknownMapPoint, Purpose.UnknownRoom);
        var director = run.Director as IMapDirector;
        while (true)
        {
            // Looked at on copies first, so that a result the arena cannot show (the house rule
            // of IMapDirector.CanHold) leaves the odds as they were; its number is used up.
            var rolled = world.UnknownOdds.Clone().Roll(rng.Clone(), allowed);
            if (director is null || director.CanHold(run, rolled))
            {
                world.UnknownOdds.Roll(rng, allowed);
                return RoomKinds.Of(rolled);
            }

            rng.NextFloat();
            var fewer = new HashSet<RolledRoom>(allowed);
            fewer.Remove(rolled);
            if (fewer.Count == 0)
                throw new InvalidOperationException($"The arena can show none of the rooms the Unknown point at {point.Coord} may hold.");
            allowed = fewer;
        }
    }

    /// <summary>
    /// A second room was entered on the point the player stands on: a fight an event started.
    /// The game records it with the point, and a shop among a point's rooms bars Shop for an
    /// Unknown point entered next.
    /// </summary>
    public void AddRoom(RoomKind room)
    {
        if (_history.Count == 0) throw new InvalidOperationException("No map point is visited yet.");
        _history[^1].Add(room);
    }

    /// <summary>
    /// Moves the act's list counter for a room that was entered as a map point's own room, as the
    /// game does once the room is set up: the hallway list for a Monster room, the elite list,
    /// the boss, and the event list for an Event room, the Ancient's included. Other rooms move
    /// nothing. Call it after the room took its list entry, never for a fight an event started.
    /// </summary>
    public static void MarkRoomVisited(Run run, RoomKind room)
    {
        var state = run.State;
        var rooms = state.Part<RunWorld>().Rooms[state.ActIndex];
        switch (room)
        {
            case RoomKind.Monster: rooms.MarkVisited(RoomType.Monster); break;
            case RoomKind.Elite: rooms.MarkVisited(RoomType.Elite); break;
            case RoomKind.Boss: rooms.MarkVisited(RoomType.Boss); break;
            case RoomKind.Event: rooms.MarkEventVisited(); break;
        }
    }

    // ---------- the boss key of the screens ----------

    /// <summary>
    /// The game's English titles of the six first-act boss encounters (its text table's
    /// <c>title</c> of each encounter, as the verified encounter notes record them). The bosses
    /// of the second and third act are not in scope.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> BossTitles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["VantomBoss"] = "Vantom",
        ["CeremonialBeastBoss"] = "Ceremonial Beast",
        ["TheKinBoss"] = "The Kin",
        ["WaterfallGiantBoss"] = "Waterfall Giant",
        ["SoulFyshBoss"] = "Soul Fysh",
        ["LagavulinMatriarchBoss"] = "Lagavulin Matriarch",
    };

    /// <summary>
    /// The value of the <c>boss</c> key the mod writes on the map, the rewards, the card reward,
    /// the shop and the rest site: the title of the act's boss encounter, named from the start
    /// of the act. Null while the player stands on the boss's point (its rewards screen): a boss
    /// already beaten is not named again, and the key is then left out.
    /// </summary>
    /// <exception cref="NotSupportedException">The act's boss is not a first-act boss: the arena does not hold its title.</exception>
    public static string? BossTitle(Run run)
    {
        var state = run.State;
        var world = state.FindPart<RunWorld>();
        if (world is not { IsBegun: true }) return null;
        var map = state.FindPart<MapState>();
        if (map is { HasMap: true } && map.Current == map.Map.Boss.Coord) return null;

        var boss = world.Rooms[state.ActIndex].Boss;
        return BossTitles.TryGetValue(boss, out var title)
            ? title
            : throw new NotSupportedException($"The arena does not hold the title of the boss encounter {boss}: only first-act bosses are in scope.");
    }

    /// <summary>Writes the <c>boss</c> key into a screen's state when there is a boss to name (<see cref="BossTitle"/>).</summary>
    public static void AddBoss(JsonObject state, Run run)
    {
        if (BossTitle(run) is { } boss) state["boss"] = boss;
    }

    // ---------- copies and dumps ----------

    public override RunPart Clone(CloneMap map) => new MapState
    {
        // A generated map never changes, so the copy shares it.
        _map = _map,
        _visited = _visited.ToList(),
        _history = _history.Select(visit => visit.Clone()).ToList(),
    };

    public override JsonNode? Dump() => new JsonObject
    {
        ["map"] = _map?.Describe(),
        ["visited"] = string.Join(" ", _visited),
        ["rooms"] = string.Join(" ", _history.Select(visit => $"{visit.PointType}:{string.Join("+", visit.Rooms)}")),
    };
}
