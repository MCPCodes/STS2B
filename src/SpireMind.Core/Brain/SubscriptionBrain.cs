using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
// The mod compiles Core with global usings for Godot and the game, and Godot has its own Environment.
// This alias keeps the .NET one there.
using Environment = System.Environment;

namespace SpireMind.Core.Brain;

/// <summary>Where and how <see cref="SubscriptionBrain"/> runs Claude Code.</summary>
public sealed record SubscriptionBrainOptions
{
    /// <summary>The prompt of a new session.</summary>
    public const string DefaultPrompt = "/play the whole run";

    /// <summary><c>claude_path</c> from config.json: the binary, when it isn't in one of the
    /// usual places. Null (or a file that doesn't exist) searches <see cref="SearchPaths"/>.</summary>
    public string? ClaudePath { get; init; }

    /// <summary>The repo folder (<c>repo</c> in config.json) Claude Code runs in. It must hold
    /// <c>.claude/commands/play.md</c>, the prompt behind /play.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>The port of the mod's HTTP bridge, whose <c>/mcp</c> endpoint Claude Code plays through.</summary>
    public required int Port { get; init; }

    /// <summary>The full model id for <c>--model</c>, read as each session starts (a resumed one
    /// too), so a /model change reaches Claude Code at its next session. Null, or null from it,
    /// leaves Claude Code's own default.</summary>
    public Func<string?>? Model { get; init; }

    /// <summary>The home folder, for "~" in the places searched and in the paths above.</summary>
    public string HomeDirectory { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>The prompt that starts a new session.</summary>
    public string Prompt { get; init; } = DefaultPrompt;

    /// <summary>Where to look for the binary, in order, when <see cref="ClaudePath"/> doesn't
    /// give one. Null for <see cref="SubscriptionBrain.DefaultSearchPaths"/>.</summary>
    public IReadOnlyList<string>? SearchPaths { get; init; }

    /// <summary>The environment the child inherits, used to work out which variables to remove
    /// and the PATH. Null for this process's own, which is what the child really inherits.</summary>
    public IReadOnlyDictionary<string, string>? InheritedEnvironment { get; init; }
}

/// <summary>
/// The subscription fallback: plays by running Claude Code headless (<c>claude -p "/play the
/// whole run"</c>) in the repo folder, signed in to Mark's Claude subscription, with the mod's
/// own MCP endpoint as its only MCP server. See docs/brain.md.
/// <list type="bullet">
/// <item>The binary is found by absolute path (a game started from Steam has no shell PATH), and
/// <c>claude auth status</c> must say it is signed in before anything else happens.</item>
/// <item>Nothing starts until a run is in progress, so no subscription turns go to the menu.
/// <see cref="BrainController"/> stops it once a run has been over for a while.</item>
/// <item>The child's environment loses the API key variables, so the CLI bills the
/// subscription, never the API credit.</item>
/// <item>Its stream-json output is logged (init, assistant text, the result, stderr). A session
/// that ends while the run goes on is resumed with <c>--resume</c> and the same flags; one that
/// ends outside a run stops the brain with <see cref="BrainStopReason.RunEnded"/>. Sessions that
/// keep ending soon after they start, and the subscription's usage limit, stop it with
/// <see cref="BrainStopReason.Error"/> and a chat line.</item>
/// </list>
/// The brain never leaves a child running once it has stopped: a stop asks Claude Code to quit
/// (SIGINT) and kills it after <see cref="StopGrace"/>; <see cref="Abort"/> kills it at once.
/// Start, stop and the status properties are safe to call from any thread; everything else
/// runs on the loop's task.
/// </summary>
public sealed class SubscriptionBrain : IPlayBrain, IDisposable
{
    /// <summary>The prompt of a resumed session.</summary>
    public const string ResumePrompt = "Keep playing: the run isn't over. Call wait_for_decision.";

    /// <summary>The name of the mod's MCP server in the <c>--mcp-config</c> Claude Code gets.</summary>
    public const string McpServerName = "spiremind";

    /// <summary>The built-in tools Claude Code may use besides the game's: reading and editing
    /// the run journal and strategy notes, and finding the MCP tools.</summary>
    public const string BuiltInTools = "Read,Edit,Write,ToolSearch";

    /// <summary>The stop line when no Claude Code binary was found.</summary>
    public const string NoClaude = "Can't find Claude Code on this Mac, so your subscription can't take over. Install it (brew install --cask claude-code) or set claude_path in ~/.config/spiremind/config.json.";

    /// <summary>The stop line when the repo folder is missing or has no play prompt.</summary>
    public const string NoRepo = "Can't find the STS2B repo folder (with .claude/commands/play.md in it), so your subscription can't take over. Set \"repo\" in ~/.config/spiremind/config.json.";

    /// <summary>The stop line when <c>claude auth status</c> doesn't say it is signed in.</summary>
    public const string NotSignedIn = "Claude Code isn't signed in to your Claude subscription, so it can't take over. Run claude once in Terminal and log in.";

    /// <summary>The stop line when Claude Code can't be started at all.</summary>
    public const string CantStart = "Couldn't start Claude Code, so your subscription can't take over. The details are in the log.";

    /// <summary>Starts the stop line when the subscription's usage limit was hit; Claude Code's
    /// own sentence follows when it says when the limit resets.</summary>
    public const string UsageLimit = "Your Claude subscription hit its usage limit, so the bot stopped.";

    /// <summary>The stop line after <see cref="MaxQuickExits"/> sessions in a row ended soon after they started.</summary>
    public const string KeepsExiting = "Claude Code keeps exiting early, so the bot stopped. The details are in the log.";

    /// <summary>The (never announced) line of a <see cref="BrainStopReason.RunEnded"/> stop.</summary>
    public const string RunOver = "The run is over, so the subscription brain stepped back.";

    /// <summary>The (never announced) line of a <see cref="BrainStopReason.UserStop"/>.</summary>
    public const string Stopping = "The subscription brain stopped.";

    private const string LogPrefix = "subscription: ";
    private const int LogCut = 300;
    private const int ChatCut = 120;

    private static readonly AsyncLocal<SubscriptionBrain?> CurrentLoop = new();

    // Variables that would make the CLI use an API key, another endpoint or another provider
    // instead of the subscription login (CLAUDE_CODE_USE_* is matched by prefix), and the
    // marker Claude Code leaves for its own children, which would make the child think it is
    // nested in another session (when the game was started from one).
    private static readonly string[] RemovedVariables = ["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "ANTHROPIC_BASE_URL", "CLAUDECODE"];
    private const string ProviderPrefix = "CLAUDE_CODE_USE_";

    // Folders put first on the child's PATH after the binary's own, for node and helpers.
    private static readonly string[] SystemPath = ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin"];

    // How Claude Code says the subscription ran out. Only searched in an error result, assistant
    // text and stderr: tool results carry game text, where "limit" means something else.
    // Claude Code 2.1.287 says "You've hit your session limit · resets 3pm (Europe/Berlin)", with
    // "weekly limit", "Opus limit", "Sonnet limit", "Fable limit", "usage limit" or just "limit"
    // in that place, and "You're out of usage credits".
    private static readonly Regex LimitPattern = new(
        @"usage limit|limit reached|rate[ _-]?limit|out of (?:extra usage|usage credits)|(?:hit|reached) your (?:[\w'’-]+ ){0,3}limit",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Claude Code's sentence for a server that is throttling for a moment, which names the usage
    // limit only to say it isn't one: "Server is temporarily limiting requests (not your usage limit)".
    private const string NotTheLimit = "not your usage limit";

    // How Claude Code says a --resume id is unknown.
    private static readonly Regex NotFoundPattern = new(
        @"no conversation found|conversation (?:was )?not found|could(?: not|n't) find (?:the |a )?conversation",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // An older limit message: "Claude AI usage limit reached|1760000000", the reset in Unix seconds.
    private static readonly Regex ResetEpoch = new(@"\|(\d{10})\b", RegexOptions.CultureInvariant);

    private readonly SubscriptionBrainOptions _options;
    private readonly IProcessLauncher _launcher;
    private readonly Func<CancellationToken, Task<bool?>> _inRun;
    private readonly Action<string> _announce;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _abort;
    private Task? _loop;
    private bool _running;
    private IChildProcess? _child;

    // Loop state, touched only by the loop's task.
    private bool _lastInRun;

    /// <param name="options">The binary, the repo, the bridge's port, the model and the prompt.</param>
    /// <param name="launcher">Starts the processes (<see cref="ProcessLauncher.Instance"/>).</param>
    /// <param name="inRun">Whether a game run is in progress: true, false, or null when the game
    /// can't tell (a modal), which keeps the last answer. Called on the loop's task, so it should
    /// await the game's main thread (<see cref="MainThreadQueue"/>) rather than block on it. If it
    /// throws, the answer counts as null.</param>
    /// <param name="announce">Puts a line in the in-game chat (as the mod, not the character): the
    /// reason whenever the brain stops by itself, except <see cref="BrainStopReason.RunEnded"/>.
    /// Called on the loop's task. If it throws, the line is logged and dropped.</param>
    /// <param name="log">The mod's log. Called on the loop's task and on the tasks reading Claude
    /// Code's output. Exceptions it throws are swallowed.</param>
    /// <param name="clock">Time source for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="delay">Waits for tests; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public SubscriptionBrain(
        SubscriptionBrainOptions options,
        IProcessLauncher launcher,
        Func<CancellationToken, Task<bool?>> inRun,
        Action<string> announce,
        Action<string> log,
        Func<DateTime>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _options = options;
        _launcher = launcher;
        _inRun = inRun;
        _announce = announce;
        _log = log;
        _clock = clock ?? (() => DateTime.UtcNow);
        _delay = delay ?? Task.Delay;
    }

    /// <summary>How often the game is asked whether a run is in progress before the first session.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long <c>claude auth status</c> may take.</summary>
    public TimeSpan AuthTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long a stopped session gets to quit after SIGINT before it is killed.</summary>
    public TimeSpan StopGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>A session that ends within this time of its start counts as a quick exit.</summary>
    public TimeSpan QuickExitWindow { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>This many quick exits in a row stop the brain with <see cref="BrainStopReason.Error"/>.
    /// A session that runs longer resets the count.</summary>
    public int MaxQuickExits { get; init; } = 3;

    /// <summary>How long to wait for the rest of a child's output once it has exited, and for a
    /// killed child to exit. Real time, never the test delay.</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>True from <see cref="Start"/> until the loop ends (it is already false while
    /// <see cref="Stopped"/> is raised).</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    /// <summary>Always null: the controller follows the run with its own poll for this brain,
    /// which never stops for a reason that switches brains.</summary>
    public bool? LastSeenInRun => null;

    /// <summary>Raised once when the loop ends, on the loop's task, with the reason and a line
    /// saying why. Every reason but <see cref="BrainStopReason.UserStop"/> and
    /// <see cref="BrainStopReason.RunEnded"/> has already been announced, so a handler shouldn't
    /// post the line again. A handler may call <see cref="Start"/> or <see cref="StopAsync"/>.</summary>
    public event Action<BrainStopReason, string>? Stopped;

    /// <summary>The places searched for the binary when claude_path doesn't name one, in order:
    /// Homebrew on Apple silicon, the native installer, Homebrew on Intel and npm, and the old
    /// local install.</summary>
    public static IReadOnlyList<string> DefaultSearchPaths(string home) =>
    [
        "/opt/homebrew/bin/claude",
        Path.Combine(home, ".local", "bin", "claude"),
        "/usr/local/bin/claude",
        Path.Combine(home, ".claude", "local", "claude"),
    ];

    /// <summary>The <c>--mcp-config</c> JSON: the mod's endpoint on <paramref name="port"/> as the only server.</summary>
    public static string McpConfig(int port) =>
        "{\"mcpServers\":{\"" + McpServerName + "\":{\"type\":\"http\",\"url\":\"http://127.0.0.1:"
        + port.ToString(CultureInfo.InvariantCulture) + "/mcp/own\"}}}";

    /// <summary>Start playing on a background task. False when already running. Each start
    /// checks the setup again and begins a new Claude Code session once a run is in progress.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_running)
                return false;
            _running = true;
            var cts = new CancellationTokenSource();
            var abort = new CancellationTokenSource();
            _cts = cts;
            _abort = abort;
            _loop = Task.Run(() => RunAsync(cts.Token, abort.Token));
            return true;
        }
    }

    /// <summary>Stop playing and wait until the brain has: a running session gets SIGINT, and is
    /// killed if it hasn't quit after <see cref="StopGrace"/>. Called from inside the loop (a
    /// <see cref="Stopped"/> handler), it only asks the brain to stop.</summary>
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

        Cancel(cts);
        if (CurrentLoop.Value == this)
            return;
        await loop.ConfigureAwait(false);
    }

    /// <summary>Stop at once without waiting, for when the game quits: kills a running child
    /// (and everything it started) and cancels the loop. Safe from any thread; returns quickly
    /// and never runs the loop's code on the caller's thread.</summary>
    public void Abort()
    {
        IChildProcess? child;
        CancellationTokenSource? cts, abort;
        lock (_gate)
        {
            child = _child;
            cts = _cts;
            abort = _abort;
        }
        Cancel(abort);
        Cancel(cts);
        child?.Kill();
    }

    /// <summary>The same as <see cref="Abort"/>.</summary>
    public void Dispose() => Abort();

    // Callbacks run on the thread pool, so a waiting loop never continues on the caller's thread.
    private static void Cancel(CancellationTokenSource? cts)
    {
        try { _ = cts?.CancelAsync(); }
        catch (ObjectDisposedException) { /* the loop already ended */ }
    }

    // ---- The loop ----

    private async Task RunAsync(CancellationToken ct, CancellationToken abort)
    {
        CurrentLoop.Value = this;
        var reason = BrainStopReason.UserStop;
        var message = Stopping;
        try
        {
            _lastInRun = false;
            var claude = FindClaude() ?? throw new StopBrainException(BrainStopReason.Error, NoClaude);
            var repo = FindRepo() ?? throw new StopBrainException(BrainStopReason.Error, NoRepo);
            var environment = ChildEnvironment(claude);
            Log($"using {claude} in {repo}");

            await CheckSignedInAsync(claude, repo, environment, ct, abort);
            await WaitForRunAsync(ct);
            await PlayAsync(claude, repo, environment, ct, abort);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Log("stopped");
        }
        catch (StopBrainException stop)
        {
            (reason, message) = (stop.Reason, stop.Message);
            Log($"stopped ({reason}): {message}");
            if (reason is not (BrainStopReason.UserStop or BrainStopReason.RunEnded))
                Announce(message);
        }
        catch (Exception ex)
        {
            (reason, message) = (BrainStopReason.Error, $"The subscription brain hit an error and stopped ({ex.GetType().Name}).");
            Log($"stopped on {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
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
                Log($"a Stopped handler threw {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    // Sessions, one after another, until the run ends or something stops the brain.
    private async Task PlayAsync(string claude, string repo, IReadOnlyDictionary<string, string?> environment,
        CancellationToken ct, CancellationToken abort)
    {
        var sessionId = Guid.NewGuid().ToString();
        var resume = false;
        var quickExits = 0;
        while (true)
        {
            var spec = new ChildProcessSpec(claude, Arguments(repo, sessionId, resume), repo, environment);
            var startedAt = _clock();
            var end = await RunSessionAsync(spec, sessionId, resume, ct, abort);
            var ranFor = _clock() - startedAt;
            Log(Invariant($"Claude Code exited with code {end.ExitCode} after {ranFor.TotalSeconds:0} s"));
            ct.ThrowIfCancellationRequested();

            if (end.Limit is { } limit)
                throw new StopBrainException(BrainStopReason.Error, LimitMessage(limit));
            if (!await AskInRunAsync(ct))
                throw new StopBrainException(BrainStopReason.RunEnded, RunOver);

            if (ranFor < QuickExitWindow)
            {
                quickExits++;
                Log($"that was a quick exit, {quickExits} in a row");
                if (quickExits >= MaxQuickExits)
                    throw new StopBrainException(BrainStopReason.Error, KeepsExiting);
            }
            else
            {
                quickExits = 0;
            }

            if (end.ConversationLost)
            {
                Log($"Claude Code can't find conversation {sessionId}; starting a fresh one");
                sessionId = Guid.NewGuid().ToString();
                resume = false;
            }
            else
            {
                Log("the run isn't over; resuming the session");
                resume = true;
            }
        }
    }

    /// <summary>
    /// The arguments of a session: <c>-p</c> with the prompt (a resumed session gets
    /// <see cref="ResumePrompt"/>), the session id after <c>--session-id</c> or <c>--resume</c>,
    /// the mod's endpoint as the only MCP server, the built-in tools it may use, permissions that
    /// allow the game tools and edits under runs/ and strategy/ and refuse anything else instead
    /// of asking, stream-json output, and the model when one is set, as it stands now.
    /// </summary>
    private List<string> Arguments(string repo, string sessionId, bool resume)
    {
        var arguments = new List<string>
        {
            "-p", resume ? ResumePrompt : _options.Prompt,
            resume ? "--resume" : "--session-id", sessionId,
            "--strict-mcp-config",
            "--mcp-config", McpConfig(_options.Port),
            "--tools", BuiltInTools,
            "--permission-mode", "dontAsk",
            "--allowedTools", "mcp__" + McpServerName, "mcp__" + McpServerName + "__*", EditRule(repo, "runs"), EditRule(repo, "strategy"),
            "--output-format", "stream-json",
            "--verbose",
        };
        if (_options.Model?.Invoke() is { } model && !string.IsNullOrWhiteSpace(model))
        {
            arguments.Add("--model");
            arguments.Add(model.Trim());
        }
        return arguments;
    }

    // A permission rule for edits under one folder of the repo. "//" starts an absolute path in
    // Claude Code's rules (a single "/" would be relative to the settings file):
    // Edit(//Users/mark/Projects/STS2B/runs/**).
    private static string EditRule(string repo, string folder) =>
        $"Edit(//{repo.Replace(Path.DirectorySeparatorChar, '/').TrimStart('/')}/{folder}/**)";

    // ---- Setup ----

    private string? FindClaude()
    {
        if (!string.IsNullOrWhiteSpace(_options.ClaudePath))
        {
            var configured = Path.GetFullPath(ExpandHome(_options.ClaudePath.Trim()));
            if (File.Exists(configured))
                return configured;
            Log($"claude_path {configured} doesn't exist; looking in the usual places");
        }
        var candidates = _options.SearchPaths ?? DefaultSearchPaths(_options.HomeDirectory);
        foreach (var path in candidates)
            if (File.Exists(path))
                return path;
        Log($"Claude Code isn't in any of {string.Join(", ", candidates)}");
        return null;
    }

    private string? FindRepo()
    {
        if (string.IsNullOrWhiteSpace(_options.WorkingDirectory))
        {
            Log("no repo folder is configured");
            return null;
        }
        var repo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ExpandHome(_options.WorkingDirectory.Trim())));
        if (File.Exists(Path.Combine(repo, ".claude", "commands", "play.md")))
            return repo;
        Log(Directory.Exists(repo) ? $"{repo} has no .claude/commands/play.md" : $"the repo folder {repo} doesn't exist");
        return null;
    }

    private string ExpandHome(string path)
    {
        if (path == "~")
            return _options.HomeDirectory;
        return path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(_options.HomeDirectory, path[2..]) : path;
    }

    /// <summary>The changes to the inherited environment for every child: the API key, auth
    /// token, base URL and provider switches removed, so the CLI uses the subscription login, and
    /// a PATH that starts with the binary's folder and the usual system folders, since a game
    /// started from Steam has a bare one.</summary>
    private Dictionary<string, string?> ChildEnvironment(string claude)
    {
        var inherited = _options.InheritedEnvironment ?? CurrentEnvironment();
        var overrides = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in RemovedVariables)
            overrides[name] = null;
        foreach (var name in inherited.Keys)
            if (name.StartsWith(ProviderPrefix, StringComparison.Ordinal))
                overrides[name] = null;

        var folders = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var inheritedPath = inherited.TryGetValue("PATH", out var path) ? path.Split(Path.PathSeparator) : [];
        foreach (var folder in new[] { Path.GetDirectoryName(claude) ?? "" }.Concat(SystemPath).Concat(inheritedPath))
            if (folder.Length > 0 && seen.Add(folder))
                folders.Add(folder);
        overrides["PATH"] = string.Join(Path.PathSeparator, folders);
        return overrides;
    }

    private static Dictionary<string, string> CurrentEnvironment()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string name && entry.Value is string value)
                variables[name] = value;
        return variables;
    }

    // claude auth status must exit 0 within AuthTimeout and print JSON with loggedIn true. Its
    // output can hold the account's email, so only loggedIn, authMethod and subscriptionType are
    // ever logged, and stderr is not read at all.
    private async Task CheckSignedInAsync(string claude, string repo, IReadOnlyDictionary<string, string?> environment,
        CancellationToken ct, CancellationToken abort)
    {
        ct.ThrowIfCancellationRequested();
        var child = Launch(new ChildProcessSpec(claude, ["auth", "status"], repo, environment));
        try
        {
            if (!await ExitedWithinAsync(child, AuthTimeout, ct))
            {
                Log(Invariant($"claude auth status didn't answer within {AuthTimeout.TotalSeconds:0} s"));
                throw new StopBrainException(BrainStopReason.Error, NotSignedIn);
            }
            var code = await child.Exited;
            var status = ParseAuthStatus(await ReadRestAsync(child.StandardOutput));
            if (status is null)
                Log(Invariant($"claude auth status exited with code {code} and didn't print JSON"));
            else
                Log(Invariant($"claude auth status exited with code {code}: loggedIn {(status.LoggedIn ? "true" : "false")}, authMethod {status.AuthMethod ?? "(none)"}, subscriptionType {status.SubscriptionType ?? "(none)"}"));
            if (code != 0 || status is not { LoggedIn: true })
                throw new StopBrainException(BrainStopReason.Error, NotSignedIn);
            if (status.AuthMethod is { } method && method != "claude.ai")
                Log($"warning: Claude Code is signed in with {method}, not a claude.ai subscription");
        }
        finally
        {
            await EndChildAsync(child, graceful: false, abort);
            Release(child);
        }
    }

    private sealed record AuthStatus(bool LoggedIn, string? AuthMethod, string? SubscriptionType);

    private static AuthStatus? ParseAuthStatus(IReadOnlyList<string> lines)
    {
        var text = string.Join("\n", lines);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
            return null;
        try
        {
            if (JsonNode.Parse(text[start..(end + 1)]) is not JsonObject json)
                return null;
            var loggedIn = json["loggedIn"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
            return new AuthStatus(loggedIn, Short(json["authMethod"]), Short(json["subscriptionType"]));
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Short(JsonNode? node) => Str(node) is { } s ? Cut(s, 40) : null;
    }

    private async Task WaitForRunAsync(CancellationToken ct)
    {
        var said = false;
        while (!await AskInRunAsync(ct))
        {
            if (!said)
            {
                Log("waiting for a run before starting Claude Code");
                said = true;
            }
            await _delay(PollInterval, ct);
        }
    }

    // The game's answer, or the last one when it can't tell or the question fails.
    private async Task<bool> AskInRunAsync(CancellationToken ct)
    {
        bool? answer;
        try
        {
            // WaitAsync: a question that ignores the token can't hold up a stop.
            answer = await _inRun(ct).WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log($"couldn't tell whether a run is in progress ({ex.GetType().Name}: {ex.Message})");
            answer = null;
        }
        if (answer is { } inRun)
            _lastInRun = inRun;
        return _lastInRun;
    }

    // ---- Sessions ----

    private sealed record SessionEnd(int ExitCode, string? Limit, bool ConversationLost);

    // One Claude Code process, from launch to exit. Its output is read as it comes. Cancelling
    // stops the child (SIGINT, then a kill) before the cancellation goes on up.
    private async Task<SessionEnd> RunSessionAsync(ChildProcessSpec spec, string sessionId, bool resume,
        CancellationToken ct, CancellationToken abort)
    {
        ct.ThrowIfCancellationRequested();
        Log($"{(resume ? "resuming" : "starting")} Claude Code session {sessionId}: {CommandLine(spec)}");
        var child = Launch(spec);
        Log(Invariant($"Claude Code is running as pid {child.Id}"));
        var watch = new SessionWatch();
        var readers = Task.WhenAll(
            Task.Run(() => ReadLinesAsync(child.StandardOutput, line => OnOutput(line, watch))),
            Task.Run(() => ReadLinesAsync(child.StandardError, line => OnError(line, watch))));
        try
        {
            var code = await child.Exited.WaitAsync(ct);
            await DrainAsync(readers);
            return watch.End(code);
        }
        finally
        {
            if (!child.Exited.IsCompleted)
            {
                await EndChildAsync(child, graceful: true, abort);
                await DrainAsync(readers);
            }
            Release(child);
        }
    }

    private IChildProcess Launch(ChildProcessSpec spec)
    {
        IChildProcess child;
        try
        {
            child = _launcher.Start(spec);
        }
        catch (Exception ex)
        {
            Log($"couldn't start {spec.FileName}: {ex.GetType().Name}: {ex.Message}");
            throw new StopBrainException(BrainStopReason.Error, CantStart);
        }
        lock (_gate)
            _child = child;
        return child;
    }

    // Forgets and disposes a child that has exited or been killed.
    private void Release(IChildProcess child)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_child, child))
                _child = null;
        }
        try { child.Dispose(); }
        catch (Exception ex) { Log($"disposing Claude Code threw {ex.GetType().Name}: {ex.Message}"); }
    }

    // Makes sure the child is gone: SIGINT and up to StopGrace to quit when graceful (and the
    // brain isn't aborting), then a kill. A graceful stop logs how it went, so the log tells a
    // working SIGINT from the kill it falls back to (exit code 137 on macOS and Linux).
    private async Task EndChildAsync(IChildProcess child, bool graceful, CancellationToken abort)
    {
        if (child.Exited.IsCompleted)
            return;
        if (graceful && !abort.IsCancellationRequested)
        {
            Log(Invariant($"asking Claude Code (pid {child.Id}) to stop"));
            if (!child.Interrupt())
                Log(Invariant($"couldn't send SIGINT to Claude Code (pid {child.Id}), so it was killed instead"));
            if (!child.Exited.IsCompleted)
            {
                using var grace = CancellationTokenSource.CreateLinkedTokenSource(abort);
                await Task.WhenAny(child.Exited, _delay(StopGrace, grace.Token));
                grace.Cancel();
            }
            if (child.Exited.IsCompleted)
            {
                Log(Invariant($"Claude Code (pid {child.Id}) exited with code {await child.Exited} after the stop"));
                return;
            }
        }
        Log(Invariant($"killing Claude Code (pid {child.Id})"));
        child.Kill();
        try { await child.Exited.WaitAsync(DrainTimeout); }
        catch (TimeoutException) { Log(Invariant($"pid {child.Id} hasn't exited after the kill")); }
    }

    // True when the child exited within the timeout; throws when the brain is stopped meanwhile.
    private async Task<bool> ExitedWithinAsync(IChildProcess child, TimeSpan timeout, CancellationToken ct)
    {
        if (child.Exited.IsCompleted)
            return true;
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await Task.WhenAny(child.Exited, _delay(timeout, timer.Token));
        timer.Cancel();
        ct.ThrowIfCancellationRequested();
        return child.Exited.IsCompleted;
    }

    // Every line still to come from a child that has exited, waiting at most DrainTimeout.
    private async Task<List<string>> ReadRestAsync(System.Threading.Channels.ChannelReader<string> reader)
    {
        var lines = new List<string>();
        using var drain = new CancellationTokenSource(DrainTimeout);
        try
        {
            await foreach (var line in reader.ReadAllAsync(drain.Token))
                lines.Add(line);
        }
        catch (OperationCanceledException)
        {
            Log("the output didn't end after the process exited; going on with what came");
        }
        return lines;
    }

    private async Task ReadLinesAsync(System.Threading.Channels.ChannelReader<string> reader, Action<string> onLine)
    {
        try
        {
            await foreach (var line in reader.ReadAllAsync())
            {
                try { onLine(line); }
                catch (Exception ex) { Log($"couldn't handle a line of output ({ex.GetType().Name}: {ex.Message})"); }
            }
        }
        catch (Exception ex)
        {
            Log($"reading Claude Code's output failed ({ex.GetType().Name}: {ex.Message})");
        }
    }

    // Waits for the output readers once the child is gone. A grandchild holding the pipe open
    // can't keep the brain waiting.
    private async Task DrainAsync(Task readers)
    {
        try { await readers.WaitAsync(DrainTimeout); }
        catch (TimeoutException) { Log("Claude Code's output didn't end after it exited; going on without the rest"); }
    }

    // ---- Output ----

    // One stdout line: stream-json, one object per line, though anything else is tolerated.
    private void OnOutput(string line, SessionWatch watch)
    {
        var text = line.Trim();
        if (text.Length == 0)
            return;
        JsonObject? json = null;
        if (text.StartsWith('{'))
        {
            try { json = JsonNode.Parse(text) as JsonObject; }
            catch (JsonException) { /* not JSON after all */ }
        }
        if (json is null)
        {
            Log($"claude: {Cut(text, LogCut)}");
            return;
        }

        switch (Str(json["type"]))
        {
            case "system":
                OnSystem(json);
                break;
            case "assistant":
                OnAssistant(json, watch);
                break;
            case "result":
                OnResult(json, watch);
                break;
            // "user" events carry the tool results, game text and all: neither logged nor searched.
        }
    }

    private void OnSystem(JsonObject json)
    {
        var subtype = Str(json["subtype"]);
        if (subtype != "init")
        {
            Log($"claude: system {subtype ?? "event"}");
            return;
        }
        var servers = json["mcp_servers"] is JsonArray array ? array.OfType<JsonObject>().ToList() : [];
        var list = servers.Select(s => $"{Str(s["name"]) ?? "?"} {Str(s["status"]) ?? "?"}").ToList();
        Log($"claude: session started on {Str(json["model"]) ?? "an unknown model"}; MCP servers: {(list.Count > 0 ? string.Join(", ", list) : "none")}");
        var status = Str(servers.FirstOrDefault(s => Str(s["name"]) == McpServerName)?["status"]);
        if (status != "connected")
            Log($"warning: the {McpServerName} MCP server is {status ?? "missing"}, so Claude Code can't reach the game");
    }

    private void OnAssistant(JsonObject json, SessionWatch watch)
    {
        if (json["message"] is not JsonObject message || message["content"] is not JsonArray content)
            return;
        var tools = new List<string>();
        var limit = false;
        foreach (var block in content.OfType<JsonObject>())
        {
            switch (Str(block["type"]))
            {
                case "text":
                    if (Str(block["text"]) is { } text && text.Trim().Length > 0)
                    {
                        Log($"claude says: {Cut(text, LogCut)}");
                        limit |= watch.SawLimit(text, inResult: false);
                    }
                    break;
                case "tool_use":
                    var name = Str(block["name"]) ?? "?";
                    var prefix = "mcp__" + McpServerName + "__";
                    tools.Add(name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name);
                    break;
            }
        }
        if (tools.Count > 0)
            Log($"claude calls {string.Join(", ", tools)}");
        if (!limit)
            watch.Progress();
    }

    private void OnResult(JsonObject json, SessionWatch watch)
    {
        var subtype = Str(json["subtype"]) ?? "?";
        var isError = json["is_error"] is JsonValue e && e.TryGetValue<bool>(out var error) && error;
        var turns = json["num_turns"] is JsonValue t && t.TryGetValue<int>(out var n) ? n : 0;
        var seconds = json["duration_ms"] is JsonValue d && d.TryGetValue<double>(out var ms) ? ms / 1000 : 0;
        var text = Str(json["result"]) ?? "";
        if (json["errors"] is JsonArray errors)
            text = string.Join(" ", new[] { text }.Concat(errors.Select(Str).OfType<string>())).Trim();

        Log(Invariant($"claude: result {subtype}, is_error {(isError ? "true" : "false")}, {turns} turns, {seconds:0.0} s: {Cut(text, LogCut)}"));
        watch.Result(isError);
        // A result that isn't an error is the model's own last message, where these words can be game talk.
        if (isError)
            watch.SawLimit(text, inResult: true);
        watch.SawConversationLost(text);
    }

    private void OnError(string line, SessionWatch watch)
    {
        var text = line.Trim();
        if (text.Length == 0)
            return;
        Log($"claude stderr: {Cut(text, LogCut)}");
        watch.SawLimit(text, inResult: false);
        watch.SawConversationLost(text);
    }

    /// <summary>
    /// What a session's output said, from the two reader tasks. A limit in the result is
    /// final. One in assistant text or stderr only counts if Claude Code didn't carry on: a later
    /// assistant message clears it, and so does a result that isn't an error.
    /// </summary>
    private sealed class SessionWatch
    {
        private readonly object _gate = new();
        private string? _limit;
        private bool _limitInResult;
        private bool _resultSeen;
        private bool _resultIsError;
        private bool _conversationLost;

        // Remembers the sentence a limit pattern matched in. True when one did.
        public bool SawLimit(string text, bool inResult)
        {
            if (text.Contains(NotTheLimit, StringComparison.OrdinalIgnoreCase))
                return false;
            var match = LimitPattern.Match(text);
            if (!match.Success)
                return false;
            var sentence = SentenceAround(text, match.Index, match.Length);
            lock (_gate)
            {
                if (inResult || !_limitInResult)
                    _limit = sentence;
                _limitInResult |= inResult;
            }
            return true;
        }

        public void Progress()
        {
            lock (_gate)
            {
                if (!_limitInResult)
                    _limit = null;
            }
        }

        public void Result(bool isError)
        {
            lock (_gate)
            {
                _resultSeen = true;
                _resultIsError = isError;
            }
        }

        public void SawConversationLost(string text)
        {
            if (!NotFoundPattern.IsMatch(text))
                return;
            lock (_gate)
                _conversationLost = true;
        }

        public SessionEnd End(int exitCode)
        {
            lock (_gate)
            {
                var limit = _limitInResult || !(_resultSeen && !_resultIsError) ? _limit : null;
                return new SessionEnd(exitCode, limit, _conversationLost);
            }
        }
    }

    // The chat line for a usage limit: Claude Code's own sentence goes along when it says when
    // the limit resets.
    private string LimitMessage(string sentence)
    {
        var now = _clock();
        var detail = ResetEpoch.Replace(sentence, m => " (resets " + ResetTime(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), now) + ")");
        return detail.Contains("reset", StringComparison.OrdinalIgnoreCase)
            ? $"{UsageLimit} Claude Code says: \"{Cut(detail, ChatCut)}\""
            : UsageLimit;
    }

    // "at 5:00 PM", with the day in front when it isn't today (local time).
    private static string ResetTime(long unixSeconds, DateTime nowUtc)
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
        var today = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToLocalTime().Date;
        return reset.Date == today
            ? "at " + reset.ToString("h:mm tt", CultureInfo.InvariantCulture)
            : reset.ToString("'on' ddd MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);
    }

    // The sentence of the line around a match: from just after the previous ". ", "! " or "? "
    // to the next one (or the line's end).
    private static string SentenceAround(string text, int index, int length)
    {
        var lineStart = index == 0 ? 0 : text.LastIndexOf('\n', index - 1) + 1;
        var lineEnd = text.IndexOf('\n', index + length);
        var line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];
        var at = index - lineStart;

        var start = 0;
        var end = line.Length;
        foreach (var stop in new[] { ". ", "! ", "? " })
        {
            var before = at > 0 ? line.LastIndexOf(stop, at - 1, StringComparison.Ordinal) : -1;
            if (before >= 0)
                start = Math.Max(start, before + stop.Length);
            var after = line.IndexOf(stop, at + length, StringComparison.Ordinal);
            if (after >= 0)
                end = Math.Min(end, after + 1);
        }
        return line[start..end].Trim();
    }

    // ---- Helpers ----

    // The command for the log, arguments with spaces or quotes in single quotes. It never holds
    // a secret: the key isn't on the command line and the environment isn't shown.
    private static string CommandLine(ChildProcessSpec spec)
    {
        var text = new StringBuilder(spec.FileName);
        foreach (var argument in spec.Arguments)
        {
            text.Append(' ');
            if (argument.Length > 0 && argument.All(c => char.IsLetterOrDigit(c) || "-_./:,=@*".Contains(c)))
                text.Append(argument);
            else
                text.Append('\'').Append(argument.Replace("'", "'\\''")).Append('\'');
        }
        return text.ToString();
    }

    // One line, at most max characters.
    private static string Cut(string text, int max)
    {
        var flat = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (flat.Length <= max)
            return flat;
        var end = max - 3;
        if (char.IsHighSurrogate(flat[end - 1]))
            end--;
        return flat[..end] + "...";
    }

    // Callbacks that must never take the loop down with them.
    private void Log(string line)
    {
        try { _log(LogPrefix + line); }
        catch (Exception) { /* a broken log can't be reported anywhere */ }
    }

    private void Announce(string line)
    {
        try { _announce(line); }
        catch (Exception ex) { Log($"announce threw {ex.GetType().Name}: {ex.Message}"); }
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private sealed class StopBrainException(BrainStopReason reason, string message) : Exception(message)
    {
        public BrainStopReason Reason { get; } = reason;
    }
}
