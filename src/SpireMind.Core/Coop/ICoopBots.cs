namespace SpireMind.Core.Coop;

/// <summary>
/// Starting and stopping bot copies of the game and drawing them on the human's screen, as the
/// game side offers it. Mirrors the static classes of SpireMind.Game.Coop (CoopBots,
/// RemoteBubbles) and the co-op nameplates, so Core can use them without referencing the game.
/// Members may be called from any thread and never block: the game's adapter marshals to the
/// main thread, and <see cref="Known"/> and <see cref="NameOf"/> return snapshots it refreshes
/// on the frame tick.
/// </summary>
public interface ICoopBots
{
    /// <summary>Starts a bot copy and returns a line for the chat. A null character lets the
    /// lobby pick one.</summary>
    Task<string> StartCopyAsync(string? character, CancellationToken ct);

    /// <summary>Asks every bot copy this copy started to leave the lobby or run and quit, and
    /// returns a line for the chat once they have (or once the game gave up waiting).</summary>
    Task<string> StopAllAsync(CancellationToken ct);

    /// <summary>The bot copies this copy started: bridge port, NetId and character.</summary>
    IReadOnlyList<(int Port, ulong NetId, string Character)> Known { get; }

    /// <summary>Draws a speech bubble with <paramref name="text"/> over the player with this
    /// NetId on the human's screen.</summary>
    void ShowLine(ulong netId, string text);

    /// <summary>Shows (true) or takes away (false) a thought cloud over the player with this
    /// NetId on the human's screen, while that bot copy decides.</summary>
    void Thinking(ulong netId, bool on);

    /// <summary>The name the human's screen gives the player with this NetId on its nameplate
    /// ("Ironclad", "Ironclad 2"), or null when it shows none (outside a local co-op game).
    /// Called under the relay's lock, so it must return at once and take no lock of its own
    /// that could wait.</summary>
    string? NameOf(ulong netId);
}
