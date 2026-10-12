using System.Globalization;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// One of the game's card pools as an object (the game's CardPoolModel): its full list of
/// cards in the game's order, whether the arena has built a card or not. A random pick of the
/// game runs over such a list by index, so the list must be the game's own, complete: with one
/// name missing every pick after it would land one card further.
/// </summary>
public sealed class CardPool
{
    private readonly Dictionary<string, GameCard> _byName;

    internal CardPool(string name, IReadOnlyList<GameCard> all)
    {
        Name = name;
        All = all;
        _byName = all.ToDictionary(c => c.ClassName, StringComparer.Ordinal);
    }

    /// <summary>The pool's name: the game's class name without "CardPool" ("Ironclad", "Colorless", "Curse").</summary>
    public string Name { get; }

    /// <summary>Every card of the pool in the game's order, unlocked or not, built or not.</summary>
    public IReadOnlyList<GameCard> All { get; }

    public bool Contains(string className) => _byName.ContainsKey(className);

    /// <summary>The pool's entry for a class name, or null when the card is not in this pool.</summary>
    public GameCard? Find(string className) => _byName.GetValueOrDefault(className);

    /// <summary>
    /// The cards of the pool a single-player run of this profile can meet, in the pool's order
    /// (the game's GetUnlockedCards): without the cards whose epoch is not revealed and without
    /// the multiplayer-only ones.
    /// </summary>
    public IReadOnlyList<GameCard> Unlocked(RunUnlocks unlocks) =>
        All.Where(c => unlocks.IsRevealed(c.Epoch) && !c.MultiplayerOnly).ToList();

    /// <summary>The cards of the pool the arena has built, in the pool's order.</summary>
    public IReadOnlyList<GameCard> Built => All.Where(c => CardPools.IsBuilt(c.ClassName)).ToList();

    public override string ToString() => Name;
}

/// <summary>
/// The run met something the arena leaves out and cannot go on as the game would: a roll that
/// can only land on cards that are not built, or any roll that lands on one in strict mode.
/// Whoever runs the act ends the run as <see cref="RunResult.Unrepresentable"/> with the
/// message as the reason; it is not a loss and is not scored.
/// </summary>
public sealed class RunUnrepresentableException(string reason) : Exception(reason);

/// <summary>
/// What a run passed over because the arena leaves it out: the house rule of run mode that a
/// roll which lands on something not built is drawn again (plan 5.1). Every such redraw is
/// written down here with the floor, so that a result line can say from which floor a run
/// stopped being the game's own run for its seed. With <see cref="Strict"/> set nothing is
/// drawn again: the first landing ends the run as unrepresentable.
/// </summary>
public sealed class LeftOutLog : RunPart
{
    /// <summary>One roll that landed on something left out.</summary>
    /// <param name="Floor">The run's total floor at the time.</param>
    /// <param name="Kind">What kind of thing: "card", "relic", "potion", "event".</param>
    /// <param name="ClassName">The game's class name of what the roll landed on.</param>
    /// <param name="What">What was being rolled, in a few words ("a transform of Strike").</param>
    public sealed record Entry(int Floor, string Kind, string ClassName, string What);

    private readonly List<Entry> _entries = new();

    /// <summary>The game's streams with nothing drawn again: a roll that lands on something left out ends the run.</summary>
    public bool Strict { get; set; }

    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>The floor of the first redraw, or null while the run is still the game's own run for its seed.</summary>
    public int? FirstFloor => _entries.Count > 0 ? _entries[0].Floor : null;

    /// <summary>
    /// A roll landed on something the arena leaves out. Notes it and returns, so that the caller
    /// draws again from the same stream; in strict mode it throws instead.
    /// </summary>
    /// <exception cref="RunUnrepresentableException">Strict mode.</exception>
    public void PassedOver(RunState state, string kind, string className, string what)
    {
        if (Strict)
            throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture,
                $"{what} on floor {state.TotalFloor} landed on the {kind} {className}, which the arena leaves out"));
        _entries.Add(new Entry(state.TotalFloor, kind, className, what));
    }

    public override RunPart Clone(CloneMap map)
    {
        var copy = new LeftOutLog { Strict = Strict };
        copy._entries.AddRange(_entries);
        return copy;
    }

    public override JsonNode Dump()
    {
        var entries = new JsonArray();
        foreach (var e in _entries) entries.Add(string.Create(CultureInfo.InvariantCulture, $"{e.Floor}:{e.Kind}:{e.ClassName}:{e.What}"));
        return new JsonObject { ["strict"] = Strict, ["passed_over"] = entries };
    }
}

/// <summary>
/// The game's card pools an Ironclad run can draw from, and the two rules every random card
/// outside a fight goes through: which pool a card belongs to, and the pick that runs over the
/// game's full list and is drawn again when it lands on a card the arena has not built.
/// </summary>
public static class CardPools
{
    /// <summary>The Ironclad's 87 cards.</summary>
    public static readonly CardPool Ironclad = new("Ironclad", CardPoolLists.Ironclad);

    /// <summary>The 64 colorless cards (53 in a single-player run).</summary>
    public static readonly CardPool Colorless = new("Colorless", CardPoolLists.Colorless);

    /// <summary>The 18 curses.</summary>
    public static readonly CardPool Curse = new("Curse", CardPoolLists.Curse);

    /// <summary>The 27 cards only events and Ancients give.</summary>
    public static readonly CardPool Event = new("Event", CardPoolLists.Event);

    /// <summary>The 3 quest cards.</summary>
    public static readonly CardPool Quest = new("Quest", CardPoolLists.Quest);

    /// <summary>The 12 status cards.</summary>
    public static readonly CardPool Status = new("Status", CardPoolLists.Status);

    /// <summary>The 14 cards only other cards and relics make.</summary>
    public static readonly CardPool Token = new("Token", CardPoolLists.Token);

    /// <summary>
    /// The seven pools in the order the game looks a card's pool up. The game has five more
    /// before and between them (the other four characters' and one of withdrawn cards); no card
    /// of these seven is in any of those, so leaving them out changes no lookup.
    /// </summary>
    public static readonly IReadOnlyList<CardPool> All = [Ironclad, Colorless, Curse, Event, Quest, Status, Token];

    /// <summary>The pool the character's card rewards, shop cards and random character cards come from.</summary>
    public static CardPool Character => Ironclad;

    /// <summary>The pool a card belongs to (the game's CardModel.Pool: the first pool that lists it), or null for a
    /// card that is in none of the seven (a check's own card).</summary>
    public static CardPool? Of(string className)
    {
        foreach (var pool in All)
            if (pool.Contains(className)) return pool;
        return null;
    }

    /// <summary>The game's entry for a class name in whichever pool holds it, or null.</summary>
    public static GameCard? Find(string className) => Of(className)?.Find(className);

    /// <summary>Whether the arena has built a card: its definition is in the registry.</summary>
    public static bool IsBuilt(string className) => Content.Registry.Has(ContentKind.Card, className);

    /// <summary>
    /// What a card can turn into when it is transformed at random outside a fight (the game's
    /// GetDefaultTransformationOptions), in the game's order, built or not:
    /// the cards of the original's own pool, or of the colorless pool for a Quest card and for a
    /// card of Event, Ancient or Token rarity; of those the unlocked ones a single-player run
    /// can hold; only Common, Uncommon and Rare ones unless the original is of Status or Curse
    /// rarity (a curse becomes any other curse); never the original's own class.
    /// </summary>
    /// <exception cref="InvalidOperationException">The card is in no pool, or nothing is left to turn into (the game fails the same way).</exception>
    public static IReadOnlyList<GameCard> TransformOptions(CardInstance original, RunUnlocks unlocks)
    {
        var def = original.Def;
        var toColorless = def.Type == CardType.Quest || def.Rarity is CardRarity.Event or CardRarity.Ancient or CardRarity.Token;
        var pool = toColorless ? Colorless : Of(def.ClassName) ?? throw new InvalidOperationException($"{def.ClassName} is in no card pool, so it cannot be transformed at random.");
        IEnumerable<GameCard> options = pool.Unlocked(unlocks);
        if (def.Rarity is not (CardRarity.Status or CardRarity.Curse))
            options = options.Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare);
        var list = options.Where(c => c.ClassName != def.ClassName).ToList();
        if (list.Count == 0) throw new InvalidOperationException($"{def.ClassName} has nothing to be transformed into.");
        return list;
    }

    /// <summary>
    /// The game's pick of one card from a list (one number, an index into the list), with the
    /// arena's house rule for cards it has not built: the pick runs over the game's full list,
    /// and a pick that lands on a card that is not built is drawn again from the same stream.
    /// </summary>
    /// <param name="rng">The stream of this one decision; the redraws come from it too.</param>
    /// <param name="options">The game's list for this pick, in its order, unbuilt cards included.</param>
    /// <param name="passedOver">Gets the class name of every card a pick landed on and passed over.</param>
    /// <returns>The built card's definition, or null when no card of the list is built (nothing is drawn then).</returns>
    /// <exception cref="ArgumentException">The list is empty: the game has nothing to pick from either.</exception>
    public static CardDef? NextBuilt(GameRng rng, IReadOnlyList<GameCard> options, ICollection<string>? passedOver = null)
    {
        if (options.Count == 0) throw new ArgumentException("There is no card to pick from.", nameof(options));
        if (!options.Any(c => IsBuilt(c.ClassName))) return null;
        while (true)
        {
            var landed = options[rng.NextInt(0, options.Count)];
            if (IsBuilt(landed.ClassName)) return Content.Registry.Card(landed.ClassName);
            passedOver?.Add(landed.ClassName);
        }
    }

    /// <summary>
    /// <see cref="NextBuilt"/> for a run: every card passed over is written to the run's
    /// <see cref="LeftOutLog"/> (in strict mode the first one ends the run), and a list with no
    /// built card at all ends the run as unrepresentable, since no redraw can help.
    /// </summary>
    /// <param name="what">What is being rolled, for the log and the reason ("a transform of Strike").</param>
    /// <exception cref="RunUnrepresentableException">No card of the list is built, or strict mode met an unbuilt card.</exception>
    public static CardDef Pick(Run run, GameRng rng, IReadOnlyList<GameCard> options, string what)
    {
        if (options.Count == 0) throw new ArgumentException("There is no card to pick from.", nameof(options));
        if (!options.Any(c => IsBuilt(c.ClassName)))
            throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture,
                $"{what} on floor {run.State.TotalFloor} can only land on cards the arena leaves out ({string.Join(", ", options.Take(4).Select(c => c.ClassName))}{(options.Count > 4 ? ", ..." : "")})"));
        while (true)
        {
            var landed = options[rng.NextInt(0, options.Count)];
            if (IsBuilt(landed.ClassName)) return Content.Registry.Card(landed.ClassName);
            // The log is made the first time something is passed over, so a run that never draws again has none.
            run.State.Part<LeftOutLog>().PassedOver(run.State, "card", landed.ClassName, what);
        }
    }

    /// <summary>The unlock state rolls of this run go by: the run's world when it has one, else everything unlocked.</summary>
    public static RunUnlocks UnlocksOf(Run run) => run.State.FindPart<RunWorld>() is { IsBegun: true } world ? world.Unlocks : RunUnlocks.All;
}
