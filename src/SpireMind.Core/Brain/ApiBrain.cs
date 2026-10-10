using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireMind.Core.Brain;

/// <summary>Why <see cref="ApiBrain"/> stopped. The first four tell the caller to fall back to
/// the subscription; see docs/brain.md.</summary>
public enum BrainStopReason
{
    /// <summary>There is no API key. Never raised by <see cref="ApiBrain"/> itself: the caller
    /// uses it when there is no key to start with, so one switch covers every fallback.</summary>
    NoKey,

    /// <summary>The key was rejected (401).</summary>
    Unauthorized,

    /// <summary>The API credit balance ran out.</summary>
    OutOfCredit,

    /// <summary>The spend limit set in the Claude Console was reached.</summary>
    SpendLimit,

    /// <summary>The mod's own monthly cap (<c>monthly_budget_usd</c>) was reached.</summary>
    MonthlyCap,

    /// <summary><see cref="ApiBrain.StopAsync"/> was called.</summary>
    UserStop,

    /// <summary>Something the brain can't recover from: a refusal, a request the API rejects,
    /// replies that keep hitting max_tokens, a model that won't play, another client playing
    /// through the mod, a bug.</summary>
    Error,
}

/// <summary>
/// The in-game brain: plays through the game tools by calling the Messages API directly, on a
/// background task. Each response's tool calls run in-process and their results go back as
/// the next request, until the brain is stopped or something it can't recover from happens.
/// <list type="bullet">
/// <item>No idle spending: the model's <c>wait_for_decision</c> is repeated locally until there
/// is a decision or a chat message, and outside a run the brain polls the game without calling
/// the API.</item>
/// <item>The system prompt and tools stay byte-identical so their cache holds; the system text
/// is rebuilt only when a new run starts or the character (or co-op) changes.</item>
/// <item>Long conversations are replaced by a fresh one that holds the run journal, the recent
/// chat and the current state; so is the conversation after a model switch or a new run.</item>
/// <item>Spend is added up from each response's usage and checked against the monthly cap
/// before every request.</item>
/// </list>
/// Start, stop and the status properties are safe to call from any thread; everything else
/// runs on the loop's task. See docs/brain.md.
/// </summary>
public sealed class ApiBrain : IPlayBrain
{
    /// <summary>The reply when the model ends its turn during a run.</summary>
    public const string KeepPlaying = "Keep playing: call wait_for_decision.";

    /// <summary>Starts the reply when the brain waited for the game first; the game's state follows.</summary>
    public const string KeepPlayingWithState = "Keep playing. This is the game now, as wait_for_decision would return it:";

    /// <summary>The reply after a response hit max_tokens.</summary>
    public const string CutOff = "Your reply hit the output limit (max_tokens) and was cut off. Keep your thinking and notes shorter, then continue.";

    /// <summary>Opens a conversation that replaces a long one.</summary>
    public const string Refreshed = "Context refreshed to save tokens.";

    /// <summary>Opens the first conversation after <see cref="Start"/>.</summary>
    public const string Starting = "You have control of the game now.";

    /// <summary>Opens a conversation after a new run started (or the character or co-op changed),
    /// with the notes reloaded so lessons written at the end of the last run are in.</summary>
    public const string NewRun = "A new run started (or the character or co-op changed), so the strategy notes in the system prompt were reloaded and the conversation starts over.";

    /// <summary>Opens a conversation after /model or /effort changed the settings. The cache belongs to
    /// a model, thinking blocks to the model that wrote them, and a new effort invalidates the cached
    /// messages, so a short start is cheaper than the old history.</summary>
    public const string ModelChanged = "The model or effort changed, so the conversation starts over.";

    /// <summary>Ends every opening message.</summary>
    public const string CarryOn = "Carry on from here.";

    /// <summary>Said once in the game chat when the API can't be reached and the brain keeps retrying.</summary>
    public const string Retrying = "Can't reach Claude right now, retrying.";

    /// <summary>The stop line when another client's wait_for_decision replaced the brain's.</summary>
    public const string AnotherClient = "Another client is playing through the mod (Claude Code's /play?), so the API brain stopped.";

    private static readonly AsyncLocal<ApiBrain?> CurrentLoop = new();

    // Model-facing JSON (tool results, snapshots in text blocks). The default encoder writes
    // '>', '+', quotes and every non-ASCII character as \uXXXX, which the model reads and is
    // billed for literally. This text never goes into HTML.
    private static readonly JsonSerializerOptions ModelJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Screens that only exist during a run. "menu" and "game_over" are outside one; anything
    // else (a modal, which can also be up at the menu, an error, an unknown screen) says nothing.
    private static readonly HashSet<string> RunScreens = new(StringComparer.Ordinal)
    {
        "combat", "hand_select", "map", "rewards", "card_reward", "card_select", "shop", "rest", "event", "treasure", "crystal_sphere",
    };

    // Tools that move the game on. Only these reset the end-turn guard: a get_state or say
    // between two ended turns doesn't make them play.
    private static readonly HashSet<string> ForwardTools = new(StringComparer.Ordinal) { "act", "act_sequence", "wait_for_decision" };

    private readonly IClaudeApi _api;
    private readonly IToolHost _host;
    private readonly NotesTools _notes;
    private readonly string _promptFolder;
    private readonly Func<BrainSettings> _settings;
    private readonly SpendTracker _spend;
    private readonly Func<string?> _character;
    private readonly Func<bool> _multiplayer;
    private readonly Transcript? _transcript;
    private readonly Func<IReadOnlyList<string>> _recentChat;
    private readonly Action<string> _announce;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;
    private long _lastPromptTokens;

    // Loop state, touched only by the loop's task.
    private readonly List<JsonObject> _messages = [];
    private readonly List<JsonNode> _unseenChat = [];
    private SystemPrompt _prompt = new("", []);
    private string? _promptCharacter;
    private bool _promptMultiplayer;
    private JsonArray _tools = new();
    private JsonObject? _lastSnapshot;
    private string? _lastScreen;
    private bool _inRun;
    private bool _newRunPending;
    private long? _conversationBaseTokens;
    private string? _conversationModel;
    private string? _conversationEffort;
    private int _endTurnsInRow;
    private int _cutOffsInRow;
    private int _conversation;
    private int _turn;
    private int _transcribed;

    /// <param name="api">The Messages API (<see cref="ClaudeClient"/>).</param>
    /// <param name="tools">The game tools: <see cref="BotToolsHost"/> around the <see cref="BotTools"/> instance the MCP endpoint uses.</param>
    /// <param name="notes">notes_read, notes_write and notes_append; their root is also where a fresher
    /// strategy/learned.md is looked for when the prompt is built.</param>
    /// <param name="promptFolder">The repo, or the fallback folder with the same layout, that
    /// <see cref="PromptBuilder"/> reads.</param>
    /// <param name="settings">Read before every request, so /model and /effort apply at once.</param>
    /// <param name="spend">Where each response's cost goes, and the monthly total the cap is checked against.</param>
    /// <param name="character">The character being played ("Ironclad"), or null between runs.</param>
    /// <param name="multiplayer">True in co-op, which adds strategy/coop.md to the prompt.</param>
    /// <param name="transcript">Optional log of every exchange.</param>
    /// <param name="recentChat">The chat log's recent lines, oldest first ("Mark: go for Strength");
    /// the last 10 go into a fresh conversation.</param>
    /// <param name="announce">Puts a line in the in-game chat (as the mod, not the character):
    /// <see cref="Retrying"/>, and the reason whenever the brain stops by itself. Called on the
    /// loop's task, like every callback here. If it throws, the line is logged and dropped.</param>
    /// <param name="log">The mod's log. Never gets the key or the system text. Exceptions it
    /// throws are swallowed.</param>
    /// <param name="clock">Time source for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="delay">Waits for tests; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public ApiBrain(
        IClaudeApi api,
        IToolHost tools,
        NotesTools notes,
        string promptFolder,
        Func<BrainSettings> settings,
        SpendTracker spend,
        Func<string?>? character = null,
        Func<bool>? multiplayer = null,
        Transcript? transcript = null,
        Func<IReadOnlyList<string>>? recentChat = null,
        Action<string>? announce = null,
        Action<string>? log = null,
        Func<DateTime>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _api = api;
        _host = tools;
        _notes = notes;
        _promptFolder = promptFolder;
        _settings = settings;
        _spend = spend;
        _character = character ?? (() => null);
        _multiplayer = multiplayer ?? (() => false);
        _transcript = transcript;
        _recentChat = recentChat ?? (() => []);
        _announce = announce ?? (_ => { });
        _log = log ?? (_ => { });
        _clock = clock ?? (() => DateTime.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>The max_tokens of every request.</summary>
    public int MaxTokens { get; init; } = 16000;

    /// <summary>A fresh conversation starts past this many prompt tokens when the bot is on the map.</summary>
    public long MapRefreshTokens { get; init; } = 60_000;

    /// <summary>A fresh conversation starts past this many prompt tokens on any screen, or
    /// sooner on a model with a long-prompt tier (see <see cref="LongPromptMargin"/>).</summary>
    public long HardRefreshTokens { get; init; } = 150_000;

    /// <summary>On a model whose long-prompt tier starts below <see cref="HardRefreshTokens"/>
    /// (Haiku 5.5 bills every rate 5x past 100k prompt tokens), a fresh conversation starts
    /// this far below the tier, since the next request is larger than the last.</summary>
    public long LongPromptMargin { get; init; } = 20_000;

    /// <summary>A conversation must have grown by this much since its first request before it is
    /// refreshed, so very long notes can't cause a refresh on every turn.</summary>
    public long MinRefreshGrowth { get; init; } = 10_000;

    /// <summary>The timeout of each local wait_for_decision call.</summary>
    public int LocalWaitSeconds { get; init; } = 240;

    /// <summary>The pause between local waits that ended without a decision, in case the game
    /// returns at once.</summary>
    public TimeSpan LocalWaitPause { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often get_state is polled outside a run.</summary>
    public TimeSpan IdlePollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The first pause before resending a request the API couldn't take; it doubles
    /// up to <see cref="RetryMaxDelay"/>.</summary>
    public TimeSpan RetryFirstDelay { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan RetryMaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>More turns than this ended in a row during a run, with no act, act_sequence or
    /// wait_for_decision between them and no chat message waking the model, stop the brain with
    /// <see cref="BrainStopReason.Error"/>, so a model that won't play can't burn money. Turns
    /// ended outside a run don't count: each one needs a human message first.</summary>
    public int MaxEndTurnsInRow { get; init; } = 5;

    /// <summary>More max_tokens replies in a row than this stop the brain with
    /// <see cref="BrainStopReason.Error"/>, so a reply that never fits can't burn money.</summary>
    public int MaxCutOffsInRow { get; init; } = 2;

    /// <summary>True from <see cref="Start"/> until the loop ends (it is already false while
    /// <see cref="Stopped"/> is raised).</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>The last request's whole prompt (input, cache reads and cache writes), for /budget.</summary>
    public long LastPromptTokens => Interlocked.Read(ref _lastPromptTokens);

    /// <summary>Raised once when the loop ends, on the loop's task, with the reason and a line
    /// saying why. When the brain stopped by itself (every reason but
    /// <see cref="BrainStopReason.UserStop"/>) that line has already gone to the chat through
    /// announce, so a handler shouldn't post it again. A handler may call <see cref="Start"/>
    /// or <see cref="StopAsync"/>.</summary>
    public event Action<BrainStopReason, string>? Stopped;

    /// <summary>Start playing on a background task. False when already running. Each start
    /// begins a new conversation from wherever the game stands.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_running)
                return false;
            _running = true;
            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = Task.Run(() => RunAsync(cts.Token));
            return true;
        }
    }

    /// <summary>Stop playing: cancels the request or tool call in flight and waits for the loop
    /// to end. Called from inside the loop (a <see cref="Stopped"/> handler), it only cancels.</summary>
    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            loop = _loop;
            cts = _cts;
        }
        if (loop is null)
            return;

        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* the loop already ended */ }

        if (CurrentLoop.Value == this)
            return;
        await loop.ConfigureAwait(false);
    }

    /// <summary>The tools as the API takes them: <c>{name, description, input_schema, strict: true}</c>
    /// with every schema made strict (see <see cref="StrictSchema"/>).</summary>
    public static JsonArray ToApiTools(IEnumerable<ToolSpec> specs) =>
        new(specs.Select(s => (JsonNode)new JsonObject
        {
            ["name"] = s.Name,
            ["description"] = s.Description,
            ["input_schema"] = StrictSchema(s.InputSchema),
            ["strict"] = true,
        }).ToArray());

    // Constraints strict tool schemas don't support. They move into the description, so the
    // model still sees them and the tool still checks its own arguments.
    private static readonly string[] FoldedKeywords =
        ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength", "maxItems", "default"];

    // Where nested schemas live: lists of schemas and maps of named schemas.
    private static readonly string[] SchemaLists = ["anyOf", "allOf", "oneOf", "prefixItems"];
    private static readonly string[] SchemaMaps = ["$defs", "definitions"];

    /// <summary>
    /// A copy of <paramref name="schema"/> that strict tool use accepts: every object schema,
    /// however deeply nested, gets <c>"additionalProperties": false</c> (and empty properties
    /// when it has none), and unsupported constraints (minimum, maximum, lengths, maxItems,
    /// minItems above 1, default) are moved into the description.
    /// </summary>
    public static JsonObject StrictSchema(JsonObject schema)
    {
        var copy = (JsonObject)schema.DeepClone();
        MakeStrict(copy);
        return copy;
    }

    private static void MakeStrict(JsonObject node)
    {
        var folded = new List<string>();
        foreach (var key in FoldedKeywords)
        {
            if (!node.TryGetPropertyValue(key, out var value))
                continue;
            folded.Add($"{key} {value?.ToJsonString(ModelJson) ?? "null"}");
            node.Remove(key);
        }
        if (node["minItems"] is JsonValue min && min.TryGetValue<int>(out var items) && items > 1)
        {
            folded.Add($"minItems {items}");
            node.Remove("minItems");
        }
        if (folded.Count > 0)
        {
            var description = node["description"] is JsonValue d && d.TryGetValue<string>(out var text) ? text + " " : "";
            node["description"] = $"{description}({string.Join(", ", folded)})";
        }

        var type = node["type"];
        var isObject = Str(type) == "object"
                       || (type is JsonArray types && types.Any(t => Str(t) == "object"))
                       || node.ContainsKey("properties");
        if (isObject)
        {
            node["properties"] ??= new JsonObject();
            node["additionalProperties"] = false;
        }

        if (node["properties"] is JsonObject properties)
            foreach (var (_, property) in properties)
                if (property is JsonObject p) MakeStrict(p);
        if (node["items"] is JsonObject itemSchema)
            MakeStrict(itemSchema);
        foreach (var key in SchemaLists)
            if (node[key] is JsonArray list)
                foreach (var entry in list)
                    if (entry is JsonObject e) MakeStrict(e);
        foreach (var key in SchemaMaps)
            if (node[key] is JsonObject defs)
                foreach (var (_, def) in defs)
                    if (def is JsonObject e) MakeStrict(e);
    }

    // ---- The loop ----

    private async Task RunAsync(CancellationToken ct)
    {
        CurrentLoop.Value = this;
        var reason = BrainStopReason.UserStop;
        var message = "The API brain stopped.";
        try
        {
            _tools = ToApiTools(_host.Specs.Concat(_notes.Specs));
            _messages.Clear();
            _unseenChat.Clear();
            _lastSnapshot = null;
            _lastScreen = null;
            _inRun = false;
            _newRunPending = false;
            _cutOffsInRow = 0;

            // Nothing is sent until a run is in progress or the human writes.
            await IdleUntilPlayAsync(ct);
            BuildPrompt();
            StartConversation(Starting);

            while (true)
                await StepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Log("brain: stopped");
        }
        catch (StopBrainException stop)
        {
            (reason, message) = (stop.Reason, stop.Message);
            Log($"brain: stopped ({reason}): {message}");
            Announce(message);
        }
        catch (Exception ex)
        {
            (reason, message) = (BrainStopReason.Error, $"The API brain hit an error and stopped ({ex.GetType().Name}).");
            Log($"brain: stopped on {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            Announce(message);
        }
        finally
        {
            lock (_gate)
                _running = false;
            try
            {
                Stopped?.Invoke(reason, message);
            }
            catch (Exception ex)
            {
                Log($"brain: a Stopped handler threw {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // One request, its accounting, and whatever its response asks for.
    private async Task StepAsync(CancellationToken ct)
    {
        var settings = _settings();
        var now = _clock();
        var cap = settings.MonthlyBudgetUsd;
        if (_spend.OverCap(cap, now))
            throw new StopBrainException(BrainStopReason.MonthlyCap,
                Invariant($"Monthly API budget reached: ${_spend.MonthTotal(now):0.00} of ${cap:0.00} spent this month, so the API brain stopped."));

        var model = BrainSettings.ResolveModel(settings.Model);
        var effort = string.IsNullOrWhiteSpace(settings.Effort) ? null : settings.Effort;
        if (_conversationModel is not null && (_conversationModel != model || _conversationEffort != effort) && _messages.Count > 1)
        {
            Log($"brain: model or effort changed ({_conversationModel}, {_conversationEffort ?? "default"} to {model}, {effort ?? "default"}); starting a fresh conversation");
            StartConversation(ModelChanged);
        }
        _conversationModel = model;
        _conversationEffort = effort;
        var request = BuildRequest(model, effort);
        var sentAt = _clock();
        if (await SendAsync(request, ct) is not { } response)
        {
            // The API said the prompt is over the context window.
            if (_messages.Count <= 1)
                throw new StopBrainException(BrainStopReason.Error, "The prompt alone is too long for the model, so the API brain stopped.");
            Log("brain: the prompt is too long; starting a fresh conversation");
            StartConversation(Refreshed);
            return;
        }
        var receivedAt = _clock();

        var billedModel = response.Model ?? model;
        var cost = response.Cost(model);
        _spend.Add(cost.Usd, receivedAt);
        var usage = response.Usage;
        Interlocked.Exchange(ref _lastPromptTokens, usage.PromptTokens);
        _conversationBaseTokens ??= usage.PromptTokens;

        var toolUses = response.ToolUses;
        var stopReason = response.StopReason;
        Log(Invariant($"brain: {billedModel} in {usage.Input} cached {usage.CacheRead} written {usage.CacheWrite} out {usage.Output} ${cost.Usd:0.0000} {stopReason ?? "?"}") +
            (toolUses.Count > 0 ? $" tools: {string.Join(" ", toolUses.Select(t => t.Name))}" : ""));
        WriteTranscript(request, response, cost.Usd, sentAt, receivedAt);
        _turn++;

        switch (stopReason)
        {
            case "refusal":
                throw new StopBrainException(BrainStopReason.Error, "Claude declined to continue (refusal), so the API brain stopped.");
            case "model_context_window_exceeded":
                Log("brain: the context window is full; starting a fresh conversation");
                StartConversation(Refreshed);
                return;
        }

        // Counted across fresh conversations too: a refresh doesn't make the next reply fit.
        if (stopReason == "max_tokens")
        {
            _cutOffsInRow++;
            if (_cutOffsInRow > MaxCutOffsInRow)
                throw new StopBrainException(BrainStopReason.Error,
                    $"Claude's replies hit the output limit {_cutOffsInRow} times in a row, so the API brain stopped.");
        }
        else
        {
            _cutOffsInRow = 0;
        }

        // The model has seen everything the last request carried. Its reply goes into the
        // conversation exactly as received, thinking blocks and their signatures included.
        _unseenChat.Clear();
        if (response.Content.Count > 0)
            _messages.Add(response.ToAssistantMessage());

        if (stopReason == "max_tokens")
        {
            // A call cut off mid-way isn't run; every tool_use still needs its tool_result.
            var blocks = new JsonArray();
            foreach (var use in toolUses)
                blocks.Add(ToolResult(use.Id,
                    new JsonObject { ["error"] = "Not run: your reply hit the output limit before this call was complete." }, isError: true));
            blocks.Add(TextBlock(CutOff));
            AddUser(blocks);
            return;
        }

        if (toolUses.Count > 0)
        {
            if (toolUses.Any(t => ForwardTools.Contains(t.Name)))
                _endTurnsInRow = 0;
            AddUser(await RunToolsAsync(toolUses, ct));
            return;
        }

        // No tool call: the model thinks it is done. Outside a run that is right: idle here,
        // for free, until a run starts or the human writes, and don't count it.
        if (!InRun)
        {
            var idle = await IdleUntilPlayAsync(ct);
            AddUser(new JsonArray(TextBlock($"{KeepPlayingWithState}\n{idle.Payload.ToJsonString(ModelJson)}")));
            return;
        }

        _endTurnsInRow++;
        if (_endTurnsInRow > MaxEndTurnsInRow)
            throw new StopBrainException(BrainStopReason.Error,
                $"Claude ended {_endTurnsInRow} turns in a row without playing, so the API brain stopped.");

        // A decision is waiting: a short nudge the first time.
        if (_endTurnsInRow == 1 && IsTrue(_lastSnapshot?["awaiting_decision"]))
        {
            AddUser(new JsonArray(TextBlock(KeepPlaying)));
            return;
        }

        // Twice in a row, or while the game is busy, paused or a teammate's turn: wait here, for
        // free, until there is something to do, and hand that over with the nudge. A human
        // message is worth answering however often it comes, so it clears the count.
        var waited = await WaitLocallyAsync(ct);
        if (HasChatMessages(waited.Payload))
            _endTurnsInRow = 0;
        AddUser(new JsonArray(TextBlock($"{KeepPlayingWithState}\n{waited.Payload.ToJsonString(ModelJson)}")));
    }

    // Adds a user message, then starts afresh when a new run started, the run changed or the
    // conversation got long.
    private void AddUser(JsonArray blocks)
    {
        _messages.Add(new JsonObject { ["role"] = "user", ["content"] = blocks });

        if (InRun && (_newRunPending || PromptOutdated()))
        {
            BuildPrompt();
            StartConversation(NewRun);
            return;
        }

        var total = Interlocked.Read(ref _lastPromptTokens);
        var grown = total - (_conversationBaseTokens ?? 0) >= MinRefreshGrowth;
        if (grown && (total > HardRefreshLimit() || (total > MapRefreshTokens && _lastScreen == "map")))
        {
            Log($"brain: {total} prompt tokens on {_lastScreen ?? "an unknown screen"}; starting a fresh conversation");
            StartConversation(Refreshed);
        }
    }

    // HardRefreshTokens, or LongPromptMargin below the model's long-prompt tier when that comes first.
    private long HardRefreshLimit() =>
        ModelPrices.For(_conversationModel, out _).LongPromptThreshold is { } tier
            ? Math.Min(HardRefreshTokens, tier - LongPromptMargin)
            : HardRefreshTokens;

    /// <summary>
    /// Replaces the conversation with one user message: <paramref name="intro"/>, the run
    /// journal, the last 10 chat lines, chat messages the model hasn't seen yet, the current
    /// state, and <see cref="CarryOn"/>. The system text and tools are left alone.
    /// </summary>
    private void StartConversation(string intro)
    {
        var text = new StringBuilder(intro).Append("\n\n");

        var journal = _notes.ReadText(NotesTools.Journal).Trim();
        text.Append($"## Run journal ({NotesTools.Journal})\n\n")
            .Append(journal.Length > 0 ? journal : "(empty: the file doesn't exist yet)")
            .Append("\n\n");

        var chat = _recentChat();
        text.Append("## Recent chat\n\n")
            .Append(chat.Count > 0 ? string.Join("\n", chat.Skip(Math.Max(0, chat.Count - 10))) : "(none)")
            .Append("\n\n");

        if (_unseenChat.Count > 0)
            text.Append("## New chat messages for you\n\n")
                .Append(new JsonArray(_unseenChat.Select(m => m.DeepClone()).ToArray()).ToJsonString(ModelJson))
                .Append("\n\n");

        text.Append("## Current state\n\n")
            .Append(_lastSnapshot?.ToJsonString(ModelJson) ?? "(unknown: call get_state)")
            .Append("\n\n")
            .Append(CarryOn);

        _messages.Clear();
        _messages.Add(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(TextBlock(text.ToString())) });
        _unseenChat.Clear();
        _conversationBaseTokens = null;
        _endTurnsInRow = 0;
        _conversation++;
        _turn = 0;
        _transcribed = 0;
    }

    private void BuildPrompt()
    {
        var character = _character();
        var multiplayer = _multiplayer();
        _prompt = PromptBuilder.Build(_promptFolder, character, multiplayer, _notes.Root);
        _promptCharacter = PromptBuilder.CharacterFile(character);
        _promptMultiplayer = multiplayer;
        _newRunPending = false;
        Log($"brain: system prompt built ({_prompt.Text.Length} chars) from {string.Join(", ", _prompt.Files.Select(Path.GetFileName))}");
    }

    // A known character other than the one the prompt was built for, or co-op switched.
    private bool PromptOutdated()
    {
        var character = PromptBuilder.CharacterFile(_character());
        return (character is not null && character != _promptCharacter) || _multiplayer() != _promptMultiplayer;
    }

    private JsonObject BuildRequest(string model, string? effort)
    {
        var messages = new JsonArray();
        foreach (var message in _messages)
            messages.Add(message.DeepClone());

        var request = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = MaxTokens,
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = _prompt.Text,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral", ["ttl"] = "1h" },
            }),
            ["tools"] = _tools.DeepClone(),
            ["messages"] = messages,
            // Automatic caching: the breakpoint follows the end of the growing conversation.
            ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
        };
        if (!string.IsNullOrWhiteSpace(effort))
            request["output_config"] = new JsonObject { ["effort"] = effort };
        return request;
    }

    // Sends the request, resending it after a growing pause while the API can't be reached.
    // Errors that need the human (credit, key, limits) and requests the API refuses stop the
    // brain. Null when the API says the prompt is too long, so the caller can start afresh.
    private async Task<ClaudeResponse?> SendAsync(JsonObject request, CancellationToken ct)
    {
        var pause = RetryFirstDelay;
        var announced = false;
        while (true)
        {
            string problem;
            var wait = pause;
            try
            {
                var response = await _api.SendAsync(request, ct);
                if (announced)
                    Log("brain: Claude is reachable again");
                return response;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (ClaudeApiException ex)
            {
                TranscriptError(ex.Kind.ToString(), ex.Status);
                switch (ex.Kind)
                {
                    case ClaudeErrorKind.Unauthorized:
                        throw Stop(BrainStopReason.Unauthorized, ex);
                    case ClaudeErrorKind.OutOfCredit:
                        throw Stop(BrainStopReason.OutOfCredit, ex);
                    case ClaudeErrorKind.SpendLimit:
                        throw Stop(BrainStopReason.SpendLimit, ex);
                    case ClaudeErrorKind.Network or ClaudeErrorKind.Overloaded or ClaudeErrorKind.Server or ClaudeErrorKind.RateLimited:
                        problem = ex.Message;
                        // A rate limit that asked for a longer wait than the client would make.
                        if (ex.RetryAfter is { } asked && asked > wait)
                            wait = asked;
                        break;
                    case ClaudeErrorKind.InvalidRequest when ex.PromptTooLong:
                        return null;
                    default:
                        Log($"brain: request rejected ({ex.Kind}): {ex.Message}");
                        throw new StopBrainException(BrainStopReason.Error,
                            "Claude rejected the brain's request, so the API brain stopped. The details are in the log.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
            {
                // A timeout or a dropped connection that slipped past the client's own handling.
                TranscriptError(ex.GetType().Name, null);
                problem = $"{ex.GetType().Name}: {ex.Message}";
            }

            Log(Invariant($"brain: can't reach Claude ({problem}); retrying in {wait.TotalSeconds:0} s"));
            if (!announced)
            {
                Announce(Retrying);
                announced = true;
            }
            await _delay(wait, ct);
            pause = pause * 2 > RetryMaxDelay ? RetryMaxDelay : pause * 2;
        }

        // The exception's summary ("The API credit has run out.") is the line for the chat.
        StopBrainException Stop(BrainStopReason reason, ClaudeApiException ex)
        {
            Log($"brain: {ex.Kind}: {ex.Message}");
            return new StopBrainException(reason, ex.Summary);
        }
    }

    // ---- Tools ----

    // Runs every tool_use block in order. wait_for_decision waits locally until it has
    // something worth a request; notes tools run here; everything else goes to the game.
    private async Task<JsonArray> RunToolsAsync(IReadOnlyList<ToolUseBlock> toolUses, CancellationToken ct)
    {
        var results = new JsonArray();
        foreach (var use in toolUses)
        {
            ToolOutcome outcome;
            if (use.Name == "wait_for_decision")
                outcome = await WaitLocallyAsync(ct);
            else if (_notes.Handles(use.Name))
                outcome = await _notes.CallAsync(use.Name, use.Input, ct);
            else
                outcome = await CallHostAsync(use.Name, use.Input, ct);
            results.Add(ToolResult(use.Id, outcome.Payload, outcome.IsError));
        }
        return results;
    }

    /// <summary>
    /// wait_for_decision, repeated in-process until the game has a decision, a chat message
    /// arrives or the call fails. No request is sent meanwhile, so enemy turns, teammates,
    /// menus and pauses cost nothing. Stopping the brain cancels it. A wait that another
    /// client's wait_for_decision replaced stops the brain (see <see cref="CallHostAsync"/>).
    /// </summary>
    private async Task<ToolOutcome> WaitLocallyAsync(CancellationToken ct)
    {
        while (true)
        {
            var outcome = await CallHostAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = LocalWaitSeconds }, ct);
            if (outcome.IsError || IsTrue(outcome.Payload["awaiting_decision"]) || HasChatMessages(outcome.Payload))
                return outcome;
            await _delay(LocalWaitPause, ct);
        }
    }

    // Polls get_state until a run is in progress or a chat message arrives. Polls that end
    // neither carry nothing the model needs (no messages), so they are dropped.
    private async Task<ToolOutcome> IdleUntilPlayAsync(CancellationToken ct)
    {
        while (true)
        {
            var outcome = await CallHostAsync("get_state", new JsonObject(), ct);
            if (InRun || HasChatMessages(outcome.Payload))
                return outcome;
            await _delay(IdlePollInterval, ct);
        }
    }

    // A game tool call. Every result is observed (the latest snapshot, unseen chat); failures
    // come back as error outcomes. Only the brain's own cancellation is thrown, and a replaced
    // wait, which stops the brain.
    private async Task<ToolOutcome> CallHostAsync(string name, JsonObject args, CancellationToken ct)
    {
        ToolOutcome outcome;
        try
        {
            outcome = await _host.CallAsync(name, args, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (name == "wait_for_decision")
        {
            // BotTools cancels a wait when a newer one starts, and the brain makes one call at a
            // time, so another client (Claude Code's /play, a leftover subscription brain) is
            // waiting too. Handing the error to the model would have the two cancel each other
            // in a paid loop, and two players acting at once is worse.
            throw new StopBrainException(BrainStopReason.Error, AnotherClient);
        }
        catch (OperationCanceledException)
        {
            outcome = new ToolOutcome(new JsonObject
            {
                ["error"] = "The call was cancelled by the game before it finished.",
            }, IsError: true);
        }
        catch (Exception ex)
        {
            outcome = new ToolOutcome(new JsonObject { ["error"] = $"{ex.GetType().Name}: {ex.Message}" }, IsError: true);
        }
        Observe(outcome.Payload);
        return outcome;
    }

    // Remembers the latest snapshot (without its chat key) and the chat messages the model
    // hasn't seen, both for a fresh conversation.
    private void Observe(JsonNode payload)
    {
        if (payload is not JsonObject json)
            return;
        if (json["chat"]?["messages"] is JsonArray messages)
            foreach (var m in messages)
                if (m is not null) _unseenChat.Add(m.DeepClone());

        var snapshot = json.ContainsKey("screen") ? json
            : json["next"] is JsonObject next && next.ContainsKey("screen") ? next
            : json["current"] is JsonObject current && current.ContainsKey("screen") ? current
            : null;
        if (snapshot is null)
            return;
        var copy = (JsonObject)snapshot.DeepClone();
        copy.Remove("chat");
        _lastSnapshot = copy;
        _lastScreen = Str(copy["screen"]);
        if (RunStateOf(copy) is { } inRun)
        {
            if (inRun && !_inRun)
                _newRunPending = true;
            _inRun = inRun;
        }
    }

    /// <summary>A run is in progress, as far as the snapshots so far tell.</summary>
    private bool InRun => _inRun;

    // True on a run screen, false at the menu or game over, null when the snapshot doesn't
    // tell (a modal, an error, an unknown screen), so the previous answer stands. An explicit
    // in_run flag wins when the game sends one.
    private static bool? RunStateOf(JsonObject snapshot)
    {
        if (snapshot["in_run"] is JsonValue flag && flag.TryGetValue<bool>(out var inRun))
            return inRun;
        return Str(snapshot["screen"]) switch
        {
            { } screen when RunScreens.Contains(screen) => true,
            "menu" or "game_over" => false,
            _ => null,
        };
    }

    private static bool HasChatMessages(JsonNode payload) =>
        payload["chat"]?["messages"] is JsonArray { Count: > 0 };

    // ---- Transcript ----

    private void WriteTranscript(JsonObject request, ClaudeResponse response, decimal cost, DateTime sentAt, DateTime receivedAt)
    {
        if (_transcript is null)
            return;
        var sent = request["messages"]!.AsArray();
        var entry = new JsonObject
        {
            ["conversation"] = _conversation,
            ["turn"] = _turn,
            ["model"] = Str(request["model"]),
            ["sent_at"] = sentAt.ToString("O"),
            ["received_at"] = receivedAt.ToString("O"),
        };
        if (_turn == 0)
        {
            entry["system_chars"] = _prompt.Text.Length;
            entry["system_files"] = new JsonArray(_prompt.Files.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray());
        }
        entry["new_messages"] = new JsonArray(sent.Skip(_transcribed).Select(m => m?.DeepClone()).ToArray());
        entry["stop_reason"] = response.StopReason;
        entry["content"] = response.Content.DeepClone();
        entry["usage"] = response.UsageJson?.DeepClone();
        entry["cost_usd"] = cost;
        _transcribed = sent.Count;
        _transcript.Append(entry);
    }

    private void TranscriptError(string kind, int? status)
    {
        _transcript?.Append(new JsonObject
        {
            ["conversation"] = _conversation,
            ["turn"] = _turn,
            ["error"] = kind,
            ["status"] = status,
        });
    }

    // ---- Helpers ----

    private static JsonObject ToolResult(string toolUseId, JsonNode payload, bool isError) => new()
    {
        ["type"] = "tool_result",
        ["tool_use_id"] = toolUseId,
        ["content"] = payload.ToJsonString(ModelJson),
        ["is_error"] = isError,
    };

    private static JsonObject TextBlock(string text) => new() { ["type"] = "text", ["text"] = text };

    // Callbacks that must never take the loop down with them.
    private void Log(string line)
    {
        try { _log(line); }
        catch (Exception) { /* a broken log can't be reported anywhere */ }
    }

    private void Announce(string line)
    {
        try { _announce(line); }
        catch (Exception ex) { Log($"brain: announce threw {ex.GetType().Name}: {ex.Message}"); }
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool IsTrue(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private sealed class StopBrainException(BrainStopReason reason, string message) : Exception(message)
    {
        public BrainStopReason Reason { get; } = reason;
    }
}
