using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// One fight played by one Claude Code session: a new <see cref="ArenaGame"/> behind its own
/// bridge on a free loopback port, and <c>claude -p</c> headless with that bridge as its only
/// MCP server, in an empty temp folder so no project settings or journals are picked up. Besides
/// the fight's tools the session has Read for the strategy folder of <see cref="Repo"/>, so it
/// can open the wiki's pages as the in-game brain can, and nothing that writes.
/// <para>
/// Claude Code headless ends as soon as the model answers without a tool call. The in-game
/// brain then starts the same session again (<see cref="SubscriptionBrain"/>), and so does a
/// fight: while it isn't over and the session ended by itself without an error, the session is
/// started again with <c>--resume</c> and <see cref="ArenaPrompt.ResumePrompt"/>, at most
/// <see cref="MaxResumes"/> times. Only a model that stops that often has abandoned its fight.
/// </para>
/// <see cref="RunAsync"/> follows the sessions' stream-json output and the game until the fight
/// is over, the session can't go on or the time limit passes, and always leaves nothing behind:
/// the child is stopped, the bridge closed, the temp folder deleted, and with it the folder
/// Claude Code kept the session in so that it could be resumed.
/// <para>
/// A position run (<c>arena position</c>) is the same with one difference: the game is not at
/// the scenario's first turn when the session starts but at <see cref="Position"/>, the start
/// of a later turn of a recorded fight, and what the session does on that turn is kept for the
/// result line (<see cref="ActionLog"/>).
/// </para>
/// </summary>
public sealed class ClaudeFight
{
    /// <summary>How often a session that ended before its fight was over is started again.</summary>
    public const int MaxResumes = 5;

    // How every fight's temp folder starts. Claude Code names the folder it keeps the fight's
    // sessions in after it, and only a folder with this in its name is ever deleted there.
    private const string TempPrefix = "spiremind-arena-";

    // Ports the real game's bridge uses; a fight never takes them even if the system offers one.
    private static readonly int[] ReservedPorts = [17772, 17773];

    // One fight at a time picks a port and starts listening on it, so two can't pick the same one.
    private static readonly object PortGate = new();

    public required Scenario Scenario { get; init; }

    /// <summary>The model, passed to <c>--model</c> as it is.</summary>
    public required string Model { get; init; }

    /// <summary>The effort the sessions are started with (<c>--effort</c>, or what
    /// <c>--effort-by-tier</c> names for the scenario's tier): passed to Claude Code and written
    /// to the result as it is.</summary>
    public required string Effort { get; init; }

    /// <summary><c>--brief</c>: the first session's prompt ends with the wiki pages of the
    /// fight's enemies (<see cref="WikiBrief"/>). The system prompt must then be the one built
    /// for it (<see cref="ArenaPrompt.Build"/> with brief), whose appendix says so.</summary>
    public bool Brief { get; init; }

    /// <summary>The simulator's experiment switches, handed to the fight as they are.</summary>
    public ExperimentOptions Experiments { get; init; } = new();

    /// <summary>The label of the run's experiment, written to the result
    /// (<see cref="FightResult.Experiment"/>).</summary>
    public string Experiment { get; init; } = ExperimentLabel.Base;

    /// <summary>For a position run: the checked position the session takes the fight over at
    /// (<see cref="PositionReplay.To"/>). Null plays the scenario from its first turn.</summary>
    public PositionStart? Position { get; init; }

    /// <summary>The run's number among the position's runs, from 1.</summary>
    public int PositionRun { get; init; } = 1;

    /// <summary><c>--reshuffle</c>: the seed this run's unseen cards and coming random numbers
    /// are dealt from (<see cref="PositionStart.DealSeed"/>). Null keeps the recorded deal.</summary>
    public int? DealSeed { get; init; }

    /// <summary>The Claude Code binary, by absolute path.</summary>
    public required string ClaudePath { get; init; }

    /// <summary>The system text appended to Claude Code's own (<see cref="ArenaPrompt.Build"/>).</summary>
    public required string SystemPrompt { get; init; }

    /// <summary>The checkout <see cref="SystemPrompt"/> was built from, by its real path
    /// (<see cref="ArenaPrompt.FindRepo"/>). The prompt's appendix names its wiki folder, so the
    /// session's Read rule is made for the same one (<see cref="ClaudeCli.Arguments"/>).</summary>
    public required string Repo { get; init; }

    public string UserPrompt { get; init; } = ArenaPrompt.UserPrompt;

    /// <summary>What a session that ended before the fight was over is started again with.</summary>
    public string ResumePrompt { get; init; } = ArenaPrompt.ResumePrompt;

    /// <summary>Changes to the inherited environment (<see cref="ClaudeCli.ChildEnvironment"/>).</summary>
    public required IReadOnlyDictionary<string, string?> Environment { get; init; }

    /// <summary>How long the fight may take, all its sessions together, before the session is
    /// stopped and it counts as a timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(ArenaOptions.DefaultTimeoutMinutes);

    /// <summary>
    /// Once the fight is over, how long the session may stay quiet before it is stopped. Each
    /// line it prints restarts the wait, up to three times this in all: the model usually needs
    /// one more reply to say it is done, and the session's result line (cost, turns, usage) only
    /// comes when it ends by itself. Keep it well above <see cref="SettleTimeout"/> and
    /// <see cref="MaxWait"/>: a tool call in flight prints nothing for that long.
    /// </summary>
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(ArenaOptions.DefaultGraceSeconds);

    /// <summary>How long a session gets to quit after SIGINT before it is killed.</summary>
    public TimeSpan KillGrace { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the game and the session are looked at.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long act waits for the next decision; the game's own wait is 20 s. The simulator
    /// settles in well under a second, so a few seconds are plenty, and an action that changes
    /// nothing (the end screen's "done") comes back after this long.
    /// </summary>
    public TimeSpan SettleTimeout { get; init; } = DefaultSettleTimeout;

    /// <summary>The longest wait_for_decision waits. Nothing in the arena takes long (no
    /// teammates, no pauses), so a wait never needs the game's four minutes.</summary>
    public TimeSpan MaxWait { get; init; } = DefaultMaxWait;

    public static readonly TimeSpan DefaultSettleTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(5);

    /// <summary>Where the sessions' stream-json output is saved line by line, or null for nowhere.</summary>
    public string? TranscriptPath { get; init; }

    /// <summary>Written to the result as <see cref="FightResult.PromptHash"/>.</summary>
    public string? PromptHash { get; init; }

    public IProcessLauncher Launcher { get; init; } = ProcessLauncher.Instance;

    /// <summary>Gets the subscription's usage windows whenever Claude Code reports them, while
    /// the fight runs. Called on the task that reads the session's output.</summary>
    public Action<UsageWindows>? OnUsage { get; init; }

    /// <summary>Asked at every look at the session: a sentence stops the session at once and
    /// the fight (if it isn't over) is an error with that sentence. For a batch that must stop
    /// everything, as when Claude Code starts charging extra usage.</summary>
    public Func<string?>? Halt { get; init; }

    /// <summary>Every session that is running right now, so they can all be killed when the arena exits.</summary>
    public static LiveChildren Children { get; } = new();

    /// <summary>What a fight's sessions have come to so far.</summary>
    private sealed class Progress
    {
        /// <summary>Why the fight can't go on, when it can't.</summary>
        public string? Error;

        public bool TimedOut;

        /// <summary>How the last session ended ("exited", "interrupted", "killed") and its exit code.</summary>
        public string? CliExit;
        public int? ExitCode;

        /// <summary>When the game said the fight was over, and how long it had taken by then.</summary>
        public DateTime? OverAt;
        public double? FightSeconds;

        public int Resumes;
    }

    /// <summary>
    /// Plays the fight and returns its result line. Nothing that goes wrong with a session
    /// throws: it comes back as <see cref="Outcomes.Error"/>. Only cancelling
    /// <paramref name="ct"/> does, after the child has been killed and everything cleaned up.
    /// </summary>
    public async Task<FightResult> RunAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        var game = NewGame();
        var watch = new StreamWatch { OnUsage = OnUsage, StrategyRoot = ClaudeCli.StrategyFolder(Repo) };
        var progress = new Progress();
        var sessionId = Guid.NewGuid().ToString();

        // A position run notes what its session does on the position's turn, where the actions reach the game.
        var log = Position is null ? null : new ActionLog(game, Position.Turn);

        BridgeServer? server = null;
        string? folder = null;
        StreamWriter? transcript = null;
        WikiBrief? brief = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            server = StartBridge((IGameBridge?)log ?? game, SettleTimeout, MaxWait, watch.Note);
            folder = Directory.CreateTempSubdirectory(TempPrefix).FullName;
            transcript = OpenTranscript();
            if (Position is not null)
                Save(transcript, new JsonObject
                {
                    ["type"] = "arena",
                    ["note"] = $"position run {PositionRun}: the session takes over {Scenario.Id} at the start of turn {Position.Turn}, after {Position.Actions.Count} recorded actions" +
                               (DealSeed is { } seed ? $"; the unseen cards and the random numbers to come were dealt again from seed {seed}" : "; the recorded deal goes on"),
                    ["state_hash"] = Position.StateHash,
                }.ToJsonString());

            // Under --brief the first message ends with the pages of the enemies the fight's
            // first snapshot names. They are read off a second, plain game of the scenario
            // (a scenario's fight starts the same way every time), so the fight being played
            // isn't looked at before the model looks at it. A resumed session has the pages
            // in its conversation already.
            var firstPrompt = UserPrompt;
            if (Brief)
            {
                brief = WikiBrief.For(Repo, WikiBrief.Enemies(new ArenaGame(Scenario)));
                firstPrompt = ArenaPrompt.UserPromptWith(brief, UserPrompt);
                foreach (var enemy in brief.Missing)
                    watch.Note($"--brief: the wiki has no page for {enemy}");
                Save(transcript, new JsonObject
                {
                    ["type"] = "arena",
                    ["note"] = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                        $"--brief: the first message ended with {brief.Pages.Count} wiki {(brief.Pages.Count == 1 ? "page" : "pages")} ({brief.Chars} characters)"),
                    ["brief_pages"] = new JsonArray(brief.Pages.Select(page => (JsonNode)JsonValue.Create(page)!).ToArray()),
                    ["brief_missing"] = new JsonArray(brief.Missing.Select(enemy => (JsonNode)JsonValue.Create(enemy)!).ToArray()),
                }.ToJsonString());
            }

            while (true)
            {
                var resume = progress.Resumes > 0;
                var arguments = ClaudeCli.Arguments(resume ? ResumePrompt : firstPrompt, sessionId, resume, Model, Effort, SystemPrompt, server.Port, Repo);
                await RunSessionAsync(new ChildProcessSpec(ClaudePath, arguments, folder, Environment), game, watch, clock, transcript, progress, ct);
                if (!ShouldResume(game, watch, clock, progress))
                    break;

                progress.Resumes++;
                watch.NextSession();
                Save(transcript, new JsonObject
                {
                    ["type"] = "arena",
                    ["note"] = $"the session ended before the fight was over; resume {progress.Resumes} of at most {MaxResumes} follows",
                }.ToJsonString());
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            progress.Error = ex is System.ComponentModel.Win32Exception
                ? $"Couldn't start {ClaudePath}: {ex.Message}"
                : $"{ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            server?.Dispose();
            try { transcript?.Dispose(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException) { /* the transcript is only a convenience */ }
            DeleteFolder(folder);
            DeleteSessions(folder, watch);
        }

        progress.FightSeconds ??= clock.Elapsed.TotalSeconds;
        return Result(game, watch, startedAt, progress, brief, log);
    }

    /// <summary>The scenario's fight, with the simulator's experiment switches handed to it: at
    /// its first turn, or for a position run at the position (<see cref="PositionStart.NewGame"/>).</summary>
    internal ArenaGame NewGame() =>
        Position is null ? new ArenaGame(Scenario) { Experiments = Experiments } : Position.NewGame(Experiments, DealSeed);

    // One Claude Code process, from its start to its end: by itself, or stopped because the
    // fight is over and it has gone quiet, it can't go on, or the fight's time is up.
    private async Task RunSessionAsync(ChildProcessSpec spec, ArenaGame game, StreamWatch watch, Stopwatch clock,
        StreamWriter? transcript, Progress progress, CancellationToken ct)
    {
        IChildProcess? child = null;
        var readers = Task.CompletedTask;
        progress.CliExit = null;
        progress.ExitCode = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            child = Launcher.Start(spec);
            Children.Add(child);
            var session = child;
            readers = Task.WhenAll(
                Task.Run(() => ReadLinesAsync(session.StandardOutput, line =>
                {
                    watch.OnOutput(line);
                    Save(transcript, line);
                })),
                Task.Run(() => ReadLinesAsync(session.StandardError, watch.OnError)));

            while (!child.Exited.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                var now = DateTime.UtcNow;
                NoteTheEnd(game, clock, progress);

                if ((watch.Failure ?? Halt?.Invoke()) is { } failure)
                {
                    progress.Error = failure;
                    progress.CliExit = await StopAsync(child);
                    break;
                }
                if (progress.OverAt is { } over)
                {
                    var quietSince = watch.LastOutputAt > over ? watch.LastOutputAt : over;
                    if (now - quietSince >= ExitGrace || now - over >= ExitGrace * 3)
                    {
                        progress.CliExit = await StopAsync(child);
                        break;
                    }
                }
                else if (clock.Elapsed >= Timeout)
                {
                    progress.TimedOut = true;
                    progress.CliExit = await StopAsync(child);
                    break;
                }

                using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await Task.WhenAny(child.Exited, Task.Delay(PollInterval, tick.Token));
                tick.Cancel();
            }

            progress.CliExit ??= "exited";
            if (child.Exited.IsCompleted)
                progress.ExitCode = await child.Exited;
            await DrainAsync(readers);
            NoteTheEnd(game, clock, progress);
            // The init line may have come with the session's last breath.
            progress.Error ??= watch.Failure;
        }
        finally
        {
            if (child is not null)
            {
                // Dispose kills a child that is still running, with everything it started.
                try { child.Dispose(); }
                catch (Exception) { /* nothing more can be done about it */ }
                Children.Remove(child);
            }
            await DrainAsync(readers);
        }
    }

    // Remembers when the game first said the fight was over.
    private static void NoteTheEnd(ArenaGame game, Stopwatch clock, Progress progress)
    {
        if (progress.OverAt is null && game.IsOver)
        {
            progress.OverAt = DateTime.UtcNow;
            progress.FightSeconds = clock.Elapsed.TotalSeconds;
        }
    }

    // Whether the session that just ended is started again. It is when the fight isn't over
    // and the session ended the way it does when the model answers without a tool call: by
    // itself, with an init line and without an error. Not after the last allowed resume, not
    // while Claude Code says requests are refused, and not once the fight's time is up.
    private bool ShouldResume(ArenaGame game, StreamWatch watch, Stopwatch clock, Progress progress)
    {
        if (game.IsOver || progress.Error is not null || progress.TimedOut || progress.CliExit != "exited")
            return false;
        if (!EndedByItself(watch, progress.ExitCode) || watch.Limit is not null || progress.Resumes >= MaxResumes)
            return false;
        if (Halt?.Invoke() is { } halted)
        {
            progress.Error = halted;
            return false;
        }
        if (clock.Elapsed >= Timeout)
        {
            progress.TimedOut = true;
            return false;
        }
        return true;
    }

    // A session that started, said what it had to say and ended without an error.
    private static bool EndedByItself(StreamWatch watch, int? exitCode) =>
        watch.InitSeen && !watch.ResultIsError && (watch.ResultSeen || exitCode == 0);

    // The result line from what the game and the sessions' output say. The game decides first: a
    // fight that was played to the end is won or lost however the session ended afterwards.
    private FightResult Result(ArenaGame game, StreamWatch watch, DateTime startedAt, Progress progress, WikiBrief? brief, ActionLog? log)
    {
        var over = game.IsOver;
        var error = progress.Error;
        var exitCode = progress.ExitCode;
        // Claude Code's own word only (an error result, a refusing rate limit status, extra
        // usage), so it stands whether the fight was finished or not.
        var limit = watch.Limit;

        string outcome;
        if (over)
        {
            outcome = game.Won ? Outcomes.Won : Outcomes.Lost;
        }
        else if (error is not null)
        {
            outcome = Outcomes.Error;
        }
        else if (progress.TimedOut)
        {
            outcome = Outcomes.Timeout;
        }
        else if (limit is not null)
        {
            outcome = Outcomes.Error;
            error = $"The subscription's usage limit was hit: {limit}"
                    + (watch.ResultError is { } said && !limit.Contains(said, StringComparison.Ordinal) ? $" (the session ended with: {said})" : "");
        }
        else if (watch.ResultIsError || (!watch.ResultSeen && exitCode is not (null or 0)))
        {
            outcome = Outcomes.Error;
            error = watch.ResultError
                    ?? (exitCode is { } code ? $"Claude Code exited with code {code} before the fight was over" : "Claude Code failed before the fight was over")
                    + (watch.LastError is { } said ? $": {said}" : ".");
        }
        else if (!watch.InitSeen)
        {
            outcome = Outcomes.Error;
            error = "Claude Code ended without starting a session (no init line in its output).";
        }
        else
        {
            // It ended by itself with the fight unfinished, and no resume is left to give it.
            outcome = Outcomes.Abandoned;
        }

        // A session that never called one of the fight's tools didn't play it: the tools weren't
        // there for it (an MCP server still connecting) or the service kept it waiting. That says
        // nothing about how the model plays, so it is an error, to be played again, and not a
        // fight the model gave up or ran out of time on.
        // (Where the fight started: its first turn, or the position a position run took over at.)
        var (startTurn, startCards) = Position is null ? (1, 0) : (Position.Turn, Position.CardsPlayed);
        if (outcome is Outcomes.Abandoned or Outcomes.Timeout && watch.GameToolCalls == 0 && game.Turn <= startTurn && game.CardsPlayed == startCards)
        {
            error = (outcome == Outcomes.Timeout
                        ? "The time limit passed before the session made one call to the fight's tools"
                        : "The session ended without one call to the fight's tools")
                    + $" (MCP server {watch.McpStatus ?? "unknown"}), so the fight wasn't played.";
            outcome = Outcomes.Error;
        }
        else if (outcome == Outcomes.Error && progress.Resumes > 0)
        {
            error += $" (This was resume {progress.Resumes} of the session.)";
        }

        // A fight that was played to the end keeps its outcome; how the session ended is a remark.
        if (over && watch.ResultError is { } late)
            error ??= late;

        var usage = watch.Usage;
        var modelUsage = watch.ModelUsage;
        var windows = watch.Windows;
        return new FightResult
        {
            ScenarioId = Scenario.Id,
            Tier = Scenario.Tier,
            Summary = Scenario.Summary,
            Seed = Scenario.Seed,
            Model = Model,
            ModelsUsed = modelUsage?.Select(m => m.Key).ToArray() ?? [],
            Effort = Effort,
            Experiment = Experiment,
            Position = Position is null ? null : new PositionInfo
            {
                Turn = Position.Turn,
                Run = PositionRun,
                Reshuffle = DealSeed is not null,
                DealSeed = DealSeed,
                StateHash = Position.StateHash,
                HpAtStart = Position.Hp,
                ReplayedActions = Position.Actions.Count,
                SnapshotsChecked = Position.SnapshotsChecked,
                SourceFolder = Position.SourceFolder,
                SourceModel = Position.SourceModel,
                SourceOutcome = Position.SourceOutcome,
                SourceHpLeft = Position.SourceHpLeft,
                SourceLine = Position.SourceLine,
                Line = log?.Labels ?? [],
                LineIds = log?.Ids ?? [],
                LineComplete = log?.Complete ?? false,
            },
            Outcome = outcome,
            HpLeft = game.PlayerHp,
            MaxHp = game.PlayerMaxHp,
            DamageTaken = game.DamageTaken,
            Turns = game.Turn,
            CardsPlayed = game.CardsPlayed,
            RefusedActions = game.RefusedActions,
            ToolCalls = watch.ToolCalls,
            ToolErrors = watch.ToolErrors,
            ReadCalls = watch.ReadCalls,
            ReadErrors = watch.ReadErrors,
            WikiPages = watch.WikiPages,
            OtherReads = watch.OtherReads,
            BriefPages = brief?.Pages,
            BriefChars = brief?.Chars,
            BriefMissing = brief?.Missing ?? [],
            WallSeconds = progress.FightSeconds ?? 0,
            Resumes = progress.Resumes,
            CliDurationMs = watch.DurationMs,
            NumTurns = watch.NumTurns,
            TotalCostUsd = watch.TotalCostUsd,
            Tokens = usage?.Tokens,
            TokensSource = usage?.Source,
            ModelUsage = modelUsage,
            UsageFiveHour = windows?.FiveHour,
            UsageSevenDay = windows?.SevenDay,
            Error = error,
            UsageLimit = limit,
            ToolsProblem = over ? null : watch.ToolsProblem,
            Notes = watch.Notes,
            StartedAt = startedAt,
            CliModel = watch.CliModel,
            CliVersion = watch.CliVersion,
            ApiKeySource = watch.ApiKeySource,
            McpStatus = watch.McpStatus,
            ResultSubtype = watch.ResultSubtype,
            RateLimitStatus = windows?.Status,
            CliExit = progress.CliExit,
            ExitCode = exitCode,
            LastText = watch.LastText,
            PromptHash = PromptHash,
            WikiHash = ArenaPrompt.WikiHash(Repo),
        };
    }

    // Removes the folder Claude Code kept this fight's sessions in (they are only kept so the
    // session can be resumed). What goes wrong there is a note on the fight, never an error.
    private void DeleteSessions(string? folder, StreamWatch watch)
    {
        if (folder is null)
            return;
        try
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            // Where the child looks: the variable as the child gets it, when the arena changes it.
            var configDir = Environment.TryGetValue("CLAUDE_CONFIG_DIR", out var given) ? given ?? "" : null;
            var problem = ClaudeCli.DeleteSessionStore(folder, TempPrefix, home, configDir, out var found);
            // A session that never started kept nothing, so there is nothing to miss.
            if (problem is not null && (found || watch.Sessions > 0))
                watch.Note(problem);
        }
        catch (Exception ex)
        {
            watch.Note($"couldn't clear this fight's sessions ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>
    /// The fight's own bridge, wired as the dev host wires the fake game, on a port the system
    /// says is free. The port is released before the bridge binds it, so a lost race tries
    /// another. The self-check plays through the same wiring (<see cref="ToolPathCheck"/>).
    /// </summary>
    /// <param name="game">The fight, or for a position run the <see cref="ActionLog"/> in front of it.</param>
    /// <param name="note">Gets what the bridge logs about requests that failed.</param>
    internal static BridgeServer StartBridge(IGameBridge game, TimeSpan settleTimeout, TimeSpan maxWait, Action<string> note)
    {
        var versioner = new SnapshotVersioner();
        var waiter = new DecisionWaiter(game, versioner);
        var chat = new ChatHub
        {
            BotName = ArenaPrompt.Character,
            Persona = new JsonObject { ["name"] = "Ironclad", ["title"] = "The Ironclad", ["id"] = "IRONCLAD", ["tone"] = "A terse, grim soldier." },
        };
        var tools = new BotTools(game, waiter, versioner, chat) { ActSettleTimeout = settleTimeout, MaxWait = maxWait };

        lock (PortGate)
        {
            Exception? last = null;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var port = FreePort();
                if (ReservedPorts.Contains(port))
                    continue;
                // Only failures are worth keeping: the bridge logs each request that threw.
                var server = new BridgeServer(port, tools, chat, "arena", line =>
                {
                    if (!line.StartsWith("bridge listening", StringComparison.Ordinal))
                        note("bridge: " + line);
                });
                try
                {
                    server.Start();
                    return server;
                }
                catch (Exception ex) when (ex is HttpListenerException or SocketException)
                {
                    last = ex;
                    server.Dispose();
                }
            }
            throw new IOException($"Couldn't open a loopback port for the fight's bridge: {last?.Message ?? "no free port"}");
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    // Asks the session to quit like Ctrl+C, and kills it (with everything it started) when it
    // hasn't after KillGrace. Says which of the two it took.
    private async Task<string> StopAsync(IChildProcess child)
    {
        if (child.Exited.IsCompleted)
            return "exited";
        if (child.Interrupt() && await ExitsWithinAsync(child, KillGrace))
            return "interrupted";
        child.Kill();
        await ExitsWithinAsync(child, TimeSpan.FromSeconds(3));
        return "killed";
    }

    private static async Task<bool> ExitsWithinAsync(IChildProcess child, TimeSpan time)
    {
        try
        {
            await child.Exited.WaitAsync(time);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task ReadLinesAsync(System.Threading.Channels.ChannelReader<string> reader, Action<string> onLine)
    {
        await foreach (var line in reader.ReadAllAsync())
        {
            try { onLine(line); }
            catch (Exception) { /* one odd line mustn't end the reading */ }
        }
    }

    // Waits for the output readers once the child is gone. A grandchild holding the pipe open
    // can't keep the fight waiting.
    private static async Task DrainAsync(Task readers)
    {
        try { await readers.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception) { /* a timeout or a broken pipe: go on without the rest */ }
    }

    private StreamWriter? OpenTranscript()
    {
        if (TranscriptPath is null)
            return null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(TranscriptPath))!);
            return new StreamWriter(TranscriptPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // One line of the session's output into the transcript. Stream-json doesn't repeat the
    // system prompt, but a line that did would be left out rather than written to disk.
    private void Save(StreamWriter? transcript, string line)
    {
        if (transcript is null)
            return;
        try
        {
            lock (transcript)
                transcript.WriteLine(HoldsPrompt(line) ? "{\"type\":\"arena\",\"note\":\"a line holding the system prompt was left out\"}" : line);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The transcript is only a convenience.
        }
    }

    // True when a line of output is long enough to hold the system prompt and has its opening
    // words: the plain run of letters the prompt starts with, which JSON leaves as it is.
    private bool HoldsPrompt(string line)
    {
        var plain = 0;
        while (plain < SystemPrompt.Length && plain < 60 && (char.IsAsciiLetterOrDigit(SystemPrompt[plain]) || SystemPrompt[plain] == ' '))
            plain++;
        return plain >= 30 && line.Length >= SystemPrompt.Length / 2
               && line.Contains(SystemPrompt[..plain], StringComparison.Ordinal);
    }

    private static void DeleteFolder(string? folder)
    {
        if (folder is null)
            return;
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the system clears its temp folder itself */ }
    }
}

/// <summary>The Claude Code sessions running right now. Fights add and remove their own; the
/// arena kills whatever is left when it exits, so no session outlives it.</summary>
public sealed class LiveChildren
{
    private readonly ConcurrentDictionary<IChildProcess, byte> _children = new();

    public int Count => _children.Count;

    public void Add(IChildProcess child) => _children[child] = 0;

    public void Remove(IChildProcess child) => _children.TryRemove(child, out _);

    /// <summary>Kills every session still running, with everything it started.</summary>
    public void KillAll()
    {
        foreach (var child in _children.Keys)
            child.Kill();
    }
}
