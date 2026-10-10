using System.Diagnostics;
using System.Text.Json.Nodes;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>Several copies of the game counting into one spend.json under one cap.</summary>
public class CoopSpendTests : IDisposable
{
    private static readonly DateTime Oct = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "spiremind-coop-spend-" + Guid.NewGuid().ToString("N"));

    public CoopSpendTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string SpendPath => Path.Combine(_dir, "spend.json");

    private JsonObject SpendJson() => JsonNode.Parse(File.ReadAllText(SpendPath))!.AsObject();

    private JsonObject? InFlight() => SpendJson()["in_flight"] as JsonObject;

    [Fact]
    public void The_instance_id_defaults_to_main()
    {
        Assert.Equal("main", new SpendTracker(_dir).InstanceId);
        Assert.Equal("main", new SpendTracker(_dir, null, "  ").InstanceId);
        Assert.Equal("bot-17773", new SpendTracker(_dir, null, "bot-17773").InstanceId);
        Assert.Equal(Path.Combine(_dir, "locks", SpendTracker.LockFileName), new SpendTracker(_dir).LockPath);
    }

    [Fact]
    public async Task Two_copies_adding_at_once_lose_nothing()
    {
        var a = new SpendTracker(_dir, null, "main");
        var b = new SpendTracker(_dir, null, "bot-17773");
        var c = new SpendTracker(_dir, null, "bot-17774");

        await Task.WhenAll(new[] { a, b, c }.SelectMany(t => Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 20; i++)
                t.Add(0.01m, Oct);
        }))));

        Assert.Equal(2.40m, new SpendTracker(_dir).MonthTotal(Oct));
        Assert.Equal(240, new SpendTracker(_dir).MonthCalls(Oct));
        Assert.Equal(0.80m, a.RunTotal);
        Assert.Equal(80, b.Calls);
        Assert.Equal(2.40m, c.MonthTotal(Oct));
        Assert.Null(a.LastError);
        Assert.True(File.Exists(Path.Combine(_dir, "locks", SpendTracker.LockFileName)));
    }

    [Fact]
    public void A_copy_alone_is_refused_exactly_when_OverCap_says_so()
    {
        var spend = new SpendTracker(_dir, null, "bot-17773");
        foreach (var (cost, cap) in new[] { (0m, 1m), (0.98m, 1m), (0.01m, 1m), (0.01m, 1m), (0m, 2m), (0m, 0m) })
        {
            if (cost > 0) spend.Add(cost, Oct);
            var allowed = spend.TryBegin(cap, Oct, out var month);
            Assert.Equal(!spend.OverCap(cap, Oct), allowed);
            Assert.Equal(spend.MonthTotal(Oct), month);
            spend.Cancel();
        }
    }

    [Fact]
    public void Another_copys_request_in_flight_counts_against_the_cap()
    {
        var a = new SpendTracker(_dir, null, "bot-17773");
        var b = new SpendTracker(_dir, null, "bot-17774");
        a.Add(0.30m, Oct); // a's next estimate is its last cost
        b.Add(0.60m, Oct);

        Assert.True(a.TryBegin(1.00m, Oct, out var month));
        Assert.Equal(0.90m, month);

        // 0.90 spent plus a's 0.30 in flight reaches the cap.
        Assert.False(b.TryBegin(1.00m, Oct, out month));
        Assert.Equal(0.90m, month);
        Assert.True(b.TryBegin(1.25m, Oct, out _));
        b.Cancel();

        // a's request cost less than estimated; now b fits again.
        a.Add(0.05m, Oct);
        Assert.True(b.TryBegin(1.00m, Oct, out month));
        Assert.Equal(0.95m, month);
    }

    [Fact]
    public void The_estimate_is_the_last_cost_and_never_below_the_minimum()
    {
        var a = new SpendTracker(_dir, null, "bot-17773");

        Assert.True(a.TryBegin(10m, Oct, out _));
        Assert.Equal(SpendTracker.MinEstimateUsd, InFlight()!["bot-17773"]!["usd"]!.GetValue<decimal>());
        a.Add(0.42m, Oct);

        Assert.True(a.TryBegin(10m, Oct, out _));
        var entry = InFlight()!["bot-17773"]!;
        Assert.Equal(0.42m, entry["usd"]!.GetValue<decimal>());
        Assert.Equal(Oct, DateTime.Parse(entry["since"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        Assert.EndsWith("Z", entry["since"]!.GetValue<string>());

        a.Add(0.001m, Oct);
        Assert.True(a.TryBegin(10m, Oct, out _));
        Assert.Equal(SpendTracker.MinEstimateUsd, InFlight()!["bot-17773"]!["usd"]!.GetValue<decimal>());
    }

    [Fact]
    public void A_copys_own_estimate_never_blocks_itself()
    {
        var a = new SpendTracker(_dir, null, "main");
        a.Add(0.90m, Oct);
        Assert.True(a.TryBegin(1m, Oct, out _));
        Assert.True(a.TryBegin(1m, Oct, out _)); // a second begin replaces the first
        Assert.Single(InFlight()!);
    }

    [Fact]
    public void Add_and_Cancel_clear_the_entry()
    {
        var a = new SpendTracker(_dir, null, "bot-17773");
        var b = new SpendTracker(_dir, null, "bot-17774");

        Assert.True(a.TryBegin(5m, Oct, out _));
        Assert.True(b.TryBegin(5m, Oct, out _));
        Assert.Equal(["bot-17773", "bot-17774"], InFlight()!.Select(e => e.Key).Order());

        a.Add(0.10m, Oct);
        Assert.Equal(["bot-17774"], InFlight()!.Select(e => e.Key));

        b.Cancel();
        Assert.Null(InFlight());
        Assert.Equal(0.10m, SpendJson()["months"]!["2026-10"]!["usd"]!.GetValue<decimal>());

        // Nothing in flight: Cancel does nothing.
        var before = File.ReadAllText(SpendPath);
        b.Cancel();
        Assert.Equal(before, File.ReadAllText(SpendPath));
    }

    [Fact]
    public void A_refused_begin_records_nothing()
    {
        var a = new SpendTracker(_dir, null, "bot-17773");
        a.Add(1m, Oct);
        Assert.False(a.TryBegin(1m, Oct, out _));
        Assert.Null(InFlight());
    }

    [Fact]
    public void Expired_estimates_no_longer_count_and_are_pruned()
    {
        var crashed = new SpendTracker(_dir, null, "bot-17773");
        Assert.True(crashed.TryBegin(1m, Oct, out _));

        var later = Oct + SpendTracker.InFlightLimit;
        var b = new SpendTracker(_dir, null, "bot-17774");
        b.Add(0.96m, Oct);

        // Still live just before the limit: 0.96 + 0.05 reaches 1.00.
        Assert.False(b.TryBegin(1m, later - TimeSpan.FromSeconds(1), out _));
        Assert.True(b.TryBegin(1m, later, out _));
        Assert.Equal(["bot-17774"], InFlight()!.Select(e => e.Key));
    }

    [Fact]
    public void A_leftover_estimate_under_this_copys_own_id_is_dropped()
    {
        Assert.True(new SpendTracker(_dir, null, "bot-17773").TryBegin(1m, Oct, out _));

        // The same bot copy, started again: its old entry doesn't count against it, and goes.
        var again = new SpendTracker(_dir, null, "bot-17773");
        again.Add(0.97m, Oct);
        Assert.Null(InFlight());
        Assert.True(again.TryBegin(1m, Oct, out _));
    }

    [Fact]
    public void An_old_spend_file_without_in_flight_reads_as_before()
    {
        File.WriteAllText(SpendPath, """{ "months": { "2026-10": { "usd": 3.21, "calls": 41 } } }""");
        var spend = new SpendTracker(_dir, null, "bot-17773");

        Assert.Equal(3.21m, spend.MonthTotal(Oct));
        Assert.True(spend.TryBegin(5m, Oct, out var month));
        Assert.Equal(3.21m, month);
        spend.Add(0.79m, Oct);

        var json = SpendJson();
        Assert.Equal(4m, json["months"]!["2026-10"]!["usd"]!.GetValue<decimal>());
        Assert.Equal(42, json["months"]!["2026-10"]!["calls"]!.GetValue<int>());
        Assert.False(json.ContainsKey("in_flight"));
    }

    [Theory]
    [InlineData("""{ "months": { "2026-10": { "usd": 1 } }, "in_flight": [] }""")]
    [InlineData("""{ "months": { "2026-10": { "usd": 1 } }, "in_flight": "lots" }""")]
    [InlineData("""{ "months": { "2026-10": { "usd": 1 } }, "in_flight": { "bot-17774": { "usd": "x", "since": "2026-10-10T12:00:00Z" } } }""")]
    [InlineData("""{ "months": { "2026-10": { "usd": 1 } }, "in_flight": { "bot-17774": { "usd": 5, "since": "yesterday" } } }""")]
    [InlineData("""{ "months": { "2026-10": { "usd": 1 } }, "in_flight": { "bot-17774": { "usd": -5, "since": "2026-10-10T12:00:00Z" } } }""")]
    [InlineData("""{ "months": { "2026-10": { "usd": 1 } }, "in_flight": { "bot-17774": 5 } }""")]
    public void A_malformed_in_flight_is_dropped_not_a_corrupt_file(string text)
    {
        File.WriteAllText(SpendPath, text);
        var log = new List<string>();
        var spend = new SpendTracker(_dir, log.Add, "bot-17773");

        Assert.True(spend.TryBegin(1.5m, Oct, out var month));
        Assert.Equal(1m, month);
        Assert.Empty(log);
        Assert.False(File.Exists(SpendPath + ".bak"));
        Assert.Equal(["bot-17773"], InFlight()!.Select(e => e.Key));
    }

    [Fact]
    public void Other_copies_entries_survive_this_copys_writes()
    {
        File.WriteAllText(SpendPath, """
            { "months": { "2026-10": { "usd": 1 } },
              "in_flight": { "bot-17774": { "usd": 0.2, "since": "2026-10-10T11:59:00.0000000Z" } } }
            """);
        var spend = new SpendTracker(_dir, null, "main");

        Assert.False(spend.TryBegin(1.2m, Oct, out _));
        Assert.True(spend.TryBegin(1.21m, Oct, out _));
        spend.Add(0.01m, Oct);

        var entry = InFlight()!["bot-17774"]!;
        Assert.Equal(0.2m, entry["usd"]!.GetValue<decimal>());
        Assert.Single(InFlight()!);
    }

    [Fact]
    public void A_lock_held_elsewhere_makes_a_change_wait_for_it()
    {
        var spend = new SpendTracker(_dir, null, "bot-17773");
        var held = FileLock.Acquire(spend.LockPath, TimeSpan.FromSeconds(1));
        var add = Task.Run(() => spend.Add(0.5m, Oct));

        Assert.False(add.Wait(TimeSpan.FromMilliseconds(200)));
        held.Dispose();
        Assert.True(add.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(0.5m, new SpendTracker(_dir).MonthTotal(Oct));
    }

    [Fact]
    public void FileLock_blocks_a_second_acquire_until_the_first_is_released()
    {
        var path = Path.Combine(_dir, "locks", "test.lock");
        using (var first = FileLock.Acquire(path, TimeSpan.FromSeconds(1)))
        {
            Assert.True(File.Exists(path));
            var second = Task.Run(() => FileLock.Acquire(path, TimeSpan.FromSeconds(5)));
            Assert.False(second.Wait(TimeSpan.FromMilliseconds(150)));
            first.Dispose();
            Assert.True(second.Wait(TimeSpan.FromSeconds(5)));
            second.Result.Dispose();
        }
        // Released and never deleted.
        Assert.True(File.Exists(path));
        FileLock.Acquire(path, TimeSpan.Zero).Dispose();
    }

    [Fact]
    public void FileLock_times_out()
    {
        var path = Path.Combine(_dir, "deep", "folder", "test.lock");
        using var first = FileLock.Acquire(path, TimeSpan.Zero);
        var clock = Stopwatch.StartNew();

        var ex = Assert.Throws<TimeoutException>(() => FileLock.Acquire(path, TimeSpan.FromMilliseconds(120)));

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(100), $"gave up after {clock.ElapsedMilliseconds} ms");
        Assert.Contains("test.lock", ex.Message);
    }

    [Fact]
    public void FileLock_fails_at_once_when_the_folder_cant_be_made()
    {
        var blocker = Path.Combine(_dir, "blocker");
        File.WriteAllText(blocker, "not a folder");
        var clock = Stopwatch.StartNew();
        Assert.ThrowsAny<IOException>(() => FileLock.Acquire(Path.Combine(blocker, "x.lock"), TimeSpan.FromSeconds(5)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2));
    }
}
