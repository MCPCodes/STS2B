using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// What Claude Code says about the subscription's usage windows in a <c>rate_limit_event</c>
/// line: how full the 5 hour and the 7 day window are (0 to 1, more when usage ran past a
/// window), when they reset (Unix seconds), the status and the window it is about, and whether
/// requests are going to extra usage.
/// </summary>
public sealed record UsageWindows(double? FiveHour, double? SevenDay, long? FiveHourResetsAt, long? SevenDayResetsAt,
    string? Status, string? LimitType, bool UsingOverage)
{
    /// <summary>The status while requests are served and nothing is amiss.</summary>
    public const string Allowed = "allowed";

    /// <summary>
    /// Requests are still served, but Claude Code warns that a window is filling faster than it
    /// empties. Claude Code 2.1.296 works that out itself: 90% of the 5 hour window used in its
    /// first 72%, or 25, 50 or 75% of the week used in its first 15, 35 or 60%. So it says
    /// "ahead of pace", early in a week already at a quarter used, and not "the limit is hit".
    /// </summary>
    public const string Warning = "allowed_warning";

    /// <summary>True when the status says requests are refused: anything but
    /// <see cref="Allowed"/> and <see cref="Warning"/> ("rejected", or a word this build doesn't know).</summary>
    public bool Refused => Status is not (null or Allowed or Warning);
}

/// <summary>
/// What one fight's Claude Code sessions printed, read from their stream-json output line by
/// line: the init line (model, MCP server status, the tools on offer), the tools the model
/// called, the files it opened with Read (which wiki pages, and what came back as an error), the
/// result line (cost, turns, usage), the usage windows and everything that wasn't
/// JSON. A fight is one session, started again with <c>--resume</c> each time it ends before
/// the fight is over; <see cref="NextSession"/> marks each restart, the counters run on across
/// them and the result lines' numbers are put together into the fight's. Standard output and standard error arrive
/// on two tasks, so every member takes the lock.
/// <para>
/// The init line is also where a session is held to what the arena asked for: it must offer
/// the fight's own tools and Read, and nothing else. A session that lists any other tool is
/// stopped the next time the fight looks at it (<see cref="Failure"/>), so no fight is played by
/// a session that could write a file.
/// </para>
/// <para>
/// Nothing the model writes is ever searched for words about limits: in a card game "limit
/// reached" is about a hand. Only Claude Code's own lines say the subscription ran out: an
/// error result, or a <c>rate_limit_event</c> whose status says requests are refused.
/// </para>
/// </summary>
public sealed partial class StreamWatch
{
    /// <summary>The name of the fight's MCP server in the <c>--mcp-config</c> Claude Code gets.</summary>
    public const string McpServerName = "spiremind";

    /// <summary>What every tool of the fight is called in Claude Code: this and the tool's own name.</summary>
    public const string ToolPrefix = "mcp__" + McpServerName + "__";

    /// <summary>The sentence a session is stopped with when Claude Code says it is on extra usage.</summary>
    public const string OverageSentence =
        "Claude Code says it is on extra usage (isUsingOverage), beyond what the subscription covers, so the session was stopped.";

    private const int MaxNotes = 20;
    private const int NoteCut = 300;
    private const int MaxOtherReads = 12;
    private const int PathCut = 160;

    /// <summary>What <see cref="OtherReads"/> puts behind a path whose Read came back as an error.</summary>
    public const string FailedMark = " (failed)";

    // Claude Code's sentence for a server that throttles for a moment, which names the usage
    // limit only to say it isn't one.
    private const string NotTheLimit = "not your usage limit";

    private readonly object _gate = new();

    // The whole fight.
    private readonly List<string> _notes = [];
    private readonly Dictionary<string, int> _toolCalls = new(StringComparer.Ordinal);
    private int _notesDropped;
    private int _toolErrors;
    private int _gameToolCalls;
    private int _readCalls, _readErrors;
    private readonly List<string> _wikiPages = [];
    private readonly List<string> _otherReads = [];
    // Read calls whose result hasn't come yet, by tool_use id, with the file each asked for.
    private readonly Dictionary<string, string> _pendingReads = new(StringComparer.Ordinal);
    private long _lastOutputTicks = DateTime.UtcNow.Ticks;
    private string? _cliModel, _cliVersion, _apiKeySource, _mcpStatus;
    private string? _lastText, _lastError;
    private int _sessions;
    private UsageWindows? _windows;
    private string? _resultLimit, _eventLimit;
    private string? _toolsProblem;
    private bool _overage;
    private Totals _earlier = Totals.None;

    // The session running now (the last one, once the fight is done).
    private readonly Dictionary<string, TokenCounts> _messageUsage = new(StringComparer.Ordinal);
    private bool _initSeen;
    private string? _failure;
    private JsonObject? _result;
    private bool _resultIsError;
    private string? _resultText;

    /// <summary>Called with every <c>rate_limit_event</c>, from the task reading the session's
    /// output and outside the lock, so a batch hears of the usage windows while its fights run.</summary>
    public Action<UsageWindows>? OnUsage { get; init; }

    /// <summary>The strategy folder of the checkout the sessions may read
    /// (<see cref="ClaudeCli.StrategyFolder"/>), by the path the prompt names. A file read from
    /// under it is reported by its path in the checkout (<c>strategy/wiki/enemies/vantom.md</c>);
    /// null reports every file as it was asked for.</summary>
    public string? StrategyRoot { get; init; }

    /// <summary>When a session last printed a line (UTC), or when the watch was made.</summary>
    public DateTime LastOutputAt => new(Interlocked.Read(ref _lastOutputTicks), DateTimeKind.Utc);

    /// <summary>Set once the session can't or mustn't go on: its init line shows the fight's
    /// MCP server can't be reached or offers none of the fight's tools, or it is on extra
    /// usage. The session is stopped at once.</summary>
    public string? Failure { get { lock (_gate) return _failure; } }

    /// <summary>
    /// Set when a session's init line doesn't list the tools the arena asked for: none of the
    /// fight's, a tool besides them and Read, or no Read. That is a matter of the Claude Code
    /// that is installed and the flags it was given, not of the fight, so every session of a
    /// batch would end the same way, and a batch stops starting fights when it sees this.
    /// </summary>
    public string? ToolsProblem { get { lock (_gate) return _toolsProblem; } }

    /// <summary>The running session printed its init line.</summary>
    public bool InitSeen { get { lock (_gate) return _initSeen; } }

    /// <summary>How many of the fight's sessions printed an init line, so really started.</summary>
    public int Sessions { get { lock (_gate) return _sessions; } }

    /// <summary>The running session's last line on standard error, or null.</summary>
    public string? LastError { get { lock (_gate) return _lastError; } }

    public string? CliModel { get { lock (_gate) return _cliModel; } }
    public string? CliVersion { get { lock (_gate) return _cliVersion; } }
    public string? ApiKeySource { get { lock (_gate) return _apiKeySource; } }
    public string? McpStatus { get { lock (_gate) return _mcpStatus; } }

    /// <summary>True once the running session's result line arrived.</summary>
    public bool ResultSeen { get { lock (_gate) return _result is not null; } }

    /// <summary>The running session's result line is an error.</summary>
    public bool ResultIsError { get { lock (_gate) return _resultIsError; } }

    /// <summary>The result line's text when it is an error (the API's or Claude Code's sentence).</summary>
    public string? ResultError { get { lock (_gate) return _resultIsError ? _resultText : null; } }

    /// <summary>The subtype of the last result line.</summary>
    public string? ResultSubtype { get { lock (_gate) return FightResult.Str(_result?["subtype"]); } }

    /// <summary>The result lines' <c>duration_ms</c>, added up over the fight's sessions; null when none printed one.</summary>
    public long? DurationMs { get { lock (_gate) return All().DurationMs; } }

    /// <summary>The result lines' <c>num_turns</c>, added up.</summary>
    public int? NumTurns { get { lock (_gate) return All().Turns; } }

    /// <summary>The fight's <c>total_cost_usd</c> over all its sessions: the result lines' added
    /// up, or the last one's where a resumed session carries the earlier ones (<see cref="CarriesEarlier"/>).</summary>
    public decimal? TotalCostUsd { get { lock (_gate) return All().Cost; } }

    /// <summary>The fight's <c>modelUsage</c> per model id over all its sessions, put together
    /// the same way as <see cref="TotalCostUsd"/>, or null.</summary>
    public JsonObject? ModelUsage { get { lock (_gate) return All().ModelUsage?.DeepClone() as JsonObject; } }

    /// <summary>The start of the model's last text.</summary>
    public string? LastText { get { lock (_gate) return _lastText; } }

    public IReadOnlyDictionary<string, int> ToolCalls
    {
        get { lock (_gate) return new Dictionary<string, int>(_toolCalls, StringComparer.Ordinal); }
    }

    /// <summary>Calls to the fight's own tools, over all its sessions. None means the fight was
    /// never touched, whatever else was said.</summary>
    public int GameToolCalls { get { lock (_gate) return _gameToolCalls; } }

    /// <summary>Tool calls that came back as errors: an id that wasn't legal, a stale version,
    /// acting while the game was busy. The tools refuse those before the simulator sees them.
    /// A Read that failed is in <see cref="ReadErrors"/> instead.</summary>
    public int ToolErrors { get { lock (_gate) return _toolErrors; } }

    /// <summary>How often the model called Read, over all the fight's sessions.</summary>
    public int ReadCalls { get { lock (_gate) return _readCalls; } }

    /// <summary>Read calls that came back as an error: a file that isn't there, or one outside
    /// the strategy folder, which the session's permissions refuse.</summary>
    public int ReadErrors { get { lock (_gate) return _readErrors; } }

    /// <summary>The wiki pages Read gave the model, each once, in the order they were first
    /// opened, as the prompt's index names them (<c>strategy/wiki/enemies/vantom.md</c>).</summary>
    public IReadOnlyList<string> WikiPages { get { lock (_gate) return _wikiPages.ToArray(); } }

    /// <summary>
    /// Every other file the model asked Read for, each once: a file that isn't a wiki page
    /// (<c>strategy/wiki/README.md</c>, <c>strategy/general.md</c>, something outside the
    /// checkout as it was asked for), and a wiki page whose Read came back as an error, which
    /// has <see cref="FailedMark"/> behind it. At most 12 are kept.
    /// </summary>
    public IReadOnlyList<string> OtherReads { get { lock (_gate) return _otherReads.ToArray(); } }

    public IReadOnlyList<string> Notes
    {
        get
        {
            lock (_gate)
                return _notesDropped == 0 ? _notes.ToArray() : [.. _notes, $"(and {_notesDropped} more lines)"];
        }
    }

    /// <summary>The usage windows as the last <c>rate_limit_event</c> gave them, or null when
    /// there was none. A window an event leaves out keeps its last value.</summary>
    public UsageWindows? Windows { get { lock (_gate) return _windows; } }

    /// <summary>
    /// Claude Code's own word that the subscription's usage limit is hit, or null: the sentence
    /// of an error result that names it, extra usage, or the last <c>rate_limit_event</c> when
    /// its status says requests are refused (<see cref="UsageWindows.Refused"/>). Never anything
    /// the model wrote, and not the "allowed_warning" status, under which requests are served.
    /// </summary>
    public string? Limit { get { lock (_gate) return _resultLimit ?? (_overage ? OverageSentence : null) ?? _eventLimit; } }

    /// <summary>The token counts and where they are from: the result lines' <c>usage</c> added
    /// up ("result"); for a session that printed no result line its assistant messages' own
    /// usage instead (a lower bound: a message cut off mid-stream reports few output tokens),
    /// which makes it "messages", or "result+messages" when other sessions had one. Null when
    /// nothing was seen.</summary>
    public (TokenCounts Tokens, string Source)? Usage
    {
        get
        {
            lock (_gate)
            {
                var all = All();
                if (all.Tokens is null)
                    return null;
                return (all.Tokens, all.FromMessages == 0 ? "result" : all.FromResult == 0 ? "messages" : "result+messages");
            }
        }
    }

    /// <summary>A remark from the runner itself, kept with the sessions' own notes.</summary>
    public void Note(string text)
    {
        lock (_gate) AddNote(text);
    }

    /// <summary>
    /// The session ended before the fight was over and is started again: what its result line
    /// reported is added to the fight's totals, and the init line, the result and a failure are
    /// read anew from the next session. Tool calls, notes and the usage windows run on.
    /// </summary>
    public void NextSession()
    {
        lock (_gate)
        {
            _earlier = All();
            _messageUsage.Clear();
            _pendingReads.Clear(); // an answer to a call of the session that ended never comes
            _initSeen = false;
            _lastError = null;
            _failure = null;
            _result = null;
            _resultIsError = false;
            _resultText = null;
        }
    }

    /// <summary>One line of standard output: stream-json, one object per line; anything else is a note.</summary>
    public void OnOutput(string line)
    {
        var text = line.Trim();
        if (text.Length == 0)
            return;
        Interlocked.Exchange(ref _lastOutputTicks, DateTime.UtcNow.Ticks);

        JsonObject? json = null;
        if (text.StartsWith('{'))
        {
            try { json = JsonNode.Parse(text) as JsonObject; }
            catch (JsonException) { /* not JSON after all */ }
        }

        UsageWindows? windows = null;
        lock (_gate)
        {
            if (json is null)
            {
                AddNote(text);
                return;
            }
            switch (FightResult.Str(json["type"]))
            {
                case "system":
                    OnSystem(json);
                    break;
                case "assistant":
                    OnAssistant(json);
                    break;
                case "user":
                    // Of the tool results only the error flags are read; their text is game text
                    // or a page of the wiki.
                    if (json["message"] is JsonObject { } reply && reply["content"] is JsonArray results)
                        foreach (var block in results.OfType<JsonObject>())
                            if (FightResult.Str(block["type"]) == "tool_result")
                                OnToolResult(block);
                    break;
                case "result":
                    OnResult(json);
                    break;
                case "rate_limit_event":
                    windows = OnRateLimit(json);
                    break;
            }
        }
        if (windows is not null)
            OnUsage?.Invoke(windows);
    }

    /// <summary>One line of standard error: kept as a note, never read for meaning.</summary>
    public void OnError(string line)
    {
        var text = line.Trim();
        if (text.Length == 0)
            return;
        lock (_gate)
        {
            AddNote("stderr: " + text);
            _lastError = Cut(text, 200);
        }
    }

    // Caller holds _gate, as for everything below.
    private void OnSystem(JsonObject json)
    {
        if (FightResult.Str(json["subtype"]) != "init")
            return;
        _initSeen = true;
        _sessions++;
        _cliModel = FightResult.Str(json["model"]);
        _cliVersion = FightResult.Str(json["claude_code_version"]);
        _apiKeySource = FightResult.Str(json["apiKeySource"]);

        if (json["mcp_servers"] is not JsonArray servers)
        {
            // An init line without the list says nothing either way; the tools below still do.
            _mcpStatus = "unknown";
            AddNote("the init line lists no MCP servers");
        }
        else
        {
            var status = FightResult.Str(servers.OfType<JsonObject>().FirstOrDefault(s => FightResult.Str(s["name"]) == McpServerName)?["status"]);
            _mcpStatus = status ?? "missing";
            if (status is not ("connected" or "pending"))
            {
                _failure = $"Claude Code can't reach the fight: the {McpServerName} MCP server is {_mcpStatus}.";
                return;
            }
        }

        // The runner passes --tools Read and counts on Claude Code offering the MCP server's
        // tools directly. If none of them is on the list the model has nothing to play with, and
        // the session would only burn usage finding that out.
        if (json["tools"] is not JsonArray tools)
        {
            AddNote("the init line has no list of tools");
            return;
        }
        var names = tools.Select(FightResult.Str).OfType<string>().ToList();
        var version = _cliVersion ?? "of an unknown version";
        if (!names.Any(name => name.StartsWith(ToolPrefix, StringComparison.Ordinal)))
        {
            if (_mcpStatus != "pending")
            {
                var listed = names.Count == 0 ? "no tools at all" : $"only {Cut(string.Join(", ", names), 120)}";
                _failure = $"Claude Code offers the model none of the fight's tools: its init line lists {listed} and no {ToolPrefix}* tool " +
                           $"(MCP server {_mcpStatus}, Claude Code {version}). The arena starts it with --tools {ClaudeCli.ReadTool} and " +
                           "counts on the MCP tools being offered directly, so this version may need another way to pass them.";
                _toolsProblem = _failure;
                return;
            }
            // Still connecting when the session started: its tools can yet arrive, so the fight
            // goes on. If they never do, the fight ends as an error for having made no call.
            AddNote($"the {McpServerName} MCP server was still pending when the session started, with none of its tools listed yet");
        }

        // A fight's session gets the fight's tools and Read. Anything else on the list is a
        // tool the arena didn't ask for (one that edits or writes, at worst), and a session
        // without Read can't open the wiki its prompt tells it about. Either way the fight
        // wouldn't be the one the arena describes, so it ends here, at the session's first line.
        var others = names.Where(name => name != ClaudeCli.ReadTool && !name.StartsWith(ToolPrefix, StringComparison.Ordinal)).ToList();
        if (others.Count > 0)
            _failure = $"Claude Code offers the model tools the arena didn't ask for: {Cut(string.Join(", ", others), 160)} " +
                       $"(Claude Code {version}). A fight's session gets the fight's own tools and {ClaudeCli.ReadTool} and nothing that " +
                       "could write a file, so the session was stopped at once.";
        else if (!names.Contains(ClaudeCli.ReadTool))
            _failure = $"Claude Code doesn't offer the model the {ClaudeCli.ReadTool} tool (Claude Code {version}): its init line lists " +
                       $"{(names.Count == 0 ? "no tools at all" : Cut(string.Join(", ", names), 120))}. The prompt tells the model to read the wiki's pages with it, " +
                       "so the session was stopped rather than played under a prompt that doesn't hold.";
        if (others.Count > 0 || !names.Contains(ClaudeCli.ReadTool))
            _toolsProblem = _failure;
    }

    private void OnAssistant(JsonObject json)
    {
        if (json["message"] is not JsonObject message)
            return;
        if (FightResult.Str(message["id"]) is { } id && message["usage"] is JsonObject usage && Counts(usage) is { } counts)
            _messageUsage[id] = counts; // one line per content block, each with the message's usage: the last one stands

        if (message["content"] is not JsonArray content)
            return;
        foreach (var block in content.OfType<JsonObject>())
        {
            switch (FightResult.Str(block["type"]))
            {
                case "text":
                    if (FightResult.Str(block["text"]) is { } text && text.Trim().Length > 0)
                        _lastText = Cut(text, NoteCut);
                    break;
                case "tool_use":
                    var name = FightResult.Str(block["name"]) ?? "?";
                    if (name.StartsWith(ToolPrefix, StringComparison.Ordinal))
                    {
                        name = name[ToolPrefix.Length..];
                        _gameToolCalls++;
                    }
                    else if (name == ClaudeCli.ReadTool)
                    {
                        // What it opened is only known once the result says whether it did.
                        _readCalls++;
                        if (FightResult.Str(block["id"]) is { } call)
                            _pendingReads[call] = FightResult.Str((block["input"] as JsonObject)?["file_path"]) ?? "";
                    }
                    _toolCalls[name] = _toolCalls.GetValueOrDefault(name) + 1;
                    break;
            }
        }
    }

    // One tool result: the answer to a Read says whether the file was opened, and which file
    // that was is remembered from the call; of every other tool only the error flag counts.
    private void OnToolResult(JsonObject block)
    {
        var failed = block["is_error"] is JsonValue flag && flag.TryGetValue<bool>(out var error) && error;
        if (FightResult.Str(block["tool_use_id"]) is not { } call || !_pendingReads.Remove(call, out var asked))
        {
            if (failed) _toolErrors++;
            return;
        }

        if (failed) _readErrors++;
        var (file, isPage) = InCheckout(asked);
        if (isPage && !failed)
        {
            if (!_wikiPages.Contains(file)) _wikiPages.Add(file);
            return;
        }
        var entry = Cut(file.Length > 0 ? file : "(no path)", PathCut) + (failed ? FailedMark : "");
        if (!_otherReads.Contains(entry) && _otherReads.Count < MaxOtherReads)
            _otherReads.Add(entry);
    }

    // A file the model asked for, by its path in the checkout when it lies under the strategy
    // folder ("strategy/wiki/enemies/vantom.md"), else as it was asked for; and whether that is
    // one of the wiki's pages, as the prompt's index and the notes tools define them.
    private (string File, bool IsPage) InCheckout(string asked)
    {
        if (StrategyRoot is not { } root || !Path.IsPathFullyQualified(asked))
            return (asked, false);
        string full;
        try { full = Path.GetFullPath(asked); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return (asked, false); }

        var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return (asked, false);
        var file = "strategy/" + full[(folder.Length + 1)..].Replace(Path.DirectorySeparatorChar, '/');
        return (file, NotesTools.IsWikiPage(file));
    }

    private void OnResult(JsonObject json)
    {
        _result = json;
        _resultIsError = json["is_error"] is JsonValue e && e.TryGetValue<bool>(out var isError) && isError;
        var text = FightResult.Str(json["result"]) ?? "";
        if (json["errors"] is JsonArray errors)
            text = string.Join(" ", new[] { text }.Concat(errors.Select(FightResult.Str).OfType<string>())).Trim();
        _resultText = text.Length > 0 ? Cut(text, NoteCut) : null;
        // Only an error result is Claude Code's own sentence. One that isn't an error repeats
        // the model's last message, where "limit reached" can be about the hand.
        if (_resultIsError && LimitSentence(text) is { } sentence)
            _resultLimit = sentence;
    }

    // {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1791651600,"rateLimitType":"five_hour",
    //  "overageStatus":"rejected","isUsingOverage":false,"unifiedWindows":{"five_hour":{"utilization":0.02,"resetsAt":1791651600},
    //  "seven_day":{"utilization":0.42,"resetsAt":1791748800}}}}, as Claude Code 2.1.296 prints it.
    private UsageWindows? OnRateLimit(JsonObject json)
    {
        if (json["rate_limit_info"] is not JsonObject info)
            return null;
        var status = FightResult.Str(info["status"]);
        var type = FightResult.Str(info["rateLimitType"]);
        // isUsingOverage: the window is used up and extra usage takes the requests;
        // overageInUse: the server's own flag for the same.
        var overage = IsTrue(info["isUsingOverage"]) || IsTrue(info["overageInUse"]);
        var windows = info["unifiedWindows"] as JsonObject;
        var five = windows?["five_hour"] as JsonObject;
        var seven = windows?["seven_day"] as JsonObject;

        _windows = new UsageWindows(
            FightResult.Number(five?["utilization"]) ?? _windows?.FiveHour,
            FightResult.Number(seven?["utilization"]) ?? _windows?.SevenDay,
            FightResult.Long(five?["resetsAt"]) ?? _windows?.FiveHourResetsAt,
            FightResult.Long(seven?["resetsAt"]) ?? _windows?.SevenDayResetsAt,
            status, type, overage);

        // The last event stands: a window that reset while the fight ran is allowed again.
        _eventLimit = _windows.Refused ? Describe(status!, type, info, windows) : null;
        if (overage)
        {
            _overage = true;
            _failure ??= OverageSentence;
        }
        return _windows;

        static bool IsTrue(JsonNode? node) => node is JsonValue flag && flag.TryGetValue<bool>(out var yes) && yes;
    }

    // "Claude Code reports the five_hour limit as rejected (window 100% used, resets 2026-10-10 15:00 UTC)".
    private static string Describe(string status, string? type, JsonObject info, JsonObject? windows)
    {
        var details = new List<string>();
        var used = FightResult.Number(info["utilization"])
                   ?? (type is not null ? FightResult.Number((windows?[type] as JsonObject)?["utilization"]) : null);
        if (used is { } fraction)
            details.Add($"window {Stats.Percent(fraction)} used");
        if (FightResult.Long(info["resetsAt"]) is { } resets and > 0 and < 100_000_000_000)
            details.Add("resets " + DateTimeOffset.FromUnixTimeSeconds(resets).UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        return $"Claude Code reports the {type ?? "usage"} limit as {status}" + (details.Count > 0 ? $" ({string.Join(", ", details)})" : "");
    }

    // The line of an error result that names the usage limit, or null.
    private static string? LimitSentence(string text)
    {
        if (text.Contains(NotTheLimit, StringComparison.OrdinalIgnoreCase))
            return null;
        var match = LimitPattern().Match(text);
        if (!match.Success)
            return null;
        var start = match.Index == 0 ? 0 : text.LastIndexOf('\n', match.Index - 1) + 1;
        var end = text.IndexOf('\n', match.Index + match.Length);
        return Cut(text[start..(end < 0 ? text.Length : end)], 200);
    }

    // A line a resumed session prints again (a notice about the model, say) is kept once.
    private void AddNote(string text)
    {
        var note = Cut(text, NoteCut);
        if (_notes.Contains(note)) return;
        if (_notes.Count < MaxNotes) _notes.Add(note);
        else _notesDropped++;
    }

    // The fight's totals so far: the sessions before this one and what this one has reported.
    private Totals All()
    {
        var all = _earlier;
        if (_result is { } result)
        {
            decimal? cost = FightResult.Number(result["total_cost_usd"]) is { } usd ? (decimal)usd : null;
            var models = result["modelUsage"] as JsonObject;
            // num_turns, duration_ms and usage are the process's own and are added up. The cost
            // and modelUsage are running totals that a resumed session takes up again, so its
            // result line then stands for the sessions before it too.
            var carried = CarriesEarlier(all, cost, models, result["usage"] as JsonObject);
            all = all with
            {
                Cost = carried ? cost : Sum(all.Cost, cost),
                Turns = Sum(all.Turns, (int?)FightResult.Long(result["num_turns"])),
                DurationMs = Sum(all.DurationMs, FightResult.Long(result["duration_ms"])),
                ModelUsage = carried ? models!.DeepClone() as JsonObject : Merge(all.ModelUsage, models),
            };
        }

        if (_result?["usage"] is JsonObject usage && Counts(usage) is { } fromResult)
        {
            all = all with { Tokens = Add(all.Tokens, fromResult), FromResult = all.FromResult + 1 };
        }
        else if (_messageUsage.Count > 0)
        {
            var sum = new TokenCounts(0, 0, 0, 0);
            foreach (var u in _messageUsage.Values)
                sum = Add(sum, u);
            all = all with { Tokens = Add(all.Tokens, sum), FromMessages = all.FromMessages + 1 };
        }
        return all;
    }

    /// <summary>
    /// Whether a resumed session's result line already holds the cost and modelUsage of the
    /// sessions before it. Claude Code 2.1.296 describes both as running totals that a resumed
    /// session takes up again from what its transcript saved, when it saved any, so that its
    /// first result line already includes the earlier sessions. Adding such a line to the earlier
    /// ones would count those twice. It is taken as carrying them when its cost is no lower than the
    /// total so far and its modelUsage grew by about the output tokens of its own <c>usage</c>
    /// or more (nine tenths, so a small difference between the two counts can't flip it);
    /// <c>usage</c> is the process's own. A session that started from nothing reports about as
    /// many output tokens in both, so its modelUsage didn't grow by that much and it is added.
    /// </summary>
    private static bool CarriesEarlier(Totals earlier, decimal? cost, JsonObject? models, JsonObject? usage)
    {
        if (earlier.Cost is not { } before || cost is not { } now || earlier.ModelUsage is null || models is null)
            return false;
        var grew = OutputTokens(models) - OutputTokens(earlier.ModelUsage);
        var own = usage is null ? 0 : FightResult.Long(usage["output_tokens"]) ?? 0;
        return now >= before && grew >= Math.Max(own - own / 10, 1);

        static long OutputTokens(JsonObject perModel) =>
            perModel.Sum(entry => entry.Value is JsonObject model ? FightResult.Long(model["outputTokens"]) ?? 0 : 0);
    }

    private static decimal? Sum(decimal? a, decimal? b) => a is null ? b : b is null ? a : a + b;

    private static int? Sum(int? a, int? b) => a is null ? b : b is null ? a : a + b;

    private static long? Sum(long? a, long? b) => a is null ? b : b is null ? a : a + b;

    private static TokenCounts Add(TokenCounts? a, TokenCounts b) =>
        a is null ? b : new TokenCounts(a.Input + b.Input, a.CacheCreation + b.CacheCreation, a.CacheRead + b.CacheRead, a.Output + b.Output);

    // Two sessions' modelUsage as one: per model id the token counts, requests and cost are
    // added; what describes the model (its context window, its names) is taken from the later one.
    private static JsonObject? Merge(JsonObject? earlier, JsonObject? later)
    {
        if (later is null) return earlier;
        if (earlier is null) return later.DeepClone() as JsonObject;
        var merged = (JsonObject)earlier.DeepClone();
        foreach (var (id, entry) in later)
        {
            if (entry is not JsonObject now || merged[id] is not JsonObject before)
            {
                merged[id] = entry?.DeepClone();
                continue;
            }
            foreach (var (key, value) in now)
            {
                var adds = key == "costUSD" || key.EndsWith("Requests", StringComparison.Ordinal)
                           || (key.EndsWith("Tokens", StringComparison.Ordinal) && !key.StartsWith("max", StringComparison.Ordinal));
                if (adds && FightResult.Number(value) is { } b && FightResult.Number(before[key]) is { } a)
                    before[key] = key == "costUSD" ? JsonValue.Create(a + b) : JsonValue.Create((long)Math.Round(a + b));
                else
                    before[key] = value?.DeepClone();
            }
        }
        return merged;
    }

    private static TokenCounts? Counts(JsonObject usage) =>
        FightResult.Long(usage["output_tokens"]) is { } output
            ? new TokenCounts(
                FightResult.Long(usage["input_tokens"]) ?? 0,
                FightResult.Long(usage["cache_creation_input_tokens"]) ?? 0,
                FightResult.Long(usage["cache_read_input_tokens"]) ?? 0,
                output)
            : null;

    /// <summary>One line, at most <paramref name="max"/> characters.</summary>
    public static string Cut(string text, int max)
    {
        var flat = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (flat.Length <= max)
            return flat;
        var end = max - 3;
        if (char.IsHighSurrogate(flat[end - 1]))
            end--;
        return flat[..end] + "...";
    }

    /// <summary>What the result lines of a fight's sessions add up to, and how many sessions'
    /// token counts came from a result line or, for want of one, from their messages.</summary>
    private sealed record Totals(decimal? Cost, int? Turns, long? DurationMs, TokenCounts? Tokens, int FromResult, int FromMessages, JsonObject? ModelUsage)
    {
        public static readonly Totals None = new(null, null, null, null, 0, 0, null);
    }

    // How Claude Code says the subscription ran out, looked for in error results only; the same
    // pattern as SubscriptionBrain's, which has the wording seen on Claude Code 2.1.287
    // ("You've hit your session limit · resets 3pm").
    [GeneratedRegex(@"usage limit|limit reached|rate[ _-]?limit|out of (?:extra usage|usage credits)|(?:hit|reached) your (?:[\w'’-]+ ){0,3}limit",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LimitPattern();
}
