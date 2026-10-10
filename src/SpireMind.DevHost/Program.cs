using SpireMind.Core;
using SpireMind.DevHost;

DevHostOptions options;
try
{
    options = DevHostOptions.Parse(args);
}
catch (UsageException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(DevHostOptions.Usage);
    return 2;
}

if (options.Help)
{
    Console.WriteLine(DevHostOptions.Usage);
    return 0;
}

if (options.Brain)
    return await BrainSmoke.RunAsync(options);
if (options.Bot)
    return await DevCoop.RunBotAsync(options);
if (options.Relay)
    return await DevCoop.RunRelayAsync(options);

var game = new FakeGame();
var versioner = new SnapshotVersioner();
var waiter = new DecisionWaiter(game, versioner);
var chat = DevChat.Create(Console.Out);
var tools = new BotTools(game, waiter, versioner, chat);

using var server = new BridgeServer(options.Port, tools, chat, "dev", Console.WriteLine);
server.Start();
Console.WriteLine("Fake game running. Ctrl+C to stop.");

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
await done.Task;
return 0;
