using System.Globalization;

namespace SpireMind.Arena;

// The first events of run mode (chunk 15 of the run-mode plan): seven of the twenty-one events
// an Overgrowth act can hold, and the two relics one of them leads to. Each class follows the
// game class of the same name (MegaCrit.Sts2.Core.Models.Events and Models.Relics, v0.107.1),
// method by method; what an event is written against is Sim/Run/EVENTS.md, and this file's own
// page is EVENTS.md next to it.
//
//   Aroma of Chaos         always allowed     transform a card, or upgrade a card
//   Jungle Maze Adventure  always allowed     lose 18 HP for 135 to 164 gold, or 35 to 64 gold
//   Morphic Grove          100 gold and two transformable cards   all gold for two transforms, or 5 max HP
//   The Sunken Statue      always allowed     Sword of Stone, or 101 to 121 gold for 7 HP
//   Tablet of Truth        always allowed     max HP for upgrades over five pages, or heal 20
//   This or That?          always allowed     6 HP for 41 to 68 gold, or a random relic and Clumsy
//   Unrest Site            HP at or below 70% heal what was missing and Poor Sleep, or 8 max HP for a random relic
//
// When an event is allowed is not here: EventList.IsAllowed holds every event's condition.
//
// Titles (of events, options and relics) are the game's English texts, read from its text
// tables. Every other text is the arena's own plain wording with the numbers as they stand.
// A finished page says what the option did, with the numbers that really moved (a relic can
// take HP off a loss), so that the page never states more than happened.

/// <summary>Registers the seven events and the two swords.</summary>
public sealed class EventsA : IContentModule
{
    /// <summary>The class names of this file's events, in the plan's order.</summary>
    public static readonly IReadOnlyList<string> Events =
        ["AromaOfChaos", "JungleMazeAdventure", "MorphicGrove", "SunkenStatue", "TabletOfTruth", "ThisOrThat", "UnrestSite"];

    /// <summary>The class names of this file's relics.</summary>
    public static readonly IReadOnlyList<string> Relics = ["SwordOfStone", "SwordOfJade"];

    public void Register(Registry r)
    {
        r.Event<AromaOfChaos>();
        r.Event<JungleMazeAdventure>();
        r.Event<MorphicGrove>();
        r.Event<SunkenStatue>();
        r.Event<TabletOfTruth>();
        r.Event<ThisOrThat>();
        r.Event<UnrestSite>();

        r.Relic<SwordOfStone>();
        r.Relic<SwordOfJade>();
    }

    /// <summary>
    /// "Obtain a random Relic" as two of these events do it (the game's
    /// RelicFactory.PullNextRelicFromFront with the player alone, then RelicCmd.Obtain): one
    /// number of the player's <c>rewards</c> stream for the rarity, the next relic of that rarity
    /// from the front of the player's bag, and the relic is obtained at once, pickup effect and
    /// all. What the pull did not take from the game goes into the run's log, as every caller of
    /// a pull writes it; a pull that can give nothing ends the run as unrepresentable.
    /// </summary>
    /// <param name="what">What is being pulled, for the log and the reason ("the relic of Unrest Site").</param>
    internal static Relic ObtainRandomRelic(Run run, string what)
    {
        var state = run.State;
        var strict = state.FindPart<LeftOutLog>()?.Strict ?? false;
        var rng = state.Part<RunWorld>().Streams.Draw(RunStream.Rewards, Purpose.RelicRarity(0));
        var pull = RunRelics.RollAndPullFromFront(state, rng, strict);
        if (!pull.Found)
            throw new RunUnrepresentableException(string.Create(CultureInfo.InvariantCulture, $"{what} on floor {state.TotalFloor}: {pull.Reason}"));
        // A rarity that was rolled again: the game's relic is of a list the arena holds nothing of, so it is not among the passed-over ones.
        if (pull.Rerolls > 0 && pull.GameRelic is { } games) state.Part<LeftOutLog>().PassedOver(state, "relic", games, $"{what}, whose rarity was then rolled again");
        foreach (var passed in pull.PassedOver) state.Part<LeftOutLog>().PassedOver(state, "relic", passed, what);
        return run.ObtainRelic(pull.Relic!);
    }

    /// <summary>The game's Rng.NextFloat(min, max): one number, scaled in 64 bits and narrowed to 32.</summary>
    internal static float NextFloat(GameRng rng, float min, float max) => (float)(rng.NextDouble() * (double)(max - min) + (double)min);

    internal static string Number(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>"Strike became Anger", for a finished page that names what a transform gave.</summary>
    internal static string Became(string before, DeckAddResult after) => $"{before} became {after.Card.Title}";
}

// ---------- Aroma of Chaos ----------

/// <summary>
/// Aroma of Chaos (AromaOfChaos.cs). Two options, no roll at the start.
/// <list type="bullet">
/// <item>Let Go: one card of the deck, chosen on the transform screen (every card that is not a
/// Quest card and not Eternal; with a single candidate it is taken without a screen, with none
/// nothing happens), becomes a random card by the game's transform rule, rolled on the event's
/// own stream. The new card goes to the end of the deck.</item>
/// <item>Maintain Control: one card of the deck that can still be upgraded, chosen on the
/// upgrade screen (the same rule for one candidate or none), is upgraded.</item>
/// </list>
/// Neither choice can be backed out of: the game's screens have no close button here.
/// </summary>
public sealed class AromaOfChaos : RunEvent
{
    public override string Title => "Aroma of Chaos";

    protected override string InitialDescription(Run run) => "A strange scent fills a clearing. You can give in to it, or hold on to yourself.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("LET_GO", "Let Go", "Transform a card of your Deck: you choose it, and it becomes a random card.", LetGo),
        Option("MAINTAIN_CONTROL", "Maintain Control", "Upgrade a card of your Deck: you choose it.", MaintainControl),
    ];

    private void LetGo(Run run)
    {
        var card = CardSelect.FromDeckForTransformation(run, CardSelectPrefs.Transform(1)).FirstOrDefault();
        if (card is null)
        {
            SetEventFinished("You let go. No card of your Deck could be transformed.");
            return;
        }
        var before = card.Title;
        var after = DeckEdits.TransformToRandom(run, card, Rng);
        SetEventFinished($"You let go. {EventsA.Became(before, after)}.");
    }

    private void MaintainControl(Run run)
    {
        var card = CardSelect.FromDeckForUpgrade(run, CardSelectPrefs.Upgrade(1)).FirstOrDefault();
        if (card is null)
        {
            SetEventFinished("You hold on. No card of your Deck could be upgraded.");
            return;
        }
        var before = card.Title;
        DeckEdits.Upgrade(run, card);
        SetEventFinished($"You hold on. {before} was upgraded to {card.Title}.");
    }
}

// ---------- Jungle Maze Adventure ----------

/// <summary>
/// Jungle Maze Adventure (JungleMazeAdventure.cs). At its start two numbers of the event's
/// stream, in this order: the gold of Solo Quest is 150 plus a fraction from -15 up to 15, the
/// gold of Join Forces 50 plus another one. The game draws each as a 32-bit float and adds it
/// to the whole number as a decimal; gold is paid in whole numbers, the fraction cut off
/// (PlayerCmd.GainGold), so Solo Quest pays 135 to 164 and Join Forces 35 to 64. The game's own
/// text shows the same whole number.
/// <list type="bullet">
/// <item>Solo Quest: lose 18 HP (damage that ignores Block and is not modified), then gain the
/// gold. Before either, the game shuffles its three sound effects on the event's stream: two
/// numbers that decide nothing. Listed as deadly at 18 HP or less.</item>
/// <item>Join Forces: gain the gold.</item>
/// </list>
/// </summary>
public sealed class JungleMazeAdventure : RunEvent
{
    public const int SoloGold = 150;
    public const int SoloHp = 18;
    public const int JoinForcesGold = 50;

    /// <summary>Each gold amount is moved by a fraction from minus this up to this.</summary>
    public const float GoldSpread = 15f;

    private decimal _soloGold = SoloGold;
    private decimal _joinForcesGold = JoinForcesGold;

    public override string Title => "Jungle Maze Adventure";

    /// <summary>What Solo Quest pays, as rolled: the whole-number part is paid.</summary>
    public decimal SoloGoldRolled => _soloGold;

    /// <summary>What Join Forces pays, as rolled.</summary>
    public decimal JoinForcesGoldRolled => _joinForcesGold;

    protected override void CalculateVars(Run run)
    {
        _soloGold += (decimal)EventsA.NextFloat(Rng, -GoldSpread, GoldSpread);
        _joinForcesGold += (decimal)EventsA.NextFloat(Rng, -GoldSpread, GoldSpread);
    }

    protected override string InitialDescription(Run run) => "A maze in the jungle is said to hold treasure. Other adventurers offer to go in with you and share what is found.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("SOLO_QUEST", "Solo Quest", string.Create(CultureInfo.InvariantCulture, $"Gain {(int)_soloGold} Gold. Lose {SoloHp} HP."), DontNeedHelp).ThatDoesDamage(SoloHp),
        Option("JOIN_FORCES", "Join Forces", string.Create(CultureInfo.InvariantCulture, $"Gain {(int)_joinForcesGold} Gold."), SafetyInNumbers),
    ];

    private void DontNeedHelp(Run run)
    {
        // The game's shuffle of its three sound effects: sorted, then shuffled on the event's stream.
        Rng.StableShuffle(new List<int> { 0, 1, 2 });
        var lost = run.Damage(SoloHp);
        if (run.IsOver) return;
        var paid = run.GainGold(_soloGold);
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You went in alone: you lost {lost} HP and gained {paid} Gold."));
    }

    private void SafetyInNumbers(Run run)
    {
        var paid = run.GainGold(_joinForcesGold);
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You went in with the others and gained {paid} Gold."));
    }
}

// ---------- Morphic Grove ----------

/// <summary>
/// Morphic Grove (MorphicGrove.cs). No roll at the start. It comes up only with 100 gold or
/// more and two transformable cards (<see cref="EventList.IsAllowed(Run, string)"/>).
/// <list type="bullet">
/// <item>Group: all gold is lost first (nobody hears it). Then two cards of the deck are chosen
/// on the transform screen (every card that is not a Quest card and not Eternal; with two
/// candidates or fewer they are taken without a screen), and each becomes a random card on the
/// event's stream, ONE AT A TIME in the order the screen hands them back: the first is out of
/// the deck and its replacement at the deck's end before the second is rolled.</item>
/// <item>Loner: gain 5 max HP (which heals 5).</item>
/// </list>
/// </summary>
public sealed class MorphicGrove : RunEvent
{
    public const int MaxHpGain = 5;
    public const int TransformCount = 2;

    public override string Title => "Morphic Grove";

    protected override string InitialDescription(Run run) => "In this grove things change their shape. A group gathered here will change two of your cards for everything you carry; or you keep to yourself.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("GROUP", "Group", string.Create(CultureInfo.InvariantCulture, $"Lose ALL of your Gold. Transform {TransformCount} cards of your Deck: you choose them, and each becomes a random card."), Group),
        Option("LONER", "Loner", string.Create(CultureInfo.InvariantCulture, $"Gain {MaxHpGain} Max HP."), Loner),
    ];

    private void Loner(Run run)
    {
        var before = run.MaxHp;
        run.GainMaxHp(MaxHpGain);
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You kept to yourself and gained {run.MaxHp - before} Max HP."));
    }

    private void Group(Run run)
    {
        var gold = run.Gold;
        run.LoseGold(gold);
        // Counted before the cards change: a relic may pay gold for a card that arrives in the deck.
        var lost = gold - run.Gold;
        var cards = CardSelect.FromDeckForTransformation(run, CardSelectPrefs.Transform(TransformCount));
        var changes = new List<string>(cards.Count);
        foreach (var card in cards)
        {
            var before = card.Title;
            changes.Add(EventsA.Became(before, DeckEdits.TransformToRandom(run, card, Rng)));
        }
        var what = changes.Count > 0 ? string.Join(", ", changes) + "." : "No card of your Deck could be transformed.";
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You joined the group and lost {lost} Gold. {what}"));
    }
}

// ---------- The Sunken Statue ----------

/// <summary>
/// The Sunken Statue (SunkenStatue.cs; also an event of Underdocks). At its start one number of
/// the event's stream: the gold is 111 plus a whole number from -10 to 10.
/// <list type="bullet">
/// <item>Grab the Sword: obtain Sword of Stone (the option's tooltip shows the relic).</item>
/// <item>Dive into the Water: gain the gold FIRST, then lose 7 HP (damage that ignores Block
/// and is not modified). Listed as deadly at 7 HP or less.</item>
/// </list>
/// </summary>
public sealed class SunkenStatue : RunEvent
{
    public const int BaseGold = 111;
    public const int GoldVariance = 10;
    public const int HpLoss = 7;

    private int _gold = BaseGold;

    public override string Title => "The Sunken Statue";

    /// <summary>The gold Dive into the Water pays, as rolled.</summary>
    public int Gold => _gold;

    /// <summary>The start roll: one whole number from -10 to 10 on top of 111.</summary>
    internal static int RollGold(GameRng rng) => BaseGold + rng.NextInt(-GoldVariance, GoldVariance + 1);

    protected override void CalculateVars(Run run) => _gold = RollGold(Rng);

    protected override string InitialDescription(Run run) => "A statue stands half under water, a stone sword in its hand. Coins glint on the bottom around it.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("GRAB_SWORD", "Grab the Sword", "Obtain Sword of Stone.", GrabSword).WithRelicTip(nameof(SwordOfStone)),
        Option("DIVE_INTO_WATER", "Dive into the Water", string.Create(CultureInfo.InvariantCulture, $"Gain {_gold} Gold. Lose {HpLoss} HP."), DiveIntoWater).ThatDoesDamage(HpLoss),
    ];

    private void GrabSword(Run run)
    {
        var relic = run.ObtainRelic(nameof(SwordOfStone));
        if (run.IsOver) return;
        SetEventFinished($"You took the sword: you obtained {relic.Name}.");
    }

    private void DiveIntoWater(Run run)
    {
        var paid = run.GainGold(_gold);
        var lost = run.Damage(HpLoss);
        if (run.IsOver) return;
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You dived for the coins: you gained {paid} Gold and lost {lost} HP."));
    }
}

// ---------- Tablet of Truth ----------

/// <summary>
/// Tablet of Truth (TabletOfTruth.cs). No roll at the start; up to five pages.
/// <list type="bullet">
/// <item>Decipher costs max HP: 3, then 6, 12 and 24 on the pages that follow, and on the fifth
/// time all max HP but 1, counted from the max HP the player had when that page was made. Each
/// of the first four upgrades ONE random card of the deck that can still be upgraded (one
/// number of the event's stream, a pick in deck order; no number when there is none); the
/// fifth upgrades every such card and finishes the event.</item>
/// <item>Smash, on the first page only: heal 20 HP.</item>
/// <item>Give Up, from the second page on: leave.</item>
/// </list>
/// Decipher says it would kill while max HP is at or below its price, asked each time the page
/// is read. The game's branch for that case (max HP goes to 1, then the player is killed) is
/// ported, though the mod never offers a deadly option. Losing max HP takes the HP above the
/// new maximum as damage first, which relics can change (Tungsten Rod).
/// </summary>
public sealed class TabletOfTruth : RunEvent
{
    public const int SmashHeal = 20;

    /// <summary>The price of the first Decipher, and of the next three by how many were done.</summary>
    public const int FirstCost = 3, SecondCost = 6, ThirdCost = 12, FourthCost = 24;

    /// <summary>The Decipher that finishes the event.</summary>
    public const int LastDecipher = 5;

    private int _decipherCount;
    private decimal _decipherCost = FirstCost;
    private string _upgraded = "";

    public override string Title => "Tablet of Truth";

    /// <summary>How often Decipher was chosen.</summary>
    public int DecipherCount => _decipherCount;

    /// <summary>The max HP the next Decipher costs.</summary>
    public decimal DecipherCost => _decipherCost;

    protected override string InitialDescription(Run run) => "A stone tablet covered in writing. Reading it wears you down, and every line costs more than the last.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("DECIPHER_1", "Decipher", DecipherText(), Decipher).ThatWillKillPlayerIf(DecipherWouldKill),
        Option("SMASH", "Smash", string.Create(CultureInfo.InvariantCulture, $"Heal {SmashHeal} HP."), Smash),
    ];

    private string DecipherText() => _decipherCount == LastDecipher - 1
        ? $"Lose {EventsA.Number(_decipherCost)} Max HP. Upgrade ALL cards of your Deck."
        : $"Lose {EventsA.Number(_decipherCost)} Max HP. Upgrade a random card of your Deck.";

    /// <summary>The titles of Decipher on the pages after the first, by how many were done.</summary>
    private string DecipherTitle() => _decipherCount switch
    {
        1 => "Continue Deciphering",
        2 => "Keep Deciphering",
        3 => "KEEP DECIPHERING",
        _ => "Lose Everything",
    };

    private bool DecipherWouldKill(Run run) => run.MaxHp <= _decipherCost;

    private void Smash(Run run)
    {
        var before = run.Hp;
        run.Heal(SmashHeal);
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You smashed the tablet and healed {run.Hp - before} HP."));
    }

    private void Decipher(Run run)
    {
        var maxHpBefore = run.MaxHp;
        LoseMaxHpAndUpgrade(run, _decipherCost);
        if (run.IsOver) return;
        _decipherCount++;
        var happened = string.Create(CultureInfo.InvariantCulture, $"You read on and lost {maxHpBefore - run.MaxHp} Max HP.") + (_upgraded.Length > 0 ? " " + _upgraded : "");
        if (_decipherCount == LastDecipher)
        {
            SetEventFinished(happened);
            return;
        }

        _decipherCost = GetDecipherCost(run);
        SetEventState(happened,
            Option("DECIPHER", DecipherTitle(), DecipherText(), Decipher).ThatWillKillPlayerIf(DecipherWouldKill),
            Option("GIVE_UP", "Give Up", "Stop reading and leave.", GiveUp));
    }

    /// <summary>The price of the next Decipher by how many were done (the game's GetDecipherCost).</summary>
    private decimal GetDecipherCost(Run run) => _decipherCount switch
    {
        1 => SecondCost,
        2 => ThirdCost,
        3 => FourthCost,
        4 => run.MaxHp - 1,
        _ => throw new InvalidOperationException($"Tablet of Truth has no price for a Decipher after {_decipherCount}."),
    };

    private void GiveUp(Run run) => SetEventFinished("You stopped reading and left the tablet.");

    private void LoseMaxHpAndUpgrade(Run run, decimal hp)
    {
        _upgraded = "";
        if (!(hp < run.MaxHp))
        {
            // The price is all the max HP there is: down to 1, then death (which a relic or a potion may prevent).
            run.LoseMaxHp(run.MaxHp - 1);
            if (run.IsOver) return;
            run.OutOfCombat(f => f.Kill([f.Player]));
            return;
        }

        run.LoseMaxHp(hp);
        if (run.IsOver) return;
        var upgradable = run.State.Deck.Where(card => card.IsUpgradable).ToList();
        if (_decipherCount == LastDecipher - 1)
        {
            DeckEdits.Upgrade(run, upgradable);
            _upgraded = upgradable.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Every card of your Deck that could be was upgraded ({upgradable.Count}).")
                : "No card of your Deck could be upgraded.";
        }
        else if (upgradable.Count != 0)
        {
            var card = Rng.NextItem(upgradable)!;
            var before = card.Title;
            DeckEdits.Upgrade(run, card);
            _upgraded = $"{before} was upgraded to {card.Title}.";
        }
        else
        {
            _upgraded = "No card of your Deck could be upgraded.";
        }
    }
}

// ---------- This or That? ----------

/// <summary>
/// This or That? (ThisOrThat.cs; a shared event). At its start one number of the event's
/// stream: the gold, a whole number from 41 to 68.
/// <list type="bullet">
/// <item>This (the game's PLAIN): lose 6 HP (damage that ignores Block and is not modified),
/// then gain the gold. Listed as deadly at 6 HP or less.</item>
/// <item>That (the game's ORNATE): obtain a random relic (a rarity rolled on the player's
/// <c>rewards</c> stream, the front of the player's bag), THEN the curse Clumsy is added to
/// the deck. The option's tooltip shows Clumsy.</item>
/// </list>
/// </summary>
public sealed class ThisOrThat : RunEvent
{
    public const int HpLoss = 6;
    public const int MinGold = 41;
    public const int MaxGold = 68;
    public const string Curse = "Clumsy";

    private int _gold;

    public override string Title => "This or That?";

    /// <summary>The gold This pays, as rolled.</summary>
    public int Gold => _gold;

    protected override void CalculateVars(Run run) => _gold = Rng.NextInt(MinGold, MaxGold + 1);

    protected override string InitialDescription(Run run) => "Two things are held out to you, a plain one and an ornate one. You may take one of them.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("PLAIN", "This", string.Create(CultureInfo.InvariantCulture, $"Lose {HpLoss} HP. Gain {_gold} Gold."), Plain).ThatDoesDamage(HpLoss),
        Option("ORNATE", "That", "Obtain a random Relic. Add Clumsy to your Deck.", Ornate).WithCards(Curse),
    ];

    private void Plain(Run run)
    {
        var lost = run.Damage(HpLoss);
        if (run.IsOver) return;
        var paid = run.GainGold(_gold);
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You took the plain one: you lost {lost} HP and gained {paid} Gold."));
    }

    private void Ornate(Run run)
    {
        var relic = EventsA.ObtainRandomRelic(run, "the relic of This or That?");
        if (run.IsOver) return;
        var curse = DeckEdits.AddCurses(run, [Content.Registry.Card(Curse)])[0];
        SetEventFinished($"You took the ornate one: you obtained {relic.Name}." + (curse.Success ? $" {curse.Card.Title} was added to your Deck." : ""));
    }
}

// ---------- Unrest Site ----------

/// <summary>
/// Unrest Site (UnrestSite.cs). It comes up only at 70% of max HP or less
/// (<see cref="EventList.IsAllowed(Run, string)"/>). At its start it notes the HP that is
/// missing (no roll): that is what Rest heals, also if HP changes while the page is open.
/// <list type="bullet">
/// <item>Rest Anyways: heal the noted amount, then the curse Poor Sleep is added to the deck.
/// The option's tooltip shows Poor Sleep.</item>
/// <item>Kill the Trees: lose 8 max HP FIRST, then obtain a random relic (a rarity rolled on
/// the player's <c>rewards</c> stream, the front of the player's bag). Listed as deadly at 8
/// max HP or less.</item>
/// </list>
/// </summary>
public sealed class UnrestSite : RunEvent
{
    public const int MaxHpLoss = 8;
    public const string Curse = "PoorSleep";

    private int _heal;

    public override string Title => "Unrest Site";

    /// <summary>The HP Rest Anyways heals: what was missing when the event started.</summary>
    public int Heal => _heal;

    protected override void CalculateVars(Run run) => _heal = run.MaxHp - run.Hp;

    protected override string InitialDescription(Run run) => "A resting place among restless trees. You are hurt and tired, and they will not let you sleep well.";

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) =>
    [
        Option("REST", "Rest Anyways", string.Create(CultureInfo.InvariantCulture, $"Heal {_heal} HP. Add Poor Sleep to your Deck."), Rest).WithCards(Curse),
        Option("KILL", "Kill the Trees", string.Create(CultureInfo.InvariantCulture, $"Lose {MaxHpLoss} Max HP. Obtain a random Relic."), Kill).ThatDecreasesMaxHp(MaxHpLoss),
    ];

    private void Rest(Run run)
    {
        var before = run.Hp;
        run.Heal(_heal);
        var healed = run.Hp - before;
        var curse = DeckEdits.AddCurses(run, [Content.Registry.Card(Curse)])[0];
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You rested anyway and healed {healed} HP.") + (curse.Success ? $" {curse.Card.Title} was added to your Deck." : ""));
    }

    private void Kill(Run run)
    {
        var before = run.MaxHp;
        run.LoseMaxHp(MaxHpLoss);
        if (run.IsOver) return;
        // Counted before the relic comes: its own pickup may change max HP again (Strawberry).
        var lost = before - run.MaxHp;
        var relic = EventsA.ObtainRandomRelic(run, "the relic of Unrest Site");
        if (run.IsOver) return;
        SetEventFinished(string.Create(CultureInfo.InvariantCulture, $"You cut the trees down: you lost {lost} Max HP and obtained {relic.Name}."));
    }
}

// ---------- the two swords ----------

/// <summary>
/// Sword of Stone (SwordOfStone.cs; Event rarity, given by The Sunken Statue). It counts the
/// Elite fights won while it is held, in the victory hook, by the room's kind alone (the
/// encounter's room type). At the fifth it is replaced by Sword of Jade, which stands where it
/// stood among the relics. The count is the relic's own and goes from room to room; the game
/// shows it on the relic, and here it is in the relic's text.
/// </summary>
public sealed class SwordOfStone : Relic
{
    /// <summary>The Elite fights it takes.</summary>
    public const int Elites = 5;

    private int _elitesDefeated;

    public override string Name => "Sword of Stone";

    /// <summary>The Elite fights won while it was held (the game's saved ElitesDefeated).</summary>
    public int ElitesDefeated => _elitesDefeated;

    public override string Describe() => string.Create(CultureInfo.InvariantCulture,
        $"After {Elites} won Elite fights it turns into Sword of Jade (at the start of every fight: gain {SwordOfJade.Strength} Strength). Won so far: {_elitesDefeated}.");

    public override void AfterCombatVictory(Fight f)
    {
        if (f.RoomType != RoomType.Elite) return;
        _elitesDefeated++;
        if (_elitesDefeated >= Elites) f.ReplaceRelic(this, new SwordOfJade());
    }
}

/// <summary>
/// Sword of Jade (SwordOfJade.cs; Event rarity, what Sword of Stone becomes): 3 Strength when a
/// fight's room is entered (any combat room), with no applier, unlike Vajra's.
/// </summary>
public sealed class SwordOfJade : Relic
{
    public const int Strength = 3;

    public override string Name => "Sword of Jade";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"At the start of every fight: gain {Strength} Strength.");

    public override void AfterRoomEntered(RunContext c, RoomKind room)
    {
        if (c.Fight is { } f && room.IsCombat()) f.ApplyPower<StrengthPower>(f.Player, Strength, null);
    }
}
