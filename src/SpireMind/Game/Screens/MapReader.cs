using System.Text;
using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// The act map while it is open: where we are, which points we can travel to next, and a
/// compact description of the rest of the act so the model can plan a route. Travel is a
/// click on the map point, which the game turns into a synced map vote (docs/game-api.md
/// section d), so co-op stays in step. Actions: "map:&lt;n&gt;" for state.next[n], plus
/// out-of-combat potion actions.
/// </summary>
internal sealed class MapReader : IScreen
{
    public static readonly MapReader Instance = new();

    public string Name => "map";

    private static readonly TimeSpan TravelTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Give a co-op vote time to reach the action queue before "idle" can mean "registered".</summary>
    private static readonly TimeSpan VoteSettle = TimeSpan.FromMilliseconds(500);

    /// <summary>Room types counted along routes for the per-option summary, in display order.</summary>
    private static readonly string[] Tally = ["monster", "elite", "unknown", "shop", "rest", "treasure"];

    // The bridge serializes with the default JSON encoder, which turns < > ' + into \uXXXX
    // escapes, so the map strings avoid those characters.
    private const string MapKey =
        "rows go upward by row number; each node is cCOL:TYPE^CHILD_COLS (children are in the next row); " +
        "@ marks you; (quest) marks a quest target. next[].ahead: min-max count of each room type over the " +
        "routes from that node to the boss, itself included.";

    private MapReader() { }

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        var screen = node as NMapScreen ?? NMapScreen.Instance;
        if (screen is null || !GodotObject.IsInstanceValid(screen) || !screen.IsOpen)
            return GameSnapshot.Busy(Name, "map closing");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");
        if (screen.IsTraveling)
            return GameSnapshot.Busy(Name, "travelling on the map");
        if (CombatManager.Instance?.IsInProgress == true)
            return GameSnapshot.Busy(Name, "map opened during combat; close it to continue");

        MapView view;
        try
        {
            view = MapView.Read(rs);
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"reading map: {ex.Message}");
            return GameSnapshot.Busy(Name, "map not readable yet");
        }
        if (view.Spots.Count == 0)
            return GameSnapshot.Busy(Name, "map not generated yet");

        var state = BuildState(ctx, rs, me, view);

        // Our vote first: the game may switch travel off locally once we have voted.
        string? waiting = null;
        if (CurrentVote(rs) is { } vote)
            waiting = $"waiting for teammates to vote on the map (you voted {Show(vote.Target)})";
        else if (!screen.IsTravelEnabled)
            waiting = "map open but travel not enabled (viewing only, or still animating)";

        var actions = new List<LegalAction>();
        if (waiting is null)
        {
            for (var i = 0; i < view.Next.Count; i++)
            {
                var at = view.Next[i];
                if (FindPointNode(screen, rs.Map, at, fresh: false) is { IsEnabled: true })
                    actions.Add(new LegalAction($"map:{i}", $"Go to {Label(view.TypeAt(at))} at {Show(at)}"));
            }
            if (actions.Count == 0)
                waiting = "no map point is selectable yet";
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        actions.AddRange(PotionActions.Legal(ctx, false));
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (PotionActions.TryExecute(ctx, actionId, false, out var potionResult))
            return potionResult;

        if (!actionId.StartsWith("map:", StringComparison.Ordinal) || !int.TryParse(actionId.AsSpan(4), out var n))
            return ActionResult.Fail($"Unknown map action '{actionId}'.");

        if (ctx.Run is not { } rs)
            return ActionResult.Fail("No run in progress.");

        var screen = node as NMapScreen ?? NMapScreen.Instance;
        if (screen is null || !GodotObject.IsInstanceValid(screen) || !screen.IsOpen)
            return ActionResult.Fail("The map is not open.");
        if (screen.IsTraveling)
            return ActionResult.Fail("Already travelling.");
        if (!screen.IsTravelEnabled)
            return ActionResult.Fail("Travel is not enabled on the map yet.");
        if (CombatManager.Instance?.IsInProgress == true)
            return ActionResult.Fail("A combat is in progress.");
        if (CurrentVote(rs) is { } vote)
            return ActionResult.Fail($"Already voted for {Show(vote.Target)}; waiting for teammates.");

        MapView view;
        try
        {
            view = MapView.Read(rs);
        }
        catch (Exception ex)
        {
            return ActionResult.Fail($"Could not read the map: {ex.Message}");
        }

        if (n < 0 || n >= view.Next.Count)
            return ActionResult.Fail($"There is no map option {n}.");

        var target = view.Next[n];
        var point = FindPointNode(screen, rs.Map, target, fresh: true);
        if (point is null)
            return ActionResult.Fail($"No map point node at {Show(target)}.");
        if (!point.IsEnabled)
            return ActionResult.Fail($"The map point at {Show(target)} is not selectable right now.");

        var what = $"travel to {Label(view.TypeAt(target))} at {Show(target)}";
        var click = Clicks.Once(point, () => point.ForceClick(), $"map point {Show(target)}");
        if (!click.Ok)
            return click;

        // In co-op the click only casts our vote; the party moves once everyone has voted.
        var multiplayer = ctx.IsMultiplayer;
        var roomBefore = rs.CurrentRoom;
        if (multiplayer)
            _vote = new Vote(rs.Map, roomBefore, view.Visited.Count, target);

        ActionTracker.Begin(
            what,
            c => Arrived(c.Run, target, roomBefore) || (multiplayer && ActionTracker.GameLooksIdle()),
            TravelTimeout,
            minWait: multiplayer ? VoteSettle : null);
        return ActionResult.Success;
    }

    // ---------- state ----------

    private static JsonObject BuildState(GameContext ctx, RunState rs, Player me, MapView view)
    {
        // Potion actions are offered here, so show what each potion does (as the rewards and
        // rest readers do) instead of the bare names.
        var player = Describe.PlayerRun(me, includeDeck: true);
        player.Remove("potions");
        var state = new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["player"] = player,
            ["potions"] = PotionActions.Describe(ctx, false),
        };

        // Co-op routes should suit the weakest deck, so teammates' HP matters here.
        if (ctx.IsMultiplayer && Describe.TeamHp(ctx.Teammates) is { Count: > 0 } team)
            state["team"] = team;

        var act = rs.Act;
        if (act is not null)
        {
            var boss = Describe.SafeText(() => act.BossEncounter?.Title); // VERIFY: EncounterModel has a Title (LocString) for the boss name
            if (boss.Length > 0) state["boss"] = boss;
            var second = Describe.SafeText(() => act.SecondBossEncounter?.Title); // VERIFY: same Title member on the second boss encounter, and the map shows humans the second boss before the first is beaten
            if (second.Length > 0) state["second_boss"] = second;

            // Only useful before we reach it; afterwards the event itself is on screen.
            if (view.At is null)
            {
                var ancient = Describe.SafeText(() => act.Ancient?.Title); // VERIFY: ActModel.Ancient has a Title, and the map shows humans which Ancient it is before arrival
                if (ancient.Length > 0) state["ancient"] = ancient;
            }
        }

        if (view.At is { } here)
            state["at"] = $"{Show(here)} {view.TypeAt(here)}";

        var next = new JsonArray();
        for (var i = 0; i < view.Next.Count; i++)
        {
            var at = view.Next[i];
            var option = new JsonObject { ["n"] = i, ["at"] = Show(at), ["type"] = view.TypeAt(at) };
            var ahead = view.Ahead(at);
            if (ahead.Length > 0) option["ahead"] = ahead;
            next.Add(option);
        }
        state["next"] = next;
        state["map"] = view.ToJson();
        return state;
    }

    // ---------- co-op vote ----------

    /// <summary>Our co-op map vote, valid until the room, the visited path or the map changes.</summary>
    private sealed record Vote(object Map, object? Room, int VisitedCount, (int Col, int Row) Target);

    private static Vote? _vote;

    private static Vote? CurrentVote(RunState rs)
    {
        if (_vote is { } v
            && (!ReferenceEquals(v.Map, rs.Map)
                || !ReferenceEquals(v.Room, rs.CurrentRoom)
                || v.VisitedCount != rs.VisitedMapCoords.Count()))
        {
            _vote = null;
        }
        return _vote;
    }

    // ---------- travel ----------

    /// <summary>
    /// Arrival per docs section d, plus a room change: while the map is open CurrentRoom is
    /// still the room we are leaving (non-null), so the documented test alone could pass if
    /// the coord updates before travel starts.
    /// </summary>
    private static bool Arrived(RunState? rs, (int Col, int Row) target, object? roomBefore)
    {
        if (rs is null) return false;
        MapCoord? now = rs.CurrentMapCoord;
        return now is { } c
            && Key(c) == target
            && rs.CurrentRoom is { } room
            && !ReferenceEquals(room, roomBefore)
            && NMapScreen.Instance?.IsTraveling != true;
    }

    // NMapPoint nodes of the open map, cached per map screen and map generation so the
    // ~150 ms poll doesn't walk the whole map scene; any miss falls back to a fresh walk.
    private static ulong _nodesScreenId;
    private static object? _nodesMap;
    private static List<NMapPoint> _nodes = new();

    private static NMapPoint? FindPointNode(NMapScreen screen, object map, (int Col, int Row) at, bool fresh)
    {
        var screenId = screen.GetInstanceId();
        var stale = fresh || _nodesScreenId != screenId || !ReferenceEquals(_nodesMap, map);
        if (!stale && MatchNode(at) is { } cached)
            return cached;

        _nodes = Nodes.FindAll<NMapPoint>(screen);
        _nodesScreenId = screenId;
        _nodesMap = map;
        return MatchNode(at);
    }

    private static NMapPoint? MatchNode((int Col, int Row) at)
    {
        foreach (var p in _nodes)
        {
            if (!GodotObject.IsInstanceValid(p) || !p.IsInsideTree()) continue;
            if (p.Point is { } point && Key(point) == at) return p;
        }
        return null;
    }

    // ---------- map data ----------

    private static (int Col, int Row) Key(MapCoord c) => (c.col, c.row); // VERIFY: MapCoord exposes lowercase col/row (fields or properties)

    private static (int Col, int Row) Key(MapPoint p) => Key(p.coord);

    private static string Show((int Col, int Row) at) => $"({at.Col},{at.Row})";

    private static string TypeWord(MapPointType type) => type.ToString() switch
    {
        // VERIFY: MapPointType member names; anything unlisted falls through lowercased.
        "Monster" => "monster",
        "Elite" => "elite",
        "RestSite" => "rest",
        "Shop" => "shop",
        "Treasure" => "treasure",
        "Unknown" => "unknown",
        "Boss" => "boss",
        "Ancient" => "ancient",
        var other => other.ToLowerInvariant(),
    };

    private static string Label(string type) => type switch
    {
        "monster" => "Monster",
        "elite" => "Elite",
        "rest" => "Rest Site",
        "shop" => "Shop",
        "treasure" => "Treasure",
        "unknown" => "Unknown (?)",
        "boss" => "Boss",
        "ancient" => "Ancient",
        _ => type,
    };

    private static bool HasQuest(MapPoint p)
    {
        try { return p.Quests?.Any() == true; } // VERIFY: MapPoint.Quests is a generic collection, non-empty when a quest marker (Spoils Map X) is drawn on the point
        catch { return false; }
    }

    private sealed record Spot((int Col, int Row) At, string Type, List<(int Col, int Row)> Children, bool Quest);

    /// <summary>The act map as plain data, read fresh from the run state on every call.</summary>
    private sealed class MapView
    {
        public required Dictionary<(int Col, int Row), Spot> Spots { get; init; }

        /// <summary>Visited points this act, oldest first; the last one is where we stand.</summary>
        public required List<(int Col, int Row)> Visited { get; init; }

        /// <summary>Travelable points in a fixed order (row, then column); "map:n" indexes this.</summary>
        public required List<(int Col, int Row)> Next { get; init; }

        public required List<(int Col, int Row)> Bosses { get; init; }

        private readonly Dictionary<(int Col, int Row), (int[] Min, int[] Max)> _ahead = new();

        public (int Col, int Row)? At => Visited.Count > 0 ? Visited[^1] : null;

        public string TypeAt((int Col, int Row) at) => Spots.TryGetValue(at, out var s) ? s.Type : "unknown";

        public static MapView Read(RunState rs)
        {
            var map = rs.Map;
            var spots = new Dictionary<(int Col, int Row), Spot>();

            void Add(MapPoint? p)
            {
                if (p is null) return;
                var key = Key(p);
                if (spots.ContainsKey(key)) return;
                var children = new List<(int Col, int Row)>();
                // Key has overloads for both MapPoint and MapCoord, so this compiles whichever the elements are.
                foreach (var child in p.Children) children.Add(Key(child)); // VERIFY: MapPoint.Children enumerates MapPoint (or MapCoord)
                children.Sort();
                spots[key] = new Spot(key, TypeWord(p.PointType), children, HasQuest(p));
            }

            Add(map.StartingMapPoint);
            foreach (var p in map.GetAllMapPoints()) Add(p);
            Add(map.BossMapPoint);
            Add(map.SecondBossMapPoint);

            var visitedCoords = rs.VisitedMapCoords.ToList();
            var visited = visitedCoords.Select(c => Key(c)).ToList();

            var bosses = new List<(int Col, int Row)>();
            if (map.BossMapPoint is { } boss) bosses.Add(Key(boss));
            if (map.SecondBossMapPoint is { } second) bosses.Add(Key(second));

            return new MapView
            {
                Spots = spots,
                Visited = visited,
                Next = NextPoints(rs, map, visitedCoords),
                Bosses = bosses,
            };
        }

        /// <summary>Mirrors the game's private RecalculateTravelability (docs section d).</summary>
        private static List<(int Col, int Row)> NextPoints(RunState rs, ActMap map, List<MapCoord> visited)
        {
            var next = new List<(int Col, int Row)>();
            if (visited.Count == 0)
            {
                if (map.StartingMapPoint is { } start) next.Add(Key(start));
                return next;
            }

            var last = visited[^1];
            var lastKey = Key(last);
            var boss = map.BossMapPoint;
            var second = map.SecondBossMapPoint;

            if (boss is not null && Key(boss) == lastKey)
            {
                if (second is not null) next.Add(Key(second));
            }
            else if (second is not null && Key(second) == lastKey)
            {
                // Act over: leaving goes through the rewards screen's proceed button.
            }
            else if (lastKey.Row == map.GetRowCount() - 1)
            {
                if (boss is not null) next.Add(Key(boss));
            }
            else if (map.GetPoint(last) is { } from)
            {
                // VERIFY: MapTravel's namespace is covered by the global usings (expected MegaCrit.Sts2.Core.Map)
                next.AddRange(MapTravel.GetTravelablePointsFrom(rs, from)
                    .Select(p => Key(p))
                    .Distinct()
                    .OrderBy(k => k.Row)
                    .ThenBy(k => k.Col));
            }
            return next;
        }

        /// <summary>Compact map: visited path plus every node still reachable, row by row upward.</summary>
        public JsonObject ToJson()
        {
            var json = new JsonObject { ["key"] = MapKey };

            if (Visited.Count > 0)
                json["visited"] = string.Join(", ", Visited.Select(k => $"{Show(k)} {TypeAt(k)}"));

            var shown = Reachable();
            var here = At;
            if (here is { } h) shown.Add(h);
            var floorRow = here?.Row ?? 0;
            foreach (var b in Bosses)
                if (b.Row >= floorRow) shown.Add(b);

            var rows = new JsonObject();
            foreach (var row in shown.GroupBy(k => k.Row).OrderBy(g => g.Key))
            {
                var sb = new StringBuilder();
                foreach (var key in row.OrderBy(k => k.Col))
                {
                    if (!Spots.TryGetValue(key, out var spot)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    if (here is { } at && at == key) sb.Append('@');
                    sb.Append('c').Append(key.Col).Append(':').Append(spot.Type);
                    if (spot.Quest) sb.Append("(quest)");
                    if (spot.Children.Count > 0) sb.Append('^').AppendJoin(',', spot.Children.Select(c => c.Col));
                }
                if (sb.Length > 0) rows[row.Key.ToString()] = sb.ToString();
            }
            json["rows"] = rows;
            return json;
        }

        /// <summary>Everything reachable from the next options by following map paths.</summary>
        private HashSet<(int Col, int Row)> Reachable()
        {
            var seen = new HashSet<(int Col, int Row)>();
            var queue = new Queue<(int Col, int Row)>(Next);
            while (queue.Count > 0)
            {
                var key = queue.Dequeue();
                if (!seen.Add(key) || !Spots.TryGetValue(key, out var spot)) continue;
                foreach (var child in spot.Children)
                    if (!seen.Contains(child)) queue.Enqueue(child);
            }
            return seen;
        }

        /// <summary>"monster 3-5, elite 1-2, rest 1": per-type min-max over routes from <paramref name="start"/>.</summary>
        public string Ahead((int Col, int Row) start)
        {
            var (min, max) = Counts(start);
            var parts = new List<string>();
            for (var i = 0; i < Tally.Length; i++)
            {
                if (max[i] == 0) continue;
                parts.Add(min[i] == max[i] ? $"{Tally[i]} {max[i]}" : $"{Tally[i]} {min[i]}-{max[i]}");
            }
            return string.Join(", ", parts);
        }

        private (int[] Min, int[] Max) Counts((int Col, int Row) key)
        {
            if (_ahead.TryGetValue(key, out var memo)) return memo;

            var min = new int[Tally.Length];
            var max = new int[Tally.Length];
            if (Spots.TryGetValue(key, out var spot))
            {
                // Paths only go upward; the row check also guards against a malformed cycle.
                var kids = spot.Children.Where(c => c.Row > key.Row && Spots.ContainsKey(c)).ToList();
                if (kids.Count > 0)
                {
                    Array.Fill(min, int.MaxValue);
                    foreach (var kid in kids)
                    {
                        var (kidMin, kidMax) = Counts(kid);
                        for (var i = 0; i < Tally.Length; i++)
                        {
                            min[i] = Math.Min(min[i], kidMin[i]);
                            max[i] = Math.Max(max[i], kidMax[i]);
                        }
                    }
                }

                var self = Array.IndexOf(Tally, spot.Type);
                if (self >= 0)
                {
                    min[self]++;
                    max[self]++;
                }
            }

            _ahead[key] = (min, max);
            return (min, max);
        }
    }
}
