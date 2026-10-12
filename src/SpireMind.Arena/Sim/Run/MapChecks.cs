using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// Checks of the map: the generator, travel, the roll of an Unknown room and the map screen.
/// <para>
/// The evidence for the generator is the real game's: the saved maps of five seeds, rebuilt
/// point for point (kinds, links in the game's own listing order, the points the paths started
/// from), and three whole runs from the game's run history, whose every step must be a way
/// through the generated map of its act and whose every Unknown room must come out as it did
/// in the game (<see cref="MapGoldenData"/>). Nothing of that is read off the code it checks.
/// </para>
/// <para>
/// Everything else is worked out by hand: from the game's rules on small maps drawn here, from
/// the drawing of a saved map (the screens of seed 1BUSLN0FY5 below were written down from the
/// save, not from the generator), and from the worked example of docs/screens.md, which the
/// map screen has to give back key for key.
/// </para>
/// </summary>
public sealed class MapChecks : IArenaChecks
{
    /// <summary>The seed of the save that was played on: its first Unknown room was an Event, rolled from base odds with every kind allowed.</summary>
    private const string PlayedSeed = MapGoldenData.PlayedSeed;

    /// <summary>The seed of the three-act run of the history: its first Unknown number gave an Event, its second,
    /// at base odds again in the third act, a Monster room: so that number is at most 0.1.</summary>
    private const string ThreeActSeed = "75F277YN2G";

    private static readonly string[] OvergrowthRun = [GameLists.Overgrowth, GameLists.Hive, GameLists.Glory];

    public void Run(Check c)
    {
        Generator(c);
        SetOrder(c);
        GoldenMaps(c);
        RealRuns(c);
        PlayedRun(c);
        MapRules(c);
        LaterPasses(c);
        Travel(c);
        UnknownRooms(c);
        ScreenAgainstTheDocs(c);
        ScreensOfARealMap(c);
        ScreenActions(c);
        CopiesAndReplay(c);
        BossAndCounters(c);
    }

    // ---------- helpers ----------

    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>JSON as compact text with "+" and quotes written as they are, as the tools send it.</summary>
    private static string Text(JsonNode? node) => node is null ? "null" : node.ToJsonString(Plain);

    private static string Keys(JsonNode? node) => node is JsonObject o ? string.Join(",", o.Select(kv => kv.Key)) : "(not an object)";

    private static string Ids(GameSnapshot snapshot) => string.Join(" ", snapshot.Actions.Select(a => a.Id));

    private static string? Label(GameSnapshot snapshot, string id) => snapshot.Actions.FirstOrDefault(a => a.Id == id)?.Label;

    private static void Do(Run run, params string[] ids)
    {
        foreach (var id in ids)
        {
            var result = run.Execute(id);
            if (!result.Ok)
                throw new InvalidOperationException($"'{id}' was refused: {result.Error} Legal now: {string.Join(", ", run.LegalActions().Select(a => a.Id))}");
        }
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    /// <summary>A float as the shortest text that reads back as the same float.</summary>
    private static string Exact(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Odds(UnknownRoomOdds odds) => $"{Exact(odds.Monster)} {Exact(odds.Elite)} {Exact(odds.Treasure)} {Exact(odds.Shop)}";

    private static string Entry(string id) => id[(id.IndexOf('.') + 1)..];

    private static string ActOf(string id) => GameNames.ClassNameOf(Entry(id), GameLists.Acts.Select(a => a.ClassName))
        ?? throw new KeyNotFoundException($"'{id}' is not an act of the arena's lists.");

    private static MapPointType Kind(string word) => word switch
    {
        "monster" => MapPointType.Monster,
        "elite" => MapPointType.Elite,
        "rest" or "rest_site" => MapPointType.RestSite,
        "shop" => MapPointType.Shop,
        "treasure" => MapPointType.Treasure,
        "unknown" => MapPointType.Unknown,
        "boss" => MapPointType.Boss,
        "ancient" => MapPointType.Ancient,
        _ => throw new ArgumentException($"'{word}' is no kind of map point."),
    };

    private static RoomKind Room(string word) => word switch
    {
        "monster" => RoomKind.Monster,
        "elite" => RoomKind.Elite,
        "boss" => RoomKind.Boss,
        "treasure" => RoomKind.Treasure,
        "shop" => RoomKind.Shop,
        "event" => RoomKind.Event,
        "rest_site" => RoomKind.RestSite,
        _ => throw new ArgumentException($"'{word}' is no kind of room."),
    };

    /// <summary>
    /// A map drawn by hand, one text per row from row 0 (the start point) to the boss's row, each
    /// point written as the map screen writes it: <c>c4:monster^3,5</c>. The children are linked
    /// in the order they are written. An exclamation mark after the kind (<c>c3:monster!^3</c>)
    /// makes a point one that cannot be modified, as the generator's fixed rows are.
    /// </summary>
    private static ActMap Sketch(params string[] rows)
    {
        var grid = new MapPoint?[ActMap.Width, rows.Length - 1];
        var points = new Dictionary<MapCoord, MapPoint>();
        var links = new List<(MapPoint From, int[] To)>();
        MapPoint? start = null, boss = null;
        for (var row = 0; row < rows.Length; row++)
        {
            foreach (var token in rows[row].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = token.IndexOf(':');
                var caret = token.IndexOf('^');
                var col = int.Parse(token[1..colon], CultureInfo.InvariantCulture);
                var word = caret < 0 ? token[(colon + 1)..] : token[(colon + 1)..caret];
                var kind = Kind(word.TrimEnd('!'));
                var point = new MapPoint(col, row) { Type = kind, CanBeModified = !word.EndsWith('!') };
                points[point.Coord] = point;
                if (kind == MapPointType.Ancient) start = point;
                else if (kind == MapPointType.Boss) boss = point;
                else grid[col, row] = point;
                if (caret >= 0) links.Add((point, token[(caret + 1)..].Split(',').Select(t => int.Parse(t, CultureInfo.InvariantCulture)).ToArray()));
            }
        }
        foreach (var (from, to) in links)
            foreach (var col in to) from.AddChild(points[new MapCoord(col, from.Row + 1)]);
        var starts = new SlotSet<MapPoint>();
        foreach (var point in start!.Children) starts.Add(point);
        return new ActMap(grid, start, boss!, starts, new MapCounts(0, 0, 0, 0), 0);
    }

    /// <summary>A check's own relic that has a name and does nothing.</summary>
    private sealed class Named(string name) : Relic
    {
        public override string Name => name;
        public override string Describe() => "A relic of the checks.";
    }

    /// <summary>Juzu Bracelet's one rule: an Unknown room never holds a hallway fight.</summary>
    private sealed class NoMonsters : Relic
    {
        public override string Name => "Juzu Bracelet";
        public override string Describe() => "Unknown rooms never hold a hallway fight.";

        public override IReadOnlySet<RolledRoom> ModifyUnknownMapPointRoomTypes(RunContext c, IReadOnlySet<RolledRoom> rooms)
        {
            var fewer = new HashSet<RolledRoom>(rooms);
            fewer.Remove(RolledRoom.Monster);
            return fewer;
        }
    }

    /// <summary>Winged Boots' rule while it has uses left: any point of the next row can be travelled to.</summary>
    private sealed class Wings : Relic
    {
        public bool On = true;
        public override string Name => "Winged Boots";
        public override string Describe() => "Travel to any point of the next row.";
        public override bool ShouldAllowFreeTravel(RunContext c) => On;
    }

    /// <summary>A stand-in for a room: one action, proceed, which finishes the room.</summary>
    private sealed class RoomStub(MapArrival arrival) : RunScreen
    {
        public override string Name => "event";

        public override GameSnapshot Snapshot(Run run) => new()
        {
            Screen = Name, AwaitingDecision = true,
            State = new JsonObject { ["room"] = arrival.Room.ToString(), ["at"] = arrival.Coord.ToString() },
            Actions = [new LegalAction("proceed", "Proceed")],
        };

        public override ActionResult Execute(Run run, string actionId)
        {
            if (actionId != "proceed") return ActionResult.Fail($"Unknown action '{actionId}'.");
            run.RoomFinished();
            return ActionResult.Success;
        }

        public override RunScreen Clone(CloneMap map) => new RoomStub(arrival);
    }

    /// <summary>
    /// A director for the checks: the world is begun, the first act's map is made (the game's, or
    /// one drawn by hand), the start point is entered as the game enters it, and the map opens as
    /// it does after Neow's Leave. A room is a stand-in with one action; the boss ends the run.
    /// </summary>
    private sealed class Tour : RunDirector, IMapDirector
    {
        public ActMap? Drawn;
        public string[]? Acts = OvergrowthRun;
        public StreamMode Mode = StreamMode.Game;
        public string? Boss;
        public bool OpenMap = true;

        /// <summary>The kinds of room this director says the arena cannot show (the house rule of <see cref="IMapDirector.CanHold"/>).</summary>
        public RolledRoom[] CannotHold = [];

        public MapArrival? Last;
        public int Rooms;
        public int ScreensWhenEntered = -1;

        public override void Begin(Run run)
        {
            var world = RunWorld.Begin(run.State, mode: Mode, acts: Acts);
            if (Boss is not null) world.Rooms[0].Boss = Boss;
            var map = Drawn is null ? MapState.BeginAct(run, 0) : MapState.BeginAct(run, 0, Drawn);
            if (!OpenMap) return;
            map.Enter(run, map.Map.Start.Coord);
            MapState.MarkRoomVisited(run, RoomKind.Event);
            run.Push(new MapScreen());
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => throw new NotSupportedException("The tour has no fights.");

        public override void AfterRoom(Run run)
        {
            run.CloseAll();
            run.Push(new MapScreen());
        }

        public void EnterRoom(Run run, MapArrival arrival)
        {
            Last = arrival;
            Rooms++;
            ScreensWhenEntered = run.Screens.Count;
            MapState.MarkRoomVisited(run, arrival.Room);
            if (arrival.Room == RoomKind.Boss) run.End(RunResult.Cleared);
            else run.Push(new RoomStub(arrival));
        }

        public bool CanHold(Run run, RolledRoom room) => !CannotHold.Contains(room);
    }

    private static Tour TourOf(Run run) => (Tour)run.Director;

    private static MapState MapOf(Run run) => run.State.Part<MapState>();

    private static RunWorld WorldOf(Run run) => run.State.Part<RunWorld>();

    /// <summary>A run whose director has made the world and the map and entered nothing: for walking by hand.</summary>
    private static Run Unentered(string seed, ActMap? drawn = null, string[]? acts = null, Func<Relic>[]? relics = null, RolledRoom[]? cannotHold = null) =>
        new(new RunSetup { Seed = seed, ExtraRelics = relics ?? [] },
            new Tour { Drawn = drawn, Acts = acts ?? OvergrowthRun, OpenMap = false, CannotHold = cannotHold ?? [] });

    private static MapArrival Enter(Run run, int col, int row) => MapOf(run).Enter(run, new MapCoord(col, row));

    // ---------- the two additions to the generator's draws ----------

    private static void Generator(Check c)
    {
        c.Group("the sorted shuffle: the result depends on what is in the list, not on the order it came in", () =>
        {
            foreach (var seed in new ulong[] { 1, 2, 3, 77, 2026 })
            {
                var a = new List<int> { 1, -1, 0 };
                var b = new List<int> { -1, 0, 1 };
                var plain = new List<int> { -1, 0, 1 };
                var first = new GameRng(seed);
                var second = new GameRng(seed);
                first.StableShuffle(a);
                second.StableShuffle(b);
                new GameRng(seed).Shuffle(plain);
                c.Sequence(a, b, $"seed {seed}: two orders of the same three numbers");
                c.Sequence(plain, a, $"seed {seed}: the plain shuffle of the sorted list");
                c.Equal(2, first.Counter, $"seed {seed}: two draws for three items");
            }

            var rng = new GameRng(5);
            rng.StableShuffle(new List<int> { 4 });
            rng.StableShuffle(new List<int>());
            c.Equal(0, rng.Counter, "a list of one and an empty list draw nothing");

            // With an order of the caller's: by length, so "ccc" sorts last whatever the letters say.
            var words = new List<string> { "ccc", "a", "bb" };
            var sorted = new List<string> { "a", "bb", "ccc" };
            new GameRng(9).StableShuffle(words, (x, y) => x.Length.CompareTo(y.Length));
            new GameRng(9).Shuffle(sorted);
            c.Sequence(sorted, words, "sorted by the order that was passed, then shuffled");
        });

        c.Group("the rounded normal draw: two numbers an attempt, the sine form, a half rounds to the even number, out of range is drawn again", () =>
        {
            // The game's rule written out once more, on the fractions the stream gives.
            static (int Value, int Draws) ByTheRule(GameRng stream, int mean, int stdDev, int min, int max)
            {
                var draws = 0;
                while (true)
                {
                    var u = 1.0 - stream.NextDouble();
                    var v = 1.0 - stream.NextDouble();
                    draws += 2;
                    var value = (int)Math.Round(mean + stdDev * (Math.Sqrt(-2.0 * Math.Log(u)) * Math.Sin(2.0 * Math.PI * v)), MidpointRounding.ToEven);
                    if (value >= min && value <= max) return (value, draws);
                }
            }

            var wrong = 0;
            var redrawn = 0;
            for (ulong seed = 1; seed <= 2000; seed++)
            {
                var rng = new GameRng(seed);
                var (value, draws) = ByTheRule(new GameRng(seed), 7, 1, 6, 7);
                if (rng.NextGaussianInt(7, 1, 6, 7) != value || rng.Counter != draws) wrong++;
                if (draws > 2) redrawn++;
            }
            c.Equal(0, wrong, "of 2000 streams, how many differ from the rule in value or in the number of draws");
            // A value of 6 or 7 comes from 5.5 to 7.5 around 7: 62.5% of attempts, so 37.5% of calls draw again.
            c.That(redrawn is > 650 and < 850, $"about three calls in eight draw more than two numbers (of 2000: {redrawn})");

            // The shares follow from the normal curve: around 7, 5.5 to 6.5 holds 24.17% and 6.5 to 7.5
            // holds 38.29%; kept to those two, 38.7% and 61.3%. Around 12 kept to 10..14: 6.1, 24.5, 38.8, 24.5, 6.1.
            var stream = new GameRng(20261011);
            const int draws7 = 40000;
            var sixes = 0;
            for (var i = 0; i < draws7; i++)
                if (stream.NextGaussianInt(7, 1, 6, 7) == 6) sixes++;
            c.That(Math.Abs(100.0 * sixes / draws7 - 38.7) < 1.5, $"6 Rest Sites in 38.7% of maps of a first act (here {100.0 * sixes / draws7:0.0}%)");

            var counts = new int[15];
            for (var i = 0; i < draws7; i++) counts[stream.NextGaussianInt(12, 1, 10, 14)]++;
            double[] shares = [6.1, 24.5, 38.8, 24.5, 6.1];
            for (var n = 10; n <= 14; n++)
                c.That(Math.Abs(100.0 * counts[n] / draws7 - shares[n - 10]) < 1.5, $"{n} Unknown rooms in {shares[n - 10]}% of maps (here {100.0 * counts[n] / draws7:0.0}%)");
            c.Equal(draws7, counts.Sum(), "and never a number outside 10 to 14");
        });

        c.Group("each act's numbers: what is drawn first, and how long the grid is", () =>
        {
            // A first act draws two normal numbers (four fractions when neither is drawn again); the
            // third act draws its Rest Sites as a plain 5 or 6 first, one fraction.
            var rules = ActMapRules.For(GameLists.Glory);
            var rng = new GameRng(31);
            var plain = new GameRng(31).NextInt(5, 7);
            var glory = rules.DrawCounts(rng);
            c.Equal(plain, glory.Rests, "Glory's Rest Sites are the stream's first plain draw of 5 or 6");
            c.That(glory.Unknowns is >= 9 and <= 13, "Glory has one Unknown room less than the draw of 10 to 14");
            c.Equal((3, 5), (glory.Shops, glory.Elites), "three shops and five elites");

            var hive = ActMapRules.For(GameLists.Hive).DrawCounts(new GameRng(31));
            c.That(hive.Rests is 6 or 7 && hive.Unknowns is >= 9 and <= 13, "Hive: 6 or 7 Rest Sites, 9 to 13 Unknown rooms");
            var first = ActMapRules.For(GameLists.Overgrowth).DrawCounts(new GameRng(31));
            var other = ActMapRules.For(GameLists.Underdocks).DrawCounts(new GameRng(31));
            c.Equal(first, other, "Overgrowth and Underdocks draw the same way");
            c.That(first.Rests is 6 or 7 && first.Unknowns is >= 10 and <= 14, "a first act: 6 or 7 Rest Sites, 10 to 14 Unknown rooms");

            // The acts' own numbers, as the game's act classes have them: around 7 for a first act, around 6 for Hive,
            // both kept to 6 or 7; Unknown rooms around 12 kept to 10..14 on the same stream right after.
            var wrong = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                var byRule = new GameRng(seed);
                var firstAct = new MapCounts(byRule.NextGaussianInt(7, 1, 6, 7), 3, 5, byRule.NextGaussianInt(12, 1, 10, 14));
                var stream = new GameRng(seed);
                if (ActMapRules.For(GameLists.Overgrowth).DrawCounts(stream) != firstAct || stream.Counter != byRule.Counter) wrong++;

                byRule = new GameRng(seed);
                var second = new MapCounts(byRule.NextGaussianInt(6, 1, 6, 7), 3, 5, byRule.NextGaussianInt(12, 1, 10, 14) - 1);
                stream = new GameRng(seed);
                if (ActMapRules.For(GameLists.Hive).DrawCounts(stream) != second || stream.Counter != byRule.Counter) wrong++;

                byRule = new GameRng(seed);
                var third = new MapCounts(byRule.NextInt(5, 7), 3, 5, byRule.NextGaussianInt(12, 1, 10, 14) - 1);
                stream = new GameRng(seed);
                if (ActMapRules.For(GameLists.Glory).DrawCounts(stream) != third || stream.Counter != byRule.Counter) wrong++;
            }
            c.Equal(0, wrong, "of 300 streams and three acts, how many counts differ from the acts' own numbers");

            c.Sequence([15, 15, 14, 13], new[] { GameLists.Overgrowth, GameLists.Underdocks, GameLists.Hive, GameLists.Glory }.Select(a => ActMapRules.For(a).Rooms), "rooms between Ancient and boss");
            c.That(Throws<ArgumentException>(() => ActMapRules.For("Nowhere")), "an act the game does not have is refused");
        });
    }

    // ---------- the set that lists its items as the game's sets do ----------

    private static void SetOrder(Check c)
    {
        c.Group("a set of map points lists in the order of adding, and an added point takes the place freed last", () =>
        {
            var set = new SlotSet<string>();
            string List() => string.Join("", set);
            foreach (var item in new[] { "A", "B", "C" }) set.Add(item);
            c.Equal("ABC", List(), "three added");
            c.That(!set.Add("B"), "what is in the set is not added again");
            set.Remove("B");
            c.Equal("AC", List(), "one taken out: the others keep their order");
            set.Add("D");
            c.Equal("ADC", List(), "the next one added takes the free place, in the middle");
            set.Remove("A");
            set.Remove("C");
            set.Add("E");
            c.Equal("DE", List(), "two freed: the one freed last is taken first");
            set.Add("F");
            c.Equal("FDE", List(), "then the one freed before it");
            set.Add("G");
            c.Equal("FDEG", List(), "and with no free place left, the end");
            c.Equal(4, set.Count, "four items");
            c.Equal("F", set.First(), "the first in listing order");
            c.That(!set.Remove("Z") && set.Contains("G") && !set.Contains("A"), "what is not there is not taken out");

            var copy = set.Clone(item => item.ToLowerInvariant());
            set.Remove("D");
            copy.Add("x");
            c.Equal("fdegx", string.Join("", copy), "a copy keeps the places and goes its own way");
        });

        c.Group("the same order as the hash set of the runtime the game's sets are", () =>
        {
            // The game keeps these sets in .NET's own hash set. As long as the arena runs on a
            // runtime whose hash set lists like the game's, the two must agree after any history.
            // A failure here means the runtime's hash set changed, not the game: the hand-worked
            // order above is the game's.
            var rng = new GameRng(404);
            var pool = Enumerable.Range(0, 12).Select(i => new object()).ToArray();
            var mine = new SlotSet<object>();
            var theirs = new HashSet<object>();
            var differences = 0;
            for (var step = 0; step < 5000; step++)
            {
                var item = pool[rng.NextInt(pool.Length)];
                if (rng.NextInt(3) == 0)
                {
                    if (mine.Remove(item) != theirs.Remove(item)) differences++;
                }
                else if (mine.Add(item) != theirs.Add(item))
                {
                    differences++;
                }
                if (!mine.SequenceEqual(theirs)) differences++;
            }
            c.Equal(0, differences, "steps of 5000 adds and removes after which the two list differently");
        });
    }

    // ---------- the real game's maps ----------

    /// <summary>Every difference between a save's map and a generated one; empty when the save's map is rebuilt exactly.</summary>
    private static List<string> Compare(JsonElement saved, ActMap map)
    {
        var differences = new List<string>();
        void Same<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) differences.Add($"{what}: the save has {expected}, rebuilt {actual}");
        }
        static string Coord(JsonElement coord) => $"({coord.GetProperty("col").GetInt32()},{coord.GetProperty("row").GetInt32()})";
        // A point as one text: place, kind, whether it can be modified (the save leaves the key out when it cannot), children in the save's order.
        static string Saved(JsonElement point)
        {
            var children = point.TryGetProperty("children", out var list) ? string.Join(" ", list.EnumerateArray().Select(Coord)) : "";
            var canModify = point.TryGetProperty("can_modify", out var flag) && flag.GetBoolean();
            return $"{Coord(point.GetProperty("coord"))} {point.GetProperty("type").GetString()}{(canModify ? "" : " fixed")} > {children}";
        }
        static string Rebuilt(MapPoint point) =>
            $"{point.Coord} {GameNames.SnakeCase(point.Type.ToString())}{(point.CanBeModified ? "" : " fixed")} > {string.Join(" ", point.Children.Select(k => k.Coord.ToString()))}";

        Same(saved.GetProperty("width").GetInt32(), map.Columns, "width");
        Same(saved.GetProperty("height").GetInt32(), map.Rows, "height");
        var points = saved.GetProperty("points").EnumerateArray().Select(Saved).ToList();
        var rebuilt = map.Points.Select(Rebuilt).ToList();
        Same(points.Count, rebuilt.Count, "number of points");
        for (var i = 0; i < Math.Min(points.Count, rebuilt.Count); i++) Same(points[i], rebuilt[i], $"point {i}");
        Same(Saved(saved.GetProperty("start")), Rebuilt(map.Start), "start point");
        Same(Saved(saved.GetProperty("boss")), Rebuilt(map.Boss), "boss");
        var starts = saved.TryGetProperty("start_coords", out var coords) ? string.Join(" ", coords.EnumerateArray().Select(Coord)) : "";
        Same(starts, string.Join(" ", map.StartPoints.Select(p => p.Coord.ToString())), "the points the paths started from");
        return differences;
    }

    private static void GoldenMaps(Check c)
    {
        foreach (var save in MapGoldenData.Saves)
        {
            c.Group($"the map of seed {save.Seed} ({ActOf(save.Act)}, {save.File}) is rebuilt from the seed alone", () =>
            {
                using var document = JsonDocument.Parse(save.Map);
                var map = ActMapGenerator.Generate(new RunStreams(save.Seed).ActMap(1), ActOf(save.Act));
                var differences = Compare(document.RootElement, map);
                c.That(differences.Count == 0, $"{differences.Count} differences: {string.Join("; ", differences.Take(5))}");
                c.That(map.Points.Count >= 40, "and the save holds a whole map");
            });
        }

        c.Group("the five saved maps are five different maps, and a changed map is told apart", () =>
        {
            var maps = MapGoldenData.Saves.Select(s => ActMapGenerator.Generate(new RunStreams(s.Seed).ActMap(1), ActOf(s.Act)).Describe()).ToList();
            c.Equal(5, maps.Distinct().Count(), "different maps");

            var save = MapGoldenData.Saves[0];
            using var document = JsonDocument.Parse(save.Map);
            c.That(Compare(document.RootElement, ActMapGenerator.Generate(new RunStreams(MapGoldenData.Saves[1].Seed).ActMap(1), ActOf(save.Act))).Count > 10, "another seed's map");
            c.That(Compare(document.RootElement, ActMapGenerator.Generate(new RunStreams(save.Seed).ActMap(2), ActOf(save.Act))).Count > 10, "the same seed's second-act stream");
            // One child link listed the other way round is a difference: the order is the game's.
            const string asSaved = """ "children":[{"col":1,"row":6},{"col":0,"row":6}] """;
            const string otherWay = """ "children":[{"col":0,"row":6},{"col":1,"row":6}] """;
            c.That(save.Map.Contains(asSaved.Trim()), "the save lists the children of (0,5) as (1,6), then (0,6)");
            using var swapped = JsonDocument.Parse(save.Map.Replace(asSaved.Trim(), otherWay.Trim()));
            c.Equal(1, Compare(swapped.RootElement, ActMapGenerator.Generate(new RunStreams(save.Seed).ActMap(1), ActOf(save.Act))).Count, "two children listed in the other order");
        });

        c.Group("golden saves on this machine, when there are any: the map of the act each was saved in", () =>
        {
            // Every *.save in $ARENA_GOLDEN_SAVES, or in ~/Projects/sts2-notes/golden-saves: saves Mark made on v0.107.1.
            var folder = Environment.GetEnvironmentVariable("ARENA_GOLDEN_SAVES");
            if (string.IsNullOrEmpty(folder))
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "sts2-notes", "golden-saves");
            if (!Directory.Exists(folder))
            {
                c.That(true, "no folder of golden saves here");
                return;
            }
            foreach (var file in Directory.GetFiles(folder, "*.save").OrderBy(f => f, StringComparer.Ordinal))
            {
                var root = GoldenSave.Load(file).Root;
                var act = root.GetProperty("current_act_index").GetInt32();
                var saved = root.GetProperty("acts")[act];
                // The rules here are those of one player at ascension 0; a save of another kind of run is not this check's.
                if (root.GetProperty("players").GetArrayLength() != 1 || root.GetProperty("ascension").GetInt32() != 0
                    || !saved.TryGetProperty("saved_map", out var map) || map.ValueKind != JsonValueKind.Object)
                {
                    c.That(true, $"{Path.GetFileName(file)}: not a single player's save at ascension 0 with a map");
                    continue;
                }
                var seed = root.GetProperty("rng").GetProperty("seed").GetString()!;
                var differences = Compare(map, ActMapGenerator.Generate(new RunStreams(seed).ActMap(act + 1), ActOf(saved.GetProperty("id").GetString()!)));
                c.That(differences.Count == 0, $"{Path.GetFileName(file)}: {string.Join("; ", differences.Take(5))}");
            }
        });
    }

    /// <summary>Every way from the start point to the boss whose points have these kinds, in order.</summary>
    private static List<List<MapPoint>> WaysOfKinds(ActMap map, IReadOnlyList<MapPointType> kinds)
    {
        var found = new List<List<MapPoint>>();
        var way = new List<MapPoint>();
        void Walk(MapPoint point)
        {
            if (way.Count >= kinds.Count || point.Type != kinds[way.Count]) return;
            way.Add(point);
            if (way.Count == kinds.Count) found.Add(way.ToList());
            else
                foreach (var child in point.Children) Walk(child);
            way.RemoveAt(way.Count - 1);
        }
        Walk(map.Start);
        return found;
    }

    private static void RealRuns(Check c)
    {
        foreach (var real in MapGoldenData.Runs)
        {
            c.Group($"the real run on seed {real.Seed} ({real.File}: {real.What}): its path is on the generated maps, and its Unknown rooms come out as they did", () =>
            {
                c.Equal("v0.107.1", real.Build, "the build that wrote the file");
                var acts = real.Acts.Select(a => ActOf(a.Act)).ToArray();
                var run = Unentered(real.Seed, acts: acts);
                var unknowns = 0;
                for (var act = 0; act < real.Acts.Length; act++)
                {
                    var path = real.Acts[act];
                    if (path.OtherMap is not null)
                    {
                        // Not the standard generator's map. It must not have moved the Unknown stream, or the next act could not be checked.
                        c.That(path.Points.All(p => p.PointType != "unknown"), $"act {act + 1} ({path.OtherMap}) has no Unknown point on the path");
                        continue;
                    }

                    // More than one player: one room less. Ascension 1 and up: eight elites. Nothing else of these runs touches the map.
                    var rules = ActMapRules.For(acts[act]);
                    var own = rules.DrawCounts;
                    if (real.Players > 1) rules = rules with { Rooms = rules.Rooms - 1 };
                    if (real.Ascension >= 1) rules = rules with { DrawCounts = rng => own(rng) with { Elites = 8 } };
                    var map = MapState.BeginAct(run, act, rules).Map;
                    c.Equal(path.Points.Length, map.Rows + 1, $"act {act + 1}: the run entered one point a row, the boss's included");

                    var kinds = path.Points.Select(p => Kind(p.PointType)).ToList();
                    var ways = WaysOfKinds(map, kinds);
                    c.That(ways.Count > 0, $"act {act + 1} ({acts[act]}): no way through the generated map has the kinds the run met\n{map.Draw()}");
                    if (ways.Count == 0) return;

                    // Walk it: every room must be the one the game recorded, the rolled ones included.
                    var wanted = path.Points.Select(p => Room(p.Rooms[0])).ToList();
                    Run? walked = null;
                    var got = new List<string>();
                    foreach (var way in ways)
                    {
                        var copy = run.Clone();
                        var part = MapOf(copy);
                        var rooms = new List<RoomKind>();
                        for (var i = 0; i < way.Count; i++)
                        {
                            rooms.Add(part.Enter(copy, way[i].Coord).Room);
                            foreach (var extra in path.Points[i].Rooms.Skip(1)) part.AddRoom(Room(extra));
                            // The one relic of these runs that changes a roll: taken from the elite on the tenth point of that run's first act.
                            if (real.File == "1783968626.run" && act == 0 && i == 9) copy.State.AddRelic(new NoMonsters());
                        }
                        got.Add(string.Join(" ", rooms));
                        if (!rooms.SequenceEqual(wanted)) continue;
                        walked = copy;
                        break;
                    }
                    c.That(walked is not null, $"act {act + 1} ({acts[act]}): the run's rooms were {string.Join(" ", wanted)}; walking the map gave {string.Join(" | ", got)}");
                    if (walked is null) return;
                    run = walked;
                    unknowns += path.Points.Count(p => p.PointType == "unknown");
                    c.Equal(unknowns, WorldOf(run).Streams.Counter(RunStream.UnknownMapPoint), $"act {act + 1}: one number of unknown_map_point for every Unknown point so far");
                }
                c.Equal(real.Acts.Where(a => a.OtherMap is null).Sum(a => a.Points.Length), run.State.TotalFloor, "the total floor counts every point entered");
            });
        }

        c.Group("what those runs prove about the Unknown rooms, in numbers", () =>
        {
            c.Equal(21, MapGoldenData.Runs.Sum(r => r.Acts.Sum(a => a.Points.Count(p => p.PointType == "unknown"))), "Unknown rooms in the three runs");
            // The single player's run: one Unknown room in the first act (an Event) and one in the third (a Monster
            // room, from odds that were set back with the act): the stream goes on from act to act, the odds do not.
            var single = MapGoldenData.Runs[0];
            c.Equal(ThreeActSeed, single.Seed, "the single player's run");
            c.Sequence(["event", "monster"], single.Acts.SelectMany(a => a.Points).Where(p => p.PointType == "unknown").Select(p => p.Rooms[0]), "its two Unknown rooms");
            var stream = new RunStreams(ThreeActSeed).Long(RunStream.UnknownMapPoint);
            c.That(stream.NextFloat() > 0.15f, "so its first number is above 0.15");
            c.That(stream.NextFloat() <= 0.1f, "and its second at most 0.1");
            c.That(new RunStreams(PlayedSeed).Long(RunStream.UnknownMapPoint).NextFloat() > 0.15f, $"the first number of seed {PlayedSeed} is above 0.15 too (its save: an Event from base odds)");
        });
    }

    // ---------- the run that was played on ----------

    private static void PlayedRun(Check c)
    {
        c.Group($"seed {PlayedSeed}, walked as the real run went: options, floors, rooms, the first Unknown room and its odds", () =>
        {
            var run = Unentered(PlayedSeed);
            var map = MapOf(run);
            var world = WorldOf(run);
            c.Equal(GameLists.Overgrowth, RunGeneration.RollActs(new RunSeed(PlayedSeed), RunUnlocks.All)[0], "the seed's own first act");
            c.Equal(0, run.State.TotalFloor, "no point entered yet");
            c.Equal(null, map.Current, "nowhere yet");
            c.Sequence(["(3,0)"], map.Next(run).Select(p => p.Coord.ToString()), "with nothing visited the start point is the one option");

            // The children below are read off the save's map, sorted by column.
            var steps = new (int Col, int Row, MapPointType Kind, RoomKind Room, string Next)[]
            {
                (3, 0, MapPointType.Ancient, RoomKind.Event, "(1,1) (3,1) (5,1)"),
                (5, 1, MapPointType.Monster, RoomKind.Monster, "(6,2)"),
                (6, 2, MapPointType.Monster, RoomKind.Monster, "(6,3)"),
                (6, 3, MapPointType.Unknown, RoomKind.Event, "(5,4) (6,4)"),
                (6, 4, MapPointType.Shop, RoomKind.Shop, "(6,5)"),
            };
            c.Sequence(MapGoldenData.PlayedPath.Select(s => (s.Col, s.Row)), steps.Select(s => (s.Col, s.Row)), "the places are the save's visited_map_coords");
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                var arrival = Enter(run, step.Col, step.Row);
                c.Equal(new MapArrival(new MapCoord(step.Col, step.Row), step.Kind, step.Room), arrival, $"point {i + 1}");
                c.Equal(i + 1, run.State.TotalFloor, $"point {i + 1}: the total floor");
                c.Equal(step.Row + 1, run.State.ActFloor, $"point {i + 1}: the act floor is the row plus 1");
                c.Equal(new RunPlace(0, step.Row, step.Col), world.Streams.Place, $"point {i + 1}: the streams know the place");
                c.Equal(step.Next, string.Join(" ", map.Next(run).Select(p => p.Coord.ToString())), $"point {i + 1}: where to next");
                if (i < MapGoldenData.PlayedPath.Count && MapGoldenData.PlayedPath[i].PointType.Length > 0)
                {
                    c.Equal(Kind(MapGoldenData.PlayedPath[i].PointType), arrival.PointType, $"point {i + 1}: the kind the game recorded");
                    c.Equal(Room(MapGoldenData.PlayedPath[i].Rooms[0]), arrival.Room, $"point {i + 1}: the room the game recorded");
                }
                // The save after this run's Unknown room: one number given, odds 0.2, -2, 0.04, 0.06.
                c.Equal(i >= 3 ? 1 : 0, world.Streams.Counter(RunStream.UnknownMapPoint), $"point {i + 1}: numbers of unknown_map_point");
                c.Equal(i >= 3 ? "0.2 -2 0.04 0.06" : "0.1 -1 0.02 0.03", Odds(world.UnknownOdds), $"point {i + 1}: the odds of an Unknown room");
            }
            c.Equal(MapGoldenData.PlayedUnknownCounter, world.Streams.Counter(RunStream.UnknownMapPoint), "the save's own counter");
            c.Sequence(steps.Select(s => new MapCoord(s.Col, s.Row)), map.Visited, "the path");
            c.Sequence(steps.Select(s => s.Kind), map.History.Select(v => v.PointType), "the kinds along it");
            c.Sequence(steps.Select(s => s.Room), map.History.Select(v => v.Rooms.Single()), "one room a point");
            c.Equal(new MapCoord(6, 4), map.Current!.Value, "where the run stands");
        });
    }

    // ---------- the rules every generated map keeps ----------

    private static void MapRules(Check c)
    {
        c.Group("400 first-act maps: the fixed rows, the placement rules, every point on a way from the start to the boss", () =>
        {
            var seeds = new GameRng(1011);
            var problems = new List<string>();
            var points = 0;
            var smallest = int.MaxValue;
            var largest = 0;
            var firstRow = new int[8];
            var minDraws = int.MaxValue;
            const int maps = 400;
            for (var n = 0; n < maps; n++)
            {
                var seed = RunSeed.Random(seeds).Text;
                var map = ActMapGenerator.Generate(new RunStreams(seed).ActMap(1), GameLists.Overgrowth);
                void Bad(string what)
                {
                    if (problems.Count < 8) problems.Add($"{seed}: {what}");
                }

                if (map.Columns != 7 || map.Rows != 16) Bad($"grid {map.Columns} by {map.Rows}");
                if (map.Start.Coord != new MapCoord(3, 0) || map.Start.Type != MapPointType.Ancient) Bad("start point");
                if (map.Boss.Coord != new MapCoord(3, 16) || map.Boss.Type != MapPointType.Boss || map.Boss.Children.Count != 0) Bad("boss");
                if (!map.Start.Children.OrderBy(p => p.Col).SequenceEqual(map.PointsInRow(1))) Bad("the start point's children are not the first row");
                if (!map.Boss.Parents.OrderBy(p => p.Col).SequenceEqual(map.PointsInRow(15))) Bad("the boss's parents are not the last row");
                if (map.PointsInRow(0).Any() || map.PointsInRow(16).Any()) Bad("a grid point on row 0 or on the boss's row");

                var monsters = 0;
                foreach (var point in map.Points)
                {
                    if (!ReferenceEquals(map.Point(point.Coord), point)) Bad($"{point.Coord} is not where the grid has it");
                    var fixedKind = point.Row switch { 1 => MapPointType.Monster, 9 => MapPointType.Treasure, 15 => MapPointType.RestSite, _ => MapPointType.Unassigned };
                    if (fixedKind != MapPointType.Unassigned && (point.Type != fixedKind || point.CanBeModified)) Bad($"{point} on a fixed row");
                    if (fixedKind == MapPointType.Unassigned && !point.CanBeModified) Bad($"{point} cannot be modified");
                    if (point.Type is MapPointType.Unassigned or MapPointType.Boss or MapPointType.Ancient) Bad($"{point}: no kind for a grid point");
                    if (point.Type == MapPointType.Treasure && point.Row != 9) Bad($"{point}: Treasure off its row");
                    if (point.Row < 6 && point.Type is MapPointType.RestSite or MapPointType.Elite) Bad($"{point} below row 6");
                    if (point.Row is 13 or 14 && point.Type == MapPointType.RestSite) Bad($"{point} in the top rows");
                    if (point.Parents.Count == 0 || point.Children.Count == 0) Bad($"{point} has no parent or no child");
                    if (point.Children.Any(child => child.Row != point.Row + 1)) Bad($"{point} has a child that is not on the next row");
                    if (point.Row < 15 && point.Children.Any(child => Math.Abs(child.Col - point.Col) > 1)) Bad($"{point} has a child more than a column away");
                    if (point.Children.Any(child => !child.Parents.Contains(point))) Bad($"{point}: a child does not know its parent");

                    if (point.CanBeModified && point.Type is MapPointType.Elite or MapPointType.RestSite or MapPointType.Shop
                        && point.Parents.Concat(point.Children).Any(other => other.Type == point.Type))
                        Bad($"{point} next to its own kind");
                    if (point.CanBeModified && point.Type is MapPointType.Elite or MapPointType.RestSite or MapPointType.Shop or MapPointType.Unknown
                        && point.Parents.SelectMany(parent => parent.Children).Any(other => !ReferenceEquals(other, point) && other.Type == point.Type))
                        Bad($"{point} has a sibling of its own kind");
                    if (point.Type == MapPointType.Monster) monsters++;
                }

                if (map.Count(MapPointType.Shop) > 3) Bad($"{map.Count(MapPointType.Shop)} shops");
                if (map.Count(MapPointType.Elite) > 5) Bad($"{map.Count(MapPointType.Elite)} elites");
                if (map.Count(MapPointType.Unknown) > map.Counts.Unknowns) Bad($"{map.Count(MapPointType.Unknown)} Unknown rooms for a count of {map.Counts.Unknowns}");
                if (map.Points.Count(p => p.Type == MapPointType.RestSite && p.CanBeModified) > map.Counts.Rests) Bad("more placed Rest Sites than the count");
                if (map.Counts.Rests is not (6 or 7) || map.Counts.Unknowns is < 10 or > 14 || map.Counts.Shops != 3 || map.Counts.Elites != 5) Bad($"counts {map.Counts}");
                if (monsters == 0) Bad("no Monster room");
                if (map.StartPoints.Any(p => p.Row != 1) || map.StartPoints.Count > map.Start.Children.Count) Bad("the points the paths started from");

                points += map.Points.Count;
                smallest = Math.Min(smallest, map.Points.Count);
                largest = Math.Max(largest, map.Points.Count);
                firstRow[map.Start.Children.Count]++;
                minDraws = Math.Min(minDraws, map.Draws);

                if (n < 20 && ActMapGenerator.Generate(new RunStreams(seed).ActMap(1), GameLists.Overgrowth).Describe() != map.Describe()) Bad("not the same map twice");
            }
            c.That(problems.Count == 0, string.Join("; ", problems));

            // A second reader's own port of the generator (run-generation.md 1.3) found about 59 points
            // a map (40 to 75), and 2, 3 or 4 first-row points in 29%, 48% and 19% of maps.
            var mean = (double)points / maps;
            c.That(mean is > 55 and < 63, $"about 59 points a map (here {mean:0.0})");
            c.That(smallest >= 30 && largest <= 85, $"between 40 and 75 or so (here {smallest} to {largest})");
            c.That(Math.Abs(100.0 * firstRow[3] / maps - 48) < 9, $"three first-row points in about 48% of maps (here {100.0 * firstRow[3] / maps:0}%)");
            c.That(Math.Abs(100.0 * firstRow[2] / maps - 29) < 9, $"two in about 29% (here {100.0 * firstRow[2] / maps:0}%)");
            c.Equal(0, firstRow[0] + firstRow[6] + firstRow[7], "never none, and never more than seven paths can give");
            // Four fractions for the two counts, seven path starts, fourteen steps of two draws for each of seven paths, and more.
            c.That(minDraws >= 4 + 7 + 7 * 14 * 2, $"no map takes fewer than 207 numbers (least here: {minDraws})");
        });

        c.Group("the other acts' maps: their length and their fixed rows", () =>
        {
            foreach (var (act, rows, treasure) in new[] { (GameLists.Underdocks, 16, 9), (GameLists.Hive, 15, 8), (GameLists.Glory, 14, 7) })
            {
                var seeds = new GameRng(77);
                for (var n = 0; n < 40; n++)
                {
                    var map = ActMapGenerator.Generate(new RunStreams(RunSeed.Random(seeds).Text).ActMap(2), act);
                    var ok = map.Rows == rows && map.Boss.Coord == new MapCoord(3, rows)
                        && map.PointsInRow(1).All(p => p.Type == MapPointType.Monster)
                        && map.PointsInRow(treasure).All(p => p.Type == MapPointType.Treasure) && map.Count(MapPointType.Treasure) == map.PointsInRow(treasure).Count()
                        && map.PointsInRow(rows - 1).All(p => p.Type == MapPointType.RestSite)
                        && map.Points.All(p => p.Type != MapPointType.RestSite || p.Row == rows - 1 || (p.Row >= 6 && p.Row < rows - 3))
                        && map.Points.All(p => p.Type != MapPointType.Elite || p.Row >= 6)
                        && map.Points.All(p => p.Parents.Count > 0 && p.Children.Count > 0);
                    if (!ok) c.That(false, $"{act}, map {n}:\n{map.Draw()}");
                }
                c.That(true, $"{act}: {rows} rows, Treasure on row {treasure}");
            }
        });

        c.Group("without the pruning pass every counted kind is placed in full, or the three passes found no place for it", () =>
        {
            // The pruning pass is what takes points away: before it, the grid holds every point the seven paths made.
            var seeds = new GameRng(5);
            for (var n = 0; n < 40; n++)
            {
                var seed = RunSeed.Random(seeds).Text;
                var whole = ActMapGenerator.Generate(new RunStreams(seed).ActMap(1), ActMapRules.For(GameLists.Overgrowth), prune: false);
                var pruned = ActMapGenerator.Generate(new RunStreams(seed).ActMap(1), GameLists.Overgrowth);
                c.That(whole.Points.Count >= pruned.Points.Count, $"{seed}: pruning never adds a point");
                c.That(whole.Count(MapPointType.Shop) <= 3 && whole.Count(MapPointType.Elite) <= 5 && whole.Count(MapPointType.Unknown) <= whole.Counts.Unknowns, $"{seed}: never more than the counts");
                c.Equal(whole.Start.Children.Count, whole.StartPoints.Count, $"{seed}: before pruning every first-row point is one a path started from");
            }
        });
    }

    // ---------- the passes after the kinds are placed, on maps drawn by hand ----------

    private static readonly MapCounts Nothing = new(0, 0, 0, 0);

    private static string Places(ActMap map) => string.Join(" ", map.Points.Select(p => p.Coord.ToString()));

    private static string ChildrenOf(ActMap map, int col, int row) => string.Join(" ", map.Point(col, row)!.Children.Select(p => p.Coord.ToString()));

    private static void LaterPasses(Check c)
    {
        c.Group("pruning: of two stretches that say the same thing, the first in shuffled order goes", () =>
        {
            // A fork at (3,1), two Monster rooms side by side, a join at (3,3). The group holds the stretch
            // through (2,2) first, as the fork lists its children. One draw shuffles the two: 1 leaves the
            // order, 0 swaps it. The first is pruned: its inner point has one parent and one child and nothing
            // else needs it. Then nothing matches any more, and nothing is short.
            var both = new HashSet<int>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var drawn = Sketch("c3:ancient^3", "c3:monster!^2,4", "c2:monster^3 c4:monster^3", "c3:monster^3", "c3:rest!^3", "c3:boss");
                var rng = new GameRng(seed);
                var draw = new GameRng(seed).NextInt(2);
                both.Add(draw);
                var map = ActMapGenerator.Finish(drawn, Nothing, rng, prune: true, sideways: false);
                var gone = draw == 1 ? 2 : 4;
                var kept = draw == 1 ? 4 : 2;
                c.That(map.Point(gone, 2) is null && map.Point(kept, 2) is not null, $"seed {seed}: the draw {draw} takes the point in column {gone}");
                c.Equal($"({kept},2)", ChildrenOf(map, 3, 1), $"seed {seed}: the fork has one child left");
                c.Equal(1, map.Point(3, 3)!.Parents.Count, $"seed {seed}: the join has one parent left");
                c.Equal(4, map.Points.Count, $"seed {seed}: four points are left");
                c.Equal(1, rng.Counter, $"seed {seed}: one number");
            }
            c.Equal(2, both.Count, "both draws came up among the seeds");
        });

        c.Group("pruning: of three equal stretches the last in shuffled order stays", () =>
        {
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var drawn = Sketch("c3:ancient^3", "c3:monster!^2,3,4", "c2:monster^3 c3:monster^3 c4:monster^3", "c3:monster^3", "c3:rest!^3", "c3:boss");
                var order = new List<int> { 2, 3, 4 };
                new GameRng(seed).Shuffle(order);
                var rng = new GameRng(seed);
                var map = ActMapGenerator.Finish(drawn, Nothing, rng, prune: true, sideways: false);
                c.Equal($"({order[2]},2)", string.Join(" ", map.PointsInRow(2).Select(p => p.Coord.ToString())), $"seed {seed}: the one left in row 2");
                c.Equal(2, rng.Counter, $"seed {seed}: two numbers shuffle three stretches, and both prunings happen in one round");
            }
        });

        c.Group("pruning: the groups are taken in the character order of their keys, so a stretch from row 10 comes before one from row 2", () =>
        {
            // One spine up column 3 with two forks, each with two equal stretches: from (3,2) to (3,4) and from
            // (3,10) to (3,12). Their keys begin "3,2-" and "3,10-", and as texts "3,10-" sorts first. Each round
            // prunes the first group and finds everything again, so the first number decides the upper fork and
            // the second the lower one: 1 takes the stretch through column 2, 0 the one through column 4.
            var differing = 0;
            for (ulong seed = 1; seed <= 16; seed++)
            {
                var drawn = Sketch(
                    "c3:ancient^3", "c3:monster!^3", "c3:monster^2,4", "c2:monster^3 c4:monster^3", "c3:monster^3",
                    "c3:monster^3", "c3:monster^3", "c3:monster^3", "c3:monster^3", "c3:monster^3",
                    "c3:monster^2,4", "c2:monster^3 c4:monster^3", "c3:monster^3", "c3:rest!^3", "c3:boss");
                var draws = new GameRng(seed);
                var first = draws.NextInt(2);
                var second = draws.NextInt(2);
                if (first != second) differing++;
                var rng = new GameRng(seed);
                var map = ActMapGenerator.Finish(drawn, Nothing, rng, prune: true, sideways: false);
                c.Equal($"({(first == 1 ? 4 : 2)},11)", string.Join(" ", map.PointsInRow(11).Select(p => p.Coord.ToString())), $"seed {seed}: the first number ({first}) decides row 11");
                c.Equal($"({(second == 1 ? 4 : 2)},3)", string.Join(" ", map.PointsInRow(3).Select(p => p.Coord.ToString())), $"seed {seed}: the second number ({second}) decides row 3");
                c.Equal(2, rng.Counter, $"seed {seed}: two numbers");
            }
            c.That(differing >= 4, $"seeds whose two numbers differ, so that the order shows: {differing}");
        });

        c.Group("pruning: a point whose parent has no other child is left alone, and the other stretch goes instead", () =>
        {
            // Two equal stretches from the fork (3,1) to the join (3,4): through (2,2) (2,3) and through (4,2) (4,3).
            // (2,2) has a second parent, the Unknown room at (5,1) (another kind, so the stretches from the start
            // point do not match). So (2,2) is not pruned (two parents), and (2,3) is not either: its parent (2,2)
            // would be left without a child. The stretch through column 4 is pruned whichever comes first.
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var drawn = Sketch("c3:ancient^3,5", "c3:monster!^2,4 c5:unknown!^2", "c2:monster^2 c4:monster^4", "c2:monster^3 c4:monster^3", "c3:monster^3", "c3:rest!^3", "c3:boss");
                var rng = new GameRng(seed);
                var map = ActMapGenerator.Finish(drawn, Nothing, rng, prune: true, sideways: false);
                c.Equal("(2,2) (2,3) (3,1) (3,4) (3,5) (5,1)", Places(map), $"seed {seed}: column 4 is gone, column 2 is whole");
                c.Equal("(2,2)", ChildrenOf(map, 3, 1), $"seed {seed}: the fork leads to (2,2)");
                c.Equal(1, rng.Counter, $"seed {seed}: one number");
            }
        });

        c.Group("pruning: when no point can go, the link into the first stretch is cut", () =>
        {
            // Two equal stretches from (3,1) to (3,3), through (2,2) and through (4,2). Each inner point has a second
            // parent (of different kinds, so nothing else matches) and cannot be pruned. Then the link from the
            // fork to the next point of the first stretch in shuffled order is cut, since that point has another parent.
            var both = new HashSet<int>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var drawn = Sketch("c3:ancient^1,3,5", "c1:unknown!^2 c3:monster!^2,4 c5:shop!^4", "c2:monster^3 c4:monster^3", "c3:monster^3", "c3:rest!^3", "c3:boss");
                var rng = new GameRng(seed);
                var draw = new GameRng(seed).NextInt(2);
                both.Add(draw);
                var map = ActMapGenerator.Finish(drawn, Nothing, rng, prune: true, sideways: false);
                var cut = draw == 1 ? 2 : 4;
                var kept = draw == 1 ? 4 : 2;
                c.Equal(7, map.Points.Count, $"seed {seed}: no point is gone");
                c.Equal($"({kept},2)", ChildrenOf(map, 3, 1), $"seed {seed}: the draw {draw} cuts the link to column {cut}");
                c.Equal(1, map.Point(cut, 2)!.Parents.Count, $"seed {seed}: that point keeps its other parent");
                c.Equal(2, map.Point(kept, 2)!.Parents.Count, $"seed {seed}: the other stretch is untouched");
                c.Equal(1, rng.Counter, $"seed {seed}: one number");
            }
            c.Equal(2, both.Count, "both draws came up among the seeds");
        });

        c.Group("repair: shops before elites, Monster rooms only, never a fixed row", () =>
        {
            // One path. A shop and an elite are short, and one Monster room can be changed: (3,6), high enough
            // for an elite. The shop is put back first and takes it; the elite finds no room left. The Monster
            // room of the first row cannot be modified and is never a candidate. A list of one is not shuffled.
            string[] rows = ["c3:ancient^3", "c3:monster!^3", "c3:unknown^3", "c3:unknown^3", "c3:unknown^3", "c3:unknown^3", "c3:monster^3", "c3:rest!^3", "c3:boss"];
            var rng = new GameRng(3);
            var map = ActMapGenerator.Finish(Sketch(rows), new MapCounts(Rests: 0, Shops: 1, Elites: 1, Unknowns: 0), rng, prune: true, sideways: false);
            c.Equal(MapPointType.Shop, map.Point(3, 6)!.Type, "the one free Monster room becomes the shop");
            c.Equal(0, map.Count(MapPointType.Elite), "no elite");
            c.Equal(0, rng.Counter, "no number");

            var two = ActMapGenerator.Finish(Sketch(rows), new MapCounts(Rests: 0, Shops: 2, Elites: 0, Unknowns: 0), new GameRng(3), prune: true, sideways: false);
            c.Equal((MapPointType.Shop, MapPointType.Monster), (two.Point(3, 6)!.Type, two.Point(3, 1)!.Type), "a second shop finds only the fixed first row, and leaves it");

            // Elites alone: (3,6) is row 6, the lowest an elite may stand on.
            var elite = ActMapGenerator.Finish(Sketch(rows), new MapCounts(Rests: 0, Shops: 0, Elites: 1, Unknowns: 0), new GameRng(3), prune: true, sideways: false);
            c.Equal(MapPointType.Elite, elite.Point(3, 6)!.Type, "an elite on row 6");
            string[] low = ["c3:ancient^3", "c3:monster!^3", "c3:unknown^3", "c3:unknown^3", "c3:unknown^3", "c3:monster^3", "c3:unknown^3", "c3:rest!^3", "c3:boss"];
            var none = ActMapGenerator.Finish(Sketch(low), new MapCounts(Rests: 0, Shops: 0, Elites: 1, Unknowns: 0), new GameRng(3), prune: true, sideways: false);
            c.Equal(MapPointType.Monster, none.Point(3, 5)!.Type, "none on row 5");
        });

        c.Group("repair: the placement rules hold for what is put back", () =>
        {
            // One shop is short. Of the two Monster rooms, (3,3) stands right after a shop and may not be one;
            // (3,5) may. Two candidates are shuffled with one number; whatever the order, (3,5) gets it.
            for (ulong seed = 1; seed <= 8; seed++)
            {
                var drawn = Sketch("c3:ancient^3", "c3:monster!^3", "c3:shop^3", "c3:monster^3", "c3:unknown^3", "c3:monster^3", "c3:rest!^3", "c3:boss");
                var rng = new GameRng(seed);
                var map = ActMapGenerator.Finish(drawn, new MapCounts(Rests: 0, Shops: 2, Elites: 0, Unknowns: 0), rng, prune: true, sideways: false);
                c.Equal((MapPointType.Monster, MapPointType.Shop), (map.Point(3, 3)!.Type, map.Point(3, 5)!.Type), $"seed {seed}: the shop goes to (3,5)");
                c.Equal(1, rng.Counter, $"seed {seed}: one number");
            }
        });

        c.Group("repair can make two stretches equal: the map is pruned again, up to three rounds", () =>
        {
            // Two stretches from (3,1) to (3,4): Unknown then Monster in column 2, Unknown then shop in column 4.
            // They differ, so the first round prunes nothing. A second shop is short, and the one Monster room
            // that can be changed is (2,3): it becomes a shop, no number drawn. Now the stretches say the same,
            // and the second round prunes the first in shuffled order, both its inner points. The shop count is
            // short again and no Monster room is left: nothing more happens.
            var both = new HashSet<int>();
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var drawn = Sketch("c3:ancient^3", "c3:monster!^2,4", "c2:unknown^2 c4:unknown^4", "c2:monster^3 c4:shop^3", "c3:treasure!^3", "c3:rest!^3", "c3:boss");
                var rng = new GameRng(seed);
                var draw = new GameRng(seed).NextInt(2);
                both.Add(draw);
                var map = ActMapGenerator.Finish(drawn, new MapCounts(Rests: 0, Shops: 2, Elites: 0, Unknowns: 0), rng, prune: true, sideways: false);
                var kept = draw == 1 ? 4 : 2;
                c.Equal($"({kept},2) ({kept},3) (3,1) (3,4) (3,5)", string.Join(" ", map.Points.OrderBy(p => p.Col != kept).ThenBy(p => p.Row).Select(p => p.Coord.ToString())), $"seed {seed}: one stretch is left, the one in column {kept}");
                c.Equal((MapPointType.Unknown, MapPointType.Shop), (map.Point(kept, 2)!.Type, map.Point(kept, 3)!.Type), $"seed {seed}: an Unknown room and a shop");
                c.Equal(1, rng.Counter, $"seed {seed}: one number, the second round's shuffle");
            }
            c.Equal(2, both.Count, "both draws came up among the seeds");
        });

        c.Group("the sideways passes: points of a row move apart, and one that sticks out moves back in line", () =>
        {
            // Two paths side by side in columns 2 and 3. Worked through by hand:
            // row 1: (3,1) can stand in 4 (next to the start point's column and its child's) and is then 2 from its neighbour: it moves;
            // row 2: (2,2) moves to 1 and (3,2), whose parent now stands in 4, to 4;
            // row 3: (2,3) must stay within one column of the boss and of (1,2): only column 2; (3,3) moves to 4;
            // then (1,2) sticks out to the left of its parent and its child, both in column 2, and moves back to 2.
            var drawn = Sketch("c3:ancient^2,3", "c2:monster^2 c3:monster^3", "c2:monster^2 c3:monster^3", "c2:rest^3 c3:rest^3", "c3:boss");
            var rng = new GameRng(1);
            var map = ActMapGenerator.Finish(drawn, Nothing, rng, prune: false, sideways: true);
            c.Equal("(2,1) (2,2) (2,3) (4,1) (4,2) (4,3)", Places(map), "the two paths end in columns 2 and 4");
            c.Equal("(2,2) | (4,2) | (2,3) | (4,3)", $"{ChildrenOf(map, 2, 1)} | {ChildrenOf(map, 4, 1)} | {ChildrenOf(map, 2, 2)} | {ChildrenOf(map, 4, 2)}", "the links are as they were");
            c.That(map.Points.All(p => ReferenceEquals(map.Point(p.Coord), p)), "every point is where the grid has it");
            c.Equal(0, rng.Counter, "no number");
            c.Equal((new MapCoord(3, 0), new MapCoord(3, 4)), (map.Start.Coord, map.Boss.Coord), "the start point and the boss never move");
        });

        c.Group("the sideways passes: a row is gone over again until nothing moves", () =>
        {
            // Row 2 holds (1,2), (2,2) and (3,2). (1,2) may only stand in column 1. (2,2) may stand in 2 or 3
            // and at first gains nothing: 3 is taken. (3,2) may stand in 4 or 5 and moves to 5, three away from
            // its neighbour. In the second turn (2,2) finds column 3 free and two away from both neighbours:
            // it moves. Rows 1 and 3 have no better place for anyone. Then the two first-row points that
            // stick out to the left of their parent (the start point, column 3) and their child move one
            // column right: (0,1) to 1, (2,1) to 3.
            var drawn = Sketch("c3:ancient^0,2,4", "c0:monster^1 c2:monster^2 c4:monster^3", "c1:monster^2 c2:monster^3 c3:monster^5", "c2:rest^3 c3:rest^3 c5:rest^3", "c3:boss");
            var map = ActMapGenerator.Finish(drawn, Nothing, new GameRng(1), prune: false, sideways: true);
            c.Equal("(1,1) (1,2) (2,3) (3,1) (3,2) (3,3) (4,1) (5,2) (5,3)", Places(map), "where every point ends");
            c.Equal("(1,2) | (3,2) | (5,2)", $"{ChildrenOf(map, 1, 1)} | {ChildrenOf(map, 3, 1)} | {ChildrenOf(map, 4, 1)}", "the first row's links are as they were");
        });

        c.Group("the sideways passes: of two columns that are equally good the lower one is taken", () =>
        {
            // Row 2 holds (1,2) and (6,2), which cannot move, and (2,2), which may stand in 2, 3 or 4. In
            // column 3 it is two from its left neighbour, in column 4 two from its right one: the same, and
            // only a better place is taken, so the first one found, column 3, stays. In row 3 the point under it
            // moves from 3 to 4 (two from its neighbour in column 2) and then, sticking out to the right of
            // its parent and the boss, both in column 3, back to 3. (0,1) sticks out to the left and moves to 1.
            var drawn = Sketch("c3:ancient^0,3,6", "c0:monster^1 c3:monster^2 c6:monster^6", "c1:monster^2 c2:monster^3 c6:monster^6", "c2:rest^3 c3:rest^3 c6:rest^3", "c3:boss");
            var map = ActMapGenerator.Finish(drawn, Nothing, new GameRng(1), prune: false, sideways: true);
            c.Equal("(1,1) (1,2) (2,3) (3,1) (3,2) (3,3) (6,1) (6,2) (6,3)", Places(map), "where every point ends");
            c.Equal("(3,2)", ChildrenOf(map, 3, 1), "the middle path runs straight up column 3");
        });

        c.Group("the sideways passes: the whole map moves a column towards two empty outer columns; a cell that is taken stops a move", () =>
        {
            // Columns 5 and 6 are empty and column 1 is not: everything moves one column to the right. Then nothing
            // can move apart (every free cell is no further from the neighbour), and the three points that stick out
            // to the left of their parent and child, (2,1), (2,2) and (2,3) after the move, find the cell to their
            // right taken, as does (3,2), which sticks out to the right. The paths stay crossed.
            var drawn = Sketch("c3:ancient^1,2", "c1:monster^2 c2:monster^1", "c1:monster^2 c2:monster^1", "c1:rest^3 c2:rest^3", "c3:boss");
            var map = ActMapGenerator.Finish(drawn, Nothing, new GameRng(1), prune: false, sideways: true);
            c.Equal("(2,1) (2,2) (2,3) (3,1) (3,2) (3,3)", Places(map), "one column to the right, and no further move");
            c.Equal("(3,2) | (2,2) | (3,3) | (2,3)", $"{ChildrenOf(map, 2, 1)} | {ChildrenOf(map, 3, 1)} | {ChildrenOf(map, 2, 2)} | {ChildrenOf(map, 3, 2)}", "the links are as they were");

            // Columns 0 and 1 empty, column 5 not: one column to the left.
            var right = ActMapGenerator.Finish(Sketch("c3:ancient^5", "c5:monster^5", "c5:monster^5", "c5:rest^3", "c3:boss"), Nothing, new GameRng(1), prune: false, sideways: true);
            c.Equal("(4,1) (4,2) (4,3)", Places(right), "to the left");
            // Both sides empty, or neither: no move.
            var middle = ActMapGenerator.Finish(Sketch("c3:ancient^3", "c3:monster^3", "c3:monster^3", "c3:rest^3", "c3:boss"), Nothing, new GameRng(1), prune: false, sideways: true);
            c.Equal("(3,1) (3,2) (3,3)", Places(middle), "both sides empty");
            var wide = ActMapGenerator.Finish(Sketch("c3:ancient^0,6", "c0:monster^0 c6:monster^6", "c0:monster^0 c6:monster^6", "c0:rest^3 c6:rest^3", "c3:boss"), Nothing, new GameRng(1), prune: false, sideways: true);
            c.Equal("(0,1) (0,2) (0,3) (6,1) (6,2) (6,3)", Places(wide), "neither side empty");
            // One outer column empty is not enough.
            var one = ActMapGenerator.Finish(Sketch("c3:ancient^4", "c4:monster^4", "c4:monster^4", "c4:rest^3", "c3:boss"), Nothing, new GameRng(1), prune: false, sideways: true);
            c.Equal("(4,1) (4,2) (4,3)", Places(one), "columns 5 and 6 empty, and 0 and 1 too");
        });
    }

    // ---------- travel ----------

    /// <summary>A small map for the travel rules: two first-row points, a fork, a join, four grid rows.</summary>
    private static ActMap Fork() => Sketch(
        "c3:ancient^2,4",
        "c2:monster^1,2 c4:monster^4",
        "c1:unknown^2 c2:shop^2 c4:elite^3",
        "c2:rest^3 c3:rest^3",
        "c3:boss");

    private static void Travel(Check c)
    {
        c.Group("where the run can go: the children, sorted by column; the boss from the last row; nowhere from the boss", () =>
        {
            var run = Unentered(PlayedSeed, Fork());
            var map = MapOf(run);
            string Next() => string.Join(" ", map.Next(run).Select(p => p.ToString()));

            c.Equal("(3,0) Ancient", Next(), "nothing visited: the start point");
            c.That(Throws<InvalidOperationException>(() => Enter(run, 2, 1)), "a first-row point cannot be entered before the start point");
            Enter(run, 3, 0);
            c.Equal("(2,1) Monster (4,1) Monster", Next(), "the start point's children");
            c.That(Throws<InvalidOperationException>(() => Enter(run, 3, 0)), "a visited point is no option");
            c.That(Throws<InvalidOperationException>(() => Enter(run, 1, 2)), "a point two rows up is no option");
            c.That(Throws<InvalidOperationException>(() => Enter(run, 5, 1)), "a place with no point is no option");
            c.Equal(1, run.State.TotalFloor, "and a refused step changes nothing");
            Enter(run, 2, 1);
            c.Equal("(1,2) Unknown (2,2) Shop", Next(), "two children");
            c.That(Throws<InvalidOperationException>(() => Enter(run, 4, 2)), "the other branch is out of reach");
            Enter(run, 2, 2);
            c.Equal("(2,3) RestSite", Next(), "one child");
            Enter(run, 2, 3);
            c.Equal("(3,4) Boss", Next(), "on the last row of the grid: the boss");
            c.Equal(new MapArrival(new MapCoord(3, 4), MapPointType.Boss, RoomKind.Boss), Enter(run, 3, 4), "the boss");
            c.Equal("", Next(), "on the boss: nowhere");
            c.Equal((5, 5), (run.State.ActFloor, run.State.TotalFloor), "five points entered; the boss's act floor is its row plus 1");
        });

        c.Group("free travel: while a relic allows it, every point of the next row", () =>
        {
            var run = Unentered(PlayedSeed, Fork(), relics: [() => new Wings()]);
            var map = MapOf(run);
            var held = (Wings)run.State.Relics.Single(r => r is Wings);
            string Next() => string.Join(" ", map.Next(run).Select(p => p.Coord.ToString()));

            Enter(run, 3, 0);
            c.Equal("(2,1) (4,1)", Next(), "from the start point the next row is its children anyway");
            Enter(run, 4, 1);
            c.Equal("(1,2) (2,2) (4,2)", Next(), "the whole next row, by column");
            held.On = false;
            c.Equal("(4,2)", Next(), "used up: the children again");
            held.On = true;
            c.Equal(new MapArrival(new MapCoord(2, 2), MapPointType.Shop, RoomKind.Shop), Enter(run, 2, 2), "a point that is no child can be entered");
            Enter(run, 3, 3);
            c.Equal("(3,4)", Next(), "from the last row it is the boss alone, boots or not");
        });

        c.Group("a new act: its own map and stream, the path cleared, the odds set back, the floors as the game counts them", () =>
        {
            var run = Unentered(PlayedSeed);
            var world = WorldOf(run);
            Enter(run, 3, 0);
            Enter(run, 5, 1);
            Enter(run, 6, 2);
            Enter(run, 6, 3);
            c.Equal("0.2 -2 0.04 0.06", Odds(world.UnknownOdds), "the first act's Unknown room moved the odds");
            var first = MapOf(run).Map;

            var map = MapState.BeginAct(run, 1);
            c.Equal(1, run.State.ActIndex, "the second act");
            c.Equal(0, run.State.ActFloor, "the act floor starts again");
            c.Equal(4, run.State.TotalFloor, "the total floor goes on");
            c.Equal(0, map.Visited.Count + map.History.Count, "nothing visited");
            c.Equal("0.1 -1 0.02 0.03", Odds(world.UnknownOdds), "the odds are the start values again");
            c.Equal(1, world.Streams.Counter(RunStream.UnknownMapPoint), "the stream is not");
            c.That(!ReferenceEquals(first, map.Map), "another map");
            c.Equal(15, map.Map.Rows, "Hive's grid is a row shorter");
            c.Equal(ActMapGenerator.Generate(new RunStreams(PlayedSeed).ActMap(2), GameLists.Hive).Describe(), map.Map.Describe(), "made on the stream act_2_map by Hive's rules");
            c.Sequence(["(3,0)"], map.Next(run).Select(p => p.Coord.ToString()), "the start point is the one option");
            Enter(run, 3, 0);
            c.Equal((1, 5), (run.State.ActFloor, run.State.TotalFloor), "the Ancient of the second act");
            c.Equal(new RunPlace(1, 0, 3), world.Streams.Place, "the place names the act");

            c.That(Throws<ArgumentOutOfRangeException>(() => MapState.BeginAct(run, 3)), "the run has three acts");
            var bare = new Run(new RunSetup(), new FixedFights());
            c.That(Throws<InvalidOperationException>(() => MapState.BeginAct(bare, 0)), "no map without the run's world");
            c.That(Throws<InvalidOperationException>(() => _ = bare.State.Part<MapState>().Map), "and no map to read before one is made");
        });
    }

    // ---------- the roll of an Unknown room ----------

    private static void UnknownRooms(Check c)
    {
        // The first number of unknown_map_point of seed 1BUSLN0FY5 is above 0.15 (the real save: an Event from
        // base odds, every kind allowed). From base odds the bars are: Monster up to 0.10, Treasure up to 0.12,
        // Shop up to 0.15. Whatever is barred below only lowers a bar, so each roll here is an Event, and the
        // odds afterwards tell which kinds were in the roll: a kind that was allowed and not rolled grows by
        // its start value, a barred one stays.

        c.Group("an Unknown room with nothing barred: every other kind grows", () =>
        {
            var run = Unentered(PlayedSeed, Sketch("c3:ancient^3", "c3:monster^3", "c3:unknown^2,3", "c2:monster^3 c3:shop^3", "c3:boss"));
            Enter(run, 3, 0);
            Enter(run, 3, 1);
            c.Equal(new MapArrival(new MapCoord(3, 2), MapPointType.Unknown, RoomKind.Event), Enter(run, 3, 2), "the room");
            c.Equal("0.2 -2 0.04 0.06", Odds(WorldOf(run).UnknownOdds), "Monster, Treasure and Shop grew; Elite sank");
            c.Equal(1, WorldOf(run).Streams.Counter(RunStream.UnknownMapPoint), "one number");
            c.Sequence([RoomKind.Event], MapOf(run).History[^1].Rooms, "recorded with the point, whose kind stays Unknown");
            c.Equal(MapPointType.Unknown, MapOf(run).History[^1].PointType, "the point's kind");
        });

        c.Group("Shop is barred when every point after the Unknown one is a shop", () =>
        {
            var run = Unentered(PlayedSeed, Sketch("c3:ancient^3", "c3:monster^3", "c3:unknown^3", "c3:shop^3", "c3:boss"));
            Enter(run, 3, 0);
            Enter(run, 3, 1);
            c.Equal(RoomKind.Event, Enter(run, 3, 2).Room, "the room");
            c.Equal("0.2 -2 0.04 0.03", Odds(WorldOf(run).UnknownOdds), "Shop was not in the roll and did not grow");
        });

        c.Group("Shop is barred when the point before held a shop, and only then", () =>
        {
            var run = Unentered(PlayedSeed, Sketch("c3:ancient^3", "c3:shop^3", "c3:unknown^3", "c3:unknown^3", "c3:monster^3", "c3:boss"));
            Enter(run, 3, 0);
            c.Equal(RoomKind.Shop, Enter(run, 3, 1).Room, "a shop");
            Enter(run, 3, 2);
            c.Equal("0.2 -2 0.04 0.03", Odds(WorldOf(run).UnknownOdds), "right after a shop: Shop did not grow");
            // The second Unknown room is not after a shop. Its number is not known here, so the room is not
            // asserted: only that Shop was in the roll again (it grew, or it was rolled and stands at its start value
            // while the room is a shop).
            var second = Enter(run, 3, 3);
            var shop = WorldOf(run).UnknownOdds.Shop;
            c.That(second.Room == RoomKind.Shop ? shop == 0.03f : shop == 0.03f + 0.03f, $"one room later Shop is in the roll again (room {second.Room}, Shop odds {Exact(shop)})");
        });

        c.Group("a shop among the rooms of the point before bars it too (a room an event opened)", () =>
        {
            var run = Unentered(PlayedSeed, Sketch("c3:ancient^3", "c3:monster^3", "c3:unknown^3", "c3:monster^3", "c3:boss"));
            Enter(run, 3, 0);
            Enter(run, 3, 1);
            MapOf(run).AddRoom(RoomKind.Shop);
            c.Sequence([RoomKind.Monster, RoomKind.Shop], MapOf(run).History[^1].Rooms, "two rooms on one point");
            Enter(run, 3, 2);
            c.Equal("0.2 -2 0.04 0.03", Odds(WorldOf(run).UnknownOdds), "Shop did not grow");
            c.That(Throws<InvalidOperationException>(() => new MapState().AddRoom(RoomKind.Monster)), "no room without a visited point");
        });

        c.Group("a relic takes kinds out of the roll (Juzu Bracelet's rule: no hallway fight)", () =>
        {
            var run = Unentered(PlayedSeed, Sketch("c3:ancient^3", "c3:monster^3", "c3:unknown^2,3", "c2:monster^3 c3:shop^3", "c3:boss"), relics: [() => new NoMonsters()]);
            Enter(run, 3, 0);
            Enter(run, 3, 1);
            c.Equal(RoomKind.Event, Enter(run, 3, 2).Room, "the room");
            c.Equal("0.1 -2 0.04 0.06", Odds(WorldOf(run).UnknownOdds), "Monster was not in the roll and did not grow");

            var allowed = RunHooks.ModifyUnknownMapPointRoomTypes(run.Context, UnknownRoomOdds.Allowed(shopBarred: true));
            c.Sequence([RolledRoom.Elite, RolledRoom.Treasure, RolledRoom.Event], allowed.OrderBy(r => r), "with Shop barred as well: Elite (never rolled), Treasure and Event are left");
            c.Equal(5, UnknownRoomOdds.AllRooms.Count, "and the full set was not touched");
        });

        c.Group("the house rule of a director: a kind the arena cannot show is rolled again without it, from the same stream", () =>
        {
            // Seed 75F277YN2G: the first number is above 0.15, the second at most 0.1 (the real run above).
            var drawn = Sketch("c3:ancient^3", "c3:monster^3", "c3:unknown^3", "c3:monster^3", "c3:boss");
            var plain = Unentered(ThreeActSeed, drawn);
            Enter(plain, 3, 0);
            Enter(plain, 3, 1);
            c.Equal(RoomKind.Event, Enter(plain, 3, 2).Room, "as the game rolls: an Event");
            c.Equal(1, WorldOf(plain).Streams.Counter(RunStream.UnknownMapPoint), "one number");

            var run = Unentered(ThreeActSeed, drawn, cannotHold: [RolledRoom.Event]);
            Enter(run, 3, 0);
            Enter(run, 3, 1);
            c.Equal(RoomKind.Monster, Enter(run, 3, 2).Room, "the Event cannot be shown: the second number, at most 0.1, is a Monster room");
            c.Equal(2, WorldOf(run).Streams.Counter(RunStream.UnknownMapPoint), "two numbers");
            c.Equal("0.1 -2 0.04 0.06", Odds(WorldOf(run).UnknownOdds), "the odds moved once, for the roll that counted: Monster at its start value, the others grown");

            // With the Event and the Monster room both out, the game's own rule can fall back on the lowest kind left,
            // an elite. A director that can show nothing at all gets no room: the roll fails, and it has to end the run.
            var none = Unentered(ThreeActSeed, drawn, cannotHold: [RolledRoom.Event, RolledRoom.Monster, RolledRoom.Elite, RolledRoom.Treasure, RolledRoom.Shop]);
            Enter(none, 3, 0);
            Enter(none, 3, 1);
            c.That(Throws<InvalidOperationException>(() => Enter(none, 3, 2)), "nothing can be shown: the roll fails");
        });

        c.Group("keyed streams: the roll is the place's own, the long stream is not touched", () =>
        {
            var drawn = Sketch("c3:ancient^3", "c3:monster^3", "c3:unknown^3", "c3:monster^3", "c3:boss");
            Run Keyed() => new(new RunSetup { Seed = PlayedSeed }, new Tour { Drawn = drawn, Mode = StreamMode.Keyed, OpenMap = false });
            var run = Keyed();
            Enter(run, 3, 0);
            Enter(run, 3, 1);
            var room = Enter(run, 3, 2).Room;
            c.Equal(0, WorldOf(run).Streams.Counter(RunStream.UnknownMapPoint), "no number of the long stream");
            c.Equal(new RunPlace(0, 2, 3), WorldOf(run).Streams.Place, "the place of the roll");
            // The number is the first of the stream keyed by seed, place and purpose: the roll from base odds by the rule.
            var number = new GameRng(RunStreams.KeyedSeed(new RunSeed(PlayedSeed), new RunPlace(0, 2, 3), Purpose.UnknownRoom)).NextFloat();
            var byRule = number <= 0.1f ? RoomKind.Monster : number <= 0.1f + 0.02f ? RoomKind.Treasure : number <= 0.1f + 0.02f + 0.03f ? RoomKind.Shop : RoomKind.Event;
            c.Equal(byRule, room, "the room that number gives");
            var again = Keyed();
            Enter(again, 3, 0);
            Enter(again, 3, 1);
            c.Equal(room, Enter(again, 3, 2).Room, "and the same for every run that stands there");
        });
    }

    // ---------- the screen, against docs/screens.md ----------

    /// <summary>
    /// The map of the worked example of docs/screens.md ("Example (solo, Act 1, standing on floor
    /// 11)"): rows 10 to 16 are the example's own, the rows below are the path its
    /// <c>visited</c> names, and one branch that cannot be reached from (4,10) runs up column 0
    /// and 1, as the example says. The last row leads to the boss, as in every map of the game.
    /// </summary>
    private static ActMap DocsExample() => Sketch(
        "c3:ancient^3",
        "c3:monster^2",
        "c2:monster^2",
        "c2:unknown^1",
        "c1:shop^2",
        "c2:monster^2",
        "c2:elite^3",
        "c3:unknown^3",
        "c3:rest^3,2",
        "c2:treasure^1 c3:treasure^4",
        "c1:monster^0 c4:monster^3,5",
        "c0:monster^0 c3:rest^2,4 c5:unknown^4,6",
        "c0:monster^1 c2:elite^1,3 c4:monster^3,5 c6:shop^5,6",
        "c1:monster^2 c3:unknown^2,4 c5:elite^4,5 c6:monster^5",
        "c2:elite^2 c4:monster^3,4 c5:unknown^4",
        "c2:rest^3 c3:rest^3 c4:rest^3",
        "c3:boss");

    private static readonly (int Col, int Row)[] DocsPath = [(3, 0), (3, 1), (2, 2), (2, 3), (1, 4), (2, 5), (2, 6), (3, 7), (3, 8), (3, 9), (4, 10)];

    private static Run DocsRun()
    {
        var setup = new RunSetup
        {
            Seed = PlayedSeed, Hp = 52, Gold = 187,
            Deck = new[] { "Bash+", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "Headbutt", "Inflame", "PommelStrike", "ShrugItOff",
                "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "TwinStrike" }.Select(DeckCard.Parse).ToList(),
            ExtraRelics = [() => new Named("Pomander"), () => new Named("Vajra")],
            Potions = ["FirePotion", "BloodPotion"],
        };
        var run = new Run(setup, new Tour { Drawn = DocsExample(), Boss = "CeremonialBeastBoss", OpenMap = false });
        foreach (var (col, row) in DocsPath) Enter(run, col, row);
        run.Push(new MapScreen());
        return run;
    }

    private static void ScreenAgainstTheDocs(Check c)
    {
        c.Group("the worked example of docs/screens.md, key for key", () =>
        {
            var run = DocsRun();
            var snapshot = run.Snapshot();
            c.Equal("map", snapshot.Screen, "screen");
            c.That(snapshot.AwaitingDecision && snapshot.Waiting is null, "a decision");
            c.Equal("run,player,potions,boss,at,next,map", Keys(snapshot.State), "the state's keys in the mod's order");

            // The example's own text, with two things that are the arena's or the game's: the potion texts are the
            // arena's wording (the docs call theirs illustrative), and the three Rest Sites of row 15 list the boss's
            // column, because in the game every point of the last row leads to the boss and the reader prints every child.
            var fire = JsonValue.Create(Content.Registry.Potion("FirePotion").Text(null)).ToJsonString(Plain);
            var blood = JsonValue.Create(Content.Registry.Potion("BloodPotion").Text(null)).ToJsonString(Plain);
            var expected = (
                """
                {"run":{"act":1,"floor":11,"ascension":0},
                "player":{"character":"The Ironclad","hp":52,"max_hp":80,"gold":187,"relics":["Burning Blood","Pomander","Vajra"],"deck_size":14,
                "deck":{"Bash+":1,"Defend":4,"Headbutt":1,"Inflame":1,"Pommel Strike":1,"Shrug It Off":1,"Strike":4,"Twin Strike":1}},
                "potions":[{"slot":0,"name":"Fire Potion","text":FIRE,"target":"AnyEnemy","usable":false},
                {"slot":1,"name":"Blood Potion","text":BLOOD,"target":"AnyPlayer","usable":true},null],
                "boss":"Ceremonial Beast","at":"(4,10) monster",
                "next":[{"n":0,"at":"(3,11)","type":"rest","ahead":"monster 0-2, elite 0-2, unknown 0-1, rest 2"},
                {"n":1,"at":"(5,11)","type":"unknown","ahead":"monster 0-2, elite 0-1, unknown 1-2, shop 0-1, rest 1"}],
                "map":{"key":"KEY",
                "visited":"(3,0) ancient, (3,1) monster, (2,2) monster, (2,3) unknown, (1,4) shop, (2,5) monster, (2,6) elite, (3,7) unknown, (3,8) rest, (3,9) treasure, (4,10) monster",
                "rows":{"10":"@c4:monster^3,5","11":"c3:rest^2,4 c5:unknown^4,6","12":"c2:elite^1,3 c4:monster^3,5 c6:shop^5,6",
                "13":"c1:monster^2 c3:unknown^2,4 c5:elite^4,5 c6:monster^5","14":"c2:elite^2 c4:monster^3,4 c5:unknown^4",
                "15":"c2:rest^3 c3:rest^3 c4:rest^3","16":"c3:boss"}}}
                """).ReplaceLineEndings("").Replace("FIRE", fire).Replace("BLOOD", blood).Replace("KEY",
                    "rows go upward by row number; each node is cCOL:TYPE^CHILD_COLS (children are in the next row); @ marks you; (quest) marks a quest target. "
                    + "next[].ahead: min-max count of each room type over the routes from that node to the boss, itself included.");
            c.Equal(expected, Text(snapshot.State), "the state");

            c.Equal("map:0 map:1 discard_potion:0 potion:1 discard_potion:1", Ids(snapshot), "the map ids, then the potion ids slot by slot, a use before its discard");
            c.Equal("Go to Rest Site at (3,11)", Label(snapshot, "map:0"), "label of map:0");
            c.Equal("Go to Unknown (?) at (5,11)", Label(snapshot, "map:1"), "label of map:1");
            c.Equal("Discard Fire Potion", Label(snapshot, "discard_potion:0"), "label of a discard");
            c.Equal("Use Blood Potion", Label(snapshot, "potion:1"), "label of a use");

            var envelope = snapshot.ToJson();
            c.Equal("version,screen,awaiting_decision,state,actions", Keys(envelope), "the envelope has no waiting key");
            c.That(!Text(snapshot.State["map"]).Any(ch => ch is '<' or '>' or '\'' or '+'), "no character in the map's strings that the plain JSON writer would escape");
        });

        c.Group("the same example further up: the last row, and the boss's own point", () =>
        {
            var run = DocsRun();
            foreach (var (col, row) in new[] { (3, 11), (2, 12), (1, 13), (2, 14), (2, 15) }) Enter(run, col, row);
            var last = run.Snapshot();
            c.Equal("(2,15) rest", last.State["at"]!.GetValue<string>(), "at");
            c.Equal("""[{"n":0,"at":"(3,16)","type":"boss"}]""", Text(last.State["next"]), "the boss is the one option and has no summary of what is ahead");
            c.Equal("""{"15":"@c2:rest^3","16":"c3:boss"}""", Text(last.State["map"]!["rows"]), "the rows: where the player stands, and the boss");
            c.Equal("Go to Boss at (3,16)", Label(last, "map:0"), "the label");
            c.Equal("Ceremonial Beast", last.State["boss"]!.GetValue<string>(), "the boss is still named");
            c.Equal(16, last.State["run"]!["floor"]!.GetValue<int>(), "floor 16");

            Enter(run, 3, 16);
            var over = run.Snapshot();
            c.Equal("run,player,potions,at,next,map", Keys(over.State), "standing on the boss: no boss key");
            c.Equal("(3,16) boss", over.State["at"]!.GetValue<string>(), "at");
            c.Equal("[]", Text(over.State["next"]), "no option");
            c.Equal("""{"16":"@c3:boss"}""", Text(over.State["map"]!["rows"]), "the rows");
            c.That(!over.AwaitingDecision && over.Actions.Count == 0, "not a decision: no action at all, potion ids included");
            c.Equal("nowhere left to travel on this map; close the map to continue", over.Waiting, "the mod's waiting text");
            c.Equal(true, over.State["potions"]![1]!["usable"]!.GetValue<bool>(), "usable is still told in a waiting snapshot");
        });

        c.Group("the words for the kinds of point, in the state and in the labels", () =>
        {
            (MapPointType Type, string Word, string Label)[] kinds =
            [
                (MapPointType.Monster, "monster", "Monster"), (MapPointType.Elite, "elite", "Elite"), (MapPointType.RestSite, "rest", "Rest Site"),
                (MapPointType.Shop, "shop", "Shop"), (MapPointType.Treasure, "treasure", "Treasure"), (MapPointType.Unknown, "unknown", "Unknown (?)"),
                (MapPointType.Boss, "boss", "Boss"), (MapPointType.Ancient, "ancient", "Ancient"),
            ];
            foreach (var (type, word, label) in kinds)
            {
                c.Equal(word, MapScreen.TypeWord(type), $"{type} in the state");
                c.Equal(label, MapScreen.TypeLabel(type), $"{type} in a label");
            }
            c.Equal("unassigned", MapScreen.TypeWord(MapPointType.Unassigned), "any other member in lower case");
            c.Sequence(["Unassigned", "Unknown", "Shop", "Treasure", "RestSite", "Monster", "Elite", "Boss", "Ancient"], Enum.GetNames<MapPointType>(), "the kinds in the game's order (the pruning pass sorts by their numbers)");
        });
    }

    // ---------- the screens of a real map, written down from the save ----------

    private static void ScreensOfARealMap(Check c)
    {
        c.Group($"seed {PlayedSeed}: the first map of the act, as the save's map says it must look", () =>
        {
            var run = new Run(new RunSetup { Seed = PlayedSeed }, new Tour());
            var snapshot = run.Snapshot();
            c.Equal("map", snapshot.Screen, "the map opens with the start point visited");
            c.Equal("run,player,potions,boss,at,next,map", Keys(snapshot.State), "keys: no ancient key in a first act");
            c.Equal("""{"act":1,"floor":1,"ascension":0}""", Text(snapshot.State["run"]), "floor 1: Neow's room");
            c.Equal("""{"character":"The Ironclad","hp":80,"max_hp":80,"gold":99,"relics":["Burning Blood"],"deck_size":10,"deck":{"Bash":1,"Defend":4,"Strike":5}}""",
                Text(snapshot.State["player"]), "the player block without potions");
            c.Equal("[null,null,null]", Text(snapshot.State["potions"]), "three empty slots");
            // The save's boss_id of this seed is VANTOM_BOSS (README of the golden saves: Vantom).
            c.Equal("Vantom", snapshot.State["boss"]!.GetValue<string>(), "the boss is named from the start");
            c.Equal("(3,0) ancient", snapshot.State["at"]!.GetValue<string>(), "at");
            c.Equal("(3,0) ancient", snapshot.State["map"]!["visited"]!.GetValue<string>(), "visited: the same single entry");
            c.Equal(MapScreen.MapKey, snapshot.State["map"]!["key"]!.GetValue<string>(), "the key");
            c.Equal("key,visited,rows", Keys(snapshot.State["map"]), "the map's keys");

            var next = snapshot.State["next"]!.AsArray();
            c.Sequence(["(1,1)", "(3,1)", "(5,1)"], next.Select(n => n!["at"]!.GetValue<string>()), "the options are the start point's children, by column");
            c.That(next.All(n => n!["type"]!.GetValue<string>() == "monster"), "all Monster rooms");
            c.Sequence([0, 1, 2], next.Select(n => n!["n"]!.GetValue<int>()), "numbered from 0");
            c.That(next.All(n => Keys(n) == "n,at,type,ahead"), "each option's keys");
            c.Equal("map:0 map:1 map:2", Ids(snapshot), "one id an option; no potion to act on");
            c.Equal("Go to Monster at (5,1)", Label(snapshot, "map:2"), "a label");

            // Every row of the save's map, each point with its children's columns in ascending order.
            string[] rows =
            [
                "@c3:ancient^1,3,5",
                "c1:monster^1 c3:monster^3 c5:monster^6",
                "c1:monster^1 c3:monster^4 c6:monster^6",
                "c1:monster^1 c4:monster^3,5 c6:unknown^5,6",
                "c1:monster^0,2 c3:monster^3 c5:monster^4 c6:shop^6",
                "c0:monster^0 c2:monster^2 c3:monster^3,4 c4:shop^4,5 c6:monster^5",
                "c0:rest^0 c2:elite^2 c3:monster^3 c4:unknown^3 c5:monster^4,6",
                "c0:unknown^0 c2:monster^2 c3:monster^3,4 c4:monster^4 c6:rest^6",
                "c0:monster^0 c2:unknown^2 c3:unknown^2 c4:rest^4 c6:elite^6",
                "c0:treasure^0 c2:treasure^1,2 c4:treasure^4 c6:treasure^6",
                "c0:monster^0 c1:rest^1 c2:monster^2 c4:monster^3,5 c6:unknown^5",
                "c0:shop^0 c1:elite^0 c2:unknown^2 c3:rest^2,3 c5:elite^4,5",
                "c0:rest^0 c2:elite^2 c3:unknown^2 c4:rest^4 c5:monster^4",
                "c0:unknown^1 c2:unknown^3 c4:unknown^3",
                "c1:monster^2 c3:monster^2",
                "c2:rest^3",
                "c3:boss",
            ];
            var shown = snapshot.State["map"]!["rows"]!.AsObject();
            c.Sequence(Enumerable.Range(0, 17).Select(r => r.ToString(CultureInfo.InvariantCulture)), shown.Select(kv => kv.Key), "the whole map is shown: rows 0 to 16 in order");
            for (var row = 0; row < rows.Length; row++)
                c.Equal(rows[row], shown[row.ToString(CultureInfo.InvariantCulture)]?.GetValue<string>(), $"row {row}");
        });

        c.Group($"seed {PlayedSeed}: a tour up the real map, every screen worked out from the save's map", () =>
        {
            // A way without an Unknown point up to row 12: (3,1) (3,2) (4,3) (5,4) (4,5) (5,6) (4,7) (4,8) (4,9) (4,10).
            var run = new Run(new RunSetup { Seed = PlayedSeed }, new Tour());
            (string Id, string Label, string Room)[] steps =
            [
                ("map:1", "Go to Monster at (3,1)", "Monster"), ("map:0", "Go to Monster at (3,2)", "Monster"), ("map:0", "Go to Monster at (4,3)", "Monster"),
                ("map:1", "Go to Monster at (5,4)", "Monster"), ("map:0", "Go to Shop at (4,5)", "Shop"), ("map:1", "Go to Monster at (5,6)", "Monster"),
                ("map:0", "Go to Monster at (4,7)", "Monster"), ("map:0", "Go to Rest Site at (4,8)", "RestSite"), ("map:0", "Go to Treasure at (4,9)", "Treasure"),
                ("map:0", "Go to Monster at (4,10)", "Monster"),
            ];
            foreach (var (id, label, room) in steps)
            {
                c.Equal(label, Label(run.Snapshot(), id), $"the label of {id}");
                Do(run, id);
                c.Equal("event", run.Snapshot().Screen, "the room is up");
                c.Equal(room, run.Snapshot().State["room"]!.GetValue<string>(), $"{label}: the room the director was handed");
                c.Equal(0, TourOf(run).ScreensWhenEntered, "the director was called with every screen closed");
                Do(run, "proceed");
            }
            c.Equal(0, WorldOf(run).Streams.Counter(RunStream.UnknownMapPoint), "no Unknown point on the way: nothing rolled");

            var snapshot = run.Snapshot();
            c.Equal("""{"act":1,"floor":11,"ascension":0}""", Text(snapshot.State["run"]), "floor 11 on row 10");
            c.Equal("(4,10) monster", snapshot.State["at"]!.GetValue<string>(), "at");
            c.Equal("(3,0) ancient, (3,1) monster, (3,2) monster, (4,3) monster, (5,4) monster, (4,5) shop, (5,6) monster, (4,7) monster, (4,8) rest, (4,9) treasure, (4,10) monster",
                snapshot.State["map"]!["visited"]!.GetValue<string>(), "visited");
            // From (3,11) the ways go on through (2,12) elite or (3,12) unknown to (2,13) unknown, (3,14) monster,
            // (2,15) rest; from (5,11) through (4,12) rest or (5,12) monster to (4,13) unknown, (3,14), (2,15).
            c.Equal("""[{"n":0,"at":"(3,11)","type":"rest","ahead":"monster 1, elite 0-1, unknown 1-2, rest 2"},{"n":1,"at":"(5,11)","type":"elite","ahead":"monster 1-2, elite 1, unknown 1, rest 1-2"}]""",
                Text(snapshot.State["next"]), "the two options and what lies ahead of each");
            c.Equal("""{"10":"@c4:monster^3,5","11":"c3:rest^2,3 c5:elite^4,5","12":"c2:elite^2 c3:unknown^2 c4:rest^4 c5:monster^4","13":"c2:unknown^3 c4:unknown^3","14":"c3:monster^2","15":"c2:rest^3","16":"c3:boss"}""",
                Text(snapshot.State["map"]!["rows"]), "the rows: only what can still be reached");

            Do(run, "map:1", "proceed");
            c.Equal("""[{"n":0,"at":"(4,12)","type":"rest","ahead":"monster 1, unknown 1, rest 2"},{"n":1,"at":"(5,12)","type":"monster","ahead":"monster 2, unknown 1, rest 1"}]""",
                Text(run.Snapshot().State["next"]), "from the elite at (5,11)");
            Do(run, "map:0", "proceed");
            c.Equal("""[{"n":0,"at":"(4,13)","type":"unknown","ahead":"monster 1, unknown 1, rest 1"}]""", Text(run.Snapshot().State["next"]), "from the Rest Site at (4,12)");
            c.Equal("Go to Unknown (?) at (4,13)", Label(run.Snapshot(), "map:0"), "an Unknown point's label");

            // The seed's first Unknown number: an Event from base odds, as in the real run (nothing is barred here either:
            // the room before was a Rest Site, and the point after is a Monster room).
            Do(run, "map:0");
            c.Equal(new MapArrival(new MapCoord(4, 13), MapPointType.Unknown, RoomKind.Event), TourOf(run).Last, "the Unknown room is an Event");
            c.Equal("0.2 -2 0.04 0.06", Odds(WorldOf(run).UnknownOdds), "the odds after it");
            Do(run, "proceed");
            c.Equal("(4,13) unknown", run.Snapshot().State["at"]!.GetValue<string>(), "a visited Unknown point stays unknown");
            c.That(run.Snapshot().State["map"]!["visited"]!.GetValue<string>().EndsWith(", (4,12) rest, (4,13) unknown", StringComparison.Ordinal), "in visited too");

            Do(run, "map:0", "proceed", "map:0", "proceed");
            var last = run.Snapshot();
            c.Equal("(2,15) rest", last.State["at"]!.GetValue<string>(), "the last row");
            c.Equal("""[{"n":0,"at":"(3,16)","type":"boss"}]""", Text(last.State["next"]), "the boss");
            c.Equal("""{"15":"@c2:rest^3","16":"c3:boss"}""", Text(last.State["map"]!["rows"]), "the rows");
            c.Equal("Vantom", last.State["boss"]!.GetValue<string>(), "the boss's name");
            c.Equal("""{"act":1,"floor":16,"ascension":0}""", Text(last.State["run"]), "floor 16");

            Do(run, "map:0");
            c.Equal(new MapArrival(new MapCoord(3, 16), MapPointType.Boss, RoomKind.Boss), TourOf(run).Last, "the boss's room");
            c.Equal((17, 17), (run.State.ActFloor, run.State.TotalFloor), "floor 17");
            c.That(run.AtEnd && run.Result == RunResult.Cleared, "the tour ends there");
            c.Equal(16, TourOf(run).Rooms, "sixteen rooms were opened");

            // The lists' counters, as the game moves them: Neow's room and the Unknown room that was an Event, eight
            // Monster points, one elite, the boss; the shop, the three Rest Sites and the Treasure room move nothing.
            var rooms = WorldOf(run).Rooms[0];
            c.Equal((2, 8, 1, 1), (rooms.EventsVisited, rooms.NormalEncountersVisited, rooms.EliteEncountersVisited, rooms.BossEncountersVisited), "events, hallway fights, elites, boss");
        });
    }

    // ---------- the screen's actions ----------

    private static void ScreenActions(Check c)
    {
        c.Group("map:n enters the point, closes every screen and hands the room to the director", () =>
        {
            var run = new Run(new RunSetup { Seed = PlayedSeed }, new Tour { Drawn = Fork() });
            c.Equal("map:0 map:1", Ids(run.Snapshot()), "two options");
            Do(run, "map:1");
            c.Equal(new MapArrival(new MapCoord(4, 1), MapPointType.Monster, RoomKind.Monster), TourOf(run).Last, "what the director got");
            c.Equal(0, TourOf(run).ScreensWhenEntered, "with every screen closed, the map included");
            c.Equal("event", run.Snapshot().Screen, "the room it put up");
            c.Sequence(["map:1"], run.Actions, "the save");
            Do(run, "proceed");
            c.Equal("map", run.Snapshot().Screen, "the map again");
            c.Equal("(4,1) monster", run.Snapshot().State["at"]!.GetValue<string>(), "one point on");
            c.Equal("""{"act":1,"floor":2,"ascension":0}""", Text(run.Snapshot().State["run"]), "floor 2");
        });

        c.Group("refusals are the mod's and change nothing", () =>
        {
            var run = new Run(new RunSetup { Seed = PlayedSeed, Potions = ["FirePotion"] }, new Tour { Drawn = Fork() });
            var before = run.Dump();
            c.Equal("There is no map option 2.", run.Execute("map:2").Error, "an option that is not there");
            c.Equal("There is no map option -1.", run.Execute("map:-1").Error, "a negative one");
            c.Equal("Unknown map action 'map:x'.", run.Execute("map:x").Error, "not a number");
            c.Equal("Unknown map action 'proceed'.", run.Execute("proceed").Error, "an id of another screen");
            c.Equal("Unknown map action 'map'.", run.Execute("map").Error, "half an id");
            c.Equal("'potion:0' is not available now.", run.Execute("potion:0").Error, "a potion that cannot be used here");
            c.Equal("'discard_potion:1' is not available now.", run.Execute("discard_potion:1").Error, "an empty slot");
            c.Equal(before, run.Dump(), "the run is as it was");
            c.Equal(0, run.Actions.Count, "and nothing is in the save");
            c.Equal("map:0 map:1 discard_potion:0", Ids(run.Snapshot()), "a fight-only potion can only be discarded");
            c.Equal(false, run.Snapshot().State["potions"]![0]!["usable"]!.GetValue<bool>(), "and is not usable");
        });

        c.Group("a potion id on the map leaves the map up", () =>
        {
            var run = new Run(new RunSetup { Seed = PlayedSeed, Potions = ["FirePotion", "BloodPotion"] }, new Tour { Drawn = Fork() });
            Do(run, "discard_potion:0");
            c.Equal("map", run.Snapshot().Screen, "still the map");
            c.Equal(null, run.Potions[0], "the slot is empty");
            c.Equal("map:0 map:1 potion:1 discard_potion:1", Ids(run.Snapshot()), "the other potion's ids are left");
            c.Equal("(3,0) ancient", run.Snapshot().State["at"]!.GetValue<string>(), "nobody moved");
        });

        c.Group("a director that cannot open rooms is told so, and the run is put back", () =>
        {
            var other = new Run(new RunSetup { Seed = PlayedSeed }, new MapButNoRooms());
            var before = other.Dump();
            c.That(Throws<InvalidOperationException>(() => other.Execute("map:0")), "map:0 fails");
            c.Equal(before, other.Dump(), "and changes nothing");
        });

        c.Group("the map of a later act with nothing visited is refused by name", () =>
        {
            var run = Unentered(PlayedSeed, Fork());
            run.Push(new MapScreen());
            c.That(Throws<NotSupportedException>(() => run.Snapshot()), "it would need the Ancient's title");
        });
    }

    private sealed class MapButNoRooms : RunDirector
    {
        public override void Begin(Run run)
        {
            RunWorld.Begin(run.State, acts: OvergrowthRun);
            var map = MapState.BeginAct(run, 0);
            map.Enter(run, map.Map.Start.Coord);
            run.Push(new MapScreen());
        }

        public override void AfterFightWon(Run run, FightOutcome fight) { }
    }

    // ---------- copies and the save ----------

    private static void CopiesAndReplay(Check c)
    {
        // The first option at every map, proceed in every room, until the run is over.
        static void FirstWayUp(Run run)
        {
            for (var step = 0; step < 100 && !run.AtEnd; step++)
                Do(run, run.Snapshot().Screen == "map" ? "map:0" : "proceed");
        }

        c.Group("a run on a real map is its seed and its actions: replayed and copied, in both stream modes", () =>
        {
            foreach (var mode in new[] { StreamMode.Game, StreamMode.Keyed })
            {
                var setup = new RunSetup { Seed = PlayedSeed };
                var run = new Run(setup, new Tour { Mode = mode });
                Do(run, "map:2", "proceed", "map:0", "proceed", "map:0");
                var copy = run.Clone();
                c.Equal(run.Dump(), copy.Dump(), $"{mode}: a copy is in the same state");
                var midway = run.Dump();
                FirstWayUp(copy);
                c.Equal(midway, run.Dump(), $"{mode}: playing the copy to the end leaves the run alone");
                c.That(copy.AtEnd && copy.Result == RunResult.Cleared, $"{mode}: the copy reached the boss");
                c.Equal(17, copy.State.TotalFloor, $"{mode}: seventeen floors");

                FirstWayUp(run);
                c.Equal(copy.Dump(), run.Dump(), $"{mode}: the run and its copy end the same");
                var again = Arena.Run.Replay(setup, new Tour { Mode = mode }, run.Actions);
                c.Equal(run.Dump(), again.Dump(), $"{mode}: the save rebuilds the run");
                c.Equal(31, run.Actions.Count, $"{mode}: sixteen points entered and fifteen rooms left; the boss's room ends the run by itself");
            }
        });

        c.Group("the run's dump holds the map and the path", () =>
        {
            var run = new Run(new RunSetup { Seed = PlayedSeed }, new Tour { Drawn = Fork() });
            Do(run, "map:0", "proceed", "map:0");
            var part = JsonNode.Parse(run.Dump())!["parts"]!["MapState"]!;
            c.Equal("(3,0) (2,1) (1,2)", part["visited"]!.GetValue<string>(), "the path");
            c.Equal("Ancient:Event Monster:Monster", string.Join(" ", part["rooms"]!.GetValue<string>().Split(' ').Take(2)), "the rooms");
            c.That(part["rooms"]!.GetValue<string>().StartsWith("Ancient:Event Monster:Monster Unknown:", StringComparison.Ordinal), "the Unknown point with the room it turned out to be");
            c.Equal("1,2:Unknown>2 2,1:Monster>1,2 2,2:Shop>2 2,3:RestSite>3 3,3:RestSite>3 4,1:Monster>4 4,2:Elite>3 | start 3,0:Ancient>2,4 | boss 3,4:Boss | paths from (2,1) (4,1)",
                part["map"]!.GetValue<string>(), "the map: every point column by column with its kind and its children in listing order");

            var two = Fork();
            var copy = two.Clone();
            c.Equal(two.Describe(), copy.Describe(), "a copy of a map is the same map");
            c.That(!ReferenceEquals(two.Start, copy.Start) && copy.Points.All(p => !two.Points.Contains(p)), "of its own points");
            c.That(copy.Start.Children.All(child => child.Parents.Contains(copy.Start)), "linked to each other");
        });
    }

    // ---------- the boss key and the lists' counters ----------

    private static void BossAndCounters(Check c)
    {
        c.Group("the boss key: the act's boss by its title, until the player stands on it", () =>
        {
            // The titles are the game's (the encounter notes' title of each boss encounter).
            (string Encounter, string Title)[] titles =
            [
                ("VantomBoss", "Vantom"), ("CeremonialBeastBoss", "Ceremonial Beast"), ("TheKinBoss", "The Kin"),
                ("WaterfallGiantBoss", "Waterfall Giant"), ("SoulFyshBoss", "Soul Fysh"), ("LagavulinMatriarchBoss", "Lagavulin Matriarch"),
            ];
            c.Sequence(titles.Select(t => t.Encounter).OrderBy(n => n, StringComparer.Ordinal),
                GameLists.ActsByIndex[0].SelectMany(a => a.Bosses).Select(b => b.ClassName).OrderBy(n => n, StringComparer.Ordinal), "the six bosses of the two first acts");
            foreach (var (encounter, title) in titles)
            {
                var run = Unentered(PlayedSeed, Fork());
                WorldOf(run).Rooms[0].Boss = encounter;
                c.Equal(title, MapState.BossTitle(run), encounter);
                var state = new JsonObject();
                MapState.AddBoss(state, run);
                c.Equal(title, state["boss"]?.GetValue<string>(), $"{encounter}, written into a state");
            }

            var onBoss = Unentered(PlayedSeed, Fork());
            foreach (var (col, row) in new[] { (3, 0), (4, 1), (4, 2), (3, 3) }) Enter(onBoss, col, row);
            c.Equal("Vantom", MapState.BossTitle(onBoss), "on the last row the boss is still named");
            Enter(onBoss, 3, 4);
            c.Equal(null, MapState.BossTitle(onBoss), "on the boss's point it is not named again");
            var none = new JsonObject();
            MapState.AddBoss(none, onBoss);
            c.Equal("", Keys(none), "and the key is left out");

            c.Equal(null, MapState.BossTitle(new Run(new RunSetup(), new FixedFights())), "a run without a world has no boss to name");
            var later = Unentered(PlayedSeed, Fork());
            WorldOf(later).Rooms[0].Boss = "KnowledgeDemonBoss";
            c.That(Throws<NotSupportedException>(() => MapState.BossTitle(later)), "a boss of a later act is refused by name, not guessed");
        });

        c.Group("the lists' counters move by the kind of room", () =>
        {
            var run = Unentered(PlayedSeed, Fork());
            var rooms = WorldOf(run).Rooms[0];
            (int, int, int, int) Counters() => (rooms.EventsVisited, rooms.NormalEncountersVisited, rooms.EliteEncountersVisited, rooms.BossEncountersVisited);
            c.Equal((0, 0, 0, 0), Counters(), "a new run");
            MapState.MarkRoomVisited(run, RoomKind.Event);
            c.Equal((1, 0, 0, 0), Counters(), "an Event room (Neow's is one)");
            MapState.MarkRoomVisited(run, RoomKind.Monster);
            MapState.MarkRoomVisited(run, RoomKind.Monster);
            c.Equal((1, 2, 0, 0), Counters(), "two Monster rooms");
            MapState.MarkRoomVisited(run, RoomKind.Elite);
            MapState.MarkRoomVisited(run, RoomKind.Boss);
            c.Equal((1, 2, 1, 1), Counters(), "an elite and the boss");
            MapState.MarkRoomVisited(run, RoomKind.Shop);
            MapState.MarkRoomVisited(run, RoomKind.Treasure);
            MapState.MarkRoomVisited(run, RoomKind.RestSite);
            c.Equal((1, 2, 1, 1), Counters(), "a shop, a Treasure room and a Rest Site move nothing");
            MapState.MarkRoomVisited(run, RoomKind.Unassigned);
            MapState.MarkRoomVisited(run, RoomKind.Map);
            c.Equal((1, 2, 1, 1), Counters(), "and so do the two kinds that are no room of a map point");
            // The game's RoomType, name by name: Unassigned, Monster, Elite, Boss, Treasure, Shop, Event, RestSite, Map.
            c.Sequence(["Unassigned=0", "Monster=1", "Elite=2", "Boss=3", "Treasure=4", "Shop=5", "Event=6", "RestSite=7", "Map=8"],
                Enum.GetValues<RoomKind>().Select(r => $"{r}={(int)r}"), "the room kinds carry the game's names and numbers");
            c.That(Enum.GetValues<RolledRoom>().All(r => Enum.IsDefined((RoomKind)(int)r) && ((RoomKind)(int)r).ToString() == r.ToString()), "and a rolled room is the room of the same name");
        });
    }
}
