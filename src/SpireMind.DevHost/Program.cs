using System.Text.Json.Nodes;
using SpireMind.Core;

var port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 17772;

var game = new FakeGame();
var versioner = new SnapshotVersioner();
var waiter = new DecisionWaiter(game, versioner);
// A small stand-in for the persona Identity reads from the game, so get_persona works here too.
var chat = new ChatHub
{
    BotName = "Ironclad",
    Persona = new JsonObject { ["name"] = "Ironclad", ["title"] = "The Ironclad", ["id"] = "IRONCLAD", ["tone"] = "A terse, grim soldier." },
};
chat.LineAdded += line => Console.WriteLine(line.From.Length > 0 ? $"chat {line.From}: {line.Text}" : $"chat: {line.Text}");
var tools = new BotTools(game, waiter, versioner, chat);

using var server = new BridgeServer(port, tools, chat, "dev", Console.WriteLine);
server.Start();
Console.WriteLine("Fake game running. Ctrl+C to stop.");

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
await done.Task;
