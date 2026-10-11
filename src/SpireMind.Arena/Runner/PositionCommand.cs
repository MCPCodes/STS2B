using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena position RESULTS_DIR SCENARIO MODEL --turn N</c>: a position test. One turn of a
/// recorded fight is played again by new sessions, to see what a model does at that decision
/// under the prompt and tools of today (or, with <c>--repo</c>, of another checkout).
/// <list type="number">
/// <item>The fight MODEL played on SCENARIO in the run RESULTS_DIR is rebuilt up to the start of
/// its player turn N and compared with the recording on the way
/// (<see cref="PositionReplay"/>). A difference stops everything before a session is started,
/// with exit code 1.</item>
/// <item>Each model (<c>--models</c>, default MODEL) then plays the position <c>--runs</c> times
/// (default 5): every run is a new session on a new game that was put at the position
/// (<see cref="PositionStart.NewGame"/>). The session gets the play prompt, the tools and the
/// snapshots of any arena fight, sees the first snapshot of turn N, and plays to the end.</item>
/// <item>Each run appends one line to results.jsonl: a fight's line with the object
/// <c>position</c> (<see cref="PositionInfo"/>), which holds the position, what the session did
/// on that turn, and where the position came from. The report prints position runs in a part
/// of their own (<see cref="PositionReport"/>).</item>
/// </list>
/// <para>
/// A fight's random streams are fixed by its scenario's seed, so without more every run would
/// draw the same cards and meet the same enemy moves, and the runs would differ only in what the
/// model does. <c>--reshuffle</c> gives each run its own deal of what the player cannot know
/// (the draw pile's order, every random number still to come), from a seed that depends only on
/// the scenario, the turn and the run's number. Run 3 is therefore the same deal for every
/// model and under every label, and two batches can be compared deal by deal.
/// </para>
/// <para>
/// Everything about starting, stopping and carrying on is <c>arena run</c>'s: the same checks
/// before the first session, the same stops, and a run the folder already holds a result for
/// (won, lost or abandoned) is skipped, so the same command plays only what is missing.
/// </para>
/// </summary>
public static class PositionCommand
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>What a mismatch is headed with, so it can't be read past.</summary>
    public const string MismatchHeading = "THE REBUILT FIGHT DOES NOT MATCH THE RECORDING. Nothing was started.";

    /// <summary>Returns 0 when every run was started (or, under <c>--check</c>, the position
    /// holds), 1 when the rebuilt fight doesn't match the recording or the batch stopped early
    /// or couldn't start, 2 for a path, scenario or turn that doesn't exist, 130 after Ctrl+C.</summary>
    public static async Task<int> RunAsync(ArenaOptions options, TextWriter output, IProcessLauncher? launcher = null)
    {
        launcher ??= ProcessLauncher.Instance;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (scenarioId, model, turn) = (options.ScenarioId!, options.PositionModel!, options.Turn);

        // The position first: it costs nothing, and nothing may start when it doesn't hold.
        PositionStart position;
        try
        {
            var recording = Recording.Load(RunCommand.ExpandHome(options.PositionSource!, home), scenarioId, model);
            position = PositionReplay.To(recording, turn);
            // Every game the runs will get, made once now: the recorded deal, or each run's own.
            foreach (var seed in options.Reshuffle ? Enumerable.Range(1, options.Runs).Select(run => (int?)PositionStart.DealSeed(scenarioId, turn, run)) : [null])
                position.NewGame(options.Experiments, seed);
        }
        catch (FileNotFoundException ex)
        {
            output.WriteLine(ex.Message);
            return 2;
        }
        catch (ArgumentException ex)
        {
            // The scenario id is one this build can't make a fight of.
            output.WriteLine(ex.Message);
            return 2;
        }
        catch (NoSuchPosition ex)
        {
            output.WriteLine(ex.Message);
            output.WriteLine("Nothing was started.");
            return 2;
        }
        catch (ReplayMismatch ex)
        {
            output.WriteLine(MismatchHeading);
            output.WriteLine(ex.Message);
            return 1;
        }

        Describe(position, output);
        if (options.CheckOnly)
        {
            // The first snapshot as a session's first call gets it, with the simulator's switches when they are on.
            var shown = position.NewGame(options.Experiments, null).CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
            output.WriteLine();
            output.WriteLine($"The first snapshot of a run{(ExperimentLabel.Switches(options.Experiments) is { Count: > 0 } on ? $" (with {string.Join(" and ", on)})" : "")}:");
            output.WriteLine(shown.ToJson().ToJsonString(Indented));
            output.WriteLine();
            output.WriteLine("Checked only (--check): nothing was started.");
            return 0;
        }

        if (RunCommand.Prepare(options, output, home, out var exitCode) is not { } setup)
            return exitCode;
        var (claude, repo, readRule, prompt, promptHash, wikiPages, wikiHash, environment) = setup;
        var label = options.Experiment;
        var scenario = position.Scenario;
        var effort = options.Effort;

        using var stop = new RunCommand.Interrupts();
        try
        {
            if (await RunCommand.SignedInAsync(claude, environment, launcher, output, stop.Token) is not { } auth)
                return 1;

            var folder = Path.GetFullPath(options.OutDir is { } dir
                ? RunCommand.ExpandHome(dir, home)
                : Path.Combine(home, ".config", "spiremind", "arena", DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(folder);
            var resultsPath = Path.Combine(folder, RunCommand.ResultsFile);

            var earlier = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _) : [];
            var schedule = new FightSchedule(scenario, options.Models, options.Runs, options.Parallel, AlreadyPlayed(earlier, position, options));

            var briefEnemies = options.Brief ? RunCommand.BriefEnemies([scenario]) : [];
            var briefPages = options.Brief ? WikiBrief.PagesByName(repo) : new Dictionary<string, string>();
            var briefMissing = briefEnemies.Where(enemy => !briefPages.ContainsKey(enemy)).ToList();

            RunCommand.WriteRunFile(Path.Combine(folder, RunCommand.RunFile), options, [scenario], claude, auth, repo, prompt, promptHash, readRule, wikiPages, wikiHash,
                briefMissing, RunFilePosition(position, options));

            output.WriteLine();
            output.WriteLine($"Arena position test: {Name(scenario.Id, turn)} x {options.Runs} {RunCommand.Plural(options.Runs, "run")} x {options.Models.Count} " +
                             $"{RunCommand.Plural(options.Models.Count, "model")} ({string.Join(", ", options.Models)}), effort {effort}, label {label}, " +
                             $"{options.Parallel} at once, {RunCommand.Minutes(options.Timeout)} min per run at most, " +
                             $"no new runs once the 7 day usage window is {Stats.Percent(options.StopAtWeekly)} full.");
            output.WriteLine(ModeLine(options.Reshuffle));
            output.WriteLine($"Claude Code: {claude}, signed in with {auth.AuthMethod ?? "an unknown method"}" +
                             (auth.SubscriptionType is { } plan ? $" ({plan})." : "."));
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Prompt: the play prompt and {prompt.Files.Count - 1} strategy notes from {repo} ({prompt.Text.Length:N0} characters, hash {promptHash})."));
            output.WriteLine($"Wiki: {wikiPages} {RunCommand.Plural(wikiPages, "page")} under {ClaudeCli.StrategyFolder(repo)}/wiki (hash {wikiHash ?? "none"}). " +
                             $"A session may read under {ClaudeCli.StrategyFolder(repo)} and can write nothing.");
            if (options.Brief)
                output.WriteLine($"Brief: each run's first message ends with the wiki pages of the fight's enemies ({briefEnemies.Count - briefMissing.Count} of {briefEnemies.Count} have a page" +
                                 (briefMissing.Count > 0 ? $"; none for {string.Join(", ", briefMissing)})." : ")."));
            if (ExperimentLabel.Switches(options.Experiments) is { Count: > 0 } switches)
                output.WriteLine($"Simulator switches: {string.Join(", ", switches)}, passed to every run.");
            output.WriteLine($"Results: {resultsPath}");
            if (schedule.Skipped > 0)
                output.WriteLine($"Skipping {schedule.Skipped} {RunCommand.Plural(schedule.Skipped, "run")} the results already hold.");
            output.WriteLine();

            int? Deal(int run) => options.Reshuffle ? PositionStart.DealSeed(scenario.Id, turn, run) : null;
            var batch = new RunCommand.Batch(options, schedule, output, resultsPath,
                (slot, running) => new ClaudeFight
                {
                    Scenario = scenario,
                    Model = slot.Model,
                    Effort = effort,
                    Brief = options.Brief,
                    Experiments = options.Experiments,
                    Experiment = label,
                    Position = position,
                    PositionRun = slot.Run,
                    DealSeed = Deal(slot.Run),
                    UserPrompt = ArenaPrompt.PositionUserPrompt(turn),
                    ClaudePath = claude,
                    SystemPrompt = prompt.Text,
                    Repo = repo,
                    Environment = environment,
                    Timeout = options.Timeout,
                    ExitGrace = options.Grace,
                    TranscriptPath = Path.Combine(folder, RunCommand.TranscriptFolder, TranscriptName(scenario.Id, slot.Model, turn, options.Reshuffle, slot.Run, label)),
                    PromptHash = promptHash,
                    Launcher = launcher,
                    OnUsage = running.OnUsage,
                    Halt = () => running.HaltReason,
                },
                name: slot => $"{Name(scenario.Id, turn)} run {slot.Run}  {slot.Model}",
                position: slot => Unplayed(position, slot.Run, Deal(slot.Run)));
            await batch.RunAsync(stop.Token);

            output.WriteLine();
            RunCommand.AfterBatch(batch, schedule, stop, repo, wikiHash, resultsPath, folder, "runs", output);

            var runs = File.Exists(resultsPath) ? FightResult.ReadFile(resultsPath, out _).Where(r => r.Position is not null).ToList() : [];
            if (runs.Count > 0)
            {
                output.WriteLine();
                PositionReport.Print(runs, output, resultsPath, options.Models);
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

    /// <summary>A position as the tables name it: "boss-878005 T4".</summary>
    public static string Name(string scenarioId, int turn) => string.Create(CultureInfo.InvariantCulture, $"{scenarioId} T{turn}");

    /// <summary>The one line that says how the runs of a position go on from it.</summary>
    public static string ModeLine(bool reshuffle) => reshuffle
        ? "Deal: reshuffled. Each run goes on with its own deal of what the player cannot know (the draw pile's order and every random number still to come); " +
          "run 1 has the same deal under every model and label, run 2 another, and so on. The runs measure the decision over several deals."
        : "Deal: recorded. Every run goes on with the recorded fight's own draw order and random numbers, so the runs differ only in what the model does. " +
          "They measure the decision on this one deal (--reshuffle gives each run its own).";

    /// <summary>
    /// The file a position run's transcript is saved in:
    /// <c>&lt;scenario&gt;__&lt;model&gt;__turn4-recorded2.jsonl</c> for run 2 on the recorded
    /// deal, <c>...__turn4-reshuffled2__new.jsonl</c> for run 2 of a reshuffled batch labelled
    /// "new". It never has the name of a whole fight's transcript, which a position test reads.
    /// </summary>
    public static string TranscriptName(string scenarioId, string model, int turn, bool reshuffle, int run, string label) =>
        RunCommand.FileName(string.Create(CultureInfo.InvariantCulture,
            $"{scenarioId}__{model}__turn{turn}-{(reshuffle ? PositionInfo.Reshuffled : PositionInfo.Recorded)}{run}") +
                            (label == ExperimentLabel.Base ? "" : "__" + label)) + ".jsonl";

    /// <summary>
    /// Which runs of a position test the result lines <paramref name="earlier"/> already hold:
    /// a line that counts (won, lost or abandoned) for the same position (scenario, turn and
    /// state), the same deal (recorded, or reshuffled with the run's own seed), model and run
    /// number, played at this batch's effort and under its label. So the same command carries
    /// on where it stopped, and a batch under another label, effort or deal plays all of its own.
    /// </summary>
    internal static Func<string, int, bool> AlreadyPlayed(IReadOnlyList<FightResult> earlier, PositionStart position, ArenaOptions options)
    {
        var label = options.Experiment;
        var key = (position.Scenario.Id, position.Turn, position.StateHash);
        var played = earlier
            .Where(r => r.Counts && r.PositionKey == key && r.Position!.Reshuffle == options.Reshuffle && r.Effort == options.Effort && r.Experiment == label)
            .Select(r => (r.Model, r.Position!.Run))
            .ToHashSet();
        return (model, run) => played.Contains((model, run));
    }

    // What is known of a run's position before its session exists: for the line of a run that failed to start.
    private static PositionInfo Unplayed(PositionStart position, int run, int? dealSeed) => new()
    {
        Turn = position.Turn,
        Run = run,
        Reshuffle = dealSeed is not null,
        DealSeed = dealSeed,
        StateHash = position.StateHash,
        HpAtStart = position.Hp,
        ReplayedActions = position.Actions.Count,
        SnapshotsChecked = position.SnapshotsChecked,
        SourceFolder = position.SourceFolder,
        SourceModel = position.SourceModel,
        SourceOutcome = position.SourceOutcome,
        SourceHpLeft = position.SourceHpLeft,
        SourceLine = position.SourceLine,
    };

    // What was rebuilt and how far it was checked, said before anything is spent.
    private static void Describe(PositionStart position, TextWriter output)
    {
        var recorded = position.SourceOutcome switch
        {
            Outcomes.Won => $"won with {position.SourceHpLeft} HP left",
            null => "no result line",
            var other => other,
        };
        output.WriteLine($"Position: {Name(position.Scenario.Id, position.Turn)}, the start of turn {position.Turn} of {position.SourceModel}'s fight in {position.SourceFolder} ({recorded}).");
        output.WriteLine($"  {position.Scenario.Summary}");
        output.WriteLine($"Rebuilt from the scenario by applying {position.Actions.Count} recorded {RunCommand.Plural(position.Actions.Count, "action")}. " +
                         $"{position.SnapshotsChecked} recorded {RunCommand.Plural(position.SnapshotsChecked, "snapshot")} compared with the rebuilt fight, all equal " +
                         $"({position.PositionSnapshots} of the position itself)" +
                         (position.LabelsChecked > 0
                             ? $"; {position.LabelsChecked} action {RunCommand.Plural(position.LabelsChecked, "label")} compared, all equal."
                             : "; the recording names no action by label (its session ran them one at a time, each with its snapshot)."));
        output.WriteLine($"At the position: {PositionReplay.Facts(position.Snapshot)}. State hash {position.StateHash}.");
        output.WriteLine(position.SourceLine.Count > 0
            ? $"The recorded session then played: {PositionInfo.Words(position.SourceLine)}."
            : "The recording ends here: its session ran nothing on this turn.");
        foreach (var note in position.Notes)
            output.WriteLine($"Note: {note}");
    }

    // What run.json says about the position.
    private static JsonObject RunFilePosition(PositionStart position, ArenaOptions options) => new()
    {
        ["scenario"] = position.Scenario.Id,
        ["turn"] = position.Turn,
        ["runs"] = options.Runs,
        ["mode"] = options.Reshuffle ? PositionInfo.Reshuffled : PositionInfo.Recorded,
        ["label"] = options.Label,
        ["state_hash"] = position.StateHash,
        ["hp_at_start"] = position.Hp,
        ["replayed_actions"] = position.Actions.Count,
        ["snapshots_checked"] = position.SnapshotsChecked,
        ["position_snapshots"] = position.PositionSnapshots,
        ["labels_checked"] = position.LabelsChecked,
        ["display_differences"] = position.DisplayDifferences,
        ["from"] = new JsonObject
        {
            ["results"] = position.SourceFolder,
            ["model"] = position.SourceModel,
            ["outcome"] = position.SourceOutcome,
            ["hp_left"] = position.SourceHpLeft,
            ["line"] = new JsonArray(position.SourceLine.Select(label => (JsonNode)JsonValue.Create(label)!).ToArray()),
        },
        ["notes"] = new JsonArray(position.Notes.Select(note => (JsonNode)JsonValue.Create(note)!).ToArray()),
    };
}
