namespace SpireMind.Arena;

/// <summary>One card of a deck list: the game's class name, and whether it is upgraded.</summary>
public readonly record struct DeckCard(string ClassName, bool Upgraded = false)
{
    /// <summary>"Bash+" names an upgraded Bash.</summary>
    public static DeckCard Parse(string text) =>
        text.EndsWith('+') ? new DeckCard(text[..^1], true) : new DeckCard(text);

    public override string ToString() => Upgraded ? ClassName + "+" : ClassName;
}

/// <summary>
/// What a scenario works out to: everything needed to start one fight the way the game would.
/// </summary>
/// <param name="Deck">The deck in deck order (the starter is 5 Strike, 4 Defend, Bash; later cards appended).</param>
/// <param name="Hp">HP the player enters the fight with.</param>
/// <param name="MaxHp">The player's maximum HP (80 for a fresh Ironclad).</param>
/// <param name="Potions">Potion class names by belt slot; null for an empty slot. Three slots.</param>
/// <param name="Encounter">The encounter's class name ("NibbitsWeak").</param>
/// <param name="Floor">The floor the snapshot's run block names.</param>
/// <param name="Seed">Seeds every random stream of the fight.</param>
internal sealed record FightSetup(
    IReadOnlyList<DeckCard> Deck, int Hp, int MaxHp, IReadOnlyList<string?> Potions, string Encounter, int Floor, int Seed);

/// <summary>A monster placed by hand (scripted checks): the monster, its slot, and its HP when it is not to be rolled.</summary>
internal sealed record PlacedMonster(Monster Monster, string? Slot = null, int? Hp = null);

/// <summary>Everything the engine needs to start a fight; a <see cref="FightSetup"/> plus what only checks set.</summary>
internal sealed class FightConfig
{
    /// <summary>The deck in deck order, as definitions (a check may use cards that are not in the registry).</summary>
    public List<(CardDef Def, bool Upgraded)> Deck { get; init; } = new();
    public int Hp { get; init; } = 80;
    public int MaxHp { get; init; } = 80;
    public string?[] Potions { get; init; } = [null, null, null];
    public string? Encounter { get; init; }

    /// <summary>When set, these monsters fight instead of the encounter's own line-up.</summary>
    public List<PlacedMonster>? Monsters { get; init; }
    public IReadOnlyList<string> Slots { get; init; } = [];
    public RoomType RoomType { get; init; } = RoomType.Monster;
    public int Floor { get; init; } = 1;
    public int Seed { get; init; } = 1;

    /// <summary>False keeps the deck in the order given: its first card is drawn first.</summary>
    public bool ShuffleDeck { get; init; } = true;
    public List<string> Relics { get; init; } = ["BurningBlood"];

    /// <summary>Relic objects that are not in the registry (a check's own).</summary>
    public List<Relic> ExtraRelics { get; init; } = new();
    public int MaxTurns { get; init; } = Tuning.MaxTurns;

    public static FightConfig From(FightSetup s) => new()
    {
        Deck = s.Deck.Select(c => (Content.Registry.Card(c.ClassName), c.Upgraded)).ToList(), Hp = s.Hp, MaxHp = s.MaxHp, Potions = s.Potions.ToArray(), Encounter = s.Encounter, Floor = s.Floor, Seed = s.Seed,
    };
}

/// <summary>What never changes during a fight; clones share it.</summary>
internal sealed record FightInfo(IReadOnlyList<(CardDef Def, bool Upgraded)> Deck, IReadOnlyList<string> Slots, RoomType RoomType, string EncounterName, int Floor, int Seed, int MaxTurns);

/// <summary>
/// Everything about a fight that changes. It is one object so that the fight can be copied
/// whole: before every action (to replay the action once the player answered a choice it
/// opened) and for players that search.
/// </summary>
internal sealed class FightState
{
    public Creature Player = null!;

    /// <summary>The live enemy list in acting order. Dead monsters leave it; a reviving one stays at 0 HP.</summary>
    public List<Creature> Enemies = new();

    /// <summary>Every enemy the fight has had, at the index its "e&lt;j&gt;" id uses.</summary>
    public List<Creature> Roster = new();
    public int Escaped;

    public List<CardInstance> Hand = new(), Draw = new(), Discard = new(), Exhaust = new(), Play = new();

    public List<Relic> Relics = new();
    public PotionDef?[] Potions = new PotionDef?[3];

    public int Energy;
    public int BaseMaxEnergy = 3;
    public int Turn = 1;
    public int Round = 1;
    public Side CurrentSide = Side.Player;
    public TurnPhase Phase;

    public RngSet Rng = null!;
    public History History = new();

    public bool InProgress, Starting, PendingLoss, Over, Won, TimedOut;
    public bool PlayerHooksActive = true;

    public int NextCardId = 1, NextCreatureId, NextPlayId = 1, NextAttackId = 1;

    /// <summary>Cards and potions whose effect is running now (nested for auto-plays).</summary>
    public int EffectDepth;

    public int CardsPlayed, DamageTaken, VictoryHeal;

    public FightState Clone(out CloneMap map)
    {
        map = new CloneMap();
        var m = map;
        var copy = new FightState
        {
            Player = Player.CloneFor(m),
            Escaped = Escaped,
            Energy = Energy, BaseMaxEnergy = BaseMaxEnergy, Turn = Turn, Round = Round, CurrentSide = CurrentSide, Phase = Phase,
            Rng = Rng.Clone(), History = History.Clone(),
            InProgress = InProgress, Starting = Starting, PendingLoss = PendingLoss, Over = Over, Won = Won, TimedOut = TimedOut,
            PlayerHooksActive = PlayerHooksActive,
            NextCardId = NextCardId, NextCreatureId = NextCreatureId, NextPlayId = NextPlayId, NextAttackId = NextAttackId,
            EffectDepth = EffectDepth, CardsPlayed = CardsPlayed, DamageTaken = DamageTaken, VictoryHeal = VictoryHeal,
            Potions = (PotionDef?[])Potions.Clone(),
        };

        // The roster holds every enemy, also the ones that left the list, so clone through it.
        foreach (var e in Roster) copy.Roster.Add(e.CloneFor(m));
        foreach (var e in Enemies) copy.Enemies.Add(m.Get(e)!);

        static void CopyPile(List<CardInstance> from, List<CardInstance> to, CloneMap m)
        {
            to.Capacity = Math.Max(to.Capacity, from.Count);
            foreach (var c in from) to.Add(c.CloneFor(m));
        }
        CopyPile(Hand, copy.Hand, m);
        CopyPile(Draw, copy.Draw, m);
        CopyPile(Discard, copy.Discard, m);
        CopyPile(Exhaust, copy.Exhaust, m);
        CopyPile(Play, copy.Play, m);

        foreach (var r in Relics) copy.Relics.Add(r.CloneFor(m));

        m.Finish();
        return copy;
    }
}
