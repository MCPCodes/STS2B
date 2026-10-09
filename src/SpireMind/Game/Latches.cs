namespace SpireMind.Game;

/// <summary>
/// Guards against double input. Many game handlers complete a TaskCompletionSource on
/// click, so a second press throws or (for act votes) advances twice. Every click the bot
/// makes goes through <see cref="Clicks.Once"/>, and one-per-turn or one-per-act actions
/// use the specific latches below.
/// </summary>
internal static class Latches
{
    /// <summary>Turn number we already requested EndPlayerTurnAction for, per combat.</summary>
    public static int EndTurnRequestedFor { get; set; } = -1;

    /// <summary>Combat identity the end-turn latch belongs to, so a new fight resets it.</summary>
    public static object? EndTurnCombat { get; set; }

    /// <summary>Act index we already voted to leave via the proceed button.</summary>
    public static int ActVoteCastFor { get; set; } = -1;

    public static bool EndTurnAlreadyRequested(GameContext ctx)
    {
        if (ctx.Combat is null || ctx.MyCombat is null) return false;
        if (!ReferenceEquals(EndTurnCombat, ctx.Combat)) return false;
        return EndTurnRequestedFor == ctx.MyCombat.TurnNumber;
    }

    public static void MarkEndTurn(GameContext ctx)
    {
        EndTurnCombat = ctx.Combat;
        EndTurnRequestedFor = ctx.MyCombat?.TurnNumber ?? -1;
    }

    public static void ResetForNewRun()
    {
        EndTurnRequestedFor = -1;
        EndTurnCombat = null;
        ActVoteCastFor = -1;
        Clicks.Reset();
    }
}

/// <summary>
/// Click-once bookkeeping keyed by Godot instance id. A node that was clicked stays
/// "spent" until it is freed or <see cref="Window"/> passes, which covers buttons that
/// re-enable during an animation.
/// </summary>
internal static class Clicks
{
    public static TimeSpan Window { get; set; } = TimeSpan.FromSeconds(2.5);

    private static readonly Dictionary<ulong, DateTime> Spent = new();

    public static bool RecentlyClicked(GodotObject node)
    {
        Prune();
        return Spent.ContainsKey(node.GetInstanceId());
    }

    /// <summary>Run <paramref name="click"/> unless this node was clicked within the window.</summary>
    public static ActionResult Once(GodotObject node, Action click, string what)
    {
        if (!GodotObject.IsInstanceValid(node))
            return ActionResult.Fail($"{what} is gone.");
        if (RecentlyClicked(node))
            return ActionResult.Fail($"{what} was already clicked; waiting for the game to react.");

        Spent[node.GetInstanceId()] = DateTime.UtcNow;
        click();
        return ActionResult.Success;
    }

    public static void Reset() => Spent.Clear();

    private static void Prune()
    {
        if (Spent.Count == 0) return;
        var cutoff = DateTime.UtcNow - Window;
        foreach (var key in Spent.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList())
            Spent.Remove(key);
    }
}
