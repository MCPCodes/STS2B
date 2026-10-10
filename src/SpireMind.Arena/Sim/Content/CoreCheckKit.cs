namespace SpireMind.Arena;

/// <summary>One move of a <see cref="CheckDummy"/>: attack, then Block, then anything else.</summary>
/// <param name="Id">The move id.</param>
/// <param name="Damage">Base damage per hit; 0 for no attack.</param>
/// <param name="Hits">Hit count.</param>
/// <param name="Block">Move Block gained after the attack.</param>
/// <param name="Then">Anything else, given the fight and the dummy's creature (so nothing is captured).</param>
public sealed record DummyStep(string Id, int Damage = 0, int Hits = 1, int Block = 0, Action<Fight, Creature>? Then = null);

/// <summary>
/// A monster for scripted checks only (it is not in the registry): fixed HP, and a fixed cycle
/// of <see cref="DummyStep"/>s. The default does nothing every turn.
/// <code>new CheckDummy { Hp = 40, Steps = [new("HIT", Damage: 7, Hits: 2), new("WAIT")] }</code>
/// </summary>
public sealed class CheckDummy : Monster
{
    public string DisplayName { get; init; } = "Dummy";
    public int Hp { get; init; } = 100;
    public IReadOnlyList<DummyStep> Steps { get; init; } = [new DummyStep("IDLE")];

    /// <summary>Starting powers, given the fight and the dummy's creature.</summary>
    public Action<Fight, Creature>? OnAdded { get; init; }

    public override string Name => DisplayName;
    public override int MinHp => Hp;
    public override int MaxHp => Hp;

    public override void AfterAddedToRoom(Fight f) => OnAdded?.Invoke(f, Creature);

    protected override MonsterState BuildMachine(MachineBuilder m)
    {
        var moves = Steps.Select(step => m.Move(step.Id, f => Perform(f, step), Intents(step))).ToList();
        for (var i = 0; i < moves.Count; i++) moves[i].FollowUp = moves[(i + 1) % moves.Count];
        return moves[0];
    }

    private static Intent[] Intents(DummyStep step)
    {
        var intents = new List<Intent>();
        if (step.Damage > 0) intents.Add(Intent.Attack(step.Damage, step.Hits));
        if (step.Block > 0) intents.Add(Intent.Defend);
        if (step.Then is not null) intents.Add(Intent.Debuff);
        if (intents.Count == 0) intents.Add(Intent.Sleep);
        return intents.ToArray();
    }

    private void Perform(Fight f, DummyStep step)
    {
        if (step.Damage > 0) Attack(f, step.Damage, step.Hits);
        if (step.Block > 0) GainBlock(f, step.Block);
        step.Then?.Invoke(f, Creature);
    }
}

/// <summary>Short forms the scripted checks share.</summary>
public static class CheckKit
{
    /// <summary>The starter deck in an order that deals Strike, Strike, Defend, Defend, Bash on turn 1
    /// and Strike, Strike, Strike, Defend, Defend on turn 2.</summary>
    public static readonly string[] StarterInOrder =
    [
        "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "Bash",
        "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad",
    ];

    /// <summary>A fight against one <see cref="CheckDummy"/> (idle, 100 HP unless given) with the starter deck in <see cref="StarterInOrder"/>.</summary>
    public static Fight VsDummy(CheckDummy? dummy = null, int seed = 1) =>
        new FightBuilder().DrawOrder(StarterInOrder).Enemy(dummy ?? new CheckDummy()).Seed(seed).Start();

    /// <summary>A fight against several dummies, in list order.</summary>
    public static Fight VsDummies(params CheckDummy[] dummies)
    {
        var builder = new FightBuilder().DrawOrder(StarterInOrder);
        foreach (var d in dummies) builder.Enemy(d);
        return builder.Start();
    }

    /// <summary>The titles of the cards in hand, left to right.</summary>
    public static List<string> HandTitles(this Fight f) => f.Hand.Select(c => c.Title).ToList();

    /// <summary>Everything a snapshot shows, as one string: equal strings mean the model sees no difference.</summary>
    public static string Seen(this Fight f) => f.Snapshot().ToJson().ToJsonString();

    /// <summary>Plays the fight to its end with a fixed rule (first non-potion action, end the turn when nothing else is offered)
    /// and returns everything that was shown on the way.</summary>
    public static string PlayOut(Fight f, int most = 5000)
    {
        var log = new System.Text.StringBuilder();
        for (var n = 0; n < most && !f.IsOver; n++)
        {
            var actions = f.LegalActions();
            var pick = actions.FirstOrDefault(a => a.Id != "end_turn" && !a.Id.Contains("potion", StringComparison.Ordinal)) ?? actions[^1];
            log.Append(pick.Id).Append('|').Append(f.Seen()).Append('\n');
            f.Do(pick.Id);
        }
        log.Append(f.Seen());
        return log.ToString();
    }
}
