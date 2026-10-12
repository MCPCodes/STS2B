using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// What the API fights of a results folder may cost and have cost so far (<c>--max-usd</c>),
/// shared by a batch's fights. Every response's cost is added the moment it comes back, so the
/// sum is the whole spend at any time, fights still running included. Safe from any thread.
/// <para>
/// The budget is the folder's, not one command's: every answered request is also written as one
/// line to <see cref="LedgerFile"/> in the results folder, and a later run of the command there
/// starts from what those lines add up to. So running the same command again never gets a fresh
/// budget, and what a batch spent is on disk even when it was interrupted or it crashed, to
/// within the one request that was in flight. Spending more in the same folder takes a higher
/// <c>--max-usd</c>, typed by hand. Requests against the scripted stand-in and requests against
/// the real API are kept apart (<c>api</c>: "stand-in" or "real"): neither counts against the other.
/// </para>
/// </summary>
public sealed class ApiBudget
{
    /// <summary>The ledger in a results folder: one line per answered request,
    /// <c>{at, usd, api, fight, model}</c>. Never holds a key or any text of a request.</summary>
    public const string LedgerFile = "api-spend.jsonl";

    private readonly object _gate = new();
    private readonly decimal _max, _earlier;
    private readonly string? _ledger;
    private readonly string _kind;
    private decimal _now, _dearest;
    private int _responses;

    /// <param name="maxUsd">The budget in dollars.</param>
    /// <param name="earlierUsd">What earlier runs in the same results folder spent (<see cref="SpentIn"/>).</param>
    /// <param name="ledgerPath">Where each response's cost is written, or null for nowhere.</param>
    /// <param name="standIn">True when the API is the scripted stand-in: its lines are marked so and never count as real.</param>
    public ApiBudget(decimal maxUsd, decimal earlierUsd = 0, string? ledgerPath = null, bool standIn = false)
    {
        _max = maxUsd;
        _earlier = Math.Max(0, earlierUsd);
        _ledger = ledgerPath;
        _kind = KindOf(standIn);
    }

    /// <summary>The budget in dollars.</summary>
    public decimal Max => _max;

    /// <summary>What earlier runs in the same results folder had spent when this one started.</summary>
    public decimal Earlier => _earlier;

    /// <summary>What this run's responses have cost so far, by the repo's price table (the brain's own count).</summary>
    public decimal SpentNow { get { lock (_gate) return _now; } }

    /// <summary>Everything that counts against the budget: the earlier runs' spend and this run's.</summary>
    public decimal Spent { get { lock (_gate) return _earlier + _now; } }

    /// <summary>What is left of the budget; never below zero.</summary>
    public decimal Left { get { lock (_gate) return Math.Max(0, _max - _earlier - _now); } }

    /// <summary>The cost of the dearest fight that has ended so far; 0 before the first.</summary>
    public decimal Dearest { get { lock (_gate) return _dearest; } }

    /// <summary>True once a response has come back in this run.</summary>
    public bool AnyResponse { get { lock (_gate) return _responses > 0; } }

    /// <summary>A problem writing the ledger, said once by the batch; null when every line was written.</summary>
    public string? LedgerProblem { get; private set; }

    /// <summary>One answered request cost <paramref name="usd"/>. Never throws: this runs between
    /// the API's answer and the brain, where an exception would look like a failed request and
    /// have it sent, and billed, again.</summary>
    public void Add(decimal usd, string? fight = null, string? model = null)
    {
        lock (_gate)
        {
            _now += usd;
            _responses++;
            if (_ledger is null)
                return;
            try
            {
                File.AppendAllText(_ledger, new JsonObject
                {
                    ["at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                    ["usd"] = usd,
                    ["api"] = _kind,
                    ["fight"] = fight,
                    ["model"] = model,
                }.ToJsonString() + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                LedgerProblem ??= $"{LedgerFile} couldn't be written ({ex.GetType().Name}: {ex.Message}), so a later run in this folder won't know what this one spent";
            }
        }
    }

    /// <summary>A fight ended, having cost <paramref name="usd"/> in all.</summary>
    public void FightEnded(decimal usd)
    {
        lock (_gate) _dearest = Math.Max(_dearest, usd);
    }

    /// <summary>
    /// Why no fight may start now with <paramref name="running"/> fights under way, or null when
    /// one may: the budget is used up, what is left is less than a fight's first request costs
    /// (<paramref name="firstRequestUsd"/>: the system prompt written to the cache, which a fight
    /// can't do without), or it is less than the dearest fight so far for each fight that would
    /// then be running. <paramref name="forGood"/> is true when waiting for the running fights
    /// can't help (none is running, or not even a first request fits).
    /// </summary>
    public string? WhyNotStart(int running, out bool forGood, decimal firstRequestUsd = 0)
    {
        lock (_gate)
        {
            var spent = _earlier + _now;
            var left = _max - spent;
            forGood = left <= 0 || left < firstRequestUsd || running == 0;
            if (left <= 0)
                return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"the budget is used up: ${spent:0.00} of ${_max:0.00} (--max-usd)") +
                       (_earlier > 0 ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $", ${_earlier:0.00} of it by earlier runs in this folder") : "");
            if (left < firstRequestUsd)
                return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"${left:0.00} is left of the budget of ${_max:0.00} (--max-usd), less than a fight's first request costs (about ${firstRequestUsd:0.00}: the system prompt written to the cache)");
            if (_dearest > 0 && left < _dearest * (running + 1))
                return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"${left:0.00} is left of the budget of ${_max:0.00} (--max-usd), less than the dearest fight so far cost (${_dearest:0.00})");
            return null;
        }
    }

    /// <summary>
    /// What the ledger at <paramref name="ledgerPath"/> says earlier runs spent against the same
    /// kind of API (the real one, or the stand-in). A missing file is 0. A line that can't be
    /// read is skipped; <paramref name="unreadable"/> counts them, so the batch can say that the
    /// sum may be short.
    /// </summary>
    public static decimal SpentIn(string ledgerPath, bool standIn, out int unreadable)
    {
        unreadable = 0;
        decimal sum = 0;
        if (!File.Exists(ledgerPath))
            return 0;
        var kind = KindOf(standIn);
        foreach (var line in File.ReadLines(ledgerPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                if (JsonNode.Parse(line) is JsonObject entry && entry["usd"] is JsonValue value && value.TryGetValue<decimal>(out var usd) && usd >= 0
                    && entry["api"] is JsonValue api && api.TryGetValue<string>(out var its))
                {
                    if (its == kind) sum += usd;
                }
                else
                {
                    unreadable++;
                }
            }
            catch (JsonException)
            {
                unreadable++;
            }
        }
        return sum;
    }

    private static string KindOf(bool standIn) => standIn ? "stand-in" : "real";
}

/// <summary>
/// Stands between the brain and the Messages API client for one fight and passes every request
/// and response through untouched, as the dev host's request cap does. It does two things the
/// arena needs and the brain doesn't do by itself:
/// <list type="bullet">
/// <item>It keeps count: each request's model, effort (the top-level one, and the one in effect
/// when an effort-only message changed it), max_tokens and thinking field, each response's
/// usage, cost and tool calls, what the notes tools were asked to read, and the brain's own
/// "Keep playing" messages. The result line is written from this and from the brain's own spend
/// tracker.</item>
/// <item>It stops the brain before a request that must not be sent: once the fight is over and
/// the requests allowed after it (<c>--after-fight</c>) are used up, and once the batch's
/// budget is reached. It stops it the way the game does (<see cref="ApiBrain.Abort"/>, the
/// request cancelled before it leaves), so the brain ends with its own clean stop and nothing
/// is billed.</item>
/// </list>
/// The brain makes one request at a time on its own task; the counts are read once it has stopped.
/// </summary>
public sealed class ApiRecorder : IClaudeApi
{
    /// <summary>Why the recorder stopped the brain: <see cref="FightOver"/> or <see cref="Budget"/>.</summary>
    public const string FightOver = "the fight was over", Budget = "the batch's budget was reached";

    private static readonly HashSet<string> GameTools = new(StringComparer.Ordinal) { "act", "act_sequence" };

    // The tools that move the game on, as ApiBrain counts them for its end-turn guard.
    private static readonly HashSet<string> ForwardTools = new(StringComparer.Ordinal) { "act", "act_sequence", "wait_for_decision", "party_pitch" };

    private readonly IClaudeApi _inner;
    private readonly Func<bool> _fightOver;
    private readonly ApiBudget? _budget;
    private readonly int _afterFight;
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Name, JsonObject Input)> _calls = new(StringComparer.Ordinal);
    private readonly HashSet<string> _counted = new(StringComparer.Ordinal);

    /// <param name="inner">The real client (<see cref="ClaudeClient"/>).</param>
    /// <param name="fightOver">True once the game says the fight is over.</param>
    /// <param name="afterFight">How many requests may still go out once it is.</param>
    /// <param name="budget">The batch's budget, or null for none.</param>
    public ApiRecorder(IClaudeApi inner, Func<bool> fightOver, int afterFight, ApiBudget? budget)
    {
        _inner = inner;
        _fightOver = fightOver;
        _afterFight = afterFight;
        _budget = budget;
    }

    /// <summary>Stops the brain (<see cref="ApiBrain.Abort"/>); set once the brain exists.</summary>
    public Action? Stop { get; set; }

    /// <summary>The fight's name for the budget's ledger (the scenario's id), or null.</summary>
    public string? Fight { get; init; }

    /// <summary>What went wrong while counting an answered request, each once; empty when nothing did.</summary>
    public List<string> CountingProblems { get; } = [];

    /// <summary>Why this recorder stopped the brain, or null when it didn't.</summary>
    public string? StoppedFor { get; private set; }

    /// <summary>Requests the API answered.</summary>
    public int Responses { get; private set; }

    /// <summary>Requests that failed in the client (after its own retries), as "RateLimited 429".</summary>
    public List<string> Failures { get; } = [];

    /// <summary>Answered requests by the effort in effect ("default" when none was sent).</summary>
    public Dictionary<string, int> Efforts { get; } = new(StringComparer.Ordinal);

    /// <summary>The same by the request's top-level <c>output_config.effort</c>.</summary>
    public Dictionary<string, int> TopLevelEfforts { get; } = new(StringComparer.Ordinal);

    /// <summary>Answered requests by their max_tokens.</summary>
    public Dictionary<string, int> MaxTokens { get; } = new(StringComparer.Ordinal);

    /// <summary>The efforts in effect, in the order they were first used.</summary>
    public List<string> EffortOrder { get; } = [];

    /// <summary>The model each request asked for, each once, in order.</summary>
    public List<string> ModelsAsked { get; } = [];

    /// <summary>The thinking field the requests carried, as JSON text, each different one once; "none" for a request without.</summary>
    public List<string> Thinking { get; } = [];

    /// <summary>Requests that carried the per-message effort beta.</summary>
    public int BetaRequests { get; private set; }

    /// <summary>Tokens and cost per model the API said it billed.</summary>
    public Dictionary<string, (TokenUsage Usage, decimal Usd, int Requests)> ByModel { get; } = new(StringComparer.Ordinal);

    public TokenUsage Usage { get; private set; } = TokenUsage.Zero;

    /// <summary>The responses' cost added up as the brain adds it (<see cref="ClaudeResponse.Cost"/>).</summary>
    public decimal Usd { get; private set; }

    /// <summary>Tool calls the model made, by tool name.</summary>
    public Dictionary<string, int> ToolCalls { get; } = new(StringComparer.Ordinal);

    /// <summary>Calls to act and act_sequence.</summary>
    public int GameCalls { get; private set; }

    /// <summary>Tool results that came back as errors, notes_read aside.</summary>
    public int ToolErrors { get; private set; }

    /// <summary>notes_read calls, the ones that failed, the wiki pages they opened (each once, in
    /// order) and every other file they asked for.</summary>
    public int ReadCalls { get; private set; }
    public int ReadErrors { get; private set; }
    public List<string> WikiPages { get; } = [];
    public List<string> OtherReads { get; } = [];

    /// <summary>Conversations the brain started (a request with one message), and the first
    /// line of each one's opening message.</summary>
    public List<string> Openings { get; } = [];

    /// <summary>The brain's own messages to a model that ended its turn mid-fight ("Keep playing ...").</summary>
    public int Nudges { get; private set; }

    /// <summary>Replies that hit max_tokens, and replies a safety classifier declined.</summary>
    public int CutOffs { get; private set; }
    public int Refusals { get; private set; }

    /// <summary>Replies in a row that ended the turn without a tool that moves the game on, as
    /// the brain counts them before it gives up on a model that won't play.</summary>
    public int EndTurnsInRow { get; private set; }

    /// <summary>Requests answered after the fight was over.</summary>
    public int AfterFightRequests { get; private set; }

    /// <summary>12 hex digits of the SHA-256 of the system text the brain sent, its length, and
    /// how many different system texts the fight's requests carried (1 unless the brain rebuilt it).</summary>
    public string? PromptHash { get; private set; }
    public int PromptChars { get; private set; }
    public int Prompts { get; private set; }

    /// <summary>The names of the tools the requests offered.</summary>
    public List<string> Tools { get; private set; } = [];

    /// <summary>The start of the model's last text.</summary>
    public string? LastText { get; private set; }

    public async Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // The two stops. Each cancels the brain's own token, so the request never leaves and the
        // brain ends as it does when the game stops it.
        var over = _fightOver();
        string? why = null;
        lock (_gate)
        {
            if (over && AfterFightRequests >= _afterFight) why = FightOver;
            else if (_budget is { } budget && budget.Spent >= budget.Max) why = Budget; // earlier runs in the folder count too
        }
        if (why is not null)
        {
            StoppedFor ??= why;
            Stop?.Invoke();
            ct.ThrowIfCancellationRequested();
            // No brain to stop (a recorder used by itself): refuse the request as the dev host's cap does.
            throw new ClaudeApiException(ClaudeErrorKind.Other, null, "arena_stop", $"Not sent: {why}.", null);
        }

        ClaudeResponse response;
        try
        {
            response = await _inner.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ClaudeApiException ex)
        {
            lock (_gate) Failures.Add(ex.Status is { } status ? $"{ex.Kind} {status}" : ex.Kind.ToString());
            throw;
        }
        catch (Exception ex)
        {
            lock (_gate) Failures.Add(ex.GetType().Name);
            throw;
        }

        lock (_gate)
        {
            Responses++;
            if (over) AfterFightRequests++;
            // The money first, and by itself: this response is billed, so its cost is in the
            // budget whatever the counting below does.
            var asked = Str(request["model"]) ?? "";
            decimal cost;
            try { cost = response.Cost(asked).Usd; }
            catch (Exception) { cost = 0; CountingProblem("a response's cost couldn't be worked out"); }
            Usd += cost;
            _budget?.Add(cost, Fight, response.Model ?? asked);
            // Nothing may be thrown from here: the brain takes an exception after the API's
            // answer for a request that failed, and an IOException would have it sent, and
            // billed, again.
            try
            {
                NoteRequest(request);
                NoteResponse(request, response, cost);
            }
            catch (Exception ex)
            {
                CountingProblem($"counting a request threw {ex.GetType().Name}: {ex.Message}");
            }
        }
        return response;
    }

    private void CountingProblem(string what)
    {
        if (!CountingProblems.Contains(what) && CountingProblems.Count < 5) CountingProblems.Add(what);
    }

    // What the answered request says about how the brain asked.
    private void NoteRequest(JsonObject request)
    {
        var model = Str(request["model"]) ?? "";
        if (!ModelsAsked.Contains(model)) ModelsAsked.Add(model);

        var top = Str(request["output_config"]?["effort"]);
        var messages = request["messages"] as JsonArray ?? [];
        // An effort-only system message changes the effort from there on; the last one rules.
        var effort = messages.OfType<JsonObject>()
            .Where(m => Str(m["role"]) == "system")
            .Select(m => Str(m["output_config"]?["effort"]))
            .LastOrDefault(e => e is not null) ?? top;
        Count(TopLevelEfforts, top ?? "default");
        Count(Efforts, effort ?? "default");
        if (!EffortOrder.Contains(effort ?? "default")) EffortOrder.Add(effort ?? "default");
        Count(MaxTokens, request["max_tokens"]?.ToJsonString() ?? "none");
        var thinking = request["thinking"]?.ToJsonString() ?? "none";
        if (!Thinking.Contains(thinking)) Thinking.Add(thinking);
        if (request[ClaudeClient.BetasField] is JsonArray) BetaRequests++;

        if (Str((request["system"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault()?["text"]) is { } system)
        {
            var hash = ArenaPrompt.Hash(system);
            if (hash != PromptHash)
            {
                PromptHash = hash;
                PromptChars = system.Length;
                Prompts++;
            }
        }
        if (Tools.Count == 0 && request["tools"] is JsonArray tools)
            Tools = tools.OfType<JsonObject>().Select(t => Str(t["name"])).OfType<string>().ToList();

        if (messages.Count == 1)
            Openings.Add(FirstLine(TextOf(messages[0])));

        // The newest user message: the results of the last reply's tool calls, and the brain's own words.
        if (messages.LastOrDefault() is not JsonObject last || Str(last["role"]) != "user" || last["content"] is not JsonArray blocks)
            return;
        foreach (var block in blocks.OfType<JsonObject>())
        {
            switch (Str(block["type"]))
            {
                case "text" when messages.Count > 1 && Str(block["text"]) is { } text
                                 && (text.StartsWith(ApiBrain.KeepPlaying, StringComparison.Ordinal) || text.StartsWith(ApiBrain.KeepPlayingWithState, StringComparison.Ordinal)):
                    Nudges++;
                    break;
                case "tool_result" when Str(block["tool_use_id"]) is { } id && _counted.Add(id) && _calls.TryGetValue(id, out var call):
                    NoteResult(call.Name, call.Input, block);
                    break;
            }
        }
    }

    // One tool result, by the call it answers.
    private void NoteResult(string name, JsonObject input, JsonObject block)
    {
        var failed = block["is_error"] is JsonValue flag && flag.TryGetValue<bool>(out var isError) && isError;
        if (name != "notes_read")
        {
            if (failed) ToolErrors++;
            return;
        }

        var file = Str(input["file"]) ?? "(no file)";
        var missing = false;
        if (!failed && Str(block["content"]) is { } content)
        {
            try { missing = JsonNode.Parse(content)?["exists"] is JsonValue exists && exists.TryGetValue<bool>(out var there) && !there; }
            catch (JsonException) { /* not JSON: counted as read */ }
        }
        if (failed) ReadErrors++;
        if (!failed && !missing && NotesTools.IsWikiPage(file))
        {
            if (!WikiPages.Contains(file)) WikiPages.Add(file);
            return;
        }
        var entry = failed ? $"{file} (failed)" : missing ? $"{file} (missing)" : file;
        if (!OtherReads.Contains(entry) && OtherReads.Count < 12) OtherReads.Add(entry);
    }

    private void NoteResponse(JsonObject request, ClaudeResponse response, decimal cost)
    {
        var asked = Str(request["model"]) ?? "";
        var usage = response.Usage;
        Usage += usage;
        var billed = response.Model ?? asked;
        var sofar = ByModel.GetValueOrDefault(billed, (TokenUsage.Zero, 0m, 0));
        ByModel[billed] = (sofar.Item1 + usage, sofar.Item2 + cost, sofar.Item3 + 1);

        var uses = response.ToolUses;
        foreach (var use in uses)
        {
            Count(ToolCalls, use.Name);
            if (GameTools.Contains(use.Name)) GameCalls++;
            if (use.Name == "notes_read") ReadCalls++;
            _calls[use.Id] = (use.Name, use.Input);
        }

        switch (response.StopReason)
        {
            case "max_tokens": CutOffs++; break;
            case "refusal": Refusals++; break;
        }
        if (uses.Any(use => ForwardTools.Contains(use.Name))) EndTurnsInRow = 0;
        else if (uses.Count == 0 && response.StopReason is not ("max_tokens" or "refusal" or "model_context_window_exceeded")) EndTurnsInRow++;

        if (response.Text.Trim() is { Length: > 0 } text)
            LastText = text.Length <= 400 ? text : text[..397] + "...";
    }

    private static void Count(Dictionary<string, int> counts, string key) => counts[key] = counts.GetValueOrDefault(key) + 1;

    // A message's text blocks, joined.
    private static string TextOf(JsonNode? message) =>
        message?["content"] is JsonArray blocks
            ? string.Join("\n", blocks.OfType<JsonObject>().Where(b => Str(b["type"]) == "text").Select(b => Str(b["text"]) ?? ""))
            : Str(message?["content"]) ?? "";

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 200 ? line : line[..197] + "...";
    }

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
