using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// The part of <c>arena report</c> for position runs (<c>arena position</c>), also printed at
/// the end of a position test: per position and label how many runs there were, how many were
/// won and the mean HP left, then what the sessions did on the position's turn, line by line
/// with how many runs took each line, and where a model has two rows on a position (two labels,
/// say old and new prompt) the later row against the earlier one. <c>arena compare</c> prints
/// the same comparison across two folders (<see cref="CompareCommand"/>).
/// <para>
/// A position is a scenario, a turn and the state the recorded fight was in at that turn's
/// start (<see cref="PositionInfo.StateHash"/>). A row is a model under one label, effort and
/// deal (recorded or reshuffled). Each of a row's runs on a position is counted once, by its
/// run number: the last line that counts (won, lost or abandoned), else the last line there is.
/// HP left is 0 for a run that wasn't won, as everywhere in the reports.
/// </para>
/// </summary>
public static class PositionReport
{
    /// <summary>A position: the scenario, the turn, and what the fight looked like there.</summary>
    internal sealed record Key(string Scenario, int Turn, string StateHash);

    /// <summary>A row: a model under one label, at one effort, on one kind of deal.</summary>
    internal sealed record Variant(string Model, string Label, string Effort, bool Reshuffle)
    {
        public bool Played(FightResult r) =>
            r.Model == Model && r.Experiment == Label && r.Effort == Effort && r.Position?.Reshuffle == Reshuffle;

        /// <summary>"experiment new, effort medium, deal recorded".</summary>
        public string Describe() => $"experiment {Label}, effort {Effort}, deal {(Reshuffle ? PositionInfo.Reshuffled : PositionInfo.Recorded)}";
    }

    /// <summary>A row's runs on one position, one per run number.</summary>
    internal sealed class Cell(IReadOnlyList<FightResult> runs)
    {
        /// <summary>Every run once, in the order of their numbers.</summary>
        public IReadOnlyList<FightResult> Runs { get; } = runs;

        /// <summary>The runs that say something: won, lost or abandoned.</summary>
        public List<FightResult> Counted => Runs.Where(r => r.Counts).ToList();

        public int Wins => Runs.Count(r => r.Won);

        /// <summary>The share of counted runs that were won; 0 when none counts.</summary>
        public double WinShare => Counted.Count == 0 ? 0 : (double)Wins / Counted.Count;

        /// <summary>Mean HP left over the counted runs, 0 for each that wasn't won.</summary>
        public double MeanHp => Counted.Count == 0 ? 0 : Counted.Average(r => (double)r.HpScore);

        /// <summary>
        /// The lines the counted runs took on the position's turn, most often taken first: the
        /// line in words, the runs that took it, how many of them were won and their mean HP left.
        /// A run whose session stopped before the turn was ended is listed under what it had
        /// played by then, marked as unfinished.
        /// </summary>
        public List<(string Line, int Runs, int Won, double MeanHp)> Lines() => Counted
            .GroupBy(r => PositionInfo.Words(r.Position!.Line) + (r.Position.LineComplete ? "" : " (the turn wasn't ended)"), StringComparer.Ordinal)
            .Select(g => (Line: g.Key, Runs: g.Count(), Won: g.Count(r => r.Won), MeanHp: g.Average(r => (double)r.HpScore)))
            .OrderByDescending(l => l.Runs).ThenBy(l => l.Line, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The position runs of a set of result lines, sorted into positions and rows.</summary>
    internal sealed class Sheet
    {
        private readonly Dictionary<(Key, Variant), Cell> _cells = [];

        /// <param name="order">The models in the order to list them; others follow by name.</param>
        public Sheet(IReadOnlyList<FightResult> results, IReadOnlyList<string>? order = null)
        {
            var runs = results.Where(r => r.Position is not null).ToList();
            Positions = runs.Select(KeyOf).Distinct().ToList();

            int Place(string model) => order?.ToList().IndexOf(model) is { } at and >= 0 ? at : int.MaxValue;
            // Labels in the order they first stand in the file, "base" before any other: the
            // batch that was played first is the one the later ones are measured against.
            var labels = runs.Select(r => r.Experiment).Distinct().OrderBy(label => label == ExperimentLabel.Base ? 0 : 1).ToList();
            Variants = runs
                .Select(r => new Variant(r.Model, r.Experiment, r.Effort, r.Position!.Reshuffle)).Distinct()
                .OrderBy(v => Place(v.Model)).ThenBy(v => v.Model, StringComparer.Ordinal)
                .ThenBy(v => labels.IndexOf(v.Label))
                .ThenBy(v => ArenaOptions.Efforts.ToList().IndexOf(v.Effort))
                .ThenBy(v => v.Reshuffle)
                .ToList();

            foreach (var position in Positions)
                foreach (var variant in Variants)
                {
                    var mine = runs.Where(r => KeyOf(r) == position && variant.Played(r)).ToList();
                    if (mine.Count == 0)
                        continue;
                    _cells[(position, variant)] = new Cell(mine
                        .GroupBy(r => r.Position!.Run).OrderBy(g => g.Key)
                        .Select(g => g.LastOrDefault(r => r.Counts) ?? g.Last())
                        .ToList());
                }
        }

        /// <summary>The positions in the order their first lines stand in the file.</summary>
        public List<Key> Positions { get; }

        /// <summary>The rows: models in the given order, under each its labels in the order they
        /// first stand in the file ("base" first), then efforts from low to max, the recorded
        /// deal before the reshuffled one.</summary>
        public List<Variant> Variants { get; }

        public Cell? Of(Key position, Variant variant) => _cells.GetValueOrDefault((position, variant));

        /// <summary>Every run once.</summary>
        public List<FightResult> All => _cells.Values.SelectMany(cell => cell.Runs).ToList();

        /// <summary>"boss-878005 T4", with the start of the state hash when the results hold
        /// that turn of that scenario in more than one state (rebuilt from different recordings).</summary>
        public string Name(Key position) =>
            PositionCommand.Name(position.Scenario, position.Turn) +
            (Positions.Count(p => p.Scenario == position.Scenario && p.Turn == position.Turn) > 1 ? $" #{Short(position.StateHash)}" : "");

        /// <summary>A row's name: the model, the label, and effort or deal where the rows differ in them.</summary>
        public string Name(Variant variant) =>
            $"{variant.Model} [{variant.Label}]" +
            (Variants.Select(v => v.Effort).Distinct().Count() > 1 ? $" ({variant.Effort})" : "") +
            (Variants.Select(v => v.Reshuffle).Distinct().Count() > 1 ? $" ({(variant.Reshuffle ? PositionInfo.Reshuffled : PositionInfo.Recorded)})" : "");

        private static Key KeyOf(FightResult r) => new(r.ScenarioId, r.Position!.Turn, r.Position.StateHash);

        private static string Short(string hash) => hash.Length > 6 ? hash[..6] : hash;
    }

    /// <summary>Prints the report for the position runs among <paramref name="results"/>; lines
    /// of whole fights are left out.</summary>
    /// <param name="order">The models in the order to list them; others follow by name.</param>
    public static void Print(IReadOnlyList<FightResult> results, TextWriter output, string? source = null, IReadOnlyList<string>? order = null)
    {
        var sheet = new Sheet(results, order);
        var runs = sheet.All;
        if (runs.Count == 0)
        {
            output.WriteLine(source is null ? "No position runs to report." : $"No position runs in {source}.");
            return;
        }

        var counted = runs.Count(r => r.Counts);
        var timeouts = runs.Count(r => r.Outcome == Outcomes.Timeout);
        var errors = runs.Count(r => !r.Counts) - timeouts;
        var models = sheet.Variants.Select(v => v.Model).Distinct().Count();
        output.WriteLine($"Position tests: {counted} {Plural(counted, "run")} on {sheet.Positions.Count} {Plural(sheet.Positions.Count, "position")} " +
                         $"by {models} {Plural(models, "model")}" +
                         (timeouts > 0 ? $", plus {timeouts} that ran out of time" : "") +
                         (errors > 0 ? $"{(timeouts > 0 ? " and" : ", plus")} {errors} that couldn't be played" : "") +
                         (source is null ? "." : $" ({source})."));
        output.WriteLine("A position is the start of one turn of a recorded fight. Each run is a new session that took the fight over there and played it to the end.");
        if (timeouts + errors > 0)
            output.WriteLine("Runs that ran out of time or couldn't be played count for nothing below; the same position command with the same --out plays them again.");
        foreach (var reshuffle in sheet.Variants.Select(v => v.Reshuffle).Distinct().Order())
            output.WriteLine(PositionCommand.ModeLine(reshuffle));
        Prompts(sheet, runs, output);
        output.WriteLine();

        PerPosition(sheet, output);
        output.WriteLine();
        LinesTaken(sheet, output);
        Between(sheet, output);
    }

    // The prompt each label was played under: what a comparison of two labels is a comparison of.
    private static void Prompts(Sheet sheet, List<FightResult> runs, TextWriter output)
    {
        var labels = sheet.Variants.Select(v => v.Label).Distinct().ToList();
        var byLabel = labels
            .Select(label => (Label: label, Hashes: runs.Where(r => r.Experiment == label).Select(r => r.PromptHash).OfType<string>().Distinct().Order(StringComparer.Ordinal).ToList()))
            .ToList();
        if (byLabel.All(l => l.Hashes.Count == 0))
            return;
        output.WriteLine("System prompt by label (its hash): " +
                         string.Join("; ", byLabel.Select(l => $"{l.Label} {(l.Hashes.Count == 0 ? "none recorded" : string.Join(" and ", l.Hashes))}")) + "." +
                         (byLabel.Any(l => l.Hashes.Count > 1) ? " A label with two hashes was played under two prompts: its runs are not one treatment." : ""));
        var wikis = runs.Select(r => r.WikiHash).OfType<string>().Distinct().Count();
        if (wikis > 1)
            output.WriteLine($"Note: the wiki's pages changed between these runs ({wikis} versions of strategy/wiki), so the sessions didn't all read the same pages.");
        var keyed = runs.Select(r => r.ApiKeySource).Where(s => s is not (null or "none")).Distinct().ToList();
        if (keyed.Count > 0)
            output.WriteLine($"Warning: some sessions report an API key source ({string.Join(", ", keyed)}), so they may not have run on the subscription.");
    }

    private static void PerPosition(Sheet sheet, TextWriter output)
    {
        var runs = sheet.All;
        output.WriteLine("Per position and label (runs are those won, lost or abandoned; HP left counts 0 for a run that wasn't won; turns is the fight's last turn, of runs played to the end):");
        var showUnfinished = runs.Any(r => r.Outcome is Outcomes.Abandoned or Outcomes.Timeout or Outcomes.Error);
        string[] headers = ["position", "model", "label", "effort", "deal", "runs", "won", "win rate (95%)", "HP left", "turns", "min/run", "$/run"];
        if (showUnfinished)
            headers = [.. headers, "abandoned", "timeout", "errors"];
        var table = new TextTable(headers);
        foreach (var position in sheet.Positions)
        {
            var first = true;
            foreach (var variant in sheet.Variants)
            {
                if (sheet.Of(position, variant) is not { } cell)
                    continue;
                var counted = cell.Counted;
                var (low, high) = Stats.Wilson(cell.Wins, counted.Count);
                string[] row =
                [
                    first ? sheet.Name(position) : "",
                    variant.Model, variant.Label, variant.Effort, variant.Reshuffle ? PositionInfo.Reshuffled : PositionInfo.Recorded,
                    counted.Count.ToString(CultureInfo.InvariantCulture),
                    cell.Wins.ToString(CultureInfo.InvariantCulture),
                    counted.Count == 0 ? "-" : $"{Stats.Percent(cell.WinShare)} ({Stats.Percent(low).TrimEnd('%')}-{Stats.Percent(high)})",
                    Stats.Mean(counted.Select(r => (double)r.HpScore)),
                    Stats.Mean(counted.Where(r => r.Finished).Select(r => (double)r.Turns)),
                    Stats.Mean(counted.Select(r => r.WallSeconds / 60)),
                    Stats.Mean(counted.Where(r => r.TotalCostUsd is not null).Select(r => (double)r.TotalCostUsd!.Value), "0.000"),
                ];
                if (showUnfinished)
                    row =
                    [
                        .. row,
                        cell.Runs.Count(r => r.Outcome == Outcomes.Abandoned).ToString(CultureInfo.InvariantCulture),
                        cell.Runs.Count(r => r.Outcome == Outcomes.Timeout).ToString(CultureInfo.InvariantCulture),
                        cell.Runs.Count(r => r.Outcome == Outcomes.Error).ToString(CultureInfo.InvariantCulture),
                    ];
                table.Add(row);
                first = false;
            }
        }
        table.Write(output);
    }

    // What the sessions did on the position's turn: each different line with the runs that took it.
    private static void LinesTaken(Sheet sheet, TextWriter output)
    {
        output.WriteLine("Lines taken on the position's turn (what a run's session did there, in order, each card with its target; " +
                         "then how many runs took that line, how many of them won, and their mean HP left):");
        foreach (var position in sheet.Positions)
        {
            var any = sheet.Variants.Select(v => sheet.Of(position, v)).OfType<Cell>().SelectMany(cell => cell.Runs).First().Position!;
            var recorded = any.SourceOutcome switch
            {
                Outcomes.Won => $"and won with {any.SourceHpLeft} HP left",
                Outcomes.Lost => "and lost",
                null => "",
                var other => $"({other})",
            };
            output.WriteLine($"  {sheet.Name(position)}: {any.HpAtStart} HP at the start." +
                             (any.SourceModel is null ? "" : any.SourceLine.Count == 0
                                 ? $" The recorded session ({any.SourceModel}) ran nothing on this turn."
                                 : $" The recorded session ({any.SourceModel}) played: {PositionInfo.Words(any.SourceLine)} {recorded}".TrimEnd() + "."));
            foreach (var variant in sheet.Variants)
            {
                if (sheet.Of(position, variant) is not { } cell)
                    continue;
                output.WriteLine($"    {sheet.Name(variant)}");
                var lines = cell.Lines();
                if (lines.Count == 0)
                    output.WriteLine("      no run that counts yet.");
                foreach (var (line, runs, won, hp) in lines)
                    output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                        $"      {runs} {Plural(runs, "run")}: {line}  ->  {won} won, HP left {hp:0.0}"));
            }
        }
    }

    // Where a model has more than one row on a position: each later row against each earlier
    // one on the same kind of deal. A reshuffled row against a recorded one would compare the
    // deals, not the rows.
    private static void Between(Sheet sheet, TextWriter output)
    {
        foreach (var model in sheet.Variants.Select(v => v.Model).Distinct())
        {
            var mine = sheet.Variants.Where(v => v.Model == model).ToList();
            for (var a = 0; a < mine.Count; a++)
                for (var b = a + 1; b < mine.Count; b++)
                {
                    var (first, second) = (mine[b], mine[a]);
                    if (first.Reshuffle != second.Reshuffle)
                        continue;
                    var rows = sheet.Positions
                        .Select(position => (Name: sheet.Name(position), First: sheet.Of(position, first), Second: sheet.Of(position, second)))
                        .Where(row => row.First is not null && row.Second is not null)
                        .Select(row => (row.Name, row.First!, row.Second!))
                        .ToList();
                    if (rows.Count == 0)
                        continue;
                    output.WriteLine();
                    output.WriteLine($"{sheet.Name(first)} against {sheet.Name(second)}, position by position:");
                    WritePairs(output, sheet.Name(first), sheet.Name(second), rows, first.Reshuffle && second.Reshuffle, "  ");
                }
        }
    }

    /// <summary>What two sides come to on one position: the share of runs won and the mean HP left of each.</summary>
    internal sealed record PairRow(string Position, int FirstWon, int FirstRuns, double FirstHp, int SecondWon, int SecondRuns, double SecondHp)
    {
        /// <summary>The first side's win share minus the second's, in points of 100.</summary>
        public double SharePoints => 100 * ((FirstRuns == 0 ? 0 : (double)FirstWon / FirstRuns) - (SecondRuns == 0 ? 0 : (double)SecondWon / SecondRuns));

        public double HpDifference => FirstHp - SecondHp;

        public static PairRow Of(string position, Cell first, Cell second) =>
            new(position, first.Wins, first.Counted.Count, first.MeanHp, second.Wins, second.Counted.Count, second.MeanHp);
    }

    /// <summary>
    /// The table that sets two sides next to each other position by position: each side's wins
    /// of its runs and mean HP left, and the first minus the second in points of win share and
    /// in HP. Its last row is over all positions, each counting the same whatever its number
    /// of runs: the differences' means, and under the table in how many positions the first
    /// kept more HP on average, with the sign test. A position where a side has no run that
    /// counts is listed and left out of that row.
    /// <para>
    /// When both sides were reshuffled, run k of a position had the same deal on both, so the
    /// runs also pair one to one: the line under the table says in how many deals the first
    /// had more HP left.
    /// </para>
    /// </summary>
    internal static void WritePairs(TextWriter output, string first, string second,
        IReadOnlyList<(string Name, Cell First, Cell Second)> positions, bool sameDeals, string indent)
    {
        var rows = positions.Select(p => PairRow.Of(p.Name, p.First, p.Second)).ToList();
        var table = new TextTable("position", $"{second}: won", "HP left", $"{first}: won", "HP left", "win share", "HP left difference");
        foreach (var row in rows)
            table.Add(Cells(row));
        var both = rows.Where(row => row.FirstRuns > 0 && row.SecondRuns > 0).ToList();
        if (both.Count > 1)
            table.Add(
                $"all {both.Count} positions",
                string.Create(CultureInfo.InvariantCulture, $"{both.Sum(r => r.SecondWon)}/{both.Sum(r => r.SecondRuns)}"),
                both.Average(r => r.SecondHp).ToString("0.0", CultureInfo.InvariantCulture),
                string.Create(CultureInfo.InvariantCulture, $"{both.Sum(r => r.FirstWon)}/{both.Sum(r => r.FirstRuns)}"),
                both.Average(r => r.FirstHp).ToString("0.0", CultureInfo.InvariantCulture),
                Points(both.Average(r => r.SharePoints)),
                Hp(both.Average(r => r.HpDifference)));
        table.Write(output, indent);

        if (both.Count > 0)
        {
            var better = both.Count(r => r.HpDifference > 0);
            var worse = both.Count(r => r.HpDifference < 0);
            output.WriteLine($"{indent}By mean HP left, {first} was ahead in {better} of {both.Count} {Plural(both.Count, "position")}, level in {both.Count - better - worse}, " +
                             $"behind in {worse} (sign test p {Stats.SignTest(better, worse).ToString("0.00", CultureInfo.InvariantCulture)}). " +
                             "A few runs of a position say little: read the lines taken, not only the means.");
        }
        if (both.Count < rows.Count)
            output.WriteLine($"{indent}A side has no run that counts on {rows.Count - both.Count} of these positions; they are in no total.");

        if (sameDeals)
        {
            // Run k of a position was dealt from the same seed on both sides.
            var deals = positions
                .SelectMany(p => p.First.Runs.Where(r => r.Finished)
                    .Join(p.Second.Runs.Where(r => r.Finished), r => (r.Position!.Run, r.Position.DealSeed), r => (r.Position!.Run, r.Position.DealSeed), (a, b) => (First: a, Second: b)))
                .ToList();
            if (deals.Count > 0)
            {
                var better = deals.Count(d => d.First.HpScore > d.Second.HpScore);
                var worse = deals.Count(d => d.First.HpScore < d.Second.HpScore);
                output.WriteLine($"{indent}Deal by deal (both sides were reshuffled, so run k of a position had the same deal on both): on {deals.Count} " +
                                 $"{Plural(deals.Count, "deal")} both played to the end, {first} had more HP left in {better}, the same in {deals.Count - better - worse}, less in {worse} " +
                                 $"(sign test p {Stats.SignTest(better, worse).ToString("0.00", CultureInfo.InvariantCulture)}); it won {deals.Count(d => d.First.Won)}, {second} {deals.Count(d => d.Second.Won)}.");
            }
        }

        static string[] Cells(PairRow row) =>
        [
            row.Position,
            string.Create(CultureInfo.InvariantCulture, $"{row.SecondWon}/{row.SecondRuns}"),
            row.SecondRuns == 0 ? "-" : row.SecondHp.ToString("0.0", CultureInfo.InvariantCulture),
            string.Create(CultureInfo.InvariantCulture, $"{row.FirstWon}/{row.FirstRuns}"),
            row.FirstRuns == 0 ? "-" : row.FirstHp.ToString("0.0", CultureInfo.InvariantCulture),
            row.FirstRuns == 0 || row.SecondRuns == 0 ? "-" : Points(row.SharePoints),
            row.FirstRuns == 0 || row.SecondRuns == 0 ? "-" : Hp(row.HpDifference),
        ];

        static string Points(double points) => points.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " points";
        static string Hp(double hp) => hp.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " HP";
    }

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";
}
