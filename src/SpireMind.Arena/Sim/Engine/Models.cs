namespace SpireMind.Arena;

/// <summary>
/// One encounter: which monsters stand in the fight. The class name is the game's
/// ("NibbitsNormal"). A new object is made for every fight.
/// </summary>
public abstract class Encounter
{
    public string ClassName => GetType().Name;

    public virtual RoomType RoomType => RoomType.Monster;

    /// <summary>One of the act's first three fights.</summary>
    public virtual bool IsWeak => false;

    /// <summary>The encounter's slot names in acting order, when it names slots. Monsters with a
    /// slot are kept sorted by this order, also ones that join later.</summary>
    public virtual IReadOnlyList<string> Slots => [];

    /// <summary>
    /// The starting line-up in creation order (HP is rolled in this order, before any sorting by
    /// slot). Draw every random choice of the line-up from <paramref name="rng"/>, the
    /// encounter's own stream, in the order the game draws them.
    /// </summary>
    public abstract IReadOnlyList<(Monster Monster, string? Slot)> GenerateMonsters(GameRng rng);
}

/// <summary>
/// A relic the player holds. In a run it is one object from the moment it is obtained: what it
/// counts in its fields goes into every fight and comes back (the fight works on a copy). It
/// hears the fight's hooks and the run's (<see cref="Listener"/>), after the listening deck
/// cards and before the potions; inside a fight after the player's powers.
/// <para>
/// A relic is obtained through <see cref="Run.ObtainRelic(Relic, int)"/> (or, from a hook,
/// <see cref="RunContext.ObtainRelic"/>), never by adding it to a list: that runs
/// <see cref="AfterObtained"/> and takes it out of the relic bags.
/// </para>
/// </summary>
public abstract class Relic : Listener
{
    public string ClassName => GetType().Name;

    /// <summary>The game's English title, as listed in the snapshot's "relics".</summary>
    public abstract string Name { get; }

    /// <summary>What it does, in our own words (get_deck shows it).</summary>
    public abstract string Describe();

    /// <summary>
    /// The pickup effect: the relic has just joined the player's relics (it is already in the
    /// list, at its place) and, outside a fight, has left the relic bags. Outside a fight <c>c.Run</c> is there
    /// for a pickup that needs the run (<see cref="RunContext.NeedRun"/>): a card choice
    /// (<see cref="Run.Ask"/>), a roll on a stream of the run, a pull from a bag, a screen. A
    /// relic can also be obtained inside a fight (Sword of Stone turns into Sword of Jade in
    /// the victory hook); then only what <see cref="RunContext"/> itself offers can be done.
    /// </summary>
    public virtual void AfterObtained(RunContext c) { }

    /// <summary>The relic has just left the player's relics (it was replaced or taken away).</summary>
    public virtual void AfterRemoved(RunContext c) { }

    internal Relic CloneFor(CloneMap map)
    {
        var copy = (Relic)MemberwiseClone();
        map.Add(this, copy);
        return copy;
    }
}

/// <summary>What a potion's <see cref="PotionDef.Use"/> gets.</summary>
public sealed class PotionContext
{
    internal PotionContext(Fight f, PotionDef potion, Creature? target)
    {
        F = f;
        Potion = potion;
        Target = target;
    }

    public Fight F { get; }
    public PotionDef Potion { get; }

    /// <summary>The player.</summary>
    public Creature Owner => F.Player;

    /// <summary>The chosen enemy of an AnyEnemy potion; the player for AnyPlayer and Self potions; null for the rest.</summary>
    public Creature? Target { get; }
}

/// <summary>
/// One potion as the registry knows it. The class name is the game's ("FirePotion"). Potion
/// damage and Block are unpowered: use <see cref="Props.NonCardUnpowered"/>.
/// </summary>
public sealed class PotionDef
{
    public PotionDef(string className, string name, PotionRarity rarity, TargetType target)
    {
        ClassName = className;
        Name = name;
        Rarity = rarity;
        Target = target;
    }

    public string ClassName { get; }

    /// <summary>The game's English title.</summary>
    public string Name { get; }
    public PotionRarity Rarity { get; }

    /// <summary>AnyEnemy asks for an enemy ("potion:0>e1"); every other kind is used with "potion:0".</summary>
    public TargetType Target { get; }

    /// <summary>CombatOnly and AnyTime potions can be used in a fight; Automatic ones cannot be used by hand.</summary>
    public PotionUsage Usage { get; init; } = PotionUsage.CombatOnly;

    /// <summary>What it does, in our own words (the snapshot's potion "text").</summary>
    public required Func<Fight?, string> Text { get; init; }

    /// <summary>The effect. The potion has already left its slot when this runs.</summary>
    public required Action<PotionContext> Use { get; init; }

    /// <summary>A potion's own extra condition for the use action (false hides it; discard stays).</summary>
    public Func<Fight, bool>? CanUse { get; init; }

    /// <summary>False for potions random generation never makes.</summary>
    public bool CanBeGeneratedInCombat { get; init; } = true;

    /// <summary>
    /// Hooks the potion hears while it sits in the belt (Fairy in a Bottle prevents a death). The
    /// object is shared by every fight, so it must keep no state; it is heard once per slot that
    /// holds the potion, after the relics, and not any more once the potion was used or discarded.
    /// </summary>
    public Listener? Hooks { get; init; }

    public override string ToString() => ClassName;
}
