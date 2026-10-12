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
/// <para>
/// A batch can be an experiment: <c>--brief</c>, <c>--effort-by-tier</c> and <c>--forecast</c>
/// are each off unless given. The ones that are on make the batch's label
/// (<see cref="ExperimentLabel"/>), which goes into every result line and run.json, and a fight
/// the folder already holds is only skipped when scenario, model, the effort it is played at
/// and the label are all the same, so variants can share a folder.
/// </para>
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
        // The mod's own brain on the API credit: no Claude Code is looked for or started.
        if (options.Brain == Brains.Api)
            return await ApiRun.RunAsync(options, output, transport: options.FakeApiTransport ? new FakeApi() : null);

        launcher ??= ProcessLauncher.Instance;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Prepare(options, output, home, out var exitCode) is not { } setup)
            return exitCode;
        var (claude, repo, readRule, prompt, promptHash, wikiPages, wikiHash, environment) = setup;
        var label = options.Experiment;

        using var stop = new Interrupts();
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
            // plays, so that fight is played again. A fight is the same fight only at the same
            // effort and under the same label: a variant never skips another's.
            var earlier = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _) : [];
            var schedule = new FightSchedule(scenarios, options.Models, options.Parallel, AlreadyPlayed(earlier, options));

            // Under --brief: which of the batch's enemies the wiki has no page for, said before
            // anything is spent. Each fight looks its own pages up again when it starts.
            var briefEnemies = options.Brief ? BriefEnemies(scenarios) : [];
            var briefPages = options.Brief ? WikiBrief.PagesByName(repo) : new Dictionary<string, string>();
            var briefMissing = briefEnemies.Where(enemy => !briefPages.ContainsKey(enemy)).ToList();

            WriteRunFile(Path.Combine(folder, RunFile), options, scenarios, claude, auth, repo, prompt, promptHash, readRule, wikiPages, wikiHash, briefMissing);

            output.WriteLine($"Arena run: {scenarios.Count} {Plural(scenarios.Count, "fight")} from seed {options.Seed} x {options.Models.Count} " +
                             $"{Plural(options.Models.Count, "model")} ({string.Join(", ", options.Models)}), effort {options.Effort}" +
                             (ExperimentLabel.TierEfforts(label) is { } tiers ? $" ({tiers})" : "") + ", " +
                             $"{options.Parallel} at once, {Minutes(options.Timeout)} min per fight at most, " +
                             $"no new fights once the 7 day usage window is {Stats.Percent(options.StopAtWeekly)} full.");
            output.WriteLine($"Claude Code: {claude}, signed in with {auth.AuthMethod ?? "an unknown method"}" +
                             (auth.SubscriptionType is { } plan ? $" ({plan})." : "."));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Prompt: the play prompt and {prompt.Files.Count - 1} strategy notes from {repo} ({prompt.Text.Length:N0} characters, hash {promptHash})."));
            output.WriteLine($"Wiki: {wikiPages} {Plural(wikiPages, "page")} under {ClaudeCli.StrategyFolder(repo)}/wiki (hash {wikiHash ?? "none"}). " +
                             $"A session may read under {ClaudeCli.StrategyFolder(repo)} and can write nothing.");
            if (label != ExperimentLabel.Base)
                output.WriteLine($"Experiment: {label}. Every result line carries this label, and fights of other labels in the same folder are left alone.");
            if (options.Brief)
                output.WriteLine($"Brief: each fight's first message ends with the wiki pages of its enemies. These fights have {briefEnemies.Count} different " +
                                 $"{Plural(briefEnemies.Count, "enemy", "enemies")}, {briefEnemies.Count - briefMissing.Count} with a page" +
                                 (briefMissing.Count > 0 ? $"; the wiki has no page for {string.Join(", ", briefMissing)}." : "."));
            if (ExperimentLabel.Switches(options.Experiments) is { Count: > 0 } switches)
                output.WriteLine($"Simulator switches: {string.Join(", ", switches)}, passed to every fight.");
            output.WriteLine($"Results: {resultsPath}");
            if (schedule.Skipped > 0)
                output.WriteLine($"Skipping {schedule.Skipped} {Plural(schedule.Skipped, "fight")} the results already hold.");
            output.WriteLine();

            var batch = new Batch(options, schedule, output, resultsPath, (fight, running) => new ClaudeFight
            {
                Scenario = fight.Scenario,
                Model = fight.Model,
                Effort = options.EffortFor(fight.Scenario.Tier),
                Brief = options.Brief,
                Experiments = options.Experiments,
                Experiment = label,
                ClaudePath = claude,
                SystemPrompt = prompt.Text,
                Repo = repo,
                Environment = environment,
                Timeout = options.Timeout,
                ExitGrace = options.Grace,
                TranscriptPath = Path.Combine(folder, TranscriptFolder, TranscriptName(fight.Scenario.Id, fight.Model, label)),
                PromptHash = promptHash,
                Launcher = launcher,
                OnUsage = running.OnUsage,
                Halt = () => running.HaltReason,
            });
            await batch.RunAsync(stop.Token);

            output.WriteLine();
            AfterBatch(batch, schedule, stop, repo, wikiHash, resultsPath, folder, "fights", output);

            // Whole fights only: a position run in the same folder has its own part of the report.
            var all = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _).Where(r => r.Position is null).ToList() : [];
            if (all.Count > 0)
            {
                output.WriteLine();
                // Fights of different labels are never pooled into one table: the report is of
                // this batch's own, and says what else the folder holds.
                var mine = all.Where(r => r.Experiment == label).ToList();
                if (mine.Count < all.Count)
                    output.WriteLine($"{resultsPath} also holds fights of other experiments ({Report.LabelCounts(all.Where(r => r.Experiment != label).ToList())}). " +
                                     $"The report below is of this run's ({label}); report --by-label on the folder lists them side by side and compares them.");
                Report.Print(mine, output, resultsPath, options.Models);
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
        }
    }

    /// <summary>What a batch needs before it can start a session: the Claude Code binary, the
    /// checkout with its Read rule, the system prompt built from it with its hash, the wiki as
    /// it stands, and the environment a session is started with.</summary>
    internal sealed record Setup(string Claude, string Repo, string ReadRule, SystemPrompt Prompt, string PromptHash, int WikiPages, string? WikiHash,
        IReadOnlyDictionary<string, string?> Environment);

    /// <summary>Finds what <see cref="Setup"/> holds for a batch run with
    /// <paramref name="options"/> (<c>--claude</c>, <c>--repo</c>, <c>--brief</c>), or says what
    /// is missing and returns null with the exit code: 2 for a path that was given and is no
    /// good, 1 for one that couldn't be found.</summary>
    internal static Setup? Prepare(ArenaOptions options, TextWriter output, string home, out int exitCode)
    {
        exitCode = 0;
        string? claude;
        if (options.ClaudePath is { } given)
        {
            claude = Path.GetFullPath(ExpandHome(given, home));
            if (!File.Exists(claude))
            {
                output.WriteLine($"--claude {claude} doesn't exist.");
                exitCode = 2;
                return null;
            }
        }
        else if ((claude = ClaudeCli.Find(home)) is null)
        {
            output.WriteLine($"Can't find Claude Code in any of {string.Join(", ", ClaudeCli.SearchPaths(home))}. " +
                             "Install it (brew install --cask claude-code) or name the binary with --claude.");
            exitCode = 1;
            return null;
        }

        if (ArenaPrompt.FindRepo(options.Repo is { } repoGiven ? ExpandHome(repoGiven, home) : null) is not { } repo)
        {
            output.WriteLine(options.Repo is null
                ? $"Couldn't find the repo's {PromptBuilder.PlayPrompt} above this build or the current folder. Name the checkout with --repo."
                : $"--repo {options.Repo} has no {PromptBuilder.PlayPrompt}.");
            exitCode = options.Repo is null ? 1 : 2;
            return null;
        }
        // The sessions read the wiki of the same checkout the prompt is built from: one path
        // goes into the prompt's appendix, the Read rule and the result lines.
        if (ClaudeCli.ReadRule(ClaudeCli.StrategyFolder(repo)) is not { } readRule)
        {
            output.WriteLine($"The checkout's path ({repo}) has a character a Claude Code permission rule can't carry (a bracket, *, ?, " +
                             "a backslash, or a folder name that starts or ends with a space), so the sessions couldn't be allowed to read its wiki. " +
                             "Use a checkout at a plainer path (--repo).");
            exitCode = 2;
            return null;
        }
        var prompt = ArenaPrompt.Build(repo, options.Brief);
        return new Setup(claude, repo, readRule, prompt, ArenaPrompt.Hash(prompt.Text, repo), NotesTools.ListWiki(repo).Count, ArenaPrompt.WikiHash(repo),
            ClaudeCli.ChildEnvironment(claude));
    }

    /// <summary>
    /// What stops a batch from outside, for as long as this lives: the first Ctrl+C and a plain
    /// kill (SIGTERM) cancel <see cref="Token"/>, so the batch stops its sessions and cleans up;
    /// a second Ctrl+C ends the process at once; and whatever ends the process kills every
    /// session still running.
    /// </summary>
    internal sealed class Interrupts : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ConsoleCancelEventHandler _onCancel;
        private readonly PosixSignalRegistration _terminate;
        private readonly EventHandler _onExit = (_, _) => ClaudeFight.Children.KillAll();
        private int _interrupts;

        public Interrupts()
        {
            _onCancel = (_, e) =>
            {
                // The first Ctrl+C stops the batch and cleans up; a second one ends the process at once.
                if (Interlocked.Increment(ref _interrupts) > 1)
                    return;
                e.Cancel = true;
                Stop();
            };
            Console.CancelKeyPress += _onCancel;
            // A plain kill (SIGTERM) stops the batch the same way, for a run started in the background.
            _terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                Stop();
            });
            AppDomain.CurrentDomain.ProcessExit += _onExit;
        }

        public CancellationToken Token => _stop.Token;

        public bool IsCancellationRequested => _stop.IsCancellationRequested;

        private void Stop()
        {
            try { _stop.Cancel(); }
            catch (ObjectDisposedException) { /* the batch already ended */ }
        }

        public void Dispose()
        {
            AppDomain.CurrentDomain.ProcessExit -= _onExit;
            Console.CancelKeyPress -= _onCancel;
            _terminate.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>What a batch says once its last fight has ended: the usage windows over the
    /// batch, whether the wiki changed under it, and why it stopped early when it did.</summary>
    /// <param name="fights">What the batch played, in the plural: "fights" or "runs".</param>
    internal static void AfterBatch(Batch batch, FightSchedule schedule, Interrupts stop, string repo, string? wikiHash, string resultsPath, string folder,
        string fights, TextWriter output)
    {
        if (batch.UsageSummary() is { } usage)
            output.WriteLine(usage);
        // No session can write to the wiki (see ClaudeCli.Arguments), so this only ever
        // differs when something else changed it meanwhile: a real run adding a lesson, say.
        var wikiAfter = ArenaPrompt.WikiHash(repo);
        output.WriteLine(wikiAfter == wikiHash
            ? $"The wiki is as it was when the batch started (hash {wikiHash ?? "none"})."
            : $"Warning: {ClaudeCli.StrategyFolder(repo)}/wiki changed while the batch ran (hash {wikiHash ?? "none"} -> {wikiAfter ?? "none"}). " +
              $"No session can write there, so something else did, and the {fights} before and after didn't read the same pages " +
              "(each result's wiki_hash says which it had). Run batches from a copy of the checkout that nothing else touches (--repo).");
        if (stop.IsCancellationRequested)
            output.WriteLine($"Stopped before the end. {batch.Written} of {schedule.Total - schedule.Skipped} {fights} have a line in {resultsPath}.");
        else if (batch.StopReason is { } why)
            output.WriteLine($"Stopped early: {why}. {batch.Written} of {schedule.Total - schedule.Skipped} {fights} have a line in {resultsPath}; " +
                             $"the same command with --out {folder} plays the rest (and any that ended as an error or a timeout) " +
                             (batch.StoppedForTools ? "once the sessions are offered the right tools." : "when there is room again."));
    }

    /// <summary>
    /// The batch itself: starts fights as the schedule and the slots allow, writes each result as
    /// it comes and stops starting new ones once Claude Code says the subscription is at its
    /// limit or the 7 day window is as full as <c>--stop-at-weekly</c> allows. The fights and
    /// their results run on one loop; only the usage windows arrive beside it, from the tasks
    /// reading the sessions' output, so what they touch is behind a lock.
    /// </summary>
    /// <param name="name">How the batch's lines name a fight; null for the scenario, its tier
    /// and the model. A position test names the turn and the run.</param>
    /// <param name="position">For a position test: what the line of a run that failed before
    /// it started still says about its position, so it is never taken for a whole fight's.</param>
    internal sealed class Batch(ArenaOptions options, FightSchedule schedule, TextWriter output, string resultsPath, Func<FightSlot, Batch, ClaudeFight> make,
        Func<FightSlot, string>? name = null, Func<FightSlot, PositionInfo>? position = null)
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
                    Effort = options.EffortFor(slot.Scenario.Tier),
                    Experiment = options.Experiment,
                    Position = position?.Invoke(slot),
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

        // The fight as the batch's lines name it; the effort only where --effort-by-tier set it.
        private string Name(FightSlot slot) => name?.Invoke(slot) ??
            $"{slot.Scenario.Id} ({slot.Scenario.Tier}{(options.EffortByTier.ContainsKey(slot.Scenario.Tier) ? ", effort " + options.EffortFor(slot.Scenario.Tier) : "")})  {slot.Model}";

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
                if (r.BriefPages is { } given)
                    text += $", given {given.Count} wiki {(given.Count == 1 ? "page" : "pages")}" +
                            (r.BriefMissing.Count > 0 ? $" (none for {string.Join(", ", r.BriefMissing)})" : "");
                if (r.ReadCalls > 0)
                    text += $", read {r.WikiPages.Count} wiki {(r.WikiPages.Count == 1 ? "page" : "pages")}" +
                            (r.ReadErrors > 0 ? $" ({r.ReadErrors} of {r.ReadCalls} Read calls failed)" : "");
                if (r.TotalCostUsd is { } usd)
                    text += string.Create(CultureInfo.InvariantCulture, $", ${usd:0.000}");
                else
                    text += ", no result line";
                // A position run: what the session did on the turn it took over at.
                if (r.Position is { } at)
                    text += $"; turn {at.Turn}: {PositionInfo.Words(at.Line)}" + (at.LineComplete ? "" : " (the turn wasn't ended)");
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
    internal static async Task<AuthStatus?> SignedInAsync(string claude, IReadOnlyDictionary<string, string?> environment,
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
    // A position test adds what it rebuilt under "position".
    internal static void WriteRunFile(string path, ArenaOptions options, IReadOnlyList<Scenario> scenarios, string claude,
        AuthStatus auth, string repo, SystemPrompt prompt, string promptHash, string readRule, int wikiPages, string? wikiHash,
        IReadOnlyList<string> briefMissing, JsonObject? position = null)
    {
        var efforts = new JsonObject();
        foreach (var tier in Scenarios.Tiers)
            if (options.EffortByTier.TryGetValue(tier, out var level))
                efforts[tier] = level;
        var json = new JsonObject
        {
            ["started_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["models"] = new JsonArray(options.Models.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["effort"] = options.Effort,
            // The experiment: its label, and each switch as it was given.
            ["experiment"] = options.Experiment,
            ["switches"] = new JsonObject
            {
                ["brief"] = options.Brief,
                ["effort_by_tier"] = efforts,
                ["forecast"] = options.Experiments.Forecast,
            },
            // The seed the batch's fights were generated from; a position test generates none.
            ["seed"] = position is null ? options.Seed : null,
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
        if (options.Brief)
            json["brief_missing"] = new JsonArray(briefMissing.Select(enemy => (JsonNode)JsonValue.Create(enemy)!).ToArray());
        if (position is not null)
            json["position"] = position;
        File.WriteAllText(path, json.ToJsonString(Indented) + "\n");
    }

    // The different enemies the batch's fights start with, as their first snapshots name them.
    internal static List<string> BriefEnemies(IReadOnlyList<Scenario> scenarios)
    {
        var enemies = new List<string>();
        foreach (var scenario in scenarios)
        {
            try
            {
                foreach (var enemy in WikiBrief.Enemies(new ArenaGame(scenario)))
                    if (!enemies.Contains(enemy, StringComparer.OrdinalIgnoreCase))
                        enemies.Add(enemy);
            }
            catch (ArgumentException)
            {
                // A scenario that can't be made into a fight fails when it is played, with its own message.
            }
        }
        return enemies;
    }

    /// <summary>
    /// The file a fight's transcript is saved in: <c>&lt;scenario&gt;__&lt;model&gt;.jsonl</c>
    /// for a run without a switch, as ever, and with the label after it for any other
    /// (<c>elite-1__claude-x__brief.jsonl</c>), so an experiment's transcript never replaces
    /// the plain run's in the same folder.
    /// </summary>
    public static string TranscriptName(string scenarioId, string model, string label) =>
        FileName(label == ExperimentLabel.Base ? $"{scenarioId}__{model}" : $"{scenarioId}__{model}__{label}") + ".jsonl";

    /// <summary>
    /// Which fights of a batch run with <paramref name="options"/> the result lines
    /// <paramref name="earlier"/> already hold: a line that counts (won, lost or abandoned) for
    /// the same scenario and model, played at the effort this batch would play it at
    /// (<see cref="ArenaOptions.EffortFor"/>) and under this batch's label
    /// (<see cref="FightResult.Identity"/>). So the same command carries on where it stopped,
    /// and a batch with other switches or another effort plays every fight of its own, beside
    /// the other's lines.
    /// </summary>
    internal static Func<Scenario, string, bool> AlreadyPlayed(IReadOnlyList<FightResult> earlier, ArenaOptions options)
    {
        var label = options.Experiment;
        // A position run of a scenario is no fight of it: it never stands in for the whole fight.
        var played = earlier.Where(r => r.Counts && r.Position is null).Select(r => r.Identity).ToHashSet();
        return (scenario, model) => played.Contains((scenario.Id, model, options.EffortFor(scenario.Tier), label));
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

    internal static string ExpandHome(string path, string home) =>
        path == "~" ? home : path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..]) : path;

    // A scenario id and model as a file name: anything that isn't plainly safe becomes "_".
    internal static string FileName(string text) =>
        string.Concat(text.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_'));

    internal static string Plural(int count, string word, string? many = null) => count == 1 ? word : many ?? word + "s";

    internal static string Minutes(TimeSpan time) => time.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
}
