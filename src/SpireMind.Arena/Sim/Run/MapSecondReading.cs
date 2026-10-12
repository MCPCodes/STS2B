using System.Globalization;
using System.Text;

namespace SpireMind.Arena;

/// <summary>
/// A second reading of the game's map generator (v0.107.1), for checks only. It was written
/// from the game's code by another reader than <see cref="ActMapGenerator"/> and shares nothing
/// with it but the random stream and the names of the kinds: its points keep their links in the
/// runtime's own hash sets (the arena's generator keeps a set of its own that lists the same
/// way), its groups of equal stretches sit in the runtime's sorted dictionary, its shuffles and
/// its normal draw are written out here once more. <see cref="GenerationChecks"/> makes the
/// same map with both and compares every point, kind, link order and draw.
/// <para>
/// It also counts. Six tests of the pruning pass can never decide anything (MAP.md, "Six tests
/// of the pruning pass that cannot decide"); <see cref="Tally"/> says how often each one did,
/// and <see cref="Without"/> leaves a test out, so a check can hold that the map is the same
/// without it.
/// </para>
/// <para>Never called by a run. The run's map is <see cref="ActMapGenerator"/>'s.</para>
/// </summary>
internal static class MapSecondReading
{
    /// <summary>Which of the six tests to leave out, for the check that asks "would it matter".</summary>
    [Flags]
    public enum Without
    {
        Nothing = 0,

        /// <summary>The test "this point of the stretch is no longer on the map: count the stretch as pruned".</summary>
        GoneTest = 1,

        /// <summary>The part "unless that parent's own cell is empty" of the test "a parent has no other child".</summary>
        EmptyCellExcuse = 2,

        /// <summary>The test "the stretch's end has one parent left: give up on this stretch".</summary>
        OneParentEnd = 4,

        /// <summary>The part "or it is on row 0" of "a stretch starts at a point with more than one child".</summary>
        RowZeroStart = 8,

        /// <summary>The test "a stretch ends at a point with two parents or more": any point may end one.</summary>
        TwoParentEnd = 16,

        /// <summary>The test "a child outside the stretch would be left without a parent".</summary>
        OrphanTest = 32,

        /// <summary>All six.</summary>
        AllSix = GoneTest | EmptyCellExcuse | OneParentEnd | RowZeroStart | TwoParentEnd | OrphanTest,
    }

    /// <summary>How often each of the six tests decided, and what the pruning pass did, on one map.</summary>
    public sealed class Tally
    {
        /// <summary>A point of a stretch was met that was no longer on the map.</summary>
        public int Gone;

        /// <summary>A parent with one child stood in the way of nothing only because its own cell was empty.</summary>
        public int EmptyCellExcused;

        /// <summary>A stretch was given up because its end had one parent left.</summary>
        public int OneParentEnds;

        /// <summary>A stretch that started on row 0 at a point with one child ended up in a group of two or more.</summary>
        public int RowZeroStartsInAGroup;

        /// <summary>A point was kept because a child outside the stretch would have lost its only parent.</summary>
        public int OrphansSpared;

        public int PointsPruned;
        public int LinksCut;

        /// <summary>Rounds of "prune, then repair" in which something was put back.</summary>
        public int RoundsThatRepaired;

        public int PointsRepaired;

        public void Add(Tally other)
        {
            Gone += other.Gone;
            EmptyCellExcused += other.EmptyCellExcused;
            OneParentEnds += other.OneParentEnds;
            RowZeroStartsInAGroup += other.RowZeroStartsInAGroup;
            OrphansSpared += other.OrphansSpared;
            PointsPruned += other.PointsPruned;
            LinksCut += other.LinksCut;
            RoundsThatRepaired += other.RoundsThatRepaired;
            PointsRepaired += other.PointsRepaired;
        }
    }

    /// <summary>What an act gives the generator, read from the act's class once more.</summary>
    /// <param name="Rooms">Rooms between the Ancient and the boss, for one player.</param>
    /// <param name="Counts">The act's own draw of Rest Sites and Unknown rooms, the first thing the stream is asked for.</param>
    public sealed record Act(int Rooms, Func<GameRng, MapCounts> Counts);

    /// <summary>The four acts: 15, 15, 14 and 13 rooms; Rest Sites around 7, 7 and 6 kept to 6 or 7, and a plain 5 or 6;
    /// Unknown rooms around 12 kept to 10..14, one less from the second act on; three shops, five elites.</summary>
    public static Act ActOf(string act) => act switch
    {
        "Overgrowth" or "Underdocks" => new Act(15, rng => { var rests = Normal(rng, 7, 1, 6, 7); return new MapCounts(rests, 3, 5, Normal(rng, 12, 1, 10, 14)); }),
        "Hive" => new Act(14, rng => { var rests = Normal(rng, 6, 1, 6, 7); return new MapCounts(rests, 3, 5, Normal(rng, 12, 1, 10, 14) - 1); }),
        "Glory" => new Act(13, rng => { var rests = 5 + (int)(rng.NextDouble() * 2); return new MapCounts(rests, 3, 5, Normal(rng, 12, 1, 10, 14) - 1); }),
        _ => throw new ArgumentException($"'{act}' is not an act.", nameof(act)),
    };

    /// <summary>The finished map as one text, written exactly as <see cref="ActMap.Describe"/> writes the arena's.</summary>
    public sealed record Reading(string Text, int Draws, MapCounts Counts, Tally Tally);

    public static Reading Read(GameRng rng, Act act, Without without = Without.Nothing, bool prune = true)
    {
        var before = rng.Counter;
        var maker = new Maker(rng, act, without);
        maker.Make(prune);
        return new Reading(maker.Describe(), rng.Counter - before, maker.Counts, maker.Tally);
    }

    // ---------- the two ways of drawing that the generator has besides a plain number ----------

    /// <summary>Two numbers make one normal value; it is scaled, rounded (a half to the even number) and drawn again while out of range.</summary>
    private static int Normal(GameRng rng, int mean, int spread, int lowest, int highest)
    {
        while (true)
        {
            var first = 1.0 - rng.NextDouble();
            var second = 1.0 - rng.NextDouble();
            var bell = Math.Sqrt(-2.0 * Math.Log(first)) * Math.Sin(Math.PI * 2.0 * second);
            var whole = (int)Math.Round(mean + spread * bell, MidpointRounding.ToEven);
            if (whole >= lowest && whole <= highest) return whole;
        }
    }

    /// <summary>The plain shuffle: from the last place down to the second, each changes places with a drawn place at or below it.</summary>
    private static void Mix<T>(List<T> items, GameRng rng)
    {
        var place = items.Count;
        while (place > 1)
        {
            place--;
            var other = (int)(rng.NextDouble() * (place + 1));
            (items[other], items[place]) = (items[place], items[other]);
        }
    }

    /// <summary>The shuffle that sorts first.</summary>
    private static void SortThenMix<T>(List<T> items, GameRng rng) where T : IComparable<T>
    {
        items.Sort();
        Mix(items, rng);
    }

    private sealed class Spot(int col, int row) : IComparable<Spot>
    {
        public int Col = col;
        public readonly int Row = row;
        public MapPointType Kind;
        public bool Fixed;

        // The runtime's own hash sets, compared by reference, as the game's points are.
        public readonly HashSet<Spot> Above = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<Spot> Below = new(ReferenceEqualityComparer.Instance);

        public int CompareTo(Spot? other) => other is null ? 1 : (Col, Row).CompareTo((other.Col, other.Row));

        public void Join(Spot child)
        {
            Above.Add(child);
            child.Below.Add(this);
        }

        public void Part(Spot child)
        {
            Above.Remove(child);
            child.Below.Remove(this);
        }
    }

    private sealed class Maker
    {
        private const int Wide = 7;

        private readonly GameRng _rng;
        private readonly Without _without;
        private readonly int _tall;
        private readonly Spot?[,] _cells;
        private readonly Spot _first, _last;
        private readonly HashSet<Spot> _pathStarts = new(ReferenceEqualityComparer.Instance);

        public MapCounts Counts { get; }
        public Tally Tally { get; } = new();

        public Maker(GameRng rng, Act act, Without without)
        {
            _rng = rng;
            _without = without;
            _tall = act.Rooms + 1;
            _cells = new Spot?[Wide, _tall];
            Counts = act.Counts(rng);
            _last = new Spot(Wide / 2, _tall);
            _first = new Spot(Wide / 2, 0);
        }

        public void Make(bool prune)
        {
            LayPaths();
            GiveKinds();
            if (prune) PruneThenRepair();
            MoveToTheMiddle();
            MoveApart();
            MoveBackInLine();
        }

        private bool Left(Without what) => (_without & what) != 0;

        private IEnumerable<Spot> Everything()
        {
            for (var col = 0; col < Wide; col++)
                for (var row = 0; row < _tall; row++)
                    if (_cells[col, row] is { } spot) yield return spot;
        }

        private Spot At(int col, int row) => _cells[col, row] ??= new Spot(col, row);

        // ---------- paths ----------

        private void LayPaths()
        {
            for (var path = 0; path < 7; path++)
            {
                var from = At((int)(_rng.NextDouble() * Wide), 1);
                if (path == 1)
                    while (_pathStarts.Contains(from)) from = At((int)(_rng.NextDouble() * Wide), 1);
                _pathStarts.Add(from);

                var here = from;
                while (here.Row < _tall - 1)
                {
                    var next = At(StepFrom(here), here.Row + 1);
                    here.Join(next);
                    here = next;
                }
            }
            for (var col = 0; col < Wide; col++)
                if (_cells[col, _tall - 1] is { } top) top.Join(_last);
            for (var col = 0; col < Wide; col++)
                if (_cells[col, 1] is { } bottom) _first.Join(bottom);
        }

        private int StepFrom(Spot here)
        {
            var ways = new List<int> { -1, 0, 1 };
            SortThenMix(ways, _rng);
            foreach (var way in ways)
            {
                var col = way < 0 ? Math.Max(0, here.Col - 1) : way > 0 ? Math.Min(here.Col + 1, Wide - 1) : here.Col;
                if (!Crosses(here, col)) return col;
            }
            throw new InvalidOperationException("No step is free: the game would fail here too.");
        }

        private bool Crosses(Spot here, int toCol)
        {
            var sideways = toCol - here.Col;
            if (sideways == 0) return false;
            var beside = _cells[toCol, here.Row];
            if (beside is null) return false;
            foreach (var child in beside.Above)
                if (child.Col - beside.Col == -sideways) return true;
            return false;
        }

        // ---------- kinds ----------

        private void GiveKinds()
        {
            FixRow(_tall - 1, MapPointType.RestSite);
            FixRow(_tall - 7, MapPointType.Treasure);
            FixRow(1, MapPointType.Monster);

            var waiting = new Queue<MapPointType>();
            for (var n = 0; n < Counts.Rests; n++) waiting.Enqueue(MapPointType.RestSite);
            for (var n = 0; n < Counts.Shops; n++) waiting.Enqueue(MapPointType.Shop);
            for (var n = 0; n < Counts.Elites; n++) waiting.Enqueue(MapPointType.Elite);
            for (var n = 0; n < Counts.Unknowns; n++) waiting.Enqueue(MapPointType.Unknown);

            for (var pass = 0; pass < 3; pass++)
            {
                if (waiting.Count <= 0) break;
                var free = Everything().Where(s => s.Kind == MapPointType.Unassigned).ToList();
                SortThenMix(free, _rng);
                foreach (var spot in free)
                {
                    if (waiting.Count == 0) break;
                    spot.Kind = FirstThatFits(waiting, spot);
                }
            }

            foreach (var spot in Everything().Where(s => s.Kind == MapPointType.Unassigned).ToList()) spot.Kind = MapPointType.Monster;
            _last.Kind = MapPointType.Boss;
            _first.Kind = MapPointType.Ancient;
        }

        private void FixRow(int row, MapPointType kind)
        {
            for (var col = 0; col < Wide; col++)
            {
                if (_cells[col, row] is not { } spot) continue;
                spot.Kind = kind;
                spot.Fixed = true;
            }
        }

        private MapPointType FirstThatFits(Queue<MapPointType> waiting, Spot spot)
        {
            for (var looked = 0; looked < waiting.Count; looked++)
            {
                var kind = waiting.Dequeue();
                if (Fits(kind, spot)) return kind;
                waiting.Enqueue(kind);
            }
            return MapPointType.Unassigned;
        }

        private bool Fits(MapPointType kind, Spot spot)
        {
            // Near the top: no Rest Site in the three top rows of the grid.
            if (spot.Row >= _tall - 3 && kind == MapPointType.RestSite) return false;
            // Near the bottom: no Rest Site and no elite below row 6.
            if (spot.Row < 6 && (kind == MapPointType.RestSite || kind == MapPointType.Elite)) return false;
            // Never twice in a row.
            if (kind is MapPointType.Elite or MapPointType.RestSite or MapPointType.Treasure or MapPointType.Shop)
            {
                if (spot.Below.Concat(spot.Above).Any(near => near.Kind == kind)) return false;
            }
            // Never two of one parent.
            if (kind is MapPointType.RestSite or MapPointType.Monster or MapPointType.Unknown or MapPointType.Elite or MapPointType.Shop)
            {
                if (spot.Below.SelectMany(parent => parent.Above).Any(sibling => !ReferenceEquals(sibling, spot) && sibling.Kind == kind)) return false;
            }
            return true;
        }

        // ---------- prune and repair ----------

        private void PruneThenRepair()
        {
            for (var round = 0; round < 3; round++)
            {
                PruneWhileSomethingMatches();
                if (!Repair()) break;
                Tally.RoundsThatRepaired++;
            }
        }

        private void PruneWhileSomethingMatches()
        {
            var times = 0;
            var groups = MatchingStretches();
            while (PruneOneGroup(groups))
            {
                times++;
                if (times > 50) throw new InvalidOperationException("Pruning does not end: the game would fail here too.");
                groups = MatchingStretches();
            }
        }

        /// <summary>
        /// The groups of stretches that say the same thing, in the plain character order of their keys.
        /// Every way from the start point to the boss is walked, the ways through an earlier child first (a
        /// point without children that is not the boss ends no way), and on each way every stretch from a
        /// point that may start one to a point that may end one, at least three points long, is looked at:
        /// the first of its key founds a group, a later one joins unless it shares an inner point with a member.
        /// <para>
        /// Two ways that follow one another share their first points, and the stretches that lie wholly in
        /// that shared part were looked at on the way before. Looking at the same stretch again changes
        /// nothing (it is in its group already, or it was kept out by a member that is still there), so
        /// only stretches that reach beyond the shared part are looked at. That is this reading's own
        /// shortcut; the game looks at all of them every time.
        /// </para>
        /// </summary>
        private List<List<Spot[]>> MatchingStretches()
        {
            var byKey = new SortedDictionary<string, List<Spot[]>>(StringComparer.Ordinal);
            var rowZeroStarts = new HashSet<Spot[]>(ReferenceEqualityComparer.Instance);
            var way = new List<Spot>();
            // How many of the first points of the way being walked were already on the way that reached the boss last.
            var shared = 0;

            void LookAtStretches()
            {
                for (var from = 0; from < way.Count - 1; from++)
                {
                    var start = way[from];
                    var byRowZero = start.Above.Count <= 1;
                    if (byRowZero && (Left(Without.RowZeroStart) || start.Row != 0)) continue;
                    for (var length = Math.Max(2, shared - from); length < way.Count - from; length++)
                    {
                        var end = way[from + length];
                        if (!Left(Without.TwoParentEnd) && end.Below.Count < 2) continue;
                        var stretch = way.GetRange(from, length + 1).ToArray();
                        var key = KeyOf(stretch);
                        if (!byKey.TryGetValue(key, out var group))
                        {
                            byKey[key] = [stretch];
                            if (byRowZero) rowZeroStarts.Add(stretch);
                        }
                        else if (!group.Any(other => ShareAnInnerPoint(other, stretch)))
                        {
                            group.Add(stretch);
                            if (byRowZero) rowZeroStarts.Add(stretch);
                        }
                    }
                }
            }

            void Climb(Spot spot)
            {
                way.Add(spot);
                if (spot.Kind == MapPointType.Boss)
                {
                    LookAtStretches();
                    shared = way.Count;
                }
                else
                {
                    foreach (var child in spot.Above) Climb(child);
                }
                way.RemoveAt(way.Count - 1);
                if (way.Count < shared) shared = way.Count;
            }

            Climb(_first);
            var matching = byKey.Values.Where(group => group.Count > 1).ToList();
            Tally.RowZeroStartsInAGroup += matching.Sum(group => group.Count(rowZeroStarts.Contains));
            return matching;
        }

        private static string KeyOf(Spot[] stretch)
        {
            var start = stretch[0];
            var end = stretch[^1];
            var key = new StringBuilder();
            if (start.Row == 0) key.Append(CultureInfo.InvariantCulture, $"{start.Row}-{end.Col},{end.Row}-");
            else key.Append(CultureInfo.InvariantCulture, $"{start.Col},{start.Row}-{end.Col},{end.Row}-");
            key.Append(string.Join(",", stretch.Select(spot => ((int)spot.Kind).ToString(CultureInfo.InvariantCulture))));
            return key.ToString();
        }

        private static bool ShareAnInnerPoint(Spot[] one, Spot[] other)
        {
            if (one.Length < 3 || other.Length < 3) return false;
            for (var i = 1; i <= one.Length - 2; i++)
                if (ReferenceEquals(one[i], other[i])) return true;
            return false;
        }

        private bool PruneOneGroup(List<List<Spot[]>> groups)
        {
            foreach (var group in groups)
            {
                Mix(group, _rng);
                if (PruneAllButTheLast(group) != 0) return true;
                foreach (var stretch in group)
                    if (CutLinksOf(stretch)) return true;
            }
            return false;
        }

        private int PruneAllButTheLast(List<Spot[]> group)
        {
            var pruned = 0;
            foreach (var stretch in group)
            {
                if (pruned == group.Count - 1) return pruned;
                if (PruneStretch(stretch)) pruned++;
            }
            return pruned;
        }

        private bool OnTheMap(Spot spot)
        {
            if (_cells[spot.Col, spot.Row] is null && spot.Kind != MapPointType.Ancient) return spot.Kind == MapPointType.Boss;
            return true;
        }

        private bool PruneStretch(Spot[] stretch)
        {
            var anyGone = false;
            for (var i = 0; i < stretch.Length - 1; i++)
            {
                var spot = stretch[i];
                if (!OnTheMap(spot))
                {
                    Tally.Gone++;
                    if (!Left(Without.GoneTest)) return true;
                }

                if (spot.Above.Count > 1 || spot.Below.Count > 1) continue;
                var onlyChildOfAParent = spot.Below.Any(parent => parent.Above.Count == 1);
                var ofAParentThatCounts = spot.Below.Any(parent => parent.Above.Count == 1 && _cells[parent.Col, parent.Row] is not null);
                if (onlyChildOfAParent && !ofAParentThatCounts) Tally.EmptyCellExcused++;
                if (Left(Without.EmptyCellExcuse) ? onlyChildOfAParent : ofAParentThatCounts) continue;

                var rest = stretch.Skip(i).ToArray();
                if (rest.Any(later => later.Above.Count > 1 && later.Below.Count == 1)) continue;

                if (stretch[^1].Below.Count == 1)
                {
                    Tally.OneParentEnds++;
                    if (!Left(Without.OneParentEnd)) return false;
                }

                var orphan = spot.Above.Where(child => !stretch.Contains(child)).Any(child => child.Below.Count == 1);
                if (orphan) Tally.OrphansSpared++;
                if (orphan && !Left(Without.OrphanTest)) continue;

                TakeOff(spot);
                anyGone = true;
            }
            return anyGone;
        }

        private void TakeOff(Spot spot)
        {
            _cells[spot.Col, spot.Row] = null;
            _pathStarts.Remove(spot);
            foreach (var child in spot.Above.ToList()) spot.Part(child);
            foreach (var parent in spot.Below.ToList()) parent.Part(spot);
            Tally.PointsPruned++;
        }

        private bool CutLinksOf(Spot[] stretch)
        {
            var cut = false;
            for (var i = 0; i < stretch.Length - 1; i++)
            {
                var spot = stretch[i];
                if (spot.Above.Count < 2) continue;
                var next = stretch[i + 1];
                if (next.Below.Count == 1) continue;
                spot.Part(next);
                Tally.LinksCut++;
                cut = true;
            }
            return cut;
        }

        /// <summary>All four kinds are tried in every round, whether or not an earlier one was put back.</summary>
        private bool Repair()
        {
            var shops = PutBack(MapPointType.Shop, Counts.Shops);
            var elites = PutBack(MapPointType.Elite, Counts.Elites);
            var rests = PutBack(MapPointType.RestSite, Counts.Rests);
            var unknowns = PutBack(MapPointType.Unknown, Counts.Unknowns);
            return shops || elites || rests || unknowns;
        }

        private bool PutBack(MapPointType kind, int wanted)
        {
            var missing = wanted - Everything().Count(s => s.Kind == kind);
            if (missing <= 0) return false;
            var done = false;
            var monsters = Everything().Where(s => s.Kind == MapPointType.Monster && !s.Fixed).ToList();
            SortThenMix(monsters, _rng);
            foreach (var spot in monsters)
            {
                if (missing == 0) break;
                if (!Fits(kind, spot)) continue;
                spot.Kind = kind;
                missing--;
                done = true;
                Tally.PointsRepaired++;
            }
            return done;
        }

        // ---------- sideways ----------

        private bool EmptyColumn(int col)
        {
            for (var row = 0; row < _tall; row++)
                if (_cells[col, row] is not null) return false;
            return true;
        }

        private void MoveToTheMiddle()
        {
            var leftFree = EmptyColumn(0) && EmptyColumn(1);
            var rightFree = EmptyColumn(Wide - 1) && EmptyColumn(Wide - 2);
            var shift = 0;
            if (leftFree && !rightFree) shift = -1;
            else if (!leftFree && rightFree) shift = 1;
            if (shift == 0) return;

            for (var row = 0; row < _tall; row++)
            {
                // Towards the right the row is gone over from the right, towards the left from the left.
                for (var step = 0; step < Wide; step++)
                {
                    var col = shift > 0 ? Wide - 1 - step : step;
                    var spot = _cells[col, row];
                    _cells[col, row] = null;
                    var to = col + shift;
                    if (to < 0 || to >= Wide) continue;
                    _cells[to, row] = spot;
                    if (spot is not null) spot.Col = to;
                }
            }
        }

        private static HashSet<int> Around(int col)
        {
            var near = new HashSet<int>(3);
            for (var d = -1; d <= 1; d++)
                if (col + d >= 0 && col + d < Wide) near.Add(col + d);
            return near;
        }

        private static HashSet<int> ColumnsFor(Spot spot)
        {
            var columns = new HashSet<int>(Enumerable.Range(0, Wide));
            foreach (var parent in spot.Below) columns.IntersectWith(Around(parent.Col));
            foreach (var child in spot.Above) columns.IntersectWith(Around(child.Col));
            return columns;
        }

        private static int Room(int col, List<Spot> row, Spot self)
        {
            var room = int.MaxValue;
            foreach (var other in row)
                if (!ReferenceEquals(other, self)) room = Math.Min(room, Math.Abs(col - other.Col));
            return room;
        }

        private void MoveApart()
        {
            for (var row = 0; row < _tall; row++)
            {
                var spots = new List<Spot>();
                for (var col = 0; col < Wide; col++)
                    if (_cells[col, row] is { } spot) spots.Add(spot);

                bool moved;
                do
                {
                    moved = false;
                    foreach (var spot in spots)
                    {
                        var stands = spot.Col;
                        var best = stands;
                        var bestRoom = Room(stands, spots, spot);
                        foreach (var col in ColumnsFor(spot))
                        {
                            if (col == stands) continue;
                            if (_cells[col, row] is not null && !ReferenceEquals(_cells[col, row], spot)) continue;
                            var room = Room(col, spots, spot);
                            if (room <= bestRoom) continue;
                            best = col;
                            bestRoom = room;
                        }
                        if (best == stands) continue;
                        _cells[stands, row] = null;
                        _cells[best, row] = spot;
                        spot.Col = best;
                        moved = true;
                    }
                }
                while (moved);
            }
        }

        private void MoveBackInLine()
        {
            for (var row = 0; row < _tall; row++)
            {
                for (var col = 0; col < Wide; col++)
                {
                    var spot = _cells[col, row];
                    if (spot is null || spot.Below.Count != 1 || spot.Above.Count != 1) continue;
                    var parent = spot.Below.First();
                    var child = spot.Above.First();
                    var leftOfBoth = spot.Col < child.Col && spot.Col < parent.Col;
                    var rightOfBoth = spot.Col > child.Col && spot.Col > parent.Col;
                    if (leftOfBoth && col < Wide - 1)
                    {
                        if (_cells[col + 1, row] is not null) continue;
                        spot.Col = col + 1;
                        _cells[col, row] = null;
                        _cells[col + 1, row] = spot;
                    }
                    if (rightOfBoth && col > 0 && _cells[col - 1, row] is null)
                    {
                        spot.Col = col - 1;
                        _cells[col, row] = null;
                        _cells[col - 1, row] = spot;
                    }
                }
            }
        }

        // ---------- the text ----------

        public string Describe()
        {
            var text = new StringBuilder();
            void Write(Spot spot)
            {
                text.Append(CultureInfo.InvariantCulture, $"{spot.Col},{spot.Row}:{spot.Kind}");
                if (spot.Fixed) text.Append('!');
                if (spot.Above.Count > 0) text.Append('>').AppendJoin(',', spot.Above.Select(child => child.Col));
                text.Append(' ');
            }
            foreach (var spot in Everything()) Write(spot);
            text.Append("| start ");
            Write(_first);
            text.Append("| boss ");
            Write(_last);
            text.Append("| paths from ").AppendJoin(' ', _pathStarts.Select(s => string.Create(CultureInfo.InvariantCulture, $"({s.Col},{s.Row})")));
            return text.ToString();
        }
    }
}
