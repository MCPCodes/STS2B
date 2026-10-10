using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The floor: takes any legal action other than ending the turn until nothing else is left,
/// then ends the turn. Its choice is a hash of its seed and the state it is shown, so a fight
/// replays the same way without the policy remembering anything.
/// </summary>
public sealed class RandomPolicy(int seed) : IPolicy
{
    public RandomPolicy() : this(1) { }

    public string Name => "Random";

    public string Choose(GameSnapshot snapshot)
    {
        if (snapshot.Actions.Count == 0) return "end_turn";

        // Throwing a potion away is never a move; ending the turn comes last.
        var useful = snapshot.Actions
            .Where(a => a.Id != "end_turn" && !a.Id.StartsWith("discard_potion:", StringComparison.Ordinal))
            .ToList();
        if (useful.Count == 0)
            return snapshot.Actions.Any(a => a.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id;

        // FNV-1a over the state's JSON, which is built in a fixed key order.
        var hash = 14695981039346656037UL ^ (uint)seed;
        foreach (var c in snapshot.State.ToJsonString())
            hash = unchecked((hash ^ c) * 1099511628211UL);
        return useful[new GameRng(hash).NextInt(useful.Count)].Id;
    }
}
