using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena report</c>, also printed at the end of <c>arena run</c>: per model how often it won
/// and at what price, per scenario each model's fight next to the scripted policies' on the same
/// fight, and a paired comparison of the HP the models had left. docs/arena.md says what the
/// columns mean and how much (little) a handful of fights can show.
/// <para>
/// Fights of different experiments (<see cref="FightResult.Experiment"/>) are never pooled into
/// one table: the plain report refuses a file that holds several labels and says so.
/// <c>report --by-label</c> lists each model once per label and compares the labels fight by
/// fight on the same scenarios; <c>arena compare</c> (<see cref="CompareCommand"/>) does the
/// same across two folders.
/// </para>
/// <para>
/// Position runs (<c>arena position</c>, lines with a <c>position</c> object) are no fights of
/// their scenario: every table here leaves them out, and <see cref="Run"/> prints them in a
/// part of their own (<see cref="PositionReport"/>).
/// </para>
/// </summary>
public static class Report
{
    private static readonly string[] MainTools = ["wait_for_decision", "act_sequence", "act", "get_state", "say"];

    // What every wiki page's path starts with in a result line; left off where the report lists pages.
    private const string Wiki = SpireMind.Core.Brain.NotesTools.WikiFolder + "/";

    /// <summary><c>arena report DIR|results.jsonl [--by-label]</c>. Returns 1 when there is no
    /// such file, and when the file holds fights of several experiments and <c>--by-label</c>
    /// wasn't given: those are not pooled into one table.</summary>
    public static int Run(ArenaOptions options, TextWriter output)
    {
        if (Read(options.ReportPath!, output) is not var (path, all))
            return 1;
        var order = RunCommand.ModelsOf(Path.GetDirectoryName(path)!);
        // Whole fights and position runs are two reports: a file can hold either or both.
        var results = all.Where(r => r.Position is null).ToList();
        var positions = all.Where(r => r.Position is not null).ToList();
        var code = 0;
        if (results.Count > 0 || positions.Count == 0)
        {
            if (options.ByLabel)
                PrintByLabel(results, output, path, order);
            else if (MixedLabels(results, path) is { } refusal)
            {
                output.WriteLine(refusal);
                code = 1;
            }
            else
                Print(results, output, path, order);
        }
        if (positions.Count > 0)
        {
            if (results.Count > 0)
                output.WriteLine();
            PositionReport.Print(positions, output, path, order);
        }
        return code;
    }

    /// <summary>The results of a run's folder or results.jsonl, with the file's full path; null,
    /// after saying so, when there is no such file.</summary>
    internal static (string Path, List<FightResult> Results)? Read(string given, TextWriter output)
    {
        var path = Path.GetFullPath(given);
        if (Directory.Exists(path))
            path = Path.Combine(path, RunCommand.ResultsFile);
        if (!File.Exists(path))
        {
            output.WriteLine($"There's no {path}.");
            return null;
        }
        var results = FightResult.ReadFile(path, out var unreadable);
        if (unreadable > 0)
            output.WriteLine($"Skipped {unreadable} {(unreadable == 1 ? "line" : "lines")} of {path} that {(unreadable == 1 ? "isn't a result" : "aren't results")}.");
        return (path, results);
    }

    /// <summary>
    /// What the plain report says instead of a table when <paramref name="results"/> hold
    /// fights of more than one experiment, or null when they are all of one. Two labels are two
    /// different treatments of the model, so one win rate over both would describe neither.
    /// </summary>
    public static string? MixedLabels(IReadOnlyList<FightResult> results, string? source = null)
    {
        if (results.Select(r => r.Experiment).Distinct(StringComparer.Ordinal).Count() < 2)
            return null;
        return $"{(source is null ? "These results hold" : source + " holds")} fights of {results.Select(r => r.Experiment).Distinct(StringComparer.Ordinal).Count()} " +
               $"experiments ({LabelCounts(results)}). Fights played under different switches are not pooled into one table. " +
               "Run report with --by-label to see each model once per label and the labels compared fight by fight.";
    }

    /// <summary>The labels in <paramref name="results"/> with how many result lines each has: "base 40, brief 38".</summary>
    internal static string LabelCounts(IReadOnlyList<FightResult> results) =>
        string.Join(", ", ExperimentLabel.Ordered(results.Select(r => r.Experiment))
            .Select(label => $"{label} {results.Count(r => r.Experiment == label)}"));

    /// <summary>
    /// One row of the per-model table: a model at one <c>--effort</c> under one experiment.
    /// <see cref="Effort"/> is null when no line can tell the batch's <c>--effort</c>, because
    /// the label names an effort for every tier that was played.
    /// </summary>
    internal sealed record Player(string Model, string? Effort, string Label)
    {
        /// <summary>
        /// True for a line that is one of this player's fights. A fight whose tier the label
        /// gives its own effort (<see cref="FightResult.BaseEffort"/> null) would have been the
        /// same fight under any <c>--effort</c>, so it belongs to the model's players at every
        /// <c>--effort</c> under that label, which is also why a batch skips it.
        /// </summary>
        public bool Played(FightResult r) =>
            r.Model == Model && r.Experiment == Label && (r.BaseEffort is not { } effort || effort == Effort);

        /// <summary>The experiment and effort in words: "experiment brief, effort medium (elite high)".</summary>
        public string Describe()
        {
            var tiers = ExperimentLabel.TierEfforts(Label);
            return $"experiment {Label}" +
                   (Effort is not null ? $", effort {Effort}" + (tiers is null ? "" : $" ({tiers})") : tiers is null ? "" : $", effort by tier ({tiers})");
        }
    }

    /// <summary>
    /// The players in <paramref name="results"/>, in the order the tables list them: models in
    /// the order of <paramref name="order"/> (then by name), under each its labels with "base"
    /// first, under each label its <c>--effort</c>s from low to max. A model played at two
    /// efforts is two players; a batch run with <c>--effort-by-tier</c> is one, whose fights
    /// differ in effort by tier.
    /// </summary>
    internal static List<Player> PlayersOf(IReadOnlyList<FightResult> results, IReadOnlyList<string>? order = null)
    {
        int Place(string model) => order?.ToList().IndexOf(model) is { } at and >= 0 ? at : int.MaxValue;
        var labels = ExperimentLabel.Ordered(results.Select(r => r.Experiment));
        var players = new List<Player>();
        foreach (var model in results.Select(r => r.Model).Distinct().OrderBy(Place).ThenBy(m => m, StringComparer.Ordinal))
            foreach (var label in labels)
            {
                var lines = results.Where(r => r.Model == model && r.Experiment == label).ToList();
                if (lines.Count == 0)
                    continue;
                var efforts = lines.Select(r => r.BaseEffort).OfType<string>().Distinct()
                    .OrderBy(effort => ArenaOptions.Efforts.ToList().IndexOf(effort)).ToList();
                if (efforts.Count == 0)
                    players.Add(new Player(model, null, label));
                else
                    players.AddRange(efforts.Select(effort => new Player(model, effort, label)));
            }
        return players;
    }

    /// <summary>
    /// The fights of a set of result lines, one per player and scenario: the last line that
    /// counts (won, lost or abandoned), else the last line there is (a timeout or an error), so
    /// a fight played again after one of those replaces it.
    /// </summary>
    internal sealed class Sheet
    {
        private readonly Dictionary<(string Scenario, string Name), FightResult> _fights = [];

        /// <param name="withLabel">Name each player with its label ("claude-x [brief]"), for
        /// tables that hold several.</param>
        public Sheet(IReadOnlyList<FightResult> results, IReadOnlyList<string>? order, bool withLabel)
        {
            Players = PlayersOf(results, order);
            // A model played at two efforts is two players.
            var severalEfforts = Players.Select(p => p.Effort).OfType<string>().Distinct().Count() > 1;
            Names = Players
                .Select(p => p.Model + (severalEfforts && p.Effort is not null ? $" ({p.Effort})" : "") + (withLabel ? $" [{p.Label}]" : ""))
                .ToList();
            Scenarios = results.Select(r => r.ScenarioId).Distinct().ToList();
            for (var i = 0; i < Players.Count; i++)
                foreach (var lines in results.Where(Players[i].Played).GroupBy(r => r.ScenarioId))
                    _fights[(lines.Key, Names[i])] = lines.LastOrDefault(r => r.Counts) ?? lines.Last();
        }

        public List<Player> Players { get; }

        /// <summary>The players' names as the tables show them, in the players' order.</summary>
        public List<string> Names { get; }

        /// <summary>The scenarios in the order their first lines stand in the file.</summary>
        public List<string> Scenarios { get; }

        public FightResult? Fight(string scenario, string name) => _fights.GetValueOrDefault((scenario, name));

        /// <summary>A player's fights, one per scenario it has a line on.</summary>
        public List<FightResult> FightsOf(string name) => Scenarios.Select(id => Fight(id, name)).OfType<FightResult>().ToList();

        /// <summary>Every fight once, also one that two players share.</summary>
        public List<FightResult> All => _fights.Values.Distinct(ReferenceEqualityComparer.Instance).Cast<FightResult>().ToList();
    }

    /// <summary>
    /// Prints the report for <paramref name="results"/>. Each model's fight on a scenario is
    /// taken once: the last line that counts (won, lost or abandoned), else its last line (a
    /// timeout or an error), so a fight played again after one of those replaces it.
    /// </summary>
    /// <param name="order">The models in the order to list them (the order of <c>--models</c>);
    /// models it doesn't name follow by name.</param>
    public static void Print(IReadOnlyList<FightResult> results, TextWriter output, string? source = null, IReadOnlyList<string>? order = null)
    {
        // A position run is no fight of its scenario.
        results = results.Where(r => r.Position is null).ToList();
        if (results.Count == 0)
        {
            output.WriteLine(source is null ? "No fights to report." : $"No fights in {source}.");
            return;
        }

        // Two labels are two treatments: never one table.
        if (MixedLabels(results, source) is { } refusal)
        {
            output.WriteLine(refusal);
            return;
        }

        var sheet = new Sheet(results, order, withLabel: false);
        var (players, scenarios) = (sheet.Names, sheet.Scenarios);
        var label = results[0].Experiment;
        // The effort: the batch's own when there is one, with the tiers the label gives another.
        var efforts = sheet.Players.Select(p => p.Effort).OfType<string>().Distinct().ToList();
        var tiers = ExperimentLabel.TierEfforts(label);
        var effort = efforts.Count switch
        {
            1 => $", effort {efforts[0]}" + (tiers is null ? "" : $" ({tiers})"),
            0 => tiers is null ? "" : $", effort by tier ({tiers})",
            _ => tiers is null ? "" : $", {tiers} at every effort",
        };

        Header("Arena report", $"by {players.Count} {(players.Count == 1 ? "model" : "models")}{effort}, experiment {label}", sheet, source, output);
        Warnings(results, output);
        Usage(results, output);
        SameFights(sheet, "models", "model", output);
        output.WriteLine();

        PerModel(players, sheet, output);
        output.WriteLine();
        ToolCalls(players, sheet, output);

        var baselines = Baselines(scenarios);
        output.WriteLine();
        PerScenario(players, scenarios, sheet.Fight, baselines, results, output);
        output.WriteLine();
        Paired(players, scenarios, sheet.Fight, baselines, output);
    }

    /// <summary>
    /// <c>arena report --by-label</c>: the same tables with one row per model and label
    /// ("claude-x [brief]"), and in place of the comparison between models the comparison
    /// between each model's own rows: its labels (and efforts) on the same scenarios, fight by
    /// fight (<see cref="PairedVariants"/>).
    /// </summary>
    public static void PrintByLabel(IReadOnlyList<FightResult> results, TextWriter output, string? source = null, IReadOnlyList<string>? order = null)
    {
        results = results.Where(r => r.Position is null).ToList();
        if (results.Count == 0)
        {
            output.WriteLine(source is null ? "No fights to report." : $"No fights in {source}.");
            return;
        }

        var sheet = new Sheet(results, order, withLabel: true);
        var (players, scenarios) = (sheet.Names, sheet.Scenarios);
        var labels = ExperimentLabel.Ordered(results.Select(r => r.Experiment));
        var models = sheet.Players.Select(p => p.Model).Distinct().Count();
        Header("Arena report by experiment",
            $"by {models} {(models == 1 ? "model" : "models")} under {labels.Count} {(labels.Count == 1 ? "experiment" : "experiments")} ({string.Join(", ", labels)})",
            sheet, source, output);
        Warnings(results, output);
        Usage(results, output);
        SameFights(sheet, "rows", "row", output);
        output.WriteLine();

        PerModel(players, sheet, output);
        output.WriteLine();
        ToolCalls(players, sheet, output);

        output.WriteLine();
        PerScenario(players, scenarios, sheet.Fight, Baselines(scenarios), results, output);
        output.WriteLine();
        PairedVariants(sheet, output);
    }

    // The report's first line: how many fights count, on how many scenarios, by whom, and how
    // many ran out of time or couldn't be played.
    private static void Header(string title, string who, Sheet sheet, string? source, TextWriter output)
    {
        var fights = sheet.All;
        var counted = fights.Count(r => r.Counts);
        var timeouts = fights.Count(r => r.Outcome == Outcomes.Timeout);
        var errors = fights.Count(r => !r.Counts) - timeouts;
        output.WriteLine($"{title}: {counted} {(counted == 1 ? "fight" : "fights")} on {sheet.Scenarios.Count} " +
                         $"{(sheet.Scenarios.Count == 1 ? "scenario" : "scenarios")} {who}" +
                         (timeouts > 0 ? $", plus {timeouts} that ran out of time" : "") +
                         (errors > 0 ? $"{(timeouts > 0 ? " and" : ", plus")} {errors} that couldn't be played" : "") +
                         (source is null ? "." : $" ({source})."));
        if (timeouts + errors > 0)
            output.WriteLine("Fights that ran out of time or couldn't be played count for nothing below; the same run command with the same --out plays them again.");
    }

    // The per-model rows only compare like with like when every model's fights are the same ones.
    private static void SameFights(Sheet sheet, string rows, string row, TextWriter output)
    {
        var (players, scenarios) = (sheet.Names, sheet.Scenarios);
        var played = players.Select(p => scenarios.Where(id => sheet.Fight(id, p) is { Counts: true }).ToList()).ToList();
        if (played.Any(mine => !mine.SequenceEqual(played[0])))
            output.WriteLine($"Note: the {rows} didn't all play the same fights (" +
                             string.Join(", ", players.Select((p, i) => $"{p} {played[i].Count}")) +
                             $" of {scenarios.Count}), so the per-{row} rows cover different fights. Compare {rows} on the paired table, or play the missing fights first.");
    }

    // Things that make the numbers below mean something else than they seem to.
    private static void Warnings(IReadOnlyList<FightResult> results, TextWriter output)
    {
        var keyed = results.Select(r => r.ApiKeySource).Where(s => s is not (null or "none")).Distinct().ToList();
        if (keyed.Count > 0)
            output.WriteLine($"Warning: some sessions report an API key source ({string.Join(", ", keyed)}), so they may not have run on the subscription.");
        // Two brains are two players: the mod's own brain on the API, or Claude Code on the subscription.
        var api = results.Where(r => r.Brain == Brains.Api).ToList();
        if (api.Count > 0)
        {
            // A fight played against the scripted stand-in (--fake-api) cost nothing, whatever its count says.
            var standIn = api.Count(r => FightResult.Str(r.Api?["key_source"]) == "fake");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"Brain: {api.Count} of these {(results.Count == 1 ? "fight was" : "fights were")} played by the mod's own brain on the API (brain api), the program that plays the real game. ") +
                string.Create(CultureInfo.InvariantCulture, $"Their $/fight is the brain's own count of what its requests cost, ${api.Sum(r => r.TotalCostUsd ?? 0):0.00} in all, timeouts and errors included") +
                (standIn == api.Count ? "; they ran against the scripted stand-in (--fake-api), so nothing was billed."
                    : standIn > 0 ? $", billed to the API credit except for the {standIn} that ran against the scripted stand-in (--fake-api)."
                    : ", billed to the API credit.") +
                (api.Count < results.Count ? " The others were played by Claude Code on the subscription." : ""));
        }
        var prompts = results.Select(r => r.PromptHash).Where(h => h is not null).Distinct().Count();
        if (prompts > 1)
            output.WriteLine($"Note: these fights were played with {prompts} different system prompts; the play prompt or the strategy notes changed in between.");
        // The pages' text isn't in the prompt, so the wiki has a hash of its own. Lines from
        // before the sessions could read it have none and aren't counted as a version.
        var wikis = results.Select(r => r.WikiHash).Where(h => h is not null).Distinct().Count();
        if (wikis > 1)
            output.WriteLine($"Note: the wiki's pages changed between these fights ({wikis} versions of strategy/wiki), so the sessions didn't all read the same pages.");
        // What was billed, when it isn't the model asked for or a dated snapshot of it: an alias
        // that means an older model, or a helper model beside it.
        foreach (var group in results.Where(r => r.Counts).GroupBy(r => r.Model))
        {
            var used = group
                .SelectMany(r => r.ModelsUsed.Count > 0 ? r.ModelsUsed : r.CliModel is { } named ? [named] : [])
                .Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (used.Any(id => !id.StartsWith(group.Key, StringComparison.OrdinalIgnoreCase)))
                output.WriteLine($"Note: the fights asked of {group.Key} used {string.Join(", ", used)}.");
        }
    }

    // What Claude Code said about the subscription's windows during these fights: each line
    // holds what its fight saw last, and the file is in the order the fights ended, so its first
    // and last lines with numbers span the batch. The windows are the whole account's, so this
    // is the batch's footprint (and that of anything else using the subscription meanwhile),
    // and it can't be split by model.
    private static void Usage(IReadOnlyList<FightResult> results, TextWriter output)
    {
        string? Window(Func<FightResult, double?> of)
        {
            var values = results.Select(of).OfType<double>().ToList();
            return values.Count == 0 ? null
                : Stats.Percent(values[0]) == Stats.Percent(values[^1]) ? $"{Stats.Percent(values[^1])} used"
                : $"{Stats.Percent(values[0])} -> {Stats.Percent(values[^1])} used";
        }
        var five = Window(r => r.UsageFiveHour);
        var seven = Window(r => r.UsageSevenDay);
        if (five is null && seven is null)
            return;
        output.WriteLine($"Subscription usage as Claude Code reported it, from the first of these fights to end to the last: " +
                         $"5 hour window {five ?? "not reported"}, 7 day window {seven ?? "not reported"}.");
    }

    private static void PerModel(List<string> players, Sheet sheet, TextWriter output)
    {
        var fights = sheet.All;
        output.WriteLine("Per model (fights are those won, lost or abandoned; HP left counts 0 for a fight that wasn't won; " +
                         "turns and cards are of fights played to the end; resumes are sessions started again because they ended mid-fight):");
        var showUnfinished = fights.Any(r => r.Outcome is Outcomes.Abandoned or Outcomes.Timeout or Outcomes.Error);
        string[] headers = ["model", "fights", "won", "win rate (95%)", "HP left", "HP lost/win", "turns", "cards", "refused/fight", "min/fight", "$/fight", "$ repo prices", "out tokens", "resumes"];
        if (showUnfinished)
            headers = [.. headers, "abandoned", "timeout", "errors"];
        var table = new TextTable(headers);

        foreach (var name in players)
        {
            var mine = sheet.FightsOf(name);
            var counted = mine.Where(r => r.Counts).ToList();
            var finished = counted.Where(r => r.Finished).ToList();
            var won = counted.Where(r => r.Won).ToList();
            var (low, high) = Stats.Wilson(won.Count, counted.Count);
            string[] row =
            [
                name,
                counted.Count.ToString(CultureInfo.InvariantCulture),
                won.Count.ToString(CultureInfo.InvariantCulture),
                counted.Count == 0 ? "-" : $"{Stats.Percent((double)won.Count / counted.Count)} ({Stats.Percent(low).TrimEnd('%')}-{Stats.Percent(high)})",
                Stats.Mean(counted.Select(r => (double)r.HpScore)),
                Stats.Mean(won.Select(r => (double)r.DamageTaken)),
                Stats.Mean(finished.Select(r => (double)r.Turns)),
                Stats.Mean(finished.Select(r => (double)r.CardsPlayed)),
                Stats.Mean(counted.Select(r => (double)r.RefusedActions), "0.00"),
                Stats.Mean(counted.Select(r => r.WallSeconds / 60)),
                Stats.Mean(counted.Where(r => r.TotalCostUsd is not null).Select(r => (double)r.TotalCostUsd!.Value), "0.000"),
                Stats.Mean(counted.Where(r => r.ListPriceUsd is not null).Select(r => (double)r.ListPriceUsd!.Value), "0.000"),
                Stats.Mean(counted.Where(r => r.OutputTokens is not null).Select(r => (double)r.OutputTokens!.Value), "0"),
                counted.Sum(r => r.Resumes).ToString(CultureInfo.InvariantCulture),
            ];
            if (showUnfinished)
                row =
                [
                    .. row,
                    mine.Count(r => r.Outcome == Outcomes.Abandoned).ToString(CultureInfo.InvariantCulture),
                    mine.Count(r => r.Outcome == Outcomes.Timeout).ToString(CultureInfo.InvariantCulture),
                    mine.Count(r => r.Outcome == Outcomes.Error).ToString(CultureInfo.InvariantCulture),
                ];
            table.Add(row);
        }
        table.Write(output);
        if (fights.Any(r => r.Outcome == Outcomes.Timeout))
            output.WriteLine("A timeout is in its own column only: not a fight, not in the win rate, and in none of the means.");
    }

    // How each model drove the tools: a model that plays card by card with act pays for it in
    // time and tokens, and so does one that reads a page it has no use for.
    // The API brain's tool for opening a wiki page, counted in the Read column like Claude Code's Read.
    private const string ApiReadTool = "notes_read";

    private static void ToolCalls(List<string> players, Sheet sheet, TextWriter output)
    {
        output.WriteLine("Tool calls per fight (Read is Claude Code's file tool, the way a session opens the wiki; wiki pages are the different pages a fight opened):");
        var table = new TextTable(["model", .. MainTools, ClaudeCli.ReadTool, "wiki pages", "other", "tool errors", "Claude Code turns"]);
        foreach (var name in players)
        {
            var counted = sheet.FightsOf(name).Where(r => r.Counts).ToList();
            double PerFight(Func<FightResult, int> count) => counted.Count == 0 ? 0 : counted.Average(r => (double)count(r));
            table.Add(
            [
                name,
                .. MainTools.Select(tool => PerFight(r => r.ToolCalls.GetValueOrDefault(tool)).ToString("0.0", CultureInfo.InvariantCulture)),
                PerFight(r => r.ReadCalls).ToString("0.0", CultureInfo.InvariantCulture),
                PerFight(r => r.WikiPages.Count).ToString("0.0", CultureInfo.InvariantCulture),
                PerFight(r => r.ToolCalls.Where(c => !MainTools.Contains(c.Key) && c.Key != ClaudeCli.ReadTool && c.Key != ApiReadTool).Sum(c => c.Value)).ToString("0.0", CultureInfo.InvariantCulture),
                PerFight(r => r.ToolErrors).ToString("0.00", CultureInfo.InvariantCulture),
                Stats.Mean(counted.Where(r => r.NumTurns is not null).Select(r => (double)r.NumTurns!.Value)),
            ]);
        }
        table.Write(output);
        if (players.Any(name => sheet.FightsOf(name).Any(r => r.Brain == Brains.Api)))
            output.WriteLine($"For a fight the API brain played, Read is its {ApiReadTool} calls (its way to open the wiki), other holds its other notes tools and get_persona, " +
                             "and the turns are its API requests.");

        // Which pages, and whether a model opens one where the prompt asks for it: before an
        // elite or a boss.
        output.WriteLine("Wiki pages opened (the prompt asks for an enemy's page before an elite or a boss):");
        foreach (var name in players)
        {
            var counted = sheet.FightsOf(name).Where(r => r.Counts).ToList();
            var hard = counted.Where(r => r.Tier is "elite" or "boss").ToList();
            var pages = counted.SelectMany(r => r.WikiPages)
                .GroupBy(page => page, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
            var line = $"  {name}: in {counted.Count(r => r.WikiPages.Count > 0)} of {counted.Count} {(counted.Count == 1 ? "fight" : "fights")}";
            if (hard.Count > 0)
                line += $" ({hard.Count(r => r.WikiPages.Count > 0)} of {hard.Count} elite and boss fights)";
            if (pages.Count > 0)
                line += ": " + string.Join(", ", pages.Take(6).Select(g => $"{Short(g.Key)} {g.Count()}")) +
                        (pages.Count > 6 ? $" and {pages.Count - 6} more" : "");
            var failed = counted.Sum(r => r.ReadErrors);
            if (failed > 0)
                line += $"; {failed} of {counted.Sum(r => r.ReadCalls)} Read calls failed (a file that isn't there, or one outside the strategy folder)";
            output.WriteLine(line + ".");
        }

        // Under --brief the enemies' pages were in the first message: how many, how long, and
        // whether a model opened one of them again all the same.
        if (players.Any(name => sheet.FightsOf(name).Any(r => r.Counts && r.BriefPages is not null)))
        {
            output.WriteLine("Wiki pages given in the first message (--brief; opened again counts a fight whose Read calls opened a page it was given):");
            foreach (var name in players)
            {
                var counted = sheet.FightsOf(name).Where(r => r.Counts).ToList();
                var briefed = counted.Where(r => r.BriefPages is not null).ToList();
                if (briefed.Count == 0)
                {
                    output.WriteLine($"  {name}: none.");
                    continue;
                }
                var line = $"  {name}: in {briefed.Count} of {counted.Count} {(counted.Count == 1 ? "fight" : "fights")}, " +
                           $"{Stats.Mean(briefed.Select(r => (double)r.BriefPages!.Count))} pages and {Stats.Mean(briefed.Select(r => (double)(r.BriefChars ?? 0)), "0")} characters a fight; " +
                           $"opened again in {briefed.Count(r => r.WikiPages.Intersect(r.BriefPages!, StringComparer.Ordinal).Any())}";
                var missing = briefed.SelectMany(r => r.BriefMissing)
                    .GroupBy(enemy => enemy, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();
                if (missing.Count > 0)
                    line += "; the wiki had no page for " + string.Join(", ", missing.Select(g => $"{g.Key} ({g.Count()} {(g.Count() == 1 ? "fight" : "fights")})"));
                output.WriteLine(line + ".");
            }
        }

        static string Short(string page) => page.StartsWith(Wiki, StringComparison.Ordinal) ? page[Wiki.Length..] : page;
    }

    // The scripted policies on each scenario, or null for a scenario this build can't make again.
    private static Dictionary<string, BaselineRow[]?> Baselines(List<string> scenarios)
    {
        var rows = new Dictionary<string, BaselineRow[]?>(StringComparer.Ordinal);
        foreach (var id in scenarios)
        {
            try
            {
                var scenario = Scenarios.FromId(id);
                rows[id] = Policies.All.Select(p => Baseline.Play(scenario, p)).ToArray();
            }
            catch (Exception)
            {
                rows[id] = null;
            }
        }
        return rows;
    }

    private static void PerScenario(List<string> players, List<string> scenarios, Func<string, string, FightResult?> fight,
        Dictionary<string, BaselineRow[]?> baselines, IReadOnlyList<FightResult> results, TextWriter output)
    {
        var policies = Policies.All.Select(p => p.Name).ToArray();
        output.WriteLine("Per scenario (W and the HP left, L for a loss; the policies are the scripted baselines on the same fight):");
        var table = new TextTable(["scenario", "tier", .. players, "|", .. policies]);
        foreach (var id in scenarios)
        {
            var tier = results.First(r => r.ScenarioId == id).Tier;
            var policyCells = baselines[id] is { } rows ? rows.Select(BaselineCommand.Cell) : policies.Select(_ => "?");
            table.Add([id, tier, .. players.Select(p => Cell(fight(id, p))), "|", .. policyCells]);
        }
        table.Write(output);

        output.WriteLine();
        output.WriteLine("The fights:");
        foreach (var id in scenarios)
            output.WriteLine($"  {id}  {results.First(r => r.ScenarioId == id).Summary}");
        if (baselines.Values.Any(rows => rows is null))
            output.WriteLine("  (? marks a scenario this build of the arena can't generate again, so the policies couldn't play it.)");
    }

    private static string Cell(FightResult? fight) => fight?.Outcome switch
    {
        null => "-",
        Outcomes.Won => "W " + fight.HpLeft.ToString(CultureInfo.InvariantCulture),
        Outcomes.Lost => "L",
        Outcomes.Abandoned => "gave up",
        Outcomes.Timeout => "timeout",
        _ => "error",
    };

    private static void Paired(List<string> players, List<string> scenarios, Func<string, string, FightResult?> fight,
        Dictionary<string, BaselineRow[]?> baselines, TextWriter output)
    {
        string[] headers = ["pair", "fights", "more HP", "same", "less HP", "mean difference", "sign test p"];
        if (players.Count > 1)
        {
            var common = scenarios.Count(id => players.All(p => fight(id, p) is { Finished: true }));
            output.WriteLine("Paired comparison: who had more HP left on the same fight, on the fights both models played to the end " +
                             $"(a loss is 0 HP; every model finished {common} of the {scenarios.Count} {(scenarios.Count == 1 ? "scenario" : "scenarios")}):");
            var table = new TextTable(headers);
            for (var a = 0; a < players.Count; a++)
                for (var b = a + 1; b < players.Count; b++)
                {
                    var pairs = scenarios
                        .Where(id => fight(id, players[a]) is { Finished: true } && fight(id, players[b]) is { Finished: true })
                        .Select(id => (fight(id, players[a])!.HpScore, fight(id, players[b])!.HpScore))
                        .ToList();
                    if (pairs.Count > 0)
                        table.Add(PairRow($"{players[a]} vs {players[b]}", pairs));
                }
            if (table.Count == 0) output.WriteLine("  none yet.");
            else table.Write(output, "  ");
            output.WriteLine("  " + DroppedPairs(players, scenarios, fight));
            output.WriteLine();
        }

        output.WriteLine("Against the scripted policies (each model's fights played to the end):");
        var policies = Policies.All.Select(p => p.Name).ToArray();
        var against = new TextTable(headers);
        foreach (var player in players)
            for (var p = 0; p < policies.Length; p++)
            {
                var pairs = scenarios
                    .Where(id => fight(id, player) is { Finished: true } && baselines[id] is not null)
                    .Select(id => (fight(id, player)!.HpScore, baselines[id]![p].Won ? baselines[id]![p].HpLeft : 0))
                    .ToList();
                if (pairs.Count > 0)
                    against.Add(PairRow($"{player} vs {policies[p]}", pairs));
            }
        if (against.Count == 0) output.WriteLine("  none yet.");
        else against.Write(output, "  ");
        var unfinished = players
            .Select(p => (Player: p, Count: scenarios.Count(id => fight(id, p) is { Finished: false })))
            .Where(p => p.Count > 0).ToList();
        if (unfinished.Count > 0)
            output.WriteLine("  Left out here as well: each model's fights that weren't played to the end (" +
                             string.Join(", ", unfinished.Select(p => $"{p.Player} {p.Count}")) + ").");
    }

    /// <summary>
    /// The line under the paired table that says what it leaves out: a pair is only compared
    /// when both models played their fight on the scenario to the end, so a fight one of them
    /// abandoned (0 HP in the per-model table) is missing here, and with it the pair. Per model,
    /// how many pairs its own unfinished fights cost, and what those fights were.
    /// </summary>
    private static string DroppedPairs(List<string> players, List<string> scenarios, Func<string, string, FightResult?> fight)
    {
        var parts = new List<string>();
        var abandonedPairs = 0;
        foreach (var player in players)
        {
            // This model's fights that were started but not played to the end, on scenarios another model has a fight on.
            var mine = scenarios
                .Select(id => (Fight: fight(id, player), Others: players.Count(other => other != player && fight(id, other) is not null)))
                .Where(x => x.Fight is { Finished: false } && x.Others > 0)
                .ToList();
            var pairs = mine.Sum(x => x.Others);
            if (pairs == 0)
            {
                parts.Add($"{player} 0");
                continue;
            }
            abandonedPairs += mine.Where(x => x.Fight!.Outcome == Outcomes.Abandoned).Sum(x => x.Others);
            var reasons = new[] { Outcomes.Abandoned, Outcomes.Timeout, Outcomes.Error }
                .Select(outcome => (Outcome: outcome, Count: mine.Count(x => x.Fight!.Outcome == outcome)))
                .Where(r => r.Count > 0)
                .Select(r => $"{r.Count} {(r.Outcome == Outcomes.Abandoned ? "abandoned" : r.Outcome == Outcomes.Timeout ? (r.Count == 1 ? "timeout" : "timeouts") : (r.Count == 1 ? "error" : "errors"))}");
            parts.Add($"{player} {pairs} ({string.Join(", ", reasons)})");
        }
        return "A pair is dropped when either side didn't play its fight to the end. Pairs dropped, by the model whose fight it was: " +
               string.Join("; ", parts) + "." +
               (abandonedPairs > 0
                   ? " An abandoned fight counts as 0 HP in the per-model table but is missing here, so this table flatters a model that gives up."
                   : "");
    }

    /// <summary>
    /// What a set of paired fights comes to: on the same scenarios, how often the first side
    /// won and the second, in how many the first had more HP left, the same, less (a fight that
    /// wasn't won is 0 HP), the mean of first minus second, and the two-sided sign test's p for
    /// the better/worse split.
    /// </summary>
    internal sealed record PairStats(int Fights, int FirstWon, int SecondWon, int Better, int Same, int Worse, double MeanDifference, double SignTestP)
    {
        public static PairStats Of(IReadOnlyList<(FightResult First, FightResult Second)> pairs)
        {
            var better = pairs.Count(p => p.First.HpScore > p.Second.HpScore);
            var worse = pairs.Count(p => p.First.HpScore < p.Second.HpScore);
            return new PairStats(pairs.Count, pairs.Count(p => p.First.Won), pairs.Count(p => p.Second.Won), better, pairs.Count - better - worse, worse,
                pairs.Count == 0 ? 0 : pairs.Average(p => (double)(p.First.HpScore - p.Second.HpScore)), Stats.SignTest(better, worse));
        }
    }

    /// <summary>The columns of the table that compares two variants of a model (<see cref="AddPairRows"/>).</summary>
    internal static readonly string[] VariantHeaders = ["pair", "tier", "fights", "won", "better", "same", "worse", "HP left difference", "sign test p"];

    /// <summary>
    /// The rows for one pair of variants: all its paired fights first, then one row per tier
    /// that has any, in the tiers' own order. "won" is the first side's wins against the
    /// second's on those fights, which is the wins by tier.
    /// </summary>
    internal static void AddPairRows(TextTable table, string name, IReadOnlyList<(FightResult First, FightResult Second)> pairs)
    {
        var tiers = Scenarios.Tiers.Where(tier => pairs.Any(p => p.First.Tier == tier)).ToList();
        tiers.AddRange(pairs.Select(p => p.First.Tier).Distinct().Where(tier => !tiers.Contains(tier)));
        Add(name, "all", pairs);
        if (tiers.Count > 1)
            foreach (var tier in tiers)
                Add("", tier, pairs.Where(p => p.First.Tier == tier).ToList());

        void Add(string pair, string tier, IReadOnlyList<(FightResult First, FightResult Second)> rows)
        {
            var stats = PairStats.Of(rows);
            table.Add(
                pair, tier,
                stats.Fights.ToString(CultureInfo.InvariantCulture),
                string.Create(CultureInfo.InvariantCulture, $"{stats.FirstWon} vs {stats.SecondWon}"),
                stats.Better.ToString(CultureInfo.InvariantCulture),
                stats.Same.ToString(CultureInfo.InvariantCulture),
                stats.Worse.ToString(CultureInfo.InvariantCulture),
                stats.MeanDifference.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " HP",
                stats.SignTestP.ToString("0.00", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>What stands above a table of <see cref="AddPairRows"/> rows.</summary>
    internal const string VariantLegend =
        "on the scenarios both played to the end (a loss is 0 HP; won is the first's wins vs the second's; better, same and worse count " +
        "the fights in which the first had more HP left, the same, less; the difference is the mean of first minus second)";

    /// <summary>
    /// The comparison between a model's own rows: every two of them, the later against the
    /// earlier (so everything is first compared with "base"), on the scenarios both played to
    /// the end. A pair is dropped when either side didn't play its fight to the end, as in the
    /// comparison between models.
    /// </summary>
    private static void PairedVariants(Sheet sheet, TextWriter output)
    {
        output.WriteLine($"Paired comparison between experiments: each model's rows against each other, {VariantLegend}:");
        var table = new TextTable(VariantHeaders);
        var dropped = new List<string>();
        foreach (var model in sheet.Players.Select(p => p.Model).Distinct())
        {
            var mine = Enumerable.Range(0, sheet.Players.Count).Where(i => sheet.Players[i].Model == model).ToList();
            for (var a = 0; a < mine.Count; a++)
                for (var b = a + 1; b < mine.Count; b++)
                {
                    var (first, second) = (sheet.Names[mine[b]], sheet.Names[mine[a]]);
                    var both = sheet.Scenarios.Where(id => sheet.Fight(id, first) is not null && sheet.Fight(id, second) is not null).ToList();
                    var pairs = both
                        .Where(id => sheet.Fight(id, first) is { Finished: true } && sheet.Fight(id, second) is { Finished: true })
                        .Select(id => (sheet.Fight(id, first)!, sheet.Fight(id, second)!))
                        .ToList();
                    if (pairs.Count > 0)
                        AddPairRows(table, $"{first} vs {second}", pairs);
                    if (pairs.Count < both.Count)
                        dropped.Add($"{first} vs {second} {both.Count - pairs.Count}");
                }
        }
        if (table.Count == 0)
            output.WriteLine(sheet.Players.GroupBy(p => p.Model).Any(g => g.Count() > 1)
                ? "  none yet: no two rows of a model have played the same scenario to the end."
                : "  nothing to compare: every model is here under one label and effort.");
        else
            table.Write(output, "  ");
        if (dropped.Count > 0)
            output.WriteLine("  A pair is dropped when either side didn't play its fight to the end (abandoned, a timeout or an error). Pairs dropped: " +
                             string.Join("; ", dropped) + ".");
    }

    private static string[] PairRow(string name, List<(int Mine, int Theirs)> pairs)
    {
        var more = pairs.Count(p => p.Mine > p.Theirs);
        var less = pairs.Count(p => p.Mine < p.Theirs);
        var difference = pairs.Average(p => (double)(p.Mine - p.Theirs));
        return
        [
            name,
            pairs.Count.ToString(CultureInfo.InvariantCulture),
            more.ToString(CultureInfo.InvariantCulture),
            (pairs.Count - more - less).ToString(CultureInfo.InvariantCulture),
            less.ToString(CultureInfo.InvariantCulture),
            difference.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " HP",
            Stats.SignTest(more, less).ToString("0.00", CultureInfo.InvariantCulture),
        ];
    }
}
