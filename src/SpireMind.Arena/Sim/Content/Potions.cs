using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// The potions an Ironclad can hold, each read from its class in the game's
/// MegaCrit.Sts2.Core.Models.Potions (v0.107.1), with the powers only potions use. Of the 48
/// potions of the Ironclad's pool (the three of his own and the 45 shared ones) 46 are here;
/// the other two are left out by name with the reason, so that nothing is missing by accident.
/// <para>
/// Potion damage and Block are unpowered: Strength, Weak, Vulnerable, Dexterity and Frail do
/// not change them, Block still absorbs the damage, and powers that do not ask whether a hit
/// is a powered attack (Slippery) still apply.
/// </para>
/// </summary>
public sealed class Potions : IContentModule
{
    /// <summary>
    /// The two cards of the Ironclad's pool that the game only offers with more than one player.
    /// Random card generation and card rewards never produce them in a solo run.
    /// </summary>
    internal static readonly IReadOnlySet<string> MultiplayerOnlyCards = new HashSet<string>(StringComparer.Ordinal) { "DemonicShield", "Tank" };

    public void Register(Registry r)
    {
        r.Power<FlexPotionPower>();
        r.Power<SpeedPotionPower>();
        r.Power<ShacklingPotionPower>();
        r.Power<RegenPower>();
        r.Power<ThornsPower>();
        r.Power<ClarityPower>();
        r.Power<DuplicationPower>();
        r.Power<RadiancePower>();
        r.Power<RetainHandPower>();
        r.Power<DemisePower>();
        r.Power<GigantificationPower>();
        r.Power<BufferPower>();
        r.Power<RitualPower>();
        r.Power<BlockNextTurnPower>();

        Common(r);
        Uncommon(r);
        Rare(r);
        LeftOut(r);
    }

    // ---------- common ----------

    private static void Common(Registry r)
    {
        // 20 damage to the chosen enemy, the user as dealer.
        r.Potion(new PotionDef("FirePotion", "Fire Potion", PotionRarity.Common, TargetType.AnyEnemy)
        {
            Text = _ => "Deal 20 damage to an enemy. Strength, Weak and Vulnerable do not change it.",
            Use = p => p.F.Damage(p.Target!, 20, Props.NonCardUnpowered, p.Owner),
        });

        // 10 damage to every enemy that can be hit, as one damage instance: all of them lose
        // Block and HP first, then the reactions run, then the deaths.
        r.Potion(new PotionDef("ExplosiveAmpoule", "Explosive Ampoule", PotionRarity.Common, TargetType.AllEnemies)
        {
            Text = _ => "Deal 10 damage to ALL enemies. Strength, Weak and Vulnerable do not change it.",
            Use = p => p.F.Damage(p.F.HittableEnemies, 10, Props.NonCardUnpowered, p.Owner),
        });

        r.Potion(new PotionDef("BlockPotion", "Block Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 12 Block. Dexterity and Frail do not change it.",
            Use = p => p.F.GainBlock(p.Target!, 12, Props.NonCardUnpowered),
        });

        r.Potion(new PotionDef("StrengthPotion", "Strength Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 2 Strength.",
            Use = p => p.F.ApplyPower<StrengthPower>(p.Target!, 2, p.Owner),
        });

        // 5 Strength through its own temporary power, which takes it back at the end of the turn.
        r.Potion(new PotionDef("FlexPotion", "Flex Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 5 Strength. At the end of this turn, lose 5 Strength.",
            Use = p => p.F.ApplyPower<FlexPotionPower>(p.Target!, 5, p.Owner),
        });

        r.Potion(new PotionDef("DexterityPotion", "Dexterity Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 2 Dexterity.",
            Use = p => p.F.ApplyPower<DexterityPower>(p.Target!, 2, p.Owner),
        });

        r.Potion(new PotionDef("SpeedPotion", "Speed Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 5 Dexterity. At the end of this turn, lose 5 Dexterity.",
            Use = p => p.F.ApplyPower<SpeedPotionPower>(p.Target!, 5, p.Owner),
        });

        r.Potion(new PotionDef("EnergyPotion", "Energy Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 2 Energy.",
            Use = p => p.F.GainEnergy(2),
        });

        r.Potion(new PotionDef("SwiftPotion", "Swift Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Text = _ => "Draw 3 cards.",
            Use = p => p.F.Draw(3),
        });

        r.Potion(new PotionDef("WeakPotion", "Weak Potion", PotionRarity.Common, TargetType.AnyEnemy)
        {
            Text = _ => "Apply 3 Weak to an enemy.",
            Use = p => p.F.ApplyPower<WeakPower>(p.Target!, 3, p.Owner),
        });

        r.Potion(new PotionDef("VulnerablePotion", "Vulnerable Potion", PotionRarity.Common, TargetType.AnyEnemy)
        {
            Text = _ => "Apply 3 Vulnerable to an enemy.",
            Use = p => p.F.ApplyPower<VulnerablePower>(p.Target!, 3, p.Owner),
        });

        // The Ironclad's own common potion: heals 20% of max HP; the resulting HP is cut to a whole number.
        r.Potion(new PotionDef("BloodPotion", "Blood Potion", PotionRarity.Common, TargetType.AnyPlayer)
        {
            Usage = PotionUsage.AnyTime,
            Text = f => f is null
                ? "Heal 20% of your Max HP."
                : string.Create(CultureInfo.InvariantCulture, $"Heal 20% of your Max HP ({(int)BloodHeal(f.Player)} HP)."),
            Use = p => p.F.Heal(p.Target!, BloodHeal(p.Target!)),
        });

        r.Potion(CardPotion("AttackPotion", "Attack Potion", CardType.Attack));
        r.Potion(CardPotion("SkillPotion", "Skill Potion", CardType.Skill));
        r.Potion(CardPotion("PowerPotion", "Power Potion", CardType.Power));
    }

    private static decimal BloodHeal(Creature creature) => creature.MaxHp * 20m / 100m;

    /// <summary>The game's upper bound for "any number of cards".</summary>
    private const int AnyNumber = 999_999_999;

    /// <summary>The card types Orobic Acid draws, in the game's order.</summary>
    internal static readonly CardType[] OrobicTypes = [CardType.Attack, CardType.Skill, CardType.Power];

    /// <summary>
    /// The Attack, Skill and Power Potion: three different random cards of one type from the
    /// Ironclad's pool; the player takes one or none; it goes to the hand and is free this turn
    /// or until played (an X card stays an X card).
    /// </summary>
    private static PotionDef CardPotion(string className, string title, CardType type) =>
        new(className, title, PotionRarity.Common, TargetType.Self)
        {
            Text = _ => $"Choose 1 of 3 random {type} cards to add to your hand, or none. It costs 0 this turn.",
            Use = p => OfferCards(p, type),
        };

    /// <summary>
    /// The cards a card potion draws from: the pool's cards of the type that random generation
    /// may make in a fight (never Basic, Ancient or Event cards, never Feed or Not Yet), in pool order.
    /// </summary>
    internal static List<CardDef> CardPotionPool(CardType type) => Content.Registry.IroncladPool
        .Where(c => c.Type == type && c.IsGeneratableInCombat && !MultiplayerOnlyCards.Contains(c.ClassName))
        .ToList();

    private static void OfferCards(PotionContext p, CardType type)
    {
        var f = p.F;
        // The game shuffles the whole list on the card-generation stream and takes the first three.
        var pool = CardPotionPool(type);
        f.Rng.CombatCardGeneration.Shuffle(pool);
        var offer = pool.Take(3).Select(def => f.CreateCard(def)).ToList();

        if (f.ChooseOneOf(offer, canSkip: true) is not { } chosen) return;
        chosen.SetCostThisTurnOrUntilPlayed(0);
        f.AddGeneratedCard(chosen, PileType.Hand, PilePosition.Bottom, p.Owner);
    }

    // ---------- uncommon ----------

    private static void Uncommon(Registry r)
    {
        // Strength first, then Dexterity.
        r.Potion(new PotionDef("FyshOil", "Fysh Oil", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 1 Strength and 1 Dexterity.",
            Use = p =>
            {
                p.F.ApplyPower<StrengthPower>(p.Target!, 1, p.Owner);
                p.F.ApplyPower<DexterityPower>(p.Target!, 1, p.Owner);
            },
        });

        // Energy first, then the cards. It removes nothing, despite the name.
        r.Potion(new PotionDef("CureAll", "Cure All", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 1 Energy. Draw 2 cards.",
            Use = p =>
            {
                p.F.GainEnergy(1);
                p.F.Draw(2);
            },
        });

        // Weak to every hittable enemy, then Vulnerable to the same enemies, 1 each.
        r.Potion(new PotionDef("PotionOfBinding", "Potion of Binding", PotionRarity.Uncommon, TargetType.AllEnemies)
        {
            Text = _ => "Apply 1 Weak and 1 Vulnerable to ALL enemies.",
            Use = p =>
            {
                var targets = p.F.HittableEnemies;
                foreach (var enemy in targets) p.F.ApplyPower<WeakPower>(enemy, 1, p.Owner);
                foreach (var enemy in targets) p.F.ApplyPower<VulnerablePower>(enemy, 1, p.Owner);
            },
        });

        r.Potion(new PotionDef("HeartOfIron", "Heart of Iron", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 7 Plating: at the end of your turn gain Block equal to your Plating; at the start of your turn Plating goes down by 1.",
            Use = p => p.F.ApplyPower<PlatingPower>(p.Target!, 7, p.Owner),
        });

        // Block equal to twice the Block held now, as unpowered Block: the total triples.
        r.Potion(new PotionDef("Fortifier", "Fortifier", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = f => f is null
                ? "Triple your Block."
                : string.Create(CultureInfo.InvariantCulture, $"Triple your Block (gain {f.Player.Block * 2} Block now)."),
            Use = p => p.F.GainBlock(p.Target!, p.Target!.Block * 2, Props.NonCardUnpowered),
        });

        // The Ironclad's own uncommon potion: any number of hand cards, none included, exhausted in
        // the order they were picked.
        r.Potion(new PotionDef("Ashwater", "Ashwater", PotionRarity.Uncommon, TargetType.Self)
        {
            Text = _ => "Exhaust any number of cards in your hand.",
            Use = p =>
            {
                foreach (var card in p.F.ChooseFromHand("Choose any number of cards to Exhaust.", 0, AnyNumber)) p.F.Exhaust(card);
            },
        });

        r.Potion(new PotionDef("BlessingOfTheForge", "Blessing of the Forge", PotionRarity.Uncommon, TargetType.Self)
        {
            Text = _ => "Upgrade all cards in your hand for the rest of combat.",
            Use = p =>
            {
                foreach (var card in p.F.Hand.ToList())
                    if (card.IsUpgradable) p.F.Upgrade(card);
            },
        });

        // 5 Regen: 5 + 4 + 3 + 2 + 1 HP over the ends of the next five turns. Never made by random generation in a fight.
        r.Potion(new PotionDef("RegenPotion", "Regen Potion", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            CanBeGeneratedInCombat = false,
            Text = _ => "Gain 5 Regen: at the end of your turn heal HP equal to your Regen, then Regen goes down by 1.",
            Use = p => p.F.ApplyPower<RegenPower>(p.Target!, 5, p.Owner),
        });

        r.Potion(new PotionDef("LiquidBronze", "Liquid Bronze", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 3 Thorns: whenever an enemy's attack hits you, that enemy takes 3 damage first, for each hit.",
            Use = p => p.F.ApplyPower<ThornsPower>(p.Target!, 3, p.Owner),
        });

        // The card first, then the power. The game's title for the potion is "Clarity Extract"; its power is "Clarity".
        r.Potion(new PotionDef("Clarity", "Clarity Extract", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = _ => "Draw 1 card. At the start of each of your next 3 turns, draw 1 more card.",
            Use = p =>
            {
                p.F.Draw(1);
                p.F.ApplyPower<ClarityPower>(p.Target!, 3, p.Owner);
            },
        });

        r.Potion(new PotionDef("Duplicator", "Duplicator", PotionRarity.Uncommon, TargetType.Self)
        {
            Text = _ => "The next card you play this turn is played an extra time.",
            Use = p => p.F.ApplyPower<DuplicationPower>(p.Owner, 1, p.Owner),
        });

        // Energy now, then the power.
        r.Potion(new PotionDef("RadiantTincture", "Radiant Tincture", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 1 Energy. At the start of each of your next 3 turns, gain 1 more Energy.",
            Use = p =>
            {
                p.F.GainEnergy(1);
                p.F.ApplyPower<RadiancePower>(p.Target!, 3, p.Owner);
            },
        });

        r.Potion(new PotionDef("StableSerum", "Stable Serum", PotionRarity.Uncommon, TargetType.AnyPlayer)
        {
            Text = _ => "Keep your hand at the end of this turn and the next (Ethereal cards are still exhausted).",
            Use = p => p.F.ApplyPower<RetainHandPower>(p.Target!, 2, p.Owner),
        });

        // A Debuff: Artifact stops it. It never wears off.
        r.Potion(new PotionDef("PowderedDemise", "Powdered Demise", PotionRarity.Uncommon, TargetType.AnyEnemy)
        {
            Text = _ => "Apply 9 Demise to an enemy: at the end of each of its turns it loses 9 HP, through Block.",
            Use = p => p.F.ApplyPower<DemisePower>(p.Target!, 9, p.Owner),
        });

        // Any number of hand cards, none included: each is discarded in the order picked, with its
        // discard hooks, then as many cards are drawn. With none picked nothing is drawn either.
        r.Potion(new PotionDef("GamblersBrew", "Gambler's Brew", PotionRarity.Uncommon, TargetType.Self)
        {
            Text = _ => "Discard any number of cards from your hand, then draw that many.",
            Use = p =>
            {
                var cards = p.F.ChooseFromHand("Choose any number of cards to discard.", 0, AnyNumber);
                p.F.DiscardAndDraw(cards, cards.Count);
            },
        });

        // One hand card that costs energy, by its own cost or by what powers make of it; an X card
        // never qualifies. It costs 0 for the rest of the fight, as far as the card's own cost goes:
        // a power that adds to costs (Tangled) still adds. One candidate is taken without a question.
        r.Potion(new PotionDef("TouchOfInsanity", "Touch of Insanity", PotionRarity.Uncommon, TargetType.Self)
        {
            Text = _ => "Choose a card in your hand that costs Energy. It costs 0 for the rest of combat.",
            Use = p =>
            {
                var f = p.F;
                var chosen = f.ChooseFromHand("Choose a card to make free for the rest of combat.", 1, 1,
                    c => f.LocalCostOf(c) > 0 || (!c.CostsX && f.CostOf(c) > 0)).FirstOrDefault();
                chosen?.SetCostThisCombat(0);
            },
        });
    }

    // ---------- rare ----------

    private static void Rare(Registry r)
    {
        // Shrink with a positive amount is a duration: it goes down by 1 at the end of each of the enemy's turns.
        r.Potion(new PotionDef("BeetleJuice", "Beetle Juice", PotionRarity.Rare, TargetType.AnyEnemy)
        {
            Text = _ => "Apply 4 Shrink to an enemy: its attacks deal 30% less damage for 4 turns.",
            Use = p => p.F.ApplyPower<ShrinkPower>(p.Target!, 4, p.Owner),
        });

        // Gaining max HP heals by the amount gained.
        r.Potion(new PotionDef("FruitJuice", "Fruit Juice", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Usage = PotionUsage.AnyTime,
            CanBeGeneratedInCombat = false,
            Text = _ => "Gain 5 Max HP (and 5 HP with it).",
            Use = p => p.F.GainMaxHp(p.Target!, 5),
        });

        // 7 Strength less on every hittable enemy through its own temporary power, which gives
        // it back at the end of the enemies' turn.
        r.Potion(new PotionDef("ShacklingPotion", "Shackling Potion", PotionRarity.Rare, TargetType.AllEnemies)
        {
            Text = _ => "ALL enemies lose 7 Strength until the end of their turn.",
            Use = p =>
            {
                foreach (var enemy in p.F.HittableEnemies) p.F.ApplyPower<ShacklingPotionPower>(enemy, 7, p.Owner);
            },
        });

        // The Ironclad's own rare potion: every Strike-tagged card that is in a combat pile now
        // (hand, draw, discard, exhaust, play) is played one more time from now on. Cards
        // created later are not touched.
        r.Potion(new PotionDef("SoldiersStew", "Soldier's Stew", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Text = _ => "Every card with Strike in its name that you have in this combat now is played 1 additional time whenever it is played.",
            Use = p =>
            {
                foreach (var card in p.F.AllCards.Where(c => c.HasTag(CardTags.Strike)).ToList()) card.ReplayCount++;
            },
        });

        // One card of the discard pile goes to the hand (a plain move) and is free this turn or
        // until played. A single card in the pile is taken without a question.
        r.Potion(new PotionDef("LiquidMemories", "Liquid Memories", PotionRarity.Rare, TargetType.Self)
        {
            Text = _ => "Choose a card in your discard pile and put it into your hand. It costs 0 this turn.",
            Use = p =>
            {
                if (p.F.ChooseFromPile(PileType.Discard, "Choose a card to put into your hand.", 1, 1).FirstOrDefault() is not { } card) return;
                card.SetCostThisTurnOrUntilPlayed(0);
                p.F.MoveCard(card, PileType.Hand);
            },
        });

        // One card of the draw pile goes to the hand (a plain move). Its cost stays.
        r.Potion(new PotionDef("DropletOfPrecognition", "Droplet of Precognition", PotionRarity.Rare, TargetType.Self)
        {
            Text = _ => "Choose a card in your draw pile and put it into your hand.",
            Use = p =>
            {
                if (p.F.ChooseFromPile(PileType.Draw, "Choose a card to put into your hand.", 1, 1).FirstOrDefault() is { } card) p.F.MoveCard(card, PileType.Hand);
            },
        });

        // One random Attack, one Skill and one Power of the pool, each drawn like a card potion's
        // (a shuffle of the whole list per type, in this order), all free this turn or until
        // played, added to the hand one after the other.
        r.Potion(new PotionDef("OrobicAcid", "Orobic Acid", PotionRarity.Rare, TargetType.Self)
        {
            Text = _ => "Add a random Attack, a random Skill and a random Power to your hand. They cost 0 this turn.",
            Use = p =>
            {
                var cards = new List<CardInstance>(3);
                foreach (var type in OrobicTypes)
                {
                    var pool = CardPotionPool(type);
                    p.F.Rng.CombatCardGeneration.Shuffle(pool);
                    if (pool.Count > 0) cards.Add(p.F.CreateCard(pool[0]));
                }
                foreach (var card in cards) card.SetCostThisTurnOrUntilPlayed(0);
                foreach (var card in cards) p.F.AddGeneratedCard(card, PileType.Hand, PilePosition.Bottom, p.Owner);
            },
        });

        // Draw 7; then every card in hand that has a cost and is not an X card costs 0 to 3 this
        // turn or until played, one roll per card in hand order on the energy-cost stream.
        r.Potion(new PotionDef("SneckoOil", "Snecko Oil", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Text = _ => "Draw 7 cards. Then every card in your hand costs a random 0 to 3 this turn.",
            Use = p =>
            {
                p.F.Draw(7);
                foreach (var card in p.F.Hand.Where(c => !c.CostsX && c.BaseCost >= 0).ToList())
                    card.SetCostThisTurnOrUntilPlayed(p.F.Rng.CombatEnergyCosts.NextInt(4));
            },
        });

        r.Potion(new PotionDef("DistilledChaos", "Distilled Chaos", PotionRarity.Rare, TargetType.Self)
        {
            Text = _ => "Play the top 3 cards of your draw pile (free; targets are random).",
            Use = p => p.F.AutoPlayFromDrawPile(3, PilePosition.Top, forceExhaust: false),
        });

        // Never drunk by hand: while it is in the belt its owner's death is prevented once; the potion
        // is then used up and heals 30% of max HP (at least 1) from 0.
        r.Potion(new PotionDef("FairyInABottle", "Fairy in a Bottle", PotionRarity.Rare, TargetType.Self)
        {
            Usage = PotionUsage.Automatic,
            CanBeGeneratedInCombat = false,
            Text = f => f is null
                ? "When you would die, heal to 30% of your Max HP instead. Works by itself, once."
                : string.Create(CultureInfo.InvariantCulture, $"When you would die, heal to 30% of your Max HP instead ({(int)FairyHeal(f.Player)} HP). Works by itself, once."),
            Use = p => p.F.Heal(p.Target!, FairyHeal(p.Target!)),
            Hooks = new FairyHooks(),
        });

        r.Potion(new PotionDef("GigantificationPotion", "Gigantification Potion", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Text = _ => "Your next Attack card deals triple damage.",
            Use = p => p.F.ApplyPower<GigantificationPower>(p.Target!, 1, p.Owner),
        });

        r.Potion(new PotionDef("LuckyTonic", "Lucky Tonic", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 1 Buffer: the next time you would lose HP, you lose none.",
            Use = p => p.F.ApplyPower<BufferPower>(p.Target!, 1, p.Owner),
        });

        r.Potion(new PotionDef("MazalethsGift", "Mazaleth's Gift", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 1 Ritual: at the end of each of your turns, gain 1 Strength.",
            Use = p => p.F.ApplyPower<RitualPower>(p.Target!, 1, p.Owner),
        });

        // 10 unpowered Block now, then the power that gives 10 more when the owner's Block is next cleared.
        r.Potion(new PotionDef("ShipInABottle", "Ship in a Bottle", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Text = _ => "Gain 10 Block. At the start of your next turn, gain 10 Block. Dexterity and Frail do not change either.",
            Use = p =>
            {
                p.F.GainBlock(p.Target!, 10, Props.NonCardUnpowered);
                p.F.ApplyPower<BlockNextTurnPower>(p.Target!, 10, p.Owner);
            },
        });

        // The hand goes onto the bottom of the draw pile, the draw pile is shuffled (the discard
        // pile with it, as in every shuffle), then 5 cards are drawn.
        r.Potion(new PotionDef("BottledPotential", "Bottled Potential", PotionRarity.Rare, TargetType.AnyPlayer)
        {
            Text = _ => "Shuffle your hand into your draw pile. Draw 5 cards.",
            Use = p =>
            {
                p.F.MoveCards(p.F.Hand.ToList(), PileType.Draw);
                p.F.Shuffle();
                p.F.Draw(5);
            },
        });
    }

    private static decimal FairyHeal(Creature creature) => Math.Max(creature.MaxHp * 0.3m, 1m);

    /// <summary>
    /// Fairy in a Bottle in the belt. The object is shared by every fight and keeps no state; it
    /// finds the slot it acts for by looking for itself in the belt.
    /// </summary>
    private sealed class FairyHooks : Listener
    {
        public override bool ShouldDie(Fight f, Creature creature) => !creature.IsPlayer;

        public override void AfterPreventingDeath(Fight f, Creature creature)
        {
            for (var slot = 0; slot < f.Potions.Count; slot++)
            {
                if (!ReferenceEquals(f.Potions[slot]?.Hooks, this)) continue;
                // A use like any other: out of the belt, the potion hooks around the heal, a line in the history.
                f.UsePotionFromHook(slot, creature);
                return;
            }
        }
    }

    // ---------- left out ----------

    private static void LeftOut(Registry r)
    {
        void Out(string className, string reason) => r.Exclude(ContentKind.Potion, className, reason);

        Out("ColorlessPotion", "offers three cards of the colorless pool (64 cards), which the arena does not have");
        Out("EntropicBrew", "fills the belt with random potions of the whole pool, and one of them (Colorless Potion) the arena does not have");
    }
}

/// <summary>
/// The Flex Potion's temporary Strength (the game's FlexPotionPower): its amount of Strength
/// at once, taken back at the end of its owner's turn. It carries the potion's name.
/// </summary>
public sealed class FlexPotionPower : TemporaryStrengthPower
{
    public override string Name => "Flex Potion";
}

/// <summary>
/// The Shackling Potion's temporary Strength loss (the game's ShacklingPotionPower): its
/// amount of Strength less at once, given back at the end of its owner's side's turn. It is a
/// Debuff, so Artifact stops all of it.
/// </summary>
public sealed class ShacklingPotionPower : TemporaryStrengthPower
{
    public override string Name => "Shackling Potion";
    protected override bool IsPositive => false;
}

/// <summary>
/// The Speed Potion's temporary Dexterity (the game's SpeedPotionPower, the positive kind of
/// its temporary-Dexterity family): applying it gives the same amount of Dexterity at once; at
/// the end of its owner's side's turn it removes itself and takes the Dexterity back with a
/// plain Dexterity application of the opposite sign.
/// </summary>
public sealed class SpeedPotionPower : Power
{
    public override string Name => "Speed Potion";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Has {Amount} extra Dexterity until the end of its turn.");

    public override void BeforeApplied(Fight f, Creature target, decimal amount, Creature? applier, CardInstance? cardSource) =>
        f.ApplyPower<DexterityPower>(target, amount, applier, cardSource);

    public override void AfterPowerAmountChanged(Fight f, Power power, decimal amount, Creature? applier, CardInstance? cardSource)
    {
        // More stacks on an instance that is already there (the first application is handled above).
        if (!ReferenceEquals(power, this) || amount == Amount) return;
        f.ApplyPower<DexterityPower>(Owner, amount, applier, cardSource);
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (!participants.Contains(Owner)) return;
        f.RemovePower(this);
        f.ApplyPower<DexterityPower>(Owner, -Amount, Owner);
    }
}

/// <summary>
/// Regen (the game's RegenPower): at the end of its owner's side's turn the owner, if alive,
/// heals HP equal to the amount, and then the amount goes down by 1.
/// </summary>
public sealed class RegenPower : Power
{
    public override string Name => "Regen";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"At the end of its turn, heals {Amount} HP. Then Regen goes down by 1.");

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (!participants.Contains(Owner) || Owner.IsDead) return;
        f.Heal(Owner, Amount);
        f.Decrement(this);
    }
}

/// <summary>
/// Thorns (the game's ThornsPower): just before a powered attack's hit reaches its owner, whoever
/// deals the hit takes the amount as unpowered damage from the owner. Once per hit; the hit still lands.
/// </summary>
public sealed class ThornsPower : Power
{
    public override string Name => "Thorns";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Whenever an attack hits it, the attacker takes {Amount} damage first.");

    public override void BeforeDamageReceived(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || dealer is null || !props.IsPoweredAttack()) return;
        f.Damage(dealer, Amount, ValueProp.Unpowered | ValueProp.SkipHurtAnim, Owner);
    }
}

/// <summary>
/// Clarity (the game's ClarityPower): its owner's turn-start draw is one card larger; the amount
/// goes down by 1 after each of the owner's turn starts, which is after that draw.
/// </summary>
public sealed class ClarityPower : Power
{
    public override string Name => "Clarity";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Draws 1 more card at the start of its turn, for {Turns(Amount)}.");

    public override decimal ModifyHandDraw(Fight f, decimal count) => Owner.IsPlayer ? count + 1m : count;

    public override void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.Decrement(this);
    }
}

/// <summary>
/// Duplication (the game's DuplicationPower): the next card its owner plays runs one extra time,
/// which costs one stack. Whatever is left is removed at the end of the owner's turn.
/// </summary>
public sealed class DuplicationPower : Power
{
    public override string Name => "Duplication";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "The next card played this turn is played an extra time."
        : string.Create(CultureInfo.InvariantCulture, $"The next {Amount} cards played this turn are each played an extra time.");

    public override int ModifyCardPlayCount(Fight f, CardInstance card, Creature? target, int playCount) =>
        Owner.IsPlayer ? playCount + 1 : playCount;

    public override void AfterModifyingCardPlayCount(Fight f, CardInstance card) => f.Decrement(this);

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.RemovePower(this);
    }
}

/// <summary>
/// Radiance (the game's RadiancePower): right after its owner's energy was reset at the start of
/// a turn, the owner gains 1 energy (always 1, whatever the amount) and the amount goes down by 1.
/// </summary>
public sealed class RadiancePower : Power
{
    private const int EnergyPerTurn = 1;

    public override string Name => "Radiance";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Gains {EnergyPerTurn} more Energy at the start of its turn, for {Turns(Amount)}.");

    public override void AfterEnergyReset(Fight f)
    {
        if (!Owner.IsPlayer) return;
        f.GainEnergy(EnergyPerTurn);
        f.Decrement(this);
    }
}

/// <summary>
/// Retain Hand (the game's RetainHandPower): its owner's hand is not flushed at the end of the
/// turn; the amount goes down by 1 at the end of each of the owner's turns. Ethereal cards and
/// cards that act at the end of the turn are dealt with before the flush, so they still go.
/// </summary>
public sealed class RetainHandPower : Power
{
    public override string Name => "Retain Hand";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Keeps its hand at the end of the turn, for {Turns(Amount)}.");

    public override bool ShouldFlush(Fight f) => !Owner.IsPlayer;

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.Decrement(this);
    }
}

/// <summary>
/// Demise (the game's DemisePower): at the end of its owner's side's turn the owner loses HP
/// equal to the amount, unpowered and past Block, with no dealer. The amount never goes down.
/// </summary>
public sealed class DemisePower : Power
{
    public override string Name => "Demise";
    public override PowerType Type => PowerType.Debuff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"At the end of its turn, loses {Amount} HP. Block does not stop it.");

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.Damage(Owner, Amount, Props.NonCardHpLoss, null);
    }
}

/// <summary>
/// Gigantification (the game's GigantificationPower): the next attack command of an Attack card
/// its owner plays deals triple damage on every hit, and then one stack goes. Until an attack
/// has claimed it, every powered card number of the owner reads tripled (the hand shows it);
/// while the claimed attack runs, only that card's damage is tripled.
/// </summary>
public sealed class GigantificationPower : Power
{
    private const decimal Factor = 3m;

    /// <summary>The attack that claimed the tripling, by its id; 0 while none has.</summary>
    private int _attackId;
    private CardInstance? _attackCard;

    public override string Name => "Gigantification";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "The next Attack card played deals triple damage."
        : string.Create(CultureInfo.InvariantCulture, $"The next {Amount} Attack cards played deal triple damage.");

    public override void BeforeAttack(Fight f, AttackCommand attack)
    {
        if (attack.CardSource is not { } card || !Owner.IsPlayer || card.Type != CardType.Attack) return;
        if (!attack.Props.IsPoweredAttack() || _attackId != 0) return;
        _attackId = attack.Id;
        _attackCard = card;
    }

    public override decimal ModifyDamageMultiplicative(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (cardSource is null || !Owner.IsPlayer || !props.IsPoweredAttack()) return 1m;
        return _attackId == 0 || ReferenceEquals(cardSource, _attackCard) ? Factor : 1m;
    }

    public override void AfterAttack(Fight f, AttackCommand attack)
    {
        if (_attackId == 0 || attack.Id != _attackId) return;
        _attackId = 0;
        _attackCard = null;
        f.Decrement(this);
    }
}

/// <summary>
/// Buffer (the game's BufferPower): HP its owner is about to lose becomes 0, in the last pass of
/// the HP-loss modifiers; when that changed a loss of 1 or more, one stack goes. A hit that
/// Block absorbed whole costs no stack.
/// </summary>
public sealed class BufferPower : Power
{
    public override string Name => "Buffer";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "The next time it would lose HP, it loses none."
        : string.Create(CultureInfo.InvariantCulture, $"The next {Amount} times it would lose HP, it loses none.");

    public override decimal ModifyHpLostAfterOstyLate(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
        ReferenceEquals(target, Owner) ? 0m : amount;

    public override void AfterModifyingHpLostAfterOsty(Fight f) => f.Decrement(this);
}

/// <summary>
/// Ritual (the game's RitualPower): at the end of its owner's side's turn the owner gains
/// Strength equal to the amount. On an enemy the first such turn end after it was applied is skipped.
/// </summary>
public sealed class RitualPower : Power
{
    private bool _wasJustAppliedByEnemy;

    /// <summary>On an enemy that got it this turn: the coming end of its turn gives nothing yet
    /// (the game's private _wasJustAppliedByEnemy, which the mod's forecast reads).</summary>
    internal bool SkipsNextTurnEnd => _wasJustAppliedByEnemy;

    public override string Name => "Ritual";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"At the end of its turn, gains {Amount} Strength.");

    public override void AfterApplied(Fight f, Creature? applier, CardInstance? cardSource)
    {
        if (Owner.IsEnemy) _wasJustAppliedByEnemy = true;
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (!participants.Contains(Owner)) return;
        if (_wasJustAppliedByEnemy)
        {
            _wasJustAppliedByEnemy = false;
            return;
        }
        f.ApplyPower<StrengthPower>(Owner, Amount, Owner);
    }
}

/// <summary>
/// Block Next Turn (the game's BlockNextTurnPower): right after its owner's Block was cleared at
/// the start of the owner's turn (also when something kept the Block), the owner gains the
/// amount as unpowered Block and the power is gone.
/// </summary>
public sealed class BlockNextTurnPower : Power
{
    public override string Name => "Block Next Turn";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Gains {Amount} Block at the start of its next turn.");

    public override void AfterBlockCleared(Fight f, Creature creature)
    {
        if (!ReferenceEquals(creature, Owner)) return;
        f.GainBlock(Owner, Amount, Props.NonCardUnpowered);
        f.RemovePower(this);
    }
}
