using SpireMind.Core;

namespace SpireMind.Game;

/// <summary>
/// Reads the live game into snapshots and turns action ids back into game commands.
/// Each screen (combat, map, rewards, shop...) gets its own <see cref="IScreen"/>; the
/// first one that recognises the current screen wins. All game access happens on the
/// main thread.
/// </summary>
internal sealed class GameBridge : IGameBridge
{
    private readonly MainThreadQueue _main;
    private readonly IScreen[] _screens;

    public GameBridge(MainThreadQueue main)
    {
        _main = main;
        _screens = [];
    }

    public Task<GameSnapshot> CaptureAsync(CancellationToken ct) =>
        _main.Run(() =>
        {
            foreach (var screen in _screens)
            {
                if (screen.IsActive())
                    return screen.Capture();
            }
            return GameSnapshot.Busy("unknown", "no reader for this screen yet");
        });

    public Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct) =>
        _main.Run(() =>
        {
            foreach (var screen in _screens)
            {
                if (screen.IsActive())
                    return screen.Execute(actionId);
            }
            return ActionResult.Fail("no active screen");
        });
}

/// <summary>One game screen the bot knows how to read and drive. Called on the main thread.</summary>
internal interface IScreen
{
    bool IsActive();
    GameSnapshot Capture();
    ActionResult Execute(string actionId);
}
