namespace SpireMind.Game;

/// <summary>
/// Tracks the one game request we have in flight (a card play, potion, end turn, map vote,
/// purchase...) so snapshots report "busy" until it lands instead of showing a hand that
/// still contains the card we just played.
///
/// This is deliberately observational: each request says how to tell it finished (the card
/// left the hand, the turn ended, the room changed) plus a timeout. GameAction.Cancel never
/// completes CompletionTask in v0.107.1 and ActionQueueSet.IsEmpty can stick, so neither is
/// trusted on its own.
/// </summary>
internal static class ActionTracker
{
    private sealed record Request(string What, Func<GameContext, bool> IsDone, DateTime NotBefore, DateTime Deadline);

    private static Request? _current;

    /// <summary>Short description of the in-flight request, or null when idle.</summary>
    public static string? Pending => _current?.What;

    /// <summary>
    /// Register a request right after making it. <paramref name="isDone"/> is polled on the
    /// main thread; <paramref name="minWait"/> stops us reporting done before the game even
    /// started reacting.
    /// </summary>
    public static void Begin(string what, Func<GameContext, bool> isDone, TimeSpan? timeout = null, TimeSpan? minWait = null)
    {
        var now = DateTime.UtcNow;
        _current = new Request(what, isDone, now + (minWait ?? TimeSpan.FromMilliseconds(120)), now + (timeout ?? TimeSpan.FromSeconds(6)));
    }

    /// <summary>Called at the start of every capture/execute on the main thread.</summary>
    public static void Poll(GameContext ctx)
    {
        if (_current is not { } r) return;
        var now = DateTime.UtcNow;
        if (now < r.NotBefore) return;

        bool done;
        try { done = r.IsDone(ctx); }
        catch { done = true; }

        if (done || now >= r.Deadline)
        {
            if (!done) ModEntry.Log($"gave up waiting on {r.What} after timeout");
            _current = null;
        }
    }

    public static void Clear() => _current = null;

    /// <summary>
    /// True when the game's own action machinery looks idle. Use as one ingredient of an
    /// IsDone check, never alone (IsEmpty can stick after an enqueue-time cancel).
    /// </summary>
    public static bool GameLooksIdle()
    {
        var rm = RunManager.Instance;
        if (rm is null || !rm.IsInProgress) return true;
        try
        {
            if (rm.ActionExecutor?.IsRunning == true) return false;
            if (rm.ActionQueueSet is { IsEmpty: false }) return false;
            if (NMapScreen.Instance?.IsTraveling == true) return false;
            if (rm.NetService?.IsGameLoading == true) return false;
        }
        catch
        {
            // If any of these vanish mid-transition, treat as busy and let the timeout decide.
            return false;
        }
        return true;
    }
}
