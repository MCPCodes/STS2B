using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>BotTools with a PartyBot: party_pitch, held pick screens and the party key on results.</summary>
public class BotToolsPartyTests
{
    private sealed class Screen : IGameBridge
    {
        private volatile GameSnapshot _current = PartyBotTests.Pick();
        public ConcurrentQueue<string> Executed { get; } = new();

        public void Show(GameSnapshot snap) => _current = snap;

        public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
        {
            var s = _current;
            return Task.FromResult(new GameSnapshot
            {
                Screen = s.Screen, AwaitingDecision = s.AwaitingDecision, Waiting = s.Waiting,
                State = (JsonObject)s.State.DeepClone(), Actions = s.Actions,
            });
        }

        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
        {
            Executed.Enqueue(actionId);
            // The pick leads on to the map.
            _current = PartyBotTests.Snap("map", PartyTriggersTests.MapState(1, r: 3));
            return Task.FromResult(ActionResult.Success);
        }

        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => Task.FromResult(new JsonObject());
    }

    private static (BotTools Tools, ChatHub Chat, PartyBot Party, Screen Game) Build(bool withParty = true)
    {
        var game = new Screen();
        var versioner = new SnapshotVersioner();
        var chat = new ChatHub { BotName = "Silent" };
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = TimeSpan.FromMilliseconds(10), SettleTime = TimeSpan.Zero };
        var tools = new BotTools(game, waiter, versioner, chat) { Delay = (_, _) => Task.CompletedTask };
        var party = new PartyBot();
        if (withParty) tools.Party = party;
        return (tools, chat, party, game);
    }

    private static JsonObject Op(string op, params (string Key, JsonNode? Value)[] fields)
    {
        var json = new JsonObject { ["op"] = op };
        foreach (var (k, v) in fields) json[k] = v;
        return json;
    }

    private static void OpenAndTurn(PartyBot party)
    {
        party.Handle(Op("open", ("session", "A1-start"), ("kind", "act_start"), ("act", 1), ("seconds", 150)));
        party.Handle(Op("turn", ("session", "A1-start"), ("round", 1), ("rounds", 1), ("seconds", 40), ("ask", "Pitch now."),
            ("said", new JsonArray()), ("mark", new JsonArray()), ("plan", null)));
    }

    [Fact]
    public void Party_pitch_is_offered_only_with_a_party_bot()
    {
        var (without, _, _, _) = Build(withParty: false);
        Assert.DoesNotContain(without.Specs, s => s.Name == "party_pitch");
        var (tools, _, party, _) = Build();
        var spec = Assert.Single(tools.Specs, s => s.Name == "party_pitch");
        Assert.StartsWith("Only in a co-op planning session", spec.Description);
        Assert.Equal(["say"], spec.InputSchema["required"]!.AsArray().Select(r => r!.GetValue<string>()));
        Assert.Throws<InvalidOperationException>(() => tools.Party = new PartyBot());
        Assert.Same(party, tools.Party);
    }

    [Fact]
    public async Task Party_pitch_speaks_and_records()
    {
        var (tools, chat, party, _) = Build();
        var refused = await tools.CallAsync("party_pitch", new JsonObject { ["say"] = "Poison for me." }, CancellationToken.None);
        Assert.True(refused.IsError);
        Assert.Equal(PartyPrompts.NotYourTurn, refused.Payload["error"]!.GetValue<string>());

        OpenAndTurn(party);
        var outcome = await tools.CallAsync("party_pitch", new JsonObject
        {
            ["say"] = "Poison for me.", ["role"] = "debuffer", ["archetype"] = "poison", ["wants"] = new JsonArray("Catalyst"),
        }, CancellationToken.None);
        Assert.False(outcome.IsError);
        Assert.True(outcome.Payload["pitched"]!.GetValue<bool>());
        Assert.Equal("Poison for me.", outcome.Payload["said"]!.GetValue<string>());
        var line = chat.LinesSince(0).Last();
        Assert.Equal(ChatLineKind.Bot, line.Kind);
        Assert.Equal("Poison for me.", line.Text);
        Assert.Equal("poison", party.Identity()["pitch"]!["archetype"]!.GetValue<string>());
        // The brief is gone once the bot pitched.
        Assert.Null(outcome.Payload["party"]);
    }

    [Fact]
    public async Task A_held_snapshot_is_masked_and_act_gets_the_held_text()
    {
        var (tools, _, party, _) = Build();
        party.Handle(Op("open", ("session", "A1-start"), ("kind", "act_start"), ("act", 1), ("seconds", 150)));

        var state = (await tools.CallAsync("get_state", new JsonObject(), CancellationToken.None)).Payload;
        Assert.False(state["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal(PartyPrompts.HeldWaiting, state["waiting"]!.GetValue<string>());
        Assert.Empty(state["actions"]!.AsArray());
        Assert.Equal(1, state["state"]!["run"]!["act"]!.GetValue<int>());

        var acted = await tools.CallAsync("act", new JsonObject { ["action_id"] = "option:0", ["version"] = state["version"]!.GetValue<long>() },
            CancellationToken.None);
        Assert.True(acted.IsError);
        Assert.StartsWith("Held for the party's planning session (A1-start, up to ", acted.Payload["error"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_wait_returns_on_the_turn_brief_and_the_plan_frees_the_pick()
    {
        var (tools, _, party, game) = Build();
        party.Handle(Op("open", ("session", "A1-start"), ("kind", "act_start"), ("act", 1), ("seconds", 150)));

        var wait = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 20 }, CancellationToken.None);
        await Task.Delay(150);
        Assert.False(wait.IsCompleted);
        party.Handle(Op("turn", ("session", "A1-start"), ("round", 1), ("rounds", 1), ("seconds", 40), ("ask", "Pitch now."),
            ("said", new JsonArray()), ("mark", new JsonArray()), ("plan", null)));
        var turn = (await wait.WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        Assert.False(turn["awaiting_decision"]!.GetValue<bool>());
        Assert.True(turn["party"]!["your_turn"]!.GetValue<bool>());
        Assert.Equal("Pitch now.", turn["party"]!["ask"]!.GetValue<string>());

        await tools.CallAsync("party_pitch", new JsonObject { ["say"] = "Poison." }, CancellationToken.None);
        wait = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 20 }, CancellationToken.None);
        await Task.Delay(150);
        Assert.False(wait.IsCompleted);
        party.Handle(Op("close", ("session", "A1-start"), ("reason", "done"), ("plan", "# Party plan"), ("yours", "Role: debuffer."),
            ("calls", new JsonArray("Damage relics go to Mark (Silent)"))));
        var freed = (await wait.WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        Assert.Equal("A1-start", freed["party"]!["closed"]!.GetValue<string>());
        Assert.Equal("# Party plan", freed["party"]!["plan"]!.GetValue<string>());
        Assert.Equal("Role: debuffer.", freed["party"]!["yours"]!.GetValue<string>());

        // The pick goes through once the plan is set, and later pick screens carry the plan's notes.
        var next = (await tools.CallAsync("get_state", new JsonObject(), CancellationToken.None)).Payload;
        Assert.True(next["awaiting_decision"]!.GetValue<bool>());
        Assert.Equal("Role: debuffer.", next["party"]!["yours"]!.GetValue<string>());
        Assert.Null(next["party"]!["closed"]);
        var acted = await tools.CallAsync("act", new JsonObject { ["action_id"] = "option:0", ["version"] = next["version"]!.GetValue<long>() },
            CancellationToken.None);
        Assert.Equal("option:0", Assert.Single(game.Executed));
        Assert.Null(acted.Payload["error"]);

        // The map never carries them.
        Assert.Equal("map", acted.Payload["next"]!["screen"]!.GetValue<string>());
        Assert.Null(acted.Payload["next"]!["party"]);
    }

    [Fact]
    public async Task The_wait_after_a_pitch_hands_over_the_map_vote_at_once()
    {
        // A mid-act session opens on the map in co-op: the brief comes with the map vote, the model
        // pitches, and its next wait must give the vote back, not hold it as a repeat.
        var (tools, _, party, game) = Build();
        var state = PartyTriggersTests.MapState(1, r: 3);
        state["team"] = new JsonArray(new JsonObject { ["id"] = "p1", ["name"] = "Mark", ["human"] = true });
        game.Show(PartyBotTests.Snap("map", state));
        party.Handle(Op("open", ("session", "A1-mid"), ("kind", "mid"), ("act", 1), ("seconds", 75)));
        party.Handle(Op("turn", ("session", "A1-mid"), ("round", 1), ("rounds", 1), ("seconds", 40), ("ask", "Pitch now."),
            ("said", new JsonArray()), ("mark", new JsonArray()), ("plan", null)));

        var turn = (await tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 20 }, CancellationToken.None)).Payload;
        Assert.True(turn["awaiting_decision"]!.GetValue<bool>());
        Assert.True(turn["party"]!["your_turn"]!.GetValue<bool>());
        Assert.False((await tools.CallAsync("party_pitch", new JsonObject { ["say"] = "Still poison." }, CancellationToken.None)).IsError);

        var wait = tools.CallAsync("wait_for_decision", new JsonObject { ["timeout_seconds"] = 20 }, CancellationToken.None);
        var vote = (await wait.WaitAsync(TimeSpan.FromSeconds(5))).Payload;
        Assert.Equal(turn["version"]!.GetValue<long>(), vote["version"]!.GetValue<long>());
        Assert.True(vote["awaiting_decision"]!.GetValue<bool>());
    }

    [Fact]
    public async Task The_curl_state_route_peeks_at_the_brief_without_taking_it()
    {
        var (tools, chat, party, _) = Build();
        OpenAndTurn(party);
        var port = BridgeTests.FreePort();
        using var server = new BridgeServer(port, tools, chat, "test", _ => { }) { Instance = new InstanceInfo(true, port, "") };
        server.Start();
        using var http = new HttpClient();
        var peek = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}/state"))!;
        Assert.True(peek["party"]!["your_turn"]!.GetValue<bool>());
        Assert.True(party.HasUndeliveredTurn);
        await tools.CallAsync("get_state", new JsonObject(), CancellationToken.None);
        Assert.False(party.HasUndeliveredTurn);
    }
}
