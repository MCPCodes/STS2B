using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

public class PartyBotTests
{
    private DateTime _now = new(2026, 10, 11, 20, 0, 0, DateTimeKind.Utc);
    private readonly ConcurrentQueue<string> _log = new();

    private PartyBot Bot() => new(_log.Enqueue, () => _now);

    internal static GameSnapshot Snap(string screen, JsonObject state, bool awaiting = true) =>
        new() { Screen = screen, AwaitingDecision = awaiting, State = state, Actions = [new LegalAction("option:0", "Take it")] };

    internal static GameSnapshot Ancient(int act = 1) => Snap("event", PartyTriggersTests.AncientState(act));

    internal static GameSnapshot Pick(string screen = "card_reward", int act = 1) =>
        Snap(screen, new JsonObject { ["run"] = PartyTriggersTests.Run(act) });

    private static JsonObject Op(string op, params (string Key, JsonNode? Value)[] fields)
    {
        var json = new JsonObject { ["op"] = op };
        foreach (var (k, v) in fields) json[k] = v;
        return json;
    }

    private static JsonObject Hello(bool auto = true) => Op("hello", ("auto", auto));

    private static JsonObject Open(string key, int seconds = 150, string kind = PartyTriggers.ActStart) =>
        Op("open", ("session", key), ("kind", kind), ("act", 1), ("seconds", seconds));

    private static JsonObject Turn(string key, int round = 1) =>
        Op("turn", ("session", key), ("round", round), ("rounds", 2), ("seconds", 40), ("ask", "Pitch now."),
            ("said", new JsonArray()), ("mark", new JsonArray()), ("plan", null));

    private static JsonObject Close(string key, string reason = "done") =>
        Op("close", ("session", key), ("reason", reason), ("plan", "# Party plan\nAct 1."), ("yours", "Role: debuffer."),
            ("calls", new JsonArray("Damage relics go to Mark (Silent)")));

    private static bool Ok(JsonObject reply) => reply["ok"]!.GetValue<bool>();

    [Fact]
    public void No_hold_without_hello_with_auto_on()
    {
        var bot = Bot();
        Assert.False(bot.Holds(Ancient()));
        Assert.Equal("A1-start", bot.Identity()["due"]!.GetValue<string>());

        bot.Handle(Hello(auto: false));
        Assert.False(bot.Holds(Ancient()));

        bot.Handle(Hello(auto: true));
        Assert.True(bot.Holds(Ancient()));
        Assert.True(bot.Identity()["holding"]!.GetValue<bool>());
        Assert.Contains(_log, l => l == "party: due A1-start (event, ancient)");
        Assert.Contains(_log, l => l == "party: holding event for A1-start");
    }

    [Fact]
    public void The_pending_hold_lets_go_after_its_timeout_and_settles_the_key()
    {
        var bot = Bot();
        bot.Handle(Hello());
        Assert.True(bot.Holds(Ancient()));
        _now += TimeSpan.FromSeconds(14);
        Assert.True(bot.Holds(Ancient()));
        _now += TimeSpan.FromSeconds(2);
        Assert.False(bot.Holds(Ancient()));
        Assert.Contains(_log, l => l == "party: released the hold for A1-start after 15 s (no session opened)");
        var identity = bot.Identity();
        Assert.Null(identity["due"]);
        Assert.Contains("A1-start", identity["settled"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.False(Ok(bot.Handle(Open("A1-start"))));
    }

    [Fact]
    public void Open_holds_pick_screens_but_never_the_map_or_a_fight()
    {
        var bot = Bot();
        Assert.True(Ok(bot.Handle(Open("A1-start"))));
        Assert.True(bot.Holds(Ancient()));
        Assert.True(bot.Holds(Pick("card_reward")));
        Assert.True(bot.Holds(Pick("shop")));
        Assert.True(bot.Holds(Pick("rest")));
        Assert.True(bot.Holds(Pick("card_select")));
        Assert.False(bot.Holds(Snap("card_select", new JsonObject { ["combat"] = new JsonObject() })));
        Assert.False(bot.Holds(Snap("map", PartyTriggersTests.MapState(1, r: 3))));
        Assert.False(bot.Holds(Pick("combat")));
        Assert.False(bot.Holds(Pick("hand_select")));
        Assert.False(bot.Holds(Pick("crystal_sphere")));
        Assert.Equal("A1-start", bot.Identity()["session"]!.GetValue<string>());
        Assert.Contains("up to 165 s more", bot.HeldText(Pick())!);
    }

    [Fact]
    public void Close_releases_the_hold_settles_the_key_and_brings_the_plan()
    {
        var bot = Bot();
        bot.Handle(Open("A1-start"));
        Assert.True(Ok(bot.Handle(Close("A1-start"))));
        Assert.False(bot.Holds(Pick()));
        Assert.Contains("A1-start", bot.Identity()["settled"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal("# Party plan\nAct 1.", bot.PlanText);
        Assert.True(bot.HasNews);

        var news = bot.Brief(take: true)!;
        Assert.Equal("A1-start", news["closed"]!.GetValue<string>());
        Assert.Equal(PartyPrompts.Closed, news["note"]!.GetValue<string>());
        Assert.Equal("# Party plan\nAct 1.", news["plan"]!.GetValue<string>());
        Assert.False(bot.HasNews);
        Assert.Null(bot.Brief(take: true));
        Assert.Contains(_log, l => l == "party: closed A1-start (done)");

        // An early close says why.
        bot.Handle(Open("A1-boss", kind: PartyTriggers.Boss));
        bot.Handle(Close("A1-boss", "Mark skipped it"));
        Assert.Equal("Planning ended early (Mark skipped it). Use the plan below as it stands, and your own Build plan where it says nothing. Your held pick is free.",
            bot.Brief(take: true)!["note"]!.GetValue<string>());
    }

    [Fact]
    public void The_sessions_time_plus_grace_releases_the_hold()
    {
        var bot = Bot();
        bot.Handle(Open("A1-start", seconds: 75));
        _now += TimeSpan.FromSeconds(89);
        Assert.True(bot.Holds(Pick()));
        _now += TimeSpan.FromSeconds(2);
        Assert.False(bot.Holds(Pick()));
        Assert.Contains("A1-start", bot.Identity()["settled"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Contains("ran out of time", bot.Brief(take: true)!["note"]!.GetValue<string>());
    }

    [Fact]
    public void A_settled_key_refuses_open()
    {
        var bot = Bot();
        bot.Handle(Op("hello", ("auto", true), ("settled", new JsonArray("A1-start"))));
        var reply = bot.Handle(Open("A1-start"));
        Assert.False(Ok(reply));
        Assert.Contains("settled", reply["error"]!.GetValue<string>());
        Assert.False(bot.Holds(Ancient()));
        Assert.False(Ok(bot.Handle(Op("dance"))));
    }

    [Fact]
    public void Pitch_is_refused_outside_a_turn_and_its_fields_are_cut()
    {
        var bot = Bot();
        var (none, error) = bot.Pitch(new JsonObject { ["say"] = "hi" });
        Assert.Null(none);
        Assert.Equal(PartyPrompts.NotYourTurn, error);

        bot.Handle(Open("A1-start"));
        Assert.False(Ok(bot.Handle(Turn("A1-mid"))));
        bot.Handle(Turn("A1-start"));
        Assert.Equal("say is required: your line to the party.", bot.Pitch(new JsonObject { ["role"] = "damage" }).Error);

        var (pitch, _) = bot.Pitch(new JsonObject
        {
            ["say"] = new string('x', 250), ["role"] = "debuffer", ["wants"] = new JsonArray("a", "b", "c", "d", "e"),
        });
        Assert.Equal(PartyPitch.MaxSay, pitch!.Say.Length);
        Assert.Equal(4, pitch.Wants.Count);
        var reported = bot.Identity()["pitch"]!;
        Assert.Equal("A1-start", reported["session"]!.GetValue<string>());
        Assert.Equal(1, reported["round"]!.GetValue<int>());
        Assert.Equal("debuffer", reported["role"]!.GetValue<string>());
        // One pitch per turn.
        Assert.Equal(PartyPrompts.NotYourTurn, bot.Pitch(new JsonObject { ["say"] = "again" }).Error);
    }

    [Fact]
    public void The_turn_brief_stays_until_the_bot_pitches_and_flips_undelivered_once()
    {
        var bot = Bot();
        bot.Handle(Open("A1-start"));
        bot.Handle(Turn("A1-start"));
        Assert.True(bot.HasUndeliveredTurn);
        Assert.True(bot.HasNews);

        var peek = bot.Brief(take: false)!;
        Assert.True(peek["your_turn"]!.GetValue<bool>());
        Assert.True(bot.HasUndeliveredTurn);

        _now += TimeSpan.FromSeconds(9);
        var brief = bot.Brief(take: true)!;
        Assert.Equal("A1-start", brief["session"]!.GetValue<string>());
        Assert.Equal(PartyTriggers.ActStart, brief["kind"]!.GetValue<string>());
        Assert.Equal(1, brief["round"]!.GetValue<int>());
        Assert.Equal(2, brief["rounds"]!.GetValue<int>());
        Assert.Equal(31, brief["seconds_left"]!.GetValue<int>());
        Assert.Equal("Pitch now.", brief["ask"]!.GetValue<string>());
        Assert.False(bot.HasUndeliveredTurn);
        Assert.False(bot.HasNews);
        Assert.NotNull(bot.Brief(take: true));

        bot.Pitch(new JsonObject { ["say"] = "Poison for me." });
        Assert.Null(bot.Brief(take: true));
    }

    [Fact]
    public void Yours_and_calls_ride_only_on_pick_screens()
    {
        var bot = Bot();
        Assert.Null(bot.PickNotes(Pick()));
        bot.Handle(Open("A1-start"));
        bot.Handle(Close("A1-start"));
        var notes = bot.PickNotes(Pick("shop"))!;
        Assert.Equal("Role: debuffer.", notes["yours"]!.GetValue<string>());
        Assert.Equal("Damage relics go to Mark (Silent)", notes["calls"]![0]!.GetValue<string>());
        Assert.Null(bot.PickNotes(Snap("map", PartyTriggersTests.MapState(1, r: 3))));
        Assert.Null(bot.PickNotes(Pick("combat")));
    }

    [Fact]
    public void A_boss_due_settles_mid_and_a_new_run_forgets_the_settled_keys()
    {
        var bot = Bot();
        bot.Observe(Snap("map", PartyTriggersTests.MapState(1, r: 14)));
        var identity = bot.Identity();
        Assert.Equal("A1-boss", identity["due"]!.GetValue<string>());
        Assert.Contains("A1-mid", identity["settled"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal("Kin Priest", identity["boss"]!.GetValue<string>());
        // A mid screen after it is no longer due.
        bot.Observe(Snap("map", PartyTriggersTests.MapState(1, r: 9)));
        Assert.Equal("A1-boss", bot.Identity()["due"]!.GetValue<string>());

        bot.Observe(Pick("game_over"));
        identity = bot.Identity();
        Assert.Empty(identity["settled"]!.AsArray());
        Assert.Null(identity["due"]);

        bot.Handle(Op("hello", ("auto", true), ("settled", new JsonArray("A2-start", "A2-mid"))));
        bot.Observe(Pick(act: 2));
        bot.Observe(Pick(act: 1));
        Assert.Empty(bot.Identity()["settled"]!.AsArray());
    }

    [Fact]
    public void Identity_has_the_reported_shape()
    {
        var bot = Bot();
        bot.Handle(Hello());
        bot.Holds(Ancient(2));
        var json = bot.Identity();
        Assert.Equal(1, json["v"]!.GetValue<int>());
        Assert.True(json["auto"]!.GetValue<bool>());
        Assert.Equal("A2-start", json["due"]!.GetValue<string>());
        Assert.IsType<JsonArray>(json["settled"]);
        Assert.Equal("event", json["screen"]!.GetValue<string>());
        Assert.Equal(2, json["act"]!.GetValue<int>());
        Assert.True(json["holding"]!.GetValue<bool>());
        Assert.Null(json["session"]);
        Assert.Null(json["pitch"]);
        // Only the last 9 settled keys are reported.
        for (var i = 0; i < 12; i++)
            bot.Handle(Close($"K{i}"));
        Assert.Equal(9, bot.Identity()["settled"]!.AsArray().Count);
    }
}
