using System.Collections;
using System.Globalization;
using System.Text;

namespace SpireMind.Arena;

/// <summary>
/// The kind of a point on an act's map, with the game's member order: the pruning pass of the
/// generator writes these numbers into the keys it sorts by, so the order is part of the rules.
/// A point's kind is fixed when the map is made. It is not the room: an Unknown point's room is
/// rolled on entering (<see cref="RoomKind"/>).
/// </summary>
public enum MapPointType { Unassigned, Unknown, Shop, Treasure, RestSite, Monster, Elite, Boss, Ancient }

/// <summary>
/// A place on the map: column from 0 at the left, row from 0 at the bottom. Row 0 holds the
/// start point, the rows above it are the grid, and the boss sits one row above the grid.
/// Sorted as the game sorts coordinates: by column, then by row.
/// </summary>
public readonly record struct MapCoord(int Col, int Row) : IComparable<MapCoord>
{
    public int CompareTo(MapCoord other) => Col != other.Col ? Col.CompareTo(other.Col) : Row.CompareTo(other.Row);

    /// <summary>"(col,row)", as the mod's map screen writes a place.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({Col},{Row})");
}

/// <summary>
/// A set that lists its items the way the game's sets of map points do. The game keeps a
/// point's children and parents, and a map's first-row points, in hash sets, and the generator
/// walks them; the order of that walk decides which of two equal stretches of path survives.
/// A .NET hash set lists its items in the order they were added, an item that was taken out
/// leaves a free place, and the next item added takes the place freed last. None of that
/// depends on hash values, so this class keeps exactly that and nothing else: places in the
/// order of adding, and a stack of freed places.
/// </summary>
internal sealed class SlotSet<T> : IReadOnlyCollection<T> where T : class
{
    private readonly List<T?> _places = new();
    private readonly List<int> _freed = new();

    public SlotSet() { }

    private SlotSet(SlotSet<T> other, Func<T, T> copyOf)
    {
        foreach (var item in other._places) _places.Add(item is null ? null : copyOf(item));
        _freed.AddRange(other._freed);
    }

    public int Count => _places.Count - _freed.Count;

    public bool Contains(T item) => IndexOf(item) >= 0;

    /// <summary>Adds an item that is not in the set yet: into the place freed last, or at the end. False when it was there already.</summary>
    public bool Add(T item)
    {
        if (IndexOf(item) >= 0) return false;
        if (_freed.Count > 0)
        {
            var place = _freed[^1];
            _freed.RemoveAt(_freed.Count - 1);
            _places[place] = item;
        }
        else
        {
            _places.Add(item);
        }
        return true;
    }

    /// <summary>Takes an item out; its place stays, empty, until an item is added again.</summary>
    public bool Remove(T item)
    {
        var place = IndexOf(item);
        if (place < 0) return false;
        _places[place] = null;
        _freed.Add(place);
        return true;
    }

    private int IndexOf(T item)
    {
        for (var i = 0; i < _places.Count; i++)
            if (ReferenceEquals(_places[i], item)) return i;
        return -1;
    }

    /// <summary>The first item in listing order. The set must not be empty.</summary>
    public T First()
    {
        foreach (var item in _places)
            if (item is not null) return item;
        throw new InvalidOperationException("The set is empty.");
    }

    public IEnumerator<T> GetEnumerator()
    {
        // By index, so that a walk sees the set as it stands; callers that change the set while
        // they walk it take a copy first, as the game does.
        for (var i = 0; i < _places.Count; i++)
            if (_places[i] is { } item) yield return item;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>A copy with the same places, free ones included; <paramref name="copyOf"/> gives the copy's item for one of this set's.</summary>
    public SlotSet<T> Clone(Func<T, T> copyOf) => new(this, copyOf);
}

/// <summary>
/// One point of an act's map: where it is, what kind it is, and which points of the next row
/// can be reached from it. Children and parents are listed in the game's set order (see
/// <see cref="SlotSet{T}"/>); the mod's map screen sorts them by column itself.
/// </summary>
public sealed class MapPoint : IComparable<MapPoint>
{
    internal SlotSet<MapPoint> ChildSet { get; private set; } = new();
    internal SlotSet<MapPoint> ParentSet { get; private set; } = new();

    internal MapPoint(int col, int row) => Coord = new MapCoord(col, row);

    private MapPoint(MapPoint other, Dictionary<MapPoint, MapPoint> copies)
    {
        copies[other] = this;
        Coord = other.Coord;
        Type = other.Type;
        CanBeModified = other.CanBeModified;
    }

    /// <summary>The point's place. The column can still change while the map is being made (the three sideways passes); afterwards it is fixed.</summary>
    public MapCoord Coord { get; internal set; }

    public int Col => Coord.Col;
    public int Row => Coord.Row;

    public MapPointType Type { get; internal set; }

    /// <summary>False for the rows whose kind the generator fixes (the first row, the Treasure row, the last row):
    /// the repair pass never turns one of them into something else.</summary>
    public bool CanBeModified { get; internal set; } = true;

    /// <summary>The points of the next row this one leads to, in the game's set order.</summary>
    public IReadOnlyCollection<MapPoint> Children => ChildSet;

    /// <summary>The points of the row below that lead here, in the game's set order.</summary>
    public IReadOnlyCollection<MapPoint> Parents => ParentSet;

    internal void AddChild(MapPoint child)
    {
        ChildSet.Add(child);
        child.ParentSet.Add(this);
    }

    internal void RemoveChild(MapPoint child)
    {
        ChildSet.Remove(child);
        child.ParentSet.Remove(this);
    }

    /// <summary>By place: column, then row. The generator's sorted shuffle sorts points this way.</summary>
    public int CompareTo(MapPoint? other) => other is null ? 1 : Coord.CompareTo(other.Coord);

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Coord} {Type}");

    internal static MapPoint Copy(MapPoint point, Dictionary<MapPoint, MapPoint> copies) => new(point, copies);

    internal void CopyLinks(MapPoint from, Dictionary<MapPoint, MapPoint> copies)
    {
        // The same places in the same order, free places included.
        ChildSet = from.ChildSet.Clone(p => copies[p]);
        ParentSet = from.ParentSet.Clone(p => copies[p]);
    }
}

/// <summary>How many points of each placed kind a map was made for: the two drawn numbers and the two fixed ones.</summary>
/// <param name="Rests">Rest Sites to place below the last row (the last row is all Rest Sites on top of these).</param>
/// <param name="Shops">Always 3.</param>
/// <param name="Elites">5 at ascension 0.</param>
/// <param name="Unknowns">Unknown rooms to place.</param>
public readonly record struct MapCounts(int Rests, int Shops, int Elites, int Unknowns);

/// <summary>
/// The map of one act as the game generates it (<see cref="ActMapGenerator"/>): a grid seven
/// columns wide, the start point under it on row 0 and the boss above it. Once made it never
/// changes, so the copies of a run share one object.
/// <code>
/// var map = ActMapGenerator.Generate(world.Streams.ActMap(1), "Overgrowth");
/// map.Start.Children          // the first-row points the run can begin on
/// map.Point(3, 16)            // the boss of a 16-row act
/// </code>
/// </summary>
public sealed class ActMap
{
    /// <summary>Every map is seven columns wide.</summary>
    public const int Width = 7;

    private readonly MapPoint?[,] _grid;
    private readonly SlotSet<MapPoint> _startPoints;
    private readonly List<MapPoint> _points = new();

    internal ActMap(MapPoint?[,] grid, MapPoint start, MapPoint boss, SlotSet<MapPoint> startPoints, MapCounts counts, int draws)
    {
        _grid = grid;
        Start = start;
        Boss = boss;
        _startPoints = startPoints;
        Counts = counts;
        Draws = draws;
        for (var col = 0; col < grid.GetLength(0); col++)
            for (var row = 0; row < grid.GetLength(1); row++)
                if (grid[col, row] is { } point) _points.Add(point);
    }

    /// <summary>The grid itself and the set of first-row points, for the generator when it goes on with a map drawn by hand (checks).</summary>
    internal MapPoint?[,] Grid => _grid;

    internal SlotSet<MapPoint> StartSet => _startPoints;

    public int Columns => _grid.GetLength(0);

    /// <summary>
    /// The rows of the grid, row 0 included (the game's row count): 16 for a first act. The grid's
    /// points are on rows 1 to <c>Rows - 1</c>; the start point is on row 0 and the boss on row <c>Rows</c>,
    /// both outside the grid.
    /// </summary>
    public int Rows => _grid.GetLength(1);

    /// <summary>The start point (the act's Ancient: Neow's room in a first act), in the middle column on row 0. Its children are the first row.</summary>
    public MapPoint Start { get; }

    /// <summary>The boss, in the middle column one row above the grid. Every point of the last row leads to it.</summary>
    public MapPoint Boss { get; }

    /// <summary>The numbers of Rest Sites, shops, elites and Unknown rooms the map was made for. The finished map
    /// can hold fewer: what the pruning pass takes away is only partly put back.</summary>
    public MapCounts Counts { get; }

    /// <summary>How many numbers the map's stream gave to make it.</summary>
    public int Draws { get; }

    /// <summary>Every point of the grid in the game's order: column by column, each column from the bottom up.
    /// Neither the start point nor the boss is among them.</summary>
    public IReadOnlyList<MapPoint> Points => _points;

    /// <summary>The first-row points the seven paths started from, in the game's set order (a save's <c>start_coords</c>).</summary>
    public IReadOnlyCollection<MapPoint> StartPoints => _startPoints;

    /// <summary>The points of one grid row, by column. Empty for a row outside the grid: the boss's row has none,
    /// which is what the game's free travel meets on the last row.</summary>
    public IEnumerable<MapPoint> PointsInRow(int row)
    {
        if (row < 0 || row >= Rows) yield break;
        for (var col = 0; col < Columns; col++)
            if (_grid[col, row] is { } point) yield return point;
    }

    /// <summary>The point at a place: the boss, the start point or a point of the grid; null when there is none.</summary>
    public MapPoint? Point(int col, int row)
    {
        if (col == Boss.Col && row == Boss.Row) return Boss;
        if (col == Start.Col && row == Start.Row) return Start;
        if (col >= 0 && col < Columns && row >= 0 && row < Rows) return _grid[col, row];
        return null;
    }

    public MapPoint? Point(MapCoord coord) => Point(coord.Col, coord.Row);

    public bool HasPoint(MapCoord coord) => Point(coord) is not null;

    /// <summary>How many points of the grid are of a kind.</summary>
    public int Count(MapPointType type) => _points.Count(p => p.Type == type);

    /// <summary>A deep copy with the same points, links and set orders. Nothing needs one while a map never changes.</summary>
    public ActMap Clone()
    {
        var copies = new Dictionary<MapPoint, MapPoint>(ReferenceEqualityComparer.Instance);
        var all = _points.Append(Start).Append(Boss).ToList();
        foreach (var point in all) MapPoint.Copy(point, copies);
        foreach (var point in all) copies[point].CopyLinks(point, copies);
        var grid = new MapPoint?[Columns, Rows];
        foreach (var point in _points) grid[point.Col, point.Row] = copies[point];
        return new ActMap(grid, copies[Start], copies[Boss], _startPoints.Clone(p => copies[p]), Counts, Draws);
    }

    /// <summary>
    /// The whole map as one text, for comparing two maps and for a run's dump: every point in the
    /// game's order as <c>col,row:Type[!]&gt;child columns</c> (an exclamation mark for a point that
    /// cannot be modified, the children in set order), then the start point, the boss and the
    /// first-row points the paths started from.
    /// </summary>
    public string Describe()
    {
        var text = new StringBuilder();
        void Write(MapPoint point)
        {
            text.Append(CultureInfo.InvariantCulture, $"{point.Col},{point.Row}:{point.Type}");
            if (!point.CanBeModified) text.Append('!');
            if (point.Children.Count > 0) text.Append('>').AppendJoin(',', point.Children.Select(c => c.Col));
            text.Append(' ');
        }
        foreach (var point in _points) Write(point);
        text.Append("| start ");
        Write(Start);
        text.Append("| boss ");
        Write(Boss);
        text.Append("| paths from ").AppendJoin(' ', _startPoints.Select(p => p.Coord.ToString()));
        return text.ToString();
    }

    /// <summary>The map drawn row by row, the boss's row first, for a maintainer's eyes: two letters per kind and the children's columns.</summary>
    public string Draw()
    {
        var text = new StringBuilder();
        for (var row = Boss.Row; row >= 0; row--)
        {
            text.Append(CultureInfo.InvariantCulture, $"{row,2} ");
            for (var col = 0; col < Columns; col++)
            {
                var cell = Point(col, row) is { } point
                    ? point.Type.ToString()[..2] + (point.Children.Count > 0 ? "^" + string.Join(",", point.Children.Select(c => c.Col).OrderBy(c => c)) : "")
                    : ".";
                text.Append(cell.PadRight(10));
            }
            text.AppendLine();
        }
        return text.ToString();
    }
}
