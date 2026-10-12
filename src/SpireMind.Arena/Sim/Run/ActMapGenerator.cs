using System.Globalization;
using System.Text;

namespace SpireMind.Arena;

/// <summary>
/// What the map generator needs to know of an act: how many rooms lie between its Ancient and
/// its boss, and how the numbers of Rest Sites and Unknown rooms are drawn. The draw is the
/// first thing the map's stream is asked for, Rest Sites before Unknown rooms.
/// </summary>
/// <param name="Act">The act's class name.</param>
/// <param name="Rooms">Rooms between the Ancient and the boss, single player: the grid has one row more (row 0 stays empty).</param>
/// <param name="DrawCounts">Draws the act's numbers from the map's stream.</param>
public sealed record ActMapRules(string Act, int Rooms, Func<GameRng, MapCounts> DrawCounts)
{
    /// <summary>Shops on every map.</summary>
    public const int Shops = 3;

    /// <summary>Elites on every map at ascension 0 (an ascension level makes it 8: out of scope).</summary>
    public const int Elites = 5;

    /// <summary>
    /// The rules of one of the game's four acts. The number of Unknown rooms is a rounded normal
    /// draw around 12 kept within 10 to 14, one less in the second and third act. Rest Sites: a
    /// rounded normal draw around 7 kept to 6 or 7 in a first act, around 6 kept to 6 or 7 in
    /// the second, and 5 or 6 by a plain draw in the third.
    /// </summary>
    /// <exception cref="ArgumentException">The act is not one of the game's.</exception>
    public static ActMapRules For(string act)
    {
        var rooms = GameLists.Act(act).Rooms;
        static int Unknowns(GameRng rng) => rng.NextGaussianInt(12, 1, 10, 14);
        return act switch
        {
            GameLists.Overgrowth or GameLists.Underdocks => new ActMapRules(act, rooms, rng =>
            {
                var rests = rng.NextGaussianInt(7, 1, 6, 7);
                return new MapCounts(rests, Shops, Elites, Unknowns(rng));
            }),
            GameLists.Hive => new ActMapRules(act, rooms, rng =>
            {
                var rests = rng.NextGaussianInt(6, 1, 6, 7);
                return new MapCounts(rests, Shops, Elites, Unknowns(rng) - 1);
            }),
            GameLists.Glory => new ActMapRules(act, rooms, rng =>
            {
                var rests = rng.NextInt(5, 7);
                return new MapCounts(rests, Shops, Elites, Unknowns(rng) - 1);
            }),
            _ => throw new ArgumentException($"The arena holds no map rules for the act '{act}'.", nameof(act)),
        };
    }
}

/// <summary>
/// The game's map generator (v0.107.1, its standard act map), number for number: the same
/// draws from the same stream in the same order, so that a seed gives the map the game gives.
/// Five real saves are rebuilt by it point for point (MapChecks), and a second reader's own
/// port of the game's generator makes the same map on 1,430 seeds (GenerationChecks).
/// <para>
/// The steps. The act's numbers of Rest Sites and Unknown rooms are drawn. Seven paths are
/// walked from the first row to the last, each step to the same or a neighbouring column, never
/// across a diagonal that is already there. The first row becomes Monster rooms, one row
/// Treasure and the last row Rest Sites; the counted kinds (Rest Sites, shops, elites, Unknown
/// rooms, queued in that order) go to the free points in shuffled order, each point taking the
/// first queued kind that is allowed there, in at most three passes; what is still free becomes
/// a Monster room. Then stretches of path that say the same thing twice (same ends, same kinds
/// in between) are pruned and the kinds that fell short are put back on Monster rooms, up to
/// three times. Last, three passes move points sideways: they draw nothing.
/// </para>
/// <para>
/// What an exact port has to keep, beyond the rules: every draw, also one whose answer is
/// forced; the sorted shuffle (<see cref="GameRng.StableShuffle"/>) where the game sorts first;
/// the order in which a point's children are listed (<see cref="SlotSet{T}"/>), because the
/// pruning pass finds its stretches by walking them; and the order the pruning pass takes the
/// stretches in, which is the plain character order of a text key ("10,3" comes before "2,3").
/// </para>
/// <para>
/// Left out: more than one player (a map one row shorter), the ascension levels that add
/// elites, swap the Treasure row for elites or add a second boss, and the maps other things
/// make (Golden Compass, Spoils Map), none of which a first act of a single Ironclad can meet.
/// </para>
/// </summary>
public static class ActMapGenerator
{
    /// <summary>How many paths are walked up the grid.</summary>
    public const int Paths = 7;

    /// <summary>The map of an act of the game, by the act's class name ("Overgrowth").</summary>
    /// <param name="rng">The map's own stream (<see cref="RunStreams.ActMap"/>): "act_1_map" for the first act.</param>
    public static ActMap Generate(GameRng rng, string act) => Generate(rng, ActMapRules.For(act));

    /// <param name="prune">False leaves the pruning pass out, as the game's own tests can: only for checks of the steps before it.</param>
    public static ActMap Generate(GameRng rng, ActMapRules rules, bool prune = true) => new Builder(rng, rules).Build(prune);

    /// <summary>
    /// For checks: the passes that follow the placing of the kinds, run on a map drawn by hand, so
    /// that each can be held against a result worked out on paper. The drawn map's own points are
    /// moved and pruned; the map that comes back is what is left of them.
    /// </summary>
    /// <param name="counts">The numbers the repair pass fills up to.</param>
    /// <param name="prune">The prune-and-repair pass.</param>
    /// <param name="sideways">The three sideways passes.</param>
    internal static ActMap Finish(ActMap drawn, MapCounts counts, GameRng rng, bool prune, bool sideways) =>
        new Builder(rng, drawn, counts).Finish(prune, sideways);

    /// <summary>One map being made. The grid is the game's: a cell holds a point or nothing, and a pruned point's cell is emptied.</summary>
    private sealed class Builder
    {
        private const int Columns = ActMap.Width;

        private readonly GameRng _rng;
        private readonly int _startCounter;
        private readonly int _rows;
        private readonly MapPoint?[,] _grid;
        private readonly MapCounts _counts;
        private readonly MapPoint _start, _boss;
        private readonly SlotSet<MapPoint> _startPoints;

        public Builder(GameRng rng, ActMapRules rules)
        {
            _rng = rng;
            _startCounter = rng.Counter;
            _rows = rules.Rooms + 1;
            _grid = new MapPoint?[Columns, _rows];
            _counts = rules.DrawCounts(rng);
            _boss = new MapPoint(Columns / 2, _rows);
            _start = new MapPoint(Columns / 2, 0);
            _startPoints = new SlotSet<MapPoint>();
        }

        /// <summary>Goes on with a map that is already there: its grid, its points and its links (<see cref="ActMapGenerator.Finish"/>).</summary>
        public Builder(GameRng rng, ActMap drawn, MapCounts counts)
        {
            _rng = rng;
            _startCounter = rng.Counter;
            _rows = drawn.Rows;
            _grid = drawn.Grid;
            _counts = counts;
            _boss = drawn.Boss;
            _start = drawn.Start;
            _startPoints = drawn.StartSet;
        }

        public ActMap Finish(bool prune, bool sideways)
        {
            if (prune) PruneAndRepair();
            if (sideways)
            {
                Centre();
                Spread();
                Straighten();
            }
            return new ActMap(_grid, _start, _boss, _startPoints, _counts, _rng.Counter - _startCounter);
        }

        public ActMap Build(bool prune)
        {
            WalkPaths();
            AssignTypes();
            if (prune) PruneAndRepair();
            Centre();
            Spread();
            Straighten();
            return new ActMap(_grid, _start, _boss, _startPoints, _counts, _rng.Counter - _startCounter);
        }

        // ---------- the grid ----------

        /// <summary>Every point of the grid, column by column, each column from the bottom up: the game's order of "all map points".</summary>
        private IEnumerable<MapPoint> AllPoints()
        {
            for (var col = 0; col < Columns; col++)
                for (var row = 0; row < _rows; row++)
                    if (_grid[col, row] is { } point) yield return point;
        }

        private IEnumerable<MapPoint> Row(int row)
        {
            for (var col = 0; col < Columns; col++)
                if (_grid[col, row] is { } point) yield return point;
        }

        private MapPoint PointAt(int col, int row)
        {
            if (_grid[col, row] is { } point) return point;
            point = new MapPoint(col, row);
            _grid[col, row] = point;
            return point;
        }

        // ---------- step 1: seven paths ----------

        private void WalkPaths()
        {
            for (var path = 0; path < Paths; path++)
            {
                var first = PointAt(_rng.NextInt(0, Columns), 1);
                // The second path may not start where the first did; the others may start anywhere.
                if (path == 1)
                    while (_startPoints.Contains(first)) first = PointAt(_rng.NextInt(0, Columns), 1);
                _startPoints.Add(first);

                var point = first;
                while (point.Row < _rows - 1)
                {
                    var next = PointAt(NextColumn(point), point.Row + 1);
                    point.AddChild(next);
                    point = next;
                }
            }

            // The last row leads to the boss, the start point to the first row; both by column.
            foreach (var point in Row(_rows - 1)) point.AddChild(_boss);
            foreach (var point in Row(1)) _start.AddChild(point);
        }

        /// <summary>
        /// The column of a path's next step. The three directions (left, straight, right) are put
        /// in a shuffled order, two draws, and the first that crosses no diagonal is taken. At an
        /// edge the direction that would leave the grid means "straight".
        /// </summary>
        private int NextColumn(MapPoint from)
        {
            var left = Math.Max(0, from.Col - 1);
            var right = Math.Min(from.Col + 1, Columns - 1);
            var directions = new List<int> { -1, 0, 1 };
            _rng.StableShuffle(directions);
            foreach (var direction in directions)
            {
                var col = direction switch { -1 => left, 0 => from.Col, _ => right };
                if (!CrossesADiagonal(from, col)) return col;
            }
            throw new InvalidOperationException("The map generator found no next step for a path; the game would have failed here too.");
        }

        /// <summary>A step to a neighbouring column crosses when the point beside this one, in that column, already steps the other way.</summary>
        private bool CrossesADiagonal(MapPoint from, int toCol)
        {
            var move = toCol - from.Col;
            if (move == 0) return false;
            if (_grid[toCol, from.Row] is not { } neighbour) return false;
            foreach (var child in neighbour.Children)
                if (child.Col - neighbour.Col == -move) return true;
            return false;
        }

        // ---------- step 2: the kinds ----------

        private void AssignTypes()
        {
            Fix(_rows - 1, MapPointType.RestSite);
            Fix(_rows - 7, MapPointType.Treasure);
            Fix(1, MapPointType.Monster);

            var queue = new Queue<MapPointType>();
            for (var i = 0; i < _counts.Rests; i++) queue.Enqueue(MapPointType.RestSite);
            for (var i = 0; i < _counts.Shops; i++) queue.Enqueue(MapPointType.Shop);
            for (var i = 0; i < _counts.Elites; i++) queue.Enqueue(MapPointType.Elite);
            for (var i = 0; i < _counts.Unknowns; i++) queue.Enqueue(MapPointType.Unknown);

            // At most three passes over the free points, each pass in a newly shuffled order.
            for (var pass = 0; pass < 3 && queue.Count > 0; pass++)
            {
                var free = AllPoints().Where(p => p.Type == MapPointType.Unassigned).ToList();
                _rng.StableShuffle(free);
                foreach (var point in free)
                {
                    if (queue.Count == 0) break;
                    point.Type = NextAllowedType(queue, point);
                }
            }

            foreach (var point in AllPoints())
                if (point.Type == MapPointType.Unassigned) point.Type = MapPointType.Monster;
            _boss.Type = MapPointType.Boss;
            _start.Type = MapPointType.Ancient;
        }

        private void Fix(int row, MapPointType type)
        {
            foreach (var point in Row(row))
            {
                point.Type = type;
                point.CanBeModified = false;
            }
        }

        /// <summary>
        /// The first kind in the queue that is allowed on the point; it leaves the queue. Every kind
        /// looked at before it goes to the back, so the queue comes out rotated. When none is
        /// allowed the queue is as it was and the point stays free.
        /// </summary>
        private MapPointType NextAllowedType(Queue<MapPointType> queue, MapPoint point)
        {
            for (var i = 0; i < queue.Count; i++)
            {
                var type = queue.Dequeue();
                if (IsAllowed(type, point)) return type;
                queue.Enqueue(type);
            }
            return MapPointType.Unassigned;
        }

        /// <summary>
        /// The placement rules. No Rest Site in the three top rows. No Rest Site and no elite below
        /// row 6. An elite, a Rest Site, a Treasure room or a shop never directly before or after
        /// one of its own kind. Two children of one parent are never both Rest Sites, both Unknown
        /// rooms, both elites or both shops.
        /// <para>
        /// The game's list of kinds that may not be siblings names the Monster room too, and so
        /// does this test. But nobody ever asks it about a Monster room: the queue holds none, the
        /// repair puts none back, and what is left free becomes one without a question. Finished
        /// maps are full of Monster rooms under one parent; that is the game's, not a slip.
        /// </para>
        /// </summary>
        private bool IsAllowed(MapPointType type, MapPoint point)
        {
            if (point.Row >= _rows - 3 && type == MapPointType.RestSite) return false;
            if (point.Row < 6 && type is MapPointType.RestSite or MapPointType.Elite) return false;

            if (type is MapPointType.Elite or MapPointType.RestSite or MapPointType.Treasure or MapPointType.Shop)
            {
                foreach (var parent in point.Parents)
                    if (parent.Type == type) return false;
                foreach (var child in point.Children)
                    if (child.Type == type) return false;
            }

            if (type is MapPointType.RestSite or MapPointType.Monster or MapPointType.Unknown or MapPointType.Elite or MapPointType.Shop)
            {
                foreach (var parent in point.Parents)
                    foreach (var sibling in parent.Children)
                        if (!ReferenceEquals(sibling, point) && sibling.Type == type) return false;
            }
            return true;
        }

        // ---------- step 3: prune and repair ----------

        /// <summary>Up to three rounds: prune until no two stretches match, then put back the kinds that fell short; stop when nothing had to be put back.</summary>
        private void PruneAndRepair()
        {
            for (var round = 0; round < 3; round++)
            {
                PruneMatchingSegments();
                if (!Repair()) break;
            }
        }

        private void PruneMatchingSegments()
        {
            var rounds = 0;
            var matching = FindMatchingSegments();
            while (PruneOnce(matching))
            {
                rounds++;
                if (rounds > 50) throw new InvalidOperationException("The map generator could not prune its matching stretches in 50 rounds; the game would have failed here too.");
                matching = FindMatchingSegments();
            }
        }

        /// <summary>
        /// Every group of stretches that say the same thing: the same first point (or any start on
        /// row 0), the same last point, the same kinds along the way, and no inner point in common.
        /// A stretch begins at a point with more than one child (or at the start point), ends at
        /// a point with more than one parent, and is at least three points long. They are found by
        /// walking every way from the start point to the boss, children in set order, and the
        /// groups come out in the plain character order of their keys.
        /// </summary>
        private List<List<MapPoint[]>> FindMatchingSegments()
        {
            var segments = new SortedDictionary<string, List<MapPoint[]>>(StringComparer.Ordinal);
            var way = new MapPoint[_rows + 1];
            var key = new StringBuilder();

            void Walk(MapPoint point, int depth)
            {
                way[depth] = point;
                if (point.Type == MapPointType.Boss)
                {
                    AddSegmentsOf(way, depth + 1);
                    return;
                }
                foreach (var child in point.Children) Walk(child, depth + 1);
            }

            void AddSegmentsOf(MapPoint[] path, int length)
            {
                for (var from = 0; from < length - 1; from++)
                {
                    var first = path[from];
                    if (first.Children.Count <= 1 && first.Row != 0) continue;
                    for (var span = 2; span < length - from; span++)
                    {
                        var last = path[from + span];
                        if (last.Parents.Count < 2) continue;

                        key.Clear();
                        if (first.Row == 0) key.Append(CultureInfo.InvariantCulture, $"{first.Row}-{last.Col},{last.Row}-");
                        else key.Append(CultureInfo.InvariantCulture, $"{first.Col},{first.Row}-{last.Col},{last.Row}-");
                        for (var i = from; i <= from + span; i++)
                        {
                            if (i > from) key.Append(',');
                            key.Append((int)path[i].Type);
                        }
                        var text = key.ToString();

                        if (!segments.TryGetValue(text, out var group))
                        {
                            segments[text] = [path[from..(from + span + 1)]];
                        }
                        else if (!SharesAnInnerPoint(group, path, from, span))
                        {
                            group.Add(path[from..(from + span + 1)]);
                        }
                    }
                }
            }

            Walk(_start, 0);
            return segments.Values.Where(group => group.Count > 1).ToList();
        }

        /// <summary>Whether the stretch of <paramref name="path"/> has an inner point in the same position as one of the group's.</summary>
        private static bool SharesAnInnerPoint(List<MapPoint[]> group, MapPoint[] path, int from, int span)
        {
            foreach (var existing in group)
                for (var i = 1; i < span; i++)
                    if (ReferenceEquals(existing[i], path[from + i])) return true;
            return false;
        }

        /// <summary>
        /// One round of pruning: the first group that gives way ends it. A group is shuffled as it
        /// stands; then all of its stretches but one are pruned, and when none could be, one link
        /// inside one of them is cut instead. True when the map changed.
        /// </summary>
        private bool PruneOnce(List<List<MapPoint[]>> matching)
        {
            foreach (var group in matching)
            {
                _rng.Shuffle(group);
                if (PruneAllButOne(group) != 0) return true;
                foreach (var segment in group)
                    if (CutALink(segment)) return true;
            }
            return false;
        }

        private int PruneAllButOne(List<MapPoint[]> group)
        {
            var pruned = 0;
            foreach (var segment in group)
            {
                if (pruned == group.Count - 1) return pruned;
                if (PruneSegment(segment)) pruned++;
            }
            return pruned;
        }

        /// <summary>
        /// Takes the points of a stretch off the map that nothing else needs: a point with one
        /// parent and one child, whose parent has other children, with no fork further along the
        /// stretch, where the stretch's end can still be reached another way and no child outside
        /// the stretch would be left without a parent.
        /// <para>
        /// Four of the tests in here can never decide (the point that is already gone, the parent
        /// whose cell is empty, the end with one parent left, the child outside the stretch), and
        /// two of those that find the stretches cannot either (a start on row 0, an end with two
        /// parents). They are the game's and are kept as written; MAP.md, "Six tests of the pruning
        /// pass that cannot decide", says why no map can show them.
        /// </para>
        /// </summary>
        private bool PruneSegment(MapPoint[] segment)
        {
            var removedAny = false;
            for (var i = 0; i < segment.Length - 1; i++)
            {
                var point = segment[i];
                // A point of this stretch is already gone (an earlier stretch of the group shared it): count the stretch as pruned.
                if (!IsOnMap(point)) return true;

                if (point.Children.Count > 1 || point.Parents.Count > 1) continue;
                if (point.Parents.Any(parent => parent.Children.Count == 1 && !CellIsEmpty(parent))) continue;

                var forkAhead = false;
                for (var k = i; k < segment.Length && !forkAhead; k++)
                    forkAhead = segment[k].Children.Count > 1 && segment[k].Parents.Count == 1;
                if (forkAhead) continue;

                if (segment[^1].Parents.Count == 1) return false;

                var orphans = false;
                foreach (var child in point.Children)
                    if (Array.IndexOf(segment, child) < 0 && child.Parents.Count == 1) orphans = true;
                if (orphans) continue;

                RemovePoint(point);
                removedAny = true;
            }
            return removedAny;
        }

        /// <summary>The start point and the boss are never in the grid and always on the map; any other point is on it while its cell holds something.</summary>
        private bool IsOnMap(MapPoint point)
        {
            if (_grid[point.Col, point.Row] is null && point.Type != MapPointType.Ancient) return point.Type == MapPointType.Boss;
            return true;
        }

        /// <summary>The game's "is removed": the point's cell is empty. That is also true of the start point, whose cell on row 0 never held it.</summary>
        private bool CellIsEmpty(MapPoint point) => _grid[point.Col, point.Row] is null;

        private void RemovePoint(MapPoint point)
        {
            _grid[point.Col, point.Row] = null;
            _startPoints.Remove(point);
            foreach (var child in point.Children.ToList()) point.RemoveChild(child);
            foreach (var parent in point.Parents.ToList()) parent.RemoveChild(point);
        }

        /// <summary>Cuts every link of a stretch that goes from a point with two or more children to a point with another parent. True when one was cut.</summary>
        private static bool CutALink(MapPoint[] segment)
        {
            var cut = false;
            for (var i = 0; i < segment.Length - 1; i++)
            {
                var point = segment[i];
                if (point.Children.Count < 2) continue;
                var next = segment[i + 1];
                if (next.Parents.Count == 1) continue;
                point.RemoveChild(next);
                cut = true;
            }
            return cut;
        }

        /// <summary>
        /// Puts back what pruning took: for shops, elites, Rest Sites and Unknown rooms in that
        /// order, while the map holds fewer than the act's number, Monster rooms in shuffled order
        /// are turned into the kind wherever the placement rules allow it. The fixed rows are never
        /// touched. The Rest Sites counted include the whole last row, so pruned Rest Sites are
        /// rarely put back. True when anything was changed: the map is then pruned again.
        /// <para>
        /// All four kinds are asked in every round, also after an earlier one was put back: the
        /// game joins the four answers with an "or" that looks at both sides. Stopping at the first
        /// yes would put one kind back a round, and make another map on about 2 seeds in 1,000.
        /// </para>
        /// </summary>
        private bool Repair()
        {
            var repaired = false;
            repaired |= Repair(MapPointType.Shop, _counts.Shops);
            repaired |= Repair(MapPointType.Elite, _counts.Elites);
            repaired |= Repair(MapPointType.RestSite, _counts.Rests);
            repaired |= Repair(MapPointType.Unknown, _counts.Unknowns);
            return repaired;
        }

        private bool Repair(MapPointType type, int target)
        {
            var missing = target - AllPoints().Count(p => p.Type == type);
            if (missing <= 0) return false;

            var repaired = false;
            var monsters = AllPoints().Where(p => p.Type == MapPointType.Monster && p.CanBeModified).ToList();
            _rng.StableShuffle(monsters);
            foreach (var point in monsters)
            {
                if (missing == 0) break;
                if (!IsAllowed(type, point)) continue;
                point.Type = type;
                missing--;
                repaired = true;
            }
            return repaired;
        }

        // ---------- step 4: three sideways passes (no draws) ----------

        private void Move(MapPoint point, int toCol)
        {
            _grid[point.Col, point.Row] = null;
            _grid[toCol, point.Row] = point;
            point.Coord = new MapCoord(toCol, point.Row);
        }

        private bool ColumnIsEmpty(int col)
        {
            for (var row = 0; row < _rows; row++)
                if (_grid[col, row] is not null) return false;
            return true;
        }

        /// <summary>When the two outer columns of one side are empty and those of the other side are not, the whole map moves one column towards the empty side.</summary>
        private void Centre()
        {
            var leftEmpty = ColumnIsEmpty(0) && ColumnIsEmpty(1);
            var rightEmpty = ColumnIsEmpty(Columns - 1) && ColumnIsEmpty(Columns - 2);
            if (leftEmpty == rightEmpty) return;

            if (rightEmpty)
            {
                for (var row = 0; row < _rows; row++)
                    for (var col = Columns - 2; col >= 0; col--)
                        if (_grid[col, row] is { } point) Move(point, col + 1);
            }
            else
            {
                for (var row = 0; row < _rows; row++)
                    for (var col = 1; col < Columns; col++)
                        if (_grid[col, row] is { } point) Move(point, col - 1);
            }
        }

        /// <summary>
        /// Row by row, points move apart: each point in turn looks at the columns it may stand in
        /// (within one column of every parent and every child) that are free, lowest column first,
        /// and moves to the one that puts it furthest from its nearest neighbour in the row, if
        /// that is further than where it stands. Repeated until a whole turn moves nothing.
        /// </summary>
        private void Spread()
        {
            for (var row = 0; row < _rows; row++)
            {
                var points = Row(row).ToList();
                var turns = 0;
                bool moved;
                do
                {
                    if (++turns > 10_000) throw new InvalidOperationException("The map generator's spreading pass does not come to rest; the game would hang here too.");
                    moved = false;
                    foreach (var point in points)
                    {
                        var best = point.Col;
                        var bestGap = Gap(point.Col, points, point);
                        for (var col = 0; col < Columns; col++)
                        {
                            if (col == point.Col || _grid[col, row] is not null || !MayStandIn(point, col)) continue;
                            var gap = Gap(col, points, point);
                            if (gap <= bestGap) continue;
                            best = col;
                            bestGap = gap;
                        }
                        if (best == point.Col) continue;
                        Move(point, best);
                        moved = true;
                    }
                }
                while (moved);
            }
        }

        private static bool MayStandIn(MapPoint point, int col)
        {
            foreach (var parent in point.Parents)
                if (Math.Abs(parent.Col - col) > 1) return false;
            foreach (var child in point.Children)
                if (Math.Abs(child.Col - col) > 1) return false;
            return true;
        }

        /// <summary>The distance from a column to the nearest other point of the row; no other point counts as endlessly far.</summary>
        private static int Gap(int col, List<MapPoint> row, MapPoint self)
        {
            var gap = int.MaxValue;
            foreach (var other in row)
                if (!ReferenceEquals(other, self)) gap = Math.Min(gap, Math.Abs(col - other.Col));
            return gap;
        }

        /// <summary>
        /// A point with one parent and one child that sticks out to one side of both moves one
        /// column back towards them when that cell is free. Rows from the bottom, columns from the
        /// left; a point that moved right is met again in its new cell and looked at once more.
        /// </summary>
        private void Straighten()
        {
            for (var row = 0; row < _rows; row++)
            {
                for (var col = 0; col < Columns; col++)
                {
                    if (_grid[col, row] is not { } point || point.Parents.Count != 1 || point.Children.Count != 1) continue;
                    var parent = point.ParentSet.First();
                    var child = point.ChildSet.First();
                    var sticksOutLeft = point.Col < child.Col && point.Col < parent.Col;
                    var sticksOutRight = point.Col > child.Col && point.Col > parent.Col;
                    if (sticksOutLeft && col < Columns - 1)
                    {
                        if (_grid[col + 1, row] is not null) continue;
                        Move(point, col + 1);
                    }
                    if (sticksOutRight && col > 0 && _grid[col - 1, row] is null) Move(point, col - 1);
                }
            }
        }
    }
}
