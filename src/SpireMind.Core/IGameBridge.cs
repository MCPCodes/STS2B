using System.Text.Json.Nodes;

namespace SpireMind.Core;

/// <summary>
/// The seam between the generic bridge and the game. The mod implements this against the
/// real game; tests and the dev host use <see cref="FakeGame"/>.
/// Implementations are called from HTTP worker threads and must marshal any game access
/// onto the game's main thread themselves (see <see cref="MainThreadQueue"/>).
/// </summary>
public interface IGameBridge
{
    Task<GameSnapshot> CaptureAsync(CancellationToken ct);

    /// <summary>Start the action. Returning means the game accepted it, not that it finished
    /// animating; callers wait for the next decision with <see cref="DecisionWaiter"/>.</summary>
    Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct);

    /// <summary>The run's whole deck with card text, relics with text, potions, HP and gold, for
    /// build decisions (snapshots only carry deck name counts). Return a new object each call;
    /// the caller adds to it.</summary>
    Task<JsonObject> DescribeDeckAsync(CancellationToken ct);
}
