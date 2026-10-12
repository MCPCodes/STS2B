using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

public class ModelPricesTests
{
    // 10k fresh input, 20k 5 minute writes, 30k 1 hour writes, 40k cache reads, 5k output:
    // a 100k prompt, right at Haiku's long-prompt line but not over it.
    private static JsonObject Usage(long input = 10_000) => new()
    {
        ["input_tokens"] = input,
        ["output_tokens"] = 5_000,
        ["cache_read_input_tokens"] = 40_000,
        ["cache_creation_input_tokens"] = 50_000,
        ["cache_creation"] = new JsonObject
        {
            ["ephemeral_5m_input_tokens"] = 20_000,
            ["ephemeral_1h_input_tokens"] = 30_000,
        },
    };

    [Theory]
    // in*4 + 5m*5 + 1h*8 + read*0.20 + out*20 (per million)
    [InlineData("claude-opus-5-5", "0.488")]
    // in*2 + 5m*2.5 + 1h*4 + read*0.10 + out*10
    [InlineData("claude-sonnet-5-5", "0.244")]
    // in*0.10 + 5m*0.125 + 1h*0.20 + read*0.01 + out*0.50
    [InlineData("claude-haiku-5-5", "0.0124")]
    // in*10 + 5m*12.5 + 1h*20 + read*0.25 + out*50
    [InlineData("claude-fable-5-1", "1.21")]
    // in*10 + 5m*12.5 + 1h*20 + read*1.00 + out*50
    [InlineData("claude-fable-5", "1.24")]
    [InlineData("claude-mythos-5", "1.24")]
    public void Cost_per_model_with_both_cache_lifetimes(string model, string expected)
    {
        var cost = ModelPrices.Cost(model, Usage());
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), cost.Usd);
        Assert.True(cost.KnownModel);
        Assert.False(cost.LongPrompt);
        Assert.Equal(model, cost.PricedAs);
    }

    [Fact]
    public void One_hour_writes_cost_more_than_five_minute_ones()
    {
        var fiveMinutes = new TokenUsage(0, 0, 0, CacheWrite5m: 1_000_000, CacheWrite1h: 0);
        var oneHour = new TokenUsage(0, 0, 0, CacheWrite5m: 0, CacheWrite1h: 1_000_000);
        Assert.Equal(5m, ModelPrices.Cost("claude-opus-5-5", fiveMinutes).Usd);
        Assert.Equal(8m, ModelPrices.Cost("claude-opus-5-5", oneHour).Usd);
        Assert.Equal(4m, ModelPrices.Cost("claude-sonnet-5-5", oneHour).Usd);
        Assert.Equal(0.20m, ModelPrices.Cost("claude-haiku-5-5", new TokenUsage(0, 0, 0, 0, 100_000)).Usd * 10);
    }

    [Fact]
    public void Haiku_prompts_over_100k_cost_five_times_everything()
    {
        var over = ModelPrices.Cost("claude-haiku-5-5", Usage(input: 10_001));
        Assert.True(over.LongPrompt);
        Assert.Equal((0.0124m + 0.0000001m) * 5, over.Usd);

        var atLine = ModelPrices.Cost("claude-haiku-5-5", Usage(input: 10_000));
        Assert.False(atLine.LongPrompt);

        // The prompt counts reads and writes, not just fresh input.
        var mostlyCached = new TokenUsage(Input: 100, Output: 1_000, CacheRead: 150_000, CacheWrite5m: 0, CacheWrite1h: 0);
        var cost = ModelPrices.Cost("claude-haiku-5-5", mostlyCached);
        Assert.True(cost.LongPrompt);
        Assert.Equal((100 * 0.10m + 150_000 * 0.01m + 1_000 * 0.50m) * 5 / 1_000_000, cost.Usd);
    }

    [Fact]
    public void Other_models_have_no_long_prompt_tier()
    {
        var big = new TokenUsage(Input: 300_000, Output: 0, CacheRead: 0, CacheWrite5m: 0, CacheWrite1h: 0);
        var cost = ModelPrices.Cost("claude-opus-5-5", big);
        Assert.False(cost.LongPrompt);
        Assert.Equal(1.2m, cost.Usd);
    }

    [Fact]
    public void Writes_without_the_split_count_as_five_minute()
    {
        var usage = new JsonObject { ["input_tokens"] = 0, ["output_tokens"] = 0, ["cache_creation_input_tokens"] = 1_000 };
        Assert.Equal(new TokenUsage(0, 0, 0, CacheWrite5m: 1_000, CacheWrite1h: 0), TokenUsage.FromJson(usage));
        Assert.Equal(0.005m, ModelPrices.Cost("claude-opus-5-5", usage).Usd);
    }

    [Fact]
    public void Writes_the_split_misses_count_as_five_minute()
    {
        var usage = new JsonObject
        {
            ["cache_creation_input_tokens"] = 1_000,
            ["cache_creation"] = new JsonObject { ["ephemeral_1h_input_tokens"] = 600 },
        };
        Assert.Equal(new TokenUsage(0, 0, 0, CacheWrite5m: 400, CacheWrite1h: 600), TokenUsage.FromJson(usage));
    }

    [Fact]
    public void Unknown_models_are_priced_at_the_highest_rates_and_flagged()
    {
        // A later version of a listed model isn't a snapshot of it, and each rate is the table's
        // highest ($1 for cache reads), so no listed model's line comes out above the estimate.
        foreach (var model in new[] { "claude-opus-6", "claude-mythos-5-1", "claude-fable-5-2", "claude-opus-5", "gpt-5", null, "" })
        {
            var cost = ModelPrices.Cost(model, Usage());
            Assert.False(cost.KnownModel);
            Assert.Equal(ModelPrices.FallbackName, cost.PricedAs);
            Assert.Equal(1.24m, cost.Usd);
        }
        var fallback = ModelPrices.Fallback;
        Assert.Equal(ModelPrices.Table.Values.Max(p => p.Input), fallback.Input);
        Assert.Equal(ModelPrices.Table.Values.Max(p => p.CacheWrite5m), fallback.CacheWrite5m);
        Assert.Equal(ModelPrices.Table.Values.Max(p => p.CacheWrite1h), fallback.CacheWrite1h);
        Assert.Equal(ModelPrices.Table.Values.Max(p => p.CacheRead), fallback.CacheRead);
        Assert.Equal(ModelPrices.Table.Values.Max(p => p.Output), fallback.Output);
        Assert.Equal(1.00m, fallback.CacheRead);
        Assert.False(ModelPrices.IsKnown("claude-opus-6"));
        var price = ModelPrices.For("nope", out var known);
        Assert.False(known);
        Assert.Same(fallback, price);
    }

    [Theory]
    [InlineData("claude-sonnet-5-5-20260930", "claude-sonnet-5-5")]
    [InlineData("claude-opus-5-5@20261001", "claude-opus-5-5")]
    [InlineData("claude-opus-5-5[1m]", "claude-opus-5-5")]
    [InlineData("claude-fable-5-20260301", "claude-fable-5")]
    [InlineData("claude-fable-5-1-20261001", "claude-fable-5-1")]
    [InlineData("CLAUDE-HAIKU-5-5", "claude-haiku-5-5")]
    [InlineData("haiku", "claude-haiku-5-5")]
    [InlineData("fable", "claude-fable-5-1")]
    public void Snapshots_and_aliases_find_their_model(string model, string pricedAs)
    {
        var cost = ModelPrices.Cost(model, Usage());
        Assert.True(cost.KnownModel);
        Assert.Equal(pricedAs, cost.PricedAs);
    }

    // ---- Fast mode ----

    private static JsonObject FastUsage(string? speed = "fast")
    {
        var usage = Usage();
        if (speed is not null)
            usage["speed"] = speed;
        return usage;
    }

    [Theory]
    // in*8 + 5m*10 + 1h*16 + read*0.40 + out*40 (per million): twice the standard 0.488
    [InlineData("claude-opus-5-5", "0.976")]
    [InlineData("opus", "0.976")]
    [InlineData("claude-opus-5-5-20261001", "0.976")]
    // in*10 + 5m*12.5 + 1h*20 + read*1.00 + out*50
    [InlineData("claude-opus-5", "1.24")]
    [InlineData("claude-opus-5-20260601", "1.24")]
    [InlineData("claude-opus-4-8", "1.24")]
    [InlineData("claude-opus-4-8-20260801", "1.24")]
    public void A_fast_response_is_priced_from_the_fast_table(string model, string expected)
    {
        var cost = ModelPrices.Cost(model, FastUsage());
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), cost.Usd);
        Assert.True(cost.Fast);
        Assert.True(cost.KnownModel);
        Assert.False(cost.LongPrompt);
        Assert.StartsWith(cost.PricedAs, BrainSettings.ResolveModel(model));
        Assert.True(ModelPrices.SupportsFast(model));
    }

    [Theory]
    // Opus 5.5 at $8 input, $10 and $16 cache writes, $0.40 cache reads, $40 output.
    [InlineData("claude-opus-5-5", "8", "10", "16", "0.40", "40")]
    // Opus 5 and Opus 4.8 at $10, $12.50 and $20, $1.00, $50.
    [InlineData("claude-opus-5", "10", "12.5", "20", "1.00", "50")]
    [InlineData("claude-opus-4-8", "10", "12.5", "20", "1.00", "50")]
    public void Each_token_class_of_a_fast_response_has_its_fast_rate(string model, string input, string write5m, string write1h, string read, string output)
    {
        static decimal D(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        const long million = 1_000_000;
        decimal Fast(TokenUsage usage) => ModelPrices.Cost(model, usage, fast: true).Usd;

        Assert.Equal(D(input), Fast(new TokenUsage(Input: million, 0, 0, 0, 0)));
        Assert.Equal(D(output), Fast(new TokenUsage(0, Output: million, 0, 0, 0)));
        Assert.Equal(D(read), Fast(new TokenUsage(0, 0, CacheRead: million, 0, 0)));
        Assert.Equal(D(write5m), Fast(new TokenUsage(0, 0, 0, CacheWrite5m: million, 0)));
        Assert.Equal(D(write1h), Fast(new TokenUsage(0, 0, 0, 0, CacheWrite1h: million)));

        // The price list: input and output as the pricing page gives them, and the prompt caching
        // multipliers on top (1.25 and 2 times the input for writes; 0.1 for reads, 0.05 on Opus 5.5).
        var price = ModelPrices.FastFor(model)!;
        Assert.Equal(price.Input * 1.25m, price.CacheWrite5m);
        Assert.Equal(price.Input * 2m, price.CacheWrite1h);
        Assert.Equal(price.Input * (model == "claude-opus-5-5" ? 0.05m : 0.1m), price.CacheRead);
        Assert.Equal(price.Input * 5m, price.Output);
        Assert.Null(price.LongPromptThreshold);

        // The same from a usage object that says it ran fast, one class at a time.
        Assert.Equal(D(input) / 1000, ModelPrices.Cost(model, new JsonObject { ["input_tokens"] = 1000, ["speed"] = "fast" }).Usd);
        Assert.Equal(D(output) / 1000, ModelPrices.Cost(model, new JsonObject { ["output_tokens"] = 1000, ["speed"] = "fast" }).Usd);
        Assert.Equal(D(read) / 1000, ModelPrices.Cost(model, new JsonObject { ["cache_read_input_tokens"] = 1000, ["speed"] = "fast" }).Usd);
        Assert.Equal(D(write5m) / 1000, ModelPrices.Cost(model, new JsonObject { ["cache_creation_input_tokens"] = 1000, ["speed"] = "fast" }).Usd);
        Assert.Equal(D(write1h) / 1000, ModelPrices.Cost(model, new JsonObject
        {
            ["cache_creation_input_tokens"] = 1000,
            ["cache_creation"] = new JsonObject { ["ephemeral_1h_input_tokens"] = 1000 },
            ["speed"] = "fast",
        }).Usd);
    }

    [Fact]
    public void Fast_is_twice_the_standard_rate_of_every_token_class()
    {
        // Opus 5.5 is in both tables; Opus 5 and 4.8 are $5, $6.25, $10, $0.50 and $25 at standard speed.
        var standard = new Dictionary<string, ModelPrice>
        {
            ["claude-opus-5-5"] = ModelPrices.Table["claude-opus-5-5"],
            ["claude-opus-5"] = new(Input: 5m, CacheWrite5m: 6.25m, CacheWrite1h: 10m, CacheRead: 0.50m, Output: 25m),
            ["claude-opus-4-8"] = new(Input: 5m, CacheWrite5m: 6.25m, CacheWrite1h: 10m, CacheRead: 0.50m, Output: 25m),
        };
        Assert.Equal(standard.Keys.Order(), ModelPrices.FastTable.Keys.Order());
        foreach (var (model, price) in standard)
        {
            var fast = ModelPrices.FastTable[model];
            Assert.Equal(2 * price.Input, fast.Input);
            Assert.Equal(2 * price.CacheWrite5m, fast.CacheWrite5m);
            Assert.Equal(2 * price.CacheWrite1h, fast.CacheWrite1h);
            Assert.Equal(2 * price.CacheRead, fast.CacheRead);
            Assert.Equal(2 * price.Output, fast.Output);
        }
        Assert.Equal(2m, ModelPrices.FastMultiplier);
        Assert.Equal("fast-mode-2026-02-01", ModelPrices.FastModeBeta);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData(null)]
    [InlineData("")]
    public void A_response_that_does_not_say_fast_is_priced_as_ever(string? speed)
    {
        foreach (var (model, expected) in new[] { ("claude-opus-5-5", 0.488m), ("claude-sonnet-5-5", 0.244m), ("claude-haiku-5-5", 0.0124m), ("claude-fable-5-1", 1.21m), ("claude-opus-5", 1.24m) })
        {
            var cost = ModelPrices.Cost(model, FastUsage(speed));
            Assert.Equal(expected, cost.Usd);
            Assert.False(cost.Fast);
            Assert.Equal(ModelPrices.Cost(model, Usage()), cost);
            Assert.Equal(ModelPrices.Cost(model, TokenUsage.FromJson(Usage())), cost);
        }
        Assert.False(ModelPrices.IsFast(FastUsage(speed)));
        Assert.False(ModelPrices.IsFast(null));
        Assert.True(ModelPrices.IsFast(FastUsage("fast")));
        Assert.True(ModelPrices.IsFast(FastUsage("FAST")));
        Assert.Equal(speed is "standard" ? "standard" : null, ModelPrices.SpeedOf(FastUsage(speed)));
    }

    [Fact]
    public void The_standard_table_is_what_it_was_before_fast_mode()
    {
        // The arena's real-brain mode reads this table: fast mode adds a table of its own and changes no row here.
        var rows = ModelPrices.Table.OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => FormattableString.Invariant($"{r.Key} {r.Value.Input} {r.Value.CacheWrite5m} {r.Value.CacheWrite1h} {r.Value.CacheRead} {r.Value.Output} {r.Value.LongPromptThreshold} {r.Value.LongPromptMultiplier}"));
        Assert.Equal(
        [
            "claude-fable-5 10 12.5 20 1.00 50  1",
            "claude-fable-5-1 10 12.5 20 0.25 50  1",
            "claude-haiku-5-5 0.10 0.125 0.20 0.01 0.50 100000 5",
            "claude-mythos-5 10 12.5 20 1.00 50  1",
            "claude-opus-5-5 4 5 8 0.20 20  1",
            "claude-sonnet-5-5 2 2.5 4 0.10 10  1",
        ], rows);
        Assert.False(ModelPrices.IsKnown("claude-opus-5"));
        Assert.False(ModelPrices.IsKnown("claude-opus-4-8"));
    }

    [Fact]
    public void A_fast_response_from_a_model_outside_the_fast_table_costs_twice_its_standard_price()
    {
        // The API refuses fast mode there, so this only keeps the estimate from coming out low.
        Assert.Equal(2 * 0.244m, ModelPrices.Cost("claude-sonnet-5-5", FastUsage()).Usd);
        Assert.Equal("claude-sonnet-5-5", ModelPrices.Cost("claude-sonnet-5-5", FastUsage()).PricedAs);
        var unknown = ModelPrices.Cost("claude-opus-6", FastUsage());
        Assert.Equal(2 * 1.24m, unknown.Usd);
        Assert.False(unknown.KnownModel);
        Assert.True(unknown.Fast);
        // Haiku's long-prompt tier still applies first.
        Assert.Equal(2 * 5 * (0.0124m + 0.0000001m), ModelPrices.Cost("claude-haiku-5-5", TokenUsage.FromJson(Usage(input: 10_001)), fast: true).Usd);
        foreach (var model in new[] { "claude-sonnet-5-5", "claude-haiku-5-5", "claude-fable-5-1", "claude-fable-5", "claude-mythos-5", "claude-opus-4-7", "claude-opus-4-6", "claude-opus-6", null, "" })
        {
            Assert.False(ModelPrices.SupportsFast(model));
            Assert.Null(ModelPrices.FastFor(model));
        }
    }

    [Fact]
    public void A_response_is_priced_at_the_speed_it_reports_and_as_asked_only_when_it_reports_none()
    {
        ClaudeResponse Response(string? speed) => new(new JsonObject { ["model"] = "claude-opus-5-5", ["usage"] = FastUsage(speed) });

        Assert.Equal("fast", Response("fast").Speed);
        Assert.Equal("standard", Response("standard").Speed);
        Assert.Null(Response(null).Speed);

        Assert.Equal(0.976m, Response("fast").Cost("claude-opus-5-5").Usd);
        Assert.Equal(0.976m, Response("fast").Cost("claude-opus-5-5", requestedFast: false).Usd);
        Assert.Equal(0.488m, Response("standard").Cost("claude-opus-5-5", requestedFast: true).Usd);
        Assert.Equal(0.488m, Response(null).Cost("claude-opus-5-5").Usd);
        Assert.Equal(0.976m, Response(null).Cost("claude-opus-5-5", requestedFast: true).Usd);
        // The response's own model decides the table row, as before.
        Assert.Equal(1.24m, new ClaudeResponse(new JsonObject { ["model"] = "claude-opus-5", ["usage"] = FastUsage() }).Cost("claude-opus-5-5").Usd);
    }

    [Fact]
    public void A_fast_cost_counts_toward_the_monthly_total_and_the_cap_at_its_fast_price()
    {
        var dir = Path.Combine(Path.GetTempPath(), "spiremind-fast-spend-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTime(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
            var spend = new SpendTracker(dir);
            spend.Add(ModelPrices.Cost("claude-opus-5-5", FastUsage()).Usd, now);
            spend.Add(ModelPrices.Cost("claude-opus-5-5", FastUsage("standard")).Usd, now);

            Assert.Equal(0.976m + 0.488m, spend.MonthTotal(now));
            Assert.Equal(0.976m + 0.488m, spend.RunTotal);
            Assert.True(spend.OverCap(1.40m, now)); // two standard responses would be 0.976: under it
            Assert.False(spend.TryBegin(1.40m, now, out var month));
            Assert.Equal(1.464m, month);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public void Missing_usage_costs_nothing()
    {
        Assert.Equal(0m, ModelPrices.Cost("claude-opus-5-5", (JsonObject?)null).Usd);
        Assert.Equal(TokenUsage.Zero, TokenUsage.FromJson(new JsonObject { ["input_tokens"] = null, ["output_tokens"] = "lots" }));
    }

    [Fact]
    public void Token_usage_adds_up_and_knows_the_prompt_size()
    {
        var a = TokenUsage.FromJson(Usage());
        Assert.Equal(100_000, a.PromptTokens);
        Assert.Equal(50_000, a.CacheWrite);
        var sum = a + new TokenUsage(1, 2, 3, 4, 5);
        Assert.Equal(new TokenUsage(10_001, 5_002, 40_003, 20_004, 30_005), sum);
    }
}

public class SpendTrackerTests : IDisposable
{
    private static readonly DateTime Oct = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-spend-" + Guid.NewGuid().ToString("N"));

    public SpendTrackerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string SpendPath => Path.Combine(_dir, "spend.json");

    [Fact]
    public void Starts_from_zero_without_a_file()
    {
        var spend = new SpendTracker(_dir);
        Assert.Equal(0m, spend.MonthTotal(Oct));
        Assert.Equal(0m, spend.RunTotal);
        Assert.Equal(0, spend.Calls);
        Assert.Null(spend.LastError);
        Assert.False(File.Exists(SpendPath));
    }

    [Fact]
    public void Adds_up_the_month_and_the_run()
    {
        var spend = new SpendTracker(_dir);
        spend.Add(0.034m, Oct);
        spend.Add(0.021m, Oct.AddHours(1));

        Assert.Equal(0.055m, spend.MonthTotal(Oct));
        Assert.Equal(2, spend.MonthCalls(Oct));
        Assert.Equal(0.055m, spend.RunTotal);
        Assert.Equal(2, spend.Calls);

        var json = JsonNode.Parse(File.ReadAllText(SpendPath))!;
        Assert.Equal(0.055m, json["months"]!["2026-10"]!["usd"]!.GetValue<decimal>());
        Assert.Equal(2, json["months"]!["2026-10"]!["calls"]!.GetValue<int>());
    }

    [Fact]
    public void Month_totals_survive_a_restart_but_the_run_does_not()
    {
        new SpendTracker(_dir).Add(1.5m, Oct);

        var later = new SpendTracker(_dir);
        Assert.Equal(1.5m, later.MonthTotal(Oct));
        Assert.Equal(1, later.MonthCalls(Oct));
        Assert.Equal(0m, later.RunTotal);
        Assert.Equal(0, later.Calls);

        later.Add(0.5m, Oct);
        Assert.Equal(2m, new SpendTracker(_dir).MonthTotal(Oct));
    }

    [Fact]
    public void A_new_utc_month_starts_from_zero()
    {
        var spend = new SpendTracker(_dir);
        var lastMinuteOfOctober = new DateTime(2026, 10, 31, 23, 59, 0, DateTimeKind.Utc);
        var firstMinuteOfNovember = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

        spend.Add(3m, lastMinuteOfOctober);
        Assert.Equal(0m, spend.MonthTotal(firstMinuteOfNovember));
        spend.Add(1m, firstMinuteOfNovember);

        Assert.Equal(3m, spend.MonthTotal(lastMinuteOfOctober));
        Assert.Equal(1m, spend.MonthTotal(firstMinuteOfNovember));
        Assert.Equal(4m, spend.RunTotal);
        Assert.False(spend.OverCap(2m, firstMinuteOfNovember));
        Assert.True(spend.OverCap(2m, lastMinuteOfOctober));

        var months = JsonNode.Parse(File.ReadAllText(SpendPath))!["months"]!.AsObject();
        Assert.Equal(["2026-10", "2026-11"], months.Select(m => m.Key));
    }

    [Fact]
    public void Month_keys_are_utc()
    {
        Assert.Equal("2026-10", SpendTracker.MonthKey(new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc)));
        Assert.Equal("2026-11", SpendTracker.MonthKey(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified)));
        var local = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(local.ToUniversalTime().ToString("yyyy-MM"), SpendTracker.MonthKey(local));
    }

    [Fact]
    public void Over_cap_once_the_month_reaches_it()
    {
        var spend = new SpendTracker(_dir);
        spend.Add(0.99m, Oct);
        Assert.False(spend.OverCap(1m, Oct));
        spend.Add(0.01m, Oct);
        Assert.True(spend.OverCap(1m, Oct));
        Assert.True(new SpendTracker(_dir).OverCap(1m, Oct));
        Assert.True(new SpendTracker(Path.Combine(_dir, "empty")).OverCap(0m, Oct));
    }

    [Fact]
    public void Reset_run_keeps_the_month()
    {
        var spend = new SpendTracker(_dir);
        spend.Add(2m, Oct);
        spend.ResetRun();
        Assert.Equal(0m, spend.RunTotal);
        Assert.Equal(0, spend.Calls);
        Assert.Equal(2m, spend.MonthTotal(Oct));
        spend.Add(1m, Oct);
        Assert.Equal(1m, spend.RunTotal);
        Assert.Equal(1, spend.Calls);
    }

    [Theory]
    [InlineData("{ this isn't json")]
    [InlineData("[]")]
    [InlineData("""{ "months": [] }""")]
    [InlineData("""{ "months": { "2026-10": { "usd": "lots" } } }""")]
    [InlineData("""{ "months": { "2026-10": { "usd": -3 } } }""")]
    public void A_corrupt_file_is_moved_aside_and_counting_starts_over(string corrupt)
    {
        File.WriteAllText(SpendPath, corrupt);
        var log = new List<string>();

        var spend = new SpendTracker(_dir, log.Add);

        Assert.Equal(0m, spend.MonthTotal(Oct));
        Assert.Equal(corrupt, File.ReadAllText(SpendPath + ".bak"));
        Assert.False(File.Exists(SpendPath));
        Assert.Contains("corrupt", spend.LastError);
        Assert.Contains("corrupt", Assert.Single(log));

        spend.Add(0.25m, Oct);
        Assert.Null(spend.LastError);
        Assert.Equal(0.25m, new SpendTracker(_dir).MonthTotal(Oct));
        Assert.Equal(corrupt, File.ReadAllText(SpendPath + ".bak"));
    }

    [Fact]
    public void A_file_corrupted_while_running_keeps_the_totals_in_memory()
    {
        var spend = new SpendTracker(_dir);
        spend.Add(5m, Oct);
        File.WriteAllText(SpendPath, "garbage");

        spend.Add(1m, Oct);

        Assert.Equal(6m, spend.MonthTotal(Oct));
        Assert.Equal(6m, new SpendTracker(_dir).MonthTotal(Oct));
        Assert.Equal("garbage", File.ReadAllText(SpendPath + ".bak"));
    }

    [Fact]
    public void A_second_tracker_on_the_same_folder_never_undercounts()
    {
        var a = new SpendTracker(_dir);
        var b = new SpendTracker(_dir);
        a.Add(1m, Oct);
        b.Add(2m, Oct);

        Assert.Equal(3m, b.MonthTotal(Oct));
        Assert.Equal(3m, a.MonthTotal(Oct));
        Assert.Equal(1m, a.RunTotal);
        Assert.Equal(2m, b.RunTotal);
    }

    [Fact]
    public void A_failed_write_is_reported_and_the_cap_still_holds()
    {
        // A regular file where the folder should be makes every write fail, even as root.
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "not a folder");
        var log = new List<string>();
        var spend = new SpendTracker(Path.Combine(blocker, "spiremind"), log.Add);

        spend.Add(3m, Oct);

        Assert.NotNull(spend.LastError);
        Assert.Contains("Couldn't save spend.json", Assert.Single(log));
        Assert.Equal(3m, spend.MonthTotal(Oct));
        Assert.True(spend.OverCap(3m, Oct));
    }

    [Fact]
    public void Negative_costs_are_refused()
    {
        var spend = new SpendTracker(_dir);
        Assert.Throws<ArgumentOutOfRangeException>(() => spend.Add(-0.01m, Oct));
        Assert.Equal(0, spend.Calls);
    }

    [Fact]
    public async Task Adds_from_many_threads_are_all_counted()
    {
        var spend = new SpendTracker(_dir);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 25; i++)
                spend.Add(0.01m, Oct);
        })));

        Assert.Equal(2m, spend.MonthTotal(Oct));
        Assert.Equal(200, spend.Calls);
        Assert.Equal(200, new SpendTracker(_dir).MonthCalls(Oct));
        Assert.Equal(["spend.json"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
