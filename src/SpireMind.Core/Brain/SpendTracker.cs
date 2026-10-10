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
/// </summary>
public sealed class SpendTracker
{
    public const string FileName = "spend.json";

    private readonly object _gate = new();
    private readonly Dictionary<string, MonthSpend> _months = new(StringComparer.Ordinal);
    private readonly Action<string>? _log;
    private readonly List<string> _pendingLogs = [];
    private decimal _runTotal;
    private int _runCalls;
    private string? _lastError;

    private readonly record struct MonthSpend(decimal Usd, int Calls);

    /// <param name="configDir">The folder holding spend.json; created on the first write.</param>
    /// <param name="log">Receives one line per problem (corrupt file, failed write). Optional.</param>
    public SpendTracker(string configDir, Action<string>? log = null)
    {
        FilePath = Path.Combine(configDir, FileName);
        _log = log;
        lock (_gate) MergeFromDisk();
        FlushLogs();
    }

    public string FilePath { get; }

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
    /// (UTC; a local time is converted) and saves spend.json.</summary>
    public void Add(decimal costUsd, DateTime now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(costUsd);
        var month = MonthKey(now);
        lock (_gate)
        {
            MergeFromDisk();
            var current = _months.GetValueOrDefault(month);
            _months[month] = new MonthSpend(current.Usd + costUsd, current.Calls + 1);
            _runTotal += costUsd;
            _runCalls++;
            Save();
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

    // Caller holds _gate. Reads spend.json and keeps the larger total of each month.
    private void MergeFromDisk()
    {
        Dictionary<string, MonthSpend>? disk;
        try
        {
            if (!File.Exists(FilePath))
                return;
            disk = Parse(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem($"Couldn't read {FileName} ({ex.GetType().Name}: {ex.Message}); using the totals in memory.");
            return;
        }

        if (disk is null)
        {
            var backup = ConfigFiles.MoveAside(FilePath);
            Problem(backup is null
                ? $"{FileName} is corrupt and couldn't be moved aside; counting from the totals in memory."
                : $"{FileName} was corrupt; moved it to {Path.GetFileName(backup)} and counting from the totals in memory.");
            return;
        }

        foreach (var (month, spend) in disk)
        {
            var mine = _months.GetValueOrDefault(month);
            _months[month] = new MonthSpend(Math.Max(mine.Usd, spend.Usd), Math.Max(mine.Calls, spend.Calls));
        }
    }

    // Null when the text isn't a spend file.
    private static Dictionary<string, MonthSpend>? Parse(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root)
                return null;
            var result = new Dictionary<string, MonthSpend>(StringComparer.Ordinal);
            if (root["months"] is null)
                return result;
            if (root["months"] is not JsonObject months)
                return null;
            foreach (var (month, node) in months)
            {
                if (node is not JsonObject entry
                    || entry["usd"] is not JsonValue usdValue || !usdValue.TryGetValue<decimal>(out var usd) || usd < 0)
                    return null;
                var calls = entry["calls"] is JsonValue c && c.TryGetValue<int>(out var n) ? Math.Max(0, n) : 0;
                result[month] = new MonthSpend(usd, calls);
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Caller holds _gate.
    private void Save()
    {
        var months = new JsonObject();
        foreach (var (month, spend) in _months.OrderBy(m => m.Key, StringComparer.Ordinal))
            months[month] = new JsonObject { ["usd"] = spend.Usd, ["calls"] = spend.Calls };
        try
        {
            ConfigFiles.WriteJson(FilePath, new JsonObject { ["months"] = months });
            _lastError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Problem($"Couldn't save {FileName} ({ex.GetType().Name}: {ex.Message}); the totals are kept in memory.");
        }
    }

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
