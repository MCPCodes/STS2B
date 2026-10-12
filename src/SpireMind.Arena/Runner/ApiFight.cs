using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SpireMind.Core;
using SpireMind.Core.Brain;

namespace SpireMind.Arena;

/// <summary>
/// One fight played by the mod's own brain (<c>--brain api</c>): <see cref="ApiBrain"/>, the
/// program that plays the real game on the API credit, unchanged, against a new
/// <see cref="ArenaGame"/>. It is wired the way the game wires it
/// (<c>src/SpireMind/Game/BrainWiring.cs</c>): the brain calls the same <see cref="BotTools"/>
/// in-process through <see cref="BotToolsHost"/>, builds its own system prompt with
/// <see cref="PromptBuilder"/>, reads its settings before every request, counts its spend in a
/// <see cref="SpendTracker"/>, and writes its transcript and fight log. So the requests are the
/// game's: the same system text, tools, thinking field, effort by room, max_tokens by effort,
/// caching and conversation handling. No Claude Code and no HTTP bridge are involved.
/// <para>
/// Where this differs from the game's wiring, and why:
/// <list type="bullet">
/// <item><b>No <see cref="BrainController"/>.</b> The controller picks between the API brain and
/// the subscription fallback and owns the chat commands. Its fallback would start Claude Code
/// on the subscription when the API brain stops for its cap, which a test must never do, and a
/// fight has no /start or /model. The brain is made and started directly, as the dev host's
/// smoke test does.</item>
/// <item><b>The client is wrapped</b> by <see cref="ApiRecorder"/>, which passes everything
/// through, counts, and stops the brain once the fight is over or the batch's budget is
/// reached.</item>
/// <item><b>The notes are a throwaway copy</b> (<see cref="ApiNotes"/>), and the spend tracker,
/// transcript, fight log and notes lock live in the fight's temp folder, never in
/// <c>~/.config/spiremind</c> or the repo.</item>
/// <item><b>The settings are fixed for the fight</b>, with the monthly cap replaced by what is
/// left of the batch's budget, so the brain's own cap check holds the fight to it.</item>
/// <item><b>The arena's facts</b> reach the model as one line of the mod in the chat
/// (<see cref="ArenaNote"/>), which the brain itself puts under "Recent chat" in the first
/// message of every conversation. The system prompt is not touched.</item>
/// <item><b>The tools' two waits</b> are the arena's (3 s and 5 s, as for Claude Code), since
/// the simulator settles at once, and the chat persona is the arena's small one.</item>
/// </list>
/// </para>
/// <see cref="RunAsync"/> returns the fight's result line. The temp folder is deleted whatever
/// happens; what the brain wrote there is in the result (<c>api.notes_written</c>) and its
/// transcript is copied to <see cref="TranscriptPath"/>.
/// </summary>
public sealed class ApiFight
{
    private const string TempPrefix = "spiremind-arena-api-";

    public required Scenario Scenario { get; init; }

    /// <summary>The model the brain asks for: a full id, as <see cref="BrainSettings.ResolveModel"/> gives it.</summary>
    public required string Model { get; init; }

    /// <summary>The brain's settings for the batch (<see cref="ApiSettings"/>): the game's own,
    /// with what the command line overrides. They need hold no key: the brain never reads it,
    /// only the client does. The model and the cap are set per fight.</summary>
    public required BrainSettings Settings { get; init; }

    /// <summary>The Messages API client the brain talks through: Core's <see cref="ClaudeClient"/>
    /// (with the key Core read, or over a stand-in transport that reaches no network).</summary>
    public required IClaudeApi Api { get; init; }

    /// <summary>The checkout: the folder the brain's prompt is built from (as the game's
    /// <c>repo</c>) and the notes are copied from.</summary>
    public required string Repo { get; init; }

    /// <summary>The batch's budget; null for a fight by itself with no limit but <see cref="Settings"/>' cap.</summary>
    public ApiBudget? Budget { get; init; }

    /// <summary>The effort the brain's settings give this fight's room: written to the result
    /// as its effort, and held against what the requests really carried.</summary>
    public required string Effort { get; init; }

    public ExperimentOptions Experiments { get; init; } = new();

    public string Experiment { get; init; } = ExperimentLabel.Api;

    /// <summary>For a position run: the checked position the brain takes the fight over at.</summary>
    public PositionStart? Position { get; init; }

    public int PositionRun { get; init; } = 1;

    public int? DealSeed { get; init; }

    /// <summary>How many requests the brain may still send once the fight is over (<c>--after-fight</c>).</summary>
    public int AfterFight { get; init; } = ArenaOptions.DefaultAfterFight;

    /// <summary>How long the fight may take before the brain is stopped and it counts as a timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(ArenaOptions.DefaultTimeoutMinutes);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The tools' waits, as for Claude Code (<see cref="ClaudeFight.SettleTimeout"/>).</summary>
    public TimeSpan SettleTimeout { get; init; } = ClaudeFight.DefaultSettleTimeout;

    public TimeSpan MaxWait { get; init; } = ClaudeFight.DefaultMaxWait;

    /// <summary>For the checks: the simulator's and the waiter's timings, shortened. Null leaves them as they are.</summary>
    public TimeSpan? ActionDelay { get; init; }

    public TimeSpan? WaiterPoll { get; init; }

    public TimeSpan? WaiterSettle { get; init; }

    /// <summary>For the checks only: the brain's hold on a repeated wait (a minute in the game),
    /// so a scripted model that stops playing is given up on in a moment. Null leaves the brain as it is.</summary>
    public TimeSpan? WaitHold { get; init; }

    /// <summary>Where the brain's own transcript of the fight is copied to, or null for nowhere.</summary>
    public string? TranscriptPath { get; init; }

    /// <summary>Where the key came from, for the result ("file", "environment", "fake"); never the key.</summary>
    public string? KeySource { get; init; }

    /// <summary>After <see cref="RunAsync"/>: why the batch should start no more fights (the
    /// budget, the key, the credit), or null.</summary>
    public string? BatchStop { get; private set; }

    /// <summary>
    /// The arena's facts, as the one line of the mod in the chat that carries them to the model.
    /// The game's brain puts the chat's last lines under "## Recent chat" in the first message of
    /// every conversation, the mod's own as "(mod) ...", which is how a line the mod announces
    /// in the real game reaches the model too. This is the only text a model gets here that the
    /// game doesn't give it. A position run adds the sentence that the fight is under way.
    /// </summary>
    public static string ArenaNote(int? positionTurn = null) =>
        "SpireMind arena: this is one practice fight in a simulator of the game's combat (v0.107.1), with the game's rules, snapshots and tools. " +
        "There is no map, reward or run around it: win it, with as much HP left as you can. " +
        "Card, power and potion texts are worded by the arena, so go by the snapshot where it and your memory differ. " +
        string.Create(CultureInfo.InvariantCulture, $"The fight counts as lost if it isn't won by the end of turn {Tuning.MaxTurns}. ") +
        $"When it is over the screen is `{ArenaGame.OverScreen}` and nothing follows." +
        (positionTurn is { } turn
            ? string.Create(CultureInfo.InvariantCulture, $" This fight is already under way: you take it over at the start of turn {turn}, and the turns before it cannot be changed.")
            : "");

    /// <summary>
    /// Plays the fight and returns its result line. Nothing that goes wrong with the brain or
    /// the API throws: it comes back as <see cref="Outcomes.Error"/>. Only cancelling
    /// <paramref name="ct"/> does, after the brain has been stopped and the temp folder deleted.
    /// </summary>
    public async Task<FightResult> RunAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var clock = Stopwatch.StartNew();
        var notes = new List<string>();
        void Note(string line)
        {
            lock (notes)
                if (notes.Count < 60) notes.Add(line);
        }

        string? folder = null;
        ApiBrain? brain = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            folder = Directory.CreateTempSubdirectory(TempPrefix).FullName;
            var configDir = Path.Combine(folder, "config");

            var game = NewGame();
            var log = Position is null ? null : new ActionLog(game, Position.Turn);
            IGameBridge bridge = (IGameBridge?)log ?? game;
            var versioner = new SnapshotVersioner();
            var waiter = WaiterPoll is null && WaiterSettle is null
                ? new DecisionWaiter(bridge, versioner)
                : new DecisionWaiter(bridge, versioner) { PollInterval = WaiterPoll ?? TimeSpan.FromMilliseconds(150), SettleTime = WaiterSettle ?? TimeSpan.FromMilliseconds(300) };
            var chat = ClaudeFight.NewChat();
            var tools = new BotTools(bridge, waiter, versioner, chat) { ActSettleTimeout = SettleTimeout, MaxWait = MaxWait };

            // The arena's facts, said by the mod in the chat before the brain looks at it.
            chat.Announce(ArenaNote(Position?.Turn));

            var copy = ApiNotes.CopyFrom(Repo, Path.Combine(folder, "notes"));
            // The cap the brain itself checks before every request: what is left of the batch's budget.
            var cap = Budget?.Left ?? Settings.MonthlyBudgetUsd;
            var settings = Settings with { Model = Model, MonthlyBudgetUsd = cap };
            var spend = new SpendTracker(configDir, line => Note($"spend: {line}"), "arena");
            var transcript = new Transcript(configDir, Note);
            var fights = new FightLog(configDir, Note);
            var recorder = new ApiRecorder(Api, () => game.IsOver, AfterFight, Budget) { Fight = Scenario.Id };
            var said = new List<string>();
            chat.LineAdded += line =>
            {
                if (line.Kind == ChatLineKind.Bot)
                    lock (said) said.Add(line.Text);
            };
            var announced = new List<string>();
            var brainLog = new List<string>();

            // As BrainWiring makes it, less what a fight has no use for (the co-op gate and the party plan).
            var host = new BotToolsHost(tools);
            var notesTools = new NotesTools(copy.Root, options: new NotesOptions { LockFile = Path.Combine(folder, "locks", "notes.lock") });
            void Announce(string line)
            {
                lock (announced) announced.Add(line);
                chat.Announce(line);
            }
            void Log(string line)
            {
                lock (brainLog) brainLog.Add(line);
            }
            // Every knob of the brain is left as the game has it. Only the checks shorten the one
            // wait that would make a scripted model that stops playing take minutes.
            brain = WaitHold is { } hold
                ? new ApiBrain(recorder, host, notesTools, Repo, () => settings, spend, character: () => chat.BotName, multiplayer: () => false,
                    transcript: transcript, recentChat: () => RecentChat(chat), announce: Announce, log: Log, fights: fights)
                {
                    RepeatWaitHold = hold,
                    LocalWaitPause = TimeSpan.FromMilliseconds(Math.Max(1, hold.TotalMilliseconds / 4)),
                }
                : new ApiBrain(recorder, host, notesTools, Repo, () => settings, spend, character: () => chat.BotName, multiplayer: () => false,
                    transcript: transcript, recentChat: () => RecentChat(chat), announce: Announce, log: Log, fights: fights);
            recorder.Stop = brain.Abort;

            var stopped = new TaskCompletionSource<(BrainStopReason Reason, string Message)>(TaskCreationOptions.RunContinuationsAsynchronously);
            brain.Stopped += (reason, message) => stopped.TrySetResult((reason, message));
            brain.Start();

            var timedOut = false;
            double? fightSeconds = null;
            while (!stopped.Task.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                if (fightSeconds is null && game.IsOver)
                    fightSeconds = clock.Elapsed.TotalSeconds;
                if (clock.Elapsed >= Timeout)
                {
                    timedOut = !game.IsOver;
                    await brain.StopAsync();
                    break;
                }
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await Task.WhenAny(stopped.Task, Task.Delay(PollInterval, tick.Token));
                tick.Cancel();
            }
            var (stopReason, stopMessage) = await stopped.Task;
            if (fightSeconds is null && game.IsOver)
                fightSeconds = clock.Elapsed.TotalSeconds;
            fightSeconds ??= clock.Elapsed.TotalSeconds;

            // The brain's own fight line: its log only closes a fight on one of the game's own
            // screens, so it is shown a game over built from where the simulator stands.
            var fightLine = CloseFightLog(fights, game);
            SaveTranscript(configDir, recorder, stopReason, stopMessage);

            lock (brainLog)
                foreach (var line in brainLog.Where(Telling).TakeLast(20))
                    Note(line);
            foreach (var problem in recorder.CountingProblems)
                Note($"recorder: {problem}");
            return Result(game, log, recorder, spend, copy, startedAt, fightSeconds.Value, timedOut, stopReason, stopMessage, cap, fightLine, said, announced, notes);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FightResult
            {
                ScenarioId = Scenario.Id,
                Tier = Scenario.Tier,
                Summary = Scenario.Summary,
                Seed = Scenario.Seed,
                Model = Model,
                Effort = Effort,
                Experiment = Experiment,
                Brain = Brains.Api,
                Position = Position is null ? null : PositionOf(null),
                Outcome = Outcomes.Error,
                Error = $"{ex.GetType().Name}: {ex.Message}",
                Notes = notes,
                StartedAt = startedAt,
            };
        }
        finally
        {
            // Whatever ended the fight, no request may go on in the background.
            if (brain is { IsRunning: true })
            {
                try { await brain.StopAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
                catch (Exception) { brain.Abort(); }
            }
            DeleteFolder(folder);
        }
    }

    /// <summary>The scenario's fight: at its first turn, or for a position run at the position.</summary>
    internal ArenaGame NewGame() =>
        Position is not null ? Position.NewGame(Experiments, DealSeed)
        : ActionDelay is { } delay ? new ArenaGame(Scenario) { Experiments = Experiments, ActionDelay = delay }
        : new ArenaGame(Scenario) { Experiments = Experiments };

    /// <summary>The chat's last lines as the game hands them to the brain (BrainWiring.RecentChat):
    /// oldest first, the mod's own as "(mod) ...".</summary>
    internal static IReadOnlyList<string> RecentChat(ChatHub chat) =>
        chat.LinesSince(chat.LastSeq - 20)
            .Select(line => line.Kind == ChatLineKind.System ? $"(mod) {line.Text}" : $"{line.From}: {line.Text}")
            .ToArray();

    // The brain's log lines worth keeping in the result's notes: not the line per response, which
    // the counts cover, and not the one about the prompt's size.
    private static bool Telling(string line) =>
        !line.StartsWith("brain: system prompt built", StringComparison.Ordinal)
        && !(line.Contains(" cached ", StringComparison.Ordinal) && line.Contains(" written ", StringComparison.Ordinal) && line.Contains(" out ", StringComparison.Ordinal));

    // Shows the brain's fight log a game over made from the simulator's end state, so it closes
    // the fight and writes its line, and reads that line back. Null when it wrote none (the
    // brain never saw the fight's board).
    private static JsonObject? CloseFightLog(FightLog fights, ArenaGame game)
    {
        try
        {
            var over = game.IsOver;
            fights.Observe(new JsonObject
            {
                ["screen"] = over ? "game_over" : "menu",
                ["state"] = new JsonObject
                {
                    ["result"] = !over ? "unknown" : game.Won ? "victory" : "defeat",
                    ["hp"] = game.PlayerHp,
                    ["max_hp"] = game.PlayerMaxHp,
                },
            });
            if (!File.Exists(fights.FilePath))
                return null;
            var line = File.ReadLines(fights.FilePath).LastOrDefault(l => l.Contains("\"kind\":\"fight\"", StringComparison.Ordinal));
            return line is null ? null : JsonNode.Parse(line) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // The brain's own transcript (requests without the system text, responses with their thinking
    // summaries, usage and cost) into the batch's folder, under one line of the arena's.
    private void SaveTranscript(string configDir, ApiRecorder recorder, BrainStopReason reason, string message)
    {
        if (TranscriptPath is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(TranscriptPath))!);
            using var writer = new StreamWriter(TranscriptPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.WriteLine(new JsonObject
            {
                ["type"] = "arena",
                ["note"] = $"{Scenario.Id} played by the mod's own brain on the API ({Model}); the lines below are the brain's own transcript, one per exchange" +
                           (Position is null ? "" : $"; position run {PositionRun}, taken over at the start of turn {Position.Turn}"),
                ["requests"] = recorder.Responses,
                ["stop"] = reason.ToString(),
                ["stop_message"] = message,
                ["stopped_for"] = recorder.StoppedFor,
            }.ToJsonString());
            var folder = Path.Combine(configDir, "transcripts");
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
                    foreach (var line in File.ReadLines(file))
                        writer.WriteLine(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The transcript is only a convenience.
        }
    }

    private PositionInfo PositionOf(ActionLog? log) => new()
    {
        Turn = Position!.Turn,
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
    };

    // The result line from what the game, the recorder and the brain's own spend tracker say.
    // The game decides first: a fight played to the end is won or lost however the brain stopped.
    private FightResult Result(ArenaGame game, ActionLog? log, ApiRecorder recorder, SpendTracker spend, ApiNotes copy, DateTime startedAt,
        double fightSeconds, bool timedOut, BrainStopReason stopReason, string stopMessage, decimal cap, JsonObject? fightLine,
        List<string> said, List<string> announced, List<string> notes)
    {
        var over = game.IsOver;
        string outcome;
        string? error = null;
        if (over)
        {
            outcome = game.Won ? Outcomes.Won : Outcomes.Lost;
        }
        else if (timedOut)
        {
            outcome = Outcomes.Timeout;
        }
        else
        {
            outcome = Outcomes.Error;
            switch (stopReason)
            {
                case BrainStopReason.UserStop when recorder.StoppedFor == ApiRecorder.Budget:
                case BrainStopReason.MonthlyCap:
                    error = string.Create(CultureInfo.InvariantCulture,
                        $"The batch's budget (--max-usd) was reached during this fight, so the brain was stopped: the fight had ${cap:0.00} of it left when it started and cost ${spend.RunTotal:0.00}.");
                    BatchStop = "the batch's budget (--max-usd) was reached during a fight";
                    break;
                case BrainStopReason.Unauthorized or BrainStopReason.OutOfCredit or BrainStopReason.SpendLimit or BrainStopReason.NoKey:
                    error = $"The API turned the brain away ({stopReason}): {stopMessage}";
                    BatchStop = $"the API turned the brain away ({stopReason}: {stopMessage})";
                    break;
                case BrainStopReason.Error when recorder.EndTurnsInRow > 1 && recorder.Failures.Count == 0 && stopMessage.Contains("without playing", StringComparison.Ordinal):
                    // The model ended its turn again and again and the brain's own "Keep playing"
                    // didn't bring it back: it gave the fight up, as a session that ends five times does.
                    outcome = Outcomes.Abandoned;
                    break;
                default:
                    error = $"The brain stopped before the fight was over ({stopReason}): {stopMessage}";
                    break;
            }
        }

        // A brain that never acted on the fight didn't play it: that says nothing about the model.
        var (startTurn, startCards) = Position is null ? (1, 0) : (Position.Turn, Position.CardsPlayed);
        if (outcome is Outcomes.Abandoned or Outcomes.Timeout && recorder.GameCalls == 0 && game.Turn <= startTurn && game.CardsPlayed == startCards)
        {
            error = (outcome == Outcomes.Timeout
                        ? "The time limit passed before the brain made one move in the fight"
                        : "The brain stopped without one move in the fight")
                    + $" ({recorder.Responses} {(recorder.Responses == 1 ? "request" : "requests")} answered), so the fight wasn't played.";
            outcome = Outcomes.Error;
        }

        // What the requests really carried, against what the settings planned for this room.
        var used = recorder.Efforts.Keys.ToList();
        if (recorder.Responses > 0 && (used.Count != 1 || used[0] != Effort))
            notes.Add($"The requests were sent at effort {string.Join(", ", recorder.EffortOrder)}, where the settings give this fight's room {Effort}.");
        if (recorder.Usd != spend.RunTotal)
            notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"The brain's spend tracker counted ${spend.RunTotal:0.0000} and the recorder ${recorder.Usd:0.0000} for the same responses."));

        var modelUsage = new JsonObject();
        foreach (var (id, (usage, usd, requests)) in recorder.ByModel.OrderBy(m => m.Key, StringComparer.Ordinal))
            modelUsage[id] = new JsonObject
            {
                ["inputTokens"] = usage.Input,
                ["outputTokens"] = usage.Output,
                ["cacheReadInputTokens"] = usage.CacheRead,
                ["cacheCreationInputTokens"] = usage.CacheWrite,
                ["cacheCreation1hInputTokens"] = usage.CacheWrite1h,
                ["costUSD"] = usd,
                ["requests"] = requests,
            };

        var written = copy.Written();
        var api = new JsonObject
        {
            ["model"] = Model,
            ["room"] = Brains.RoomOf(Scenario.Tier),
            ["settings"] = new JsonObject
            {
                ["effort"] = Settings.Effort,
                ["elite_effort"] = Settings.EffortFor(BrainSettings.EliteRoom),
                ["boss_effort"] = Settings.EffortFor(BrainSettings.BossRoom),
                ["max_tokens"] = Settings.MaxTokens is { } fixedTokens ? JsonValue.Create(fixedTokens) : JsonValue.Create("by effort"),
            },
            ["requests"] = recorder.Responses,
            ["efforts"] = Counts(recorder.Efforts),
            ["top_level_efforts"] = Counts(recorder.TopLevelEfforts),
            ["max_tokens"] = Counts(recorder.MaxTokens),
            ["thinking"] = Strings(recorder.Thinking),
            ["effort_messages"] = recorder.BetaRequests,
            ["conversations"] = recorder.Openings.Count,
            ["openings"] = Strings(recorder.Openings),
            ["keep_playing"] = recorder.Nudges,
            ["cut_offs"] = recorder.CutOffs,
            ["refusals"] = recorder.Refusals,
            ["failed_requests"] = Strings(recorder.Failures),
            ["after_fight_requests"] = recorder.AfterFightRequests,
            ["stop"] = stopReason.ToString(),
            ["stopped_for"] = recorder.StoppedFor ?? (timedOut ? "the time limit" : stopReason == BrainStopReason.UserStop ? "the arena" : "the brain itself"),
            ["stop_message"] = stopMessage,
            ["spend"] = new JsonObject
            {
                ["usd"] = spend.RunTotal,
                ["requests"] = spend.Calls,
                ["cap_usd"] = cap,
            },
            ["key_source"] = KeySource,
            ["prompt_chars"] = recorder.PromptChars,
            ["prompts"] = recorder.Prompts,
            ["tools"] = Strings(recorder.Tools),
            ["arena_note"] = ArenaNote(Position?.Turn),
            ["said"] = Strings(said),
            ["announced"] = Strings(announced),
            ["fight_log"] = fightLine,
            ["notes_copied"] = copy.Copied,
            ["notes_written"] = written,
        };

        return new FightResult
        {
            ScenarioId = Scenario.Id,
            Tier = Scenario.Tier,
            Summary = Scenario.Summary,
            Seed = Scenario.Seed,
            Model = Model,
            ModelsUsed = recorder.ByModel.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
            Effort = Effort,
            Experiment = Experiment,
            Brain = Brains.Api,
            Api = api,
            Position = Position is null ? null : PositionOf(log),
            Outcome = outcome,
            HpLeft = game.PlayerHp,
            MaxHp = game.PlayerMaxHp,
            DamageTaken = game.DamageTaken,
            Turns = game.Turn,
            CardsPlayed = game.CardsPlayed,
            RefusedActions = game.RefusedActions,
            ToolCalls = new Dictionary<string, int>(recorder.ToolCalls, StringComparer.Ordinal),
            ToolErrors = recorder.ToolErrors,
            ReadCalls = recorder.ReadCalls,
            ReadErrors = recorder.ReadErrors,
            WikiPages = recorder.WikiPages,
            OtherReads = recorder.OtherReads,
            WallSeconds = fightSeconds,
            // The brain's own way of carrying on: its "Keep playing" to a model that ended its turn.
            Resumes = recorder.Nudges,
            NumTurns = spend.Calls,
            // The brain's own figure, from its spend tracker in the fight's temp folder.
            TotalCostUsd = spend.RunTotal,
            Tokens = new TokenCounts(recorder.Usage.Input, recorder.Usage.CacheWrite, recorder.Usage.CacheRead, recorder.Usage.Output),
            TokensSource = "api",
            ModelUsage = modelUsage,
            Error = error,
            Notes = notes,
            StartedAt = startedAt,
            CliModel = recorder.ByModel.Keys.FirstOrDefault(),
            ResultSubtype = stopReason.ToString(),
            LastText = recorder.LastText,
            PromptHash = recorder.PromptHash,
            WikiHash = ArenaPrompt.WikiHash(Repo),
        };
    }

    private static JsonObject Counts(Dictionary<string, int> counts)
    {
        var json = new JsonObject();
        foreach (var (key, count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
            json[key] = count;
        return json;
    }

    private static JsonArray Strings(IEnumerable<string> items)
    {
        lock (items)
            return new JsonArray(items.Select(item => (JsonNode)JsonValue.Create(item)!).ToArray());
    }

    private static void DeleteFolder(string? folder)
    {
        if (folder is null)
            return;
        try { Directory.Delete(folder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* the system clears its temp folder itself */ }
    }
}
