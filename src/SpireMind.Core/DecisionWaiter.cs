namespace SpireMind.Core;

/// <summary>
/// Polls the game until the local player has a decision to make. The game resolves card
/// effects, animations and enemy turns asynchronously, so after an action the bot waits
/// here instead of guessing how long to sleep.
/// </summary>
public sealed class DecisionWaiter
{
    private readonly IGameBridge _game;
    private readonly SnapshotVersioner _versioner;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>A decision must look the same for this long before we hand it over, so we
    /// don't report a hand mid-draw or a screen mid-transition.</summary>
    public TimeSpan SettleTime { get; init; } = TimeSpan.FromMilliseconds(300);

    public DecisionWaiter(IGameBridge game, SnapshotVersioner versioner)
    {
        _game = game;
        _versioner = versioner;
    }

    public async Task<GameSnapshot> CaptureAsync(CancellationToken ct) =>
        _versioner.Stamp(await _game.CaptureAsync(ct));

    /// <summary>
    /// The game's current snapshot without a version: the versioner is left alone, so a look
    /// from outside the bot's tools (the brain controller's screen poll) can't bump the version
    /// while the model plans. Otherwise a deck view opened and closed meanwhile would count as
    /// two changes, and the model's next act would be refused as stale with the game unchanged.
    /// </summary>
    public Task<GameSnapshot> PeekAsync(CancellationToken ct) => _game.CaptureAsync(ct);

    /// <summary>
    /// Wait for a settled decision newer than <paramref name="afterVersion"/> (or any
    /// settled decision when null). On timeout returns whatever the game shows now.
    /// </summary>
    /// <param name="interrupt">Checked every poll; when true, returns the current snapshot at
    /// once without waiting for it to settle (a chat message for the model arrived).</param>
    /// <param name="hold">While true no decision is handed over and the wait runs on until
    /// <paramref name="interrupt"/> or the timeout (the human paused the bot).</param>
    public async Task<GameSnapshot> WaitAsync(long? afterVersion, TimeSpan timeout, CancellationToken ct,
        Func<bool>? interrupt = null, Func<bool>? hold = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        GameSnapshot? candidate = null;
        var candidateSince = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var snap = await CaptureAsync(ct);
            if (interrupt?.Invoke() == true)
                return snap;

            var isNew = afterVersion is null || snap.Version > afterVersion;
            if (snap.AwaitingDecision && isNew && hold?.Invoke() != true)
            {
                if (candidate is null || candidate.Version != snap.Version)
                {
                    candidate = snap;
                    candidateSince = DateTime.UtcNow;
                }
                if (DateTime.UtcNow - candidateSince >= SettleTime)
                    return snap;
            }
            else
            {
                candidate = null;
            }

            if (DateTime.UtcNow >= deadline)
                return snap;

            await Task.Delay(PollInterval, ct);
        }
    }
}
