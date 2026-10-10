using System.Collections.Concurrent;
using SpireMind.Core.Brain;
using Xunit;

namespace SpireMind.Core.Tests;

/// <summary>BrainController in co-op: <see cref="BrainController.Blocked"/> keeps the human
/// copy's brain off, and <see cref="BrainController.ForceAutostart"/> starts a bot copy's.</summary>
public class BrainGateTests
{
    private const string Reason = InstanceRole.HumanCoopLine;

    [Fact]
    public async Task A_blocked_start_says_why_and_saves_nothing()
    {
        using var rig = new GateRig();
        rig.BlockedReason = Reason;
        await rig.Init();

        Assert.Equal(Reason, rig.Post("/start"));

        Assert.Empty(rig.Saved);
        Assert.False(rig.Controller.Settings.Autostart);
        Assert.Empty(rig.ApiBrains);
        Assert.Equal(BrainKind.None, rig.Controller.Playing);
        Assert.Contains(rig.Logged, l => l.Contains("/start by Mark refused") && l.Contains(Reason));
    }

    [Fact]
    public async Task A_blocked_start_is_refused_before_the_other_client_check()
    {
        using var rig = new GateRig { OtherClient = true };
        rig.BlockedReason = Reason;
        await rig.Init();
        Assert.Equal(Reason, rig.Post("/start"));
    }

    [Fact]
    public async Task The_poll_loop_stops_a_playing_brain_once_blocked_and_says_why_once()
    {
        using var rig = new GateRig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var api = Assert.Single(rig.ApiBrains);
        Assert.True(api.IsRunning);
        var mark = rig.Mark();
        var stops = rig.Chat.Stops;

        rig.BlockedReason = Reason;
        await rig.Poll("combat");

        await GateRig.WaitUntil(() => !api.IsRunning);
        Assert.Equal(BrainKind.None, rig.Controller.Playing);
        Assert.Equal([Reason], rig.LinesSince(mark));
        Assert.True(rig.Chat.Stops > stops);

        // Said once per blocked stretch, and nothing starts again, also at a new run.
        await rig.Poll("combat");
        await rig.Poll("menu");
        await rig.Poll("map");
        Assert.Equal([Reason], rig.LinesSince(mark));
        Assert.Single(rig.ApiBrains);
        Assert.Empty(rig.SubBrains);

        // /start would have said the same; autostart stays on for when it is unblocked.
        Assert.True(rig.Controller.Settings.Autostart);
    }

    [Fact]
    public async Task Nothing_starts_at_a_run_start_while_blocked_and_the_reason_is_only_logged()
    {
        using var rig = new GateRig();
        rig.Config = rig.Config with { Autostart = true };
        rig.BlockedReason = InstanceRole.HumanCoopNoBotsLine;
        var mark = rig.Mark();
        await rig.Init();

        await rig.Poll("map");
        await rig.Poll("combat");

        Assert.Empty(rig.ApiBrains);
        Assert.Empty(rig.SubBrains);
        Assert.Empty(rig.LinesSince(mark));
        Assert.Contains(rig.Logged, l => l == $"brain: autostart is on, but {InstanceRole.HumanCoopNoBotsLine}");
        Assert.Single(rig.Logged, l => l.Contains("no brain plays here"));
    }

    [Fact]
    public async Task Without_a_key_the_subscription_doesnt_start_while_blocked()
    {
        using var rig = new GateRig { Key = null };
        rig.Config = rig.Config with { Autostart = true };
        rig.BlockedReason = Reason;
        await rig.Init();
        await rig.Poll("combat");
        await rig.Poll("combat");
        Assert.Empty(rig.SubBrains);
    }

    [Fact]
    public async Task Once_unblocked_the_next_run_starts_a_brain_again()
    {
        using var rig = new GateRig();
        rig.Config = rig.Config with { Autostart = true };
        rig.BlockedReason = Reason;
        await rig.Init();
        await rig.Poll("combat");
        Assert.Empty(rig.ApiBrains);

        rig.BlockedReason = null;
        await rig.Poll("game_over");
        var mark = rig.Mark();
        await rig.Poll("map");

        Assert.True(Assert.Single(rig.ApiBrains).IsRunning);
        // Nobody typed /start in this session, so the first brain says that it plays.
        Assert.Single(rig.LinesSince(mark), l => l.StartsWith("Ironclad is playing on your API credit"));

        // Blocked again later: stopped and said again, since it is a new stretch.
        mark = rig.Mark();
        rig.BlockedReason = Reason;
        await rig.Poll("map");
        Assert.Equal([Reason], rig.LinesSince(mark));
    }

    [Fact]
    public async Task A_blocked_api_stop_doesnt_fall_back_to_the_subscription()
    {
        using var rig = new GateRig();
        await rig.Init();
        await rig.Poll("combat");
        rig.Post("/start");
        var api = rig.ApiBrains[0];
        var mark = rig.Mark();

        rig.BlockedReason = Reason;
        api.Finish(BrainStopReason.OutOfCredit);

        Assert.Empty(rig.SubBrains);
        Assert.DoesNotContain(BrainController.SwitchingLine, rig.LinesSince(mark));
    }

    [Fact]
    public async Task Budget_shows_the_reason_when_nothing_plays()
    {
        using var rig = new GateRig();
        rig.BlockedReason = Reason;
        await rig.Init();

        var reply = rig.Post("/budget")!;

        Assert.Equal(Reason, reply.Split('\n')[1]);
        Assert.Contains("this month,", reply);
    }

    [Fact]
    public async Task Budget_of_a_bot_copy_counts_every_copy()
    {
        using var rig = new GateRig();
        rig.Config = rig.Config with { SharedConfigDir = Path.Combine(rig.Dir, "shared") };
        await rig.Init();
        Assert.Contains("of $200 this month across every copy,", rig.Post("/budget"));
    }

    [Fact]
    public async Task ForceAutostart_starts_the_api_brain_at_initialize_without_saving()
    {
        using var rig = new GateRig { ForceAutostart = true };
        Assert.False(rig.Config.Autostart);

        await rig.Init();

        var api = Assert.Single(rig.ApiBrains);
        Assert.True(api.IsRunning);
        Assert.True(rig.Controller.Settings.Autostart);
        Assert.Empty(rig.Saved);
        Assert.False(rig.Config.Autostart);
        Assert.Contains(rig.Logged, l => l.StartsWith("brain: autostart is on. Ironclad is playing on your API credit"));
    }

    [Fact]
    public async Task ForceAutostart_still_lets_stop_turn_it_off()
    {
        using var rig = new GateRig { ForceAutostart = true };
        await rig.Init();
        Assert.Equal("Ironclad stopped. /start hands control back.", rig.Post("/stop"));
        await rig.Poll("game_over");
        await rig.Poll("map");
        Assert.Single(rig.ApiBrains);
        Assert.False(rig.Saved[^1].Autostart);
    }

    [Fact]
    public async Task Unblocked_and_without_force_nothing_changes()
    {
        using var rig = new GateRig();
        rig.BlockedReason = null;
        await rig.Init();
        Assert.Empty(rig.ApiBrains);

        Assert.StartsWith("Ironclad is playing on your API credit", rig.Post("/start"));
        await rig.Poll("combat");
        await rig.Poll("combat");
        Assert.True(Assert.Single(rig.ApiBrains).IsRunning);
        Assert.True(Assert.Single(rig.Saved).Autostart);
        Assert.DoesNotContain(rig.Logged, l => l.Contains("no brain plays here"));
    }

    [Fact]
    public async Task A_throwing_gate_counts_as_open_and_is_logged_once()
    {
        using var rig = new GateRig { GateThrows = true };
        await rig.Init();
        Assert.StartsWith("Ironclad is playing on your API credit", rig.Post("/start"));
        await rig.Poll("combat");
        Assert.Single(rig.Logged, l => l.Contains("couldn't tell whether a brain may play here"));
    }
}

/// <summary>A controller over fake brains, a fixed clock and a poll loop the test steps by hand.</summary>
file sealed class GateRig : IDisposable
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly object _gate = new();
    private readonly SemaphoreSlim _entered = new(0);
    private readonly SemaphoreSlim _release = new(0);
    private readonly ConcurrentQueue<string> _logged = new();
    private readonly List<GateBrain> _api = [];
    private readonly List<GateBrain> _sub = [];
    private readonly List<BrainSettings> _saved = [];
    private BrainSettings _config;
    private string? _screen = "menu";
    private volatile string? _blocked;
    private BrainController? _controller;

    public GateRig()
    {
        Directory.CreateDirectory(Dir);
        Spend = new SpendTracker(Dir);
        _config = new BrainSettings { ConfigDir = Dir, Effort = "high", MonthlyBudgetUsd = 200m };
    }

    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "spiremind-gate-" + Guid.NewGuid().ToString("N"));
    public SpendTracker Spend { get; }
    public ChatHub Chat { get; } = new() { BotName = "Ironclad" };
    public DateTime Now { get; } = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    public string? Key { get; init; } = "sk-ant-api03-gate-test-key";
    public bool OtherClient { get; init; }
    public bool ForceAutostart { get; init; }
    public bool GateThrows { get; init; }

    /// <summary>What Blocked returns.</summary>
    public string? BlockedReason
    {
        get => _blocked;
        set => _blocked = value;
    }

    public BrainSettings Config
    {
        get { lock (_gate) return _config; }
        set { lock (_gate) _config = value; }
    }

    private string? Screen
    {
        get { lock (_gate) return _screen; }
        set { lock (_gate) _screen = value; }
    }

    public GateBrain[] ApiBrains { get { lock (_gate) return _api.ToArray(); } }
    public GateBrain[] SubBrains { get { lock (_gate) return _sub.ToArray(); } }
    public BrainSettings[] Saved { get { lock (_gate) return _saved.ToArray(); } }
    public string[] Logged => _logged.ToArray();

    public BrainController Controller => _controller ??= Build();

    public async Task Init()
    {
        Controller.Initialize();
        Assert.True(await _entered.WaitAsync(Guard), "the poll loop didn't run");
    }

    public async Task Poll(string? screen)
    {
        Screen = screen;
        _release.Release();
        Assert.True(await _entered.WaitAsync(Guard), "the poll loop didn't come back");
    }

    public string? Post(string text) => Chat.PostFromHuman("Mark", text).Reply;

    public long Mark() => Chat.LastSeq;

    public string[] LinesSince(long seq) => Chat.LinesSince(seq).Select(l => l.Text).ToArray();

    public static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Guard;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("timed out waiting");
            await Task.Delay(5);
        }
    }

    private BrainController Build() => new(
        loadSettings: () => Config with { ApiKey = Key, ApiKeySource = Key is null ? ApiKeySource.None : ApiKeySource.File },
        saveSettings: s =>
        {
            lock (_gate) _saved.Add(s);
            Config = s with { ApiKey = null, ApiKeySource = ApiKeySource.None };
        },
        createApi: s => Add(_api),
        createSubscription: s => Add(_sub),
        currentScreen: _ => Task.FromResult(Screen),
        spend: Spend,
        chat: Chat,
        log: _logged.Enqueue,
        clock: () => Now,
        delay: async (_, ct) =>
        {
            _entered.Release();
            await _release.WaitAsync(ct);
        },
        otherClient: () => OtherClient)
    {
        StopWaitLimit = TimeSpan.FromSeconds(10),
        Blocked = () => GateThrows ? throw new InvalidOperationException("no relay") : BlockedReason,
        ForceAutostart = ForceAutostart,
    };

    private GateBrain Add(List<GateBrain> list)
    {
        var brain = new GateBrain();
        lock (_gate) list.Add(brain);
        return brain;
    }

    public void Dispose()
    {
        _controller?.Shutdown();
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}

/// <summary>A brain that plays until stopped; <see cref="Finish"/> stops it as it would by itself.</summary>
file sealed class GateBrain : IPlayBrain
{
    private readonly object _gate = new();
    private bool _running;

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public bool? LastSeenInRun { get; set; }

    public event Action<BrainStopReason, string>? Stopped;

    public bool Start()
    {
        lock (_gate)
        {
            if (_running) return false;
            _running = true;
            return true;
        }
    }

    public Task StopAsync()
    {
        Finish(BrainStopReason.UserStop, "stopped");
        return Task.CompletedTask;
    }

    public void Abort() => Finish(BrainStopReason.UserStop, "aborted");

    public void Finish(BrainStopReason reason, string message = "finished")
    {
        lock (_gate)
        {
            if (!_running) return;
            _running = false;
        }
        Stopped?.Invoke(reason, message);
    }
}
