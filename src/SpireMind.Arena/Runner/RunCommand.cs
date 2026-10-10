using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena run</c>: every model plays every generated fight once, through Claude Code headless
/// on the subscription, a few fights at a time. Each finished fight is appended to
/// results.jsonl at once, so whatever stops the batch (Ctrl+C, the usage limit, a crash) keeps
/// what was played, and the same command with the same <c>--out</c> plays only what is missing.
/// One failed fight never stops the batch. What Claude Code itself says about the subscription
/// does: the usage limit being hit, since every fight after it would fail the same way; the
/// 7 day window filling up to <c>--stop-at-weekly</c>, so a batch can't eat the week; and extra
/// usage, which stops the running sessions too, since it is billed on top of the subscription.
/// So does a session whose init line doesn't list the tools the arena asked for (the fight's
/// own and Read, nothing else): every session would be the same, and each costs a start.
/// </summary>
public static class RunCommand
{
    public const string ResultsFile = "results.jsonl";
    public const string RunFile = "run.json";
    public const string TranscriptFolder = "transcripts";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Runs the batch from the command line and returns the exit code: 0 when every
    /// fight was started, 1 when it stopped early (the usage limit, the 7 day window, extra
    /// usage) or couldn't start, 2 for a path that doesn't exist, 130 after Ctrl+C.</summary>
    public static async Task<int> RunAsync(ArenaOptions options, TextWriter output, IProcessLauncher? launcher = null)
    {
        launcher ??= ProcessLauncher.Instance;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        string? claude;
        if (options.ClaudePath is { } given)
        {
            claude = Path.GetFullPath(ExpandHome(given, home));
            if (!File.Exists(claude))
            {
                output.WriteLine($"--claude {claude} doesn't exist.");
                return 2;
            }
        }
        else if ((claude = ClaudeCli.Find(home)) is null)
        {
            output.WriteLine($"Can't find Claude Code in any of {string.Join(", ", ClaudeCli.SearchPaths(home))}. " +
                             "Install it (brew install --cask claude-code) or name the binary with --claude.");
            return 1;
        }

        if (ArenaPrompt.FindRepo(options.Repo is { } repoGiven ? ExpandHome(repoGiven, home) : null) is not { } repo)
        {
            output.WriteLine(options.Repo is null
                ? $"Couldn't find the repo's {PromptBuilder.PlayPrompt} above this build or the current folder. Name the checkout with --repo."
                : $"--repo {options.Repo} has no {PromptBuilder.PlayPrompt}.");
            return options.Repo is null ? 1 : 2;
        }
        // The sessions read the wiki of the same checkout the prompt is built from: one path
        // goes into the prompt's appendix, the Read rule and the result lines.
        if (ClaudeCli.ReadRule(ClaudeCli.StrategyFolder(repo)) is not { } readRule)
        {
            output.WriteLine($"The checkout's path ({repo}) has a character a Claude Code permission rule can't carry (a bracket, *, ?, " +
                             "a backslash, or a folder name that starts or ends with a space), so the sessions couldn't be allowed to read its wiki. " +
                             "Use a checkout at a plainer path (--repo).");
            return 2;
        }
        var prompt = ArenaPrompt.Build(repo);
        var promptHash = ArenaPrompt.Hash(prompt.Text, repo);
        var wikiPages = NotesTools.ListWiki(repo).Count;
        var wikiHash = ArenaPrompt.WikiHash(repo);
        var environment = ClaudeCli.ChildEnvironment(claude);

        using var stop = new CancellationTokenSource();
        void Stop()
        {
            try { stop.Cancel(); }
            catch (ObjectDisposedException) { /* the batch already ended */ }
        }
        var interrupts = 0;
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            // The first Ctrl+C stops the batch and cleans up; a second one ends the process at once.
            if (Interlocked.Increment(ref interrupts) > 1)
                return;
            e.Cancel = true;
            Stop();
        };
        Console.CancelKeyPress += onCancel;
        // A plain kill (SIGTERM) stops the batch the same way, for a run started in the background.
        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            Stop();
        });
        EventHandler onExit = (_, _) => ClaudeFight.Children.KillAll();
        AppDomain.CurrentDomain.ProcessExit += onExit;

        try
        {
            if (await SignedInAsync(claude, environment, launcher, output, stop.Token) is not { } auth)
                return 1;

            var scenarios = Scenarios.Generate(options.Scenarios, options.Seed);
            var folder = Path.GetFullPath(options.OutDir is { } dir
                ? ExpandHome(dir, home)
                : Path.Combine(home, ".config", "spiremind", "arena", DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(folder);
            var resultsPath = Path.Combine(folder, ResultsFile);

            // What the folder already holds. A fight that was won, lost or abandoned is the
            // model's own result and stays; an error or a timeout says nothing about how it
            // plays, so that fight is played again.
            var earlier = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _) : [];
            var played = earlier.Where(r => r.Counts && r.Effort == options.Effort)
                .Select(r => (r.ScenarioId, r.Model)).ToHashSet();
            var schedule = new FightSchedule(scenarios, options.Models, options.Parallel, (s, m) => played.Contains((s.Id, m)));

            WriteRunFile(Path.Combine(folder, RunFile), options, scenarios, claude, auth, repo, prompt, promptHash, readRule, wikiPages, wikiHash);

            output.WriteLine($"Arena run: {scenarios.Count} {Plural(scenarios.Count, "fight")} from seed {options.Seed} x {options.Models.Count} " +
                             $"{Plural(options.Models.Count, "model")} ({string.Join(", ", options.Models)}), effort {options.Effort}, " +
                             $"{options.Parallel} at once, {Minutes(options.Timeout)} min per fight at most, " +
                             $"no new fights once the 7 day usage window is {Stats.Percent(options.StopAtWeekly)} full.");
            output.WriteLine($"Claude Code: {claude}, signed in with {auth.AuthMethod ?? "an unknown method"}" +
                             (auth.SubscriptionType is { } plan ? $" ({plan})." : "."));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Prompt: the play prompt and {prompt.Files.Count - 1} strategy notes from {repo} ({prompt.Text.Length:N0} characters, hash {promptHash})."));
            output.WriteLine($"Wiki: {wikiPages} {Plural(wikiPages, "page")} under {ClaudeCli.StrategyFolder(repo)}/wiki (hash {wikiHash ?? "none"}). " +
                             $"A session may read under {ClaudeCli.StrategyFolder(repo)} and can write nothing.");
            output.WriteLine($"Results: {resultsPath}");
            if (schedule.Skipped > 0)
                output.WriteLine($"Skipping {schedule.Skipped} {Plural(schedule.Skipped, "fight")} the results already hold.");
            output.WriteLine();

            var batch = new Batch(options, schedule, output, resultsPath, (fight, running) => new ClaudeFight
            {
                Scenario = fight.Scenario,
                Model = fight.Model,
                Effort = options.Effort,
                ClaudePath = claude,
                SystemPrompt = prompt.Text,
                Repo = repo,
                Environment = environment,
                Timeout = options.Timeout,
                ExitGrace = options.Grace,
                TranscriptPath = Path.Combine(folder, TranscriptFolder, FileName($"{fight.Scenario.Id}__{fight.Model}") + ".jsonl"),
                PromptHash = promptHash,
                Launcher = launcher,
                OnUsage = running.OnUsage,
                Halt = () => running.HaltReason,
            });
            await batch.RunAsync(stop.Token);

            output.WriteLine();
            if (batch.UsageSummary() is { } usage)
                output.WriteLine(usage);
            // No session can write to the wiki (see ClaudeCli.Arguments), so this only ever
            // differs when something else changed it meanwhile: a real run adding a lesson, say.
            var wikiAfter = ArenaPrompt.WikiHash(repo);
            output.WriteLine(wikiAfter == wikiHash
                ? $"The wiki is as it was when the batch started (hash {wikiHash ?? "none"})."
                : $"Warning: {ClaudeCli.StrategyFolder(repo)}/wiki changed while the batch ran (hash {wikiHash ?? "none"} -> {wikiAfter ?? "none"}). " +
                  "No session can write there, so something else did, and the fights before and after didn't read the same pages " +
                  "(each result's wiki_hash says which it had). Run batches from a copy of the checkout that nothing else touches (--repo).");
            if (stop.IsCancellationRequested)
                output.WriteLine($"Stopped before the end. {batch.Written} of {schedule.Total - schedule.Skipped} fights have a line in {resultsPath}.");
            else if (batch.StopReason is { } why)
                output.WriteLine($"Stopped early: {why}. {batch.Written} of {schedule.Total - schedule.Skipped} fights have a line in {resultsPath}; " +
                                 $"the same command with --out {folder} plays the rest (and any that ended as an error or a timeout) " +
                                 (batch.StoppedForTools ? "once the sessions are offered the right tools." : "when there is room again."));

            var all = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _) : [];
            if (all.Count > 0)
            {
                output.WriteLine();
                Report.Print(all, output, resultsPath, options.Models);
            }
            return stop.IsCancellationRequested ? 130 : batch.StopReason is not null ? 1 : 0;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            output.WriteLine("Stopped.");
            return 130;
        }
        finally
        {
            ClaudeFight.Children.KillAll();
            AppDomain.CurrentDomain.ProcessExit -= onExit;
            Console.CancelKeyPress -= onCancel;
        }
    }

    /// <summary>
    /// The batch itself: starts fights as the schedule and the slots allow, writes each result as
    /// it comes and stops starting new ones once Claude Code says the subscription is at its
    /// limit or the 7 day window is as full as <c>--stop-at-weekly</c> allows. The fights and
    /// their results run on one loop; only the usage windows arrive beside it, from the tasks
    /// reading the sessions' output, so what they touch is behind a lock.
    /// </summary>
    private sealed class Batch(ArenaOptions options, FightSchedule schedule, TextWriter output, string resultsPath, Func<FightSlot, Batch, ClaudeFight> make)
    {
        private readonly DateTime _startedAt = DateTime.UtcNow;
        private readonly object _usageGate = new();
        private int _finished;
        private bool _warnedAboutKey, _warnedAboutPace;
        private UsageWindows? _firstUsage, _lastUsage;
        private string? _stop, _halt;

        /// <summary>Result lines written by this batch.</summary>
        public int Written { get; private set; }

        /// <summary>Why no more fights are started, once that is so: what Claude Code said about
        /// the usage limit, the 7 day window reaching <c>--stop-at-weekly</c>, or extra usage.</summary>
        public string? StopReason { get { lock (_usageGate) return _stop; } }

        /// <summary>True when what stopped the batch is a session that wasn't offered the tools
        /// the arena asked for, which no waiting cures.</summary>
        public bool StoppedForTools { get; private set; }

        /// <summary>Set when the running sessions must stop at once too: Claude Code is on extra
        /// usage, which is billed on top of the subscription. Every fight asks for it as it goes.</summary>
        public string? HaltReason { get { lock (_usageGate) return _halt; } }

        /// <summary>
        /// The usage windows as a running fight's session just reported them: the first report
        /// is printed as where the batch starts from, the last one kept for the end, and the
        /// two stops that don't wait for a fight to finish are decided here.
        /// </summary>
        public void OnUsage(UsageWindows usage)
        {
            lock (_usageGate)
            {
                if (usage.FiveHour is not null || usage.SevenDay is not null)
                {
                    if (_firstUsage is null)
                    {
                        _firstUsage = usage;
                        output.WriteLine($"{Clock()}  Subscription usage as the batch starts: 5 hour window {Used(usage.FiveHour)}, 7 day window {Used(usage.SevenDay)}.");
                    }
                    _lastUsage = usage;
                }

                if (usage.UsingOverage && _halt is null)
                {
                    _halt = StreamWatch.OverageSentence;
                    _stop ??= "Claude Code said it was on extra usage (isUsingOverage), which is billed on top of your subscription";
                    output.WriteLine($"{Clock()}  Claude Code says it is on extra usage, beyond what the subscription covers: " +
                                     "the running sessions are stopped now and no more fights are started.");
                }
                else if (usage.SevenDay is { } week && week >= options.StopAtWeekly && _stop is null)
                {
                    _stop = $"the 7 day usage window is {Stats.Percent(week)} full and --stop-at-weekly is {Stats.Percent(options.StopAtWeekly)}";
                    output.WriteLine($"{Clock()}  The 7 day usage window is {Stats.Percent(week)} full (--stop-at-weekly {Stats.Percent(options.StopAtWeekly)}): " +
                                     "no more fights are started; the ones running are left to end.");
                }

                if (usage.Status == UsageWindows.Warning && !_warnedAboutPace)
                {
                    _warnedAboutPace = true;
                    output.WriteLine($"{Clock()}  Note: Claude Code warns that the {usage.LimitType ?? "usage"} window is filling faster than it empties " +
                                     "(status allowed_warning). Requests are still served, so the batch goes on.");
                }
            }
        }

        /// <summary>The usage windows at the batch's first and last report and what lies between, or null when no session reported them.</summary>
        public string? UsageSummary()
        {
            lock (_usageGate)
            {
                if (_firstUsage is not { } first || _lastUsage is not { } last)
                    return null;
                return $"Subscription usage over the batch: 5 hour window {Change(first.FiveHour, last.FiveHour, first.FiveHourResetsAt, last.FiveHourResetsAt)}, " +
                       $"7 day window {Change(first.SevenDay, last.SevenDay, first.SevenDayResetsAt, last.SevenDayResetsAt)}.";
            }
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var toPlay = schedule.Waiting;
            var running = new List<(FightSlot Slot, Task<FightResult?> Task)>();
            while (true)
            {
                while (StopReason is null && !ct.IsCancellationRequested && running.Count < options.Parallel
                       && schedule.Next(running.Select(r => r.Slot.Model).ToList()) is { } slot)
                {
                    output.WriteLine($"{Clock()}  start   {Name(slot)}");
                    running.Add((slot, PlayAsync(slot, ct)));
                }
                if (running.Count == 0)
                    break;

                var done = await Task.WhenAny(running.Select(r => r.Task));
                var index = running.FindIndex(r => r.Task == done);
                var finished = running[index].Slot;
                running.RemoveAt(index);
                if (await done is not { } result)
                    continue; // cancelled: nothing to record

                File.AppendAllText(resultsPath, result.ToLine() + "\n");
                Written++;
                _finished++;
                output.WriteLine($"{Clock()}  {_finished,2}/{toPlay}  {Name(finished)}  {Describe(result)}");
                if (result.ApiKeySource is not (null or "none") && !_warnedAboutKey)
                {
                    _warnedAboutKey = true;
                    output.WriteLine($"{Clock()}  Warning: Claude Code names an API key source ({result.ApiKeySource}) for that session, " +
                                     "so it may not be running on your subscription. Ctrl+C stops the batch.");
                }

                if (result.UsageLimit is { } limit)
                {
                    bool first;
                    lock (_usageGate)
                    {
                        first = _stop is null;
                        _stop ??= $"Claude Code says your subscription's usage limit is hit (\"{limit}\")";
                    }
                    if (first)
                        output.WriteLine($"{Clock()}  Claude Code says the subscription's usage limit is hit, so no more fights are started" +
                                         (running.Count > 0 ? $"; waiting for the {running.Count} still running." : "."));
                }

                // The tools a session is offered depend on the Claude Code that is installed and
                // the flags it gets, never on the fight: the next session would be stopped at
                // its init line too, and each one stopped has already cost its start.
                if (result.ToolsProblem is not null)
                {
                    bool first;
                    lock (_usageGate)
                    {
                        first = _stop is null;
                        _stop ??= "a session's init line didn't list the tools the arena asked for (the fight's own and Read, nothing else); that fight's error says what it listed";
                    }
                    StoppedForTools |= first;
                    if (first)
                        output.WriteLine($"{Clock()}  That session wasn't offered the tools the arena asked for, and every session would be the same, so no more fights are started" +
                                         (running.Count > 0 ? $"; waiting for the {running.Count} still running." : "."));
                }
            }
        }

        // One fight, never throwing: a cancelled one is null, anything unexpected is an error line.
        private async Task<FightResult?> PlayAsync(FightSlot slot, CancellationToken ct)
        {
            var startedAt = DateTime.UtcNow;
            try
            {
                return await Task.Run(() => make(slot, this).RunAsync(ct), CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                return new FightResult
                {
                    ScenarioId = slot.Scenario.Id,
                    Tier = slot.Scenario.Tier,
                    Summary = slot.Scenario.Summary,
                    Seed = slot.Scenario.Seed,
                    Model = slot.Model,
                    Effort = options.Effort,
                    Outcome = Outcomes.Error,
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                    StartedAt = startedAt,
                };
            }
        }

        private string Clock()
        {
            var elapsed = DateTime.UtcNow - _startedAt;
            return $"[{(int)elapsed.TotalMinutes,3}:{elapsed.Seconds:00}]";
        }

        private static string Name(FightSlot slot) => $"{slot.Scenario.Id} ({slot.Scenario.Tier})  {slot.Model}";

        private static string Describe(FightResult r)
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
                if (r.RefusedActions > 0)
                    text += $", {r.RefusedActions} refused";
                if (r.Resumes > 0)
                    text += $", resumed {r.Resumes} {(r.Resumes == 1 ? "time" : "times")}";
                if (r.ReadCalls > 0)
                    text += $", read {r.WikiPages.Count} wiki {(r.WikiPages.Count == 1 ? "page" : "pages")}" +
                            (r.ReadErrors > 0 ? $" ({r.ReadErrors} of {r.ReadCalls} Read calls failed)" : "");
                if (r.TotalCostUsd is { } usd)
                    text += string.Create(CultureInfo.InvariantCulture, $", ${usd:0.000}");
                else
                    text += ", no result line";
            }
            return text;
        }

        private static string Used(double? fraction) => fraction is { } f ? Stats.Percent(f) + " used" : "not reported";

        // "2% -> 9% used (+7 points)", and a word when the window started anew in between.
        private static string Change(double? from, double? to, long? fromResets, long? toResets)
        {
            if (from is not { } a || to is not { } b)
                return Used(to ?? from);
            var points = (int)Math.Round(b * 100) - (int)Math.Round(a * 100);
            var reset = fromResets is not null && toResets is not null && fromResets != toResets ? "; the window reset in between" : "";
            return $"{Stats.Percent(a)} -> {Stats.Percent(b)} used ({points.ToString("+0;-0;+0", CultureInfo.InvariantCulture)} points{reset})";
        }
    }

    // claude auth status must say it is signed in with a Claude subscription, or the batch
    // doesn't start: a session on an API key would be billed to the API credit.
    private static async Task<AuthStatus?> SignedInAsync(string claude, IReadOnlyDictionary<string, string?> environment,
        IProcessLauncher launcher, TextWriter output, CancellationToken ct)
    {
        AuthStatus? status;
        try
        {
            status = await ClaudeCli.AuthStatusAsync(claude, environment, launcher, TimeSpan.FromSeconds(15), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            output.WriteLine($"Couldn't start {claude}: {ex.Message}");
            return null;
        }
        if (status is not { LoggedIn: true })
        {
            output.WriteLine($"Claude Code ({claude}) isn't signed in, or `claude auth status` didn't answer. Run claude once in Terminal and log in with your subscription.");
            return null;
        }
        if (status.AuthMethod != ClaudeCli.SubscriptionAuth)
        {
            output.WriteLine($"Claude Code is signed in with {status.AuthMethod ?? "an unknown method"}, not a Claude subscription ({ClaudeCli.SubscriptionAuth}), " +
                             "so these fights wouldn't run on your subscription. Run claude in Terminal and /login with the subscription first.");
            return null;
        }
        return status;
    }

    // The batch's settings, for whoever reads the folder later. Never the prompt's text.
    private static void WriteRunFile(string path, ArenaOptions options, IReadOnlyList<Scenario> scenarios, string claude,
        AuthStatus auth, string repo, SystemPrompt prompt, string promptHash, string readRule, int wikiPages, string? wikiHash)
    {
        var json = new JsonObject
        {
            ["started_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["models"] = new JsonArray(options.Models.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["effort"] = options.Effort,
            ["seed"] = options.Seed,
            ["parallel"] = options.Parallel,
            ["timeout_minutes"] = options.Timeout.TotalMinutes,
            ["grace_seconds"] = options.Grace.TotalSeconds,
            ["stop_at_weekly"] = options.StopAtWeekly,
            ["max_resumes"] = ClaudeFight.MaxResumes,
            ["claude"] = claude,
            ["auth_method"] = auth.AuthMethod,
            ["subscription"] = auth.SubscriptionType,
            ["repo"] = repo,
            ["prompt_hash"] = promptHash,
            ["prompt_chars"] = prompt.Text.Length,
            ["prompt_files"] = new JsonArray(prompt.Files.Select(f => (JsonNode)JsonValue.Create(Path.GetRelativePath(repo, f))!).ToArray()),
            // What a session may open besides the fight's tools, and the wiki it finds there.
            ["session_tools"] = ClaudeCli.ReadTool,
            ["read_rule"] = readRule,
            ["wiki_pages"] = wikiPages,
            ["wiki_hash"] = wikiHash,
            ["scenarios"] = new JsonArray(scenarios.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s.Id, ["seed"] = s.Seed, ["tier"] = s.Tier, ["summary"] = s.Summary,
            }).ToArray()),
        };
        File.WriteAllText(path, json.ToJsonString(Indented) + "\n");
    }

    /// <summary>The models of the batch that wrote <paramref name="folder"/>, in the order they
    /// were given, from its run.json; empty when there is none to read.</summary>
    public static IReadOnlyList<string> ModelsOf(string folder)
    {
        try
        {
            var path = Path.Combine(folder, RunFile);
            return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject json && json["models"] is JsonArray models
                ? models.Select(FightResult.Str).OfType<string>().ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static string ExpandHome(string path, string home) =>
        path == "~" ? home : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..]) : path;

    // A scenario id and model as a file name: anything that isn't plainly safe becomes "_".
    private static string FileName(string text) =>
        string.Concat(text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";

    private static string Minutes(TimeSpan time) => time.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
}
