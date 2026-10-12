using System.Globalization;

namespace SpireMind.Arena;

// Neow's first relics (chunk 14 of the run-mode plan): 14 of the 27 relics Neow can offer an
// Ironclad in a single-player run, the ones that need no screen the arena does not have yet.
// Each class follows the game class of the same name (MegaCrit.Sts2.Core.Models.Relics,
// v0.107.1); what a relic is written against is Sim/Run/RELICS.md, Neow's room and her offer
// are Sim/Run/EVENTS.md, and this file's own page is NEOW-CORE.md next to it.
//
// Always in Neow's positive list: Arcane Scroll, Booming Conch, Golden Pearl, Lost Coffer, Neow's Torment,
//                                 New Leaf, Precise Scissors
// Positive, one of a coin's pair:  Nutritious Oyster (or Stone Humidifier), Neow's Talisman or Pomander
// Cursed:                          Cursed Pearl, Hefty Tablet, Leafy Poultice, Precarious Shears
//
// Thirteen of them are a pickup effect and nothing else: after it the relic does nothing for the
// rest of the run. Booming Conch is the one that acts in fights. None keeps a number between
// rooms.
//
// A pickup that needs the run (a card choice, a roll on a stream of the run, a rewards screen,
// a change of the deck) takes it with c.NeedRun: Neow hands her relics out outside a fight, and
// inside one such a pickup is refused by name.
//
// Which stream a roll is on is the game's: Arcane Scroll's and Hefty Tablet's cards and Lost
// Coffer's rewards on the player's rewards stream, New Leaf's new card on the run's niche
// stream, Leafy Poultice's two on the player's transformations stream.

/// <summary>Registers the 14 relics.</summary>
public sealed class NeowCore : IContentModule
{
    /// <summary>The class names of this file's positive relics, in the plan's order: the seven of Neow's fixed list, then the three a coin adds.</summary>
    public static readonly IReadOnlyList<string> Positive =
        ["ArcaneScroll", "BoomingConch", "GoldenPearl", "LostCoffer", "NeowsTorment", "NewLeaf", "PreciseScissors", "NutritiousOyster", "NeowsTalisman", "Pomander"];

    /// <summary>The class names of this file's cursed relics.</summary>
    public static readonly IReadOnlyList<string> Cursed = ["CursedPearl", "HeftyTablet", "LeafyPoultice", "PrecariousShears"];

    /// <summary>All 14, the positive ones first.</summary>
    public static readonly IReadOnlyList<string> All = [.. Positive, .. Cursed];

    public void Register(Registry r)
    {
        r.Relic<ArcaneScroll>();
        r.Relic<BoomingConch>();
        r.Relic<GoldenPearl>();
        r.Relic<LostCoffer>();
        r.Relic<NeowsTorment>();
        r.Relic<NewLeaf>();
        r.Relic<PreciseScissors>();
        r.Relic<NutritiousOyster>();
        r.Relic<NeowsTalisman>();
        r.Relic<Pomander>();

        r.Relic<CursedPearl>();
        r.Relic<HeftyTablet>();
        r.Relic<LeafyPoultice>();
        r.Relic<PrecariousShears>();
    }

    /// <summary>
    /// The offer Arcane Scroll and Hefty Tablet roll their cards with: the character's Rare cards,
    /// each as likely as any other (uniform odds roll no rarity), and no upgrade roll, whose
    /// number is then not drawn either. So one card is one number of the rewards stream.
    /// </summary>
    internal static CardCreationOptions RareCards() =>
        new CardCreationOptions([CardPools.Character], CardCreationSource.Other, CardOddsType.Uniform, static card => card.Rarity == CardRarity.Rare)
            .WithFlags(CardCreationFlags.NoUpgradeRoll);

    /// <summary>The starter cards of a deck, in deck order: the cards of Basic rarity (Strike, Defend, Bash).</summary>
    internal static List<CardInstance> Basics(Run run) => run.State.Deck.Where(card => card.Def.Rarity == CardRarity.Basic).ToList();
}

// ---------- positive, always in Neow's list ----------

/// <summary>
/// Arcane Scroll: at pickup one random Rare card of the character's pool goes into the deck. The
/// card is rolled like a reward card with uniform odds over the Rares and without an upgrade
/// roll: one pick, one number of the player's rewards stream. The pity offset of card rewards
/// does not move.
/// </summary>
public sealed class ArcaneScroll : Relic
{
    public const int Cards = 1;

    public override string Name => "Arcane Scroll";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: add {Cards} random Rare card to your deck.");

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Arcane Scroll's card");
        var cards = CardFactory.CreateForReward(run, Cards, NeowCore.RareCards()).Select(result => result.Card).ToList();
        if (cards.Count > 0) DeckEdits.Add(run, cards[0]);
    }
}

/// <summary>
/// Booming Conch: in an Elite room the turn-start draw of turn 1 is 2 cards larger (the first
/// pass of the hand-draw modifiers), and in "after side turn start" of the player's turn 1,
/// which is after that draw, the player gains 1 energy. It is a gain, so the energy-gain
/// modifiers see it. Nothing in a hallway fight or against a boss.
/// </summary>
public sealed class BoomingConch : Relic
{
    public const int Cards = 2;
    public const int Energy = 1;

    public override string Name => "Booming Conch";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"On the first turn of every Elite fight: draw {Cards} more cards and gain {Energy} energy.");

    public override decimal ModifyHandDraw(Fight f, decimal count) => f.Turn > 1 || f.RoomType != RoomType.Elite ? count : count + Cards;

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(f.Player) && f.Turn <= 1 && f.RoomType == RoomType.Elite) f.GainEnergy(Energy);
    }
}

/// <summary>Golden Pearl: 150 gold at pickup, through the gold hooks like any gold gained.</summary>
public sealed class GoldenPearl : Relic
{
    public const int Gold = 150;

    public override string Name => "Golden Pearl";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: gain {Gold} gold.");

    public override void AfterObtained(RunContext c) => c.GainGold(Gold);
}

/// <summary>
/// Lost Coffer: at pickup a rewards screen with a card reward of 3 cards and one random potion
/// (shown potion first, as every rewards screen sorts its rows). The cards are the character's,
/// rolled on the regular table WITHOUT the pity offset, which they do not move either (the
/// offer is not a fight's): Rare below 0.03, Uncommon below 0.37, else Common. Each card takes
/// a rarity roll, a pick and an upgrade roll, the potion a rarity roll and a pick: eleven
/// numbers of the rewards stream, cards first. Neither row has to be taken.
/// </summary>
public sealed class LostCoffer : Relic
{
    public const int Cards = 3;

    public override string Name => "Lost Coffer";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: a rewards screen with a card reward (1 of {Cards} cards) and a random potion.");

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Lost Coffer's rewards");
        var options = new CardCreationOptions([CardPools.Character], CardCreationSource.Other, CardOddsType.Regular);
        RunRewards.OfferCustom(run, [new CardReward(run, options, Cards), new PotionReward()]);
    }
}

/// <summary>Neow's Torment: at pickup the card Neow's Fury goes into the deck. The relic's tooltip shows the card.</summary>
public sealed class NeowsTorment : Relic, ICardTips
{
    /// <summary>The class name of the card it adds.</summary>
    public const string Card = "NeowsFury";

    public override string Name => "Neow's Torment";

    public override string Describe() => "When picked up: add the card Neow's Fury to your deck.";

    public IReadOnlyList<string> CardTips => [Card];

    public override void AfterObtained(RunContext c) => DeckEdits.AddNew(c.NeedRun("Neow's Torment's card"), Card);
}

/// <summary>
/// New Leaf: at pickup the player chooses 1 card of the deck (any that is not a Quest card and
/// not Eternal) and it is transformed into a random card: one pick on the run's niche stream,
/// not the player's transformations stream. The game transforms the chosen cards one at a time,
/// each with a call of its own; the new card goes to the end of the deck.
/// </summary>
public sealed class NewLeaf : Relic
{
    public const int Cards = 1;

    public override string Name => "New Leaf";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: choose {Cards} card of your deck. It is transformed into a random card.");

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("New Leaf's transform");
        var chosen = CardSelect.FromDeckForTransformation(run, CardSelectPrefs.Transform(Cards));
        for (var n = 0; n < chosen.Count; n++)
            DeckEdits.TransformToRandom(run, chosen[n], run.State.Part<RunWorld>().Streams.Draw(RunStream.Niche, Purpose.Relic(nameof(NewLeaf), n)));
    }
}

/// <summary>
/// Precise Scissors: at pickup the player chooses 1 card to remove from the deck (any that is
/// not Eternal). The choice does not ask for confirmation of its own, so a deck with a single
/// such card loses it without a screen.
/// </summary>
public sealed class PreciseScissors : Relic
{
    public const int Cards = 1;

    public override string Name => "Precise Scissors";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: choose {Cards} card to remove from your deck.");

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Precise Scissors' choice");
        foreach (var card in CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(Cards))) DeckEdits.Remove(run, card);
    }
}

// ---------- positive, one of a coin's pair ----------

/// <summary>Nutritious Oyster: 11 max HP at pickup (which also heals 11).</summary>
public sealed class NutritiousOyster : Relic
{
    public const int MaxHp = 11;

    public override string Name => "Nutritious Oyster";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: gain {MaxHp} max HP.");

    public override void AfterObtained(RunContext c) => c.GainMaxHp(MaxHp);
}

/// <summary>
/// Neow's Talisman: at pickup two starter cards are upgraded: of the deck's cards of Basic
/// rarity, the LAST one that is a Strike and the last one that is a Defend. Which card it is
/// does not depend on whether it can still be upgraded: when that last Strike is upgraded
/// already nothing happens for the Strikes (no earlier one is taken instead), and the same for
/// the Defends. A Strike or Defend of another rarity does not count.
/// </summary>
public sealed class NeowsTalisman : Relic
{
    public override string Name => "Neow's Talisman";

    public override string Describe() => "When picked up: upgrade one of your starter Strikes and one of your starter Defends (the last of each in your deck).";

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Neow's Talisman's upgrades");
        var basics = NeowCore.Basics(run);
        CardInstance?[] cards = [basics.LastOrDefault(card => card.HasTag(CardTags.Strike)), basics.LastOrDefault(card => card.HasTag(CardTags.Defend))];
        foreach (var card in cards)
            if (card is not null) DeckEdits.Upgrade(run, card);
    }
}

/// <summary>
/// Pomander: at pickup the player chooses 1 card of the deck that can still be upgraded, and it
/// is upgraded. The choice does not ask for confirmation of its own, so a deck with a single
/// such card has it upgraded without a screen.
/// </summary>
public sealed class Pomander : Relic
{
    public const int Cards = 1;

    public override string Name => "Pomander";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: choose {Cards} card of your deck to upgrade.");

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Pomander's choice");
        DeckEdits.Upgrade(run, CardSelect.FromDeckForUpgrade(run, CardSelectPrefs.Upgrade(Cards)));
    }
}

// ---------- cursed ----------

/// <summary>
/// Cursed Pearl: at pickup the curse Greed goes into the deck, then the player gains 333 gold,
/// in that order (a relic that hears a card arrive sees the old gold). The relic's tooltip
/// shows Greed, which is Eternal: nothing removes or transforms it.
/// </summary>
public sealed class CursedPearl : Relic, ICardTips
{
    public const int Gold = 333;

    /// <summary>The class name of the curse it adds.</summary>
    public const string Curse = "Greed";

    public override string Name => "Cursed Pearl";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: add the curse Greed to your deck, then gain {Gold} gold.");

    public IReadOnlyList<string> CardTips => [Curse];

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Cursed Pearl's curse");
        DeckEdits.AddCurses(run, [Content.Registry.Card(Curse)]);
        run.GainGold(Gold);
    }
}

/// <summary>
/// Hefty Tablet: at pickup 3 different random Rare cards of the character's pool are rolled
/// (as Arcane Scroll rolls its one: three numbers of the rewards stream, each pick over the
/// Rares not offered yet) and shown in the choose-a-card screen, which can be skipped. Then the
/// chosen card, if there is one, and the curse Injury go into the deck in ONE add, the chosen
/// card first: both are asked "may it go in" and both are put in before anybody hears that
/// either arrived. The relic's tooltip shows Injury.
/// </summary>
public sealed class HeftyTablet : Relic, ICardTips
{
    public const int Cards = 3;

    /// <summary>The class name of the curse it adds.</summary>
    public const string Curse = "Injury";

    public override string Name => "Hefty Tablet";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: choose 1 of {Cards} random Rare cards to add to your deck (you may skip), then add the curse Injury to your deck.");

    public IReadOnlyList<string> CardTips => [Curse];

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Hefty Tablet's cards");
        var offered = CardFactory.CreateForReward(run, Cards, NeowCore.RareCards()).Select(result => result.Card).ToList();
        var chosen = CardSelect.FromChooseACardScreen(run, offered, canSkip: true);
        var injury = run.State.NewCard(Content.Registry.Card(Curse));
        DeckEdits.Add(run, chosen is null ? [injury] : [chosen, injury]);
    }
}

/// <summary>
/// Leafy Poultice: at pickup the player loses 12 max HP (HP above the new maximum is lost as
/// damage first, so a relic that lowers HP loss hears of it, and a player whose maximum was 12
/// or less can die of it). Then two starter cards are transformed into random cards: of the
/// deck's cards of Basic rarity, the FIRST Strike and the first Defend. Both go in ONE transform
/// on the player's transformations stream, the Strike's roll first: both leave the deck, then
/// the new cards go to the end of the deck in the order of the places their originals had.
/// </summary>
public sealed class LeafyPoultice : Relic
{
    public const int MaxHp = 12;

    public override string Name => "Leafy Poultice";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: lose {MaxHp} max HP. One of your starter Strikes and one of your starter Defends (the first of each in your deck) are each transformed into a random card.");

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Leafy Poultice's transform");
        run.LoseMaxHp(MaxHp);
        if (run.IsOver) return;

        var basics = NeowCore.Basics(run);
        var transformations = new List<CardTransformation>();
        if (basics.FirstOrDefault(card => card.HasTag(CardTags.Strike)) is { } strike) transformations.Add(new CardTransformation(strike));
        if (basics.FirstOrDefault(card => card.HasTag(CardTags.Defend)) is { } defend) transformations.Add(new CardTransformation(defend));
        DeckEdits.Transform(run, transformations, n => run.State.Part<RunWorld>().Streams.Draw(RunStream.Transformations, Purpose.Transform(n)));
    }
}

/// <summary>
/// Precarious Shears: at pickup the player chooses 2 cards to remove from the deck (any that are
/// not Eternal; a deck with no more than two such cards loses them without a screen), then
/// takes 16 damage. The damage is unpowered and has no dealer; it is damage, not HP loss: Block
/// would absorb it (there is none outside a fight), a relic that lowers HP loss lowers it, and
/// it can kill.
/// </summary>
public sealed class PrecariousShears : Relic
{
    public const int Cards = 2;
    public const int Damage = 16;

    public override string Name => "Precarious Shears";

    public override string Describe() => string.Create(CultureInfo.InvariantCulture, $"When picked up: choose {Cards} cards to remove from your deck, then take {Damage} damage.");

    public override void AfterObtained(RunContext c)
    {
        var run = c.NeedRun("Precarious Shears' choice");
        foreach (var card in CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(Cards))) DeckEdits.Remove(run, card);
        run.Damage(Damage, Props.NonCardUnpowered);
    }
}
