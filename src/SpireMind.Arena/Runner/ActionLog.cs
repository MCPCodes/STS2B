using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// What a session did on one turn of its fight, noted where its actions reach the game: this
/// stands between the tools and a position run's <see cref="ArenaGame"/> and passes everything
/// through unchanged. Each action the game accepted while the fight stood on
/// <paramref name="turn"/> is kept by id and by the label the snapshot offered it under
/// ("Play Defend", "Play Cinder on Vantom", "End turn"), the picks of a card choice and the
/// closing end_turn included. So the line is what really ran, in the order it ran, whether the
/// session played it card by card or as one act_sequence.
/// </summary>
internal sealed class ActionLog(ArenaGame game, int turn) : IGameBridge
{
    private readonly object _gate = new();
    private readonly List<(string Id, string Label)> _line = [];
    private bool _complete;

    /// <summary>The labels of the actions that ran on the turn, in order.</summary>
    public IReadOnlyList<string> Labels { get { lock (_gate) return _line.Select(action => action.Label).ToList(); } }

    /// <summary>Their ids.</summary>
    public IReadOnlyList<string> Ids { get { lock (_gate) return _line.Select(action => action.Id).ToList(); } }

    /// <summary>True once the turn is behind the fight: it was ended, or the fight ended in it.</summary>
    public bool Complete { get { lock (_gate) return _complete; } }

    public Task<GameSnapshot> CaptureAsync(CancellationToken ct) => game.CaptureAsync(ct);

    public Task<JsonObject> DescribeDeckAsync(CancellationToken ct) => game.DescribeDeckAsync(ct);

    public async Task<ActionResult> ExecuteAsync(string actionId, CancellationToken ct)
    {
        // The tools send one action at a time and wait for each, so nothing moves between
        // these lines; an action sent while the game is busy finds no label and is refused.
        var onTurn = !game.IsOver && game.Turn == turn;
        var label = onTurn ? (await game.CaptureAsync(ct)).Actions.FirstOrDefault(action => action.Id == actionId)?.Label : null;
        var result = await game.ExecuteAsync(actionId, ct);
        if (result.Ok && onTurn)
            lock (_gate)
            {
                _line.Add((actionId, label ?? actionId));
                _complete = game.IsOver || game.Turn != turn;
            }
        return result;
    }
}
