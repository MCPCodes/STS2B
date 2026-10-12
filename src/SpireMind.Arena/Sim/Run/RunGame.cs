using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// One run behind the same seam the real game sits behind, as <see cref="ArenaGame"/> is for a
/// single fight: the bot's tools (get_state, wait_for_decision, act, act_sequence, get_deck)
/// drive it unchanged. It adds to <see cref="Run"/> a lock, since snapshots and actions arrive
/// from several threads, and a short busy spell after every action, so that a model has to
/// wait for its next decision as it does in the game. It can also write the run's save, the
/// list of accepted action ids, to a file as it grows.
/// </summary>
public sealed class RunGame : IGameBridge
{
    private readonly object _gate = new();
    private readonly Run _run;
    private DateTime _busyUntil = DateTime.MinValue;
    private GameSnapshot? _busy;
    private int _refused;
    private int _written;

    /// <summary>A game around a run. The game owns the run from here on.</summary>
    public RunGame(Run run) => _run = run;

    /// <summary>
    /// The experiment switches the run's fights are shown with; all off unless the runner sets
    /// them, and with all of them off every snapshot is the run's own. See <see cref="ArenaGame.Experiments"/>.
    /// </summary>
    public ExperimentOptions Experiments { get; init; } = new();

    /// <summary>How long the snapshot stays busy after an action, and twice that after end_turn (the
    /// enemy turn). Zero for the scripted players, which never wait.</summary>
    public TimeSpan ActionDelay { get; init; } = ArenaGame.DefaultActionDelay;

    /// <summary>
    /// When set, the run's save is kept in this file: a first line that names the run's seed,
    /// then every accepted action id on a line of its own, each written the moment it was
    /// accepted. <see cref="ReadActionLog"/> reads it back, and <see cref="Run.Replay"/> builds
    /// the run again from it. A file that is already there is carried on.
    /// </summary>
    public string? ActionLogPath { get; init; }

    /// <summary>What went wrong the last time the save could not be written, or null. The run goes on regardless.</summary>
    public string? ActionLogError { get { lock (_gate) return _logError; } }
    private string? _logError;

    /// <summary>
    /// The live run, for a scripted player: look, or copy it with <see cref="Run.Clone"/> (or its
    /// fight with <see cref="Fight.CloneDeterminized"/>) and play the copy. Never act on it
    /// directly, and only touch it from the thread that drives this game. Whoever shares the
    /// game with other threads (a runner whose session calls the tools over HTTP) takes
    /// <see cref="Copy"/> instead.
    /// </summary>
    public Run Run => _run;

    /// <summary>
    /// A copy of the run as it stands, taken under the game's lock: safe while tool calls arrive
    /// on other threads, and its own run from then on (the game does not move it, and playing it
    /// does not move the game). For a result line or a floor log while a session is live, and
    /// for playing a fight on from where the game stands.
    /// </summary>
    public Run Copy() { lock (_gate) return _run.Clone(); }

    /// <summary>The run is decided (after a death the game over screen may still be up).</summary>
    public bool IsOver { get { lock (_gate) return _run.IsOver; } }

    /// <summary>The end snapshot is up.</summary>
    public bool AtEnd { get { lock (_gate) return _run.AtEnd; } }

    /// <summary><see cref="Run.Result"/>: how the run ended, or null while it goes on.</summary>
    public string? Result { get { lock (_gate) return _run.Result; } }

    /// <summary>Actions <see cref="ExecuteAsync"/> turned down: illegal ids, or anything sent while the run was busy.</summary>
    public int RefusedActions { get { lock (_gate) return _refused; } }

    /// <summary>The save so far: every accepted action id, in order.</summary>
    public IReadOnlyList<string> Actions { get { lock (_gate) return _run.Actions.ToList(); } }

    public Task<GameSnapshot> CaptureAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            if (DateTime.UtcNow < _busyUntil && _busy is { } busy)
            {
                // A new object every time: the caller may add to what it is handed.
                return Task.FromResult(new GameSnapshot
                {
                    Screen = busy.Screen, AwaitingDecision = false, Waiting = busy.Waiting, State = (JsonObject)busy.State.DeepClone(),
                });
            }
            var snapshot = _run.Snapshot();
            if (Experiments.Forecast && _run.Fight is { } fight) Forecast.AddTo(fight, snapshot);
            return Task.FromResult(snapshot);
        }
    }

    public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
    {
        lock (_gate)
        {
            ActionResult result;
            // Nothing resolves after the end, so "done" leaves no busy spell behind it.
            var atEnd = _run.AtEnd;
            var fight = _run.Fight;
            var screen = _run.Question?.Name ?? _run.Top?.Name ?? "unknown";
            if (DateTime.UtcNow < _busyUntil)
            {
                result = ActionResult.Fail("Cannot act now (resolving). Call wait_for_decision.");
            }
            else
            {
                try
                {
                    result = _run.Execute(actionId);
                }
                catch (Exception ex)
                {
                    // The rules are meant to refuse, never to throw; if they do, the run is put back
                    // as it was before the action and the caller still gets an answer.
                    result = ActionResult.Fail($"The arena could not run '{actionId}' ({ex.GetType().Name}: {ex.Message}).");
                }
            }

            if (!result.Ok)
            {
                _refused++;
            }
            else
            {
                WriteActionLog();
                if (!atEnd && ActionDelay > TimeSpan.Zero)
                {
                    // In a fight the mod shows its small waiting state while a card resolves or the
                    // enemies act; outside one, a busy snapshot under the screen the action was taken on.
                    _busy = fight is not null
                        ? new GameSnapshot { Screen = "combat", AwaitingDecision = false, Waiting = "resolving", State = fight.WaitingState() }
                        : GameSnapshot.Busy(screen, "resolving");
                    _busyUntil = DateTime.UtcNow + (fight is not null && actionId == "end_turn" ? ActionDelay * 2 : ActionDelay);
                }
            }
            return Task.FromResult(result);
        }
    }

    public Task<JsonObject> DescribeDeckAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_run.DescribeDeck());
        }
    }

    // ---------- the save on disk ----------

    private const string LogHeader = "# spiremind arena run, seed ";

    /// <summary>Appends the action ids the file does not hold yet. A failure is noted and never stops the run.</summary>
    private void WriteActionLog()
    {
        if (ActionLogPath is not { } path) return;
        try
        {
            var actions = _run.Actions;
            if (_written == 0 && !File.Exists(path))
            {
                if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
                File.WriteAllText(path, LogHeader + _run.State.Seed + "\n");
            }
            if (actions.Count > _written)
            {
                File.AppendAllLines(path, actions.Skip(_written));
                _written = actions.Count;
            }
            _logError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logError = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>
    /// A game around a run that is built again from a save file, to be carried on: the file's
    /// actions are replayed, and further actions are appended to the same file.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file is of another seed, or an action in it is refused.</exception>
    public static RunGame Resume(RunSetup setup, RunDirector director, string path, TimeSpan? actionDelay = null, ExperimentOptions? experiments = null)
    {
        var (seed, actions) = ReadActionLog(path);
        if (seed is not null && seed != setup.Seed)
            throw new InvalidOperationException($"The save {path} is of the run with seed {seed}, not {setup.Seed}.");
        var run = Run.Replay(setup, director, actions);
        return new RunGame(run)
        {
            ActionLogPath = path, _written = actions.Count,
            ActionDelay = actionDelay ?? ArenaGame.DefaultActionDelay, Experiments = experiments ?? new ExperimentOptions(),
        };
    }

    /// <summary>Reads a save file: the seed its first line names (null when it names none) and the action ids in order.</summary>
    public static (string? Seed, IReadOnlyList<string> Actions) ReadActionLog(string path)
    {
        string? seed = null;
        var actions = new List<string>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                if (line.StartsWith(LogHeader, StringComparison.Ordinal)) seed = line[LogHeader.Length..].Trim();
                continue;
            }
            actions.Add(line);
        }
        return (seed, actions);
    }
}
