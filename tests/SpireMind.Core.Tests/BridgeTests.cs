using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using Xunit;

namespace SpireMind.Core.Tests;

public class BridgeTests
{
    private static (BotTools Tools, FakeGame Game) Build()
    {
        var game = new FakeGame { ActionDelay = TimeSpan.FromMilliseconds(50) };
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner)
        {
            PollInterval = TimeSpan.FromMilliseconds(10),
            SettleTime = TimeSpan.FromMilliseconds(30),
        };
        return (new BotTools(game, waiter, versioner, new ChatHub()) { ActSettleTimeout = TimeSpan.FromSeconds(3) }, game);
    }

    private static JsonObject Call(BotTools tools, string name, JsonObject? args = null)
    {
        var outcome = tools.CallAsync(name, args ?? new JsonObject(), CancellationToken.None).GetAwaiter().GetResult();
        return outcome.Payload.AsObject();
    }

    [Fact]
    public void Version_is_stable_until_state_changes()
    {
        var (tools, _) = Build();
        var a = Call(tools, "get_state")["version"]!.GetValue<long>();
        var b = Call(tools, "get_state")["version"]!.GetValue<long>();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Act_returns_the_next_settled_decision()
    {
        var (tools, _) = Build();
        var snap = Call(tools, "get_state");
        var result = Call(tools, "act", new JsonObject
        {
            ["action_id"] = "play:0>e0",
            ["version"] = snap["version"]!.GetValue<long>(),
        });

        var next = result["next"]!.AsObject();
        Assert.True(next["awaiting_decision"]!.GetValue<bool>());
        Assert.True(next["version"]!.GetValue<long>() > snap["version"]!.GetValue<long>());
        Assert.Equal(34, next["state"]!["enemies"]![0]!["hp"]!.GetValue<int>());
    }

    [Fact]
    public void Stale_version_is_rejected_without_acting()
    {
        var (tools, _) = Build();
        var snap = Call(tools, "get_state");
        var outcome = tools.CallAsync("act", new JsonObject
        {
            ["action_id"] = "play:0>e0",
            ["version"] = snap["version"]!.GetValue<long>() + 5,
        }, CancellationToken.None).Result;

        Assert.True(outcome.IsError);
        Assert.Equal(40, outcome.Payload["current"]!["state"]!["enemies"]![0]!["hp"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_screen_peek_while_the_model_plans_leaves_its_version_valid(bool peek)
    {
        // The brain controller's poll looks at the screen while a deck view is open, then again
        // once it is closed. A peek leaves the version alone; a stamped capture (the old way)
        // counts two changes and the model's act is refused with the game unchanged.
        var game = new CoverableGame();
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = TimeSpan.FromMilliseconds(10), SettleTime = TimeSpan.FromMilliseconds(30) };
        var tools = new BotTools(game, waiter, versioner, new ChatHub()) { ActSettleTimeout = TimeSpan.FromSeconds(3) };
        Task<GameSnapshot> Look() => peek ? waiter.PeekAsync(CancellationToken.None) : waiter.CaptureAsync(CancellationToken.None);
        var version = Call(tools, "get_state")["version"]!.GetValue<long>();

        game.Covered = true;
        Assert.Equal("unknown", (await Look()).Screen);
        game.Covered = false;
        Assert.Equal("combat", (await Look()).Screen);

        var outcome = await tools.CallAsync("act", new JsonObject { ["action_id"] = "play:0>e0", ["version"] = version }, CancellationToken.None);
        Assert.Equal(!peek, outcome.IsError);
        if (peek)
            Assert.Equal(34, outcome.Payload["next"]!["state"]!["enemies"]![0]!["hp"]!.GetValue<int>());
        else
            Assert.Contains("Snapshot changed", outcome.Payload["error"]!.GetValue<string>());
    }

    /// <summary>The fake fight, with a deck view the test can open over it.</summary>
    private sealed class CoverableGame : IGameBridge
    {
        private readonly FakeGame _game = new() { ActionDelay = TimeSpan.FromMilliseconds(50) };

        public volatile bool Covered;

        public Task<GameSnapshot> CaptureAsync(CancellationToken ct) =>
            Covered ? Task.FromResult(GameSnapshot.Busy("unknown", "a deck view or the pause menu is open")) : _game.CaptureAsync(ct);

        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct) => _game.ExecuteAsync(actionId, ct);
        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => _game.DescribeDeckAsync(ct);
    }

    [Fact]
    public void Illegal_action_is_rejected()
    {
        var (tools, _) = Build();
        var outcome = tools.CallAsync("act", new JsonObject { ["action_id"] = "play:9>e0" }, CancellationToken.None).Result;
        Assert.True(outcome.IsError);
        Assert.Contains("not a legal action", outcome.Payload["error"]!.GetValue<string>());
    }

    [Fact]
    public void Mcp_initialize_list_and_call()
    {
        var (tools, _) = Build();
        var mcp = new McpEndpoint(tools, "test");

        var init = mcp.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""", default).Result!;
        Assert.Equal("2025-06-18", init["result"]!["protocolVersion"]!.GetValue<string>());

        Assert.Null(mcp.HandleAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", default).Result);

        var list = mcp.HandleAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""", default).Result!;
        var names = list["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).ToArray();
        Assert.Equal(["get_state", "wait_for_decision", "act", "act_sequence", "say", "get_deck", "get_persona"], names);

        var call = mcp.HandleAsync("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_state","arguments":{}}}""", default).Result!;
        var text = call["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Equal("combat", JsonNode.Parse(text)!["screen"]!.GetValue<string>());
        Assert.False(call["result"]!["isError"]!.GetValue<bool>());
    }

    [Fact]
    public void Mcp_unknown_method_and_bad_json()
    {
        var (tools, _) = Build();
        var mcp = new McpEndpoint(tools, "test");
        Assert.Equal(-32601, mcp.HandleAsync("""{"jsonrpc":"2.0","id":1,"method":"nope"}""", default).Result!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32700, mcp.HandleAsync("{not json", default).Result!["error"]!["code"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("https://evil.example", false)]
    public void Origin_check(string? origin, bool allowed) =>
        Assert.Equal(allowed, BridgeServer.IsLocalOrigin(origin));

    [Fact]
    public async Task Http_server_serves_mcp_end_to_end()
    {
        var (tools, _) = Build();
        var port = FreePort();
        using var server = new BridgeServer(port, tools, new ChatHub(), "test", _ => { });
        server.Start();

        using var http = new HttpClient();
        var health = await http.GetStringAsync($"http://127.0.0.1:{port}/health");
        Assert.Contains("spiremind", health);

        var res = await http.PostAsync($"http://127.0.0.1:{port}/mcp",
            new StringContent("""{"jsonrpc":"2.0","id":7,"method":"tools/list"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("wait_for_decision", await res.Content.ReadAsStringAsync());

        var note = await http.PostAsync($"http://127.0.0.1:{port}/mcp",
            new StringContent("""{"jsonrpc":"2.0","method":"notifications/initialized"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, note.StatusCode);

        var get = await http.GetAsync($"http://127.0.0.1:{port}/mcp");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
    }

    [Fact]
    public async Task Tool_calls_on_mcp_count_as_another_client_and_those_on_mcp_own_do_not()
    {
        var (tools, _) = Build();
        var port = FreePort();
        using var server = new BridgeServer(port, tools, new ChatHub(), "test", _ => { });
        server.Start();
        using var http = new HttpClient();
        const string call = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"get_state","arguments":{}}}""";
        var window = TimeSpan.FromSeconds(30);

        // Listing tools is not playing, and the mod's own Claude Code (/mcp/own) never counts.
        await http.PostAsync($"http://127.0.0.1:{port}/mcp",
            new StringContent("""{"jsonrpc":"2.0","id":7,"method":"tools/list"}""", Encoding.UTF8, "application/json"));
        var own = await http.PostAsync($"http://127.0.0.1:{port}/mcp/own", new StringContent(call, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains("awaiting_decision", await own.Content.ReadAsStringAsync());
        Assert.False(server.ExternalClientActive(window));

        var other = await http.PostAsync($"http://127.0.0.1:{port}/mcp", new StringContent(call, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.True(server.ExternalClientActive(window));
        Assert.False(server.ExternalClientActive(TimeSpan.Zero)); // nothing in flight, and the window is over

        var get = await http.GetAsync($"http://127.0.0.1:{port}/mcp/own");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
    }

    internal static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}

public class ActWarningTests
{
    private sealed class DeafGame : IGameBridge
    {
        public Task<GameSnapshot> CaptureAsync(CancellationToken ct) => Task.FromResult(new GameSnapshot
        {
            Screen = "combat", AwaitingDecision = true, Actions = [new LegalAction("end_turn", "End turn")],
        });
        public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct) => Task.FromResult(ActionResult.Success);
        public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => Task.FromResult(new JsonObject());
    }

    [Fact]
    public void Warns_when_an_action_changes_nothing()
    {
        var game = new DeafGame();
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = TimeSpan.FromMilliseconds(5), SettleTime = TimeSpan.FromMilliseconds(5) };
        var tools = new BotTools(game, waiter, versioner, new ChatHub()) { ActSettleTimeout = TimeSpan.FromMilliseconds(100) };
        var outcome = tools.CallAsync("act", new System.Text.Json.Nodes.JsonObject { ["action_id"] = "end_turn" }, default).Result;
        Assert.False(outcome.IsError);
        Assert.Contains("Nothing changed", outcome.Payload["warning"]!.GetValue<string>());
    }
}
