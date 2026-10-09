namespace SpireMind.Game;

/// <summary>
/// One game screen the bot can read and drive. Implementations are stateless singletons
/// (state that must survive between calls, like click latches, lives in <see cref="Latches"/>).
/// Every method runs on the Godot main thread.
/// </summary>
internal interface IScreen
{
    /// <summary>Snapshot screen name: combat, map, rewards, card_reward, card_select, shop, rest, event, treasure, game_over, modal, menu.</summary>
    string Name { get; }

    /// <summary>Build the snapshot. <paramref name="node"/> is the node the router matched (the overlay, room or modal), when there is one.</summary>
    GameSnapshot Capture(GameContext ctx, Node? node);

    /// <summary>
    /// Do one action. The bridge has already checked that <paramref name="actionId"/> is in a
    /// fresh <see cref="Capture"/> of this same screen, but implementations still re-check
    /// enablement right before clicking and must never click the same thing twice.
    /// </summary>
    ActionResult Execute(GameContext ctx, Node? node, string actionId);
}

internal readonly record struct ActiveScreen(IScreen Screen, Node? Node);
