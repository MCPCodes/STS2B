using System.Globalization;
using System.Text.Json;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The 14 relics of NeowCore, each from its game class (MegaCrit.Sts2.Core.Models.Relics,
/// v0.107.1), outside Neow's room and through it.
/// <para>
/// What the expected values rest on. Numbers, order and stream of every pickup: the relic's
/// own class and the commands it calls, read by hand (the game adds every card of one add
/// before anybody hears of one; a transform of several cards takes all the originals out
/// first; a max HP loss is damage first; a curse comes before the gold). A random card: the
/// Ironclad's pool typed in below from the game's own list (IroncladCardPool.cs and each
/// card's class), indexed by a second stream of the same seed walked by the rule in words.
/// The real game's records (sts2-notes/golden-saves/history), typed in where they are used:
/// on seed 1BUSLN0FY5 Neow offered Precise Scissors, Arcane Scroll and Leafy Poultice, and
/// Arcane Scroll gave Barricade from one number of rewards; on seed 75F277YN2G Pomander was
/// taken and Bash was upgraded; on seed AG5XPLQQNY Golden Pearl paid 150 gold. Titles are the
/// game's English name table's. Screens are the mod's (docs/screens.md).
/// </para>
/// </summary>
public sealed class NeowCoreChecks : IArenaChecks
{
    // The entry point is the interface's own, so that inside this class "Run" is the run.
    void IArenaChecks.Run(Check c)
    {
        Table(c);
        GoldAndMaxHp(c);
        CardsThatAreNamed(c);
        Talisman(c);
        ArcaneScrollsCard(c);
        HeftyTabletsChoice(c);
        LostCoffersRewards(c);
        Scissors(c);
        Shears(c);
        PomandersUpgrade(c);
        NewLeafsTransform(c);
        Poultice(c);
        Conch(c);
        InsideAFight(c);
        NeowOnTheRealSeed(c);
        TheGamesRecords(c);
        EveryRelicThroughAnAncient(c);
        SeedsWhoseOfferIsBuilt(c);
        GoldenFolder(c);
    }

    private const string RealSeed = "1BUSLN0FY5";

    private static readonly string[] OvergrowthActs = ["Overgrowth", "Hive", "Glory"];

    // ====================================================================================
    // the game's lists, typed in
    // ====================================================================================

    /// <summary>
    /// The Ironclad's cards a single-player run can roll, in the order of the game's pool
    /// (IroncladCardPool.GenerateAllCards), each with the rarity of its own class: the 87 less
    /// the three Basic cards, the two Ancient ones (Break, Corruption) and the two that are
    /// multiplayer only (Demonic Shield, Tank). C Common, U Uncommon, R Rare.
    /// </summary>
    private static readonly (string ClassName, CardRarity Rarity)[] Pool = (
        "Aggression:R Anger:C Armaments:C AshenStrike:U Barricade:R BattleTrance:U BloodWall:C Bloodletting:C Bludgeon:U BodySlam:C Brand:R Breakthrough:C " +
        "Bully:U BurningPact:U Cascade:R Cinder:C Colossus:U Conflagration:R CrimsonMantle:R Cruelty:R DarkEmbrace:R DemonForm:R Dismantle:U Dominate:U " +
        "DrumOfBattle:U EvilEye:U ExpectAFight:U Feed:R FeelNoPain:U FiendFire:R FightMe:U FlameBarrier:U ForgottenRitual:U Havoc:C Headbutt:C Hellraiser:R " +
        "Hemokinesis:U HowlFromBeyond:U Impervious:R InfernalBlade:U Inferno:U Inflame:U IronWave:C Juggernaut:R Juggling:U Mangle:R MoltenFist:C NotYet:R " +
        "Offering:R OneTwoPunch:R PactsEnd:R PerfectedStrike:C Pillage:U PommelStrike:C PrimalForce:R Pyre:R Rage:U Rampage:U Rupture:U SecondWind:U " +
        "SetupStrike:C ShrugItOff:C Spite:U Stampede:U Stoke:R Stomp:U StoneArmor:U SwordBoomerang:C Taunt:U TearAsunder:R Thrash:R Thunderclap:C Tremble:C " +
        "TrueGrit:C TwinStrike:C Unmovable:R Unrelenting:U Uppercut:U Vicious:U Whirlwind:U")
        .Split(' ')
        .Select(entry => entry.Split(':'))
        .Select(parts => (parts[0], parts[1] switch { "C" => CardRarity.Common, "U" => CardRarity.Uncommon, _ => CardRarity.Rare }))
        .ToArray();

    /// <summary>The 25 Rares, in the pool's order: what Arcane Scroll and Hefty Tablet pick from.</summary>
    private static readonly string[] Rares = Pool.Where(card => card.Rarity == CardRarity.Rare).Select(card => card.ClassName).ToArray();

    /// <summary>Class name, the game's English title, the arena's text.</summary>
    private static readonly (string ClassName, string Title, string Text)[] Expected =
    [
        ("ArcaneScroll", "Arcane Scroll", "When picked up: add 1 random Rare card to your deck."),
        ("BoomingConch", "Booming Conch", "On the first turn of every Elite fight: draw 2 more cards and gain 1 energy."),
        ("GoldenPearl", "Golden Pearl", "When picked up: gain 150 gold."),
        ("LostCoffer", "Lost Coffer", "When picked up: a rewards screen with a card reward (1 of 3 cards) and a random potion."),
        ("NeowsTorment", "Neow's Torment", "When picked up: add the card Neow's Fury to your deck."),
        ("NewLeaf", "New Leaf", "When picked up: choose 1 card of your deck. It is transformed into a random card."),
        ("PreciseScissors", "Precise Scissors", "When picked up: choose 1 card to remove from your deck."),
        ("NutritiousOyster", "Nutritious Oyster", "When picked up: gain 11 max HP."),
        ("NeowsTalisman", "Neow's Talisman", "When picked up: upgrade one of your starter Strikes and one of your starter Defends (the last of each in your deck)."),
        ("Pomander", "Pomander", "When picked up: choose 1 card of your deck to upgrade."),
        ("CursedPearl", "Cursed Pearl", "When picked up: add the curse Greed to your deck, then gain 333 gold."),
        ("HeftyTablet", "Hefty Tablet", "When picked up: choose 1 of 3 random Rare cards to add to your deck (you may skip), then add the curse Injury to your deck."),
        ("LeafyPoultice", "Leafy Poultice", "When picked up: lose 12 max HP. One of your starter Strikes and one of your starter Defends (the first of each in your deck) are each transformed into a random card."),
        ("PrecariousShears", "Precarious Shears", "When picked up: choose 2 cards to remove from your deck, then take 16 damage."),
    ];

    // ====================================================================================
    // helpers
    // ====================================================================================

    /// <summary>
    /// A relic of the checks that writes down what it hears, with what the player has at that
    /// moment: a card arriving in the deck, a card about to leave it, gold gained, HP about to
    /// be lost, and the cards of an offer that was just rolled, with what the offer was made for.
    /// </summary>
    private sealed class Witness : Relic, IDeckListener, IRewardHooks
    {
        public string Seen = "";

        public override string Name => "Witness";
        public override string Describe() => "A relic of the checks.";

        private void Note(FormattableString what) => Seen = (Seen.Length == 0 ? "" : Seen + "; ") + what.ToString(CultureInfo.InvariantCulture);

        public void AfterCardChangedPiles(RunContext c, CardInstance card, PileType oldPile) =>
            Note($"{card.Def.ClassName} in from {oldPile}, {c.Deck.Count} in the deck, {c.Gold} gold");

        public void BeforeCardRemoved(RunContext c, CardInstance card) => Note($"{card.Def.ClassName} out, {c.Deck.Count} in the deck, {c.Hp} HP");

        public override void AfterGoldGained(RunContext c) => Note($"gold is {c.Gold}");

        public bool TryModifyCardRewardOptions(RunContext c, List<CardCreationResult> cards, CardCreationOptions options)
        {
            Note($"{cards.Count} rolled for {options.Source} on the {options.RarityOdds} table ({options.Flags})");
            return false;
        }

        public override decimal ModifyHpLostAfterOsty(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, f.Player)) Note($"losing {amount} HP ({props}) at {target.Hp} of {target.MaxHp}");
            return amount;
        }
    }

    /// <summary>A run that stands at a desk outside any room, on its seed's own world when asked: one action per relic takes it.</summary>
    private sealed class AtADesk(bool world, StreamMode mode) : RunDirector
    {
        public override void Begin(Run run)
        {
            if (world) RunWorld.Begin(run.State, mode: mode);
            run.Push(new Desk(TakeScript));
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    /// <summary>The desk's actions: "take:GoldenPearl" obtains that relic as Neow's option does, inside an action, so that a pickup can ask.</summary>
    private static readonly Dictionary<string, Action<Run, Desk>> TakeScript =
        NeowCore.All.ToDictionary(name => "take:" + name, name => (Action<Run, Desk>)((run, _) => run.ObtainRelic(name)), StringComparer.Ordinal);

    private static Run Desk(RunSetup? setup = null, bool world = true, StreamMode mode = StreamMode.Game) =>
        new(setup ?? new RunSetup { Seed = RealSeed }, new AtADesk(world, mode));

    private static void Take(Run run, string relic) => Do(run, "take:" + relic);

    /// <summary>A setup on the real seed with a deck of the check's own ("Bash+" is an upgraded Bash) and the witness.</summary>
    private static RunSetup With(string[]? deck = null, int hp = 80, int maxHp = 80, string seed = RealSeed, string[]? relics = null, bool witness = true) => new()
    {
        Seed = seed, Hp = hp, MaxHp = maxHp,
        Deck = deck is null ? RunSetup.IroncladDeck : deck.Select(DeckCard.Parse).ToList(),
        Relics = relics ?? ["BurningBlood"],
        ExtraRelics = witness ? [static () => new Witness()] : [],
    };

    private static string Heard(Run run) => run.State.FindRelic<Witness>()?.Seen ?? "(no witness)";

    private static RunStreams Streams(Run run) => run.State.Part<RunWorld>().Streams;

    private static (int Rewards, int Niche, int Transformations) Counters(Run run) =>
        (Streams(run).Counter(RunStream.Rewards), Streams(run).Counter(RunStream.Niche), Streams(run).Counter(RunStream.Transformations));

    /// <summary>A stream of a seed as it stands before anything was drawn: for walking a roll by the rule in words.</summary>
    private static GameRng Fresh(string seed, RunStream stream) => new RunStreams(seed).Long(stream);

    private static string Starter => "StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash";

    private static string Kind(GameSnapshot s) => s.State["kind"]?.GetValue<string>() ?? "(no kind)";

    private static (int Min, int Max) MinMax(GameSnapshot s) => (s.State["min"]!.GetValue<int>(), s.State["max"]!.GetValue<int>());

    /// <summary>The entries of a card grid as "i:name" or "i:name x copies".</summary>
    private static string Listed(GameSnapshot s) => string.Join(", ", s.State["cards"]!.AsArray().Select(card =>
        string.Create(CultureInfo.InvariantCulture, $"{card!["i"]}:{card["name"]}{(card["copies"] is { } copies ? $" x{copies}" : "")}{(card["selected"] is not null ? " (selected)" : "")}")));

    private static List<string> Titles(GameSnapshot s) => s.State["options"]!.AsArray().Select(option => option!["title"]!.GetValue<string>()).ToList();

    private static string Q(string json) => json.Replace('\'', '"');

    private static string Player(int hp = 80, int maxHp = 80, int gold = 99, string relics = "'Burning Blood'", string deck = "'deck_size':10,'deck':{'Bash':1,'Defend':4,'Strike':5}") =>
        Q($"'player':{{'character':'The Ironclad','hp':{hp},'max_hp':{maxHp},'gold':{gold},'relics':[{relics}],{deck}}}");

    private static readonly string NoPotions = Q("'potions':[null,null,null]");

    /// <summary>A card as an event option's tooltip lists it, by class name.</summary>
    private static string CardJson(string className) => Text(RunDescribe.Card(new RunState(new RunSetup()).NewCard(Content.Registry.Card(className))));

    private static List<string> OvergrowthSeeds(int count, ulong from)
    {
        var maker = new GameRng(from);
        var seeds = new List<string>();
        while (seeds.Count < count)
        {
            var seed = RunSeed.Random(maker);
            if (RunGeneration.RollActs(seed, RunUnlocks.All)[0] == GameLists.Overgrowth) seeds.Add(seed.Text);
        }
        return seeds;
    }

    // ---------- Neow's room ----------

    /// <summary>Relics of the checks under the class names of Neow's relics that later chunks build, so that a real offer which
    /// holds one can be shown. None is taken in a check; none does anything.</summary>
    private static class NotBuiltYet
    {
        public sealed class SilverCrucible : Relic
        {
            public override string Name => "Silver Crucible";
            public override string Describe() => "A relic of the checks.";
        }

        public sealed class PhialHolster : Relic
        {
            public override string Name => "Phial Holster";
            public override string Describe() => "A relic of the checks.";
        }

        public sealed class SilkenTress : Relic
        {
            public override string Name => "Silken Tress";
            public override string Describe() => "A relic of the checks.";
        }

        public sealed class LavaRock : Relic
        {
            public override string Name => "Lava Rock";
            public override string Describe() => "A relic of the checks.";
        }

        /// <summary>Any other relic of Neow's that is not built, for an offer read from a record.</summary>
        public sealed class Other : Relic
        {
            public override string Name => "(a relic of a later chunk)";
            public override string Describe() => "A relic of the checks.";
        }

        public static readonly IReadOnlyDictionary<string, Func<Relic>> Make = new Dictionary<string, Func<Relic>>(StringComparer.Ordinal)
        {
            ["SilverCrucible"] = static () => new SilverCrucible(), ["PhialHolster"] = static () => new PhialHolster(),
            ["SilkenTress"] = static () => new SilkenTress(), ["LavaRock"] = static () => new LavaRock(),
        };
    }

    /// <summary>Neow herself, on her own stream, making the game's own offer whatever is built: this chunk's 14 relics are the
    /// registry's, every other one is a stand-in of <see cref="NotBuiltYet"/>.</summary>
    private sealed class NeowOfTheRecords : Neow
    {
        public override string ClassName => "Neow";
        protected override bool IsBuilt(string relicClassName) => true;
        protected override Relic NewRelic(string relicClassName) =>
            NeowCore.All.Contains(relicClassName) ? Content.Registry.NewRelic(relicClassName)
            : NotBuiltYet.Make.TryGetValue(relicClassName, out var make) ? make() : new NotBuiltYet.Other();
    }

    /// <summary>An Ancient of the checks that offers all 14 relics at once, each as an Ancient's relic option.</summary>
    private sealed class Shelf : AncientEvent
    {
        public override string Title => "Shelf";
        protected override string InitialDescription(Run run) => "Every relic of the chunk.";
        protected override IReadOnlyList<EventOption> GenerateInitialOptions(Run run) => NeowCore.All.Select(RelicOption).ToList();
    }

    /// <summary>
    /// The start of an act as the game lays it out: the world of the seed, the map, the start
    /// point entered at once, the Ancient's room; its Leave opens the map. Nothing travels on.
    /// </summary>
    private sealed class ActStart : RunDirector, IMapDirector
    {
        /// <summary>"registry": the act's own Ancient by name, as the act's director opens it. "records": <see cref="NeowOfTheRecords"/>. "shelf": <see cref="Shelf"/>.</summary>
        public string Ancient { get; init; } = "registry";
        public bool Strict { get; init; }
        public bool ForceOvergrowth { get; init; }

        public override void Begin(Run run)
        {
            var world = RunWorld.Begin(run.State, acts: ForceOvergrowth ? OvergrowthActs : null);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            var map = MapState.BeginAct(run, 0);
            var arrival = map.Enter(run, map.Map.Start.Coord);
            switch (Ancient)
            {
                case "registry": EventScreen.Open(run, world.Rooms[0].Ancient!); break;
                case "records": EventScreen.Open(run, new NeowOfTheRecords()); break;
                default: EventScreen.Open(run, new Shelf()); break;
            }
            MapState.MarkRoomVisited(run, arrival.Room);
        }

        public void EnterRoom(Run run, MapArrival arrival) => throw new NotSupportedException("These checks do not travel on.");

        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);

        public override void AfterRoom(Run run)
        {
            run.CloseAll();
            run.Push(new MapScreen());
        }
    }

    /// <summary>
    /// Answers whatever a pickup put up until the room's own page is back: on a grid the first
    /// card that is not picked yet, then the preview's confirm; on a rewards screen every row
    /// from the top; of a card reward the first card.
    /// </summary>
    private static void Settle(Run run)
    {
        for (var step = 0; step < 40; step++)
        {
            var s = run.Snapshot();
            var id = s.Screen switch
            {
                "card_select" => s.Actions.Any(action => action.Id == "confirm")
                    ? "confirm"
                    : s.Actions.First(action => action.Id.StartsWith("card:", StringComparison.Ordinal) && !action.Label.StartsWith("Deselect", StringComparison.Ordinal)).Id,
                "rewards" => s.Actions.FirstOrDefault(action => action.Id.StartsWith("reward:", StringComparison.Ordinal))?.Id ?? "proceed",
                "card_reward" => "card:0",
                _ => null,
            };
            if (id is null) return;
            Do(run, id);
        }
        throw new InvalidOperationException($"The pickup's screens did not close ({run.Snapshot().Screen}).");
    }

    // ====================================================================================
    // the table
    // ====================================================================================

    private static void Table(Check c)
    {
        c.Group("the 14 relics carry the game's titles and are Neow's: seven of her fixed list, three that a coin adds, four cursed", () =>
        {
            var registry = Content.Registry;
            c.Equal(14, Expected.Length, "14 relics");
            c.Sequence(Expected.Select(e => e.ClassName), NeowCore.All, "the module names them in the plan's order");
            foreach (var e in Expected)
            {
                c.That(registry.Has(ContentKind.Relic, e.ClassName), $"{e.ClassName} is registered");
                if (!registry.Has(ContentKind.Relic, e.ClassName)) continue;
                var relic = registry.NewRelic(e.ClassName);
                c.Equal(e.Title, relic.Name, $"{e.ClassName}: the game's title");
                c.Equal(e.Text, relic.Describe(), $"{e.ClassName}: the text, with its numbers");
                c.Equal("NeowCore", registry.SourceOf(ContentKind.Relic, e.ClassName), $"{e.ClassName} is this module's");
                c.Equal((RelicSource.Neow, 14), (ReachableRelics.Find(e.ClassName)?.Source, ReachableRelics.Find(e.ClassName)?.Chunk ?? 0), $"{e.ClassName}: on the list of reachable relics as Neow's, chunk 14's");
                c.That(!GameLists.IsBagRelic(e.ClassName), $"{e.ClassName} is in no relic bag: only Neow gives it");
                c.That(RunRelics.IsBuilt(e.ClassName), $"{e.ClassName} counts as built");
            }

            // Neow.cs: the fourteen of the positive list, the three coins, the eight cursed.
            c.Sequence(["ArcaneScroll", "BoomingConch", "GoldenPearl", "LostCoffer", "NeowsTorment", "NewLeaf", "PreciseScissors"], NeowRelics.Positive.Where(NeowCore.All.Contains),
                "seven are always in Neow's positive list");
            c.Sequence(["NutritiousOyster", "NeowsTalisman", "Pomander"], NeowRelics.Coins.SelectMany(pair => new[] { pair.Heads, pair.Tails }).Where(NeowCore.All.Contains),
                "three come by a coin: the second coin's heads, and both sides of the third");
            c.Sequence(["CursedPearl", "HeftyTablet", "LeafyPoultice", "PrecariousShears"], NeowRelics.Cursed.Where(NeowCore.All.Contains), "four are cursed");
            c.Sequence(["GoldenPearl", "ArcaneScroll", "NewLeaf", "PreciseScissors"], NeowCore.Cursed.Select(cursed => NeowRelics.Twins[cursed]),
                "and each cursed one's plain twin, which it takes out of the offer, is of this file too");
            c.Equal((10, 4), (NeowCore.Positive.Count, NeowCore.Cursed.Count), "10 positive, 4 cursed");
            c.Sequence(NeowCore.All.Order(StringComparer.Ordinal), ReachableRelics.All.Where(relic => relic.Chunk == 14).Select(relic => relic.ClassName), "they are the plan's chunk 14, all of it");

            // Which tooltips show a card (the game's ExtraHoverTips with a card in them).
            c.Sequence(["NeowsTorment:NeowsFury", "CursedPearl:Greed", "HeftyTablet:Injury"],
                NeowCore.All.Select(name => (name, Relic: registry.NewRelic(name))).Where(r => r.Relic is ICardTips).Select(r => $"{r.name}:{string.Join("+", ((ICardTips)r.Relic).CardTips)}"),
                "three show a card in their tooltip: Neow's Fury, Greed, Injury");
        });

        c.Group("the Ironclad's pool as typed in here is the game's: 80 cards a single-player run can roll, 25 of them Rare", () =>
        {
            c.Equal(80, Pool.Length, "87 less 3 Basic, 2 Ancient, 2 multiplayer only");
            c.Equal((20, 35, 25), (Pool.Count(card => card.Rarity == CardRarity.Common), Pool.Count(card => card.Rarity == CardRarity.Uncommon), Rares.Length), "20 Common, 35 Uncommon, 25 Rare");
            c.Equal(("Aggression", "Barricade", "Unmovable"), (Rares[0], Rares[1], Rares[^1]), "the Rares start Aggression, Barricade and end with Unmovable");
            c.That(Pool.All(card => CardPools.IsBuilt(card.ClassName)), "every one of them is built, so no pick among them is ever drawn again");
            // The list the arena's own pool object gives (made by another reader's script from the same game files).
            c.Sequence(Pool.Select(card => card.ClassName),
                CardPools.Ironclad.Unlocked(RunUnlocks.All).Where(card => card.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare).Select(card => card.ClassName),
                "and it is the list the arena's pool object gives");
        });
    }

    // ====================================================================================
    // pickups that change a number
    // ====================================================================================

    private static void GoldAndMaxHp(Check c)
    {
        c.Group("Golden Pearl: 150 gold at pickup, through the gold hooks", () =>
        {
            var run = Desk(With());
            Take(run, "GoldenPearl");
            c.Equal(249, run.State.Gold, "99 + 150 (the game's record of seed AG5XPLQQNY has gold_gained 150 at Neow; the second player of seed FS5LGNDZZ7 stood at 249 gold)");
            c.Equal("gold is 249", Heard(run), "the relics heard 'gold gained' once, after it was paid");
            c.Sequence(["BurningBlood", "Witness", "GoldenPearl"], run.State.Relics.Select(relic => relic.ClassName), "the relic is the player's last");
            c.Equal(Starter, DeckNames(run), "the deck is as it was");
        });

        c.Group("Nutritious Oyster: 11 max HP at pickup, which heals as much", () =>
        {
            var run = Desk(With());
            Take(run, "NutritiousOyster");
            c.Equal((91, 91), (run.State.Hp, run.State.MaxHp), "from 80 of 80");
            run = Desk(With(hp: 50));
            Take(run, "NutritiousOyster");
            c.Equal((61, 91), (run.State.Hp, run.State.MaxHp), "from 50 of 80: the gain heals 11 and no more");
            c.Equal("", Heard(run), "no HP was lost and no gold gained");
        });
    }

    private static void CardsThatAreNamed(Check c)
    {
        c.Group("Neow's Torment: the card Neow's Fury goes to the end of the deck", () =>
        {
            var run = Desk(With());
            Take(run, "NeowsTorment");
            c.Equal(Starter + " NeowsFury", DeckNames(run), "the eleventh card, not upgraded");
            c.Equal("NeowsFury in from None, 11 in the deck, 99 gold", Heard(run), "a plain add: heard once, with no old pile");
            c.Equal("Neow's Fury", run.DescribeDeck()["deck"]![10]!["name"]!.GetValue<string>(), "get_deck lists it last");
            c.Equal(0, Counters(run).Rewards + Counters(run).Niche + Counters(run).Transformations, "nothing is rolled");
        });

        c.Group("Cursed Pearl: the curse Greed first, then 333 gold", () =>
        {
            var run = Desk(With());
            Take(run, "CursedPearl");
            c.Equal(Starter + " Greed", DeckNames(run), "Greed is the eleventh card");
            c.Equal(432, run.State.Gold, "99 + 333");
            c.Equal("Greed in from None, 11 in the deck, 99 gold; gold is 432", Heard(run), "the curse arrived while the gold was still 99; then the gold was gained");
            c.That(!DeckEdits.IsRemovable(run.State.Deck[^1]), "Greed is Eternal: no removal and no transform lists it");
        });
    }

    private static void Talisman(Check c)
    {
        c.Group("Neow's Talisman: the last starter Strike and the last starter Defend of the deck are upgraded", () =>
        {
            string After(params string[] deck)
            {
                var run = Desk(With(deck));
                Take(run, "NeowsTalisman");
                return DeckNames(run);
            }

            var run = Desk(With());
            Take(run, "NeowsTalisman");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad+ DefendIronclad DefendIronclad DefendIronclad DefendIronclad+ Bash", DeckNames(run),
                "the starter: the fifth Strike and the fourth Defend");
            c.Equal("", Heard(run), "an upgrade is heard by nobody");
            c.Equal(run.Snapshot().Screen, Desk(With()).Snapshot().Screen, "and nothing is asked");

            c.Equal("StrikeIronclad StrikeIronclad+ DefendIronclad DefendIronclad+", After("StrikeIronclad", "StrikeIronclad+", "DefendIronclad", "DefendIronclad+"),
                "the last Strike and the last Defend are upgraded already: nothing happens, and no earlier card is taken in their place");
            c.Equal("StrikeIronclad+ StrikeIronclad+ DefendIronclad+ DefendIronclad+", After("StrikeIronclad+", "StrikeIronclad", "DefendIronclad+", "DefendIronclad"),
                "the last of each is upgraded though the one before it already was");
            c.Equal("StrikeIronclad+ DefendIronclad+ TwinStrike UltimateDefend PerfectedStrike", After("StrikeIronclad", "DefendIronclad", "TwinStrike", "UltimateDefend", "PerfectedStrike"),
                "a Strike or Defend that is no starter card (Twin Strike, Perfected Strike, Ultimate Defend) does not count, wherever it stands");
            c.Equal("StrikeIronclad+ Bash", After("StrikeIronclad", "Bash"), "no starter Defend: the Strike alone; Bash is a starter card and neither a Strike nor a Defend");
            c.Equal("Bash Barricade", After("Bash", "Barricade"), "neither: nothing happens");
            c.Equal("DefendIronclad StrikeIronclad DefendIronclad+ StrikeIronclad+", After("DefendIronclad", "StrikeIronclad", "DefendIronclad", "StrikeIronclad"), "the last of each by deck order, whatever lies between");
        });
    }

    // ====================================================================================
    // Arcane Scroll, Hefty Tablet, Lost Coffer: cards rolled on the rewards stream
    // ====================================================================================

    private static void ArcaneScrollsCard(Check c)
    {
        c.Group("Arcane Scroll on the real seed: Barricade, from one number of rewards", () =>
        {
            c.Equal(1, Fresh(RealSeed, RunStream.Rewards).NextInt(0, 25), "the first number of the seed's rewards stream, as a pick among 25, is 1: the second Rare of the pool, Barricade");
            var run = Desk(With());
            Take(run, "ArcaneScroll");
            c.Equal(Starter + " Barricade", DeckNames(run), "Barricade is the eleventh card, as the game's save and record of this run have it");
            c.Equal((1, 0, 0), Counters(run), "one number of rewards: no rarity roll, no upgrade roll");
            c.Equal(RunState.StartingCardRarityOffset, run.State.CardRarityOffset, "the pity offset of card rewards has not moved");
            c.Equal("1 rolled for Other on the Uniform table (NoUpgradeRoll); Barricade in from None, 11 in the deck, 99 gold", Heard(run),
                "the card is rolled as an offer of one that is no fight's (the offer hooks are asked), then added like any card");
            c.Equal("event", run.Snapshot().Screen, "and nothing was asked");
        });

        c.Group("Arcane Scroll on 300 seeds: the Rare a second stream of the seed picks by the rule in words", () =>
        {
            var wrong = new List<string>();
            var met = new HashSet<string>(StringComparer.Ordinal);
            foreach (var seed in OvergrowthSeeds(300, 14_001))
            {
                var expected = Rares[Fresh(seed, RunStream.Rewards).NextInt(0, Rares.Length)];
                var run = Desk(With(seed: seed, witness: false));
                Take(run, "ArcaneScroll");
                var card = run.State.Deck[^1];
                met.Add(card.Def.ClassName);
                if (card.Def.ClassName != expected || card.IsUpgraded || run.State.Deck.Count != 11 || Counters(run) != (1, 0, 0))
                    wrong.Add($"{seed}: {card.Def.ClassName}{(card.IsUpgraded ? "+" : "")} for {expected}, rewards at {Counters(run).Rewards}");
            }
            c.Equal("", string.Join(" | ", wrong.Take(5)), $"one pick among the 25 Rares in the pool's order, never upgraded ({wrong.Count} seeds differ)");
            c.Equal(25, met.Count, "and every one of the 25 came up on some seed");
        });
    }

    /// <summary>Hefty Tablet's three Rares by the rule in words: each a pick among the Rares not offered yet.</summary>
    private static List<string> ThreeRares(GameRng rewards)
    {
        var left = Rares.ToList();
        var offered = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var pick = left[rewards.NextInt(0, left.Count)];
            offered.Add(pick);
            left.Remove(pick);
        }
        return offered;
    }

    private static string TitleOf(string cardClassName) => Content.Registry.Card(cardClassName).Name;

    private static void HeftyTabletsChoice(Check c)
    {
        var onTheSeed = ThreeRares(Fresh(RealSeed, RunStream.Rewards));

        c.Group("Hefty Tablet: three different Rares to choose from, and the curse Injury whatever is chosen", () =>
        {
            c.Equal("Barricade", onTheSeed[0], "on the real seed the first of the three is the card Arcane Scroll gave: the same first pick among the same 25");
            var run = Desk(With());
            Take(run, "HeftyTablet");
            var s = run.Snapshot();
            c.Equal(("card_select", "choose"), (s.Screen, Kind(s)), "the choose-a-card screen");
            c.Equal((0, 1), MinMax(s), "none or one");
            c.That(s.State["can_skip"]!.GetValue<bool>(), "it can be skipped");
            c.Sequence(onTheSeed.Select(TitleOf), s.State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "the three cards, in the order they were rolled");
            c.That(s.State["cards"]!.AsArray().All(card => card!["rarity"]!.GetValue<string>() == "Rare"), "all Rare");
            c.Equal($"card:0=Take {TitleOf(onTheSeed[0])} | card:1=Take {TitleOf(onTheSeed[1])} | card:2=Take {TitleOf(onTheSeed[2])} | skip=Skip", Labels(s), "one action each, and Skip");
            c.Equal(Starter, DeckNames(run), "the action waits for the answer: nothing is in the deck yet");
            c.That(run.State.HasRelic("HeftyTablet"), "the relic is the player's already: it is obtained first, then its pickup asks");

            Do(run, "card:1");
            c.Equal($"{Starter} {onTheSeed[1]} Injury", DeckNames(run), "the chosen card, then Injury");
            c.Equal((3, 0, 0), Counters(run), "three numbers of rewards: one pick a card, no rarity roll, no upgrade roll");
            c.Equal($"3 rolled for Other on the Uniform table (NoUpgradeRoll); {onTheSeed[1]} in from None, 12 in the deck, 99 gold; Injury in from None, 12 in the deck, 99 gold", Heard(run),
                "the three are one offer that is no fight's; then ONE add for both cards: each was heard only when both were in the deck");
            c.That(run.State.HasRelic("HeftyTablet"), "the relic is the player's");
            c.Equal("event", run.Snapshot().Screen, "the choice is closed");
        });

        c.Group("Hefty Tablet skipped: Injury alone", () =>
        {
            var run = Desk(With());
            Take(run, "HeftyTablet");
            Do(run, "skip");
            c.Equal(Starter + " Injury", DeckNames(run), "only the curse");
            c.Equal((3, 0, 0), Counters(run), "the three cards were rolled all the same");
            c.Equal("3 rolled for Other on the Uniform table (NoUpgradeRoll); Injury in from None, 11 in the deck, 99 gold", Heard(run), "one card arrived");
            c.That(run.State.HasRelic("HeftyTablet"), "the relic is the player's");
        });

        c.Group("Hefty Tablet on 200 seeds: the three Rares a second stream of the seed picks", () =>
        {
            var wrong = new List<string>();
            foreach (var seed in OvergrowthSeeds(200, 14_002))
            {
                var expected = ThreeRares(Fresh(seed, RunStream.Rewards));
                var run = Desk(With(seed: seed, witness: false));
                Take(run, "HeftyTablet");
                var shown = run.Snapshot().State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()).ToList();
                if (!shown.SequenceEqual(expected.Select(TitleOf)) || shown.Distinct().Count() != 3) wrong.Add($"{seed}: {string.Join(", ", shown)} for {string.Join(", ", expected)}");
            }
            c.Equal("", string.Join(" | ", wrong.Take(5)), $"each a pick among the Rares the offer does not show yet ({wrong.Count} seeds differ)");
        });
    }

    /// <summary>Lost Coffer's three cards by the rule in words: for each a rarity on the regular table with no offset (Rare below
    /// 0.03, Uncommon below 0.37, else Common), a pick among the cards of that rarity not offered yet, and an upgrade roll.</summary>
    private static List<string> CofferCards(GameRng rewards)
    {
        var offered = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var roll = rewards.NextFloat();
            var rarity = roll < 0.03f ? CardRarity.Rare : roll < 0.37f ? CardRarity.Uncommon : CardRarity.Common;
            var options = Pool.Where(card => card.Rarity == rarity && !offered.Contains(card.ClassName)).ToList();
            offered.Add(options[rewards.NextInt(0, options.Count)].ClassName);
            rewards.NextFloat();
        }
        return offered;
    }

    /// <summary>The potion after them: a rarity (Rare at or below 0.1, Uncommon at or below 0.35, else Common) and a pick among the
    /// potions of that rarity in the game's list; both again while the pick lands on a potion the arena leaves out.</summary>
    private static string CofferPotion(Run run, GameRng rewards)
    {
        var list = PotionFactory.GetPotionOptions(run);
        while (true)
        {
            var roll = rewards.NextFloat();
            var rarity = roll <= 0.1f ? PotionRarity.Rare : roll <= 0.35f ? PotionRarity.Uncommon : PotionRarity.Common;
            var ofRarity = list.Where(potion => potion.Rarity == rarity).ToList();
            var landed = ofRarity[rewards.NextInt(0, ofRarity.Count)];
            if (Content.Registry.Has(ContentKind.Potion, landed.ClassName)) return landed.ClassName;
        }
    }

    private static List<string> Rows(GameSnapshot rewards) =>
        rewards.State["rewards"]!.AsArray().Select(row => row!["kind"]!.GetValue<string>() switch
        {
            "potion" => $"potion {row["name"]}",
            "card" => $"card x{row["count"]}",
            var other => other,
        }).ToList();

    private static void LostCoffersRewards(Check c)
    {
        c.Group("Lost Coffer: a rewards screen with a random potion and a choice of three cards, on eleven numbers of rewards", () =>
        {
            var second = Fresh(RealSeed, RunStream.Rewards);
            var cards = CofferCards(second);
            c.Equal(9, second.Counter, "three cards take nine numbers: a rarity, a pick and an upgrade roll each");
            var run = Desk(With());
            var potion = CofferPotion(run, second);
            Take(run, "LostCoffer");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "a rewards screen is up");
            c.Sequence([$"potion {Content.Potion(potion).Name}", "card x3"], Rows(s), "the potion's row first, then the card reward: the rows are sorted, though the cards were rolled first");
            c.Equal(second.Counter, Counters(run).Rewards, $"the stream stands where the second reading stands ({second.Counter}: eleven unless a potion roll landed on a potion that is left out)");
            c.Equal((RunState.StartingCardRarityOffset, RunState.StartingPotionChance), (run.State.CardRarityOffset, run.State.PotionChance),
                "neither the pity offset nor the potion chance has moved: this is no fight's reward");
            c.That(run.State.HasRelic("LostCoffer"), "the relic is the player's while the screen is up");
            c.Equal("3 rolled for Other on the Regular table (IsCardReward)", Heard(run), "the cards are a card reward's that is no fight's, on the regular table, with an upgrade roll");
            c.That(s.Actions.Any(action => action.Id == "proceed"), "the screen can be left with rows unclaimed");

            Do(run, "reward:1");
            s = run.Snapshot();
            c.Equal("card_reward", s.Screen, "the card row opens the card reward");
            c.Sequence(cards.Select(TitleOf), s.State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "the three cards of the second reading, in the order rolled");
            Do(run, "card:2");
            c.Equal($"{Starter} {cards[2]}", DeckNames(run), "the card taken is the deck's last, not upgraded");
            c.Equal("rewards", run.Snapshot().Screen, "the potion row is still there");
            Do(run, "reward:0");
            c.Sequence([potion, null, null], run.State.Belt.Slots.Select(held => held?.ClassName), "the potion goes into the first slot");
            c.Equal("event", run.Snapshot().Screen, "with its last row taken the rewards screen is gone");
            c.Equal(second.Counter, Counters(run).Rewards, "taking draws nothing");
        });

        c.Group("Lost Coffer on 200 seeds: the cards and the potion a second stream of the seed gives", () =>
        {
            var wrong = new List<string>();
            var rarities = new Dictionary<CardRarity, int>();
            foreach (var seed in OvergrowthSeeds(200, 14_003))
            {
                var second = Fresh(seed, RunStream.Rewards);
                var cards = CofferCards(second);
                var run = Desk(With(seed: seed, witness: false));
                var potion = CofferPotion(run, second);
                Take(run, "LostCoffer");
                var rows = Rows(run.Snapshot());
                var counter = Counters(run).Rewards;
                Do(run, $"reward:{rows.Count - 1}");
                var shown = run.Snapshot().State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()).ToList();
                foreach (var card in cards) rarities[Pool.First(entry => entry.ClassName == card).Rarity] = rarities.GetValueOrDefault(Pool.First(entry => entry.ClassName == card).Rarity) + 1;
                if (!shown.SequenceEqual(cards.Select(TitleOf)) || rows[0] != $"potion {Content.Potion(potion).Name}" || counter != second.Counter || run.State.CardRarityOffset != RunState.StartingCardRarityOffset)
                    wrong.Add($"{seed}: {string.Join(", ", shown)} and {rows[0]} for {string.Join(", ", cards)} and {potion}; rewards at {counter} for {second.Counter}");
            }
            c.Equal("", string.Join(" | ", wrong.Take(4)), $"the regular table without the pity offset, three different cards, then the potion ({wrong.Count} seeds differ)");
            // 600 cards at 3%, 34% and 63%: 18, 204 and 378 on average.
            c.That(rarities.GetValueOrDefault(CardRarity.Rare) is >= 4 and <= 38, $"about 3% of the 600 cards are Rare ({rarities.GetValueOrDefault(CardRarity.Rare)})");
            c.That(rarities.GetValueOrDefault(CardRarity.Uncommon) is >= 160 and <= 250, $"about 34% Uncommon ({rarities.GetValueOrDefault(CardRarity.Uncommon)})");
            c.That(rarities.GetValueOrDefault(CardRarity.Common) is >= 330 and <= 425, $"about 63% Common ({rarities.GetValueOrDefault(CardRarity.Common)})");
        });

        c.Group("Lost Coffer's rows may be left: proceed closes the screen with nothing taken", () =>
        {
            var run = Desk(With());
            Take(run, "LostCoffer");
            Do(run, "proceed");
            c.Equal("event", run.Snapshot().Screen, "back at the desk");
            c.Equal(Starter, DeckNames(run), "no card");
            c.Sequence([null, null, null], run.State.Belt.Slots.Select(held => held?.ClassName), "no potion");
        });
    }

    // ====================================================================================
    // choices from the deck
    // ====================================================================================

    private static void Scissors(Check c)
    {
        c.Group("Precise Scissors: one card of the player's choice leaves the deck", () =>
        {
            var run = Desk(With());
            Take(run, "PreciseScissors");
            var s = run.Snapshot();
            c.Equal(("card_select", "remove"), (s.Screen, Kind(s)), "the removal grid");
            c.Equal((1, 1), MinMax(s), "exactly one");
            c.Equal("0:Strike x5, 5:Defend x4, 9:Bash", Listed(s), "the whole deck, equal cards listed once");
            c.Equal("card:0=Pick Strike to remove | card:5=Pick Defend to remove | card:9=Pick Bash to remove", Labels(s), "a pick each, and no way to close the screen: this choice cannot be backed out of");
            c.That(run.State.HasRelic("PreciseScissors") && run.State.Deck.Count == 10, "the relic is the player's already (it is obtained first, then its pickup asks); the deck waits for the answer");

            Do(run, "card:9");
            c.Equal("confirm=Confirm: remove Bash | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "the pick opens the preview");
            Do(run, "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad", DeckNames(run), "Bash is gone");
            c.Equal("Bash out, 10 in the deck, 80 HP", Heard(run), "removed as the game removes a card: heard while it was still in the deck");
            c.That(run.State.HasRelic("PreciseScissors"), "the relic is the player's");
            c.Equal((0, 0, 0), Counters(run), "nothing is rolled");
        });

        c.Group("Precise Scissors lists curses first and never an Eternal card", () =>
        {
            var run = Desk(With([.. Starter.Split(' '), "Greed", "Injury"]));
            Take(run, "PreciseScissors");
            c.Equal("0:Injury, 1:Strike x5, 6:Defend x4, 10:Bash", Listed(run.Snapshot()), "Injury leads the grid; Greed is Eternal and is not listed");
            Do(run, "card:0", "confirm");
            c.Equal(Starter + " Greed", DeckNames(run), "Injury is removed");
        });

        c.Group("Precise Scissors with a single card that can go: it goes without a screen", () =>
        {
            var run = Desk(With(["Bash"]));
            Take(run, "PreciseScissors");
            c.Equal(("event", ""), (run.Snapshot().Screen, DeckNames(run)), "the only card is removed; nothing was asked");

            run = Desk(With(["Greed", "Bash"]));
            Take(run, "PreciseScissors");
            c.Equal(("event", "Greed"), (run.Snapshot().Screen, DeckNames(run)), "beside an Eternal card the other one is the single candidate");

            run = Desk(With(["Greed"]));
            Take(run, "PreciseScissors");
            c.Equal(("event", "Greed"), (run.Snapshot().Screen, DeckNames(run)), "with nothing that can go nothing happens");
            c.That(run.State.HasRelic("PreciseScissors"), "and the relic is taken all the same");
        });
    }

    private static void Shears(Check c)
    {
        c.Group("Precarious Shears: two cards of the player's choice leave the deck, then 16 damage", () =>
        {
            var run = Desk(With());
            Take(run, "PrecariousShears");
            var s = run.Snapshot();
            c.Equal(("card_select", "remove"), (s.Screen, Kind(s)), "the removal grid");
            c.Equal((2, 2), MinMax(s), "exactly two");
            Do(run, "card:5");
            s = run.Snapshot();
            c.Equal("0:Strike x5, 5:Defend (selected), 6:Defend x3, 9:Bash", Listed(s), "one picked: the grid stays up");
            c.That(s.Actions.All(action => action.Id != "confirm"), "no confirm before both are picked");
            Do(run, "card:0");
            c.Equal("confirm=Confirm: remove Defend, Strike | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "the second pick opens the preview, the cards in pick order");
            c.Equal(80, run.State.Hp, "no damage before the choice is made");
            Do(run, "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash", DeckNames(run), "a Defend and a Strike are gone");
            c.Equal((64, 80), (run.State.Hp, run.State.MaxHp), "80 - 16; max HP as it was");
            c.Equal("DefendIronclad out, 10 in the deck, 80 HP; StrikeIronclad out, 9 in the deck, 80 HP; losing 16 HP (Unpowered) at 80 of 80", Heard(run),
                "the cards go one by one in pick order, both before the damage; the damage is unpowered damage, not an HP loss that ignores Block");
            c.That(run.State.HasRelic("PrecariousShears") && !run.IsOver, "the relic is the player's");
        });

        c.Group("Precarious Shears' damage is damage: Tungsten Rod takes 1 off, and it can kill", () =>
        {
            var run = Desk(With(relics: ["BurningBlood", "TungstenRod"]));
            Take(run, "PrecariousShears");
            Do(run, "card:0", "card:1", "confirm");
            c.Equal(65, run.State.Hp, "with Tungsten Rod 15 are lost");

            run = Desk(With(hp: 17));
            Take(run, "PrecariousShears");
            Do(run, "card:0", "card:1", "confirm");
            c.That(run.State.Hp == 1 && !run.IsOver, "at 17 HP the player is left with 1");

            run = Desk(With(hp: 16));
            Take(run, "PrecariousShears");
            Do(run, "card:0", "card:1", "confirm");
            c.That(run.IsOver && run.Result == RunResult.Died, "at 16 HP the player dies of it: the run is over");
            c.Equal("game_over", run.Snapshot().Screen, "with the game over screen up");
            c.Equal(8, run.State.Deck.Count, "the two cards went first");
        });

        c.Group("Precarious Shears with no more than two cards that can go: they go without a screen", () =>
        {
            var run = Desk(With(["Bash", "Greed", "StrikeIronclad"]));
            Take(run, "PrecariousShears");
            c.Equal(("event", "Greed"), (run.Snapshot().Screen, DeckNames(run)), "both candidates are removed; nothing was asked");
            c.Equal(64, run.State.Hp, "and the 16 damage is dealt");

            run = Desk(With(["Greed"]));
            Take(run, "PrecariousShears");
            c.Equal(("Greed", 64), (DeckNames(run), run.State.Hp), "with nothing to remove the damage is dealt all the same");
        });
    }

    private static void PomandersUpgrade(Check c)
    {
        c.Group("Pomander: one card of the player's choice is upgraded (the real run on seed 75F277YN2G upgraded Bash)", () =>
        {
            var run = Desk(With());
            Take(run, "Pomander");
            var s = run.Snapshot();
            c.Equal(("card_select", "upgrade"), (s.Screen, Kind(s)), "the upgrade grid");
            c.Equal((1, 1), MinMax(s), "exactly one");
            c.Equal("0:Strike x5, 5:Defend x4, 9:Bash", Listed(s), "every card that can be upgraded");
            c.Equal("card:0=Pick Strike to upgrade | card:5=Pick Defend to upgrade | card:9=Pick Bash to upgrade", Labels(s), "a pick each; no way to close the screen");
            Do(run, "card:9");
            s = run.Snapshot();
            c.Sequence(["Bash", "Bash+"], s.State["preview"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "the preview shows the card and what it becomes");
            Do(run, "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash+", DeckNames(run),
                "Bash is upgraded where it stands, as the game's record has it (upgraded_cards: Bash)");
            c.That(run.State.HasRelic("Pomander"), "the relic is the player's");
            c.Equal("", Heard(run), "an upgrade is heard by nobody");
        });

        c.Group("Pomander lists only what can still be upgraded, and takes a single such card without a screen", () =>
        {
            var run = Desk(With(["StrikeIronclad+", "DefendIronclad", "Bash+", "Injury", "DefendIronclad"]));
            Take(run, "Pomander");
            c.Equal("0:Defend x2", Listed(run.Snapshot()), "the two Defends, numbered within the grid's own list; the upgraded cards and the curse are not listed");
            Do(run, "card:0", "confirm");
            c.Equal("StrikeIronclad+ DefendIronclad+ Bash+ Injury DefendIronclad", DeckNames(run), "the first of the two equal cards is the one picked");

            run = Desk(With(["StrikeIronclad+", "Bash", "Injury"]));
            Take(run, "Pomander");
            c.Equal(("event", "StrikeIronclad+ Bash+ Injury"), (run.Snapshot().Screen, DeckNames(run)), "one candidate: upgraded with nothing asked");

            run = Desk(With(["StrikeIronclad+", "Injury"]));
            Take(run, "Pomander");
            c.Equal(("event", "StrikeIronclad+ Injury"), (run.Snapshot().Screen, DeckNames(run)), "no candidate: nothing happens");
        });
    }

    /// <summary>What a transformed starter card becomes: one pick over the 80 (none of them is the card itself).</summary>
    private static string Transformed(GameRng rng) => Pool[rng.NextInt(0, Pool.Length)].ClassName;

    private static void NewLeafsTransform(Check c)
    {
        c.Group("New Leaf: one card of the player's choice becomes a random card, on the niche stream, at the end of the deck", () =>
        {
            var expected = Transformed(Fresh(RealSeed, RunStream.Niche));
            var run = Desk(With());
            Take(run, "NewLeaf");
            var s = run.Snapshot();
            c.Equal(("card_select", "transform"), (s.Screen, Kind(s)), "the transform grid");
            c.Equal((1, 1), MinMax(s), "exactly one");
            c.Equal("0:Strike x5, 5:Defend x4, 9:Bash", Listed(s), "the whole deck");
            Do(run, "card:0");
            c.Equal("confirm=Confirm: transform Strike | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "the pick opens the preview");
            Do(run, "confirm");
            c.Equal($"StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash {expected}", DeckNames(run),
                "the first Strike is gone and the new card is the deck's LAST: the first number of niche as a pick among the 80");
            c.Equal((0, 1, 0), Counters(run), "one number of niche; none of the player's transformations stream");
            c.Equal($"{expected} in from Deck, 10 in the deck, 99 gold", Heard(run), "a transform: the new card is heard with the deck as its old pile, and no removal is heard");
            c.That(run.State.HasRelic("NewLeaf"), "the relic is the player's");
        });

        c.Group("New Leaf never lists an Eternal card, and transforms a single candidate without a screen", () =>
        {
            var run = Desk(With([.. Starter.Split(' '), "Greed"]));
            Take(run, "NewLeaf");
            c.Equal("0:Strike x5, 5:Defend x4, 9:Bash", Listed(run.Snapshot()), "Greed is not listed");

            var expected = Transformed(Fresh(RealSeed, RunStream.Niche));
            run = Desk(With(["Greed", "Bash"]));
            Take(run, "NewLeaf");
            c.Equal(("event", $"Greed {expected}"), (run.Snapshot().Screen, DeckNames(run)), "Bash, the one candidate, is transformed with nothing asked");
        });

        c.Group("New Leaf on 200 seeds, and with keyed streams", () =>
        {
            var wrong = new List<string>();
            foreach (var seed in OvergrowthSeeds(200, 14_004))
            {
                var expected = Transformed(Fresh(seed, RunStream.Niche));
                var run = Desk(With(seed: seed, witness: false));
                Take(run, "NewLeaf");
                Do(run, "card:9", "confirm");
                if (run.State.Deck[^1].Def.ClassName != expected || run.State.Deck.Count != 10 || run.State.Deck.Any(card => card.Def.ClassName == "Bash") || Counters(run) != (0, 1, 0))
                    wrong.Add($"{seed}: {DeckNames(run)} for {expected}");
            }
            c.Equal("", string.Join(" | ", wrong.Take(4)), $"Bash becomes the card a second niche stream of the seed picks ({wrong.Count} seeds differ)");

            // Keyed streams (a house rule of run mode): the roll has a stream of the place and of its purpose, the relic's own.
            var keyed = Desk(With(), mode: StreamMode.Keyed);
            var place = Streams(keyed).Place;
            Take(keyed, "NewLeaf");
            Do(keyed, "card:9", "confirm");
            c.Equal(Transformed(new GameRng(RunStreams.KeyedSeed(new RunSeed(RealSeed), place, Purpose.Relic("NewLeaf", 0)))), keyed.State.Deck[^1].Def.ClassName,
                "in keyed mode the pick comes from the stream of the purpose relic:NewLeaf/0");
            c.Equal((0, 0, 0), Counters(keyed), "and no long stream moves");
        });
    }

    private static void Poultice(Check c)
    {
        c.Group("Leafy Poultice: 12 max HP lost, then the first starter Strike and the first starter Defend become random cards in one transform", () =>
        {
            var second = Fresh(RealSeed, RunStream.Transformations);
            var (forStrike, forDefend) = (Transformed(second), Transformed(second));
            var run = Desk(With());
            Take(run, "LeafyPoultice");
            c.Equal((68, 68), (run.State.Hp, run.State.MaxHp), "80 - 12 of both");
            c.Equal($"StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash {forStrike} {forDefend}", DeckNames(run),
                "the first Strike and the first Defend are gone; their new cards are the deck's last two, the Strike's first: two numbers of transformations as picks among the 80");
            c.Equal((0, 0, 2), Counters(run), "two numbers of the player's transformations stream; none of niche");
            c.Equal($"losing 12 HP (Unblockable, Unpowered) at 80 of 80; {forStrike} in from Deck, 9 in the deck, 99 gold; {forDefend} in from Deck, 10 in the deck, 99 gold", Heard(run),
                "the HP above the new maximum is lost as an HP loss before the maximum falls; then ONE transform: both originals were out before the first new card went in");
            c.Equal("event", run.Snapshot().Screen, "nothing is asked");
            c.That(run.State.HasRelic("LeafyPoultice"), "the relic is the player's");
        });

        c.Group("Leafy Poultice and HP: nothing is lost below the new maximum; Tungsten Rod changes nothing; a maximum of 12 or less kills", () =>
        {
            var run = Desk(With(hp: 50));
            Take(run, "LeafyPoultice");
            c.Equal((50, 68), (run.State.Hp, run.State.MaxHp), "50 of 80 becomes 50 of 68");
            c.That(!Heard(run).Contains("losing", StringComparison.Ordinal), "no HP loss was heard");

            run = Desk(With(hp: 70));
            Take(run, "LeafyPoultice");
            c.Equal((68, 68), (run.State.Hp, run.State.MaxHp), "70 of 80 loses 2");
            c.That(Heard(run).StartsWith("losing 2 HP (Unblockable, Unpowered) at 70 of 80", StringComparison.Ordinal), $"heard as a loss of 2 ({Heard(run)})");

            run = Desk(With(relics: ["BurningBlood", "TungstenRod"]));
            Take(run, "LeafyPoultice");
            c.Equal((68, 68), (run.State.Hp, run.State.MaxHp), "with Tungsten Rod the loss is 11, and the cut to the new maximum makes it 68 all the same");

            run = Desk(With(hp: 13, maxHp: 13));
            Take(run, "LeafyPoultice");
            c.Equal((1, 1), (run.State.Hp, run.State.MaxHp), "13 of 13: the maximum falls to 1 and 12 HP are lost");
            c.That(!run.IsOver && run.State.Deck.Count == 10 && Counters(run).Transformations == 2, "the player lives and the two cards are transformed");

            run = Desk(With(hp: 12, maxHp: 12));
            Take(run, "LeafyPoultice");
            c.That(run.IsOver && run.Result == RunResult.Died, "12 of 12: all 12 HP are lost and the player dies");
            c.Equal((Starter, 0), (DeckNames(run), Counters(run).Transformations), "nothing is transformed after a death");
        });

        c.Group("Leafy Poultice takes the FIRST starter Strike and Defend, and only what is there", () =>
        {
            var second = Fresh(RealSeed, RunStream.Transformations);
            var (first, next) = (Transformed(second), Transformed(second));

            // Here the first starter Defend (place 1) stands before the first starter Strike (place 3). The Strike is still
            // rolled first and leaves first; then the Defend. The new cards go in by the places their originals had: the
            // Defend's, then the Strike's.
            var run = Desk(With(["Bash", "DefendIronclad", "TwinStrike", "StrikeIronclad", "StrikeIronclad+", "DefendIronclad+"]));
            Take(run, "LeafyPoultice");
            c.Equal($"Bash TwinStrike StrikeIronclad+ DefendIronclad+ {next} {first}", DeckNames(run),
                "the first starter Strike (not Twin Strike) and the first starter Defend; the Strike's roll is the first number, and the Defend's new card goes in first because its original stood earlier");

            run = Desk(With(["DefendIronclad", "Bash"]));
            Take(run, "LeafyPoultice");
            c.Equal(($"Bash {first}", 1), (DeckNames(run), Counters(run).Transformations), "no starter Strike: the Defend alone, on one number");

            run = Desk(With(["Bash", "Barricade"]));
            Take(run, "LeafyPoultice");
            c.Equal(("Bash Barricade", 0, 68), (DeckNames(run), Counters(run).Transformations, run.State.MaxHp), "neither: only the max HP is lost");
        });

        c.Group("Leafy Poultice on 200 seeds, and with keyed streams", () =>
        {
            var wrong = new List<string>();
            foreach (var seed in OvergrowthSeeds(200, 14_005))
            {
                var second = Fresh(seed, RunStream.Transformations);
                var expected = $"StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash {Transformed(second)} {Transformed(second)}";
                var run = Desk(With(seed: seed, witness: false));
                Take(run, "LeafyPoultice");
                if (DeckNames(run) != expected || Counters(run) != (0, 0, 2)) wrong.Add($"{seed}: {DeckNames(run)} for {expected}");
            }
            c.Equal("", string.Join(" | ", wrong.Take(3)), $"the two cards a second transformations stream of the seed picks, the Strike's first ({wrong.Count} seeds differ)");

            var keyed = Desk(With(), mode: StreamMode.Keyed);
            var place = Streams(keyed).Place;
            Take(keyed, "LeafyPoultice");
            var seedOf = new RunSeed(RealSeed);
            c.Equal($"{Transformed(new GameRng(RunStreams.KeyedSeed(seedOf, place, Purpose.Transform(0))))} {Transformed(new GameRng(RunStreams.KeyedSeed(seedOf, place, Purpose.Transform(1))))}",
                string.Join(" ", keyed.State.Deck.TakeLast(2).Select(card => card.Def.ClassName)), "in keyed mode each roll has its own stream: transform/0 for the Strike, transform/1 for the Defend");
        });
    }

    // ====================================================================================
    // Booming Conch
    // ====================================================================================

    /// <summary>Writes down energy and hand size in "after player turn start": after the turn's draw, before "after side turn start".</summary>
    private sealed class TurnProbe : Relic
    {
        public string Seen = "";
        public override string Name => "Turn Probe";
        public override string Describe() => "A relic of the checks.";

        public override void AfterPlayerTurnStart(Fight f) =>
            Seen += string.Create(CultureInfo.InvariantCulture, $"{(Seen.Length == 0 ? "" : "; ")}turn {f.Turn}: {f.Hand.Count} cards, {f.Energy} energy");
    }

    /// <summary>Halves the turn-start draw in the LATE pass of the hand-draw modifiers, rounding down. (The game's one late
    /// listener, Fiddle, belongs to a later act's Ancient; this one is the checks' own, to tell the two passes apart.)</summary>
    private sealed class HalvesLate : Relic
    {
        public override string Name => "Halves Late";
        public override string Describe() => "A relic of the checks.";
        public override decimal ModifyHandDrawLate(Fight f, decimal count) => Math.Floor(count / 2);
    }

    private static Fight Fight(RoomType room, params string[] relics) =>
        new FightBuilder().DrawOrder([.. CheckKit.StarterInOrder, .. CheckKit.StarterInOrder]).Relics(["BurningBlood", .. relics]).Relic(new TurnProbe()).Room(room).Enemy(new CheckDummy()).Start();

    private static void Conch(Check c)
    {
        c.Group("Booming Conch: 2 more cards and 1 more energy on turn 1 of an Elite fight, and only there", () =>
        {
            (string Turn1, string Turn2, int MaxEnergy) In(RoomType room, params string[] relics)
            {
                var f = Fight(room, relics);
                var first = string.Create(CultureInfo.InvariantCulture, $"{f.Hand.Count} cards, {f.Energy} energy");
                var max = f.MaxEnergy;
                f.EndTurn();
                return (first, string.Create(CultureInfo.InvariantCulture, $"{f.Hand.Count} cards, {f.Energy} energy"), max);
            }

            c.Equal(("7 cards, 4 energy", "5 cards, 3 energy", 3), In(RoomType.Elite, "BoomingConch"), "an Elite: 7 cards and 4 energy on turn 1, the usual 5 and 3 on turn 2; the maximum stays 3");
            c.Equal(("5 cards, 3 energy", "5 cards, 3 energy", 3), In(RoomType.Monster, "BoomingConch"), "a hallway fight: nothing");
            c.Equal(("5 cards, 3 energy", "5 cards, 3 energy", 3), In(RoomType.Boss, "BoomingConch"), "a boss: nothing");
            c.Equal(("5 cards, 3 energy", "5 cards, 3 energy", 3), In(RoomType.Elite), "an Elite without it: nothing");
            c.Equal(("9 cards, 5 energy", "5 cards, 3 energy", 3), In(RoomType.Elite, "BoomingConch", "BagOfPreparation", "Lantern"), "with Bag of Preparation and Lantern in an Elite: 9 cards, 5 energy");

            // The energy is gained when the PLAYER's turn 1 starts, not again when the enemies' turn starts: with Ice Cream,
            // which keeps what is left, turn 2 has the 4 of turn 1 and its own 3.
            var kept = Fight(RoomType.Elite, "BoomingConch", "IceCream");
            kept.EndTurn();
            c.Equal(7, kept.Energy, "with Ice Cream and nothing played, turn 2 of an Elite has 4 + 3 energy");

            // The two cards are added in the FIRST pass of the hand-draw modifiers: a relic that halves the draw in the late
            // pass, though it was picked up before the conch, halves 7 and not 5.
            var halved = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(new HalvesLate()).Relic(new BoomingConch()).Room(RoomType.Elite).Enemy(new CheckDummy()).Start();
            c.Equal(3, halved.Hand.Count, "half of 5 + 2, rounded down");

            var f = Fight(RoomType.Elite, "BoomingConch");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Strike", "Strike"], f.HandTitles(), "the seven cards are the top seven of the draw pile");
            c.Equal("turn 1: 7 cards, 3 energy", f.Relics.OfType<TurnProbe>().Single().Seen,
                "the two cards come with the turn's own draw; the energy comes after it, in 'after side turn start': right after the draw it is still 3");
        });

        c.Group("Booming Conch over a run: an Elite, then a hallway fight", () =>
        {
            static FightRequest Dummy(RoomType room) => new()
            {
                Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 5 })], RoomType = room, ShuffleDeck = false,
            };
            var setup = new RunSetup { Relics = ["BurningBlood", "BoomingConch"] };
            var run = new Run(setup, new FixedFights(Dummy(RoomType.Elite), Dummy(RoomType.Monster), Dummy(RoomType.Elite)));
            c.Equal((7, 4), (run.Fight!.Hand.Count, run.Fight.Energy), "the Elite: 7 cards, 4 energy");
            Do(run, "play:0>e0");
            c.Equal((5, 3), (run.Fight!.Hand.Count, run.Fight.Energy), "the hallway fight after it: 5 cards, 3 energy");
            Do(run, "play:0>e0");
            c.Equal((7, 4), (run.Fight!.Hand.Count, run.Fight.Energy), "and again in the next Elite: the relic keeps nothing between rooms");
            c.Equal(run.Dump(), Run.Replay(setup, new FixedFights(Dummy(RoomType.Elite), Dummy(RoomType.Monster), Dummy(RoomType.Elite)), run.Actions).Dump(), "the run replays");
        });

        c.Group("Booming Conch has no pickup effect", () =>
        {
            var run = Desk(With());
            Take(run, "BoomingConch");
            c.Equal((80, 80, 99, Starter, ""), (run.State.Hp, run.State.MaxHp, run.State.Gold, DeckNames(run), Heard(run)), "nothing changes at pickup");
            c.Equal("event", run.Snapshot().Screen, "nothing is asked");
        });
    }

    // ====================================================================================
    // inside a fight
    // ====================================================================================

    private static void InsideAFight(Check c)
    {
        c.Group("obtained inside a fight: gold and max HP work there; a pickup that needs the run is refused by name", () =>
        {
            var f = CheckKit.VsDummy();
            var gold = f.Gold;
            f.ObtainRelic(new GoldenPearl());
            c.Equal(gold + 150, f.Gold, "Golden Pearl pays the fight's gold");
            f.SetHp(f.Player, 50);
            f.ObtainRelic(new NutritiousOyster());
            c.Equal((61, 91), (f.Player.Hp, f.Player.MaxHp), "Nutritious Oyster raises the fight's player");
            f.ObtainRelic(new BoomingConch());
            c.That(f.Relics.Any(relic => relic is BoomingConch), "Booming Conch has no pickup effect to refuse");

            string[] needTheRun = ["ArcaneScroll", "LostCoffer", "NeowsTorment", "NewLeaf", "PreciseScissors", "NeowsTalisman", "Pomander", "CursedPearl", "HeftyTablet", "LeafyPoultice", "PrecariousShears"];
            c.Sequence(NeowCore.All.Except(["GoldenPearl", "NutritiousOyster", "BoomingConch"]).Order(StringComparer.Ordinal), needTheRun.Order(StringComparer.Ordinal), "the other eleven need the run");
            foreach (var name in needTheRun)
            {
                var fight = CheckKit.VsDummy();
                var (hp, maxHp, held) = (fight.Player.Hp, fight.Player.MaxHp, fight.Gold);
                var message = "(nothing was thrown)";
                try
                {
                    fight.ObtainRelic(Content.Registry.NewRelic(name));
                }
                catch (NotSupportedException refused)
                {
                    message = refused.Message;
                }
                c.That(message.StartsWith("The arena leaves out ", StringComparison.Ordinal) && message.Contains(" inside a fight", StringComparison.Ordinal), $"{name}: refused by name ({message})");
                c.Equal((hp, maxHp, held), (fight.Player.Hp, fight.Player.MaxHp, fight.Gold), $"{name}: and nothing was done before the refusal");
            }
        });
    }

    // ====================================================================================
    // through Neow's room
    // ====================================================================================

    private const string NeowsText = "Neow offers three relics: two blessings, and a third that comes with a price. Take one.";

    private static void NeowOnTheRealSeed(Check c)
    {
        c.Group("the registry's Neow on the real seed: the three relics the game offered, all built, and the pick the real run made", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var run = new Run(setup, new ActStart());
            var s = run.Snapshot();
            c.Equal("event", s.Screen, "the run begins in Neow's room");
            c.Equal(Q("{'run':{'act':1,'floor':1,'ascension':0},'event':'Neow','ancient':true,'text':'" + NeowsText + "','options':["
                      + "{'n':0,'title':'Precise Scissors','text':'When picked up: choose 1 card to remove from your deck.'},"
                      + "{'n':1,'title':'Arcane Scroll','text':'When picked up: add 1 random Rare card to your deck.'},"
                      + "{'n':2,'title':'Leafy Poultice','text':'When picked up: lose 12 max HP. One of your starter Strikes and one of your starter Defends (the first of each in your deck) are each transformed into a random card.'}],")
                    + $"{Player()},{NoPotions}}}",
                Text(s.State), "the page as docs/screens.md has it, with the relics' own names and texts");
            c.Equal("option:0=Take Precise Scissors | option:1=Take Arcane Scroll | option:2=Take Leafy Poultice", Labels(s), "the labels name the relics");
            c.That(run.State.FindPart<LeftOutLog>() is null or { Entries.Count: 0 }, "nothing was passed over: the offer is the game's own");
            c.Equal(0, Counters(run).Rewards, "nothing on Neow's screen draws from rewards");

            Do(run, "option:1");
            c.Equal(Starter + " Barricade", DeckNames(run), "Arcane Scroll gives Barricade, as in the real run");
            c.Equal((1, 0, 0), Counters(run), "from one number of rewards, as the game's save after the pick has it");
            s = run.Snapshot();
            c.Equal(Q("{'run':{'act':1,'floor':1,'ascension':0},'event':'Neow','ancient':true,'finished':true,'options':[{'n':0,'title':'Proceed','leave':true}],")
                    + $"{Player(relics: "'Burning Blood','Arcane Scroll'", deck: "'deck_size':11,'deck':{'Barricade':1,'Bash':1,'Defend':4,'Strike':5}")},{NoPotions}}}",
                Text(s.State), "Neow's finished page, with the relic and the card in the player's block");
            c.Equal("""{"name":"Arcane Scroll","text":"When picked up: add 1 random Rare card to your deck."}""", Text(run.DescribeDeck()["relics"]![1]), "get_deck shows the relic with its text");
            Do(run, "option:0");
            s = run.Snapshot();
            c.Equal(("map", "(3,0) ancient"), (s.Screen, s.State["at"]!.GetValue<string>()), "Leave opens the map on the start point");
            c.Equal(1, run.State.Part<RunWorld>().Rooms[0].EventsVisited, "and Neow's room has moved the act's event counter to 1");
            c.Sequence(["option:1", "option:0"], run.Actions, "two actions");
            c.Equal(run.Dump(), Run.Replay(setup, new ActStart(), run.Actions).Dump(), "the run replays from them");
            c.Equal(run.Dump(), run.Clone().Dump(), "and a copy is in the same state");
        });

        c.Group("the same room under the strict switch: the offer is the game's, so the run goes on", () =>
        {
            var run = new Run(new RunSetup { Seed = RealSeed }, new ActStart { Strict = true });
            c.Sequence(["Precise Scissors", "Arcane Scroll", "Leafy Poultice"], Titles(run.Snapshot()), "the same three");
            Do(run, "option:1", "option:0");
            c.That(!run.IsOver && run.Snapshot().Screen == "map", "Arcane Scroll's pick lands on a built card: the strict run stands on the map");
            c.Equal(0, run.State.Part<LeftOutLog>().Entries.Count, "with nothing passed over");
        });

        c.Group("the real seed's other two options, through Neow's room", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var run = new Run(setup, new ActStart());
            Do(run, "option:0");
            var s = run.Snapshot();
            c.Equal(("card_select", "remove", "0:Strike x5, 5:Defend x4, 9:Bash"), (s.Screen, Kind(s), Listed(s)), "Precise Scissors asks which card to remove");
            c.Sequence(["BurningBlood", "PreciseScissors"], run.State.Relics.Select(relic => relic.ClassName), "the relic is the player's while its question is up");
            c.Equal(10, run.State.Deck.Count, "and no card is removed before the answer");
            Do(run, "card:5", "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash", DeckNames(run), "the first Defend is removed");
            c.Equal("option:0=Leave", Labels(run.Snapshot()), "and Neow is finished");
            Do(run, "option:0");
            c.Equal("map", run.Snapshot().Screen, "Leave opens the map");
            c.Equal(run.Dump(), Run.Replay(setup, new ActStart(), run.Actions).Dump(), "the run replays across the question");

            var second = Fresh(RealSeed, RunStream.Transformations);
            var expected = $"StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad Bash {Transformed(second)} {Transformed(second)}";
            run = new Run(setup, new ActStart());
            Do(run, "option:2");
            c.Equal((68, 68, expected), (run.State.Hp, run.State.MaxHp, DeckNames(run)), "Leafy Poultice: 68 of 68 and two transformed cards");
            c.Equal("option:0=Leave", Labels(run.Snapshot()), "Neow is finished");
        });
    }

    private static void TheGamesRecords(Check c)
    {
        c.Group("seed 75F277YN2G as the game recorded it: Pomander, Nutritious Oyster, Silver Crucible; Pomander was taken and Bash upgraded", () =>
        {
            // The seed's first act is Underdocks; Neow's offer does not depend on the act. Silver Crucible is a later chunk's.
            var setup = new RunSetup { Seed = "75F277YN2G" };
            var director = new ActStart { Ancient = "records", ForceOvergrowth = true };
            var run = new Run(setup, director);
            c.Sequence(["Pomander", "Nutritious Oyster", "Silver Crucible"], Titles(run.Snapshot()), "the offer of the record");
            Do(run, "option:0");
            c.Equal("upgrade", Kind(run.Snapshot()), "Pomander asks which card to upgrade");
            Do(run, "card:9", "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash+", DeckNames(run), "Bash is upgraded, as in the record");
            c.Sequence(["BurningBlood", "Pomander"], run.State.Relics.Select(relic => relic.ClassName), "the relic is the player's");
            c.Equal((80, 80, 99), (run.State.Hp, run.State.MaxHp, run.State.Gold), "80 of 80 HP and 99 gold, as the record has them after Neow");
            c.Equal("option:0=Leave", Labels(run.Snapshot()), "Neow is finished");
            c.Equal(run.Dump(), Run.Replay(setup, director, run.Actions).Dump(), "the run replays");

            run = new Run(setup, director);
            Do(run, "option:1");
            c.Equal((91, 91), (run.State.Hp, run.State.MaxHp), "the second option, Nutritious Oyster, would have given 91 of 91");
        });

        c.Group("seed AG5XPLQQNY as the game recorded it: Phial Holster, Golden Pearl, Silken Tress; Golden Pearl was taken for 150 gold", () =>
        {
            var run = new Run(new RunSetup { Seed = "AG5XPLQQNY" }, new ActStart { Ancient = "records" });
            c.Equal("Overgrowth", run.State.Part<RunWorld>().Acts[0], "an Overgrowth seed");
            c.Sequence(["Phial Holster", "Golden Pearl", "Silken Tress"], Titles(run.Snapshot()), "the offer of the record");
            Do(run, "option:1");
            c.Equal(249, run.State.Gold, "99 + 150, the record's gold_gained");
            c.Sequence(["BurningBlood", "GoldenPearl"], run.State.Relics.Select(relic => relic.ClassName), "the relic is the player's");
        });

        c.Group("seed 1CJXGGA4E5 as the game recorded it: Neow's Torment, Lava Rock, Cursed Pearl; the two of this chunk show their cards", () =>
        {
            var run = new Run(new RunSetup { Seed = "1CJXGGA4E5" }, new ActStart { Ancient = "records", ForceOvergrowth = true });
            var s = run.Snapshot();
            c.Equal("[{\"n\":0,\"title\":\"Neow's Torment\",\"text\":\"When picked up: add the card Neow's Fury to your deck.\",\"cards\":[" + CardJson("NeowsFury") + "]},"
                    + "{\"n\":1,\"title\":\"Lava Rock\",\"text\":\"A relic of the checks.\"},"
                    + "{\"n\":2,\"title\":\"Cursed Pearl\",\"text\":\"When picked up: add the curse Greed to your deck, then gain 333 gold.\",\"cards\":[" + CardJson("Greed") + "]}]",
                Text(s.State["options"]), "the options of the record; Neow's Torment lists Neow's Fury and Cursed Pearl lists Greed, and no relic key repeats the option");
            c.Equal("option:0=Take Neow's Torment | option:1=Take Lava Rock | option:2=Take Cursed Pearl", Labels(s), "the labels");
            Do(run, "option:2");
            c.Equal((432, Starter + " Greed"), (run.State.Gold, DeckNames(run)), "Cursed Pearl: 432 gold and Greed");
            c.Equal("option:0=Leave", Labels(run.Snapshot()), "Neow is finished");
        });
    }

    /// <summary>What the player has after a relic was taken at full HP with the starter deck and <see cref="Settle"/> answered its screens.</summary>
    private static readonly (string Relic, int Cards, int Upgraded, int Gold, int Hp, int MaxHp, int Potions)[] AfterTaking =
    [
        ("ArcaneScroll", 11, 0, 99, 80, 80, 0),
        ("BoomingConch", 10, 0, 99, 80, 80, 0),
        ("GoldenPearl", 10, 0, 249, 80, 80, 0),
        ("LostCoffer", 11, 0, 99, 80, 80, 1),        // the potion and the first of the three cards
        ("NeowsTorment", 11, 0, 99, 80, 80, 0),
        ("NewLeaf", 10, 0, 99, 80, 80, 0),
        ("PreciseScissors", 9, 0, 99, 80, 80, 0),
        ("NutritiousOyster", 10, 0, 99, 91, 91, 0),
        ("NeowsTalisman", 10, 2, 99, 80, 80, 0),
        ("Pomander", 10, 1, 99, 80, 80, 0),
        ("CursedPearl", 11, 0, 432, 80, 80, 0),
        ("HeftyTablet", 12, 0, 99, 80, 80, 0),       // the first of the three Rares, and Injury
        ("LeafyPoultice", 10, 0, 99, 68, 68, 0),
        ("PrecariousShears", 8, 0, 99, 64, 80, 0),
    ];

    /// <summary>The finished page of an Ancient's room: its leave option, and behind it nothing but the ids of a potion that is held.</summary>
    private static bool OnlyLeaveIsLeft(GameSnapshot s) =>
        s.Screen == "event" && s.Actions.Count > 0 && s.Actions[0] is { Id: "option:0", Label: "Leave" }
        && s.Actions.Skip(1).All(action => action.Id.StartsWith("potion:", StringComparison.Ordinal) || action.Id.StartsWith("discard_potion:", StringComparison.Ordinal));

    private static (int Cards, int Upgraded, int Gold, int Hp, int MaxHp, int Potions) Has(Run run) =>
        (run.State.Deck.Count, run.State.Deck.Count(card => card.IsUpgraded), run.State.Gold, run.State.Hp, run.State.MaxHp, run.State.Belt.Slots.Count(potion => potion is not null));

    private static void EveryRelicThroughAnAncient(Check c)
    {
        c.Group("every one of the 14 as an Ancient's option: taken, its screens answered, the room finished and left; each run replays and copies", () =>
        {
            c.Sequence(NeowCore.All, AfterTaking.Select(e => e.Relic), "a line for each");
            var setup = new RunSetup { Seed = RealSeed };
            var director = new ActStart { Ancient = "shelf" };
            var page = new Run(setup, director).Snapshot();
            c.Sequence(Expected.Select(e => e.Title), Titles(page), "the shelf shows all 14 by their titles");
            c.Sequence(Expected.Select(e => $"Take {e.Title}"), page.Actions.Select(action => action.Label), "labelled as an Ancient's options");
            c.Sequence(["NeowsTorment", "CursedPearl", "HeftyTablet"], page.State["options"]!.AsArray().Where(option => option!["cards"] is not null).Select(option => NeowCore.All[option!["n"]!.GetValue<int>()]),
                "three options list a card");
            c.Equal("[" + CardJson("Injury") + "]", Text(page.State["options"]![NeowCore.All.ToList().IndexOf("HeftyTablet")]!["cards"]), "Hefty Tablet lists Injury");
            c.That(page.State["options"]!.AsArray().All(option => option!["relic"] is null), "no option repeats its relic under a relic key");

            for (var n = 0; n < AfterTaking.Length; n++)
            {
                var e = AfterTaking[n];
                var run = new Run(setup, director);
                Do(run, $"option:{n}");
                var asked = run.Snapshot().Screen;
                Run? halfway = asked == "event" ? null : run.Clone();
                var halfwayAt = run.Actions.Count;
                Settle(run);
                c.Equal((e.Cards, e.Upgraded, e.Gold, e.Hp, e.MaxHp, e.Potions), Has(run), $"{e.Relic}: cards, upgraded cards, gold, HP, max HP, potions");
                c.Sequence(["BurningBlood", e.Relic], run.State.Relics.Select(relic => relic.ClassName), $"{e.Relic}: the relic is the player's");
                c.That(OnlyLeaveIsLeft(run.Snapshot()), $"{e.Relic}: the Ancient's room is finished ({Labels(run.Snapshot())})");
                Do(run, "option:0");
                c.Equal("map", run.Snapshot().Screen, $"{e.Relic}: Leave opens the map");
                c.Equal(run.Dump(), Run.Replay(setup, director, run.Actions).Dump(), $"{e.Relic}: the run replays from its {run.Actions.Count} actions");
                c.Equal(run.Dump(), run.Clone().Dump(), $"{e.Relic}: a copy is in the same state");
                if (halfway is null) continue;
                // A copy taken while the pickup's first screen was up, played on with the same answers.
                foreach (var id in run.Actions.Skip(halfwayAt)) halfway.Execute(id);
                c.Equal(run.Dump(), halfway.Dump(), $"{e.Relic}: a copy taken in the middle of the pickup ({asked}) plays on to the same state");
            }
        });

        c.Group("which pickups ask: five put up a card screen, one a rewards screen, eight nothing", () =>
        {
            var setup = new RunSetup { Seed = RealSeed };
            var asked = new List<string>();
            for (var n = 0; n < NeowCore.All.Count; n++)
            {
                var run = new Run(setup, new ActStart { Ancient = "shelf" });
                Do(run, $"option:{n}");
                var s = run.Snapshot();
                asked.Add(s.Screen == "card_select" ? $"{NeowCore.All[n]}:{Kind(s)}" : s.Screen == "event" ? "" : $"{NeowCore.All[n]}:{s.Screen}");
            }
            c.Sequence(["LostCoffer:rewards", "NewLeaf:transform", "PreciseScissors:remove", "Pomander:upgrade", "HeftyTablet:choose", "PrecariousShears:remove"], asked.Where(a => a.Length > 0),
                "Lost Coffer, New Leaf, Precise Scissors, Pomander, Hefty Tablet, Precarious Shears");
        });
    }

    // ====================================================================================
    // seeds
    // ====================================================================================

    private static void SeedsWhoseOfferIsBuilt(Check c)
    {
        c.Group("how many real offers these 14 cover: 7 in 60", () =>
        {
            // The cursed relic is one of the four with chance 4/8. Each of the four takes its twin out, so the shuffled list
            // has 15 relics: six of this file's seven fixed ones, Neow's Talisman or Pomander (always), Nutritious Oyster with
            // chance 1/2, and Lava Rock or Small Capsule, which are not of this file. So 7 or 8 of the 15, half the time each,
            // and the two shown are both of this file with chance (7*6 + 8*7) / 2 / (15*14) = 7/30. Times 1/2: 7/60.
            const int Seeds = 6000;
            var maker = new GameRng(14_006);
            var whole = 0;
            var offered = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var n = 0; n < Seeds; n++)
            {
                var offer = NeowOffer.OfTheGame(new RunStreams(RunSeed.Random(maker)), RunUnlocks.All);
                if (offer.All(NeowCore.All.Contains)) whole++;
                foreach (var relic in offer) offered[relic] = offered.GetValueOrDefault(relic) + 1;
            }
            // 700 expected; four standard deviations are 100.
            c.That(whole is >= 600 and <= 800, $"of {Seeds} seeds about 700 have an offer of three relics of this file ({whole})");
            c.That(NeowCore.All.All(relic => offered.GetValueOrDefault(relic) > 0), "and every one of the 14 is offered on some seed");
            // Each cursed relic is the third option on one seed in eight: 750 of 6000, three standard deviations are 77.
            foreach (var cursed in NeowCore.Cursed)
                c.That(offered.GetValueOrDefault(cursed) is >= 670 and <= 830, $"{cursed} is offered on about one seed in eight ({offered.GetValueOrDefault(cursed)})");
        });

        c.Group("seeds whose whole offer is built, through the registry's Neow: every relic taken on some seed, as a run of the game would have it", () =>
        {
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            var problems = new List<string>();
            var maker = new GameRng(14_007);
            var runs = 0;
            for (var tried = 0; tried < 6000 && (taken.Count < 14 || taken.Values.Min() < 2); tried++)
            {
                var seed = RunSeed.Random(maker);
                if (RunGeneration.RollActs(seed, RunUnlocks.All)[0] != GameLists.Overgrowth) continue;
                var offer = NeowOffer.OfTheGame(new RunStreams(seed), RunUnlocks.All);
                if (!offer.All(NeowCore.All.Contains)) continue;

                var setup = new RunSetup { Seed = seed.Text };
                var run = new Run(setup, new ActStart());
                var option = runs++ % 3;
                var titles = Titles(run.Snapshot());
                if (!titles.SequenceEqual(offer.Select(relic => Content.Registry.NewRelic(relic).Name))) problems.Add($"{seed.Text}: shows {string.Join(", ", titles)} for {string.Join(", ", offer)}");
                Do(run, $"option:{option}");
                Settle(run);
                var relic = offer[option];
                taken[relic] = taken.GetValueOrDefault(relic) + 1;
                var expected = AfterTaking.Single(e => e.Relic == relic);
                if (Has(run) != (expected.Cards, expected.Upgraded, expected.Gold, expected.Hp, expected.MaxHp, expected.Potions)) problems.Add($"{seed.Text}: after {relic} the player has {Has(run)}");
                if (!run.State.Relics.Select(held => held.ClassName).SequenceEqual(["BurningBlood", relic])) problems.Add($"{seed.Text}: holds {string.Join(", ", run.State.Relics.Select(held => held.ClassName))}");
                if (!OnlyLeaveIsLeft(run.Snapshot())) problems.Add($"{seed.Text}: after {relic} the room is not finished ({Labels(run.Snapshot())})");
                Do(run, "option:0");
                if (run.Snapshot().Screen != "map") problems.Add($"{seed.Text}: Leave did not open the map");
                // Only a potion of Lost Coffer can have been drawn again (two potions are left out); Neow's part is the game's own.
                var passedOver = run.State.FindPart<LeftOutLog>()?.Entries ?? [];
                if (passedOver.Any(entry => entry.Kind != "potion")) problems.Add($"{seed.Text}: passed over {string.Join(", ", passedOver.Select(entry => entry.ClassName))}");
                if (run.Dump() != Run.Replay(setup, new ActStart(), run.Actions).Dump()) problems.Add($"{seed.Text}: the replay ends in another state");
            }
            c.Equal("", string.Join(" | ", problems.Take(5)), $"{runs} runs: the offer shown is the game's, the relic does what its line above says, the room finishes, the run replays ({problems.Count} problems)");
            c.That(taken.Count == 14 && taken.Values.Min() >= 2, $"every relic was taken on at least two seeds ({string.Join(", ", taken.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => $"{t.Key} {t.Value}"))})");
        });
    }

    // ====================================================================================
    // the game's own records, when the folder is on the machine
    // ====================================================================================

    private static string CardId(CardInstance card) => "CARD." + GameNames.Slug(card.Def.ClassName);

    /// <summary>
    /// Every record of a single-player Ironclad run of this build in sts2-notes/golden-saves/history (or $ARENA_GOLDEN_SAVES)
    /// whose pick at Neow is a relic of this file and whose outcome the record states: the pick is made here and held
    /// against the record. Arcane Scroll and Neow's Torment by the card gained, Golden Pearl and Cursed Pearl by the gold
    /// gained, Nutritious Oyster by the max HP, Pomander by the card the record says was upgraded (which is then chosen
    /// here too), Neow's Talisman by the two cards upgraded. The other picks hang on choices or rolls a record does not
    /// hold in a form these checks read, and are counted as passed over.
    /// </summary>
    private static void GoldenFolder(Check c)
    {
        c.Group("the game's own run records on this machine, when there are any: every pick of one of these relics at Neow", () =>
        {
            var folder = Environment.GetEnvironmentVariable("ARENA_GOLDEN_SAVES");
            if (string.IsNullOrEmpty(folder))
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "sts2-notes", "golden-saves");
            var history = Path.Combine(folder, "history");
            if (!Directory.Exists(history))
            {
                c.That(true, "no folder of run records here");
                return;
            }

            var bySlug = NeowRelics.All.ToDictionary(GameNames.Slug, StringComparer.Ordinal);
            var (compared, passedOver) = (0, 0);
            foreach (var file in Directory.GetFiles(history, "*.run").OrderBy(f => f, StringComparer.Ordinal))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (!root.TryGetProperty("build_id", out var build) || build.GetString() != "v0.107.1") continue;
                if (!root.TryGetProperty("game_mode", out var mode) || mode.GetString() != "standard") continue;
                if (!root.TryGetProperty("ascension", out var ascension) || ascension.GetInt32() != 0) continue;
                var players = root.GetProperty("players");
                if (players.GetArrayLength() != 1 || players[0].GetProperty("character").GetString() != "CHARACTER.IRONCLAD") continue;
                if (!root.TryGetProperty("map_point_history", out var acts) || acts.GetArrayLength() == 0 || acts[0].GetArrayLength() == 0) continue;
                var stats = acts[0][0].GetProperty("player_stats")[0];
                if (!stats.TryGetProperty("ancient_choice", out var choices)) continue;
                var offer = choices.EnumerateArray().Select(choice => (Key: choice.GetProperty("TextKey").GetString()!, Chosen: choice.GetProperty("was_chosen").GetBoolean())).ToList();
                var option = offer.FindIndex(choice => choice.Chosen);
                if (option < 0 || offer.Any(choice => !bySlug.ContainsKey(choice.Key))) continue;
                var relic = bySlug[offer[option].Key];
                if (!NeowCore.All.Contains(relic)) continue;

                var seed = root.GetProperty("seed").GetString()!;
                var name = $"{Path.GetFileName(file)}, seed {seed}, {relic}";
                List<string> Ids(string key) => stats.TryGetProperty(key, out var list)
                    ? list.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : item.GetProperty("id").GetString()!).ToList()
                    : [];
                var run = new Run(new RunSetup { Seed = seed }, new ActStart { Ancient = "records", ForceOvergrowth = true });
                c.Equal(Content.Registry.NewRelic(relic).Name, Titles(run.Snapshot())[option], $"{name}: the option of the record is offered at its place");
                var before = run.State.Deck.ToList();
                Do(run, $"option:{option}");
                switch (relic)
                {
                    case "ArcaneScroll" or "NeowsTorment":
                        c.Sequence(Ids("cards_gained"), run.State.Deck.Skip(before.Count).Select(CardId), $"{name}: the card gained");
                        break;
                    case "GoldenPearl":
                        c.Equal(stats.GetProperty("gold_gained").GetInt32(), run.State.Gold - RunSetup.IroncladGold, $"{name}: the gold gained");
                        break;
                    case "CursedPearl":
                        c.Equal(stats.GetProperty("gold_gained").GetInt32(), run.State.Gold - RunSetup.IroncladGold, $"{name}: the gold gained");
                        c.Sequence(Ids("cards_gained"), run.State.Deck.Skip(before.Count).Select(CardId), $"{name}: the curse gained");
                        break;
                    case "NutritiousOyster":
                        c.Equal(stats.GetProperty("max_hp_gained").GetInt32(), run.State.MaxHp - RunSetup.IroncladHp, $"{name}: the max HP gained");
                        break;
                    case "NeowsTalisman":
                        c.Sequence(Ids("upgraded_cards").Order(StringComparer.Ordinal), run.State.Deck.Where(card => card.IsUpgraded).Select(CardId).Order(StringComparer.Ordinal), $"{name}: the two cards upgraded");
                        break;
                    case "Pomander" when Ids("upgraded_cards") is [var upgraded]:
                        var s = run.Snapshot();
                        var entry = s.State["cards"]!.AsArray().FirstOrDefault(card => run.State.Deck.Any(held => CardId(held) == upgraded && held.Title == card!["name"]!.GetValue<string>()));
                        c.That(entry is not null, $"{name}: the card the record says was upgraded, {upgraded}, is on the grid");
                        if (entry is null) break;
                        Do(run, $"card:{entry["i"]}", "confirm");
                        c.Sequence([upgraded], run.State.Deck.Where(card => card.IsUpgraded).Select(CardId), $"{name}: that card is upgraded");
                        break;
                    default:
                        passedOver++;
                        continue;
                }
                c.That(run.State.HasRelic(relic), $"{name}: the relic is the player's");
                compared++;
            }
            c.That(true, $"{compared} recorded picks compared, {passedOver} passed over");
            // The three picks of the folder as it was when these checks were written: Arcane Scroll, Pomander, Golden Pearl.
            if (File.Exists(Path.Combine(history, "1791755111.run")) && File.Exists(Path.Combine(history, "1791592972.run")) && File.Exists(Path.Combine(history, "1791597100.run")))
                c.That(compared >= 3, $"the three known records were compared ({compared})");
        });
    }
}
