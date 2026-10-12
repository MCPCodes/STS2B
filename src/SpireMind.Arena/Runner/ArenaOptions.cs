using System.Globalization;

namespace SpireMind.Arena;

/// <summary>Bad command-line arguments. The message says what is wrong; <see cref="ArenaOptions.Usage"/> says what works.</summary>
public sealed class UsageException(string message) : Exception(message);

public enum ArenaCommand { Baseline, Run, Position, Report, Compare, SelfCheck, Honesty, Content, Snapshot }

/// <summary>
/// The arena's command line: <c>baseline</c> plays the scripted policies, <c>run</c> lets Claude
/// Code play on the subscription, <c>position</c> lets it play on from one turn of a recorded
/// fight, <c>report</c> prints the report of a run again, <c>compare</c> pairs the fights of two
/// runs and <c>selfcheck</c> proves the simulator and the tool path still hold. <c>honesty</c>
/// compares what the snapshots promise with what then happens, and <c>content</c> lists what
/// the simulator holds of the game and what it leaves out. See docs/arena.md.
/// (<c>values</c>, the card-value measurement, reads its own options: <see cref="ValuesCommand"/>.)
/// <para>
/// <c>run</c> has switches for experiments (<c>--brief</c>, <c>--effort-by-tier</c>,
/// <c>--forecast</c>). Each is off unless it is given, and with none given a batch is the batch
/// it was before there were any; <see cref="Experiment"/> names the ones that are on.
/// </para>
/// </summary>
public sealed record ArenaOptions
{
    public const int DefaultBaselineScenarios = 30;
    public const int DefaultHonestyScenarios = 300;
    public const int DefaultRunScenarios = 8;
    public const int DefaultParallel = 3;
    public const string DefaultEffort = "medium";
    public const double DefaultTimeoutMinutes = 30;
    public const double DefaultGraceSeconds = 20;

    /// <summary>How full the subscription's 7 day window may get before a batch stops starting fights.</summary>
    public const double DefaultStopAtWeekly = 0.90;

    /// <summary>The shortest grace: a tool call in flight when the fight ends keeps the session
    /// silent for a few seconds, and stopping it in that time would lose its result line.</summary>
    public const double LeastGraceSeconds = 10;

    /// <summary>The most scenarios one command takes, so a typo can't start a thousand fights.</summary>
    public const int MostScenarios = 500;

    /// <summary>How often each model plays a position unless <c>--runs</c> says otherwise.</summary>
    public const int DefaultRuns = 5;

    /// <summary>The most runs of one position one command takes.</summary>
    public const int MostRuns = 50;

    /// <summary>The most fights at once: each is its own Claude Code session on the same subscription.</summary>
    public const int MostParallel = 8;

    /// <summary>The effort levels Claude Code's <c>--effort</c> takes.</summary>
    public static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    public const string Usage =
        "Usage:\n" +
        "  scripts/arena.sh baseline [--scenarios N] [--seed S] [--policies A,B] [--threads K]\n" +
        "      Play every scripted policy on N generated fights (default 30) and print who won with how\n" +
        "      much HP left, overall and by tier. Half a minute for 30 fights; costs nothing.\n" +
        "      --policies names the players instead (Random,Greedy,Lookahead,Rollout): it is how the slow\n" +
        "      ones play (Rollout, Rollout:fast, Rollout:strong take seconds a fight), and it adds the\n" +
        "      seconds a fight took. --threads plays K fights at once (default 1, at most 8).\n" +
        "  scripts/arena.sh run --models a,b,c [--scenarios N] [--seed S] [--parallel K] [--effort LEVEL]\n" +
        "                       [--timeout-minutes M] [--grace-seconds G] [--stop-at-weekly F] [--out DIR]\n" +
        "                       [--claude PATH] [--repo DIR]\n" +
        "                       [--brief] [--effort-by-tier TIER=LEVEL,...] [--forecast]\n" +
        "      Let Claude Code play N generated fights (default 8) with each model on your Claude\n" +
        "      subscription, K fights at once (default 3), and append one JSON line per fight to\n" +
        "      DIR/results.jsonl (default ~/.config/spiremind/arena/<timestamp>), then print the report.\n" +
        "      Models go to Claude Code as given, so use full ids such as claude-haiku-5-5.\n" +
        "      --effort is low, medium (default), high, xhigh or max. A fight is stopped after M minutes\n" +
        "      (default 30) and then counts for nothing; once it is over Claude Code gets G seconds\n" +
        "      (default 20, at least 10) to finish by itself.\n" +
        "      The batch stops starting fights when the subscription's 7 day usage window is F full\n" +
        "      (a fraction, default 0.9), when Claude Code says the usage limit is hit, and at once when\n" +
        "      it says it is on extra usage.\n" +
        "      Fights DIR already has a result for (won, lost or abandoned) are skipped, so the same\n" +
        "      command carries on after a stop and plays timeouts and errors again. --claude names the\n" +
        "      Claude Code binary, --repo the STS2B checkout the play prompt and strategy notes are read\n" +
        "      from, and whose wiki pages the sessions may open (default: the one this was built in).\n" +
        "      A session can read under that checkout's strategy folder and can write nothing.\n" +
        "      The last three are experiments, all off unless given; the ones that are on make the\n" +
        "      run's label (brief+elite=high, or base for none), which every result line carries.\n" +
        "      --brief puts the wiki pages of the fight's enemies into the first message of each fight.\n" +
        "      --effort-by-tier starts the fights of a tier (weak, normal, elite, boss) at their own\n" +
        "      effort, e.g. elite=high,boss=high; the other tiers use --effort. --forecast is a switch\n" +
        "      of the simulator, passed on to every fight. A fight is the same fight only with the same\n" +
        "      scenario, model, effort and label, so variants can share one DIR.\n" +
        "  scripts/arena.sh position RESULTS_DIR SCENARIO MODEL --turn N [--runs K] [--models a,b] [--effort LEVEL]\n" +
        "                       [--out DIR] [--label L] [--reshuffle] [--check] [--repo DIR] [--claude PATH]\n" +
        "                       [--parallel P] [--timeout-minutes M] [--grace-seconds G] [--stop-at-weekly F]\n" +
        "                       [--brief] [--forecast]\n" +
        "      A position test. Rebuild the fight MODEL played on SCENARIO in the run RESULTS_DIR up to\n" +
        "      the start of its player turn N, compare the rebuilt fight with every snapshot that\n" +
        "      recording holds on the way (a difference stops everything), then let a new session of\n" +
        "      each model (default MODEL) take the fight over there and play it to the end, K times\n" +
        "      (default 5). Each run appends a line to DIR/results.jsonl that also holds the position,\n" +
        "      what the session did on that turn, the outcome and the HP left.\n" +
        "      Without --reshuffle every run goes on with the recorded fight's own draw order and random\n" +
        "      numbers. With it each run gets its own deal of what the player cannot know (run 3 is the\n" +
        "      same deal under every label), so the runs measure the decision and not one deal.\n" +
        "      --label names the batch (old, new) in every result line; --repo picks the checkout the\n" +
        "      prompt is read from, so the same position can be played under an earlier prompt. --check\n" +
        "      rebuilds and compares only: it prints the position and starts nothing. The other options\n" +
        "      are run's. Runs DIR already has a result for are skipped.\n" +
        "  scripts/arena.sh report DIR|results.jsonl [--by-label]\n" +
        "      Print the report for a finished or interrupted run again. Fights of different labels are\n" +
        "      not pooled into one table: --by-label lists each model once per label and compares the\n" +
        "      labels fight by fight. Position runs get their own part: per position and label the runs,\n" +
        "      wins, mean HP left and the lines taken on the position's turn with their counts.\n" +
        "  scripts/arena.sh compare DIR_A DIR_B [--model M]\n" +
        "      Pair the fights of two runs on the scenarios both played: for each model (or only M) in how\n" +
        "      many B had more HP left than A, the same, less, the mean difference and the sign test,\n" +
        "      overall and by tier, and every scenario that was won in one and lost in the other.\n" +
        "      Position runs are paired by position (scenario and turn): wins, mean HP left and the\n" +
        "      lines taken, B against A.\n" +
        "  scripts/arena.sh selfcheck [--scenarios N] [--seed S] [--checks A,B]\n" +
        "      Check the simulator on N generated fights (default 30), then play fights over HTTP\n" +
        "      through the same tools a model gets. A minute or two, costs nothing; ends with PASSED or\n" +
        "      FAILED. --checks runs only the named check classes (Cards1Checks,PotionsChecks) and nothing else.\n" +
        "  scripts/arena.sh honesty [--scenarios N] [--seed S]\n" +
        "      Play N generated fights (default 300) with every scripted policy and compare what each\n" +
        "      snapshot promised (intents, incoming damage, card numbers and costs) with what happened.\n" +
        "  scripts/arena.sh content\n" +
        "      List what the simulator holds of the game's cards, encounters and potions, and what it\n" +
        "      leaves out and why.\n" +
        "  scripts/arena.sh snapshot SCENARIO-ID [--forecast]\n" +
        "      Print the first snapshot of a scenario (normal-944074) as a model gets it from get_state,\n" +
        "      with the simulator's experiment switch on when it is given.";

    public ArenaCommand Command { get; init; }

    /// <summary>--help or -h: print <see cref="Usage"/> and exit.</summary>
    public bool Help { get; init; }

    /// <summary>How many fights to generate (baseline, run and selfcheck).</summary>
    public int Scenarios { get; init; }

    /// <summary>The seed the fights are generated from; the same seed gives the same fights.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>The models for <c>run</c>, exactly as typed, in the order typed.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];

    public int Parallel { get; init; } = DefaultParallel;

    /// <summary>The effort a fight's sessions are started with, unless
    /// <see cref="EffortByTier"/> names the fight's tier.</summary>
    public string Effort { get; init; } = DefaultEffort;

    /// <summary>
    /// <c>--effort-by-tier</c>: the effort for the fights of a tier, by tier name. Tiers it
    /// doesn't name use <see cref="Effort"/>. Empty unless the switch is given.
    /// <para>
    /// Where this would come from in the real game: the brain, which sets the effort of its own
    /// requests. A fight's first snapshot says what kind of room it is (<c>state.run.room</c>:
    /// Monster, Elite or Boss), and the act's first three hallway fights are its weak ones, so
    /// the brain knows the tier before the model's first request of the fight and can ask for
    /// that fight at another effort. Nothing is shown to the model that it doesn't see anyway.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, string> EffortByTier { get; init; } = new Dictionary<string, string>();

    /// <summary><c>--brief</c>: the first message of every fight ends with the wiki pages of
    /// the fight's enemies (<see cref="WikiBrief"/>).</summary>
    public bool Brief { get; init; }

    /// <summary><c>--forecast</c>: the simulator's own switch, handed to every fight
    /// (<see cref="ArenaGame.Experiments"/>).</summary>
    public ExperimentOptions Experiments { get; init; } = new();

    /// <summary>The run's label: the name a position test was given (<see cref="Label"/>), then
    /// the switches that are on, or <see cref="ExperimentLabel.Base"/> for nothing at all.</summary>
    public string Experiment => ExperimentLabel.Of(Brief, Experiments, EffortByTier, Label);

    /// <summary><c>position</c>: the folder (or results.jsonl) of the run the recorded fight is in.</summary>
    public string? PositionSource { get; init; }

    /// <summary><c>position</c>: the model whose recorded fight on <see cref="ScenarioId"/> is rebuilt.</summary>
    public string? PositionModel { get; init; }

    /// <summary><c>position --turn</c>: the player turn whose start the new sessions take over at.</summary>
    public int Turn { get; init; }

    /// <summary><c>position --runs</c>: how often each model plays the position.</summary>
    public int Runs { get; init; } = DefaultRuns;

    /// <summary><c>position --label</c>: the batch's own name, first part of its label; null for none.</summary>
    public string? Label { get; init; }

    /// <summary><c>position --reshuffle</c>: each run goes on with its own deal of what the
    /// player cannot know, instead of the recorded fight's.</summary>
    public bool Reshuffle { get; init; }

    /// <summary><c>position --check</c>: rebuild and compare, print the position, start nothing.</summary>
    public bool CheckOnly { get; init; }

    /// <summary>The effort the sessions of a fight of <paramref name="tier"/> are started with.</summary>
    public string EffortFor(string tier) => EffortByTier.TryGetValue(tier, out var level) ? level : Effort;

    /// <summary>How long one fight may take before Claude Code is stopped and it counts as a timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(DefaultTimeoutMinutes);

    /// <summary>The batch starts no more fights once the 7 day usage window is this full (0 to 1).</summary>
    public double StopAtWeekly { get; init; } = DefaultStopAtWeekly;

    /// <summary>How long Claude Code may stay quiet after its fight is over before it is stopped.</summary>
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(DefaultGraceSeconds);

    /// <summary>Where <c>run</c> writes; null for a new folder under ~/.config/spiremind/arena.</summary>
    public string? OutDir { get; init; }

    /// <summary>The Claude Code binary; null looks in the usual places.</summary>
    public string? ClaudePath { get; init; }

    /// <summary>The checkout the prompt is built from and the sessions read the wiki of; null
    /// finds the one this was built in.</summary>
    public string? Repo { get; init; }

    /// <summary>The folder or results.jsonl that <c>report</c> reads.</summary>
    public string? ReportPath { get; init; }

    /// <summary><c>report --by-label</c>: one row per label per model, and the labels compared
    /// fight by fight, where the plain report refuses to pool them.</summary>
    public bool ByLabel { get; init; }

    /// <summary>The two folders (or results.jsonl files) <c>compare</c> pairs: A, then B.</summary>
    public IReadOnlyList<string> ComparePaths { get; init; } = [];

    /// <summary><c>compare --model</c>: the one model to compare; null for every model both runs hold.</summary>
    public string? CompareModel { get; init; }

    /// <summary>The scenario <c>snapshot</c> prints, and the one <c>position</c> rebuilds.</summary>
    public string? ScenarioId { get; init; }

    /// <summary>For <c>selfcheck --checks</c>: the check classes to run, and nothing else. Empty runs everything.</summary>
    public IReadOnlyList<string> Checks { get; init; } = [];

    /// <summary>For <c>baseline --policies</c>: the players by name, in the order typed. Empty plays <see cref="Policies.All"/>.</summary>
    public IReadOnlyList<string> PolicyNames { get; init; } = [];

    /// <summary>For <c>baseline --threads</c>: how many fights are played at once.</summary>
    public int Threads { get; init; } = 1;

    /// <summary>
    /// Reads the arguments: the command first, then its options in any order. Throws
    /// <see cref="UsageException"/> for an unknown command or option, a missing or bad value,
    /// and an option that belongs to another command.
    /// </summary>
    public static ArenaOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            throw new UsageException($"Say what to do: {Commands}.");
        if (args.Any(a => a is "--help" or "-h"))
            return new ArenaOptions { Help = true };

        var command = args[0] switch
        {
            "baseline" => ArenaCommand.Baseline,
            "run" => ArenaCommand.Run,
            "position" => ArenaCommand.Position,
            "report" => ArenaCommand.Report,
            "compare" => ArenaCommand.Compare,
            "selfcheck" => ArenaCommand.SelfCheck,
            "honesty" => ArenaCommand.Honesty,
            "content" => ArenaCommand.Content,
            "snapshot" => ArenaCommand.Snapshot,
            var other when other.StartsWith('-') => throw new UsageException($"The command comes first ({Commands}), not '{other}'."),
            var other => throw new UsageException($"There's no '{other}' command: use {Commands}."),
        };
        var options = new ArenaOptions
        {
            Command = command,
            Scenarios = command switch
            {
                ArenaCommand.Run => DefaultRunScenarios,
                ArenaCommand.Honesty => DefaultHonestyScenarios,
                _ => DefaultBaselineScenarios,
            },
        };

        for (var i = 1; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--scenarios":
                    Only(arg, command, ArenaCommand.Baseline, ArenaCommand.Run, ArenaCommand.SelfCheck, ArenaCommand.Honesty);
                    options = options with { Scenarios = Whole(arg, Value(args, ref i), 1, MostScenarios) };
                    break;
                case "--seed":
                    Only(arg, command, ArenaCommand.Baseline, ArenaCommand.Run, ArenaCommand.SelfCheck, ArenaCommand.Honesty);
                    options = options with { Seed = Whole(arg, Value(args, ref i), 0, int.MaxValue) };
                    break;
                case "--checks":
                    Only(arg, command, ArenaCommand.SelfCheck);
                    options = options with { Checks = Value(args, ref i).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) };
                    if (options.Checks.Count == 0) throw new UsageException("--checks takes check class names separated by commas.");
                    break;
                case "--policies":
                    Only(arg, command, ArenaCommand.Baseline);
                    options = options with { PolicyNames = PolicyList(Value(args, ref i)) };
                    break;
                case "--threads":
                    Only(arg, command, ArenaCommand.Baseline);
                    options = options with { Threads = Whole(arg, Value(args, ref i), 1, MostParallel) };
                    break;
                case "--models":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { Models = ModelList(Value(args, ref i)) };
                    break;
                case "--parallel":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { Parallel = Whole(arg, Value(args, ref i), 1, MostParallel) };
                    break;
                case "--turn":
                    Only(arg, command, ArenaCommand.Position);
                    options = options with { Turn = Whole(arg, Value(args, ref i), 1, Tuning.MaxTurns) };
                    break;
                case "--runs":
                    Only(arg, command, ArenaCommand.Position);
                    options = options with { Runs = Whole(arg, Value(args, ref i), 1, MostRuns) };
                    break;
                case "--label":
                    Only(arg, command, ArenaCommand.Position);
                    var name = Value(args, ref i);
                    if (ExperimentLabel.NameProblem(name) is { } problem)
                        throw new UsageException($"--label '{name}': {problem}.");
                    options = options with { Label = name };
                    break;
                case "--reshuffle":
                    Only(arg, command, ArenaCommand.Position);
                    options = options with { Reshuffle = true };
                    break;
                case "--check":
                    Only(arg, command, ArenaCommand.Position);
                    options = options with { CheckOnly = true };
                    break;
                case "--effort":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    var effort = Value(args, ref i);
                    if (!Efforts.Contains(effort))
                        throw new UsageException($"--effort takes {string.Join(", ", Efforts)}, not '{effort}'.");
                    options = options with { Effort = effort };
                    break;
                case "--effort-by-tier":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { EffortByTier = TierEfforts(Value(args, ref i)) };
                    break;
                case "--brief":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { Brief = true };
                    break;
                case "--forecast":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position, ArenaCommand.Snapshot);
                    options = options with { Experiments = options.Experiments with { Forecast = true } };
                    break;
                case "--by-label":
                    Only(arg, command, ArenaCommand.Report);
                    options = options with { ByLabel = true };
                    break;
                case "--model":
                    Only(arg, command, ArenaCommand.Compare);
                    options = options with { CompareModel = Value(args, ref i) };
                    break;
                case "--timeout-minutes":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { Timeout = TimeSpan.FromMinutes(Positive(arg, Value(args, ref i), 0, 240)) };
                    break;
                case "--grace-seconds":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { Grace = TimeSpan.FromSeconds(Positive(arg, Value(args, ref i), LeastGraceSeconds, 600)) };
                    break;
                case "--stop-at-weekly":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { StopAtWeekly = Fraction(arg, Value(args, ref i)) };
                    break;
                case "--out":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { OutDir = Value(args, ref i) };
                    break;
                case "--claude":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { ClaudePath = Value(args, ref i) };
                    break;
                case "--repo":
                    Only(arg, command, ArenaCommand.Run, ArenaCommand.Position);
                    options = options with { Repo = Value(args, ref i) };
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new UsageException($"There's no {arg} option.");
                    if (command == ArenaCommand.Snapshot)
                    {
                        if (options.ScenarioId is not null)
                            throw new UsageException($"snapshot takes one scenario id, not '{options.ScenarioId}' and '{arg}'.");
                        options = options with { ScenarioId = arg };
                        break;
                    }
                    if (command == ArenaCommand.Position)
                    {
                        // The run the recording is in, the scenario, the model that played it: in that order.
                        if (options.PositionSource is null) options = options with { PositionSource = arg };
                        else if (options.ScenarioId is null) options = options with { ScenarioId = arg };
                        else if (options.PositionModel is null) options = options with { PositionModel = arg };
                        else throw new UsageException($"position takes a run's folder, a scenario id and a model, not '{arg}' as well.");
                        break;
                    }
                    if (command == ArenaCommand.Compare)
                    {
                        if (options.ComparePaths.Count == 2)
                            throw new UsageException($"compare takes two folders or results.jsonl files, not '{arg}' as well.");
                        options = options with { ComparePaths = [.. options.ComparePaths, arg] };
                        break;
                    }
                    if (command != ArenaCommand.Report)
                        throw new UsageException($"'{arg}' has no option name in front of it.");
                    if (options.ReportPath is not null)
                        throw new UsageException($"report takes one folder or results.jsonl, not '{options.ReportPath}' and '{arg}'.");
                    options = options with { ReportPath = arg };
                    break;
            }
        }

        if (command == ArenaCommand.Run && options.Models.Count == 0)
            throw new UsageException("run needs --models, e.g. --models claude-haiku-5-5,claude-sonnet-5-5,claude-opus-5-5.");
        if (command == ArenaCommand.Position)
        {
            if (options.PositionModel is null)
                throw new UsageException("position needs the folder of a run, a scenario id and the model whose fight is rebuilt, " +
                                         "e.g. position ~/.config/spiremind/arena/pilot-seed7 boss-878005 claude-opus-5-5 --turn 4.");
            if (options.Turn == 0)
                throw new UsageException("position needs --turn N: the player turn the new sessions take the fight over at.");
            // The model that played the recording plays the position, unless others are named.
            if (options.Models.Count == 0)
                options = options with { Models = ModelList(options.PositionModel) };
        }
        if (command == ArenaCommand.Report && options.ReportPath is null)
            throw new UsageException("report needs the folder of a run or its results.jsonl.");
        if (command == ArenaCommand.Compare && options.ComparePaths.Count != 2)
            throw new UsageException("compare needs two folders of runs (or their results.jsonl): A, then B.");
        if (command == ArenaCommand.Snapshot && options.ScenarioId is null)
            throw new UsageException("snapshot needs a scenario id, e.g. normal-944074 (baseline lists them).");
        return options;
    }

    // The commands, as the messages about a missing or unknown one list them.
    private const string Commands = "baseline, run, position, report, compare, selfcheck, honesty, content, snapshot or values";

    // An option used with a command it doesn't belong to.
    private static void Only(string option, ArenaCommand command, params ArenaCommand[] allowed)
    {
        if (!allowed.Contains(command))
            throw new UsageException($"{option} only applies to {string.Join(", ", allowed.Select(c => c.ToString().ToLowerInvariant()))}.");
    }

    // The value after an option, which must be there and not be another option.
    private static string Value(IReadOnlyList<string> args, ref int i)
    {
        var name = args[i];
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new UsageException($"{name} needs a value.");
        return args[++i];
    }

    private static int Whole(string option, string text, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < min || n > max)
            throw new UsageException(max == int.MaxValue
                ? $"{option} takes a whole number from {min} up, not '{text}'."
                : $"{option} takes a whole number from {min} to {max}, not '{text}'.");
        return n;
    }

    // A number above 0 and no smaller than min, with or without decimals.
    private static double Positive(string option, string text, double min, double max)
    {
        if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || n <= 0 || n < min || n > max)
            throw new UsageException(min > 0
                ? $"{option} takes a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}, not '{text}'."
                : $"{option} takes a number above 0, at most {max.ToString(CultureInfo.InvariantCulture)}, not '{text}'.");
        return n;
    }

    // A share of the usage window, written as a fraction: 0.9 is 90%. A percentage is turned
    // away rather than guessed at, since 90 would never be reached and so never stop anything.
    private static double Fraction(string option, string text)
    {
        if (!double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || n < 0.01 || n > 1)
            throw new UsageException($"{option} takes a fraction from 0.01 to 1 (0.9 is 90%), not '{text}'.");
        return n;
    }

    // "elite=high,boss=high": a tier of the scenarios and an effort level each, no tier twice.
    private static Dictionary<string, string> TierEfforts(string text)
    {
        const string form = "--effort-by-tier takes TIER=LEVEL pairs separated by commas, e.g. elite=high,boss=high";
        var efforts = new Dictionary<string, string>(StringComparer.Ordinal);
        var pairs = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (pairs.Length == 0)
            throw new UsageException(form + ".");
        foreach (var pair in pairs)
        {
            var parts = pair.Split('=');
            if (parts.Length != 2)
                throw new UsageException($"{form}, not '{pair}'.");
            var (tier, level) = (parts[0].Trim(), parts[1].Trim());
            if (!SpireMind.Arena.Scenarios.Tiers.Contains(tier))
                throw new UsageException($"--effort-by-tier knows the tiers {string.Join(", ", SpireMind.Arena.Scenarios.Tiers)}, not '{tier}'.");
            if (!Efforts.Contains(level))
                throw new UsageException($"--effort-by-tier takes the levels {string.Join(", ", Efforts)}, not '{level}' (for {tier}).");
            if (!efforts.TryAdd(tier, level))
                throw new UsageException($"--effort-by-tier names {tier} twice.");
        }
        return efforts;
    }

    // The players of a baseline, by the names Policies.ByName knows.
    private static List<string> PolicyList(string text)
    {
        var names = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (names.Count == 0)
            throw new UsageException("--policies takes player names separated by commas.");
        var known = string.Join(", ", Policies.All.Select(p => p.Name).Concat(Policies.OnRequest));
        if (names.FirstOrDefault(n => Policies.ByName(n) is null) is { } unknown)
            throw new UsageException($"There's no player called '{unknown}': use {known}.");
        if (names.GroupBy(n => Policies.ByName(n)!.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new UsageException($"--policies names {twice.Key} twice.");
        return names;
    }

    // "a,b,c" as typed, without empty entries. A model named twice would only repeat its fights.
    private static List<string> ModelList(string text)
    {
        var models = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (models.Count == 0)
            throw new UsageException("--models takes model ids separated by commas.");
        if (models.GroupBy(m => m, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new UsageException($"--models names {twice.Key} twice.");
        if (models.FirstOrDefault(m => m.StartsWith('-') || m.Any(char.IsWhiteSpace)) is { } odd)
            throw new UsageException($"'{odd}' can't be a model id.");
        return models;
    }
}
