using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>
/// /fast from the chat box to the wire and back into the spend, with nothing faked in between:
/// a real <see cref="ChatHub"/>, a real <see cref="BrainController"/> over a config.json and an
/// api_key on disk, the real <see cref="ApiBrain"/> and the real <see cref="ClaudeClient"/>, wired
/// as the mod wires them (BrainWiring), over a scripted HTTP transport. The prices are worked out
/// by hand below, not taken from the mod's tables.
/// </summary>
public class FastModeEndToEndTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    // Claude Opus 5.5, dollars per million tokens: standard $4 input, $5 and $8 for 5 minute and
    // 1 hour cache writes, $0.20 cache reads, $20 output; fast mode $8, $10, $16, $0.40 and $40.
    //
    // Usage A: 12,345 input, 2,000 written for 5 minutes, 30,000 written for 1 hour, 45,678 read, 1,234 output.
    //   standard: 49,380 + 10,000 + 240,000 + 9,135.6 + 24,680 = 333,195.6 millionths of a dollar
    //   fast:     98,760 + 20,000 + 480,000 + 18,271.2 + 49,360 = 666,391.2
    private const decimal StandardA = 0.3331956m;
    private const decimal FastA = 0.6663912m;

    // Usage B: 1,000 input, 80,000 read, 2,000 output.
    //   standard: 4,000 + 16,000 + 40,000 = 60,000; fast: 8,000 + 32,000 + 80,000 = 120,000
    private const decimal StandardB = 0.06m;
    private const decimal FastB = 0.12m;

    private static JsonObject UsageA(string? speed) => Usage(12_345, 1_234, 45_678, 2_000, 30_000, speed);

    private static JsonObject UsageB(string? speed) => Usage(1_000, 2_000, 80_000, 0, 0, speed);

    private static JsonObject Usage(int input, int output, int read, int write5m, int write1h, string? speed)
    {
        var usage = new JsonObject
        {
            ["input_tokens"] = input,
            ["output_tokens"] = output,
            ["cache_read_input_tokens"] = read,
            ["cache_creation_input_tokens"] = write5m + write1h,
            ["cache_creation"] = new JsonObject { ["ephemeral_5m_input_tokens"] = write5m, ["ephemeral_1h_input_tokens"] = write1h },
        };
        if (speed is not null)
            usage["speed"] = speed;
        return usage;
    }

    private const string FastPitch = "answers come up to 2.5x faster at twice the price ($8 / $40 per million tokens on Opus 5.5)";

    [Fact]
    public async Task Typed_in_the_chat_fast_mode_goes_on_the_wire_and_into_the_spend_at_the_fast_price()
    {
        using var bench = new Bench();
        await bench.Init();
        string? on = null, off = null, budgetOn = null;
        bench.Wire
            .Then(_ =>
            {
                on = bench.Post("/fast on"); // typed while the first request is out
                return Wire.Ok(UsageA("standard"));
            })
            .Then(_ => Wire.Ok(UsageA("fast")))
            .Then(_ =>
            {
                budgetOn = bench.Post("/budget");
                off = bench.Post("/fast off");
                return Wire.Ok(UsageB("fast"));
            })
            .Then(_ => Wire.Ok(UsageB("standard")));

        Assert.StartsWith("Ironclad is playing on your API credit (Opus 5.5, high effort, $0.00 of $200", bench.Post("/start"));
        await bench.Wire.Exhausted.Task.WaitAsync(Guard);
        var sent = bench.Wire.Sent;

        Assert.Equal($"Fast mode on: {FastPitch}. /fast off to go back.", on);
        Assert.Equal("Fast mode off: back to normal speed and price.", off);

        // Off: no speed anywhere in the body, and no beta header at all.
        Assert.DoesNotContain("speed", sent[0].Body);
        Assert.Empty(sent[0].BetaHeaders);
        // On, from the very next request: the top-level field, and the flag once in one header.
        foreach (var fast in new[] { sent[1], sent[2] })
        {
            Assert.Equal("fast", fast.Json["speed"]!.GetValue<string>());
            Assert.Equal(["fast-mode-2026-02-01"], fast.BetaHeaders);
            Assert.DoesNotContain("anthropic-beta", fast.Body);
            Assert.DoesNotContain("fast-mode", fast.Body);
            // Nothing else was added to the request: the keys of the standard one, then speed.
            Assert.Equal(sent[0].Json.Select(p => p.Key).Append("speed"), fast.Json.Select(p => p.Key));
        }
        // Off again, from the next request, in the same conversation.
        foreach (var standard in new[] { sent[3], sent[4] })
        {
            Assert.DoesNotContain("speed", standard.Body);
            Assert.Empty(standard.BetaHeaders);
        }
        Assert.Equal([1, 3, 5, 7, 9], sent.Select(s => s.Json["messages"]!.AsArray().Count));

        // The money: each response at the price of the speed it reports.
        Assert.Equal(StandardA + FastA + FastB + StandardB, bench.Spend.RunTotal);
        Assert.Equal(1.1795868m, bench.Spend.MonthTotal(DateTime.UtcNow));
        Assert.Contains("Opus 5.5, high effort, fast mode.\n", budgetOn);
        Assert.StartsWith($"API spend: $1.00 of $200 this month, $1.00 this run in 2 requests.", budgetOn); // 0.3331956 + 0.6663912
        var budget = bench.Post("/budget")!;
        Assert.StartsWith("API spend: $1.18 of $200 this month, $1.18 this run in 4 requests. Opus 5.5, high effort.\n", budget);

        // Nothing about it was written to config.json, and nobody was told anything went wrong.
        Assert.DoesNotContain("fast", File.ReadAllText(bench.ConfigPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(bench.Lines, l => l.Contains("back to normal speed."));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_monthly_cap_is_checked_against_what_each_speed_costs(bool fast)
    {
        // A $1 cap. Usage A costs $0.67 in fast mode and $0.33 at standard speed, so the cap is
        // reached after two fast responses, or after four standard ones.
        using var bench = new Bench("""{"monthly_budget_usd": 1}""");
        await bench.Init();
        for (var i = 0; i < 6; i++)
            bench.Wire.Then(_ => Wire.Ok(UsageA(fast ? "fast" : "standard")));
        if (fast)
            bench.Post("/fast on");

        bench.Post("/start");
        var line = await bench.WaitForLine("Monthly API budget reached");

        Assert.Equal(fast ? 2 : 4, bench.Wire.Sent.Length);
        Assert.All(bench.Wire.Sent, s => Assert.Equal(fast ? "fast" : null, s.Json["speed"]?.GetValue<string>()));
        Assert.Equal(fast ? 2 * FastA : 4 * StandardA, bench.Spend.MonthTotal(DateTime.UtcNow));
        Assert.Equal($"Monthly API budget reached: $1.33 of $1.00 spent this month, so the API brain stopped.", line);
    }

    private const string RateLimitBody = """{"type":"error","error":{"type":"rate_limit_error","message":"This request would exceed your organization's rate limit for fast mode."}}""";

    [Fact]
    public async Task A_rate_limit_that_lasts_sends_the_same_request_at_standard_speed_and_the_chat_says_so_once()
    {
        using var bench = new Bench();
        await bench.Init();
        bench.Post("/fast on");
        bench.Wire
            .Then(_ => Wire.Ok(UsageB("fast")))
            // The second request: 429 on every try the client makes.
            .Then(_ => (429, RateLimitBody, "3")).Then(_ => (429, RateLimitBody, "3")).Then(_ => (429, RateLimitBody, "3"))
            .Then(_ => (429, RateLimitBody, "3")).Then(_ => (429, RateLimitBody, "3"))
            .Then(_ => Wire.Ok(UsageB("standard")))
            .Then(_ => Wire.Ok(UsageB("standard")));

        bench.Post("/start");
        await bench.Wire.Exhausted.Task.WaitAsync(Guard);
        var sent = bench.Wire.Sent;

        // Five tries in fast mode, the API's 3 seconds waited before each retry.
        Assert.Equal(9, sent.Length);
        Assert.All(sent[1..6], s => Assert.Equal(sent[1].Body, s.Body));
        Assert.Equal("fast", sent[1].Json["speed"]!.GetValue<string>());
        Assert.Equal([3, 3, 3, 3], bench.ClientWaits.Select(w => (int)w.TotalSeconds));
        // Then the very same request without the speed field and without the flag: the turn isn't lost.
        var again = (JsonObject)sent[1].Json.DeepClone();
        again.Remove("speed");
        Assert.Equal(again.ToJsonString(), sent[6].Body);
        Assert.Empty(sent[6].BetaHeaders);
        // The brain keeps playing at standard speed, in the same conversation.
        Assert.All(sent[6..], s => Assert.DoesNotContain("speed", s.Body));
        Assert.Equal([3, 5, 7], sent[6..].Select(s => s.Json["messages"]!.AsArray().Count));
        Assert.Equal(BrainKind.Api, bench.Controller.Playing);
        Assert.True(Assert.Single(bench.Brains).IsRunning);

        // Said once, and /fast and /budget say what is true now.
        Assert.Single(bench.Lines, l => l == "Fast mode hit its rate limit; back to normal speed.");
        Assert.DoesNotContain(bench.Lines, l => l.Contains("rate limiting your API key"));
        Assert.False(bench.Controller.Settings.Fast);
        Assert.Equal($"Fast mode is off. /fast on makes {FastPitch}.", bench.Post("/fast"));
        Assert.DoesNotContain("fast mode", bench.Post("/budget"));
        // One fast response and two standard ones were billed; the five 429s cost nothing.
        Assert.Equal(FastB + 2 * StandardB, bench.Spend.RunTotal);
    }

    [Fact]
    public async Task A_rate_limit_that_clears_on_the_clients_retry_stays_in_fast_mode_and_says_nothing()
    {
        using var bench = new Bench();
        await bench.Init();
        bench.Post("/fast on");
        bench.Wire
            .Then(_ => Wire.Ok(UsageB("fast")))
            .Then(_ => (429, RateLimitBody, "2"))
            .Then(_ => Wire.Ok(UsageB("fast")))
            .Then(_ => Wire.Ok(UsageB("fast")));

        bench.Post("/start");
        await bench.Wire.Exhausted.Task.WaitAsync(Guard);
        var sent = bench.Wire.Sent;

        Assert.Equal(5, sent.Length);
        Assert.Equal(sent[1].Body, sent[2].Body); // the retry is the same fast request
        Assert.All(sent, s => Assert.Equal("fast", s.Json["speed"]!.GetValue<string>()));
        Assert.All(sent, s => Assert.Equal(["fast-mode-2026-02-01"], s.BetaHeaders));
        Assert.Equal([2], bench.ClientWaits.Select(w => (int)w.TotalSeconds));
        Assert.True(bench.Controller.Settings.Fast);
        Assert.DoesNotContain(bench.Lines, l => l.Contains("back to normal speed") || l.Contains("retrying"));
        Assert.Equal(3 * FastB, bench.Spend.RunTotal);
        Assert.Equal([1, 3, 3, 5, 7], sent.Select(s => s.Json["messages"]!.AsArray().Count));
    }

    [Theory]
    [InlineData(false)] // the very first fast request
    [InlineData(true)] // after fast mode has worked
    public async Task A_400_that_names_the_speed_field_falls_back_once_and_the_brain_keeps_playing(bool worked)
    {
        using var bench = new Bench();
        await bench.Init();
        bench.Post("/fast on");
        if (worked)
            bench.Wire.Then(_ => Wire.Ok(UsageB("fast")));
        bench.Wire
            .Then(_ => (400, """{"type":"error","error":{"type":"invalid_request_error","message":"speed: Extra inputs are not permitted"}}""", null))
            .Then(_ => Wire.Ok(UsageB("standard")))
            .Then(_ => Wire.Ok(UsageB("standard")));

        bench.Post("/start");
        await bench.Wire.Exhausted.Task.WaitAsync(Guard);
        var sent = bench.Wire.Sent;
        var refused = worked ? 1 : 0;

        Assert.Equal("fast", sent[refused].Json["speed"]!.GetValue<string>());
        var again = (JsonObject)sent[refused].Json.DeepClone();
        again.Remove("speed");
        Assert.Equal(again.ToJsonString(), sent[refused + 1].Body);
        Assert.All(sent[(refused + 1)..], s => Assert.DoesNotContain("speed", s.Body));
        Assert.All(sent[(refused + 1)..], s => Assert.Empty(s.BetaHeaders));
        Assert.Single(bench.Lines, l => l.StartsWith("Claude turned down fast mode"));
        Assert.False(bench.Controller.Settings.Fast);
        Assert.Equal(BrainKind.Api, bench.Controller.Playing);
        Assert.True(Assert.Single(bench.Brains).IsRunning);
        Assert.DoesNotContain(bench.Lines, l => l.Contains("the API brain stopped"));
    }

    private const string OtherRefusal = """{"type":"error","error":{"type":"invalid_request_error","message":"tools.3.custom.input_schema: JSON schema is invalid"}}""";

    [Theory]
    [InlineData(false)] // the very first fast request
    [InlineData(true)] // after fast mode has worked
    public async Task A_400_about_something_else_stops_the_brain_as_ever_and_leaves_fast_mode_on(bool worked)
    {
        using var bench = new Bench();
        await bench.Init();
        bench.Post("/fast on");
        if (worked)
            bench.Wire.Then(_ => Wire.Ok(UsageB("fast")));
        // The request is wrong at any speed: the API says so however often it is asked.
        for (var i = 0; i < 4; i++)
            bench.Wire.Then(_ => (400, OtherRefusal, null));

        bench.Post("/start");
        await bench.WaitForLine("Claude rejected the brain's request, so the API brain stopped.");
        await bench.WaitUntil(() => bench.Controller.Playing == BrainKind.None);
        var sent = bench.Wire.Sent;

        // Nothing loops: fast mode that never worked gets one more try at standard speed, and
        // one that worked gets none.
        Assert.Equal(worked ? new[] { "fast", "fast" } : new[] { "fast", null }, sent.Select(s => s.Json["speed"]?.GetValue<string>()));
        // Fast mode wasn't what the API refused, so it isn't blamed and stays as it was set.
        Assert.DoesNotContain(bench.Lines, l => l.Contains("turned down fast mode") || l.Contains("back to normal speed"));
        Assert.True(bench.Controller.Settings.Fast);
        Assert.Equal($"Fast mode is on: {FastPitch}. /fast off to go back.", bench.Post("/fast"));
    }

    [Theory]
    [InlineData(400, "invalid_request_error", "Request not allowed")]
    [InlineData(403, "permission_error", "Your organization does not have access to this feature.")]
    [InlineData(404, "not_found_error", "Not found")]
    public async Task A_key_without_fast_mode_plays_on_at_standard_speed_whatever_the_refusal_says(int status, string type, string message)
    {
        using var bench = new Bench();
        await bench.Init();
        bench.Wire
            .Then(_ => Wire.Ok(UsageB("standard")))
            .Then(_ =>
            {
                bench.Post("/fast on");
                return Wire.Ok(UsageB("standard"));
            })
            .Then(_ => (status, new JsonObject { ["type"] = "error", ["error"] = new JsonObject { ["type"] = type, ["message"] = message } }.ToJsonString(), null))
            .Then(_ => Wire.Ok(UsageB("standard")))
            .Then(_ => Wire.Ok(UsageB("standard")));

        bench.Post("/start");
        await bench.Wire.Exhausted.Task.WaitAsync(Guard);
        var sent = bench.Wire.Sent;

        Assert.Equal([null, null, "fast", null, null, null], sent.Select(s => s.Json["speed"]?.GetValue<string>()));
        Assert.Equal([1, 3, 5, 5, 7, 9], sent.Select(s => s.Json["messages"]!.AsArray().Count));
        Assert.Single(bench.Lines, l => l.StartsWith("Claude turned down fast mode"));
        Assert.False(bench.Controller.Settings.Fast);
        Assert.Equal(BrainKind.Api, bench.Controller.Playing);
        Assert.True(Assert.Single(bench.Brains).IsRunning);
        Assert.Equal(4 * StandardB, bench.Spend.RunTotal);
    }

    [Fact]
    public async Task Nothing_but_the_command_switches_it_on_and_nothing_keeps_it_on_past_the_game()
    {
        // A config.json that asks for it in every spelling, and an environment that says yes to everything.
        const string config = """{"fast": true, "Fast": true, "speed": "fast", "fast_mode": true, "monthly_budget_usd": 200, "effort": "high"}""";
        using var bench = new Bench(config, env: name => name == BrainSettings.ApiKeyEnvVar ? null : "1");
        await bench.Init();
        Assert.False(bench.Controller.Settings.Fast);
        Assert.Contains(bench.Controller.Settings.Warnings, w => w.Contains("'fast' in config.json is ignored"));
        bench.Wire
            .Then(_ => Wire.Ok(UsageB("standard")))
            .Then(_ =>
            {
                bench.Post("/fast on");
                // Every command that reads config.json again and saves it.
                bench.Post("/effort medium");
                bench.Post("/effort boss xhigh");
                bench.Post("/model claude-opus-5-5-20261001");
                bench.Post("/model opus");
                return Wire.Ok(UsageB("standard"));
            })
            .Then(_ => Wire.Ok(UsageB("fast")));

        bench.Post("/start");
        await bench.Wire.Exhausted.Task.WaitAsync(Guard);

        // On for this game: the model and effort changes started a fresh conversation, still fast.
        Assert.True(bench.Controller.Settings.Fast);
        Assert.Equal([null, null, "fast", "fast"], bench.Wire.Sent.Select(s => s.Json["speed"]?.GetValue<string>()));
        // What the commands saved has the model and efforts, and not a word that /fast wrote.
        var saved = JsonNode.Parse(File.ReadAllText(bench.ConfigPath))!.AsObject();
        Assert.Equal("medium", saved["effort"]!.GetValue<string>());
        Assert.Equal("xhigh", saved["boss_effort"]!.GetValue<string>());
        Assert.True(saved["autostart"]!.GetValue<bool>());
        Assert.Equal(JsonNode.Parse(config)!.AsObject().Select(p => p.Key).Concat(["model", "boss_effort", "autostart"]).Order(), saved.Select(p => p.Key).Order());

        // The game closes and starts again: off, and the brain that autostart brings back asks for nothing.
        bench.Controller.Shutdown();
        using var next = new Bench(null, dir: bench.Dir);
        next.Wire.Then(_ => Wire.Ok(UsageB("standard")));
        await next.Init();
        await next.Wire.Exhausted.Task.WaitAsync(Guard);

        Assert.False(next.Controller.Settings.Fast);
        Assert.True(next.Controller.Settings.Autostart);
        Assert.All(next.Wire.Sent, s => Assert.DoesNotContain("speed", s.Body));
        Assert.All(next.Wire.Sent, s => Assert.Empty(s.BetaHeaders));
        Assert.Equal($"Fast mode is off. /fast on makes {FastPitch}.", next.Post("/fast"));
    }

    [Fact]
    public async Task A_bot_copy_starts_at_standard_speed_takes_the_command_and_is_off_again_after_a_relaunch()
    {
        var shared = Path.Combine(Path.GetTempPath(), "spiremind-e2e-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shared);
        try
        {
            File.WriteAllText(Path.Combine(shared, BrainSettings.ConfigFileName), """{"fast": true, "monthly_budget_usd": 200}""");
            File.WriteAllText(Path.Combine(shared, BrainSettings.ApiKeyFileName), Bench.FakeKey + "\n");
            var own = Path.Combine(shared, "bot-17773");

            // The bot copy plays by itself from its launch (autostart is forced), with no /start.
            using var bot = new Bench("""{"effort": "high"}""", dir: own, sharedDir: shared);
            bot.Wire
                .Then(_ => Wire.Ok(UsageB("standard")))
                .Then(_ =>
                {
                    // What the relay posts to a bot copy when Mark types /fast on in his window.
                    bot.Post("/fast on");
                    return Wire.Ok(UsageB("standard"));
                })
                .Then(_ => Wire.Ok(UsageB("fast")));
            await bot.Init();
            await bot.Wire.Exhausted.Task.WaitAsync(Guard);

            Assert.Equal([null, null, "fast", "fast"], bot.Wire.Sent.Select(s => s.Json["speed"]?.GetValue<string>()));
            Assert.True(bot.Controller.Settings.Fast);
            bot.Post("/effort low"); // a bot copy saves its own config.json
            Assert.Equal("""{"fast": true, "monthly_budget_usd": 200}""", File.ReadAllText(Path.Combine(shared, BrainSettings.ConfigFileName)));
            var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(own, BrainSettings.ConfigFileName)))!.AsObject();
            Assert.Equal(["autostart", "effort", "model"], saved.Select(p => p.Key).Order());
            bot.Controller.Shutdown();

            // Relaunched: normal speed until /fast on is typed again.
            using var again = new Bench(null, dir: own, sharedDir: shared);
            again.Wire.Then(_ => Wire.Ok(UsageB("standard")));
            await again.Init();
            await again.Wire.Exhausted.Task.WaitAsync(Guard);

            Assert.False(again.Controller.Settings.Fast);
            Assert.All(again.Wire.Sent, s => Assert.DoesNotContain("speed", s.Body));
            Assert.All(again.Wire.Sent, s => Assert.Empty(s.BetaHeaders));
        }
        finally
        {
            try { Directory.Delete(shared, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }
}

/// <summary>The mod's brain wiring over a temp folder and a scripted transport.</summary>
file sealed class Bench : IDisposable
{
    public const string FakeKey = "sk-ant-api03-test-key";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    private readonly bool _ownsDir;
    private readonly ConcurrentQueue<TimeSpan> _clientWaits = new();
    private readonly ConcurrentQueue<string> _logged = new();
    private readonly ConcurrentQueue<ApiBrain> _brains = new();
    private readonly TaskCompletionSource _polled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <param name="config">config.json's text; null leaves the folder's file as it is.</param>
    /// <param name="dir">This copy's config folder; a fresh temp folder when null.</param>
    /// <param name="sharedDir">For a bot copy: the folder every copy shares (its key and spend).</param>
    /// <param name="env">The environment the settings are read with; nothing is set when null.</param>
    public Bench(string? config = """{"monthly_budget_usd": 200, "effort": "high"}""", string? dir = null, string? sharedDir = null,
        Func<string, string?>? env = null)
    {
        _ownsDir = dir is null;
        Dir = dir ?? Path.Combine(Path.GetTempPath(), "spiremind-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Dir, ".claude", "commands"));
        File.WriteAllText(Path.Combine(Dir, ".claude", "commands", "play.md"), "---\ndescription: test\n---\nPlay: $ARGUMENTS\n");
        if (config is not null)
            File.WriteAllText(ConfigPath, config);
        if (sharedDir is null)
            File.WriteAllText(Path.Combine(Dir, BrainSettings.ApiKeyFileName), FakeKey + "\n");
        env ??= _ => null;

        Spend = new SpendTracker(sharedDir ?? Dir, instanceId: sharedDir is null ? null : "bot-17773");
        var snapshot = new JsonObject
        {
            ["version"] = 1, ["screen"] = "combat", ["awaiting_decision"] = true,
            ["state"] = new JsonObject(), ["actions"] = new JsonArray(),
        };
        var host = new BenchHost(snapshot);
        BrainController? controller = null;
        controller = new BrainController(
            loadSettings: () => BrainSettings.Load(Dir, env, sharedDir),
            saveSettings: settings => settings.Save(),
            createApi: settings =>
            {
                var client = new ClaudeClient(settings.ApiKey!, Wire, "http://localhost:9999/",
                    delay: (wait, _) => { _clientWaits.Enqueue(wait); return Task.CompletedTask; }, random: () => 0);
                var brain = new ApiBrain(client, host, new NotesTools(Dir), Dir, () => controller!.Settings, Spend,
                    character: () => Chat.BotName,
                    announce: line => Chat.Announce(line),
                    log: _logged.Enqueue,
                    delay: async (_, ct) => { ct.ThrowIfCancellationRequested(); await Task.Yield(); },
                    fastDeclined: () => controller!.FastDeclined());
                _brains.Enqueue(brain);
                return brain;
            },
            createSubscription: _ => new IdleBrain(),
            currentScreen: _ => Task.FromResult<string?>("combat"),
            spend: Spend,
            chat: Chat,
            log: _logged.Enqueue,
            // One round of the poll loop (it sees the run in progress), then it waits for the end.
            delay: async (_, ct) =>
            {
                _polled.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
            })
        {
            ForceAutostart = sharedDir is not null,
        };
        Controller = controller;
    }

    public string Dir { get; }
    public string ConfigPath => Path.Combine(Dir, BrainSettings.ConfigFileName);
    public Wire Wire { get; } = new();
    public ChatHub Chat { get; } = new() { BotName = "Ironclad" };
    public SpendTracker Spend { get; }
    public BrainController Controller { get; }

    public ApiBrain[] Brains => _brains.ToArray();
    public TimeSpan[] ClientWaits => _clientWaits.ToArray();
    public string[] Logged => _logged.ToArray();

    /// <summary>Every line of the chat log: what was typed, the replies and what the brain announced.</summary>
    public string[] Lines => Chat.LinesSince(0).Select(l => l.Text).ToArray();

    /// <summary>Mod load: the controller starts its poll loop, which sees the run once.</summary>
    public async Task Init()
    {
        Controller.Initialize();
        await _polled.Task.WaitAsync(Guard);
    }

    /// <summary>A line typed in the chat box; the command's reply.</summary>
    public string? Post(string text) => Chat.PostFromHuman("Mark", text).Reply;

    public async Task<string> WaitForLine(string start)
    {
        string? found = null;
        await WaitUntil(() => (found = Lines.FirstOrDefault(l => l.StartsWith(start, StringComparison.Ordinal))) is not null);
        return found!;
    }

    public async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("timed out waiting; the chat has:\n" + string.Join("\n", Lines) + "\nand the log:\n" + string.Join("\n", Logged));
            await Task.Delay(5);
        }
    }

    public void Dispose()
    {
        Controller.Shutdown();
        if (!_ownsDir)
            return;
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}

/// <summary>Game tools that always show the same fight.</summary>
file sealed class BenchHost(JsonObject snapshot) : IToolHost
{
    public IReadOnlyList<ToolSpec> Specs { get; } =
    [
        new("get_state", "State", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        new("wait_for_decision", "Wait", new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }),
        new("act", "Act", new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["action_id"] = new JsonObject { ["type"] = "string" } },
            ["required"] = new JsonArray("action_id"),
        }),
    ];

    public Task<ToolOutcome> CallAsync(string name, JsonObject args, CancellationToken ct) =>
        Task.FromResult(new ToolOutcome(snapshot.DeepClone()));
}

/// <summary>The subscription fallback, which these tests never play with.</summary>
file sealed class IdleBrain : IPlayBrain
{
    public bool IsRunning { get; private set; }
    public bool? LastSeenInRun => null;
    public bool Start() => IsRunning = true;

    public Task StopAsync()
    {
        Abort();
        return Task.CompletedTask;
    }

    public void Abort()
    {
        IsRunning = false;
        Stopped?.Invoke(BrainStopReason.UserStop, "stopped");
    }

    public event Action<BrainStopReason, string>? Stopped;
}

/// <summary>One request as it went over the wire: its body, and every value of every
/// anthropic-beta header it carried (a flag sent in two headers would show as two values).</summary>
file sealed record WireRequest(string Body, string[] BetaHeaders)
{
    public JsonObject Json { get; } = (JsonObject)JsonNode.Parse(Body)!;
}

/// <summary>A scripted HTTP transport: each request gets the next step's status, body and
/// retry-after. A step runs while its request is out, so it can type in the chat meanwhile.
/// Once the script runs out, a request is recorded and held until the brain stops.</summary>
file sealed class Wire : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly Queue<Func<WireRequest, (int Status, string Body, string? RetryAfter)>> _script = new();
    private readonly List<WireRequest> _sent = [];
    private static int _ids;

    public TaskCompletionSource Exhausted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public WireRequest[] Sent
    {
        get { lock (_gate) return _sent.ToArray(); }
    }

    public Wire Then(Func<WireRequest, (int Status, string Body, string? RetryAfter)> step)
    {
        lock (_gate) _script.Enqueue(step);
        return this;
    }

    /// <summary>A 200 from Claude Opus 5.5 that calls get_state, so the brain asks again at once.</summary>
    public static (int Status, string Body, string? RetryAfter) Ok(JsonObject usage) => (200, new JsonObject
    {
        ["id"] = "msg_" + Interlocked.Increment(ref _ids),
        ["type"] = "message",
        ["role"] = "assistant",
        ["model"] = "claude-opus-5-5",
        ["content"] = new JsonArray(new JsonObject
        {
            ["type"] = "tool_use", ["id"] = "toolu_" + Interlocked.Increment(ref _ids), ["name"] = "get_state", ["input"] = new JsonObject(),
        }),
        ["stop_reason"] = "tool_use",
        ["usage"] = usage,
    }.ToJsonString(), null);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var betas = request.Headers.TryGetValues("anthropic-beta", out var values) ? values.ToArray() : [];
        var sent = new WireRequest(body, betas);
        Func<WireRequest, (int Status, string Body, string? RetryAfter)>? step;
        lock (_gate)
        {
            _sent.Add(sent);
            step = _script.Count > 0 ? _script.Dequeue() : null;
        }
        if (step is null)
        {
            Exhausted.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
        }
        var (status, text, retryAfter) = step!(sent);
        var reply = new HttpResponseMessage((System.Net.HttpStatusCode)status)
        {
            Content = new StringContent(text, Encoding.UTF8, "application/json"),
        };
        if (retryAfter is not null)
            reply.Headers.TryAddWithoutValidation("retry-after", retryAfter);
        return reply;
    }
}
