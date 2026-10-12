using System.Globalization;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// What the shops keep for the whole run: how many card removals were bought. A shop's removal
/// costs 75 gold plus 25 for each one bought before (the game's CardShopRemovalsUsed); a
/// removal an event or a relic gives does not count.
/// </summary>
public sealed class ShopState : RunPart
{
    /// <summary>Card removals bought in shops so far.</summary>
    public int CardRemovalsUsed { get; internal set; }

    public override RunPart Clone(CloneMap map) => new ShopState { CardRemovalsUsed = CardRemovalsUsed };

    public override JsonNode Dump() => new JsonObject { ["card_removals_used"] = CardRemovalsUsed };
}

/// <summary>
/// The price tables of a shop and the two pieces of arithmetic every price goes through. A
/// price is a table value times a rolled factor, worked out in 32-bit floats and rounded to
/// the nearest whole number, a half going to the even one (the game rounds with .NET's default
/// rounding everywhere here).
/// </summary>
public static class ShopPrices
{
    public const int CommonCard = 50;
    public const int UncommonCard = 75;
    public const int RareCard = 150;

    /// <summary>A colorless card's table value is the card value times this, rounded: 86 for an Uncommon one and 172
    /// for a Rare one (150 times 1.15 is exactly 172.5 in a 32-bit float, and the half goes to the even number).</summary>
    public const float ColorlessMarkup = 1.15f;

    public const int CommonRelic = 175;
    public const int UncommonRelic = 225;
    public const int RareRelic = 275;
    public const int ShopRelic = 200;

    public const int CommonPotion = 50;
    public const int UncommonPotion = 75;
    public const int RarePotion = 100;

    /// <summary>A card's and a potion's factor is rolled between these two.</summary>
    public const float NarrowLow = 0.95f, NarrowHigh = 1.05f;

    /// <summary>A relic's factor is rolled between these two.</summary>
    public const float WideLow = 0.85f, WideHigh = 1.15f;

    /// <summary>The first card removal of a run costs this.</summary>
    public const int Removal = 75;

    /// <summary>Each removal bought in a shop makes the next one dearer by this.</summary>
    public const int RemovalIncrease = 25;

    /// <summary>
    /// A card's table value: 150 for a Rare card, 75 for an Uncommon one, 50 for every other
    /// rarity; a card of the colorless pool costs 15% more.
    /// </summary>
    public static int CardBase(CardInstance card)
    {
        var value = card.Def.Rarity switch
        {
            CardRarity.Rare => RareCard,
            CardRarity.Uncommon => UncommonCard,
            _ => CommonCard,
        };
        if (ReferenceEquals(CardPools.Of(card.Def.ClassName), CardPools.Colorless)) value = Round((float)value * ColorlessMarkup);
        return value;
    }

    /// <summary>A relic's table value by its rarity. Only the four bag rarities are ever sold; the others have the
    /// game's stand-in values.</summary>
    public static int RelicBase(RelicRarity rarity) => rarity switch
    {
        RelicRarity.Common => CommonRelic,
        RelicRarity.Uncommon => UncommonRelic,
        RelicRarity.Rare => RareRelic,
        RelicRarity.Shop => ShopRelic,
        RelicRarity.None => 1,
        _ => 999999999,
    };

    public static int PotionBase(PotionRarity rarity) => rarity switch
    {
        PotionRarity.Rare => RarePotion,
        PotionRarity.Uncommon => UncommonPotion,
        _ => CommonPotion,
    };

    /// <summary>What the removal costs in a shop made now.</summary>
    public static int RemovalPrice(RunState state) => Removal + RemovalIncrease * (state.FindPart<ShopState>()?.CardRemovalsUsed ?? 0);

    /// <summary>
    /// One number of the stream as a factor between <paramref name="low"/> and
    /// <paramref name="high"/>, as the game's ranged float draw makes it: the fraction times the
    /// width of the range (a float subtraction) plus the lower bound, worked out in doubles and
    /// narrowed.
    /// </summary>
    internal static float Factor(GameRng rng, float low, float high) => (float)(rng.NextDouble() * (double)(high - low) + (double)low);

    /// <summary>To the nearest whole number, a half to the even one.</summary>
    internal static int Round(float value) => (int)MathF.Round(value);
}

/// <summary>
/// One thing a shop sells (the game's MerchantEntry): a card, a relic, a potion or the card
/// removal. An entry keeps its place on the shelf for the whole visit: bought, it is empty
/// (<see cref="IsStocked"/> false) unless a relic has it filled again.
/// <para>
/// <see cref="BaseCost"/> is the price as rolled. What the player pays is <see cref="Cost"/>:
/// the rolled price after the price hooks, cut to a whole number.
/// </para>
/// </summary>
public abstract class ShopEntry
{
    /// <summary>The price as rolled when the entry was stocked, before any relic changes it.</summary>
    public int BaseCost { get; protected set; }

    /// <summary>Something is on this place of the shelf.</summary>
    public abstract bool IsStocked { get; }

    /// <summary>What the mod calls the thing in a refusal and in <c>last_purchase_failed</c>: its title, or "a card removal".</summary>
    public abstract string What { get; }

    /// <summary>
    /// The price now (MerchantEntry.Cost): the rolled price run through every listener's
    /// <see cref="Listener.ModifyMerchantPrice"/> and cut to a whole number. The game asks the
    /// hook while the player stands in a merchant's room, which a shop's entry always does (the
    /// Fake Merchant of act 2, whose entries are priced outside one, is not built).
    /// </summary>
    public int Cost(Run run) => (int)ShopHooks.ModifyMerchantPrice(run.Context, this, BaseCost);

    /// <summary>The player has the gold for it.</summary>
    public bool EnoughGold(Run run) => Cost(run) <= run.Gold;

    /// <summary>What buying does. Success false leaves everything as it was; the gold is what was paid.</summary>
    internal abstract (bool Success, int GoldSpent) OnTryPurchase(Run run, ShopInventory inventory);

    /// <summary>Empties the place after a purchase.</summary>
    internal abstract void ClearAfterPurchase();

    /// <summary>Fills the place again after a purchase (a relic said so: The Courier).</summary>
    internal abstract void RestockAfterPurchase(Run run, ShopInventory inventory);

    /// <summary>Something was bought in this shop: bring the entry up to date.</summary>
    internal virtual void UpdateEntry(Run run) { }

    internal abstract ShopEntry Clone(CloneMap map);

    private protected static RunStreams Streams(Run run) => run.State.Part<RunWorld>().Streams;

    private protected static GameRng PriceRoll(Run run, int priceSlot) => Streams(run).Draw(RunStream.Shops, Purpose.ShopPrice(priceSlot));

    /// <summary>A copy of something on offer that nobody holds: a copy of the run copies it by itself, unless the
    /// run's own copy already has one (the thing was handed over in the middle of an action that is waiting for an answer).</summary>
    private protected static T CopyOf<T>(T thing, CloneMap map, Func<T, CloneMap, T> clone) where T : class
    {
        if (map.TryGet(thing, out var known) && known is T copy) return copy;
        var own = new CloneMap();
        copy = clone(thing, own);
        own.Finish();
        return copy;
    }
}

/// <summary>
/// A card on the shelf (the game's MerchantCardEntry): one of the five places for the
/// character's cards, each of a fixed type, or one of the two for colorless cards, each of a
/// fixed rarity. The card is made by <see cref="CardFactory.CreateForMerchant(Run, IEnumerable{GameCard}, CardType, Purpose)"/>
/// from the pool without the cards the shelf already shows, passes
/// <see cref="IRewardHooks.ModifyMerchantCardCreationResults"/>, and gets its price: one factor;
/// the card on sale draws a second one at once and costs half of that price, rounded down.
/// </summary>
public sealed class ShopCardEntry : ShopEntry
{
    private readonly IReadOnlyList<GameCard> _pool;
    private readonly CardType? _type;
    private readonly CardRarity? _rarity;

    /// <summary>The place among the shop's seven cards: 0 to 4 the character's, 5 and 6 the colorless ones. It names the entry's rolls.</summary>
    public int Slot { get; }

    /// <summary>The type the place holds; null for a colorless place.</summary>
    public CardType? Type => _type;

    /// <summary>The rarity a colorless place holds; null for a character's place, whose rarity is rolled.</summary>
    public CardRarity? Rarity => _rarity;

    /// <summary>The card as the factory made it, with what the hooks did to it; null once it is sold.</summary>
    public CardCreationResult? Result { get; private set; }

    /// <summary>The card on the shelf; null once it is sold.</summary>
    public CardInstance? Card => Result?.Card;

    /// <summary>The card is the one at half price.</summary>
    public bool IsOnSale { get; private set; }

    public override bool IsStocked => Result is not null;
    public override string What => Card?.Title ?? "card";

    /// <param name="pool">The cards the place is filled from, in the game's order: the unlocked cards of the character's pool, or of the colorless pool.</param>
    internal ShopCardEntry(int slot, IReadOnlyList<GameCard> pool, CardType type)
    {
        Slot = slot;
        _pool = pool;
        _type = type;
    }

    internal ShopCardEntry(int slot, IReadOnlyList<GameCard> pool, CardRarity rarity)
    {
        Slot = slot;
        _pool = pool;
        _rarity = rarity;
    }

    private ShopCardEntry(ShopCardEntry other)
    {
        Slot = other.Slot;
        _pool = other._pool;
        _type = other._type;
        _rarity = other._rarity;
        Result = other.Result?.Clone();
        IsOnSale = other.IsOnSale;
        BaseCost = other.BaseCost;
    }

    /// <summary>Puts a given card on the place at a given price, with no roll: for a shelf built by hand (checks).</summary>
    internal ShopCardEntry Stock(CardInstance card, int baseCost, bool onSale = false)
    {
        Result = new CardCreationResult(card);
        BaseCost = baseCost;
        IsOnSale = onSale;
        return this;
    }

    /// <summary>Makes the place's card (MerchantCardEntry.Populate): never one the shelf shows already, this place's own last card included.</summary>
    internal void Populate(Run run, ShopInventory inventory)
    {
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in inventory.CardEntries)
            if (entry.Card is { } card) shown.Add(card.Def.ClassName);
        var options = _pool.Where(card => !shown.Contains(card.ClassName));

        var purpose = Purpose.ShopCard(Slot);
        Result = _type is { } type
            ? CardFactory.CreateForMerchant(run, options, type, purpose)
            : CardFactory.CreateForMerchant(run, options, _rarity!.Value, purpose);
        RewardHooks.ModifyMerchantCardCreationResults(run, [Result]);
        CalcCost(run);
    }

    /// <summary>Marks the card as the one on sale: its price is rolled again and halved.</summary>
    internal void SetOnSale(Run run)
    {
        IsOnSale = true;
        CalcCost(run);
    }

    /// <summary>One factor from the shops stream: the card's table value times it, rounded; half of that, rounded down, on sale.</summary>
    private void CalcCost(Run run)
    {
        if (Result is null) throw new InvalidOperationException("There is no card to price.");
        var factor = ShopPrices.Factor(PriceRoll(run, Slot), ShopPrices.NarrowLow, ShopPrices.NarrowHigh);
        BaseCost = ShopPrices.Round((float)ShopPrices.CardBase(Result.Card) * factor);
        if (IsOnSale) BaseCost /= 2;
    }

    internal override (bool Success, int GoldSpent) OnTryPurchase(Run run, ShopInventory inventory)
    {
        // The card goes into the deck first; gold is taken only when it got in.
        if (!DeckEdits.Add(run, Result!.Card).Success) return (false, 0);
        run.LoseGold(Cost(run));
        return (true, Cost(run));
    }

    internal override void ClearAfterPurchase() => Result = null;

    internal override void RestockAfterPurchase(Run run, ShopInventory inventory)
    {
        IsOnSale = false;
        Populate(run, inventory);
    }

    /// <summary>The card hooks are asked again about a card that is still on the shelf (an Egg bought here upgrades it).</summary>
    internal override void UpdateEntry(Run run)
    {
        if (Result is not null) RewardHooks.ModifyMerchantCardCreationResults(run, [Result]);
    }

    internal override ShopEntry Clone(CloneMap map) => new ShopCardEntry(this);
}

/// <summary>
/// A relic on the shelf (the game's MerchantRelicEntry): the next relic of a rarity from the
/// BACK of the player's bag that a shop may sell (<see cref="RunRelics.PullFromBack"/>). It
/// has left both bags whether or not it is bought. Its price is the rarity's table value times
/// a factor between 0.85 and 1.15.
/// </summary>
public sealed class ShopRelicEntry : ShopEntry
{
    /// <summary>The place among the shop's relics, from 0. It names the entry's rolls.</summary>
    public int Slot { get; }

    /// <summary>The relic on the shelf: an object nobody holds yet; null once it is sold.</summary>
    public Relic? Relic { get; private set; }

    public override bool IsStocked => Relic is not null;
    public override string What => Relic?.Name ?? "relic";

    internal ShopRelicEntry(int slot) => Slot = slot;

    /// <summary>Puts a given relic on the place at a given price, with no pull and no roll: for a shelf built by hand (checks).</summary>
    internal ShopRelicEntry Stock(Relic relic, int baseCost)
    {
        Relic = relic;
        BaseCost = baseCost;
        return this;
    }

    /// <summary>
    /// Fills the place (MerchantRelicEntry.FillSlot): a pull from the back of the player's bag,
    /// then the price.
    /// </summary>
    /// <param name="rerollWith">The stream the rarity was rolled on; null for a rarity that was not rolled (the Shop-rarity place).</param>
    /// <param name="blacklist">Relics the pull must pass over besides the ones a shop never sells.</param>
    /// <exception cref="RunUnrepresentableException">The bag holds no built relic the place could show, or strict mode met an unbuilt one.</exception>
    internal void FillSlot(Run run, RelicRarity rarity, GameRng? rerollWith, IReadOnlySet<string>? blacklist = null)
    {
        var state = run.State;
        var strict = state.FindPart<LeftOutLog>()?.Strict ?? false;
        var pull = RunRelics.PullFromBack(state, rarity, rerollWith, strict, blacklist is null ? null : relic => !blacklist.Contains(relic));
        if (!pull.Found)
            throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture, $"a shop relic on floor {state.TotalFloor}: {pull.Reason}"));
        // What the game would have shelved and the arena has not built goes into the log; a rarity
        // that was rolled again left the game's own relic behind without passing over it.
        foreach (var passed in pull.PassedOver) state.Part<LeftOutLog>().PassedOver(state, "relic", passed, "a shop relic");
        if (pull.Rerolls > 0 && pull.GameRelic is { } games && !pull.PassedOver.Contains(games))
            state.Part<LeftOutLog>().PassedOver(state, "relic", games, "a shop relic");

        Relic = Content.Registry.NewRelic(pull.Relic!);
        var factor = ShopPrices.Factor(PriceRoll(run, ShopInventory.FirstRelicPriceSlot + Slot), ShopPrices.WideLow, ShopPrices.WideHigh);
        BaseCost = (int)Math.Round((double)((float)ShopPrices.RelicBase(pull.Rarity) * factor));
    }

    internal override (bool Success, int GoldSpent) OnTryPurchase(Run run, ShopInventory inventory)
    {
        // Gold first, then the relic with its pickup effect; what is reported as spent is the price as it stands afterwards.
        run.LoseGold(Cost(run));
        run.ObtainRelic(Relic!);
        return (true, Cost(run));
    }

    internal override void ClearAfterPurchase() => Relic = null;

    /// <summary>A new rarity is rolled on the rewards stream and the place is filled from the bag again, never with a
    /// relic that is on the shelf now (the one just bought among them).</summary>
    internal override void RestockAfterPurchase(Run run, ShopInventory inventory)
    {
        var onShelf = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in inventory.Relics)
            if (entry.Relic is { } relic) onShelf.Add(relic.ClassName);
        var rng = Streams(run).Draw(RunStream.Rewards, Purpose.ShopRelicRarity(Slot));
        FillSlot(run, RunRelics.RollRarity(rng), rng, onShelf);
    }

    internal override ShopEntry Clone(CloneMap map) => new ShopRelicEntry(Slot)
    {
        Relic = Relic is null ? null : CopyOf(Relic, map, static (relic, own) => relic.CloneFor(own)),
        BaseCost = BaseCost,
    };
}

/// <summary>
/// A potion on the shelf (the game's MerchantPotionEntry). The three potions of a shop are
/// rolled together, no two the same (<see cref="PotionFactory.CreateRandomPotionsOutOfCombat"/>
/// on the shops stream), and priced afterwards: the rarity's table value times a factor
/// between 0.95 and 1.05.
/// </summary>
public sealed class ShopPotionEntry : ShopEntry
{
    /// <summary>The place among the shop's potions, from 0. It names the entry's rolls.</summary>
    public int Slot { get; }

    /// <summary>The potion on the shelf; null once it is sold.</summary>
    public PotionDef? Potion { get; private set; }

    public override bool IsStocked => Potion is not null;
    public override string What => Potion?.Name ?? "potion";

    internal ShopPotionEntry(int slot) => Slot = slot;

    /// <summary>Puts a given potion on the place at a given price, with no roll: for a shelf built by hand (checks).</summary>
    internal ShopPotionEntry Stock(PotionDef potion, int baseCost)
    {
        Potion = potion;
        BaseCost = baseCost;
        return this;
    }

    /// <summary>Puts a rolled potion on the place and rolls its price.</summary>
    internal void SetPotion(Run run, PotionDef potion)
    {
        Potion = potion;
        var factor = ShopPrices.Factor(PriceRoll(run, ShopInventory.FirstPotionPriceSlot + Slot), ShopPrices.NarrowLow, ShopPrices.NarrowHigh);
        BaseCost = ShopPrices.Round((float)ShopPrices.PotionBase(potion.Rarity) * factor);
    }

    internal override (bool Success, int GoldSpent) OnTryPurchase(Run run, ShopInventory inventory)
    {
        // The potion goes into the belt first; with no free slot, or kept out by a hook, nothing is paid.
        if (RunPotions.TryToProcure(run, Potion!) != PotionProcureResult.Success) return (false, 0);
        run.LoseGold(Cost(run));
        return (true, Cost(run));
    }

    internal override void ClearAfterPurchase() => Potion = null;

    /// <summary>Any potion of the list, one already on the shelf included: the game works its list of shelved
    /// potions out here and then does not use it.</summary>
    internal override void RestockAfterPurchase(Run run, ShopInventory inventory) =>
        SetPotion(run, PotionFactory.CreateRandomPotionOutOfCombat(run, Streams(run).Draw(RunStream.Shops, Purpose.ShopPotion(Slot))));

    internal override ShopEntry Clone(CloneMap map) => new ShopPotionEntry(Slot) { Potion = Potion, BaseCost = BaseCost };
}

/// <summary>
/// The card removal of a shop (the game's MerchantCardRemovalEntry): once per shop, at 75 gold
/// plus 25 for every removal bought in a shop before (<see cref="ShopState"/>). The price is
/// fixed when the shop is made. Buying it asks which card (the deck's removal screen, which
/// can be closed without choosing: nothing is paid then and the removal stays).
/// </summary>
public sealed class ShopRemovalEntry : ShopEntry
{
    /// <summary>The removal was bought in this shop.</summary>
    public bool Used { get; private set; }

    public override bool IsStocked => !Used;
    public override string What => "a card removal";

    internal ShopRemovalEntry(int baseCost) => BaseCost = baseCost;

    internal override (bool Success, int GoldSpent) OnTryPurchase(Run run, ShopInventory inventory)
    {
        if (Used) return (false, 0);
        var price = Cost(run);
        // OneOffSynchronizer.DoMerchantCardRemoval: one card, the screen can be closed, the pick is confirmed.
        var chosen = CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1) with { Cancelable = true, RequireManualConfirmation = true });
        if (chosen.Count == 0) return (false, price);
        run.LoseGold(price);
        DeckEdits.Remove(run, chosen[0]);
        run.State.Part<ShopState>().CardRemovalsUsed++;
        Used = true;
        return (true, price);
    }

    internal override void ClearAfterPurchase() { }

    internal override void RestockAfterPurchase(Run run, ShopInventory inventory) { }

    internal override ShopEntry Clone(CloneMap map) => new ShopRemovalEntry(BaseCost) { Used = Used };
}

/// <summary>
/// What one shop sells (the game's MerchantInventory), made when the shop's room is entered:
/// five cards of the character (Attack, Attack, Skill, Skill, Power; one of them at half
/// price), two colorless cards (Uncommon, Rare), three relics (two of a rolled rarity, one of
/// Shop rarity), three potions and the card removal.
/// <para>
/// The draws, in the game's order. On the player's <c>shops</c> stream: which of the five
/// cards is on sale; each card's pick and its price (the card on sale draws a second price);
/// each relic's price; the three potions (a rarity and a pick each), then their prices: 28
/// numbers. On the player's <c>rewards</c> stream: the rarity of each of the five character
/// cards and one upgrade roll that cannot succeed for each of the seven cards, then the two
/// relic rarities: 14 numbers. Each roll names its place on the shelf
/// (<see cref="Purpose.ShopCard"/>, <see cref="Purpose.ShopPrice"/>, <see cref="Purpose.ShopRelicRarity"/>,
/// <see cref="Purpose.ShopPotion"/>, <see cref="Purpose.ShopSale"/>) for keyed streams.
/// </para>
/// <para>
/// A roll that lands on something the arena has not built is drawn again by the house rule of
/// its factory (<see cref="CardPools.Pick"/>, <see cref="PotionFactory"/>, <see cref="RunRelics"/>);
/// a place nothing built can fill throws <see cref="RunUnrepresentableException"/>: a real shop
/// has no empty place, so such a shop is not shown at all.
/// </para>
/// </summary>
public sealed class ShopInventory
{
    /// <summary>The types of the five places for the character's cards, in shelf order.</summary>
    public static readonly IReadOnlyList<CardType> CharacterCardTypes = [CardType.Attack, CardType.Attack, CardType.Skill, CardType.Skill, CardType.Power];

    /// <summary>The rarities of the two places for colorless cards, in shelf order.</summary>
    public static readonly IReadOnlyList<CardRarity> ColorlessCardRarities = [CardRarity.Uncommon, CardRarity.Rare];

    /// <summary>How many relics a shop shows: two of a rolled rarity, then one of Shop rarity.</summary>
    public const int RelicPlaces = 3;

    /// <summary>How many potions a shop shows.</summary>
    public const int PotionPlaces = 3;

    /// <summary>The price rolls are numbered over the whole shelf: the seven cards 0 to 6, then the relics, then the potions.</summary>
    internal const int FirstRelicPriceSlot = 7;
    internal const int FirstPotionPriceSlot = FirstRelicPriceSlot + RelicPlaces;

    private readonly List<ShopCardEntry> _characterCards = new();
    private readonly List<ShopCardEntry> _colorlessCards = new();
    private readonly List<ShopRelicEntry> _relics = new();
    private readonly List<ShopPotionEntry> _potions = new();

    /// <summary>The character's cards, in shelf order; an entry's index is its <c>n</c> in <c>buy:card&lt;n&gt;</c>.</summary>
    public IReadOnlyList<ShopCardEntry> CharacterCards => _characterCards;

    /// <summary>The colorless cards; an entry's index is its <c>n</c> in <c>buy:colorless&lt;n&gt;</c>.</summary>
    public IReadOnlyList<ShopCardEntry> ColorlessCards => _colorlessCards;

    public IReadOnlyList<ShopRelicEntry> Relics => _relics;
    public IReadOnlyList<ShopPotionEntry> Potions => _potions;

    /// <summary>The card removal; null for a shelf that has none.</summary>
    public ShopRemovalEntry? Removal { get; private set; }

    /// <summary>The seven card entries: the character's, then the colorless ones.</summary>
    public IEnumerable<ShopCardEntry> CardEntries => _characterCards.Concat(_colorlessCards);

    /// <summary>Every entry in the game's order: cards, relics, potions, the removal.</summary>
    public IEnumerable<ShopEntry> AllEntries
    {
        get
        {
            foreach (var entry in CardEntries) yield return entry;
            foreach (var entry in _relics) yield return entry;
            foreach (var entry in _potions) yield return entry;
            if (Removal is not null) yield return Removal;
        }
    }

    private ShopInventory() { }

    /// <summary>A shelf built by hand, entry by entry, with no roll: for checks.</summary>
    internal ShopInventory(IEnumerable<ShopCardEntry> characterCards, IEnumerable<ShopCardEntry> colorlessCards, IEnumerable<ShopRelicEntry> relics,
        IEnumerable<ShopPotionEntry> potions, ShopRemovalEntry? removal)
    {
        _characterCards.AddRange(characterCards);
        _colorlessCards.AddRange(colorlessCards);
        _relics.AddRange(relics);
        _potions.AddRange(potions);
        Removal = removal;
    }

    /// <summary>
    /// The stock of a merchant's room (MerchantInventory.CreateForNormalMerchant), rolled now on
    /// the run's streams.
    /// </summary>
    /// <exception cref="RunUnrepresentableException">A place of the shelf can only be filled with something the arena
    /// has not built, or strict mode met such a thing: the run cannot show this shop.</exception>
    public static ShopInventory CreateForNormalMerchant(Run run)
    {
        if (run.Fight is not null) throw new InvalidOperationException("A shop is made outside a fight.");
        var inventory = new ShopInventory();
        inventory.StockCharacterCards(run);
        inventory.StockColorlessCards(run);
        inventory.StockRelics(run);
        inventory.StockPotions(run);
        inventory.Removal = new ShopRemovalEntry(ShopPrices.RemovalPrice(run.State));
        return inventory;
    }

    private static RunStreams Streams(Run run) => run.State.Part<RunWorld>().Streams;

    /// <summary>The sale's number first; then card by card: made, shelved, and the one on sale priced again.</summary>
    internal void StockCharacterCards(Run run)
    {
        var onSale = Streams(run).Draw(RunStream.Shops, Purpose.ShopSale).NextInt(CharacterCardTypes.Count);
        var pool = CardPools.Character.Unlocked(CardPools.UnlocksOf(run));
        for (var i = 0; i < CharacterCardTypes.Count; i++)
        {
            var entry = new ShopCardEntry(i, pool, CharacterCardTypes[i]);
            entry.Populate(run, this);
            _characterCards.Add(entry);
            if (i == onSale) entry.SetOnSale(run);
        }
    }

    internal void StockColorlessCards(Run run)
    {
        var pool = CardPools.Colorless.Unlocked(CardPools.UnlocksOf(run));
        for (var i = 0; i < ColorlessCardRarities.Count; i++)
        {
            var entry = new ShopCardEntry(CharacterCardTypes.Count + i, pool, ColorlessCardRarities[i]);
            entry.Populate(run, this);
            _colorlessCards.Add(entry);
        }
    }

    /// <summary>Both rarities are rolled before the first relic is pulled; then each place is pulled and priced in turn.</summary>
    internal void StockRelics(Run run)
    {
        var rolls = new (RelicRarity Rarity, GameRng? Rng)[RelicPlaces];
        for (var i = 0; i < RelicPlaces - 1; i++)
        {
            var rng = Streams(run).Draw(RunStream.Rewards, Purpose.ShopRelicRarity(i));
            rolls[i] = (RunRelics.RollRarity(rng), rng);
        }
        rolls[RelicPlaces - 1] = (RelicRarity.Shop, null);

        for (var i = 0; i < RelicPlaces; i++)
        {
            var entry = new ShopRelicEntry(i);
            entry.FillSlot(run, rolls[i].Rarity, rolls[i].Rng);
            _relics.Add(entry);
        }
    }

    /// <summary>The three potions are rolled together; their prices follow.</summary>
    internal void StockPotions(Run run)
    {
        var potions = PotionFactory.CreateRandomPotionsOutOfCombat(run, PotionPlaces, Streams(run).Draw(RunStream.Shops, Purpose.ShopPotion(0)));
        for (var i = 0; i < potions.Count; i++)
        {
            var entry = new ShopPotionEntry(i);
            entry.SetPotion(run, potions[i]);
            _potions.Add(entry);
        }
    }

    /// <summary>
    /// Buys an entry (MerchantEntry.OnTryPurchaseWrapper, and the removal's own form of it).
    /// False, and nothing changed, when the place is empty, the gold is short, or the purchase
    /// itself did not go through: a card the deck did not take, a potion the belt did not take,
    /// a removal that was backed out of. After a purchase the place is emptied, or filled again
    /// when a listener's <see cref="Listener.ShouldRefillMerchantEntry"/> says so (never the
    /// removal, which is not asked); then <see cref="Listener.AfterItemPurchased"/> is heard, and
    /// every entry is brought up to date.
    /// </summary>
    public bool TryPurchase(Run run, ShopEntry entry)
    {
        var removal = entry is ShopRemovalEntry;
        if (!removal && !entry.IsStocked) return false;
        if (!entry.EnoughGold(run)) return false;

        var (success, goldSpent) = entry.OnTryPurchase(run, this);
        if (!success) return false;
        if (!removal)
        {
            if (ShopHooks.ShouldRefillMerchantEntry(run.Context, entry)) entry.RestockAfterPurchase(run, this);
            else entry.ClearAfterPurchase();
        }
        ShopHooks.AfterItemPurchased(run.Context, entry, goldSpent);
        foreach (var each in AllEntries.ToList()) each.UpdateEntry(run);
        return true;
    }

    internal ShopInventory Clone(CloneMap map)
    {
        var copy = new ShopInventory();
        copy._characterCards.AddRange(_characterCards.Select(entry => (ShopCardEntry)entry.Clone(map)));
        copy._colorlessCards.AddRange(_colorlessCards.Select(entry => (ShopCardEntry)entry.Clone(map)));
        copy._relics.AddRange(_relics.Select(entry => (ShopRelicEntry)entry.Clone(map)));
        copy._potions.AddRange(_potions.Select(entry => (ShopPotionEntry)entry.Clone(map)));
        copy.Removal = (ShopRemovalEntry?)Removal?.Clone(map);
        return copy;
    }
}

/// <summary>
/// The call sites of the shop's three hooks, each asking the run's listeners in the game's
/// order through <see cref="RunHooks"/>. The hooks are declared on <see cref="Listener"/>; the
/// shop's card hooks, whose arguments are card offers, are the rewards'
/// (<see cref="RewardHooks.ModifyMerchantCardPool"/>, <see cref="RewardHooks.ModifyMerchantCardRarity"/>,
/// <see cref="RewardHooks.ModifyMerchantCardCreationResults"/>).
/// </summary>
public static class ShopHooks
{
    /// <summary>Hook.ModifyMerchantPrice: every listener in turn gets the price the one before returned (Membership
    /// Card halves it, The Courier takes a fifth off).</summary>
    public static decimal ModifyMerchantPrice(RunContext c, ShopEntry entry, decimal price) =>
        RunHooks.Fold(c, price, (listener, p) => listener.ModifyMerchantPrice(c, entry, p));

    /// <summary>Hook.ShouldRefillMerchantEntry: whether a bought entry is filled again; the first yes decides.</summary>
    public static bool ShouldRefillMerchantEntry(RunContext c, ShopEntry entry) =>
        RunHooks.Any(c, listener => listener.ShouldRefillMerchantEntry(c, entry));

    /// <summary>Hook.AfterItemPurchased: something was bought, for this much gold.</summary>
    public static void AfterItemPurchased(RunContext c, ShopEntry entry, int goldSpent) =>
        RunHooks.Each(c, listener => listener.AfterItemPurchased(c, entry, goldSpent));
}

/// <summary>
/// The merchant's room. A director opens it when the map leads into a shop:
/// <code>
/// ShopRoom.Enter(run);                       // in IMapDirector.EnterRoom, for RoomKind.Shop
/// MapState.MarkRoomVisited(run, RoomKind.Shop);
/// </code>
/// The screen's <c>proceed</c> takes the screen away and calls <see cref="Run.RoomFinished"/>.
/// </summary>
public static class ShopRoom
{
    /// <summary>
    /// Enters a merchant's room (MerchantRoom.EnterInternal): the stock is rolled, the shop's
    /// screen is put up with its inventory closed, and the listeners hear that a Shop room was
    /// entered.
    /// </summary>
    /// <exception cref="RunUnrepresentableException">The stock cannot be made of what the arena has built (<see cref="ShopInventory.CreateForNormalMerchant"/>).</exception>
    public static ShopScreen Enter(Run run) => Enter(run, ShopInventory.CreateForNormalMerchant(run));

    /// <summary>The same with a shelf that is already made (checks build one by hand): the screen, then the announcement.</summary>
    internal static ShopScreen Enter(Run run, ShopInventory inventory)
    {
        var screen = new ShopScreen(inventory);
        run.Push(screen);
        run.AfterRoomEntered(RoomKind.Shop);
        return screen;
    }
}
