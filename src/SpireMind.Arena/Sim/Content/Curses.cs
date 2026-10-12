using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// The 18 cards of the game's curse pool and Neow's Fury (run mode: chunk 11 of the plan
/// sts2-notes/run-mode-v0.107.1/plan.md), each read from its class in
/// MegaCrit.Sts2.Core.Models.Cards. No curse can be upgraded. A single fight never holds one of
/// these: a run gets them from events and from Neow's relics, and carries them from fight to fight.
/// <para>
/// Thirteen curses are given by a fixed source or by Neow's Bones (Clumsy, Debt, Decay, Doubt,
/// Greed, Guilty, Injury, Normality, Poor Sleep, Regret, Shame, Spore Mind, Writhe). The other
/// five (Ascender's Bane, Bad Luck, Curse of the Bell, Enthralled, Folly) reach an act 1 run
/// only when a curse is transformed, which gives any other of the eighteen with equal chance.
/// With all eighteen here that pick is always the game's own, never drawn again.
/// </para>
/// <para>
/// Guilty is the one card of the game in reach that acts as the card in the DECK: it counts
/// the fights that end while it is in the deck and leaves the deck after the fifth. Its hook
/// class carries <see cref="IListensInDeck"/>; the copy of it in a fight's piles hears the same
/// hook and does nothing, as in the game (Guilty.cs tests its pile).
/// </para>
/// </summary>
public sealed class Curses : IContentModule
{
    /// <summary>How many fights Guilty stays for (the game's Guilty.maxCombats).</summary>
    public const int GuiltyCombats = 5;

    /// <summary>How many cards a turn Normality allows while it is in the hand (the game's private constant in Normality.cs).</summary>
    public const int NormalityCardsPerTurn = 3;

    public void Register(Registry r)
    {
        KeywordsOnly(r);
        EndOfTurnInHand(r);
        WithHooks(r);
        NeowsFury(r);
        NotBuiltYet(r);
    }

    private static CardDef Curse(string className, string name, CardKeywords keywords, int cost = CardDef.NoCost) =>
        new(className, name, CardType.Curse, CardRarity.Curse, cost, TargetType.None) { MaxUpgradeLevel = 0, Keywords = keywords };

    // ---------- curses that are keywords and nothing else ----------

    private static void KeywordsOnly(Registry r)
    {
        // Ascender's Bane: Unplayable, Ethereal, Eternal. (The game puts it into the starting deck at an ascension
        // level of that name; at ascension 0 it can only come out of a transform.)
        r.Card(Curse("AscendersBane", "Ascender's Bane", CardKeywords.Eternal | CardKeywords.Unplayable | CardKeywords.Ethereal));

        // Clumsy: Unplayable, Ethereal.
        r.Card(Curse("Clumsy", "Clumsy", CardKeywords.Unplayable | CardKeywords.Ethereal));

        // Curse of the Bell: Unplayable, Eternal.
        r.Card(Curse("CurseOfTheBell", "Curse of the Bell", CardKeywords.Eternal | CardKeywords.Unplayable));

        // Folly: Unplayable, Innate, Ethereal, Eternal.
        r.Card(Curse("Folly", "Folly", CardKeywords.Unplayable | CardKeywords.Eternal | CardKeywords.Innate | CardKeywords.Ethereal));

        // Greed: Unplayable, Eternal: it can be neither removed nor transformed while it is in the deck.
        r.Card(Curse("Greed", "Greed", CardKeywords.Eternal | CardKeywords.Unplayable));

        // Injury: Unplayable.
        r.Card(Curse("Injury", "Injury", CardKeywords.Unplayable));

        // Poor Sleep: Unplayable, Retain: it is not discarded with the hand at the end of the turn.
        r.Card(Curse("PoorSleep", "Poor Sleep", CardKeywords.Unplayable | CardKeywords.Retain));

        // Spore Mind: cost 1, playable, does nothing when played, Exhaust.
        r.Card(Curse("SporeMind", "Spore Mind", CardKeywords.Exhaust, cost: 1));

        // Writhe: Unplayable, Innate.
        r.Card(Curse("Writhe", "Writhe", CardKeywords.Innate | CardKeywords.Unplayable));
    }

    // ---------- curses that act when they are in the hand at the end of the turn ----------

    /// <summary>The curse hurts its holder with the holder as dealer and the card as source, ignoring Block
    /// (the game's Damage with the flags Unblockable, Unpowered, Move: Bad Luck, Regret).</summary>
    private static void LoseHp(Fight f, CardInstance card, decimal amount) =>
        f.Damage(f.Player, amount, Props.CardHpLoss, f.Player, card);

    /// <summary>
    /// Doubt and Shame: a duration debuff on the holder, with nobody as its applier and the card as
    /// its source. When the holder did not have the power before and it landed, the game's card
    /// sets the skip-next-tick flag itself, on top of what applying a new debuff to the player
    /// already does; written out the same here.
    /// </summary>
    private static void GainDebuff<T>(Fight f, CardInstance card, string number) where T : Power, new()
    {
        var hadIt = f.Player.HasPower<T>();
        var power = f.ApplyPower<T>(f.Player, card.Get(number), null, card);
        if (power is not null && !hadIt) power.SkipNextDurationTick = true;
    }

    private static void EndOfTurnInHand(Registry r)
    {
        // Bad Luck: Unplayable, Eternal. In hand at end of turn: lose 13 HP (ignores Block).
        r.Card(new CardDef("BadLuck", "Bad Luck", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Eternal | CardKeywords.Unplayable,
            Vars = { ["HpLoss"] = 13 },
            Text = t => $"At the end of your turn, if this is in your hand, lose {t.N("HpLoss")} HP.",
            TurnEndInHand = (f, card) => LoseHp(f, card, card.Get("HpLoss")),
        });

        // Debt: Unplayable. In hand at end of turn: lose 10 gold, or all of it when the player has less.
        r.Card(new CardDef("Debt", "Debt", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["Gold"] = 10 },
            Text = t => $"At the end of your turn, if this is in your hand, lose {t.N("Gold")} gold (all of it if you have less).",
            TurnEndInHand = (f, card) => f.LoseGold(Math.Min((int)card.Get("Gold"), f.Gold)),
        });

        // Decay: Unplayable. In hand at end of turn: 2 damage, blockable, not modified.
        r.Card(new CardDef("Decay", "Decay", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["Damage"] = Var.Damage(2, Props.CardUnpowered) },
            Text = t => $"At the end of your turn, if this is in your hand, take {t.Damage()} damage.",
            TurnEndInHand = (f, card) => f.Damage(f.Player, card.Get("Damage"), card.VarDef("Damage").Props, f.Player, card),
        });

        // Doubt: Unplayable. In hand at end of turn: 1 Weak on the holder.
        r.Card(new CardDef("Doubt", "Doubt", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["WeakPower"] = 1 },
            Text = t => $"At the end of your turn, if this is in your hand, gain {t.N("WeakPower")} Weak.",
            TurnEndInHand = (f, card) => GainDebuff<WeakPower>(f, card, "WeakPower"),
        });

        // Regret: Unplayable. In hand at end of turn: lose 1 HP (ignores Block) per card that was in the
        // hand when the turn's end began. The card notes that number in "before side turn end", so cards
        // that leave the hand afterwards (an Ethereal card exhausted, an earlier end-of-turn card) still count.
        r.Card(new CardDef("Regret", "Regret", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Text = _ => "At the end of your turn, if this is in your hand, lose 1 HP for each card in your hand (this one counts; counted before any card leaves the hand).",
            Behaviour = () => new RegretBehaviour(),
            TurnEndInHand = (f, card) =>
            {
                var regret = (RegretBehaviour)card.Behaviour!;
                LoseHp(f, card, regret.CardsInHand);
                regret.CardsInHand = 0;
            },
        });

        // Shame: Unplayable. In hand at end of turn: 1 Frail on the holder.
        r.Card(new CardDef("Shame", "Shame", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["Frail"] = 1 },
            Text = t => $"At the end of your turn, if this is in your hand, gain {t.N("Frail")} Frail.",
            TurnEndInHand = (f, card) => GainDebuff<FrailPower>(f, card, "Frail"),
        });
    }

    /// <summary>Regret's memory: the size of the hand when the player's turn began to end.</summary>
    private sealed class RegretBehaviour : CardBehaviour
    {
        public int CardsInHand;

        public override void BeforeSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            if (!participants.Contains(f.Player) || Card.Pile != PileType.Hand) return;
            CardsInHand = f.Hand.Count;
        }
    }

    // ---------- curses with a hook of their own ----------

    private static void WithHooks(Registry r)
    {
        // Enthralled: cost 2, playable, does nothing when played, Eternal. While it is in the hand no other
        // card can be played by hand; cards an effect plays are not held back.
        r.Card(new CardDef("Enthralled", "Enthralled", CardType.Curse, CardRarity.Curse, 2, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Eternal,
            Text = _ => "While this is in your hand, you cannot play any other card until you have played this one.",
            Behaviour = () => new EnthralledBehaviour(),
        });

        // Guilty: Unplayable. It counts the fights that end while it is in the deck and leaves the deck after the fifth.
        r.Card(new CardDef("Guilty", "Guilty", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["Combats"] = GuiltyCombats },
            Text = t => $"Leaves your deck for good after {TextContext.Count(t.N("Combats"), "more fight")}.",
            Behaviour = () => new GuiltyBehaviour(),
        });

        // Normality: Unplayable. While it is in the hand, no card can be played (by hand or by an effect) once
        // 3 runs of card effects have started this turn. The two numbers are the game's, which only its text uses.
        r.Card(new CardDef("Normality", "Normality", CardType.Curse, CardRarity.Curse, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["CalculationBase"] = NormalityCardsPerTurn, ["CalculationExtra"] = -1 },
            Text = NormalityText,
            Behaviour = () => new NormalityBehaviour(),
        });
    }

    /// <summary>
    /// As the game's text: the rule, and in a running fight, for a card in a combat pile, how many
    /// plays are left this turn (its calculated number: the base 3 minus 1 per play started this
    /// turn, at most 3 of them). The count shows whether or not the card is in the hand.
    /// </summary>
    private static string NormalityText(TextContext t)
    {
        var rule = string.Create(CultureInfo.InvariantCulture, $"While this is in your hand, no card can be played once {NormalityCardsPerTurn} cards have been played this turn");
        var pile = t.Card.Pile != PileType.None ? t.Card.Pile : t.Pile;
        if (t.F is not { InProgress: true } f || pile is not (PileType.Hand or PileType.Draw or PileType.Discard or PileType.Exhaust or PileType.Play))
            return rule + ".";
        var left = (int)(t.Card.Get("CalculationBase") + t.Card.Get("CalculationExtra") * Math.Min(NormalityCardsPerTurn, f.History.CardPlaysStartedThisTurn(f)));
        return string.Create(CultureInfo.InvariantCulture, $"{rule} ({TextContext.Count(left, "card")} left).");
    }

    /// <summary>
    /// Guilty in the deck. In the game every deck card hears "after combat end" beside its copy
    /// in the fight's piles, and Guilty acts only as the card whose pile is the deck: it counts
    /// the fight, shows what is left in its number, and after the fifth removes itself from the
    /// deck (CardPileCmd.RemoveFromDeck, with its "before card removed" hook).
    /// </summary>
    private sealed class GuiltyBehaviour : CardBehaviour, IListensInDeck
    {
        private int _combatsSeen;

        public override void AfterCombatEnd(Fight f)
        {
            if (Card.Pile != PileType.Deck) return;
            _combatsSeen++;
            Card.Set("Combats", GuiltyCombats - _combatsSeen);
            if (_combatsSeen >= GuiltyCombats && Card.Pile == PileType.Deck) f.RemoveFromDeck(Card);
        }
    }

    /// <summary>Normality in the hand: nothing more may be played once three runs of card effects have started this turn.</summary>
    private sealed class NormalityBehaviour : CardBehaviour
    {
        public override bool ShouldPlay(Fight f, CardInstance card, AutoPlayType autoPlay) =>
            Card.Pile != PileType.Hand || f.History.CardPlaysStartedThisTurn(f) < NormalityCardsPerTurn;
    }

    /// <summary>Enthralled in the hand: only an Enthralled may be played by hand; a card an effect plays is let through.</summary>
    private sealed class EnthralledBehaviour : CardBehaviour
    {
        public override bool ShouldPlay(Fight f, CardInstance card, AutoPlayType autoPlay) =>
            Card.Pile != PileType.Hand || card.Def.ClassName == "Enthralled" || autoPlay != AutoPlayType.None;
    }

    // ---------- Neow's Fury ----------

    private static void NeowsFury(Registry r)
    {
        // Neow's Fury (event pool, Ancient rarity; given by Neow's Torment): cost 1, Attack on one enemy,
        // Exhaust. 10 damage (14 upgraded), then the player may put up to 2 (3) cards of the discard pile
        // into the hand, as many as the hand has room for. Never made by random generation in a fight.
        r.Card(new CardDef("NeowsFury", "Neow's Fury", CardType.Attack, CardRarity.Ancient, 1, TargetType.AnyEnemy)
        {
            Keywords = CardKeywords.Exhaust,
            CanBeGeneratedInCombat = false,
            Vars = { ["Damage"] = 10, ["Cards"] = 2 },
            Upgrade = c => { c.Add("Damage", 4); c.Add("Cards", 1); },
            Text = t => $"Deal {t.Damage()} damage. Then put up to {TextContext.Count(t.N("Cards"), "card")} of your choice from your discard pile into your hand.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                // The card itself is in the Play pile now, so it takes no room in the hand.
                var count = Math.Min((int)c.V("Cards"), Fight.HandLimit - c.F.Hand.Count);
                if (count <= 0) return;
                var prompt = string.Create(CultureInfo.InvariantCulture, $"Choose up to {TextContext.Count(count, "card")} to put into your hand.");
                // Zero to count cards: the screen always has a confirm, and is shown also for a single card.
                c.F.MoveCards(c.F.ChooseFromPile(PileType.Discard, prompt, 0, count), PileType.Hand);
            },
        });
    }

    // ---------- named and left out ----------

    private static void NotBuiltYet(Registry r)
    {
        // The other cards of the event and quest pools that an act 1 Ironclad run can be given
        // (Completeness.EventAndQuestCards). Whoever builds one takes its line out.
        r.Exclude(ContentKind.Card, "Peck", "not built yet: given by the event Wood Carvings, which the run-mode plan builds in chunk 32");
        r.Exclude(ContentKind.Card, "ToricToughness", "not built yet: given by the event Wood Carvings, which the run-mode plan builds in chunk 32");
        r.Exclude(ContentKind.Card, "SpoilsMap", "not built yet: given by the event The Legends Were True, which the run-mode plan builds in chunk 24");
        r.Exclude(ContentKind.Card, "ByrdonisEgg", "not built yet: given by the event Byrdonis Nest, which the run-mode plan builds in chunk 35");
        r.Exclude(ContentKind.Card, "ByrdSwoop", "not built yet: what Byrdonis Egg becomes when it hatches at a rest site (run-mode plan, chunk 35)");
    }
}
