namespace SpireMind.Game;

/// <summary>
/// Reads the live game into snapshots and turns action ids back into game input.
/// <see cref="ScreenRouter"/> picks the screen; the screen does the rest. All game access
/// happens on the main thread.
/// </summary>
internal sealed class GameBridge : IGameBridge
{
    private readonly MainThreadQueue _main;

    public GameBridge(MainThreadQueue main) => _main = main;

    public Task<GameSnapshot> CaptureAsync(CancellationToken ct) =>
        _main.Run(() =>
        {
            try
            {
                var ctx = GameContext.Capture();
                ActionTracker.Poll(ctx);

                if (ActionTracker.Pending is { } pending)
                    return GameSnapshot.Busy(ScreenRouter.LastScreenName, $"resolving {pending}");

                var routed = ScreenRouter.Resolve(ctx, out var busy);
                if (routed is not { } active)
                    return busy!;

                return active.Screen.Capture(ctx, active.Node);
            }
            catch (Exception ex)
            {
                // A reader blowing up must not take the bridge down; report it so the model waits.
                ModEntry.LogError($"capture failed: {ex}");
                return GameSnapshot.Busy("error", $"reader error: {ex.GetType().Name}: {ex.Message}");
            }
        });

    public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct) =>
        _main.Run(() =>
        {
            try
            {
                var ctx = GameContext.Capture();
                ActionTracker.Poll(ctx);
                if (ActionTracker.Pending is { } pending)
                    return ActionResult.Fail($"Still resolving {pending}.");

                if (ScreenRouter.Resolve(ctx, out _) is not { } active)
                    return ActionResult.Fail("The game is busy.");

                // The model chose from a snapshot taken a moment ago on another call; make sure
                // the id is still offered by the screen that is up right now.
                var fresh = active.Screen.Capture(ctx, active.Node);
                if (!fresh.AwaitingDecision)
                    return ActionResult.Fail($"Not your decision right now ({fresh.Waiting ?? fresh.Screen}).");
                if (fresh.Actions.All(a => a.Id != actionId))
                    return ActionResult.Fail($"'{actionId}' is no longer available on the {fresh.Screen} screen.");

                var result = active.Screen.Execute(ctx, active.Node, actionId);
                if (result.Ok)
                    ModEntry.Log($"{fresh.Screen}: {actionId}");
                else
                    ModEntry.Log($"{fresh.Screen}: {actionId} refused: {result.Error}");
                return result;
            }
            catch (Exception ex)
            {
                ModEntry.LogError($"execute {actionId} failed: {ex}");
                return ActionResult.Fail($"{ex.GetType().Name}: {ex.Message}");
            }
        });
}
