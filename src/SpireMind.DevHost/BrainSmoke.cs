using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpireMind.Core;
using SpireMind.Core.Brain;

namespace SpireMind.DevHost;

/// <summary>
/// <c>scripts/dev-host.sh --brain</c>: the real <see cref="ApiBrain"/> plays the fake fight
/// through the real tools and chat, with the real key and spend tracker, so the API calls can
/// be proven before the first in-game /start. Only the model (for this run, never saved), the
/// notes folder (a fresh temp folder, so the live run journal is never touched) and the number
/// of requests (<see cref="RequestCap"/>) differ from the game. It prints every request's
/// model, stop reason, tool calls, usage and cost, then the totals, and passes when the brain
/// stopped at the cap or because the fake fight was won, with no failed request. See
/// docs/brain.md.
/// </summary>
public sealed partial class BrainSmoke
{
    // How the brain writes JSON for the model; the tool calls are printed the same way.
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const int MaxShown = 110;

    /// <summary>The run's settings, key included (never printed). The model comes from <see cref="Options"/>.</summary>
    public required BrainSettings Settings { get; init; }

    public required DevHostOptions Options { get; init; }

    /// <summary>The repo, which the system prompt is built from.</summary>
    public required string PromptFolder { get; init; }

    /// <summary>Where the brain's notes and the transcript go.</summary>
    public required string NotesFolder { get; init; }

    public TextWriter Output { get; init; } = Console.Out;

    /// <summary>How long the whole test may take before it is stopped and fails.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>The fake fight's animation time; tests shorten it.</summary>
    public TimeSpan ActionDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The decision waiter's timings, as in the game unless a test shortens them.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// The whole smoke test from the command line: loads the settings and the key, finds the
    /// repo, makes the temp folder and the API client, and stops on Ctrl+C. Returns the exit
    /// code: 0 when it passed, 1 when it didn't.
    /// </summary>
    public static async Task<int> RunAsync(DevHostOptions options)
    {
        var settings = BrainSettings.Load();
        foreach (var warning in settings.Warnings)
            Console.WriteLine($"config: {warning}");
        if (!settings.HasApiKey)
        {
            Console.WriteLine($"No API key, so there is nothing to test. Save it to {settings.ApiKeyPath} (one line, chmod 600; " +
                              $"the README shows how) or set {BrainSettings.ApiKeyEnvVar}.");
            return 1;
        }
        if (FindRepo(settings) is not { } repo)
        {
            Console.WriteLine($"Couldn't find the repo's {PromptBuilder.PlayPrompt}. Run this from the checkout, or set \"repo\" in {settings.ConfigPath}.");
            return 1;
        }

        var notes = Directory.CreateTempSubdirectory("spiremind-brain-smoke-").FullName;
        using var client = new ClaudeClient(settings.ApiKey!, log: line => Console.WriteLine($"  {line}"));
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            var smoke = new BrainSmoke { Settings = settings, Options = options, PromptFolder = repo, NotesFolder = notes };
            return await smoke.RunAsync(client, stop.Token);
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// Runs the brain on a new fake fight through <paramref name="api"/> until it stops, and
    /// reports. Returns 0 when it stopped at the request cap or because the fight was won with
    /// no failed request, else 1. <paramref name="stop"/> stops it early, which fails.
    /// </summary>
    public async Task<int> RunAsync(IClaudeApi api, CancellationToken stop)
    {
        var output = TextWriter.Synchronized(Output);
        var settings = Settings with { Model = Options.Model };
        var spend = new SpendTracker(settings.ConfigDir, line => output.WriteLine($"spend: {line}"));
        var monthBefore = spend.MonthTotal(DateTime.UtcNow);

        output.WriteLine($"Brain smoke test on the fake fight: {settings.Model}, effort {settings.Effort}, at most {Options.MaxRequests} requests.");
        output.WriteLine($"Key {KeySource(settings)}. Prompt from {PromptFolder}.");
        output.WriteLine($"Notes and transcript go to {NotesFolder}, so the repo's runs/ and strategy/ aren't touched.");
        output.WriteLine(Invariant($"API spend this month so far: ${monthBefore:0.00##} of ${settings.MonthlyBudgetUsd:0.00}."));
        output.WriteLine();

        var game = new FakeGame { ActionDelay = ActionDelay };
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = PollInterval, SettleTime = SettleTime };
        var chat = DevChat.Create(output);
        var tools = new BotTools(game, waiter, versioner, chat);

        var report = new Report(output);
        var cap = new RequestCap(api, Options.MaxRequests, report.Response, report.Failure);
        ApiBrain? brain = null;
        var watch = new FightWatch(new BotToolsHost(tools), () => brain?.Abort());
        brain = new ApiBrain(
            cap,
            watch,
            new NotesTools(NotesFolder),
            PromptFolder,
            () => settings,
            spend,
            character: () => chat.BotName,
            multiplayer: () => false,
            transcript: new Transcript(NotesFolder, line => output.WriteLine($"  {line}")),
            recentChat: () => RecentChat(chat),
            // Once the cap refuses a request, the brain's own lines are about that refusal.
            announce: line => { if (!cap.CapReached) chat.Announce(line); },
            log: line => { if (!cap.CapReached && !PerRequestLine().IsMatch(line)) output.WriteLine($"  {line}"); })
        {
            RetryFirstDelay = TimeSpan.FromSeconds(5),
            RetryMaxDelay = TimeSpan.FromSeconds(20),
        };

        var stopped = new TaskCompletionSource<(BrainStopReason Reason, string Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
        brain.Stopped += (reason, message) => stopped.TrySetResult((reason, message));
        brain.Start();

        var interrupted = false;
        var timedOut = false;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            var ended = await Task.WhenAny(stopped.Task, Task.Delay(Timeout, limit.Token));
            if (ended != stopped.Task)
            {
                interrupted = stop.IsCancellationRequested;
                timedOut = !interrupted;
                await brain.StopAsync();
            }
            limit.Cancel();
        }
        var (stopReason, stopMessage) = await stopped.Task;

        output.WriteLine();
        var passed = true;
        if (interrupted)
        {
            output.WriteLine("Stopped with Ctrl+C.");
            passed = false;
        }
        else if (timedOut)
        {
            output.WriteLine(Invariant($"Stopped after {Timeout.TotalMinutes:0.#} minutes without finishing."));
            passed = false;
        }
        else if (cap.CapReached)
        {
            output.WriteLine($"Stopped at the cap of {cap.Max} requests, as planned.");
        }
        else if (watch.FightOver && stopReason == BrainStopReason.UserStop)
        {
            output.WriteLine("The fake fight is won, so the test stopped there.");
        }
        else
        {
            output.WriteLine($"The brain stopped by itself ({stopReason}): {stopMessage}");
            passed = false;
        }

        if (cap.Failed > 0)
        {
            output.WriteLine($"{cap.Failed} of {cap.Sent} requests failed; the lines above say why.");
            passed = false;
        }
        if (report.Count == 0)
        {
            output.WriteLine("No response came back.");
            passed = false;
        }

        report.Totals(spend.MonthTotal(DateTime.UtcNow));
        output.WriteLine(passed ? "PASSED" : "FAILED");
        return passed ? 0 : 1;
    }

    /// <summary>The checkout this runs from (the build folder's ancestors, then the current
    /// folder's), else <c>repo</c> in config.json; null when none has the play prompt.</summary>
    public static string? FindRepo(BrainSettings settings)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (BrainPaths.HasPrompt(dir.FullName))
                    return dir.FullName;
        return BrainPaths.Resolve(settings, modDir: null).Repo;
    }

    private static string KeySource(BrainSettings settings) => settings.ApiKeySource switch
    {
        ApiKeySource.File => $"from {settings.ApiKeyPath}",
        ApiKeySource.Environment => $"from {BrainSettings.ApiKeyEnvVar}",
        _ => "set",
    };

    // The chat's last lines as the game hands them to the brain.
    private static IReadOnlyList<string> RecentChat(ChatHub chat) =>
        chat.LinesSince(chat.LastSeq - 20)
            .Select(l => l.Kind == ChatLineKind.System ? $"(mod) {l.Text}" : $"{l.From}: {l.Text}")
            .ToArray();

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Shorten(string text)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= MaxShown ? text : text[..(MaxShown - 3)] + "...";
    }

    // ApiBrain's own line for each response, which the report already covers.
    [GeneratedRegex(@"^brain: \S+ in \d+ cached \d+ written \d+ out \d+ ")]
    private static partial Regex PerRequestLine();

    /// <summary>Prints each response and failure, numbered by request, and adds up the totals.
    /// Calls arrive one at a time on the brain's task.</summary>
    private sealed class Report(TextWriter output)
    {
        private readonly List<(int Request, TokenUsage Usage)> _answered = [];
        private TokenUsage _total = TokenUsage.Zero;
        private decimal _cost;
        private int _requests;

        /// <summary>Responses so far.</summary>
        public int Count => _answered.Count;

        public void Response(JsonObject request, ClaudeResponse response)
        {
            var requested = request["model"] is JsonValue m && m.TryGetValue<string>(out var id) ? id : "";
            var usage = response.Usage;
            var cost = response.Cost(requested).Usd;
            _answered.Add((++_requests, usage));
            _total += usage;
            _cost += cost;

            output.WriteLine(Invariant(
                $"#{_requests} {response.Model ?? requested}, {response.StopReason ?? "no stop reason"}: {Tokens(usage)}, ${cost:0.0000}"));
            foreach (var use in response.ToolUses)
                output.WriteLine($"     {Shorten($"{use.Name} {use.Input.ToJsonString(Relaxed)}")}");
            if (response.Text.Trim() is { Length: > 0 } text)
                output.WriteLine($"     text: {Shorten(text)}");
        }

        public void Failure(Exception ex) => output.WriteLine($"#{++_requests} failed: {ex.GetType().Name}: {ex.Message}");

        public void Totals(decimal monthTotal)
        {
            output.WriteLine(Invariant(
                $"Total: {Count} {(Count == 1 ? "response" : "responses")}, {Tokens(_total)}, ${_cost:0.0000}. API spend this month now ${monthTotal:0.00##}."));
            if (Count > 0 && _answered[0].Usage.CacheWrite == 0)
                output.WriteLine($"Warning: request #{_answered[0].Request} wrote nothing to the cache, so caching isn't working.");
            foreach (var (request, usage) in _answered.Skip(1))
                if (usage.CacheRead == 0)
                    output.WriteLine($"Warning: request #{request} read nothing from the cache, so the start of the prompt is changing between requests.");
        }

        private static string Tokens(TokenUsage u) => Invariant(
            $"in {u.Input:N0}, cache read {u.CacheRead:N0}, cache write {u.CacheWrite:N0} (1h {u.CacheWrite1h:N0}), out {u.Output:N0}");
    }
}

/// <summary>
/// The game tools as the smoke test's brain sees them, watching for the end of the fake
/// fight: the first result that shows the rewards screen runs <c>onFightOver</c>, which stops
/// the brain before it spends another request.
/// </summary>
public sealed class FightWatch(IToolHost inner, Action onFightOver) : IToolHost
{
    private int _over;

    public bool FightOver => Volatile.Read(ref _over) != 0;

    public IReadOnlyList<ToolSpec> Specs => inner.Specs;

    public async Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct)
    {
        var outcome = await inner.CallAsync(name, args, ct);
        if (ScreenOf(outcome.Payload) == "rewards" && Interlocked.Exchange(ref _over, 1) == 0)
            onFightOver();
        return outcome;
    }

    /// <summary>The screen a tool result shows: its own (get_state, wait_for_decision), its
    /// next (act, act_sequence) or the current one it was refused against. Null when none.</summary>
    public static string? ScreenOf(JsonNode? payload)
    {
        if (payload is not JsonObject json)
            return null;
        var snapshot = json.ContainsKey("screen") ? json : json["next"] as JsonObject ?? json["current"] as JsonObject;
        return snapshot?["screen"] is JsonValue v && v.TryGetValue<string>(out var screen) ? screen : null;
    }
}
