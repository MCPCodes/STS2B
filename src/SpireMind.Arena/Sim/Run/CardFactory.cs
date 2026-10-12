namespace SpireMind.Arena;

/// <summary>What a card is made for (the game's CardCreationSource). Hooks ask it: some relics change only the cards of a fight's reward.</summary>
public enum CardCreationSource
{
    None,
    /// <summary>The card reward after a fight.</summary>
    Encounter,
    Shop,
    /// <summary>A reward an event or a relic gives.</summary>
    Other,
}

/// <summary>What may change the cards of an offer (the game's CardCreationFlags, with its numbers).</summary>
[Flags]
public enum CardCreationFlags
{
    None = 0,
    /// <summary>The source names the rarity; nothing may change it.</summary>
    NoRarityModification = 1,
    /// <summary>No upgrade roll: the number is not even drawn.</summary>
    NoUpgradeRoll = 2,
    /// <summary>Hooks that upgrade offered cards (the Eggs) stay out.</summary>
    NoHookUpgrades = 4,
    /// <summary>The "cards were rolled" hooks are not asked at all (a hook that makes a card itself sets this).</summary>
    NoModifyHooks = 8,
    /// <summary>Hooks may not change the pools the cards are rolled from.</summary>
    NoCardPoolModifications = 0x10,
    /// <summary>Hooks may not swap the cards for others (they may still upgrade them).</summary>
    NoCardModelModifications = 0x20,
    /// <summary>The roll moves the run's pity offset although it is not a fight's reward.</summary>
    ForceRarityOddsChange = 0x40,
    /// <summary>The cards are those of a card reward; <see cref="CardReward"/> sets it.</summary>
    IsCardReward = 0x80,
    NoUpgrades = NoUpgradeRoll | NoHookUpgrades,
    NoModifications = -1,
}

/// <summary>
/// How the cards of an offer are rolled (the game's CardCreationOptions): from which pools
/// (or from a list of the caller's), for what, on which table of rarity odds, under which
/// flags. As in the game it is one changeable object that the offer owns: <c>With...</c>
/// changes it and returns it, and a hook that changes the pools changes them for every card
/// of the offer that is still to be rolled.
/// <para>
/// A pool filter is a function over <see cref="GameCard"/>, the game's own entry for a card
/// whether the arena has built it or not: it may ask the type, the rarity and the flags, and
/// must not hold anything of a run, since the options are copied with the run.
/// </para>
/// <para>
/// Left out: the game's rng override (the Crystal Sphere of act 2 rolls its rewards on the
/// event's own stream), and the lookup that picks the reward's icon.
/// </para>
/// </summary>
public sealed class CardCreationOptions
{
    private readonly List<CardPool> _cardPools = new();

    /// <summary>The pools the cards come from; empty when <see cref="CustomCardPool"/> is set.</summary>
    public IReadOnlyList<CardPool> CardPools => _cardPools;

    /// <summary>Which cards of the pools count; null for all.</summary>
    public Func<GameCard, bool>? CardPoolFilter { get; private set; }

    /// <summary>The caller's own list of cards, instead of pools.</summary>
    public IReadOnlyList<GameCard>? CustomCardPool { get; private set; }

    public CardCreationSource Source { get; }

    /// <summary>The table the rarity is rolled on; <see cref="CardOddsType.Uniform"/> rolls no rarity at all.</summary>
    public CardOddsType RarityOdds { get; private set; }

    public CardCreationFlags Flags { get; private set; }

    public CardCreationOptions(IEnumerable<CardPool> cardPools, CardCreationSource source, CardOddsType rarityOdds, Func<GameCard, bool>? cardPoolFilter = null)
    {
        _cardPools.AddRange(cardPools);
        Source = source;
        RarityOdds = rarityOdds;
        CardPoolFilter = cardPoolFilter;
    }

    /// <exception cref="InvalidOperationException">The list holds one rarity only and the odds are not uniform: the game refuses that, since a rolled rarity could miss the list.</exception>
    public CardCreationOptions(IEnumerable<GameCard> customCardPool, CardCreationSource source, CardOddsType rarityOdds)
    {
        CustomCardPool = customCardPool.ToList();
        Source = source;
        RarityOdds = rarityOdds;
        AssertUniformOddsIfSingleRarityPool();
    }

    private CardCreationOptions(CardCreationOptions other)
    {
        _cardPools.AddRange(other._cardPools);
        CardPoolFilter = other.CardPoolFilter;
        CustomCardPool = other.CustomCardPool;
        Source = other.Source;
        RarityOdds = other.RarityOdds;
        Flags = other.Flags;
    }

    /// <summary>A copy for a copy of the run: the same pools, list, filter and flags in an object of its own.</summary>
    public CardCreationOptions Clone() => new(this);

    /// <summary>
    /// The options of a room's own cards (CardCreationOptions.ForRoom): the character's pool;
    /// a fight's reward on the regular, elite or boss table, a shop on the shop table.
    /// </summary>
    /// <exception cref="InvalidOperationException">An event room: the game refuses it too.</exception>
    public static CardCreationOptions ForRoom(RoomKind room)
    {
        var source = room switch
        {
            RoomKind.Monster or RoomKind.Elite or RoomKind.Boss => CardCreationSource.Encounter,
            RoomKind.Shop => CardCreationSource.Shop,
            RoomKind.Event => throw new InvalidOperationException("ForRoom should not be used in event rooms"),
            _ => CardCreationSource.Other,
        };
        var odds = room switch
        {
            RoomKind.Monster => CardOddsType.Regular,
            RoomKind.Elite => CardOddsType.Elite,
            RoomKind.Boss => CardOddsType.Boss,
            RoomKind.Shop => CardOddsType.Shop,
            _ => CardOddsType.Regular,
        };
        return new CardCreationOptions([SpireMind.Arena.CardPools.Character], source, odds);
    }

    /// <summary>For an event or a relic: the regular table without the pity offset, and no upgrade roll.</summary>
    public static CardCreationOptions ForNonCombatWithDefaultOdds(IEnumerable<CardPool> cardPools, Func<GameCard, bool>? cardPoolFilter = null) =>
        new CardCreationOptions(cardPools, CardCreationSource.Other, CardOddsType.Regular, cardPoolFilter).WithFlags(CardCreationFlags.NoUpgradeRoll);

    /// <summary>The same over a list of the caller's. Not for a list of one rarity: use uniform odds there.</summary>
    public static CardCreationOptions ForNonCombatWithDefaultOdds(IEnumerable<GameCard> customCardPool) =>
        new CardCreationOptions(customCardPool, CardCreationSource.Other, CardOddsType.Regular).WithFlags(CardCreationFlags.NoUpgradeRoll);

    /// <summary>For an event or a relic: every card of the pools as likely as any other, and no upgrade roll.</summary>
    public static CardCreationOptions ForNonCombatWithUniformOdds(IEnumerable<CardPool> cardPools, Func<GameCard, bool>? cardPoolFilter = null) =>
        new CardCreationOptions(cardPools, CardCreationSource.Other, CardOddsType.Uniform, cardPoolFilter).WithFlags(CardCreationFlags.NoUpgradeRoll);

    /// <summary>
    /// Every card these options can give in this run, in the game's order, built or not: the
    /// caller's list, or the unlocked single-player cards of the pools that pass the filter.
    /// </summary>
    public IEnumerable<GameCard> GetPossibleCards(Run run)
    {
        if (_cardPools.Count <= 0)
            return CustomCardPool ?? throw new InvalidOperationException("Tried to get possible cards from CardCreationOptions but neither the pool nor custom pool were set!");
        var unlocks = SpireMind.Arena.CardPools.UnlocksOf(run);
        return _cardPools.SelectMany(pool => pool.Unlocked(unlocks)).Where(card => CardPoolFilter is null || CardPoolFilter(card));
    }

    /// <summary>Rolls from a list of the caller's from now on; the pools are dropped.</summary>
    public CardCreationOptions WithCustomPool(IEnumerable<GameCard> customPool, CardOddsType? rarityOdds = null)
    {
        CustomCardPool = customPool.ToList();
        _cardPools.Clear();
        RarityOdds = rarityOdds ?? RarityOdds;
        AssertUniformOddsIfSingleRarityPool();
        return this;
    }

    /// <summary>Rolls from these pools from now on; a list of the caller's is dropped.</summary>
    public CardCreationOptions WithCardPools(IEnumerable<CardPool> pools, Func<GameCard, bool>? cardPoolFilter = null)
    {
        var list = pools.ToList();
        _cardPools.Clear();
        _cardPools.AddRange(list);
        CardPoolFilter = cardPoolFilter;
        CustomCardPool = null;
        return this;
    }

    /// <summary>Adds flags.</summary>
    public CardCreationOptions WithFlags(CardCreationFlags flag)
    {
        Flags |= flag;
        return this;
    }

    private void AssertUniformOddsIfSingleRarityPool()
    {
        if (CustomCardPool is null || RarityOdds == CardOddsType.Uniform || CustomCardPool.Count == 0) return;
        var first = CustomCardPool[0];
        if (CustomCardPool.All(card => card.Rarity == first.Rarity))
            throw new InvalidOperationException($"A custom card pool with only one rarity was passed to CardCreationOptions with a rarity odds of {RarityOdds}: card pools with only one rarity must use Uniform rarity odds.");
    }
}

/// <summary>
/// One card of an offer (the game's CardCreationResult): the card as it was rolled, and the
/// card a hook put in its place, if any. A hook never changes the rolled card itself: it makes
/// a copy, changes that, and hands it to <see cref="ModifyCard"/>.
/// </summary>
public sealed class CardCreationResult
{
    private CardInstance? _modifiedCard;
    private readonly List<string> _modifyingRelics = new();

    /// <summary>The card as it was rolled.</summary>
    public CardInstance OriginalCard { get; }

    /// <summary>The card on offer: the last one a hook put in place, else the rolled one.</summary>
    public CardInstance Card => _modifiedCard ?? OriginalCard;

    public bool HasBeenModified => _modifiedCard is not null;

    /// <summary>The class names of the relics that changed the card, in order.</summary>
    public IReadOnlyList<string> ModifyingRelics => _modifyingRelics;

    public CardCreationResult(CardInstance originalCard) => OriginalCard = originalCard;

    /// <summary>Puts another card in place of the one on offer. Make it with <see cref="RunState.NewCard"/>, so that it has an id of its own.</summary>
    public void ModifyCard(CardInstance card, Relic? modifyingRelic = null)
    {
        _modifiedCard = card;
        if (modifyingRelic is not null) _modifyingRelics.Add(modifyingRelic.ClassName);
    }

    /// <summary>A copy with copies of its cards, for a copy of the run.</summary>
    internal CardCreationResult Clone()
    {
        var copy = new CardCreationResult(CopyOf(OriginalCard));
        if (_modifiedCard is not null) copy._modifiedCard = CopyOf(_modifiedCard);
        copy._modifyingRelics.AddRange(_modifyingRelics);
        return copy;
    }

    /// <summary>The same card (id, upgrade, numbers, hook state) as an object of its own: a card on offer is in no
    /// deck, so a copy of the run has to copy it by itself.</summary>
    internal static CardInstance CopyOf(CardInstance card)
    {
        var map = new CloneMap();
        var copy = card.CloneFor(map);
        map.Finish();
        return copy;
    }
}

/// <summary>
/// Makes the cards the game makes outside a fight for the player to choose from: the cards of a
/// card reward (and of everything else that rolls "cards for a reward": events, relics) and the
/// cards of a shop. A port of the game's CardFactory, number for number.
/// <para>
/// One card of a reward takes, in this order: a rarity roll (not with uniform odds), a pick
/// among the cards of that rarity the offer does not show yet, and an upgrade roll (unless the
/// options say no upgrade roll). All three come from one draw of the player's rewards stream,
/// named by what the card is (<see cref="Purpose.Card"/>).
/// </para>
/// <para>
/// A pick runs over the game's full list, cards the arena has not built included, and one that
/// lands on such a card is drawn again from the same stream (<see cref="CardPools.Pick"/>): a
/// house rule, written to the run's <see cref="LeftOutLog"/>; in strict mode the run cannot go
/// on instead. The rarity is not rolled again: its roll has already moved the run's pity offset.
/// </para>
/// <para>
/// The game's CardFactory also makes cards inside a fight (those rules have been the fight
/// engine's since the single fights) and says what a transformed card may become
/// (<see cref="CardPools.TransformOptions"/>, with the deck edits).
/// </para>
/// </summary>
public static class CardFactory
{
    /// <summary>What a reward card's upgrade chance grows by per act (the game halves it at an ascension level the arena does not play).</summary>
    public const decimal UpgradedCardOddScaling = 0.25m;

    /// <summary>The upgrade chance a shop's card starts from: it can never come upgraded, but the roll is drawn.</summary>
    public const decimal MerchantUpgradeChance = -999999999m;

    private static RunStreams Streams(Run run) => run.State.Part<RunWorld>().Streams;

    /// <summary>A single-player run never sees the cards the game marks as multiplayer only.</summary>
    private static IEnumerable<GameCard> FilterForPlayerCount(IEnumerable<GameCard> options) => options.Where(card => !card.MultiplayerOnly);

    // ---------- cards for a reward ----------

    /// <summary>
    /// The cards of one offer (CardFactory.CreateForReward): <paramref name="cardCount"/> cards,
    /// no two of the same class. After the last one the "cards were rolled" hooks are asked,
    /// unless the options forbid it.
    /// </summary>
    /// <param name="purpose">Names the roll of card number i for keyed streams; a card reward passes its own number. The default is <c>Purpose.Card(0, i)</c>.</param>
    /// <exception cref="RunUnrepresentableException">A pick could only land on cards that are not built, or strict mode met one.</exception>
    /// <exception cref="InvalidOperationException">No rarity or no card is left to roll: the game fails the same way.</exception>
    public static List<CardCreationResult> CreateForReward(Run run, int cardCount, CardCreationOptions options, Func<int, Purpose>? purpose = null)
    {
        var blacklist = new List<string>();
        var results = new List<CardCreationResult>();
        for (var i = 0; i < cardCount; i++)
        {
            var rng = Streams(run).Draw(RunStream.Rewards, purpose?.Invoke(i) ?? Purpose.Card(0, i));
            var card = CreateForReward(run, blacklist, options, rng);
            blacklist.Add(card.Def.ClassName);
            results.Add(new CardCreationResult(card));
            if (!options.Flags.HasFlag(CardCreationFlags.NoUpgradeRoll)) RollForUpgrade(run, card, 0m, rng);
        }
        if (!options.Flags.HasFlag(CardCreationFlags.NoModifyHooks))
        {
            var modifiers = RewardHooks.ModifyCardRewardOptions(run, results, options);
            if (modifiers.Count > 0) RunHooks.AfterModifyingCardRewardOptions(run.Context, modifiers);
        }
        return results;
    }

    /// <summary>One card of an offer: the options hooks, the cards it may be, the rarity, the pick.</summary>
    private static CardInstance CreateForReward(Run run, List<string> blacklist, CardCreationOptions options, GameRng rng)
    {
        options = RewardHooks.ModifyCardRewardCreationOptions(run, options);

        // The game takes the possible cards without the ones the offer already shows as a set: a card listed twice counts once.
        var seen = new HashSet<string>(blacklist, StringComparer.Ordinal);
        var cards = new List<GameCard>();
        foreach (var card in options.GetPossibleCards(run))
            if (seen.Add(card.ClassName)) cards.Add(card);
        cards = FilterForPlayerCount(cards).ToList();

        CardRarity? selectedRarity = null;
        List<GameCard> items;
        if (options.RarityOdds == CardOddsType.Uniform)
        {
            items = cards.Where(card => card.Rarity is not (CardRarity.Basic or CardRarity.Ancient)).ToList();
        }
        else
        {
            var allowedRarities = cards.Select(card => card.Rarity).ToHashSet();
            selectedRarity = RollForRarity(run, rng, options.RarityOdds, options.Source, allowedRarities, options.Flags.HasFlag(CardCreationFlags.ForceRarityOddsChange));
            if (selectedRarity == CardRarity.None)
                throw new InvalidOperationException($"Tried to create a card for a reward, but no valid rarity could be rolled. Odds: {options.RarityOdds}, cards already offered: {string.Join(",", blacklist)}.");
            items = cards.Where(card => card.Rarity == selectedRarity).ToList();
        }

        if (items.Count == 0)
            throw new InvalidOperationException($"Tried to create a card for a reward, but no card is left to pick. Selected rarity: {selectedRarity}, odds: {options.RarityOdds}, cards already offered: {string.Join(",", blacklist)}.");
        var def = SpireMind.Arena.CardPools.Pick(run, rng, items, options.Flags.HasFlag(CardCreationFlags.IsCardReward) ? "a card of a card reward" : "a card offer");
        return run.State.NewCard(def);
    }

    /// <summary>
    /// The rarity of one card (CardFactory.RollForRarity). A fight's reward on the regular, elite
    /// or boss table, and any roll that forces it, is the roll that counts the pity offset and
    /// moves it. Everything else is rolled on the table's own odds with no offset, and there the
    /// game tests Uncommon against its odds alone (<see cref="CardRarityOdds.RollWithBaseOdds"/>).
    /// A rarity the cards do not have gives way to the next one (<see cref="GetNextAllowedRarity"/>).
    /// </summary>
    public static CardRarity RollForRarity(Run run, GameRng rng, CardOddsType rollMethod, CardCreationSource source, IReadOnlySet<CardRarity> allowedRarities, bool forceRarityOddsChange)
    {
        var changesOdds = forceRarityOddsChange
            || (source == CardCreationSource.Encounter && rollMethod is CardOddsType.Regular or CardOddsType.Elite or CardOddsType.Boss);
        var rarity = changesOdds
            ? RunWorld.RollCardRarity(run.State, rng, rollMethod)
            : CardRarityOdds.RollWithBaseOdds(rng, rollMethod);
        return GetNextAllowedRarity(rarity, allowedRarities.Contains);
    }

    /// <summary>
    /// The rolled rarity, or the next one that is allowed: Common gives way to Uncommon, Uncommon
    /// to Rare, Rare to Common; back at the rolled one there is none
    /// (<see cref="CardRarity.None"/>). The rarities that are never rolled lead nowhere.
    /// </summary>
    public static CardRarity GetNextAllowedRarity(CardRarity rarity, Func<CardRarity, bool> isAllowed)
    {
        var rolled = rarity;
        var steps = 0;
        while (!isAllowed(rarity) && rarity != CardRarity.None)
        {
            rarity = NextHighestRarityWithWrapping(rarity);
            if (rarity == rolled) return CardRarity.None;
            // Basic steps to Common and never comes back to itself: the game would walk Common, Uncommon, Rare for ever.
            // No roll gives Basic; a caller that passes it with nothing allowed is told instead.
            if (++steps > 4) throw new InvalidOperationException($"No rarity is allowed, and {rolled} is not one a roll can give.");
        }
        return rarity;
    }

    private static CardRarity NextHighestRarityWithWrapping(CardRarity rarity) => rarity switch
    {
        CardRarity.Basic => CardRarity.Common,
        CardRarity.Common => CardRarity.Uncommon,
        CardRarity.Uncommon => CardRarity.Rare,
        CardRarity.Rare => CardRarity.Common,
        _ => CardRarity.None,
    };

    /// <summary>
    /// The upgrade roll of a made card (CardFactory.RollForUpgrade). One number is drawn whatever
    /// the card is. A card that can be upgraded comes upgraded when the number is at or below
    /// the chance: the base chance, plus 0.25 per act already cleared unless the card is Rare,
    /// then whatever the hooks make of it. In the first act that chance is 0 for a reward card,
    /// so only a roll of exactly 0 would upgrade it.
    /// </summary>
    public static void RollForUpgrade(Run run, CardInstance card, decimal baseChance, GameRng rng)
    {
        var roll = (decimal)rng.NextFloat();
        if (!card.IsUpgradable) return;
        var odds = baseChance;
        if (card.Def.Rarity != CardRarity.Rare) odds += run.State.ActIndex * UpgradedCardOddScaling;
        odds = RewardHooks.ModifyCardRewardUpgradeOdds(run, card, odds);
        if (roll <= odds) card.ApplyUpgrade();
    }

    // ---------- cards for a shop ----------

    /// <summary>
    /// A shop card of a given type (CardFactory.CreateForMerchant, the character's five slots):
    /// the pool hooks, no Basic cards; the rarity rolled on the shop table with the run's pity
    /// offset, which stays as it is; a rarity that has no card of the type gives way to the next;
    /// the pick; the upgrade roll, which is drawn and cannot succeed.
    /// <para>
    /// The rarity and the upgrade roll come from the player's rewards stream, the pick from the
    /// shops stream, both named by <paramref name="purpose"/> (the slot).
    /// </para>
    /// </summary>
    /// <exception cref="RunUnrepresentableException">The pick could only land on cards that are not built, or strict mode met one.</exception>
    public static CardCreationResult CreateForMerchant(Run run, IEnumerable<GameCard> options, CardType type, Purpose purpose)
    {
        var cards = MerchantCards(run, options);
        var rewards = Streams(run).Draw(RunStream.Rewards, purpose);
        var rolled = new CardRarityOdds(run.State.CardRarityOffset).RollWithoutChangingFutureOdds(rewards, CardOddsType.Shop);
        var rarity = GetNextAllowedRarity(RewardHooks.ModifyMerchantCardRarity(run, rolled), r => cards.Any(card => card.Rarity == r && card.Type == type));
        if (rarity == CardRarity.None)
            throw new InvalidOperationException($"Can't generate a valid rarity for the merchant card type {type}.");
        var items = cards.Where(card => card.Rarity == rarity && card.Type == type).ToList();
        return MerchantCard(run, items, rewards, purpose);
    }

    /// <summary>
    /// A shop card of a given rarity (the two colorless slots): the pool hooks, no Basic cards,
    /// the rarity hooks on the rarity named, the pick, the upgrade roll that cannot succeed.
    /// </summary>
    /// <exception cref="RunUnrepresentableException">The pick could only land on cards that are not built, or strict mode met one.</exception>
    public static CardCreationResult CreateForMerchant(Run run, IEnumerable<GameCard> options, CardRarity rarity, Purpose purpose)
    {
        var cards = MerchantCards(run, options);
        var modifiedRarity = RewardHooks.ModifyMerchantCardRarity(run, rarity);
        var items = cards.Where(card => card.Rarity == modifiedRarity).ToList();
        return MerchantCard(run, items, Streams(run).Draw(RunStream.Rewards, purpose), purpose);
    }

    private static List<GameCard> MerchantCards(Run run, IEnumerable<GameCard> options)
    {
        options = RewardHooks.ModifyMerchantCardPool(run, options);
        options = options.Where(card => card.Rarity != CardRarity.Basic);
        return FilterForPlayerCount(options).ToList();
    }

    private static CardCreationResult MerchantCard(Run run, List<GameCard> items, GameRng rewards, Purpose purpose)
    {
        if (items.Count == 0) throw new InvalidOperationException("A shop slot has no card to pick from.");
        var shops = Streams(run).Draw(RunStream.Shops, purpose);
        var card = run.State.NewCard(SpireMind.Arena.CardPools.Pick(run, shops, items, "a shop card"));
        RollForUpgrade(run, card, MerchantUpgradeChance, rewards);
        return new CardCreationResult(card);
    }
}
