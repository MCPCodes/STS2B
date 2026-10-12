using SpireMind.Core;
using SpireMind.Core.Coop;

namespace SpireMind.DevHost;

/// <summary>
/// Co-op chat on localhost without the game: <c>scripts/dev-host.sh 17773 --bot</c> is a fake
/// bot copy and <c>scripts/dev-host.sh --relay</c> a fake human copy whose chat is relayed to
/// the bot copies by the real <see cref="CoopRelay"/>, as Mark's game does. Start two bots
/// (17773 and 17774, with --echo) and the relay, then type in the relay's window:
/// <c>hello</c>, <c>/pause</c>, <c>/pause Silent</c>, <c>/bots</c>, <c>/bot add Regent</c>.
/// Every chat line of each host is printed. Only 127.0.0.1 is contacted.
/// </summary>
public static class DevCoop
{
    /// <summary>The commands a bot copy's brain owns, which the fake bot answers with a stub line.</summary>
    public static IReadOnlyList<string> StubCommands { get; } = ["start", "stop", "budget", "model", "effort", "fast"];

    /// <summary>How often the echo looks for messages to answer.</summary>
    public static readonly TimeSpan EchoInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// <c>--bot</c>: serves the fake fight on the port as a bot copy playing
    /// <see cref="DevHostOptions.Character"/> with <see cref="DevHostOptions.BotNetId"/>, with
    /// stub brain commands and, with <c>--echo</c>, an answer to each message for it. Runs until
    /// Ctrl+C, or until the relay's /bot stop asks it to quit.
    /// </summary>
    public static async Task<int> RunBotAsync(DevHostOptions options)
    {
        var chat = DevChat.Create(Console.Out, options.Character);
        AddStubCommands(chat);
        var quit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var info = new InstanceInfo(true, options.Port, $"bot-{options.Port}")
        {
            NetId = options.BotNetId,
            // The dev host has no brain, so /bots in the relay says this bot isn't playing.
            Playing = () => "none",
            Status = () => new BotStatus("a dev host: no game, so no lobby", options.Character.ToUpperInvariant(), false, false),
            Quit = from =>
            {
                Console.WriteLine($"{from} asked this fake bot copy to quit; quitting in a moment.");
                // The reply goes out first, as the game's own quit runs on a later frame.
                _ = Task.Delay(200).ContinueWith(_ => quit.TrySetResult(), TaskScheduler.Default);
                return "Leaving the game and quitting (dev host).";
            },
            RetryJoin = () => Console.WriteLine("asked to look for the host's lobby now (a dev host has none)"),
        };

        using var server = new BridgeServer(options.Port, FakeTools(chat), chat, "dev", Console.WriteLine) { Instance = info };
        server.Start();
        using var stop = new CancellationTokenSource();
        var echo = options.Echo ? Task.Run(() => EchoAsync(chat, Console.Out, EchoInterval, stop.Token)) : Task.CompletedTask;
        Console.WriteLine($"Fake bot copy: {options.Character}, NetId {options.BotNetId}, port {options.Port}" +
                          (options.Echo ? ", answering every message for it" : "") + ". Ctrl+C to stop.");

        await Task.WhenAny(UntilCtrlC(), quit.Task);
        stop.Cancel();
        await echo;
        return 0;
    }

    /// <summary>
    /// <c>--relay</c>: a fake human copy. Serves the fake fight on the port with no bot role,
    /// relays its chat to the bot copies on <see cref="DevHostOptions.BotPorts"/> (else
    /// SPIREMIND_BOT_PORTS, else 17773 to 17775) through a <see cref="FakeCoopBots"/>, and posts
    /// each line typed on stdin as Mark. Runs until Ctrl+C.
    /// </summary>
    public static async Task<int> RunRelayAsync(DevHostOptions options)
    {
        var chat = DevChat.Create(Console.Out);
        var ports = options.BotPorts ?? EnvPorts(Console.Out) ?? CoopRelayOptions.DefaultProbePorts;

        using var server = new BridgeServer(options.Port, FakeTools(chat), chat, "dev", Console.WriteLine);
        server.Start();
        var bots = new FakeCoopBots(Console.Out);
        using var relay = new CoopRelay(chat, new CoopRelayOptions { OwnPort = options.Port, ProbePorts = ports }, bots,
            line => Console.WriteLine($"coop: {line}"));
        bots.NextBot = () => NextBot(relay.Peers, ports, options.Port);
        relay.Start();
        Console.WriteLine($"Fake human copy on port {options.Port}, looking for bot copies on {string.Join(", ", ports)}. " +
                          "Type a line to say it as Mark, or a command such as /bots. Ctrl+C to stop.");

        // Console.ReadLine blocks, so it gets a thread of its own; the end of input only stops the reading.
        var reader = new Thread(() =>
        {
            while (Console.ReadLine() is { } line)
                chat.PostFromHuman("Mark", line);
        }) { IsBackground = true, Name = "stdin" };
        reader.Start();

        await UntilCtrlC();
        return 0;
    }

    /// <summary>Adds /start, /stop, /budget, /model, /effort and /fast, each answering
    /// "(dev host) /name args", since the dev host has no brain.</summary>
    public static void AddStubCommands(ChatHub chat)
    {
        foreach (var name in StubCommands)
        {
            var usage = name switch { "model" => "[name]", "effort" => "[level]", "fast" => "[on|off]", _ => "" };
            chat.AddCommand(name, usage, "a stub: the dev host has no brain",
                (_, args) => args.Length > 0 ? $"(dev host) /{name} {args}" : $"(dev host) /{name}");
        }
    }

    /// <summary>
    /// Until <paramref name="ct"/> is cancelled, answers each human message queued for this bot
    /// with "Heard you, name." on the bot's side of the chat. Teammate lines (another bot's,
    /// relayed in) are taken but never answered, so two echoing bots can't talk forever. Past
    /// the chat's burst limit the answer is dropped and the limit is printed.
    /// </summary>
    public static async Task EchoAsync(ChatHub chat, TextWriter output, TimeSpan interval, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { return; }

            // HasUnread counts only the human's messages.
            if (!chat.HasUnread) continue;
            foreach (var message in chat.TakeUnread())
            {
                if (message.Teammate) continue;
                try { chat.Say($"Heard you, {message.From}."); }
                catch (InvalidOperationException ex) { output.WriteLine($"echo: {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// The port and character a new fake bot should take: the first probed port with no live
    /// bot on it (never the relay's own), and the first character no live bot plays. Null port
    /// when every probed port has a bot.
    /// </summary>
    public static (int? Port, string Character) NextBot(IReadOnlyList<BotPeer> peers, IReadOnlyList<int> ports, int ownPort)
    {
        var live = peers.Where(p => p.Live).ToList();
        var port = ports.Where(p => p != ownPort && live.All(l => l.Port != p)).Select(p => (int?)p).FirstOrDefault();
        var character = DevHostOptions.Characters.FirstOrDefault(c => live.All(l => !c.Equals(l.Character, StringComparison.OrdinalIgnoreCase)))
                        ?? DevHostOptions.DefaultCharacter;
        return (port, character);
    }

    /// <summary>
    /// Stands in for the game side's bot launcher and bubbles: it can't start a copy, so it says
    /// how to start a fake one; a bubble or a thought cloud is printed; it knows of no copies (the
    /// relay finds the fake bots by probing their ports, and /bot stop asks them through their
    /// bridges) and shows no nameplates.
    /// </summary>
    public sealed class FakeCoopBots(TextWriter output) : ICoopBots
    {
        /// <summary>Where and as whom the next fake bot would run; set once the relay exists.</summary>
        public Func<(int? Port, string Character)>? NextBot { get; set; }

        public IReadOnlyList<(int Port, ulong NetId, string Character)> Known { get; } = [];

        public Task<string> StartCopyAsync(string? character, CancellationToken ct)
        {
            var next = NextBot?.Invoke() ?? (null, DevHostOptions.DefaultCharacter);
            var name = character is null || character.Equals("Random", StringComparison.OrdinalIgnoreCase) ? next.Character : character;
            var port = next.Port?.ToString() ?? "<free port>";
            return Task.FromResult($"(dev host) start one with: scripts/dev-host.sh {port} --bot --character {name} --echo");
        }

        public void ShowLine(ulong netId, string text) => output.WriteLine($"bubble over {netId}: {text}");

        public Task<string> StopAllAsync(CancellationToken ct) => Task.FromResult("(dev host) this host started no bot copies.");

        public void Thinking(ulong netId, bool on) => output.WriteLine(on ? $"thought cloud over {netId}" : $"no thought cloud over {netId}");

        public string? NameOf(ulong netId) => null;
    }

    // The fake fight's tools, which the bridge needs; co-op chat never uses them.
    private static BotTools FakeTools(ChatHub chat)
    {
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        return new BotTools(game, new DecisionWaiter(game, versioner), versioner, chat);
    }

    // SPIREMIND_BOT_PORTS, or null when unset or unusable (the problem is printed).
    private static IReadOnlyList<int>? EnvPorts(TextWriter output)
    {
        var ports = CoopRelayOptions.ParsePorts(Environment.GetEnvironmentVariable(CoopRelayOptions.PortsEnvVar), out var warning);
        if (warning is not null)
            output.WriteLine(warning);
        return ports;
    }

    private static Task UntilCtrlC()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
        return done.Task;
    }
}
