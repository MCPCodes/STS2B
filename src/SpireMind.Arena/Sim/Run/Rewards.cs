using System.Globalization;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// The room a set of rewards is made for, as far as the rewards ask (the game's AbstractRoom,
/// CombatRoom and its encounter): the kind of room, and for a fight's room what share of its
/// gold it pays, the gold range when the encounter names its own, whether the encounter gives
/// rewards at all, and rewards the fight itself earned on top.
/// </summary>
public sealed record RewardRoom
{
    /// <summary>The kind of room. A fight's room is of its encounter's kind, also when an event started it.</summary>
    public required RoomKind Kind { get; init; }

    /// <summary>The game's "room is CombatRoom": true for a fight's room, whatever started the fight.</summary>
    public bool IsCombatRoom => Kind.IsCombat();

    /// <summary>
    /// What share of its gold a hallway fight pays (CombatRoom.GoldProportion): 1 unless monsters
    /// left without dying (<see cref="GoldShare"/>). Elites and bosses pay in full whatever it is.
    /// </summary>
    public float GoldProportion { get; init; } = 1f;

    /// <summary>The encounter's own gold range (EncounterModel.MinGoldReward and MaxGoldReward), when it does not
    /// pay its room kind's (<see cref="DefaultGold"/>).</summary>
    public (int Min, int Max)? Gold { get; init; }

    /// <summary>False for an encounter that gives nothing when won (EncounterModel.ShouldGiveRewards): no rewards screen follows.</summary>
    public bool ShouldGiveRewards { get; init; } = true;

    /// <summary>Rewards the fight earned on top of the room's own (CombatRoom.ExtraRewards): they go behind them before the set is filled.</summary>
    public IReadOnlyList<Reward> ExtraRewards { get; init; } = [];

    /// <summary>The treasure room: it has no rewards of its own, but the hooks may add some.</summary>
    public static RewardRoom Treasure { get; } = new() { Kind = RoomKind.Treasure };

    /// <summary>A fight's room of this kind that pays in full.</summary>
    public static RewardRoom Combat(RoomType fight) => new() { Kind = RoomKinds.Of(fight) };

    /// <summary>
    /// The room of a fight the run has won: its kind, and the share of gold it pays for the
    /// monsters that left without dying (<see cref="GoldShare"/>). The game divides by the kinds
    /// of monster the fight had, not by their number; the outcome names the monsters by title,
    /// and no two monster classes of the arena share a title (the checks hold that), so the
    /// distinct titles are counted.
    /// </summary>
    public static RewardRoom Of(FightOutcome fight)
    {
        var room = Combat(fight.RoomType);
        if (fight.Escaped == 0) return room;
        var kinds = fight.Enemies.Split(" + ", StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Count();
        return room with { GoldProportion = GoldShare(fight.Escaped, kinds) };
    }

    /// <summary>
    /// EncounterModel.CalculateGoldProportion: 1 minus the creatures that escaped over the KINDS
    /// of monster that stood in the fight at any time, summoned ones included (the game keeps
    /// each monster class once). Three Nibbits of which one flees pay nothing: 1 - 1/1.
    /// </summary>
    public static float GoldShare(int escaped, int monsterKinds) => 1f - (float)escaped / (float)monsterKinds;

    /// <summary>What a fight pays by its room kind (EncounterModel): 10 to 20, 35 to 45, 100.</summary>
    public static (int Min, int Max) DefaultGold(RoomKind kind) => kind switch
    {
        RoomKind.Monster => (10, 20),
        RoomKind.Elite => (35, 45),
        RoomKind.Boss => (100, 100),
        _ => (0, 0),
    };
}

/// <summary>
/// One reward of a rewards set (the game's Reward): something the player may take from the
/// rewards screen. A reward is made empty or with its content named; <see cref="Populate"/>
/// rolls what is still open; <see cref="Select"/> takes it.
/// <para>
/// Rewards live in a <see cref="RewardsSet"/>, which a <see cref="RewardsScreen"/> holds; they
/// are copied with it. Left out: the game's rng override on a reward (only the Crystal Sphere
/// of act 2 sets one) and linked rewards (take one, lose the others), which nothing in the
/// game makes yet.
/// </para>
/// </summary>
public abstract class Reward
{
    /// <summary>Where the row stands among the rows of its set (the game's RewardsSetIndex): gold 1, potion 2, relic 3, special card 4, card 5, removal 7.</summary>
    public abstract int RewardsSetIndex { get; }

    /// <summary>The reward's content is settled: nothing is left to roll.</summary>
    public abstract bool IsPopulated { get; }

    /// <summary>The reward was taken.</summary>
    public bool SuccessfullySelected { get; private set; }

    /// <summary>Rolls what the reward is, where that is still open.</summary>
    /// <param name="index">Which reward of its kind this is in its set, from 0: it names the roll for keyed streams.</param>
    /// <exception cref="RunUnrepresentableException">The roll can only land on something the arena has not built, or strict mode met such a thing.</exception>
    public abstract void Populate(Run run, int index);

    /// <summary>What taking the reward does. False when it was not taken after all: a potion with a full belt, a card reward that was skipped, a removal that was backed out of.</summary>
    protected abstract bool OnSelect(Run run);

    /// <summary>The rewards screen was left with this reward still on it.</summary>
    public virtual void OnSkipped(Run run) { }

    /// <summary>
    /// Takes the reward (Reward.SelectUnsynchronized): its effect, then "a reward was taken" to
    /// the listeners. False when it was not taken; it then stays on its screen.
    /// </summary>
    public bool Select(Run run)
    {
        var success = OnSelect(run);
        if (success)
        {
            RewardHooks.AfterRewardTaken(run, this);
            SuccessfullySelected = true;
        }
        return success;
    }

    // ---------- as the mod shows it ----------

    /// <summary>The row's <c>kind</c> in the mod's snapshot.</summary>
    public abstract string Kind { get; }

    /// <summary>Writes the row's own keys behind <c>n</c> and <c>kind</c>.</summary>
    internal abstract void Describe(JsonObject row);

    /// <summary>The label of the row's <c>reward:&lt;n&gt;</c> action.</summary>
    public abstract string Label { get; }

    /// <summary>What the mod calls the row in a refusal ("the 41 gold reward").</summary>
    public abstract string What { get; }

    /// <summary>Why the row cannot be taken right now, or null: the mod's <c>note</c>.</summary>
    public virtual string? Note(Run run) => null;

    /// <summary>A deep copy, for a copy of the run.</summary>
    internal abstract Reward Clone();

    /// <summary>Carries the base class's state over to a copy.</summary>
    protected T CopyTo<T>(T copy) where T : Reward
    {
        copy.SuccessfullySelected = SuccessfullySelected;
        return copy;
    }

    protected static RunStreams Streams(Run run) => run.State.Part<RunWorld>().Streams;
}

/// <summary>
/// Gold (the game's GoldReward): a fixed amount, or one rolled between two bounds. Filling the
/// reward draws one number of the rewards stream either way, as the game does.
/// </summary>
public sealed class GoldReward : Reward
{
    private readonly int _min;
    private readonly int _max;

    /// <summary>The gold; -1 until a range was rolled.</summary>
    public int Amount { get; private set; } = -1;

    /// <summary>The least and the most gold the reward can be.</summary>
    public (int Min, int Max) Range => (_min, _max);

    /// <summary>The gold is what a thief took and the fight won back; the game words the row differently.</summary>
    public bool WasGoldStolenBack { get; }

    public GoldReward(int amount, bool wasGoldStolenBack = false)
    {
        _min = amount;
        _max = amount;
        Amount = amount;
        WasGoldStolenBack = wasGoldStolenBack;
    }

    public GoldReward(int min, int max, bool wasGoldStolenBack = false)
    {
        _min = min;
        _max = max;
        WasGoldStolenBack = wasGoldStolenBack;
    }

    public override int RewardsSetIndex => 1;
    public override bool IsPopulated => Amount >= 0;

    public override void Populate(Run run, int index)
    {
        // The game's NextInt refuses a range that is empty; the upper bound is one past the most gold.
        if (_min >= _max + 1) throw new InvalidOperationException($"A gold reward of {_min} to {_max} has no amount to roll: the minimum must not be above the maximum.");
        Amount = Streams(run).Draw(RunStream.Rewards, Purpose.Gold).NextInt(_min, _max + 1);
    }

    protected override bool OnSelect(Run run)
    {
        run.GainGold(Amount);
        return true;
    }

    public override string Kind => "gold";
    internal override void Describe(JsonObject row) => row["amount"] = Amount;
    public override string Label => string.Create(CultureInfo.InvariantCulture, $"Take {Amount} gold");
    public override string What => string.Create(CultureInfo.InvariantCulture, $"the {Amount} gold reward");

    internal override Reward Clone() => CopyTo(new GoldReward(_min, _max, WasGoldStolenBack) { Amount = Amount });
}

/// <summary>
/// A potion (the game's PotionReward): a named one, or a random one of the character's potions
/// (<see cref="PotionFactory"/>: two numbers). It cannot be taken with a full belt.
/// </summary>
public sealed class PotionReward : Reward
{
    public PotionDef? Potion { get; private set; }

    /// <summary>A random potion, rolled when the reward is filled.</summary>
    public PotionReward() { }

    public PotionReward(PotionDef potion) => Potion = potion;

    public override int RewardsSetIndex => 2;
    public override bool IsPopulated => Potion is not null;

    public override void Populate(Run run, int index)
    {
        if (Potion is not null) return;
        Potion = PotionFactory.CreateRandomPotionOutOfCombat(run, Streams(run).Draw(RunStream.Rewards, Purpose.Potion(index)));
    }

    protected override bool OnSelect(Run run)
    {
        var result = RunPotions.TryToProcure(run, Potion!);
        // Taken; or bounced off a full belt; or kept out by a hook, which uses the reward up all the same.
        return result != PotionProcureResult.TooFull;
    }

    public override string Kind => "potion";

    internal override void Describe(JsonObject row)
    {
        if (Potion is null) return;
        foreach (var (key, value) in RunDescribe.Potion(Potion)) row[key] = value?.DeepClone();
    }

    public override string Label => $"Take potion {Potion?.Name ?? "potion"}";
    public override string What => $"the {Potion?.Name ?? "potion"} reward";

    public override string? Note(Run run) => run.State.Belt.HasOpenSlot ? null : RunPotions.SlotsFullNote;

    internal override Reward Clone() => CopyTo(new PotionReward { Potion = Potion });
}

/// <summary>
/// A relic (the game's RelicReward): a named one, or the next one of the player's bag
/// (<see cref="RunRelics"/>), of a named rarity or of a rarity that is rolled when the reward
/// is filled (one number).
/// </summary>
public sealed class RelicReward : Reward
{
    private readonly RelicRarity _rarity;

    public Relic? Relic { get; private set; }

    /// <summary>The rarity the reward was made with; <see cref="RelicRarity.None"/> for a rolled or a named relic.</summary>
    public RelicRarity Rarity => _rarity;

    /// <summary>The next relic of the bag, of a rolled rarity (an elite's relic).</summary>
    public RelicReward() { }

    /// <summary>This relic: a new object nobody holds yet.</summary>
    public RelicReward(Relic relic) => Relic = relic;

    /// <summary>The next relic of the bag of this rarity.</summary>
    public RelicReward(RelicRarity rarity) => _rarity = rarity;

    public override int RewardsSetIndex => 3;
    public override bool IsPopulated => Relic is not null;

    public override void Populate(Run run, int index)
    {
        if (Relic is not null) return;
        // RelicFactory.PullNextRelicFromFront, with the roll (RollRarity, one number) when no rarity was named. The pull,
        // and what happens when the game's relic is one the arena has not built, are the relic framework's.
        var state = run.State;
        var strict = state.FindPart<LeftOutLog>()?.Strict ?? false;
        var pull = _rarity == RelicRarity.None
            ? RunRelics.RollAndPullFromFront(state, Streams(run).Draw(RunStream.Rewards, Purpose.RelicRarity(index)), strict)
            : RunRelics.PullFromFront(state, _rarity, rerollWith: null, strict);
        if (!pull.Found) throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture, $"a relic reward on floor {state.TotalFloor}: {pull.Reason}"));
        // A rarity that was rolled again: the game's relic is of a list the arena holds nothing of, so it is not among the passed-over ones.
        if (pull.Rerolls > 0 && pull.GameRelic is { } games) state.Part<LeftOutLog>().PassedOver(state, "relic", games, "a relic reward, whose rarity was then rolled again");
        foreach (var passed in pull.PassedOver) state.Part<LeftOutLog>().PassedOver(state, "relic", passed, "a relic reward");
        Relic = Content.Registry.NewRelic(pull.Relic!);
    }

    protected override bool OnSelect(Run run)
    {
        run.ObtainRelic(Relic!);
        return true;
    }

    public override string Kind => "relic";

    internal override void Describe(JsonObject row)
    {
        if (Relic is null) return;
        foreach (var (key, value) in RunDescribe.Relic(Relic)) row[key] = value?.DeepClone();
    }

    public override string Label => $"Take relic {Relic?.Name ?? "relic"}";
    public override string What => $"the {Relic?.Name ?? "relic"} relic";

    internal override Reward Clone() => CopyTo(new RelicReward(_rarity) { Relic = Relic is null ? null : CopyOf(Relic) });

    /// <summary>A relic on offer is in nobody's list: a copy of the run copies it by itself.</summary>
    private static Relic CopyOf(Relic relic)
    {
        var map = new CloneMap();
        var copy = relic.CloneFor(map);
        map.Finish();
        return copy;
    }
}

/// <summary>A given card that goes into the deck when taken (the game's SpecialCardReward): a quest card an event hands
/// out, a stolen card won back.</summary>
public sealed class SpecialCardReward : Reward
{
    /// <summary>The card: made with <see cref="RunState.NewCard"/>, in no deck yet.</summary>
    public CardInstance Card { get; }

    public SpecialCardReward(CardInstance card) => Card = card;

    public override int RewardsSetIndex => 4;
    public override bool IsPopulated => true;

    public override void Populate(Run run, int index) { }

    protected override bool OnSelect(Run run)
    {
        // The game takes the reward as given whether or not the card got into the deck.
        DeckEdits.Add(run, Card);
        return true;
    }

    public override string Kind => "special_card";

    internal override void Describe(JsonObject row)
    {
        foreach (var (key, value) in RunDescribe.CardOffer(Card)) row[key] = value?.DeepClone();
    }

    public override string Label => $"Take card {Card.Title}";
    public override string What => $"the {Card.Title} card";

    internal override Reward Clone() => CopyTo(new SpecialCardReward(CardCreationResult.CopyOf(Card)));
}

/// <summary>
/// The removal of one card of the deck (the game's CardRemovalReward): taking it opens the
/// removal choice, which can be backed out of; the reward then stays.
/// </summary>
public sealed class CardRemovalReward : Reward
{
    public override int RewardsSetIndex => 7;
    public override bool IsPopulated => true;

    public override void Populate(Run run, int index) { }

    protected override bool OnSelect(Run run)
    {
        // RewardSynchronizer.DoUnsyncedCardRemoval: one card, the screen can be closed, the pick is confirmed.
        var chosen = CardSelect.FromDeckForRemoval(run, CardSelectPrefs.RemoveForReward());
        if (chosen.Count == 0) return false;
        DeckEdits.Remove(run, chosen[0]);
        return true;
    }

    public override string Kind => "card_removal";
    internal override void Describe(JsonObject row) { }
    public override string Label => "Remove a card";
    public override string What => "the card removal reward";

    internal override Reward Clone() => CopyTo(new CardRemovalReward());
}

/// <summary>
/// A choice of one card out of several (the game's CardReward): rolled from pools by the card
/// factory when the reward is filled, or a list of cards named beforehand. Taking the reward
/// opens the card reward screen; a card taken there goes into the deck and the reward is done;
/// skipping it leaves the reward on its rewards screen, which lists its cards from then on.
/// <para>
/// From the moment a rolled card reward is made until its screen was opened and closed again,
/// or its rewards screen is left, it hears of every relic the player obtains and lets that
/// relic change its cards (<see cref="OnRelicObtained"/>): Molten Egg taken from the row above
/// upgrades its Attacks.
/// </para>
/// <para>
/// Left out, by name: Reroll (the relic Driftwood) and Sacrifice (Pael's Wing), both of act 2
/// Ancients. The only button besides the cards is Skip.
/// </para>
/// </summary>
public sealed class CardReward : Reward
{
    /// <summary>The game's id of the Skip button of a card reward.</summary>
    public const string SkipOption = "Skip";

    private readonly List<CardCreationResult> _cards = new();
    private bool _cardsWereManuallySet;

    /// <summary>How many cards the offer has when it is rolled.</summary>
    public int OptionCount { get; private init; }

    /// <summary>How the cards are rolled. The object is this reward's own; the hooks may change it.</summary>
    public CardCreationOptions Options { get; private init; }

    /// <summary>False takes the Skip button away: one of the cards has to be taken.</summary>
    public bool CanSkip { get; init; } = true;

    /// <summary>The reward still lets a relic the player obtains change its cards.</summary>
    public bool HearsRelics { get; private set; }

    /// <summary>The reward's screen was opened at least once: the rewards screen lists its cards from then on.</summary>
    public bool WasOpened { get; private set; }

    /// <summary>The order card rewards were made in over the whole run: relics obtained are heard in this order.</summary>
    public int Sequence { get; private init; }

    /// <summary>The cards on offer, as they stand.</summary>
    public IReadOnlyList<CardInstance> Cards => _cards.Select(result => result.Card).ToList();

    /// <summary>The same with what the hooks did to each card.</summary>
    public IReadOnlyList<CardCreationResult> Results => _cards;

    /// <summary>
    /// A card reward rolled from pools: the usual one. <paramref name="options"/> becomes the
    /// reward's own and is marked as a card reward's.
    /// </summary>
    public CardReward(Run run, CardCreationOptions options, int cardCount)
    {
        OptionCount = cardCount;
        Options = options.WithFlags(CardCreationFlags.IsCardReward);
        HearsRelics = true;
        Sequence = run.State.Part<RewardsState>().CardRewardsMade++;
    }

    /// <summary>
    /// A card reward of these very cards (made with <see cref="RunState.NewCard"/>). Hooks may
    /// still upgrade or enchant them when the reward is filled, but not swap them; a relic
    /// obtained later does not change them.
    /// </summary>
    public CardReward(Run run, IEnumerable<CardInstance> cardsToOffer, CardCreationSource source)
    {
        Options = new CardCreationOptions(Array.Empty<GameCard>(), source, CardOddsType.Uniform)
            .WithFlags(CardCreationFlags.NoCardPoolModifications | CardCreationFlags.NoCardModelModifications | CardCreationFlags.IsCardReward);
        _cardsWereManuallySet = true;
        _cards.AddRange(cardsToOffer.Select(card => new CardCreationResult(card)));
        OptionCount = _cards.Count;
        Sequence = run.State.Part<RewardsState>().CardRewardsMade++;
    }

    private CardReward(CardReward other)
    {
        _cards.AddRange(other._cards.Select(result => result.Clone()));
        _cardsWereManuallySet = other._cardsWereManuallySet;
        OptionCount = other.OptionCount;
        Options = other.Options.Clone();
        CanSkip = other.CanSkip;
        HearsRelics = other.HearsRelics;
        WasOpened = other.WasOpened;
        Sequence = other.Sequence;
    }

    public override int RewardsSetIndex => 5;
    public override bool IsPopulated => _cards.Count > 0;

    public override void Populate(Run run, int index)
    {
        if (_cardsWereManuallySet)
        {
            var modifiers = RewardHooks.ModifyCardRewardOptions(run, _cards, Options);
            if (modifiers.Count > 0) RunHooks.AfterModifyingCardRewardOptions(run.Context, modifiers);
        }
        else if (_cards.Count <= 0)
        {
            _cards.AddRange(CardFactory.CreateForReward(run, OptionCount, Options, card => Purpose.Card(index, card)));
            // The game builds the buttons here too (and again when the screen opens), which asks the hooks.
            Alternatives(run);
        }
    }

    /// <summary>
    /// A relic was obtained while the reward waits (CardReward.OnRelicObtained): the relic alone
    /// is asked whether it changes the cards, in both passes, and is told with "rewards were
    /// changed" each time it did.
    /// </summary>
    public void OnRelicObtained(Run run, Relic relic)
    {
        if (!HearsRelics || relic is not IRewardHooks hooks) return;
        var c = run.Context;
        if (hooks.TryModifyCardRewardOptions(c, _cards, Options)) relic.AfterModifyingRewards(c);
        if (hooks.TryModifyCardRewardOptionsLate(c, _cards, Options)) relic.AfterModifyingRewards(c);
    }

    /// <summary>The buttons besides the cards, by the game's option ids (CardRewardAlternative.Generate).</summary>
    /// <exception cref="NotSupportedException">A hook added a button the arena leaves out.</exception>
    public IReadOnlyList<string> Alternatives(Run run)
    {
        var alternatives = new List<string>();
        if (CanSkip) alternatives.Add(SkipOption);
        RewardHooks.ModifyCardRewardAlternatives(run, this, alternatives);
        if (alternatives.Count > 2) throw new InvalidOperationException("More than 2 card reward alternatives are not supported.");
        foreach (var option in alternatives)
            if (option != SkipOption)
                throw new NotSupportedException($"The arena leaves out the card reward button '{option}': Reroll and Sacrifice come from relics of act 2 Ancients.");
        return alternatives;
    }

    protected override bool OnSelect(Run run)
    {
        var rewardComplete = false;
        var endSelection = false;
        var canSkip = Alternatives(run).Contains(SkipOption);
        while (!endSelection)
        {
            // The card reward screen is a question of this action: the first time the answer is not
            // known the action is abandoned here, and run again from its start once the player answered.
            var answer = run.Ask(new CardRewardScreen(Cards, canSkip));
            if (answer is null)
            {
                // Skip ends the selection and leaves the reward open, also after a card was taken.
                rewardComplete = false;
                endSelection = true;
                continue;
            }

            var obtained = _cards[answer[0]].Card;
            rewardComplete = true;
            endSelection = !RewardHooks.ShouldAllowSelectingMoreCardRewards(run, this);
            var added = DeckEdits.Add(run, obtained);
            // The game drops the card that went into the deck from the offer. When a hook put another
            // card in on the way, that one is not among the offered cards and all of them stay.
            if (added.Success) _cards.RemoveAll(result => ReferenceEquals(result.Card, added.Card));
        }
        HearsRelics = false;
        WasOpened = true;
        return rewardComplete;
    }

    public override void OnSkipped(Run run) => HearsRelics = false;

    public override string Kind => "card";

    internal override void Describe(JsonObject row)
    {
        if (_cards.Count > 0) row["count"] = _cards.Count;
        if (!WasOpened || _cards.Count == 0) return;
        var cards = new JsonArray();
        foreach (var result in _cards) cards.Add(RunDescribe.CardOffer(result.Card));
        row["cards"] = cards;
    }

    public override string Label => _cards.Count > 0
        ? string.Create(CultureInfo.InvariantCulture, $"Open card reward ({_cards.Count} cards)")
        : "Open card reward";

    public override string What => "the card reward";

    internal override Reward Clone() => CopyTo(new CardReward(this));
}

/// <summary>
/// A set of rewards (the game's RewardsSet): what one rewards screen offers. It is made for a
/// room (<see cref="WithRewardsFromRoom"/>) or from rewards an event or a relic names
/// (<see cref="WithCustomRewards"/>), filled (<see cref="GenerateWithoutOffering"/>) and shown
/// (<see cref="Offer"/>). <see cref="RunRewards"/> has the short forms.
/// <para>
/// The draw order of a fight's rewards follows from how the game builds a set. Making the set
/// rolls whether a potion drops (one number). Filling it rolls each reward in the order the
/// set lists them: the gold (one number), the potion if one dropped (two), each card of the
/// card reward (three: rarity, pick, upgrade), then an elite's relic rarity (one). Then the
/// hooks may add rewards, which are filled in the order added. Only then the rows are sorted
/// for the screen: gold, potion, relic, special card, card, removal.
/// </para>
/// </summary>
public sealed class RewardsSet
{
    private readonly List<Reward> _rewards = new();
    private bool _allowEmptyRewards;
    private bool _isGenerated;

    /// <summary>The kind of room the set was made for; null for rewards an event or a relic offers.</summary>
    public RoomKind? Room { get; private set; }

    /// <summary>The set belongs to a fight's room: its screen is the room's last one and has the proceed button that leaves the room.</summary>
    public bool IsForCombatRoom { get; private set; }

    /// <summary>The rewards, taken ones included; sorted for the screen once the set is filled.</summary>
    public IReadOnlyList<Reward> Rewards => _rewards;

    /// <summary>Every reward has to be taken: the screen has no proceed button while one is left.</summary>
    public bool DisallowSkipping { get; private set; }

    public bool IsGenerated => _isGenerated;

    public bool AllRewardsSuccessfullySelected => _rewards.All(reward => reward.SuccessfullySelected);

    /// <summary>The set of a fight's room that gives no rewards: an empty screen with its proceed button.</summary>
    public RewardsSet EmptyForRoom(RewardRoom room)
    {
        Room = room.Kind;
        IsForCombatRoom = room.IsCombatRoom;
        _allowEmptyRewards = true;
        return this;
    }

    /// <summary>
    /// The rewards a room gives by itself (RewardsSet.WithRewardsFromRoom), not filled yet. For a
    /// fight's room: gold, the potion roll, three cards, and after an elite a relic; then the
    /// fight's extra rewards. The boss of the run's last act gives nothing. A treasure room has
    /// none of its own. The potion roll is drawn here.
    /// <para>
    /// Left out: the scripted rewards of a profile's very first run.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">A room that is neither a fight's nor a treasure room.</exception>
    public RewardsSet WithRewardsFromRoom(Run run, RewardRoom room)
    {
        Room = room.Kind;
        IsForCombatRoom = room.IsCombatRoom;
        var world = run.State.Part<RunWorld>();
        if (!world.IsBegun) throw new InvalidOperationException("Rewards need the run's world: call RunWorld.Begin(state) before the first room.");
        if (room.Kind == RoomKind.Boss && run.State.ActIndex >= world.Acts.Count - 1) return this;
        _rewards.AddRange(GenerateRewardsFor(run, room));
        if (room.IsCombatRoom) _rewards.AddRange(room.ExtraRewards);
        return this;
    }

    /// <summary>Rewards an event or a relic offers.</summary>
    public RewardsSet WithCustomRewards(IEnumerable<Reward> rewards)
    {
        _rewards.AddRange(rewards);
        return this;
    }

    /// <summary>The player has to take every reward before the screen can be left.</summary>
    public RewardsSet WithSkippingDisallowed()
    {
        DisallowSkipping = true;
        return this;
    }

    private static List<Reward> GenerateRewardsFor(Run run, RewardRoom room)
    {
        var list = new List<Reward>();
        if (!room.IsCombatRoom)
        {
            if (room.Kind != RoomKind.Treasure)
                throw new InvalidOperationException($"Tried to generate a reward for invalid room type: {room.Kind}");
            return list;
        }

        var (min, max) = room.Gold ?? RewardRoom.DefaultGold(room.Kind);
        switch (room.Kind)
        {
            case RoomKind.Monster:
                // Only a hallway fight pays by the share of its monsters that did not get away, each bound rounded to the nearest even.
                if (room.GoldProportion > 0f)
                    list.Add(new GoldReward((int)Math.Round((float)min * room.GoldProportion), (int)Math.Round((float)max * room.GoldProportion)));
                RollForPotionAndAddTo(list, run, room.Kind);
                list.Add(new CardReward(run, CardCreationOptions.ForRoom(room.Kind), 3));
                break;
            case RoomKind.Elite:
                list.Add(new GoldReward(min, max));
                RollForPotionAndAddTo(list, run, room.Kind);
                list.Add(new CardReward(run, CardCreationOptions.ForRoom(room.Kind), 3));
                list.Add(new RelicReward());
                break;
            case RoomKind.Boss:
                list.Add(new GoldReward(min, max));
                RollForPotionAndAddTo(list, run, room.Kind);
                list.Add(new CardReward(run, CardCreationOptions.ForRoom(room.Kind), 3));
                break;
        }
        return list;
    }

    /// <summary>The potion roll of a fight (PotionRewardOdds.Roll): the hooks are asked first, then the number is drawn whatever they said.</summary>
    private static void RollForPotionAndAddTo(List<Reward> rewards, Run run, RoomKind room)
    {
        var forced = RunHooks.ShouldForcePotionReward(run.Context, room);
        var rng = run.State.Part<RunWorld>().Streams.Draw(RunStream.Rewards, Purpose.PotionChance);
        if (RunWorld.RollPotionDrop(run.State, rng, elite: room == RoomKind.Elite, forced)) rewards.Add(new PotionReward());
    }

    /// <summary>
    /// Fills the set (RewardsSet.GenerateWithoutOffering): every reward is filled in list order
    /// (a fixed gold amount still draws its number); the hooks may add to the list; what they
    /// added and is not filled yet is filled in the order added; the listeners that changed
    /// something are told; the rows are sorted for the screen. Only the first call does anything.
    /// </summary>
    /// <exception cref="RunUnrepresentableException">A roll can only land on something the arena has not built, or strict mode met such a thing.</exception>
    public void GenerateWithoutOffering(Run run)
    {
        if (_isGenerated) return;
        var before = _rewards.ToList();
        foreach (var reward in before) Populate(run, reward);
        var modifiers = RewardHooks.ModifyRewards(run, _rewards, Room);
        foreach (var added in _rewards.Where(reward => !before.Contains(reward)).Distinct().ToList())
            if (!added.IsPopulated) Populate(run, added);
        RunHooks.AfterModifyingRewards(run.Context, modifiers);
        SmallSort.Sort(_rewards, (x, y) => x.RewardsSetIndex.CompareTo(y.RewardsSetIndex));
        _isGenerated = true;
    }

    /// <summary>Fills one reward, telling it which of its kind it is in this set.</summary>
    private void Populate(Run run, Reward reward)
    {
        var index = 0;
        foreach (var other in _rewards)
        {
            if (ReferenceEquals(other, reward)) break;
            if (other.GetType() == reward.GetType()) index++;
        }
        reward.Populate(run, index);
    }

    /// <summary>
    /// Fills the set and puts its rewards screen up (RewardsSet.Offer). Nothing is shown, and
    /// null comes back, for a dead player, and for a set that is empty unless it belongs to a
    /// fight's room (a treasure room no relic added anything to).
    /// </summary>
    /// <exception cref="RunUnrepresentableException">A roll can only land on something the arena has not built, or strict mode met such a thing.</exception>
    public RewardsScreen? Offer(Run run)
    {
        if (run.State.IsDead) return null;
        GenerateWithoutOffering(run);
        if (_rewards.Count <= 0 && !IsForCombatRoom && !_allowEmptyRewards) return null;
        var screen = new RewardsScreen(this, isTerminal: IsForCombatRoom);
        run.Push(screen);
        return screen;
    }

    internal RewardsSet Clone()
    {
        var copy = new RewardsSet
        {
            Room = Room, IsForCombatRoom = IsForCombatRoom, DisallowSkipping = DisallowSkipping,
            _allowEmptyRewards = _allowEmptyRewards, _isGenerated = _isGenerated,
        };
        foreach (var reward in _rewards) copy._rewards.Add(reward.Clone());
        return copy;
    }
}

/// <summary>
/// The sort the game's lists use (List.Sort with a comparison) for the short lists the arena
/// meets, written out because its order of equal items is part of the game's behaviour and is
/// not what one would guess: two items are swapped when out of order; three are settled by the
/// swaps (0,1), (0,2), (1,2), which turns [x, x', y] with y first into [y, x', x]; four to
/// sixteen are sorted by insertion, which keeps equal items in order. Longer lists take
/// another path in .NET that is not rebuilt here.
/// </summary>
public static class SmallSort
{
    /// <summary>The longest list .NET sorts by insertion.</summary>
    public const int Longest = 16;

    /// <exception cref="NotSupportedException">More than sixteen items.</exception>
    public static void Sort<T>(IList<T> items, Comparison<T> comparison)
    {
        var n = items.Count;
        if (n < 2) return;
        if (n > Longest)
            throw new NotSupportedException(string.Create(CultureInfo.InvariantCulture, $"The arena leaves out the game's sort of more than {Longest} items ({n} here)."));

        void SwapIfGreater(int i, int j)
        {
            if (comparison(items[i], items[j]) > 0) (items[i], items[j]) = (items[j], items[i]);
        }

        if (n == 2)
        {
            SwapIfGreater(0, 1);
            return;
        }
        if (n == 3)
        {
            SwapIfGreater(0, 1);
            SwapIfGreater(0, 2);
            SwapIfGreater(1, 2);
            return;
        }
        for (var i = 0; i < n - 1; i++)
        {
            var item = items[i + 1];
            var j = i;
            while (j >= 0 && comparison(item, items[j]) < 0)
            {
                items[j + 1] = items[j];
                j--;
            }
            items[j + 1] = item;
        }
    }
}

/// <summary>
/// The rewards of a run in short (the game's RewardsCmd): after a won fight
/// <code>
/// public override void AfterFightWon(Run run, FightOutcome fight) => RunRewards.OfferForRoomEnd(run, fight);
/// </code>
/// puts up the fight's rewards screen, whose proceed button calls <see cref="Run.RoomFinished"/>;
/// an event or a relic offers rewards of its own with <see cref="OfferCustom"/>, whose screen
/// closes by itself and shows what was under it again.
/// <para>
/// Rewards need the run's world (<see cref="RunWorld.Begin(RunState, RunUnlocks?, StreamMode, string, IReadOnlyList{string}?)"/>):
/// their rolls are draws of the player's rewards stream. They are for outside a fight.
/// </para>
/// <para>
/// A roll that can only land on something the arena has not built, or any such landing in
/// strict mode (<see cref="LeftOutLog.Strict"/>), throws <see cref="RunUnrepresentableException"/>:
/// whoever runs the act ends the run as unrepresentable with its message.
/// </para>
/// </summary>
public static class RunRewards
{
    /// <summary>
    /// The rewards of a room that is finished, shown (CombatRoom.OfferRoomEndRewards): the screen,
    /// or null when there is none: a dead player, an encounter that gives no rewards (the room is
    /// then simply finished), a treasure room nothing was added to.
    /// </summary>
    public static RewardsScreen? OfferForRoomEnd(Run run, RewardRoom room)
    {
        if (room.IsCombatRoom && !room.ShouldGiveRewards) return null;
        return GenerateForRoomEnd(run, room).Offer(run);
    }

    /// <summary>The same for a fight the run has just won.</summary>
    public static RewardsScreen? OfferForRoomEnd(Run run, FightOutcome fight) => OfferForRoomEnd(run, RewardRoom.Of(fight));

    /// <summary>Rewards an event or a relic offers, shown (RewardsCmd.OfferCustom). Null when nothing is shown: no rewards, or a dead player.</summary>
    public static RewardsScreen? OfferCustom(Run run, IEnumerable<Reward> rewards) => new RewardsSet().WithCustomRewards(rewards).Offer(run);

    /// <summary>A room's rewards, filled and not shown (RewardsCmd.GenerateForRoomEnd).</summary>
    public static RewardsSet GenerateForRoomEnd(Run run, RewardRoom room)
    {
        var set = new RewardsSet().WithRewardsFromRoom(run, room);
        set.GenerateWithoutOffering(run);
        return set;
    }

    /// <summary>Custom rewards, filled and not shown (RewardsCmd.GenerateCustom).</summary>
    public static RewardsSet GenerateCustom(Run run, IEnumerable<Reward> rewards)
    {
        var set = new RewardsSet().WithCustomRewards(rewards);
        set.GenerateWithoutOffering(run);
        return set;
    }

    /// <summary>The card rewards on the run's open rewards screens that still hear of relics obtained, in the order they were made.</summary>
    public static IReadOnlyList<CardReward> PendingCardRewards(Run run) =>
        run.Screens.OfType<RewardsScreen>()
            .SelectMany(screen => screen.Set.Rewards.OfType<CardReward>())
            .Where(reward => reward.HearsRelics)
            .OrderBy(reward => reward.Sequence)
            .ToList();
}
