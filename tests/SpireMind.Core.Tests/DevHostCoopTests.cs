using System.Diagnostics;
using System.Text;
using SpireMind.Core;
using SpireMind.Core.Coop;
using SpireMind.DevHost;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>The dev host's co-op modes: --bot and --relay parsing, and the pieces they run.</summary>
public class DevHostCoopOptionsTests
{
    private static DevHostOptions Parse(params string[] args) => DevHostOptions.Parse(args);

    [Fact]
    public void No_mode_is_neither_bot_nor_relay()
    {
        var options = Parse();
        Assert.False(options.Bot);
        Assert.False(options.Relay);
        Assert.Equal("Ironclad", options.Character);
        Assert.Null(options.NetId);
        Assert.False(options.Echo);
        Assert.Null(options.BotPorts);
    }

    [Fact]
    public void Bot_defaults_to_the_Ironclad_and_a_NetId_from_its_port()
    {
        var options = Parse("17773", "--bot");
        Assert.True(options.Bot);
        Assert.False(options.Relay);
        Assert.False(options.Brain);
        Assert.Equal(17773, options.Port);
        Assert.Equal("Ironclad", options.Character);
        Assert.Null(options.NetId);
        Assert.Equal(1000UL, options.BotNetId);
        Assert.False(options.Echo);

        Assert.Equal(1001UL, Parse("17774", "--bot").BotNetId);
        Assert.Equal(1002UL, Parse("17775", "--bot").BotNetId);
        Assert.Equal(999UL, Parse("--bot").BotNetId);
    }

    [Fact]
    public void A_low_port_never_gets_the_hosts_NetId()
    {
        Assert.Equal(2UL, DevHostOptions.DefaultNetId(8080));
        Assert.Equal(2UL, DevHostOptions.DefaultNetId(1));
        Assert.Equal(1000UL, DevHostOptions.DefaultNetId(17773));
    }

    [Fact]
    public void Bot_takes_a_character_a_NetId_and_echo_in_any_order()
    {
        var options = Parse("17774", "--echo", "--net-id", "1005", "--bot", "--character", "the silent");
        Assert.True(options.Bot);
        Assert.Equal("Silent", options.Character);
        Assert.Equal(1005UL, options.NetId);
        Assert.Equal(1005UL, options.BotNetId);
        Assert.True(options.Echo);
    }

    [Theory]
    [InlineData("ironclad", "Ironclad")]
    [InlineData("SILENT", "Silent")]
    [InlineData("Defect", "Defect")]
    [InlineData("The Regent", "Regent")]
    [InlineData("necrobinder", "Necrobinder")]
    public void Characters_match_in_any_case_with_or_without_the(string given, string expected) =>
        Assert.Equal(expected, Parse("--bot", "--character", given).Character);

    [Fact]
    public void Relay_defaults_to_the_environment_or_the_default_ports()
    {
        var options = Parse("--relay");
        Assert.True(options.Relay);
        Assert.False(options.Bot);
        Assert.Equal(17772, options.Port);
        Assert.Null(options.BotPorts);
    }

    [Theory]
    [InlineData("17773,17774", new[] { 17773, 17774 })]
    [InlineData("17775", new[] { 17775 })]
    [InlineData("17773 17774", new[] { 17773, 17774 })]
    [InlineData("17773,17773,17774", new[] { 17773, 17774 })]
    public void Relay_takes_its_bot_ports(string list, int[] expected) =>
        Assert.Equal(expected, Parse("17780", "--relay", "--bot-ports", list).BotPorts);

    [Fact]
    public void Help_still_works_with_a_mode()
    {
        Assert.True(Parse("--bot", "--help").Help);
        Assert.True(Parse("-h", "--relay").Help);
    }

    [Fact]
    public void Usage_describes_both_modes()
    {
        Assert.Contains("--bot [--character <name>] [--net-id <n>] [--echo]", DevHostOptions.Usage);
        Assert.Contains("--relay [--bot-ports <list>]", DevHostOptions.Usage);
    }

    [Theory]
    [InlineData("--brain, --bot and --relay are different modes", "--bot", "--relay")]
    [InlineData("--brain, --bot and --relay are different modes", "--brain", "--bot")]
    [InlineData("--brain, --bot and --relay are different modes", "--relay", "--brain")]
    [InlineData("--character, --net-id and --echo only apply with --bot", "--character", "Silent")]
    [InlineData("--character, --net-id and --echo only apply with --bot", "--echo")]
    [InlineData("--character, --net-id and --echo only apply with --bot", "--relay", "--net-id", "1000")]
    [InlineData("--character, --net-id and --echo only apply with --bot", "--brain", "--echo")]
    [InlineData("--bot-ports only applies with --relay", "--bot-ports", "17773")]
    [InlineData("--bot-ports only applies with --relay", "17773", "--bot", "--bot-ports", "17774")]
    [InlineData("--model and --max-requests only apply with --brain", "--bot", "--model", "sonnet")]
    [InlineData("--model and --max-requests only apply with --brain", "--relay", "--max-requests", "2")]
    [InlineData("--character takes Ironclad, Silent, Defect, Regent or Necrobinder, not 'Watcher'", "--bot", "--character", "Watcher")]
    [InlineData("--character takes Ironclad, Silent, Defect, Regent or Necrobinder, not 'Random'", "--bot", "--character", "Random")]
    [InlineData("--character needs a value", "--bot", "--character")]
    [InlineData("--character needs a value", "--bot", "--character", "--echo")]
    [InlineData("--net-id takes a whole number of 1 or more, not '0'", "--bot", "--net-id", "0")]
    [InlineData("--net-id takes a whole number of 1 or more, not '-5'", "--bot", "--net-id", "-5")]
    [InlineData("--net-id takes a whole number of 1 or more, not 'lots'", "--bot", "--net-id", "lots")]
    [InlineData("--net-id needs a value", "--bot", "--net-id")]
    [InlineData("--bot-ports takes ports from 1 to 65535 separated by commas", "--relay", "--bot-ports", "70000")]
    [InlineData("--bot-ports takes ports from 1 to 65535 separated by commas", "--relay", "--bot-ports", "17773,x")]
    [InlineData("--bot-ports takes ports from 1 to 65535 separated by commas", "--relay", "--bot-ports", ",")]
    [InlineData("--bot-ports needs a value", "--relay", "--bot-ports")]
    [InlineData("Only the port can come without an option name", "--bot", "17773")]
    [InlineData("There's no --party option", "--relay", "--party")]
    public void Bad_arguments_say_what_is_wrong(string expected, params string[] args)
    {
        var ex = Assert.Throws<UsageException>(() => DevHostOptions.Parse(args));
        Assert.Contains(expected, ex.Message);
    }
}

/// <summary>The pieces --bot and --relay run: the chat, the stub commands, the echo and the fake launcher.</summary>
public sealed class DevCoopTests : IDisposable
{
    private readonly List<IDisposable> _cleanup = [];

    public void Dispose()
    {
        foreach (var item in Enumerable.Reverse(_cleanup))
        {
            try { item.Dispose(); }
            catch { /* already gone */ }
        }
    }

    private static async Task Until(Func<bool> condition, string what, int timeoutMs = 8000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
                throw new Xunit.Sdk.XunitException($"Timed out waiting for {what}.");
            await Task.Delay(10);
        }
    }

    private static bool Has(ChatHub chat, string text) => chat.LinesSince(0).Any(l => l.Text == text);

    /// <summary>Output written from several threads at once and read while they write.</summary>
    private sealed class LockedWriter : TextWriter
    {
        private readonly StringBuilder _text = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { lock (_text) _text.Append(value); }
        public override void Write(string? value) { lock (_text) _text.Append(value); }
        public override void WriteLine(string? value) { lock (_text) _text.Append(value).Append('\n'); }
        public override string ToString() { lock (_text) return _text.ToString(); }
    }

    [Fact]
    public void The_chat_plays_the_given_character_and_prints_every_line()
    {
        var output = new StringWriter();
        var chat = DevChat.Create(output, "Silent");

        Assert.Equal("Silent", chat.BotName);
        Assert.Equal("Silent", chat.Persona!["name"]!.GetValue<string>());
        Assert.Equal("The Silent", chat.Persona!["title"]!.GetValue<string>());
        Assert.Equal("SILENT", chat.Persona!["id"]!.GetValue<string>());
        Assert.Contains("ellipses", chat.Persona!["tone"]!.GetValue<string>());

        chat.PostFromHuman("Mark", "hello");
        chat.Say("...");
        chat.Announce("Silent paused.");
        chat.PostFromTeammate("Defect", "Beep. Going frost.");
        chat.AnnounceFrom("Defect", "Defect paused.");

        var text = output.ToString();
        Assert.Contains("chat Mark: hello", text);
        Assert.Contains("chat Silent: ...", text);
        Assert.Contains("chat: Silent paused.", text);
        Assert.Contains("chat Defect (relayed): Beep. Going frost.", text);
        Assert.Contains("chat (relayed): Defect: Defect paused.", text);
    }

    [Fact]
    public void The_default_chat_is_still_the_Ironclad()
    {
        var chat = DevChat.Create(TextWriter.Null);
        Assert.Equal("Ironclad", chat.BotName);
        Assert.Equal("A terse, grim soldier.", chat.Persona!["tone"]!.GetValue<string>());
    }

    [Fact]
    public void Stub_commands_answer_with_what_was_typed()
    {
        var chat = DevChat.Create(TextWriter.Null, "Defect");
        DevCoop.AddStubCommands(chat);

        Assert.Equal("(dev host) /model sonnet", chat.PostFromHuman("Mark", "/model sonnet").Reply);
        Assert.Equal("(dev host) /effort high", chat.PostFromHuman("Mark", "/effort high").Reply);
        Assert.Equal("(dev host) /fast on", chat.PostFromHuman("Mark", "/fast on").Reply);
        Assert.Equal("(dev host) /start", chat.PostFromHuman("Mark", "/start").Reply);
        Assert.Equal("(dev host) /stop", chat.PostFromHuman("Mark", "/stop").Reply);
        Assert.Equal("(dev host) /budget", chat.PostFromHuman("Mark", "/budget").Reply);
        // The built-ins stay real.
        Assert.Contains("paused", chat.PostFromHuman("Mark", "/pause").Reply);
        Assert.True(chat.Paused);
    }

    [Fact]
    public async Task Echo_answers_the_humans_messages_and_never_a_teammates()
    {
        var output = new LockedWriter();
        var chat = DevChat.Create(output, "Silent");
        using var stop = new CancellationTokenSource();
        var echo = Task.Run(() => DevCoop.EchoAsync(chat, output, TimeSpan.FromMilliseconds(10), stop.Token));

        chat.PostFromTeammate("Defect", "Silent, take the left one.");
        chat.PostFromHuman("Mark", "hello");
        chat.PostFromHuman("Mark", "Defect, go frost.");   // for another bot, so never queued here
        await Until(() => Has(chat, "Heard you, Mark."), "the echo");
        await Task.Delay(100);

        stop.Cancel();
        await echo.WaitAsync(TimeSpan.FromSeconds(5));
        var said = chat.LinesSince(0).Where(l => l.Kind == ChatLineKind.Bot && !l.Remote).Select(l => l.Text).ToList();
        Assert.Equal(["Heard you, Mark."], said);
        Assert.Empty(chat.PeekUnread());
    }

    [Fact]
    public async Task Fake_launcher_says_how_to_start_the_next_bot_and_prints_bubbles()
    {
        var output = new StringWriter();
        var bots = new DevCoop.FakeCoopBots(output);
        Assert.Empty(bots.Known);

        Assert.Equal("(dev host) start one with: scripts/dev-host.sh <free port> --bot --character Regent --echo",
            await bots.StartCopyAsync("Regent", CancellationToken.None));

        bots.NextBot = () => (17775, "Defect");
        Assert.Equal("(dev host) start one with: scripts/dev-host.sh 17775 --bot --character Regent --echo",
            await bots.StartCopyAsync("Regent", CancellationToken.None));
        Assert.Equal("(dev host) start one with: scripts/dev-host.sh 17775 --bot --character Defect --echo",
            await bots.StartCopyAsync(null, CancellationToken.None));
        Assert.Equal("(dev host) start one with: scripts/dev-host.sh 17775 --bot --character Defect --echo",
            await bots.StartCopyAsync("Random", CancellationToken.None));

        bots.ShowLine(1000, "Heard you, Mark.");
        Assert.Contains("bubble over 1000: Heard you, Mark.", output.ToString());
    }

    [Fact]
    public void The_next_bot_takes_a_free_port_and_a_character_nobody_plays()
    {
        var peers = new List<BotPeer>
        {
            new(17773, "a", "Silent", 1000, null, false, true),
            new(17774, "b", "Ironclad", 1001, null, false, false), // stopped answering: its port is free again
        };
        Assert.Equal((17774, "Ironclad"), DevCoop.NextBot(peers, [17773, 17774, 17775], 17772));
        Assert.Equal((17775, "Ironclad"), DevCoop.NextBot(peers, [17773, 17775], 17772));
        Assert.Equal((17775, "Ironclad"), DevCoop.NextBot(peers, [17774, 17775], 17774));
        Assert.Equal(((int?)null, "Ironclad"), DevCoop.NextBot(peers, [17773], 17772));
        Assert.Equal((17773, "Ironclad"), DevCoop.NextBot([], CoopRelayOptions.DefaultProbePorts, 17772));
    }

    /// <summary>The smoke test in one process: two fake bots built as --bot builds them, and the
    /// relay as --relay builds it.</summary>
    [Fact]
    public async Task Two_echoing_bots_answer_the_relay()
    {
        using var stop = new CancellationTokenSource();
        var silent = StartBot("Silent", stop.Token);
        var defect = StartBot("Defect", stop.Token);

        var output = new LockedWriter();
        var mark = DevChat.Create(output);
        var bots = new DevCoop.FakeCoopBots(output);
        var ports = new[] { silent.Port, defect.Port };
        var own = BridgeTests.FreePort();
        var relay = new CoopRelay(mark, new CoopRelayOptions
        {
            OwnPort = own,
            ProbePorts = ports,
            PollInterval = TimeSpan.FromMilliseconds(30),
            ProbeInterval = TimeSpan.FromMilliseconds(60),
            ProbeTimeout = TimeSpan.FromMilliseconds(500),
            PollTimeout = TimeSpan.FromMilliseconds(500),
            PostTimeout = TimeSpan.FromSeconds(2),
        }, bots);
        _cleanup.Add(relay);
        bots.NextBot = () => DevCoop.NextBot(relay.Peers, ports, own);
        relay.Start();
        await Until(() => relay.Peers.Count(p => p.Live) == 2, "both bots to link");

        mark.PostFromHuman("Mark", "hello");
        await Until(() => mark.LinesSince(0).Count(l => l.Remote && l.Text == "Heard you, Mark.") == 2, "both echoes");
        await Until(() => output.ToString().Contains($"bubble over {silent.NetId}: Heard you, Mark."), "Silent's bubble");
        Assert.Contains($"bubble over {defect.NetId}: Heard you, Mark.", output.ToString());

        mark.PostFromHuman("Mark", "/model Silent sonnet");
        await Until(() => Has(mark, "Silent: (dev host) /model sonnet"), "Silent's stub reply");
        Assert.DoesNotContain(mark.LinesSince(0), l => l.Text.StartsWith("Defect: (dev host) /model", StringComparison.Ordinal));

        mark.PostFromHuman("Mark", "/bot add Regent");
        var next = $"(dev host) start one with: scripts/dev-host.sh <free port> --bot --character Regent --echo";
        await Until(() => Has(mark, next), "the fake launcher's line");
    }

    private (int Port, ulong NetId) StartBot(string character, CancellationToken ct)
    {
        var chat = DevChat.Create(TextWriter.Null, character);
        DevCoop.AddStubCommands(chat);
        var port = BridgeTests.FreePort();
        var options = DevHostOptions.Parse([port.ToString(), "--bot", "--character", character, "--echo"]);
        var info = new InstanceInfo(true, port, $"bot-{port}") { NetId = options.BotNetId, Playing = () => "none" };
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        var tools = new BotTools(game, new DecisionWaiter(game, versioner), versioner, chat);
        var server = new BridgeServer(port, tools, chat, "test", _ => { }) { Instance = info };
        server.Start();
        _cleanup.Add(server);
        _ = Task.Run(() => DevCoop.EchoAsync(chat, TextWriter.Null, TimeSpan.FromMilliseconds(10), ct));
        return (port, options.BotNetId);
    }
}
