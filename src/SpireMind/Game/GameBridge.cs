using System.Text.Json.Nodes;

namespace SpireMind.Game;

/// <summary>
/// Reads the live game into snapshots and turns action ids back into game input.
/// <see cref="ScreenRouter"/> picks the screen; the screen does the rest. All game access
/// happens on the main thread.
/// </summary>
internal sealed class GameBridge : IGameBridge
{
    private readonly MainThreadQueue _main;
    private volatile bool _multiplayer;

    public GameBridge(MainThreadQueue main) => _main = main;

    /// <summary>Whether the last snapshot was taken in a co-op run. Safe to read from any
    /// thread: the API brain checks it on its own task, where the game can't be touched.</summary>
    public bool IsMultiplayer => _multiplayer;

    public Task<GameSnapshot> CaptureAsync(CancellationToken ct) =>
        _main.Run(() =>
        {
            try
            {
                var ctx = GameContext.Capture();
                _multiplayer = ctx.IsMultiplayer;
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
                if (result.Ok && LeavesRoom(fresh, active.Node, actionId))
                    Latches.ProceededFrom = ctx.Run?.CurrentRoom;
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

    public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) =>
        _main.Run(() =>
        {
            try
            {
                var ctx = GameContext.Capture();
                if (ctx.Run is not { } run || ctx.Me is not { } me)
                    return new JsonObject { ["error"] = "no run in progress" };
                return Describe.Deck(run, me);
            }
            catch (Exception ex)
            {
                ModEntry.LogError($"describe deck failed: {ex}");
                return new JsonObject { ["error"] = $"reader error: {ex.GetType().Name}: {ex.Message}" };
            }
        });

    /// <summary>A room's proceed button, or an event option labelled Leave.</summary>
    private static bool LeavesRoom(GameSnapshot snapshot, Node? node, string actionId) =>
        actionId == "proceed"
            // A non-terminal rewards screen (Orrery or Cauldron in a shop, a chest's extra rewards) only closes itself.
            ? !(node is NRewardsScreen rewards && Nodes.TryGetPrivate(rewards, "_isTerminal") is false)
            : snapshot.Screen == "event"
              && snapshot.Actions.FirstOrDefault(a => a.Id == actionId)?.Label.StartsWith("Leave", StringComparison.Ordinal) == true;
}
