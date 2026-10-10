using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// A scripted player: the yardstick the models are measured against. It decides from the
/// snapshot alone, as a model does, and returns one of the snapshot's action ids. It keeps
/// nothing between calls, so one instance can play any number of fights, also at the same time.
/// <para>
/// A class that implements this and has a parameterless constructor is found by reflection and
/// plays in <c>arena baseline</c> and the reports; nothing else has to be edited.
/// </para>
/// </summary>
public interface IPolicy
{
    string Name { get; }

    /// <summary>The id of one legal action of <paramref name="snapshot"/>, a snapshot that awaits a decision.
    /// The screen can be "combat", "hand_select" or "card_select".</summary>
    string Choose(GameSnapshot snapshot);
}

/// <summary>
/// A scripted player that also gets the running fight, to search with. The fight it is handed
/// is the live one: it must not act on it. It copies it with <see cref="Fight.CloneDeterminized"/>
/// (the draw order and every future random number are replaced, so the search cannot peek) or
/// <see cref="Fight.Clone"/> (an exact copy), plays the copies with <see cref="Fight.Execute"/>,
/// and returns an action id of the live fight's snapshot.
/// </summary>
public interface IEnginePolicy : IPolicy
{
    string Choose(GameSnapshot snapshot, Fight fight);
}

public static class Policies
{
    /// <summary>The order the known players are listed in, weakest first; any others follow by name.</summary>
    private static readonly string[] Order = ["Random", "Greedy", "Lookahead"];

    /// <summary>The scripted players, weakest first.</summary>
    public static IReadOnlyList<IPolicy> All { get; } = Discover();

    private static IReadOnlyList<IPolicy> Discover()
    {
        var found = typeof(Policies).Assembly.GetTypes()
            .Where(t => typeof(IPolicy).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (IPolicy)Activator.CreateInstance(t)!)
            .ToList();
        int Rank(IPolicy p)
        {
            var i = Array.IndexOf(Order, p.Name);
            return i < 0 ? Order.Length : i;
        }
        return found.OrderBy(Rank).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
    }
}

/// <summary>One scripted fight's outcome.</summary>
/// <param name="HpLeft">0 after a death; what was left after a fight lost to the turn limit;
/// after a win it includes Burning Blood's heal, as the next fight would start.</param>
/// <param name="HpAtStart">HP the fight was entered with.</param>
/// <param name="DamageTaken">All HP lost in the fight, whatever took it (healing in the fight is not taken off).</param>
/// <param name="TimedOut">Lost to the arena's turn limit, not by dying.</param>
/// <param name="Refused">Actions the fight turned down: an id the policy named that was not on offer, or a rule that threw. 0 in a sound arena.</param>
public sealed record BaselineRow(string ScenarioId, string Policy, bool Won, int HpLeft, int MaxHp, int Turns, int CardsPlayed,
    int HpAtStart = 0, int DamageTaken = 0, bool TimedOut = false, int Refused = 0);

public static class Baseline
{
    /// <summary>More decisions than any fight has: <see cref="Tuning.MaxTurns"/> turns of a full hand and then some.</summary>
    private const int MostDecisions = 6000;

    /// <summary>Plays <paramref name="s"/> to the end with <paramref name="p"/>, without any waiting.</summary>
    public static BaselineRow Play(Scenario s, IPolicy p)
    {
        var game = new ArenaGame(s) { ActionDelay = TimeSpan.Zero };
        var hpAtStart = game.PlayerHp;
        for (var n = 0; n < MostDecisions && !game.IsOver; n++)
        {
            // The game answers synchronously, so nothing blocks here.
            var snapshot = game.CaptureAsync(CancellationToken.None).GetAwaiter().GetResult();
            var choice = p is IEnginePolicy searching ? searching.Choose(snapshot, game.Fight) : p.Choose(snapshot);
            var result = game.ExecuteAsync(choice, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Ok) continue;

            // A policy that names an illegal action gives up its turn instead of spinning; inside a
            // card choice there is no turn to give up, so the first offered action is taken.
            var fallback = snapshot.Actions.Any(a => a.Id == "end_turn") ? "end_turn" : snapshot.Actions[0].Id;
            game.ExecuteAsync(fallback, CancellationToken.None).GetAwaiter().GetResult();
        }
        return new BaselineRow(s.Id, p.Name, game.Won, game.PlayerHp, game.PlayerMaxHp, game.Turn, game.CardsPlayed,
            hpAtStart, game.DamageTaken, game.TimedOut, game.RefusedActions);
    }
}
