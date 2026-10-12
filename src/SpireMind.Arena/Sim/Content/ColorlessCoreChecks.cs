using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// Scripted checks for the colorless cards of <see cref="ColorlessCore"/> (run mode, chunk 13).
/// Every number is worked out by hand from the game's card classes
/// (MegaCrit.Sts2.Core.Models.Cards) and the rules the fight checks already hold: a hit loses
/// its fraction when it lands, Strength adds per hit, Vulnerable is x1.5, Weak and Frail x0.75,
/// Dexterity adds to a card's Block. The pool's lists below are typed in from the game's
/// ColorlessCardPool and each card's own class, a second time and apart from the generated
/// <c>CardPoolLists</c>. Where a pick is random, a second stream of the same seed is walked by
/// the rule in words. The shelves of six real shops are from the game's own run records
/// (sts2-notes/golden-saves/history, v0.107.1).
/// <para>
/// Unless a check says otherwise a fight is the starter deck in a fixed order against one idle
/// 100 HP dummy, with the hand replaced by the cards the check names; the draw pile is then
/// Strike, Strike, Strike, Defend, Defend from the top, and the player has 3 energy.
/// </para>
/// </summary>
public sealed class ColorlessCoreChecks : IArenaChecks
{
    // ---------- the game's lists, typed in from its files ----------

    /// <summary>ColorlessCardPool.GenerateAllCards, in its order.</summary>
    private static readonly string[] Pool =
    [
        "Alchemize", "Anointed", "Automation", "BeaconOfHope", "BeatDown", "BelieveInYou", "Bolas", "Calamity", "Catastrophe", "Coordinate",
        "DarkShackles", "Discovery", "DramaticEntrance", "Entropy", "Equilibrium", "EternalArmor", "Fasten", "Finesse", "Fisticuffs", "FlashOfSteel",
        "GangUp", "GoldAxe", "HandOfGreed", "HiddenGem", "HuddleUp", "Impatience", "Intercept", "JackOfAllTrades", "Jackpot", "Knockdown",
        "Lift", "MasterOfStrategy", "Mayhem", "Mimic", "MindBlast", "Nostalgia", "Omnislice", "Panache", "PanicButton", "PrepTime",
        "Production", "Prolong", "Prowess", "Purity", "Rally", "Rend", "Restlessness", "RollingBoulder", "Salvo", "Scrawl",
        "SecretTechnique", "SecretWeapon", "SeekerStrike", "Shockwave", "Splash", "Stratagem", "TagTeam", "TheBomb", "TheGambit", "ThinkingAhead",
        "ThrummingHatchet", "UltimateDefend", "UltimateStrike", "Volley",
    ];

    /// <summary>The cards whose class says multiplayer only.</summary>
    private static readonly string[] MultiplayerOnly =
        ["BeaconOfHope", "BelieveInYou", "Coordinate", "GangUp", "HuddleUp", "Intercept", "Knockdown", "Lift", "Mimic", "Rally", "TagTeam"];

    /// <summary>The single-player cards whose class says Rare, in pool order; every other single-player card is Uncommon.</summary>
    private static readonly string[] Rares =
    [
        "Alchemize", "Anointed", "BeatDown", "Bolas", "Calamity", "Entropy", "EternalArmor", "GoldAxe", "HandOfGreed", "HiddenGem", "Jackpot",
        "MasterOfStrategy", "Mayhem", "Nostalgia", "Rend", "RollingBoulder", "Salvo", "Scrawl", "SecretTechnique", "SecretWeapon", "TheGambit",
    ];

    /// <summary>Chunk 13's twelve (chunks.json), in pool order.</summary>
    private static readonly string[] Built =
    [
        "DramaticEntrance", "Finesse", "Fisticuffs", "GoldAxe", "Impatience", "MasterOfStrategy",
        "MindBlast", "Salvo", "Scrawl", "UltimateDefend", "UltimateStrike", "Volley",
    ];

    /// <summary>The cards of chunk 27, colorless-b (chunks.json).</summary>
    private static readonly string[] ColorlessB =
    [
        "Alchemize", "Anointed", "Automation", "BeatDown", "Bolas", "Calamity", "Catastrophe", "DarkShackles", "Discovery", "Entropy",
        "Equilibrium", "EternalArmor", "Fasten", "FlashOfSteel", "HandOfGreed", "HiddenGem", "JackOfAllTrades", "Jackpot", "Mayhem", "Nostalgia",
    ];

    /// <summary>The cards of chunk 28, colorless-c-and-users (chunks.json).</summary>
    private static readonly string[] ColorlessC =
    [
        "Omnislice", "Panache", "PanicButton", "PrepTime", "Production", "Prolong", "Prowess", "Purity", "Rend", "Restlessness",
        "RollingBoulder", "SecretTechnique", "SecretWeapon", "SeekerStrike", "Shockwave", "Stratagem", "TheBomb", "TheGambit", "ThinkingAhead", "ThrummingHatchet",
    ];

    /// <summary>The 53 cards a single-player run can meet, in pool order.</summary>
    private static List<string> SinglePlayer() => Pool.Where(name => !MultiplayerOnly.Contains(name)).ToList();

    /// <summary>The 32 single-player Uncommons, in pool order.</summary>
    private static List<string> Uncommons() => SinglePlayer().Where(name => !Rares.Contains(name)).ToList();

    // ---------- set-up helpers ----------

    private static void SetHand(Fight f, string[] hand)
    {
        f.Clear(PileType.Hand);
        foreach (var card in hand) f.Put(card, PileType.Hand);
    }

    /// <summary>One idle 100 HP dummy; the hand is exactly these cards.</summary>
    private static Fight Solo(params string[] hand) => Vs(new CheckDummy(), hand);

    private static Fight Vs(CheckDummy dummy, params string[] hand)
    {
        var f = CheckKit.VsDummy(dummy);
        SetHand(f, hand);
        return f;
    }

    /// <summary>Two idle dummies, e0 "Left" and e1 "Right".</summary>
    private static Fight Duo(int leftHp, int rightHp, params string[] hand)
    {
        var f = CheckKit.VsDummies(new CheckDummy { Hp = leftHp, DisplayName = "Left" }, new CheckDummy { Hp = rightHp, DisplayName = "Right" });
        SetHand(f, hand);
        return f;
    }

    private static string HandText(Fight f, int index = 0) => Fight.CardText(f, f.Hand[index], PileType.Hand);

    private static string? Versus(Fight f, int handIndex, int enemy) =>
        (string?)f.Snapshot().State["hand"]?[handIndex]?["vs"]?[$"e{enemy}"];

    private static bool HasVersus(Fight f, int handIndex) => f.Snapshot().State["hand"]?[handIndex]?["vs"] is not null;

    private static IEnumerable<string> Titles(IReadOnlyList<CardInstance> pile) => pile.Select(card => card.Title);

    private static int Finished(Fight f) => f.History.Count(e => e.Kind == HistoryKind.CardPlayFinished);

    private static List<HistoryEntry> Entries(Fight f, HistoryKind kind) => f.History.Entries.Where(e => e.Kind == kind).ToList();

    private static int IndexOfLast(Fight f, Func<HistoryEntry, bool> match)
    {
        var i = 0;
        var found = -1;
        foreach (var e in f.History.Entries)
        {
            if (match(e)) found = i;
            i++;
        }
        return found;
    }

    public void Run(Check c)
    {
        Registered(c);
        ThePool(c);
        FightsStillGenerateIroncladCards(c);
        DramaticEntrance(c);
        Finesse(c);
        Fisticuffs(c);
        Impatience(c);
        MindBlast(c);
        UltimateDefendAndStrike(c);
        Volley(c);
        GoldAxe(c);
        MasterOfStrategy(c);
        Salvo(c);
        Scrawl(c);
        LiveFights(c);
        ShopSlots(c);
        RealShops(c);
        Transforms(c);
        InARun(c);
    }

    // ---------- what is registered ----------

    private sealed record Row(string Class, string Title, CardType Type, CardRarity Rarity, int Cost, int UpgradedCost, TargetType Target,
        CardKeywords Keywords, CardKeywords UpgradedKeywords, CardTags Tags, string Text, string UpgradedText);

    private const int X = CardDef.CostX;
    private const CardKeywords None = CardKeywords.None;

    private const string FisticuffsTail = " Gain Block equal to the damage dealt (blocked and overkill damage count; Dexterity and Frail then change the Block).";
    private const string SalvoTail = " Keep your hand at the end of this turn (Ethereal cards are still exhausted).";

    /// <summary>The twelve cards as their game classes have them: constructor arguments, keywords, tags, numbers plain and upgraded.</summary>
    private static readonly Row[] Rows =
    [
        new("DramaticEntrance", "Dramatic Entrance", CardType.Attack, CardRarity.Uncommon, 0, 0, TargetType.AllEnemies,
            CardKeywords.Innate | CardKeywords.Exhaust, CardKeywords.Innate | CardKeywords.Exhaust, CardTags.None,
            "Innate. / Deal 11 damage to ALL enemies. / Exhaust.", "Innate. / Deal 15 damage to ALL enemies. / Exhaust."),
        new("Finesse", "Finesse", CardType.Skill, CardRarity.Uncommon, 0, 0, TargetType.Self, None, None, CardTags.None,
            "Gain 4 Block. Draw 1 card.", "Gain 7 Block. Draw 1 card."),
        new("Fisticuffs", "Fisticuffs", CardType.Attack, CardRarity.Uncommon, 1, 1, TargetType.AnyEnemy, None, None, CardTags.None,
            "Deal 7 damage." + FisticuffsTail, "Deal 9 damage." + FisticuffsTail),
        new("GoldAxe", "Gold Axe", CardType.Attack, CardRarity.Rare, 1, 1, TargetType.AnyEnemy, None, CardKeywords.Retain, CardTags.None,
            "Deal damage equal to the number of cards played this combat.", "Retain. / Deal damage equal to the number of cards played this combat."),
        new("Impatience", "Impatience", CardType.Skill, CardRarity.Uncommon, 0, 0, TargetType.Self, None, None, CardTags.None,
            "If you have no Attacks in your hand, draw 2 cards.", "If you have no Attacks in your hand, draw 3 cards."),
        new("MasterOfStrategy", "Master of Strategy", CardType.Skill, CardRarity.Rare, 0, 0, TargetType.Self, CardKeywords.Exhaust, CardKeywords.Exhaust, CardTags.None,
            "Draw 3 cards. / Exhaust.", "Draw 4 cards. / Exhaust."),
        new("MindBlast", "Mind Blast", CardType.Attack, CardRarity.Uncommon, 1, 0, TargetType.AnyEnemy, CardKeywords.Innate, CardKeywords.Innate, CardTags.None,
            "Innate. / Deal damage equal to the number of cards in your draw pile.", "Innate. / Deal damage equal to the number of cards in your draw pile."),
        new("Salvo", "Salvo", CardType.Attack, CardRarity.Rare, 1, 1, TargetType.AnyEnemy, None, None, CardTags.None,
            "Deal 12 damage." + SalvoTail, "Deal 16 damage." + SalvoTail),
        new("Scrawl", "Scrawl", CardType.Skill, CardRarity.Rare, 1, 1, TargetType.Self, CardKeywords.Exhaust, CardKeywords.Exhaust | CardKeywords.Retain, CardTags.None,
            "Draw cards until your hand is full. / Exhaust.", "Retain. / Draw cards until your hand is full. / Exhaust."),
        new("UltimateDefend", "Ultimate Defend", CardType.Skill, CardRarity.Uncommon, 1, 1, TargetType.Self, None, None, CardTags.Defend,
            "Gain 11 Block.", "Gain 15 Block."),
        new("UltimateStrike", "Ultimate Strike", CardType.Attack, CardRarity.Uncommon, 1, 1, TargetType.AnyEnemy, None, None, CardTags.Strike,
            "Deal 14 damage.", "Deal 20 damage."),
        new("Volley", "Volley", CardType.Attack, CardRarity.Uncommon, X, X, TargetType.RandomEnemy, None, None, CardTags.None,
            "Deal 10 damage to a random enemy X times.", "Deal 14 damage to a random enemy X times."),
    ];

    private static void Registered(Check c)
    {
        c.Group("the twelve cards are registered with the game's title, type, rarity, cost, target, keywords and tags", () =>
        {
            var registry = Content.Registry;
            var f = Solo();
            c.Sequence(Built, Rows.Select(row => row.Class), "one row per card of the chunk, in pool order");
            foreach (var row in Rows)
            {
                var def = registry.Card(row.Class);
                c.Equal("ColorlessCore", registry.SourceOf(ContentKind.Card, row.Class), $"{row.Class}: registered by this file");
                c.Equal(row.Title, def.Name, $"{row.Class}: title");
                c.Equal(row.Type, def.Type, $"{row.Class}: type");
                c.Equal(row.Rarity, def.Rarity, $"{row.Class}: rarity");
                c.Equal(row.Target, def.Target, $"{row.Class}: target");
                c.Equal(row.Cost == X, def.CostsX, $"{row.Class}: X cost");
                if (row.Cost != X) c.Equal(row.Cost, def.Cost, $"{row.Class}: cost");
                c.Equal(row.Tags, def.Tags, $"{row.Class}: tags");
                c.Equal(1, def.MaxUpgradeLevel, $"{row.Class}: can be upgraded once");
                c.That(def.CanBeGeneratedInCombat, $"{row.Class}: its class does not bar it from being made in a fight");

                // The pool's own entry, read from the game's files by script, says the same.
                var entry = CardPools.Colorless.Find(row.Class);
                c.Equal(CardPools.Colorless, CardPools.Of(row.Class), $"{row.Class}: a card of the colorless pool");
                c.That(entry is { MultiplayerOnly: false, GeneratedInCombat: true, GeneratedByModifiers: true } && entry.Type == row.Type && entry.Rarity == row.Rarity,
                    $"{row.Class}: the pool's entry has the same type and rarity, and no flag set");
                c.Equal(Rares.Contains(row.Class) ? CardRarity.Rare : CardRarity.Uncommon, def.Rarity, $"{row.Class}: the rarity its class names");

                var plain = f.CreateCard(row.Class);
                var upgraded = f.CreateCard(row.Class, upgraded: true);
                c.Equal(row.Text, Fight.CardText(null, plain, PileType.Deck), $"{row.Class}: text");
                c.Equal(row.UpgradedText, Fight.CardText(null, upgraded, PileType.Deck), $"{row.Class}+: text");
                c.Equal(row.Title + "+", upgraded.Title, $"{row.Class}+: title");
                c.Equal(row.Keywords, plain.Keywords, $"{row.Class}: keywords");
                c.Equal(row.UpgradedKeywords, upgraded.Keywords, $"{row.Class}+: keywords");
                c.That(plain.HasTag(CardTags.Strike) == (row.Tags == CardTags.Strike) && plain.HasTag(CardTags.Defend) == (row.Tags == CardTags.Defend), $"{row.Class}: Strike and Defend tags of an instance");
                if (row.Cost != X)
                {
                    c.Equal(row.Cost, plain.BaseCost, $"{row.Class}: cost of an instance");
                    c.Equal(row.UpgradedCost, upgraded.BaseCost, $"{row.Class}+: cost");
                }
            }
            c.Equal(8, Rows.Count(row => row.Rarity == CardRarity.Uncommon), "eight Uncommon");
            c.Equal(4, Rows.Count(row => row.Rarity == CardRarity.Rare), "four Rare");
            c.Sequence(["Finesse", "Fisticuffs", "UltimateDefend"], ColorlessCore.GainsBlock, "the three whose class says it gains Block");
        });

        c.Group("a colorless card on offer reads like any card, with its rarity", () =>
        {
            var state = new RunState(new RunSetup());
            c.Equal("""{"name":"Gold Axe","cost":"1","type":"Attack","text":"Deal damage equal to the number of cards played this combat.","rarity":"Rare"}""",
                DeckCheckKit.Text(RunDescribe.CardOffer(state.NewCard(Content.Card("GoldAxe")))), "Gold Axe as a shop or reward entry");
            c.Equal("""{"name":"Volley+","cost":"X","type":"Attack","text":"Deal 14 damage to a random enemy X times.","rarity":"Uncommon"}""",
                DeckCheckKit.Text(RunDescribe.CardOffer(state.NewCard(Content.Card("Volley"), upgraded: true))), "an upgraded X-cost card");
            c.Equal("""{"name":"Mind Blast+","cost":"0","type":"Attack","text":"Innate. / Deal damage equal to the number of cards in your draw pile.","rarity":"Uncommon"}""",
                DeckCheckKit.Text(RunDescribe.CardOffer(state.NewCard(Content.Card("MindBlast"), upgraded: true))), "Mind Blast upgraded costs 0");
        });
    }

    // ---------- the pool ----------

    private static void ThePool(Check c)
    {
        c.Group("the colorless pool: 64 cards, 53 in a single-player run, twelve built and 52 left out by name", () =>
        {
            var registry = Content.Registry;
            c.Equal(64, Pool.Length, "64 cards in the game's pool");
            c.Sequence(Pool, CardPools.Colorless.All.Select(card => card.ClassName), "the pool object lists them in the game's order");
            c.Sequence(MultiplayerOnly, CardPools.Colorless.All.Where(card => card.MultiplayerOnly).Select(card => card.ClassName), "the eleven multiplayer-only cards");

            var unlocked = CardPools.Colorless.Unlocked(RunUnlocks.All);
            c.Sequence(SinglePlayer(), unlocked.Select(card => card.ClassName), "the 53 of a single-player run");
            c.Equal(32, Uncommons().Count, "32 of them Uncommon");
            c.Equal(21, Rares.Length, "21 Rare");
            c.Sequence(Uncommons(), unlocked.Where(card => card.Rarity == CardRarity.Uncommon).Select(card => card.ClassName), "the Uncommons a shop's first colorless slot picks among");
            c.Sequence(Rares, unlocked.Where(card => card.Rarity == CardRarity.Rare).Select(card => card.ClassName), "the Rares its second slot picks among");

            c.Sequence(Built, CardPools.Colorless.Built.Select(card => card.ClassName), "the built cards of the pool are chunk 13's twelve");
            c.Sequence(Built, ColorlessCore.Cards, "the module's own list of them");
            c.Sequence(["DramaticEntrance", "Finesse", "Fisticuffs", "Impatience", "MindBlast", "UltimateDefend", "UltimateStrike", "Volley"],
                Uncommons().Where(name => registry.Has(ContentKind.Card, name)), "8 of the 32 Uncommons are built: a shop's Uncommon slot can always be filled");
            c.Sequence(["GoldAxe", "MasterOfStrategy", "Salvo", "Scrawl"],
                Rares.Where(name => registry.Has(ContentKind.Card, name)), "4 of the 21 Rares are built: so can its Rare slot");

            // Every other card of the pool is left out by this file, by name, with the reason.
            void LeftOut(IEnumerable<string> names, string because, string what)
            {
                foreach (var name in names)
                {
                    c.That(!registry.Has(ContentKind.Card, name), $"{name}: not a card of the arena");
                    c.That(registry.IsExcluded(ContentKind.Card, name, out var why) && why.Contains(because, StringComparison.Ordinal), $"{name}: left out, and the reason says {what}");
                    c.Equal("ColorlessCore", registry.SourceOf(ContentKind.Card, name), $"{name}: left out by this file");
                }
            }
            LeftOut(MultiplayerOnly, "multiplayer only", "multiplayer only");
            LeftOut(["Splash"], "other four characters", "it needs the other characters' cards");
            LeftOut(ColorlessB, "chunk 27", "which chunk builds it");
            LeftOut(ColorlessC, "chunk 28", "which chunk builds it");
            c.Sequence(MultiplayerOnly, ColorlessCore.MultiplayerOnly, "the module's multiplayer-only list");
            c.Sequence(ColorlessB, ColorlessCore.WaitingForColorlessB, "the module's list for chunk 27");
            c.Sequence(ColorlessC, ColorlessCore.WaitingForColorlessC, "the module's list for chunk 28");

            var named = Built.Concat(MultiplayerOnly).Append("Splash").Concat(ColorlessB).Concat(ColorlessC).ToList();
            c.Equal(64, named.Count, "12 built + 11 multiplayer only + Splash + 20 + 20");
            c.Sequence(Pool.Order(StringComparer.Ordinal), named.Order(StringComparer.Ordinal), "together they are the pool, each card once");
        });

        c.Group("the self-check's completeness list covers the colorless pool", () =>
        {
            var rows = Completeness.Rows().Where(row => row.Kind == ContentKind.Card && row.Wanted.Contains("Finesse")).ToList();
            c.Equal(1, rows.Count, "one row of Sim/Completeness.cs wants the colorless cards");
            if (rows.Count == 1)
            {
                c.Sequence(Pool, rows[0].Wanted, "it wants the whole pool");
                c.Sequence(Built, rows[0].Registered, "twelve registered");
                c.Equal(52, rows[0].LeftOut.Count, "52 left out");
                c.Equal(0, rows[0].Missing.Count, "none missing");
            }
            // Only what concerns this pool: another pool's holes are that pool's to report.
            var complaints = Completeness.Problems()
                .Where(problem => problem.StartsWith("random card generation", StringComparison.Ordinal) || Pool.Any(name => problem.Contains($" {name} ", StringComparison.Ordinal)))
                .ToList();
            c.Sequence([], complaints, "and the list has no complaint about it: no colorless card is unlisted or missing, and a fight generates from the game's 78 cards");
        });
    }

    private static void FightsStillGenerateIroncladCards(Check c)
    {
        c.Group("a fight's own card generation picks from the Ironclad's cards alone, as before", () =>
        {
            var registry = Content.Registry;
            var pool = registry.IroncladPool;
            c.Equal(85, pool.Count, "the Ironclad's 85 built cards");
            c.That(pool.All(def => !Pool.Contains(def.ClassName)), "no colorless card is in the list a fight generates from");
            c.Sequence(Completeness.IroncladPool.Where(name => registry.Has(ContentKind.Card, name)), pool.Select(def => def.ClassName), "it is the Ironclad's pool, in its order");

            var generatable = pool.Where(def => def.IsGeneratableInCombat).ToList();
            c.Equal((78, 33, 27, 18),
                (generatable.Count, generatable.Count(d => d.Type == CardType.Attack), generatable.Count(d => d.Type == CardType.Skill), generatable.Count(d => d.Type == CardType.Power)),
                "78 cards a fight may generate: 33 Attacks, 27 Skills, 18 Powers");
            foreach (var type in new[] { CardType.Attack, CardType.Skill, CardType.Power })
                c.That(Potions.CardPotionPool(type).All(def => !Pool.Contains(def.ClassName)), $"the {type} Potion's cards");
            c.That(Scenarios.Catalog.FromRegistry().RewardCards.All(card => !Pool.Contains(card.ClassName)), "the single fights' reward cards");

            // Infernal Blade adds a random Attack of the character's pool to the hand, forty fights over.
            for (var seed = 1; seed <= 40; seed++)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new CheckDummy()).Seed(seed).Start();
                SetHand(f, ["InfernalBlade"]);
                f.Play("Infernal Blade");
                var made = f.Hand.Single().Def;
                c.That(made.Type == CardType.Attack && Completeness.IroncladPool.Contains(made.ClassName), $"seed {seed}: Infernal Blade made the Ironclad Attack {made.ClassName}");
            }
        });
    }

    // ---------- Dramatic Entrance ----------

    private static void DramaticEntrance(Check c)
    {
        c.Group("Dramatic Entrance: 11 damage to every enemy for 0, then it is exhausted", () =>
        {
            var f = Duo(30, 40, "DramaticEntrance");
            f.Play("Dramatic Entrance");
            c.Equal(19, f.Enemy(0).Hp, "30 - 11");
            c.Equal(29, f.Enemy(1).Hp, "40 - 11");
            c.Equal(3, f.Energy, "costs 0");
            c.Sequence(["Dramatic Entrance"], Titles(f.ExhaustPile), "Exhaust");
            c.That(f.Hand.Count == 0 && f.DiscardPile.Count == 0, "and nowhere else");

            var up = Duo(30, 40, "DramaticEntrance+");
            up.Play("Dramatic Entrance+");
            c.Equal((15, 25), (up.Enemy(0).Hp, up.Enemy(1).Hp), "upgraded: 15 to each");
        });

        c.Group("Dramatic Entrance: Strength adds to the hit on each enemy, Vulnerable multiplies it on that enemy alone", () =>
        {
            var f = Duo(30, 40, "DramaticEntrance");
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            c.Equal("Innate. / Deal 14 damage to ALL enemies. / Exhaust.", HandText(f), "the hand text with 3 Strength");
            c.Equal("Innate. / Deal 21 damage to ALL enemies. / Exhaust.", Versus(f, 0, 0), "against the Vulnerable enemy: 14 x 1.5");
            c.Equal(null, Versus(f, 0, 1), "against the other the text at rest holds");
            f.Play("Dramatic Entrance");
            c.Equal((9, 26), (f.Enemy(0).Hp, f.Enemy(1).Hp), "30 - 21 and 40 - 14");
        });

        c.Group("Dramatic Entrance is Innate: it is in the opening hand whatever the shuffle", () =>
        {
            for (var seed = 1; seed <= 20; seed++)
            {
                var f = new FightBuilder().Deck([.. CheckKit.StarterInOrder, "DramaticEntrance"]).Enemy(new CheckDummy()).Seed(seed).Start();
                c.That(f.Hand.Count == 5 && f.HandTitles().Contains("Dramatic Entrance"), $"seed {seed}: among the five cards of turn 1 ({string.Join(", ", f.HandTitles())})");
            }
        });
    }

    // ---------- Finesse ----------

    private static void Finesse(Check c)
    {
        c.Group("Finesse: 4 Block, then 1 card, for 0", () =>
        {
            var f = Solo("Finesse");
            f.Play("Finesse");
            c.Equal(4, f.Player.Block, "4 Block");
            c.Sequence(["Strike"], f.HandTitles(), "the top card of the draw pile");
            c.Equal(4, f.DrawPile.Count, "one card fewer in the draw pile");
            c.Equal(3, f.Energy, "costs 0");
            c.Sequence(["Finesse"], Titles(f.DiscardPile), "it goes to the discard pile");
            var block = IndexOfLast(f, e => e.Kind == HistoryKind.BlockGained);
            var draw = IndexOfLast(f, e => e.Kind == HistoryKind.CardDrawn);
            c.That(block >= 0 && block < draw, "the Block comes before the draw");

            var up = Solo("Finesse+");
            up.Play("Finesse+");
            c.Equal(7, up.Player.Block, "upgraded: 7 Block");
            c.Equal(1, up.Hand.Count, "still 1 card");
        });

        c.Group("Finesse: Dexterity adds to its Block and Frail cuts it", () =>
        {
            var dex = Solo("Finesse");
            dex.ApplyPower<DexterityPower>(dex.Player, 2, dex.Player);
            c.Equal("Gain 6 Block. Draw 1 card.", HandText(dex), "the hand text with 2 Dexterity");
            dex.Play("Finesse");
            c.Equal(6, dex.Player.Block, "4 + 2");

            var frail = Solo("Finesse");
            frail.ApplyPower<FrailPower>(frail.Player, 1, frail.Enemy(0));
            frail.Play("Finesse");
            c.Equal(3, frail.Player.Block, "4 x 0.75");

            var both = Solo("Finesse+");
            both.ApplyPower<DexterityPower>(both.Player, 2, both.Player);
            both.ApplyPower<FrailPower>(both.Player, 1, both.Enemy(0));
            both.Play("Finesse+");
            c.Equal(6, both.Player.Block, "(7 + 2) x 0.75 = 6.75, cut to 6");
        });
    }

    // ---------- Fisticuffs ----------

    private static void Fisticuffs(Check c)
    {
        c.Group("Fisticuffs: 7 damage, then that much Block", () =>
        {
            var f = Solo("Fisticuffs");
            c.Equal("Deal 7 damage." + FisticuffsTail, HandText(f), "the hand text");
            f.Play("Fisticuffs", 0);
            c.Equal(93, f.Enemy(0).Hp, "7 damage");
            c.Equal(7, f.Player.Block, "7 Block");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Fisticuffs"], Titles(f.DiscardPile), "to the discard pile");
            var hit = IndexOfLast(f, e => e.Kind == HistoryKind.DamageReceived);
            var block = IndexOfLast(f, e => e.Kind == HistoryKind.BlockGained);
            c.That(hit >= 0 && hit < block, "the hit comes before the Block");

            var up = Solo("Fisticuffs+");
            up.Play("Fisticuffs+", 0);
            c.Equal((91, 9), (up.Enemy(0).Hp, up.Player.Block), "upgraded: 9 damage, 9 Block");
        });

        c.Group("Fisticuffs: damage the enemy's Block absorbs counts, and so does damage beyond its last HP", () =>
        {
            var partly = Solo("Fisticuffs");
            partly.SetBlock(partly.Enemy(0), 5);
            partly.Play("Fisticuffs", 0);
            c.Equal((98, 0, 7), (partly.Enemy(0).Hp, partly.Enemy(0).Block, partly.Player.Block), "5 blocked and 2 through: 7 Block");

            var wholly = Solo("Fisticuffs");
            wholly.SetBlock(wholly.Enemy(0), 20);
            wholly.Play("Fisticuffs", 0);
            c.Equal((100, 13, 7), (wholly.Enemy(0).Hp, wholly.Enemy(0).Block, wholly.Player.Block), "all 7 blocked: still 7 Block");

            // Left has 3 HP: 3 are lost and 4 go beyond. Right keeps the fight going.
            var over = Duo(3, 50, "Fisticuffs");
            over.Play("Fisticuffs", 0);
            c.That(over.Enemy(0).IsDead && !over.IsOver, "the enemy is dead and the fight goes on");
            c.Equal(7, over.Player.Block, "3 lost and 4 overkill: 7 Block");
        });

        c.Group("Fisticuffs: the Block follows what the hit really did", () =>
        {
            var vulnerable = Solo("Fisticuffs");
            vulnerable.ApplyPower<VulnerablePower>(vulnerable.Enemy(0), 1, vulnerable.Player);
            c.Equal("Deal 10 damage." + FisticuffsTail, Versus(vulnerable, 0, 0), "against a Vulnerable enemy the text says 10");
            vulnerable.Play("Fisticuffs", 0);
            c.Equal((90, 10), (vulnerable.Enemy(0).Hp, vulnerable.Player.Block), "7 x 1.5 = 10.5: 10 HP lost, 10 Block");

            var strong = Solo("Fisticuffs");
            strong.ApplyPower<StrengthPower>(strong.Player, 3, strong.Player);
            c.Equal("Deal 10 damage." + FisticuffsTail, HandText(strong), "the hand text with 3 Strength");
            strong.Play("Fisticuffs", 0);
            c.Equal((90, 10), (strong.Enemy(0).Hp, strong.Player.Block), "3 Strength: 10 damage, 10 Block");

            var weak = Solo("Fisticuffs");
            weak.ApplyPower<WeakPower>(weak.Player, 1, weak.Enemy(0));
            weak.Play("Fisticuffs", 0);
            c.Equal((95, 5), (weak.Enemy(0).Hp, weak.Player.Block), "Weak: 7 x 0.75 = 5.25: 5 damage, 5 Block");

            // Slippery turns any HP loss into 1: the Block is the 1 HP that was lost, not the 7 of the card.
            var slippery = Solo("Fisticuffs");
            slippery.ApplyPower<SlipperyPower>(slippery.Enemy(0), 1, slippery.Enemy(0));
            slippery.Play("Fisticuffs", 0);
            c.Equal((99, 1), (slippery.Enemy(0).Hp, slippery.Player.Block), "a Slippery enemy loses 1 HP: 1 Block");
        });

        c.Group("Fisticuffs: its Block is a card's Block, so Dexterity adds to it and Frail cuts it", () =>
        {
            var dex = Solo("Fisticuffs");
            dex.ApplyPower<DexterityPower>(dex.Player, 2, dex.Player);
            dex.Play("Fisticuffs", 0);
            c.Equal((93, 9), (dex.Enemy(0).Hp, dex.Player.Block), "7 damage, 7 + 2 Block");

            var frail = Solo("Fisticuffs");
            frail.ApplyPower<FrailPower>(frail.Player, 1, frail.Enemy(0));
            frail.Play("Fisticuffs", 0);
            c.Equal((93, 5), (frail.Enemy(0).Hp, frail.Player.Block), "7 x 0.75 = 5.25: 5 Block");
        });

        c.Group("Fisticuffs played twice on an enemy the first run kills: the second run hits nothing and asks for 0 Block", () =>
        {
            // Left has 5 HP. Run 1: 5 lost and 2 overkill, 7 Block, plus 2 Dexterity. Run 2: no hit, 0 Block, plus 2 Dexterity.
            var f = Duo(5, 50, "Fisticuffs");
            f.ApplyPower<DexterityPower>(f.Player, 2, f.Player);
            f.ApplyPower("DuplicationPower", f.Player, 1, f.Player);
            f.Play("Fisticuffs", 0);
            c.That(f.Enemy(0).IsDead && f.Enemy(1).Hp == 50, "the second run did not move on to the other enemy");
            c.Sequence([9, 2], Entries(f, HistoryKind.BlockGained).Select(e => e.Amount), "two Block gains: 7 + 2, then 0 + 2");
            c.Equal(11, f.Player.Block, "11 Block in all");
            c.Equal(1, Entries(f, HistoryKind.DamageReceived).Count, "one hit");
        });

        c.Group("Fisticuffs that kills the last enemy wins the fight", () =>
        {
            var f = Vs(new CheckDummy { Hp = 5 }, "Fisticuffs");
            f.Play("Fisticuffs", 0);
            c.That(f.IsOver && f.Won, "won");
        });
    }

    // ---------- Impatience ----------

    private static void Impatience(Check c)
    {
        c.Group("Impatience: 2 cards when no Attack is in hand, nothing otherwise", () =>
        {
            var f = Solo("Impatience", "DefendIronclad");
            f.Play("Impatience");
            c.Sequence(["Defend", "Strike", "Strike"], f.HandTitles(), "no Attack in hand: the two top cards are drawn");
            c.Equal(3, f.Energy, "costs 0");
            c.Sequence(["Impatience"], Titles(f.DiscardPile), "to the discard pile");

            var held = Solo("Impatience", "StrikeIronclad");
            held.Play("Impatience");
            c.Sequence(["Strike"], held.HandTitles(), "an Attack in hand: nothing is drawn");
            c.Equal(5, held.DrawPile.Count, "the draw pile is untouched");
            c.Equal(1, Finished(held), "the card was played all the same");

            var up = Solo("Impatience+", "DefendIronclad");
            up.Play("Impatience+");
            c.Sequence(["Defend", "Strike", "Strike", "Strike"], up.HandTitles(), "upgraded: 3 cards");
        });

        c.Group("Impatience: only Attacks in the hand count, and the card itself has left the hand", () =>
        {
            var alone = Solo("Impatience");
            alone.Play("Impatience");
            c.Equal(2, alone.Hand.Count, "alone in hand: 2 cards");

            var others = Solo("Impatience", "Impatience", "Wound", "UltimateDefend");
            others.Play("Impatience");
            c.Equal(5, others.Hand.Count, "a Skill, a second Impatience and a Status card are no Attacks: 3 kept, 2 drawn");

            // Attacks anywhere else do not matter: the draw pile is three Strikes and two Defends.
            var drawn = Solo("Impatience", "Impatience");
            drawn.Play("Impatience");
            c.Sequence(["Impatience", "Strike", "Strike"], drawn.HandTitles(), "the first draws two Strikes");
            drawn.Play("Impatience");
            c.Sequence(["Strike", "Strike"], drawn.HandTitles(), "the second finds them in hand and draws nothing");
        });
    }

    // ---------- Mind Blast ----------

    private static void MindBlast(Check c)
    {
        c.Group("Mind Blast: damage equal to the cards in the draw pile", () =>
        {
            var f = Solo("MindBlast");
            c.Equal("Innate. / Deal damage equal to the number of cards in your draw pile (5 right now).", HandText(f), "the hand text with 5 cards in the draw pile");
            f.Play("Mind Blast", 0);
            c.Equal(95, f.Enemy(0).Hp, "5 damage");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Mind Blast"], Titles(f.DiscardPile), "to the discard pile");

            var up = Solo("MindBlast+");
            up.Play("Mind Blast+", 0);
            c.Equal((95, 3), (up.Enemy(0).Hp, up.Energy), "upgraded: the same damage for 0");

            var after = Solo("Finesse", "MindBlast");
            after.Play("Finesse");
            c.Equal("Innate. / Deal damage equal to the number of cards in your draw pile (4 right now).", HandText(after, 0), "one card was drawn: 4");
            after.Play("Mind Blast", 0);
            c.Equal(96, after.Enemy(0).Hp, "4 damage");
        });

        c.Group("Mind Blast: the count is the hit's size before Strength and Vulnerable", () =>
        {
            var f = Solo("MindBlast");
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            c.Equal("Innate. / Deal damage equal to the number of cards in your draw pile (7 right now).", HandText(f), "5 + 2 Strength");
            c.Equal("Innate. / Deal damage equal to the number of cards in your draw pile (10 right now).", Versus(f, 0, 0), "against the Vulnerable enemy: 7 x 1.5 = 10.5");
            f.Play("Mind Blast", 0);
            c.Equal(90, f.Enemy(0).Hp, "10 damage");
        });

        c.Group("Mind Blast with an empty draw pile is a hit of 0", () =>
        {
            var f = Solo("MindBlast");
            f.Clear(PileType.Draw);
            c.Equal("Innate. / Deal damage equal to the number of cards in your draw pile (0 right now).", HandText(f), "the hand text");
            f.Play("Mind Blast", 0);
            c.Equal(100, f.Enemy(0).Hp, "no damage");
            c.Sequence([1], Entries(f, HistoryKind.CreatureAttacked).Select(e => e.Amount), "the attack still made its one hit");
        });

        c.Group("Mind Blast is Innate: with it and the starter deck, turn 1 has it in hand and 6 cards in the draw pile", () =>
        {
            for (var seed = 1; seed <= 20; seed++)
            {
                var f = new FightBuilder().Deck([.. CheckKit.StarterInOrder, "MindBlast"]).Enemy(new CheckDummy()).Seed(seed).Start();
                var at = f.HandTitles().IndexOf("Mind Blast");
                c.That(at >= 0, $"seed {seed}: in the opening hand");
                if (at < 0) continue;
                c.Equal("Innate. / Deal damage equal to the number of cards in your draw pile (6 right now).", HandText(f, at), $"seed {seed}: 11 cards less the 5 in hand");
                f.Do($"play:{at}>e0");
                c.Equal(94, f.Enemy(0).Hp, $"seed {seed}: 6 damage");
            }
        });
    }

    // ---------- Ultimate Defend, Ultimate Strike ----------

    private static void UltimateDefendAndStrike(Check c)
    {
        c.Group("Ultimate Defend: 11 Block for 1", () =>
        {
            var f = Solo("UltimateDefend");
            f.Play("Ultimate Defend");
            c.Equal((11, 2), (f.Player.Block, f.Energy), "11 Block, costs 1");
            c.Sequence(["Ultimate Defend"], Titles(f.DiscardPile), "to the discard pile");

            var up = Solo("UltimateDefend+");
            up.ApplyPower<DexterityPower>(up.Player, 2, up.Player);
            c.Equal("Gain 17 Block.", HandText(up), "upgraded, with 2 Dexterity: the hand text");
            up.Play("Ultimate Defend+");
            c.Equal(17, up.Player.Block, "15 + 2");
        });

        c.Group("Ultimate Strike: 14 damage for 1", () =>
        {
            var f = Solo("UltimateStrike");
            f.Play("Ultimate Strike", 0);
            c.Equal((86, 2), (f.Enemy(0).Hp, f.Energy), "14 damage, costs 1");
            c.Sequence(["Ultimate Strike"], Titles(f.DiscardPile), "to the discard pile");

            var up = Solo("UltimateStrike+");
            up.ApplyPower<VulnerablePower>(up.Enemy(0), 1, up.Player);
            c.Equal("Deal 30 damage.", Versus(up, 0, 0), "upgraded, against a Vulnerable enemy: 20 x 1.5");
            up.Play("Ultimate Strike+", 0);
            c.Equal(70, up.Enemy(0).Hp, "30 damage");
        });

        c.Group("Ultimate Strike counts as a Strike: Perfected Strike gains 2 damage for it", () =>
        {
            // Perfected Strike is 6 plus 2 for each card with Strike in its name, in any pile. The piles hold three Strikes.
            var without = Solo("PerfectedStrike");
            without.Play("Perfected Strike", 0);
            c.Equal(86, without.Enemy(0).Hp, "itself and three Strikes: 6 + 2 x 4 = 14");

            var with = Solo("PerfectedStrike", "UltimateStrike", "UltimateDefend");
            with.Play("Perfected Strike", 0);
            c.Equal(84, with.Enemy(0).Hp, "with an Ultimate Strike in hand: 6 + 2 x 5 = 16 (the Ultimate Defend is no Strike)");
        });
    }

    // ---------- Volley ----------

    private static void Volley(Check c)
    {
        c.Group("Volley: X hits of 10 for all the energy", () =>
        {
            var f = Solo("Volley");
            var before = f.Rng.CombatTargets.Counter;
            c.Equal("X", (string?)f.Snapshot().State["hand"]?[0]?["cost"], "the cost reads X");
            f.Play("Volley");
            c.Equal(70, f.Enemy(0).Hp, "3 energy: 3 x 10");
            c.Equal(0, f.Energy, "all energy spent");
            c.Equal(before + 3, f.Rng.CombatTargets.Counter, "a target is drawn for every hit, also with one enemy");
            c.Sequence([3], Entries(f, HistoryKind.CreatureAttacked).Select(e => e.Amount), "one attack of three hits");
            c.Sequence(["Volley"], Titles(f.DiscardPile), "to the discard pile");

            var up = Solo("Volley+");
            up.SetEnergy(2);
            up.Play("Volley+");
            c.Equal((72, 0), (up.Enemy(0).Hp, up.Energy), "upgraded with 2 energy: 2 x 14");

            var strong = Solo("Volley");
            strong.ApplyPower<StrengthPower>(strong.Player, 2, strong.Player);
            c.Equal("Deal 12 damage to a random enemy X times.", HandText(strong), "the hand text with 2 Strength");
            strong.Play("Volley");
            c.Equal(64, strong.Enemy(0).Hp, "Strength adds to every hit: 3 x 12");
        });

        c.Group("Volley with no energy is an attack of no hits", () =>
        {
            var f = Solo("Volley");
            f.SetEnergy(0);
            var before = f.Rng.CombatTargets.Counter;
            f.Play("Volley");
            c.Equal(100, f.Enemy(0).Hp, "no damage");
            c.Equal(before, f.Rng.CombatTargets.Counter, "no target drawn");
            c.Sequence([0], Entries(f, HistoryKind.CreatureAttacked).Select(e => e.Amount), "the attack is made all the same, with no hit");
            c.Equal(1, Finished(f), "and the card was played");
        });

        c.Group("Volley: each hit picks one of the living enemies anew", () =>
        {
            // The rule on a second stream: per hit one number, an index into the enemies alive at that moment.
            var killed = 0;
            foreach (var (leftHp, rightHp) in new[] { (100, 100), (10, 100), (100, 10) })
            {
                var f = Duo(leftHp, rightHp, "Volley");
                f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
                c.That(!HasVersus(f, 0), $"{leftHp}/{rightHp}: a card that picks its own target has no text against an enemy");
                var twin = f.Rng.CombatTargets.Clone();
                var start = f.Rng.CombatTargets.Counter;
                var hp = new[] { leftHp, rightHp };
                for (var hit = 0; hit < 3; hit++)
                {
                    var alive = Enumerable.Range(0, 2).Where(i => hp[i] > 0).ToList();
                    var target = alive[(int)(twin.NextDouble() * alive.Count)];
                    hp[target] = Math.Max(0, hp[target] - (target == 0 ? 15 : 10));   // Left is Vulnerable: 10 x 1.5
                }
                f.Play("Volley");
                c.Equal((hp[0], hp[1]), (f.Enemy(0).Hp, f.Enemy(1).Hp), $"{leftHp}/{rightHp}: the hits landed where the stream sent them");
                c.Equal(start + 3, f.Rng.CombatTargets.Counter, $"{leftHp}/{rightHp}: three numbers");
                if (hp[0] == 0 || hp[1] == 0) killed++;
            }
            c.That(killed >= 1, "in at least one of the fights an enemy died in mid-volley and the later hits went to the other");
        });
    }

    // ---------- Gold Axe ----------

    private static void GoldAxe(Check c)
    {
        c.Group("Gold Axe: damage equal to the cards played so far in the fight", () =>
        {
            var f = Solo("DefendIronclad", "DefendIronclad", "GoldAxe");
            c.Equal("Deal damage equal to the number of cards played this combat (0 right now).", HandText(f, 2), "nothing played yet");
            f.Play("Defend");
            f.Play("Defend");
            c.Equal("Deal damage equal to the number of cards played this combat (2 right now).", HandText(f, 0), "after two cards");
            f.Play("Gold Axe", 0);
            c.Equal(98, f.Enemy(0).Hp, "2 damage");
            c.Equal(0, f.Energy, "costs 1");
            c.Sequence(["Defend", "Defend", "Gold Axe"], Titles(f.DiscardPile), "to the discard pile");

            var first = Solo("GoldAxe", "GoldAxe");
            first.Play("Gold Axe", 0);
            c.Equal(100, first.Enemy(0).Hp, "as the first card of the fight it deals 0: its own play is not finished while it hits");
            first.Play("Gold Axe", 0);
            c.Equal(99, first.Enemy(0).Hp, "the second one counts the first: 1");
        });

        c.Group("Gold Axe: Strength and Vulnerable work on the count", () =>
        {
            var f = Solo("DefendIronclad", "DefendIronclad", "GoldAxe");
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.Play("Defend");
            f.Play("Defend");
            c.Equal("Deal damage equal to the number of cards played this combat (5 right now).", HandText(f), "2 + 3 Strength");
            c.Equal("Deal damage equal to the number of cards played this combat (7 right now).", Versus(f, 0, 0), "against the Vulnerable enemy: 5 x 1.5 = 7.5");
            f.Play("Gold Axe", 0);
            c.Equal(93, f.Enemy(0).Hp, "7 damage");
        });

        c.Group("Gold Axe counts the whole fight, and upgraded it is kept in hand", () =>
        {
            var f = Solo("DefendIronclad", "GoldAxe+");
            f.Play("Defend");
            f.EndTurn();
            c.Sequence(["Gold Axe+", "Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "Retain: still in hand on turn 2, beside the five drawn");
            c.Equal("Retain. / Deal damage equal to the number of cards played this combat (1 right now).", HandText(f), "last turn's Defend still counts");
            f.Play("Strike", 0);
            f.Play("Gold Axe+", 0);
            c.Equal(92, f.Enemy(0).Hp, "100 - 6 for the Strike - 2 for the Gold Axe");

            var plain = Solo("DefendIronclad", "GoldAxe");
            plain.EndTurn();
            c.Sequence(["Strike", "Strike", "Strike", "Defend", "Defend"], plain.HandTitles(), "the plain one is discarded with the hand");
        });

        c.Group("Gold Axe: every run of a card's effect counts, a card played for free too", () =>
        {
            // Duplication plays the next card twice. Run 1 finds no finished play, run 2 finds run 1.
            var twice = Solo("GoldAxe");
            twice.ApplyPower("DuplicationPower", twice.Player, 1, twice.Player);
            twice.Play("Gold Axe", 0);
            c.Equal(99, twice.Enemy(0).Hp, "played twice as the first card: 0, then 1");
            c.Equal(2, Finished(twice), "two finished plays");

            // One Defend played twice is two plays, not one card.
            var doubled = Solo("DefendIronclad", "GoldAxe");
            doubled.ApplyPower("DuplicationPower", doubled.Player, 1, doubled.Player);
            doubled.Play("Defend");
            c.Equal(10, doubled.Player.Block, "the Defend ran twice");
            doubled.Play("Gold Axe", 0);
            c.Equal(98, doubled.Enemy(0).Hp, "both runs of the one Defend count: 2 damage");

            // Havoc plays the top Strike for free (6 damage); that play and Havoc's own are both finished.
            var havoc = Solo("Havoc", "GoldAxe");
            havoc.Play("Havoc");
            c.Equal(94, havoc.Enemy(0).Hp, "Havoc played a Strike");
            havoc.Play("Gold Axe", 0);
            c.Equal(92, havoc.Enemy(0).Hp, "the Strike and Havoc: 2 damage");
            c.Equal(1, havoc.Energy, "1 for Havoc, 1 for the Gold Axe");
        });
    }

    // ---------- Master of Strategy ----------

    private static void MasterOfStrategy(Check c)
    {
        c.Group("Master of Strategy: 3 cards for 0, then it is exhausted", () =>
        {
            var f = Solo("MasterOfStrategy");
            f.Play("Master of Strategy");
            c.Sequence(["Strike", "Strike", "Strike"], f.HandTitles(), "the three top cards");
            c.Equal(3, f.Energy, "costs 0");
            c.Sequence(["Master of Strategy"], Titles(f.ExhaustPile), "Exhaust");
            c.Equal(0, f.DiscardPile.Count, "not discarded");

            var up = Solo("MasterOfStrategy+");
            up.Play("Master of Strategy+");
            c.Sequence(["Strike", "Strike", "Strike", "Defend"], up.HandTitles(), "upgraded: 4 cards");
        });
    }

    // ---------- Salvo ----------

    private static void Salvo(Check c)
    {
        c.Group("Salvo: 12 damage, and the hand is kept at the end of this turn only", () =>
        {
            var f = Solo("Salvo", "DefendIronclad", "DefendIronclad");
            f.Play("Salvo", 0);
            c.Equal((88, 2), (f.Enemy(0).Hp, f.Energy), "12 damage, costs 1");
            c.Equal(1, f.Player.PowerAmount("RetainHandPower"), "1 Retain Hand on the player");
            c.Equal("Retain Hand", f.Player.GetPower("RetainHandPower")!.Name, "the power the Stable Serum gives");
            c.Sequence(["Salvo"], Titles(f.DiscardPile), "to the discard pile");

            f.EndTurn();
            c.Sequence(["Defend", "Defend", "Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "turn 2: the two cards kept, then the five drawn");
            c.Equal(0, f.Player.PowerAmount("RetainHandPower"), "the power is gone");
            f.EndTurn();
            c.Equal((5, 3, 0), (f.Hand.Count, f.DrawPile.Count, f.DiscardPile.Count), "turn 3: all seven were discarded as usual; the eight cards were shuffled and five drawn");

            var control = Solo("DefendIronclad", "DefendIronclad");
            control.EndTurn();
            c.Sequence(["Strike", "Strike", "Strike", "Defend", "Defend"], control.HandTitles(), "without Salvo the same hand is discarded");

            var up = Solo("Salvo+");
            up.Play("Salvo+", 0);
            c.Equal(84, up.Enemy(0).Hp, "upgraded: 16 damage");
        });

        c.Group("Salvo: an Ethereal card in the kept hand is still exhausted", () =>
        {
            var f = Solo("Salvo", "Dazed", "DefendIronclad");
            f.Play("Salvo", 0);
            f.EndTurn();
            c.Sequence(["Dazed"], Titles(f.ExhaustPile), "Dazed was exhausted at the end of the turn");
            c.Sequence(["Defend", "Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "the Defend was kept");
        });

        c.Group("Salvo: the hit comes first, and a killing hit on the last enemy ends the fight", () =>
        {
            var f = Duo(12, 50, "Salvo");
            f.Play("Salvo", 0);
            c.That(f.Enemy(0).IsDead && f.Player.PowerAmount("RetainHandPower") == 1, "with another enemy standing the power is still applied after the kill");

            var last = Vs(new CheckDummy { Hp = 12 }, "Salvo");
            last.Play("Salvo", 0);
            c.That(last.IsOver && last.Won, "won");
        });
    }

    // ---------- Scrawl ----------

    private static void Scrawl(Check c)
    {
        c.Group("Scrawl: draws until ten cards are in hand, then it is exhausted", () =>
        {
            var f = Solo("Scrawl", "DefendIronclad", "DefendIronclad");
            for (var i = 0; i < 10; i++) f.Put("StrikeIronclad", PileType.Draw);
            f.Play("Scrawl");
            c.Equal(10, f.Hand.Count, "2 cards in hand: 8 are drawn");
            c.Equal(7, f.DrawPile.Count, "15 less 8 in the draw pile");
            c.Equal(2, f.Energy, "costs 1");
            c.Sequence(["Scrawl"], Titles(f.ExhaustPile), "Exhaust");

            var nearlyFull = Solo("Scrawl", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad");
            nearlyFull.Play("Scrawl");
            c.Equal((10, 4), (nearlyFull.Hand.Count, nearlyFull.DrawPile.Count), "9 other cards in hand: 1 is drawn");

            var few = Solo("Scrawl");
            few.Play("Scrawl");
            c.Equal((5, 0), (few.Hand.Count, few.DrawPile.Count), "with five cards to draw and no discard pile it draws those five and stops");
        });

        c.Group("Scrawl draws through a reshuffle", () =>
        {
            // 4 cards in hand, 2 in the draw pile, 3 in the discard pile: 6 are wanted, 5 can come.
            var f = Solo("Scrawl", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad");
            f.Clear(PileType.Draw);
            for (var i = 0; i < 2; i++) f.Put("StrikeIronclad", PileType.Draw);
            for (var i = 0; i < 3; i++) f.Put("Bash", PileType.Discard);
            f.Play("Scrawl");
            c.Equal((9, 0, 0), (f.Hand.Count, f.DrawPile.Count, f.DiscardPile.Count), "the two, then the three of the discard pile shuffled in: 9 in hand");
            c.Sequence(["Defend", "Defend", "Defend", "Defend", "Strike", "Strike", "Bash", "Bash", "Bash"], f.HandTitles(), "in that order");
        });

        c.Group("Scrawl: a draw that is forbidden draws nothing, and upgraded it has Retain", () =>
        {
            // Battle Trance draws 3 and forbids further draws this turn.
            var f = Solo("BattleTrance", "Scrawl");
            f.Play("Battle Trance");
            f.Play("Scrawl");
            c.Equal(3, f.Hand.Count, "No Draw: Scrawl draws nothing");
            c.Sequence(["Scrawl"], Titles(f.ExhaustPile), "and is exhausted all the same");

            var kept = Solo("Scrawl+", "DefendIronclad");
            c.Equal("Retain. / Draw cards until your hand is full. / Exhaust.", HandText(kept), "the hand text");
            kept.EndTurn();
            c.Sequence(["Scrawl+", "Strike", "Strike", "Strike", "Defend", "Defend"], kept.HandTitles(), "kept in hand over the turn");
            kept.Play("Scrawl+");
            c.Sequence(["Scrawl+"], Titles(kept.ExhaustPile), "played, it is exhausted");

            var plain = Solo("Scrawl", "DefendIronclad");
            plain.EndTurn();
            c.That(!plain.HandTitles().Contains("Scrawl"), "the plain one is discarded with the hand");
        });
    }

    // ---------- live fights ----------

    private static readonly string[] LiveDeck =
    [
        "DramaticEntrance", "Finesse+", "Fisticuffs", "Impatience", "MindBlast+", "UltimateDefend", "UltimateStrike+", "Volley",
        "GoldAxe+", "MasterOfStrategy", "Salvo", "Scrawl+", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad",
    ];

    private static Fight LiveFight(int seed)
    {
        var builder = new FightBuilder().Deck(LiveDeck).Seed(seed);
        if (seed <= 24) return builder.Encounter("NibbitsNormal").Start();
        return builder
            .Enemy(new CheckDummy { Hp = 250, DisplayName = "Left", Steps = [new DummyStep("HIT", Damage: 3)] })
            .Enemy(new CheckDummy { Hp = 250, DisplayName = "Right", Steps = [new DummyStep("HIT", Damage: 3)] })
            .Start();
    }

    /// <summary>
    /// Every card of this file in real fights, picked by a fixed rule. Nothing may throw, a fight
    /// and its copy must show the same after every action (Gold Axe reads the fight's history,
    /// Volley its target stream), and the same ids must lead to the same end.
    /// </summary>
    private static void LiveFights(Check c)
    {
        c.Group("every card in live fights: a fight and its copy stay in step, and the ids replay to the same end", () =>
        {
            var played = new HashSet<string>(StringComparer.Ordinal);
            for (var seed = 1; seed <= 30; seed++)
            {
                var f = LiveFight(seed);
                var ids = new List<string>();
                for (var step = 0; step < 5000 && !f.IsOver; step++)
                {
                    var actions = f.LegalActions();
                    var pick = actions[(seed * 31 + step * 7) % actions.Count].Id;
                    if (pick.StartsWith("play:", StringComparison.Ordinal))
                    {
                        var arrow = pick.IndexOf('>');
                        played.Add(f.Hand[int.Parse(arrow < 0 ? pick[5..] : pick[5..arrow], System.Globalization.CultureInfo.InvariantCulture)].Def.ClassName);
                    }
                    var twin = f.Clone();
                    f.Do(pick);
                    twin.Do(pick);
                    ids.Add(pick);
                    if (f.Seen() == twin.Seen()) continue;
                    c.That(false, $"seed {seed}: after {string.Join(" ", ids)} the copy shows something else");
                    break;
                }
                c.That(f.IsOver, $"seed {seed}: the fight ended");

                var again = LiveFight(seed);
                foreach (var id in ids) again.Do(id);
                c.That(again.Seen() == f.Seen(), $"seed {seed}: replaying the ids ends the same way");
            }
            c.Sequence(Built, Built.Where(played.Contains), "each of the twelve was played in some fight");
        });
    }

    // ---------- a shop's colorless slots ----------

    private static readonly Dictionary<string, Action<Arena.Run, DeckCheckKit.Desk>> NoScript = new();

    /// <summary>A run that stands on floor 5 in the world of its seed, as in a shop: the shops stream moved on if asked.</summary>
    private sealed class Stand : RunDirector
    {
        public bool Strict { get; init; }
        public int ShopsDrawn { get; init; }

        public override void Begin(Arena.Run run)
        {
            var world = RunWorld.Begin(run.State, acts: ["Overgrowth", "Hive", "Glory"]);
            world.Streams.FastForward(RunStream.Shops, ShopsDrawn);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            run.State.TotalFloor = 5;
            world.Streams.MoveTo(0, 4, 0);
            run.Push(new DeckCheckKit.Desk(NoScript));
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    private static Arena.Run Standing(string seed, int shopsDrawn = 0, bool strict = false) =>
        new(new RunSetup { Seed = seed }, new Stand { ShopsDrawn = shopsDrawn, Strict = strict });

    private static int Counter(Arena.Run run, RunStream stream) => run.State.Part<RunWorld>().Streams.Counter(stream);

    private static GameRng ShopsStream(string seed) => new(RunStreams.LongSeed(new RunSeed(seed), RunStream.Shops));

    /// <summary>The shop's call for a colorless slot: the pool's cards of a single-player run, one rarity.</summary>
    private static CardInstance ColorlessSlot(Arena.Run run, CardRarity rarity, int slot) =>
        CardFactory.CreateForMerchant(run, CardPools.Colorless.Unlocked(CardPools.UnlocksOf(run)), rarity, Purpose.ShopCard(slot)).Card;

    private static List<string> LoggedCards(Arena.Run run) =>
        run.State.FindPart<LeftOutLog>()?.Entries.Select(e => $"{e.Floor}:{e.Kind}:{e.ClassName}:{e.What}").ToList() ?? [];

    private static void ShopSlots(Check c)
    {
        c.Group("a shop's two colorless slots: an index into the 32 Uncommons, then into the 21 Rares, each drawn again until it lands on a built card", () =>
        {
            var uncommons = Uncommons();
            var uncommonAtOnce = 0;
            var rareAtOnce = 0;
            var again = 0;
            string? firstStop = null;
            string? stopSeed = null;
            for (var n = 1; n <= 30; n++)
            {
                var seed = $"COLORLESS{n}";
                var run = Standing(seed);
                var twin = ShopsStream(seed);
                var passed = new List<string>();
                string Walk(IReadOnlyList<string> list)
                {
                    while (true)
                    {
                        var name = list[(int)(twin.NextDouble() * list.Count)];
                        if (Built.Contains(name)) return name;
                        passed.Add(name);
                    }
                }

                var expected = Walk(uncommons);
                if (passed.Count > 0 && firstStop is null) (firstStop, stopSeed) = (passed[0], seed);
                var uncommon = ColorlessSlot(run, CardRarity.Uncommon, 5);
                c.Equal(expected, uncommon.Def.ClassName, $"{seed}: the Uncommon slot");
                c.Equal(twin.Counter, Counter(run, RunStream.Shops), $"{seed}: one number of the shops stream per try");
                c.Equal(1, Counter(run, RunStream.Rewards), $"{seed}: one number of rewards, the upgrade roll that cannot succeed");

                var passedForUncommon = passed.Count;
                expected = Walk(Rares);
                var rare = ColorlessSlot(run, CardRarity.Rare, 6);
                c.Equal(expected, rare.Def.ClassName, $"{seed}: the Rare slot");
                c.Equal(twin.Counter, Counter(run, RunStream.Shops), $"{seed}: the shops stream after both");
                c.Equal(2, Counter(run, RunStream.Rewards), $"{seed}: two numbers of rewards after both");
                c.That(uncommon.Def.Rarity == CardRarity.Uncommon && rare.Def.Rarity == CardRarity.Rare && !uncommon.IsUpgraded && !rare.IsUpgraded, $"{seed}: of the rarity asked for, never upgraded");
                c.Sequence(passed.Select(name => $"5:card:{name}:a shop card"), LoggedCards(run), $"{seed}: the run's log has every card passed over");
                if (passedForUncommon == 0) uncommonAtOnce++;
                if (passed.Count == passedForUncommon) rareAtOnce++;
                if (passed.Count > 0) again++;
            }
            c.That(uncommonAtOnce >= 2 && rareAtOnce >= 2 && again >= 10,
                $"both ways came up: {again} of 30 shelves drew again; the Uncommon slot landed on a built card at once {uncommonAtOnce} times (8 of 32 are built), the Rare slot {rareAtOnce} times (4 of 21)");

            // Strict mode: the game's streams with nothing drawn again.
            c.That(stopSeed is not null, "some seed's Uncommon slot lands on an unbuilt card first");
            if (stopSeed is not null)
            {
                var strict = Standing(stopSeed, strict: true);
                var reason = "";
                try { ColorlessSlot(strict, CardRarity.Uncommon, 5); }
                catch (RunUnrepresentableException ex) { reason = ex.Message; }
                c.Equal($"a shop card on floor 5 landed on the card {firstStop}, which the arena leaves out", reason, $"{stopSeed}, strict: the run cannot go on, and says why");
            }
        });
    }

    /// <summary>
    /// The colorless cards six real shops showed (the game's own run records, v0.107.1, single
    /// player, every epoch revealed): the first shop of the run on seed 1BUSLN0FY5
    /// (history/1791755111.run) and the five shops of the run on seed 75F277YN2G
    /// (history/1791592972.run), in the order they were met.
    /// </summary>
    private static readonly (string Seed, int Shop, string Uncommon, string Rare)[] RealShelves =
    [
        ("1BUSLN0FY5", 0, "Discovery", "Calamity"),
        ("75F277YN2G", 0, "JackOfAllTrades", "SecretTechnique"),
        ("75F277YN2G", 1, "Equilibrium", "Entropy"),
        ("75F277YN2G", 2, "Impatience", "Entropy"),
        ("75F277YN2G", 3, "UltimateStrike", "TheGambit"),
        ("75F277YN2G", 4, "FlashOfSteel", "Jackpot"),
    ];

    /// <summary>
    /// The numbers of the shops stream one shop takes when nothing bought is restocked: 1 for the
    /// card on sale, 2 for each of the five character cards (pick, price), 1 for the second price
    /// of the card on sale, 2 for each colorless card, 3 relic prices, 2 for each of the three
    /// potions (rarity, pick) and 3 potion prices.
    /// </summary>
    private const int NumbersPerShop = 1 + 5 * 2 + 1 + 2 * 2 + 3 + 3 * 2 + 3;

    /// <summary>The numbers a shop takes before its Uncommon colorless pick: the sale, the five character cards, the sale's second price.</summary>
    private const int BeforeColorless = 1 + 5 * 2 + 1;

    private static void RealShops(Check c)
    {
        c.Group("six real shops: the colorless cards the game showed are the pool's Uncommons and Rares at the stream's index", () =>
        {
            // The shops stream is the player's own and only shops draw from it. The Uncommon pick is the number
            // after the 12 a shop takes first, the Rare pick comes after the Uncommon's price.
            c.Equal(28, NumbersPerShop, "28 numbers a shop");
            var uncommons = Uncommons();
            foreach (var (seed, shop, uncommon, rare) in RealShelves)
            {
                var stream = ShopsStream(seed);
                for (var i = 0; i < shop * NumbersPerShop + BeforeColorless; i++) stream.NextDouble();
                c.Equal(uncommon, uncommons[(int)(stream.NextDouble() * 32)], $"{seed}, shop {shop + 1}: the Uncommon colorless card");
                stream.NextDouble();
                c.Equal(rare, Rares[(int)(stream.NextDouble() * 21)], $"{seed}, shop {shop + 1}: the Rare colorless card");
            }
        });

        c.Group("at the real shops' places the arena shows the game's card where it is built, and says so where it is not", () =>
        {
            // The third shop of the run on seed 75F277YN2G showed Impatience, the fourth Ultimate Strike: both are built.
            foreach (var (shop, card) in new[] { (2, "Impatience"), (3, "UltimateStrike") })
            {
                var at = shop * NumbersPerShop + BeforeColorless;
                var run = Standing("75F277YN2G", shopsDrawn: at);
                c.Equal(card, ColorlessSlot(run, CardRarity.Uncommon, 5).Def.ClassName, $"shop {shop + 1}: the Uncommon slot is the game's {card}");
                c.Equal(at + 1, Counter(run, RunStream.Shops), $"shop {shop + 1}: with one number");
                c.Sequence([], LoggedCards(run), $"shop {shop + 1}: nothing was passed over");
            }

            // The Rare slot of the third shop was Entropy, which is not built: the pick is drawn again and the log says so.
            var third = Standing("75F277YN2G", shopsDrawn: 2 * NumbersPerShop + BeforeColorless + 2);
            var rare = ColorlessSlot(third, CardRarity.Rare, 6);
            c.That(Rares.Contains(rare.Def.ClassName) && Built.Contains(rare.Def.ClassName), $"shop 3: the Rare slot holds a built Rare ({rare.Def.ClassName})");
            c.Equal("5:card:Entropy:a shop card", LoggedCards(third).FirstOrDefault(), "shop 3: Entropy was passed over first");

            // The first shop of the run on seed 1BUSLN0FY5 showed Discovery and Calamity: neither is built.
            var first = Standing("1BUSLN0FY5", shopsDrawn: BeforeColorless);
            var uncommon = ColorlessSlot(first, CardRarity.Uncommon, 5);
            c.That(Built.Contains(uncommon.Def.ClassName) && uncommon.Def.Rarity == CardRarity.Uncommon, $"1BUSLN0FY5: the Uncommon slot holds a built Uncommon ({uncommon.Def.ClassName})");
            c.Equal("5:card:Discovery:a shop card", LoggedCards(first).FirstOrDefault(), "1BUSLN0FY5: Discovery was passed over first");
            c.Equal(5, first.State.FindPart<LeftOutLog>()?.FirstFloor, "from floor 5 on that run is no longer the game's own");

            var strict = Standing("1BUSLN0FY5", shopsDrawn: BeforeColorless, strict: true);
            var reason = "";
            try { ColorlessSlot(strict, CardRarity.Uncommon, 5); }
            catch (RunUnrepresentableException ex) { reason = ex.Message; }
            c.Equal("a shop card on floor 5 landed on the card Discovery, which the arena leaves out", reason, "1BUSLN0FY5, strict: the run cannot be the game's own past this shop until Discovery is built");
        });
    }

    // ---------- transforms ----------

    /// <summary>A Quest card and an Event-rarity card of the checks' own, under the game's class names.</summary>
    private static readonly CardDef QuestCard = new("SpoilsMap", "Spoils Map", CardType.Quest, CardRarity.Quest, CardDef.NoCost, TargetType.None)
    {
        Keywords = CardKeywords.Unplayable, MaxUpgradeLevel = 0, Text = _ => "A quest card of the checks.",
    };

    private static readonly CardDef EventCard = new("Peck", "Peck", CardType.Attack, CardRarity.Event, 1, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 2 }, Text = _ => "An event card of the checks.",
    };

    private static void Transforms(Check c)
    {
        c.Group("a Quest card and a card of Event, Ancient or Token rarity become a colorless card: an index into the 53, drawn again until it lands on a built one", () =>
        {
            var options = SinglePlayer();
            c.Equal(53, options.Count, "53 cards the pick runs over");
            (string What, CardDef Def)[] originals =
            [
                ("a Quest card", QuestCard), ("an Event-rarity card", EventCard),
                ("Break, the Ironclad's own Ancient-rarity card", Content.Card("Break")), ("the token Giant Rock", Content.Card("GiantRock")),
            ];
            var straight = 0;
            var again = 0;
            for (var o = 0; o < originals.Length; o++)
            {
                var (what, def) = originals[o];
                for (var seed = (ulong)(o * 100 + 1); seed <= (ulong)(o * 100 + 12); seed++)
                {
                    var run = DeckCheckKit.Outside(new RunSetup { ExtraCards = [(def, false)] });
                    run.State.TotalFloor = 7;
                    var original = run.State.Deck[^1];

                    var twin = new GameRng(seed);
                    var passed = new List<string>();
                    string landed;
                    while (!Built.Contains(landed = options[(int)(twin.NextDouble() * 53)])) passed.Add(landed);

                    var rng = new GameRng(seed);
                    var result = DeckEdits.TransformToRandom(run, original, rng);
                    c.Equal(landed, result.Card.Def.ClassName, $"{what}, seed {seed}: the first built card the stream lands on");
                    c.Equal(twin.Counter, rng.Counter, $"{what}, seed {seed}: one number per try");
                    c.That(run.State.Deck.Count == 11 && ReferenceEquals(run.State.Deck[^1], result.Card) && !result.Card.IsUpgraded && original.Pile == PileType.None,
                        $"{what}, seed {seed}: the new card is last in the deck, not upgraded, and the original is gone");
                    c.Sequence(passed.Select(name => $"7:card:{name}:a transform of {def.Name}"), LoggedCards(run), $"{what}, seed {seed}: the log has what was passed over");
                    if (passed.Count == 0) straight++;
                    else again++;
                }
            }
            c.That(straight >= 3 && again >= 20, $"both ways came up: {again} transforms drew again, {straight} landed on a built card at once (12 of 53 are built)");
        });

        c.Group("a colorless card becomes another colorless card, never itself", () =>
        {
            var options = SinglePlayer().Where(name => name != "Finesse").ToList();
            c.Equal(52, options.Count, "52 cards a Finesse can become");
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var run = DeckCheckKit.Outside(new RunSetup { Deck = [new DeckCard("Finesse", true)] });
                var twin = new GameRng(seed);
                string landed;
                while (!Built.Contains(landed = options[(int)(twin.NextDouble() * 52)])) { }
                var rng = new GameRng(seed);
                var result = DeckEdits.TransformToRandom(run, run.State.Deck[0], rng);
                c.Equal(landed, result.Card.Def.ClassName, $"seed {seed}: the card");
                c.Equal(twin.Counter, rng.Counter, $"seed {seed}: the numbers drawn");
                c.That(result.Card.Def.ClassName != "Finesse" && !result.Card.IsUpgraded, $"seed {seed}: not a Finesse, and the upgrade is not carried over");
            }
        });

        c.Group("a transform of a token inside an action: the run goes on; in strict mode it ends there when the pick lands on an unbuilt card", () =>
        {
            // Two seeds for the stream of the roll: one that lands on an unbuilt card first, one that lands on a built card at once.
            var options = SinglePlayer();
            string First(ulong seed) => options[(int)(new GameRng(seed).NextDouble() * 53)];
            ulong search = 1;
            while (Built.Contains(First(search))) search++;
            var stops = search;
            search = 1;
            while (!Built.Contains(First(search))) search++;
            var lands = search;

            Arena.Run Make(ulong seed, bool strict)
            {
                var script = new Dictionary<string, Action<Arena.Run, DeckCheckKit.Desk>>
                {
                    ["transform"] = (run, desk) => desk.Notes.Add(DeckEdits.TransformToRandom(run, run.State.Deck[^1], new GameRng(seed)).Card.Def.ClassName),
                };
                var run = new Arena.Run(new RunSetup { ExtraCards = [(Content.Card("GiantRock"), false)] }, new DeckCheckKit.DeskDirector(script));
                if (strict) run.State.Part<LeftOutLog>().Strict = true;
                return run;
            }

            var loose = Make(stops, strict: false);
            DeckCheckKit.Do(loose, "transform");
            c.That(!loose.IsOver, "drawing again: the run goes on");
            c.That(Built.Contains(loose.State.Deck[^1].Def.ClassName), $"with a built colorless card at the end of the deck ({loose.State.Deck[^1].Def.ClassName})");
            c.Equal($"0:card:{First(stops)}:a transform of Giant Rock", LoggedCards(loose).FirstOrDefault(), "and the card passed over in its log");
            var script = new Dictionary<string, Action<Arena.Run, DeckCheckKit.Desk>>
            {
                ["transform"] = (run, desk) => desk.Notes.Add(DeckEdits.TransformToRandom(run, run.State.Deck[^1], new GameRng(stops)).Card.Def.ClassName),
            };
            c.Equal(loose.Dump(), Arena.Run.Replay(new RunSetup { ExtraCards = [(Content.Card("GiantRock"), false)] }, new DeckCheckKit.DeskDirector(script), loose.Actions).Dump(),
                "the replay of its actions ends in the same state");

            var strict = Make(stops, strict: true);
            strict.Execute("transform");
            c.Equal(RunResult.Unrepresentable, strict.Result, "strict, landing on an unbuilt card: the run ends as unrepresentable");
            c.Equal($"a transform of Giant Rock on floor 0 landed on the card {First(stops)}, which the arena leaves out", strict.ResultReason, "with the reason");
            c.Equal("GiantRock", strict.State.Deck[^1].Def.ClassName, "standing as before the action: the token is still in the deck");

            var lucky = Make(lands, strict: true);
            DeckCheckKit.Do(lucky, "transform");
            c.That(!lucky.IsOver, "strict, landing on a built card: the run goes on");
            c.Equal(First(lands), lucky.State.Deck[^1].Def.ClassName, "with the card the game would give");
            c.Sequence([], LoggedCards(lucky), "and nothing passed over");
        });
    }

    // ---------- a colorless card in a run ----------

    private static void InARun(Check c)
    {
        c.Group("colorless cards in a run's deck go into a fight, are played and come back as they were", () =>
        {
            // The deck in its order is the draw pile: Ultimate Strike+, Finesse, Strike, Strike, Defend in hand, a Defend on top of the pile.
            var setup = new RunSetup
            {
                Deck = [new DeckCard("UltimateStrike", true), new DeckCard("Finesse"), new DeckCard("StrikeIronclad"), new DeckCard("StrikeIronclad"), new DeckCard("DefendIronclad"), new DeckCard("DefendIronclad")],
            };
            FixedFights Director() => new(new FightRequest { Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 25 })], ShuffleDeck = false });
            var run = new Arena.Run(setup, Director());

            var deck = run.DescribeDeck()["deck"]!.AsArray();
            c.Equal("""{"name":"Ultimate Strike+","cost":"1","type":"Attack","text":"Deal 20 damage.","rarity":"Uncommon","upgraded":true}""", DeckCheckKit.Text(deck[0]), "get_deck: the upgraded Ultimate Strike");
            c.Equal("""{"name":"Finesse","cost":"0","type":"Skill","text":"Gain 4 Block. Draw 1 card.","rarity":"Uncommon"}""", DeckCheckKit.Text(deck[1]), "get_deck: Finesse");

            c.Equal("combat", run.Snapshot().Screen, "the fight is on");
            c.Sequence(["Ultimate Strike+", "Finesse", "Strike", "Strike", "Defend"], run.Fight!.HandTitles(), "the opening hand");
            DeckCheckKit.Do(run, "play:0>e0");
            c.Equal(5, run.Fight!.Enemy(0).Hp, "Ultimate Strike+: 25 - 20");
            DeckCheckKit.Do(run, "play:0");
            c.Equal(4, run.Fight!.Player.Block, "Finesse: 4 Block");
            c.Sequence(["Strike", "Strike", "Defend", "Defend"], run.Fight!.HandTitles(), "and the Defend drawn");
            DeckCheckKit.Do(run, "play:0>e0");
            c.Equal(RunResult.Cleared, run.Result, "the Strike wins the fight and the run of one fight is cleared");
            c.Equal("UltimateStrike+ Finesse StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad", DeckCheckKit.DeckNames(run), "the deck is what it was, the upgrade kept");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, Director(), run.Actions).Dump(), "the replay of the actions ends in the same state");
        });
    }
}
