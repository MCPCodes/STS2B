using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using Xunit;

namespace SpireMind.Core.Tests;

public class ChatHubTests
{
    [Theory]
    [InlineData("Ironclad, go for Strength", PostOutcome.Queued)]
    [InlineData("The Ironclad: block this turn", PostOutcome.Queued)]
    [InlineData("hey ironclad what's next", PostOutcome.Queued)]
    [InlineData("@Ironclad nice", PostOutcome.Queued)]
    [InlineData("ok, IRONCLAD!", PostOutcome.Queued)]
    [InlineData("Ironclad?", PostOutcome.Queued)]
    [InlineData("nice block", PostOutcome.Queued)]
    [InlineData("Silently judging that pick", PostOutcome.Queued)]
    [InlineData("Silent, take the shiv", PostOutcome.NotForMe)]
    [InlineData("hey Defect: orbs?", PostOutcome.NotForMe)]
    [InlineData("@necrobinder hi", PostOutcome.NotForMe)]
    [InlineData("the regent", PostOutcome.NotForMe)]
    [InlineData("   ", PostOutcome.Empty)]
    public void Addressing(string text, PostOutcome expected)
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        Assert.Equal(expected, hub.PostFromHuman("Mark", text).Outcome);
        Assert.Equal(expected == PostOutcome.Queued, hub.HasUnread);
    }

    [Fact]
    public void Message_for_another_bot_is_logged_but_not_queued()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        hub.PostFromHuman("Mark", "Silent, take the shiv");
        var line = Assert.Single(hub.LinesSince(0));
        Assert.Equal(ChatLineKind.Human, line.Kind);
        Assert.Equal("Mark", line.From);
        Assert.Equal("Silent, take the shiv", line.Text);
        Assert.Empty(hub.TakeUnread());
    }

    [Fact]
    public void Without_a_bot_name_every_message_is_for_this_bot()
    {
        // Between runs the last character is forgotten, so an order to the next one gets through.
        var hub = new ChatHub { BotName = "Ironclad" };
        hub.BotName = null;
        Assert.Equal(PostOutcome.Queued, hub.PostFromHuman("Mark", "what's the plan?").Outcome);
        Assert.Equal(PostOutcome.Queued, hub.PostFromHuman("Mark", "Ironclad, hi").Outcome);
        Assert.Equal(PostOutcome.Queued, hub.PostFromHuman("Mark", "Silent, go poison this run").Outcome);
        Assert.Equal(3, hub.TakeUnread().Count);
    }

    [Fact]
    public void Peek_unread_leaves_the_messages_unread()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        Assert.Empty(hub.PeekUnread());
        hub.PostFromHuman("Mark", "Ironclad, take the elite");
        Assert.Equal("Ironclad, take the elite", Assert.Single(hub.PeekUnread()).Text);
        Assert.True(hub.HasUnread);
        Assert.Single(hub.TakeUnread());
        Assert.Empty(hub.PeekUnread());
    }

    [Theory]
    [InlineData("The Ironclad", "Ironclad")]
    [InlineData("  the Silent ", "Silent")]
    [InlineData("Defect", "Defect")]
    [InlineData("  ", null)]
    [InlineData(null, null)]
    public void Bot_name_drops_the_leading_the(string? given, string? expected)
    {
        var hub = new ChatHub { BotName = given };
        Assert.Equal(expected, hub.BotName);
    }

    [Fact]
    public void Pause_and_resume_answer_with_system_lines()
    {
        var hub = new ChatHub { BotName = "Ironclad" };

        var paused = hub.PostFromHuman("Mark", "  /PAUSE ");
        Assert.Equal(PostOutcome.Command, paused.Outcome);
        Assert.Equal("Ironclad paused. Type /resume to hand back control.", paused.Reply);
        Assert.True(hub.Paused);
        Assert.Equal("Mark", hub.PausedBy);

        var again = hub.PostFromHuman("Anna", "/pause");
        Assert.Equal(PostOutcome.Command, again.Outcome);
        Assert.Contains("already paused", again.Reply);
        Assert.Equal("Mark", hub.PausedBy);

        var resumed = hub.PostFromHuman("Mark", "/Resume");
        Assert.Equal(PostOutcome.Command, resumed.Outcome);
        Assert.False(hub.Paused);
        Assert.Null(hub.PausedBy);

        var notPaused = hub.PostFromHuman("Mark", "/resume");
        Assert.Equal("Ironclad isn't paused.", notPaused.Reply);

        var lines = hub.LinesSince(0);
        Assert.Equal(4, lines.Count);
        Assert.All(lines, l =>
        {
            Assert.Equal(ChatLineKind.System, l.Kind);
            Assert.Equal("", l.From);
        });
        Assert.Equal([paused.Reply, again.Reply, resumed.Reply, notPaused.Reply], lines.Select(l => l.Text));
        Assert.False(hub.HasUnread);
    }

    [Fact]
    public void Commands_must_be_the_whole_message()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        Assert.Equal(PostOutcome.Queued, hub.PostFromHuman("Mark", "/pause now").Outcome);
        Assert.False(hub.Paused);
    }

    [Fact]
    public void System_lines_say_the_bot_when_the_name_is_unknown()
    {
        var hub = new ChatHub();
        Assert.Equal("The bot paused. Type /resume to hand back control.", hub.PostFromHuman("Mark", "/pause").Reply);
    }

    [Fact]
    public void Help_lists_the_commands()
    {
        var hub = new ChatHub();
        var help = hub.PostFromHuman("Mark", "/help");
        Assert.Equal(PostOutcome.Command, help.Outcome);
        Assert.Contains("/pause", help.Reply);
        Assert.Contains("/resume", help.Reply);
        Assert.Contains("/help", help.Reply);
        Assert.Equal(ChatLineKind.System, Assert.Single(hub.LinesSince(0)).Kind);
    }

    [Fact]
    public void Say_collapses_whitespace_and_logs_under_the_bot_name()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        var line = hub.Say("  Aye.\n\n  Inflame   it is.  ");
        Assert.Equal("Aye. Inflame it is.", line.Text);
        Assert.Equal("Ironclad", line.From);
        Assert.Equal(ChatLineKind.Bot, line.Kind);
        Assert.Equal(line, Assert.Single(hub.LinesSince(0)));

        Assert.Equal("Bot", new ChatHub().Say("hi").From);
    }

    [Fact]
    public void Say_caps_long_text_with_an_ellipsis()
    {
        var hub = new ChatHub { MaxSayLength = 20 };
        var line = hub.Say(new string('a', 50));
        Assert.True(line.Text.Length <= 20);
        Assert.EndsWith("...", line.Text);
        Assert.Equal("exactly twenty chars", hub.Say("exactly twenty chars").Text);
    }

    [Fact]
    public void Say_refuses_empty_text()
    {
        var hub = new ChatHub();
        Assert.Throws<ArgumentException>(() => hub.Say(" \n\t "));
        Assert.Empty(hub.LinesSince(0));
    }

    [Fact]
    public void Say_burst_limit_resets_after_ten_seconds()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var hub = new ChatHub(() => now);
        for (var i = 0; i < 6; i++)
            hub.Say($"line {i}");

        var ex = Assert.Throws<InvalidOperationException>(() => hub.Say("one more"));
        Assert.Contains("Slow down", ex.Message);
        Assert.Equal(6, hub.LinesSince(0).Count);

        now = now.AddSeconds(10);
        hub.Say("better now");
        Assert.Equal(7, hub.LinesSince(0).Count);
    }

    [Fact]
    public void Take_unread_hands_over_once_in_order()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var hub = new ChatHub(() => now) { BotName = "Ironclad" };
        hub.PostFromHuman("Mark", "first");
        hub.PostFromHuman(" ", "second");

        var taken = hub.TakeUnread();
        Assert.Equal([new ChatMessage("Mark", "first", now), new ChatMessage("Player", "second", now)], taken);
        Assert.False(hub.HasUnread);
        Assert.Empty(hub.TakeUnread());
    }

    [Fact]
    public void Lines_since_returns_newer_lines_and_keeps_the_last_200()
    {
        var hub = new ChatHub();
        hub.PostFromHuman("Mark", "a");
        hub.PostFromHuman("Mark", "b");
        hub.PostFromHuman("Mark", "c");
        Assert.Equal(3, hub.LastSeq);
        Assert.Equal(["b", "c"], hub.LinesSince(1).Select(l => l.Text));
        Assert.Empty(hub.LinesSince(3));

        for (var i = 0; i < 247; i++)
            hub.PostFromHuman("Mark", $"spam {i}");
        var all = hub.LinesSince(0);
        Assert.Equal(200, all.Count);
        Assert.Equal(51, all[0].Seq);
        Assert.Equal(250, all[^1].Seq);
    }

    [Fact]
    public void Line_added_is_raised_outside_the_lock()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        var seen = new List<ChatLine>();
        var otherThreadGotIn = true;
        hub.LineAdded += line =>
        {
            seen.Add(line);
            // Monitor is reentrant, so calling back on this thread proves nothing. Another
            // thread can only read the hub now if the lock was released before raising.
            otherThreadGotIn &= Task.Run(() => hub.LastSeq + hub.LinesSince(0).Count).Wait(TimeSpan.FromSeconds(2));
        };

        hub.PostFromHuman("Mark", "Ironclad, hi");
        hub.PostFromHuman("Mark", "/pause");
        hub.Say("Aye.");

        Assert.True(otherThreadGotIn);
        Assert.Equal([1L, 2L, 3L], seen.Select(l => l.Seq));
        Assert.Equal([ChatLineKind.Human, ChatLineKind.System, ChatLineKind.Bot], seen.Select(l => l.Kind));
    }

    [Fact]
    public void Persona_is_replaced_whole_and_handed_out_as_copies()
    {
        var hub = new ChatHub();
        Assert.Null(hub.Persona);

        var given = new JsonObject { ["name"] = "Ironclad", ["voice_lines"] = new JsonArray("Hm.") };
        hub.Persona = given;
        given["name"] = "Silent";
        given["voice_lines"]!.AsArray().Add("...");

        var first = hub.Persona!;
        Assert.Equal("Ironclad", first["name"]!.GetValue<string>());
        Assert.Single(first["voice_lines"]!.AsArray());
        first["name"] = "Defect";
        first["voice_lines"]!.AsArray().Clear();

        var second = hub.Persona!;
        Assert.NotSame(first, second);
        Assert.Equal("Ironclad", second["name"]!.GetValue<string>());
        Assert.Equal("Hm.", second["voice_lines"]![0]!.GetValue<string>());

        // A persona that already sits inside another document is copied, not re-parented.
        var holder = new JsonObject { ["persona"] = new JsonObject { ["name"] = "Regent" } };
        hub.Persona = holder["persona"]!.AsObject();
        Assert.Equal("Regent", hub.Persona!["name"]!.GetValue<string>());

        hub.Persona = null;
        Assert.Null(hub.Persona);
    }

    [Fact]
    public void Thinking_since_keeps_the_first_time_until_cleared()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var hub = new ChatHub(() => now);
        Assert.Null(hub.ThinkingSince);

        hub.MarkThinking(true);
        var started = now;
        now = now.AddSeconds(5);
        hub.MarkThinking(true);
        Assert.Equal(started, hub.ThinkingSince);

        hub.MarkThinking(false);
        Assert.Null(hub.ThinkingSince);
        hub.MarkThinking(false);
        Assert.Null(hub.ThinkingSince);

        hub.MarkThinking(true);
        Assert.Equal(now, hub.ThinkingSince);
    }

    [Fact]
    public void A_stale_thinking_mark_reads_as_null_and_restarts()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var hub = new ChatHub(() => now) { ThinkingStaleAfter = TimeSpan.FromMinutes(5) };

        // The model was handed a decision and then went away: no tool call clears the mark.
        hub.MarkThinking(true);
        var started = now;
        now = now.AddMinutes(4);
        Assert.Equal(started, hub.ThinkingSince);
        now = now.AddMinutes(1);
        Assert.Null(hub.ThinkingSince);

        // A model that comes back starts a new clock instead of keeping the stale one.
        hub.MarkThinking(true);
        Assert.Equal(now, hub.ThinkingSince);
    }

    [Fact]
    public void Pause_clears_thinking()
    {
        var hub = new ChatHub { BotName = "Ironclad" };
        hub.MarkThinking(true);
        hub.PostFromHuman("Mark", "Ironclad, hold on");
        Assert.NotNull(hub.ThinkingSince);

        hub.PostFromHuman("Mark", "/pause");
        Assert.Null(hub.ThinkingSince);
    }
}

public class ChatToolTests
{
    private static (BotTools Tools, ChatHub Chat) Build(IGameBridge? game = null)
    {
        game ??= new FakeGame { ActionDelay = TimeSpan.FromMilliseconds(50) };
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner)
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            SettleTime = TimeSpan.FromMilliseconds(30),
        };
        var chat = new ChatHub { BotName = "Ironclad" };
        return (new BotTools(game, waiter, versioner, chat) { ActSettleTimeout = TimeSpan.FromSeconds(3) }, chat);
    }

    private static ToolOutcome Call(BotTools tools, string name, JsonObject? args = null) =>
        tools.CallAsync(name, args ?? new JsonObject(), CancellationToken.None).GetAwaiter().GetResult();

    private sealed class BusyGame : IGameBridge
    {
        public Task<GameSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(GameSnapshot.Busy("combat", "enemy turn"));
        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct) => Task.FromResult(ActionResult.Fail("busy"));
        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => Task.FromResult(new JsonObject());
    }

    /// <summary>The fake fight, telling the test when an action reaches the game.</summary>
    private sealed class WatchedGame : IGameBridge
    {
        private readonly FakeGame _game = new() { ActionDelay = TimeSpan.FromMilliseconds(50) };
        private readonly Action _onExecute;

        public WatchedGame(Action onExecute) => _onExecute = onExecute;

        public Task<GameSnapshot> CaptureAsync(CancellationToken ct) => _game.CaptureAsync(ct);
        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => _game.DescribeDeckAsync(ct);

        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
        {
            _onExecute();
            return _game.ExecuteAsync(actionId, ct);
        }
    }

    [Fact]
    public void No_chat_key_without_messages_or_pause()
    {
        var (tools, chat) = Build();
        Assert.False(Call(tools, "get_state").Payload.AsObject().ContainsKey("chat"));

        chat.PostFromHuman("Mark", "Silent, this one's yours");
        Assert.False(Call(tools, "get_state").Payload.AsObject().ContainsKey("chat"));
    }

    [Fact]
    public void Messages_ride_along_once()
    {
        var (tools, chat) = Build();
        chat.PostFromHuman("Mark", "Ironclad, go for Strength");

        var first = Call(tools, "get_state").Payload;
        Assert.Equal("Ironclad", first["chat"]!["you_are"]!.GetValue<string>());
        Assert.False(first["chat"]!.AsObject().ContainsKey("paused"));
        var message = Assert.Single(first["chat"]!["messages"]!.AsArray())!;
        Assert.Equal("Mark", message["from"]!.GetValue<string>());
        Assert.Equal("Ironclad, go for Strength", message["text"]!.GetValue<string>());
        Assert.InRange(message["seconds_ago"]!.GetValue<int>(), 0, 1);

        Assert.False(Call(tools, "get_state").Payload.AsObject().ContainsKey("chat"));
    }

    [Fact]
    public void Chat_never_changes_the_version()
    {
        var (tools, chat) = Build();
        var a = Call(tools, "get_state").Payload["version"]!.GetValue<long>();
        chat.PostFromHuman("Mark", "hello");
        var b = Call(tools, "get_state").Payload["version"]!.GetValue<long>();
        chat.PostFromHuman("Mark", "/pause");
        var c = Call(tools, "get_state").Payload["version"]!.GetValue<long>();
        chat.PostFromHuman("Mark", "/resume");
        var d = Call(tools, "get_state").Payload["version"]!.GetValue<long>();
        Assert.Equal([a, a, a], new[] { b, c, d });
    }

    [Fact]
    public void Paused_state_shows_the_board_but_no_decision()
    {
        var (tools, chat) = Build();
        chat.PostFromHuman("Mark", "/pause");

        var snap = Call(tools, "get_state").Payload;
        Assert.False(snap["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal("paused by Mark", snap["waiting"]!.GetValue<string>());
        Assert.Empty(snap["actions"]!.AsArray());
        Assert.Equal(40, snap["state"]!["enemies"]![0]!["hp"]!.GetValue<int>());
        Assert.True(snap["chat"]!["paused"]!.GetValue<bool>());
        Assert.False(snap["chat"]!.AsObject().ContainsKey("messages"));
    }

    [Fact]
    public void Act_is_refused_while_paused()
    {
        var (tools, chat) = Build();
        var version = Call(tools, "get_state").Payload["version"]!.GetValue<long>();
        chat.PostFromHuman("Mark", "/pause");

        var outcome = Call(tools, "act", new JsonObject { ["action_id"] = "play:0>e0", ["version"] = version });
        Assert.True(outcome.IsError);
        Assert.Equal("Paused by Mark. Call wait_for_decision; you can still talk with say.", outcome.Payload["error"]!.GetValue<string>());
        Assert.Empty(outcome.Payload["current"]!["actions"]!.AsArray());
        Assert.True(outcome.Payload["chat"]!["paused"]!.GetValue<bool>());

        chat.PostFromHuman("Mark", "/resume");
        Assert.Equal(40, Call(tools, "get_state").Payload["state"]!["enemies"]![0]!["hp"]!.GetValue<int>());
    }

    [Fact]
    public void Act_replies_and_rejections_carry_messages()
    {
        var (tools, chat) = Build();
        var version = Call(tools, "get_state").Payload["version"]!.GetValue<long>();

        chat.PostFromHuman("Mark", "Bash first");
        var acted = Call(tools, "act", new JsonObject { ["action_id"] = "play:3>e0", ["version"] = version });
        Assert.False(acted.IsError);
        Assert.Equal("Bash first", acted.Payload["chat"]!["messages"]![0]!["text"]!.GetValue<string>());

        chat.PostFromHuman("Mark", "not that one");
        var refused = Call(tools, "act", new JsonObject { ["action_id"] = "play:9>e0" });
        Assert.True(refused.IsError);
        Assert.Equal("not that one", refused.Payload["chat"]!["messages"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Wait_returns_early_when_a_message_arrives()
    {
        var (tools, chat) = Build(new BusyGame());
        var clock = Stopwatch.StartNew();
        var wait = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 10 }, CancellationToken.None);
        await Task.Delay(100);
        chat.PostFromHuman("Mark", "Ironclad, you there?");

        var outcome = await wait;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
        Assert.False(outcome.Payload["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal("Ironclad, you there?", outcome.Payload["chat"]!["messages"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Paused_wait_holds_until_the_timeout()
    {
        var (tools, chat) = Build();
        chat.PostFromHuman("Mark", "/pause");

        var clock = Stopwatch.StartNew();
        var outcome = await tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 1 }, CancellationToken.None);
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(900), $"took {clock.Elapsed}");
        Assert.False(outcome.Payload["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal("paused by Mark", outcome.Payload["waiting"]!.GetValue<string>());
        Assert.Empty(outcome.Payload["actions"]!.AsArray());
    }

    [Fact]
    public async Task Paused_wait_still_returns_early_for_a_message()
    {
        var (tools, chat) = Build();
        chat.PostFromHuman("Mark", "/pause");

        var clock = Stopwatch.StartNew();
        var wait = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 10 }, CancellationToken.None);
        await Task.Delay(100);
        chat.PostFromHuman("Mark", "watch this");

        var outcome = await wait;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
        Assert.Equal("paused by Mark", outcome.Payload["waiting"]!.GetValue<string>());
        Assert.True(outcome.Payload["chat"]!["paused"]!.GetValue<bool>());
        Assert.Equal("watch this", outcome.Payload["chat"]!["messages"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task Resume_during_a_wait_hands_over_the_decision()
    {
        var (tools, chat) = Build();
        chat.PostFromHuman("Mark", "/pause");

        var clock = Stopwatch.StartNew();
        var wait = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 10 }, CancellationToken.None);
        await Task.Delay(100);
        chat.PostFromHuman("Mark", "/resume");

        var outcome = await wait;
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"took {clock.Elapsed}");
        Assert.True(outcome.Payload["awaiting_decision"]!.GetValue<bool>());
        Assert.NotEmpty(outcome.Payload["actions"]!.AsArray());
        Assert.False(outcome.Payload.AsObject().ContainsKey("chat"));
    }

    [Fact]
    public async Task A_newer_wait_cancels_the_old_one_and_gets_the_message()
    {
        var (tools, chat) = Build();
        chat.PostFromHuman("Mark", "/pause");

        // The first wait's client went away without saying so; the model then waits again.
        var orphan = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 10 }, CancellationToken.None);
        await Task.Delay(50);
        var live = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 10 }, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orphan);

        chat.PostFromHuman("Mark", "Ironclad, take the elite");
        var outcome = await live;
        Assert.Equal("Ironclad, take the elite", outcome.Payload["chat"]!["messages"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_cancelled_mcp_call_leaves_messages_unread()
    {
        var (tools, chat) = Build();
        var mcp = new McpEndpoint(tools, "test");
        chat.PostFromHuman("Mark", "/pause");

        var call = mcp.HandleAsync(
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"wait_for_decision","arguments":{"timeout_seconds":10}}}""",
            CancellationToken.None);
        await Task.Delay(50);
        var ack = await mcp.HandleAsync(
            """{"jsonrpc":"2.0","method":"notifications/cancelled","params":{"requestId":7,"reason":"user pressed Esc"}}""",
            CancellationToken.None);
        Assert.Null(ack);

        var reply = (await call.WaitAsync(TimeSpan.FromSeconds(3)))!;
        Assert.Equal(7, reply["id"]!.GetValue<int>());
        Assert.Equal(-32800, reply["error"]!["code"]!.GetValue<int>());

        chat.PostFromHuman("Mark", "Ironclad, skip the shop");
        await Task.Delay(100);
        Assert.Equal("Ironclad, skip the shop", Assert.Single(chat.TakeUnread()).Text);
    }

    [Fact]
    public void Say_tool_speaks_and_reports_what_was_shown()
    {
        var (tools, chat) = Build();
        var outcome = Call(tools, "say", new JsonObject { ["text"] = "  Aye.   Inflame it is. " });
        Assert.False(outcome.IsError);
        Assert.Equal("Aye. Inflame it is.", outcome.Payload["said"]!.GetValue<string>());
        var line = Assert.Single(chat.LinesSince(0));
        Assert.Equal("Ironclad", line.From);
        Assert.Equal(ChatLineKind.Bot, line.Kind);

        chat.PostFromHuman("Mark", "good call");
        var withChat = Call(tools, "say", new JsonObject { ["text"] = "Thanks." });
        Assert.Equal("good call", withChat.Payload["chat"]!["messages"]![0]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void Say_tool_errors_carry_the_hub_message()
    {
        var (tools, _) = Build();
        var empty = Call(tools, "say", new JsonObject { ["text"] = "   " });
        Assert.True(empty.IsError);
        Assert.StartsWith("Nothing to say", empty.Payload["error"]!.GetValue<string>());

        Assert.True(Call(tools, "say").IsError);

        for (var i = 0; i < 6; i++)
            Assert.False(Call(tools, "say", new JsonObject { ["text"] = $"line {i}" }).IsError);
        var burst = Call(tools, "say", new JsonObject { ["text"] = "too much" });
        Assert.True(burst.IsError);
        Assert.Contains("Slow down", burst.Payload["error"]!.GetValue<string>());
    }

    [Fact]
    public void Get_deck_returns_the_deck_with_the_envelope()
    {
        var (tools, chat) = Build();
        var deck = Call(tools, "get_deck");
        Assert.False(deck.IsError);
        var bash = deck.Payload["deck"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == "Bash")!;
        Assert.Contains("Vulnerable", bash["text"]!.GetValue<string>());
        Assert.Equal("Burning Blood", deck.Payload["relics"]![0]!["name"]!.GetValue<string>());
        Assert.False(deck.Payload.AsObject().ContainsKey("chat"));

        chat.PostFromHuman("Mark", "/pause");
        Assert.True(Call(tools, "get_deck").Payload["chat"]!["paused"]!.GetValue<bool>());
    }

    [Fact]
    public void Get_persona_returns_a_copy_of_the_persona_with_the_envelope()
    {
        var (tools, chat) = Build();
        var none = Call(tools, "get_persona");
        Assert.True(none.IsError);
        Assert.Equal("no character yet; call get_persona again once the run has started", none.Payload["error"]!.GetValue<string>());

        chat.Persona = new JsonObject
        {
            ["name"] = "Ironclad",
            ["title"] = "The Ironclad",
            ["id"] = "IRONCLAD",
            ["tone"] = "A terse, grim soldier.",
            ["voice_lines"] = new JsonArray("Hm."),
        };
        var persona = Call(tools, "get_persona");
        Assert.False(persona.IsError);
        Assert.Equal("Ironclad", persona.Payload["name"]!.GetValue<string>());
        Assert.Equal("IRONCLAD", persona.Payload["id"]!.GetValue<string>());
        Assert.Equal("Hm.", persona.Payload["voice_lines"]![0]!.GetValue<string>());
        Assert.False(persona.Payload.AsObject().ContainsKey("chat"));

        // The chat key goes on the copy, never on the hub's persona.
        chat.PostFromHuman("Mark", "Ironclad, who are you?");
        var withChat = Call(tools, "get_persona");
        Assert.Equal("Ironclad, who are you?", withChat.Payload["chat"]!["messages"]![0]!["text"]!.GetValue<string>());
        Assert.False(chat.Persona!.ContainsKey("chat"));
    }

    [Fact]
    public void Deciding_keeps_the_first_thinking_time()
    {
        var (tools, chat) = Build();
        Assert.Null(chat.ThinkingSince);

        Call(tools, "get_state");
        var since = chat.ThinkingSince;
        Assert.NotNull(since);

        // Looking again, talking and reading the deck or persona are all part of deciding.
        Call(tools, "get_state");
        Call(tools, "say", new JsonObject { ["text"] = "Hm." });
        Call(tools, "get_deck");
        Call(tools, "get_persona");
        Call(tools, "wait_for_decision", new JsonObject { ["timeout_seconds"] = 1 });
        Assert.Equal(since, chat.ThinkingSince);
    }

    [Fact]
    public void Act_clears_thinking_and_its_next_decision_marks_it_again()
    {
        ChatHub? hub = null;
        var duringAct = new List<DateTime?>();
        var (tools, chat) = Build(new WatchedGame(() => duringAct.Add(hub!.ThinkingSince)));
        hub = chat;

        var version = Call(tools, "get_state").Payload["version"]!.GetValue<long>();
        var first = chat.ThinkingSince;
        Assert.NotNull(first);

        var acted = Call(tools, "act", new JsonObject { ["action_id"] = "play:0>e0", ["version"] = version });
        Assert.False(acted.IsError);
        Assert.True(acted.Payload["next"]!["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal([null], duringAct);
        Assert.True(chat.ThinkingSince > first, "the next decision starts a new clock");

        // A refusal hands back the current decision, so the bot is deciding again.
        var refused = Call(tools, "act", new JsonObject { ["action_id"] = "play:9>e0" });
        Assert.True(refused.IsError);
        Assert.NotNull(chat.ThinkingSince);
        Assert.Single(duringAct);
    }

    [Fact]
    public async Task A_result_without_a_decision_clears_thinking()
    {
        var (tools, chat) = Build(new BusyGame());
        chat.MarkThinking(true);
        Call(tools, "get_state");
        Assert.Null(chat.ThinkingSince);

        chat.MarkThinking(true);
        var wait = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 10 }, CancellationToken.None);
        await Task.Delay(50);
        chat.PostFromHuman("Mark", "Ironclad, you there?");
        Assert.False((await wait).Payload["awaiting_decision"]!.GetValue<bool>());
        Assert.Null(chat.ThinkingSince);

        chat.MarkThinking(true);
        Assert.True(Call(tools, "act", new JsonObject { ["action_id"] = "end_turn" }).IsError);
        Assert.Null(chat.ThinkingSince);
    }

    [Fact]
    public void Paused_results_never_mark_thinking()
    {
        var (tools, chat) = Build();
        chat.PostFromHuman("Mark", "/pause");

        Call(tools, "get_state");
        Assert.Null(chat.ThinkingSince);
        Call(tools, "wait_for_decision", new JsonObject { ["timeout_seconds"] = 1 });
        Assert.Null(chat.ThinkingSince);
        Assert.True(Call(tools, "act", new JsonObject { ["action_id"] = "end_turn" }).IsError);
        Assert.Null(chat.ThinkingSince);

        chat.PostFromHuman("Mark", "/resume");
        Call(tools, "get_state");
        Assert.NotNull(chat.ThinkingSince);
    }
}

public class ChatHttpTests
{
    [Fact]
    public async Task Chat_routes_post_and_read_the_log()
    {
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        var chat = new ChatHub { BotName = "Ironclad" };
        var tools = new BotTools(game, new DecisionWaiter(game, versioner), versioner, chat);
        var port = BridgeTests.FreePort();
        using var server = new BridgeServer(port, tools, chat, "test", _ => { });
        server.Start();

        using var http = new HttpClient();
        async Task<(HttpStatusCode Status, JsonNode Body)> Post(string json)
        {
            var res = await http.PostAsync($"http://127.0.0.1:{port}/chat", new StringContent(json, Encoding.UTF8, "application/json"));
            return (res.StatusCode, JsonNode.Parse(await res.Content.ReadAsStringAsync())!);
        }

        var queued = await Post("""{"from":"Mark","text":"Ironclad, go for Strength"}""");
        Assert.Equal(HttpStatusCode.OK, queued.Status);
        Assert.Equal("queued", queued.Body["outcome"]!.GetValue<string>());
        Assert.Null(queued.Body["reply"]);

        var elsewhere = await Post("""{"text":"Silent, your turn"}""");
        Assert.Equal("not_for_me", elsewhere.Body["outcome"]!.GetValue<string>());

        var paused = await Post("""{"from":"Mark","text":"/pause"}""");
        Assert.Equal("command", paused.Body["outcome"]!.GetValue<string>());
        Assert.Equal("Ironclad paused. Type /resume to hand back control.", paused.Body["reply"]!.GetValue<string>());

        var empty = await Post("""{"from":"Mark","text":"  "}""");
        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        Assert.Equal("empty", empty.Body["outcome"]!.GetValue<string>());

        var log = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/chat?since=0"))!;
        Assert.Equal("Ironclad", log["bot"]!.GetValue<string>());
        Assert.True(log["paused"]!.GetValue<bool>());
        var lines = log["lines"]!.AsArray();
        Assert.Equal(3, lines.Count);
        Assert.Equal(1, lines[0]!["seq"]!.GetValue<long>());
        Assert.Equal("human", lines[0]!["kind"]!.GetValue<string>());
        Assert.Equal("Mark", lines[0]!["from"]!.GetValue<string>());
        Assert.Equal("Player", lines[1]!["from"]!.GetValue<string>());
        Assert.Equal("system", lines[2]!["kind"]!.GetValue<string>());
        Assert.Equal("", lines[2]!["from"]!.GetValue<string>());

        var later = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/chat?since=2"))!;
        Assert.Single(later["lines"]!.AsArray());
    }

    [Fact]
    public async Task Bad_bodies_get_400_and_state_only_peeks_at_messages()
    {
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        var chat = new ChatHub { BotName = "Ironclad" };
        var tools = new BotTools(game, new DecisionWaiter(game, versioner), versioner, chat);
        var port = BridgeTests.FreePort();
        var logged = new List<string>();
        using var server = new BridgeServer(port, tools, chat, "test", logged.Add);
        server.Start();
        logged.Clear();

        using var http = new HttpClient();
        foreach (var (route, body) in new[] { ("chat", ""), ("chat", "Ironclad hi"), ("act", "{") })
        {
            var res = await http.PostAsync($"http://127.0.0.1:{port}/{route}", new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
            Assert.StartsWith("body must be a JSON object", JsonNode.Parse(await res.Content.ReadAsStringAsync())!["error"]!.GetValue<string>());
        }
        Assert.Empty(logged);

        chat.PostFromHuman("Mark", "Ironclad, take the elite");
        for (var i = 0; i < 2; i++)
        {
            var state = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/state"))!;
            Assert.Equal("Ironclad, take the elite", state["chat"]!["messages"]![0]!["text"]!.GetValue<string>());
            Assert.True(state["awaiting_decision"]!.GetValue<bool>());
        }
        Assert.True(chat.HasUnread);
        // Nothing was handed to the model, so no thought cloud.
        Assert.Null(chat.ThinkingSince);
    }
}
