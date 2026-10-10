using System.Globalization;
using System.Text.RegularExpressions;

namespace SpireMind.Core.Brain;

/// <summary>Which brain plays: none, the API brain on the API credit, or the subscription fallback.</summary>
public enum BrainKind { None, Api, Subscription }

/// <summary>
/// Picks the brain that plays and runs it: the API brain on Mark's API credit first, his Claude
/// subscription as the fallback. It owns the chat commands /start, /stop, /budget, /model and
/// /effort, and a poll loop that follows the game from run to run. See docs/brain.md.
/// <list type="bullet">
/// <item>The API brain plays when the settings have a key, this month's spend is under
/// monthly_budget_usd and the API hasn't failed during this run. It may start at the menu,
/// since it idles there for free. Otherwise the subscription brain plays, and only while a run
/// is in progress: it starts with a run and is stopped once the run has been over for
/// <see cref="RunOverGrace"/>, so it never spends subscription turns at the menu.</item>
/// <item>When the API brain stops for a missing or rejected key, the credit, the Console spend
/// limit or the monthly cap, the subscription brain takes over for the rest of the run (or the
/// next run, when it stopped between runs). The next run tries the API again.</item>
/// <item>A brain that stops with an error, or that something else stopped, isn't restarted
/// until the next run starts (while autostart is on) or /start. One that stepped back because
/// the run ended is started again when a run is in progress.</item>
/// <item>Brains announce their own stops; the controller only adds the lines about switching.</item>
/// </list>
/// All members are thread-safe. Command handlers run on the posting thread (often Godot's main
/// thread) and never wait: brains start without blocking, and stopping one happens on a pool
/// thread. One lock guards the state, and no await, StopAsync or chat line happens under it.
/// </summary>
public sealed partial class BrainController
{
    /// <summary>Said when the API brain stopped for a fallback reason during a run.</summary>
    public const string SwitchingLine = "Switching to your Claude subscription for the rest of this run.";

    /// <summary>Said when the API brain stopped for a fallback reason between runs.</summary>
    public const string SwitchingNextRunLine = "Switching to your Claude subscription for the next run.";

    /// <summary>Said when a run starts on the API credit after the API failed during the last one.</summary>
    public const string BackOnApiLine = "New run, so back on your API credit.";

    private const string NoKeyReason = "there's no API key";
    private const string BudgetReason = "this month's API budget is used up";
    private const string ZeroBudgetReason = "the monthly API budget is set to $0";
    private const string FailedReason = "the API failed this run";

    private readonly Func<BrainSettings> _loadSettings;
    private readonly Action<BrainSettings> _saveSettings;
    private readonly Func<BrainSettings, IPlayBrain> _createApi;
    private readonly Func<BrainSettings, IPlayBrain> _createSubscription;
    private readonly Func<CancellationToken, Task<string?>> _currentScreen;
    private readonly SpendTracker _spend;
    private readonly ChatHub _chat;
    private readonly Action<string> _log;
    private readonly Func<DateTime> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly object _gate = new();
    private readonly List<IPlayBrain> _live = []; // started and not stopped yet, the current one or old ones
    private BrainSettings _settings;
    private IPlayBrain? _brain; // the brain that plays, or is about to start; null when none
    private BrainKind _kind;
    private long _generation; // bumped by every start and stop, so late events and deferred starts know they are stale
    private bool? _inRun; // the last answer RunScreens gave, null until one
    private long _run; // runs seen starting
    private long? _apiFailedRun; // the run the API failed in (or the next one, when it failed between runs)
    private bool _hold; // a brain stopped with an error or by someone else: nothing restarts until the next run or /start
    private int _restarts; // subscription restarts during this run
    private bool _announceStart; // /start said a brain would play once a run is in progress; say so when it does
    private DateTime? _outOfRunSince; // when the subscription brain was first seen playing outside a run
    private Task _stopping = Task.CompletedTask; // StopAsync calls in flight
    private CancellationTokenSource? _pollCts;
    private bool _initialized;
    private bool _shutdown;

    /// <param name="loadSettings">Reads config.json and the API key (<see cref="BrainSettings.Load"/>).
    /// Called here, by /start, /stop, /model and /effort before they change a setting, and when a
    /// run starts, so a key added meanwhile is picked up. The autostart flag in memory wins over
    /// the file's, except in /start and /stop.</param>
    /// <param name="saveSettings">Writes config.json (<see cref="BrainSettings.Save"/>). A failure
    /// is logged and mentioned in the reply; the change still holds until the game closes.</param>
    /// <param name="createApi">Makes an API brain for one start, called only when the settings
    /// have a key (the settings passed in are for the key). It should read <see cref="Settings"/>
    /// through a Func before every request, so /model and /effort apply at once. Both factories
    /// run under the controller's lock, so they must return quickly and not call back into it.
    /// A fresh instance per start is best; a reused one works too, once it has stopped.</param>
    /// <param name="createSubscription">Makes a subscription brain for one start.</param>
    /// <param name="currentScreen">The screen of the game's current snapshot ("combat", "menu"),
    /// or null when it can't be read. Called on the poll loop's task, never on the caller's thread.</param>
    /// <param name="spend">The API spend: the monthly total for the cap and /budget, and the run
    /// total, which the controller resets when a run starts.</param>
    /// <param name="chat">Where the commands go, and the lines about switching brains.</param>
    /// <param name="log">The mod's log. Never gets the key. Exceptions it throws are swallowed.</param>
    /// <param name="clock">Time source for tests; defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="delay">The poll loop's wait, for tests; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
    public BrainController(
        Func<BrainSettings> loadSettings,
        Action<BrainSettings> saveSettings,
        Func<BrainSettings, IPlayBrain> createApi,
        Func<BrainSettings, IPlayBrain> createSubscription,
        Func<CancellationToken, Task<string?>> currentScreen,
        SpendTracker spend,
        ChatHub chat,
        Action<string>? log = null,
        Func<DateTime>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _loadSettings = loadSettings;
        _saveSettings = saveSettings;
        _createApi = createApi;
        _createSubscription = createSubscription;
        _currentScreen = currentScreen;
        _spend = spend;
        _chat = chat;
        _log = log ?? (_ => { });
        _clock = clock ?? (() => DateTime.UtcNow);
        _delay = delay ?? Task.Delay;
        _settings = Reload(new BrainSettings(), keepAutostart: false);
        RegisterCommands();
    }

    /// <summary>How often the poll loop reads the screen.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How many times in one run the subscription brain is started again after it
    /// stepped back (<see cref="BrainStopReason.RunEnded"/>) while the screen still shows the
    /// run, so a brain that keeps ending at once can't burn subscription turns.</summary>
    public int MaxRestartsPerRun { get; init; } = 3;

    /// <summary>How long a stopping brain gets before it is aborted. The next brain starts only
    /// once the last one has stopped, since two at once would cancel each other's waits.</summary>
    public TimeSpan StopWaitLimit { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long the subscription brain may go on once the poll loop sees the run over
    /// (game over or the menu): time for the post-mortem in the journal. After that it is
    /// stopped, and the next run starts it again. Claude Code would otherwise keep waiting at the
    /// menu, a subscription turn every few minutes.</summary>
    public TimeSpan RunOverGrace { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The current settings. Brains read them through a Func before every request, so
    /// /model and /effort apply at once.</summary>
    public BrainSettings Settings
    {
        get { lock (_gate) return _settings; }
    }

    /// <summary>The brain that plays, or is about to start once the last one has stopped.</summary>
    public BrainKind Playing
    {
        get { lock (_gate) return _kind; }
    }

    /// <summary>
    /// Called once at mod load: starts the poll loop and, when autostart is on, does what
    /// /start does without saving and without a chat line (the line goes to the log).
    /// </summary>
    public void Initialize()
    {
        BrainSettings settings;
        lock (_gate)
        {
            if (_initialized || _shutdown)
                return;
            _initialized = true;
            settings = _settings;
        }
        Log($"brain: {settings}");
        foreach (var warning in settings.Warnings)
            Log($"brain: {warning}");
        if (settings.Autostart)
            Log($"brain: autostart is on. {StartChosen(settings, fromCommand: false)}");
        EnsurePolling();
    }

    /// <summary>
    /// For when the game quits: stops the poll loop and aborts every brain that plays or is
    /// still stopping. Returns at once and never waits for a brain.
    /// </summary>
    public void Shutdown()
    {
        CancellationTokenSource? poll;
        List<IPlayBrain> brains;
        lock (_gate)
        {
            if (_shutdown)
                return;
            _shutdown = true;
            _generation++;
            poll = _pollCts;
            brains = [.. _live];
            if (_brain is { } current && !brains.Any(b => ReferenceEquals(b, current)))
                brains.Add(current);
            _brain = null;
            _kind = BrainKind.None;
        }

        try { poll?.Cancel(); }
        catch (ObjectDisposedException) { /* already gone */ }
        foreach (var brain in brains)
        {
            try { brain.Abort(); }
            catch (Exception ex) { Log($"brain: abort threw {ex.GetType().Name}: {ex.Message}"); }
        }
        Log("brain: shut down");
    }

    /// <summary>A model id as people say it: claude-&lt;family&gt;-&lt;major&gt;-&lt;minor&gt; reads
    /// "&lt;Family&gt; &lt;major&gt;.&lt;minor&gt;", and a dated snapshot drops its date. Ids of another
    /// shape come back as they are.</summary>
    public static string ModelName(string? model)
    {
        var id = model?.Trim() ?? "";
        var match = ModelId().Match(id);
        if (!match.Success)
            return id;
        var family = match.Groups[1].Value;
        var name = char.ToUpperInvariant(family[0]) + family[1..].ToLowerInvariant();
        var version = match.Groups[3].Success ? $"{match.Groups[2].Value}.{match.Groups[3].Value}" : match.Groups[2].Value;
        return $"{name} {version}";
    }

    // ---- Commands ----

    private void RegisterCommands()
    {
        _chat.AddCommand("start", "", "lets the bot play by itself", StartCommand);
        _chat.AddCommand("stop", "", "stops the bot until you /start it again", StopCommand);
        _chat.AddCommand("budget", "", "shows the API spend and what's playing", BudgetCommand);
        _chat.AddCommand("model", $"[{string.Join("|", BrainSettings.ModelAliases.Keys)}]", "shows or switches the model", ModelCommand);
        _chat.AddCommand("effort", $"[{string.Join("|", BrainSettings.Efforts)}]", "shows or sets the thinking effort", EffortCommand);
    }

    private string StartCommand(string from, string args)
    {
        var settings = Reload(Settings, keepAutostart: false) with { Autostart = true };
        var saved = Save(settings);
        lock (_gate) _settings = settings;
        Log($"brain: /start by {from}");
        var reply = StartChosen(settings, fromCommand: true);
        EnsurePolling();
        return reply + saved;
    }

    private string StopCommand(string from, string args)
    {
        var settings = Reload(Settings, keepAutostart: false) with { Autostart = false };
        var saved = Save(settings);
        IPlayBrain? brain;
        bool wasOn;
        lock (_gate)
        {
            wasOn = _settings.Autostart;
            _settings = settings;
            brain = _brain;
            _brain = null;
            _kind = BrainKind.None;
            _generation++; // its Stopped event is now stale, so it can't trigger a fallback
            _announceStart = false;
        }
        Log($"brain: /stop by {from}");

        var bot = BotName;
        if (brain is null)
            return (wasOn ? $"{bot} isn't playing, and won't start by itself until you type /start." : $"{bot} isn't playing.") + saved;
        ClearThinking(); // at once, not only when the brain has stopped (StopBrain clears it again)
        _ = StopBrain(brain);
        return $"{bot} stopped. /start hands control back.{saved}";
    }

    private string BudgetCommand(string from, string args)
    {
        var settings = Settings;
        var now = _clock();
        var month = _spend.MonthTotal(now);
        var run = _spend.RunTotal;
        var calls = _spend.Calls;

        string playing;
        lock (_gate)
        {
            var reason = SubscriptionReason(_settings, month);
            playing = _kind switch
            {
                BrainKind.Api => "Playing on your API credit.",
                BrainKind.Subscription when reason == FailedReason => "Playing on your Claude subscription as the fallback until the next run.",
                BrainKind.Subscription when reason is not null => $"Playing on your Claude subscription, since {reason}.",
                BrainKind.Subscription => "Playing on your Claude subscription until the next run.",
                _ when !_settings.Autostart => "Nothing is playing. /start hands control over.",
                _ when reason is not null && _inRun != true && !_hold =>
                    $"Nothing is playing yet. Your Claude subscription takes over once a run is in progress, since {reason}.",
                _ => "Nothing is playing until the next run starts.",
            };
        }

        var requests = calls == 1 ? "1 request" : $"{calls} requests";
        return $"API spend: {Money(month)} of {Budget(settings.MonthlyBudgetUsd)} this month, {Money(run)} this run in {requests}. " +
               $"{ModelName(settings.Model)}, {settings.Effort} effort.\n{playing}";
    }

    private string ModelCommand(string from, string args)
    {
        var current = Settings;
        var options = $"Options: {string.Join(", ", BrainSettings.ModelAliases.Keys)}, or a full model id.";
        if (args.Length == 0)
            return $"The model is {ModelName(current.Model)} ({current.Model}). {options}";
        if (!BrainSettings.TryResolveModel(args, out var id))
            return $"\"{args}\" isn't a model the mod knows. {options}";
        if (id == current.Model)
            return $"The model is {ModelName(id)} already.";

        var settings = Reload(current, keepAutostart: true) with { Model = id };
        var saved = Save(settings);
        bool subscription;
        lock (_gate)
        {
            _settings = settings;
            subscription = _kind == BrainKind.Subscription;
        }
        Log($"brain: model set to {id} by {from}");
        // Claude Code gets --model when a session starts, so a session already running keeps its model.
        return subscription
            ? $"Model set to {ModelName(id)}. Claude Code keeps its current model until its next session.{saved}"
            : $"Model set to {ModelName(id)}. It applies from the next request.{saved}";
    }

    private string EffortCommand(string from, string args)
    {
        var current = Settings;
        var options = $"Options: {string.Join(", ", BrainSettings.Efforts)}.";
        if (args.Length == 0)
            return $"The effort is {current.Effort}. {options}";
        if (!BrainSettings.TryParseEffort(args, out var effort))
            return $"\"{args}\" isn't an effort level. {options}";
        if (effort == current.Effort)
            return $"The effort is {effort} already.";

        var settings = Reload(current, keepAutostart: true) with { Effort = effort };
        var saved = Save(settings);
        bool subscription;
        lock (_gate)
        {
            _settings = settings;
            subscription = _kind == BrainKind.Subscription;
        }
        Log($"brain: effort set to {effort} by {from}");
        // Claude Code isn't told an effort, so only the API brain uses it.
        return subscription
            ? $"Effort set to {effort}. Only the API brain uses it, so Claude Code playing now isn't affected.{saved}"
            : $"Effort set to {effort}. It applies from the next request.{saved}";
    }

    // Starts the brain the settings call for unless one plays, and says what happened: the
    // reply to /start, or the log line when autostart starts it at mod load.
    private string StartChosen(BrainSettings settings, bool fromCommand)
    {
        var spent = _spend.MonthTotal(_clock());
        var bot = BotName;
        lock (_gate)
        {
            if (_shutdown)
                return "The game is closing.";
            _hold = false;
            if (_brain is not null)
                return $"{bot} is already playing on your {Source(_kind)}. /stop to take over.";

            var reason = SubscriptionReason(settings, spent);
            if (reason is null)
                return Launch(BrainKind.Api, settings, out var problem)
                    ? PlayingLine(bot, BrainKind.Api, null, settings, spent)
                    : CouldntStart(problem);
            if (_inRun == true)
                return Launch(BrainKind.Subscription, settings, out var problem)
                    ? PlayingLine(bot, BrainKind.Subscription, reason, settings, spent)
                    : CouldntStart(problem);

            _announceStart = fromCommand;
            return $"{bot} will play on your Claude subscription once a run is in progress, since {reason}.";
        }
    }

    // ---- Starting and stopping ----

    // Caller holds _gate. Makes the brain and starts it, or, while an old brain is still
    // stopping, starts it once that one has. False when nothing could be started; problem
    // then says why.
    private bool Launch(BrainKind kind, BrainSettings settings, out string? problem)
    {
        problem = null;
        if (_shutdown || _brain is not null)
        {
            problem = _shutdown ? "the game is closing" : "a brain is already playing";
            return false;
        }

        IPlayBrain brain;
        try
        {
            brain = kind == BrainKind.Api ? _createApi(settings) : _createSubscription(settings);
        }
        catch (Exception ex)
        {
            problem = ex.GetType().Name;
            Log($"brain: couldn't make the {KindName(kind)} brain: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        var generation = ++_generation;
        _brain = brain;
        _kind = kind;
        if (_stopping.IsCompleted)
            return StartNow(brain, kind, generation, out problem);

        Log($"brain: the {KindName(kind)} brain starts once the last one has stopped");
        _stopping.ContinueWith(_ => StartWhenStopped(brain, kind, generation),
            CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        return true;
    }

    private void StartWhenStopped(IPlayBrain brain, BrainKind kind, long generation)
    {
        lock (_gate)
        {
            if (_shutdown || generation != _generation || !ReferenceEquals(_brain, brain))
                return;
            StartNow(brain, kind, generation, out _);
        }
    }

    // Caller holds _gate. Start returns at once (the brain plays on its own task), so it runs
    // under the lock and a /stop can't slip in between. The Stopped handler belongs to this
    // start and removes itself, so a reused instance never reports an old start.
    private bool StartNow(IPlayBrain brain, BrainKind kind, long generation, out string? problem)
    {
        problem = null;
        Action<BrainStopReason, string>? handler = null;
        handler = (reason, message) =>
        {
            brain.Stopped -= handler;
            OnStopped(brain, generation, reason, message);
        };
        brain.Stopped += handler;
        _live.Add(brain);

        var started = false;
        try
        {
            started = brain.Start();
        }
        catch (Exception ex)
        {
            problem = ex.GetType().Name;
            Log($"brain: starting the {KindName(kind)} brain threw {ex.GetType().Name}: {ex.Message}");
        }
        if (started)
        {
            Log($"brain: the {KindName(kind)} brain started");
            return true;
        }

        brain.Stopped -= handler;
        _live.RemoveAll(b => ReferenceEquals(b, brain));
        if (ReferenceEquals(_brain, brain))
        {
            _brain = null;
            _kind = BrainKind.None;
        }
        problem ??= "it was already running";
        Log($"brain: the {KindName(kind)} brain didn't start ({problem})");
        return false;
    }

    // Stops a brain on a pool thread, never the caller's: the caller may be the main thread,
    // which the brain's loop may be waiting on. A brain still running after StopWaitLimit is
    // aborted. Then the thinking mark is cleared, also when a tool result that came in during
    // the stop set it again. The task never faults, and the next start waits for it, so the
    // clear can't wipe the next brain's mark.
    private Task StopBrain(IPlayBrain brain)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                var stop = brain.StopAsync();
                using var timeout = new CancellationTokenSource();
                if (await Task.WhenAny(stop, Task.Delay(StopWaitLimit, timeout.Token)).ConfigureAwait(false) == stop)
                {
                    timeout.Cancel();
                    await stop.ConfigureAwait(false);
                }
                else
                {
                    _ = stop.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    Log(Invariant($"brain: a brain didn't stop within {StopWaitLimit.TotalSeconds:0} s; aborting it"));
                    brain.Abort();
                }
            }
            catch (Exception ex)
            {
                Log($"brain: stopping a brain failed: {ex.GetType().Name}: {ex.Message}");
            }
            ClearThinking();
        });
        lock (_gate)
            _stopping = _stopping.IsCompleted ? task : Task.WhenAll(_stopping, task);
        return task;
    }

    // A brain's Stopped event, on the brain's own task. Only the brain that plays counts: one
    // that /stop, a new run or Shutdown replaced is ignored, so /stop never causes a fallback.
    private void OnStopped(IPlayBrain brain, long generation, BrainStopReason reason, string message)
    {
        string? line = null;
        var seen = brain.LastSeenInRun;
        lock (_gate)
        {
            _live.RemoveAll(b => ReferenceEquals(b, brain));
            if (generation != _generation || !ReferenceEquals(_brain, brain))
            {
                Log($"brain: a replaced brain stopped ({reason}); ignored");
                return;
            }

            var kind = _kind;
            _brain = null;
            _kind = BrainKind.None;
            Log($"brain: the {KindName(kind)} brain stopped ({reason}): {message}");
            // It can't act on a decision it was handed. Here, after the checks above, so a late
            // stop of a replaced brain can't wipe the next one's mark, and before a fallback
            // starts. ChatHub never calls out while it holds its lock, so taking it under ours is safe.
            ClearThinking();
            if (_shutdown)
                return;

            switch (reason)
            {
                case BrainStopReason.NoKey or BrainStopReason.Unauthorized or BrainStopReason.OutOfCredit
                    or BrainStopReason.SpendLimit or BrainStopReason.MonthlyCap when kind == BrainKind.Api:
                    // The API brain already said why. The subscription takes over for this run,
                    // or for the next one when this happened between runs. The brain's own look
                    // at the screen decides, since the poll's can be PollInterval old: a request
                    // that fails at once can beat the poll to a run start or a game over. A run
                    // the poll hasn't counted yet is the one its next round counts as _run + 1.
                    var polled = _inRun == true;
                    var inRun = seen ?? polled;
                    _apiFailedRun = inRun && polled ? _run : _run + 1;
                    line = inRun ? SwitchingLine : SwitchingNextRunLine;
                    if (inRun && _settings.Autostart && !Launch(BrainKind.Subscription, _settings, out var problem))
                        line += " " + CouldntStart(problem);
                    break;

                case BrainStopReason.RunEnded:
                    // The poll loop starts a brain again once a run is in progress.
                    break;

                default:
                    // An error the brain announced, or someone else stopped it. Nothing plays
                    // until the next run starts or /start.
                    _hold = true;
                    break;
            }
        }
        if (line is not null)
            Announce(line);
    }

    // ---- The poll loop ----

    // The loop runs from Initialize (or the first /start) until Shutdown, also while autostart
    // is off, so /start knows whether a run is in progress and no run start is missed. It acts
    // only while autostart is on.
    private void EnsurePolling()
    {
        lock (_gate)
        {
            if (_shutdown || _pollCts is not null)
                return;
            var cts = new CancellationTokenSource();
            _pollCts = cts;
            _ = Task.Run(() => PollAsync(cts.Token));
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        var failing = false;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? screen = null;
                try
                {
                    screen = await _currentScreen(ct).ConfigureAwait(false);
                    failing = false;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!failing)
                        Log($"brain: couldn't read the screen: {ex.GetType().Name}: {ex.Message}");
                    failing = true;
                }

                try
                {
                    await OnScreenAsync(screen).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log($"brain: the poll loop hit {ex.GetType().Name}: {ex.Message}");
                }

                await _delay(PollInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            Log($"brain: the poll loop stopped on {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task OnScreenAsync(string? screen)
    {
        var seen = RunScreens.InRun(screen);
        var now = _clock();
        bool runStarted, autostart;
        IPlayBrain? idle = null;
        lock (_gate)
        {
            if (_shutdown)
                return;
            runStarted = seen == true && _inRun != true;
            if (seen is { } inRun)
                _inRun = inRun;
            if (runStarted)
            {
                _run++;
                _hold = false;
                _restarts = 0;
            }
            autostart = _settings.Autostart;
            idle = TakeIdleSubscription(now);
        }

        if (idle is not null)
        {
            // Not held: the next run starts it again.
            Log(Invariant($"brain: the run has been over for {RunOverGrace.TotalSeconds:0} s; stopping the subscription brain until the next run"));
            _ = StopBrain(idle);
        }
        if (runStarted)
        {
            _spend.ResetRun();
            Log($"brain: a run is in progress ({screen})");
        }
        if (!autostart)
            return;
        if (runStarted)
            await OnRunStartedAsync().ConfigureAwait(false);
        else
            RestartSubscription();
    }

    // A run started with autostart on: settings read again, then the API is tried again when it
    // can be used, or whatever is chosen starts when nothing plays.
    private async Task OnRunStartedAsync()
    {
        var basis = Settings;
        var loaded = Reload(basis, keepAutostart: true);
        var spent = _spend.MonthTotal(_clock());
        var bot = BotName;
        IPlayBrain? toStop = null;
        long generation = 0;
        bool backOnApi;
        string? line = null;
        lock (_gate)
        {
            if (_shutdown || !_settings.Autostart)
                return;
            if (ReferenceEquals(_settings, basis)) // a command may have changed them meanwhile
                _settings = loaded;

            var settings = _settings;
            var reason = SubscriptionReason(settings, spent);
            backOnApi = reason is null && _apiFailedRun == _run - 1;
            if (_brain is null)
            {
                var kind = reason is null ? BrainKind.Api : BrainKind.Subscription;
                if (Launch(kind, settings, out _))
                    line = backOnApi ? BackOnApiLine : _announceStart ? PlayingLine(bot, kind, reason, settings, spent) : null;
            }
            else if (_kind == BrainKind.Subscription && reason is null)
            {
                toStop = _brain;
                _brain = null;
                _kind = BrainKind.None;
                generation = ++_generation;
            }
            _announceStart = false;
        }

        if (toStop is not null)
        {
            Log("brain: new run and the API can be used again; stopping the subscription brain");
            await StopBrain(toStop).ConfigureAwait(false);
            lock (_gate)
            {
                if (_shutdown || generation != _generation || !_settings.Autostart || _brain is not null)
                    return;
                if (Launch(BrainKind.Api, _settings, out _))
                    line = backOnApi ? BackOnApiLine : PlayingLine(bot, BrainKind.Api, null, _settings, spent);
            }
        }

        if (line is not null)
            Announce(line);
    }

    // Caller holds _gate. The subscription brain, taken off the controller to be stopped, once
    // the run has been over for RunOverGrace while it plays; null otherwise. A modal (no answer)
    // keeps the clock running, and seeing the run again resets it.
    private IPlayBrain? TakeIdleSubscription(DateTime now)
    {
        if (_inRun != false || _kind != BrainKind.Subscription || _brain is null)
        {
            _outOfRunSince = null;
            return null;
        }
        _outOfRunSince ??= now;
        if (now - _outOfRunSince < RunOverGrace)
            return null;

        var brain = _brain;
        _brain = null;
        _kind = BrainKind.None;
        _generation++; // its Stopped event is now stale
        _outOfRunSince = null;
        return brain;
    }

    // During a run with autostart on: the subscription brain starts when it is the choice and
    // nothing plays (it stepped back while the run goes on, or it was waiting for a run). Never
    // after an error in this run, and the API brain is never restarted mid-run.
    private void RestartSubscription()
    {
        lock (_gate)
        {
            if (_shutdown || !_settings.Autostart || _brain is not null || _hold || _inRun != true)
                return;
        }

        var spent = _spend.MonthTotal(_clock());
        var bot = BotName;
        string? line = null;
        lock (_gate)
        {
            if (_shutdown || !_settings.Autostart || _brain is not null || _hold || _inRun != true)
                return;
            var reason = SubscriptionReason(_settings, spent);
            if (reason is null)
                return;
            if (_restarts >= MaxRestartsPerRun)
            {
                if (_restarts++ == MaxRestartsPerRun)
                    Log("brain: the subscription brain keeps stepping back during this run; it starts again with the next run");
                return;
            }
            _restarts++;
            if (Launch(BrainKind.Subscription, _settings, out _) && _announceStart)
                line = PlayingLine(bot, BrainKind.Subscription, reason, _settings, spent);
            _announceStart = false;
        }
        if (line is not null)
            Announce(line);
    }

    // ---- Helpers ----

    // Caller holds _gate. Why the subscription plays instead of the API, or null when the API can.
    private string? SubscriptionReason(BrainSettings settings, decimal spent)
    {
        if (!settings.HasApiKey)
            return NoKeyReason;
        if (settings.MonthlyBudgetUsd <= 0)
            return ZeroBudgetReason;
        if (spent >= settings.MonthlyBudgetUsd)
            return BudgetReason;
        return _apiFailedRun == (_inRun == true ? _run : _run + 1) ? FailedReason : null;
    }

    private static string PlayingLine(string bot, BrainKind kind, string? reason, BrainSettings settings, decimal spent) =>
        kind == BrainKind.Api
            ? $"{bot} is playing on your API credit ({ModelName(settings.Model)}, {settings.Effort} effort, " +
              $"{Money(spent)} of {Budget(settings.MonthlyBudgetUsd)} spent this month). /stop to take over."
            : $"{bot} is playing on your Claude subscription, since {reason}. /stop to take over.";

    private static string CouldntStart(string? problem) =>
        $"Couldn't start the brain ({problem ?? "unknown problem"}). The details are in the log.";

    // config.json and the key read again. keepAutostart keeps the flag the controller holds,
    // since only /start and /stop change it while the game runs. The current settings when the
    // read fails.
    private BrainSettings Reload(BrainSettings current, bool keepAutostart)
    {
        try
        {
            var loaded = _loadSettings();
            return keepAutostart ? loaded with { Autostart = current.Autostart } : loaded;
        }
        catch (Exception ex)
        {
            Log($"brain: couldn't read the settings ({ex.GetType().Name}: {ex.Message}); keeping the current ones");
            return current;
        }
    }

    // Saves config.json. Empty when it worked, else a sentence for the end of the reply.
    private string Save(BrainSettings settings)
    {
        try
        {
            _saveSettings(settings);
            return "";
        }
        catch (Exception ex)
        {
            Log($"brain: couldn't save {BrainSettings.ConfigFileName}: {ex.GetType().Name}: {ex.Message}");
            return $" Couldn't save that to {BrainSettings.ConfigFileName} ({ex.GetType().Name}), so it only lasts until the game closes.";
        }
    }

    private string BotName => _chat.BotName ?? "The bot";

    private static string Source(BrainKind kind) => kind == BrainKind.Api ? "API credit" : "Claude subscription";

    private static string KindName(BrainKind kind) => kind == BrainKind.Api ? "API" : "subscription";

    // Spend with cents ("$3.20"); a budget without them when it is whole ("$200").
    private static string Money(decimal usd) => Invariant($"${usd:0.00}");

    private static string Budget(decimal usd) => usd == decimal.Truncate(usd) ? Invariant($"${usd:0}") : Money(usd);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    // A brain that stopped can't act on the decision it was handed, so no thought cloud should
    // stay over the character while the human plays (as /pause does).
    private void ClearThinking()
    {
        try { _chat.MarkThinking(false); }
        catch (Exception ex) { Log($"brain: couldn't clear the thinking mark: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void Announce(string line)
    {
        try { _chat.Announce(line); }
        catch (Exception ex) { Log($"brain: couldn't post to the chat: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void Log(string line)
    {
        try { _log(line); }
        catch (Exception) { /* a broken log can't be reported anywhere */ }
    }

    // claude-<family>-<major>[-<minor>], then the end or a snapshot suffix.
    [GeneratedRegex(@"^claude-([a-z]+)-(\d+)(?:-(\d{1,2}))?(?=$|[-@\[])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModelId();
}
