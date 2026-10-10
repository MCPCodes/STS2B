using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena report</c>, also printed at the end of <c>arena run</c>: per model how often it won
/// and at what price, per scenario each model's fight next to the scripted policies' on the same
/// fight, and a paired comparison of the HP the models had left. docs/arena.md says what the
/// columns mean and how much (little) a handful of fights can show.
/// </summary>
public static class Report
{
    private static readonly string[] MainTools = ["wait_for_decision", "act_sequence", "act", "get_state", "say"];

    // What every wiki page's path starts with in a result line; left off where the report lists pages.
    private const string Wiki = SpireMind.Core.Brain.NotesTools.WikiFolder + "/";

    /// <summary><c>arena report DIR|results.jsonl</c>.</summary>
    public static int Run(ArenaOptions options, TextWriter output)
    {
        var path = Path.GetFullPath(options.ReportPath!);
        if (Directory.Exists(path))
            path = Path.Combine(path, RunCommand.ResultsFile);
        if (!File.Exists(path))
        {
            output.WriteLine($"There's no {path}.");
            return 1;
        }
        var results = FightResult.ReadFile(path, out var unreadable);
        if (unreadable > 0)
            output.WriteLine($"Skipped {unreadable} {(unreadable == 1 ? "line" : "lines")} of {path} that {(unreadable == 1 ? "isn't a result" : "aren't results")}.");
        Print(results, output, path, RunCommand.ModelsOf(Path.GetDirectoryName(path)!));
        return 0;
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
        if (results.Count == 0)
        {
            output.WriteLine(source is null ? "No fights to report." : $"No fights in {source}.");
            return;
        }

        // A model played at two efforts is two players.
        var severalEfforts = results.Select(r => r.Effort).Distinct().Count() > 1;
        string Player(FightResult r) => severalEfforts ? $"{r.Model} ({r.Effort})" : r.Model;

        int Place(FightResult r) => order?.ToList().IndexOf(r.Model) is { } at and >= 0 ? at : int.MaxValue;
        var players = results
            .OrderBy(Place).ThenBy(r => r.Model, StringComparer.Ordinal).ThenBy(r => ArenaOptions.Efforts.ToList().IndexOf(r.Effort))
            .Select(Player).Distinct().ToList();
        var scenarios = results.Select(r => r.ScenarioId).Distinct().ToList();
        var fights = results
            .GroupBy(r => (r.ScenarioId, Player: Player(r)))
            .ToDictionary(g => g.Key, g => g.LastOrDefault(r => r.Counts) ?? g.Last());
        FightResult? Fight(string scenario, string player) => fights.GetValueOrDefault((scenario, player));

        var counted = fights.Values.Count(r => r.Counts);
        var timeouts = fights.Values.Count(r => r.Outcome == Outcomes.Timeout);
        var errors = fights.Values.Count(r => !r.Counts) - timeouts;
        output.WriteLine($"Arena report: {counted} {(counted == 1 ? "fight" : "fights")} on {scenarios.Count} " +
                         $"{(scenarios.Count == 1 ? "scenario" : "scenarios")} by {players.Count} {(players.Count == 1 ? "model" : "models")}" +
                         (severalEfforts ? "" : $", effort {results[0].Effort}") +
                         (timeouts > 0 ? $", plus {timeouts} that ran out of time" : "") +
                         (errors > 0 ? $"{(timeouts > 0 ? " and" : ", plus")} {errors} that couldn't be played" : "") +
                         (source is null ? "." : $" ({source})."));
        if (timeouts + errors > 0)
            output.WriteLine("Fights that ran out of time or couldn't be played count for nothing below; the same run command with the same --out plays them again.");
        Warnings(results, output);
        Usage(results, output);
        // The per-model rows only compare like with like when every model's fights are the same ones.
        var played = players.Select(p => scenarios.Where(id => Fight(id, p) is { Counts: true }).ToList()).ToList();
        if (played.Any(mine => !mine.SequenceEqual(played[0])))
            output.WriteLine("Note: the models didn't all play the same fights (" +
                             string.Join(", ", players.Select((p, i) => $"{p} {played[i].Count}")) +
                             $" of {scenarios.Count}), so the per-model rows cover different fights. Compare models on the paired table, or play the missing fights first.");
        output.WriteLine();

        PerModel(players, fights.Values.ToList(), Player, output);
        output.WriteLine();
        ToolCalls(players, fights.Values.ToList(), Player, output);

        var baselines = Baselines(scenarios);
        output.WriteLine();
        PerScenario(players, scenarios, Fight, baselines, results, output);
        output.WriteLine();
        Paired(players, scenarios, Fight, baselines, output);
    }

    // Things that make the numbers below mean something else than they seem to.
    private static void Warnings(IReadOnlyList<FightResult> results, TextWriter output)
    {
        var keyed = results.Select(r => r.ApiKeySource).Where(s => s is not (null or "none")).Distinct().ToList();
        if (keyed.Count > 0)
            output.WriteLine($"Warning: some sessions report an API key source ({string.Join(", ", keyed)}), so they may not have run on the subscription.");
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

    private static void PerModel(List<string> players, List<FightResult> fights, Func<FightResult, string> player, TextWriter output)
    {
        output.WriteLine("Per model (fights are those won, lost or abandoned; HP left counts 0 for a fight that wasn't won; " +
                         "turns and cards are of fights played to the end; resumes are sessions started again because they ended mid-fight):");
        var showUnfinished = fights.Any(r => r.Outcome is Outcomes.Abandoned or Outcomes.Timeout or Outcomes.Error);
        string[] headers = ["model", "fights", "won", "win rate (95%)", "HP left", "HP lost/win", "turns", "cards", "refused/fight", "min/fight", "$/fight", "$ repo prices", "out tokens", "resumes"];
        if (showUnfinished)
            headers = [.. headers, "abandoned", "timeout", "errors"];
        var table = new TextTable(headers);

        foreach (var name in players)
        {
            var mine = fights.Where(r => player(r) == name).ToList();
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
    private static void ToolCalls(List<string> players, List<FightResult> fights, Func<FightResult, string> player, TextWriter output)
    {
        output.WriteLine("Tool calls per fight (Read is Claude Code's file tool, the way a session opens the wiki; wiki pages are the different pages a fight opened):");
        var table = new TextTable(["model", .. MainTools, ClaudeCli.ReadTool, "wiki pages", "other", "tool errors", "Claude Code turns"]);
        foreach (var name in players)
        {
            var counted = fights.Where(r => player(r) == name && r.Counts).ToList();
            double PerFight(Func<FightResult, int> count) => counted.Count == 0 ? 0 : counted.Average(r => (double)count(r));
            table.Add(
            [
                name,
                .. MainTools.Select(tool => PerFight(r => r.ToolCalls.GetValueOrDefault(tool)).ToString("0.0", CultureInfo.InvariantCulture)),
                PerFight(r => r.ReadCalls).ToString("0.0", CultureInfo.InvariantCulture),
                PerFight(r => r.WikiPages.Count).ToString("0.0", CultureInfo.InvariantCulture),
                PerFight(r => r.ToolCalls.Where(c => !MainTools.Contains(c.Key) && c.Key != ClaudeCli.ReadTool).Sum(c => c.Value)).ToString("0.0", CultureInfo.InvariantCulture),
                PerFight(r => r.ToolErrors).ToString("0.00", CultureInfo.InvariantCulture),
                Stats.Mean(counted.Where(r => r.NumTurns is not null).Select(r => (double)r.NumTurns!.Value)),
            ]);
        }
        table.Write(output);

        // Which pages, and whether a model opens one where the prompt asks for it: before an
        // elite or a boss.
        output.WriteLine("Wiki pages opened (the prompt asks for an enemy's page before an elite or a boss):");
        foreach (var name in players)
        {
            var counted = fights.Where(r => player(r) == name && r.Counts).ToList();
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
