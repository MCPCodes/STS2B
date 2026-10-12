using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The 25 relics of Relics1, the power Vigor, the potion Potion-Shaped Rock and the list of the
/// 157 relics an act 1 Ironclad can get.
/// <para>
/// Every expected number is worked out by hand from the game's relic classes
/// (MegaCrit.Sts2.Core.Models.Relics, v0.107.1) and the order of its turn start
/// (CombatManager.StartTurn and SetupPlayerTurn: Block falls, "after block cleared", energy,
/// the hand draw, "after player turn start", "after side turn start"). Titles are the game's
/// English name table's. The pulls are held against the relic bags the real game saved for seed
/// 1BUSLN0FY5 and against the first shop of the real run on seed 75F277YN2G
/// (sts2-notes/golden-saves); what is needed of them is written out here.
/// </para>
/// </summary>
public sealed class Relics1Checks : IArenaChecks
{
    public void Run(Check c)
    {
        Table(c);
        Reachable(c);
        StartOfAFight(c);
        FirstTurns(c);
        Vigor(c);
        EveryTurn(c);
        Triggers(c);
        HookMoments(c);
        Pickups(c);
        OverARun(c);
        Pulls(c);
        ThroughTheRewards(c);
        ForecastMark(c);
        WholeFights(c);
    }

    // ---------- helpers ----------

    /// <summary>A fight builder with the starter deck in <see cref="CheckKit.StarterInOrder"/> and Burning Blood plus these relics, in this pickup order.</summary>
    private static FightBuilder With(params string[] relics) =>
        new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relics(["BurningBlood", .. relics]);

    /// <summary>The same against one dummy (idle with 100 HP unless given).</summary>
    private static Fight Against(CheckDummy? dummy, params string[] relics) => With(relics).Enemy(dummy ?? new CheckDummy()).Start();

    private static CheckDummy Hitter(int damage, int hits = 1, int hp = 100) => new() { Hp = hp, Steps = [new DummyStep("HIT", Damage: damage, Hits: hits)] };

    private static int Strength(Creature creature) => creature.PowerAmount<StrengthPower>();

    private static string HandText(Fight f, int index) => Fight.CardText(f, f.Hand[index], PileType.Hand);

    private static List<string?> Belt(Fight f) => f.Potions.Select(potion => potion?.ClassName).ToList();

    private static FightRequest RunDummy(int hp, int damage = 0, RoomType room = RoomType.Monster) => new()
    {
        Monsters = () => [new PlacedMonster(new CheckDummy { Hp = hp, Steps = [damage > 0 ? new DummyStep("HIT", Damage: damage) : new DummyStep("IDLE")] })],
        RoomType = room,
        ShuffleDeck = false,
    };

    /// <summary>A run that stands outside a fight on a seed's own world, with a desk whose actions are a script.</summary>
    private sealed class OnTheSeed(IReadOnlyDictionary<string, Action<Arena.Run, Desk>> script) : RunDirector
    {
        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State);
            run.Push(new Desk(script));
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    private static readonly Dictionary<string, Action<Arena.Run, Desk>> Nothing = new();

    private const string PlayedSeed = "1BUSLN0FY5";

    private static Arena.Run Seeded(string seed, IReadOnlyDictionary<string, Action<Arena.Run, Desk>>? script = null) =>
        new(new RunSetup { Seed = seed }, new OnTheSeed(script ?? Nothing));

    // ---------- the table ----------

    /// <summary>Class name, the game's English title, the game's rarity.</summary>
    private static readonly (string ClassName, string Title, RelicRarity Rarity)[] Expected =
    [
        ("Anchor", "Anchor", RelicRarity.Common),
        ("Vajra", "Vajra", RelicRarity.Common),
        ("BronzeScales", "Bronze Scales", RelicRarity.Common),
        ("OddlySmoothStone", "Oddly Smooth Stone", RelicRarity.Common),
        ("BagOfPreparation", "Bag of Preparation", RelicRarity.Common),
        ("Lantern", "Lantern", RelicRarity.Common),
        ("Strawberry", "Strawberry", RelicRarity.Common),
        ("Akabeko", "Akabeko", RelicRarity.Uncommon),
        ("Candelabra", "Candelabra", RelicRarity.Uncommon),
        ("MercuryHourglass", "Mercury Hourglass", RelicRarity.Uncommon),
        ("HornCleat", "Horn Cleat", RelicRarity.Uncommon),
        ("PetrifiedToad", "Petrified Toad", RelicRarity.Uncommon),
        ("Pear", "Pear", RelicRarity.Uncommon),
        ("Bellows", "Bellows", RelicRarity.Rare),
        ("CaptainsWheel", "Captain's Wheel", RelicRarity.Rare),
        ("Chandelier", "Chandelier", RelicRarity.Rare),
        ("Mango", "Mango", RelicRarity.Rare),
        ("GamePiece", "Game Piece", RelicRarity.Rare),
        ("IceCream", "Ice Cream", RelicRarity.Rare),
        ("TungstenRod", "Tungsten Rod", RelicRarity.Rare),
        ("Bread", "Bread", RelicRarity.Shop),
        ("SlingOfCourage", "Sling of Courage", RelicRarity.Shop),
        ("Brimstone", "Brimstone", RelicRarity.Shop),
        ("LeesWaffle", "Lee's Waffle", RelicRarity.Shop),
        ("TheAbacus", "The Abacus", RelicRarity.Shop),
    ];

    private static void Table(Check c)
    {
        c.Group("the 25 relics carry the game's titles and stand in the game's bags under the game's rarities", () =>
        {
            var registry = Content.Registry;
            c.Equal(25, Expected.Length, "25 relics");
            c.Sequence(Expected.Select(e => e.ClassName), Relics1.All, "the module names them in the same order");
            foreach (var e in Expected)
            {
                c.That(registry.Has(ContentKind.Relic, e.ClassName), $"{e.ClassName} is registered");
                if (!registry.Has(ContentKind.Relic, e.ClassName)) continue;
                var relic = registry.NewRelic(e.ClassName);
                c.Equal(e.Title, relic.Name, $"{e.ClassName} title");
                c.That(relic.Describe().Length > 20, $"{e.ClassName} has a text");
                c.Equal(e.Rarity, GameLists.BagRelic(e.ClassName).Rarity, $"{e.ClassName}: the rarity of the game's list");
                c.That(!RunRelics.NeverInShops.Contains(e.ClassName), $"{e.ClassName} may be sold in a shop");
                c.That(!RunRelics.OnlyBeforeTheLastChest.Contains(e.ClassName), $"{e.ClassName} may come up on any floor");
                c.Equal("Relics1", registry.SourceOf(ContentKind.Relic, e.ClassName), $"{e.ClassName} is this module's");
            }
            c.Equal((7, 6, 7, 5), (Count(RelicRarity.Common), Count(RelicRarity.Uncommon), Count(RelicRarity.Rare), Count(RelicRarity.Shop)), "7 Common, 6 Uncommon, 7 Rare, 5 Shop");
            c.Sequence(["Brimstone"], Expected.Select(e => e.ClassName).Where(name => GameLists.CharacterRelics["Ironclad"].Any(r => r.ClassName == name)), "Brimstone is the Ironclad's own; the other 24 are shared (a chest can hold them)");
            c.Equal(24, Expected.Count(e => GameLists.SharedRelics.Any(r => r.ClassName == e.ClassName)), "24 shared relics");

            static int Count(RelicRarity rarity) => Expected.Count(e => e.Rarity == rarity);
        });

        c.Group("Potion-Shaped Rock: the game's one token potion, 15 damage to an enemy, in a fight only", () =>
        {
            var rock = Content.Potion(Relics1.PotionShapedRock);
            c.Equal("Potion-Shaped Rock", rock.Name, "title");
            c.Equal(PotionRarity.Token, rock.Rarity, "rarity Token");
            c.Equal(TargetType.AnyEnemy, rock.Target, "aimed at an enemy");
            c.Equal(PotionUsage.CombatOnly, rock.Usage, "in a fight only");
            c.That(Scenarios.PotionPool.All(p => p.ClassName != Relics1.PotionShapedRock), "it is in neither pool that rewards, shops and random potions read");
            c.That(!RunPotions.Usable(rock), "outside a fight it can only be thrown away");

            // 15 unpowered: Vulnerable on the target and Strength on the user change nothing; Block absorbs.
            var f = Against(new CheckDummy { Steps = [new DummyStep("GUARD", Block: 4)] }, "Vajra");
            f.SetPotion(0, Relics1.PotionShapedRock);
            f.Play("Bash", 0);
            c.Equal(91, f.Enemy(0).Hp, "Bash with 1 Strength: 9");
            f.Do("potion:0>e0");
            c.Equal(76, f.Enemy(0).Hp, "the rock deals 15 to a Vulnerable enemy, thrown by a player with Strength");
            c.Equal(null, f.Potions[0], "and is used up");
            f.SetPotion(0, Relics1.PotionShapedRock);
            f.EndTurn();
            f.Do("potion:0>e0");
            c.Equal((0, 65), (f.Enemy(0).Block, f.Enemy(0).Hp), "4 Block takes 4 of the 15");
        });
    }

    // ---------- the list of reachable relics ----------

    /// <summary>The relic options of the game's Neow event without Massive Scroll, which is offered only to more than one player (Neow.cs).</summary>
    private static readonly string[] NeowsRelics =
    [
        "ArcaneScroll", "BoomingConch", "CursedPearl", "FishingRod", "GoldenPearl", "HeftyTablet", "Kaleidoscope", "LargeCapsule", "LavaRock",
        "LeadPaperweight", "LeafyPoultice", "LostCoffer", "NeowsBones", "NeowsTalisman", "NeowsTorment", "NewLeaf", "NutritiousOyster", "PhialHolster",
        "Pomander", "PrecariousShears", "PreciseScissors", "ScrollBoxes", "SilkenTress", "SilverCrucible", "SmallCapsule", "StoneHumidifier", "WingedBoots",
    ];

    /// <summary>What act 1 events hand out by name: Sunken Statue's sword and what it turns into, Room Full of Cheese, Tea Master's three, Byrdonis Nest's egg.</summary>
    private static readonly string[] EventRelics = ["BoneTea", "Byrdpip", "ChosenCheese", "EmberTea", "SwordOfJade", "SwordOfStone", "TeaOfDiscourtesy"];

    /// <summary>A stand-in under the name of a relic no chunk builds. Check only: it must not count as that relic's class.</summary>
    private sealed class Kaleidoscope : Relic
    {
        public override string Name => "Kaleidoscope (a check's stand-in)";
        public override string Describe() => "Check only.";
    }

    private static void Reachable(Check c)
    {
        c.Group("the 157 relics an act 1 Ironclad can get: the start, the bag's 122, Neow's 27, the events' 7", () =>
        {
            var all = ReachableRelics.All;
            c.Equal(157, all.Count, "157 relics");
            c.Equal(157, all.Select(r => r.ClassName).Distinct(StringComparer.Ordinal).Count(), "no name twice");
            c.Sequence(["BurningBlood"], ReachableRelics.From(RelicSource.Start).Select(r => r.ClassName), "the starting relic");

            // The player's bag as the game fills it: the shared pool's and the Ironclad's relics of the four bag rarities.
            var bag = RelicBag.PlayerPool(RunUnlocks.All, "Ironclad").Order(StringComparer.Ordinal).ToList();
            c.Equal(122, bag.Count, "the game's bag holds 122");
            c.Sequence(bag, ReachableRelics.From(RelicSource.Bag).Select(r => r.ClassName), "the bag relics of the list are the game's bag");
            c.Sequence(NeowsRelics, ReachableRelics.From(RelicSource.Neow).Select(r => r.ClassName), "Neow's 27");
            c.Sequence(EventRelics, ReachableRelics.From(RelicSource.Event).Select(r => r.ClassName), "the events' 7");

            // Who builds what: the relic counts of the plan's chunks (chunks.json).
            (int Chunk, int Relics)[] plan = [(12, 25), (14, 14), (15, 2), (22, 9), (23, 18), (24, 4), (25, 24), (26, 17), (28, 3), (29, 16), (30, 14), (32, 7), (33, 1), (35, 1)];
            foreach (var (chunk, relics) in plan)
            {
                c.Equal(relics, all.Count(r => r.Chunk == chunk), $"chunk {chunk} builds {relics}");
                c.That(ReachableRelics.Chunks.ContainsKey(chunk), $"chunk {chunk} has its name");
            }
            c.Sequence(["Kaleidoscope"], all.Where(r => r.Chunk == ReachableRelics.Never).Select(r => r.ClassName), "one relic no chunk builds");
            c.Equal(1 + plan.Sum(p => p.Relics) + 1, all.Count, "and that is all of them");
            c.Sequence(Relics1.All.Order(StringComparer.Ordinal), all.Where(r => r.Chunk == 12).Select(r => r.ClassName), "chunk 12's are this file's 25");
        });

        c.Group("every one of them is registered or left out by name with a reason, and nothing else is registered", () =>
        {
            var registry = Content.Registry;
            foreach (var relic in ReachableRelics.All)
            {
                var has = registry.Has(ContentKind.Relic, relic.ClassName);
                var leftOut = registry.IsExcluded(ContentKind.Relic, relic.ClassName, out var reason);
                c.That(has != leftOut, $"{relic.ClassName} is either registered or left out");
                c.Equal(has, ReachableRelics.HasClass(relic.ClassName), $"{relic.ClassName}: registered exactly when it has a class");
                if (leftOut) c.Equal(ReachableRelics.WhyLeftOut(relic), reason, $"{relic.ClassName}: the reason");
            }
            foreach (var name in Relics1.All.Append("BurningBlood")) c.That(registry.Has(ContentKind.Relic, name), $"{name} is registered");
            foreach (var name in registry.RelicNames) c.That(ReachableRelics.Find(name) is not null, $"the registry's {name} is a relic an act 1 Ironclad can get");

            // What a strict run says when it stops at one: the chunk that will build it.
            c.Equal("not built yet: chunk 26 (relics-fight-c) of the run-mode plan builds it", ReachableRelics.WhyLeftOut(ReachableRelics.Find("StoneCracker")!), "Stone Cracker's reason");
            c.That(ReachableRelics.WhyLeftOut(ReachableRelics.Find("Kaleidoscope")!).Contains("other four characters", StringComparison.Ordinal), "Kaleidoscope's reason is its own");
            if (!RunRelics.IsBuilt("StoneCracker")) c.That(RunRelics.WhyNotBuilt("StoneCracker").Contains("chunk 26", StringComparison.Ordinal), "a pull that stops at Stone Cracker names the chunk");
            c.That(!ReachableRelics.HasClass("Strawberries") && ReachableRelics.Find("Circlet") is null, "a name that is no relic of the list has neither class nor entry");
            c.That(new Kaleidoscope().Name.Length > 0 && !ReachableRelics.HasClass("Kaleidoscope") && registry.IsExcluded(ContentKind.Relic, "Kaleidoscope", out _),
                "a check's own stand-in, nested in its checks class, does not count as the relic's class");

            var row = Completeness.Rows().SingleOrDefault(r => r.Kind == ContentKind.Relic);
            c.That(row is not null, "the completeness table has a line for relics");
            if (row is null) return;
            c.Equal(157, row.Wanted.Count, "157 wanted");
            c.Equal(0, row.Missing.Count, "none missing");
            c.Equal(157, row.Registered.Count + row.LeftOut.Count, "registered and left out add up");
            c.That(Completeness.Problems().All(problem => !problem.Contains("Relic", StringComparison.Ordinal) && !problem.Contains("relics", StringComparison.Ordinal)), "and the self-check has nothing to say about relics");
            c.That(Completeness.Rows().Any(r => r.Kind == ContentKind.Potion && r.Wanted.SequenceEqual([Relics1.PotionShapedRock]) && r.Registered.Count == 1), "Potion-Shaped Rock has its own line");
        });
    }

    // ---------- relics that act when a fight starts ----------

    private static void StartOfAFight(Check c)
    {
        c.Group("Anchor: 10 Block when the fight starts, not changed by Dexterity, gone after the first enemy turn", () =>
        {
            var f = Against(Hitter(7), "Anchor", "OddlySmoothStone");
            c.Equal(10, f.Player.Block, "10 Block before the first card (1 Dexterity adds nothing)");
            c.Equal(10, f.Snapshot().State["me"]!["block"]!.GetValue<int>(), "the snapshot shows it");
            f.EndTurn();
            c.Equal((80, 0), (f.Player.Hp, f.Player.Block), "it took the 7; what was left fell at the start of turn 2");
        });

        c.Group("Vajra: 1 Strength in every fight's room, before the monsters get their starting powers", () =>
        {
            // The dummy copies the player's Strength as it joins: it sees Vajra's.
            var copycat = new CheckDummy { OnAdded = (f, me) => f.ApplyPower<StrengthPower>(me, Strength(f.Player), me) };
            var f = Against(copycat, "Vajra");
            c.Equal(1, Strength(f.Player), "1 Strength");
            c.Equal(1, Strength(f.Enemy(0)), "it was there when the monster got its starting powers");
            f.Play("Strike", 0);
            c.Equal(93, f.Enemy(0).Hp, "a Strike deals 7");
            foreach (var room in new[] { RoomType.Elite, RoomType.Boss })
                c.Equal(1, Strength(With("Vajra").Enemy(new CheckDummy()).Room(room).Start().Player), $"also in a {room} room");
        });

        c.Group("Bronze Scales: 3 Thorns; every hit of an attack costs the attacker 3, Vulnerable or not", () =>
        {
            var f = Against(Hitter(5, hits: 2), "BronzeScales");
            c.Equal(3, f.Player.PowerAmount("ThornsPower"), "3 Thorns");
            f.Play("Bash", 0);
            c.Equal(92, f.Enemy(0).Hp, "Bash: 8, and the enemy is Vulnerable");
            f.EndTurn();
            c.Equal(86, f.Enemy(0).Hp, "two hits, 3 each");
            c.Equal(70, f.Player.Hp, "the hits still land: 5 and 5");
        });

        c.Group("Oddly Smooth Stone: 1 Dexterity", () =>
        {
            var f = Against(null, "OddlySmoothStone");
            c.Equal(1, f.Player.PowerAmount<DexterityPower>(), "1 Dexterity");
            f.Play("Defend");
            c.Equal(6, f.Player.Block, "a Defend gives 6");
        });

        c.Group("Sling of Courage: 2 Strength in an Elite room and in no other", () =>
        {
            Fight In(RoomType room, params string[] relics) => With(relics).Enemy(new CheckDummy()).Room(room).Start();
            c.Equal(2, Strength(In(RoomType.Elite, "SlingOfCourage").Player), "Elite: 2");
            c.Equal(0, Strength(In(RoomType.Monster, "SlingOfCourage").Player), "a hallway fight: none");
            c.Equal(0, Strength(In(RoomType.Boss, "SlingOfCourage").Player), "the boss: none");
            c.Equal(3, Strength(In(RoomType.Elite, "Vajra", "SlingOfCourage").Player), "with Vajra in an Elite room: 3");
        });

        c.Group("Petrified Toad: a Potion-Shaped Rock into the first free slot when the fight starts; none with a full belt", () =>
        {
            var f = Against(null, "PetrifiedToad");
            c.Sequence([Relics1.PotionShapedRock, null, null], Belt(f), "an empty belt: slot 0");
            c.That(f.LegalActions().Any(a => a.Id == "potion:0>e0"), "it can be thrown at the enemy");
            f.Do("potion:0>e0");
            c.Equal(85, f.Enemy(0).Hp, "15 damage");

            f = With("PetrifiedToad").Potions("FirePotion", null, "BlockPotion").Enemy(new CheckDummy()).Start();
            c.Sequence(["FirePotion", Relics1.PotionShapedRock, "BlockPotion"], Belt(f), "the first free slot is the middle one");
            f = With("PetrifiedToad").Potions("FirePotion", "BlockPotion", "StrengthPotion").Enemy(new CheckDummy()).Start();
            c.Sequence(["FirePotion", "BlockPotion", "StrengthPotion"], Belt(f), "a full belt stays as it is");
        });
    }

    // ---------- relics that act on a numbered turn ----------

    private static void FirstTurns(Check c)
    {
        c.Group("Bag of Preparation: 7 cards on turn 1, 5 from then on", () =>
        {
            string[] order =
            [
                "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "Bash", "StrikeIronclad", "DefendIronclad",
                "Bash", "Bash", "StrikeIronclad", "StrikeIronclad", "DefendIronclad",
            ];
            var f = new FightBuilder().DrawOrder(order).Relics("BurningBlood", "BagOfPreparation").Enemy(new CheckDummy()).Start();
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Strike", "Defend"], f.HandTitles(), "turn 1: the first seven");
            f.EndTurn();
            c.Sequence(["Bash", "Bash", "Strike", "Strike", "Defend"], f.HandTitles(), "turn 2: the next five");
        });

        c.Group("Lantern, Candelabra, Chandelier: 1 energy on turn 1, 2 on turn 2, 3 on turn 3", () =>
        {
            List<int> Energies(params string[] relics)
            {
                var f = Against(null, relics);
                var seen = new List<int> { f.Energy };
                for (var turn = 2; turn <= 4; turn++)
                {
                    f.EndTurn();
                    seen.Add(f.Energy);
                }
                return seen;
            }
            c.Sequence([4, 3, 3, 3], Energies("Lantern"), "Lantern: turns 1 to 4");
            c.Sequence([3, 5, 3, 3], Energies("Candelabra"), "Candelabra");
            c.Sequence([3, 3, 6, 3], Energies("Chandelier"), "Chandelier");
            c.Sequence([4, 5, 6, 3], Energies("Lantern", "Candelabra", "Chandelier"), "all three");
            c.Equal(3, Against(null, "Lantern").MaxEnergy, "none of them raises the maximum");
        });

        c.Group("Horn Cleat and Captain's Wheel: 14 Block on turn 2, 18 on turn 3, after last turn's Block fell", () =>
        {
            var f = Against(null, "HornCleat", "CaptainsWheel");
            c.Equal(0, f.Player.Block, "turn 1: none");
            f.Play("Defend");
            f.EndTurn();
            c.Equal(14, f.Player.Block, "turn 2: the Defend's 5 are gone, Horn Cleat's 14 are there");
            f.EndTurn();
            c.Equal(18, f.Player.Block, "turn 3: Captain's Wheel's 18, not 32");
            f.EndTurn();
            c.Equal(0, f.Player.Block, "turn 4: none");

            // Unpowered: 2 Dexterity change nothing.
            f = Against(null, "HornCleat");
            f.ApplyPower<DexterityPower>(f.Player, 2, f.Player);
            f.EndTurn();
            c.Equal(14, f.Player.Block, "with 2 Dexterity still 14");
        });

        c.Group("Bellows: the hand of turn 1 is upgraded for the fight; the draw pile and the deck are not", () =>
        {
            var f = Against(null, "Bellows");
            c.Sequence(["Strike+", "Strike+", "Defend+", "Defend+", "Bash+"], f.HandTitles(), "turn 1");
            c.That(f.DrawPile.All(card => !card.IsUpgraded), "the cards still to be drawn are as they were");
            f.Play("Strike+", 0);
            c.Equal(91, f.Enemy(0).Hp, "an upgraded Strike deals 9");
            f.EndTurn();
            c.Sequence(["Strike", "Strike", "Strike", "Defend", "Defend"], f.HandTitles(), "turn 2: nothing is upgraded");
            f.EndTurn();
            c.Equal(5, f.AllCards.Count(card => card.IsUpgraded), "turn 3, after the shuffle: the five of turn 1 are still upgraded, wherever they are");

            // With Bag of Preparation all seven; a card that already was upgraded is not upgraded twice.
            f = new FightBuilder().DrawOrder("Bash+", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad")
                .Relics("BurningBlood", "BagOfPreparation", "Bellows").Enemy(new CheckDummy()).Start();
            c.Sequence(["Bash+", "Strike+", "Strike+", "Defend+", "Defend+", "Strike+", "Strike+"], f.HandTitles(), "seven cards, all upgraded once");

            var run = new Arena.Run(new RunSetup { Relics = ["BurningBlood", "Bellows"] }, new FixedFights(RunDummy(hp: 9)));
            c.Sequence(["Strike+", "Strike+", "Strike+", "Strike+", "Strike+"], run.Fight!.HandTitles(), "in a run: the first hand");
            Do(run, "play:0>e0");
            c.That(run.IsOver && run.Result == RunResult.Cleared, "one upgraded Strike wins the fight against 9 HP");
            c.That(run.State.Deck.All(card => !card.IsUpgraded), "and the deck comes back without an upgrade");
        });

        c.Group("Bread: 1 energy on turn 1, then 4 a turn", () =>
        {
            var f = Against(null, "Bread");
            c.Equal((1, 3), (f.Energy, f.MaxEnergy), "turn 1: 3 less 2, and the maximum is 3");
            f.EndTurn();
            c.Equal((4, 4), (f.Energy, f.MaxEnergy), "turn 2: 4 of 4");
            f.EndTurn();
            c.Equal((4, 4), (f.Energy, f.MaxEnergy), "turn 3: the same");
            c.Equal(2, Against(null, "Bread", "Lantern").Energy, "with Lantern turn 1 has 2");
            c.Equal(2, Against(null, "Lantern", "Bread").Energy, "in either pickup order");
        });

        c.Group("Ice Cream: energy is set on turn 1 and added to from turn 2 on", () =>
        {
            var f = Against(null, "IceCream");
            c.Equal(3, f.Energy, "turn 1: 3");
            f.Play("Strike", 0);
            f.EndTurn();
            c.Equal(5, f.Energy, "turn 2: the 2 that were left and 3 more");
            f.EndTurn();
            c.Equal(8, f.Energy, "turn 3: 5 and 3");

            f = Against(null, "IceCream", "Bread");
            c.Equal(1, f.Energy, "with Bread: 1 on turn 1");
            f.EndTurn();
            c.Equal(5, f.Energy, "and 1 kept plus 4 on turn 2");
        });
    }

    // ---------- Akabeko and Vigor ----------

    /// <summary>On a dummy: whoever hits it with a powered attack and does not kill it gains 5 Vigor. Check only.</summary>
    private sealed class VigorFeeder : Power
    {
        public override string Name => "Vigor Feeder";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "Check only.";

        public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner) && dealer is not null && props.IsPoweredAttack()) f.ApplyPower<VigorPower>(dealer, 5, dealer);
        }
    }

    private static void Vigor(Check c)
    {
        c.Group("Akabeko: 8 Vigor on turn 1; the first attack deals 8 more per hit and uses it up", () =>
        {
            var f = Against(null, "Akabeko");
            c.Equal(8, f.Player.PowerAmount<VigorPower>(), "8 Vigor on turn 1");
            c.Equal("Vigor", f.Player.GetPower<VigorPower>()!.Name, "under the game's title");
            c.Equal("Deal 14 damage.", HandText(f, 0), "a Strike in hand reads 14");
            f.Play("Strike", 0);
            c.Equal(86, f.Enemy(0).Hp, "the first Strike deals 14");
            c.That(!f.Player.HasPower<VigorPower>(), "the Vigor is gone");
            c.Equal("Deal 6 damage.", HandText(f, 0), "the next Strike reads 6");
            f.Play("Strike", 0);
            c.Equal(80, f.Enemy(0).Hp, "and deals 6");
            f.EndTurn();
            c.That(!f.Player.HasPower<VigorPower>(), "turn 2 brings none");
        });

        c.Group("Vigor is added to every hit of the one attack", () =>
        {
            var f = new FightBuilder().DrawOrder("TwinStrike", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "Bash")
                .Relics("BurningBlood", "Akabeko").Enemy(new CheckDummy()).Start();
            f.Play("Twin Strike", 0);
            c.Equal(74, f.Enemy(0).Hp, "Twin Strike: 13 and 13");
            f.Play("Strike", 0);
            c.Equal(68, f.Enemy(0).Hp, "then a Strike for 6");
        });

        c.Group("Vigor waits for an attack: a turn without one keeps it, and damage that is no attack leaves it", () =>
        {
            var f = With("Akabeko").Potions("FirePotion").Enemy(Hitter(5)).Start();
            c.Sequence(["attack 5"], f.IntentTexts(f.Enemy(0)), "the player's Vigor is not the enemy's: its intent reads 5");
            f.Do("potion:0>e0");
            c.Equal((80, 8), (f.Enemy(0).Hp, f.Player.PowerAmount<VigorPower>()), "a Fire Potion deals its 20 and uses no Vigor");
            f.Play("Defend");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "the enemy's hit of 5 went into the Defend's Block");
            c.Equal(8, f.Player.PowerAmount<VigorPower>(), "turn 2: still 8, and no second dose");
            f.Play("Strike", 0);
            c.Equal(66, f.Enemy(0).Hp, "the first attack, a turn later: 14");
        });

        c.Group("Vigor gained during the attack stays, and stays bound to that attack's card (the game never forgets the claim)", () =>
        {
            // VigorPower.cs: the attack notes the amount when it starts (8) and takes that much off when it
            // is over; the claim is never cleared. So 8 + 5 - 8 = 5 are left, and they go only to hits of
            // the same card (or of no card): the next Strike, another card, gets nothing and uses nothing.
            var f = Against(new CheckDummy { OnAdded = (fight, me) => fight.ApplyPower<VigorFeeder>(me, 1, me) }, "Akabeko");
            f.Play("Strike", 0);
            c.Equal((86, 5), (f.Enemy(0).Hp, f.Player.PowerAmount<VigorPower>()), "14 dealt; 13 Vigor less the 8 the attack started with");
            c.Equal("Deal 6 damage.", HandText(f, 0), "the other Strike reads 6");
            f.Play("Strike", 0);
            c.Equal((80, 10), (f.Enemy(0).Hp, f.Player.PowerAmount<VigorPower>()), "it deals 6, and the 5 it earned are added to the 5 nobody can use");
        });
    }

    // ---------- relics that act every turn ----------

    private static void EveryTurn(Check c)
    {
        c.Group("Mercury Hourglass: 3 damage to every enemy at the start of each turn; Block absorbs it, Strength does not raise it", () =>
        {
            var f = With("Vajra", "MercuryHourglass").Enemy(new CheckDummy { Steps = [new DummyStep("GUARD", Block: 5)] }).Enemy(new CheckDummy { Hp = 50 }).Start();
            c.Equal((97, 47), (f.Enemy(0).Hp, f.Enemy(1).Hp), "turn 1: both lost 3, with 1 Strength on the player");
            f.EndTurn();
            c.Equal((97, 2), (f.Enemy(0).Hp, f.Enemy(0).Block), "turn 2: the first one's 5 Block took the 3");
            c.Equal(44, f.Enemy(1).Hp, "the second lost 3 more");
        });

        c.Group("Mercury Hourglass can win the fight at the start of a turn", () =>
        {
            var f = With("MercuryHourglass").Hp(70).Enemy(new CheckDummy { Hp = 6 }).Start();
            c.Equal(3, f.Enemy(0).Hp, "turn 1: 3 of 6");
            f.EndTurn();
            c.That(f.IsOver && f.Won, "turn 2: the enemy is dead before a card is played");
            c.Equal(76, f.Player.Hp, "Burning Blood heals as after any win");
        });

        c.Group("Brimstone: 2 Strength for the player and 1 for every living enemy at the start of each turn", () =>
        {
            var f = With("Brimstone").Enemy(Hitter(5)).Enemy(new CheckDummy { Hp = 6 }).Start();
            c.Equal((2, 1, 1), (Strength(f.Player), Strength(f.Enemy(0)), Strength(f.Enemy(1))), "turn 1");
            c.Sequence(["attack 6"], f.IntentTexts(f.Enemy(0)), "the enemy's intent has its Strength");
            f.Play("Strike", 1);
            c.That(f.Enemy(1).IsDead, "a Strike for 8 kills the 6 HP enemy");
            f.EndTurn();
            c.Equal(74, f.Player.Hp, "the other one hits for 6");
            c.Equal((4, 2), (Strength(f.Player), Strength(f.Enemy(0))), "turn 2: 2 more and 1 more");
            c.Equal(0, Strength(f.Enemy(1)), "the dead one gets none");
            f.EndTurn();
            c.Equal((6, 3), (Strength(f.Player), Strength(f.Enemy(0))), "turn 3");
        });
    }

    // ---------- relics that answer something ----------

    private static void Triggers(Check c)
    {
        c.Group("Game Piece: a Power card played draws 1 card; another card draws none", () =>
        {
            var f = new FightBuilder().DrawOrder("Inflame", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "Bash", "StrikeIronclad")
                .Relics("BurningBlood", "GamePiece").Enemy(new CheckDummy()).Start();
            f.Play("Inflame");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash"], f.HandTitles(), "Inflame left the hand and the next card of the draw pile came");
            f.Play("Strike", 0);
            c.Sequence(["Strike", "Defend", "Defend", "Bash"], f.HandTitles(), "a Strike draws nothing");
        });

        c.Group("The Abacus: 6 Block whenever the discard pile is shuffled into the draw pile", () =>
        {
            // Ten cards: turns 1 and 2 draw them all, turn 3 has to shuffle before its draw. Block falls
            // before the draw, so the 6 stand on turn 3.
            var f = Against(null, "TheAbacus", "OddlySmoothStone");
            c.Equal(0, f.Player.Block, "turn 1: the deck's own shuffle at the start of the fight is no such shuffle");
            f.EndTurn();
            c.Equal(0, f.Player.Block, "turn 2: no shuffle");
            f.EndTurn();
            c.Equal(6, f.Player.Block, "turn 3: the shuffle gives 6 (1 Dexterity adds nothing)");

            // In the middle of a turn: Pommel Strike's draw from an empty draw pile.
            f = new FightBuilder().DrawOrder("StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "PommelStrike")
                .Relics("BurningBlood", "TheAbacus").Enemy(new CheckDummy()).Start();
            f.Play("Strike", 0);
            c.Equal(0, f.Player.Block, "nothing yet");
            f.Play("Pommel Strike", 0);
            c.Equal(6, f.Player.Block, "its draw shuffles the one discarded Strike in: 6 Block");
            c.Sequence(["Strike", "Defend", "Defend", "Strike"], f.HandTitles(), "and draws it");

            // A draw with nothing to shuffle is no shuffle.
            f = new FightBuilder().DrawOrder("StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "PommelStrike")
                .Relics("BurningBlood", "TheAbacus").Enemy(new CheckDummy()).Start();
            f.Play("Pommel Strike", 0);
            c.Equal(0, f.Player.Block, "both piles empty: no shuffle, no Block");
        });

        c.Group("Tungsten Rod: every HP loss of the player is 1 less, in a fight and outside one", () =>
        {
            var f = Against(Hitter(5, hits: 2), "TungstenRod");
            f.Play("Strike", 0);
            c.Equal(94, f.Enemy(0).Hp, "the enemy loses what it always lost");
            f.EndTurn();
            c.Equal(72, f.Player.Hp, "two hits of 5: 4 and 4");

            f = new FightBuilder().DrawOrder("Bloodletting", "DefendIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "Bash")
                .Relics("BurningBlood", "TungstenRod").Enemy(Hitter(6)).Start();
            f.Play("Bloodletting");
            c.Equal(78, f.Player.Hp, "Bloodletting's 3 are 2");
            f.Play("Defend");
            f.EndTurn();
            c.Equal(78, f.Player.Hp, "a hit of 6 into 5 Block leaves 1, which the rod takes: nothing lost");

            f = With("TungstenRod").Hp(70).Enemy(Hitter(3)).Start();
            f.Play("Defend");
            f.EndTurn();
            c.Equal(70, f.Player.Hp, "a hit that Block takes whole loses nothing and gives nothing back");

            var run = Outside(new RunSetup { Relics = ["BurningBlood", "TungstenRod"] });
            c.Equal(4, run.Damage(5), "outside a fight: 5 HP lost are 4");
            c.Equal(0, run.Damage(1), "and 1 is none");
            c.Equal(76, run.State.Hp, "76 of 80");
        });

        c.Group("Tungsten Rod acts before Buffer: a loss of 1 that the rod takes costs no Buffer", () =>
        {
            // The rod is in the first pass after Block, Buffer in the late one (BufferPower.cs), which then sees 0.
            var f = Against(Hitter(1), "TungstenRod");
            f.ApplyPower<BufferPower>(f.Player, 1, f.Player);
            f.EndTurn();
            c.Equal((80, 1), (f.Player.Hp, f.Player.PowerAmount<BufferPower>()), "no HP lost and the Buffer is still there");
        });
    }

    // ---------- the moment of each hook ----------

    /// <summary>Writes down what it sees at four moments of the player's turn start. Check only.</summary>
    private sealed class Probe : Relic
    {
        public int BlockAtEnergyResetTurn2, BlockAtEnergyResetTurn3;
        public int EnemyHpEarly, UpgradedEarly;
        public int EnemyHpLate, UpgradedLate, EnergyLate, VigorLate, StrengthLate;
        public int EnergyAfterSide, VigorAfterSide, StrengthAfterSide, EnemyStrengthAfterSide;

        public override string Name => "Probe";
        public override string Describe() => "Check only.";

        public override void AfterEnergyReset(Fight f)
        {
            if (f.Turn == 2) BlockAtEnergyResetTurn2 = f.Player.Block;
            if (f.Turn == 3) BlockAtEnergyResetTurn3 = f.Player.Block;
        }

        public override void AfterPlayerTurnStartEarly(Fight f)
        {
            if (f.Turn != 1) return;
            EnemyHpEarly = f.Enemy(0).Hp;
            UpgradedEarly = f.Hand.Count(card => card.IsUpgraded);
        }

        public override void AfterPlayerTurnStartLate(Fight f)
        {
            if (f.Turn != 1) return;
            EnemyHpLate = f.Enemy(0).Hp;
            UpgradedLate = f.Hand.Count(card => card.IsUpgraded);
            EnergyLate = f.Energy;
            VigorLate = f.Player.PowerAmount<VigorPower>();
            StrengthLate = f.Player.PowerAmount<StrengthPower>();
        }

        public override void AfterSideTurnStartLate(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            if (f.Turn != 1 || side != Side.Player) return;
            EnergyAfterSide = f.Energy;
            VigorAfterSide = f.Player.PowerAmount<VigorPower>();
            StrengthAfterSide = f.Player.PowerAmount<StrengthPower>();
            EnemyStrengthAfterSide = f.Enemy(0).PowerAmount<StrengthPower>();
        }
    }

    /// <summary>Puts a Fire Potion into the belt in the first pass of "before combat start". Check only.</summary>
    private sealed class EarlyPotion : Relic
    {
        public override string Name => "Early Potion";
        public override string Describe() => "Check only.";
        public override void BeforeCombatStart(Fight f) => f.GainPotion(Content.Potion("FirePotion"));
    }

    private static void HookMoments(Check c)
    {
        c.Group("each relic acts in the game's own hook of the turn start", () =>
        {
            // CombatManager.StartTurn: "after block cleared" (Horn Cleat, Captain's Wheel), then per player
            // energy, the draw and "after player turn start" (Mercury Hourglass, Bellows), then "after side
            // turn start" for everybody (Lantern, Akabeko, Brimstone).
            var f = With("MercuryHourglass", "Bellows", "Lantern", "Akabeko", "Brimstone", "HornCleat", "CaptainsWheel")
                .Relic(new Probe()).Enemy(new CheckDummy()).Start();
            f.EndTurn();
            f.EndTurn();
            var probe = f.Relics.OfType<Probe>().Single();
            c.Equal((100, 0), (probe.EnemyHpEarly, probe.UpgradedEarly), "before 'after player turn start': no damage yet, no upgrade yet");
            c.Equal((97, 5), (probe.EnemyHpLate, probe.UpgradedLate), "after it: Mercury Hourglass has hit and Bellows has upgraded the hand");
            c.Equal((3, 0, 0), (probe.EnergyLate, probe.VigorLate, probe.StrengthLate), "but Lantern, Akabeko and Brimstone have not acted yet");
            c.Equal((4, 8, 2, 1), (probe.EnergyAfterSide, probe.VigorAfterSide, probe.StrengthAfterSide, probe.EnemyStrengthAfterSide), "after 'after side turn start' they have");
            c.Equal((14, 18), (probe.BlockAtEnergyResetTurn2, probe.BlockAtEnergyResetTurn3), "Horn Cleat's and Captain's Wheel's Block is there before the turn's energy is");
        });

        c.Group("Petrified Toad acts in the late pass of 'before combat start'", () =>
        {
            // The toad was picked up first; a relic that fills a slot in the first pass still gets slot 0.
            var f = With("PetrifiedToad").Relic(new EarlyPotion()).Enemy(new CheckDummy()).Start();
            c.Sequence(["FirePotion", Relics1.PotionShapedRock, null], Belt(f), "the rock comes second");
        });
    }

    // ---------- pickup effects ----------

    private static void Pickups(Check c)
    {
        c.Group("Strawberry, Pear, Mango: 7, 10 and 14 max HP at pickup, each healing as much", () =>
        {
            (int Hp, int MaxHp) After(string relic, int hp = 50)
            {
                var run = Outside(new RunSetup { Hp = hp });
                run.ObtainRelic(relic);
                return (run.State.Hp, run.State.MaxHp);
            }
            c.Equal((57, 87), After("Strawberry"), "Strawberry from 50 of 80");
            c.Equal((60, 90), After("Pear"), "Pear");
            c.Equal((64, 94), After("Mango"), "Mango");
            c.Equal((87, 87), After("Strawberry", hp: 80), "Strawberry at full HP");

            var all = Outside(new RunSetup { Hp = 50 });
            foreach (var relic in new[] { "Strawberry", "Pear", "Mango" }) all.ObtainRelic(relic);
            c.Equal((81, 111), (all.State.Hp, all.State.MaxHp), "all three: 31 more of each");
            c.Sequence(["Burning Blood", "Strawberry", "Pear", "Mango"], all.State.Relics.Select(r => r.Name), "in pickup order");
        });

        c.Group("Lee's Waffle: 7 max HP and a heal to full", () =>
        {
            var run = Outside(new RunSetup { Hp = 12 });
            run.ObtainRelic("LeesWaffle");
            c.Equal((87, 87), (run.State.Hp, run.State.MaxHp), "from 12 of 80 to 87 of 87");
        });

        c.Group("a relic's text is what get_deck shows, and a room that is no fight leaves the fight relics alone", () =>
        {
            var run = Outside(new RunSetup { Relics = ["BurningBlood", .. Relics1.All] });
            c.Equal((80, 80, 99), (run.State.Hp, run.State.MaxHp, run.State.Gold), "relics a run starts with have no pickup effect (they were not obtained)");
            var relics = run.DescribeDeck()["relics"]!.AsArray();
            c.Equal(26, relics.Count, "26 relics listed");
            c.Equal("""{"name":"Anchor","text":"At the start of every fight: gain 10 Block. Dexterity and Frail do not change it."}""", Text(relics[1]), "name and text");
            foreach (var room in new[] { RoomKind.Shop, RoomKind.RestSite, RoomKind.Treasure, RoomKind.Event })
                run.AfterRoomEntered(room);
            c.Equal((80, 80, 99), (run.State.Hp, run.State.MaxHp, run.State.Gold), "entering a shop, a rest site, a treasure room and an event changes nothing");
        });
    }

    // ---------- over a run ----------

    private static void OverARun(Check c)
    {
        c.Group("fight relics act again in every fight of a run, and the run replays", () =>
        {
            // Two fights against 40 HP that hit for 12. Every fight: 10 Block (Anchor), 1 Strength (Vajra),
            // 4 energy on turn 1 (Lantern), 3 damage at each turn start (Mercury Hourglass).
            //   turn 1: 40 - 3 = 37; four Strikes of 7: 9 left; the hit of 12 meets 10 Block: 2, less 1 (Tungsten Rod).
            //   turn 2 (the four Defends and Bash in hand): 9 - 3 = 6; Bash for 9 kills. Burning Blood heals 6.
            // HP: 60 - 1 + 6 = 65, then 65 - 1 + 6 = 70.
            var setup = new RunSetup { Hp = 60, Relics = ["BurningBlood", "Anchor", "Vajra", "Lantern", "MercuryHourglass", "TungstenRod"] };
            RunDirector Director() => new FixedFights(RunDummy(40, damage: 12), RunDummy(40, damage: 12, RoomType.Elite));
            var run = new Arena.Run(setup, Director());

            foreach (var after in new[] { 65, 70 })
            {
                var f = run.Fight!;
                c.Equal((10, 1, 4, 37), (f.Player.Block, Strength(f.Player), f.Energy, f.Enemy(0).Hp), $"the fight that ends on {after} HP: Block, Strength, energy and the enemy's HP on turn 1");
                Do(run, "play:0>e0", "play:0>e0", "play:0>e0", "play:0>e0");
                c.Equal(9, run.Fight!.Enemy(0).Hp, "four Strikes of 7");
                Do(run, "end_turn");
                c.Equal((after - 6, 0, 3, 6), (run.Fight!.Player.Hp, run.Fight.Player.Block, run.Fight.Energy, run.Fight.Enemy(0).Hp), "turn 2: 1 HP lost, no Block, 3 energy, 3 more damage");
                Do(run, "play:4>e0");
                c.Equal(after, run.Hp, "won, and healed 6");
            }
            c.That(run.IsOver && run.Result == RunResult.Cleared, "the run is cleared");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, Director(), run.Actions).Dump(), "it replays from its actions");
        });

        c.Group("Petrified Toad over two fights: a rock that was not thrown is kept, and the next fight adds one", () =>
        {
            var setup = new RunSetup { Relics = ["BurningBlood", "PetrifiedToad"] };
            var run = new Arena.Run(setup, new FixedFights(RunDummy(6), RunDummy(21)));
            c.Sequence([Relics1.PotionShapedRock, null, null], run.Potions.Select(p => p?.ClassName), "fight 1: one rock");
            Do(run, "play:0>e0");
            c.Sequence([Relics1.PotionShapedRock, Relics1.PotionShapedRock, null], run.Potions.Select(p => p?.ClassName), "fight 2: the old one and a new one");
            Do(run, "potion:0>e0", "play:0>e0");
            c.That(run.IsOver && run.Result == RunResult.Cleared, "15 and a Strike of 6 win against 21 HP");
            c.Sequence([null, Relics1.PotionShapedRock, null], run.State.Belt.Slots.Select(p => p?.ClassName), "the belt the run keeps");
        });
    }

    // ---------- pulls from the real bags ----------

    private static void Pulls(Check c)
    {
        c.Group("seed 1BUSLN0FY5, the player's bag from the front: the first relic of each list that the arena holds", () =>
        {
            // The lists as the game saved them at Neow (run-164514): Uncommon starts Stone Cracker, Parrying
            // Shield, Ornamental Fan, Kusarigama, Pen Nib, Pantograph, Self-Forming Clay, Nunchaku, Miniature
            // Cannon, Mercury Hourglass; Common starts Regal Pillow, Strawberry; Rare starts Shuriken,
            // Rainbow Ring, Prayer Wheel, Captain's Wheel.
            var run = Seeded(PlayedSeed);
            var world = run.State.Part<RunWorld>();

            // The real run's event relic: the 26th number of rewards rolled Uncommon and gave Stone Cracker,
            // which is chunk 26's. Strict stops there; otherwise the next one the arena holds is given.
            world.Streams.FastForward(RunStream.Rewards, 25);
            var strict = RunRelics.RollAndPullFromFront(run.State, world.Streams.Draw(RunStream.Rewards, Purpose.RelicRarity()), strict: true);
            c.Equal((false, "StoneCracker", RelicRarity.Uncommon), (strict.Found, strict.GameRelic, strict.Asked), "strict: the game gives Stone Cracker, which is not one of the first relics");
            c.That(strict.Reason is { } reason && reason.Contains("StoneCracker", StringComparison.Ordinal) && reason.Contains("chunk 26", StringComparison.Ordinal), $"and the reason says who builds it ({strict.Reason})");

            var uncommon = RunRelics.PullFromFront(run.State, RelicRarity.Uncommon);
            c.Equal("MercuryHourglass", uncommon.Relic, "drawn again: Mercury Hourglass");
            c.Sequence(["StoneCracker", "ParryingShield", "OrnamentalFan", "Kusarigama", "PenNib", "Pantograph", "SelfFormingClay", "Nunchaku", "MiniatureCannon"], uncommon.PassedOver, "nine relics passed over");
            c.That(!uncommon.IsTheGames && uncommon.GameRelic == "StoneCracker", "which is not the game's own pull");
            c.Equal("StoneCracker", world.PlayerBag.Relics(RelicRarity.Uncommon)[0], "Stone Cracker stays at the front of the bag");
            c.That(!world.PlayerBag.Contains("MercuryHourglass") && !world.SharedBag.Contains("MercuryHourglass"), "the relic that was pulled has left both bags");

            var common = RunRelics.PullFromFront(run.State, RelicRarity.Common);
            c.Equal("Strawberry", common.Relic, "Common: Strawberry");
            c.Sequence(["RegalPillow"], common.PassedOver, "past Regal Pillow");
            var rare = RunRelics.PullFromFront(run.State, RelicRarity.Rare);
            c.Equal("CaptainsWheel", rare.Relic, "Rare: Captain's Wheel");
            c.Sequence(["Shuriken", "RainbowRing", "PrayerWheel"], rare.PassedOver, "past three");
        });

        c.Group("seed 1BUSLN0FY5, the player's bag from the back: what a shop gets where the real one showed Paper Phrog, Pendulum, Ringing Triangle", () =>
        {
            // The backs of the saved lists: Uncommon ends Pear, Sparkling Rouge, Paper Phrog, Lucky Fysh,
            // Bowler Hat (the last two are never sold); Common ends Anchor, Pendulum; Shop ends Sling of
            // Courage, Wing Charm, Orrery, Ringing Triangle.
            var run = Seeded(PlayedSeed);
            var first = RunRelics.PullFromBack(run.State, RelicRarity.Uncommon);
            var second = RunRelics.PullFromBack(run.State, RelicRarity.Common);
            var third = RunRelics.PullFromBack(run.State, RelicRarity.Shop);
            c.Sequence(["PaperPhrog", "Pendulum", "RingingTriangle"], new[] { first.GameRelic, second.GameRelic, third.GameRelic }, "the game's three, as its record of the run has them");
            c.Sequence(["Pear", "Anchor", "SlingOfCourage"], new[] { first.Relic, second.Relic, third.Relic }, "the arena's three");
            c.Sequence(["PaperPhrog", "SparklingRouge"], first.PassedOver, "Uncommon: two passed over (the two a shop never sells are not counted)");
            c.Sequence(["Pendulum"], second.PassedOver, "Common: one");
            c.Sequence(["RingingTriangle", "Orrery", "WingCharm"], third.PassedOver, "Shop: three, and a built Shop relic was left to land on without a roll");
        });

        c.Group("seed 1BUSLN0FY5, a chest: the front of the shared bag", () =>
        {
            // The shared lists as saved: Uncommon starts Akabeko; Rare starts Tungsten Rod; Common starts
            // Festive Popper, Strike Dummy, Oddly Smooth Stone.
            var run = Seeded(PlayedSeed);
            var uncommon = RunRelics.PullFromShared(run.State, RelicRarity.Uncommon);
            var rare = RunRelics.PullFromShared(run.State, RelicRarity.Rare);
            var common = RunRelics.PullFromShared(run.State, RelicRarity.Common);
            c.That(uncommon is { Relic: "Akabeko", IsTheGames: true }, $"Uncommon: Akabeko, the game's own pull ({uncommon.Relic})");
            c.That(rare is { Relic: "TungstenRod", IsTheGames: true }, $"Rare: Tungsten Rod, the game's own pull ({rare.Relic})");
            c.Equal("OddlySmoothStone", common.Relic, "Common: Oddly Smooth Stone");
            c.Sequence(["FestivePopper", "StrikeDummy"], common.PassedOver, "past two");
        });

        c.Group("seed 75F277YN2G: the first shop of the real run put Lantern on its shelf, then Red Skull and Burning Sticks", () =>
        {
            // The game's record of that run (history/1791592972.run, floor 3): relic slots Lantern, Red Skull,
            // Burning Sticks. Lantern and Red Skull are Common, so both rolled slots took the back of the
            // player's Common list; the third slot is the back of the Shop list. No relic was pulled before.
            var run = Seeded("75F277YN2G");
            var first = RunRelics.PullFromBack(run.State, RelicRarity.Common);
            c.That(first is { Relic: "Lantern", IsTheGames: true }, $"the back of the Common list is Lantern, a relic of this chunk: the game's own pull ({first.Relic}, the game gives {first.GameRelic})");
            c.Equal("RedSkull", RunRelics.PullFromBack(run.State, RelicRarity.Common, strict: true).GameRelic, "behind it stands Red Skull");
            c.Equal("BurningSticks", RunRelics.PullFromBack(run.State, RelicRarity.Shop, strict: true).GameRelic, "and the back of the Shop list is Burning Sticks");
        });
    }

    // ---------- relic rows on a rewards screen ----------

    private static readonly Dictionary<string, Action<Arena.Run, Desk>> OfferCommon = new()
    {
        ["offer"] = static (run, _) => RunRewards.OfferCustom(run, [new RelicReward(RelicRarity.Common)]),
    };

    private static readonly Dictionary<string, Action<Arena.Run, Desk>> OfferRolledAfterTheUncommons = new()
    {
        ["offer"] = static (run, _) =>
        {
            foreach (var relic in Relics1.Uncommon) run.ObtainRelic(relic);
            run.State.Part<RunWorld>().Streams.FastForward(RunStream.Rewards, 25);
            RunRewards.OfferCustom(run, [new RelicReward()]);
        },
    };

    /// <summary>An Elite fight against a dummy on the seed's own streams, then its rewards.</summary>
    private sealed class EliteAndRewards : RunDirector
    {
        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State);
            run.State.ActFloor = 7;
            run.State.TotalFloor = 7;
            run.StartFight(RunDummy(hp: 12, room: RoomType.Elite));
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight)
        {
            if (RunRewards.OfferForRoomEnd(run, fight) is null) run.End(RunResult.Error, "no rewards were offered");
        }

        public override void AfterRoom(Arena.Run run) => run.End(RunResult.Cleared);
    }

    private static void ThroughTheRewards(Check c)
    {
        c.Group("a relic row holds a relic of the arena, and taking it runs the pickup effect", () =>
        {
            var run = Seeded(PlayedSeed, OfferCommon);
            Do(run, "offer");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "the rewards screen");
            var row = s.State["rewards"]![0]!;
            c.Equal("""{"n":0,"kind":"relic","name":"Strawberry","text":"When picked up: gain 7 max HP."}""", Text(row), "the row");
            c.Equal("reward:0=Take relic Strawberry", Labels(s).Split(" | ")[0], "its action");
            Do(run, "reward:0");
            c.Equal((87, 87), (run.State.Hp, run.State.MaxHp), "taken: 7 max HP");
            c.That(run.State.HasRelic("Strawberry"), "the player holds it");
            var log = run.State.Part<LeftOutLog>();
            c.Sequence(["relic RegalPillow"], log.Entries.Select(e => $"{e.Kind} {e.ClassName}"), "the log names the relic the game would have given");
        });

        c.Group("a relic row whose rarity had to be rolled again: the log names the game's relic too", () =>
        {
            // The player holds all six Uncommon relics the arena has, and the 26th number of rewards rolls
            // Uncommon (the real run's event): the game gives Stone Cracker, no Uncommon of the arena is left,
            // and the rarity is rolled again until it names a list that has one. Whichever that is, the run
            // stopped being the game's own at Stone Cracker.
            var run = Seeded(PlayedSeed, OfferRolledAfterTheUncommons);
            Do(run, "offer");
            var name = run.Snapshot().State["rewards"]![0]!["name"]!.GetValue<string>();
            c.That(name is "Strawberry" or "Captain's Wheel", $"the row holds the first Common or Rare relic the arena has ({name})");
            var entries = run.State.Part<LeftOutLog>().Entries.Where(e => e.Kind == "relic").Select(e => e.ClassName).ToList();
            c.Equal("StoneCracker", entries.FirstOrDefault(), "the log starts with Stone Cracker, the relic the game gives");
            c.Sequence(name == "Strawberry" ? ["RegalPillow"] : ["Shuriken", "RainbowRing", "PrayerWheel"], entries.Skip(1), "then the relics passed over in the list the new roll named");
            c.Equal(0, run.State.Part<LeftOutLog>().FirstFloor, "so the run knows from which floor it is no longer the game's");
        });

        c.Group("an Elite's rewards on the seed's streams: the relic row is there, with the relic its rarity roll lands on", () =>
        {
            var setup = new RunSetup { Seed = PlayedSeed };
            var run = new Arena.Run(setup, new EliteAndRewards());
            Do(run, "play:0>e0", "play:0>e0");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "two Strikes win against 12 HP, and the rewards are up");
            var rows = s.State["rewards"]!.AsArray();
            var relicRow = rows.SingleOrDefault(r => r!["kind"]!.GetValue<string>() == "relic");
            c.That(relicRow is not null, $"one relic row among {string.Join(", ", rows.Select(r => r!["kind"]!.GetValue<string>()))}");
            if (relicRow is null) return;

            // The relic's rarity is the last number the rewards took (RewardsSet: potion chance, gold, the
            // potion, the three cards, then the Elite's relic). Read it off a second stream of the seed.
            var world = run.State.Part<RunWorld>();
            var second = new RunStreams(PlayedSeed).Long(RunStream.Rewards);
            for (var n = 1; n < world.Streams.Counter(RunStream.Rewards); n++) second.NextU64();
            var roll = second.NextFloat();
            var (expected, passedOver) = roll < 0.5f ? ("Strawberry", 1) : roll < 0.83f ? ("Mercury Hourglass", 9) : ("Captain's Wheel", 3);
            c.Equal(expected, relicRow["name"]!.GetValue<string>(), $"a roll of {roll}: the first relic the arena holds in that list of the saved bag");
            c.Equal(passedOver, run.State.Part<LeftOutLog>().Entries.Count(e => e.Kind == "relic"), "and the log holds every relic before it");
            c.Equal(7, run.State.Part<LeftOutLog>().FirstFloor, "from floor 7 on the run is no longer the game's own");

            Do(run, $"reward:{relicRow["n"]}");
            c.Equal(2, run.State.Relics.Count, "the relic is taken");
            c.Equal(expected, run.State.Relics[1].Name, "and it is the one the row named");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new EliteAndRewards(), run.Actions).Dump(), "the run replays");
        });
    }

    // ---------- whole fights ----------

    /// <summary>Plays a fight to its end and returns the ids taken, or what went wrong.</summary>
    private static (List<string> Ids, string? Problem) PlayOut(Fight f, IPolicy player)
    {
        var ids = new List<string>();
        for (var n = 0; n < 6000 && !f.IsOver; n++)
        {
            var snapshot = f.Snapshot();
            var id = player is IEnginePolicy searching ? searching.Choose(snapshot, f) : player.Choose(snapshot);
            if (snapshot.Actions.All(a => a.Id != id)) return (ids, $"chose '{id}', which is not offered on turn {f.Turn}");
            ids.Add(id);
            var result = f.Execute(id);
            if (!result.Ok) return (ids, $"'{id}' was refused on turn {f.Turn}: {result.Error}");
        }
        return (ids, f.IsOver ? null : "the fight did not end");
    }

    private static void WholeFights(Check c)
    {
        c.Group("every relic through whole fights of the game's encounters: nothing throws, nothing is refused, a fight replays", () =>
        {
            // Each relic alone and all 25 together, against two encounters each, played by Greedy; the
            // fights with all of them also by Lookahead, which plays on copies of the fight.
            var encounters = Content.Registry.EncounterNames.Order(StringComparer.Ordinal).ToList();
            var greedy = Policies.All.Single(policy => policy.Name == "Greedy");
            var sets = Relics1.All.Select(relic => new[] { relic }).Append(Relics1.All.ToArray()).ToList();
            var fights = 0;
            for (var i = 0; i < sets.Count; i++)
            {
                for (var k = 0; k < 2; k++)
                {
                    var encounter = encounters[(i * 2 + k) % encounters.Count];
                    var seed = 500 + i * 2 + k;
                    Fight Build() => new FightBuilder().Relics(["BurningBlood", .. sets[i]]).Encounter(encounter).Seed(seed).Start();
                    var what = $"{(sets[i].Length == 1 ? sets[i][0] : "all 25")} against {encounter}";
                    try
                    {
                        var first = Build();
                        var played = PlayOut(first, greedy);
                        c.Equal(null, played.Problem, $"Greedy, {what}");
                        var again = Build();
                        c.Sequence(played.Ids, PlayOut(again, greedy).Ids, $"Greedy, {what}: the replay");
                        c.Equal(first.Seen(), again.Seen(), $"Greedy, {what}: the same end");
                        fights++;
                        if (sets[i].Length == 1) continue;
                        c.Equal(null, PlayOut(Build(), new LookaheadPolicy(seed: 1, budget: 30, worlds: 2)).Problem, $"Lookahead, {what}");
                    }
                    catch (Exception ex)
                    {
                        c.That(false, $"{what} threw {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            c.Equal(52, fights, "52 fights were played twice");
        });
    }

    // ---------- the forecast experiment ----------

    private static void ForecastMark(Check c)
    {
        c.Group("with Brimstone held, the forecast marks every number of an enemy's coming hits, as the mod does", () =>
        {
            // A lone Nibbit: Butt 12, Slice 6 and Block, Hiss (2 Strength), Butt. Without Brimstone the
            // coming hits are plain: 6, then 12 + 2 = 14.
            var plain = new FightBuilder().Hp(900, 900).Enemy(new Nibbit { IsAlone = true }).Start();
            c.Sequence(["attack 6 + defend", "buff", "attack 14"], Forecast.For(plain, plain.Enemy(0)) ?? [], "no Brimstone");

            // With it the Nibbit has 1 Strength on turn 1 and will have more by each coming hit. The mod
            // does not add that up: it prints today's number and marks it (SpireMind/Game/Forecast.cs).
            var f = new FightBuilder().Hp(900, 900).Relics("BurningBlood", "Brimstone").Enemy(new Nibbit { IsAlone = true }).Start();
            c.Sequence(["attack 13"], f.IntentTexts(f.Enemy(0)), "the shown intent has the Strength of turn 1");
            c.Sequence(["attack ~7 + defend", "buff", "attack ~15"], Forecast.For(f, f.Enemy(0)) ?? [], "the coming hits are marked");
        });
    }
}
