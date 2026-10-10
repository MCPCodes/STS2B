using System.Globalization;

namespace SpireMind.Arena;

// Ironclad cards, part 3: Pommel Strike to Whirlwind in pool order (cards-3.json), the Giant
// Rock token, and the powers only these cards use. Every card and power follows its game class
// of the same name (v0.107.1): cost, numbers, upgrade, the order of effects, the hooks. The
// card and power texts are our own wording.

/// <summary>Registers the cards of cards-3.json that are not in the engine core, and their powers.</summary>
public sealed class Cards3 : IContentModule
{
    // One pair of constants for the token and for the card that describes it, so they cannot differ.
    private const int GiantRockDamage = 16;
    private const int GiantRockUpgrade = 4;

    public void Register(Registry r)
    {
        r.Power<PyrePower>();
        r.Power<RagePower>();
        r.Power<RupturePower>();
        r.Power<SetupStrikePower>();
        r.Power<StampedePower>();
        r.Power<UnmovablePower>();
        r.Power<FreeAttackPower>();
        r.Power<ViciousPower>();

        PommelStrikeToRupture(r);
        SecondWindToStoneArmor(r);
        SwordBoomerangToTwinStrike(r);
        UnmovableToWhirlwind(r);
        GiantRock(r);

        r.Exclude(ContentKind.Card, "Tank",
            "multiplayer only: the game filters it out of every single-player card pool, rewards and random generation alike");
    }

    private static void PommelStrikeToRupture(Registry r)
    {
        // Pommel Strike: 9 damage (10), then draw 1 card (2). Carries the Strike tag. It is in the
        // Play pile while it resolves, so its own draw can never reshuffle it in.
        r.Card(new CardDef("PommelStrike", "Pommel Strike", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
        {
            Tags = CardTags.Strike,
            Vars = { ["Damage"] = 9, ["Cards"] = 1 },
            Upgrade = c =>
            {
                c.Add("Damage", 1);
                c.Add("Cards", 1);
            },
            Text = t => $"Deal {t.Damage()} damage. Draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.Draw(c.V("Cards"));
            },
        });

        // Primal Force: every Attack in hand, as the hand is when the effect starts, becomes a new
        // Giant Rock in the same place (an upgraded one from the upgraded card). The original
        // leaves the fight without being exhausted or discarded, and nothing of it carries over.
        // A Giant Rock in hand is an Attack too and is replaced like any other.
        r.Card(new CardDef("PrimalForce", "Primal Force", CardType.Skill, CardRarity.Rare, 0, TargetType.Self)
        {
            Text = t => t.Upgraded
                ? $"Transform every Attack in your hand into a Giant Rock+ (cost 1: deal {GiantRockDamage + GiantRockUpgrade} damage)."
                : $"Transform every Attack in your hand into a Giant Rock (cost 1: deal {GiantRockDamage} damage).",
            Play = c =>
            {
                var rock = Content.Registry.Card("GiantRock");
                foreach (var attack in c.F.Hand.Where(h => h.Type == CardType.Attack).ToList())
                    c.F.Transform(attack, rock, c.Upgraded);
            },
        });

        // Pyre: 1 Pyre (2), which raises maximum energy. Nothing is gained when it is played; the
        // next turn's refill is the first to reach the new maximum.
        r.Card(new CardDef("Pyre", "Pyre", CardType.Power, CardRarity.Rare, 2, TargetType.Self)
        {
            Vars = { ["Energy"] = 1 },
            Upgrade = c => c.Add("Energy", 1),
            Text = t => $"Raise your maximum energy by {t.N("Energy")}. Energy refills to the new maximum from your next turn on.",
            Play = c => c.ApplyToSelf<PyrePower>(c.V("Energy")),
        });

        // Rage: 3 Rage (5) until the end of the turn. Attacks played before it give nothing.
        r.Card(new CardDef("Rage", "Rage", CardType.Skill, CardRarity.Uncommon, 0, TargetType.Self)
        {
            Vars = { ["Power"] = 3 },
            Upgrade = c => c.Add("Power", 2),
            Text = t => $"This turn, gain {t.N("Power")} Block whenever you play an Attack.",
            Play = c => c.ApplyToSelf<RagePower>(c.V("Power")),
        });

        // Rampage: its current damage (9 at first), then this copy's damage goes up by 5 (9
        // upgraded) for the rest of the fight. The rise comes after the hit every time the effect
        // runs, also when the hit was blocked or the target is gone. The upgrade changes only the rise.
        r.Card(new CardDef("Rampage", "Rampage", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 9, ["Increase"] = 5 },
            Upgrade = c => c.Add("Increase", 4),
            Text = t => $"Deal {t.Damage()} damage. This card's damage goes up by {t.N("Increase")} for the rest of combat.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.Card.Add("Damage", c.V("Increase"));
            },
        });

        // Rupture: 1 Rupture (2).
        r.Card(new CardDef("Rupture", "Rupture", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["Strength"] = 1 },
            Upgrade = c => c.Add("Strength", 1),
            Text = t => $"Whenever you lose HP during your turn, gain {t.N("Strength")} Strength.",
            Play = c => c.ApplyToSelf<RupturePower>(c.V("Strength")),
        });
    }

    private static void SecondWindToStoneArmor(Registry r)
    {
        // Second Wind: every card in hand that is not an Attack, as the hand is when the effect
        // starts, is exhausted in hand order, and after each one 5 Block (7) is gained as a Block
        // gain of its own. A card that reaches the hand during the loop stays.
        r.Card(new CardDef("SecondWind", "Second Wind", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["Block"] = 5 },
            Upgrade = c => c.Add("Block", 2),
            Text = t => $"Exhaust every card in your hand that is not an Attack. Gain {t.Block()} Block for each card exhausted.",
            Play = c =>
            {
                foreach (var card in c.F.Hand.Where(h => h.Type != CardType.Attack).ToList())
                {
                    c.F.Exhaust(card);
                    c.GainBlock(c.V("Block"));
                }
            },
        });

        // Setup Strike: 7 damage (9), then 2 Strength (3) that is taken away again at the end of the
        // turn. The Strength comes after the hit. Carries the Strike tag.
        r.Card(new CardDef("SetupStrike", "Setup Strike", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
        {
            Tags = CardTags.Strike,
            Vars = { ["Damage"] = 7, ["Strength"] = 2 },
            Upgrade = c =>
            {
                c.Add("Damage", 2);
                c.Add("Strength", 1);
            },
            Text = t => $"Deal {t.Damage()} damage. Gain {t.N("Strength")} Strength until the end of your turn.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.ApplyToSelf<SetupStrikePower>(c.V("Strength"));
            },
        });

        // Shrug It Off: 8 Block (11), then draw 1 card. The upgrade does not change the draw.
        r.Card(new CardDef("ShrugItOff", "Shrug It Off", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
        {
            Vars = { ["Block"] = 8, ["Cards"] = 1 },
            Upgrade = c => c.Add("Block", 3),
            Text = t => $"Gain {t.Block()} Block. Draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c =>
            {
                c.GainBlock(c.V("Block"));
                c.Draw(c.V("Cards"));
            },
        });

        // Spite: 5 damage, twice (three times upgraded) if the player lost HP earlier in this same
        // turn, once otherwise. HP lost in the enemy turn before does not count. The count is
        // settled before the first hit.
        r.Card(new CardDef("Spite", "Spite", CardType.Attack, CardRarity.Uncommon, 0, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 5, ["Repeat"] = 2 },
            Upgrade = c => c.Add("Repeat", 1),
            Text = t => $"Deal {t.Damage()} damage. If you lost HP this turn, deal it {t.N("Repeat")} times instead.",
            Play = c =>
            {
                var hits = c.F.History.HpLossEventsThisTurn(c.F, c.Owner) > 0 ? (int)c.V("Repeat") : 1;
                c.Attack(c.V("Damage")).Hits(hits).Execute();
            },
        });

        // Stampede: 1 Stampede. The upgrade makes it cost 1.
        r.Card(new CardDef("Stampede", "Stampede", CardType.Power, CardRarity.Uncommon, 2, TargetType.Self)
        {
            Vars = { ["Power"] = 1 },
            Upgrade = c => c.UpgradeCostBy(-1),
            Text = t => $"At the end of your turn, play {TextContext.Count(t.N("Power"), "random Attack")} from your hand for free.",
            Play = c => c.ApplyToSelf<StampedePower>(c.V("Power")),
        });

        // Stoke: the hand, as it is when the effect starts, is exhausted card by card; then as many
        // random cards are made as were in it (independent picks, the same card may come twice;
        // all upgraded from the upgraded card) and put into the hand one by one. Each pick is one
        // CombatCardGeneration draw over the pool cards that can be generated in a fight, in pool
        // order. Stoke itself is in the Play pile and can come back as a pick.
        r.Card(new CardDef("Stoke", "Stoke", CardType.Skill, CardRarity.Rare, 1, TargetType.Self)
        {
            Text = t => t.Upgraded
                ? "Exhaust your hand. Add a random upgraded card to your hand for each card that was in it."
                : "Exhaust your hand. Add a random card to your hand for each card that was in it.",
            Play = c =>
            {
                var hand = c.F.Hand.ToList();
                foreach (var card in hand) c.F.Exhaust(card);

                var pool = Content.Registry.IroncladPool.Where(d => d.IsGeneratableInCombat).ToList();
                var made = new List<CardInstance>(hand.Count);
                for (var i = 0; i < hand.Count; i++)
                {
                    if (c.F.Rng.CombatCardGeneration.NextItem(pool) is { } pick) made.Add(c.F.CreateCard(pick, c.Upgraded));
                }
                foreach (var card in made) c.F.AddGeneratedCard(card, PileType.Hand, creator: c.Owner);
            },
        });

        // Stomp: 12 damage (15) to every enemy. Its cost falls by 1 until the end of the turn each
        // time an Attack starts to be played, wherever this card is (StompBehaviour).
        r.Card(new CardDef("Stomp", "Stomp", CardType.Attack, CardRarity.Uncommon, 3, TargetType.AllEnemies)
        {
            Vars = { ["Damage"] = 12 },
            Upgrade = c => c.Add("Damage", 3),
            Text = t => $"Deal {t.Damage()} damage to ALL enemies. Costs 1 less this turn for each Attack played this turn.",
            Play = c => c.Attack(c.V("Damage")).Execute(),
            Behaviour = () => new StompBehaviour(),
        });

        // Stone Armor: 4 Plating (6).
        r.Card(new CardDef("StoneArmor", "Stone Armor", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["Plating"] = 4 },
            Upgrade = c => c.Add("Plating", 2),
            Text = t => $"Gain {t.N("Plating")} Plating: at the end of your turn gain that much Block, and Plating drops by 1 at the start of your turn.",
            Play = c => c.ApplyToSelf<PlatingPower>(c.V("Plating")),
        });
    }

    private static void SwordBoomerangToTwinStrike(Registry r)
    {
        // Sword Boomerang: 3 damage to a random enemy, 3 times (4). The target is rolled anew for
        // every hit among the enemies alive then, one CombatTargets draw per hit also with one
        // enemy. The player picks no target.
        r.Card(new CardDef("SwordBoomerang", "Sword Boomerang", CardType.Attack, CardRarity.Common, 1, TargetType.RandomEnemy)
        {
            Vars = { ["Damage"] = 3, ["Repeat"] = 3 },
            Upgrade = c => c.Add("Repeat", 1),
            Text = t => $"Deal {t.Damage()} damage to a random enemy {t.N("Repeat")} times.",
            Play = c => c.Attack(c.V("Damage")).Hits((int)c.V("Repeat")).Execute(),
        });

        // Taunt: 7 Block (8), then 1 Vulnerable (2) on the chosen enemy. A Skill that needs a target.
        r.Card(new CardDef("Taunt", "Taunt", CardType.Skill, CardRarity.Uncommon, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Block"] = 7, ["Vulnerable"] = 1 },
            Upgrade = c =>
            {
                c.Add("Block", 1);
                c.Add("Vulnerable", 1);
            },
            Text = t => $"Gain {t.Block()} Block. Apply {t.N("Vulnerable")} Vulnerable.",
            Play = c =>
            {
                c.GainBlock(c.V("Block"));
                c.Apply<VulnerablePower>(c.Target, c.V("Vulnerable"));
            },
        });

        // Tear Asunder: 5 damage (7) once, plus once for every damage event of this fight in which
        // the player lost HP: any turn, any source, each hit of a multi-attack that got through on
        // its own. The game works the count out as base 0 plus extra 1 times (1 + events); its
        // Repeat number is not used.
        r.Card(new CardDef("TearAsunder", "Tear Asunder", CardType.Attack, CardRarity.Rare, 2, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 5, ["Repeat"] = 1, ["CalculationBase"] = 0, ["CalculationExtra"] = 1 },
            Upgrade = c => c.Add("Damage", 2),
            Text = t => t.F is { InProgress: true } f
                ? $"Deal {t.Damage()} damage once, plus once more for each time you lost HP this combat (now {TextContext.Count(TearAsunderHits(f, t.Card), "hit")})."
                : $"Deal {t.Damage()} damage once, plus once more for each time you lost HP this combat.",
            Play = c => c.Attack(c.V("Damage")).Hits(TearAsunderHits(c.F, c.Card)).Execute(),
        });

        // Thrash: its current damage (4 at first; 6 upgraded) twice. Then one random Attack in hand
        // (one CombatCardSelection draw; none with no Attack there) gives its damage number to this
        // copy for the rest of the fight and is exhausted. The number is the other card's per-hit
        // number (a calculated one is worked out with no target) run through the player's own
        // modifiers with no target: Strength and Weak count, the enemy's Vulnerable does not. It
        // is not rounded, so the fraction stays in this card's damage.
        r.Card(new CardDef("Thrash", "Thrash", CardType.Attack, CardRarity.Rare, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 4 },
            Upgrade = c => c.Add("Damage", 2),
            Text = t => $"Deal {t.Damage()} damage twice. Exhaust a random Attack in your hand and add its damage to this card's damage for the rest of combat.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Hits(2).Execute();

                var picked = c.F.Rng.CombatCardSelection.NextItem(c.F.Hand.Where(h => h.Type == CardType.Attack).ToList());
                if (picked is null) return;
                // A card without a damage number counts as 0 before the modifiers, as in the game.
                var taken = c.F.ModifyDamage(null, c.Owner, c.F.DamageNumberOf(picked) ?? 0m, Props.Card, picked);
                c.Card.Add("Damage", taken);
                c.F.Exhaust(picked);
            },
        });

        // Thunderclap: 4 damage (7) to every enemy, then 1 Vulnerable to every enemy that can still
        // be hit, one application after another in enemy order. The upgrade changes only the damage.
        r.Card(new CardDef("Thunderclap", "Thunderclap", CardType.Attack, CardRarity.Common, 1, TargetType.AllEnemies)
        {
            Vars = { ["Damage"] = 4, ["Vulnerable"] = 1 },
            Upgrade = c => c.Add("Damage", 3),
            Text = t => $"Deal {t.Damage()} damage to ALL enemies. Apply {t.N("Vulnerable")} Vulnerable to ALL enemies.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                foreach (var enemy in c.F.HittableEnemies) c.Apply<VulnerablePower>(enemy, c.V("Vulnerable"));
            },
        });

        // Tremble: 3 Vulnerable (4) on the chosen enemy. Exhaust.
        r.Card(new CardDef("Tremble", "Tremble", CardType.Skill, CardRarity.Common, 1, TargetType.AnyEnemy)
        {
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Vulnerable"] = 3 },
            Upgrade = c => c.Add("Vulnerable", 1),
            Text = t => $"Apply {t.N("Vulnerable")} Vulnerable.",
            Play = c => c.Apply<VulnerablePower>(c.Target, c.V("Vulnerable")),
        });

        // True Grit: 7 Block (9), then one card of the hand is exhausted: a random one (one
        // CombatCardSelection draw, none with an empty hand), or from the upgraded card the one the
        // player picks (not optional; a single card is taken without asking).
        r.Card(new CardDef("TrueGrit", "True Grit", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
        {
            Vars = { ["Block"] = 7 },
            Upgrade = c => c.Add("Block", 2),
            Text = t => t.Upgraded
                ? $"Gain {t.Block()} Block. Exhaust a card in your hand."
                : $"Gain {t.Block()} Block. Exhaust a random card in your hand.",
            Play = c =>
            {
                c.GainBlock(c.V("Block"));
                var card = c.Upgraded ? c.F.ChooseHandCardToExhaust() : c.F.Rng.CombatCardSelection.NextItem(c.F.Hand);
                if (card is not null) c.F.Exhaust(card);
            },
        });

        // Twin Strike: 5 damage (7) twice. Carries the Strike tag.
        r.Card(new CardDef("TwinStrike", "Twin Strike", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
        {
            Tags = CardTags.Strike,
            Vars = { ["Damage"] = 5 },
            Upgrade = c => c.Add("Damage", 2),
            Text = t => $"Deal {t.Damage()} damage twice.",
            Play = c => c.Attack(c.V("Damage")).Hits(2).Execute(),
        });
    }

    private static void UnmovableToWhirlwind(Registry r)
    {
        // Unmovable: 1 Unmovable. The upgrade makes it cost 1.
        r.Card(new CardDef("Unmovable", "Unmovable", CardType.Power, CardRarity.Rare, 2, TargetType.Self)
        {
            Upgrade = c => c.UpgradeCostBy(-1),
            Text = _ => "Each turn, the Block from the first card that gives you Block is doubled.",
            Play = c => c.ApplyToSelf<UnmovablePower>(1),
        });

        // Unrelenting: 14 damage (20), then 1 Free Attack. The power arrives after this card's own
        // play began, so the card does not use up its own charge.
        r.Card(new CardDef("Unrelenting", "Unrelenting", CardType.Attack, CardRarity.Uncommon, 2, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 14 },
            Upgrade = c => c.Add("Damage", 6),
            Text = t => $"Deal {t.Damage()} damage. The next Attack you play costs 0.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.ApplyToSelf<FreeAttackPower>(1);
            },
        });

        // Uppercut: 13 damage, then 1 Weak (2), then 1 Vulnerable (2); one number feeds both. The
        // upgrade does not change the damage.
        r.Card(new CardDef("Uppercut", "Uppercut", CardType.Attack, CardRarity.Uncommon, 2, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 13, ["Power"] = 1 },
            Upgrade = c => c.Add("Power", 1),
            Text = t => $"Deal {t.Damage()} damage. Apply {t.N("Power")} Weak. Apply {t.N("Power")} Vulnerable.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.Apply<WeakPower>(c.Target, c.V("Power"));
                c.Apply<VulnerablePower>(c.Target, c.V("Power"));
            },
        });

        // Vicious: 1 Vicious (2).
        r.Card(new CardDef("Vicious", "Vicious", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Vars = { ["Cards"] = 1 },
            Upgrade = c => c.Add("Cards", 1),
            Text = t => $"Whenever you apply Vulnerable, draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c => c.ApplyToSelf<ViciousPower>(c.V("Cards")),
        });

        // Whirlwind: X cost. 5 damage (8) to every enemy, X times; who is alive is read again
        // before every hit. With X = 0 it does nothing and still counts as an Attack played.
        r.Card(new CardDef("Whirlwind", "Whirlwind", CardType.Attack, CardRarity.Uncommon, CardDef.CostX, TargetType.AllEnemies)
        {
            Vars = { ["Damage"] = 5 },
            Upgrade = c => c.Add("Damage", 3),
            Text = t => $"Deal {t.Damage()} damage to ALL enemies X times.",
            Play = c => c.Attack(c.V("Damage")).Hits(c.X).Execute(),
        });
    }

    private static void GiantRock(Registry r)
    {
        // Giant Rock: the token Primal Force makes. 16 damage (20). Not in the card pool; it goes
        // to the discard pile like any Attack and stays in the deck for the rest of the fight.
        r.Card(new CardDef("GiantRock", "Giant Rock", CardType.Attack, CardRarity.Token, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = GiantRockDamage },
            Upgrade = c => c.Add("Damage", GiantRockUpgrade),
            Text = t => $"Deal {t.Damage()} damage.",
            Play = c => c.Attack(c.V("Damage")).Execute(),
        });
    }

    /// <summary>Tear Asunder's hit count by the game's formula; the multiplier counts as 0 while no fight is in progress.</summary>
    private static int TearAsunderHits(Fight f, CardInstance card)
    {
        var multiplier = f.InProgress ? 1 + f.History.HpLossEvents(f.Player) : 0;
        return (int)(card.Get("CalculationBase") + card.Get("CalculationExtra") * multiplier);
    }

    /// <summary>
    /// Stomp's cost. It hears the hooks from any combat pile. Every run of an Attack's effect that
    /// starts (its own included, auto-plays included) takes 1 off until the end of the turn; the
    /// cost of that Attack is already paid by then. A Stomp that is made in the middle of a fight
    /// starts with one off for every Attack run already finished this turn; a copy of another
    /// Stomp does not, it carries the original's reductions instead.
    /// </summary>
    private sealed class StompBehaviour : CardBehaviour
    {
        public override void AfterCardEnteredCombat(Fight f, CardInstance card)
        {
            if (!ReferenceEquals(card, Card) || Card.IsClone) return;
            Card.AddCostThisTurn(-f.History.CardPlaysFinishedThisTurn(f, CardType.Attack));
        }

        public override void BeforeCardPlayed(Fight f, CardPlay play)
        {
            if (play.Card.Type == CardType.Attack) Card.AddCostThisTurn(-1);
        }
    }
}

/// <summary>Pyre: adds its amount to its owner's maximum energy for the rest of the fight. Energy is
/// set to the maximum at the start of each player turn, so nothing is gained at the moment of play.</summary>
public sealed class PyrePower : Power
{
    public override string Name => "Pyre";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Maximum energy is {Amount} higher.");

    public override decimal ModifyMaxEnergy(Fight f, decimal amount) => Owner.IsPlayer ? amount + Amount : amount;
}

/// <summary>
/// Rage: after each run of an Attack's effect its owner gains Block equal to the amount. The
/// Block is unpowered and has no card behind it: Dexterity, Frail and Unmovable leave it alone,
/// and it does not use Unmovable up. Removed at the end of its owner's turn, after the hand is
/// discarded, so an Attack that is auto-played when the turn ends still gets it.
/// </summary>
public sealed class RagePower : Power
{
    public override string Name => "Rage";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Until the end of its turn, gains {Amount} Block whenever it plays an Attack.");

    public override void AfterCardPlayed(Fight f, CardPlay play)
    {
        if (Owner.IsPlayer && play.Card.Type == CardType.Attack) f.GainBlock(Owner, Amount, Props.NonCardUnpowered);
    }

    public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
    {
        if (participants.Contains(Owner)) f.RemovePower(this);
    }
}

/// <summary>
/// Rupture: whenever its owner loses HP while it is its owner's side's turn, the owner gains
/// Strength equal to the amount. HP lost to a card that is being played right now is paid out
/// when that card's run ends, once per damage event, so the card's own attack does not profit.
/// Everything else (no card behind the damage, or a card that is not being played, such as a
/// Burn at end of turn) pays at once.
/// </summary>
public sealed class RupturePower : Power
{
    // Strength owed for each card whose effect is running now.
    private Dictionary<CardInstance, int> _owed = new();

    public override string Name => "Rupture";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Whenever it loses HP during its own turn, gains {Amount} Strength.");

    public override void OnCloned(CloneMap map)
    {
        var copy = new Dictionary<CardInstance, int>(_owed.Count);
        foreach (var (card, owed) in _owed) copy[map.Get(card)!] = owed;
        _owed = copy;
    }

    public override void BeforeCardPlayed(Fight f, CardPlay play)
    {
        if (!Owner.IsPlayer || f.CurrentSide != Owner.Side) return;
        _owed[play.Card] = 0;
    }

    public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage <= 0 || f.CurrentSide != Owner.Side) return;
        if (cardSource is not null && _owed.ContainsKey(cardSource)) _owed[cardSource] += Amount;
        else f.ApplyPower<StrengthPower>(Owner, Amount, Owner);
    }

    public override void AfterCardPlayed(Fight f, CardPlay play)
    {
        if (Owner.IsPlayer && _owed.Remove(play.Card, out var owed)) f.ApplyPower<StrengthPower>(Owner, owed, Owner);
    }
}

/// <summary>Setup Strike's temporary Strength: given when applied, taken back at the end of its owner's turn.
/// The game titles a temporary Strength power after the card it comes from.</summary>
public sealed class SetupStrikePower : TemporaryStrengthPower
{
    public override string Name => "Setup Strike";
}

/// <summary>
/// Stampede: when its owner ends the turn, as the first step of the turn's end, once per stack:
/// one random Attack in hand that is not Unplayable is played for free. The pick is one draw of
/// the Shuffle stream when there is a candidate (also with a single one), none otherwise. Energy
/// is not looked at; the play follows the auto-play rules (random target, X = the energy left).
/// </summary>
public sealed class StampedePower : Power
{
    public override string Name => "Stampede";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "At the end of its turn, plays a random Attack from its hand for free."
        : string.Create(CultureInfo.InvariantCulture, $"At the end of its turn, plays {Amount} random Attacks from its hand for free, one after another.");

    public override void AfterAutoPostPlayPhaseEntered(Fight f)
    {
        if (!Owner.IsPlayer) return;
        for (var i = 0; i < Amount; i++)
        {
            var attacks = f.Hand.Where(c => c.Type == CardType.Attack && !c.Has(CardKeywords.Unplayable)).ToList();
            if (f.Rng.Shuffle.NextItem(attacks) is { } card) f.AutoPlay(card);
        }
    }
}

/// <summary>
/// Unmovable: Block a player gains from a card is doubled as long as fewer card Block gains than
/// the amount were recorded this turn for other runs of card effects. With 1 stack that is every
/// gain of the turn's first card that gives Block. The test reads the whole turn, so a Block
/// card played before Unmovable itself already counts. Block from powers is neither doubled nor
/// counted, and a gain that came to nothing is not recorded.
/// </summary>
public sealed class UnmovablePower : Power
{
    public override string Name => "Unmovable";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "Each turn, the Block from the first card that gives it Block is doubled."
        : string.Create(CultureInfo.InvariantCulture, $"Each turn, Block from cards is doubled until {Amount} Block gains from earlier cards have happened.");

    public override decimal ModifyBlockMultiplicative(Fight f, Creature target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!target.IsPlayer || !props.IsCardOrMove()) return 1m;
        // Every card belongs to the player.
        if (cardSource is not null && !Owner.IsPlayer) return 1m;
        return f.History.CardBlockGainsThisTurn(f, target, cardPlay) >= Amount ? 1m : 2m;
    }
}

/// <summary>
/// Free Attack (Unrelenting): while it is there, every Attack of its owner in the hand or the
/// Play pile costs 0, decided in the late cost pass. X-cost cards are not touched. Each run of
/// an Attack's effect that starts uses one stack, also an auto-played or already free one. It
/// has no end: it waits for the next Attack, on a later turn if need be.
/// </summary>
public sealed class FreeAttackPower : Power
{
    public override string Name => "Free Attack";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() => Amount == 1
        ? "The next Attack it plays costs 0."
        : string.Create(CultureInfo.InvariantCulture, $"The next {Amount} Attacks it plays cost 0.");

    private bool Covers(CardInstance card) =>
        Owner.IsPlayer && card.Type == CardType.Attack && card.Pile is PileType.Hand or PileType.Play;

    public override decimal ModifyEnergyCostLate(Fight f, CardInstance card, decimal cost) => Covers(card) ? 0m : cost;

    public override void BeforeCardPlayed(Fight f, CardPlay play)
    {
        if (Covers(play.Card)) f.Decrement(this);
    }
}

/// <summary>Vicious: whenever its owner is the one who applies a positive amount of Vulnerable to anyone
/// (new, or more on an existing stack), the owner draws cards equal to the amount, at once, in the
/// middle of whatever applied it. Once per creature; not when the application came to nothing.</summary>
public sealed class ViciousPower : Power
{
    public override string Name => "Vicious";
    public override PowerType Type => PowerType.Buff;

    public override string Describe() =>
        string.Create(CultureInfo.InvariantCulture, $"Whenever it applies Vulnerable, draws {(Amount == 1 ? "1 card" : $"{Amount} cards")}.");

    public override void AfterPowerAmountChanged(Fight f, Power power, decimal amount, Creature? applier, CardInstance? cardSource)
    {
        if (amount > 0m && ReferenceEquals(applier, Owner) && power is VulnerablePower) f.Draw(Amount);
    }
}
