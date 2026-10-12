using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena compare DIR_A DIR_B [--model M]</c>: the fights of two runs paired on the scenarios
/// both played, for each model both hold (or only M). It prints, for B against A, the table
/// <c>report --by-label</c> prints for two labels in one folder (wins, in how many fights B had
/// more HP left, the same, less, the mean difference and the sign test's p, overall and by
/// tier), and then every scenario that was won in one run and lost in the other.
/// <para>
/// A is the run to measure against and B the one that was changed: a positive difference means
/// B kept more HP. Each folder must hold a model once, under one label and one effort; a folder
/// with several variants of a model is compared inside itself with <c>report --by-label</c>.
/// It replays nothing and starts nothing, so it is quick and costs nothing.
/// </para>
/// <para>
/// Position runs (<c>arena position</c>) are compared apart from whole fights, and paired by
/// position: the scenario, the turn and the state the fight was in there. For each position
/// both folders hold, B's wins and mean HP left stand next to A's, and the lines each side took
/// on the position's turn (<see cref="PrintPositions"/>). This is how the same positions played
/// under an earlier prompt (<c>--repo</c>) are set against today's.
/// </para>
/// </summary>
public static class CompareCommand
{
    /// <summary>Returns 0 after the comparison, 1 when a folder has no results, the two share no
    /// model, or a folder holds a model in more than one variant.</summary>
    public static int Run(ArenaOptions options, TextWriter output)
    {
        if (Report.Read(options.ComparePaths[0], output) is not var (pathA, resultsA)
            || Report.Read(options.ComparePaths[1], output) is not var (pathB, resultsB))
            return 1;
        var order = RunCommand.ModelsOf(Path.GetDirectoryName(pathA)!);

        // Whole fights pair by scenario, position runs by position: two comparisons, each
        // printed when both folders hold lines of its kind.
        var fights = resultsA.Any(r => r.Position is null) && resultsB.Any(r => r.Position is null);
        var positions = resultsA.Any(r => r.Position is not null) && resultsB.Any(r => r.Position is not null);
        if (!fights && !positions && (resultsA.Any(r => r.Position is not null) || resultsB.Any(r => r.Position is not null)))
        {
            output.WriteLine("One folder holds whole fights and the other position runs (or one is empty), so there is nothing to pair: " +
                             "a position run is no fight of its scenario.");
            return 1;
        }
        var code = 0;
        if (fights || !positions)
            code = Print(resultsA, resultsB, output, pathA, pathB, options.CompareModel, order);
        if (positions)
        {
            if (fights)
                output.WriteLine();
            code = Math.Max(code, PrintPositions(resultsA, resultsB, output, pathA, pathB, options.CompareModel, order));
        }
        return code;
    }

    /// <summary>Prints the comparison of <paramref name="b"/> against <paramref name="a"/> and
    /// returns the exit code.</summary>
    /// <param name="model">The one model to compare, or null for every model both hold.</param>
    /// <param name="order">The models in the order to list them; others follow by name.</param>
    public static int Print(IReadOnlyList<FightResult> a, IReadOnlyList<FightResult> b, TextWriter output,
        string nameA = "A", string nameB = "B", string? model = null, IReadOnlyList<string>? order = null)
    {
        // A position run is no fight of its scenario: PrintPositions pairs those.
        a = a.Where(r => r.Position is null).ToList();
        b = b.Where(r => r.Position is null).ToList();
        int Place(string m) => order?.ToList().IndexOf(m) is { } at and >= 0 ? at : int.MaxValue;
        var inA = a.Select(r => r.Model).Distinct().ToList();
        var inB = b.Select(r => r.Model).Distinct().ToList();
        var models = (model is null ? inA.Where(inB.Contains) : new[] { model }.Where(m => inA.Contains(m) && inB.Contains(m)))
            .OrderBy(Place).ThenBy(m => m, StringComparer.Ordinal).ToList();
        if (models.Count == 0)
        {
            output.WriteLine(model is null
                ? $"The two runs share no model (A: {Listed(inA)}; B: {Listed(inB)}), so there is nothing to pair."
                : $"{model} isn't in both runs (A: {Listed(inA)}; B: {Listed(inB)}).");
            return 1;
        }

        // One variant of a model per folder, or it isn't clear which fights to pair.
        var sides = new List<(string Model, Report.Sheet A, Report.Sheet B)>();
        foreach (var m in models)
        {
            var sheetA = new Report.Sheet(a.Where(r => r.Model == m).ToList(), null, withLabel: true);
            var sheetB = new Report.Sheet(b.Where(r => r.Model == m).ToList(), null, withLabel: true);
            foreach (var (side, sheet, path) in new[] { ("A", sheetA, nameA), ("B", sheetB, nameB) })
                if (sheet.Players.Count > 1)
                {
                    output.WriteLine($"{side} ({path}) holds {m} in {sheet.Players.Count} variants ({string.Join("; ", sheet.Players.Select(p => p.Describe()))}), " +
                                     "and compare pairs one run with one run. Use report --by-label on that folder to compare the variants inside it.");
                    return 1;
                }
            sides.Add((m, sheetA, sheetB));
        }

        output.WriteLine("Arena compare: B against A, fight by fight on the scenarios both played. A positive difference means B kept more HP.");
        foreach (var (side, path, sheets) in new[]
                 {
                     ("A", nameA, sides.Select(s => (s.Model, Sheet: s.A)).ToList()),
                     ("B", nameB, sides.Select(s => (s.Model, Sheet: s.B)).ToList()),
                 })
        {
            output.WriteLine($"  {side}: {path}");
            foreach (var (m, sheet) in sheets)
                output.WriteLine($"     {m}: {sheet.Players[0].Describe()}, {sheet.All.Count(r => r.Counts)} fights that count on {sheet.Scenarios.Count} " +
                                 $"{(sheet.Scenarios.Count == 1 ? "scenario" : "scenarios")}");
        }
        Warnings(a.Where(r => models.Contains(r.Model)).ToList(), b.Where(r => models.Contains(r.Model)).ToList(), sides, output);
        output.WriteLine();

        output.WriteLine($"Paired comparison, B vs A, {Report.VariantLegend}:");
        var table = new TextTable(Report.VariantHeaders);
        var left = new List<string>();
        var flips = new List<string>();
        foreach (var (m, sheetA, sheetB) in sides)
        {
            var (playerA, playerB) = (sheetA.Names[0], sheetB.Names[0]);
            var both = sheetB.Scenarios.Where(id => sheetA.Fight(id, playerA) is not null && sheetB.Fight(id, playerB) is not null).ToList();
            var pairs = both
                .Where(id => sheetA.Fight(id, playerA) is { Finished: true } && sheetB.Fight(id, playerB) is { Finished: true })
                .Select(id => (First: sheetB.Fight(id, playerB)!, Second: sheetA.Fight(id, playerA)!))
                .ToList();
            if (pairs.Count > 0)
                Report.AddPairRows(table, $"{m}: B vs A", pairs);

            var onlyA = sheetA.Scenarios.Count(id => sheetB.Fight(id, playerB) is null);
            var onlyB = sheetB.Scenarios.Count(id => sheetA.Fight(id, playerA) is null);
            if (onlyA + onlyB + both.Count - pairs.Count > 0)
                left.Add($"{m}: {onlyA} only A has, {onlyB} only B has, {both.Count - pairs.Count} that a side didn't play to the end");

            foreach (var (first, second) in pairs.Where(p => p.First.Won != p.Second.Won))
                flips.Add($"  {second.ScenarioId}  {second.Tier}  {m}  A {Cell(second)} -> B {Cell(first)}  {Enemies(second.Summary)}");
        }
        if (table.Count == 0)
            output.WriteLine("  none: no scenario was played to the end in both runs.");
        else
            table.Write(output, "  ");
        if (left.Count > 0)
            output.WriteLine("  Scenarios left out (a pair needs both fights played to the end; abandoned, timeouts and errors don't pair): " + string.Join("; ", left) + ".");

        output.WriteLine();
        if (flips.Count == 0)
            output.WriteLine("No scenario's outcome flipped: every paired fight was won in both runs or lost in both.");
        else
        {
            output.WriteLine($"{flips.Count} {(flips.Count == 1 ? "scenario's outcome" : "scenarios' outcomes")} flipped (won in one run, lost in the other; W and the HP left, L for a loss):");
            foreach (var flip in flips)
                output.WriteLine(flip);
        }
        return 0;
    }

    /// <summary>
    /// Prints the comparison of the position runs in <paramref name="b"/> against those in
    /// <paramref name="a"/>, paired by position, and returns the exit code: 0 after the
    /// comparison, 1 when the two share no model with position runs or a folder holds a model
    /// in more than one variant (label, effort or deal) there.
    /// </summary>
    /// <param name="model">The one model to compare, or null for every model both hold.</param>
    /// <param name="order">The models in the order to list them; others follow by name.</param>
    public static int PrintPositions(IReadOnlyList<FightResult> a, IReadOnlyList<FightResult> b, TextWriter output,
        string nameA = "A", string nameB = "B", string? model = null, IReadOnlyList<string>? order = null)
    {
        a = a.Where(r => r.Position is not null).ToList();
        b = b.Where(r => r.Position is not null).ToList();
        int Place(string m) => order?.ToList().IndexOf(m) is { } at and >= 0 ? at : int.MaxValue;
        var inA = a.Select(r => r.Model).Distinct().ToList();
        var inB = b.Select(r => r.Model).Distinct().ToList();
        var models = (model is null ? inA.Where(inB.Contains) : new[] { model }.Where(m => inA.Contains(m) && inB.Contains(m)))
            .OrderBy(Place).ThenBy(m => m, StringComparer.Ordinal).ToList();
        if (models.Count == 0)
        {
            output.WriteLine(model is null
                ? $"The two runs share no model with position runs (A: {Listed(inA)}; B: {Listed(inB)}), so there is nothing to pair."
                : $"{model} has no position runs in both (A: {Listed(inA)}; B: {Listed(inB)}).");
            return 1;
        }

        // One variant of a model per folder, or it isn't clear which runs to set against which.
        var sides = new List<(string Model, PositionReport.Sheet A, PositionReport.Sheet B)>();
        foreach (var m in models)
        {
            var sheetA = new PositionReport.Sheet(a.Where(r => r.Model == m).ToList());
            var sheetB = new PositionReport.Sheet(b.Where(r => r.Model == m).ToList());
            foreach (var (side, sheet, path) in new[] { ("A", sheetA, nameA), ("B", sheetB, nameB) })
                if (sheet.Variants.Count > 1)
                {
                    output.WriteLine($"{side} ({path}) holds position runs of {m} in {sheet.Variants.Count} variants ({string.Join("; ", sheet.Variants.Select(v => v.Describe()))}), " +
                                     "and compare sets one batch against one batch. The report of that folder compares the variants inside it.");
                    return 1;
                }
            sides.Add((m, sheetA, sheetB));
        }

        output.WriteLine("Arena compare, position runs: B against A, position by position (scenario and turn). A positive difference means B did better.");
        foreach (var (side, path, sheets, results) in new[]
                 {
                     ("A", nameA, sides.Select(s => (s.Model, Sheet: s.A)).ToList(), a),
                     ("B", nameB, sides.Select(s => (s.Model, Sheet: s.B)).ToList(), b),
                 })
        {
            output.WriteLine($"  {side}: {path}");
            foreach (var (m, sheet) in sheets)
                output.WriteLine($"     {m}: {sheet.Variants[0].Describe()}, {sheet.All.Count(r => r.Counts)} runs that count on {sheet.Positions.Count} " +
                                 $"{(sheet.Positions.Count == 1 ? "position" : "positions")}, system prompt {Listed(Hashes(results.Where(r => r.Model == m), r => r.PromptHash))}");
        }

        // What the two sides differ in: for position runs this is usually the prompt, and the point.
        var compared = models.ToHashSet();
        var (promptsA, promptsB) = (Hashes(a.Where(r => compared.Contains(r.Model)), r => r.PromptHash), Hashes(b.Where(r => compared.Contains(r.Model)), r => r.PromptHash));
        if (promptsA.Count > 0 && promptsB.Count > 0)
            output.WriteLine(promptsA.SequenceEqual(promptsB)
                ? $"Note: both sides were played under the same system prompt ({Listed(promptsA)}), so a difference below is not the prompt's."
                : $"The two sides were played under different system prompts (A {Listed(promptsA)}; B {Listed(promptsB)}): the play prompt, the strategy notes or the wiki index differ.");
        var (wikisA, wikisB) = (Hashes(a.Where(r => compared.Contains(r.Model)), r => r.WikiHash), Hashes(b.Where(r => compared.Contains(r.Model)), r => r.WikiHash));
        if (wikisA.Count > 0 && wikisB.Count > 0 && !wikisA.SequenceEqual(wikisB))
            output.WriteLine($"Note: the wiki's pages differed between the two sides (A {Listed(wikisA)}; B {Listed(wikisB)}), so their sessions didn't read the same pages.");
        if (BrainNote(a.Where(r => compared.Contains(r.Model)).ToList(), b.Where(r => compared.Contains(r.Model)).ToList()) is { } brains)
            output.WriteLine(brains);
        foreach (var (m, sheetA, sheetB) in sides)
        {
            var (variantA, variantB) = (sheetA.Variants[0], sheetB.Variants[0]);
            if (variantA.Reshuffle != variantB.Reshuffle)
                output.WriteLine($"Note: {m} played A on the {Deal(variantA)} deal and B on the {Deal(variantB)} one, so the two sides didn't face the same draws: " +
                                 "part of a difference below is the deal's.");
            if (variantA == variantB && promptsA.SequenceEqual(promptsB))
                output.WriteLine($"Note: A and B hold {m} as the same variant ({variantA.Describe()}) under the same prompt, so this shows how much the same positions " +
                                 "differ from one batch to the next, not what a change does.");
        }

        var leftOut = new List<string>();
        foreach (var (m, sheetA, sheetB) in sides)
        {
            var (variantA, variantB) = (sheetA.Variants[0], sheetB.Variants[0]);
            var both = sheetA.Positions.Where(sheetB.Positions.Contains).ToList();
            output.WriteLine();
            output.WriteLine($"{m}: B against A.");
            if (both.Count == 0)
                output.WriteLine("  none: the two sides hold no position in common.");
            else
                PositionReport.WritePairs(output, "B", "A",
                    both.Select(position => (sheetA.Name(position), sheetB.Of(position, variantB)!, sheetA.Of(position, variantA)!)).ToList(),
                    variantA.Reshuffle && variantB.Reshuffle, "  ");

            // The same turn of the same scenario in another state is another position: rebuilt
            // from another recording, or by a build that shows the fight differently.
            foreach (var position in sheetA.Positions.Where(p => !sheetB.Positions.Contains(p)))
                leftOut.Add(sheetB.Positions.FirstOrDefault(p => p.Scenario == position.Scenario && p.Turn == position.Turn) is { } other
                    ? $"{PositionCommand.Name(position.Scenario, position.Turn)} ({m}: A and B are not the same position, state {position.StateHash} against {other.StateHash}: " +
                      "they were rebuilt from different recordings, or by builds that show the fight differently)"
                    : $"{PositionCommand.Name(position.Scenario, position.Turn)} ({m}: only A has it)");
            foreach (var position in sheetB.Positions.Where(p => !sheetA.Positions.Contains(p) && !sheetA.Positions.Any(q => q.Scenario == p.Scenario && q.Turn == p.Turn)))
                leftOut.Add($"{PositionCommand.Name(position.Scenario, position.Turn)} ({m}: only B has it)");

            if (both.Count > 0)
            {
                output.WriteLine("  Lines taken on the position's turn (runs that took the line, how many of them won, their mean HP left):");
                foreach (var position in both)
                {
                    output.WriteLine($"    {sheetA.Name(position)}");
                    foreach (var (side, cell) in new[] { ("A", sheetA.Of(position, variantA)!), ("B", sheetB.Of(position, variantB)!) })
                    {
                        var lines = cell.Lines();
                        if (lines.Count == 0)
                            output.WriteLine($"      {side}  no run that counts.");
                        foreach (var (line, runs, won, hp) in lines)
                            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"      {side}  {runs} {(runs == 1 ? "run" : "runs")}: {line}  ->  {won} won, HP left {hp:0.0}"));
                    }
                }
            }
        }
        if (leftOut.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Positions left out: " + string.Join("; ", leftOut) + ".");
        }
        return 0;

        static string Deal(PositionReport.Variant variant) => variant.Reshuffle ? PositionInfo.Reshuffled : PositionInfo.Recorded;
    }

    private static List<string> Hashes(IEnumerable<FightResult> results, Func<FightResult, string?> of) =>
        results.Select(of).OfType<string>().Distinct().OrderBy(h => h, StringComparer.Ordinal).ToList();

    // What makes a difference between the two runs something else than the switch: other
    // notes, another wiki, or nothing changed at all.
    private static void Warnings(IReadOnlyList<FightResult> a, IReadOnlyList<FightResult> b,
        List<(string Model, Report.Sheet A, Report.Sheet B)> sides, TextWriter output)
    {
        var (promptsA, promptsB) = (Hashes(a, r => r.PromptHash), Hashes(b, r => r.PromptHash));
        if (promptsA.Count > 0 && promptsB.Count > 0 && !promptsA.SequenceEqual(promptsB))
            output.WriteLine($"Note: the two runs were played with different system prompts (A {Listed(promptsA)}; B {Listed(promptsB)}): the play prompt or the " +
                             "strategy notes changed between them, so a difference below isn't the switch's alone.");
        var (wikisA, wikisB) = (Hashes(a, r => r.WikiHash), Hashes(b, r => r.WikiHash));
        if (wikisA.Count > 0 && wikisB.Count > 0 && !wikisA.SequenceEqual(wikisB))
            output.WriteLine($"Note: the wiki's pages differed between the two runs (A {Listed(wikisA)}; B {Listed(wikisB)}), so their sessions didn't read the same pages.");
        if (BrainNote(a, b) is { } brains)
            output.WriteLine(brains);
        foreach (var (m, sheetA, sheetB) in sides)
            if (sheetA.Players[0] == sheetB.Players[0])
                output.WriteLine($"Note: A and B hold {m} as the same variant ({sheetA.Players[0].Describe()}), so this shows how much the same fights " +
                                 "differ from one run to the next, not what a switch does.");
    }

    /// <summary>What to say when the two sides were not driven by the same brain, or null when
    /// they were: Claude Code and the mod's own brain differ in more than a switch, and their
    /// system prompts differ by design.</summary>
    internal static string? BrainNote(IReadOnlyList<FightResult> a, IReadOnlyList<FightResult> b)
    {
        var (brainsA, brainsB) = (Hashes(a, r => r.Brain), Hashes(b, r => r.Brain));
        if (brainsA.SequenceEqual(brainsB))
            return null;
        return $"Note: the two sides were not driven the same way (A {Listed(brainsA)}; B {Listed(brainsB)}). {Brains.ClaudeCode} is Claude Code on the subscription, with its own " +
               $"instructions around the arena's prompt; {Brains.Api} is the mod's own brain on the API, as in the real game. Their system prompts differ by design, " +
               "and so does what a model is told about the fight, so a difference below is the two harnesses' as well as anything else's.";
    }

    private static string Listed(IReadOnlyList<string> items) => items.Count == 0 ? "none" : string.Join(", ", items);

    private static string Cell(FightResult fight) =>
        fight.Won ? "W " + fight.HpLeft.ToString(CultureInfo.InvariantCulture) : "L";

    // The end of a scenario's one-line summary, which names the enemies: "vs Vantom (VantomBoss)".
    private static string Enemies(string summary)
    {
        var at = summary.LastIndexOf(" vs ", StringComparison.Ordinal);
        return at < 0 ? "" : summary[(at + 1)..];
    }
}
