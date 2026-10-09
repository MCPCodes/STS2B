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
    /// Wait for a settled decision newer than <paramref name="afterVersion"/> (or any
    /// settled decision when null). On timeout returns whatever the game shows now.
    /// </summary>
    public async Task<GameSnapshot> WaitAsync(long? afterVersion, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        GameSnapshot? candidate = null;
        var candidateSince = DateTime.UtcNow;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var snap = await CaptureAsync(ct);

            var isNew = afterVersion is null || snap.Version > afterVersion;
            if (snap.AwaitingDecision && isNew)
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
