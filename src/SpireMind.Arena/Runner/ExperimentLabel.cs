namespace SpireMind.Arena;

/// <summary>
/// The name of a run's experiment: a short string built from the switches of <c>arena run</c>
/// that are on, <see cref="Base"/> when none is. It is written to every result line and to
/// run.json and shown in the report's header, and it is part of what makes two fights the same
/// fight: scenario, model, the effort the fight was played at, and this.
/// <para>
/// The parts are joined by "+", always in the same order: <c>brief</c>, <c>forecast</c>, then
/// one <c>tier=level</c> for each tier <c>--effort-by-tier</c> names, in the tiers' own order
/// (weak, normal, elite, boss). So <c>--brief --effort-by-tier
/// boss=high,elite=high</c> is <c>brief+elite=high+boss=high</c> however it was typed, and the
/// label can be read back (<see cref="TierEffort"/>).
/// </para>
/// <para>
/// A position test can also be given a name of its own (<c>arena position --label old</c>),
/// which says what no switch can: which prompt it was played under. The name comes first and
/// the switches follow it (<c>new+forecast</c>), so a name never hides a switch.
/// </para>
/// </summary>
public static class ExperimentLabel
{
    /// <summary>The label of a run with no switch on, and of every result line from before there were any.</summary>
    public const string Base = "base";

    public const string Brief = "brief";
    public const string Forecast = "forecast";

    private const char Join = '+';

    /// <summary>The label for these switches.</summary>
    /// <param name="effortByTier">What <c>--effort-by-tier</c> named: tier to effort level.</param>
    /// <param name="name">A name the batch was given (<c>arena position --label</c>), or null.
    /// "base" names nothing: it is what a batch without a name and without a switch is called.</param>
    public static string Of(bool brief, ExperimentOptions simulator, IReadOnlyDictionary<string, string> effortByTier, string? name = null)
    {
        var parts = new List<string>();
        if (name is { Length: > 0 } && name != Base) parts.Add(name);
        if (brief) parts.Add(Brief);
        if (simulator.Forecast) parts.Add(Forecast);
        foreach (var tier in Scenarios.Tiers)
            if (effortByTier.TryGetValue(tier, out var level))
                parts.Add($"{tier}={level}");
        return parts.Count == 0 ? Base : string.Join(Join, parts);
    }

    /// <summary>
    /// What is wrong with <paramref name="name"/> as a batch's own name, or null when it can be
    /// one: letters, digits, "-", "_" and "." only (it goes into file names and table rows, and
    /// "+" and "=" are the label's own marks), at most 40 of them, and not the name of a switch,
    /// which would claim a switch that may not be on.
    /// </summary>
    public static string? NameProblem(string name)
    {
        if (name.Length is 0 or > 40 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            return "a label is 1 to 40 letters, digits, '-', '_' or '.'";
        if (name is Brief or Forecast)
            return $"'{name}' is the name of a switch (--{name}); pick another label";
        return null;
    }

    /// <summary>The simulator's switches that are on, as they are typed: "--forecast".</summary>
    public static IReadOnlyList<string> Switches(ExperimentOptions simulator) =>
        simulator.Forecast ? ["--" + Forecast] : [];

    /// <summary>The effort a label gives fights of <paramref name="tier"/>, or null when it
    /// leaves that tier to <c>--effort</c>.</summary>
    public static string? TierEffort(string label, string tier)
    {
        foreach (var part in label.Split(Join))
            if (part.Length > tier.Length + 1 && part[tier.Length] == '=' && part.StartsWith(tier, StringComparison.Ordinal))
                return part[(tier.Length + 1)..];
        return null;
    }

    /// <summary>The tier efforts in a label as "elite high, boss high", or null when it names none.</summary>
    public static string? TierEfforts(string label)
    {
        var parts = Scenarios.Tiers
            .Select(tier => (Tier: tier, Level: TierEffort(label, tier)))
            .Where(t => t.Level is not null)
            .Select(t => $"{t.Tier} {t.Level}")
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>Labels in the order the reports list them: <see cref="Base"/> first, then by name.</summary>
    public static List<string> Ordered(IEnumerable<string> labels) =>
        labels.Distinct(StringComparer.Ordinal)
            .OrderBy(label => label == Base ? 0 : 1).ThenBy(label => label, StringComparer.Ordinal)
            .ToList();
}
