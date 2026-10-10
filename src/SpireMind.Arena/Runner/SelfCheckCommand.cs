namespace SpireMind.Arena;

/// <summary>
/// <c>arena selfcheck</c>: the simulator's own checks on a set of generated fights
/// (<see cref="SelfCheck"/>), then one of those fights and one built by hand played over HTTP
/// through the tools a model gets (<see cref="ToolPathCheck"/>). It costs nothing and takes a
/// minute or two, so it is worth running after a change to the simulator or to SpireMind.Core,
/// before a batch. With <c>--checks</c> only the named scripted check classes run.
/// </summary>
public static class SelfCheckCommand
{
    /// <summary>Returns 0 when both halves passed, 1 otherwise.</summary>
    public static async Task<int> RunAsync(ArenaOptions options, TextWriter output)
    {
        if (options.Checks.Count > 0) return SelfCheck.Run(output, options.Scenarios, options.Seed, options.Checks) ? 0 : 1;

        var simulator = SelfCheck.Run(output, options.Scenarios, options.Seed);
        output.WriteLine();

        // The first fight with company for the main enemy, so a sequence can outlive its target.
        var scenarios = Scenarios.Generate(options.Scenarios, options.Seed);
        var fight = scenarios.FirstOrDefault(s => s.Summary.Contains(" + ", StringComparison.Ordinal)) ?? scenarios[0];
        var tools = await ToolPathCheck.RunAsync(fight, output);

        output.WriteLine();
        output.WriteLine(simulator && tools ? "PASSED" : "FAILED");
        return simulator && tools ? 0 : 1;
    }
}
