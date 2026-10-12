using SpireMind.Arena;

// The card-value measurement has its own options (see ValuesCommand.Usage). A call for help
// goes on to the usage of every command below.
if (args.Length > 0 && args[0] == "values" && !args.Any(a => a is "--help" or "-h"))
    return ValuesCommand.Run(args[1..], TextWriter.Synchronized(Console.Out));

// So has the baseline of whole runs (see RunBaselineCommand.Usage): the scripted run players on whole acts.
if (args.Length > 0 && args[0] == "run-baseline" && !args.Any(a => a is "--help" or "-h"))
    return RunBaselineCommand.Run(args[1..], TextWriter.Synchronized(Console.Out));

ArenaOptions options;
try
{
    options = ArenaOptions.Parse(args);
}
catch (UsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(ArenaOptions.Usage);
    Console.Error.WriteLine(RunBaselineCommand.Usage);
    Console.Error.WriteLine(ValuesCommand.Usage);
    return 2;
}

if (options.Help)
{
    Console.WriteLine(ArenaOptions.Usage);
    Console.WriteLine(RunBaselineCommand.Usage);
    Console.WriteLine(ValuesCommand.Usage);
    return 0;
}

// Fights finish on their own tasks, but every line is written from one loop; the synchronized
// writer only keeps a late line from another thread whole.
var output = TextWriter.Synchronized(Console.Out);
return options.Command switch
{
    ArenaCommand.Baseline => BaselineCommand.Run(options, output),
    ArenaCommand.Run => await RunCommand.RunAsync(options, output),
    ArenaCommand.Position => await PositionCommand.RunAsync(options, output),
    ArenaCommand.Report => Report.Run(options, output),
    ArenaCommand.Compare => CompareCommand.Run(options, output),
    ArenaCommand.SelfCheck => await SelfCheckCommand.RunAsync(options, output),
    ArenaCommand.Honesty => HonestyCommand.Run(options, output),
    ArenaCommand.Content => ContentCommand.Run(output),
    ArenaCommand.Snapshot => ContentCommand.Snapshot(options.ScenarioId!, output, options.Experiments),
    _ => 2,
};
