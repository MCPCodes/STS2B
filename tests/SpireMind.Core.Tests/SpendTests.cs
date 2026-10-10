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
    public void Unknown_models_are_priced_as_the_most_expensive_and_flagged()
    {
        // Fable 5 and Mythos 5.1 cost as much as Fable 5.1; "claude-fable-5" is shorter than the
        // table key, so it doesn't match it as a snapshot and must not get cheaper rates.
        foreach (var model in new[] { "claude-opus-6", "claude-fable-5", "claude-mythos-5-1", "claude-opus-5", "gpt-5", null, "" })
        {
            var cost = ModelPrices.Cost(model, Usage());
            Assert.False(cost.KnownModel);
            Assert.Equal("claude-fable-5-1", cost.PricedAs);
            Assert.Equal(1.21m, cost.Usd);
        }
        var fallback = ModelPrices.Table[ModelPrices.FallbackModel];
        Assert.Equal(ModelPrices.Table.Values.Max(p => p.Output), fallback.Output);
        Assert.Equal(ModelPrices.Table.Values.Max(p => p.Input), fallback.Input);
        Assert.False(ModelPrices.IsKnown("claude-opus-6"));
        ModelPrices.For("nope", out var known);
        Assert.False(known);
    }

    [Theory]
    [InlineData("claude-sonnet-5-5-20260930", "claude-sonnet-5-5")]
    [InlineData("CLAUDE-HAIKU-5-5", "claude-haiku-5-5")]
    [InlineData("haiku", "claude-haiku-5-5")]
    [InlineData("fable", "claude-fable-5-1")]
    public void Snapshots_and_aliases_find_their_model(string model, string pricedAs)
    {
        var cost = ModelPrices.Cost(model, Usage());
        Assert.True(cost.KnownModel);
        Assert.Equal(pricedAs, cost.PricedAs);
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
