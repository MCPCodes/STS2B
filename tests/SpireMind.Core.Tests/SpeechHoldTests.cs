using System.Text.Json.Nodes;
using SpireMind.Core;
using Xunit;

namespace SpireMind.Core.Tests;

public class SpeechHoldTests
{
    private static readonly DateTime Start = new(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>A room off combat: a list of choices ("map:0" labelled "Go to the elite"), each
    /// gone once taken, plus proceed. Every id that reaches the game goes into the shared log.</summary>
    private sealed class Room(string screen, string prefix, List<string> log, params string[] choices) : IGameBridge
    {
        public List<string> Choices { get; } = [.. choices];

        public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
        {
            var actions = Choices.Select((c, i) => new LegalAction($"{prefix}:{i}", c)).ToList();
            actions.Add(new LegalAction("proceed", "Proceed"));
            return Task.FromResult(new GameSnapshot
            {
                Screen = screen,
                AwaitingDecision = true,
                State = new JsonObject { ["choices"] = new JsonArray(Choices.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()) },
                Actions = actions,
            });
        }

        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
        {
            log.Add($"run {actionId}");
            if (actionId.StartsWith(prefix + ":"))
                Choices.RemoveAt(int.Parse(actionId[(prefix.Length + 1)..]));
            return Task.FromResult(ActionResult.Success);
        }

        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => Task.FromResult(new JsonObject());
    }

    /// <summary>The tools on <paramref name="game"/> with a hub on a clock the test moves. The
    /// delay logs "wait" and runs <paramref name="duringWait"/>; it doesn't move the clock.</summary>
    private sealed class Rig
    {
        public DateTime Now = Start;
        public List<string> Log { get; } = [];
        public List<TimeSpan> Waits { get; } = [];
        public ChatHub Chat { get; }
        public BotTools Tools { get; }

        public Rig(Func<List<string>, IGameBridge> game, Action? duringWait = null)
        {
            var g = game(Log);
            var versioner = new SnapshotVersioner();
            var waiter = new DecisionWaiter(g, versioner)
            {
                PollInterval = TimeSpan.FromMilliseconds(2),
                SettleTime = TimeSpan.FromMilliseconds(10),
            };
            Chat = new ChatHub(() => Now) { BotName = "Ironclad" };
            Tools = new BotTools(g, waiter, versioner, Chat)
            {
                ActSettleTimeout = TimeSpan.FromSeconds(1),
                Delay = (wait, ct) =>
                {
                    Waits.Add(wait);
                    Log.Add("wait");
                    duringWait?.Invoke();
                    return Task.CompletedTask;
                },
            };
        }

        public ToolOutcome Call(string name, JsonObject? args = null) =>
            Tools.CallAsync(name, args ?? new JsonObject(), CancellationToken.None).GetAwaiter().GetResult();

        public long Version() => Call("get_state").Payload["version"]!.GetValue<long>();

        public ToolOutcome Act(string id) => Call("act", new JsonObject { ["action_id"] = id, ["version"] = Version() });

        public ToolOutcome Say(string text) => Call("say", new JsonObject { ["text"] = text });
    }

    private static Rig OnMap(Action? duringWait = null) =>
        new(log => new Room("map", "map", log, "Go to the elite", "Go to the rest site"), duringWait);

    // ---- ChatHub ----

    [Theory]
    [InlineData("Aye.", 1500)]                                      // 4 letters: 0.7 s, raised to the floor
    [InlineData("Twenty letters here, plus", 1600)]                 // 22 letters: 0.5 + 1.1 s
    [InlineData("Thirty-five letters in this line, I think", 2250)] // 35 letters: 0.5 + 1.75 s
    [InlineData("A long line that goes on and on, far past the point where anyone would still be reading the bubble.", 3000)] // capped
    public void Say_holds_the_next_move_for_its_length(string text, int millis)
    {
        var now = Start;
        var hub = new ChatHub(() => now);
        Assert.Equal(TimeSpan.Zero, hub.SpeechHoldLeft());

        hub.Say(text);
        Assert.Equal(TimeSpan.FromMilliseconds(millis), hub.SpeechHoldLeft());
        Assert.Equal(TimeSpan.FromMilliseconds(millis), ChatHub.SpeechHold(text));
    }

    [Fact]
    public void Spaces_dont_count_toward_the_hold()
    {
        // 30 letters either way: 0.5 + 1.5 s.
        Assert.Equal(TimeSpan.FromSeconds(2), ChatHub.SpeechHold("abcdefghijklmnopqrstuvwxyzabcd"));
        Assert.Equal(TimeSpan.FromSeconds(2), ChatHub.SpeechHold("abcde fghij klmno pqrst uvwxy zabcd"));
    }

    [Fact]
    public void The_hold_runs_down_with_the_clock()
    {
        var now = Start;
        var hub = new ChatHub(() => now);
        hub.Say("Thirty-five letters in this line, I think"); // 2.25 s

        now += TimeSpan.FromSeconds(1);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), hub.SpeechHoldLeft());
        now += TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.Zero, hub.SpeechHoldLeft());
    }

    [Fact]
    public void A_short_line_doesnt_cut_a_longer_hold_short()
    {
        var now = Start;
        var hub = new ChatHub(() => now);
        hub.Say("A long line that goes on and on, far past the point where anyone would still be reading the bubble.");
        hub.Say("Aye.");
        Assert.Equal(ChatHub.MaxSpeechHold, hub.SpeechHoldLeft());
    }

    [Fact]
    public void A_refused_line_sets_no_hold()
    {
        var now = Start;
        var hub = new ChatHub(() => now);
        Assert.Throws<ArgumentException>(() => hub.Say("   "));
        Assert.Equal(TimeSpan.Zero, hub.SpeechHoldLeft());
    }

    // ---- BotTools ----

    [Fact]
    public void Act_off_combat_waits_out_the_hold_before_the_move()
    {
        var rig = OnMap();
        rig.Say("Thirty-five letters in this line, I think");

        var acted = rig.Act("map:0");

        Assert.False(acted.IsError, acted.Payload.ToJsonString());
        Assert.Equal([TimeSpan.FromMilliseconds(2250)], rig.Waits);
        Assert.Equal(["wait", "run map:0"], rig.Log);
    }

    [Fact]
    public void Act_waits_only_for_what_is_left_of_the_hold()
    {
        var rig = OnMap();
        rig.Say("Thirty-five letters in this line, I think");
        rig.Now += TimeSpan.FromSeconds(2);

        rig.Act("map:0");

        Assert.Equal([TimeSpan.FromMilliseconds(250)], rig.Waits);
    }

    [Fact]
    public void Act_long_after_say_doesnt_wait()
    {
        var rig = OnMap();
        rig.Say("Elites. Good.");
        rig.Now += TimeSpan.FromSeconds(10);

        rig.Act("map:0");

        Assert.Empty(rig.Waits);
        Assert.Equal(["run map:0"], rig.Log);
    }

    [Fact]
    public void Act_without_a_line_doesnt_wait()
    {
        var rig = OnMap();
        rig.Act("map:1");
        Assert.Empty(rig.Waits);
    }

    [Theory]
    [InlineData("combat")]
    [InlineData("hand_select")]
    public void Act_in_a_fight_doesnt_wait(string screen)
    {
        // The bubble sits over the character there and stays through the move.
        var rig = new Rig(log => new Room(screen, "play", log, "Play Strike", "Play Defend"));
        rig.Say("Down you go.");

        var acted = rig.Act("play:0");

        Assert.False(acted.IsError, acted.Payload.ToJsonString());
        Assert.Empty(rig.Waits);
        Assert.Equal(["run play:0"], rig.Log);
    }

    [Fact]
    public void Act_on_the_fake_fight_doesnt_wait()
    {
        var waits = 0;
        var game = new FakeGame { ActionDelay = TimeSpan.FromMilliseconds(20) };
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = TimeSpan.FromMilliseconds(5), SettleTime = TimeSpan.FromMilliseconds(20) };
        var tools = new BotTools(game, waiter, versioner, new ChatHub())
        {
            ActSettleTimeout = TimeSpan.FromSeconds(3),
            Delay = (_, _) => { waits++; return Task.CompletedTask; },
        };
        tools.CallAsync("say", new JsonObject { ["text"] = "Bash first." }, CancellationToken.None).GetAwaiter().GetResult();

        var acted = tools.CallAsync("act", new JsonObject { ["action_id"] = "play:3>e0" }, CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(acted.IsError, acted.Payload.ToJsonString());
        Assert.Equal(0, waits);
    }

    [Fact]
    public void Act_sequence_waits_once_before_its_first_step_only()
    {
        // The delay doesn't move the clock, so the hold is still running at every later step.
        var rig = new Rig(log => new Room("rewards", "reward", log, "Take 15 gold", "Take Fire Potion", "Take Fear Potion"));
        rig.Say("Gold and potions. I'll take it all.");

        var ran = rig.Call("act_sequence", new JsonObject
        {
            ["version"] = rig.Version(),
            ["actions"] = new JsonArray("reward:0", "reward:1", "reward:2"),
        });

        Assert.False(ran.IsError, ran.Payload.ToJsonString());
        Assert.Null(ran.Payload["stopped"]);
        Assert.Equal([TimeSpan.FromMilliseconds(1950)], rig.Waits);
        // Each later reward is found again by its label as the list shifts up.
        Assert.Equal(["wait", "run reward:0", "run reward:0", "run reward:0"], rig.Log);
    }

    [Fact]
    public void A_snapshot_that_changes_during_the_wait_is_refused_like_a_stale_id()
    {
        Room? room = null;
        var rig = new Rig(log => room = new Room("map", "map", log, "Go to the elite", "Go to the rest site"),
            duringWait: () => room!.Choices.Add("Go to the shop"));
        rig.Say("Elites. Good.");

        var acted = rig.Act("map:0");

        Assert.True(acted.IsError);
        Assert.Contains("Snapshot changed", acted.Payload["error"]!.GetValue<string>());
        Assert.Contains(acted.Payload["current"]!["actions"]!.AsArray(), a => a!["label"]!.GetValue<string>() == "Go to the shop");
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("run"));
    }

    [Fact]
    public void Act_sequence_refuses_a_change_during_the_wait_and_runs_nothing()
    {
        Room? room = null;
        var rig = new Rig(log => room = new Room("rewards", "reward", log, "Take 15 gold", "Take Fire Potion"),
            duringWait: () => room!.Choices.RemoveAt(1));
        rig.Say("Mine.");

        var ran = rig.Call("act_sequence", new JsonObject
        {
            ["version"] = rig.Version(),
            ["actions"] = new JsonArray("reward:0", "reward:1"),
        });

        Assert.True(ran.IsError);
        Assert.Contains("Snapshot changed", ran.Payload["error"]!.GetValue<string>());
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("run"));
    }

    [Fact]
    public void A_pause_during_the_wait_stops_the_move()
    {
        Rig? rig = null;
        rig = OnMap(duringWait: () => rig!.Chat.PostFromHuman("Mark", "/pause"));
        rig.Say("Elites. Good.");

        var acted = rig.Act("map:0");

        Assert.True(acted.IsError);
        Assert.StartsWith("Paused by Mark", acted.Payload["error"]!.GetValue<string>());
        Assert.DoesNotContain(rig.Log, l => l.StartsWith("run"));
    }

    [Fact]
    public void A_cancelled_wait_throws_and_runs_nothing()
    {
        var log = new List<string>();
        var game = new Room("map", "map", log, "Go to the elite");
        var versioner = new SnapshotVersioner();
        var tools = new BotTools(game, new DecisionWaiter(game, versioner), versioner, new ChatHub())
        {
            Delay = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct),
        };
        tools.CallAsync("say", new JsonObject { ["text"] = "Onward." }, CancellationToken.None).GetAwaiter().GetResult();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        Assert.ThrowsAny<OperationCanceledException>(() =>
            tools.CallAsync("act", new JsonObject { ["action_id"] = "map:0" }, cts.Token).GetAwaiter().GetResult());
        Assert.Empty(log);
    }
}
