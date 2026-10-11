using System.Collections.Concurrent;
using System.Diagnostics;
using SpireMind.Core;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>
/// The co-op relay against real bridges on localhost: fake bot copies (BridgeServer with a bot
/// InstanceInfo) and the human's chat hub, with short timings.
/// </summary>
public sealed class CoopRelayTests : IDisposable
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

    /// <summary>A fake bot copy: a bridge with a bot identity and a few stub commands.</summary>
    private sealed class Bot : IDisposable
    {
        public required BridgeServer Server { get; init; }
        public required ChatHub Chat { get; init; }
        public required InstanceInfo Info { get; init; }
        public int Port => Server.Port;
        public ConcurrentQueue<string> ModelArgs { get; } = new();
        public ConcurrentQueue<string> EffortArgs { get; } = new();
        public void Dispose() => Server.Dispose();
    }

    private Bot StartBot(string character, ulong netId, int? port = null, bool keep = true)
    {
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        var chat = new ChatHub { BotName = character };
        var tools = new BotTools(game, new DecisionWaiter(game, versioner), versioner, chat);
        var p = port ?? BridgeTests.FreePort();
        var info = new InstanceInfo(true, p, $"bot-{p}") { NetId = netId };
        var server = new BridgeServer(p, tools, chat, "test", _ => { }) { Instance = info };
        var bot = new Bot { Server = server, Chat = chat, Info = info };
        chat.AddCommand("model", "[name]", "sets the model", (_, args) =>
        {
            bot.ModelArgs.Enqueue(args);
            return $"{character} now uses {args}.";
        });
        chat.AddCommand("effort", "[level]", "sets the effort", (_, args) =>
        {
            bot.EffortArgs.Enqueue(args);
            return $"{character} effort {args}.";
        });
        server.Start();
        if (keep) _cleanup.Add(bot);
        return bot;
    }

    private static CoopRelayOptions Options(int ownPort, params int[] ports) => new()
    {
        OwnPort = ownPort,
        ProbePorts = ports,
        PollInterval = TimeSpan.FromMilliseconds(30),
        ProbeInterval = TimeSpan.FromMilliseconds(60),
        ProbeTimeout = TimeSpan.FromMilliseconds(500),
        PollTimeout = TimeSpan.FromMilliseconds(500),
        PostTimeout = TimeSpan.FromSeconds(2),
        DownAfterFailures = 2,
    };

    private (CoopRelay Relay, ChatHub Mark, ConcurrentQueue<string> Log) StartRelay(CoopRelayOptions options,
        ICoopBots? coopBots = null, Func<DateTime>? clock = null, Func<bool>? inCoop = null)
    {
        var mark = new ChatHub { BotName = "Ironclad" };
        var log = new ConcurrentQueue<string>();
        var relay = new CoopRelay(mark, options, coopBots, log.Enqueue, clock, inCoop);
        _cleanup.Add(relay);
        relay.Start();
        return (relay, mark, log);
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

    // Long enough for several polls and probes to pass.
    private static Task Settle() => Task.Delay(400);

    private static int Count(ChatHub chat, string text) => chat.LinesSince(0).Count(l => l.Text == text);

    private static bool Has(ChatHub chat, string text) => Count(chat, text) > 0;

    private sealed class FakeCoopBots : ICoopBots
    {
        private volatile IReadOnlyList<(int Port, ulong NetId, string Character)> _known = [];
        public IReadOnlyList<(int Port, ulong NetId, string Character)> Known => _known;
        public void SetKnown(params (int Port, ulong NetId, string Character)[] known) => _known = known;

        public ConcurrentQueue<(ulong NetId, string Text)> Shown { get; } = new();
        public void ShowLine(ulong netId, string text) => Shown.Enqueue((netId, text));

        public ConcurrentQueue<string?> Starts { get; } = new();
        public TaskCompletionSource<string> NextStart { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> StartCopyAsync(string? character, CancellationToken ct)
        {
            Starts.Enqueue(character);
            return NextStart.Task;
        }
    }

    /// <summary>Fails POSTs while told to, for a bot copy that closes mid-command.</summary>
    private sealed class FlakyHandler() : DelegatingHandler(new SocketsHttpHandler { UseProxy = false })
    {
        public volatile bool FailPosts;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (FailPosts && request.Method == HttpMethod.Post)
                throw new HttpRequestException("connection refused");
            return base.SendAsync(request, ct);
        }
    }

    [Fact]
    public async Task Discovery_links_only_bots_and_never_its_own_port()
    {
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        var humanChat = new ChatHub();
        var humanPort = BridgeTests.FreePort();
        using var human = new BridgeServer(humanPort, new BotTools(game, new DecisionWaiter(game, versioner), versioner, humanChat),
            humanChat, "test", _ => { });
        human.Start();
        var silent = StartBot("Silent", 1000);
        var own = StartBot("Defect", 1001); // a bot on the relay's own port must never be linked

        var (relay, mark, log) = StartRelay(Options(own.Port, humanPort, silent.Port, own.Port, 0, 70000));
        await Until(() => relay.Peers.Count == 1, "the bot to link");
        await Settle();

        var peer = Assert.Single(relay.Peers);
        Assert.Equal(silent.Port, peer.Port);
        Assert.Equal(silent.Server.Session, peer.Session);
        Assert.Equal("Silent", peer.Character);
        Assert.Equal(1000UL, peer.NetId);
        Assert.True(peer.Live);
        Assert.True(relay.BotsExist);
        Assert.Equal(1, Count(mark, $"Silent joined the chat (port {silent.Port})."));
        Assert.Single(log, l => l.Contains($"port {humanPort}") && l.Contains("role human"));
        Assert.DoesNotContain(log, l => l.Contains($"port {own.Port}") && !l.Contains("looking for"));
        Assert.Equal(CoopRelay.CoopHelpNote, mark.HelpNote);
    }

    [Fact]
    public async Task First_contact_skips_old_lines_and_a_say_shows_once_with_a_bubble()
    {
        var silent = StartBot("Silent", 1000);
        silent.Chat.Say("Old news.");
        silent.Chat.Announce("Old system line.");
        var coopBots = new FakeCoopBots();
        var (_, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port), coopBots);
        await Until(() => Has(mark, $"Silent joined the chat (port {silent.Port})."), "the join line");

        silent.Chat.Say("Poison   is ticking.");
        await Until(() => Has(mark, "Poison is ticking."), "the bot's line");
        await Settle();

        var line = mark.LinesSince(0).Single(l => l.Text == "Poison is ticking.");
        Assert.Equal(ChatLineKind.Bot, line.Kind);
        Assert.True(line.Remote);
        Assert.Equal("Silent", line.From);
        Assert.Equal(1, Count(mark, "Poison is ticking."));
        Assert.False(Has(mark, "Old news."));
        Assert.DoesNotContain(mark.LinesSince(0), l => l.Text.Contains("Old system line."));
        Assert.Equal([(1000UL, "Poison is ticking.")], coopBots.Shown);

        // A system line from the bot shows with its name in front.
        silent.Chat.Announce("Switching to the subscription.");
        await Until(() => Has(mark, "Silent: Switching to the subscription."), "the bot's system line");
        var system = mark.LinesSince(0).Single(l => l.Text == "Silent: Switching to the subscription.");
        Assert.Equal(ChatLineKind.System, system.Kind);
        Assert.True(system.Remote);
        Assert.Single(coopBots.Shown);
    }

    [Fact]
    public async Task Marks_lines_reach_every_bot_and_each_bot_decides_by_name()
    {
        var silent = StartBot("Silent", 1000);
        var defect = StartBot("Defect", 1001);
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port, defect.Port));
        await Until(() => relay.Peers.Count(p => p.Live) == 2, "both bots to link");

        mark.PostFromHuman("Mark", "Silent, go poison");
        await Until(() => Has(silent.Chat, "Silent, go poison") && Has(defect.Chat, "Silent, go poison"), "the line at both bots");
        Assert.True(silent.Chat.HasUnread);
        Assert.False(defect.Chat.HasUnread);
        var atBot = silent.Chat.LinesSince(0).Single(l => l.Text == "Silent, go poison");
        Assert.Equal(ChatLineKind.Human, atBot.Kind);
        Assert.Equal("Mark", atBot.From);

        mark.PostFromHuman("Mark", "nice fight");
        await Until(() => Has(silent.Chat, "nice fight") && Has(defect.Chat, "nice fight"), "the second line at both bots");
        Assert.True(defect.Chat.HasUnread);
        await Settle();

        // Mark's copy doesn't keep his lines for a brain that can't play, nor show them twice.
        Assert.False(mark.HasUnread);
        Assert.Equal(1, Count(mark, "nice fight"));
        Assert.Equal(1, Count(mark, "Silent, go poison"));
    }

    [Fact]
    public async Task A_command_reaches_every_bot_and_each_reply_shows_once()
    {
        var silent = StartBot("Silent", 1000);
        var defect = StartBot("Defect", 1001);
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port, defect.Port));
        await Until(() => relay.Peers.Count(p => p.Live) == 2, "both bots to link");

        var before = mark.LastSeq;
        var result = mark.PostFromHuman("Mark", "/pause");
        Assert.Equal(new PostResult(PostOutcome.Command), result);
        Assert.False(mark.Paused);

        const string silentReply = "Silent: Silent paused. Type /resume to hand back control.";
        const string defectReply = "Defect: Defect paused. Type /resume to hand back control.";
        await Until(() => Has(mark, silentReply) && Has(mark, defectReply), "both replies");
        Assert.True(silent.Chat.Paused);
        Assert.True(defect.Chat.Paused);
        await Settle();

        Assert.Equal(1, Count(mark, silentReply));
        Assert.Equal(1, Count(mark, defectReply));
        Assert.Equal(2, mark.LinesSince(before).Count);
        Assert.All(mark.LinesSince(before), l => Assert.True(l.Remote));
        await Until(() => relay.Peers.All(p => p.Paused), "the paused flags");

        mark.PostFromHuman("Mark", "/RESUME");
        await Until(() => Has(mark, "Silent: Silent has control again.") && Has(mark, "Defect: Defect has control again."), "the resume replies");
        Assert.False(silent.Chat.Paused);
        Assert.False(defect.Chat.Paused);
    }

    [Fact]
    public async Task A_bots_name_first_sends_a_command_to_that_bot_only()
    {
        var silent = StartBot("Silent", 1000);
        var defect = StartBot("Defect", 1001);
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port, defect.Port));
        await Until(() => relay.Peers.Count(p => p.Live) == 2, "both bots to link");

        Assert.Equal("", mark.PostFromHuman("Mark", "/model Silent sonnet").Reply ?? "");
        await Until(() => Has(mark, "Silent: Silent now uses sonnet."), "Silent's reply");
        Assert.Equal(["sonnet"], silent.ModelArgs);
        Assert.Empty(defect.ModelArgs);

        mark.PostFromHuman("Mark", "/model the defect, big");
        await Until(() => Has(mark, "Defect: Defect now uses big."), "Defect's reply");
        Assert.Equal(["big"], defect.ModelArgs);
        Assert.Equal(["sonnet"], silent.ModelArgs);

        // Without a name it goes to both; a word that isn't a name stays in the arguments.
        mark.PostFromHuman("Mark", "/effort high");
        await Until(() => Has(mark, "Silent: Silent effort high.") && Has(mark, "Defect: Defect effort high."), "both effort replies");

        var missing = mark.PostFromHuman("Mark", "/pause Regent");
        Assert.Equal("No bot is playing Regent right now. /bots lists them.", missing.Reply);
        Assert.False(silent.Chat.Paused);

        // /help is this copy's own, ending with the co-op note, unless a bot is named.
        var help = mark.PostFromHuman("Mark", "/help").Reply!;
        Assert.StartsWith("Commands: ", help);
        Assert.EndsWith(CoopRelay.CoopHelpNote, help);
        Assert.Contains("/bots lists the bot copies", help);
        Assert.Equal("", mark.PostFromHuman("Mark", "/help Silent").Reply ?? "");
        await Until(() => mark.LinesSince(0).Any(l => l.Text.StartsWith("Silent: Commands: ")), "Silent's help");
        Assert.DoesNotContain(mark.LinesSince(0), l => l.Text.StartsWith("Defect: Commands: "));
        await Settle();
        Assert.Single(silent.ModelArgs);
    }

    [Fact]
    public async Task Without_bots_every_command_runs_here_as_in_solo_play()
    {
        var nobody = BridgeTests.FreePort();
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), nobody));
        var started = 0;
        mark.AddCommand("start", "", "starts the brain", (_, _) => { started++; return "Started here."; });
        await Settle();

        Assert.False(relay.BotsExist);
        Assert.Empty(relay.Peers);
        Assert.Equal("Started here.", mark.PostFromHuman("Mark", "/start").Reply);
        Assert.Equal(1, started);
        Assert.Equal("Ironclad paused. Type /resume to hand back control.", mark.PostFromHuman("Mark", "/pause").Reply);
        Assert.True(mark.Paused);
        Assert.DoesNotContain(CoopRelay.CoopHelpNote, mark.PostFromHuman("Mark", "/help").Reply);
        Assert.Null(mark.HelpNote);
        Assert.Equal(PostOutcome.Queued, mark.PostFromHuman("Mark", "what now?").Outcome);
        Assert.True(mark.HasUnread);
    }

    [Fact]
    public async Task Outside_co_op_a_linked_bot_gets_only_what_names_it()
    {
        // Solo play after a co-op session, with the bot copy still running.
        var silent = StartBot("Silent", 1000);
        var inCoop = false;
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port), inCoop: () => Volatile.Read(ref inCoop));
        var started = 0;
        mark.AddCommand("start", "", "starts the brain", (_, _) => { started++; return "Started here."; });
        await Until(() => relay.BotsExist, "the bot to link");
        Assert.False(relay.InCoop);

        // His own commands, lines and /help work as in solo play.
        Assert.Equal("Started here.", mark.PostFromHuman("Mark", "/start").Reply);
        Assert.Equal(1, started);
        Assert.Equal("Ironclad paused. Type /resume to hand back control.", mark.PostFromHuman("Mark", "/pause").Reply);
        Assert.True(mark.Paused);
        mark.PostFromHuman("Mark", "/resume");
        Assert.StartsWith("Commands: ", mark.PostFromHuman("Mark", "/help").Reply);
        Assert.Equal(PostOutcome.Queued, mark.PostFromHuman("Mark", "Ironclad, what now?").Outcome);
        Assert.True(mark.HasUnread);

        // A command or line that names the linked bot first still reaches it.
        Assert.Equal("", mark.PostFromHuman("Mark", "/model Silent sonnet").Reply ?? "");
        await Until(() => Has(mark, "Silent: Silent now uses sonnet."), "Silent's reply");
        mark.PostFromHuman("Mark", "Silent, ready for the next one?");
        await Until(() => Has(silent.Chat, "Silent, ready for the next one?"), "the named line");
        await Settle();
        Assert.False(Has(silent.Chat, "Ironclad, what now?"));
        Assert.False(silent.Chat.Paused);
        Assert.True(mark.HasUnread);

        // In co-op everything goes to the bots again.
        Volatile.Write(ref inCoop, true);
        Assert.Equal("", mark.PostFromHuman("Mark", "/pause").Reply ?? "");
        await Until(() => silent.Chat.Paused, "the forwarded pause");
        mark.PostFromHuman("Mark", "nice fight");
        await Until(() => Has(silent.Chat, "nice fight"), "the co-op line");
        Assert.False(mark.HasUnread);
        Assert.Equal(1, started);
    }

    [Fact]
    public async Task A_redirect_is_never_followed()
    {
        // Something on a probed port that answers every request with a redirect to a real bot.
        var silent = StartBot("Silent", 1000);
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        _cleanup.Add(new Closer(listener.Stop));
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var hits = 0;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                System.Net.Sockets.TcpClient client;
                try { client = await listener.AcceptTcpClientAsync(); }
                catch (Exception) { return; }
                Interlocked.Increment(ref hits);
                using (client)
                {
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    try { _ = await stream.ReadAsync(buffer); }
                    catch (Exception) { continue; }
                    var reply = $"HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:{silent.Port}/coop/info\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                    try { await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(reply)); }
                    catch (Exception) { /* gone */ }
                }
            }
        });

        var (relay, _, log) = StartRelay(Options(BridgeTests.FreePort(), port));
        await Until(() => Volatile.Read(ref hits) >= 2, "a few probes");
        await Settle();

        Assert.Empty(relay.Peers);
        Assert.False(relay.BotsExist);
        Assert.Single(log, l => l.Contains($"port {port} answered /coop/info with HTTP 307"));
        Assert.False(CoopRelay.NewLoopbackHandler(Options(0)).AllowAutoRedirect);
        Assert.False(CoopRelay.NewLoopbackHandler(Options(0)).UseProxy);
    }

    private sealed class Closer(Action close) : IDisposable
    {
        public void Dispose() => close();
    }

    [Fact]
    public async Task Bots_the_game_started_count_before_they_answer_and_are_linked_when_they_do()
    {
        var port = BridgeTests.FreePort();
        var coopBots = new FakeCoopBots();
        coopBots.SetKnown((port, 1002UL, "The Defect"));
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort()), coopBots);

        Assert.True(relay.BotsExist);
        Assert.Equal("No bot copy is answering yet; try again in a moment.", mark.PostFromHuman("Mark", "/pause").Reply);
        Assert.False(mark.Paused);
        Assert.EndsWith(CoopRelay.CoopHelpNote, mark.PostFromHuman("Mark", "/help").Reply);
        Assert.Equal($"Bots: Defect on {port} (starting).", mark.PostFromHuman("Mark", "/bots").Reply);

        // Its port comes from Known, not ProbePorts. Until it reports a character, the game's name labels it.
        var defect = StartBot("Defect", 1002, port);
        defect.Chat.BotName = null;
        await Until(() => Has(mark, $"Defect joined the chat (port {port})."), "the join line");
        defect.Chat.Say("Orbs up.");
        await Until(() => Has(mark, "Orbs up."), "the bot's line");
        Assert.Equal("Defect", mark.LinesSince(0).Single(l => l.Text == "Orbs up.").From);
        Assert.Equal([(1002UL, "Orbs up.")], coopBots.Shown);
    }

    [Fact]
    public async Task Party_lines_reach_the_other_bots_once_and_are_rate_limited()
    {
        var now = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc);
        var silent = StartBot("Silent", 1000);
        var defect = StartBot("Defect", 1001);
        var options = Options(BridgeTests.FreePort(), silent.Port, defect.Port) with { PartyLinesPerMinute = 2 };
        var (relay, mark, log) = StartRelay(options, clock: () => now);
        await Until(() => relay.Peers.Count(p => p.Live) == 2, "both bots to link");

        silent.Chat.Say("Defect, take the elite.");
        await Until(() => Has(defect.Chat, "Defect, take the elite."), "the party line at Defect");
        var heard = defect.Chat.LinesSince(0).Single(l => l.Text == "Defect, take the elite.");
        Assert.Equal(ChatLineKind.Bot, heard.Kind);
        Assert.True(heard.Remote);
        Assert.Equal("Silent", heard.From);
        Assert.False(defect.Chat.HasUnread); // never wakes the bot
        var message = Assert.Single(defect.Chat.TakeUnread());
        Assert.True(message.Teammate);

        defect.Chat.Say("On it.");
        await Until(() => Has(silent.Chat, "On it."), "the answer at Silent");
        await Settle();

        // Each line went once to the other bot and once to Mark, and never came back or bounced on.
        Assert.Equal(1, Count(silent.Chat, "Defect, take the elite."));
        Assert.False(silent.Chat.LinesSince(0).Single(l => l.Text == "Defect, take the elite.").Remote);
        Assert.Equal(1, Count(defect.Chat, "Defect, take the elite."));
        Assert.Equal(1, Count(defect.Chat, "On it."));
        Assert.Equal(1, Count(silent.Chat, "On it."));
        Assert.Equal(1, Count(mark, "Defect, take the elite."));
        Assert.Equal(1, Count(mark, "On it."));

        // Two lines a minute from Silent: the first was one, so only "one" of the next three passes.
        silent.Chat.Say("one");
        silent.Chat.Say("two");
        silent.Chat.Say("three");
        await Until(() => Has(mark, "three"), "Silent's lines at Mark");
        await Settle();
        Assert.True(Has(defect.Chat, "one"));
        Assert.False(Has(defect.Chat, "two"));
        Assert.False(Has(defect.Chat, "three"));
        Assert.True(Has(mark, "two"));
        Assert.Single(log, l => l.StartsWith("party chat: Silent said more than 2 lines"));

        now += TimeSpan.FromSeconds(61);
        silent.Chat.Say("four");
        await Until(() => Has(defect.Chat, "four"), "a line after the minute");
    }

    [Fact]
    public async Task Party_off_keeps_bots_from_hearing_each_other()
    {
        var silent = StartBot("Silent", 1000);
        var defect = StartBot("Defect", 1001);
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port, defect.Port));
        await Until(() => relay.Peers.Count(p => p.Live) == 2, "both bots to link");

        Assert.Equal("Party chat is on: the bots hear each other's lines.", mark.PostFromHuman("Mark", "/party").Reply);
        Assert.Equal("Party chat is off: the bots hear only you.", mark.PostFromHuman("Mark", "/party OFF").Reply);
        Assert.False(relay.PartyChat);
        Assert.Equal("Use /party on or /party off.", mark.PostFromHuman("Mark", "/party maybe").Reply);

        silent.Chat.Say("Just for Mark.");
        await Until(() => Has(mark, "Just for Mark."), "the line at Mark");
        await Settle();
        Assert.False(Has(defect.Chat, "Just for Mark."));

        mark.PostFromHuman("Mark", "/party on");
        Assert.True(relay.PartyChat);
        silent.Chat.Say("For everyone.");
        await Until(() => Has(defect.Chat, "For everyone."), "the party line");
    }

    [Fact]
    public async Task A_closed_copy_is_announced_once_and_a_restarted_one_is_read_from_its_start()
    {
        var port = BridgeTests.FreePort();
        var first = StartBot("Silent", 1000, port, keep: false);
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), port));
        await Until(() => relay.BotsExist, "the bot to link");
        Assert.NotNull(mark.HelpNote);

        first.Dispose();
        await Until(() => Has(mark, "Silent's copy stopped answering."), "the down line");
        Assert.False(relay.BotsExist);
        var peer = Assert.Single(relay.Peers);
        Assert.False(peer.Live);
        Assert.Null(mark.HelpNote);
        Assert.Equal("Ironclad paused. Type /resume to hand back control.", mark.PostFromHuman("Mark", "/pause").Reply);
        mark.PostFromHuman("Mark", "/resume");

        var second = StartBot("Silent", 1000, port);
        second.Chat.Say("Back again.");
        await Until(() => Has(mark, "Silent's copy restarted."), "the restart line");
        await Until(() => Has(mark, "Back again."), "the new copy's first line");
        await Settle();
        Assert.Equal(1, Count(mark, "Silent's copy stopped answering."));
        Assert.Equal(1, Count(mark, "Silent's copy restarted."));
        Assert.Equal(1, Count(mark, "Back again."));
        Assert.Equal(second.Server.Session, Assert.Single(relay.Peers).Session);
        Assert.True(relay.BotsExist);
    }

    [Fact]
    public async Task A_restart_between_polls_is_noticed_from_the_new_session()
    {
        var port = BridgeTests.FreePort();
        var first = StartBot("Silent", 1000, port, keep: false);
        first.Chat.Say("Line one.");
        first.Chat.Say("Line two.");
        var options = Options(BridgeTests.FreePort(), port) with { DownAfterFailures = 1000 };
        var (relay, mark, _) = StartRelay(options);
        await Until(() => relay.BotsExist, "the bot to link");

        first.Dispose();
        var second = StartBot("Silent", 1000, port);
        second.Chat.Say("Fresh start.");
        await Until(() => Has(mark, "Fresh start."), "the new copy's line");
        Assert.Equal(1, Count(mark, "Silent's copy restarted."));
        Assert.False(Has(mark, "Silent's copy stopped answering."));
        Assert.False(Has(mark, "Line one."));
    }

    [Fact]
    public async Task A_command_a_bot_never_answers_says_so()
    {
        var silent = StartBot("Silent", 1000);
        var handler = new FlakyHandler();
        _cleanup.Add(handler);
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port) with { Handler = handler });
        await Until(() => relay.BotsExist, "the bot to link");

        handler.FailPosts = true;
        mark.PostFromHuman("Mark", "/pause");
        await Until(() => Has(mark, "Silent didn't answer (its copy may have closed)."), "the failure line");
        Assert.False(silent.Chat.Paused);
        Assert.True(relay.BotsExist); // polls still work, so it is still linked

        handler.FailPosts = false;
        mark.PostFromHuman("Mark", "/pause");
        await Until(() => silent.Chat.Paused, "the second try");
    }

    [Fact]
    public async Task Bots_lists_live_and_starting_copies()
    {
        var silent = StartBot("Silent", 1000);
        silent.Info.Playing = () => "api";
        var defect = StartBot("Defect", 1001);
        defect.Info.Playing = () => "none";
        defect.Info.NetId = null;
        var startingPort = BridgeTests.FreePort();
        var coopBots = new FakeCoopBots();
        coopBots.SetKnown((startingPort, 1002UL, "Regent"));
        var (relay, mark, _) = StartRelay(Options(BridgeTests.FreePort(), silent.Port, defect.Port), coopBots);
        await Until(() => relay.Peers.Count(p => p.Live) == 2, "both bots to link");

        mark.PostFromHuman("Mark", "/pause Silent");
        await Until(() => relay.Peers.Single(p => p.Port == silent.Port).Paused, "Silent's paused flag");

        var expected = new[]
        {
            (silent.Port, $"Silent on {silent.Port} (NetId 1000, playing on API credit, paused)"),
            (defect.Port, $"Defect on {defect.Port} (not playing)"),
            (startingPort, $"Regent on {startingPort} (starting)"),
        };
        var linked = string.Join(", ", expected.Take(2).OrderBy(e => e.Item1).Select(e => e.Item2));
        Assert.Equal($"Bots: {linked}, {expected[2].Item2}.", mark.PostFromHuman("Mark", "/bots").Reply);
    }

    [Fact]
    public async Task Bot_add_validates_starts_one_at_a_time_and_reports_the_result()
    {
        var (_, plain, _) = StartRelay(Options(BridgeTests.FreePort()));
        Assert.Equal("No bot copies are running. /bot add [character] starts one.", plain.PostFromHuman("Mark", "/bots").Reply);
        Assert.Equal("This build can't start bot copies; start one with scripts/coop-bot.sh.", plain.PostFromHuman("Mark", "/bot add").Reply);
        const string usage = "Use /bot add [Ironclad|Silent|Defect|Regent|Necrobinder|Random].";
        Assert.Equal(usage, plain.PostFromHuman("Mark", "/bot add Watcher").Reply);
        Assert.Equal(usage, plain.PostFromHuman("Mark", "/bot remove").Reply);
        Assert.Equal(usage, plain.PostFromHuman("Mark", "/bot").Reply);

        var coopBots = new FakeCoopBots();
        var (_, mark, _) = StartRelay(Options(BridgeTests.FreePort()), coopBots);
        Assert.Equal("Starting a bot copy as Silent...", mark.PostFromHuman("Mark", "/bot add the silent").Reply);
        await Until(() => coopBots.Starts.Count == 1, "the start call");
        Assert.Equal("A bot copy is already starting.", mark.PostFromHuman("Mark", "/bot add Defect").Reply);
        Assert.Equal("A bot copy is starting.", mark.PostFromHuman("Mark", "/bots").Reply);

        var done = coopBots.NextStart;
        coopBots.NextStart = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult("Started a bot copy on port 17773.");
        await Until(() => Has(mark, "Started a bot copy on port 17773."), "the start result");

        await Until(() => mark.PostFromHuman("Mark", "/bot add RANDOM").Reply == "Starting a bot copy as Random...", "the next start");
        await Until(() => coopBots.Starts.Count == 2, "the second start call");
        coopBots.NextStart.SetException(new InvalidOperationException("No free port."));
        await Until(() => Has(mark, "Couldn't start a bot copy: No free port."), "the failure line");

        await Until(() => mark.PostFromHuman("Mark", "/bot add").Reply == "Starting a bot copy...", "a start with no character");
        await Until(() => coopBots.Starts.Count == 3, "the third start call");
        Assert.Equal(["Silent", "Random", null], coopBots.Starts);
    }

    [Fact]
    public async Task A_start_that_never_ends_gives_bot_add_back()
    {
        var coopBots = new FakeCoopBots();
        var (_, mark, log) = StartRelay(Options(BridgeTests.FreePort()) with { StartTimeout = TimeSpan.FromMilliseconds(200) }, coopBots);

        Assert.Equal("Starting a bot copy as Silent...", mark.PostFromHuman("Mark", "/bot add Silent").Reply);
        Assert.Equal("A bot copy is already starting.", mark.PostFromHuman("Mark", "/bot add Defect").Reply);
        await Until(() => Has(mark, "The bot copy didn't come up within 1 second. /bots shows what is running; /bot add tries again."),
            "the timeout line");
        Assert.Contains(log, l => l.Contains("didn't finish within"));

        await Until(() => mark.PostFromHuman("Mark", "/bot add Defect").Reply == "Starting a bot copy as Defect...", "the next start");
        await Until(() => coopBots.Starts.Count == 2, "the second start call");
        Assert.Equal(["Silent", "Defect"], coopBots.Starts);
    }

    [Fact]
    public void Bot_add_stops_at_the_most_bots()
    {
        // Ports nothing listens on, so the copies are all still starting.
        var coopBots = new FakeCoopBots();
        coopBots.SetKnown((BridgeTests.FreePort(), 1000UL, "Silent"), (BridgeTests.FreePort(), 1001UL, "Defect"),
            (BridgeTests.FreePort(), 1002UL, "Regent"));
        var (_, mark, _) = StartRelay(Options(BridgeTests.FreePort(), []), coopBots);
        Assert.Equal("Three bot copies is the most.", mark.PostFromHuman("Mark", "/bot add").Reply);
        Assert.Empty(coopBots.Starts);

        var one = new FakeCoopBots();
        one.SetKnown((BridgeTests.FreePort(), 1000UL, "Silent"));
        var (_, other, _) = StartRelay(Options(BridgeTests.FreePort(), []) with { MaxBots = 1 }, one);
        Assert.Equal("One bot copy is the most.", other.PostFromHuman("Mark", "/bot add").Reply);
    }

    [Fact]
    public async Task No_more_than_max_bots_are_linked()
    {
        var silent = StartBot("Silent", 1000);
        var defect = StartBot("Defect", 1001);
        var (relay, _, log) = StartRelay(Options(BridgeTests.FreePort(), silent.Port, defect.Port) with { MaxBots = 1 });
        await Until(() => relay.BotsExist, "a bot to link");
        await Settle();
        Assert.Single(relay.Peers);
        Assert.Single(log, l => l.Contains("1 are linked already"));
    }

    [Fact]
    public async Task Dispose_hands_the_chat_back()
    {
        var silent = StartBot("Silent", 1000);
        var mark = new ChatHub { BotName = "Ironclad" };
        Func<string, string, string, string?> other = (_, _, _) => null;
        var relay = new CoopRelay(mark, Options(BridgeTests.FreePort(), silent.Port));
        relay.Start();
        Assert.Throws<InvalidOperationException>(relay.Start);
        await Until(() => relay.BotsExist, "the bot to link");
        Assert.NotNull(mark.CommandFilter);

        relay.Dispose();
        Assert.False(relay.BotsExist);
        Assert.Null(mark.CommandFilter);
        Assert.Null(mark.HelpNote);
        Assert.StartsWith("There's no /bots command", mark.PostFromHuman("Mark", "/bots").Reply);
        Assert.Equal("Ironclad paused. Type /resume to hand back control.", mark.PostFromHuman("Mark", "/pause").Reply);
        mark.PostFromHuman("Mark", "after the relay");
        await Settle();
        Assert.False(Has(silent.Chat, "after the relay"));
        relay.Dispose();

        // A filter someone else set later is left alone.
        var again = new CoopRelay(mark, Options(BridgeTests.FreePort()));
        again.Start();
        mark.CommandFilter = other;
        again.Dispose();
        Assert.Same(other, mark.CommandFilter);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("  ", null, false)]
    [InlineData("17773", "17773", false)]
    [InlineData("17773,17774 17775", "17773,17774,17775", false)]
    [InlineData("17774, 17774;17773", "17774,17773", false)]
    [InlineData("17773,abc,0,70000", "17773", true)]
    [InlineData("abc", null, true)]
    public void Ports_parse_from_the_environment(string? text, string? expected, bool warns)
    {
        var ports = CoopRelayOptions.ParsePorts(text, out var warning);
        Assert.Equal(expected, ports is null ? null : string.Join(",", ports));
        Assert.Equal(warns, warning is not null);
        if (warns) Assert.Contains(CoopRelayOptions.PortsEnvVar, warning);
    }

    [Fact]
    public void Defaults_match_the_bot_ports()
    {
        var options = new CoopRelayOptions { OwnPort = 17772 };
        Assert.Equal([17773, 17774, 17775], options.ProbePorts);
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollInterval);
        Assert.Equal(3, options.MaxBots);
        Assert.True(options.PartyChat);
        Assert.Equal("SPIREMIND_BOT_PORTS", CoopRelayOptions.PortsEnvVar);
        Assert.Equal(["start", "stop", "pause", "resume", "budget", "model", "effort"], CoopRelay.ForwardedCommands);
    }
}
