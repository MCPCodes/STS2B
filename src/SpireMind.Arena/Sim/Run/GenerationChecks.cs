using System.Globalization;
using System.Text.RegularExpressions;

namespace SpireMind.Arena;

/// <summary>
/// A second reader's checks of what a run is made of before its first room and of the map:
/// the game's code was read once more, by someone who had not written the port, and what that
/// reading gives is held against the port here.
/// <para>
/// The map: <see cref="MapSecondReading"/> is a second port of the generator, written from the
/// game's code alone and kept on the runtime's own hash sets; the two must make the same map,
/// point for point and number for number, on a thousand seeds and under the rules of every
/// act. The same thousand maps are held against the placement rules. Six tests of the pruning
/// pass are shown never to decide. What the real runs' walked paths can and cannot tell apart
/// is measured.
/// </para>
/// <para>
/// The lists: a whole real run of three acts (seed 75F277YN2G, the game's own run record) met
/// the lists' entries in the generated order, act for act; eleven real runs rolled the first
/// act the port rolls. The Unknown room's roll is written out a second time. The names that
/// are hashed into stream seeds are held against the game's own pattern.
/// </para>
/// <para>
/// Expected values are the real game's records, or come from the second reading, never from
/// the code under check. Nothing here needs a folder: what is used of the real files is typed in.
/// </para>
/// </summary>
public sealed class GenerationChecks : IArenaChecks
{
    public void Run(Check c)
    {
        Maps(c);
        RealPaths(c);
        UnknownRoomRoll(c);
        Names(c);
        RealRunLists(c);
        Bags(c);
    }

    // ---------- helpers ----------

    /// <summary>At most four workers: the machine is shared.</summary>
    private static T[] InParallel<T>(int count, Func<int, T> make)
    {
        var results = new T[count];
        Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i => results[i] = make(i));
        return results;
    }

    /// <summary>Seeds as the game makes them, the same ones on every run of the checks.</summary>
    private static List<string> Seeds(ulong from, int count)
    {
        var rng = new GameRng(from);
        var seeds = new List<string>(count);
        for (var i = 0; i < count; i++) seeds.Add(RunSeed.Random(rng).Text);
        return seeds;
    }

    // ---------- the map: two readings, the rules, the six tests ----------

    /// <summary>One set of maps to make with both readings: an act's rules, perhaps changed as a run of two players or of an ascension level changes them.</summary>
    private sealed record Batch(string What, string Act, int ActNumber, int Maps, int WithoutSix, ulong SeedsFrom,
        Func<ActMapRules, ActMapRules>? Change = null, Func<MapSecondReading.Act, MapSecondReading.Act>? ChangeSecond = null, bool Prune = true, int Elites = 5);

    private sealed record Made(string Seed, ActMap Map, MapSecondReading.Reading Second, MapSecondReading.Reading? WithoutSix);

    private static readonly Batch[] Batches =
    [
        new("Overgrowth", GameLists.Overgrowth, 1, 1000, 100, 71),
        new("Underdocks", GameLists.Underdocks, 1, 30, 0, 72),
        new("Hive", GameLists.Hive, 2, 120, 40, 73),
        new("Glory", GameLists.Glory, 3, 120, 40, 74),
        // More than one player: a row less. An ascension level: eight elites. The pruning pass left out, as the game's own tests can.
        new("Overgrowth for two players", GameLists.Overgrowth, 1, 30, 0, 75, r => r with { Rooms = r.Rooms - 1 }, a => a with { Rooms = a.Rooms - 1 }),
        new("Hive for two players", GameLists.Hive, 2, 30, 0, 76, r => r with { Rooms = r.Rooms - 1 }, a => a with { Rooms = a.Rooms - 1 }),
        new("Glory for two players", GameLists.Glory, 3, 30, 0, 77, r => r with { Rooms = r.Rooms - 1 }, a => a with { Rooms = a.Rooms - 1 }),
        new("Overgrowth with eight elites", GameLists.Overgrowth, 1, 30, 20, 78,
            r => { var own = r.DrawCounts; return r with { DrawCounts = rng => own(rng) with { Elites = 8 } }; },
            a => { var own = a.Counts; return a with { Counts = rng => own(rng) with { Elites = 8 } }; }, Elites: 8),
        new("Overgrowth without the pruning pass", GameLists.Overgrowth, 1, 40, 0, 79, Prune: false),
    ];

    private static ActMapRules RulesOf(Batch batch)
    {
        var rules = ActMapRules.For(batch.Act);
        return batch.Change is null ? rules : batch.Change(rules);
    }

    private static Made[] Make(Batch batch)
    {
        var seeds = Seeds(batch.SeedsFrom, batch.Maps);
        return InParallel(batch.Maps, i =>
        {
            var second = MapSecondReading.ActOf(batch.Act);
            if (batch.ChangeSecond is not null) second = batch.ChangeSecond(second);

            var map = ActMapGenerator.Generate(new RunStreams(seeds[i]).ActMap(batch.ActNumber), RulesOf(batch), batch.Prune);
            var read = MapSecondReading.Read(new RunStreams(seeds[i]).ActMap(batch.ActNumber), second, prune: batch.Prune);
            var without = i < batch.WithoutSix
                ? MapSecondReading.Read(new RunStreams(seeds[i]).ActMap(batch.ActNumber), second, MapSecondReading.Without.AllSix, batch.Prune)
                : null;
            return new Made(seeds[i], map, read, without);
        });
    }

    private static void Maps(Check c)
    {
        var all = new MapSecondReading.Tally();
        var maps = 0;
        var comparedWithoutSix = 0;
        var monsterSiblings = 0;
        var firstAct = 0;

        foreach (var batch in Batches)
        {
            Made[] made = [];
            c.Group($"two readings of the generator make the same map: {batch.What}, {batch.Maps} seeds", () =>
            {
                made = Make(batch);
                var different = made.Where(m => m.Map.Describe() != m.Second.Text).Select(m => m.Seed).ToList();
                c.That(different.Count == 0, $"{different.Count} maps differ in a point, a kind, a link or the order of a point's children; the first seeds: {string.Join(", ", different.Take(4))}");
                var draws = made.Where(m => m.Map.Draws != m.Second.Draws).Select(m => m.Seed).ToList();
                c.That(draws.Count == 0, $"{draws.Count} maps took another number of numbers from the stream; the first seeds: {string.Join(", ", draws.Take(4))}");
                c.That(made.All(m => m.Map.Counts == m.Second.Counts), "the drawn numbers of Rest Sites and Unknown rooms");
                c.Equal(batch.Maps, made.Select(m => m.Second.Text).Distinct().Count(), "and every seed has a map of its own");
            });

            c.Group($"the placement rules, as the game's code gives them, on those {batch.Maps} maps: {batch.What}", () =>
            {
                var rooms = RulesOf(batch).Rooms;
                var problems = new List<string>();
                foreach (var m in made)
                {
                    foreach (var problem in RuleBreaks(m.Map, rooms, batch.Act, batch.Elites, batch.Prune))
                        if (problems.Count < 6) problems.Add($"{m.Seed}: {problem}");
                    monsterSiblings += MonsterSiblings(m.Map);
                }
                c.That(problems.Count == 0, string.Join("; ", problems));
                c.Equal(batch.Maps, made.Length, "maps looked at");
            });

            foreach (var m in made)
            {
                all.Add(m.Second.Tally);
                if (m.WithoutSix is null) continue;
                comparedWithoutSix++;
                if (m.WithoutSix.Text != m.Second.Text || m.WithoutSix.Draws != m.Second.Draws)
                    c.That(false, $"{batch.What}, seed {m.Seed}: the map is another one without the six tests");
            }
            maps += made.Length;
            if (batch.What == "Overgrowth") firstAct = made.Length;
        }

        c.Group("1,000 maps of the first act went through both", () =>
        {
            c.Equal(1000, firstAct, "Overgrowth maps");
            c.That(maps >= 1400, $"and {maps} maps in all");
        });

        c.Group("six tests of the pruning pass never decide (MAP.md has the reasons): not once on all those maps", () =>
        {
            // Each of these is a line of the game's pruning pass that the port keeps as written. They cannot
            // come true (or cannot matter): the stretches of one group share only their two ends, a group is
            // always looked at on the map it was found on, and the pass stops before a group's last stretch.
            c.Equal(0, all.Gone, "a point of a stretch that was already off the map");
            c.Equal(0, all.EmptyCellExcused, "a parent with one child whose own cell was empty (the start point)");
            c.Equal(0, all.OneParentEnds, "a stretch given up because its end had one parent left");
            c.Equal(0, all.RowZeroStartsInAGroup, "a stretch from a start point with one child in a group of two or more");
            c.Equal(0, all.OrphansSpared, "a point kept for the sake of a child outside the stretch");
            // The sixth (a stretch must end at a point with two parents) cannot be counted; it is in the comparison below.
            c.That(comparedWithoutSix >= 200, $"and with all six left out the second reading made the same map from the same numbers, {comparedWithoutSix} times of {comparedWithoutSix}");
            // The pass itself was busy all the while: the tests were not idle for want of work.
            c.That(all.PointsPruned > 5000 && all.LinksCut > 500, $"while it pruned {all.PointsPruned} points and cut {all.LinksCut} links");
            c.That(all.RoundsThatRepaired > 150 && all.PointsRepaired >= all.RoundsThatRepaired, $"and put {all.PointsRepaired} kinds back in {all.RoundsThatRepaired} rounds");
        });

        c.Group("two children of one parent can both be Monster rooms: that part of the game's sibling rule is never asked", () =>
        {
            // The game's list of kinds that may not be siblings names the Monster room, but nothing ever asks
            // about it: the queue of kinds to place holds no Monster room, the repair puts none back, and what
            // is left unplaced becomes a Monster room without a question. A generator that kept Monster
            // rooms apart would make other maps than the game.
            c.That(monsterSiblings > maps, $"pairs of Monster rooms under one parent on {maps} maps: {monsterSiblings}");
        });
    }

    private static int MonsterSiblings(ActMap map)
    {
        var pairs = 0;
        foreach (var parent in map.Points.Append(map.Start))
        {
            var monsters = parent.Children.Count(child => child.Type == MapPointType.Monster && child.CanBeModified);
            pairs += monsters * (monsters - 1) / 2;
        }
        return pairs;
    }

    /// <summary>
    /// What a finished map must look like, read from the game's generator by the second reader:
    /// the grid, the three fixed rows, where each counted kind may stand, what it may stand
    /// next to, and that the pruning pass leaves every point on a way from the start to the boss.
    /// </summary>
    private static List<string> RuleBreaks(ActMap map, int rooms, string act, int elites, bool pruned)
    {
        var broken = new List<string>();
        var rows = rooms + 1;
        var last = rows - 1;
        var treasureRow = rows - 7;

        if (map.Columns != 7 || map.Rows != rows) broken.Add($"a grid of {map.Columns} by {map.Rows}");
        if (map.Start.Coord != new MapCoord(3, 0) || map.Start.Type != MapPointType.Ancient || map.Start.Parents.Count != 0) broken.Add("the start point");
        if (map.Boss.Coord != new MapCoord(3, rows) || map.Boss.Type != MapPointType.Boss || map.Boss.Children.Count != 0) broken.Add("the boss");

        // The numbers the act draws: Rest Sites 6 or 7 (5 or 6 in the third act), Unknown rooms 10 to 14 (one less after the first act).
        var counts = map.Counts;
        var (restsFrom, restsTo) = act == GameLists.Glory ? (5, 6) : (6, 7);
        var (unknownsFrom, unknownsTo) = act is GameLists.Hive or GameLists.Glory ? (9, 13) : (10, 14);
        if (counts.Rests < restsFrom || counts.Rests > restsTo || counts.Unknowns < unknownsFrom || counts.Unknowns > unknownsTo
            || counts.Shops != 3 || counts.Elites != elites) broken.Add($"the drawn numbers {counts}");

        var previous = new MapCoord(-1, -1);
        foreach (var point in map.Points)
        {
            if (point.Coord.CompareTo(previous) <= 0) broken.Add($"{point} is not listed column by column, each column from the bottom");
            previous = point.Coord;
            if (!ReferenceEquals(map.Point(point.Coord), point)) broken.Add($"{point} is not in its own cell");
            if (point.Row < 1 || point.Row > last || point.Col < 0 || point.Col > 6) broken.Add($"{point} is outside the grid's rows 1 to {last}");

            // The three fixed rows, and nothing else fixed.
            var fixedKind = point.Row == 1 ? MapPointType.Monster : point.Row == treasureRow ? MapPointType.Treasure : point.Row == last ? MapPointType.RestSite : MapPointType.Unassigned;
            if (fixedKind != MapPointType.Unassigned && (point.Type != fixedKind || point.CanBeModified)) broken.Add($"{point} on a fixed row");
            if (fixedKind == MapPointType.Unassigned && !point.CanBeModified) broken.Add($"{point} is fixed off the fixed rows");
            if (point.Type is not (MapPointType.Monster or MapPointType.Elite or MapPointType.RestSite or MapPointType.Shop or MapPointType.Treasure or MapPointType.Unknown))
                broken.Add($"{point} has no kind of room");
            if (point.Type == MapPointType.Treasure && point.Row != treasureRow) broken.Add($"{point}: Treasure off row {treasureRow}");

            // Where the counted kinds may stand.
            if (point.Row < 6 && (point.Type == MapPointType.RestSite || point.Type == MapPointType.Elite)) broken.Add($"{point} below row 6");
            if (point.Row >= rows - 3 && point.Row != last && point.Type == MapPointType.RestSite) broken.Add($"{point} in the top rows");

            // Never directly before or after its own kind; never two of one parent. The fixed last row is all
            // Rest Sites whatever its parents hold, so the rule is the placed points'.
            if (point.CanBeModified && (point.Type == MapPointType.Elite || point.Type == MapPointType.RestSite || point.Type == MapPointType.Shop))
            {
                if (point.Parents.Any(p => p.Type == point.Type) || point.Children.Any(k => k.Type == point.Type)) broken.Add($"{point} stands next to its own kind");
            }
            if (point.CanBeModified && (point.Type == MapPointType.Elite || point.Type == MapPointType.RestSite || point.Type == MapPointType.Shop || point.Type == MapPointType.Unknown))
            {
                foreach (var parent in point.Parents)
                    if (parent.Children.Any(sibling => !ReferenceEquals(sibling, point) && sibling.Type == point.Type)) broken.Add($"{point} has a sibling of its own kind under {parent.Coord}");
            }

            // Links: one row up, to the same or a neighbouring column, known from both ends.
            if (point.Parents.Count == 0) broken.Add($"{point} has no parent");
            if (point.Children.Count == 0) broken.Add($"{point} has no child");
            foreach (var child in point.Children)
            {
                if (child.Row != point.Row + 1) broken.Add($"{point} leads to {child.Coord}, which is not on the next row");
                if (!child.Parents.Contains(point)) broken.Add($"{child} does not know its parent {point.Coord}");
                if (point.Row < last && Math.Abs(child.Col - point.Col) > 1) broken.Add($"{point} leads to {child.Coord}, more than a column away");
                if (point.Row < last && !ReferenceEquals(map.Point(child.Coord), child)) broken.Add($"{point} leads to a point that is not on the map");
                if (point.Row == last && !ReferenceEquals(child, map.Boss)) broken.Add($"{point} on the last row does not lead to the boss");
            }
            if (point.Row == 1 && !(point.Parents.Count == 1 && ReferenceEquals(point.Parents.First(), map.Start))) broken.Add($"{point} on the first row does not come from the start point alone");
            if (point.Row == last && point.Children.Count != 1) broken.Add($"{point} on the last row has more than the boss as its child");
        }

        if (!map.Start.Children.OrderBy(p => p.Col).SequenceEqual(map.PointsInRow(1))) broken.Add("the start point's children are not the first row");
        if (!map.Boss.Parents.OrderBy(p => p.Col).SequenceEqual(map.PointsInRow(last))) broken.Add("the boss's parents are not the last row");
        if (!map.PointsInRow(1).Any() || !map.PointsInRow(last).Any()) broken.Add("an empty first or last row");
        if (map.StartPoints.Any(p => p.Row != 1 || !ReferenceEquals(map.Point(p.Coord), p))) broken.Add("a path started from a point that is not on the first row of the map");
        if (!pruned && map.StartPoints.Count != map.Start.Children.Count) broken.Add("before pruning every first-row point is one a path started from");

        // Never more than the numbers: the queue holds no more, and the repair stops at the number.
        if (map.Count(MapPointType.Shop) > counts.Shops) broken.Add($"{map.Count(MapPointType.Shop)} shops");
        if (map.Count(MapPointType.Elite) > counts.Elites) broken.Add($"{map.Count(MapPointType.Elite)} elites for {counts.Elites}");
        if (map.Count(MapPointType.Unknown) > counts.Unknowns) broken.Add($"{map.Count(MapPointType.Unknown)} Unknown rooms for {counts.Unknowns}");
        if (map.Points.Count(p => p.Type == MapPointType.RestSite && p.CanBeModified) > counts.Rests) broken.Add("more placed Rest Sites than the drawn number");
        return broken;
    }

    // ---------- what the real runs' paths tell apart ----------

    private static MapPointType KindOf(string word) => word switch
    {
        "monster" => MapPointType.Monster,
        "elite" => MapPointType.Elite,
        "rest_site" => MapPointType.RestSite,
        "shop" => MapPointType.Shop,
        "treasure" => MapPointType.Treasure,
        "unknown" => MapPointType.Unknown,
        "boss" => MapPointType.Boss,
        "ancient" => MapPointType.Ancient,
        _ => throw new ArgumentException($"'{word}' is no kind of map point."),
    };

    /// <summary>Every way from the start point whose points have these kinds, in order.</summary>
    private static List<List<MapPoint>> Ways(ActMap map, IReadOnlyList<MapPointType> kinds)
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

    private static string ActName(string id) => GameNames.ClassNameOf(id, GameLists.Acts.Select(a => a.ClassName))
        ?? throw new KeyNotFoundException($"'{id}' is not an act of the arena's lists.");

    /// <summary>The rules a real run's maps were made by: a row less for more than one player, eight elites from the first ascension level on.</summary>
    private static ActMapRules RulesOf(MapGoldenData.RunPath real, string act)
    {
        var rules = ActMapRules.For(act);
        var own = rules.DrawCounts;
        if (real.Players > 1) rules = rules with { Rooms = rules.Rooms - 1 };
        if (real.Ascension >= 1) rules = rules with { DrawCounts = rng => own(rng) with { Elites = 8 } };
        return rules;
    }

    private static void RealPaths(Check c)
    {
        c.Group("how much a walked path proves: other seeds' maps do not hold a way with the kinds a real run met", () =>
        {
            // MapChecks finds exactly one such way on the map of the run's own seed. That is evidence for the
            // whole generator only if a way of those kinds is rare: here it is looked for on the maps of other
            // seeds, made by the same rules. (Measured once on 400 other seeds an act: 1 map of 2,400 held one.)
            const int others = 40;
            var seeds = Seeds(1783968626, others);
            var acts = 0;
            var held = 0;
            foreach (var real in MapGoldenData.Runs)
            {
                for (var act = 0; act < real.Acts.Length; act++)
                {
                    var path = real.Acts[act];
                    if (path.OtherMap is not null) continue;
                    var name = ActName(path.Act);
                    var kinds = path.Points.Select(p => KindOf(p.PointType)).ToList();
                    var number = act + 1;
                    var own = Ways(ActMapGenerator.Generate(new RunStreams(real.Seed).ActMap(number), RulesOf(real, name)), kinds).Count;
                    c.Equal(1, own, $"seed {real.Seed}, act {number} ({name}): ways of the run's kinds on its own map");
                    var holding = InParallel(others, i => Ways(ActMapGenerator.Generate(new RunStreams(seeds[i]).ActMap(number), RulesOf(real, name)), kinds).Count > 0 ? 1 : 0).Sum();
                    c.That(holding <= 2, $"seed {real.Seed}, act {number} ({name}): of {others} other seeds' maps, {holding} hold such a way");
                    held += holding;
                    acts++;
                }
            }
            c.Equal(6, acts, "acts of real runs with a standard map");
            c.That(held <= 3, $"over all six acts, {held} of {acts * others} other maps hold a real run's way");
        });

        c.Group("what a walked path cannot see: Hive's draw of Rest Sites changes one point off the path", () =>
        {
            // The game draws Hive's Rest Sites around 6 (kept to 6 or 7) where a first act draws around 7. The
            // one real run through a standard Hive map (seed BQ4CE2FRRJ, two players, eight elites) would fit a
            // draw around 7 as well: the same numbers are used, the count is 7 instead of 6, and the seventh
            // Rest Site lands on one point the run did not pass. So that draw rests on the reading of the
            // game's code (the act's own class), which both readings here follow: around 6.
            var real = MapGoldenData.Runs.Single(r => r.Seed == "BQ4CE2FRRJ");
            var path = real.Acts[1];
            c.Equal("Hive", ActName(path.Act), "the run's second act");
            var rules = RulesOf(real, GameLists.Hive);
            var around7 = rules with { DrawCounts = rng => { var rests = rng.NextGaussianInt(7, 1, 6, 7); return new MapCounts(rests, 3, 8, rng.NextGaussianInt(12, 1, 10, 14) - 1); } };
            var game = ActMapGenerator.Generate(new RunStreams(real.Seed).ActMap(2), rules);
            var other = ActMapGenerator.Generate(new RunStreams(real.Seed).ActMap(2), around7);
            c.Equal((6, 7), (game.Counts.Rests, other.Counts.Rests), "Rest Sites drawn around 6, and around 7 from the same numbers");
            c.Equal(game.Draws, other.Draws, "the same number of numbers");
            c.Sequence(game.Points.Select(p => p.Coord), other.Points.Select(p => p.Coord), "the same points");

            var differing = game.Points.Where(p => other.Point(p.Coord)!.Type != p.Type).ToList();
            c.Equal(1, differing.Count, "points of another kind");
            if (differing.Count != 1) return;
            c.Equal((MapPointType.Monster, MapPointType.RestSite), (differing[0].Type, other.Point(differing[0].Coord)!.Type), $"{differing[0].Coord}: a Monster room, a Rest Site under the other draw");

            var kinds = path.Points.Select(p => KindOf(p.PointType)).ToList();
            var way = Ways(game, kinds).Single();
            c.That(way.All(p => p.Coord != differing[0].Coord), "the run's way does not pass it");
            c.Equal(1, Ways(other, kinds).Count, "so the run fits the other map too");
        });
    }

    // ---------- the Unknown room's roll, written out a second time ----------

    private static void UnknownRoomRoll(Check c)
    {
        c.Group("an Unknown room's roll, read a second time: the same room and the same odds after 30,000 rolls of every kind of bar", () =>
        {
            // The game's roll, as the second reader has it: what may come out is every kind without the
            // barred ones. The answer starts as an Event if that is allowed, else the lowest kind allowed. One
            // number; the kinds are walked Monster, Elite, Treasure, Shop, each allowed one whose odds are
            // not below 0 adds its odds, and the first whose sum reaches the number is the answer. Then the
            // kind that came out goes back to its start value and every other ALLOWED kind grows by its
            // start value (the Elite's is -1: it sinks). An empty set of kinds is an error.
            RolledRoom[] order = [RolledRoom.Monster, RolledRoom.Elite, RolledRoom.Treasure, RolledRoom.Shop];
            var start = new Dictionary<RolledRoom, float> { [RolledRoom.Monster] = 0.1f, [RolledRoom.Elite] = -1f, [RolledRoom.Treasure] = 0.02f, [RolledRoom.Shop] = 0.03f };

            var choose = new GameRng(1011);
            var rolls = 0;
            var wrong = new List<string>();
            var seen = new HashSet<RolledRoom>();
            var bothFailed = 0;
            for (var run = 0; run < 1000; run++)
            {
                var odds = new UnknownRoomOdds();
                var second = new Dictionary<RolledRoom, float>(start);
                var stream = new GameRng((ulong)(5000 + run));
                for (var step = 0; step < 30; step++)
                {
                    // Most rolls with nothing or only the Shop barred, as in a run; the rest with any set of kinds.
                    var allowed = new HashSet<RolledRoom>(UnknownRoomOdds.AllRooms);
                    var how = choose.NextInt(10);
                    if (how is 4 or 5) allowed.Remove(RolledRoom.Shop);
                    else if (how >= 6)
                        foreach (var kind in UnknownRoomOdds.AllRooms)
                            if (choose.NextInt(3) == 0) allowed.Remove(kind);

                    var number = stream.Clone().NextFloat();
                    RolledRoom? expected = null;
                    if (allowed.Count > 0)
                    {
                        var answer = allowed.Contains(RolledRoom.Event) ? RolledRoom.Event : allowed.Order().First();
                        var sum = 0f;
                        foreach (var kind in order)
                        {
                            if (!allowed.Contains(kind) || second[kind] < 0f) continue;
                            sum += second[kind];
                            if (number <= sum)
                            {
                                answer = kind;
                                break;
                            }
                        }
                        foreach (var kind in order)
                        {
                            if (kind == answer) second[kind] = start[kind];
                            else if (allowed.Contains(kind)) second[kind] += start[kind];
                        }
                        expected = answer;
                    }

                    RolledRoom? got = null;
                    try
                    {
                        got = odds.Roll(stream, allowed);
                    }
                    catch (InvalidOperationException)
                    {
                        // Nothing may come out: the game's roll fails too, before it draws.
                    }
                    rolls++;
                    if (expected is null && got is null)
                    {
                        bothFailed++;
                        continue;
                    }
                    if (got is { } room) seen.Add(room);
                    var same = expected == got && odds.Monster.Equals(second[RolledRoom.Monster]) && odds.Elite.Equals(second[RolledRoom.Elite])
                        && odds.Treasure.Equals(second[RolledRoom.Treasure]) && odds.Shop.Equals(second[RolledRoom.Shop]);
                    if (!same && wrong.Count < 4)
                        wrong.Add(string.Create(CultureInfo.InvariantCulture,
                            $"run {run} step {step}: number {number:R}, allowed [{string.Join(",", allowed.Order())}]: expected {expected} and then odds {second[RolledRoom.Monster]:R} {second[RolledRoom.Elite]:R} {second[RolledRoom.Treasure]:R} {second[RolledRoom.Shop]:R}, got {got} and {odds.Monster:R} {odds.Elite:R} {odds.Treasure:R} {odds.Shop:R}"));
                    if (!same) break;
                }
            }
            c.That(wrong.Count == 0, string.Join("; ", wrong));
            c.Equal(30000, rolls, "rolls");
            c.Equal(5, seen.Count, "every kind came out, the Elite too (the lowest kind left when Event and Monster are barred)");
            c.That(bothFailed > 20, $"and a roll with nothing allowed failed in both, {bothFailed} times");
        });
    }

    // ---------- names that are hashed ----------

    private static void Names(Check c)
    {
        c.Group("stream names and id entries: the game's own pattern gives the same text for every name the lists hold", () =>
        {
            // The game puts an underscore into a class name with this pattern (replace by "$1_$2"), then lower
            // case for a stream name; for an id entry upper case, runs of white space to one underscore, and
            // every character dropped that is not a capital, a digit or an underscore. The arena does the same
            // by hand; here the runtime's own pattern matcher is asked.
            var camel = new Regex(@"([A-Za-z0-9]|\G(?!^))([A-Z])", RegexOptions.CultureInvariant);
            var space = new Regex(@"\s+", RegexOptions.CultureInvariant);
            var special = new Regex("[^A-Z0-9_]", RegexOptions.CultureInvariant);
            string Snake(string name) => camel.Replace(name.Trim(), "$1_$2").ToLowerInvariant();
            string Slug(string name) => special.Replace(space.Replace(camel.Replace(name.Trim(), "$1_$2").ToUpperInvariant(), "_"), "");

            var names = new List<string>();
            names.AddRange(Enum.GetNames<RunStream>());
            names.AddRange(GameLists.SharedRelics.Select(r => r.ClassName));
            names.AddRange(GameLists.CharacterRelics.Values.SelectMany(pool => pool).Select(r => r.ClassName));
            names.AddRange(GameLists.SharedEvents);
            names.AddRange(GameLists.SharedAncients.Select(a => a.ClassName));
            foreach (var act in GameLists.Acts)
            {
                names.Add(act.ClassName);
                names.AddRange(act.Events);
                names.AddRange(act.Encounters.Select(e => e.ClassName));
                names.AddRange(act.Ancients.Select(a => a.ClassName));
            }
            names.AddRange(CardPools.All.SelectMany(pool => pool.All).Select(card => card.ClassName));
            // And names no list holds, for the corners of the pattern: runs of capitals, digits, a capital at the
            // very start, white space, characters that are dropped.
            names.AddRange(["A", "AB", "ABC", "aB", "aBC", "XMLHttp", "X1Y", "Act1Map", "already_snake", "Mixed_CaseName", "  Padded  ",
                "Two Words", "Tab\tAnd  Spaces", "Odd-Chars!", "Lee'sWaffle", "ÉcoleA", "ñB", "", "9Lives", "_Leading", "Trailing_", "a", "aA", "AAa"]);

            var differing = names.Distinct().Where(n => GameNames.SnakeCase(n) != Snake(n) || GameNames.Slug(n) != Slug(n)).ToList();
            c.That(differing.Count == 0, $"names the arena writes differently from the game's pattern: {string.Join(", ", differing.Take(8).Select(n => $"'{n}' ({GameNames.Slug(n)} / {Slug(n)})"))}");
            c.That(names.Distinct().Count() > 500, $"names compared: {names.Distinct().Count()}");
            c.Equal("the_kin_boss / THE_KIN_BOSS", $"{Snake("TheKinBoss")} / {Slug("TheKinBoss")}", "the pattern itself, on one name spelled out");
            c.Equal("a_b_c / X1_Y", $"{Snake("ABC")} / {Slug("X1Y")}", "and on a run of capitals and a digit");
        });
    }

    // ---------- the lists in use: a whole real run ----------

    /// <summary>One act of the real run on seed 75F277YN2G, from the game's own record of it (history/1791592972.run):
    /// the entries of the ids of what the run met, in the order it met them.</summary>
    private sealed record RealAct(string Act, string Ancient, string[] HallwayFights, string[] Elites, string Boss);

    private static readonly RealAct[] ThreeActs =
    [
        new("UNDERDOCKS", "NEOW",
            ["SEAPUNK_WEAK", "CORPSE_SLUGS_WEAK", "TOADPOLES_WEAK", "HAUNTED_SHIP_NORMAL", "GREMLIN_MERC_NORMAL", "TWO_TAILED_RATS_NORMAL", "FOSSIL_STALKER_NORMAL"],
            ["SKULKING_COLONY_ELITE", "TERROR_EEL_ELITE"], "LAGAVULIN_MATRIARCH_BOSS"),
        new("HIVE", "OROBAS",
            ["BOWLBUGS_WEAK", "TUNNELER_WEAK", "MYTES_NORMAL", "LOUSE_PROGENITOR_NORMAL", "BOWLBUGS_NORMAL", "OVICOPTER_NORMAL", "EXOSKELETONS_NORMAL"],
            ["DECIMILLIPEDE_ELITE"], "KNOWLEDGE_DEMON_BOSS"),
        // The fifth hallway fight of this act was met in an Unknown room that turned out to be a Monster room.
        new("GLORY", "NONUPEIPE",
            ["DEVOTED_SCULPTOR_WEAK", "TURRET_OPERATOR_WEAK", "CONSTRUCT_MENAGERIE_NORMAL", "THE_LOST_AND_FORGOTTEN_NORMAL", "AXEBOTS_NORMAL"],
            ["MECHA_KNIGHT_ELITE"], "AEONGLASS_BOSS"),
    ];

    /// <summary>The first act of eleven real runs of v0.107.1 (the <c>acts</c> of the run files and saves of golden-saves),
    /// and the first hallway fight where the run got that far. The last two were runs of two players.</summary>
    private static readonly (string Seed, string FirstAct, string? FirstFight)[] RealStarts =
    [
        ("KMEHXZW0R5", "OVERGROWTH", null), ("EEXRY5WA1R", "OVERGROWTH", null), ("F30CH975SF", "UNDERDOCKS", null), ("E9U400ERJ4", "OVERGROWTH", null),
        ("1BUSLN0FY5", "OVERGROWTH", "SHRINKER_BEETLE_WEAK"), ("75F277YN2G", "UNDERDOCKS", "SEAPUNK_WEAK"), ("1CJXGGA4E5", "UNDERDOCKS", "SLUDGE_SPINNER_WEAK"),
        ("3FBTGQ63TM", "OVERGROWTH", "SLIMES_WEAK"), ("AG5XPLQQNY", "OVERGROWTH", null), ("BQ4CE2FRRJ", "UNDERDOCKS", null), ("FS5LGNDZZ7", "UNDERDOCKS", null),
    ];

    private static void RealRunLists(Check c)
    {
        c.Group("eleven real runs: the first act is the one the seed rolls, and the first fight the list's first entry", () =>
        {
            foreach (var (seed, firstAct, firstFight) in RealStarts)
            {
                var start = RunGeneration.Start(new RunStreams(seed), RunUnlocks.All);
                c.Equal(firstAct, GameNames.Slug(start.Acts[0]), $"{seed}: the first act");
                c.Sequence(["HIVE", "GLORY"], start.Acts.Skip(1).Select(GameNames.Slug), $"{seed}: the other two");
                if (firstFight is not null) c.Equal(firstFight, GameNames.Slug(start.Rooms[0].NormalEncounters[0]), $"{seed}: the first hallway fight");
            }
            c.Equal(6, RealStarts.Count(s => s.FirstAct == "OVERGROWTH"), "six of the eleven were Overgrowth runs");
        });

        c.Group("a whole real run of three acts (seed 75F277YN2G): every fight was the next entry of its act's list", () =>
        {
            // The game's record of a single Ironclad's won run at ascension 0, 48 floors. The k-th Monster room of
            // an act takes hallway entry k (an Unknown room that turns out to be one included), the k-th Elite room
            // elite entry k, and each act opens with its Ancient: all read off lists that the seed alone makes.
            var start = RunGeneration.Start(new RunStreams("75F277YN2G"), RunUnlocks.All);
            c.Sequence(ThreeActs.Select(a => a.Act), start.Acts.Select(GameNames.Slug), "the acts");
            for (var i = 0; i < ThreeActs.Length; i++)
            {
                var real = ThreeActs[i];
                var rooms = start.Rooms[i];
                var name = $"act {i + 1} ({start.Acts[i]})";
                c.Equal(real.Ancient, GameNames.Slug(rooms.Ancient ?? ""), $"{name}: the Ancient");
                c.Sequence(real.HallwayFights, rooms.NormalEncounters.Take(real.HallwayFights.Length).Select(GameNames.Slug), $"{name}: the hallway fights, in the order met");
                c.Sequence(real.Elites, rooms.EliteEncounters.Take(real.Elites.Length).Select(GameNames.Slug), $"{name}: the elites, in the order met");
                if (i < 2) c.Equal(real.Boss, GameNames.Slug(rooms.Boss), $"{name}: the boss");
            }

            // The one Event room of the run, in its first act: the walk starts at entry 1, since Neow's room
            // moved the counter, and entry 1 was allowed and not met before.
            c.Equal("THE_LEGENDS_WERE_TRUE", GameNames.Slug(start.Rooms[0].Events[1]), "act 1: the event the run met is entry 1 of the event list");
            c.That(GameNames.Slug(start.Rooms[0].Events[0]) != "THE_LEGENDS_WERE_TRUE", "and not entry 0");
        });

        c.Group("that run's last boss was not the rolled one: the first boss the profile had never met is forced", () =>
        {
            // The roll for Glory's boss on this seed gives the Queen; the run met Aeonglass. Aeonglass is the last
            // of Glory's three in the order the game introduces bosses, so this is what a profile gets that had met
            // the other two and not yet Aeonglass (the run was Mark's on 2026-10-09; his saves of two days later
            // list Aeonglass as met). Nothing is drawn for it: every list before and after stays as rolled.
            var everything = RunGeneration.Start(new RunStreams("75F277YN2G"), RunUnlocks.All);
            c.Equal("QUEEN_BOSS", GameNames.Slug(everything.Rooms[2].Boss), "the roll, for a profile that has met every boss");
            c.Equal("AEONGLASS_BOSS", ThreeActs[2].Boss, "what the run met");

            var met = GameLists.Acts.SelectMany(a => a.Encounters).Select(e => e.ClassName).Where(e => e != "AeonglassBoss");
            var before = new RunUnlocks(GameLists.Epochs, met, 50, GameLists.Acts.Select(a => a.ClassName));
            var then = RunGeneration.Start(new RunStreams("75F277YN2G"), before);
            c.Equal(ThreeActs[2].Boss, GameNames.Slug(then.Rooms[2].Boss), "for a profile that had not met Aeonglass");
            c.Sequence(["QueenBoss", "TestSubjectBoss", "AeonglassBoss"], GameLists.Act(GameLists.Glory).BossDiscoveryOrder, "Glory's order of introduction");
            c.Equal(everything.UpFrontDraws, then.UpFrontDraws, "the same number of numbers");
            c.Sequence(everything.Rooms[2].NormalEncounters, then.Rooms[2].NormalEncounters, "the same hallway fights");
            c.Equal(everything.Rooms[2].Ancient, then.Rooms[2].Ancient, "the same Ancient, though it is drawn after the boss");
            c.Sequence(everything.Rooms.Take(2).Select(r => r.Boss), then.Rooms.Take(2).Select(r => r.Boss), "and the first two acts' bosses, met before, are the rolled ones");
        });
    }

    // ---------- the bags: a pull always ends ----------

    private static void Bags(Check c)
    {
        c.Group("a relic pull always ends: after Rare there is nothing, also in a bag that only holds Shop relics", () =>
        {
            // The way from an empty rarity to the next is Shop, Common, Uncommon, Rare, and then nothing. A bag
            // in which Rare gave way to Shop again would hand out a Shop relic here, and would search an empty
            // bag forever; the bag counts its steps so that such a circle is an error and no check can hang on it.
            RelicBag ShopOnly()
            {
                var bag = new RelicBag();
                bag.Populate(["Bread", "LeesWaffle"], new GameRng(3));
                return bag;
            }
            c.Equal(null, ShopOnly().PullFromFront(RelicRarity.Rare), "a Rare pull from the front");
            c.Equal(null, ShopOnly().PullFromBack(RelicRarity.Rare), "a Rare pull from the back");
            c.Equal(null, ShopOnly().PullFromFront(RelicRarity.Uncommon), "an Uncommon pull: Rare is next, and then nothing");
            c.Equal(null, ShopOnly().PullFromFront(RelicRarity.Common), "a Common pull");
            c.That(ShopOnly().PullFromBack(RelicRarity.Shop) is "Bread" or "LeesWaffle", "a Shop pull finds one");
            c.That(ShopOnly().HasAvailableRelics(), "and the bag says it has relics left");

            var empty = new RelicBag();
            empty.Populate(["Bread"], new GameRng(3));
            c.Equal("Bread", empty.PullFromFront(RelicRarity.Shop), "the only relic");
            foreach (var rarity in Enum.GetValues<RelicRarity>())
                c.Equal(null, empty.PullFromFront(rarity), $"an empty bag, asked for {rarity}");
            c.That(!empty.HasAvailableRelics(), "an empty bag has nothing left");

            // The shared bag fills an empty rarity again, so its pulls end too, with a relic.
            var shared = new RelicBag(refreshAllowed: true);
            shared.Populate(["Bread"], new GameRng(3));
            c.Equal(("Bread", "Bread"), (shared.PullFromFront(RelicRarity.Shop), shared.PullFromFront(RelicRarity.Shop)), "the shared bag hands its only relic out again");
            c.Equal(null, shared.PullFromFront(RelicRarity.Rare), "and has nothing for a rarity it never held");
        });
    }
}
