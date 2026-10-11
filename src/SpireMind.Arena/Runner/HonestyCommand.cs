using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena honesty</c>: every scripted policy plays the generated fights, and at every action
/// what the snapshot promised is compared with what then happened (<see cref="Honesty"/>). It
/// prints how many promises were kept, every kind of difference with its cause and an example,
/// and ends with PASSED when no difference is left unexplained.
/// </summary>
public static class HonestyCommand
{
    public static int Run(ArenaOptions options, TextWriter output)
    {
        var scenarios = Scenarios.Generate(options.Scenarios, options.Seed);
        // ARENA_HONESTY_TRACE=<scenario id>[,<policy>] writes every action of that fight with what it did, for looking into a difference.
        var trace = Environment.GetEnvironmentVariable("ARENA_HONESTY_TRACE")?.Split(',', StringSplitOptions.TrimEntries);
        var policies = Policies.All;
        Action<string>? log = null;
        if (trace is { Length: > 0 } && trace[0].Length > 0)
        {
            scenarios = [Scenarios.FromId(trace[0])];
            if (trace.Length > 1) policies = policies.Where(p => p.Name == trace[1]).ToList();
            log = output.WriteLine;
        }
        var report = Honesty.Run(scenarios, policies, log);

        output.WriteLine($"Arena honesty check: {scenarios.Count} scenarios from seed {options.Seed}, played by {string.Join(", ", policies.Select(p => p.Name))}: " +
                         $"{report.Fights} fights, {report.Decisions} decisions.");
        output.WriteLine();
        var table = new TextTable("promise", "checked", "kept", "differed");
        void Row(string what, int all, int kept) =>
            table.Add(what, all.ToString(CultureInfo.InvariantCulture), kept.ToString(CultureInfo.InvariantCulture), (all - kept).ToString(CultureInfo.InvariantCulture));
        Row("attack intents (hits and size per enemy, per enemy turn)", report.IntentAttacks, report.IntentAttacksKept);
        Row("incoming_damage (against the sum of the hits)", report.IncomingTotals, report.IncomingTotalsKept);
        Row("other intents (defend, buff, debuff, statuscard, ...)", report.OtherIntents, report.OtherIntentsKept);
        Row("card damage (per hit and hit count, per target)", report.CardDamage, report.CardDamageKept);
        Row("card Block", report.CardBlock, report.CardBlockKept);
        Row("card cost (against the energy paid)", report.CardCosts, report.CardCostsKept);
        Row("potion damage and Block", report.PotionNumbers, report.PotionNumbersKept);
        // The forecast's promises (Sim/HonestyForecast.cs): worked out at every end_turn, whether or not the switch is on.
        Honesty.AddForecastRows(report, Row);
        table.Write(output);
        output.WriteLine($"({report.EnemyTurns} enemy turns, {report.CardPlays} cards played, {report.PotionUses} potions used; {report.Won} fights won, " +
                         $"{report.TurnLimit.Count} lost to the turn limit of {Tuning.MaxTurns} turns.)");
        Honesty.WriteForecast(report, output);
        foreach (var line in report.TurnLimit) output.WriteLine($"  TURN LIMIT: {line}");

        output.WriteLine();
        output.WriteLine($"Differences with a known cause: {report.Explained.Values.Sum(v => v.Count)} of {report.Explained.Count} kinds.");
        foreach (var (cause, (count, example)) in report.Explained.OrderByDescending(e => e.Value.Count))
        {
            output.WriteLine($"  {count,5}  {cause}");
            output.WriteLine($"         e.g. {example}");
        }

        output.WriteLine();
        output.WriteLine($"Unexplained differences: {report.Unexplained.Count}");
        foreach (var line in report.Unexplained.Take(200)) output.WriteLine($"  {line}");
        if (report.Unexplained.Count > 200) output.WriteLine($"  ... and {report.Unexplained.Count - 200} more");
        foreach (var error in report.Errors) output.WriteLine($"  ERROR: {error}");

        output.WriteLine();
        output.WriteLine(report.Passed ? "PASSED" : "FAILED");
        return report.Passed ? 0 : 1;
    }
}
