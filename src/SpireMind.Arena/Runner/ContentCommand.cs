namespace SpireMind.Arena;

/// <summary>
/// <c>arena content</c>: what the simulator holds of the game and what it leaves out, group by
/// group, with every name. Exit code 1 when something is neither registered nor left out.
/// </summary>
public static class ContentCommand
{
    private static readonly System.Text.Json.JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        // As a reader sees it, not as it travels: ">" and "+" unescaped.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary><c>arena snapshot ID</c>: the scenario's first snapshot, as get_state hands it to a model.</summary>
    /// <param name="experiments">The simulator's experiment switches (<c>--forecast</c>) the
    /// fight is made with, to see what a model gets when they are on. Null for none.</param>
    public static int Snapshot(string scenarioId, TextWriter output, ExperimentOptions? experiments = null)
    {
        Scenario scenario;
        try
        {
            scenario = Scenarios.FromId(scenarioId);
        }
        catch (ArgumentException ex)
        {
            output.WriteLine(ex.Message);
            return 2;
        }
        var game = new ArenaGame(scenario) { ActionDelay = TimeSpan.Zero, Experiments = experiments ?? new ExperimentOptions() };
        var snapshot = game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
        output.WriteLine($"{scenario.Id}: {scenario.Summary}" +
                         (experiments is not null && ExperimentLabel.Switches(experiments).Count > 0 ? $" (with {string.Join(" and ", ExperimentLabel.Switches(experiments))})" : ""));
        output.WriteLine(snapshot.ToJson().ToJsonString(Indented));
        return 0;
    }

    public static int Run(TextWriter output)
    {
        var registry = Content.Registry;
        output.WriteLine("Arena content, from the game's v0.107.1 data (Ironclad, ascension 0, act 1 \"Overgrowth\", single player):");
        output.WriteLine($"{registry.Cards.Count} cards, {registry.PowerNames.Count()} powers, {registry.MonsterNames.Count()} monsters, " +
                         $"{registry.EncounterNames.Count()} encounters, {registry.Potions.Count} potions, {registry.RelicNames.Count()} relic, " +
                         $"{registry.Afflictions.Count} afflictions.");
        output.WriteLine();
        Completeness.Write(output, names: true);

        output.WriteLine();
        output.WriteLine($"monsters ({registry.MonsterNames.Count()}):");
        output.WriteLine("  " + string.Join(", ", registry.MonsterNames.OrderBy(n => n, StringComparer.Ordinal)));
        output.WriteLine();
        output.WriteLine($"powers ({registry.PowerNames.Count()}):");
        output.WriteLine("  " + string.Join(", ", registry.PowerNames.OrderBy(n => n, StringComparer.Ordinal)));

        // The cards as a model reads them outside a fight: cost, type and our own wording of the rules text.
        output.WriteLine();
        output.WriteLine("card texts (base, then upgraded where the card can be upgraded):");
        foreach (var def in registry.Cards.Values.OrderBy(c => c.ClassName, StringComparer.Ordinal))
        {
            foreach (var upgraded in def.MaxUpgradeLevel > 0 ? new[] { false, true } : [false])
            {
                var card = new CardInstance(0, def);
                if (upgraded) card.ApplyUpgrade();
                var cost = card.CostsX ? "X" : card.BaseCost >= 0 ? card.BaseCost.ToString() : "-";
                output.WriteLine($"  {card.Title} [{cost}, {def.Type}, {def.Rarity}, {def.Target}]: {Fight.CardText(null, card, PileType.Deck)}");
            }
        }

        output.WriteLine();
        output.WriteLine("potion texts:");
        foreach (var potion in registry.Potions.Values.OrderBy(p => p.ClassName, StringComparer.Ordinal))
            output.WriteLine($"  {potion.Name} [{potion.Rarity}, {potion.Target}, {potion.Usage}]: {potion.Text(null)}");

        var problems = Completeness.Problems().Concat(registry.Problems).ToList();
        foreach (var problem in problems) output.WriteLine($"PROBLEM: {problem}");
        return problems.Count == 0 ? 0 : 1;
    }
}
