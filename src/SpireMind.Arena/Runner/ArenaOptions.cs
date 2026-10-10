using System.Globalization;

namespace SpireMind.Arena;

/// <summary>Bad command-line arguments. The message says what is wrong; <see cref="ArenaOptions.Usage"/> says what works.</summary>
public sealed class UsageException(string message) : Exception(message);

public enum ArenaCommand { Baseline, Run, Report, SelfCheck, Honesty, Content, Snapshot }

/// <summary>
/// The arena's command line: <c>baseline</c> plays the scripted policies, <c>run</c> lets Claude
/// Code play on the subscription, <c>report</c> prints the report of a run again and
/// <c>selfcheck</c> proves the simulator and the tool path still hold. <c>honesty</c> compares
/// what the snapshots promise with what then happens, and <c>content</c> lists what the
/// simulator holds of the game and what it leaves out. See docs/arena.md.
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

    /// <summary>The most fights at once: each is its own Claude Code session on the same subscription.</summary>
    public const int MostParallel = 8;

    /// <summary>The effort levels Claude Code's <c>--effort</c> takes.</summary>
    public static readonly IReadOnlyList<string> Efforts = ["low", "medium", "high", "xhigh", "max"];

    public const string Usage =
        "Usage:\n" +
        "  scripts/arena.sh baseline [--scenarios N] [--seed S]\n" +
        "      Play every scripted policy on N generated fights (default 30) and print who won with how\n" +
        "      much HP left, overall and by tier. Half a minute for 30 fights; costs nothing.\n" +
        "  scripts/arena.sh run --models a,b,c [--scenarios N] [--seed S] [--parallel K] [--effort LEVEL]\n" +
        "                       [--timeout-minutes M] [--grace-seconds G] [--stop-at-weekly F] [--out DIR]\n" +
        "                       [--claude PATH] [--repo DIR]\n" +
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
        "  scripts/arena.sh report DIR|results.jsonl\n" +
        "      Print the report for a finished or interrupted run again.\n" +
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
        "  scripts/arena.sh snapshot SCENARIO-ID\n" +
        "      Print the first snapshot of a scenario (normal-944074) as a model gets it from get_state.";

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

    public string Effort { get; init; } = DefaultEffort;

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

    /// <summary>The scenario <c>snapshot</c> prints.</summary>
    public string? ScenarioId { get; init; }

    /// <summary>For <c>selfcheck --checks</c>: the check classes to run, and nothing else. Empty runs everything.</summary>
    public IReadOnlyList<string> Checks { get; init; } = [];

    /// <summary>
    /// Reads the arguments: the command first, then its options in any order. Throws
    /// <see cref="UsageException"/> for an unknown command or option, a missing or bad value,
    /// and an option that belongs to another command.
    /// </summary>
    public static ArenaOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            throw new UsageException("Say what to do: baseline, run, report, selfcheck, honesty, content or snapshot.");
        if (args.Any(a => a is "--help" or "-h"))
            return new ArenaOptions { Help = true };

        var command = args[0] switch
        {
            "baseline" => ArenaCommand.Baseline,
            "run" => ArenaCommand.Run,
            "report" => ArenaCommand.Report,
            "selfcheck" => ArenaCommand.SelfCheck,
            "honesty" => ArenaCommand.Honesty,
            "content" => ArenaCommand.Content,
            "snapshot" => ArenaCommand.Snapshot,
            var other when other.StartsWith('-') => throw new UsageException($"The command comes first (baseline, run, report, selfcheck, honesty, content or snapshot), not '{other}'."),
            var other => throw new UsageException($"There's no '{other}' command: use baseline, run, report, selfcheck, honesty, content or snapshot."),
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
                case "--models":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { Models = ModelList(Value(args, ref i)) };
                    break;
                case "--parallel":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { Parallel = Whole(arg, Value(args, ref i), 1, MostParallel) };
                    break;
                case "--effort":
                    Only(arg, command, ArenaCommand.Run);
                    var effort = Value(args, ref i);
                    if (!Efforts.Contains(effort))
                        throw new UsageException($"--effort takes {string.Join(", ", Efforts)}, not '{effort}'.");
                    options = options with { Effort = effort };
                    break;
                case "--timeout-minutes":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { Timeout = TimeSpan.FromMinutes(Positive(arg, Value(args, ref i), 0, 240)) };
                    break;
                case "--grace-seconds":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { Grace = TimeSpan.FromSeconds(Positive(arg, Value(args, ref i), LeastGraceSeconds, 600)) };
                    break;
                case "--stop-at-weekly":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { StopAtWeekly = Fraction(arg, Value(args, ref i)) };
                    break;
                case "--out":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { OutDir = Value(args, ref i) };
                    break;
                case "--claude":
                    Only(arg, command, ArenaCommand.Run);
                    options = options with { ClaudePath = Value(args, ref i) };
                    break;
                case "--repo":
                    Only(arg, command, ArenaCommand.Run);
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
        if (command == ArenaCommand.Report && options.ReportPath is null)
            throw new UsageException("report needs the folder of a run or its results.jsonl.");
        if (command == ArenaCommand.Snapshot && options.ScenarioId is null)
            throw new UsageException("snapshot needs a scenario id, e.g. normal-944074 (baseline lists them).");
        return options;
    }

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
