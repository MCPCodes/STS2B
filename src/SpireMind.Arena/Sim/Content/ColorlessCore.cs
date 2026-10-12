namespace SpireMind.Arena;

/// <summary>
/// The colorless card pool, first part (run mode, chunk 13 "colorless-core"): twelve colorless
/// cards that need no power the arena does not have, eight Uncommon and four Rare, and every
/// other card of the pool left out by name. Each card is read from its class in the game
/// (v0.107.1, <c>MegaCrit.Sts2.Core.Models.Cards</c>); the titles are the game's English names.
/// <para>
/// Where colorless cards come from in an Ironclad run of act 1: the two colorless slots of
/// every shop (one Uncommon, one Rare), and a random transform of a Quest card or of a card of
/// Event, Ancient or Token rarity. Both pick over the game's full list
/// (<see cref="CardPools.Colorless"/>, 64 cards, 53 of them in a single-player run) and draw
/// again while they land on a card that is not built (a strict run ends there instead), so
/// the twelve here are what a run can be shown until the rest of the pool is built. With
/// eight of the 32 Uncommons and four of the 21 Rares a shop's two slots can always be filled.
/// </para>
/// <para>
/// A colorless card is never made by a fight's random generation of character cards
/// (Infernal Blade, the card potions): those pick from the Ironclad's pool
/// (<see cref="Registry.IroncladPool"/>), which lists the Ironclad's cards alone.
/// </para>
/// </summary>
public sealed class ColorlessCore : IContentModule
{
    /// <summary>The twelve cards this file builds, in the order of the game's pool.</summary>
    public static readonly IReadOnlyList<string> Cards =
    [
        "DramaticEntrance", "Finesse", "Fisticuffs", "GoldAxe", "Impatience", "MasterOfStrategy",
        "MindBlast", "Salvo", "Scrawl", "UltimateDefend", "UltimateStrike", "Volley",
    ];

    /// <summary>The eleven colorless cards the game takes out of every single-player list.</summary>
    public static readonly IReadOnlyList<string> MultiplayerOnly =
        ["BeaconOfHope", "BelieveInYou", "Coordinate", "GangUp", "HuddleUp", "Intercept", "Knockdown", "Lift", "Mimic", "Rally", "TagTeam"];

    /// <summary>
    /// Not built yet: the twenty cards of run-mode chunk 27 ("colorless-b"), most of which bring
    /// a power of their own. Whoever builds one takes its name off this list.
    /// </summary>
    public static readonly IReadOnlyList<string> WaitingForColorlessB =
    [
        "Alchemize", "Anointed", "Automation", "BeatDown", "Bolas", "Calamity", "Catastrophe", "DarkShackles", "Discovery", "Entropy",
        "Equilibrium", "EternalArmor", "Fasten", "FlashOfSteel", "HandOfGreed", "HiddenGem", "JackOfAllTrades", "Jackpot", "Mayhem", "Nostalgia",
    ];

    /// <summary>Not built yet: the twenty cards of run-mode chunk 28 ("colorless-c-and-users"). Whoever builds one takes its name off this list.</summary>
    public static readonly IReadOnlyList<string> WaitingForColorlessC =
    [
        "Omnislice", "Panache", "PanicButton", "PrepTime", "Production", "Prolong", "Prowess", "Purity", "Rend", "Restlessness",
        "RollingBoulder", "SecretTechnique", "SecretWeapon", "SeekerStrike", "Shockwave", "Stratagem", "TheBomb", "TheGambit", "ThinkingAhead", "ThrummingHatchet",
    ];

    /// <summary>
    /// The cards of this file whose game class says it gains Block (the game's GainsBlock). The
    /// arena's card definition has no such switch yet; the enchantment Nimble asks for it.
    /// </summary>
    public static readonly IReadOnlyList<string> GainsBlock = ["Finesse", "Fisticuffs", "UltimateDefend"];

    public void Register(Registry r)
    {
        Uncommons(r);
        Rares(r);
        LeftOut(r);
    }

    private static void Uncommons(Registry r)
    {
        // Dramatic Entrance: 11 damage (15) to every enemy. Innate, Exhaust.
        r.Card(new CardDef("DramaticEntrance", "Dramatic Entrance", CardType.Attack, CardRarity.Uncommon, 0, TargetType.AllEnemies)
        {
            Keywords = CardKeywords.Innate | CardKeywords.Exhaust,
            Vars = { ["Damage"] = 11 },
            Upgrade = c => c.Add("Damage", 4),
            Text = t => $"Deal {t.Damage()} damage to ALL enemies.",
            Play = c => c.Attack(c.V("Damage")).Execute(),
        });

        // Finesse: 4 Block (7), then draw 1 card.
        r.Card(new CardDef("Finesse", "Finesse", CardType.Skill, CardRarity.Uncommon, 0, TargetType.Self)
        {
            Vars = { ["Block"] = 4, ["Cards"] = 1 },
            Upgrade = c => c.Add("Block", 3),
            Text = t => $"Gain {t.Block()} Block. Draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c =>
            {
                c.GainBlock(c.V("Block"));
                c.Draw(c.V("Cards"));
            },
        });

        // Fisticuffs: 7 damage (9), then Block equal to what the hit took off the enemy: the Block
        // it went through, the HP it removed, and whatever went beyond the enemy's last HP. The
        // Block is a card's Block like any other: Dexterity and Frail change it. When the hit does
        // not happen (a second run of the card on an enemy the first run killed) the card still
        // asks for a Block gain of 0, which Dexterity raises like any other.
        r.Card(new CardDef("Fisticuffs", "Fisticuffs", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 7 },
            Upgrade = c => c.Add("Damage", 2),
            Text = t => $"Deal {t.Damage()} damage. Gain Block equal to the damage dealt (blocked and overkill damage count; Dexterity and Frail then change the Block).",
            Play = c =>
            {
                var attack = c.Attack(c.V("Damage")).Execute();
                c.GainBlock(attack.Results.Sum(hit => hit.TotalDamage + hit.OverkillDamage));
            },
        });

        // Impatience: draw 2 cards (3), but only with no Attack in the hand. The card itself has
        // left the hand by then, and it is a Skill anyway.
        r.Card(new CardDef("Impatience", "Impatience", CardType.Skill, CardRarity.Uncommon, 0, TargetType.Self)
        {
            Vars = { ["Cards"] = 2 },
            Upgrade = c => c.Add("Cards", 1),
            Text = t => $"If you have no Attacks in your hand, draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c =>
            {
                if (!c.F.Hand.Any(card => card.Type == CardType.Attack)) c.Draw(c.V("Cards"));
            },
        });

        // Mind Blast: damage equal to the number of cards in the draw pile, counted when it hits.
        // Innate. Costs 1 (0).
        r.Card(new CardDef("MindBlast", "Mind Blast", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy)
        {
            Keywords = CardKeywords.Innate,
            Vars = { ["CalculationBase"] = 0, ["ExtraDamage"] = 1 },
            DamageMultiplier = (f, _, _) => f.DrawPile.Count,
            Upgrade = c => c.UpgradeCostBy(-1),
            // Outside a fight there is no draw pile to count, so no number is shown.
            Text = t => t.F is null
                ? "Deal damage equal to the number of cards in your draw pile."
                : $"Deal damage equal to the number of cards in your draw pile ({t.CalculatedDamage()} right now).",
            Play = c => c.CalculatedAttack().Execute(),
        });

        // Ultimate Defend: 11 Block (15). It counts as a Defend.
        r.Card(new CardDef("UltimateDefend", "Ultimate Defend", CardType.Skill, CardRarity.Uncommon, 1, TargetType.Self)
        {
            Tags = CardTags.Defend,
            Vars = { ["Block"] = 11 },
            Upgrade = c => c.Add("Block", 4),
            Text = t => $"Gain {t.Block()} Block.",
            Play = c => c.GainBlock(c.V("Block")),
        });

        // Ultimate Strike: 14 damage (20). It counts as a Strike.
        r.Card(new CardDef("UltimateStrike", "Ultimate Strike", CardType.Attack, CardRarity.Uncommon, 1, TargetType.AnyEnemy)
        {
            Tags = CardTags.Strike,
            Vars = { ["Damage"] = 14 },
            Upgrade = c => c.Add("Damage", 6),
            Text = t => $"Deal {t.Damage()} damage.",
            Play = c => c.Attack(c.V("Damage")).Execute(),
        });

        // Volley: X hits of 10 damage (14), each at a random enemy drawn anew. With no energy it
        // is an attack of no hits.
        r.Card(new CardDef("Volley", "Volley", CardType.Attack, CardRarity.Uncommon, CardDef.CostX, TargetType.RandomEnemy)
        {
            Vars = { ["Damage"] = 10 },
            Upgrade = c => c.Add("Damage", 4),
            Text = t => $"Deal {t.Damage()} damage to a random enemy X times.",
            Play = c => c.Attack(c.V("Damage")).Hits(c.X).Execute(),
        });
    }

    private static void Rares(Registry r)
    {
        // Gold Axe: damage equal to the number of card plays finished in this fight so far. Every
        // run of a card's effect counts, also a card played for free or a second time by an effect;
        // the Gold Axe's own play is not finished while it hits. Upgraded it has Retain.
        r.Card(new CardDef("GoldAxe", "Gold Axe", CardType.Attack, CardRarity.Rare, 1, TargetType.AnyEnemy)
        {
            Vars = { ["CalculationBase"] = 0, ["ExtraDamage"] = 1 },
            DamageMultiplier = (f, _, _) => f.History.Count(e => e.Kind == HistoryKind.CardPlayFinished),
            Upgrade = c => c.Keywords |= CardKeywords.Retain,
            // Outside a fight no card has been played, so no number is shown.
            Text = t => t.F is null
                ? "Deal damage equal to the number of cards played this combat."
                : $"Deal damage equal to the number of cards played this combat ({t.CalculatedDamage()} right now).",
            Play = c => c.CalculatedAttack().Execute(),
        });

        // Master of Strategy: draw 3 cards (4). Exhaust.
        r.Card(new CardDef("MasterOfStrategy", "Master of Strategy", CardType.Skill, CardRarity.Rare, 0, TargetType.Self)
        {
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Cards"] = 3 },
            Upgrade = c => c.Add("Cards", 1),
            Text = t => $"Draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c => c.Draw(c.V("Cards")),
        });

        // Salvo: 12 damage (16), then 1 Retain Hand on the player: the hand is kept at the end
        // of this turn. The power is the one the Stable Serum gives (Potions.cs).
        r.Card(new CardDef("Salvo", "Salvo", CardType.Attack, CardRarity.Rare, 1, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 12 },
            Upgrade = c => c.Add("Damage", 4),
            Text = t => $"Deal {t.Damage()} damage. Keep your hand at the end of this turn (Ethereal cards are still exhausted).",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.F.ApplyPower("RetainHandPower", c.Owner, 1, c.Owner, c.Card);
            },
        });

        // Scrawl: draw until the hand holds ten cards. Exhaust. Upgraded it has Retain too.
        r.Card(new CardDef("Scrawl", "Scrawl", CardType.Skill, CardRarity.Rare, 1, TargetType.Self)
        {
            Keywords = CardKeywords.Exhaust,
            Upgrade = c => c.Keywords |= CardKeywords.Retain,
            Text = _ => "Draw cards until your hand is full.",
            Play = c => c.Draw(Fight.HandLimit - c.F.Hand.Count),
        });
    }

    private static void LeftOut(Registry r)
    {
        foreach (var name in MultiplayerOnly)
            r.Exclude(ContentKind.Card, name, "multiplayer only: the game takes it out of every single-player list of the colorless pool");

        r.Exclude(ContentKind.Card, "Splash", "it offers Attacks of the other four characters, whose cards the arena does not have");

        // A pick that lands on one of these is drawn again; in a strict run it ends the run (CardPools.Pick).
        foreach (var name in WaitingForColorlessB)
            r.Exclude(ContentKind.Card, name, "not built yet: it comes with run-mode chunk 27 (colorless-b)");
        foreach (var name in WaitingForColorlessC)
            r.Exclude(ContentKind.Card, name, "not built yet: it comes with run-mode chunk 28 (colorless-c-and-users)");
    }
}
