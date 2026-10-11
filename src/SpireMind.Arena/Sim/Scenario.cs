using System.Collections.Concurrent;
using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// One generated fight: a believable moment of an act-1 run. <see cref="Id"/> rebuilds it
/// (<see cref="Scenarios.FromId"/>), <see cref="Seed"/> seeds everything random about it, the
/// run that led up to it as well as the fight itself, <see cref="Tier"/> groups fights in the
/// reports and <see cref="Summary"/> is one line for a human.
/// </summary>
public sealed record Scenario(string Id, int Seed, string Tier, string Summary);

/// <summary>
/// Generates scenarios the way a run of the game produces them (run-generation.md, v0.107.1,
/// Ironclad, ascension 0, act 1 "Overgrowth"): the act's encounter lists are drawn, a path up
/// the map is walked, and every fight won on the way gives its potion roll and its card offer
/// with the game's odds. The fight the scenario is about is one room of that path.
/// <para>
/// What comes from the game's code is marked "game"; what the spec worked out from the game's
/// map generator by simulation is marked DERIVED; what no code decides (how a player picks,
/// rests, shops, drinks and how much HP the fights cost) is marked ESTIMATE. The estimates are
/// plain constants: change them here.
/// </para>
/// <para>
/// The generator deals only what the registry holds: a card or potion that is left out is
/// rolled again, a run whose fight is an encounter the arena lacks is rolled again, and a tier
/// with no encounter at all is an error with the tier's name in it.
/// </para>
/// <para>
/// Not modelled, so a scenario is a little plainer than a real run: relics other than Burning
/// Blood (the Neow relics with a lasting effect, elite and treasure relics), what events do,
/// cards and potions bought in shops, and Underdocks as act 1.
/// </para>
/// </summary>
public static class Scenarios
{
    public const string WeakTier = "weak";
    public const string NormalTier = "normal";
    public const string EliteTier = "elite";
    public const string BossTier = "boss";

    /// <summary>The tier names: one of the act's first three fights; a later hallway fight; an elite; the act's boss.</summary>
    internal static readonly string[] Tiers = [WeakTier, NormalTier, EliteTier, BossTier];

    /// <summary>
    /// The tiers <see cref="Generate"/> deals, in this order, over and over: 2 weak, 4 normal,
    /// 2 elite and 2 boss in every 10, mixed so that a short list has every tier. The harder
    /// fights tell players apart, so they get more room than a run gives them.
    /// </summary>
    internal static readonly string[] TierPattern =
        [NormalTier, EliteTier, WeakTier, BossTier, NormalTier, NormalTier, EliteTier, WeakTier, BossTier, NormalTier];

    /// <summary>Scenario seeds run from 1 to this, which keeps ids short.</summary>
    private const int SeedSpace = 999_999;

    // =====================================================================================
    // Game: the act (run-generation.md 1.2, 1.5, 1.6)
    // =====================================================================================

    /// <summary>The first fight is on floor 2 (floor 1 is Neow), on every path.</summary>
    internal const int FirstFloor = 2;

    /// <summary>The first floor that can hold an Elite or a Rest Site.</summary>
    internal const int FirstEliteFloor = 7;

    /// <summary>Always a Treasure room.</summary>
    internal const int TreasureFloor = 10;

    /// <summary>The first of the two floors below the last Rest Site: no Rest Site there.</summary>
    internal const int FirstLateFloor = 14;

    /// <summary>Always a Rest Site.</summary>
    internal const int LastRestFloor = 16;

    internal const int BossFloor = 17;

    /// <summary>The act's list of hallway fights starts with this many weak encounters.</summary>
    internal const int WeakEncountersPerAct = 3;

    /// <summary>The length of the act's list of hallway fights (3 weak, then 12 regular).</summary>
    internal const int HallwayListLength = 15;

    internal const int EliteListLength = 15;

    /// <summary>A map has this many Shop nodes in all, so a path cannot pass more (an Unknown room may still turn out a shop).</summary>
    internal const int ShopNodesPerMap = 3;

    internal enum EncounterKind { Weak, Regular, Elite, Boss }

    /// <summary>One encounter of the act: its class name, what it is, and its tags (two encounters
    /// that share a tag are kept apart in the act's lists where possible).</summary>
    internal sealed record RosterEntry(string ClassName, EncounterKind Kind, params string[] Tags)
    {
        public bool SharesTagWith(RosterEntry other) => Tags.Intersect(other.Tags).Any();
    }

    /// <summary>Overgrowth's 22 encounters in the act's own order, which is the order the bags are filled in.</summary>
    internal static readonly IReadOnlyList<RosterEntry> Roster =
    [
        new("BygoneEffigyElite", EncounterKind.Elite),
        new("ByrdonisElite", EncounterKind.Elite),
        new("CeremonialBeastBoss", EncounterKind.Boss),
        new("CubexConstructNormal", EncounterKind.Regular),
        new("FlyconidNormal", EncounterKind.Regular, "Mushroom", "Slimes"),
        new("FogmogNormal", EncounterKind.Regular),
        new("FuzzyWurmCrawlerWeak", EncounterKind.Weak, "Crawler"),
        new("InkletsNormal", EncounterKind.Regular),
        new("MawlerNormal", EncounterKind.Regular),
        new("NibbitsNormal", EncounterKind.Regular),
        new("NibbitsWeak", EncounterKind.Weak, "Nibbit"),
        new("OvergrowthCrawlers", EncounterKind.Regular, "Shrinker", "Crawler"),
        new("PhrogParasiteElite", EncounterKind.Elite),
        new("RubyRaidersNormal", EncounterKind.Regular),
        new("ShrinkerBeetleWeak", EncounterKind.Weak, "Shrinker"),
        new("SlimesNormal", EncounterKind.Regular, "Slimes"),
        new("SlimesWeak", EncounterKind.Weak, "Slimes"),
        new("SlitheringStranglerNormal", EncounterKind.Regular),
        new("SnappingJaxfruitNormal", EncounterKind.Regular, "Mushroom"),
        new("TheKinBoss", EncounterKind.Boss),
        new("VantomBoss", EncounterKind.Boss),
        new("VineShamblerNormal", EncounterKind.Regular),
    ];

    // =====================================================================================
    // Game: the Ironclad's start (run-generation.md 4, 6.1) and Neow's relics (3.4)
    // =====================================================================================

    internal const int StartMaxHp = 80;

    internal const string StrikeCard = "StrikeIronclad";
    internal const string DefendCard = "DefendIronclad";
    internal const string BashCard = "Bash";

    /// <summary>Nutritious Oyster: this much more max HP, and HP with it.</summary>
    internal const int OysterMaxHp = 11;

    /// <summary>What the relic taken from Neow did to the starting state, as far as the arena shows it.</summary>
    internal enum NeowStart
    {
        /// <summary>Any relic that leaves deck, belt and HP alone.</summary>
        NoChange,

        /// <summary>Pomander: one chosen card is upgraded.</summary>
        Pomander,

        /// <summary>Precise Scissors: one chosen card is removed.</summary>
        PreciseScissors,

        /// <summary>Arcane Scroll: one random Rare card of the pool is added.</summary>
        ArcaneScroll,

        /// <summary>Nutritious Oyster: 11 more max HP.</summary>
        NutritiousOyster,
    }

    // =====================================================================================
    // Game: card rewards (run-generation.md 2.1 to 2.4)
    // =====================================================================================

    /// <summary>Hallway fights, elites and bosses all offer this many cards; the player takes at most one.</summary>
    internal const int CardsPerReward = 3;

    // The odds themselves, and the rolls on them, are the run's (Sim/Run/RunOdds.cs): these are its numbers.
    internal const float RegularRareOdds = CardRarityOdds.RegularRare;
    internal const float RegularUncommonOdds = CardRarityOdds.RegularUncommon;
    internal const float EliteRareOdds = CardRarityOdds.EliteRare;
    internal const float EliteUncommonOdds = CardRarityOdds.EliteUncommon;

    /// <summary>The pity offset a run starts with, and returns to after every Rare card rolled.</summary>
    internal const float BaseRarityOffset = CardRarityOdds.BaseOffset;

    /// <summary>What the offset grows by after every card rolled that is not Rare.</summary>
    internal const float RarityGrowth = CardRarityOdds.Growth;

    internal const float MaxRarityOffset = CardRarityOdds.MaxOffset;

    /// <summary>A reward card that is not Rare comes upgraded with this chance times the act's index. Act 1 has index 0.</summary>
    internal const decimal UpgradeChancePerAct = 0.25m;

    internal const int ActIndex = 0;

    // =====================================================================================
    // Game: potion drops (run-generation.md 5.3)
    // =====================================================================================

    internal const int PotionSlots = 3;

    /// <summary>The drop chance a run starts with; it moves by <see cref="PotionOddsStep"/> after every roll, down after a drop.</summary>
    internal const float BasePotionOdds = PotionOdds.Base;

    internal const float PotionOddsStep = PotionOdds.Step;

    /// <summary>The elite bonus as the game's constant has it; the roll adds half of it.</summary>
    internal const float PotionEliteBonus = PotionOdds.EliteBonus;

    /// <summary>A dropped potion is Rare at or below this roll.</summary>
    internal const float PotionRareThreshold = 0.1f;

    /// <summary>... Uncommon at or below this one, Common above it.</summary>
    internal const float PotionUncommonThreshold = 0.35f;

    /// <summary>The one potion of the pool that cannot be drunk: it waits in the belt for its owner's death.</summary>
    internal const string AutomaticPotion = "FairyInABottle";

    /// <summary>
    /// Every potion a drop can be, in the order the game lists them: the Ironclad's own three,
    /// then the 45 shared ones. 16 Common, 16 Uncommon, 16 Rare. A drop rolls the rarity and
    /// then takes one of that rarity's 16 with equal chance.
    /// </summary>
    internal static readonly IReadOnlyList<(string ClassName, PotionRarity Rarity)> PotionPool =
    [
        ("BloodPotion", PotionRarity.Common), ("SoldiersStew", PotionRarity.Rare), ("Ashwater", PotionRarity.Uncommon),
        ("AttackPotion", PotionRarity.Common), ("BeetleJuice", PotionRarity.Rare), ("BlessingOfTheForge", PotionRarity.Uncommon),
        ("BlockPotion", PotionRarity.Common), ("BottledPotential", PotionRarity.Rare), ("Clarity", PotionRarity.Uncommon),
        ("ColorlessPotion", PotionRarity.Common), ("CureAll", PotionRarity.Uncommon), ("DexterityPotion", PotionRarity.Common),
        ("DistilledChaos", PotionRarity.Rare), ("DropletOfPrecognition", PotionRarity.Rare), ("Duplicator", PotionRarity.Uncommon),
        ("EnergyPotion", PotionRarity.Common), ("EntropicBrew", PotionRarity.Rare), ("ExplosiveAmpoule", PotionRarity.Common),
        ("FairyInABottle", PotionRarity.Rare), ("FirePotion", PotionRarity.Common), ("FlexPotion", PotionRarity.Common),
        ("Fortifier", PotionRarity.Uncommon), ("FruitJuice", PotionRarity.Rare), ("FyshOil", PotionRarity.Uncommon),
        ("GamblersBrew", PotionRarity.Uncommon), ("GigantificationPotion", PotionRarity.Rare), ("HeartOfIron", PotionRarity.Uncommon),
        ("LiquidBronze", PotionRarity.Uncommon), ("LiquidMemories", PotionRarity.Rare), ("LuckyTonic", PotionRarity.Rare),
        ("MazalethsGift", PotionRarity.Rare), ("OrobicAcid", PotionRarity.Rare), ("PotionOfBinding", PotionRarity.Uncommon),
        ("PowderedDemise", PotionRarity.Uncommon), ("PowerPotion", PotionRarity.Common), ("RadiantTincture", PotionRarity.Uncommon),
        ("RegenPotion", PotionRarity.Uncommon), ("ShacklingPotion", PotionRarity.Rare), ("ShipInABottle", PotionRarity.Rare),
        ("SkillPotion", PotionRarity.Common), ("SneckoOil", PotionRarity.Rare), ("SpeedPotion", PotionRarity.Common),
        ("StableSerum", PotionRarity.Uncommon), ("StrengthPotion", PotionRarity.Common), ("SwiftPotion", PotionRarity.Common),
        ("TouchOfInsanity", PotionRarity.Uncommon), ("VulnerablePotion", PotionRarity.Common), ("WeakPotion", PotionRarity.Common),
    ];

    // =====================================================================================
    // Game: what an Unknown room turns out to be (run-generation.md 1.4)
    // =====================================================================================

    internal const float UnknownMonsterOdds = UnknownRoomOdds.BaseMonster;
    internal const float UnknownTreasureOdds = UnknownRoomOdds.BaseTreasure;
    internal const float UnknownShopOdds = UnknownRoomOdds.BaseShop;

    // =====================================================================================
    // DERIVED: the rooms of a path (run-generation.md 1.3). The game builds a whole map; these
    // are the shares of room types per floor that the spec measured on its port of the map
    // generator, good to a point or two. They are not numbers of the game's code.
    // =====================================================================================

    internal enum MapNode { Monster, Unknown, Shop, Elite, Rest, Treasure, Boss }

    /// <summary>Floors 3 to 6: no Elite and no Rest Site this low.</summary>
    private static readonly (MapNode Node, double Share)[] EarlyFloorShares =
        [(MapNode.Monster, 0.62), (MapNode.Unknown, 0.29), (MapNode.Shop, 0.08)];

    /// <summary>Floors 7 to 9 and 11 to 13.</summary>
    private static readonly (MapNode Node, double Share)[] MiddleFloorShares =
        [(MapNode.Monster, 0.36), (MapNode.Unknown, 0.22), (MapNode.Shop, 0.04), (MapNode.Elite, 0.13), (MapNode.Rest, 0.25)];

    /// <summary>Floors 14 and 15: no Rest Site right below the last one.</summary>
    private static readonly (MapNode Node, double Share)[] LateFloorShares =
        [(MapNode.Monster, 0.49), (MapNode.Unknown, 0.23), (MapNode.Shop, 0.08), (MapNode.Elite, 0.20)];

    // =====================================================================================
    // ESTIMATE: what the player did on the way (run-generation.md 2.5, 3, 5.3, 6.3). No code
    // decides any of this; the numbers lean on one logged run and are meant to be believable,
    // not measured.
    // =====================================================================================

    /// <summary>ESTIMATE. How often a card is taken from an offer (the logged run took 8 of 9).
    /// The card taken is one of the offered cards with equal chance.</summary>
    internal const double TakeCardChance = 0.85;

    /// <summary>ESTIMATE. How often a Rest Site is used to upgrade a card instead of healing (1 of 3 in the logged run).
    /// The card is a random upgradable one, a basic Strike or Defend only when there is nothing else.</summary>
    internal const double SmithChance = 0.4;

    /// <summary>ESTIMATE. How often the first shop on the way is used to remove a card: a Strike, or a Defend
    /// when no Strike is left. One removal per act at most; cards bought are not modelled.</summary>
    internal const double RemovalChance = 0.5;

    /// <summary>ESTIMATE. The chance that a potion in the belt was drunk in a hallway fight on the way.</summary>
    internal const double PotionUseChanceHallway = 0.2;

    /// <summary>ESTIMATE. The same for an elite fight.</summary>
    internal const double PotionUseChanceElite = 0.5;

    /// <summary>ESTIMATE. The relic taken from Neow: most leave deck and HP as they are; the four small starts share the rest.</summary>
    private static readonly (NeowStart Start, int Weight)[] NeowWeights =
    [
        (NeowStart.NoChange, 60), (NeowStart.Pomander, 10), (NeowStart.PreciseScissors, 10),
        (NeowStart.ArcaneScroll, 10), (NeowStart.NutritiousOyster, 10),
    ];

    /// <summary>ESTIMATE. HP on entering a fight, of 80, lowest and highest (run-generation.md 6.3). The very
    /// first fight is not an estimate: Neow heals to full and nothing comes before it.</summary>
    internal static readonly (int Min, int Max) EarlyHp = (68, 80);

    /// <summary>ESTIMATE. Hallway fights after the third and elites, up to the Treasure floor.</summary>
    internal static readonly (int Min, int Max) MiddleHp = (45, 75);

    /// <summary>ESTIMATE. The same above the Treasure floor.</summary>
    internal static readonly (int Min, int Max) LateHp = (35, 65);

    /// <summary>ESTIMATE. The boss, after the Rest Site in front of it.</summary>
    internal static readonly (int Min, int Max) BossHp = (55, 80);

    // =====================================================================================
    // The public face
    // =====================================================================================

    /// <summary>
    /// <paramref name="count"/> different fights, the same ones for the same <paramref name="seed"/>;
    /// a longer list starts with the shorter one. Tiers follow <see cref="TierPattern"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">A tier of the pattern has no encounter in the registry.</exception>
    public static IReadOnlyList<Scenario> Generate(int count, int seed)
    {
        var list = new List<Scenario>(Math.Max(0, count));
        var rng = RngSet.For(seed, "scenarios");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var tier = TierPattern[i % TierPattern.Length];
            int scenarioSeed;
            do scenarioSeed = 1 + rng.NextInt(SeedSpace);
            while (!seen.Add(IdOf(tier, scenarioSeed)));
            list.Add(Create(tier, scenarioSeed));
        }
        return list;
    }

    /// <summary>The scenario an id names ("elite-48213": tier, then seed).</summary>
    /// <exception cref="ArgumentException">The id is not one <see cref="Generate"/> makes: a tier, a dash, a seed from 1 to 999999 as it is written.</exception>
    /// <exception cref="InvalidOperationException">The tier has no encounter in the registry.</exception>
    public static Scenario FromId(string id)
    {
        var dash = id?.LastIndexOf('-') ?? -1;
        if (dash <= 0
            || !Tiers.Contains(id![..dash])
            || !int.TryParse(id[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var seed)
            || seed is < 1 or > SeedSpace
            || IdOf(id[..dash], seed) != id)
            throw new ArgumentException($"'{id}' is not a scenario id. Ids look like {string.Join(", ", Tiers.Select(t => t + "-123"))}.", nameof(id));
        return Create(id[..dash], seed);
    }

    /// <summary>Everything the engine needs to start the scenario's fight.</summary>
    /// <exception cref="ArgumentException">The scenario's tier is not one of the arena's.</exception>
    internal static FightSetup Setup(Scenario scenario)
    {
        if (!Tiers.Contains(scenario.Tier))
            throw new ArgumentException($"'{scenario.Tier}' is not a scenario tier ({string.Join(", ", Tiers)}).", nameof(scenario));
        var plan = PlanFor(scenario.Tier, scenario.Seed);
        return new FightSetup(plan.Deck, plan.Hp, plan.MaxHp, plan.Potions, plan.Encounter, plan.Floor, scenario.Seed);
    }

    private static string IdOf(string tier, int seed) => string.Create(CultureInfo.InvariantCulture, $"{tier}-{seed}");

    private static Scenario Create(string tier, int seed) => new(IdOf(tier, seed), seed, tier, PlanFor(tier, seed).Summary);

    // Scenarios are asked for again and again (every game that is started works its fight out
    // from the id), and the registry never changes within a process: keep the last few thousand.
    private static readonly ConcurrentDictionary<(string Tier, int Seed), Plan> Plans = new();
    private static readonly Lazy<Catalog> RegistryCatalogLazy = new(Catalog.FromRegistry);

    /// <summary>What the registry holds of the act's content.</summary>
    internal static Catalog RegistryCatalog => RegistryCatalogLazy.Value;

    private static Plan PlanFor(string tier, int seed)
    {
        if (Plans.TryGetValue((tier, seed), out var known)) return known;
        var plan = Build(tier, seed, RegistryCatalog);
        if (Plans.Count >= 4096) Plans.Clear();
        Plans[(tier, seed)] = plan;
        return plan;
    }

    // =====================================================================================
    // What there is to deal
    // =====================================================================================

    /// <summary>
    /// What the generator may deal: the registry's part of the act's cards, potions and
    /// encounters. The checks pass one of their own to try the rules on the game's full lists.
    /// </summary>
    internal sealed class Catalog
    {
        /// <summary>The cards a reward can show: the pool's Common, Uncommon and Rare cards of a solo run, in pool order.</summary>
        public required IReadOnlyList<(string ClassName, CardRarity Rarity)> RewardCards { get; init; }

        /// <summary>Which potions of <see cref="PotionPool"/> can be dealt.</summary>
        public required IReadOnlySet<string> PotionNames { get; init; }

        /// <summary>Which encounters of <see cref="Roster"/> can be fought.</summary>
        public required IReadOnlySet<string> Encounters { get; init; }

        public Func<string, bool> CanUpgrade { get; init; } = _ => true;
        public Func<string, string> CardTitle { get; init; } = name => name;
        public Func<string, string> PotionTitle { get; init; } = name => name;

        /// <summary>The encounter's line-up for a fight seed as "Nibbit + Nibbit", or null when it cannot be told.</summary>
        public Func<string, int, string?> LineUp { get; init; } = (_, _) => null;

        public bool Has(EncounterKind kind) => Roster.Any(e => e.Kind == kind && Encounters.Contains(e.ClassName));

        public static Catalog FromRegistry()
        {
            var registry = Content.Registry;
            var cards = registry.IroncladPool
                .Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare && !Potions.MultiplayerOnlyCards.Contains(c.ClassName))
                .Select(c => (c.ClassName, c.Rarity))
                .ToList();

            // A card potion that cannot show three cards of its type, or an Orobic Acid that cannot
            // give a card of each type, would not be the game's potion.
            bool CanDeal(string potion) => registry.Has(ContentKind.Potion, potion) && potion switch
            {
                "AttackPotion" => Potions.CardPotionPool(CardType.Attack).Count >= 3,
                "SkillPotion" => Potions.CardPotionPool(CardType.Skill).Count >= 3,
                "PowerPotion" => Potions.CardPotionPool(CardType.Power).Count >= 3,
                "OrobicAcid" => Potions.OrobicTypes.All(type => Potions.CardPotionPool(type).Count > 0),
                _ => true,
            };

            return new Catalog
            {
                RewardCards = cards,
                PotionNames = PotionPool.Select(p => p.ClassName).Where(CanDeal).ToHashSet(StringComparer.Ordinal),
                Encounters = Roster.Select(e => e.ClassName).Where(name => registry.Has(ContentKind.Encounter, name)).ToHashSet(StringComparer.Ordinal),
                CanUpgrade = name => registry.Has(ContentKind.Card, name) && registry.Card(name).MaxUpgradeLevel > 0,
                CardTitle = name => registry.Has(ContentKind.Card, name) ? registry.Card(name).Name : name,
                PotionTitle = name => registry.Has(ContentKind.Potion, name) ? registry.Potion(name).Name : name,
                LineUp = (encounter, seed) =>
                {
                    try
                    {
                        // The fight itself says who stands in it, in the order of the e<j> ids; the line-up
                        // depends on the encounter and the seed alone, so any deck will do.
                        var deck = Enumerable.Repeat(StrikeCard, 5).Concat(Enumerable.Repeat(DefendCard, 4)).Append(BashCard)
                            .Select(name => (registry.Card(name), false)).ToList();
                        var fight = new Fight(new FightConfig { Deck = deck, Encounter = encounter, Seed = seed });
                        return string.Join(" + ", fight.Roster.Select(e => e.Name));
                    }
                    catch (Exception)
                    {
                        // A fight that cannot start fails when it is played, with its own message; the summary must not.
                        return null;
                    }
                },
            };
        }
    }

    // =====================================================================================
    // The run that leads to the fight
    // =====================================================================================

    internal enum Room { Monster, Elite, Rest, Shop, Treasure, Event, Boss }

    /// <summary>One floor of the path: the room that was there, and whether the map showed it as Unknown.</summary>
    internal readonly record struct Step(int Floor, Room Room, bool WasUnknown);

    /// <summary>One card of an offer.</summary>
    internal readonly record struct OfferedCard(string ClassName, CardRarity Rarity, bool Upgraded);

    /// <summary>One card reward on the way: the cards offered, and the index of the one taken, or -1.</summary>
    internal sealed record Offer(bool Elite, IReadOnlyList<OfferedCard> Cards, int Taken);

    /// <summary>The act's lists as the game draws them at the start of a run.</summary>
    internal sealed record ActLists(IReadOnlyList<string> Hallway, IReadOnlyList<string> Elites, string Boss);

    /// <summary>Everything that was decided for one scenario.</summary>
    internal sealed record Plan
    {
        public required string Tier { get; init; }
        public required int Seed { get; init; }

        /// <summary>The path from floor 2 up to and including the floor of the fight.</summary>
        public required IReadOnlyList<Step> Path { get; init; }

        public required int Floor { get; init; }
        public required string Encounter { get; init; }

        /// <summary>Hallway fights and elites won before this fight.</summary>
        public required int HallwayFightsWon { get; init; }
        public required int ElitesWon { get; init; }

        public required NeowStart Neow { get; init; }
        public required IReadOnlyList<DeckCard> Deck { get; init; }

        /// <summary>Cards added to the starter, in the order they came.</summary>
        public required IReadOnlyList<DeckCard> Added { get; init; }

        /// <summary>Class names of the cards upgraded on the way (Pomander, Rest Sites).</summary>
        public required IReadOnlyList<string> Upgraded { get; init; }

        /// <summary>Class names of the cards removed on the way (Precise Scissors, a shop).</summary>
        public required IReadOnlyList<string> Removed { get; init; }

        public required IReadOnlyList<Offer> Offers { get; init; }

        /// <summary>Potions dropped on the way, whether or not the belt had room and whether or not they were drunk since.</summary>
        public required int PotionsDropped { get; init; }

        public required int Hp { get; init; }
        public required int MaxHp { get; init; }

        /// <summary>The belt by slot; null for an empty slot.</summary>
        public required IReadOnlyList<string?> Potions { get; init; }

        public required string Summary { get; init; }
    }

    /// <summary>
    /// Works out the scenario of a tier and a seed. Every random number comes from streams of
    /// the seed, one per kind of decision, so a change to one kind does not shift the others.
    /// </summary>
    /// <exception cref="InvalidOperationException">The catalog has no encounter for the tier.</exception>
    internal static Plan Build(string tier, int seed, Catalog catalog)
    {
        var kind = tier switch
        {
            WeakTier => EncounterKind.Weak,
            NormalTier => EncounterKind.Regular,
            EliteTier => EncounterKind.Elite,
            BossTier => EncounterKind.Boss,
            _ => throw new ArgumentException($"'{tier}' is not a scenario tier ({string.Join(", ", Tiers)}).", nameof(tier)),
        };
        if (!catalog.Has(kind))
        {
            var wanted = string.Join(", ", Roster.Where(e => e.Kind == kind).Select(e => e.ClassName));
            throw new InvalidOperationException(
                $"The arena cannot make a fight of the tier '{tier}': none of the act's {kind.ToString().ToLowerInvariant()} encounters is in the registry ({wanted}). " +
                "Add the encounter content, or take the tier out of Scenarios.TierPattern.");
        }

        // The run up to the fight. A run whose fight the arena cannot stage (the path has no such
        // room, or the encounter is left out) is thrown away and the next one is drawn.
        var actRng = RngSet.For(seed, "scenario_act");
        var mapRng = RngSet.For(seed, "scenario_map");
        var unknownRng = RngSet.For(seed, "scenario_unknown");
        const int mostRuns = 100_000;
        for (var run = 0; run < mostRuns; run++)
        {
            var lists = BuildActLists(actRng);
            var path = BuildPath(mapRng, unknownRng);
            if (ChooseMoment(tier, path, mapRng) is not { } moment) continue;

            var hallwayBefore = path.Take(moment).Count(s => s.Room == Room.Monster);
            var elitesBefore = path.Take(moment).Count(s => s.Room == Room.Elite);
            var encounter = path[moment].Room switch
            {
                Room.Monster => lists.Hallway[hallwayBefore],
                Room.Elite => lists.Elites[elitesBefore],
                _ => lists.Boss,
            };
            if (!catalog.Encounters.Contains(encounter)) continue;

            return Walk(tier, seed, catalog, path.Take(moment + 1).ToList(), encounter, hallwayBefore, elitesBefore);
        }
        throw new InvalidOperationException($"No run in {mostRuns} led to a fight of the tier '{tier}' that the arena has; seed {seed}.");
    }

    // ---------- the act's lists (game: run-generation.md 1.5) ----------

    /// <summary>
    /// The lists a run draws for the act before its first room: three weak encounters, then
    /// twelve regular ones, each from a bag that is emptied before it is refilled; fifteen elites
    /// the same way; one boss at random. Every draw prefers an encounter that is not the one
    /// before it and shares no tag with it.
    /// </summary>
    internal static ActLists BuildActLists(GameRng rng)
    {
        var hallway = new List<RosterEntry>(HallwayListLength);
        var bag = new List<RosterEntry>();
        for (var i = 0; i < WeakEncountersPerAct; i++)
        {
            if (bag.Count == 0) bag.AddRange(Roster.Where(e => e.Kind == EncounterKind.Weak));
            GrabWithoutRepeatingTags(hallway, bag, rng);
        }
        bag = new List<RosterEntry>();
        for (var i = WeakEncountersPerAct; i < HallwayListLength; i++)
        {
            if (bag.Count == 0) bag.AddRange(Roster.Where(e => e.Kind == EncounterKind.Regular));
            GrabWithoutRepeatingTags(hallway, bag, rng);
        }
        var elites = new List<RosterEntry>(EliteListLength);
        bag = new List<RosterEntry>();
        for (var i = 0; i < EliteListLength; i++)
        {
            if (bag.Count == 0) bag.AddRange(Roster.Where(e => e.Kind == EncounterKind.Elite));
            GrabWithoutRepeatingTags(elites, bag, rng);
        }
        // A profile that has met every boss of the act gets any of them with equal chance.
        var boss = rng.NextItem(Roster.Where(e => e.Kind == EncounterKind.Boss).ToList())!;
        return new ActLists(hallway.Select(e => e.ClassName).ToList(), elites.Select(e => e.ClassName).ToList(), boss.ClassName);
    }

    private static void GrabWithoutRepeatingTags(List<RosterEntry> list, List<RosterEntry> bag, GameRng rng)
    {
        var last = list.Count > 0 ? list[^1] : null;
        bool Fits(RosterEntry e) => last is null || (!e.SharesTagWith(last) && !ReferenceEquals(e, last));

        // The bag draws with equal weights until an entry fits; when none can fit, any entry is taken.
        int index;
        if (bag.Any(Fits))
        {
            do index = (int)(rng.NextDouble() * bag.Count);
            while (!Fits(bag[index]));
        }
        else
        {
            index = (int)(rng.NextDouble() * bag.Count);
        }
        list.Add(bag[index]);
        bag.RemoveAt(index);
    }

    // ---------- the path (structure: game; shares: DERIVED) ----------

    /// <summary>
    /// One way up the map, floor 2 to the boss on floor 17. The fixed floors and the bans (no
    /// Elite or Rest Site below floor 7, no Rest Site on floors 14 and 15, never two Elites, Rest
    /// Sites or Shops in a row, three Shop nodes at most) are the game's; the mix on the other
    /// floors is the measured one. Unknown rooms are settled as the game settles them.
    /// <para>
    /// Drawing floor by floor from the measured shares and then applying the bans gives paths
    /// close to the measured ones (per path about 6.7 Monster nodes, 1.1 Elites, 0.7 Shops and
    /// 3.0 Unknowns against 6.8, 1.1, 0.7 and 2.9), with a few Rest Sites less (2.3 against 2.5).
    /// </para>
    /// </summary>
    internal static List<Step> BuildPath(GameRng mapRng, GameRng unknownRng)
    {
        var path = new List<Step>(BossFloor - FirstFloor + 1);
        float monsterOdds = UnknownMonsterOdds, treasureOdds = UnknownTreasureOdds, shopOdds = UnknownShopOdds;
        var previousNode = MapNode.Boss;
        var previousRoom = Room.Boss;
        var shopNodes = 0;
        for (var floor = FirstFloor; floor <= BossFloor; floor++)
        {
            var node = floor switch
            {
                FirstFloor => MapNode.Monster,
                TreasureFloor => MapNode.Treasure,
                LastRestFloor => MapNode.Rest,
                BossFloor => MapNode.Boss,
                _ => DrawNode(floor < FirstEliteFloor ? EarlyFloorShares : floor < FirstLateFloor ? MiddleFloorShares : LateFloorShares,
                    previousNode, shopNodes < ShopNodesPerMap, mapRng),
            };
            if (node == MapNode.Shop) shopNodes++;

            var room = node switch
            {
                MapNode.Monster => Room.Monster,
                MapNode.Shop => Room.Shop,
                MapNode.Elite => Room.Elite,
                MapNode.Rest => Room.Rest,
                MapNode.Treasure => Room.Treasure,
                MapNode.Boss => Room.Boss,
                _ => RollUnknown(previousRoom == Room.Shop, ref monsterOdds, ref treasureOdds, ref shopOdds, unknownRng),
            };
            path.Add(new Step(floor, room, node == MapNode.Unknown));
            previousNode = node;
            previousRoom = room;
        }
        return path;
    }

    private static MapNode DrawNode((MapNode Node, double Share)[] shares, MapNode previous, bool shopLeft, GameRng rng)
    {
        var total = shares.Sum(s => s.Share);
        while (true)
        {
            var x = rng.NextDouble() * total;
            var node = shares[^1].Node;
            foreach (var (candidate, share) in shares)
            {
                x -= share;
                if (x >= 0) continue;
                node = candidate;
                break;
            }
            // Game: an Elite, Rest Site or Shop is never directly above one of its own kind.
            if (node == previous && node is MapNode.Elite or MapNode.Rest or MapNode.Shop) continue;
            if (node == MapNode.Shop && !shopLeft) continue;
            return node;
        }
    }

    /// <summary>
    /// Game: one roll against the growing odds, in the order Monster, Treasure, Shop, and an
    /// Event when none is met. The kind that was rolled goes back to its starting odds; the
    /// others grow by theirs. A Shop cannot follow a Shop: then it is not in the roll and does
    /// not grow. (The game also bars the Shop when every next room is one, which a single path
    /// cannot know.)
    /// </summary>
    internal static Room RollUnknown(bool afterShop, ref float monsterOdds, ref float treasureOdds, ref float shopOdds, GameRng rng)
    {
        // The run's own odds object does the roll (Sim/Run/RunOdds.cs); a scenario only carries the three numbers.
        var odds = new UnknownRoomOdds(monsterOdds, UnknownRoomOdds.BaseElite, treasureOdds, shopOdds);
        var rolled = odds.Roll(rng, UnknownRoomOdds.Allowed(shopBarred: afterShop));
        monsterOdds = odds.Monster;
        treasureOdds = odds.Treasure;
        shopOdds = odds.Shop;
        return rolled switch
        {
            RolledRoom.Monster => Room.Monster,
            RolledRoom.Treasure => Room.Treasure,
            RolledRoom.Shop => Room.Shop,
            _ => Room.Event,
        };
    }

    /// <summary>Which room of the path the scenario is about; null when the path has none for the tier.</summary>
    private static int? ChooseMoment(string tier, List<Step> path, GameRng rng)
    {
        var hallway = Enumerable.Range(0, path.Count).Where(i => path[i].Room == Room.Monster).ToList();
        switch (tier)
        {
            case WeakTier:
            {
                // One of the act's first three fights, each as likely as the others.
                var k = rng.NextInt(WeakEncountersPerAct);
                return k < hallway.Count ? hallway[k] : null;
            }
            case NormalTier:
            {
                var later = hallway.Skip(WeakEncountersPerAct).ToList();
                return later.Count > 0 ? rng.NextItem(later) : null;
            }
            case EliteTier:
            {
                var elites = Enumerable.Range(0, path.Count).Where(i => path[i].Room == Room.Elite).ToList();
                return elites.Count > 0 ? rng.NextItem(elites) : null;
            }
            default:
                return path.Count - 1;
        }
    }

    // ---------- what the rooms on the way did ----------

    private static Plan Walk(string tier, int seed, Catalog catalog, List<Step> path, string encounter, int hallwayBefore, int elitesBefore)
    {
        // The game rolls potions and cards on one stream; the player's own choices have another.
        var rewards = RngSet.For(seed, "scenario_rewards");
        var player = RngSet.For(seed, "scenario_player");

        var deck = new List<DeckCard>();
        for (var i = 0; i < 5; i++) deck.Add(new DeckCard(StrikeCard));
        for (var i = 0; i < 4; i++) deck.Add(new DeckCard(DefendCard));
        deck.Add(new DeckCard(BashCard));

        var added = new List<DeckCard>();
        var upgraded = new List<string>();
        var removed = new List<string>();
        var offers = new List<Offer>();
        var belt = new string?[PotionSlots];
        var maxHp = StartMaxHp;
        var rarityOffset = BaseRarityOffset;
        var potionOdds = BasePotionOdds;
        var potionsDropped = 0;
        var removalBought = false;

        void Upgrade(int index)
        {
            deck[index] = deck[index] with { Upgraded = true };
            upgraded.Add(deck[index].ClassName);
        }

        bool Remove(string className)
        {
            var index = deck.FindLastIndex(c => c.ClassName == className && !c.Upgraded);
            if (index < 0) index = deck.FindLastIndex(c => c.ClassName == className);
            if (index < 0) return false;
            deck.RemoveAt(index);
            removed.Add(className);
            return true;
        }

        // Neow, on floor 1.
        var neow = ChooseNeow(catalog, player);
        switch (neow)
        {
            case NeowStart.Pomander:
                // ESTIMATE: the card a player upgrades first is Bash.
                Upgrade(deck.FindIndex(c => c.ClassName == BashCard));
                break;
            case NeowStart.PreciseScissors:
                // ESTIMATE: the card a player removes first is a Strike.
                Remove(StrikeCard);
                break;
            case NeowStart.ArcaneScroll:
            {
                // Game: one of the pool's Rare cards, each as likely as the others, never upgraded.
                var rares = catalog.RewardCards.Where(c => c.Rarity == CardRarity.Rare).ToList();
                var card = new DeckCard(rewards.NextItem(rares).ClassName);
                deck.Add(card);
                added.Add(card);
                break;
            }
            case NeowStart.NutritiousOyster:
                maxHp += OysterMaxHp;
                break;
        }

        // Every room below the fight, in order.
        foreach (var step in path.Take(path.Count - 1))
        {
            switch (step.Room)
            {
                case Room.Monster:
                case Room.Elite:
                {
                    var elite = step.Room == Room.Elite;

                    // ESTIMATE: potions drunk in that fight. (The one that works by itself stays: the run is alive.)
                    for (var slot = 0; slot < belt.Length; slot++)
                    {
                        if (belt[slot] is null or AutomaticPotion) continue;
                        if (player.NextDouble() < (elite ? PotionUseChanceElite : PotionUseChanceHallway)) belt[slot] = null;
                    }

                    // Game: the potion roll comes first, then the three cards.
                    if (RollPotionDrop(elite, ref potionOdds, rewards))
                    {
                        potionsDropped++;
                        var potion = RollPotion(rewards, catalog);
                        // Game: a potion goes into the first empty slot, and with a full belt it cannot be taken.
                        // ESTIMATE: a player with a full belt leaves it lying.
                        var free = Array.IndexOf(belt, null);
                        if (potion is not null && free >= 0) belt[free] = potion;
                    }

                    var cards = RollCardReward(elite, ref rarityOffset, rewards, catalog);
                    var taken = -1;
                    if (cards.Count > 0 && player.NextDouble() < TakeCardChance)
                    {
                        taken = player.NextInt(cards.Count);
                        var card = new DeckCard(cards[taken].ClassName, cards[taken].Upgraded);
                        deck.Add(card);
                        added.Add(card);
                    }
                    offers.Add(new Offer(elite, cards, taken));
                    break;
                }
                case Room.Rest:
                {
                    // Game: heal or upgrade one card, not both. ESTIMATE: which, and which card.
                    if (player.NextDouble() >= SmithChance) break;
                    var candidates = Enumerable.Range(0, deck.Count).Where(i => !deck[i].Upgraded && catalog.CanUpgrade(deck[i].ClassName)).ToList();
                    var better = candidates.Where(i => deck[i].ClassName is not (StrikeCard or DefendCard)).ToList();
                    if (better.Count > 0) candidates = better;
                    if (candidates.Count > 0) Upgrade(player.NextItem(candidates));
                    break;
                }
                case Room.Shop:
                {
                    // Game: one removal per shop, 75 gold for the first. ESTIMATE: whether, and which card.
                    if (removalBought || player.NextDouble() >= RemovalChance) break;
                    removalBought = Remove(StrikeCard) || Remove(DefendCard);
                    break;
                }
                // A Treasure room gives a relic and an Event can do many things; neither is modelled:
                // the arena's fights have no relics beyond Burning Blood, and events are left alone.
            }
        }

        var moment = path[^1];
        var hp = EntryHp(moment, hallwayBefore, maxHp, player);
        var summary = Summarize(catalog, moment, hallwayBefore, elitesBefore, neow, deck, added, upgraded, removed, hp, maxHp, belt, encounter, seed);
        return new Plan
        {
            Tier = tier, Seed = seed, Path = path, Floor = moment.Floor, Encounter = encounter,
            HallwayFightsWon = hallwayBefore, ElitesWon = elitesBefore, Neow = neow,
            Deck = deck, Added = added, Upgraded = upgraded, Removed = removed, Offers = offers, PotionsDropped = potionsDropped,
            Hp = hp, MaxHp = maxHp, Potions = belt, Summary = summary,
        };
    }

    private static NeowStart ChooseNeow(Catalog catalog, GameRng player)
    {
        var roll = player.NextInt(NeowWeights.Sum(w => w.Weight));
        var start = NeowStart.NoChange;
        foreach (var (candidate, weight) in NeowWeights)
        {
            roll -= weight;
            if (roll >= 0) continue;
            start = candidate;
            break;
        }
        // Without a Rare card to give, the scroll is not on offer.
        if (start == NeowStart.ArcaneScroll && !catalog.RewardCards.Any(c => c.Rarity == CardRarity.Rare)) start = NeowStart.NoChange;
        return start;
    }

    /// <summary>HP on entering the fight. The first fight of the act is entered at full HP (game); the rest is ESTIMATE.</summary>
    private static int EntryHp(Step moment, int hallwayBefore, int maxHp, GameRng player)
    {
        if (moment.Room == Room.Monster && hallwayBefore == 0) return maxHp;
        var (min, max) = moment.Room == Room.Boss ? BossHp
            : moment.Room == Room.Monster && hallwayBefore < WeakEncountersPerAct ? EarlyHp
            : moment.Floor <= TreasureFloor ? MiddleHp
            : LateHp;
        // More max HP from Neow is HP the player has had since floor 1: the whole range moves up with it.
        var hp = min + player.NextInt(max - min + 1) + (maxHp - StartMaxHp);
        return Math.Clamp(hp, 1, maxHp);
    }

    // ---------- the game's rolls ----------

    /// <summary>
    /// Game: the rarity of one reward card. One roll: Rare below the rare odds plus the pity
    /// offset, Uncommon below that plus the uncommon odds, else Common. Then the offset goes back
    /// to its start after a Rare and grows after anything else.
    /// </summary>
    internal static CardRarity RollRarity(bool elite, ref float offset, GameRng rng)
    {
        var odds = new CardRarityOdds(offset);
        var rarity = odds.Roll(rng, elite ? CardOddsType.Elite : CardOddsType.Regular);
        offset = odds.Offset;
        return rarity;
    }

    /// <summary>
    /// Game: the cards one fight offers. Per card: the rarity roll; one of that rarity's cards
    /// that this offer does not show yet, each as likely as the others; then the upgrade roll
    /// (which never succeeds in act 1). A rarity with no card left gives way to the next one up,
    /// Rare wrapping to Common. A card the arena leaves out is not in the catalog, which is the
    /// same as rolling the card again until one that exists comes up.
    /// </summary>
    internal static List<OfferedCard> RollCardReward(bool elite, ref float offset, GameRng rng, Catalog catalog)
    {
        var offer = new List<OfferedCard>(CardsPerReward);
        for (var n = 0; n < CardsPerReward; n++)
        {
            var rolled = RollRarity(elite, ref offset, rng);

            List<(string ClassName, CardRarity Rarity)> Left(CardRarity rarity) =>
                catalog.RewardCards.Where(c => c.Rarity == rarity && offer.All(o => o.ClassName != c.ClassName)).ToList();

            var rarity = rolled;
            var options = Left(rarity);
            while (options.Count == 0)
            {
                rarity = rarity switch { CardRarity.Common => CardRarity.Uncommon, CardRarity.Uncommon => CardRarity.Rare, _ => CardRarity.Common };
                if (rarity == rolled) return offer; // nothing left at all: only a nearly empty registry gets here
                options = Left(rarity);
            }
            var card = rng.NextItem(options);

            var upgradeRoll = (decimal)rng.NextFloat();
            var upgradeChance = card.Rarity == CardRarity.Rare ? 0m : ActIndex * UpgradeChancePerAct;
            offer.Add(new OfferedCard(card.ClassName, card.Rarity, catalog.CanUpgrade(card.ClassName) && upgradeRoll <= upgradeChance));
        }
        return offer;
    }

    /// <summary>
    /// Game: whether a won fight drops a potion. One roll against the current odds (an elite adds
    /// half of its bonus); the odds then go down after a drop and up after none.
    /// </summary>
    internal static bool RollPotionDrop(bool elite, ref float odds, GameRng rng)
    {
        var potionOdds = new PotionOdds(odds);
        var dropped = potionOdds.Roll(rng, elite);
        odds = potionOdds.Chance;
        return dropped;
    }

    /// <summary>
    /// Game: which potion dropped. One roll for the rarity, then one of the pool's potions of that
    /// rarity. A potion the arena does not have is rolled again, rarity and all, so the ones it
    /// has keep the chances they have against each other. Null when it has none.
    /// </summary>
    internal static string? RollPotion(GameRng rng, Catalog catalog)
    {
        if (!PotionPool.Any(p => catalog.PotionNames.Contains(p.ClassName))) return null;
        while (true)
        {
            var roll = rng.NextFloat();
            var rarity = roll <= PotionRareThreshold ? PotionRarity.Rare : roll <= PotionUncommonThreshold ? PotionRarity.Uncommon : PotionRarity.Common;
            var potion = rng.NextItem(PotionPool.Where(p => p.Rarity == rarity).ToList()).ClassName;
            if (catalog.PotionNames.Contains(potion)) return potion;
        }
    }

    // ---------- the one line ----------

    private static string Summarize(Catalog catalog, Step moment, int hallwayBefore, int elitesBefore, NeowStart neow,
        List<DeckCard> deck, List<DeckCard> added, List<string> upgraded, List<string> removed, int hp, int maxHp, string?[] belt, string encounter, int seed)
    {
        var what = moment.Room switch
        {
            Room.Monster => $"fight {hallwayBefore + 1}",
            Room.Elite => $"elite {elitesBefore + 1}",
            _ => "boss",
        };

        // "Anger, Inflame x2": every name once, in the order it first came, with its count.
        static string Names(IEnumerable<string> titles) => string.Join(", ", titles
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => g.Count() > 1 ? string.Create(CultureInfo.InvariantCulture, $"{g.Key} x{g.Count()}") : g.Key));

        var changes = new List<string>();
        if (added.Count > 0) changes.Add("added " + Names(added.Select(c => catalog.CardTitle(c.ClassName) + (c.Upgraded ? "+" : ""))));
        if (upgraded.Count > 0) changes.Add("upgraded " + Names(upgraded.Select(catalog.CardTitle)));
        if (removed.Count > 0) changes.Add("removed " + Names(removed.Select(catalog.CardTitle)));
        if (neow != NeowStart.NoChange)
        {
            changes.Add("Neow gave " + neow switch
            {
                NeowStart.Pomander => "Pomander",
                NeowStart.PreciseScissors => "Precise Scissors",
                NeowStart.ArcaneScroll => "Arcane Scroll",
                _ => "Nutritious Oyster",
            });
        }
        var deckNote = changes.Count > 0 ? string.Join("; ", changes) : "the starter";

        var held = belt.Where(p => p is not null).Select(p => catalog.PotionTitle(p!)).ToList();
        var potions = held.Count > 0 ? "potions " + string.Join(", ", held) : "no potions";

        // " + " joins the monsters and stands nowhere else in the line: the self-check looks for it.
        var foes = catalog.LineUp(encounter, seed) is { Length: > 0 } lineUp ? $"{lineUp} ({encounter})" : encounter;

        return string.Create(CultureInfo.InvariantCulture,
            $"floor {moment.Floor} {what}: {hp}/{maxHp} HP, {deck.Count} cards ({deckNote}), {potions}, vs {foes}");
    }
}
