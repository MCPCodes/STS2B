using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Coop;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>The bridge's co-op routes: /coop/info, the identity on /chat and teammate posts.</summary>
public class BridgeCoopTests
{
    private static (BridgeServer Server, ChatHub Chat, int Port) Serve(InstanceInfo? instance, string? botName = "Silent")
    {
        var game = new FakeGame();
        var versioner = new SnapshotVersioner();
        var chat = new ChatHub { BotName = botName };
        var tools = new BotTools(game, new DecisionWaiter(game, versioner), versioner, chat);
        var port = BridgeTests.FreePort();
        var server = new BridgeServer(port, tools, chat, "0.1.0", _ => { }) { Instance = instance };
        server.Start();
        return (server, chat, port);
    }

    private static async Task<JsonObject> Get(HttpClient http, int port, string path) =>
        JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{port}{path}"))!.AsObject();

    private static async Task<(HttpStatusCode Status, JsonObject Body)> Post(HttpClient http, int port, string json)
    {
        var res = await http.PostAsync($"http://127.0.0.1:{port}/chat", new StringContent(json, Encoding.UTF8, "application/json"));
        return (res.StatusCode, JsonNode.Parse(await res.Content.ReadAsStringAsync())!.AsObject());
    }

    [Fact]
    public async Task Coop_info_describes_a_bot_copy()
    {
        var info = new InstanceInfo(true, 17773, "bot-17773") { NetId = 1000, Playing = () => "api" };
        var (server, chat, port) = Serve(info);
        using var _ = server;
        chat.Say("ready");
        chat.PostFromHuman("Mark", "/pause");

        using var http = new HttpClient();
        var json = await Get(http, port, "/coop/info");
        Assert.Equal("spiremind", json["name"]!.GetValue<string>());
        Assert.Equal("0.1.0", json["version"]!.GetValue<string>());
        Assert.Equal("bot", json["role"]!.GetValue<string>());
        Assert.Equal(port, json["port"]!.GetValue<int>());
        Assert.Equal("bot-17773", json["instance"]!.GetValue<string>());
        Assert.Equal(server.Session, json["session"]!.GetValue<string>());
        Assert.Equal(32, server.Session.Length);
        Assert.Equal(1000UL, json["net_id"]!.GetValue<ulong>());
        Assert.Equal("Silent", json["character"]!.GetValue<string>());
        Assert.Equal("api", json["playing"]!.GetValue<string>());
        Assert.True(json["paused"]!.GetValue<bool>());
        Assert.Equal(2, json["last_seq"]!.GetValue<long>());

        // The NetId and the brain change while the copy runs; a failing Playing reads as unknown.
        info.NetId = 18_000_000_000_000_000_000UL;
        info.Playing = () => throw new InvalidOperationException("no controller");
        json = await Get(http, port, "/coop/info");
        Assert.Equal(18_000_000_000_000_000_000UL, json["net_id"]!.GetValue<ulong>());
        Assert.Null(json["playing"]);
        Assert.Equal(server.Session, json["session"]!.GetValue<string>());
    }

    [Fact]
    public async Task Without_an_instance_the_copy_reports_the_human_role()
    {
        var (server, _, port) = Serve(null, botName: null);
        using var _s = server;
        using var http = new HttpClient();
        var json = await Get(http, port, "/coop/info");
        Assert.Equal("human", json["role"]!.GetValue<string>());
        Assert.Equal("main", json["instance"]!.GetValue<string>());
        Assert.Equal(port, json["port"]!.GetValue<int>());
        Assert.Null(json["net_id"]);
        Assert.Null(json["character"]);
        Assert.Null(json["playing"]);
        Assert.False(json["paused"]!.GetValue<bool>());
        Assert.Equal(0, json["last_seq"]!.GetValue<long>());
        Assert.True(json.ContainsKey("net_id") && json.ContainsKey("character") && json.ContainsKey("playing"));

        // Every server has its own session.
        var (other, _, _) = Serve(null);
        using var _o = other;
        Assert.NotEqual(server.Session, other.Session);
    }

    [Fact]
    public async Task Chat_log_carries_the_identity_and_marks_remote_lines()
    {
        var (server, chat, port) = Serve(new InstanceInfo(true, 17774, "bot-17774") { NetId = 1001 });
        using var _ = server;
        chat.PostFromHuman("Mark", "Silent, hi");
        chat.Say("Hello.");
        chat.PostFromTeammate("Ironclad", "Hi all.");
        chat.Announce("Switching brains.");

        using var http = new HttpClient();
        var log = await Get(http, port, "/chat?since=0");
        Assert.Equal("Silent", log["bot"]!.GetValue<string>());
        Assert.False(log["paused"]!.GetValue<bool>());
        Assert.Equal("bot", log["role"]!.GetValue<string>());
        Assert.Equal(port, log["port"]!.GetValue<int>());
        Assert.Equal("bot-17774", log["instance"]!.GetValue<string>());
        Assert.Equal(server.Session, log["session"]!.GetValue<string>());
        Assert.Equal(1001UL, log["net_id"]!.GetValue<ulong>());
        Assert.Equal("Silent", log["character"]!.GetValue<string>());
        Assert.Equal(4, log["last_seq"]!.GetValue<long>());

        var lines = log["lines"]!.AsArray();
        Assert.Equal(["human", "bot", "bot", "system"], lines.Select(l => l!["kind"]!.GetValue<string>()));
        Assert.Equal([false, false, true, false], lines.Select(l => l!["remote"]!.GetValue<bool>()));
        Assert.Equal("Ironclad", lines[2]!["from"]!.GetValue<string>());

        var later = await Get(http, port, "/chat?since=3");
        Assert.Single(later["lines"]!.AsArray());
        Assert.Equal(4, later["last_seq"]!.GetValue<long>());
        var bad = await Get(http, port, "/chat?since=soon");
        Assert.Equal(4, bad["lines"]!.AsArray().Count);
    }

    [Fact]
    public async Task Teammate_posts_go_to_the_party_path_and_every_post_reports_its_seq()
    {
        var (server, chat, port) = Serve(new InstanceInfo(true, 17773, "bot-17773"));
        using var _ = server;
        using var http = new HttpClient();

        var human = await Post(http, port, """{"from":"Mark","text":"go poison"}""");
        Assert.Equal(HttpStatusCode.OK, human.Status);
        Assert.Equal("queued", human.Body["outcome"]!.GetValue<string>());
        Assert.Equal(1, human.Body["seq"]!.GetValue<long>());
        Assert.True(chat.HasUnread);
        chat.TakeUnread();

        var party = await Post(http, port, """{"from":"Ironclad","text":"/pause","teammate":true}""");
        Assert.Equal("queued", party.Body["outcome"]!.GetValue<string>());
        Assert.Equal(2, party.Body["seq"]!.GetValue<long>());
        Assert.Null(party.Body["reply"]);
        Assert.False(chat.Paused);
        Assert.False(chat.HasUnread);
        var line = chat.LinesSince(1).Single();
        Assert.Equal(ChatLineKind.Bot, line.Kind);
        Assert.True(line.Remote);
        Assert.Equal("Ironclad", line.From);
        Assert.True(Assert.Single(chat.TakeUnread()).Teammate);

        var notMine = await Post(http, port, """{"from":"Ironclad","text":"Defect, orbs","teammate":true}""");
        Assert.Equal("not_for_me", notMine.Body["outcome"]!.GetValue<string>());
        Assert.Equal(3, notMine.Body["seq"]!.GetValue<long>());

        var notTeammate = await Post(http, port, """{"from":"Mark","text":"/pause","teammate":false}""");
        Assert.Equal("command", notTeammate.Body["outcome"]!.GetValue<string>());
        Assert.Equal("Silent paused. Type /resume to hand back control.", notTeammate.Body["reply"]!.GetValue<string>());
        Assert.Equal(4, notTeammate.Body["seq"]!.GetValue<long>());
        Assert.True(chat.Paused);

        chat.AddCommand("quiet", "", "says nothing", (_, _) => "");
        var quiet = await Post(http, port, """{"from":"Mark","text":"/quiet"}""");
        Assert.Equal("command", quiet.Body["outcome"]!.GetValue<string>());
        Assert.True(quiet.Body.ContainsKey("seq"));
        Assert.Null(quiet.Body["seq"]);

        var empty = await Post(http, port, """{"from":"Ironclad","text":"  ","teammate":true}""");
        Assert.Equal(HttpStatusCode.BadRequest, empty.Status);
        Assert.Equal("empty", empty.Body["outcome"]!.GetValue<string>());
        Assert.Null(empty.Body["seq"]);

        // Missing from: a teammate is "Bot", a human "Player", as before.
        await Post(http, port, """{"text":"anyone?","teammate":true}""");
        Assert.Equal("Bot", chat.LinesSince(0).Last().From);
    }

    [Fact]
    public void Instance_info_is_safe_to_read_and_falls_back_on_a_blank_id()
    {
        var info = new InstanceInfo(true, 17775, " ");
        Assert.Equal("bot-17775", info.InstanceId);
        Assert.Equal("main", new InstanceInfo(false, 17772, "").InstanceId);
        Assert.True(info.IsBot);
        Assert.Equal(17775, info.Port);
        Assert.Null(info.NetId);
        Assert.Null(info.ReadPlaying());
        info.Playing = () => "  ";
        Assert.Null(info.ReadPlaying());
        info.Playing = () => "subscription";
        Assert.Equal("subscription", info.ReadPlaying());

        Parallel.For(0, 2000, i =>
        {
            if (i % 2 == 0) info.NetId = (ulong)i;
            else _ = info.NetId;
        });
        Assert.NotNull(info.NetId);
    }
}
