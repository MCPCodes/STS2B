using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireMind.Core.Brain;

/// <summary>
/// The mod's own record of what the API has cost: totals per UTC calendar month in
/// <c>spend.json</c> in the config folder, and the current run's total in memory. There is no
/// API to read the credit balance with a normal key, so this estimate is what the monthly cap
/// is checked against.
///
/// All members are thread-safe. Every <see cref="Add"/> writes the file atomically. Before
/// adding and before reporting a month, the file is read again and each month keeps the larger
/// of the two totals, so an edit that raises a total (or a second game on the same folder)
/// is never undercounted. A missing file starts from zero. A corrupt one is moved aside to
/// spend.json.bak and counting goes on from the totals in memory (zero at startup). A failed
/// write is logged and the totals stay in memory, so the cap still holds for this session.
///
/// In co-op every copy of the game counts into the same spend.json, so one monthly cap holds
/// for all of them. <see cref="Add"/>, <see cref="TryBegin"/> and <see cref="Cancel"/> read,
/// change and write the file under a lock every copy takes (<see cref="FileLock"/> on
/// locks/spend.json.lock), so two copies adding at once never lose a cost. Before a request,
/// <see cref="TryBegin"/> records an estimate of its cost under this copy's
/// <see cref="InstanceId"/> in the file's <c>in_flight</c> map, and counts the other copies'
/// estimates, and while there are any its own too, against the cap, so several copies can't
/// each start a request that together would go over it. A copy's first request, and a fresh
/// conversation's, is estimated at <see cref="FreshEstimateUsd"/> at least, since that one
/// writes the cache and costs the most. If the lock can't be had within
/// <see cref="LockTimeout"/>, counting goes on without it (logged once).
/// </summary>
public sealed class SpendTracker
{
    public const string FileName = "spend.json";

    /// <summary>The lock file's name, in the <c>locks</c> folder beside spend.json.</summary>
    public const string LockFileName = "spend.json.lock";

    /// <summary>An in-flight estimate older than this no longer counts (its copy crashed or hung).</summary>
    public static readonly TimeSpan InFlightLimit = TimeSpan.FromMinutes(10);

    /// <summary>The smallest estimate <see cref="TryBegin"/> records for a request.</summary>
    public const decimal MinEstimateUsd = 0.05m;

    /// <summary>The smallest estimate for a copy's first request since it started, and for the
    /// first request of a fresh conversation: a whole prompt written to the cache plus a long
    /// reply, the most a request usually costs.</summary>
    public const decimal FreshEstimateUsd = 0.50m;

    /// <summary>How long <see cref="Add"/>, <see cref="TryBegin"/> and <see cref="Cancel"/> wait
    /// for another copy to release the lock.</summary>
    public static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Dictionary<string, MonthSpend> _months = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InFlight> _others = new(StringComparer.Ordinal); // other copies' in-flight requests, as last read
    private readonly Action<string>? _log;
    private readonly List<string> _pendingLogs = [];
    private decimal _runTotal;
    private int _runCalls;
    private string? _lastError;
    private InFlight? _mine; // this copy's request in flight, between TryBegin and Add or Cancel
    private decimal _lastCost; // the last request's cost, the estimate for the next one
    private decimal _largestCost; // the most one request cost since this tracker was made
    private bool _hasCost; // a request's cost was added since this tracker was made
    private bool _lockWarned;

    private readonly record struct MonthSpend(decimal Usd, int Calls);

    private readonly record struct InFlight(decimal Usd, DateTime Since);

    private sealed record SpendFile(Dictionary<string, MonthSpend> Months, Dictionary<string, InFlight> InFlight);

    /// <param name="configDir">The folder holding spend.json; created on the first write. In
    /// co-op every copy passes the shared folder.</param>
    /// <param name="log">Receives one line per problem (corrupt file, failed write). Optional.</param>
    /// <param name="instanceId">Names this copy's in-flight request in spend.json ("main" for the
    /// human copy, "bot-17773" for a bot copy). Null or blank for "main".</param>
    public SpendTracker(string configDir, Action<string>? log = null, string? instanceId = null)
    {
        FilePath = Path.Combine(configDir, FileName);
        LockPath = Path.Combine(configDir, "locks", LockFileName);
        InstanceId = string.IsNullOrWhiteSpace(instanceId) ? "main" : instanceId.Trim();
        _log = log;
        lock (_gate) MergeFromDisk();
        FlushLogs();
    }

    public string FilePath { get; }

    /// <summary>The lock file every copy takes around a change to spend.json. Never deleted.</summary>
    public string LockPath { get; }

    /// <summary>This copy's name in spend.json's in_flight map.</summary>
    public string InstanceId { get; }

    /// <summary>The last problem reading or writing spend.json, or null when the last write worked.</summary>
    public string? LastError
    {
        get { lock (_gate) return _lastError; }
    }

    /// <summary>Dollars spent since the last <see cref="ResetRun"/> (or since the tracker was made).</summary>
    public decimal RunTotal
    {
        get { lock (_gate) return _runTotal; }
    }

    /// <summary>API calls since the last <see cref="ResetRun"/>.</summary>
    public int Calls
    {
        get { lock (_gate) return _runCalls; }
    }

    /// <summary>Records one API call that cost <paramref name="costUsd"/> at <paramref name="now"/>
    /// (UTC; a local time is converted) and saves spend.json, without this copy's in-flight
    /// estimate, which the cost replaces. Runs under the lock every copy takes.</summary>
    public void Add(decimal costUsd, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(costUsd);
        var month = MonthKey(now);
        var utc = Utc(now);
        lock (_gate)
        {
            UnderFileLock(() =>
            {
                MergeFromDisk();
                var current = _months.GetValueOrDefault(month);
                _months[month] = new MonthSpend(current.Usd + costUsd, current.Calls + 1);
                _runTotal += costUsd;
                _runCalls++;
                _lastCost = costUsd;
                _largestCost = Math.Max(_largestCost, costUsd);
                _hasCost = true;
                _mine = null;
                Save(utc);
            });
        }
        FlushLogs();
    }

    /// <summary>
    /// Call before each API request. False when this month's total has reached
    /// <paramref name="capUsd"/>, or, while other copies have requests in flight (estimates newer
    /// than <see cref="InFlightLimit"/>), when the total plus their estimates and this request's
    /// would reach it: the request must not be sent. Otherwise records this request's estimate in
    /// spend.json and returns true; <see cref="Add"/> or <see cref="Cancel"/> must follow. The
    /// estimate is the last request's cost, at least <see cref="MinEstimateUsd"/>; for this copy's
    /// first request and for <paramref name="freshConversation"/> it is the largest cost so far,
    /// at least <see cref="FreshEstimateUsd"/>. <paramref name="monthTotal"/> is this month's total
    /// either way. A copy alone sees no other estimates, so it is refused exactly when
    /// <see cref="OverCap"/> is true. Runs under the lock every copy takes.
    /// </summary>
    public bool TryBegin(decimal capUsd, DateTime now, out decimal monthTotal, bool freshConversation = false)
    {
        var month = MonthKey(now);
        var utc = Utc(now);
        var allowed = false;
        decimal total = 0;
        lock (_gate)
        {
            UnderFileLock(() =>
            {
                MergeFromDisk();
                total = _months.GetValueOrDefault(month).Usd;
                var live = _others.Values.Where(e => Live(e, utc)).ToArray();
                var estimate = freshConversation || !_hasCost
                    ? Math.Max(_largestCost, FreshEstimateUsd)
                    : Math.Max(_lastCost, MinEstimateUsd);
                // Alone, a copy may start a request below the cap as before; with others in
                // flight its own estimate counts too, so N copies can't each pass on the same
                // headroom.
                var needed = total + live.Sum(e => e.Usd) + (live.Length > 0 ? estimate : 0m);
                if (needed >= capUsd)
                {
                    if (_mine is not null)
                    {
                        _mine = null;
                        Save(utc);
                    }
                    return;
                }
                _mine = new InFlight(estimate, utc);
                allowed = true;
                Save(utc);
            });
        }
        FlushLogs();
        monthTotal = total;
        return allowed;
    }

    /// <summary>Removes this copy's in-flight estimate without a cost, for a request that failed
    /// before it was billed or was never sent. Does nothing when there is none.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            if (_mine is null)
                return;
            UnderFileLock(() =>
            {
                MergeFromDisk();
                _mine = null;
                Save(null);
            });
        }
        FlushLogs();
    }

    /// <summary>Dollars spent in the UTC calendar month containing <paramref name="now"/>.</summary>
    public decimal MonthTotal(DateTime now)
    {
        var month = MonthKey(now);
        decimal usd;
        lock (_gate)
        {
            MergeFromDisk();
            usd = _months.GetValueOrDefault(month).Usd;
        }
        FlushLogs();
        return usd;
    }

    /// <summary>API calls in the UTC calendar month containing <paramref name="now"/>.</summary>
    public int MonthCalls(DateTime now)
    {
        var month = MonthKey(now);
        int calls;
        lock (_gate)
        {
            MergeFromDisk();
            calls = _months.GetValueOrDefault(month).Calls;
        }
        FlushLogs();
        return calls;
    }

    /// <summary>True when this month's spend has reached <paramref name="capUsd"/>. A cap of zero
    /// or less is always reached.</summary>
    public bool OverCap(decimal capUsd, DateTime now) => MonthTotal(now) >= capUsd;

    /// <summary>Starts a new run: <see cref="RunTotal"/> and <see cref="Calls"/> go back to zero.
    /// Month totals are untouched.</summary>
    public void ResetRun()
    {
        lock (_gate)
        {
            _runTotal = 0;
            _runCalls = 0;
        }
    }

    /// <summary>The spend.json key for the UTC month of <paramref name="now"/>, e.g. "2026-10".
    /// An Unspecified time is taken as UTC.</summary>
    public static string MonthKey(DateTime now)
    {
        var utc = now.Kind == DateTimeKind.Local ? now.ToUniversalTime() : now;
        return utc.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }

    // The UTC time of now; an Unspecified time is taken as UTC.
    private static DateTime Utc(DateTime now) => now.Kind switch
    {
        DateTimeKind.Local => now.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(now, DateTimeKind.Utc),
        _ => now,
    };

    // An estimate still counts until InFlightLimit has passed (either way, so a bad clock
    // can't keep one forever).
    private static bool Live(InFlight entry, DateTime utcNow) => (utcNow - entry.Since).Duration() < InFlightLimit;

    // Caller holds _gate. Runs body under the lock every copy takes, or without it when the
    // lock can't be had. A timeout is logged once per tracker; another failure (the folder
    // can't be created) only when the write that followed worked, since otherwise the write's
    // own problem says enough.
    private void UnderFileLock(Action body)
    {
        IDisposable? held = null;
        string? problem = null;
        var timedOut = false;
        try
        {
            held = FileLock.Acquire(LockPath, LockTimeout);
        }
        catch (TimeoutException)
        {
            timedOut = true;
            problem = Invariant($"Couldn't lock {FileName} within {LockTimeout.TotalSeconds:0} s (another copy may be stuck); counting without the lock.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            problem = $"Couldn't lock {FileName} ({ex.GetType().Name}: {ex.Message}); counting without the lock.";
        }

        try
        {
            body();
        }
        finally
        {
            held?.Dispose();
        }

        if (problem is not null && !_lockWarned && (timedOut || _lastError is null))
        {
            _lockWarned = true;
            if (_log is not null) _pendingLogs.Add(problem);
        }
    }

    // Caller holds _gate. Reads spend.json and keeps the larger total of each month, and takes
    // the other copies' in-flight estimates from it. An estimate under this copy's own id is a
    // leftover of an earlier session (this one keeps its own in memory) and is dropped at the
    // next write.
    private void MergeFromDisk()
    {
        SpendFile? disk;
        try
        {
            if (!File.Exists(FilePath))
            {
                _others.Clear();
                return;
            }
            disk = Parse(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem($"Couldn't read {FileName} ({ex.GetType().Name}: {ex.Message}); using the totals in memory.");
            return;
        }

        if (disk is null)
        {
            _others.Clear();
            var backup = ConfigFiles.MoveAside(FilePath);
            Problem(backup is null
                ? $"{FileName} is corrupt and couldn't be moved aside; counting from the totals in memory."
                : $"{FileName} was corrupt; moved it to {Path.GetFileName(backup)} and counting from the totals in memory.");
            return;
        }

        foreach (var (month, spend) in disk.Months)
        {
            var mine = _months.GetValueOrDefault(month);
            _months[month] = new MonthSpend(Math.Max(mine.Usd, spend.Usd), Math.Max(mine.Calls, spend.Calls));
        }

        _others.Clear();
        foreach (var (id, entry) in disk.InFlight)
            if (id != InstanceId)
                _others[id] = entry;
    }

    // Null when the text isn't a spend file. A malformed in_flight map, or a malformed entry in
    // it, is dropped: those are only estimates, and the totals are what must not be lost.
    private static SpendFile? Parse(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root)
                return null;
            var result = new SpendFile(new Dictionary<string, MonthSpend>(StringComparer.Ordinal),
                new Dictionary<string, InFlight>(StringComparer.Ordinal));
            if (root["months"] is not null and not JsonObject)
                return null;
            if (root["months"] is JsonObject months)
            {
                foreach (var (month, node) in months)
                {
                    if (node is not JsonObject entry
                        || entry["usd"] is not JsonValue usdValue || !usdValue.TryGetValue<decimal>(out var usd) || usd < 0)
                        return null;
                    var calls = entry["calls"] is JsonValue c && c.TryGetValue<int>(out var n) ? Math.Max(0, n) : 0;
                    result.Months[month] = new MonthSpend(usd, calls);
                }
            }
            if (root["in_flight"] is JsonObject flights)
            {
                foreach (var (id, node) in flights)
                {
                    if (node is JsonObject entry
                        && entry["usd"] is JsonValue u && u.TryGetValue<decimal>(out var usd) && usd >= 0
                        && entry["since"] is JsonValue s && s.TryGetValue<string>(out var sinceText)
                        && DateTime.TryParse(sinceText, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var since))
                        result.InFlight[id] = new InFlight(usd, since);
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Caller holds _gate. Writes the months, and the in-flight estimates: the other copies'
    // (without those past InFlightLimit when now is given) and this copy's own. The in_flight
    // key is left out when there are none, so a copy alone writes the same file as ever.
    private void Save(DateTime? now)
    {
        var months = new JsonObject();
        foreach (var (month, spend) in _months.OrderBy(m => m.Key, StringComparer.Ordinal))
            months[month] = new JsonObject { ["usd"] = spend.Usd, ["calls"] = spend.Calls };
        var root = new JsonObject { ["months"] = months };

        if (now is { } utcNow)
            foreach (var id in _others.Where(e => !Live(e.Value, utcNow)).Select(e => e.Key).ToList())
                _others.Remove(id);
        var flights = new JsonObject();
        foreach (var (id, entry) in _others.OrderBy(e => e.Key, StringComparer.Ordinal))
            flights[id] = InFlightJson(entry);
        if (_mine is { } mine)
            flights[InstanceId] = InFlightJson(mine);
        if (flights.Count > 0)
            root["in_flight"] = flights;

        try
        {
            ConfigFiles.WriteJson(FilePath, root);
            _lastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem($"Couldn't save {FileName} ({ex.GetType().Name}: {ex.Message}); the totals are kept in memory.");
        }
    }

    private static JsonObject InFlightJson(InFlight entry) => new()
    {
        ["usd"] = entry.Usd,
        ["since"] = entry.Since.ToString("O", CultureInfo.InvariantCulture),
    };

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // Caller holds _gate. The line is logged by FlushLogs once the lock is released.
    private void Problem(string message)
    {
        _lastError = message;
        if (_log is not null) _pendingLogs.Add(message);
    }

    // Logs outside the lock, so a slow or reentrant logger can't hold up other threads.
    private void FlushLogs()
    {
        if (_log is null) return;
        string[] lines;
        lock (_gate)
        {
            if (_pendingLogs.Count == 0) return;
            lines = _pendingLogs.ToArray();
            _pendingLogs.Clear();
        }
        foreach (var line in lines) _log(line);
    }
}
