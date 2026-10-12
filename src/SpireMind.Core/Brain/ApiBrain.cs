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

    /// <summary><see cref="IPlayBrain.StopAsync"/> or <see cref="IPlayBrain.Abort"/> was called.</summary>
    UserStop,

    /// <summary>The game run ended and the brain stepped back until a controller starts one
    /// again. Never announced. <see cref="ApiBrain"/> doesn't raise it (it idles between runs);
    /// the subscription fallback does, since its Claude Code session ends with the run.</summary>
    RunEnded,

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
/// <item>Elite and boss rooms have an effort of their own (<see cref="BrainSettings.EffortFor"/>).
/// A switch to a higher effort starts a fresh conversation at that effort; a switch to a lower one
/// keeps the conversation and lowers it with an effort-only system message where the model takes one
/// (see <see cref="SupportsPerMessageEffort"/>).</item>
/// <item>Two switches, off by default, ask for a whole-turn plan at the start of each player turn in
/// those rooms and raise the effort of that one request (see <see cref="TurnPlan"/>).</item>
/// <item>Spend is added up from each response's usage and checked against the monthly cap
/// before every request, together with the other copies' requests in flight in co-op
/// (<see cref="SpendTracker.TryBegin"/>).</item>
/// <item>While <see cref="BrainSettings.Fast"/> is on and the model takes it, every request asks
/// for the API's fast mode (<c>"speed": "fast"</c> and its beta flag). A switch needs no fresh
/// conversation, and when the API says no the same request goes again at standard speed (see
/// <see cref="FastTurnedDown"/>).</item>
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
    public const string CutOff = "Your reply hit the output limit (max_tokens) and was cut off, so nothing in it was run. Continue from the current state: make your tool call sooner and keep note and chat text short.";

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

    /// <summary>Opens a conversation after the bot entered or left an elite or boss room and the effort
    /// went up, or went down on a model that can't lower it inside a conversation (see <see cref="SwitchRoomEffort"/>).</summary>
    public const string RoomEffortChanged = "The thinking effort changed with the room (elite and boss rooms have their own), so the conversation starts over.";

    /// <summary>The beta that lets an effort-only system message change effort inside a conversation
    /// without restarting its cache. Sent only on requests whose conversation holds such a message.</summary>
    public const string PerMessageEffortBeta = "mid-conversation-output-config-2026-07-01";

    /// <summary>Said once in the game chat when the API turns down an effort-only system message.</summary>
    public const string PerMessageEffortRefused = "Claude turned down the in-conversation effort change, so leaving an elite or boss room starts a fresh conversation until the next /start. The details are in the log.";

    /// <summary>Added after the first snapshot of each player turn in an elite or boss room while
    /// <see cref="BrainSettings.TurnPlanNote"/> is on (see <see cref="TurnPlan"/>).</summary>
    public const string PlanTheTurn = "(New turn. Before your first tool call, plan the whole turn: the enemies' damage this turn and over their next two turns (next_moves), when each enemy dies, and the order of every card and potion. Then play it.)";

    /// <summary>Said once in the game chat when a fast-mode request was still rate limited (429) after
    /// the client's own retries, and the brain sent it again at standard speed and switched fast mode off.</summary>
    public const string FastRateLimited = "Fast mode hit its rate limit; back to normal speed.";

    /// <summary>Said instead of <see cref="FastRateLimited"/> when the fast-mode request was still
    /// answered 529 after the client's retries: fast mode has a capacity of its own.</summary>
    public const string FastOverloaded = "Fast mode is out of capacity right now; back to normal speed.";

    /// <summary>Said instead of <see cref="FastRateLimited"/> when the API refused the speed field or
    /// its beta flag (see <see cref="FastTurnedDown"/>).</summary>
    public const string FastRefused = "Claude turned down fast mode (a research preview your API key may not have yet); back to normal speed. The details are in the log.";

    /// <summary>Ends every opening message.</summary>
    public const string CarryOn = "Carry on from here.";

    /// <summary>Said once in the game chat when the API can't be reached and the brain keeps retrying.</summary>
    public const string Retrying = "Can't reach Claude right now, retrying.";

    /// <summary>Said instead of <see cref="Retrying"/> when the API answers 429; the API's own sentence follows.</summary>
    public const string RateLimited = "Claude is rate limiting your API key, retrying. /model switches models.";

    /// <summary>Said instead of <see cref="Retrying"/> when the API answers 529.</summary>
    public const string Overloaded = "Claude is overloaded right now, retrying. /model switches models.";

    /// <summary>The stop line when another client's wait_for_decision replaced the brain's.</summary>
    public const string AnotherClient = "Another client is playing through the mod (Claude Code's /play?), so the API brain stopped.";

    private static readonly AsyncLocal<ApiBrain?> CurrentLoop = new();

    // Model-facing JSON (tool results, snapshots in text blocks). The default encoder writes
    // '>', '+', quotes and every non-ASCII character as \uXXXX, which the model reads and is
    // billed for literally. This text never goes into HTML.
    private static readonly JsonSerializerOptions ModelJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Tools that move the game on. Only these reset the end-turn guard: a get_state or say
    // between two ended turns doesn't make them play.
    private static readonly HashSet<string> ForwardTools = new(StringComparer.Ordinal) { "act", "act_sequence", "wait_for_decision", "party_pitch" };

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
    private readonly Func<string?>? _blocked;
    private readonly FightLog? _fights;
    private readonly Func<string?>? _partyPlan;
    private readonly Action? _onFastDeclined;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;
    private bool? _lastSeenInRun;
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
    private string? _conversationEffort; // the top-level effort, fixed for the conversation so its cache holds
    private string? _currentEffort; // the room's effort in effect: _conversationEffort, or the last room switch's message (a turn plan's raise aside)
    private string? _effortRoom; // the fight room the last request was built for
    private string? _fightRoom; // the elite or boss room the bot is in (RunScreens.FightRoom), or null
    private bool _effortMessages; // the conversation holds an effort-only system message
    private bool _effortMessageUnsent; // the newest one hasn't been in a request the API answered yet
    private bool _perMessageEffortRefused; // the API refused one, so room switches start fresh conversations and turn plans don't raise
    private string? _planTurn; // the turn and round ("3/3") of this fight's last new player turn (NoteTurn)
    private bool _planPending; // that turn's first snapshot hasn't been in a request the API answered yet
    private bool _planSent; // the last user message already carries the turn plan
    private string? _planRaised; // the turn_plan_effort an effort message raised the conversation to, until it is lowered
    private string? _planSkipLogged; // why the last raise was skipped, logged once
    private string? _sentEffort; // the effort the last request was sent at
    private bool _sentTurnPlan; // the last request carried a turn plan
    private bool _sentFast; // the last request the API answered asked for fast mode
    private bool _fastProven; // the API has answered a fast request since Start
    private bool _fastDeclined; // the API turned fast mode down and the setting stayed on: standard speed until it is switched off (and on again)
    private bool _fastMismatchLogged; // a response to a fast request said it ran at standard speed (logged once)
    private int _endTurnsInRow;
    private int _cutOffsInRow;
    private int _refusalsInRow;
    private long? _waitShown; // the version of the decision the last wait_for_decision handed to the model
    private string? _partyTurnShown; // the planning session's turn ("A2-start#1") last handed to the model
    private int _conversation;
    private int _turn;
    private int _transcribed;
    private bool _heldBack; // the last step found the brain blocked and sent nothing
    private bool _refreshDeferred; // this conversation passed HardRefreshTokens in a fight (logged once)

    /// <param name="api">The Messages API (<see cref="ClaudeClient"/>).</param>
    /// <param name="tools">The game tools: <see cref="BotToolsHost"/> around the <see cref="BotTools"/> instance the MCP endpoint uses.</param>
    /// <param name="notes">notes_read, notes_write, notes_append and notes_list; their root is also
    /// where a fresher strategy/learned.md is looked for, and where the wiki index's pages come
    /// from, when the prompt is built.</param>
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
    /// <param name="blocked">Why no brain may play in this copy right now (the controller's
    /// <see cref="BrainController.Blocked"/>), or null when one may. Checked before every request
    /// and before every act or act_sequence, so a brain the controller hasn't stopped yet (its
    /// poll runs every few seconds) never sends or acts once a co-op run starts. Must return at
    /// once and not touch the game; one that throws counts as null.</param>
    /// <param name="fights">Optional record of each fight, fed from the snapshots the brain sees
    /// and its own requests. It never throws.</param>
    /// <param name="partyPlan">The party plan from co-op planning sessions (PartyBot.PlanText), or
    /// null; a fresh conversation carries it after the run journal. One that throws counts as null.</param>
    /// <param name="fastDeclined">Called when the API turned fast mode down and the brain went on at
    /// standard speed (<see cref="BrainController.FastDeclined"/> switches the setting off, so /fast
    /// says what is true). The brain has already said why in the chat. Without it, or when the
    /// setting stays on, the brain itself keeps to standard speed until /fast is switched off and on again.</param>
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
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Func<string?>? blocked = null,
        FightLog? fights = null,
        Func<string?>? partyPlan = null,
        Action? fastDeclined = null)
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
        _blocked = blocked;
        _fights = fights;
        _partyPlan = partyPlan;
        _onFastDeclined = fastDeclined;
    }

    /// <summary>
    /// The max_tokens of a request at <paramref name="effort"/> when config.json doesn't set
    /// one: 16000 at low and medium, 32000 at high, 64000 at xhigh and max. Thinking counts
    /// toward it and runs longer at the top levels, where a smaller cap would cut replies off
    /// mid-thought. It is only a ceiling: what isn't used isn't billed.
    /// </summary>
    public static int MaxTokensFor(string? effort) => effort switch
    {
        "xhigh" or "max" => 64_000,
        "high" => 32_000,
        _ => 16_000,
    };

    // The models the per-message effort beta lists (all with thinking on, which is their default).
    private static readonly HashSet<string> PerMessageEffortModels = new(StringComparer.Ordinal)
    {
        "claude-opus-5-5", "claude-opus-5", "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1", "claude-mythos-5-1",
    };

    /// <summary>
    /// True when <paramref name="model"/> takes an effort-only system message
    /// (<c>{"role": "system", "content": [], "output_config": {"effort": ...}}</c>, beta
    /// <see cref="PerMessageEffortBeta"/>), which changes effort from the next user turn with new input
    /// on without touching the cached prefix or the thinking blocks. The brain uses it to lower effort
    /// (see <see cref="SwitchRoomEffort"/>); on other models that starts a fresh conversation too (a
    /// top-level change would restart the messages cache anyway). With turn_plan_effort set, it also
    /// raises effort for the first request of a player turn, on these models only (see <see cref="TurnPlan"/>).
    /// </summary>
    public static bool SupportsPerMessageEffort(string? model) =>
        model is not null && PerMessageEffortModels.Contains(model);

    /// <summary>A fresh conversation starts past this many prompt tokens when the bot is on the map.</summary>
    public long MapRefreshTokens { get; init; } = 60_000;

    /// <summary>A fresh conversation starts past this many prompt tokens on any screen, or
    /// sooner on a model with a long-prompt tier (see <see cref="LongPromptMargin"/>). In a fight
    /// it waits for the fight's end, up to <see cref="FightRefreshTokens"/>.</summary>
    public long HardRefreshTokens { get; init; } = 150_000;

    /// <summary>In a fight (the combat and hand_select screens, and a card choice that opens
    /// mid-fight), the refresh at <see cref="HardRefreshTokens"/> waits until the fight is over or
    /// the conversation passes this many prompt tokens, so a fresh conversation doesn't cut a
    /// fight in half and drop its earlier turns and the pages read. Not on a model with a
    /// long-prompt tier, where <see cref="LongPromptMargin"/> rules. A full context window or a
    /// prompt the API calls too long still starts a fresh conversation at once.</summary>
    public long FightRefreshTokens { get; init; } = 300_000;

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

    /// <summary>How long a wait_for_decision is held locally when the model repeats it on a decision
    /// the last wait already handed over, so a model that waits on a pending decision (the human
    /// said "hold on" without /pause, co-op) costs one request per hold instead of one every few seconds.</summary>
    public TimeSpan RepeatWaitHold { get; init; } = TimeSpan.FromSeconds(60);

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

    /// <inheritdoc/>
    public bool? LastSeenInRun
    {
        get { lock (_gate) return _lastSeenInRun; }
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
            _lastSeenInRun = null;
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

    /// <summary>Cancels the loop without waiting for it, for when the game quits.</summary>
    public void Abort()
    {
        CancellationTokenSource? cts;
        lock (_gate) cts = _cts;
        try { cts?.Cancel(); }
        catch (ObjectDisposedException) { /* the loop already ended */ }
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
            _refusalsInRow = 0;
            _waitShown = null;
            _fightRoom = null;
            _perMessageEffortRefused = false;
            _planTurn = null;
            _planPending = false;
            _planSkipLogged = null;
            _heldBack = false;
            _sentFast = false;
            _fastProven = false;
            _fastDeclined = false;
            _fastMismatchLogged = false;

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
        // No brain may play here (a co-op run in the human's copy): send nothing and wait for
        // the controller, which stops this brain at its next poll.
        if (BlockedReason() is { } blocked)
        {
            if (!_heldBack)
                Log($"brain: holding back: {blocked}");
            _heldBack = true;
            await _delay(IdlePollInterval, ct);
            return;
        }
        _heldBack = false;

        var settings = _settings();
        var now = _clock();
        var cap = settings.MonthlyBudgetUsd;
        // /fast: asked for on every request while it is on and the model takes it.
        var fast = FastFor(settings);
        // Records this request as in flight, so other copies count it against the same cap.
        // Every way out below that doesn't reach _spend.Add cancels it.
        // A conversation's first request writes the whole prompt to the cache, so it is
        // estimated high for the other copies. So does the first request at another speed:
        // requests at different speeds don't share cached prefixes.
        if (!_spend.TryBegin(cap, now, out var month, freshConversation: OpeningOnly() || fast != _sentFast))
            throw new StopBrainException(BrainStopReason.MonthlyCap, month >= cap
                ? Invariant($"Monthly API budget reached: ${month:0.00} of ${cap:0.00} spent this month, so the API brain stopped.")
                : Invariant($"Monthly API budget nearly reached: ${month:0.00} of ${cap:0.00} spent this month, and the other copies' requests in flight would go over it, so the API brain stopped."));

        string model;
        JsonObject request;
        DateTime sentAt;
        ClaudeResponse? sent;
        try
        {
            model = BrainSettings.ResolveModel(settings.Model);
            // Elite and boss rooms have an effort of their own (BrainSettings.EffortFor).
            var room = _fightRoom;
            var effort = EffortOf(settings, room);
            if (_conversationModel is not null && !OpeningOnly())
            {
                if (_conversationModel != model || (_currentEffort != effort && room == _effortRoom))
                {
                    // /model or /effort: a fresh conversation, as before fight efforts existed.
                    Log($"brain: model or effort changed ({_conversationModel}, {_currentEffort ?? "default"} to {model}, {effort ?? "default"}); starting a fresh conversation");
                    StartConversation(ModelChanged);
                }
                else if (_currentEffort != effort)
                {
                    SwitchRoomEffort(model, room, effort);
                }
            }
            if (OpeningOnly())
            {
                // A conversation's first request fixes its top-level effort, a fight room's included.
                _conversationEffort = effort;
            }
            _conversationModel = model;
            _currentEffort = effort;
            _effortRoom = room;
            // The first request of a player turn in an elite or boss room may be raised above the
            // room's effort for that request alone; _currentEffort stays the room's.
            _sentEffort = TurnPlan(settings, model, room, effort);
            request = BuildRequest(model, _conversationEffort, settings.MaxTokens ?? MaxTokensFor(_sentEffort), fast);
            sentAt = _clock();
            sent = await SendAsync(request, ct);
        }
        catch (SettingsChangedException)
        {
            // /model or /effort while the request was being resent: the next step builds a new one.
            _spend.Cancel();
            Log("brain: the model or effort changed while retrying; sending a new request");
            // A turn plan put in an opening message was made for the old settings, and the next
            // step leaves an opening alone: build that over too.
            if (_sentTurnPlan && OpeningOnly())
                StartConversation(ModelChanged);
            return;
        }
        catch (EffortMessageRefusedException)
        {
            // The API turned down the per-message effort beta: change effort the old way from now on,
            // and raise no turn plan. A refused request isn't billed.
            _spend.Cancel();
            _perMessageEffortRefused = true;
            Log("brain: the API refused the per-message effort change; starting a fresh conversation, fresh ones for room effort changes and no turn plan raises from now on");
            Announce(PerMessageEffortRefused);
            StartConversation(RoomEffortChanged);
            return;
        }
        catch
        {
            // Stopped, cancelled or failed before anything was billed.
            _spend.Cancel();
            throw;
        }
        _effortMessageUnsent = false;
        if (sent is not { } response)
        {
            // The API said the prompt is over the context window.
            _spend.Cancel();
            if (OpeningOnly())
                throw new StopBrainException(BrainStopReason.Error, "The prompt alone is too long for the model, so the API brain stopped.");
            Log("brain: the prompt is too long; starting a fresh conversation");
            StartConversation(Refreshed);
            return;
        }
        var receivedAt = _clock();

        var billedModel = response.Model ?? model;
        // The request as it was answered: without the speed field when the API turned fast mode
        // down and it went again at standard speed. The price follows what the response says it
        // ran at (usage.speed), not what was asked.
        _sentFast = IsFast(request);
        if (_sentFast)
            _fastProven = true;
        var cost = response.Cost(model, _sentFast);
        if (_sentFast && !cost.Fast && !_fastMismatchLogged)
        {
            _fastMismatchLogged = true;
            Log($"brain: a request asked for fast mode but the response says speed {response.Speed ?? "?"}; priced at the standard rate");
        }
        _spend.Add(cost.Usd, receivedAt);
        _fights?.Response(cost.Usd, _sentEffort, response.StopReason == "max_tokens", _sentFast);
        var usage = response.Usage;
        Interlocked.Exchange(ref _lastPromptTokens, usage.PromptTokens);
        _conversationBaseTokens ??= usage.PromptTokens;

        var toolUses = response.ToolUses;
        var stopReason = response.StopReason;
        Log(Invariant($"brain: {billedModel}{(cost.Fast ? " fast" : "")} in {usage.Input} cached {usage.CacheRead} written {usage.CacheWrite} out {usage.Output} ${cost.Usd:0.0000} {stopReason ?? "?"}") +
            (toolUses.Count > 0 ? $" tools: {string.Join(" ", toolUses.Select(t => t.Name))}" : ""));
        WriteTranscript(request, response, cost.Usd, sentAt, receivedAt);
        _turn++;

        switch (stopReason)
        {
            case "refusal":
                // A safety classifier declined (they also fire on harmless requests). The refused
                // turn can't be continued, so try once from a fresh conversation, then give up.
                var category = Str(response.Raw["stop_details"]?["category"]) ?? "no category";
                _refusalsInRow++;
                Log($"brain: Claude declined the request (refusal, {category}), {_refusalsInRow} in a row");
                if (_refusalsInRow > 1)
                    throw new StopBrainException(BrainStopReason.Error,
                        $"Claude declined to continue twice in a row (refusal, {category}), so the API brain stopped.");
                StartConversation(Refreshed);
                return;
            case "model_context_window_exceeded":
                Log("brain: the context window is full; starting a fresh conversation");
                StartConversation(Refreshed);
                return;
        }

        _refusalsInRow = 0;
        // A turn plan this request carried has its answer; one it didn't send yet goes in the next.
        // A refused or overflowing reply (above) is thrown away, so its plan goes in the fresh opening.
        _planPending = false;
        _planSent = false;

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
            _waitShown = null; // whatever it calls next, it isn't waiting on purpose
            AddUser(blocks);
            return;
        }

        if (toolUses.Count > 0)
        {
            if (toolUses.Any(t => ForwardTools.Contains(t.Name)))
                _endTurnsInRow = 0;
            var results = await RunToolsAsync(toolUses, ct);
            // Some models read user text inside a tool_result as an injection attempt, so the
            // chat lines (the human's, and in co-op the other bots') also go in a text block after
            // the last tool_result. _unseenChat was cleared after the response, so it holds this
            // round's messages only.
            if (_unseenChat.Count > 0)
                results.Add(TextBlock("New lines in the game chat (the same as chat.messages above; teammate: true marks another bot's line, the rest are the human's):\n" +
                                      new JsonArray(_unseenChat.Select(m => m.DeepClone()).ToArray()).ToJsonString(ModelJson)));
            AddUser(results);
            return;
        }

        // No tool call: the model thinks it is done. Outside a run that is right: idle here,
        // for free, until a run starts or the human writes, and don't count it.
        if (!InRun)
        {
            var idle = await IdleUntilPlayAsync(ct);
            AddUser(WithDroppedChat(new JsonArray(TextBlock($"{KeepPlayingWithState}\n{idle.Payload.ToJsonString(ModelJson)}")), idle.Payload));
            return;
        }

        _endTurnsInRow++;
        if (_endTurnsInRow > MaxEndTurnsInRow)
            throw new StopBrainException(BrainStopReason.Error,
                $"Claude ended {_endTurnsInRow} turns in a row without playing, so the API brain stopped.");

        // A decision is waiting: a short nudge the first time.
        if (_endTurnsInRow == 1 && IsTrue(_lastSnapshot?["awaiting_decision"]))
        {
            _waitShown = null; // the nudge asks for a wait_for_decision, which must answer at once
            AddUser(new JsonArray(TextBlock(KeepPlaying)));
            return;
        }

        // Twice in a row, or while the game is busy, paused or a teammate's turn: wait here, for
        // free, until there is something to do, and hand that over with the nudge. A human
        // message is worth answering however often it comes, so it clears the count.
        var waited = await WaitLocallyAsync(ct);
        if (HasChatMessages(waited.Payload))
            _endTurnsInRow = 0;
        AddUser(WithDroppedChat(new JsonArray(TextBlock($"{KeepPlayingWithState}\n{waited.Payload.ToJsonString(ModelJson)}")), waited.Payload));
    }

    // Adds the chat lines that polls dropped on the way to payload (in co-op, the other bots'
    // lines, which never end a wait or an idle stretch) as a text block, so they reach the model
    // with the payload instead of being cleared unseen after the next response. The payload's
    // own chat.messages were the last ones observed, so they are left out here.
    private JsonArray WithDroppedChat(JsonArray blocks, JsonNode payload)
    {
        var carried = payload["chat"]?["messages"] is JsonArray messages ? messages.Count(m => m is not null) : 0;
        var dropped = _unseenChat.Count - carried;
        if (dropped > 0)
            blocks.Add(TextBlock("Earlier lines in the game chat while you waited (teammate: true marks another bot's line, the rest are the human's):\n" +
                                 new JsonArray(_unseenChat.Take(dropped).Select(m => m.DeepClone()).ToArray()).ToJsonString(ModelJson)));
        return blocks;
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
        var limit = HardRefreshLimit();
        if (grown && (total > limit || (total > MapRefreshTokens && _lastScreen == "map")))
        {
            Log($"brain: {total} prompt tokens on {_lastScreen ?? "an unknown screen"}; starting a fresh conversation");
            StartConversation(Refreshed);
        }
        else if (grown && total > HardRefreshTokens && !_refreshDeferred)
        {
            // Past the usual limit in a fight: the first screen after it (rewards, game over)
            // goes back to the usual limit and refreshes there.
            _refreshDeferred = true;
            Log($"brain: {total} prompt tokens on {_lastScreen}; the fresh conversation waits until the fight is over");
        }
    }

    // The effort for a request in room, or null to leave it to the API's default.
    private static string? EffortOf(BrainSettings settings, string? room)
    {
        var effort = settings.EffortFor(room);
        return string.IsNullOrWhiteSpace(effort) ? null : effort;
    }

    /// <summary>
    /// The bot entered or left an elite or boss room whose effort differs from the one in effect.
    /// <list type="bullet">
    /// <item>Higher: a fresh conversation (the run journal and the state) at the new top-level
    /// effort, the way the benchmark behind fight efforts played each fight. Raising effort with an
    /// effort-only message works best for large jumps, and medium to high is the smallest; a model
    /// reading a history written at the old effort tends to stay at it; and the short history is
    /// cheaper to re-read through a fight than the old one.</item>
    /// <item>Lower, on a model that takes per-message effort: an effort-only system message just
    /// before the user message about to be sent, which gets a short note so it counts as new input
    /// (a user turn of tool results alone doesn't, and the change would wait for one that does). The
    /// conversation stays append-only, so its cache (the top-level effort is unchanged) and its
    /// thinking blocks stay valid, and the model keeps the room's rewards in mind. The docs call
    /// lowering this way reliable.</item>
    /// <item>Lower elsewhere: a fresh conversation, since a top-level change would restart the
    /// messages cache anyway.</item>
    /// </list>
    /// </summary>
    private void SwitchRoomEffort(string model, string? room, string? effort)
    {
        var where = room is null ? $"left the {_effortRoom ?? "fight"} room" : $"entered the {room} room";
        if (Lower(effort, _currentEffort) && SupportsPerMessageEffort(model) && !_perMessageEffortRefused && LastUserContent() is not null)
        {
            Log($"brain: {where}; effort {_currentEffort} to {effort}, keeping the conversation");
            SetMessageEffort(effort!, room is null
                ? $"(You left the {_effortRoom ?? "fight"} room, so the thinking effort is back to {effort}.)"
                : $"({room} rooms are played at {effort} effort, so the thinking effort is now {effort}.)");
            _planRaised = null; // the room's effort replaces a turn plan's raise
            return;
        }
        Log($"brain: {where}; effort {_currentEffort ?? "default"} to {effort ?? "default"}, starting a fresh conversation");
        StartConversation(RoomEffortChanged);
    }

    /// <summary>
    /// The turn plan switches, for arena measurement and off by default, applied to the request
    /// about to be built. Returns the effort that request is sent at.
    /// <list type="bullet">
    /// <item>The request that carries the first actionable snapshot of a new player turn in an elite
    /// or boss room (see <see cref="NoteTurn"/>) gets <see cref="PlanTheTurn"/> after its last block
    /// when <see cref="BrainSettings.TurnPlanNote"/> is on.</item>
    /// <item>When <see cref="BrainSettings.TurnPlanEffort"/> is higher than the room's effort, that
    /// request also gets an effort-only system message raising it, put in like a room switch's (see
    /// <see cref="SwitchRoomEffort"/>), with the plan note or a short note of its own so the user
    /// turn counts as new input. A raise still in effect from the turn before simply stays.</item>
    /// <item>The next request lowers effort back to the room's the same way. A room switch in
    /// between takes its place, and a fresh conversation starts at the room's effort anyway.</item>
    /// <item>No raise on a model without per-message effort, or once the API refused an effort
    /// message: changing the top-level effort would start a fresh conversation every turn. Logged once.</item>
    /// </list>
    /// The room's effort (<c>_currentEffort</c>) is left alone, so the room logic never takes the
    /// raise for an /effort change.
    /// </summary>
    private string? TurnPlan(BrainSettings settings, string model, string? room, string? effort)
    {
        // The same request again (the settings changed while it was retried, then changed back).
        if (_planSent)
            return _planRaised ?? effort;
        _sentTurnPlan = false;
        if (LastUserContent() is not { } content)
            return _planRaised ?? effort;

        var level = settings.TurnPlanEffort;
        if (_planPending && room is not null && (settings.TurnPlanNote || level is not null))
        {
            var note = settings.TurnPlanNote ? PlanTheTurn : null;
            var higher = level is not null && Lower(effort, level);
            var raise = higher && SupportsPerMessageEffort(model) && !_perMessageEffortRefused;
            if (higher && !raise)
            {
                var why = _perMessageEffortRefused ? "the API refused per-message effort" : $"{model} doesn't take per-message effort";
                if (_planSkipLogged != why)
                    Log($"brain: turn_plan_effort {level} skipped: {why}");
                _planSkipLogged = why;
            }

            if (raise && _planRaised != level)
            {
                Log($"brain: new turn in the {room} room; effort {effort} to {level} for its plan");
                SetMessageEffort(level!, note ?? $"(New turn: thinking effort is {level} while you plan it.)");
                _planRaised = level;
            }
            else if (!raise && _planRaised is not null)
            {
                // turn_plan_effort went down or off while raised.
                Log($"brain: new turn in the {room} room; effort {_planRaised} back to {effort}");
                SetMessageEffort(effort!, note ?? $"(Thinking effort is back to {effort}.)");
                _planRaised = null;
            }
            else if (note is not null)
            {
                content.Add(TextBlock(note));
            }
            _planSent = true;
            _sentTurnPlan = note is not null || _planRaised is not null;
            return _planRaised ?? effort;
        }

        if (_planRaised is not null)
        {
            Log($"brain: turn planned; effort {_planRaised} back to {effort}");
            SetMessageEffort(effort!, $"(Turn planned; thinking effort back to {effort}.)");
            _planRaised = null;
        }
        return effort;
    }

    // Puts an effort-only system message just before the last user message and a note at the end of
    // that message, so it counts as new input and the change applies to this very reply. One still
    // unsent there (a room switch in this step) is changed instead of adding a second.
    private void SetMessageEffort(string effort, string note)
    {
        LastUserContent()!.Add(TextBlock(note));
        if (_effortMessageUnsent && _messages.Count > 1 && Role(_messages[^2]) == "system")
            _messages[^2]["output_config"] = new JsonObject { ["effort"] = effort };
        else
            _messages.Insert(_messages.Count - 1, new JsonObject
            {
                ["role"] = "system",
                ["content"] = new JsonArray(),
                ["output_config"] = new JsonObject { ["effort"] = effort },
            });
        _effortMessages = true;
        _effortMessageUnsent = true;
    }

    // The content of the last message when it is a user message (always, between steps), else null.
    private JsonArray? LastUserContent() =>
        _messages.Count > 0 && Role(_messages[^1]) == "user" ? _messages[^1]["content"] as JsonArray : null;

    // True while the conversation's first request hasn't been answered: only its opening message,
    // with a turn plan's effort message before it aside.
    private bool OpeningOnly() => _messages.Count(m => Role(m) != "system") <= 1;

    private static string? Role(JsonNode? message) => Str(message?["role"]);

    // True when both are levels and effort is below than.
    private static bool Lower(string? effort, string? than)
    {
        if (effort is null || than is null)
            return false;
        var levels = BrainSettings.Efforts.ToList();
        var rank = levels.IndexOf(effort);
        return rank >= 0 && rank < levels.IndexOf(than);
    }

    // A 400 on a request holding an effort-only message is taken for a refusal of the beta only on
    // the first request that carries the newest such message, or when the API's words name it;
    // any other 400 stops the brain as before.
    private bool EffortMessageRefused(ClaudeApiException ex) =>
        _effortMessages && (_effortMessageUnsent
                            || ex.ApiMessage.Contains("output_config", StringComparison.OrdinalIgnoreCase)
                            || ex.ApiMessage.Contains("effort", StringComparison.OrdinalIgnoreCase));

    // HardRefreshTokens, or LongPromptMargin below the model's long-prompt tier when that comes
    // first. In a fight, on a model without such a tier, FightRefreshTokens.
    private long HardRefreshLimit()
    {
        if (ModelPrices.For(_conversationModel, out _).LongPromptThreshold is { } tier)
            return Math.Min(HardRefreshTokens, tier - LongPromptMargin);
        return InFight(_lastSnapshot) ? Math.Max(HardRefreshTokens, FightRefreshTokens) : HardRefreshTokens;
    }

    /// <summary>True when <paramref name="snapshot"/> is part of a fight: the combat and
    /// hand_select screens, and a card selection with a combat summary (a pile choice mid-fight).</summary>
    public static bool InFight(JsonObject? snapshot) =>
        Str(snapshot?["screen"]) switch
        {
            "combat" or "hand_select" => true,
            "card_select" => snapshot!["state"]?["combat"] is JsonObject,
            _ => false,
        };

    /// <summary>
    /// Replaces the conversation with one user message: <paramref name="intro"/>, today's date
    /// (for the journal's header line), the run journal, the last 10 chat lines, chat messages
    /// the model hasn't seen yet, the current state, and <see cref="CarryOn"/>. The system text
    /// and tools are left alone, so the date doesn't touch their cache.
    /// </summary>
    private void StartConversation(string intro)
    {
        var text = new StringBuilder(intro).Append("\n\n");

        text.Append($"Today is {Today(_clock()):yyyy-MM-dd}.\n\n");

        var journal = _notes.ReadText(NotesTools.Journal).Trim();
        text.Append($"## Run journal ({NotesTools.Journal})\n\n")
            .Append(journal.Length > 0 ? journal : "(empty: the file doesn't exist yet)")
            .Append("\n\n");

        if (PartyPlan() is { } plan)
            text.Append("## Party plan (from the planning sessions)\n\n").Append(plan).Append("\n\n");

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
        _effortMessages = false;
        _effortMessageUnsent = false;
        // A turn plan not answered yet goes in the opening instead; the top-level effort is the room's.
        _planSent = false;
        _planRaised = null;
        _unseenChat.Clear();
        _conversationBaseTokens = null;
        _refreshDeferred = false;
        _endTurnsInRow = 0;
        _waitShown = null;
        _partyTurnShown = null;
        _conversation++;
        _turn = 0;
        _fights?.ConversationStarted(refresh: intro == Refreshed);
        _transcribed = 0;
    }

    private void BuildPrompt()
    {
        var character = _character();
        var multiplayer = _multiplayer();
        // The prompt shows learned.md and the wiki index as they are now, so that is what the
        // notes tools' write guard takes the model to have seen.
        _notes.ResetSeen();
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

    private JsonObject BuildRequest(string model, string? effort, int maxTokens, bool fast)
    {
        var messages = new JsonArray();
        foreach (var message in _messages)
            messages.Add(message.DeepClone());

        var request = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = maxTokens,
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
        // Thinking is already on for these models; this asks for its summary instead of empty
        // thinking blocks, so the transcript says why a move was made. Other models are left as they were.
        if (ModelPrices.TakesSummarizedThinking(model))
            request["thinking"] = new JsonObject { ["type"] = "adaptive", ["display"] = "summarized" };
        if (_effortMessages)
            request[ClaudeClient.BetasField] = new JsonArray(PerMessageEffortBeta);
        // Fast mode: the speed field and its beta flag, joined with the betas above in the header.
        // Off, nothing is added, so the request is exactly what it was before fast mode existed.
        if (fast)
            AddFast(request);
        return request;
    }

    // True when the next request should ask for fast mode: /fast is on, the model takes it (on any
    // other a request with the speed field is an error), and the API hasn't turned it down while the
    // setting stayed on (SendAsync). Seeing it off forgets that, so the next /fast on tries again.
    private bool FastFor(BrainSettings settings)
    {
        if (!settings.Fast)
        {
            _fastDeclined = false;
            return false;
        }
        return !_fastDeclined && ModelPrices.SupportsFast(settings.Model);
    }

    private static bool IsFast(JsonObject request) => Str(request["speed"]) == ModelPrices.FastSpeed;

    // Puts fast mode into a request the brain built: the top-level speed field, and the beta flag
    // after whatever betas the request already names.
    private static void AddFast(JsonObject request)
    {
        request["speed"] = ModelPrices.FastSpeed;
        if (request[ClaudeClient.BetasField] is not JsonArray betas)
            request[ClaudeClient.BetasField] = betas = new JsonArray();
        if (!betas.Any(b => Str(b) == ModelPrices.FastModeBeta))
            betas.Add(ModelPrices.FastModeBeta);
    }

    // Takes it out again, leaving the request as it would have been built without it.
    private static void DropFast(JsonObject request)
    {
        request.Remove("speed");
        if (request[ClaudeClient.BetasField] is not JsonArray betas)
            return;
        for (var i = betas.Count - 1; i >= 0; i--)
            if (Str(betas[i]) == ModelPrices.FastModeBeta)
                betas.RemoveAt(i);
        if (betas.Count == 0)
            request.Remove(ClaudeClient.BetasField);
    }

    /// <summary>
    /// Why the API's answer to a request that asked for fast mode means "not at this speed": the line
    /// for the chat, or null when the failure has nothing to do with fast mode (or the request didn't
    /// ask for it). The caller then sends the same request again without the speed field, once.
    /// <list type="bullet">
    /// <item>A 429 that outlasted the client's own retries (which honour retry-after): fast mode has
    /// a rate limit of its own, separate from the model's standard one.</item>
    /// <item>A 529 that outlasted them: fast mode has a capacity of its own too.</item>
    /// <item>A request the API refuses as invalid (a 400, or a 404 or 413) whose words name the speed
    /// field, fast mode or its beta flag. So does any such refusal of the first fast request since
    /// Start that names no effort message (fast mode is a research preview a key may not have, and the
    /// API's words for that aren't documented), like a refused effort message. A prompt the API calls
    /// too long is never one of these.</item>
    /// <item>A 403 the same way: one that names fast mode, or the first fast request's. A key the API
    /// rejects outright fails the standard request too, and stops the brain as before; a 401 is
    /// never taken for fast mode.</item>
    /// </list>
    /// </summary>
    private string? FastTurnedDown(JsonObject request, ClaudeApiException ex)
    {
        if (!IsFast(request))
            return null;
        switch (ex.Kind)
        {
            case ClaudeErrorKind.RateLimited:
                return FastRateLimited;
            case ClaudeErrorKind.Overloaded:
                return FastOverloaded;
        }

        var named = ex.ApiMessage.Contains("speed", StringComparison.OrdinalIgnoreCase)
                    || ex.ApiMessage.Contains("fast", StringComparison.OrdinalIgnoreCase);
        var effortNamed = ex.ApiMessage.Contains("output_config", StringComparison.OrdinalIgnoreCase)
                          || ex.ApiMessage.Contains("effort", StringComparison.OrdinalIgnoreCase);
        var first = !_fastProven && !effortNamed;
        var refused = (ex.Kind == ClaudeErrorKind.InvalidRequest && !ex.PromptTooLong) || ex.Status == 403;
        return refused && (named || first) ? FastRefused : null;
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
            var line = Retrying;
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
                if (FastTurnedDown(request, ex) is { } said)
                {
                    // The API won't run this request in fast mode: the same request again at once
                    // without the speed field, so the turn isn't lost, and no more fast requests
                    // until fast mode is switched on again. The request no longer asks for it, so
                    // this can't come round twice.
                    Log($"brain: fast mode turned down ({ex.Kind}): {ex.Message}; sending the request again at standard speed");
                    DropFast(request);
                    Announce(said);
                    try { _onFastDeclined?.Invoke(); }
                    catch (Exception callback) { Log($"brain: the fast mode callback threw {callback.GetType().Name}: {callback.Message}"); }
                    // The controller has switched the setting off, and a /fast on from here on is a
                    // new try. Where nobody did (no controller), the brain keeps to standard speed itself.
                    _fastDeclined = _settings().Fast;
                    continue;
                }
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
                        line = ex.Kind switch
                        {
                            ClaudeErrorKind.RateLimited => $"{RateLimited} The API says: \"{Shorten(ex.ApiMessage, 160)}\"",
                            ClaudeErrorKind.Overloaded => Overloaded,
                            _ => Retrying,
                        };
                        // A rate limit that asked for a longer wait than the client would make.
                        if (ex.RetryAfter is { } asked && asked > wait)
                            wait = asked;
                        break;
                    case ClaudeErrorKind.InvalidRequest when ex.PromptTooLong:
                        return null;
                    case ClaudeErrorKind.InvalidRequest when EffortMessageRefused(ex):
                        // Most likely the per-message effort beta; a fresh conversation without
                        // effort messages either works or fails again and stops the brain below.
                        Log($"brain: request rejected ({ex.Kind}) with an effort message in it: {ex.Message}");
                        throw new EffortMessageRefusedException();
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
                Announce(line);
                announced = true;
            }
            await _delay(wait, ct);
            pause = pause * 2 > RetryMaxDelay ? RetryMaxDelay : pause * 2;

            // /model or /effort meanwhile: this request would go on asking the old model.
            var now = _settings();
            if (BrainSettings.ResolveModel(now.Model) != _conversationModel
                || EffortOf(now, _effortRoom) != _currentEffort)
                throw new SettingsChangedException();
            // /fast meanwhile: the same request at the other speed, with nothing else rebuilt.
            var fast = FastFor(now);
            if (fast != IsFast(request))
            {
                Log($"brain: fast mode switched {(fast ? "on" : "off")} while retrying; resending at {(fast ? "fast" : "standard")} speed");
                if (fast) AddFast(request);
                else DropFast(request);
            }
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
        // A party_pitch that went through is followed by a wait here, so a planning turn costs one
        // request; a wait_for_decision after it in the same reply has nothing left to wait for.
        var waitedAfterPitch = false;
        foreach (var use in toolUses)
        {
            ToolOutcome outcome;
            if (use.Name is "act" or "act_sequence")
            {
                _waitShown = null;
                waitedAfterPitch = false;
            }
            if (use.Name is "act" or "act_sequence" && BlockedReason() is { } blocked)
                outcome = new ToolOutcome(new JsonObject { ["error"] = $"Not run: {blocked}" }, IsError: true);
            else if (use.Name == "wait_for_decision" && waitedAfterPitch)
                outcome = new ToolOutcome(new JsonObject { ["waited"] = "Already waited after party_pitch: its result's next is the game now." });
            else if (use.Name == "wait_for_decision")
                outcome = await WaitLocallyAsync(ct);
            else if (use.Name == "party_pitch")
            {
                outcome = await CallHostAsync(use.Name, use.Input, ct);
                if (!outcome.IsError && IsTrue(outcome.Payload["pitched"]) && outcome.Payload is JsonObject pitched)
                {
                    // The wait that brought the turn may have shown a decision too (the map vote);
                    // the model answered the turn, not that, so it isn't held back as a repeat.
                    _waitShown = null;
                    var next = await WaitLocallyAsync(ct);
                    pitched["next"] = next.Payload;
                    waitedAfterPitch = true;
                }
            }
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
        // A decision this conversation's last wait already handed over is held back for up to
        // RepeatWaitHold: the model is waiting for something to change, and asking it again every
        // few seconds would be a paid loop. A new version, a chat message or an error ends the hold.
        var shown = _waitShown;
        var held = TimeSpan.Zero;
        while (true)
        {
            var outcome = await CallHostAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = LocalWaitSeconds }, ct);
            var decision = !outcome.IsError && IsTrue(outcome.Payload["awaiting_decision"]);
            var version = outcome.Payload["version"] is JsonValue v && v.TryGetValue<long>(out var n) ? n : (long?)null;
            var repeated = decision && shown is not null && version == shown && held < RepeatWaitHold;
            if (outcome.IsError || HasChatMessages(outcome.Payload) || (decision && !repeated) || PartyNews(outcome.Payload))
            {
                _waitShown = decision ? version : null;
                return outcome;
            }
            await _delay(LocalWaitPause, ct);
            held += LocalWaitPause;
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
        _fights?.Observe(payload);
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
        var roomBefore = _fightRoom;
        _fightRoom = RunScreens.FightRoom(_fightRoom, copy);
        NoteTurn(copy, roomBefore);
        if (RunStateOf(copy) is { } inRun)
        {
            if (inRun && !_inRun)
                _newRunPending = true;
            _inRun = inRun;
            lock (_gate) _lastSeenInRun = inRun;
        }
    }

    // Marks the first actionable combat snapshot of each player turn in an elite or boss room, told
    // apart by its turn and round, for TurnPlan. A hand_select, a busy or waiting snapshot and a turn
    // already marked don't count. The fight's end (its rewards, or leaving the room) starts over.
    private void NoteTurn(JsonObject snapshot, string? roomBefore)
    {
        var screen = Str(snapshot["screen"]);
        if (_fightRoom is null || _fightRoom != roomBefore || screen is "rewards" or "card_reward")
        {
            _planTurn = null;
            _planPending = false;
        }
        if (_fightRoom is null || screen != "combat" || !IsTrue(snapshot["awaiting_decision"])
            || snapshot["actions"] is not JsonArray { Count: > 0 } || snapshot["state"] is not JsonObject state
            || state["turn"] is not JsonValue turn || turn.GetValueKind() != JsonValueKind.Number
            || state["round"] is not JsonValue round || round.GetValueKind() != JsonValueKind.Number)
            return;
        var key = $"{turn.ToJsonString()}/{round.ToJsonString()}";
        if (key == _planTurn)
            return;
        _planTurn = key;
        _planPending = true;
    }

    // True when a result carries the planning session's news for the model: its turn to pitch (one it
    // wasn't handed yet), or the plan once a session it was in closed (carried only once).
    private bool PartyNews(JsonNode payload)
    {
        if (payload["party"] is not JsonObject party)
            return false;
        if (party["closed"] is not null)
            return true;
        if (!IsTrue(party["your_turn"]))
            return false;
        var turn = $"{Str(party["session"])}#{party["round"]?.ToJsonString()}";
        if (turn == _partyTurnShown)
            return false;
        _partyTurnShown = turn;
        return true;
    }

    // The party plan for a fresh conversation, or null. A throwing callback counts as null.
    private string? PartyPlan()
    {
        try
        {
            return _partyPlan?.Invoke() is { } plan && !string.IsNullOrWhiteSpace(plan) ? plan.Trim() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // The blocked callback's reason, or null. A throwing callback counts as null.
    private string? BlockedReason()
    {
        if (_blocked is not { } blocked)
            return null;
        try
        {
            return blocked() is { } reason && !string.IsNullOrWhiteSpace(reason) ? reason : null;
        }
        catch (Exception)
        {
            return null;
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
        return RunScreens.InRun(Str(snapshot["screen"]));
    }

    // True when the payload carries a chat message from the human. Another bot's line (teammate:
    // true, relayed in co-op) never counts, so party chat can't wake a waiting or idle brain
    // and cost a request; it still reaches the model with the next result through Observe.
    private static bool HasChatMessages(JsonNode payload) =>
        payload["chat"]?["messages"] is JsonArray messages && messages.Any(m => m is not null && !IsTrue(m["teammate"]));

    /// <summary>The player's calendar day for <paramref name="now"/>, as Claude Code's /play would
    /// date a run: local time, with a clock time of unspecified kind taken as UTC.</summary>
    public static DateTime Today(DateTime now) =>
        (now.Kind == DateTimeKind.Local ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc).ToLocalTime()).Date;

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
            ["effort"] = _sentEffort,
            ["speed"] = _sentFast ? ModelPrices.FastSpeed : ModelPrices.StandardSpeed,
            ["sent_at"] = sentAt.ToString("O"),
            ["received_at"] = receivedAt.ToString("O"),
        };
        if (_sentTurnPlan)
            entry["turn_plan"] = true;
        if (_turn == 0)
        {
            entry["system_chars"] = _prompt.Text.Length;
            entry["system_files"] = new JsonArray(_prompt.Files.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray());
        }
        entry["new_messages"] = new JsonArray(sent.Skip(_transcribed).Select(m => m?.DeepClone()).ToArray());
        entry["stop_reason"] = response.StopReason;
        if (response.Raw["stop_details"] is { } details)
            entry["stop_details"] = details.DeepClone();
        var thinking = Transcript.ThinkingSummaries(response.Content);
        if (thinking.Count > 0)
            entry["thinking"] = new JsonArray(thinking.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
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

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

    // /model or /effort changed while SendAsync was resending a request.
    private sealed class SettingsChangedException : Exception;

    // The API rejected a request whose conversation holds an effort-only system message.
    private sealed class EffortMessageRefusedException : Exception;

    private sealed class StopBrainException(BrainStopReason reason, string message) : Exception(message)
    {
        public BrainStopReason Reason { get; } = reason;
    }
}
