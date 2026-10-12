using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpireMind.Core;
using SpireMind.Core.Brain;
using SpireMind.DevHost;
using Xunit;

namespace SpireMind.Core.Tests;

public class DevHostOptionsTests
{
    private static DevHostOptions Parse(params string[] args) => DevHostOptions.Parse(args);

    [Fact]
    public void No_arguments_serve_the_bridge_on_the_default_port()
    {
        var options = Parse();
        Assert.False(options.Brain);
        Assert.False(options.Help);
        Assert.Equal(17772, options.Port);
    }

    [Fact]
    public void The_port_is_the_first_argument()
    {
        Assert.Equal(17773, Parse("17773").Port);
        Assert.Equal(17773, Parse("17773", "--brain").Port);
    }

    [Fact]
    public void Brain_defaults_to_the_cheap_model_and_six_requests()
    {
        var options = Parse("--brain");
        Assert.True(options.Brain);
        Assert.Equal(BrainSettings.ResolveModel("haiku"), options.Model);
        Assert.Equal(6, options.MaxRequests);
    }

    [Fact]
    public void Brain_takes_a_model_and_a_cap_in_any_order()
    {
        var options = Parse("--max-requests", "3", "--brain", "--model", "sonnet");
        Assert.True(options.Brain);
        Assert.Equal(BrainSettings.ResolveModel("sonnet"), options.Model);
        Assert.Equal(3, options.MaxRequests);

        Assert.Equal(BrainSettings.ResolveModel("opus"), Parse("--brain", "--model", BrainSettings.ResolveModel("opus")).Model);
    }

    [Fact]
    public void Fast_is_off_unless_asked_for_and_needs_a_model_that_takes_it()
    {
        Assert.False(Parse("--brain").Fast);
        Assert.False(Parse("--brain", "--model", "opus").Fast);
        Assert.True(Parse("--brain", "--fast", "--model", "opus").Fast);
        Assert.True(Parse("--model", "claude-opus-5-5-20261001", "--fast", "--brain").Fast);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Help(string arg) => Assert.True(Parse(arg).Help);

    [Theory]
    [InlineData("--model and --max-requests only apply with --brain", "--model", "sonnet")]
    [InlineData("--model and --max-requests only apply with --brain", "--max-requests", "2")]
    [InlineData("--model needs a value", "--brain", "--model")]
    [InlineData("--model needs a value", "--brain", "--model", "--max-requests", "2")]
    [InlineData("isn't a model the price table knows", "--brain", "--model", "gpt")]
    [InlineData("--max-requests takes a whole number from 1 to 50", "--brain", "--max-requests", "0")]
    [InlineData("--max-requests takes a whole number from 1 to 50", "--brain", "--max-requests", "51")]
    [InlineData("--max-requests takes a whole number from 1 to 50", "--brain", "--max-requests", "lots")]
    [InlineData("There's no --verbose option", "--brain", "--verbose")]
    [InlineData("--fast only applies with --brain", "--fast")]
    [InlineData("--fast only applies with --brain", "17773", "--bot", "--fast")]
    [InlineData("--fast needs a model that takes fast mode (Opus 5.5, Opus 5 or Opus 4.8), not claude-haiku-5-5: add --model opus.", "--brain", "--fast")]
    [InlineData("--fast needs a model that takes fast mode", "--brain", "--fast", "--model", "sonnet")]
    [InlineData("The port must be a number from 1 to 65535", "eighty")]
    [InlineData("The port must be a number from 1 to 65535", "70000")]
    [InlineData("Only the port can come without an option name", "--brain", "17773")]
    [InlineData("Only the port can come without an option name", "17773", "17774")]
    public void Bad_arguments_say_what_is_wrong(string expected, params string[] args)
    {
        var ex = Assert.Throws<UsageException>(() => DevHostOptions.Parse(args));
        Assert.Contains(expected, ex.Message);
    }
}

public class RequestCapTests
{
    private sealed class Echo : IClaudeApi
    {
        public int Calls;
        public Exception? Throw;

        public Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
        {
            Calls++;
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(new ClaudeResponse(new JsonObject { ["id"] = $"msg_{Calls}", ["model"] = request["model"]?.DeepClone() }));
        }
    }

    private static JsonObject Request() => new() { ["model"] = BrainSettings.ResolveModel("haiku") };

    [Fact]
    public async Task Passes_the_first_requests_then_refuses_the_rest()
    {
        var inner = new Echo();
        var seen = new List<string?>();
        var cap = new RequestCap(inner, 2, (_, response) => seen.Add(response.Id));

        await cap.SendAsync(Request(), CancellationToken.None);
        await cap.SendAsync(Request(), CancellationToken.None);
        Assert.False(cap.CapReached);

        var refused = await Assert.ThrowsAsync<ClaudeApiException>(() => cap.SendAsync(Request(), CancellationToken.None));
        Assert.Equal(ClaudeErrorKind.Other, refused.Kind);
        Assert.False(refused.NeedsHuman);
        Assert.Contains("cap of 2 requests", refused.Message);
        await Assert.ThrowsAsync<ClaudeApiException>(() => cap.SendAsync(Request(), CancellationToken.None));

        Assert.True(cap.CapReached);
        Assert.Equal(2, cap.Sent);
        Assert.Equal(2, inner.Calls);
        Assert.Equal(["msg_1", "msg_2"], seen);
        Assert.Equal(0, cap.Failed);
    }

    [Fact]
    public async Task A_failure_is_counted_reported_and_passed_on()
    {
        var error = new ClaudeApiException(ClaudeErrorKind.InvalidRequest, 400, "invalid_request_error", "bad", "req_1");
        var reported = new List<Exception>();
        var cap = new RequestCap(new Echo { Throw = error }, 3, onFailure: reported.Add);

        var thrown = await Assert.ThrowsAsync<ClaudeApiException>(() => cap.SendAsync(Request(), CancellationToken.None));

        Assert.Same(error, thrown);
        Assert.Equal([error], reported);
        Assert.Equal(1, cap.Failed);
        Assert.Equal(1, cap.Sent);
        Assert.False(cap.CapReached);
    }

    [Fact]
    public async Task A_cancelled_call_throws_before_it_counts()
    {
        var inner = new Echo();
        var cap = new RequestCap(inner, 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cap.SendAsync(Request(), cts.Token));

        Assert.Equal(0, cap.Sent);
        Assert.Equal(0, inner.Calls);
        await cap.SendAsync(Request(), CancellationToken.None);
    }

    [Fact]
    public void The_cap_is_at_least_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestCap(new Echo(), 0));
}

public class FightWatchTests
{
    [Fact]
    public void Screen_of_reads_a_snapshot_an_act_result_or_a_refusal()
    {
        Assert.Equal("combat", FightWatch.ScreenOf(new JsonObject { ["screen"] = "combat" }));
        Assert.Equal("rewards", FightWatch.ScreenOf(new JsonObject { ["done"] = "play:0>e0", ["next"] = new JsonObject { ["screen"] = "rewards" } }));
        Assert.Equal("rewards", FightWatch.ScreenOf(new JsonObject { ["error"] = "x", ["current"] = new JsonObject { ["screen"] = "rewards" } }));
        Assert.Null(FightWatch.ScreenOf(new JsonObject { ["said"] = "Hm." }));
        Assert.Null(FightWatch.ScreenOf(null));
    }

    [Fact]
    public async Task Winning_the_fake_fight_is_noticed_once()
    {
        var game = new FakeGame { ActionDelay = TimeSpan.FromMilliseconds(10) };
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner) { PollInterval = TimeSpan.FromMilliseconds(2), SettleTime = TimeSpan.FromMilliseconds(10) };
        var over = 0;
        var host = new BotToolsHost(new BotTools(game, waiter, versioner, new ChatHub()));
        var watch = new FightWatch(host, () => over++);
        Assert.Same(host.Specs, watch.Specs);

        // Bash and a Strike a turn: 14 damage against 40 HP.
        var version = (await watch.CallAsync("get_state", new JsonObject(), CancellationToken.None)).Payload["version"]!.GetValue<long>();
        for (var turn = 0; turn < 3; turn++)
            foreach (var id in new[] { "play:3>e0", "play:0>e0", "end_turn" })
            {
                var acted = await watch.CallAsync("act", new JsonObject { ["action_id"] = id, ["version"] = version }, CancellationToken.None);
                version = (acted.Payload["next"] ?? acted.Payload["current"])!["version"]!.GetValue<long>();
            }
        await watch.CallAsync("get_state", new JsonObject(), CancellationToken.None);

        Assert.True(watch.FightOver);
        Assert.Equal(1, over);
    }
}

public sealed class BrainSmokeTests : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);
    private readonly string _dir = Directory.CreateTempSubdirectory("spiremind-smoke-test-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    /// <summary>Hands out scripted responses in order; anything after the script fails the call.</summary>
    private sealed class Script : IClaudeApi
    {
        private readonly ConcurrentQueue<Func<ClaudeResponse>> _steps = new();
        public List<JsonObject> Requests { get; } = [];

        public Script Then(ClaudeResponse response)
        {
            _steps.Enqueue(() => response);
            return this;
        }

        /// <summary>A response made from the request it answers, as a model reads the snapshot's version.</summary>
        public Script Then(Func<JsonObject, ClaudeResponse> respond)
        {
            _steps.Enqueue(() => respond(Requests[^1]));
            return this;
        }

        public Script ThenThrow(Exception ex)
        {
            _steps.Enqueue(() => throw ex);
            return this;
        }

        public Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
        {
            lock (Requests) Requests.Add((JsonObject)request.DeepClone());
            return _steps.TryDequeue(out var step)
                ? Task.FromResult(step())
                : throw new InvalidOperationException("The script has no more responses.");
        }
    }

    private BrainSmoke Smoke(StringWriter output, int maxRequests = 6, params string[] more)
    {
        var prompt = Path.Combine(_dir, "repo");
        Directory.CreateDirectory(Path.Combine(prompt, ".claude", "commands"));
        File.WriteAllText(Path.Combine(prompt, PromptBuilder.PlayPrompt), "Play the fight well.");
        return new BrainSmoke
        {
            Settings = new BrainSettings { ConfigDir = Path.Combine(_dir, "config"), Model = "opus" },
            Options = DevHostOptions.Parse(["--brain", "--max-requests", maxRequests.ToString(), .. more]),
            PromptFolder = prompt,
            NotesFolder = Path.Combine(_dir, "notes"),
            Output = output,
            Timeout = Guard,
            ActionDelay = TimeSpan.FromMilliseconds(10),
            PollInterval = TimeSpan.FromMilliseconds(2),
            SettleTime = TimeSpan.FromMilliseconds(10),
        };
    }

    // One turn of the fake fight as one act_sequence on the latest snapshot the request shows:
    // Bash and a Strike (14 damage), then end the turn.
    private static Func<JsonObject, ClaudeResponse> Turn(int n, long cacheRead, long cacheWrite, string? speed = null) => request =>
    {
        var response = Turn(n, cacheRead, cacheWrite, request);
        if (speed is not null)
        {
            response.Raw["model"] = BrainSettings.ResolveModel("opus");
            response.Raw["usage"]!["speed"] = speed;
        }
        return response;
    };

    private static ClaudeResponse Turn(int n, long cacheRead, long cacheWrite, JsonObject request) =>
        new(new JsonObject
        {
            ["id"] = $"msg_{n}",
            ["model"] = BrainSettings.ResolveModel("haiku"),
            ["stop_reason"] = "tool_use",
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = "Bash, then a Strike." },
                new JsonObject
                {
                    ["type"] = "tool_use", ["id"] = $"t{n}", ["name"] = "act_sequence",
                    ["input"] = new JsonObject
                    {
                        ["version"] = LatestVersion(request),
                        ["actions"] = new JsonArray("play:3>e0", "play:0>e0", "end_turn"),
                    },
                }),
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = 100, ["output_tokens"] = 50,
                ["cache_read_input_tokens"] = cacheRead, ["cache_creation_input_tokens"] = cacheWrite,
            },
        });

    /// <summary>The version of the last snapshot in the request's messages (the state it opened
    /// with, or the latest tool result's), written as JSON inside a text block.</summary>
    private static long LatestVersion(JsonObject request)
    {
        var texts = request["messages"]!.AsArray()
            .SelectMany(m => m!["content"] as JsonArray ?? [])
            .Select(block => block?["text"] ?? block?["content"])
            .Select(text => text is JsonValue v && v.TryGetValue<string>(out var t) ? t : text?.ToJsonString() ?? "");
        return long.Parse(Regex.Matches(string.Join("\n", texts), @"""version"":(\d+)")[^1].Groups[1].Value);
    }

    [Fact]
    public async Task Winning_the_fight_passes_and_stops_before_another_request()
    {
        var api = new Script().Then(Turn(1, 0, 5000)).Then(Turn(2, 5000, 300)).Then(Turn(3, 5300, 300));
        var output = new StringWriter();

        var code = await Smoke(output).RunAsync(api, CancellationToken.None).WaitAsync(Guard);

        var text = output.ToString();
        Assert.True(code == 0, text);
        Assert.Equal(3, api.Requests.Count);
        Assert.Contains("The fake fight is won", text);
        Assert.Contains("PASSED", text);
        // Every request, its tool calls and the totals.
        Assert.Contains($"#1 {BrainSettings.ResolveModel("haiku")}, tool_use: in 100, cache read 0, cache write 5,000 (1h 0), out 50, $", text);
        Assert.Matches(@"     act_sequence \{""version"":\d+,""actions"":\[""play:3>e0"",""play:0>e0"",""end_turn""\]\}", text);
        Assert.Contains("     text: Bash, then a Strike.", text);
        Assert.Contains("Total: 3 responses, in 300, cache read 10,300, cache write 5,600 (1h 0), out 150, $", text);
        Assert.DoesNotContain("Warning", text);
        // The model is the one from the options, never the one in the settings.
        Assert.All(api.Requests, r => Assert.Equal(BrainSettings.ResolveModel("haiku"), r["model"]!.GetValue<string>()));
        // The spend counts, and the notes and transcript stay in the given folder.
        Assert.Equal(3, new SpendTracker(Path.Combine(_dir, "config")).MonthCalls(DateTime.UtcNow));
        Assert.True(Directory.Exists(Path.Combine(_dir, "notes", "transcripts")));
    }

    [Fact]
    public async Task With_fast_the_requests_ask_for_fast_mode_and_the_report_shows_the_speed_and_the_fast_price()
    {
        var api = new Script().Then(Turn(1, 0, 5000, "fast")).Then(Turn(2, 5000, 300, "fast")).Then(Turn(3, 5300, 300, "fast"));
        var output = new StringWriter();

        var code = await Smoke(output, 6, "--fast", "--model", "opus").RunAsync(api, CancellationToken.None).WaitAsync(Guard);

        var text = output.ToString();
        Assert.True(code == 0, text);
        Assert.All(api.Requests, r =>
        {
            Assert.Equal("fast", r["speed"]!.GetValue<string>());
            Assert.Equal(ModelPrices.FastModeBeta, Assert.Single(r[ClaudeClient.BetasField]!.AsArray())!.GetValue<string>());
        });
        Assert.Contains("Brain smoke test on the fake fight: claude-opus-5-5, effort medium, fast mode (twice the price), at most 6 requests.", text);
        // 100 in at $8, 5000 written (5 minutes) at $10, 50 out at $40 per million.
        Assert.Contains("#1 claude-opus-5-5, tool_use, speed fast: in 100, cache read 0, cache write 5,000 (1h 0), out 50, $0.0528", text);
        Assert.Contains("Fast mode: all 3 responses say they ran fast.", text);
        Assert.DoesNotContain("brain: claude-opus-5-5 fast in", text); // the brain's own per-response line stays out
        Assert.Contains("PASSED", text);
        // Without --fast nothing about speed is sent or printed.
        var plain = new Script().Then(Turn(1, 0, 5000)).Then(Turn(2, 5000, 300)).Then(Turn(3, 5300, 300));
        var plainOutput = new StringWriter();
        await Smoke(plainOutput).RunAsync(plain, CancellationToken.None).WaitAsync(Guard);
        Assert.All(plain.Requests, r => Assert.False(r.ContainsKey("speed") || r.ContainsKey(ClaudeClient.BetasField)));
        Assert.DoesNotContain("speed", plainOutput.ToString());
        Assert.DoesNotContain("ast mode", plainOutput.ToString());
    }

    [Fact]
    public async Task With_fast_a_key_without_fast_mode_fails_the_test_and_the_fight_goes_on_at_standard_speed()
    {
        var api = new Script()
            .ThenThrow(new ClaudeApiException(ClaudeErrorKind.InvalidRequest, 400, "invalid_request_error", "speed: Extra inputs are not permitted", "req_1"))
            .Then(Turn(1, 0, 5000, "standard")).Then(Turn(2, 5000, 300, "standard")).Then(Turn(3, 5300, 300, "standard"));
        var output = new StringWriter();

        var code = await Smoke(output, 6, "--fast", "--model", "opus").RunAsync(api, CancellationToken.None).WaitAsync(Guard);

        var text = output.ToString();
        Assert.True(code == 1, text);
        Assert.Equal(["fast", null, null, null], api.Requests.Select(r => r["speed"]?.GetValue<string>()));
        Assert.Contains("#1 failed: ClaudeApiException", text);
        Assert.Contains("speed: Extra inputs are not permitted", text);
        Assert.Contains(ApiBrain.FastRefused, text);
        Assert.Contains("The fake fight is won", text);
        Assert.Contains("1 of 4 requests failed", text);
        Assert.Contains("Fast mode: only 0 of 3 responses say they ran fast", text);
        Assert.Contains("FAILED", text);
    }

    [Fact]
    public async Task Stopping_at_the_cap_passes()
    {
        var api = new Script().Then(Turn(1, 0, 5000)).Then(Turn(2, 5000, 300)).Then(Turn(3, 5300, 300));
        var output = new StringWriter();

        var code = await Smoke(output, maxRequests: 2).RunAsync(api, CancellationToken.None).WaitAsync(Guard);

        var text = output.ToString();
        Assert.True(code == 0, text);
        Assert.Equal(2, api.Requests.Count);
        Assert.Contains("Stopped at the cap of 2 requests, as planned.", text);
        // The brain's own lines about the refusal aren't shown.
        Assert.DoesNotContain("rejected", text);
        Assert.Contains("PASSED", text);
    }

    [Fact]
    public async Task A_rejected_request_fails()
    {
        var api = new Script()
            .Then(Turn(1, 0, 5000))
            .ThenThrow(new ClaudeApiException(ClaudeErrorKind.InvalidRequest, 400, "invalid_request_error", "tools.0.input_schema: bad", "req_2"));
        var output = new StringWriter();

        var code = await Smoke(output).RunAsync(api, CancellationToken.None).WaitAsync(Guard);

        var text = output.ToString();
        Assert.True(code == 1, text);
        Assert.Contains("#2 failed: ClaudeApiException", text);
        Assert.Contains("tools.0.input_schema: bad", text);
        Assert.Contains("The brain stopped by itself (Error)", text);
        Assert.Contains("1 of 2 requests failed", text);
        Assert.Contains("FAILED", text);
    }

    [Fact]
    public async Task A_cold_cache_after_the_first_request_is_flagged()
    {
        var api = new Script().Then(Turn(1, 0, 5000)).Then(Turn(2, 0, 5300));
        var output = new StringWriter();

        var code = await Smoke(output, maxRequests: 2).RunAsync(api, CancellationToken.None).WaitAsync(Guard);

        var text = output.ToString();
        Assert.True(code == 0, text);
        Assert.Contains("Warning: request #2 read nothing from the cache", text);
    }

    [Fact]
    public async Task Stopping_it_early_fails()
    {
        // Never answers, so only the stop ends it.
        var api = new Hang();
        var output = new StringWriter();
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var code = await Smoke(output).RunAsync(api, stop.Token).WaitAsync(Guard);

        Assert.Equal(1, code);
        Assert.Contains("Stopped with Ctrl+C.", output.ToString());
    }

    private sealed class Hang : IClaudeApi
    {
        public async Task<ClaudeResponse> SendAsync(JsonObject request, CancellationToken ct)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }
}
