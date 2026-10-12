using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// Where the game reads its brain's settings and key: this copy's config folder and the one
/// every copy shares, as <see cref="InstanceRole.Detect"/> works them out for Mark's own copy
/// (<c>~/.config/spiremind</c>, or what <c>SPIREMIND_CONFIG_DIR</c> names), and the environment
/// the key may come from. The checks name folders of their own, so they never look at his.
/// </summary>
public sealed record ApiPlaces(string ConfigDir, string SharedConfigDir, Func<string, string?> GetEnv)
{
    /// <summary>The game's own places on this Mac.</summary>
    public static ApiPlaces OfTheGame()
    {
        var role = InstanceRole.Detect([], InstanceRole.HumanPort);
        return new ApiPlaces(role.ConfigDir, role.SharedConfigDir, Environment.GetEnvironmentVariable);
    }
}

/// <summary>For the checks: the simulator's and the tools' timings, shortened so a scripted fight takes a moment.</summary>
public sealed record ApiTimings(TimeSpan ActionDelay, TimeSpan WaiterPoll, TimeSpan WaiterSettle, TimeSpan PollInterval, TimeSpan WaitHold);

/// <summary>
/// The settings the API brain is run with in the arena: the game's own, read by the game's own
/// code (<see cref="BrainSettings.Load"/>), with what the command line overrides.
/// </summary>
public static class ApiSettings
{
    /// <summary>
    /// The game's settings without its key: config.json is copied to a folder of its own (with
    /// the shared one, for a copy that has both) and read there by <see cref="BrainSettings.Load"/>
    /// with an empty environment. So the model, the efforts and max_tokens are parsed by the
    /// code the game parses them with, and the key file is not opened: there is none in that
    /// folder. This is what a batch plans with and says before it spends anything.
    /// </summary>
    public static BrainSettings WithoutKey(ApiPlaces places)
    {
        var temp = Directory.CreateTempSubdirectory("spiremind-arena-settings-").FullName;
        try
        {
            var shared = Path.Combine(temp, "shared");
            var own = Path.Combine(temp, "own");
            Directory.CreateDirectory(shared);
            Directory.CreateDirectory(own);
            CopyConfig(places.SharedConfigDir, shared);
            if (SamePath(places.ConfigDir, places.SharedConfigDir))
                return BrainSettings.Load(shared, _ => null);
            CopyConfig(places.ConfigDir, own);
            return BrainSettings.Load(own, _ => null, shared);
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the system clears its temp folder itself */ }
        }
    }

    /// <summary><paramref name="settings"/> with the command line's own: <c>--effort</c> when it
    /// was typed, <c>--elite-effort</c> and <c>--boss-effort</c> ("default" clears one back to
    /// the mod's default, as /effort elite default does). The model is set per fight.</summary>
    public static BrainSettings Apply(BrainSettings settings, ArenaOptions options)
    {
        if (options.Given.Contains("--effort"))
            settings = settings with { Effort = options.Effort };
        if (options.EliteEffort is { } elite && BrainSettings.TryParseFightEffort(elite, out var eliteEffort))
            settings = settings with { EliteEffort = eliteEffort };
        if (options.BossEffort is { } boss && BrainSettings.TryParseFightEffort(boss, out var bossEffort))
            settings = settings with { BossEffort = bossEffort };
        return settings;
    }

    /// <summary>
    /// The efforts these settings give the tiers' rooms where they differ from the normal one,
    /// as <c>--effort-by-tier</c> names them (elite=high, boss=high). They go into the batch's
    /// label, so a fight's planned effort is part of what makes it the same fight, and the
    /// report reads a batch as one player whose fights differ in effort by tier.
    /// </summary>
    public static Dictionary<string, string> EffortByTier(BrainSettings settings)
    {
        var efforts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tier in Scenarios.Tiers)
            if (settings.EffortFor(Brains.RoomOf(tier)) is var effort && effort != settings.Effort)
                efforts[tier] = effort;
        return efforts;
    }

    /// <summary>The settings in words: "effort medium, elite rooms high, boss rooms high, max_tokens by effort (16000 at medium, 32000 at high)".</summary>
    public static string Describe(BrainSettings settings)
    {
        var (elite, boss) = (settings.EffortFor(BrainSettings.EliteRoom), settings.EffortFor(BrainSettings.BossRoom));
        var tokens = settings.MaxTokens is { } fixedTokens
            ? string.Create(CultureInfo.InvariantCulture, $"max_tokens {fixedTokens}")
            : "max_tokens by effort (" + string.Join(", ", new[] { settings.Effort, elite, boss }.Distinct()
                .Select(effort => string.Create(CultureInfo.InvariantCulture, $"{ApiBrain.MaxTokensFor(effort)} at {effort}"))) + ")";
        return $"effort {settings.Effort}, elite rooms {elite}, boss rooms {boss}, {tokens}";
    }

    /// <summary>True when two settings would make different requests: another model, effort,
    /// room effort or max_tokens.</summary>
    public static bool Differ(BrainSettings a, BrainSettings b) =>
        a.Model != b.Model || a.Effort != b.Effort || a.EliteEffort != b.EliteEffort || a.BossEffort != b.BossEffort || a.MaxTokens != b.MaxTokens;

    private static void CopyConfig(string from, string to)
    {
        try
        {
            var source = Path.Combine(from, BrainSettings.ConfigFileName);
            if (File.Exists(source))
                File.Copy(source, Path.Combine(to, BrainSettings.ConfigFileName), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable config.json is the defaults, as it is for the game.
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.Ordinal);
}

/// <summary>
/// <c>arena run --brain api</c> and <c>arena position --brain api</c>: the batch's fights played
/// by the mod's own brain on the API credit (<see cref="ApiFight"/>), one result line each, in
/// the same results.jsonl and with the same rule for what is already played as Claude Code's
/// batches. Since this spends money:
/// <list type="bullet">
/// <item>It says first how many fights it plays at most, with which model and efforts, and on
/// what budget, and it starts nothing without <c>--yes-spend</c>.</item>
/// <item>The key is read by Core's own code (<see cref="BrainSettings.Load"/>, from the place
/// the game reads it) when the first fight is about to start, and goes from there straight
/// into Core's client. It is never printed, written or kept here.</item>
/// <item>The budget (<c>--max-usd</c>) is the results folder's, not one command's: every answered
/// request is written to <see cref="ApiBudget.LedgerFile"/> there, and a later run in the same
/// folder starts from what that file adds up to, so the same command again never gets a fresh
/// budget. It holds three ways: no fight starts once the folder's fights have cost that much,
/// when what is left is less than a fight's first request, or less than the dearest fight so
/// far; each fight's brain has what is left as its own cap; and no request leaves once the sum
/// is reached. The request that reaches it is the last one sent, so a batch can end one
/// request (per fight running) over it.</item>
/// <item>Each fight counts its spend in its own temp folder. Mark's spend.json is not read or
/// written. Of the game's monthly cap only the setting is honoured: a monthly_budget_usd of 0
/// (never use the API) starts nothing, and <c>--max-usd</c> may not be above it.</item>
/// </list>
/// One fight runs at a time unless <c>--parallel</c> says otherwise, and the first fight of a
/// run is played alone to its end: it writes the system prompt to the cache, and it tells the
/// budget what a fight costs before several run at once.
/// </summary>
public static class ApiRun
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>What a batch that isn't allowed to spend says before it stops.</summary>
    public const string NeedsYes = "Nothing was started: this bills your API credit, so it needs --yes-spend.";

    /// <summary>Runs <c>arena run --brain api</c> and returns the exit code: 0 when every fight
    /// was started, 1 when the batch stopped early (the budget, the key, the credit), 2 when it
    /// wasn't allowed to start or a path is no good, 130 after Ctrl+C.</summary>
    /// <param name="places">Where the settings and the key are read; null for the game's own.</param>
    /// <param name="transport">A stand-in for the API (<see cref="FakeApi"/>): no key is read
    /// and nothing is billed. Null talks to the real API.</param>
    /// <param name="timings">For the checks.</param>
    public static Task<int> RunAsync(ArenaOptions options, TextWriter output, ApiPlaces? places = null, HttpMessageHandler? transport = null, ApiTimings? timings = null) =>
        PlayAsync(options, output, null, places, transport, timings);

    /// <summary>The same for the runs of a position test, once <see cref="PositionCommand"/> has rebuilt and checked the position.</summary>
    public static Task<int> PositionAsync(ArenaOptions options, TextWriter output, PositionStart position, ApiPlaces? places = null,
        HttpMessageHandler? transport = null, ApiTimings? timings = null) =>
        PlayAsync(options, output, position, places, transport, timings);

    private static async Task<int> PlayAsync(ArenaOptions options, TextWriter output, PositionStart? position, ApiPlaces? places,
        HttpMessageHandler? transport, ApiTimings? timings)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        places ??= ApiPlaces.OfTheGame();

        // The game's settings, without its key, and what the command line changes.
        var own = ApiSettings.WithoutKey(places);
        var settings = ApiSettings.Apply(own, options);
        var models = (options.Models.Count > 0 ? options.Models : [settings.Model]).Select(BrainSettings.ResolveModel).Distinct(StringComparer.Ordinal).ToList();
        // The planned effort of each tier's fights goes into the label, as --effort-by-tier's does.
        options = options with { Models = models, Effort = settings.Effort, EffortByTier = ApiSettings.EffortByTier(settings) };
        var label = options.Experiment;
        var parallel = options.Given.Contains("--parallel") ? options.Parallel : 1;

        // The checkout: --repo, else the game's own (repo in config.json, when it holds the play
        // prompt), else the one this was built in.
        var repoGiven = options.Repo is { } named ? RunCommand.ExpandHome(named, home) : null;
        var repo = repoGiven is not null
            ? ArenaPrompt.FindRepo(repoGiven)
            : (settings.Repo is { } configured ? ArenaPrompt.FindRepo(configured) : null) ?? ArenaPrompt.FindRepo(null);
        if (repo is null)
        {
            output.WriteLine(options.Repo is null
                ? $"Couldn't find the repo's {PromptBuilder.PlayPrompt} above this build or the current folder. Name the checkout with --repo."
                : $"--repo {options.Repo} has no {PromptBuilder.PlayPrompt}.");
            return options.Repo is null ? 1 : 2;
        }
        // The system text as the brain will build it for a fight: from the checkout, with the
        // wiki index of a copy that holds the same pages.
        var prompt = PromptBuilder.Build(repo, ArenaPrompt.Character, multiplayer: false, repo);
        var promptHash = ArenaPrompt.Hash(prompt.Text);
        var wikiPages = NotesTools.ListWiki(repo).Count;
        var wikiHash = ArenaPrompt.WikiHash(repo);

        var scenarios = position is not null ? [position.Scenario]
            : options.OnlyScenarios.Count > 0 ? options.OnlyScenarios.Select(Scenarios.FromId).ToList()
            : Scenarios.Generate(options.Scenarios, options.Seed);
        var folder = Path.GetFullPath(options.OutDir is { } dir
            ? RunCommand.ExpandHome(dir, home)
            : Path.Combine(home, ".config", "spiremind", "arena", DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)));
        var resultsPath = Path.Combine(folder, RunCommand.ResultsFile);
        var earlier = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _) : [];
        var schedule = position is null
            ? new FightSchedule(scenarios, models, parallel, RunCommand.AlreadyPlayed(earlier, options))
            : new FightSchedule(position.Scenario, models, options.Runs, parallel,
                PositionCommand.AlreadyPlayed(earlier, position, options with { Effort = options.EffortFor(position.Scenario.Tier) }));
        var toPlay = schedule.Total - schedule.Skipped;
        var what = position is null ? "fight" : "run";

        // The budget is the folder's: what earlier runs here spent on the same API counts against it.
        var standIn = transport is not null;
        var ledgerPath = Path.Combine(folder, ApiBudget.LedgerFile);
        decimal spentBefore;
        int unreadableLedger;
        try
        {
            spentBefore = ApiBudget.SpentIn(ledgerPath, standIn, out unreadableLedger);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"Nothing was started: {ledgerPath}, which says what earlier runs in this folder spent, couldn't be read ({ex.GetType().Name}: {ex.Message}).");
            return 2;
        }
        // What a fight can't do without: its first request writes the system prompt to the cache.
        // About four characters a token, which errs low. The stand-in bills a made-up usage instead.
        var firstRequest = standIn ? 0m : models.Min(model => FirstRequestUsd(prompt.Text.Length, model));

        // ---- said before anything is spent ----
        output.WriteLine(position is null
            ? $"Arena run on the API brain: {scenarios.Count} {RunCommand.Plural(scenarios.Count, "fight")} {(options.OnlyScenarios.Count > 0 ? "named with --only" : $"from seed {options.Seed}")} x {models.Count} " +
              $"{RunCommand.Plural(models.Count, "model")} ({string.Join(", ", models)}), label {label}."
            : $"Arena position test on the API brain: {PositionCommand.Name(position.Scenario.Id, position.Turn)} x {options.Runs} {RunCommand.Plural(options.Runs, "run")} x " +
              $"{models.Count} {RunCommand.Plural(models.Count, "model")} ({string.Join(", ", models)}), label {label}.");
        if (position is not null)
            output.WriteLine(PositionCommand.ModeLine(options.Reshuffle));
        output.WriteLine($"Brain: the mod's own (SpireMind.Core's ApiBrain), calling the Claude API directly: {ApiSettings.Describe(settings)}. " +
                         $"Settings from {SettingsSource(places, options)}.");
        foreach (var warning in own.Warnings)
            output.WriteLine($"  config: {warning}");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Spend: at most {toPlay} {RunCommand.Plural(toPlay, what)} on a budget of ${options.MaxUsd:0.00} for this results folder (--max-usd), ") +
            (transport is null ? "billed to your API credit. " : "but the API is a scripted stand-in here (--fake-api): no key is read and nothing is billed. ") +
            (spentBefore > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Earlier runs in this folder have already cost ${spentBefore:0.00} of it ({ApiBudget.LedgerFile}), so ${Math.Max(0, options.MaxUsd - spentBefore):0.00} is left. ")
                : "") +
            $"No {what} starts once the folder's {what}s have cost that much, or when what is left is less than the dearest {what} so far, and a running one is stopped when the budget is reached: " +
            "the request that reaches it is the last one sent, so the batch can end one request over it. " +
            $"Your spend record (spend.json) isn't touched: each {what} counts its spend in a temp folder of its own, and every answered request is written to {ApiBudget.LedgerFile} beside the results.");
        if (unreadableLedger > 0)
            output.WriteLine($"  Warning: {unreadableLedger} {RunCommand.Plural(unreadableLedger, "line")} of {ledgerPath} couldn't be read, so earlier runs here may have spent more than is counted.");
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Prompt: the brain's own system prompt, built from {repo} ({prompt.Files.Count} files, {prompt.Text.Length:N0} characters, hash {promptHash}). ") +
            "The arena adds one line of the mod to the chat; nothing else.");
        output.WriteLine($"Notes: each {what} gets a throwaway copy of strategy/learned.md and the wiki ({wikiPages} {RunCommand.Plural(wikiPages, "page")}, hash {wikiHash ?? "none"}). " +
                         $"What the brain writes goes into its result line and never reaches {repo}.");
        output.WriteLine($"{parallel} at once, {RunCommand.Minutes(options.Timeout)} min per {what} at most, {options.AfterFight} " +
                         $"{RunCommand.Plural(options.AfterFight, "request")} allowed once a fight is over.");
        if (ExperimentLabel.Switches(options.Experiments) is { Count: > 0 } switches)
            output.WriteLine($"Simulator switches: {string.Join(", ", switches)}, passed to every {what}.");
        output.WriteLine($"Results: {resultsPath}");
        if (schedule.Skipped > 0)
            output.WriteLine($"Skipping {schedule.Skipped} {RunCommand.Plural(schedule.Skipped, what)} the results already hold.");

        // The game's own monthly cap is a setting of Mark's about this key. The arena doesn't count
        // into it, but it doesn't go against it either.
        if (transport is null && own.MonthlyBudgetUsd <= 0)
        {
            output.WriteLine($"Nothing was started: {Path.Combine(places.SharedConfigDir, BrainSettings.ConfigFileName)} sets monthly_budget_usd to 0, " +
                             "which tells the mod never to use the API, so the arena doesn't either.");
            return 2;
        }
        if (transport is null && options.MaxUsd > own.MonthlyBudgetUsd)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Nothing was started: --max-usd {options.MaxUsd:0.00} is more than the game's own monthly budget (${own.MonthlyBudgetUsd:0.00}, monthly_budget_usd), and a test may not spend more than the game may in a month."));
            return 2;
        }
        if (transport is null && !options.YesSpend)
        {
            output.WriteLine(NeedsYes);
            return 2;
        }
        if (toPlay == 0)
        {
            output.WriteLine($"Nothing to play: the results already hold every {what}.");
            return 0;
        }

        var budget = new ApiBudget(options.MaxUsd, spentBefore, ledgerPath, standIn);
        // What a fight cost in earlier runs here, so this one doesn't start a fight it can't pay for.
        foreach (var played in earlier.Where(r => r.Brain == Brains.Api && r.Finished && (FightResult.Str(r.Api?["key_source"]) == "fake") == standIn))
            budget.FightEnded(played.TotalCostUsd ?? 0);
        if (budget.WhyNotStart(0, out _, firstRequest) is { } nothingLeft)
        {
            output.WriteLine($"Nothing was started: {nothingLeft}. The budget is this results folder's, earlier runs included: " +
                             "a higher --max-usd spends more here, and a new --out starts a new budget.");
            return 1;
        }
        output.WriteLine();

        Directory.CreateDirectory(folder);
        using var stop = new RunCommand.Interrupts();
        var startedAt = DateTime.UtcNow;
        string Clock()
        {
            var elapsed = DateTime.UtcNow - startedAt;
            return $"[{(int)elapsed.TotalMinutes,3}:{elapsed.Seconds:00}]";
        }

        // The client, made when the first fight is about to start. For the real API that is the
        // moment Core reads the key, from the place the game reads it (BrainWiring.LoadSettings).
        IClaudeApi? client = null;
        string? keySource = null;
        string? ClientProblem()
        {
            if (client is not null)
                return null;
            void Retry(string line) => output.WriteLine($"{Clock()}  {line}");
            if (transport is not null)
            {
                client = new ClaudeClient(FakeApi.Key, transport, log: Retry);
                keySource = "fake";
                return null;
            }
            var loaded = BrainSettings.Load(places.ConfigDir, places.GetEnv, places.SharedConfigDir);
            if (!loaded.HasApiKey)
                return $"There is no API key: the game reads it from {loaded.ApiKeyPath} (one line, chmod 600; the README shows how)" +
                       (loaded.IsInstance ? $", else {loaded.SharedApiKeyPath}" : "") + $", else from {BrainSettings.ApiKeyEnvVar}, and none of them holds one";
            if (ApiSettings.Differ(loaded, own))
                return $"{loaded.ConfigPath} changed while the batch was starting, so its fights wouldn't be played with the settings it announced; start it again";
            // The one place the key passes through the arena: from Core's loader into Core's client, as BrainWiring does it.
            client = new ClaudeClient(loaded.ApiKey!, log: Retry);
            keySource = loaded.ApiKeySource == ApiKeySource.File ? "file" : "environment";
            return null;
        }

        WriteRunFile(Path.Combine(folder, RunCommand.RunFile), options, settings, scenarios, repo, prompt, promptHash, wikiPages, wikiHash, parallel, places,
            transport is not null, position, spentBefore);

        int? Deal(int run) => position is not null && options.Reshuffle ? PositionStart.DealSeed(position.Scenario.Id, position.Turn, run) : null;
        string Name(FightSlot slot) => position is null
            ? $"{slot.Scenario.Id} ({slot.Scenario.Tier}, effort {options.EffortFor(slot.Scenario.Tier)})  {slot.Model}"
            : $"{PositionCommand.Name(slot.Scenario.Id, position.Turn)} run {slot.Run}  {slot.Model}";

        string? stopReason = null;
        var written = 0;
        var finished = 0;
        var running = new List<(FightSlot Slot, ApiFight Fight, Task<FightResult?> Task)>();
        try
        {
            while (true)
            {
                while (stopReason is null && !stop.IsCancellationRequested && running.Count < parallel && schedule.Waiting > 0)
                {
                    // The first fight of a run is played alone, to its end (the loop below only
                    // comes back here when a fight has ended): it writes the system prompt to the
                    // cache, and it tells the budget what a fight costs before several run at once.
                    if (running.Count > 0 && !budget.AnyResponse)
                        break;
                    if (budget.WhyNotStart(running.Count, out var forGood, firstRequest) is { } broke)
                    {
                        if (forGood) stopReason = broke;
                        break;
                    }
                    if (ClientProblem() is { } problem)
                    {
                        stopReason = problem;
                        break;
                    }
                    if (schedule.Next(running.Select(r => r.Slot.Model).ToList()) is not { } slot)
                        break;

                    var fight = new ApiFight
                    {
                        Scenario = slot.Scenario,
                        Model = slot.Model,
                        Settings = settings,
                        Api = client!,
                        Repo = repo,
                        Budget = budget,
                        Effort = options.EffortFor(slot.Scenario.Tier),
                        Experiments = options.Experiments,
                        Experiment = label,
                        Position = position,
                        PositionRun = position is null ? 1 : slot.Run,
                        DealSeed = Deal(slot.Run),
                        AfterFight = options.AfterFight,
                        Timeout = options.Timeout,
                        KeySource = keySource,
                        TranscriptPath = Path.Combine(folder, RunCommand.TranscriptFolder, position is null
                            ? RunCommand.TranscriptName(slot.Scenario.Id, slot.Model, label)
                            : PositionCommand.TranscriptName(slot.Scenario.Id, slot.Model, position.Turn, options.Reshuffle, slot.Run, label)),
                        ActionDelay = timings?.ActionDelay,
                        WaiterPoll = timings?.WaiterPoll,
                        WaiterSettle = timings?.WaiterSettle,
                        PollInterval = timings?.PollInterval ?? TimeSpan.FromMilliseconds(250),
                        WaitHold = timings?.WaitHold,
                    };
                    output.WriteLine($"{Clock()}  start   {Name(slot)}");
                    running.Add((slot, fight, PlayOneAsync(fight, stop.Token)));
                }
                if (running.Count == 0)
                    break;

                var done = await Task.WhenAny(running.Select(r => r.Task));
                var index = running.FindIndex(r => r.Task == done);
                var (ended, played, _) = running[index];
                running.RemoveAt(index);
                if (await done is not { } result)
                    continue; // cancelled: nothing to record

                File.AppendAllText(resultsPath, result.ToLine() + "\n");
                written++;
                finished++;
                budget.FightEnded(result.TotalCostUsd ?? 0);
                output.WriteLine($"{Clock()}  {finished,2}/{toPlay}  {Name(ended)}  {Describe(result, budget)}");
                if (played.BatchStop is { } why && stopReason is null)
                {
                    stopReason = why;
                    output.WriteLine($"{Clock()}  No more {what}s are started: {why}" + (running.Count > 0 ? $"; waiting for the {running.Count} still running." : "."));
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            output.WriteLine("Stopped.");
        }
        finally
        {
            (client as IDisposable)?.Dispose();
        }

        output.WriteLine();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"API spend of this batch: ${budget.SpentNow:0.00}") +
            (budget.Earlier > 0
                ? string.Create(CultureInfo.InvariantCulture, $"; with the earlier runs in this folder ${budget.Spent:0.00} of the ${budget.Max:0.00} budget")
                : string.Create(CultureInfo.InvariantCulture, $" of the ${budget.Max:0.00} budget")) +
            ", by the brain's own count (each response's usage at the repo's price table)." +
            (transport is null
                ? " The Claude Console has what was really billed: a request that was cut off in flight (Ctrl+C, the time limit, a retry after a timeout) may be billed without being counted here."
                : " Nothing was billed: the API was the scripted stand-in."));
        if (budget.LedgerProblem is { } ledgerProblem)
            output.WriteLine($"Warning: {ledgerProblem}.");
        var wikiAfter = ArenaPrompt.WikiHash(repo);
        if (wikiAfter != wikiHash)
            output.WriteLine($"Warning: {ClaudeCli.StrategyFolder(repo)}/wiki changed while the batch ran (hash {wikiHash ?? "none"} -> {wikiAfter ?? "none"}). " +
                             $"No {what} of this batch can write there, so something else did, and the {what}s before and after didn't get the same pages (each result's wiki_hash says which).");
        if (stop.IsCancellationRequested)
            output.WriteLine($"Stopped before the end. {written} of {toPlay} {what}s have a line in {resultsPath}.");
        else if (stopReason is not null)
            output.WriteLine($"Stopped early: {stopReason}. {written} of {toPlay} {what}s have a line in {resultsPath}; " +
                             $"the same command with --out {folder} plays the rest (and any that ended as an error or a timeout)" +
                             (budget.WhyNotStart(0, out _, firstRequest) is null ? "." : ", once it is given a higher --max-usd: the budget is the folder's, and this run used it up."));

        var all = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _) : [];
        if (position is null)
        {
            var fights = all.Where(r => r.Position is null).ToList();
            var mine = fights.Where(r => r.Experiment == label).ToList();
            if (mine.Count > 0)
            {
                output.WriteLine();
                if (mine.Count < fights.Count)
                    output.WriteLine($"{resultsPath} also holds fights of other experiments ({Report.LabelCounts(fights.Where(r => r.Experiment != label).ToList())}). " +
                                     $"The report below is of this run's ({label}); report --by-label on the folder lists them side by side and compares them.");
                Report.Print(mine, output, resultsPath, models);
            }
        }
        else if (all.Where(r => r.Position is not null).ToList() is { Count: > 0 } runs)
        {
            output.WriteLine();
            PositionReport.Print(runs, output, resultsPath, models);
        }
        return stop.IsCancellationRequested ? 130 : stopReason is not null ? 1 : 0;
    }

    // One fight, never throwing: a cancelled one is null, anything unexpected is an error line.
    private static async Task<FightResult?> PlayOneAsync(ApiFight fight, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        try
        {
            return await Task.Run(() => fight.RunAsync(ct), CancellationToken.None);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            return new FightResult
            {
                ScenarioId = fight.Scenario.Id,
                Tier = fight.Scenario.Tier,
                Summary = fight.Scenario.Summary,
                Seed = fight.Scenario.Seed,
                Model = fight.Model,
                Effort = fight.Effort,
                Experiment = fight.Experiment,
                Brain = Brains.Api,
                Outcome = Outcomes.Error,
                Error = $"{ex.GetType().Name}: {ex.Message}",
                StartedAt = startedAt,
            };
        }
    }

    /// <summary>One finished fight in the batch's own words: how it ended, what the brain sent and what it cost.</summary>
    internal static string Describe(FightResult r, ApiBudget budget)
    {
        var text = r.Outcome switch
        {
            Outcomes.Won => $"won with {r.HpLeft}/{r.MaxHp} HP",
            Outcomes.Lost => "lost",
            Outcomes.Abandoned => $"abandoned at {r.HpLeft}/{r.MaxHp} HP",
            Outcomes.Timeout => $"timed out at {r.HpLeft}/{r.MaxHp} HP (counts for nothing; the next run plays it again)",
            _ => $"error: {r.Error}",
        };
        if (r.Outcome != Outcomes.Error)
        {
            text += string.Create(CultureInfo.InvariantCulture, $", turn {r.Turns}, {r.CardsPlayed} cards, {r.WallSeconds / 60:0.0} min");
            if (r.Position is { } at)
                text += $"; turn {at.Turn}: {PositionInfo.Words(at.Line)}" + (at.LineComplete ? "" : " (the turn wasn't ended)");
        }
        var requests = (int)(FightResult.Long(r.Api?["requests"]) ?? 0);
        var efforts = r.Api?["efforts"] is JsonObject counts && counts.Count > 0 ? " at " + string.Join(", ", counts.Select(c => c.Key)) : "";
        text += $"; {requests} {RunCommand.Plural(requests, "request")}{efforts}";
        if (r.Resumes > 0)
            text += $", told to keep playing {r.Resumes} {(r.Resumes == 1 ? "time" : "times")}";
        if (r.ReadCalls > 0)
            text += $", read {r.WikiPages.Count} wiki {(r.WikiPages.Count == 1 ? "page" : "pages")}";
        if (r.Api?["notes_written"] is JsonArray { Count: > 0 } notes)
            text += $", wrote {notes.Count} notes {(notes.Count == 1 ? "file" : "files")}";
        text += string.Create(CultureInfo.InvariantCulture, $", ${r.TotalCostUsd ?? 0:0.000} (batch ${budget.Spent:0.00} of ${budget.Max:0.00})");
        return text;
    }

    /// <summary>
    /// About what the first request of a fight costs on <paramref name="model"/> at the least:
    /// a system prompt of <paramref name="promptChars"/> characters written to the one-hour cache,
    /// at four characters a token (which errs low; the tools and the reply come on top). A budget
    /// with less than this left can't pay for any fight, so none is started on it.
    /// </summary>
    public static decimal FirstRequestUsd(int promptChars, string model) =>
        promptChars / 4m * ModelPrices.For(model, out _).CacheWrite1h / 1_000_000m;

    // Where the settings came from, in words.
    private static string SettingsSource(ApiPlaces places, ArenaOptions options)
    {
        var file = Path.Combine(places.ConfigDir, BrainSettings.ConfigFileName);
        var typed = new[] { "--model", "--models", "--effort", "--elite-effort", "--boss-effort" }.Where(options.Given.Contains).ToList();
        return (File.Exists(file) ? file : $"the mod's defaults (there is no {file})") + (typed.Count > 0 ? $", with {string.Join(", ", typed)} from the command line" : "");
    }

    // The batch's settings, for whoever reads the folder later. Never the prompt's text, never the key.
    private static void WriteRunFile(string path, ArenaOptions options, BrainSettings settings, IReadOnlyList<Scenario> scenarios, string repo,
        SystemPrompt prompt, string promptHash, int wikiPages, string? wikiHash, int parallel, ApiPlaces places, bool fake, PositionStart? position, decimal spentBefore)
    {
        var efforts = new JsonObject();
        foreach (var tier in Scenarios.Tiers)
            efforts[tier] = options.EffortFor(tier);
        var json = new JsonObject
        {
            ["started_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["brain"] = Brains.Api,
            ["api"] = fake ? "a scripted stand-in (--fake-api): nothing was billed" : "the Claude API, on the API credit",
            ["models"] = new JsonArray(options.Models.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["effort"] = settings.Effort,
            ["elite_effort"] = settings.EffortFor(BrainSettings.EliteRoom),
            ["boss_effort"] = settings.EffortFor(BrainSettings.BossRoom),
            ["max_tokens"] = settings.MaxTokens is { } fixedTokens ? JsonValue.Create(fixedTokens) : JsonValue.Create("by effort"),
            ["effort_by_tier"] = efforts,
            ["settings_from"] = SettingsSource(places, options),
            ["experiment"] = options.Experiment,
            ["switches"] = new JsonObject { ["forecast"] = options.Experiments.Forecast },
            ["seed"] = position is null && options.OnlyScenarios.Count == 0 ? options.Seed : null,
            ["only"] = options.OnlyScenarios.Count > 0 ? new JsonArray(options.OnlyScenarios.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()) : null,
            ["max_usd"] = options.MaxUsd,
            ["spent_before_usd"] = spentBefore,
            ["ledger"] = ApiBudget.LedgerFile,
            ["after_fight_requests"] = options.AfterFight,
            ["parallel"] = parallel,
            ["timeout_minutes"] = options.Timeout.TotalMinutes,
            ["repo"] = repo,
            ["prompt_hash"] = promptHash,
            ["prompt_chars"] = prompt.Text.Length,
            ["prompt_files"] = new JsonArray(prompt.Files.Select(f => (JsonNode)JsonValue.Create(Path.GetRelativePath(repo, f))!).ToArray()),
            ["arena_note"] = ApiFight.ArenaNote(position?.Turn),
            ["wiki_pages"] = wikiPages,
            ["wiki_hash"] = wikiHash,
            ["scenarios"] = new JsonArray(scenarios.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id, ["seed"] = s.Seed, ["tier"] = s.Tier, ["summary"] = s.Summary,
            }).ToArray()),
        };
        if (position is not null)
            json["position"] = new JsonObject
            {
                ["scenario"] = position.Scenario.Id,
                ["turn"] = position.Turn,
                ["runs"] = options.Runs,
                ["mode"] = options.Reshuffle ? PositionInfo.Reshuffled : PositionInfo.Recorded,
                ["label"] = options.Label,
                ["state_hash"] = position.StateHash,
                ["from"] = new JsonObject { ["results"] = position.SourceFolder, ["model"] = position.SourceModel },
            };
        File.WriteAllText(path, json.ToJsonString(Indented) + "\n");
    }
}
