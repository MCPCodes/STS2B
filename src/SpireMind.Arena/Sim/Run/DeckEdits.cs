namespace SpireMind.Arena;

/// <summary>
/// The game's hooks that hear the deck change outside a fight, besides the one that is declared
/// with the run's other hooks on <see cref="Listener"/>
/// (<see cref="Listener.TryModifyCardBeingAddedToDeck"/> and its late pass: the Eggs). A relic, a
/// deck card's hook object (<see cref="CardBehaviour"/>) or a potion's hooks takes part by
/// implementing this; every method has the game's name, is handed the run's
/// <see cref="RunContext"/> like every hook of the run (<c>c.Run</c> is there: these are heard
/// outside a fight) and does nothing by default. Who is asked, and in which order, is
/// <see cref="Run.Listeners"/>.
/// <para>
/// The interface carries <see cref="IListensInDeck"/>, so a card's hook class that implements
/// it is heard as the deck card (Spoils Map hears its own removal) and stands under that
/// marker's rule; on a relic or a potion the marker says nothing.
/// </para>
/// </summary>
public interface IDeckListener : IListensInDeck
{
    /// <summary>False keeps the card out of the deck (the game's ShouldAddToDeck). Nothing in the game says no in this build.</summary>
    bool ShouldAddToDeck(RunContext c, CardInstance card) => true;

    /// <summary>Told to the one listener whose <see cref="ShouldAddToDeck"/> kept a card out.</summary>
    void AfterAddToDeckPrevented(RunContext c, CardInstance card) { }

    /// <summary>
    /// A card is in the deck now (the game's AfterCardChangedPiles, as far as it is heard outside
    /// a fight: Lucky Fysh pays gold, Book of Five Rings counts). <paramref name="oldPile"/> is
    /// None for a card that was added and Deck for the card a transform put in. Inside a fight
    /// the same hook of the game is <see cref="Listener.AfterCardChangedPiles"/>.
    /// </summary>
    void AfterCardChangedPiles(RunContext c, CardInstance card, PileType oldPile) { }

    /// <summary>The same, asked of every listener after all of them heard <see cref="AfterCardChangedPiles"/>.</summary>
    void AfterCardChangedPilesLate(RunContext c, CardInstance card, PileType oldPile) { }

    /// <summary>A card is about to be removed from the deck and is still in it (the game's BeforeCardRemoved: Spoils Map).
    /// A transform does not call this.</summary>
    void BeforeCardRemoved(RunContext c, CardInstance card) { }
}

/// <summary>
/// The call sites of the deck hooks: one method per hook of the game, each asking every
/// listener of the run in the game's order (<see cref="Run.Listeners"/>, the one definition of
/// that order: the deck cards that listen in deck order, the relics in pickup order, the
/// potions slot by slot; the list is taken first and each listener is asked only if it is
/// still there when its turn comes, so a card a hook just removed is not asked, and a card a
/// hook just added is not either). <see cref="DeckEdits"/> calls them at the moments the game
/// does. The hook that changes a card on its way in is sent by
/// <see cref="RunHooks.ModifyCardBeingAddedToDeck"/>, with the run's other hooks.
/// </summary>
public static class DeckHooks
{
    /// <summary>Hook: ShouldAddToDeck. False, with the listener that said no, when one keeps the card out.</summary>
    public static bool ShouldAddToDeck(Run run, CardInstance card, out Listener? preventer)
    {
        var c = run.Context;
        foreach (var listener in run.Listeners())
        {
            if (listener is IDeckListener deck && !deck.ShouldAddToDeck(c, card))
            {
                preventer = listener;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    /// <summary>Hook: AfterAddToDeckPrevented, told to the one listener that kept the card out.</summary>
    public static void AfterAddToDeckPrevented(Run run, Listener preventer, CardInstance card) =>
        (preventer as IDeckListener)?.AfterAddToDeckPrevented(run.Context, card);

    /// <summary>Hooks: AfterCardChangedPiles, then AfterCardChangedPilesLate, for a card that is now in the deck.</summary>
    public static void AfterCardChangedPiles(Run run, CardInstance card, PileType oldPile)
    {
        var c = run.Context;
        foreach (var listener in run.Listeners())
            (listener as IDeckListener)?.AfterCardChangedPiles(c, card, oldPile);
        foreach (var listener in run.Listeners())
            (listener as IDeckListener)?.AfterCardChangedPilesLate(c, card, oldPile);
    }

    /// <summary>Hook: BeforeCardRemoved, for a card that is still in the deck.</summary>
    public static void BeforeCardRemoved(Run run, CardInstance card)
    {
        var c = run.Context;
        foreach (var listener in run.Listeners())
            (listener as IDeckListener)?.BeforeCardRemoved(c, card);
    }

    internal static bool InDeck(RunState state, CardInstance card)
    {
        foreach (var held in state.Deck)
            if (ReferenceEquals(held, card)) return true;
        return false;
    }
}

/// <summary>What became of one card that was to go into the deck.</summary>
/// <param name="Success">False when the card did not go in: a listener kept it out, or the player is dead.</param>
/// <param name="Card">The card that went in. It is another object than the one handed over when a listener replaced it.</param>
/// <param name="ModifiedBy">The listeners that replaced the card on its way in, in order (the Eggs).</param>
public readonly record struct DeckAddResult(bool Success, CardInstance Card, IReadOnlyList<Listener> ModifiedBy);

/// <summary>
/// One card to transform (the game's CardTransformation): into <paramref name="Replacement"/>
/// when one is named, else into a card rolled by the game's rule for the original's kind
/// (<see cref="CardPools.TransformOptions"/>).
/// </summary>
public sealed record CardTransformation(CardInstance Original, CardInstance? Replacement = null);

/// <summary>
/// The game's commands that change the deck outside a fight, with their hooks, in the game's
/// order: add (CardPileCmd.Add to the deck), remove (CardPileCmd.RemoveFromDeck), upgrade
/// (CardCmd.Upgrade) and transform (CardCmd.Transform, TransformTo, TransformToRandom). The
/// plain list operations of <see cref="RunState"/> call no hook; these do.
/// <para>
/// They change <see cref="Run.State"/>, so they are for outside a fight: while a fight is on
/// the state is the player as the fight began and the fight holds the live deck
/// (<see cref="Fight.DeckList"/>). Called during a fight they throw.
/// </para>
/// </summary>
public static class DeckEdits
{
    /// <summary>The longest list .NET's sort handles by insertion, which is the only part of it rebuilt here.</summary>
    private const int LargestSortedTransform = 16;

    private static RunState Outside(Run run) =>
        run.Fight is null ? run.State : throw new InvalidOperationException("The deck commands of a run are for outside a fight; a fight changes its own deck (Fight.DeckList).");

    // ---------- what may be done to a card ----------

    /// <summary>A card can be removed from the deck unless it is Eternal (the game's IsRemovable).</summary>
    public static bool IsRemovable(CardInstance card) => !card.Has(CardKeywords.Eternal);

    /// <summary>A card can be transformed unless it is an Eternal card sitting in the deck (the game's IsTransformable).</summary>
    public static bool IsTransformable(Run run, CardInstance card) => IsRemovable(card) || !DeckHooks.InDeck(run.State, card);

    // ---------- add ----------

    /// <summary>Adds one card to the end of the deck as the game does, with the card-added hooks. See <see cref="Add(Run, IReadOnlyList{CardInstance})"/>.</summary>
    public static DeckAddResult Add(Run run, CardInstance card) => Add(run, [card])[0];

    /// <summary>
    /// Adds cards to the end of the deck in the order given (the game's CardPileCmd.Add with the
    /// deck as the pile). The game's order, which shows when several cards go in at once: first
    /// every card is put to ShouldAddToDeck; then each card that may go in passes
    /// ModifyCardBeingAddedToDeck and is appended; only then AfterCardChangedPiles is heard for
    /// each of them, with old pile None. Nothing goes in while the player is dead.
    /// </summary>
    /// <param name="cards">Cards that are in no deck yet: made with <see cref="RunState.NewCard"/>, or offered and taken.</param>
    /// <exception cref="InvalidOperationException">A card is already in the deck, or a fight is on.</exception>
    public static IReadOnlyList<DeckAddResult> Add(Run run, IReadOnlyList<CardInstance> cards)
    {
        var state = Outside(run);
        var results = new DeckAddResult[cards.Count];
        for (var i = 0; i < cards.Count; i++)
        {
            if (DeckHooks.InDeck(state, cards[i])) throw new InvalidOperationException($"{cards[i]} is already in the deck.");
            results[i] = new DeckAddResult(!state.IsDead, cards[i], []);
        }

        for (var i = 0; i < results.Length; i++)
        {
            if (!results[i].Success || DeckHooks.ShouldAddToDeck(run, results[i].Card, out var preventer)) continue;
            DeckHooks.AfterAddToDeckPrevented(run, preventer!, results[i].Card);
            results[i] = results[i] with { Success = false };
        }

        for (var i = 0; i < results.Length; i++)
        {
            if (!results[i].Success) continue;
            var card = RunHooks.ModifyCardBeingAddedToDeck(run.Context, results[i].Card, out var modifiers);
            results[i] = new DeckAddResult(true, card, modifiers);
            state.PutInDeck(card);
        }

        foreach (var result in results)
            if (result.Success) DeckHooks.AfterCardChangedPiles(run, result.Card, PileType.None);
        return results;
    }

    /// <summary>Makes a new card of this definition and adds it to the deck (the game's CreateCard, then Add).</summary>
    public static DeckAddResult AddNew(Run run, CardDef def, bool upgraded = false) => Add(run, Outside(run).NewCard(def, upgraded));

    /// <summary>The same by class name.</summary>
    public static DeckAddResult AddNew(Run run, string className, bool upgraded = false) => AddNew(run, Content.Registry.Card(className), upgraded);

    /// <summary>
    /// Adds curses to the deck (the game's AddCursesToDeck): each is made and added on its own,
    /// so the hooks of one curse are done before the next goes in.
    /// </summary>
    /// <exception cref="ArgumentException">A definition is not a curse.</exception>
    public static IReadOnlyList<DeckAddResult> AddCurses(Run run, IEnumerable<CardDef> curses)
    {
        var state = Outside(run);
        var results = new List<DeckAddResult>();
        foreach (var curse in curses)
        {
            if (curse.Type != CardType.Curse) throw new ArgumentException($"{curse.ClassName} is not a curse.", nameof(curses));
            results.Add(Add(run, state.NewCard(curse)));
        }
        return results;
    }

    // ---------- remove ----------

    /// <summary>Removes one card from the deck as the game does. See <see cref="Remove(Run, IReadOnlyList{CardInstance})"/>.</summary>
    public static void Remove(Run run, CardInstance card) => Remove(run, [card]);

    /// <summary>
    /// Removes cards from the deck in the order given (the game's CardPileCmd.RemoveFromDeck):
    /// for each card BeforeCardRemoved is heard while it is still in the deck, then it leaves.
    /// Whether a card may be removed (<see cref="IsRemovable"/>) is asked by the screen that
    /// offers the choice, not here, as in the game.
    /// </summary>
    /// <exception cref="InvalidOperationException">A card is not in the deck, or a fight is on.</exception>
    public static void Remove(Run run, IReadOnlyList<CardInstance> cards)
    {
        var state = Outside(run);
        foreach (var card in cards)
        {
            if (!DeckHooks.InDeck(state, card)) throw new InvalidOperationException($"{card} is not in the deck, so it cannot be removed from it.");
            DeckHooks.BeforeCardRemoved(run, card);
            state.RemoveCard(card);
        }
    }

    // ---------- upgrade ----------

    /// <summary>Upgrades one card (the game's CardCmd.Upgrade). See <see cref="Upgrade(Run, IEnumerable{CardInstance})"/>.</summary>
    public static void Upgrade(Run run, CardInstance card) => Upgrade(run, [card]);

    /// <summary>
    /// Upgrades cards (the game's CardCmd.Upgrade): a card that cannot be upgraded any further is
    /// passed over, and no hook hears an upgrade. The cards need not be in the deck: a card on
    /// offer is upgraded the same way.
    /// </summary>
    public static void Upgrade(Run run, IEnumerable<CardInstance> cards)
    {
        Outside(run);
        foreach (var card in cards)
            if (card.IsUpgradable) card.ApplyUpgrade();
    }

    // ---------- transform ----------

    /// <summary>Transforms one deck card into a card rolled by the game's rule (the game's TransformToRandom).</summary>
    /// <param name="rng">The stream the source of the transform names: the player's transformations stream, niche, or an event's own.</param>
    public static DeckAddResult TransformToRandom(Run run, CardInstance original, GameRng rng) =>
        Transform(run, [new CardTransformation(original)], rng)[0];

    /// <summary>Transforms one deck card into a new card of this definition (the game's TransformTo).</summary>
    public static DeckAddResult TransformTo(Run run, CardInstance original, CardDef replacement) =>
        Transform(run, original, Outside(run).NewCard(replacement));

    /// <summary>Transforms one deck card into a card that already exists and is in no deck (the game's Transform with a named replacement).</summary>
    public static DeckAddResult Transform(Run run, CardInstance original, CardInstance replacement) =>
        Transform(run, [new CardTransformation(original, replacement)])[0];

    /// <summary>Transforms several deck cards whose replacements are all named: nothing is rolled.</summary>
    public static IReadOnlyList<DeckAddResult> Transform(Run run, IReadOnlyList<CardTransformation> transformations) =>
        TransformAll(run, transformations, null);

    /// <summary>Transforms several deck cards with every roll on one stream, as the game draws them (the game's Transform with its one stream).</summary>
    public static IReadOnlyList<DeckAddResult> Transform(Run run, IReadOnlyList<CardTransformation> transformations, GameRng rng) =>
        TransformAll(run, transformations, _ => rng);

    /// <summary>
    /// Transforms deck cards (the game's CardCmd.Transform), in the game's order. First, in the
    /// order given, each original is checked, its place in the deck is noted, its replacement
    /// is rolled (one pick over <see cref="CardPools.TransformOptions"/>, drawn again when it
    /// lands on a card that is not built) and the original leaves the deck at once, so a later
    /// original's place is counted without the earlier ones. Then the replacements go in,
    /// ordered by the place of their originals as the game's sort leaves them: each passes
    /// ModifyCardBeingAddedToDeck (but not ShouldAddToDeck), is put at the END of the deck (not
    /// where the original was) and is heard as AfterCardChangedPiles with old pile Deck.
    /// BeforeCardRemoved is not heard for an original.
    /// </summary>
    /// <param name="rngFor">The stream for the roll of the n-th transformation (from 0): in game mode all of them give the
    /// same long stream; in keyed mode each roll has its own (Purpose.Transform(n)).</param>
    /// <returns>One result per transformation, in the order the replacements went into the deck.</returns>
    /// <exception cref="InvalidOperationException">An original is Eternal, or not in the deck; or a fight is on.</exception>
    /// <exception cref="RunUnrepresentableException">A roll can only land on cards the arena has not built (or strict mode met one).</exception>
    public static IReadOnlyList<DeckAddResult> Transform(Run run, IReadOnlyList<CardTransformation> transformations, Func<int, GameRng> rngFor) =>
        TransformAll(run, transformations, rngFor);

    private static IReadOnlyList<DeckAddResult> TransformAll(Run run, IReadOnlyList<CardTransformation> transformations, Func<int, GameRng>? rngFor)
    {
        var state = Outside(run);
        if (transformations.Count == 0) return [];
        if (transformations.Count > LargestSortedTransform)
            throw new NotSupportedException($"The arena transforms at most {LargestSortedTransform} cards at once: beyond that the order the game's sort gives equal places is not rebuilt.");

        var unlocks = CardPools.UnlocksOf(run);
        var rolled = new List<(int Place, CardInstance Replacement)>();
        for (var n = 0; n < transformations.Count; n++)
        {
            var (original, named) = transformations[n];
            if (!IsTransformable(run, original)) throw new InvalidOperationException($"{original} cannot be transformed: it is Eternal.");
            var place = IndexInDeck(state, original);
            if (place < 0) throw new InvalidOperationException($"{original} is not in the deck, so it cannot be transformed there.");

            var replacement = named;
            if (replacement is null)
            {
                if (rngFor is null) throw new ArgumentException("A random transform needs a stream to roll on.", nameof(transformations));
                var def = CardPools.Pick(run, rngFor(n), CardPools.TransformOptions(original, unlocks), $"a transform of {original.Def.Name}");
                replacement = state.NewCard(def);
            }
            state.RemoveCard(original);
            rolled.Add((place, replacement));
        }

        SortAsTheGame(rolled, (a, b) => a.Place.CompareTo(b.Place));
        var results = new List<DeckAddResult>(rolled.Count);
        foreach (var (_, replacement) in rolled)
        {
            var card = RunHooks.ModifyCardBeingAddedToDeck(run.Context, replacement, out var modifiers);
            state.PutInDeck(card);
            DeckHooks.AfterCardChangedPiles(run, card, PileType.Deck);
            results.Add(new DeckAddResult(true, card, modifiers));
        }
        return results;
    }

    private static int IndexInDeck(RunState state, CardInstance card)
    {
        for (var i = 0; i < state.Deck.Count; i++)
            if (ReferenceEquals(state.Deck[i], card)) return i;
        return -1;
    }

    /// <summary>
    /// The game sorts the transformations with .NET's List.Sort, which does not keep equal keys
    /// in their order in every case, and equal places do occur (two neighbours transformed in
    /// deck order both note the same place). For up to sixteen items that sort is: two items, one
    /// compare-and-swap; three items, three compare-and-swaps (first and second, first and
    /// third, second and third: this one can turn two equal keys around); more, a plain
    /// insertion sort. Rebuilt here so that the order does not hang on the runtime.
    /// </summary>
    private static void SortAsTheGame<T>(List<T> items, Comparison<T> compare)
    {
        void SwapIfGreater(int i, int j)
        {
            if (compare(items[i], items[j]) > 0) (items[i], items[j]) = (items[j], items[i]);
        }

        switch (items.Count)
        {
            case < 2:
                return;
            case 2:
                SwapIfGreater(0, 1);
                return;
            case 3:
                SwapIfGreater(0, 1);
                SwapIfGreater(0, 2);
                SwapIfGreater(1, 2);
                return;
        }

        for (var i = 0; i < items.Count - 1; i++)
        {
            var moving = items[i + 1];
            var j = i;
            while (j >= 0 && compare(moving, items[j]) < 0)
            {
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = moving;
        }
    }
}
