using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// Ironclad cards, part 2 (cards-2.json): Drum of Battle to Pillage, 29 cards, and the nine
/// powers only they use. Each follows its game class of the same name (v0.107.1): the steps of
/// every Play are in the order of the game's play method, and every number is the game's.
/// None of the 29 is multiplayer only.
/// </summary>
public sealed class Cards2 : IContentModule
{
    public void Register(Registry r)
    {
        r.Power<FeelNoPainPower>();
        r.Power<FlameBarrierPower>();
        r.Power<HellraiserPower>();
        r.Power<InfernoPower>();
        r.Power<JuggernautPower>();
        r.Power<JugglingPower>();
        r.Power<ManglePower>();
        r.Power<NoEnergyGainPower>();
        r.Power<OneTwoPunchPower>();

        DrumOfBattleToFlameBarrier(r);
        ForgottenRitualToInflame(r);
        IronWaveToPillage(r);
    }

    private static void DrumOfBattleToFlameBarrier(Registry r)
    {
        // Drum of Battle: draw 2. It has no Exhaust keyword; the energy (2, upgraded 3) comes when
        // something else exhausts this very card.
        r.Card(new CardDef("DrumOfBattle", "Drum of Battle", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["Cards"] = 2, ["Energy"] = 2 },
            Upgrade = c => c.Add("Energy", 1),
            Text = t => $"Draw {TextContext.Count(t.N("Cards"), "card")}. Whenever this card is Exhausted, gain {t.N("Energy")} energy.",
            Play = c => c.Draw(c.V("Cards")),
            Behaviour = () => new DrumOfBattleBehaviour(),
        });

        // Evil Eye: 8 Block (11); a second, separate gain of the same if any card was exhausted
        // earlier this turn. The question is asked once, before the first gain.
        r.Card(new CardDef("EvilEye", "Evil Eye", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["Block"] = 8 },
            Upgrade = c => c.Add("Block", 3),
            Text = t => $"Gain {t.Block()} Block. If a card was Exhausted this turn, gain {t.Block()} Block again.",
            Play = c =>
            {
                var gains = c.F.History.WasCardExhaustedThisTurn(c.F) ? 2 : 1;
                for (var i = 0; i < gains; i++) c.GainBlock(c.V("Block"));
            },
        });

        // Expect a Fight: energy equal to the Attacks in hand (0 + 1 each), then no more energy
        // gains this turn. The power lands after the gain, so it does not stop this one.
        // Upgraded: costs 1. The Energy number is the game's display-only 0.
        r.Card(new CardDef("ExpectAFight", "Expect a Fight", CardType.Skill, CardRarity.Uncommon, 2, TargetType.Self)
        {
            Vars = { ["Energy"] = 0, ["CalculationBase"] = 0, ["CalculationExtra"] = 1 },
            Upgrade = c => c.UpgradeCostBy(-1),
            Text = t =>
            {
                var now = t.F is { InProgress: true } f && t.Pile == PileType.Hand
                    ? string.Create(CultureInfo.InvariantCulture, $" (now {(int)ExpectedEnergy(f, t.Card)})")
                    : "";
                return $"Gain {t.N("CalculationExtra")} energy for each Attack in your hand{now}. You cannot gain energy for the rest of this turn.";
            },
            Play = c =>
            {
                c.GainEnergy(ExpectedEnergy(c.F, c.Card));
                c.ApplyToSelf<NoEnergyGainPower>(1);
            },
        });

        // Feed: 10 damage (12). If that hit kills and the kill counts as Fatal (decided before
        // the hit: never for a Minion), max HP goes up by 3 (4), which also heals that much.
        // Never made by random generation.
        r.Card(new CardDef("Feed", "Feed", CardType.Attack, CardRarity.Rare, 1, TargetType.AnyEnemy)
        {
            Keywords = CardKeywords.Exhaust,
            CanBeGeneratedInCombat = false,
            Vars = { ["Damage"] = 10, ["MaxHp"] = 3 },
            Upgrade = c =>
            {
                c.Add("Damage", 2);
                c.Add("MaxHp", 1);
            },
            Text = t => $"Deal {t.Damage()} damage. If this kills an enemy that is not a Minion, gain {t.N("MaxHp")} Max HP.",
            Play = c =>
            {
                var fatal = c.Target is { } target && target.Powers.All(p => p.ShouldOwnerDeathTriggerFatal);
                var attack = c.Attack(c.V("Damage")).Execute();
                if (fatal && attack.KilledAny) c.F.GainMaxHp(c.Owner, c.V("MaxHp"));
            },
        });

        // Feel No Pain: the power, 3 (4).
        r.Card(new CardDef("FeelNoPain", "Feel No Pain", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["Power"] = 3 },
            Upgrade = c => c.Add("Power", 1),
            Text = t => $"Whenever a card is Exhausted, gain {t.N("Power")} Block.",
            Play = c => c.ApplyToSelf<FeelNoPainPower>(c.V("Power")),
        });

        // Fiend Fire: exhausts the hand as it is now, one card at a time (each card's triggers
        // finish before the next), then hits the target once per card that was in it, 7 each (10).
        // The hit count is fixed before the exhausting starts.
        r.Card(new CardDef("FiendFire", "Fiend Fire", CardType.Attack, CardRarity.Rare, 2, TargetType.AnyEnemy)
        {
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Damage"] = 7 },
            Upgrade = c => c.Add("Damage", 3),
            Text = t => $"Exhaust all cards in your hand. Deal {t.Damage()} damage for each of them.",
            Play = c =>
            {
                var hand = c.F.Hand.ToList();
                foreach (var card in hand) c.F.Exhaust(card);
                c.Attack(c.V("Damage")).Hits(hand.Count).Execute();
            },
        });

        // Fight Me!: 5 damage twice (6), then 3 Strength to the player (4), then 1 Strength to
        // the target. The Strength comes after the hits. A positive amount is a Buff, so
        // Artifact on the enemy does not stop it; a dead enemy gets none.
        r.Card(new CardDef("FightMe", "Fight Me!", CardType.Attack, CardRarity.Uncommon, 2, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 5, ["Repeat"] = 2, ["StrengthPower"] = 3, ["EnemyStrength"] = 1 },
            Upgrade = c =>
            {
                c.Add("Damage", 1);
                c.Add("StrengthPower", 1);
            },
            Text = t => $"Deal {t.Damage()} damage {t.N("Repeat")} times. Gain {t.N("StrengthPower")} Strength. The enemy gains {t.N("EnemyStrength")} Strength.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Hits((int)c.V("Repeat")).Execute();
                c.ApplyToSelf<StrengthPower>(c.V("StrengthPower"));
                c.Apply<StrengthPower>(c.Target, c.V("EnemyStrength"));
            },
        });

        // Flame Barrier: 12 Block (16), then the power, 4 (6).
        r.Card(new CardDef("FlameBarrier", "Flame Barrier", CardType.Skill, CardRarity.Uncommon, 2, TargetType.Self)
        {
            Vars = { ["Block"] = 12, ["DamageBack"] = 4 },
            Upgrade = c =>
            {
                c.Add("Block", 4);
                c.Add("DamageBack", 2);
            },
            Text = t => $"Gain {t.Block()} Block. Until your next turn, whenever an enemy hits you with an attack, deal {t.N("DamageBack")} damage back to it.",
            Play = c =>
            {
                c.GainBlock(c.V("Block"));
                c.ApplyToSelf<FlameBarrierPower>(c.V("DamageBack"));
            },
        });
    }

    private static void ForgottenRitualToInflame(Registry r)
    {
        // Forgotten Ritual: 3 energy (4) if a card was exhausted earlier this turn. Its own
        // exhaust comes after the effect, so it never counts for itself.
        r.Card(new CardDef("ForgottenRitual", "Forgotten Ritual", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Energy"] = 3 },
            Upgrade = c => c.Add("Energy", 1),
            Text = t => $"If a card was Exhausted this turn, gain {t.N("Energy")} energy.",
            Play = c =>
            {
                if (c.F.History.WasCardExhaustedThisTurn(c.F)) c.GainEnergy(c.V("Energy"));
            },
        });

        // Havoc: plays the top card of the draw pile for free and exhausts it (a Power card just
        // leaves the fight as usual). Upgraded: costs 0.
        r.Card(new CardDef("Havoc", "Havoc", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
        {
            Upgrade = c => c.UpgradeCostBy(-1),
            Text = _ => "Play the top card of your draw pile for free and Exhaust it.",
            Play = c => c.F.AutoPlayFromDrawPile(1, PilePosition.Top, forceExhaust: true),
        });

        // Headbutt: 9 damage (12), then one card of the discard pile goes on top of the draw
        // pile (the player picks; one candidate is taken without asking). Headbutt itself is in
        // the Play pile meanwhile.
        r.Card(new CardDef("Headbutt", "Headbutt", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 9 },
            Upgrade = c => c.Add("Damage", 3),
            Text = t => $"Deal {t.Damage()} damage. Put a card from your discard pile on top of your draw pile.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                var picked = c.F.ChooseFromPile(PileType.Discard, "Choose a card to put on top of your draw pile.", 1, 1).FirstOrDefault();
                if (picked is not null) c.F.MoveCard(picked, PileType.Draw, PilePosition.Top);
            },
        });

        // Hellraiser: the power. Upgraded: costs 1.
        r.Card(new CardDef("Hellraiser", "Hellraiser", CardType.Power, CardRarity.Rare, 2, TargetType.Self)
        {
            Upgrade = c => c.UpgradeCostBy(-1),
            Text = _ => "Whenever you draw a card with Strike in its name, it is played for free at a random enemy.",
            Play = c => c.ApplyToSelf<HellraiserPower>(1),
        });

        // Hemokinesis: lose 2 HP first, then 15 damage (20).
        r.Card(new CardDef("Hemokinesis", "Hemokinesis", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy)
        {
            Vars = { ["HpLoss"] = 2, ["Damage"] = 15 },
            Upgrade = c => c.Add("Damage", 5),
            Text = t => $"Lose {t.N("HpLoss")} HP. Deal {t.Damage()} damage.",
            Play = c =>
            {
                c.LoseHp(c.V("HpLoss"));
                c.Attack(c.V("Damage")).Execute();
            },
        });

        // Howl from Beyond: 16 damage to every enemy (21). No Exhaust keyword: when something
        // else exhausted it, it plays itself for free as the turn ends and lands in the discard pile.
        r.Card(new CardDef("HowlFromBeyond", "Howl from Beyond", CardType.Attack, CardRarity.Uncommon, 3, TargetType.AllEnemies)
        {
            Vars = { ["Damage"] = 16 },
            Upgrade = c => c.Add("Damage", 5),
            Text = t => $"Deal {t.Damage()} damage to ALL enemies. At the end of your turn, if this card is in your exhaust pile, it is played for free and goes to your discard pile.",
            Play = c => c.Attack(c.V("Damage")).Execute(),
            Behaviour = () => new HowlFromBeyondBehaviour(),
        });

        // Impervious: 30 Block (40).
        r.Card(new CardDef("Impervious", "Impervious", CardType.Skill, CardRarity.Rare, 2, TargetType.Self)
        {
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Block"] = 30 },
            Upgrade = c => c.Add("Block", 10),
            Text = t => $"Gain {t.Block()} Block.",
            Play = c => c.GainBlock(c.V("Block")),
        });

        // Infernal Blade: one random Attack of the pool, free until the turn ends or it is played,
        // into the hand. The game shuffles the whole candidate list on the card-generation stream
        // and takes the first, so the number of draws is the list's length minus 1. Upgraded: costs 0.
        r.Card(new CardDef("InfernalBlade", "Infernal Blade", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Keywords = CardKeywords.Exhaust,
            Upgrade = c => c.UpgradeCostBy(-1),
            Text = _ => "Add a random Attack to your hand. It costs 0 this turn.",
            Play = c =>
            {
                var candidates = Content.Registry.IroncladPool.Where(d => d.Type == CardType.Attack && d.IsGeneratableInCombat).ToList();
                c.F.Rng.CombatCardGeneration.Shuffle(candidates);
                if (candidates.Count == 0) return;
                var card = c.F.CreateCard(candidates[0]);
                card.SetCostThisTurnOrUntilPlayed(0);
                c.F.AddGeneratedCard(card, PileType.Hand, PilePosition.Bottom, c.Owner);
            },
        });

        // Inferno: the power, 6 (9), and one more point of its start-of-turn self-damage per play.
        r.Card(new CardDef("Inferno", "Inferno", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["InfernoPower"] = 6 },
            Upgrade = c => c.Add("InfernoPower", 3),
            Text = t => $"At the start of your turn, lose 1 HP. Whenever you lose HP on your turn, deal {t.N("InfernoPower")} damage to ALL enemies.",
            Play = c => c.ApplyToSelf<InfernoPower>(c.V("InfernoPower"))?.IncrementSelfDamage(),
        });

        // Inflame: 2 Strength (3).
        r.Card(new CardDef("Inflame", "Inflame", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["StrengthPower"] = 2 },
            Upgrade = c => c.Add("StrengthPower", 1),
            Text = t => $"Gain {t.N("StrengthPower")} Strength.",
            Play = c => c.ApplyToSelf<StrengthPower>(c.V("StrengthPower")),
        });
    }

    private static void IronWaveToPillage(Registry r)
    {
        // Iron Wave: 5 Block first (7), then 5 damage (7).
        r.Card(new CardDef("IronWave", "Iron Wave", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 5, ["Block"] = 5 },
            Upgrade = c =>
            {
                c.Add("Damage", 2);
                c.Add("Block", 2);
            },
            Text = t => $"Gain {t.Block()} Block. Deal {t.Damage()} damage.",
            Play = c =>
            {
                c.GainBlock(c.V("Block"));
                c.Attack(c.V("Damage")).Execute();
            },
        });

        // Juggernaut: the power, 6 (8).
        r.Card(new CardDef("Juggernaut", "Juggernaut", CardType.Power, CardRarity.Rare, 2, TargetType.Self)
        {
            Vars = { ["JuggernautPower"] = 6 },
            Upgrade = c => c.Add("JuggernautPower", 2),
            Text = t => $"Whenever you gain Block, deal {t.N("JuggernautPower")} damage to a random enemy.",
            Play = c => c.ApplyToSelf<JuggernautPower>(c.V("JuggernautPower")),
        });

        // Juggling: the power. Upgraded: Innate, nothing else changes.
        r.Card(new CardDef("Juggling", "Juggling", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Upgrade = c => c.Keywords |= CardKeywords.Innate,
            Text = _ => "Whenever you play your third Attack in a turn, add a copy of it to your hand.",
            Play = c => c.ApplyToSelf<JugglingPower>(1),
        });

        // Mangle: 15 damage (20), then the target loses 10 Strength (15) until the end of its turn.
        r.Card(new CardDef("Mangle", "Mangle", CardType.Attack, CardRarity.Rare, 3, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 15, ["StrengthLoss"] = 10 },
            Upgrade = c =>
            {
                c.Add("Damage", 5);
                c.Add("StrengthLoss", 5);
            },
            Text = t => $"Deal {t.Damage()} damage. The enemy loses {t.N("StrengthLoss")} Strength until the end of its turn.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.Apply<ManglePower>(c.Target, c.V("StrengthLoss"));
            },
        });

        // Molten Fist: 10 damage (14), then a target that is still alive gets as much Vulnerable
        // again as it has after the hit.
        r.Card(new CardDef("MoltenFist", "Molten Fist", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
        {
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Damage"] = 10 },
            Upgrade = c => c.Add("Damage", 4),
            Text = t => $"Deal {t.Damage()} damage. Double the enemy's Vulnerable.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                var vulnerable = c.Target is { IsAlive: true } target ? target.PowerAmount<VulnerablePower>() : 0;
                if (vulnerable > 0) c.Apply<VulnerablePower>(c.Target, vulnerable);
            },
        });

        // Not Yet: heal 10 (13). No play condition in this build. Never made by random generation.
        r.Card(new CardDef("NotYet", "Not Yet", CardType.Skill, CardRarity.Rare, 2, TargetType.Self)
        {
            Keywords = CardKeywords.Exhaust,
            CanBeGeneratedInCombat = false,
            Vars = { ["Heal"] = 10 },
            Upgrade = c => c.Add("Heal", 3),
            Text = t => $"Heal {t.N("Heal")} HP.",
            Play = c => c.F.Heal(c.Owner, c.V("Heal")),
        });

        // Offering: lose 6 HP, gain 2 energy, draw 3 (5), in that order.
        r.Card(new CardDef("Offering", "Offering", CardType.Skill, CardRarity.Rare, 0, TargetType.Self)
        {
            Keywords = CardKeywords.Exhaust,
            Vars = { ["HpLoss"] = 6, ["Energy"] = 2, ["Cards"] = 3 },
            Upgrade = c => c.Add("Cards", 2),
            Text = t => $"Lose {t.N("HpLoss")} HP. Gain {t.N("Energy")} energy. Draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c =>
            {
                c.LoseHp(c.V("HpLoss"));
                c.GainEnergy(c.V("Energy"));
                c.Draw(c.V("Cards"));
            },
        });

        // One-Two Punch: the power, 1 (2).
        r.Card(new CardDef("OneTwoPunch", "One-Two Punch", CardType.Skill, CardRarity.Rare, 1, TargetType.Self)
        {
            Vars = { ["Attacks"] = 1 },
            Upgrade = c => c.Add("Attacks", 1),
            Text = t => t.N("Attacks") == 1
                ? "The next Attack you play this turn is played twice."
                : $"The next {t.N("Attacks")} Attacks you play this turn are each played twice.",
            Play = c => c.ApplyToSelf<OneTwoPunchPower>(c.V("Attacks")),
        });

        // Pact's End: always playable; 17 damage to every enemy (23) only with 3 or more cards
        // in the exhaust pile.
        r.Card(new CardDef("PactsEnd", "Pact's End", CardType.Attack, CardRarity.Rare, 0, TargetType.AllEnemies)
        {
            Vars = { ["Damage"] = 17, ["Cards"] = 3 },
            Upgrade = c => c.Add("Damage", 6),
            Text = t => $"If your exhaust pile has {t.N("Cards")} or more cards, deal {t.Damage()} damage to ALL enemies.",
            Play = c =>
            {
                if (c.F.ExhaustPile.Count >= c.V("Cards")) c.Attack(c.V("Damage")).Execute();
            },
        });

        // Perfected Strike: 6 + 2 (3) for every card with the Strike tag in any of the five
        // piles, itself included (it is in the Play pile while it resolves).
        r.Card(new CardDef("PerfectedStrike", "Perfected Strike", CardType.Attack, CardRarity.Common, 2, TargetType.AnyEnemy)
        {
            Tags = CardTags.Strike,
            Vars = { ["CalculationBase"] = 6, ["ExtraDamage"] = 2 },
            Upgrade = c => c.Add("ExtraDamage", 1),
            DamageMultiplier = (f, _, _) => f.AllCards.Count(card => card.HasTag(CardTags.Strike)),
            Text = t => $"Deal {t.CalculatedDamage()} damage ({t.N("CalculationBase")}, plus {t.N("ExtraDamage")} for each of your cards with Strike in its name, in any pile).",
            Play = c => c.CalculatedAttack().Execute(),
        });

        // Pillage: 6 damage (9), then single draws for as long as the card just drawn is an
        // Attack and the hand is not full. At least one draw is always tried.
        r.Card(new CardDef("Pillage", "Pillage", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 6 },
            Upgrade = c => c.Add("Damage", 3),
            Text = t => $"Deal {t.Damage()} damage. Draw a card; if it is an Attack, draw another, and so on.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                CardInstance? drawn;
                do drawn = c.F.Draw(1).FirstOrDefault();
                while (drawn is { Type: CardType.Attack } && c.F.Hand.Count < Fight.HandLimit);
            },
        });
    }

    /// <summary>Expect a Fight's calculated energy: its base plus its extra for every Attack in hand.</summary>
    private static decimal ExpectedEnergy(Fight f, CardInstance card) =>
        card.Get("CalculationBase") + card.Get("CalculationExtra") * f.Hand.Count(h => h.Type == CardType.Attack);

    /// <summary>
    /// Drum of Battle's own hook: when this very card reaches the exhaust pile it gives its
    /// energy once per run a play of the card would get (the play-count hooks are asked, which
    /// spends a one-shot doubling charge exactly as a play would). The gain goes through the
    /// usual energy modifiers, so No Energy Gain makes it 0.
    /// </summary>
    private sealed class DrumOfBattleBehaviour : CardBehaviour
    {
        public override void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal)
        {
            if (!ReferenceEquals(card, Card)) return;
            var playCount = f.GeneratePlayCount(Card, null);
            for (var i = 0; i < playCount; i++) f.GainEnergy(Card.Get("Energy"));
        }
    }

    /// <summary>Howl from Beyond's own hook: right after the turn was ended, a Howl that sits in
    /// the exhaust pile is auto-played. Its result pile is then the discard pile.</summary>
    private sealed class HowlFromBeyondBehaviour : CardBehaviour
    {
        public override void AfterAutoPostPlayPhaseEntered(Fight f)
        {
            if (Card.Pile == PileType.Exhaust) f.AutoPlay(Card);
        }
    }
}

/// <summary>Feel No Pain: whenever a card of its owner is exhausted, for any reason (Ethereal
/// too), the owner gains Block equal to the amount. The Block is unpowered: no Dexterity, no Frail.</summary>
public sealed class FeelNoPainPower : Power
{
    public override string Name => "Feel No Pain";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Whenever a card is Exhausted, gain {Amount} Block.");

    public override void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal)
    {
        // Every card of the fight belongs to the player.
        if (Owner.IsPlayer) f.GainBlock(Owner, Amount, Props.NonCardUnpowered);
    }
}

/// <summary>
/// Flame Barrier: every powered attack hit its owner receives from a known dealer and survives
/// (blocked or not) sends the amount back to that dealer as unpowered damage. It goes at the
/// end of the other side's turn, so on the player it lasts through one enemy turn.
/// </summary>
public sealed class FlameBarrierPower : Power
{
    public override string Name => "Flame Barrier";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Whenever an attack hits you, deal {Amount} damage back to the attacker. Ends after the enemy turn.");

    public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (ReferenceEquals(target, Owner) && dealer is not null && props.IsPoweredAttack())
            f.Damage(dealer, Amount, Props.NonCardUnpowered, Owner);
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (Owner.Side != side) f.RemovePower(this);
    }
}

/// <summary>
/// Hellraiser: a card with the Strike tag is auto-played the moment it is drawn, before the
/// next card of the same draw. The game's safety cap: a counter goes up on every Strike drawn
/// while every hittable enemy shows infinite HP (which an empty list satisfies), and from 9 on
/// such Strikes stay in hand; it is reset by a Strike drawn while some hittable enemy has
/// ordinary HP, and at the end of the owner's turn. No creature in scope ever shows infinite
/// HP (only the Waterfall Giant and Hardened Shell set that), so here the condition is
/// exactly "no enemy can be hit".
/// </summary>
public sealed class HellraiserPower : Power
{
    private const int InfiniteAutoPlayCap = 9;

    private int _infiniteAutoPlaysThisTurn;

    public override string Name => "Hellraiser";
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override string Describe() => "Whenever you draw a card with Strike in its name, it is played for free at a random enemy.";

    public override void AfterCardDrawnEarly(Fight f, CardInstance card, bool fromHandDraw)
    {
        if (!Owner.IsPlayer || !card.HasTag(CardTags.Strike)) return;

        var play = true;
        if (f.HittableEnemies.Count == 0)
        {
            if (_infiniteAutoPlaysThisTurn >= InfiniteAutoPlayCap) play = false;
            _infiniteAutoPlaysThisTurn++;
        }
        else
        {
            _infiniteAutoPlaysThisTurn = 0;
        }
        if (play) f.AutoPlay(card);
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) _infiniteAutoPlaysThisTurn = 0;
    }
}

/// <summary>
/// Inferno: at the start of its owner's turn, after the hand draw, the owner loses
/// <see cref="SelfDamage"/> HP (Block ignored); and whenever the owner actually loses HP while
/// it is its own side's turn, every hittable enemy takes the amount as one unpowered damage
/// call. The first triggers the second. The card raises <see cref="SelfDamage"/> by 1 per play.
/// </summary>
public sealed class InfernoPower : Power
{
    /// <summary>HP lost at the start of each turn: 0 on a new power, plus 1 for every Inferno played.</summary>
    public int SelfDamage { get; private set; }

    public override string Name => "Inferno";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"At the start of your turn, lose {SelfDamage} HP. Whenever you lose HP on your turn, deal {Amount} damage to ALL enemies.");

    public void IncrementSelfDamage() => SelfDamage++;

    public override void AfterPlayerTurnStart(Fight f)
    {
        if (Owner.IsPlayer) f.Damage(Owner, SelfDamage, Props.NonCardHpLoss, Owner);
    }

    public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage <= 0 || f.CurrentSide != Owner.Side) return;
        f.Damage(f.HittableEnemies, Amount, Props.NonCardUnpowered, Owner);
    }
}

/// <summary>Juggernaut: after every Block gain of its owner whose final amount is above 0 (even a
/// fraction that added nothing), one random hittable enemy takes the amount as unpowered
/// damage. The target is one draw from the CombatTargets stream, also with one enemy.</summary>
public sealed class JuggernautPower : Power
{
    public override string Name => "Juggernaut";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Whenever you gain Block, deal {Amount} damage to a random enemy.");

    public override void AfterBlockGained(Fight f, Creature creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
        if (amount <= 0m || !ReferenceEquals(creature, Owner)) return;
        var enemies = f.HittableEnemies;
        if (enemies.Count == 0) return;
        var target = f.Rng.CombatTargets.NextItem(enemies)!;
        f.Damage(target, Amount, Props.NonCardUnpowered, Owner);
    }
}

/// <summary>
/// Juggling: counts the runs of Attack effects this turn (a new power starts from the Attack
/// plays already started this turn). The run that takes the count to exactly 3 puts as many
/// copies of that card as the amount into the hand; later ones do nothing. The count is reset
/// at the end of its owner's turn.
/// </summary>
public sealed class JugglingPower : Power
{
    private const int TriggerOn = 3;

    private int _attacksPlayedThisTurn;

    public override string Name => "Juggling";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "Whenever you play your third Attack in a turn, add a copy of it to your hand."
        : string.Create(CultureInfo.InvariantCulture, $"Whenever you play your third Attack in a turn, add {Amount} copies of it to your hand.");

    public override void AfterApplied(Fight f, Creature? applier, CardInstance? cardSource) =>
        _attacksPlayedThisTurn = f.History.CardPlaysStartedThisTurn(f, CardType.Attack);

    public override void AfterCardPlayed(Fight f, CardPlay play)
    {
        if (!Owner.IsPlayer || play.Card.Type != CardType.Attack) return;
        _attacksPlayedThisTurn++;
        if (_attacksPlayedThisTurn != TriggerOn) return;
        for (var i = 0; i < Amount; i++)
            f.AddGeneratedCard(f.CloneCard(play.Card), PileType.Hand, PilePosition.Bottom, Owner);
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) _attacksPlayedThisTurn = 0;
    }
}

/// <summary>Mangle's Strength loss: a temporary Strength power of the lowering kind. It is shown
/// under the card's name. Artifact stops all of it.</summary>
public sealed class ManglePower : TemporaryStrengthPower
{
    public override string Name => "Mangle";
    protected override bool IsPositive => false;
}

/// <summary>No Energy Gain (Expect a Fight): every energy gain of its owner through the
/// gain-energy command becomes 0. Removed at the end of its owner's own turn, so the next
/// turn's energy is never touched.</summary>
public sealed class NoEnergyGainPower : Power
{
    public override string Name => "No Energy Gain";
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override string Describe() => "You cannot gain energy this turn.";

    public override decimal ModifyEnergyGain(Fight f, decimal amount) => Owner.IsPlayer ? 0m : amount;

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.RemovePower(this);
    }
}

/// <summary>One-Two Punch: the next Attack of its owner that starts to be played, by hand or by
/// an effect, runs one more time, and the power loses a stack. What is left goes at the end
/// of its owner's turn.</summary>
public sealed class OneTwoPunchPower : Power
{
    public override string Name => "One-Two Punch";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "The next Attack you play this turn is played twice."
        : string.Create(CultureInfo.InvariantCulture, $"The next {Amount} Attacks you play this turn are each played twice.");

    public override int ModifyCardPlayCount(Fight f, CardInstance card, Creature? target, int playCount) =>
        Owner.IsPlayer && card.Type == CardType.Attack ? playCount + 1 : playCount;

    public override void AfterModifyingCardPlayCount(Fight f, CardInstance card) => f.Decrement(this);

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.RemovePower(this);
    }
}
