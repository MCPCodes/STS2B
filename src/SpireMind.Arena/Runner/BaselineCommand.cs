using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena baseline</c>: every scripted policy plays every generated fight, which costs nothing
/// (the searching player takes about half a second a fight). The tables show what the fights
/// are and how hard, overall and by tier: a fight every policy wins with full HP tells the
/// models apart no better than one nobody can win. A fight lost to the turn limit and an action
/// a fight turned down are named, since neither should happen.
/// </summary>
public static class BaselineCommand
{
    public static int Run(ArenaOptions options, TextWriter output)
    {
        var scenarios = Scenarios.Generate(options.Scenarios, options.Seed);
        var policies = Policies.All;
        var rows = scenarios.Select(s => policies.Select(p => Baseline.Play(s, p)).ToArray()).ToArray();

        output.WriteLine($"Arena baseline: {scenarios.Count} {(scenarios.Count == 1 ? "fight" : "fights")} from seed {options.Seed}, " +
                         $"played by {string.Join(", ", policies.Select(p => p.Name))}.");
        output.WriteLine("W and the HP left, or L for a loss.");
        output.WriteLine();

        var fights = new TextTable(["scenario", "tier", .. policies.Select(p => p.Name), "fight"]);
        for (var s = 0; s < scenarios.Count; s++)
            fights.Add([scenarios[s].Id, scenarios[s].Tier, .. rows[s].Select(Cell), scenarios[s].Summary]);
        fights.Write(output);
        output.WriteLine();

        var totals = new TextTable("policy", "fights", "won", "win rate", "HP left on wins", "HP lost in wins", "turns", "turn limit", "refused");
        for (var p = 0; p < policies.Count; p++)
        {
            var played = rows.Select(r => r[p]).ToList();
            var won = played.Where(r => r.Won).ToList();
            totals.Add(
                policies[p].Name,
                played.Count.ToString(CultureInfo.InvariantCulture),
                won.Count.ToString(CultureInfo.InvariantCulture),
                played.Count > 0 ? Stats.Percent((double)won.Count / played.Count) : "-",
                Stats.Mean(won.Select(r => (double)r.HpLeft)),
                Stats.Mean(won.Select(r => (double)r.DamageTaken)),
                Stats.Mean(played.Select(r => (double)r.Turns)),
                played.Count(r => r.TimedOut).ToString(CultureInfo.InvariantCulture),
                played.Sum(r => r.Refused).ToString(CultureInfo.InvariantCulture));
        }
        totals.Write(output);
        output.WriteLine("HP left on wins is what the next fight would start with: it includes Burning Blood's 6. HP lost is all HP the fight took, before any healing.");

        var tiers = scenarios.Select(s => s.Tier).Distinct().ToList();
        if (tiers.Count > 1)
        {
            output.WriteLine();
            output.WriteLine("By tier: wins, and in brackets the mean HP left on those wins.");
            var byTier = new TextTable(["tier", "fights", "HP at start", .. policies.Select(p => p.Name)]);
            foreach (var tier in tiers)
            {
                var index = Enumerable.Range(0, scenarios.Count).Where(s => scenarios[s].Tier == tier).ToList();
                byTier.Add([tier, index.Count.ToString(CultureInfo.InvariantCulture),
                    Stats.Mean(index.Select(s => (double)rows[s][0].HpAtStart)),
                    .. Enumerable.Range(0, policies.Count).Select(p =>
                    {
                        var won = index.Where(s => rows[s][p].Won).ToList();
                        return $"{won.Count}/{index.Count} ({Stats.Mean(won.Select(s => (double)rows[s][p].HpLeft))})";
                    })]);
            }
            byTier.Write(output);
        }

        // A fight that hits the turn limit, or an action that was refused, is worth a look every time.
        for (var s = 0; s < scenarios.Count; s++)
            for (var p = 0; p < policies.Count; p++)
            {
                if (rows[s][p].TimedOut) output.WriteLine($"TURN LIMIT: {policies[p].Name} on {scenarios[s].Id} was not done after {Tuning.MaxTurns} turns ({scenarios[s].Summary})");
                if (rows[s][p].Refused > 0) output.WriteLine($"REFUSED: {policies[p].Name} on {scenarios[s].Id} had {rows[s][p].Refused} actions turned down ({scenarios[s].Summary})");
            }
        return rows.Any(r => r.Any(x => x.Refused > 0)) ? 1 : 0;
    }

    /// <summary>A policy's fight in a table cell: "W 44" or "L".</summary>
    public static string Cell(BaselineRow row) =>
        row.Won ? "W " + row.HpLeft.ToString(CultureInfo.InvariantCulture) : "L";
}
