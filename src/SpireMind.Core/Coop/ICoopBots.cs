namespace SpireMind.Core.Coop;

/// <summary>
/// Starting bot copies of the game and drawing their lines on the human's screen, as the game
/// side offers it. Mirrors the static class SpireMind.Game.Coop.CoopBots, so Core can use it
/// without referencing the game. Members may be called from any thread and never block: the
/// game's adapter marshals to the main thread, and <see cref="Known"/> returns a snapshot it
/// refreshes on the frame tick.
/// </summary>
public interface ICoopBots
{
    /// <summary>Starts a bot copy and returns a line for the chat. A null character lets the
    /// lobby pick one.</summary>
    Task<string> StartCopyAsync(string? character, CancellationToken ct);

    /// <summary>The bot copies this copy started: bridge port, NetId and character.</summary>
    IReadOnlyList<(int Port, ulong NetId, string Character)> Known { get; }

    /// <summary>Draws a speech bubble with <paramref name="text"/> over the player with this
    /// NetId on the human's screen.</summary>
    void ShowLine(ulong netId, string text);
}
