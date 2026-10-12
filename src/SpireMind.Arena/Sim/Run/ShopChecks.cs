using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The shop: what it sells, what each thing costs, buying, the removal, the hooks and the
/// screen. Chunk 10 of the run-mode plan.
/// <para>
/// Where the numbers come from. (1) Two real shops, from the game's own records of two runs
/// of v0.107.1 (sts2-notes/golden-saves/history): the first shop of the run on seed 1BUSLN0FY5
/// (1791755111.run, floor 5) and the first shop of the run on seed 75F277YN2G (1791592972.run,
/// floor 3). A record lists what was still on the shelf when the shop was left, in shelf
/// order, what was bought and the gold spent. What the record holds is typed in below; the
/// folder is not needed. (2) A second reading of the game's draw order, written straight down
/// in this file from MerchantInventory, the four entry classes, CardFactory.CreateForMerchant,
/// PotionFactory and RelicFactory, which calls nothing of the shop: <see cref="Read"/>. It
/// gives both real shops slot for slot, and every shop the shop code makes on 300 seeds is
/// compared with it. (3) Shelves built by hand, entry by entry with fixed prices, where every
/// number of a purchase can be worked out on paper; and the mod's ShopReader and
/// docs/screens.md for keys, order, labels and refusals.
/// </para>
/// <para>
/// A shop can only be shown when the arena has built an Uncommon and a Rare colorless card
/// and relics of the bags. The checks that need a whole rolled shop ask first whether one can
/// be made from what is registered, and otherwise hold that the run ends as unrepresentable;
/// everything else brings its own cards and relics.
/// </para>
/// </summary>
public sealed class ShopChecks : IArenaChecks
{
    public void Run(Check c)
    {
        PriceTables(c);
        RealShops(c);
        RealShopsFromTheShop(c);
        AgainstTheSecondReading(c);
        AlwaysFilled(c);
        BagWalk(c);
        KeyedStreams(c);
        Entering(c);
        Screen(c);
        Buying(c);
        PotionBuys(c);
        Removal(c);
        PriceHooks(c);
        PurchaseHooks(c);
        CardHooks(c);
        Copies(c);
        HookList(c);
    }

    // =====================================================================================
    // The game's record of two real shops
    // =====================================================================================

    /// <summary>One real shop as the game's run record has it, and where the run stood when it was entered.</summary>
    /// <param name="RewardsBefore">How many numbers the rewards stream had given before the shop.</param>
    /// <param name="NonRareRewardCards">How many reward cards had been rolled before, none of them Rare: the pity offset is -0.05 plus 0.01 for each.</param>
    /// <param name="HeldBagRelics">Bag relics the player held already: they had left both bags.</param>
    /// <param name="Bought">The card that was bought, and for how much: the one price a record shows.</param>
    /// <param name="CardsLeft">The character cards still on the shelf at the end, in shelf order.</param>
    private sealed record RealShop(string Seed, int RewardsBefore, int NonRareRewardCards, string[] HeldBagRelics, string Bought, int BoughtFor,
        string[] CardsLeft, string[] Colorless, string[] Relics, string[] Potions);

    // history/1791755111.run, seed 1BUSLN0FY5, floor 5. Before it: Arcane Scroll's card (1 number of rewards, no rarity roll), two
    // hallway fights (11 and 13 numbers, six cards, none Rare), the relic of This or That (1 number; Stone Cracker, held since).
    // The record: card_choices ASHEN_STRIKE, TREMBLE, BLOOD_WALL, VICIOUS, DISCOVERY, CALAMITY; cards_gained TEAR_ASUNDER; gold_spent 77;
    // relic_choices PAPER_PHROG, PENDULUM, RINGING_TRIANGLE; potion_choices BLOCK_POTION, POTION_OF_BINDING, SHACKLING_POTION.
    private static readonly RealShop FirstRun = new("1BUSLN0FY5", 26, 6, ["StoneCracker"], "TearAsunder", 77,
        ["AshenStrike", "Tremble", "BloodWall", "Vicious"], ["Discovery", "Calamity"],
        ["PaperPhrog", "Pendulum", "RingingTriangle"], ["BlockPotion", "PotionOfBinding", "ShacklingPotion"]);

    // history/1791592972.run, seed 75F277YN2G (its first act is Underdocks), floor 3. Before it: Pomander (no number) and one hallway
    // fight (11 numbers, three cards, none Rare). The record: card_choices POMMEL_STRIKE, TAUNT, SECOND_WIND, JUGGLING,
    // JACK_OF_ALL_TRADES, SECRET_TECHNIQUE; cards_gained ANGER; cards_removed STRIKE_IRONCLAD; gold_spent 99, which is the removal's 75
    // and 24 for Anger; relic_choices LANTERN, RED_SKULL, BURNING_STICKS; potion_choices STABLE_SERUM, SWIFT_POTION, WEAK_POTION.
    private static readonly RealShop SecondRun = new("75F277YN2G", 11, 3, [], "Anger", 24,
        ["PommelStrike", "Taunt", "SecondWind", "Juggling"], ["JackOfAllTrades", "SecretTechnique"],
        ["Lantern", "RedSkull", "BurningSticks"], ["StableSerum", "SwiftPotion", "WeakPotion"]);

    /// <summary>The pity offset after so many reward cards that were not Rare, added up in 32-bit floats as the game does.</summary>
    private static float Pity(int nonRareCards)
    {
        var offset = -0.05f;
        for (var i = 0; i < nonRareCards; i++) offset = Math.Min(offset + 0.01f, 0.4f);
        return offset;
    }

    // =====================================================================================
    // The second reading: the game's shop straight down its draw order
    // =====================================================================================

    private sealed record Shelved(string Name, int Cost, bool OnSale = false);

    private sealed class Reading
    {
        public readonly List<Shelved> Cards = new(), Colorless = new(), Relics = new(), Potions = new();

        /// <summary>The price of the card on sale before it was halved.</summary>
        public int SalePriceBeforeHalving = -1;

        /// <summary>What the arena's rule for unbuilt things could not fill, or null.</summary>
        public string? Stopped;

        /// <summary>Everything a roll landed on and the arena has not built, in order.</summary>
        public readonly List<string> PassedOver = new();

        /// <summary>How often a relic's rarity was rolled again because its list held no built relic.</summary>
        public int Rerolls;

        public IEnumerable<Shelved> All => Cards.Concat(Colorless).Concat(Relics).Concat(Potions);
    }

    /// <summary>The five relics whose classes say a shop never sells them.</summary>
    private static readonly string[] NeverSold = ["AmethystAubergine", "BowlerHat", "LuckyFysh", "OldCoin", "TheCourier"];

    private static float Between(GameRng rng, float low, float high) => (float)(rng.NextDouble() * (double)(high - low) + (double)low);

    private static int Nearest(float value) => (int)MathF.Round(value);

    /// <summary>The next sellable relic from the back of the player's bag, falling through Shop, Common, Uncommon, Rare; it leaves both bags.</summary>
    private static string? BackOfTheBag(RunWorld world, RelicRarity rarity)
    {
        for (var r = rarity; r != RelicRarity.None;
             r = r switch { RelicRarity.Shop => RelicRarity.Common, RelicRarity.Common => RelicRarity.Uncommon, RelicRarity.Uncommon => RelicRarity.Rare, _ => RelicRarity.None })
        {
            var list = world.PlayerBag.Relics(r);
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (NeverSold.Contains(list[i])) continue;
                var relic = list[i];
                world.PlayerBag.Remove(relic);
                world.SharedBag.Remove(relic);
                return relic;
            }
        }
        return null;
    }

    /// <summary>
    /// The shop the game makes for a run that stands as <paramref name="state"/> does, read
    /// straight down the game's code, on the run's two long player streams (which it moves, with
    /// the bags). With <paramref name="everythingBuilt"/> it is the game's own shop. Without, the
    /// arena's rule for what it has not built is applied by hand: a card pick that lands on an
    /// unbuilt card is drawn again from shops; a potion's rarity and pick are both drawn again;
    /// a relic is the relic framework's own pull.
    /// </summary>
    /// <param name="colorlessAndRelics">False reads only the character's cards and the potions, as if the shelf had no other
    /// places: the two kinds the arena can always fill.</param>
    private static Reading Read(RunState state, bool everythingBuilt, bool colorlessAndRelics = true)
    {
        var world = state.Part<RunWorld>();
        if (!ReferenceEquals(world.Unlocks, RunUnlocks.All)) throw new InvalidOperationException("The second reading is written for a profile with everything unlocked.");
        var rewards = world.Streams.Long(RunStream.Rewards);
        var shops = world.Streams.Long(RunStream.Shops);
        var reading = new Reading();
        bool CardBuilt(string name) => everythingBuilt || Content.Registry.Has(ContentKind.Card, name);
        bool PotionBuilt(string name) => everythingBuilt || Content.Registry.Has(ContentKind.Potion, name);

        GameCard? Pick(List<GameCard> items)
        {
            if (items.Count == 0) throw new InvalidOperationException("The game has no card to pick here.");
            if (!items.Any(card => CardBuilt(card.ClassName))) return null;
            while (true)
            {
                var landed = items[shops.NextInt(0, items.Count)];
                if (CardBuilt(landed.ClassName)) return landed;
                reading.PassedOver.Add(landed.ClassName);
            }
        }

        // MerchantInventory.PopulateCharacterCardEntries: the sale's number, then five cards. Each: a rarity on rewards (the shop
        // table with the pity offset, which does not move), the pick on shops among the pool's cards of that rarity and type that
        // the shelf does not show, an upgrade roll on rewards that cannot succeed, a price factor on shops; the card on sale a second one.
        var sale = shops.NextInt(5);
        var shown = new List<string>();
        var ironclad = CardPools.Ironclad.All.Where(card => !card.MultiplayerOnly && card.Rarity != CardRarity.Basic).ToList();
        CardType[] types = [CardType.Attack, CardType.Attack, CardType.Skill, CardType.Skill, CardType.Power];
        for (var i = 0; i < 5; i++)
        {
            var options = ironclad.Where(card => !shown.Contains(card.ClassName)).ToList();
            var roll = rewards.NextFloat();
            var rare = 0.09f + state.CardRarityOffset;
            var rarity = roll < rare ? CardRarity.Rare : roll < 0.37f + rare ? CardRarity.Uncommon : CardRarity.Common;
            for (var steps = 0; !options.Any(card => card.Rarity == rarity && card.Type == types[i]); steps++)
            {
                if (steps > 2) throw new InvalidOperationException("No rarity has a card of the type.");
                rarity = rarity switch { CardRarity.Common => CardRarity.Uncommon, CardRarity.Uncommon => CardRarity.Rare, _ => CardRarity.Common };
            }
            var card = Pick(options.Where(option => option.Rarity == rarity && option.Type == types[i]).ToList());
            if (card is null)
            {
                reading.Stopped = "a character card";
                return reading;
            }
            rewards.NextFloat();
            shown.Add(card.ClassName);
            var table = card.Rarity switch { CardRarity.Rare => 150, CardRarity.Uncommon => 75, _ => 50 };
            var cost = Nearest((float)table * Between(shops, 0.95f, 1.05f));
            if (i == sale)
            {
                reading.SalePriceBeforeHalving = Nearest((float)table * Between(shops, 0.95f, 1.05f));
                cost = reading.SalePriceBeforeHalving / 2;
            }
            reading.Cards.Add(new Shelved(card.ClassName, cost, i == sale));
        }

        // PopulateColorlessCardEntries: an Uncommon, then a Rare. No rarity roll; the pick, the upgrade roll, the price.
        var colorless = CardPools.Colorless.All.Where(card => !card.MultiplayerOnly && card.Rarity != CardRarity.Basic).ToList();
        foreach (var rarity in colorlessAndRelics ? new[] { CardRarity.Uncommon, CardRarity.Rare } : [])
        {
            var card = Pick(colorless.Where(option => !shown.Contains(option.ClassName) && option.Rarity == rarity).ToList());
            if (card is null)
            {
                reading.Stopped = "a colorless card";
                return reading;
            }
            rewards.NextFloat();
            shown.Add(card.ClassName);
            var table = Nearest((float)(rarity == CardRarity.Rare ? 150 : 75) * 1.15f);
            reading.Colorless.Add(new Shelved(card.ClassName, Nearest((float)table * Between(shops, 0.95f, 1.05f))));
        }

        // PopulateRelicEntries: two rarities on rewards, then for each of three places the back of the player's bag and a price.
        RelicRarity Roll()
        {
            var roll = rewards.NextFloat();
            return roll < 0.5f ? RelicRarity.Common : roll < 0.83f ? RelicRarity.Uncommon : RelicRarity.Rare;
        }
        RelicRarity[] rarities = colorlessAndRelics ? [Roll(), Roll(), RelicRarity.Shop] : [];
        foreach (var rarity in rarities)
        {
            string? relic;
            if (everythingBuilt)
            {
                relic = BackOfTheBag(world, rarity);
                if (relic is null) throw new InvalidOperationException("The bag is empty.");
            }
            else
            {
                var pull = RunRelics.PullFromBack(state, rarity, rarity == RelicRarity.Shop ? null : rewards);
                if (!pull.Found)
                {
                    reading.Stopped = "a relic";
                    return reading;
                }
                reading.PassedOver.AddRange(pull.PassedOver);
                reading.Rerolls += pull.Rerolls;
                if (pull.Rerolls > 0 && pull.GameRelic is { } games && !pull.PassedOver.Contains(games)) reading.PassedOver.Add(games);
                relic = pull.Relic!;
            }
            var table = GameLists.BagRelic(relic).Rarity switch { RelicRarity.Common => 175, RelicRarity.Uncommon => 225, RelicRarity.Rare => 275, _ => 200 };
            reading.Relics.Add(new Shelved(relic, (int)Math.Round((double)((float)table * Between(shops, 0.85f, 1.15f)))));
        }

        // PopulatePotionEntries: three potions together (a rarity and a pick each, no two the same), then their prices.
        var potions = Scenarios.PotionPool.ToList();
        var made = new List<(string ClassName, PotionRarity Rarity)>();
        for (var i = 0; i < 3; i++)
        {
            if (!potions.Any(potion => PotionBuilt(potion.ClassName)))
            {
                reading.Stopped = "a potion";
                return reading;
            }
            while (true)
            {
                var roll = shops.NextFloat();
                var rarity = roll <= 0.1f ? PotionRarity.Rare : roll <= 0.35f ? PotionRarity.Uncommon : PotionRarity.Common;
                var ofRarity = potions.Where(potion => potion.Rarity == rarity).ToList();
                var landed = ofRarity[shops.NextInt(0, ofRarity.Count)];
                if (!PotionBuilt(landed.ClassName))
                {
                    reading.PassedOver.Add(landed.ClassName);
                    continue;
                }
                made.Add(landed);
                potions.Remove(landed);
                break;
            }
        }
        foreach (var (name, rarity) in made)
        {
            var table = rarity switch { PotionRarity.Rare => 100, PotionRarity.Uncommon => 75, _ => 50 };
            reading.Potions.Add(new Shelved(name, Nearest((float)table * Between(shops, 0.95f, 1.05f))));
        }
        return reading;
    }

    // =====================================================================================
    // Directors and shelves of the checks
    // =====================================================================================

    private static readonly Dictionary<string, Action<Arena.Run, Desk>> NoScript = new();

    /// <summary>A desk with one action, <c>enter</c>, which enters a shop: for a shop that is entered by an action of the run.</summary>
    private static readonly Dictionary<string, Action<Arena.Run, Desk>> Door = new() { ["enter"] = static (run, _) => ShopRoom.Enter(run) };

    /// <summary>
    /// Begins the run's world, puts the run where a check wants it (stream counters, pity offset,
    /// relics already held, the place on the map) and enters a shop: at once, or, when told not
    /// to, by the action <c>enter</c> of the desk it puts up instead.
    /// </summary>
    private sealed class Merchant : RunDirector
    {
        public int RewardsAt, ShopsAt;
        public float Offset = -0.05f;
        public StreamMode Mode = StreamMode.Game;
        public bool Strict;
        public bool Enter = true;
        public string[] Gone = [];
        public RelicRarity Emptied = RelicRarity.None;

        /// <summary>A rarity whose built relics have all left the bag: a pull of it can only land on relics the arena has not built.</summary>
        public RelicRarity OnlyUnbuilt = RelicRarity.None;

        public int Row = 4, Col = 6;
        public int Left;

        public override void Begin(Arena.Run run)
        {
            var state = run.State;
            var world = RunWorld.Begin(state, mode: Mode);
            if (Strict) state.Part<LeftOutLog>().Strict = true;
            world.Streams.FastForward(RunStream.Rewards, RewardsAt);
            world.Streams.FastForward(RunStream.Shops, ShopsAt);
            state.CardRarityOffset = Offset;
            foreach (var relic in Gone) RunRelics.TakeOutOfBags(state, relic);
            if (Emptied != RelicRarity.None)
                foreach (var relic in world.PlayerBag.Relics(Emptied).ToList()) RunRelics.TakeOutOfBags(state, relic);
            if (OnlyUnbuilt != RelicRarity.None)
                foreach (var relic in world.PlayerBag.Relics(OnlyUnbuilt).Where(RunRelics.IsBuilt).ToList()) RunRelics.TakeOutOfBags(state, relic);
            state.TotalFloor = Row + 1;
            state.ActFloor = Row + 1;
            world.Streams.MoveTo(0, Row, Col);
            if (Enter) ShopRoom.Enter(run);
            else run.Push(new Desk(Door));
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => run.End(RunResult.Cleared);

        public override void AfterRoom(Arena.Run run)
        {
            Left++;
            run.Push(new Desk(NoScript));
        }
    }

    private static Merchant At(RealShop real, bool enter, bool strict = false) => new()
    {
        RewardsAt = real.RewardsBefore, Offset = Pity(real.NonRareRewardCards), Gone = real.HeldBagRelics, Enter = enter, Strict = strict,
    };

    private static Arena.Run Start(string seed, RunDirector director, RunSetup? setup = null) =>
        new(setup is null ? new RunSetup { Seed = seed } : setup, director);

    private static RunWorld World(Arena.Run run) => run.State.Part<RunWorld>();

    private static int Counter(Arena.Run run, RunStream stream) => World(run).Streams.Counter(stream);

    private static ShopScreen Shop(Arena.Run run) => (ShopScreen)run.Top!;

    private static ShopInventory EmptyShelf() => new([], [], [], [], null);

    /// <summary>True when the arena has built enough for this run's shop to be shown at all.</summary>
    private static bool ShopShown(Arena.Run run) => run.Top is ShopScreen;

    private static readonly CardDef Trick = new("CheckTrick", "Check Trick", CardType.Skill, CardRarity.Uncommon, 0, TargetType.Self)
    {
        MaxUpgradeLevel = 0, Text = _ => "A card of the checks.",
    };

    private static readonly CardDef Marvel = new("CheckMarvel", "Check Marvel", CardType.Skill, CardRarity.Rare, 2, TargetType.Self)
    {
        MaxUpgradeLevel = 0, Text = _ => "A card of the checks.",
    };

    /// <summary>A relic of the checks that can raise max HP when it is picked up.</summary>
    private sealed class Keepsake : Relic
    {
        public string Title { get; init; } = "Check Keepsake";
        public int MaxHp { get; init; }
        public override string Name => Title;
        public override string Describe() => MaxHp > 0 ? $"At pickup: gain {MaxHp} Max HP." : "Does nothing.";

        public override void AfterObtained(RunContext c)
        {
            if (MaxHp > 0) c.GainMaxHp(MaxHp);
        }
    }

    private enum Shelf { Full, PotionsOnly, MemberFirst, WhetterFirst, HamperFirst }

    /// <summary>
    /// A shelf built by hand, with every price fixed. Full: Headbutt 52, Twin Strike 24 on sale,
    /// Shrug It Off 48, Flame Barrier 78, Inflame 73; two cards of the checks at 85 and 168;
    /// three relics of the checks at 147 (gives 7 Max HP), 236 and 218; Fire Potion 52, Swift
    /// Potion 49, Fruit Juice 104; the removal at its price for the run.
    /// </summary>
    private static ShopInventory Build(Arena.Run run, Shelf kind)
    {
        var state = run.State;
        var unlocks = CardPools.UnlocksOf(run);
        var ironclad = CardPools.Character.Unlocked(unlocks);
        var colorless = CardPools.Colorless.Unlocked(unlocks);
        CardInstance Card(string name) => state.NewCard(Content.Registry.Card(name));
        List<ShopPotionEntry> potions =
        [
            new ShopPotionEntry(0).Stock(Content.Potion("FirePotion"), 52),
            new ShopPotionEntry(1).Stock(Content.Potion("SwiftPotion"), 49),
            new ShopPotionEntry(2).Stock(Content.Potion("FruitJuice"), 104),
        ];
        if (kind == Shelf.PotionsOnly) return new ShopInventory([], [], [], potions, null);

        Relic first = kind switch
        {
            Shelf.MemberFirst => new Haggle { Title = "Check Member", Times = 0.5m },
            Shelf.WhetterFirst => new Whetter(),
            Shelf.HamperFirst => new Hamper(),
            _ => new Keepsake { Title = "Check Charm", MaxHp = 7 },
        };
        return new ShopInventory(
            [
                new ShopCardEntry(0, ironclad, CardType.Attack).Stock(Card("Headbutt"), 52),
                new ShopCardEntry(1, ironclad, CardType.Attack).Stock(Card("TwinStrike"), 24, onSale: true),
                new ShopCardEntry(2, ironclad, CardType.Skill).Stock(Card("ShrugItOff"), 48),
                new ShopCardEntry(3, ironclad, CardType.Skill).Stock(Card("FlameBarrier"), 78),
                new ShopCardEntry(4, ironclad, CardType.Power).Stock(Card("Inflame"), 73),
            ],
            [
                new ShopCardEntry(5, colorless, CardRarity.Uncommon).Stock(state.NewCard(Trick), 85),
                new ShopCardEntry(6, colorless, CardRarity.Rare).Stock(state.NewCard(Marvel), 168),
            ],
            [
                new ShopRelicEntry(0).Stock(first, 147),
                new ShopRelicEntry(1).Stock(new Keepsake { Title = "Check Idol" }, 236),
                new ShopRelicEntry(2).Stock(new Keepsake { Title = "Check Bell" }, 218),
            ],
            potions,
            new ShopRemovalEntry(ShopPrices.RemovalPrice(state)));
    }

    /// <summary>Puts up a shop with a shelf built by hand; a shop that is left is followed by the next one, then by an empty desk.</summary>
    private sealed class Stall : RunDirector
    {
        public Shelf Kind = Shelf.Full;

        /// <summary>Begin the run's world first: the boss is named, and a place that is filled again has streams to roll on.</summary>
        public bool World;

        public StreamMode Mode = StreamMode.Game;

        /// <summary>How many shops the run meets, one after the other.</summary>
        public int Shops = 1;

        /// <summary>Enter the room as a director does (ShopRoom.Enter with the shelf), so that the listeners hear of it.</summary>
        public bool Announce;

        public int Left;

        public override void Begin(Arena.Run run)
        {
            if (World)
            {
                var world = RunWorld.Begin(run.State, mode: Mode);
                run.State.TotalFloor = 5;
                run.State.ActFloor = 5;
                world.Streams.MoveTo(0, 4, 6);
            }
            if (Announce) ShopRoom.Enter(run, Build(run, Kind));
            else run.Push(new ShopScreen(Build(run, Kind)));
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => run.End(RunResult.Cleared);

        public override void AfterRoom(Arena.Run run)
        {
            Left++;
            if (Left < Shops) run.Push(new ShopScreen(Build(run, Kind)));
            else run.Push(new Desk(NoScript));
        }
    }

    private static Arena.Run Open(int gold, Stall? stall = null, string?[]? potions = null, Func<Relic>[]? relics = null, string seed = "1BUSLN0FY5")
    {
        var run = new Arena.Run(new RunSetup { Seed = seed, Gold = gold, Potions = potions ?? [], ExtraRelics = relics ?? [] }, stall ?? new Stall());
        Do(run, "open");
        return run;
    }

    private static JsonNode Stock(Arena.Run run) => run.Snapshot().State["stock"]!;

    private static string Numbers(JsonNode? list) => list is JsonArray array ? string.Join(",", array.Select(entry => entry!["n"]!.GetValue<int>())) : "(none)";

    private static string Names(JsonNode? list) => list is JsonArray array ? string.Join(", ", array.Select(entry => entry!["name"]!.GetValue<string>())) : "(none)";

    private static string Prices(JsonNode? list) => list is JsonArray array ? string.Join(",", array.Select(entry => entry!["price"]!.GetValue<int>())) : "(none)";

    /// <summary>Sends an id that must be refused and returns the refusal; the run must stand exactly as before.</summary>
    private static string Refused(Check c, Arena.Run run, string id)
    {
        var before = run.Dump();
        var actions = run.Actions.Count;
        var result = run.Execute(id);
        c.That(!result.Ok, $"'{id}' is refused");
        c.That(run.Dump() == before && run.Actions.Count == actions, $"a refused '{id}' changes nothing and is not in the save");
        return result.Error ?? "(accepted)";
    }

    // =====================================================================================
    // Check relics
    // =====================================================================================

    /// <summary>Changes shop prices: times a factor, minus an amount; optionally only for relic entries.</summary>
    private sealed class Haggle : Relic
    {
        public string Title { get; init; } = "Check Haggle";
        public decimal Times { get; init; } = 1m;
        public decimal Minus { get; init; }
        public bool OnlyRelics { get; init; }
        public override string Name => Title;
        public override string Describe() => "Changes shop prices.";

        public override decimal ModifyMerchantPrice(RunContext c, ShopEntry entry, decimal price) =>
            OnlyRelics && entry is not ShopRelicEntry ? price : price * Times - Minus;
    }

    /// <summary>Notes what the shop asks and tells it, and says whether a bought entry is filled again.</summary>
    private sealed class Clerk : Relic
    {
        public bool Refills { get; init; }
        public string Heard = "";
        public override string Name => "Check Clerk";
        public override string Describe() => "Listens in shops.";

        public override bool ShouldRefillMerchantEntry(RunContext c, ShopEntry entry)
        {
            Heard += $"refill? {entry.What};";
            return Refills;
        }

        public override void AfterItemPurchased(RunContext c, ShopEntry entry, int goldSpent) =>
            Heard += string.Create(CultureInfo.InvariantCulture, $"bought {entry.GetType().Name} for {goldSpent}, {(entry.IsStocked ? "stocked" : "empty")};");

        public override void AfterRoomEntered(RunContext c, RoomKind room) =>
            Heard += $"entered {room} with {(c.Run?.Top is ShopScreen shop ? shop.Inventory.AllEntries.Count(entry => entry.IsStocked) : -1)} things on the shelf;";
    }

    /// <summary>Upgrades the Attacks a shop sells, as Molten Egg does, and counts how often it was asked.</summary>
    private sealed class Whetter : Relic, IRewardHooks
    {
        public int Asked;
        public int Lists;
        public override string Name => "Check Whetter";
        public override string Describe() => "Shop Attacks come upgraded.";

        public void ModifyMerchantCardCreationResults(RunContext c, List<CardCreationResult> cards)
        {
            Lists++;
            Asked += cards.Count;
            foreach (var result in cards)
            {
                var card = result.Card;
                if (card.Type != CardType.Attack || !card.IsUpgradable) continue;
                result.ModifyCard(c.Run!.State.NewCard(card.Def, upgraded: true), this);
            }
        }
    }

    /// <summary>Offers a reward of 10 gold when it is picked up, as Orrery and Cauldron offer theirs.</summary>
    private sealed class Hamper : Relic
    {
        public override string Name => "Check Hamper";
        public override string Describe() => "At pickup: a reward of 10 gold.";
        public override void AfterObtained(RunContext c) => RunRewards.OfferCustom(c.NeedRun("its reward"), [new GoldReward(10)]);
    }

    /// <summary>Keeps every card out of the deck.</summary>
    private sealed class Bouncer : Relic, IDeckListener
    {
        public override string Name => "Check Bouncer";
        public override string Describe() => "No card gets into the deck.";
        public bool ShouldAddToDeck(RunContext c, CardInstance card) => false;
    }

    /// <summary>Keeps every potion out of the belt, as Sozu does.</summary>
    private sealed class Dry : Relic, IRewardHooks
    {
        public override string Name => "Check Dry";
        public override string Describe() => "No potion gets into the belt.";
        public bool ShouldProcurePotion(RunContext c, PotionDef potion) => false;
    }

    // =====================================================================================
    // Prices
    // =====================================================================================

    private static void PriceTables(Check c)
    {
        c.Group("the price tables", () =>
        {
            // MerchantCardEntry.GetCost, RelicModel.MerchantCost, MerchantPotionEntry.GetCost, MerchantCardRemovalEntry.
            var state = new RunState(new RunSetup());
            int Of(CardDef def) => ShopPrices.CardBase(state.NewCard(def));
            c.Equal(50, Of(Content.Card("Anger")), "a Common card");
            c.Equal(75, Of(Content.Card("Inflame")), "an Uncommon card");
            c.Equal(150, Of(Content.Card("Barricade")), "a Rare card");
            c.Equal(50, Of(Content.Card("StrikeIronclad")), "any other rarity counts as Common");
            c.Equal(75, Of(Trick), "a card that is in no pool pays no markup");

            // A card of the colorless pool: the table value times 1.15 in 32-bit floats, rounded half to even.
            // 75 x 1.15f = 86.25 -> 86. 150 x 1.15f is exactly 172.5 -> 172 (the even one), not 173.
            var finesse = new CardDef("Finesse", "Finesse", CardType.Skill, CardRarity.Uncommon, 0, TargetType.Self) { Text = _ => "" };
            var scrawl = new CardDef("Scrawl", "Scrawl", CardType.Skill, CardRarity.Rare, 1, TargetType.Self) { Text = _ => "" };
            c.Equal(86, Of(finesse), "an Uncommon colorless card");
            c.Equal(172, Of(scrawl), "a Rare colorless card");
            c.That((float)150 * 1.15f == 172.5f, "150 times 1.15 is exactly 172.5 in 32-bit floats");

            c.Equal((175, 225, 275, 200), (ShopPrices.RelicBase(RelicRarity.Common), ShopPrices.RelicBase(RelicRarity.Uncommon), ShopPrices.RelicBase(RelicRarity.Rare), ShopPrices.RelicBase(RelicRarity.Shop)), "relics by rarity");
            c.Equal((50, 75, 100), (ShopPrices.PotionBase(PotionRarity.Common), ShopPrices.PotionBase(PotionRarity.Uncommon), ShopPrices.PotionBase(PotionRarity.Rare)), "potions by rarity");
            c.Equal(75, ShopPrices.RemovalPrice(state), "the first removal of a run");
            state.Part<ShopState>().CardRemovalsUsed = 3;
            c.Equal(150, ShopPrices.RemovalPrice(state), "75 + 25 x 3 after three removals bought");
        });

        c.Group("a price factor: the fraction times the width of the range plus its lower end", () =>
        {
            // Rng.NextFloat(min, max): (float)(fraction * (double)(max - min) + (double)min), one number of the stream.
            var rng = new GameRng(77);
            var twin = rng.Clone();
            var low = float.MaxValue;
            var high = float.MinValue;
            var same = true;
            for (var i = 0; i < 20000; i++)
            {
                var fraction = twin.NextDouble();
                var factor = ShopPrices.Factor(rng, 0.95f, 1.05f);
                same &= factor == (float)(fraction * (double)(1.05f - 0.95f) + (double)0.95f);
                low = Math.Min(low, factor);
                high = Math.Max(high, factor);
            }
            c.That(same, "every factor is the game's formula on the stream's next fraction");
            c.Equal(20000, rng.Counter, "one number each");
            c.That(low >= 0.95f && high <= 1.05f, "never outside 0.95 to 1.05");
            c.That(low < 0.9501f && high > 1.0499f, "and the whole range is used");
            c.Equal(48, ShopPrices.Round(47.5f), "a half goes to the even number: 47.5 -> 48");
            c.Equal(142, ShopPrices.Round(142.5f), "142.5 -> 142");
        });
    }

    // =====================================================================================
    // The two real shops
    // =====================================================================================

    private static RunState Before(RealShop real)
    {
        var state = new RunState(new RunSetup { Seed = real.Seed });
        var world = RunWorld.Begin(state);
        world.Streams.FastForward(RunStream.Rewards, real.RewardsBefore);
        state.CardRarityOffset = Pity(real.NonRareRewardCards);
        foreach (var relic in real.HeldBagRelics) RunRelics.TakeOutOfBags(state, relic);
        return state;
    }

    private static void RealShops(Check c)
    {
        foreach (var real in new[] { FirstRun, SecondRun })
        {
            c.Group($"the real shop of seed {real.Seed}, read straight down the game's draw order", () =>
            {
                var state = Before(real);
                var world = state.Part<RunWorld>();
                var reading = Read(state, everythingBuilt: true);

                var cards = reading.Cards.Select(card => card.Name).ToList();
                c.Sequence(real.CardsLeft, cards.Where(name => name != real.Bought), "the four character cards the record lists as left, in shelf order");
                c.That(cards.Contains(real.Bought), $"the card that was bought is the fifth ({string.Join(", ", cards)})");
                var bought = reading.Cards.Single(card => card.Name == real.Bought);
                c.Equal(real.BoughtFor, bought.Cost, $"{real.Bought} costs what the record says was spent on it");
                c.That(bought.OnSale, "it is the card on sale: its rarity's full price is at least twice that");
                c.Equal(1, reading.Cards.Count(card => card.OnSale), "one card is on sale");
                c.Sequence(real.Colorless, reading.Colorless.Select(card => card.Name), "the colorless cards: the Uncommon one, then the Rare one");
                c.Sequence(real.Relics, reading.Relics.Select(relic => relic.Name), "the three relics");
                c.Sequence(real.Potions, reading.Potions.Select(potion => potion.Name), "the three potions");
                c.Equal(real.RewardsBefore + 14, world.Streams.Counter(RunStream.Rewards), "14 numbers of rewards: 5 rarities, 7 upgrade rolls, 2 relic rarities");
                c.Equal(28, world.Streams.Counter(RunStream.Shops), "28 numbers of shops: the sale, 7 picks, 8 card prices, 3 relic prices, 6 for the potions, 3 potion prices");
                foreach (var relic in real.Relics)
                    c.That(!world.PlayerBag.Contains(relic) && !world.SharedBag.Contains(relic), $"{relic} has left both bags");
            });
        }

        c.Group("the two real shops: what else the records show", () =>
        {
            // Seed 1BUSLN0FY5: Tear Asunder is a Rare card: 150 times a factor of 0.95 to 1.05 is 142 to 158, and half of that 71 to 79.
            // 77 means the price before halving was 154 or 155. Seed 75F277YN2G: Anger is Common: 48 to 52, halved 24 to 26.
            var first = Read(Before(FirstRun), everythingBuilt: true);
            c.That(first.SalePriceBeforeHalving is 154 or 155, $"Tear Asunder was rolled at 154 or 155 ({first.SalePriceBeforeHalving})");
            c.Equal(0, first.Cards.FindIndex(card => card.OnSale), "seed 1BUSLN0FY5: the sale is on the first Attack");
            var second = Read(Before(SecondRun), everythingBuilt: true);
            c.That(second.SalePriceBeforeHalving is 48 or 49, $"Anger was rolled at 48 or 49 ({second.SalePriceBeforeHalving})");
            c.Equal(1, second.Cards.FindIndex(card => card.OnSale), "seed 75F277YN2G: the sale is on the second Attack");
            // The Power place can never hold a Common card: the Ironclad has none. Both real shops rolled Common for it and show an Uncommon Power.
            c.That(CardPools.Ironclad.All.All(card => card.Type != CardType.Power || card.Rarity != CardRarity.Common), "the Ironclad has no Common Power");
            c.Equal(CardRarity.Uncommon, CardPools.Ironclad.Find(first.Cards[4].Name)!.Rarity, "Vicious is an Uncommon Power");
        });
    }

    private static void RealShopsFromTheShop(Check c)
    {
        foreach (var real in new[] { FirstRun, SecondRun })
        {
            c.Group($"the real shop of seed {real.Seed} from the shop itself: the five character cards", () =>
            {
                // Every card of the Ironclad's pool is built, so this half of the shelf is the game's own whatever else is.
                var run = Start(real.Seed, At(real, enter: false));
                var shelf = EmptyShelf();
                shelf.StockCharacterCards(run);
                var cards = shelf.CharacterCards.Select(entry => entry.Card!.Def.ClassName).ToList();
                c.Sequence(real.CardsLeft, cards.Where(name => name != real.Bought), "the four cards the record lists as left, in shelf order");
                var bought = shelf.CharacterCards.Single(entry => entry.Card!.Def.ClassName == real.Bought);
                c.Equal(real.BoughtFor, bought.BaseCost, $"{real.Bought} at the price the record says was paid");
                c.That(bought.IsOnSale && shelf.CharacterCards.Count(entry => entry.IsOnSale) == 1, "and it is the one card on sale");
                c.Sequence(ShopInventory.CharacterCardTypes, shelf.CharacterCards.Select(entry => entry.Card!.Type), "Attack, Attack, Skill, Skill, Power");
                c.That(shelf.CharacterCards.All(entry => !entry.Card!.IsUpgraded), "none comes upgraded");
                c.Equal(real.RewardsBefore + 10, Counter(run, RunStream.Rewards), "10 numbers of rewards: a rarity and an upgrade roll per card");
                c.Equal(12, Counter(run, RunStream.Shops), "12 numbers of shops: the sale, 5 picks, 6 prices");
                c.Equal(Pity(real.NonRareRewardCards), run.State.CardRarityOffset, "the pity offset is as it was");
            });

            c.Group($"the real shop of seed {real.Seed} from the shop itself, with nothing drawn again", () =>
            {
                // The game's shelf in the order it is made. Under the game's own streams the shop is the game's until something on
                // it is not built; that thing, and nothing before it, must be what ends the run.
                var reading = Read(Before(real), everythingBuilt: true);
                var missing = reading.Cards.Concat(reading.Colorless).Select(card => card.Name).Where(name => !CardPools.IsBuilt(name))
                    .Concat(reading.Relics.Select(relic => relic.Name).Where(name => !RunRelics.IsBuilt(name)))
                    .Concat(reading.Potions.Select(potion => potion.Name).Where(name => !Content.Registry.Has(ContentKind.Potion, name)))
                    .FirstOrDefault();

                var run = Start(real.Seed, At(real, enter: false, strict: true));
                Do(run, "enter");
                if (missing is null)
                {
                    c.That(ShopShown(run), "everything on the game's shelf is built: the shop is up");
                    var shelf = Shop(run).Inventory;
                    c.Sequence(reading.Cards.Select(card => (card.Name, card.Cost, card.OnSale)), shelf.CharacterCards.Select(entry => (entry.Card!.Def.ClassName, entry.BaseCost, entry.IsOnSale)), "the character cards");
                    c.Sequence(reading.Colorless.Select(card => (card.Name, card.Cost)), shelf.ColorlessCards.Select(entry => (entry.Card!.Def.ClassName, entry.BaseCost)), "the colorless cards");
                    c.Sequence(reading.Relics.Select(relic => (relic.Name, relic.Cost)), shelf.Relics.Select(entry => (entry.Relic!.ClassName, entry.BaseCost)), "the relics");
                    c.Sequence(reading.Potions.Select(potion => (potion.Name, potion.Cost)), shelf.Potions.Select(entry => (entry.Potion!.ClassName, entry.BaseCost)), "the potions");
                }
                else
                {
                    c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, $"{missing} is not built: the run ends as unrepresentable instead of showing another shop");
                    // While the arena has no colorless card of that rarity at all, the pick is not even drawn: the reason then says so.
                    var colorless = CardPools.Colorless.Find(missing);
                    var noneToLandOn = colorless is not null && !CardPools.Colorless.All.Any(card => card.Rarity == colorless.Rarity && CardPools.IsBuilt(card.ClassName));
                    c.That(run.ResultReason is { } reason && (noneToLandOn ? reason.Contains("can only land on cards the arena leaves out", StringComparison.Ordinal) : reason.Contains(missing, StringComparison.Ordinal)),
                        $"and the reason {(noneToLandOn ? "says that no card of its rarity is built" : "names it")} ({run.ResultReason})");
                    c.Equal(real.RewardsBefore, Counter(run, RunStream.Rewards), "the run stands as before the shop: no number of rewards is gone");
                    c.Equal(0, Counter(run, RunStream.Shops), "and none of shops");
                    c.Equal("enter", run.Actions[^1], "the action that entered the shop stays in the save");
                    c.Equal(run.Dump(), Arena.Run.Replay(new RunSetup { Seed = real.Seed }, At(real, enter: false, strict: true), run.Actions).Dump(), "which replays to the same end");
                }
            });
        }
    }

    // =====================================================================================
    // Every shop the code makes, against the second reading
    // =====================================================================================

    private static void AgainstTheSecondReading(Check c)
    {
        c.Group("300 shops against the second reading of the game's draw order", () =>
        {
            float[] offsets = [-0.05f, -0.02f, 0.01f, 0.1f, 0.4f];
            RelicRarity[] emptied = [RelicRarity.None, RelicRarity.None, RelicRarity.None, RelicRarity.None, RelicRarity.Common, RelicRarity.Shop];
            RelicRarity[] onlyUnbuilt = [RelicRarity.None, RelicRarity.None, RelicRarity.None, RelicRarity.Common, RelicRarity.None, RelicRarity.None];
            var shown = 0;
            var rerolls = 0;
            var stopped = 0;
            var pure = 0;
            var differences = new List<string>();
            var onSale = new int[5];
            var seenCost = new Dictionary<string, (int Low, int High)>(StringComparer.Ordinal);
            void Seen(string kind, int cost)
            {
                var (low, high) = seenCost.TryGetValue(kind, out var range) ? range : (int.MaxValue, int.MinValue);
                seenCost[kind] = (Math.Min(low, cost), Math.Max(high, cost));
            }

            for (var i = 0; i < 300; i++)
            {
                var seed = string.Create(CultureInfo.InvariantCulture, $"SH0P{i:D4}");
                var director = new Merchant
                {
                    RewardsAt = i * 7 % 61, ShopsAt = i % 3 == 0 ? 0 : i * 5 % 43, Offset = offsets[i % 5], Emptied = emptied[i % 6], OnlyUnbuilt = onlyUnbuilt[i % 6],
                };
                var run = Start(seed, director);
                var twin = Start(seed, new Merchant
                {
                    RewardsAt = director.RewardsAt, ShopsAt = director.ShopsAt, Offset = director.Offset, Emptied = director.Emptied, OnlyUnbuilt = director.OnlyUnbuilt, Enter = false,
                });
                var reading = Read(twin.State, everythingBuilt: false);
                void Differs(string what) => differences.Add($"{seed}: {what}");

                if (reading.Stopped is not null)
                {
                    stopped++;
                    if (run.Result != RunResult.Unrepresentable) Differs($"the reading cannot fill {reading.Stopped}, the shop came out all the same");
                    continue;
                }
                if (!ShopShown(run))
                {
                    Differs($"the reading fills the shelf, the run ended as {run.Result}: {run.ResultReason}");
                    continue;
                }
                shown++;
                rerolls += reading.Rerolls;

                var shelf = Shop(run).Inventory;
                string Line(IEnumerable<(string Name, int Cost, bool OnSale)> entries) => string.Join(" ", entries.Select(e => $"{e.Name}:{e.Cost}{(e.OnSale ? "*" : "")}"));
                var expected = Line(reading.All.Select(s => (s.Name, s.Cost, s.OnSale)));
                var actual = Line(shelf.CardEntries.Select(e => (e.Card!.Def.ClassName, e.BaseCost, e.IsOnSale))
                    .Concat(shelf.Relics.Select(e => (e.Relic!.ClassName, e.BaseCost, false)))
                    .Concat(shelf.Potions.Select(e => (e.Potion!.ClassName, e.BaseCost, false))));
                if (expected != actual) Differs($"the shelf: expected {expected}, got {actual}");
                foreach (var stream in new[] { RunStream.Rewards, RunStream.Shops })
                    if (Counter(run, stream) != Counter(twin, stream)) Differs($"{stream}: expected {Counter(twin, stream)} numbers, got {Counter(run, stream)}");
                if (World(run).Dump()!["player_bag"]!.ToJsonString() != World(twin).Dump()!["player_bag"]!.ToJsonString()) Differs("the player's bag");
                if (World(run).Dump()!["shared_bag"]!.ToJsonString() != World(twin).Dump()!["shared_bag"]!.ToJsonString()) Differs("the shared bag");
                var logged = run.State.FindPart<LeftOutLog>()?.Entries.Select(entry => entry.ClassName).ToList() ?? [];
                if (!logged.SequenceEqual(reading.PassedOver)) Differs($"passed over: expected [{string.Join(", ", reading.PassedOver)}], logged [{string.Join(", ", logged)}]");

                // What holds for every shop, worked out from the game's code and not from either reading.
                if (shelf.CharacterCards.Count != 5 || shelf.ColorlessCards.Count != 2 || shelf.Relics.Count != 3 || shelf.Potions.Count != 3 || shelf.Removal is null)
                    Differs("a shop has 5 + 2 cards, 3 relics, 3 potions and the removal");
                if (!shelf.CharacterCards.Select(e => e.Card!.Type).SequenceEqual(ShopInventory.CharacterCardTypes)) Differs("the types of the five places");
                if (shelf.ColorlessCards[0].Card!.Def.Rarity != CardRarity.Uncommon || shelf.ColorlessCards[1].Card!.Def.Rarity != CardRarity.Rare) Differs("the colorless rarities");
                if (shelf.ColorlessCards.Any(e => !ReferenceEquals(CardPools.Of(e.Card!.Def.ClassName), CardPools.Colorless))) Differs("a colorless place holds a card of another pool");
                if (shelf.CharacterCards.Any(e => !ReferenceEquals(CardPools.Of(e.Card!.Def.ClassName), CardPools.Ironclad) || e.Card!.Def.Rarity == CardRarity.Basic)) Differs("a character place holds a Basic card or one of another pool");
                if (shelf.CardEntries.Select(e => e.Card!.Def.ClassName).Distinct().Count() != 7) Differs("two places hold the same card");
                if (shelf.CardEntries.Count(e => e.IsOnSale) != 1 || shelf.ColorlessCards.Any(e => e.IsOnSale)) Differs("exactly one character card is on sale");
                if (shelf.Potions.Select(e => e.Potion!.ClassName).Distinct().Count() != 3) Differs("two places hold the same potion");
                if (shelf.Relics.Select(e => e.Relic!.ClassName).Distinct().Count() != 3) Differs("two places hold the same relic");
                if (shelf.Relics.Any(e => NeverSold.Contains(e.Relic!.ClassName))) Differs("a relic a shop never sells is on the shelf");
                if (shelf.Relics.Any(e => World(run).PlayerBag.Contains(e.Relic!.ClassName) || World(run).SharedBag.Contains(e.Relic!.ClassName))) Differs("a shelved relic is still in a bag");
                if (director.Emptied != RelicRarity.Shop && GameLists.BagRelic(shelf.Relics[2].Relic!.ClassName).Rarity != RelicRarity.Shop) Differs("the third relic is not of Shop rarity");
                if (shelf.Removal?.BaseCost != 75) Differs("the removal costs 75");
                if (run.State.CardRarityOffset != director.Offset) Differs("the pity offset moved");
                if (reading.PassedOver.Count == 0)
                {
                    pure++;
                    if (Counter(run, RunStream.Rewards) != director.RewardsAt + 14 || Counter(run, RunStream.Shops) != director.ShopsAt + 28)
                        Differs("a shop with nothing drawn again takes 14 numbers of rewards and 28 of shops");
                }

                onSale[shelf.CharacterCards.ToList().FindIndex(e => e.IsOnSale)]++;
                foreach (var entry in shelf.CardEntries)
                    Seen($"{(entry.Rarity is null ? "card" : "colorless")} {entry.Card!.Def.Rarity}{(entry.IsOnSale ? " on sale" : "")}", entry.BaseCost);
                foreach (var entry in shelf.Relics) Seen($"relic {GameLists.BagRelic(entry.Relic!.ClassName).Rarity}", entry.BaseCost);
                foreach (var entry in shelf.Potions) Seen($"potion {entry.Potion!.Rarity}", entry.BaseCost);
            }

            c.Equal("", string.Join(" | ", differences.Take(5)), $"no shop differs from the second reading ({differences.Count} differences)");
            c.Equal(300, shown + stopped, $"every seed is accounted for ({shown} shops shown, {stopped} that the arena cannot fill, {pure} with nothing drawn again)");

            // The price of each kind lies in the range its table value and factor give. Worked out in 32-bit floats: a table value times
            // 0.95f or 1.05f (0.85f or 1.15f for a relic), rounded half to even; a card on sale is half of that, rounded down.
            var ranges = new Dictionary<string, (int Low, int High)>(StringComparer.Ordinal)
            {
                ["card Common"] = (48, 52), ["card Uncommon"] = (71, 79), ["card Rare"] = (142, 158),
                ["card Common on sale"] = (24, 26), ["card Uncommon on sale"] = (35, 39), ["card Rare on sale"] = (71, 79),
                ["colorless Uncommon"] = (82, 90), ["colorless Rare"] = (163, 181),
                ["relic Common"] = (149, 201), ["relic Uncommon"] = (191, 259), ["relic Rare"] = (234, 316), ["relic Shop"] = (170, 230),
                ["potion Common"] = (48, 52), ["potion Uncommon"] = (71, 79), ["potion Rare"] = (95, 105),
            };
            foreach (var (kind, (low, high)) in seenCost.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                c.That(ranges.TryGetValue(kind, out var range) && low >= range.Low && high <= range.High,
                    $"{kind}: every price from {low} to {high} lies in its range{(ranges.TryGetValue(kind, out var r) ? $" {r.Low} to {r.High}" : " (no range is known for it)")}");
            }
            if (shown >= 100)
            {
                c.That(onSale.All(count => count > 0), $"each of the five places is the one on sale in some shop ({string.Join(", ", onSale)})");
                c.That(rerolls > 0, "and in some shop a relic's rarity was rolled again, because its list held only relics the arena has not built");
                c.That(seenCost["card Common"] == (48, 52), "the Common cards use their whole range, 48 to 52");
                c.That(seenCost["potion Common"] == (48, 52), "and so do the Common potions");
            }
        });
    }

    private static void AlwaysFilled(Check c)
    {
        c.Group("the character cards and the potions of 150 shops against the second reading: the places the arena can always fill", () =>
        {
            // The Ironclad's whole pool is built, and all but two potions: these two kinds never wait for other content.
            float[] offsets = [-0.05f, -0.03f, 0f, 0.2f, 0.4f];
            var differences = new List<string>();
            var passedOver = 0;
            for (var i = 0; i < 150; i++)
            {
                var seed = string.Create(CultureInfo.InvariantCulture, $"HALF{i:D4}");
                Merchant Director() => new() { RewardsAt = i * 3 % 47, ShopsAt = i * 11 % 37, Offset = offsets[i % 5], Enter = false };
                var run = Start(seed, Director());
                var shelf = EmptyShelf();
                shelf.StockCharacterCards(run);
                shelf.StockPotions(run);
                var twin = Start(seed, Director());
                var reading = Read(twin.State, everythingBuilt: false, colorlessAndRelics: false);

                var expected = string.Join(" ", reading.All.Select(s => $"{s.Name}:{s.Cost}{(s.OnSale ? "*" : "")}"));
                var actual = string.Join(" ", shelf.CharacterCards.Select(e => $"{e.Card!.Def.ClassName}:{e.BaseCost}{(e.IsOnSale ? "*" : "")}")
                    .Concat(shelf.Potions.Select(e => $"{e.Potion!.ClassName}:{e.BaseCost}")));
                if (expected != actual) differences.Add($"{seed}: expected {expected}, got {actual}");
                foreach (var stream in new[] { RunStream.Rewards, RunStream.Shops })
                    if (Counter(run, stream) != Counter(twin, stream)) differences.Add($"{seed}: {stream}: expected {Counter(twin, stream)} numbers, got {Counter(run, stream)}");
                var logged = run.State.FindPart<LeftOutLog>()?.Entries.Select(entry => entry.ClassName).ToList() ?? [];
                if (!logged.SequenceEqual(reading.PassedOver)) differences.Add($"{seed}: passed over: expected [{string.Join(", ", reading.PassedOver)}], logged [{string.Join(", ", logged)}]");
                passedOver += reading.PassedOver.Count;
                // Ten numbers of rewards. Twelve of shops for the cards, nine for the potions, two more for every potion that was rolled again.
                if (Counter(run, RunStream.Rewards) != Director().RewardsAt + 10 || Counter(run, RunStream.Shops) != Director().ShopsAt + 21 + 2 * reading.PassedOver.Count)
                    differences.Add($"{seed}: the numbers taken");
                if (shelf.Potions.Select(e => e.Potion!.ClassName).Distinct().Count() != 3) differences.Add($"{seed}: two places hold the same potion");
            }
            c.Equal("", string.Join(" | ", differences.Take(5)), $"no shelf differs from the second reading ({differences.Count} differences)");
            c.That(passedOver > 0 || Scenarios.PotionPool.All(potion => Content.Registry.Has(ContentKind.Potion, potion.ClassName)),
                $"while some potion is not built, a roll lands on one in 150 shops and is drawn again ({passedOver} times)");
        });
    }

    private static void BagWalk(Check c)
    {
        c.Group("the relic places against a walk of the bag from its back", () =>
        {
            RelicRarity[] rolled = [RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare];
            var differences = new List<string>();
            var fellThrough = 0;
            var passedNeverSold = 0;
            for (var i = 0; i < 90; i++)
            {
                var seed = string.Create(CultureInfo.InvariantCulture, $"BAG{i:D4}");
                RunState Make()
                {
                    var state = new RunState(new RunSetup { Seed = seed });
                    var world = RunWorld.Begin(state);
                    if (i % 3 == 2)
                        foreach (var relic in world.PlayerBag.Relics(RelicRarity.Shop).ToList()) RunRelics.TakeOutOfBags(state, relic);
                    return state;
                }
                var walked = Make();
                var pulled = Make();
                RelicRarity[] places = [rolled[i % 3], rolled[i / 3 % 3], RelicRarity.Shop];
                foreach (var rarity in places)
                {
                    var back = walked.Part<RunWorld>().PlayerBag.Relics(rarity);
                    if (back.Count > 0 && NeverSold.Contains(back[^1])) passedNeverSold++;
                    var mine = BackOfTheBag(walked.Part<RunWorld>(), rarity);
                    var pull = RunRelics.PullFromBack(pulled, rarity, isBuilt: _ => true);
                    if (mine != pull.Relic) differences.Add($"{seed} {rarity}: walked {mine}, pulled {pull.Relic}");
                    if (mine is not null && GameLists.BagRelic(mine).Rarity != rarity) fellThrough++;
                }
                if (walked.Part<RunWorld>().Dump()!["player_bag"]!.ToJsonString() != pulled.Part<RunWorld>().Dump()!["player_bag"]!.ToJsonString()) differences.Add($"{seed}: the player's bag afterwards");
                if (walked.Part<RunWorld>().Dump()!["shared_bag"]!.ToJsonString() != pulled.Part<RunWorld>().Dump()!["shared_bag"]!.ToJsonString()) differences.Add($"{seed}: the shared bag afterwards");
            }
            c.Equal("", string.Join(" | ", differences.Take(5)), "the relic framework's pull from the back gives what a plain walk gives");
            c.Equal(30, fellThrough, "with the Shop list empty the third place falls through to the back of the Common list (30 seeds)");
            c.That(passedNeverSold > 0, $"and a relic a shop never sells stood at the back of a list and was passed over ({passedNeverSold} times)");
        });
    }

    // =====================================================================================
    // Keyed streams
    // =====================================================================================

    private static void KeyedStreams(Check c)
    {
        c.Group("keyed streams: every roll of a shop names its place on the shelf", () =>
        {
            const string seed = "KEYEDSH0P";
            // Whether the colorless places and the relic places can be filled depends on what the arena has built so far.
            var whole = ShopShown(Start(seed, new Merchant { Mode = StreamMode.Keyed }));

            ShopInventory Make(Arena.Run run)
            {
                if (whole) return ShopInventory.CreateForNormalMerchant(run);
                var shelf = EmptyShelf();
                shelf.StockCharacterCards(run);
                shelf.StockPotions(run);
                return shelf;
            }
            string Line(ShopInventory shelf) => string.Join(" ", shelf.CardEntries.Select(e => $"{e.Card!.Def.ClassName}:{e.BaseCost}{(e.IsOnSale ? "*" : "")}")
                .Concat(shelf.Relics.Select(e => $"{e.Relic!.ClassName}:{e.BaseCost}")).Concat(shelf.Potions.Select(e => $"{e.Potion!.ClassName}:{e.BaseCost}")));

            var run = Start(seed, new Merchant { Mode = StreamMode.Keyed, Enter = false, RewardsAt = 5 });
            var made = Make(run);
            var sale = made.CharacterCards.ToList().FindIndex(entry => entry.IsOnSale);

            // The streams that were asked for at this place, each with how often. A card asks for its slot's stream twice (the rarity and
            // the upgrade roll come from one, the pick from the other); a price once, the card on sale a second time; the two rolled
            // relic rarities once each; the three potions together once.
            var expected = new SortedDictionary<string, int>(StringComparer.Ordinal) { ["0/4,6/0|shop_sale/0/0"] = 1, ["0/4,6/0|shop_potion/0/0"] = 1 };
            for (var slot = 0; slot < (whole ? 7 : 5); slot++)
            {
                expected[string.Create(CultureInfo.InvariantCulture, $"0/4,6/0|shop_card/{slot}/0")] = 2;
                expected[string.Create(CultureInfo.InvariantCulture, $"0/4,6/0|shop_price/{slot}/0")] = slot == sale ? 2 : 1;
            }
            if (whole)
            {
                expected["0/4,6/0|shop_relic_rarity/0/0"] = 1;
                expected["0/4,6/0|shop_relic_rarity/1/0"] = 1;
                foreach (var slot in new[] { 7, 8, 9 }) expected[string.Create(CultureInfo.InvariantCulture, $"0/4,6/0|shop_price/{slot}/0")] = 1;
            }
            foreach (var slot in new[] { 10, 11, 12 }) expected[string.Create(CultureInfo.InvariantCulture, $"0/4,6/0|shop_price/{slot}/0")] = 1;
            var asked = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var (key, times) in World(run).Streams.Dump()["asked"]!.AsObject()) asked[key] = times!.GetValue<int>();
            c.Equal(string.Join(" ", expected.Select(pair => $"{pair.Key}={pair.Value}")), string.Join(" ", asked.Select(pair => $"{pair.Key}={pair.Value}")),
                $"the streams asked for ({(whole ? "a whole shop" : "the character cards and the potions: the arena cannot fill the other places yet")})");
            c.Equal((5, 0), (Counter(run, RunStream.Rewards), Counter(run, RunStream.Shops)), "the long streams did not move");

            // Same node, another history: other counters on the long streams. The shelf is the same.
            var other = Start(seed, new Merchant { Mode = StreamMode.Keyed, Enter = false, RewardsAt = 37, ShopsAt = 11 });
            c.Equal(Line(made), Line(Make(other)), "two players on the same node meet the same shelf at the same prices, whatever they rolled before");
            // Another node: another shelf.
            var elsewhere = Start(seed, new Merchant { Mode = StreamMode.Keyed, Enter = false, RewardsAt = 5, Row = 7, Col = 2 });
            c.That(Line(made) != Line(Make(elsewhere)), "another node has another shelf");
            // The game's own streams give yet another: keyed mode is a house rule and not the game's run for the seed.
            var games = Start(seed, new Merchant { Enter = false, RewardsAt = 5 });
            c.That(Line(made) != Line(Make(games)), "and the game's own streams give the game's shelf, not this one");
        });
    }

    // =====================================================================================
    // Entering the room
    // =====================================================================================

    private static void Entering(Check c)
    {
        c.Group("the room is announced once, when its shelf stands and its screen is up", () =>
        {
            // MerchantRoom.EnterInternal: the inventory is made, the room is shown, then "after room entered" is sent.
            var run = new Arena.Run(new RunSetup { ExtraRelics = [() => new Clerk()] }, new Stall { Announce = true });
            c.Equal("entered Shop with 14 things on the shelf;", run.State.Relics.OfType<Clerk>().Single().Heard, "the listener is told of a Shop room and finds 5 + 2 cards, 3 relics, 3 potions and the removal on its shelf");
            c.Equal(RoomKind.Shop, run.Room, "the run stands in a Shop room");
            c.That(run.Screens.Count == 1 && run.Top is ShopScreen { IsOpen: false }, "the shop's screen is up, its inventory closed");
        });

        c.Group("entering a shop: the stock is made, the screen is up with the inventory closed, the listeners hear a Shop room", () =>
        {
            var setup = new RunSetup { Seed = "ENTER1", ExtraRelics = [() => new Clerk()] };
            var run = new Arena.Run(setup, new Merchant());
            if (!ShopShown(run))
            {
                c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "the arena cannot fill a shelf yet: the run ends as unrepresentable, by itself");
                c.That(run.ResultReason is { Length: > 0 } reason && (reason.Contains("a shop card", StringComparison.Ordinal) || reason.Contains("a shop relic", StringComparison.Ordinal) || reason.Contains("potion", StringComparison.Ordinal)),
                    $"and the reason says what could not be made ({run.ResultReason})");
                return;
            }

            var s = run.Snapshot();
            c.Equal("shop", s.Screen, "the shop's screen");
            c.That(s.AwaitingDecision, "it is a decision");
            c.Equal(false, s.State["inventory_open"]!.GetValue<bool>(), "a visit starts with the inventory closed");
            c.Equal("run,boss,gold,inventory_open,player,potions,stock", Keys(s.State), "the state's keys, in the reader's order");
            c.Equal("cards,colorless,relics,potions,removal", Keys(s.State["stock"]), "the stock is listed while the inventory is closed");
            c.Equal("0,1,2,3,4", Numbers(s.State["stock"]!["cards"]), "five character cards");
            c.Equal("0,1", Numbers(s.State["stock"]!["colorless"]), "two colorless cards");
            c.Equal("0,1,2", Numbers(s.State["stock"]!["relics"]), "three relics");
            c.Equal("0,1,2", Numbers(s.State["stock"]!["potions"]), "three potions");
            c.Equal("""{"price":75,"affordable":true}""", Text(s.State["stock"]!["removal"]), "the removal at 75 gold, which 99 gold pays for");
            c.Equal(RoomKind.Shop, run.Room, "the run stands in a Shop room");
            c.Equal("entered Shop with 14 things on the shelf;", run.State.Relics.OfType<Clerk>().Single().Heard, "the room was announced once, after the stock was made");
            c.That(s.Actions.Any(action => action.Id == "proceed") && s.Actions.All(action => !action.Id.StartsWith("buy:", StringComparison.Ordinal)), "leaving is offered, buying is not");

            Do(run, "proceed");
            c.Equal(1, ((Merchant)run.Director).Left, "proceed tells the director that the room is finished");
            c.That(run.Screens.Count == 1 && run.Top is Desk, "and the shop's screen is gone");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Merchant(), run.Actions).Dump(), "the visit replays from its save");
        });
    }

    // =====================================================================================
    // The screen
    // =====================================================================================

    private static void Screen(Check c)
    {
        c.Group("the shop screen with the inventory closed: keys, order, labels", () =>
        {
            var run = new Arena.Run(new RunSetup { Gold = 200, Potions = ["BloodPotion"] }, new Stall());
            var s = run.Snapshot();
            c.Equal("shop", s.Screen, "the screen");
            c.That(s.AwaitingDecision && s.Waiting is null, "a decision");
            c.Equal("run,gold,inventory_open,player,potions,stock", Keys(s.State), "the keys in order; a run without a world names no boss");
            c.Equal(200, s.State["gold"]!.GetValue<int>(), "the gold is a key of its own");
            c.Equal(false, s.State["inventory_open"]!.GetValue<bool>(), "closed");
            c.Equal("""{"character":"The Ironclad","hp":80,"max_hp":80,"relics":["Burning Blood"],"deck_size":10,"deck":{"Bash":1,"Defend":4,"Strike":5}}""",
                Text(s.State["player"]), "the player block: no gold and no potions in it");
            c.Equal("slot,name,text,target,usable", Keys(s.State["potions"]![0]), "the detailed potion slots");
            c.That(s.State["potions"]![0]!["usable"]!.GetValue<bool>() && s.State["potions"]![1] is null && s.State["potions"]!.AsArray().Count == 3, "Blood Potion can be used here; two slots are empty");

            var stock = s.State["stock"]!;
            c.Equal("cards,colorless,relics,potions,removal", Keys(stock), "the stock's keys");
            c.Equal("n,name,cost,type,text,rarity,price,affordable", Keys(stock["cards"]![0]), "a card entry");
            c.Equal("n,name,cost,type,text,rarity,price,on_sale,affordable", Keys(stock["cards"]![1]), "the card on sale has on_sale before affordable");
            c.Equal(true, stock["cards"]![1]!["on_sale"]!.GetValue<bool>(), "on_sale is true");
            c.Equal("Headbutt, Twin Strike, Shrug It Off, Flame Barrier, Inflame", Names(stock["cards"]), "the character cards");
            c.Equal("52,24,48,78,73", Prices(stock["cards"]), "their prices");
            c.Equal("Common,Common,Common,Uncommon,Uncommon", string.Join(",", stock["cards"]!.AsArray().Select(card => card!["rarity"]!.GetValue<string>())), "with their rarities");
            c.Equal("n,name,cost,type,text,rarity,price,affordable", Keys(stock["colorless"]![0]), "a colorless entry: the same keys, never on_sale");
            c.Equal("Check Trick, Check Marvel", Names(stock["colorless"]), "the colorless cards");
            c.Equal("""{"n":0,"name":"Check Charm","text":"At pickup: gain 7 Max HP.","price":147,"affordable":true}""", Text(stock["relics"]![0]), "a relic entry");
            c.Equal("""{"n":1,"name":"Check Idol","text":"Does nothing.","price":236,"affordable":false}""", Text(stock["relics"]![1]), "a relic the gold does not reach");
            c.Equal("n,name,text,target,price,affordable", Keys(stock["potions"]![0]), "a potion entry");
            c.Equal(("Fire Potion", "AnyEnemy", 52), (stock["potions"]![0]!["name"]!.GetValue<string>(), stock["potions"]![0]!["target"]!.GetValue<string>(), stock["potions"]![0]!["price"]!.GetValue<int>()), "its name, target and price");
            c.Equal("""{"price":75,"affordable":true}""", Text(stock["removal"]), "the removal: a price and whether the gold is there, nothing else");
            c.That(s.State["potion_slots_full"] is null && s.State["last_purchase_failed"] is null, "no potion_slots_full and no last_purchase_failed");

            c.Equal("open=Open the shop inventory to buy | proceed=Leave the shop | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion", Labels(s),
                "closed: open, proceed, then the potion ids");
        });

        c.Group("the shop screen with the inventory open: the buys, then close, then the potion ids", () =>
        {
            var run = Open(200, potions: ["BloodPotion"]);
            var s = run.Snapshot();
            c.Equal(true, s.State["inventory_open"]!.GetValue<bool>(), "open");
            c.Equal(
                "buy:card0=Buy Headbutt for 52 gold | buy:card1=Buy Twin Strike for 24 gold (on sale) | buy:card2=Buy Shrug It Off for 48 gold | "
                + "buy:card3=Buy Flame Barrier for 78 gold | buy:card4=Buy Inflame for 73 gold | buy:colorless0=Buy colorless Check Trick for 85 gold | "
                + "buy:colorless1=Buy colorless Check Marvel for 168 gold | buy:relic0=Buy relic Check Charm for 147 gold | "
                + "buy:potion0=Buy potion Fire Potion for 52 gold | buy:potion1=Buy potion Swift Potion for 49 gold | buy:potion2=Buy potion Fruit Juice for 104 gold | "
                + "buy:remove=Pay 75 gold to remove a card from your deck | close=Close the shop inventory | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion",
                Labels(s), "every entry 200 gold pays for, in the reader's order; the two relics above 200 are listed and not offered");
            c.That(s.Actions.All(action => action.Id != "proceed"), "no proceed while the inventory is open");
            c.Equal("cards,colorless,relics,potions,removal", Keys(s.State["stock"]), "the same stock as when closed");
        });

        c.Group("the boss is named right after run when the run has a world", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = "1BUSLN0FY5" }, new Stall { World = true });
            var s = run.Snapshot();
            c.Equal("run,boss,gold,inventory_open,player,potions,stock", Keys(s.State), "the keys in order");
            c.Equal("Vantom", s.State["boss"]!.GetValue<string>(), "the act's boss on seed 1BUSLN0FY5");
            c.Equal("""{"act":1,"floor":5,"ascension":0}""", Text(s.State["run"]), "the run block");
        });
    }

    // =====================================================================================
    // Buying
    // =====================================================================================

    private static void Buying(Check c)
    {
        c.Group("buying a card and a relic: gold, deck, relics, and numbers that never shift", () =>
        {
            var run = Open(200, potions: ["BloodPotion"]);

            Do(run, "buy:card1");
            c.Equal(176, run.Gold, "200 - 24 for the card on sale");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash TwinStrike", DeckNames(run), "Twin Strike is the deck's last card");
            var s = run.Snapshot();
            c.Equal("0,2,3,4", Numbers(s.State["stock"]!["cards"]), "the bought card leaves a gap: the others keep their numbers");
            c.That(s.Actions.All(action => action.Id != "buy:card1") && s.Actions.Any(action => action.Id == "buy:card2"), "its buy is gone, the others stay");
            c.Equal(11, s.State["player"]!["deck_size"]!.GetValue<int>(), "the player block shows eleven cards");
            c.Equal(true, s.State["inventory_open"]!.GetValue<bool>(), "the inventory stays open");
            c.Equal("'buy:card1' is not in stock.", Refused(c, run, "buy:card1"), "a sold-out entry");

            Do(run, "buy:relic0");
            c.Equal(29, run.Gold, "176 - 147");
            c.Sequence(["Burning Blood", "Check Charm"], run.State.Relics.Select(relic => relic.Name), "the relic is the player's");
            c.Equal((87, 87), (run.Hp, run.MaxHp), "its pickup effect ran: 7 Max HP, healed by as much");
            s = run.Snapshot();
            c.Equal("1,2", Numbers(s.State["stock"]!["relics"]), "the relics left keep their numbers");
            c.Equal("close potion:0 discard_potion:0", Ids(s), "29 gold pays for nothing on the shelf: only close is left, with the potion ids");
            c.That(s.State["stock"]!["cards"]!.AsArray().All(card => !card!["affordable"]!.GetValue<bool>()), "everything is listed as not affordable");

            c.Equal("Not enough gold for Headbutt (52).", Refused(c, run, "buy:card0"), "a card the gold does not reach");
            c.Equal("Not enough gold for Check Idol (236).", Refused(c, run, "buy:relic1"), "a relic the gold does not reach");
            c.Equal("Not enough gold for a card removal (75).", Refused(c, run, "buy:remove"), "the removal the gold does not reach");
            c.Equal("Close the shop inventory first.", Refused(c, run, "proceed"), "proceed with the inventory open");
            c.Equal("The shop inventory is already open.", Refused(c, run, "open"), "open twice");
            c.Equal("'buy:card9' is not in stock.", Refused(c, run, "buy:card9"), "an entry that does not exist");
            c.Equal("'buy:' is not in stock.", Refused(c, run, "buy:"), "a buy id that names nothing");
            c.Equal("Unknown shop action 'haggle'.", Refused(c, run, "haggle"), "an id that is not the shop's");
            c.Equal("No tip is showing.", Refused(c, run, "dismiss"), "dismiss: no tutorial tip is ever up");
            c.Equal("'potion:2' is not available now.", Refused(c, run, "potion:2"), "a potion id that is not offered");

            Do(run, "close");
            s = run.Snapshot();
            c.Equal("proceed potion:0 discard_potion:0", Ids(s), "closed with nothing to pay for: open is not offered");
            c.Equal("'open' is no longer available on the shop screen.", Refused(c, run, "open"), "open when it is not offered: the bridge's refusal");
            c.Equal("The shop inventory is not open.", Refused(c, run, "close"), "close twice");
            c.Equal("Open the shop inventory first.", Refused(c, run, "buy:card0"), "a buy with the inventory closed");

            Do(run, "proceed");
            c.Equal(1, ((Stall)run.Director).Left, "proceed leaves the room");
            c.That(run.Screens.Count == 1 && run.Top is Desk, "the shop's screen is gone");
            c.Sequence(["open", "buy:card1", "buy:relic0", "close", "proceed"], run.Actions, "the save holds the accepted ids");
        });

        c.Group("the gold is enough when it equals the price", () =>
        {
            // MerchantEntry.EnoughGold: the price is at most the gold.
            var run = Open(52);
            var cards = Stock(run)["cards"]!.AsArray();
            c.Equal("True,True,True,False,False", string.Join(",", cards.Select(card => card!["affordable"]!.GetValue<bool>())), "52 gold: Headbutt at 52 is affordable, Flame Barrier at 78 and Inflame at 73 are not");
            Do(run, "buy:card0");
            c.Equal(0, run.Gold, "and buying it leaves no gold");
            c.Equal("close", Ids(run.Snapshot()), "nothing is left to buy");
            Do(run, "close");
            c.Equal("proceed", Ids(run.Snapshot()), "and nothing to open the inventory for");
        });

        c.Group("the purchase itself turns down an empty place and a price above the gold", () =>
        {
            // MerchantEntry.OnTryPurchaseWrapper, asked directly: the screen never sends it either case.
            var run = Open(60);
            var before = run.Dump();
            c.That(!Shop(run).Inventory.TryPurchase(run, Shop(run).Inventory.CharacterCards[3]), "Flame Barrier at 78 with 60 gold is not bought");
            c.That(!Shop(run).Inventory.TryPurchase(run, Shop(run).Inventory.Removal!), "nor the removal at 75");
            c.Equal(before, run.Dump(), "and nothing changed");
            Do(run, "buy:card1");
            before = run.Dump();
            c.That(!Shop(run).Inventory.TryPurchase(run, Shop(run).Inventory.CharacterCards[1]), "the place Twin Strike stood on is empty: nothing is bought there");
            c.Equal(before, run.Dump(), "and nothing changed");
            c.That(!Shop(run).Inventory.TryPurchase(run, Shop(run).Inventory.Potions[1]), "Swift Potion at 49 with 36 gold left is not bought either");
            c.Equal(before, run.Dump(), "and again nothing changed");
        });

        c.Group("a relic whose pickup opens a rewards list: the list lies over the shop, and the shop is back when it is done", () =>
        {
            var run = Open(400, new Stall { World = true, Kind = Shelf.HamperFirst }, relics: [() => new Clerk()]);
            Do(run, "buy:relic0");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "the relic's rewards are up");
            c.Sequence(["shop", "rewards"], run.Screens.Select(screen => screen.Name), "over the shop");
            c.Equal(253, run.Gold, "400 - 147 is paid already");
            c.Equal("reward:0=Take 10 gold | proceed=Skip (leave without the unclaimed rewards)", Labels(s), "a rewards list that is not a room's last: its proceed only closes it");
            Do(run, "reward:0");
            s = run.Snapshot();
            c.Equal("shop", s.Screen, "taking its last row closes it: the shop is back");
            c.Equal((263, true), (run.Gold, s.State["inventory_open"]!.GetValue<bool>()), "with 10 gold more and the inventory still open");
            c.Equal("1,2", Numbers(s.State["stock"]!["relics"]), "the relic's place is empty");
            c.Equal("refill? Check Hamper;bought ShopRelicEntry for 147, empty;", run.State.Relics.OfType<Clerk>().Single().Heard, "and the purchase was told once");
        });

        c.Group("a card the deck does not take is not paid for", () =>
        {
            // MerchantCardEntry.OnTryPurchase: the card is added first; when that fails nothing is paid and the entry stays.
            var run = Open(200, relics: [() => new Bouncer()]);
            Do(run, "buy:card0");
            c.Equal(200, run.Gold, "no gold is gone");
            c.Equal(10, run.State.Deck.Count, "the deck is as it was");
            var s = run.Snapshot();
            c.Equal("0,1,2,3,4", Numbers(s.State["stock"]!["cards"]), "the card is still on the shelf");
            c.Equal("Headbutt", s.State["last_purchase_failed"]!.GetValue<string>(), "the snapshot names what failed");
            c.Equal("run,gold,inventory_open,player,potions,stock,last_purchase_failed", Keys(s.State), "at the end of the state");
            c.Equal("buy:card0", run.Actions[^1], "the buy was accepted all the same: a purchase the game turns down is not a refusal");
        });
    }

    private static void PotionBuys(Check c)
    {
        c.Group("buying potions: a free slot is needed", () =>
        {
            var run = Open(300, potions: ["BloodPotion", "FirePotion", null]);
            Do(run, "buy:potion1");
            c.Equal(251, run.Gold, "300 - 49");
            c.Sequence(["BloodPotion", "FirePotion", "SwiftPotion"], run.State.Belt.Slots.Select(potion => potion?.ClassName), "the potion goes into the free slot");
            var s = run.Snapshot();
            c.Equal("0,2", Numbers(s.State["stock"]!["potions"]), "the potions left keep their numbers");
            c.Equal(true, s.State["potion_slots_full"]!.GetValue<bool>(), "the belt is full and potions are on the shelf: potion_slots_full");
            c.Equal("run,gold,inventory_open,player,potions,stock,potion_slots_full", Keys(s.State), "after the stock");
            c.That(s.Actions.All(action => !action.Id.StartsWith("buy:potion", StringComparison.Ordinal)), "no potion buy is offered");
            c.That(s.State["stock"]!["potions"]![0]!["affordable"]!.GetValue<bool>(), "although the gold is there");
            c.Equal("Potion slots are full; use or discard a potion first.", Refused(c, run, "buy:potion0"), "a potion buy with a full belt");

            Do(run, "discard_potion:1");
            s = run.Snapshot();
            c.That(s.State["potion_slots_full"] is null, "with a slot free the key is gone");
            c.That(s.Actions.Any(action => action.Id == "buy:potion0") && s.Actions.Any(action => action.Id == "buy:potion2"), "and the potion buys are back");
            Do(run, "buy:potion2");
            c.Equal(147, run.Gold, "251 - 104 for Fruit Juice");
            c.Sequence(["BloodPotion", "FruitJuice", "SwiftPotion"], run.State.Belt.Slots.Select(potion => potion?.ClassName), "into the slot that was emptied");
        });

        c.Group("a shelf of potions with a full belt: open is offered for what the gold reaches, a buy is not", () =>
        {
            // The reader offers open when some stocked entry can be paid for; whether the belt has room is asked only for the buy.
            var run = new Arena.Run(new RunSetup { Gold = 60, Potions = ["BloodPotion", "FirePotion", "SwiftPotion"] }, new Stall { Kind = Shelf.PotionsOnly });
            var s = run.Snapshot();
            c.Equal("potions", Keys(s.State["stock"]), "kinds with nothing on the shelf are left out of the stock, the removal too");
            c.Equal(true, s.State["potion_slots_full"]!.GetValue<bool>(), "potion_slots_full");
            c.Equal("open proceed potion:0 discard_potion:0 discard_potion:1 discard_potion:2", Ids(s), "open is offered: Fire Potion at 52 and Swift Potion at 49 can be paid for");
            Do(run, "open");
            c.Equal("close potion:0 discard_potion:0 discard_potion:1 discard_potion:2", Ids(run.Snapshot()), "open: nothing to buy but close");
            Do(run, "potion:0");
            c.Equal("buy:potion0 buy:potion1 close discard_potion:1 discard_potion:2", Ids(run.Snapshot()), "using Blood Potion frees a slot: the two potions 60 gold pays for");
        });

        c.Group("potion_slots_full needs a potion on the shelf", () =>
        {
            var run = new Arena.Run(new RunSetup { Gold = 205 }, new Stall { Kind = Shelf.PotionsOnly });
            Do(run, "open", "buy:potion0", "buy:potion1", "buy:potion2");
            c.Equal(0, run.Gold, "205 - 52 - 49 - 104");
            var s = run.Snapshot();
            c.Equal("{}", Text(s.State["stock"]), "the shelf is empty: the stock is an empty object");
            c.That(s.State["potion_slots_full"] is null, "the belt is full, but no potion is on the shelf: no potion_slots_full");
            c.Equal("close discard_potion:0 discard_potion:1 potion:2 discard_potion:2", Ids(s), "close, and the potions that were bought (Fruit Juice can be used here)");
        });

        c.Group("a potion a hook keeps out of the belt is not paid for; potion actions leave the note", () =>
        {
            var run = Open(300, potions: ["BloodPotion"], relics: [() => new Dry()]);
            Do(run, "buy:potion0");
            c.Equal(300, run.Gold, "no gold is gone");
            c.Sequence(["BloodPotion", null, null], run.State.Belt.Slots.Select(potion => potion?.ClassName), "the belt is as it was");
            c.Equal("Fire Potion", run.Snapshot().State["last_purchase_failed"]!.GetValue<string>(), "the snapshot names the potion");
            c.Equal("0,1,2", Numbers(Stock(run)["potions"]), "it is still on the shelf");
            Do(run, "discard_potion:0");
            c.Equal("Fire Potion", run.Snapshot().State["last_purchase_failed"]!.GetValue<string>(), "a potion action does not clear the note");
            Do(run, "buy:card1");
            c.That(run.Snapshot().State["last_purchase_failed"] is null, "the next shop action that succeeds does");
            Do(run, "buy:potion0", "close");
            c.That(run.Snapshot().State["last_purchase_failed"] is null, "closing clears it too");
        });
    }

    // =====================================================================================
    // The card removal
    // =====================================================================================

    private static void Removal(Check c)
    {
        c.Group("the removal: a card choice of the buy, 75 gold, once per shop", () =>
        {
            var setup = new RunSetup { Gold = 100 };
            var run = new Arena.Run(setup, new Stall());
            Do(run, "open", "buy:remove");
            var s = run.Snapshot();
            c.Equal("card_select", s.Screen, "the deck's removal screen is up");
            c.Equal(("remove", 1, 1), (s.State["kind"]!.GetValue<string>(), s.State["min"]!.GetValue<int>(), s.State["max"]!.GetValue<int>()), "kind remove, one card");
            c.Equal("card:0=Pick Strike to remove | card:5=Pick Defend to remove | card:9=Pick Bash to remove | cancel=Close without removing a card", Labels(s), "the deck's cards, and the screen can be closed");
            c.Equal(100, run.Gold, "nothing is paid yet");

            Do(run, "card:0");
            c.Equal("confirm=Confirm: remove Strike | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "the pick opens the preview");
            Do(run, "confirm");
            s = run.Snapshot();
            c.Equal("shop", s.Screen, "back in the shop");
            c.Equal(25, run.Gold, "100 - 75");
            c.Equal("""{"Bash":1,"Defend":4,"Strike":4}""", Text(s.State["player"]!["deck"]), "a Strike is gone");
            c.That(s.State["stock"]!["removal"] is null, "the removal is no longer on the shelf");
            c.That(s.State["last_purchase_failed"] is null, "nothing failed");
            c.Equal(true, s.State["inventory_open"]!.GetValue<bool>(), "the inventory is still open");
            c.Equal(1, run.State.Part<ShopState>().CardRemovalsUsed, "the run counts one removal bought");
            c.Equal(100, ShopPrices.RemovalPrice(run.State), "the next shop asks 100");
            c.Equal("'buy:remove' is not in stock.", Refused(c, run, "buy:remove"), "a second removal in the same shop");
            c.Sequence(["open", "buy:remove", "card:0", "confirm"], run.Actions, "the save: the buy, then the steps of its question");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Stall(), run.Actions).Dump(), "which replays to the same state");
        });

        c.Group("closing the removal screen: nothing paid, the removal stays, the snapshot says what failed", () =>
        {
            var setup = new RunSetup { Gold = 100, Potions = ["BloodPotion"] };
            var run = new Arena.Run(setup, new Stall());
            Do(run, "open", "buy:remove", "cancel");
            var s = run.Snapshot();
            c.Equal("shop", s.Screen, "back in the shop");
            c.Equal(100, run.Gold, "no gold is gone");
            c.Equal(10, run.State.Deck.Count, "no card is gone");
            c.Equal("""{"price":75,"affordable":true}""", Text(s.State["stock"]!["removal"]), "the removal is still on the shelf");
            c.Equal("a card removal", s.State["last_purchase_failed"]!.GetValue<string>(), "last_purchase_failed");
            c.Equal(0, run.State.FindPart<ShopState>()?.CardRemovalsUsed ?? 0, "no removal is counted");
            c.Equal(75, ShopPrices.RemovalPrice(run.State), "and the price has not risen");
            c.Equal(run.Dump(), run.Clone().Dump(), "a copy carries the note");
            c.Equal("Close the shop inventory first.", Refused(c, run, "proceed"), "and a refused action leaves it where it is");

            Do(run, "potion:0");
            c.Equal("a card removal", run.Snapshot().State["last_purchase_failed"]!.GetValue<string>(), "a potion action leaves the note");

            // Pick, go back from the preview, then close: the same end.
            Do(run, "buy:remove");
            c.That(run.Snapshot().Screen == "card_select", "the removal can be tried again");
            Do(run, "card:9", "cancel", "cancel");
            c.Equal(("shop", 100, "a card removal"), (run.Snapshot().Screen, run.Gold, run.Snapshot().State["last_purchase_failed"]!.GetValue<string>()), "backed out again");

            Do(run, "buy:remove", "card:9", "confirm");
            s = run.Snapshot();
            c.Equal(25, run.Gold, "this time it is paid");
            c.Equal("""{"Defend":4,"Strike":5}""", Text(s.State["player"]!["deck"]), "Bash is gone");
            c.That(s.State["last_purchase_failed"] is null, "and the note is gone");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Stall(), run.Actions).Dump(), "the whole visit replays from its save");
            c.Equal(run.Dump(), run.Clone().Dump(), "and a copy is in the same state");
        });

        c.Group("the removal's price over a run: 75, 100, 125, and only shop removals count", () =>
        {
            var setup = new RunSetup { Gold = 400 };
            var run = new Arena.Run(setup, new Stall { Shops = 3 });
            // A removal that does not come from a shop (an event's, a relic's) leaves the price alone.
            DeckEdits.Remove(run, run.State.Deck[0]);
            c.Equal(75, ShopPrices.RemovalPrice(run.State), "a card removed by something else does not raise the price");

            c.Equal("""{"price":75,"affordable":true}""", Text(Stock(run)["removal"]), "the first shop: 75");
            Do(run, "open", "buy:remove", "card:0", "confirm", "close", "proceed");
            c.Equal(325, run.Gold, "400 - 75");
            c.Equal("""{"price":100,"affordable":true}""", Text(Stock(run)["removal"]), "the second shop: 100");
            Do(run, "open", "buy:remove", "card:0", "confirm", "close", "proceed");
            c.Equal(225, run.Gold, "325 - 100");
            c.Equal("""{"price":125,"affordable":true}""", Text(Stock(run)["removal"]), "the third shop: 125");
            Do(run, "open");
            c.That(Labels(run.Snapshot()).Contains("buy:remove=Pay 125 gold to remove a card from your deck", StringComparison.Ordinal), "and its label says so");
            c.Equal(7, run.State.Deck.Count, "three cards are gone in all");
        });
    }

    // =====================================================================================
    // The price hook
    // =====================================================================================

    private static void PriceHooks(Check c)
    {
        c.Group("the price hook: each listener gets what the one before returned; the result is cut to a whole number", () =>
        {
            // Hook.ModifyMerchantPrice and MerchantEntry.Cost. First minus 10, then times 0.5:
            // Headbutt (52 - 10) x 0.5 = 21; Twin Strike (24 - 10) x 0.5 = 7; Check Marvel (168 - 10) x 0.5 = 79; the removal (75 - 10) x 0.5 = 32.5 -> 32.
            var run = Open(30, relics: [() => new Haggle { Minus = 10 }, () => new Haggle { Times = 0.5m }]);
            var stock = Stock(run);
            c.Equal("21,7,19,34,31", Prices(stock["cards"]), "the cards: (52, 24, 48, 78, 73 minus 10) halved, the halves cut off");
            c.Equal("37,79", Prices(stock["colorless"]), "the colorless cards: (85 - 10) / 2 = 37.5 -> 37, (168 - 10) / 2 = 79");
            c.Equal("68,113,104", Prices(stock["relics"]), "the relics: 68.5 -> 68, 113, 104");
            c.Equal("21,19,47", Prices(stock["potions"]), "the potions: 21, 19.5 -> 19, 47");
            c.Equal("""{"price":32,"affordable":false}""", Text(stock["removal"]), "the removal goes through the hook too: 32.5 -> 32, which 30 gold does not pay");
            c.That(stock["cards"]![0]!["affordable"]!.GetValue<bool>() && !stock["cards"]![3]!["affordable"]!.GetValue<bool>(), "affordable is by the price after the hook: 21 yes, 34 no");
            c.That(Labels(run.Snapshot()).Contains("buy:card0=Buy Headbutt for 21 gold |", StringComparison.Ordinal), "the label shows the price after the hook");
            Do(run, "buy:card0");
            c.Equal(9, run.Gold, "and that is what is paid: 30 - 21");

            // The other way round: times 0.5, then minus 10. Headbutt 52 x 0.5 - 10 = 16; Twin Strike 2; the removal 27.5 -> 27.
            var reversed = Open(30, relics: [() => new Haggle { Times = 0.5m }, () => new Haggle { Minus = 10 }]);
            c.Equal("16,2,14,29,26", Prices(Stock(reversed)["cards"]), "the listeners are asked in pickup order");
            c.Equal(27, Stock(reversed)["removal"]!["price"]!.GetValue<int>(), "the removal: 27.5 -> 27");

            // Cut, not rounded: 24 x 0.9 = 21.6 -> 21; 78 x 0.9 = 70.2 -> 70; 48 x 0.9 = 43.2 -> 43.
            var cut = Open(30, relics: [() => new Haggle { Times = 0.9m }]);
            c.Equal("46,21,43,70,65", Prices(Stock(cut)["cards"]), "46.8 -> 46, 21.6 -> 21, 43.2 -> 43, 70.2 -> 70, 65.7 -> 65");

            // The hook is handed the entry.
            var relicsOnly = Open(300, relics: [() => new Haggle { Times = 0.5m, OnlyRelics = true }]);
            c.Equal("52,24,48,78,73", Prices(Stock(relicsOnly)["cards"]), "a listener that only changes relic prices leaves the cards alone");
            c.Equal("73,118,109", Prices(Stock(relicsOnly)["relics"]), "and halves the relics: 73.5 -> 73, 118, 109");
        });

        c.Group("a relic that changes prices, bought in the shop: paid in full, and what is reported as spent is the price afterwards", () =>
        {
            // MerchantRelicEntry.OnTryPurchase: gold is taken at the price as it stands, then the relic is obtained, and the amount
            // handed to the purchase hook is the price read once more, now with the new relic's discount on itself.
            var run = Open(300, new Stall { Kind = Shelf.MemberFirst }, relics: [() => new Clerk()]);
            c.Equal(147, Stock(run)["relics"]![0]!["price"]!.GetValue<int>(), "the relic at 147 while nobody holds it");
            Do(run, "buy:relic0");
            c.Equal(153, run.Gold, "300 - 147: the full price");
            c.Equal("refill? Check Member;bought ShopRelicEntry for 73, empty;", run.State.Relics.OfType<Clerk>().Single().Heard, "the hook is told 73: 147 halved by the relic that was just bought");
            c.Equal("26,12,24,39,36", Prices(Stock(run)["cards"]), "from now on everything costs half");
        });
    }

    // =====================================================================================
    // Refill, and the purchase hook
    // =====================================================================================

    private static void PurchaseHooks(Check c)
    {
        c.Group("after a purchase: asked whether the place is filled again, then told what was bought", () =>
        {
            var run = Open(400, relics: [() => new Clerk()]);
            Clerk Listener() => run.State.Relics.OfType<Clerk>().Single();
            Do(run, "buy:card1");
            c.Equal("refill? Twin Strike;bought ShopCardEntry for 24, empty;", Listener().Heard, "a card: the refill question, then the purchase with the gold spent; the place is empty by then");
            Do(run, "buy:potion0");
            c.Equal("refill? Twin Strike;bought ShopCardEntry for 24, empty;refill? Fire Potion;bought ShopPotionEntry for 52, empty;", Listener().Heard, "a potion");
            Do(run, "buy:remove", "card:0", "confirm");
            c.That(Listener().Heard.EndsWith("bought ShopPotionEntry for 52, empty;bought ShopRemovalEntry for 75, empty;", StringComparison.Ordinal),
                $"the removal: told, with 75 gold, and never asked about a refill ({Listener().Heard})");
            var heard = Listener().Heard;
            c.That(!run.Execute("buy:remove").Ok && !run.Execute("buy:colorless9").Ok && Listener().Heard == heard, "a refusal is heard by nobody");
        });

        c.Group("a purchase that does not go through is heard by nobody", () =>
        {
            var run = Open(400, relics: [() => new Clerk()]);
            Do(run, "buy:remove", "cancel");
            c.Equal("", run.State.Relics.OfType<Clerk>().Single().Heard, "a removal that was backed out of: no refill question, no purchase");
        });

        c.Group("the first listener that says refill decides; the ones after it are not asked", () =>
        {
            var run = Open(400, new Stall { World = true }, relics: [() => new Clerk { Refills = true }, () => new Clerk()]);
            Do(run, "buy:potion0");
            var clerks = run.State.Relics.OfType<Clerk>().ToList();
            c.Equal("refill? Fire Potion;bought ShopPotionEntry for 52, stocked;", clerks[0].Heard, "the first says yes: the place is filled when the purchase is told");
            c.Equal("bought ShopPotionEntry for 52, stocked;", clerks[1].Heard, "the second is not asked, and is told all the same");
        });

        c.Group("a card place filled again: a new card of the same type, never on sale, one price", () =>
        {
            // MerchantCardEntry.RestockAfterPurchase: the sale mark goes, and the place is populated again: a rarity and an upgrade roll
            // on rewards, a pick and one price on shops. The new card is none the shelf shows, the bought one included.
            var run = Open(400, new Stall { World = true }, relics: [() => new Clerk { Refills = true }]);
            var (rewards, shops) = (Counter(run, RunStream.Rewards), Counter(run, RunStream.Shops));
            Do(run, "buy:card1");
            c.Equal(376, run.Gold, "400 - 24");
            var s = run.Snapshot();
            var cards = s.State["stock"]!["cards"]!.AsArray();
            c.Equal("0,1,2,3,4", Numbers(cards), "the place is filled: no gap");
            var entry = Shop(run).Inventory.CharacterCards[1];
            c.Equal(CardType.Attack, entry.Card!.Type, "an Attack again");
            c.That(entry.Card.Def.ClassName is not ("TwinStrike" or "Headbutt"), $"neither the card just bought nor the other Attack on the shelf ({entry.Card.Def.ClassName})");
            c.That(!entry.IsOnSale && cards[1]!["on_sale"] is null, "the new card is not on sale");
            var (low, high) = entry.Card.Def.Rarity switch { CardRarity.Rare => (142, 158), CardRarity.Uncommon => (71, 79), _ => (48, 52) };
            c.That(entry.BaseCost >= low && entry.BaseCost <= high, $"its price is a full one for its rarity ({entry.Card.Def.Rarity}: {entry.BaseCost})");
            c.Equal((rewards + 2, shops + 2), (Counter(run, RunStream.Rewards), Counter(run, RunStream.Shops)), "two numbers of rewards and two of shops");
            c.Sequence(["TwinStrike"], run.State.Deck.TakeLast(1).Select(card => card.Def.ClassName), "the bought card is in the deck");
            c.That(s.Actions.Any(action => action.Id == "buy:card1"), "the same id now buys the new card");
            var (newCard, newPrice) = (entry.Card.Def.ClassName, entry.BaseCost);
            Do(run, "buy:card1");
            c.Equal(376 - newPrice, run.Gold, "at the new card's price");
            c.Equal(newCard, run.State.Deck[^1].Def.ClassName, "and the new card is the one that goes into the deck");
        });

        c.Group("a place filled again names the same place of the shelf for keyed streams", () =>
        {
            var run = Open(400, new Stall { World = true, Mode = StreamMode.Keyed }, relics: [() => new Clerk { Refills = true }]);
            string Asked() => string.Join(" ", World(run).Streams.Dump()["asked"]!.AsObject().OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));
            c.Equal("", Asked(), "a shelf built by hand has rolled nothing");
            Do(run, "buy:card1");
            c.Equal("0/4,6/0|shop_card/1/0=2 0/4,6/0|shop_price/1/0=1", Asked(), "the card place 1: its card's two streams and one price");
            Do(run, "buy:potion2");
            c.Equal("0/4,6/0|shop_card/1/0=2 0/4,6/0|shop_potion/2/0=1 0/4,6/0|shop_price/1/0=1 0/4,6/0|shop_price/12/0=1", Asked(), "the potion place 2: its potion and its price");
            c.Equal((0, 0), (Counter(run, RunStream.Rewards), Counter(run, RunStream.Shops)), "the long streams did not move");
        });

        c.Group("a potion place filled again: any potion, three numbers of shops", () =>
        {
            var run = Open(400, new Stall { World = true }, relics: [() => new Clerk { Refills = true }]);
            var (rewards, shops) = (Counter(run, RunStream.Rewards), Counter(run, RunStream.Shops));
            Do(run, "buy:potion0");
            var entry = Shop(run).Inventory.Potions[0];
            c.That(entry.IsStocked, "the place holds a potion again");
            c.Equal("0,1,2", Numbers(Stock(run)["potions"]), "no gap");
            var passed = run.State.FindPart<LeftOutLog>()?.Entries.Count ?? 0;
            c.Equal((rewards, shops + 3 + 2 * passed), (Counter(run, RunStream.Rewards), Counter(run, RunStream.Shops)), "a rarity, a pick and a price on shops (two more for every potion passed over); none of rewards");
            var (low, high) = entry.Potion!.Rarity switch { PotionRarity.Rare => (95, 105), PotionRarity.Uncommon => (71, 79), _ => (48, 52) };
            c.That(entry.BaseCost >= low && entry.BaseCost <= high, $"priced by its rarity ({entry.Potion.Rarity}: {entry.BaseCost})");
        });

        c.Group("a relic place filled again: a new rarity on rewards, the back of the bag, a price on shops", () =>
        {
            var run = Open(400, new Stall { World = true }, relics: [() => new Clerk { Refills = true }]);
            var (rewards, shops) = (Counter(run, RunStream.Rewards), Counter(run, RunStream.Shops));
            Do(run, "buy:relic0");
            if (run.IsOver)
            {
                c.That(run.Result == RunResult.Unrepresentable && run.ResultReason!.Contains("a shop relic", StringComparison.Ordinal),
                    $"no relic of the bag is built yet: the run ends as unrepresentable ({run.ResultReason})");
                return;
            }
            var entry = Shop(run).Inventory.Relics[0];
            c.That(entry.IsStocked && GameLists.IsBagRelic(entry.Relic!.ClassName), $"the place holds a relic of the bag ({entry.Relic?.ClassName})");
            c.That(!NeverSold.Contains(entry.Relic!.ClassName), "one a shop sells");
            c.That(!World(run).PlayerBag.Contains(entry.Relic.ClassName) && !World(run).SharedBag.Contains(entry.Relic.ClassName), "it has left both bags");
            c.That(Counter(run, RunStream.Rewards) > rewards, "the rarity was rolled on rewards");
            c.Equal(shops + 1, Counter(run, RunStream.Shops), "one number of shops: the price");
            c.Equal(253, run.Gold, "400 - 147 for the relic that was bought");
        });
    }

    // =====================================================================================
    // The card hook
    // =====================================================================================

    private static void CardHooks(Check c)
    {
        c.Group("the card hook is asked for each card as it is made, one card at a time", () =>
        {
            // MerchantCardEntry.Populate: after the factory, before the price.
            var run = Start("1BUSLN0FY5", At(FirstRun, enter: false), new RunSetup { Seed = "1BUSLN0FY5", ExtraRelics = [() => new Whetter()] });
            var shelf = EmptyShelf();
            shelf.StockCharacterCards(run);
            var whetter = run.State.Relics.OfType<Whetter>().Single();
            c.Equal((5, 5), (whetter.Lists, whetter.Asked), "five lists of one card");
            c.Sequence(["Tear Asunder+", "Ashen Strike+", "Tremble", "Blood Wall", "Vicious"], shelf.CharacterCards.Select(entry => entry.Card!.Title), "the two Attacks of the real shelf come upgraded, the rest plain");
            c.That(shelf.CharacterCards[0].Result!.HasBeenModified && !shelf.CharacterCards[2].Result!.HasBeenModified, "the result says which card a relic changed");
            c.Equal(77, shelf.CharacterCards[0].BaseCost, "the price is the same: an upgrade costs nothing");
        });

        c.Group("after every purchase the card hook is asked again for every card still on the shelf", () =>
        {
            // MerchantInventory.UpdateEntries: every entry is brought up to date when any purchase is completed.
            var run = Open(400, relics: [() => new Whetter()]);
            Whetter Listener() => run.State.Relics.OfType<Whetter>().Single();
            c.Equal("Headbutt, Twin Strike, Shrug It Off, Flame Barrier, Inflame", Names(Stock(run)["cards"]), "a shelf built by hand was never shown to the hook");
            Do(run, "buy:potion0");
            c.Equal((7, 7), (Listener().Lists, Listener().Asked), "one potion bought: seven cards on the shelf, seven lists of one");
            c.Equal("Headbutt+, Twin Strike+, Shrug It Off, Flame Barrier, Inflame", Names(Stock(run)["cards"]), "the Attacks are upgraded where they stand");
            c.Equal("52,24,48,78,73", Prices(Stock(run)["cards"]), "at the same prices");
            Do(run, "buy:card0");
            c.Equal("Headbutt+", run.State.Deck[^1].Title, "the upgraded card is what is bought");
            c.Equal(13, Listener().Lists, "then six cards are left: six more lists");
            Do(run, "buy:remove", "card:0", "confirm");
            c.Equal(19, Listener().Lists, "the removal brings the shelf up to date too: six more");
        });

        c.Group("a relic bought in the shop changes the cards on its shelf at once", () =>
        {
            var run = Open(400, new Stall { Kind = Shelf.WhetterFirst });
            c.Equal("Headbutt, Twin Strike, Shrug It Off, Flame Barrier, Inflame", Names(Stock(run)["cards"]), "before");
            Do(run, "buy:relic0");
            c.Equal("Headbutt+, Twin Strike+, Shrug It Off, Flame Barrier, Inflame", Names(Stock(run)["cards"]), "after: its hook was asked when the purchase was completed");
            c.That(Labels(run.Snapshot()).Contains("buy:card1=Buy Twin Strike+ for 24 gold (on sale)", StringComparison.Ordinal), "the label names the upgraded card");
        });
    }

    // =====================================================================================
    // Copies and the save
    // =====================================================================================

    private static void Copies(Check c)
    {
        c.Group("a copy of a run in a shop is its own: buying in one changes nothing in the other", () =>
        {
            var setup = new RunSetup { Gold = 500, Potions = ["BloodPotion"] };
            var run = new Arena.Run(setup, new Stall());
            Do(run, "open", "buy:card3");
            var before = run.Dump();
            var copy = run.Clone();
            c.Equal(before, copy.Dump(), "a copy is in the same state");
            Do(copy, "buy:relic0", "buy:card0", "buy:potion2", "buy:remove", "card:0", "confirm", "close", "proceed");
            c.Equal(before, run.Dump(), "the original has not moved");
            c.Equal("0,1,2,4", Numbers(Stock(run)["cards"]), "its shelf still has what the copy bought");
            Do(run, "buy:relic0", "buy:card0", "buy:potion2", "buy:remove", "card:0", "confirm", "close", "proceed");
            c.Equal(copy.Dump(), run.Dump(), "the same ids take it to where the copy went");
            c.Equal(44, run.Gold, "500 minus 78, 147, 52, 104 and 75");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Stall(), run.Actions).Dump(), "and the save replays");

            // In the middle of the removal's question.
            var asking = new Arena.Run(setup, new Stall());
            Do(asking, "open", "buy:remove", "card:5");
            var twin = asking.Clone();
            c.Equal(asking.Dump(), twin.Dump(), "a copy in the middle of the removal's question");
            Do(asking, "confirm");
            Do(twin, "confirm");
            c.Equal(asking.Dump(), twin.Dump(), "both go on alike");
            c.Equal("""{"Bash":1,"Defend":3,"Strike":5}""", Text(twin.Snapshot().State["player"]!["deck"]), "a Defend is gone");
        });

        c.Group("a rolled shop: copies and the save", () =>
        {
            var setup = new RunSetup { Seed = "C0PY1", Gold = 500 };
            var run = new Arena.Run(setup, new Merchant { RewardsAt = 9 });
            if (!ShopShown(run))
            {
                c.That(run.Result == RunResult.Unrepresentable, "the arena cannot fill a shelf yet: the run ends as unrepresentable");
                c.Equal(run.Dump(), Arena.Run.Replay(setup, new Merchant { RewardsAt = 9 }, run.Actions).Dump(), "which a replay does too");
                return;
            }
            Do(run, "open");
            var buys = run.Snapshot().Actions.Where(action => action.Id.StartsWith("buy:", StringComparison.Ordinal) && action.Id != "buy:remove").Select(action => action.Id).Take(3).ToArray();
            var gold = run.Gold;
            var spent = 0;
            foreach (var id in buys)
            {
                var action = run.Snapshot().Actions.FirstOrDefault(each => each.Id == id);
                if (action is null) continue;
                // The label is the promise: "... for <price> gold".
                var words = action.Label.Split(' ');
                spent += int.Parse(words[Array.IndexOf(words, "gold") - 1], CultureInfo.InvariantCulture);
                Do(run, id);
            }
            c.Equal(gold - spent, run.Gold, "each buy takes what its label says");
            Do(run, "close", "proceed");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, new Merchant { RewardsAt = 9 }, run.Actions).Dump(), "the visit replays from its save");
        });
    }

    // =====================================================================================
    // The list of the run's hooks
    // =====================================================================================

    private static void HookList(Check c)
    {
        c.Group("the shop's two hooks of the 27 are declared and name who sends them", () =>
        {
            var price = RunHooks.Hooks.Single(hook => hook.Name == "ModifyMerchantPrice");
            var refill = RunHooks.Hooks.Single(hook => hook.Name == "ShouldRefillMerchantEntry");
            c.That(price.IsDeclared && price.SentBy == "ShopHooks.ModifyMerchantPrice", $"ModifyMerchantPrice is sent by ShopHooks.ModifyMerchantPrice ({price.SentBy}, waits for {price.Waits})");
            c.That(refill.IsDeclared && refill.SentBy == "ShopHooks.ShouldRefillMerchantEntry", $"ShouldRefillMerchantEntry is sent by ShopHooks.ShouldRefillMerchantEntry ({refill.SentBy}, waits for {refill.Waits})");
            foreach (var name in new[] { "ModifyMerchantPrice", "ShouldRefillMerchantEntry", "AfterItemPurchased" })
                c.That(typeof(Listener).GetMethod(name) is { IsVirtual: true } method && method.GetParameters()[0].ParameterType == typeof(RunContext) && method.GetParameters()[1].ParameterType == typeof(ShopEntry),
                    $"{name} is a hook of Listener, handed the run's context and the entry");
        });
    }
}
