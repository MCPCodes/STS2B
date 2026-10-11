namespace SpireMind.Arena;

// The game's own lists (v0.107.1), by class name and in the game's order. A random pick or a
// shuffle of the game runs over one of these lists, so the order and the completeness matter:
// one name too few shifts every pick after it. Read from the game's files by script
// (pools, acts, epochs; the rarity from each relic's class); names and numbers only.
// Made by Sim/Run/Tools/game_lists.py: run it again after a game update, never edit by hand.
// RunStreamsChecks counts every list.

/// <summary>A relic's rarity, with the game's member order.</summary>
public enum RelicRarity { None, Starter, Common, Uncommon, Rare, Shop, Event, Ancient }

/// <summary>One relic of a pool. <paramref name="Epoch"/> is the timeline epoch a profile must have
/// revealed for the relic to be in its runs, or null when it is always there.</summary>
public sealed record GameRelic(string ClassName, RelicRarity Rarity, string? Epoch = null);

/// <summary>One encounter of an act: its room type, whether it is one of the act's weak
/// encounters, and its tags (the act's lists keep two encounters that share a tag apart).</summary>
public sealed record GameEncounter(string ClassName, RoomType Room, bool IsWeak, params string[] Tags)
{
    public bool SharesTagsWith(GameEncounter? other) => other is not null && Tags.Intersect(other.Tags).Any();
}

/// <summary>One Ancient an act can open with, and the epoch that unlocks it (null: always).</summary>
public sealed record GameAncient(string ClassName, string? Epoch = null);

/// <summary>One act as the game defines it.</summary>
/// <param name="Index">0 for a first act, 1 for a second, 2 for a third.</param>
/// <param name="IsDefault">The act a profile has from the start (Overgrowth, Hive, Glory).</param>
/// <param name="UnlockEpoch">The epoch that unlocks the act, or null.</param>
/// <param name="WeakEncounters">How many entries of the hallway list come from the weak encounters.</param>
/// <param name="Rooms">The length of the hallway list (the act's rooms between Ancient and boss, single player).</param>
/// <param name="Events">The act's own events in the game's order; the shared events follow them in a run's list.</param>
/// <param name="Encounters">Every encounter of the act in the game's order, which is the order the bags are filled in.</param>
/// <param name="BossDiscoveryOrder">A profile that has not met one of these gets the first unmet one as the boss.</param>
public sealed record GameAct(string ClassName, int Index, bool IsDefault, string? UnlockEpoch, int WeakEncounters, int Rooms,
    IReadOnlyList<string> Events, IReadOnlyList<GameEncounter> Encounters, IReadOnlyList<GameAncient> Ancients, IReadOnlyList<string> BossDiscoveryOrder)
{
    public IEnumerable<GameEncounter> Weak => Encounters.Where(e => e.Room == RoomType.Monster && e.IsWeak);
    public IEnumerable<GameEncounter> Regular => Encounters.Where(e => e.Room == RoomType.Monster && !e.IsWeak);
    public IEnumerable<GameEncounter> Elites => Encounters.Where(e => e.Room == RoomType.Elite);
    public IEnumerable<GameEncounter> Bosses => Encounters.Where(e => e.Room == RoomType.Boss);
}

public static class GameLists
{
    public const string Overgrowth = "Overgrowth";
    public const string Underdocks = "Underdocks";
    public const string Hive = "Hive";
    public const string Glory = "Glory";

    /// <summary>The 57 timeline epochs, by the id a save uses.</summary>
    public static readonly IReadOnlyList<string> Epochs =
    [
        "ACT2_B_EPOCH", "ACT3_B_EPOCH", "COLORLESS1_EPOCH", "COLORLESS2_EPOCH", "COLORLESS3_EPOCH", "COLORLESS4_EPOCH", "COLORLESS5_EPOCH",
        "CUSTOM_AND_SEEDS_EPOCH", "DAILY_RUN_EPOCH", "DARV_EPOCH", "DEFECT1_EPOCH", "DEFECT2_EPOCH", "DEFECT3_EPOCH", "DEFECT4_EPOCH",
        "DEFECT5_EPOCH", "DEFECT6_EPOCH", "DEFECT7_EPOCH", "EVENT1_EPOCH", "EVENT2_EPOCH", "EVENT3_EPOCH", "IRONCLAD2_EPOCH", "IRONCLAD3_EPOCH",
        "IRONCLAD4_EPOCH", "IRONCLAD5_EPOCH", "IRONCLAD6_EPOCH", "IRONCLAD7_EPOCH", "NECROBINDER1_EPOCH", "NECROBINDER2_EPOCH", "NECROBINDER3_EPOCH",
        "NECROBINDER4_EPOCH", "NECROBINDER5_EPOCH", "NECROBINDER6_EPOCH", "NECROBINDER7_EPOCH", "NEOW_EPOCH", "OROBAS_EPOCH", "POTION1_EPOCH",
        "POTION2_EPOCH", "REGENT1_EPOCH", "REGENT2_EPOCH", "REGENT3_EPOCH", "REGENT4_EPOCH", "REGENT5_EPOCH", "REGENT6_EPOCH", "REGENT7_EPOCH",
        "RELIC1_EPOCH", "RELIC2_EPOCH", "RELIC3_EPOCH", "RELIC4_EPOCH", "RELIC5_EPOCH", "SILENT1_EPOCH", "SILENT2_EPOCH", "SILENT3_EPOCH",
        "SILENT4_EPOCH", "SILENT5_EPOCH", "SILENT6_EPOCH", "SILENT7_EPOCH", "UNDERDOCKS_EPOCH",
    ];

    /// <summary>The shared relic pool: 118 relics in the game's order. A run's shared bag is filled from all of them,
    /// the player's bag from those of the four bag rarities.</summary>
    public static readonly IReadOnlyList<GameRelic> SharedRelics =
    [
        new("Akabeko", RelicRarity.Uncommon), new("AmethystAubergine", RelicRarity.Common), new("Anchor", RelicRarity.Common),
        new("ArtOfWar", RelicRarity.Rare), new("BagOfMarbles", RelicRarity.Common), new("BagOfPreparation", RelicRarity.Common),
        new("BeatingRemnant", RelicRarity.Rare, "RELIC5_EPOCH"), new("Bellows", RelicRarity.Rare), new("BeltBuckle", RelicRarity.Shop),
        new("BloodVial", RelicRarity.Common), new("BookOfFiveRings", RelicRarity.Common, "RELIC2_EPOCH"), new("BowlerHat", RelicRarity.Uncommon),
        new("Bread", RelicRarity.Shop), new("BronzeScales", RelicRarity.Common), new("BurningSticks", RelicRarity.Shop),
        new("Candelabra", RelicRarity.Uncommon), new("CaptainsWheel", RelicRarity.Rare), new("Cauldron", RelicRarity.Shop),
        new("CentennialPuzzle", RelicRarity.Common), new("Chandelier", RelicRarity.Rare), new("ChemicalX", RelicRarity.Shop),
        new("CloakClasp", RelicRarity.Rare), new("DingyRug", RelicRarity.Shop), new("DollysMirror", RelicRarity.Shop),
        new("DragonFruit", RelicRarity.Shop), new("EternalFeather", RelicRarity.Uncommon), new("FestivePopper", RelicRarity.Common, "RELIC3_EPOCH"),
        new("FresnelLens", RelicRarity.Event), new("FrozenEgg", RelicRarity.Rare), new("GamblingChip", RelicRarity.Rare),
        new("GamePiece", RelicRarity.Rare), new("GhostSeed", RelicRarity.Shop), new("Girya", RelicRarity.Rare),
        new("GnarledHammer", RelicRarity.Shop), new("Gorget", RelicRarity.Common), new("GremlinHorn", RelicRarity.Uncommon),
        new("HappyFlower", RelicRarity.Common), new("HornCleat", RelicRarity.Uncommon), new("IceCream", RelicRarity.Rare, "RELIC2_EPOCH"),
        new("IntimidatingHelmet", RelicRarity.Rare, "RELIC1_EPOCH"), new("JossPaper", RelicRarity.Uncommon, "RELIC5_EPOCH"),
        new("JuzuBracelet", RelicRarity.Common), new("Kifuda", RelicRarity.Shop), new("Kunai", RelicRarity.Rare),
        new("Kusarigama", RelicRarity.Uncommon, "RELIC2_EPOCH"), new("Lantern", RelicRarity.Common), new("LastingCandy", RelicRarity.Uncommon),
        new("LavaLamp", RelicRarity.Shop), new("LeesWaffle", RelicRarity.Shop), new("LetterOpener", RelicRarity.Uncommon),
        new("LizardTail", RelicRarity.Rare), new("LoomingFruit", RelicRarity.Ancient), new("LuckyFysh", RelicRarity.Uncommon),
        new("Mango", RelicRarity.Rare), new("MealTicket", RelicRarity.Common), new("MeatOnTheBone", RelicRarity.Rare),
        new("MembershipCard", RelicRarity.Shop), new("MercuryHourglass", RelicRarity.Uncommon),
        new("MiniatureCannon", RelicRarity.Uncommon, "RELIC4_EPOCH"), new("MiniatureTent", RelicRarity.Shop), new("MoltenEgg", RelicRarity.Rare),
        new("MummifiedHand", RelicRarity.Rare), new("MysticLighter", RelicRarity.Shop), new("Nunchaku", RelicRarity.Uncommon),
        new("OddlySmoothStone", RelicRarity.Common), new("OldCoin", RelicRarity.Rare), new("Orichalcum", RelicRarity.Uncommon),
        new("OrnamentalFan", RelicRarity.Uncommon), new("Orrery", RelicRarity.Shop), new("Pantograph", RelicRarity.Uncommon),
        new("ParryingShield", RelicRarity.Uncommon), new("Pear", RelicRarity.Uncommon), new("PenNib", RelicRarity.Uncommon),
        new("Pendulum", RelicRarity.Common), new("Permafrost", RelicRarity.Uncommon), new("PetrifiedToad", RelicRarity.Uncommon),
        new("Planisphere", RelicRarity.Uncommon), new("Pocketwatch", RelicRarity.Rare), new("PotionBelt", RelicRarity.Common),
        new("PrayerWheel", RelicRarity.Rare), new("PunchDagger", RelicRarity.Shop), new("RainbowRing", RelicRarity.Rare),
        new("RazorTooth", RelicRarity.Rare), new("RedMask", RelicRarity.Common), new("RegalPillow", RelicRarity.Common),
        new("ReptileTrinket", RelicRarity.Uncommon, "RELIC1_EPOCH"), new("RingingTriangle", RelicRarity.Shop),
        new("RippleBasin", RelicRarity.Uncommon, "RELIC3_EPOCH"), new("RoyalStamp", RelicRarity.Shop), new("ScreamingFlagon", RelicRarity.Shop),
        new("Shovel", RelicRarity.Rare), new("Shuriken", RelicRarity.Rare), new("SlingOfCourage", RelicRarity.Shop),
        new("SparklingRouge", RelicRarity.Uncommon), new("StoneCalendar", RelicRarity.Rare), new("StoneCracker", RelicRarity.Uncommon),
        new("Strawberry", RelicRarity.Common), new("StrikeDummy", RelicRarity.Common), new("SturdyClamp", RelicRarity.Rare),
        new("TheAbacus", RelicRarity.Shop), new("TheCourier", RelicRarity.Rare), new("TinyMailbox", RelicRarity.Uncommon, "RELIC5_EPOCH"),
        new("Toolbox", RelicRarity.Shop), new("ToxicEgg", RelicRarity.Rare), new("TungstenRod", RelicRarity.Rare, "RELIC4_EPOCH"),
        new("TuningFork", RelicRarity.Uncommon), new("UnceasingTop", RelicRarity.Rare), new("UnsettlingLamp", RelicRarity.Rare, "RELIC1_EPOCH"),
        new("Vajra", RelicRarity.Common), new("Vambrace", RelicRarity.Uncommon), new("VenerableTeaSet", RelicRarity.Common),
        new("VeryHotCocoa", RelicRarity.Ancient), new("VexingPuzzlebox", RelicRarity.Rare, "RELIC3_EPOCH"), new("WarPaint", RelicRarity.Common),
        new("Whetstone", RelicRarity.Common), new("WhiteBeastStatue", RelicRarity.Rare), new("WhiteStar", RelicRarity.Rare, "RELIC4_EPOCH"),
        new("WingCharm", RelicRarity.Shop),
    ];

    /// <summary>Each character's own relic pool, 8 relics in the game's order (the first Starter one is its starting relic).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<GameRelic>> CharacterRelics = new Dictionary<string, IReadOnlyList<GameRelic>>(StringComparer.Ordinal)
    {
        ["Ironclad"] =
        [
            new("Brimstone", RelicRarity.Shop), new("BurningBlood", RelicRarity.Starter), new("CharonsAshes", RelicRarity.Rare, "IRONCLAD6_EPOCH"),
            new("DemonTongue", RelicRarity.Rare, "IRONCLAD6_EPOCH"), new("PaperPhrog", RelicRarity.Uncommon, "IRONCLAD3_EPOCH"),
            new("RedSkull", RelicRarity.Common, "IRONCLAD3_EPOCH"), new("RuinedHelmet", RelicRarity.Rare, "IRONCLAD3_EPOCH"),
            new("SelfFormingClay", RelicRarity.Uncommon, "IRONCLAD6_EPOCH"),
        ],
        ["Silent"] =
        [
            new("HelicalDart", RelicRarity.Rare, "SILENT6_EPOCH"), new("NinjaScroll", RelicRarity.Shop),
            new("PaperKrane", RelicRarity.Rare, "SILENT3_EPOCH"), new("RingOfTheSnake", RelicRarity.Starter),
            new("SneckoSkull", RelicRarity.Common, "SILENT6_EPOCH"), new("Tingsha", RelicRarity.Uncommon, "SILENT3_EPOCH"),
            new("ToughBandages", RelicRarity.Rare, "SILENT3_EPOCH"), new("TwistedFunnel", RelicRarity.Uncommon, "SILENT6_EPOCH"),
        ],
        ["Regent"] =
        [
            new("DivineRight", RelicRarity.Starter), new("FencingManual", RelicRarity.Common, "REGENT3_EPOCH"),
            new("GalacticDust", RelicRarity.Uncommon, "REGENT3_EPOCH"), new("LunarPastry", RelicRarity.Rare, "REGENT3_EPOCH"),
            new("MiniRegent", RelicRarity.Rare, "REGENT6_EPOCH"), new("OrangeDough", RelicRarity.Rare, "REGENT6_EPOCH"),
            new("Regalite", RelicRarity.Uncommon, "REGENT6_EPOCH"), new("VitruvianMinion", RelicRarity.Shop),
        ],
        ["Necrobinder"] =
        [
            new("BigHat", RelicRarity.Rare, "NECROBINDER6_EPOCH"), new("BoneFlute", RelicRarity.Common, "NECROBINDER3_EPOCH"),
            new("BookRepairKnife", RelicRarity.Uncommon, "NECROBINDER3_EPOCH"), new("Bookmark", RelicRarity.Rare, "NECROBINDER6_EPOCH"),
            new("BoundPhylactery", RelicRarity.Starter), new("FuneraryMask", RelicRarity.Uncommon, "NECROBINDER3_EPOCH"),
            new("IvoryTile", RelicRarity.Rare, "NECROBINDER6_EPOCH"), new("UndyingSigil", RelicRarity.Shop),
        ],
        ["Defect"] =
        [
            new("CrackedCore", RelicRarity.Starter), new("DataDisk", RelicRarity.Common, "DEFECT3_EPOCH"),
            new("EmotionChip", RelicRarity.Rare, "DEFECT6_EPOCH"), new("GoldPlatedCables", RelicRarity.Uncommon, "DEFECT6_EPOCH"),
            new("PowerCell", RelicRarity.Rare, "DEFECT6_EPOCH"), new("Metronome", RelicRarity.Rare, "DEFECT3_EPOCH"),
            new("RunicCapacitor", RelicRarity.Shop), new("SymbioticVirus", RelicRarity.Uncommon, "DEFECT3_EPOCH"),
        ],
    };

    /// <summary>The 18 events every act adds after its own, in the game's order.</summary>
    public static readonly IReadOnlyList<string> SharedEvents =
    [
        "BrainLeech", "CrystalSphere", "DollRoom", "FakeMerchant", "PotionCourier", "RanwidTheElder", "RelicTrader", "RoomFullOfCheese",
        "SelfHelpBook", "SlipperyBridge", "StoneOfAllTime", "Symbiote", "TeaMaster", "TheFutureOfPotions", "TheLegendsWereTrue", "ThisOrThat",
        "WarHistorianRepy", "WelcomeToWongos",
    ];

    /// <summary>Events a profile only meets once their epoch is revealed.</summary>
    public static readonly IReadOnlyDictionary<string, string> EventEpochs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["TrashHeap"] = "EVENT1_EPOCH",
        ["Reflections"] = "EVENT2_EPOCH",
        ["ColorfulPhilosophers"] = "EVENT3_EPOCH",
    };

    /// <summary>The Ancients that belong to no act; a run deals them to its second and third act.</summary>
    public static readonly IReadOnlyList<GameAncient> SharedAncients = [new("Darv", "DARV_EPOCH")];

    /// <summary>The four acts in the game's order. Two have index 0: a run rolls one of them as its first act.</summary>
    public static readonly IReadOnlyList<GameAct> Acts =
    [
        new("Overgrowth", Index: 0, IsDefault: true, UnlockEpoch: null, WeakEncounters: 3, Rooms: 15,
            Events:
            [
                "AromaOfChaos", "ByrdonisNest", "DenseVegetation", "JungleMazeAdventure", "LuminousChoir", "MorphicGrove", "SapphireSeed",
                "SunkenStatue", "TabletOfTruth", "UnrestSite", "Wellspring", "WhisperingHollow", "WoodCarvings",
            ],
            Encounters:
            [
                new("BygoneEffigyElite", RoomType.Elite, false), new("ByrdonisElite", RoomType.Elite, false),
                new("CeremonialBeastBoss", RoomType.Boss, false), new("CubexConstructNormal", RoomType.Monster, false),
                new("FlyconidNormal", RoomType.Monster, false, "Mushroom", "Slimes"), new("FogmogNormal", RoomType.Monster, false),
                new("FuzzyWurmCrawlerWeak", RoomType.Monster, true, "Crawler"), new("InkletsNormal", RoomType.Monster, false),
                new("MawlerNormal", RoomType.Monster, false), new("NibbitsNormal", RoomType.Monster, false),
                new("NibbitsWeak", RoomType.Monster, true, "Nibbit"), new("OvergrowthCrawlers", RoomType.Monster, false, "Shrinker", "Crawler"),
                new("PhrogParasiteElite", RoomType.Elite, false), new("RubyRaidersNormal", RoomType.Monster, false),
                new("ShrinkerBeetleWeak", RoomType.Monster, true, "Shrinker"), new("SlimesNormal", RoomType.Monster, false, "Slimes"),
                new("SlimesWeak", RoomType.Monster, true, "Slimes"), new("SlitheringStranglerNormal", RoomType.Monster, false),
                new("SnappingJaxfruitNormal", RoomType.Monster, false, "Mushroom"), new("TheKinBoss", RoomType.Boss, false),
                new("VantomBoss", RoomType.Boss, false), new("VineShamblerNormal", RoomType.Monster, false),
            ],
            Ancients: [new("Neow", "NEOW_EPOCH")],
            BossDiscoveryOrder: ["VantomBoss", "CeremonialBeastBoss", "TheKinBoss"]),
        new("Underdocks", Index: 0, IsDefault: false, UnlockEpoch: "UNDERDOCKS_EPOCH", WeakEncounters: 3, Rooms: 15,
            Events:
            [
                "AbyssalBaths", "DrowningBeacon", "EndlessConveyor", "PunchOff", "SpiralingWhirlpool", "SunkenStatue", "SunkenTreasury",
                "DoorsOfLightAndDark", "TrashHeap", "WaterloggedScriptorium",
            ],
            Encounters:
            [
                new("CorpseSlugsNormal", RoomType.Monster, false, "Slugs"), new("CorpseSlugsWeak", RoomType.Monster, true, "Slugs"),
                new("CultistsNormal", RoomType.Monster, false), new("FossilStalkerNormal", RoomType.Monster, false),
                new("GremlinMercNormal", RoomType.Monster, false), new("HauntedShipNormal", RoomType.Monster, false),
                new("LagavulinMatriarchBoss", RoomType.Boss, false), new("LivingFogNormal", RoomType.Monster, false),
                new("PhantasmalGardenersElite", RoomType.Elite, false), new("PunchConstructNormal", RoomType.Monster, false),
                new("SeapunkNormal", RoomType.Monster, false, "Seapunk"), new("SeapunkWeak", RoomType.Monster, true, "Seapunk"),
                new("SewerClamNormal", RoomType.Monster, false), new("SkulkingColonyElite", RoomType.Elite, false),
                new("SludgeSpinnerWeak", RoomType.Monster, true), new("SoulFyshBoss", RoomType.Boss, false),
                new("TerrorEelElite", RoomType.Elite, false), new("ToadpolesWeak", RoomType.Monster, true),
                new("TwoTailedRatsNormal", RoomType.Monster, false), new("WaterfallGiantBoss", RoomType.Boss, false),
            ],
            Ancients: [new("Neow", "NEOW_EPOCH")],
            BossDiscoveryOrder: ["WaterfallGiantBoss", "SoulFyshBoss", "LagavulinMatriarchBoss"]),
        new("Hive", Index: 1, IsDefault: true, UnlockEpoch: null, WeakEncounters: 2, Rooms: 14,
            Events:
            [
                "Amalgamator", "Bugslayer", "ColorfulPhilosophers", "ColossalFlower", "FieldOfManSizedHoles", "InfestedAutomaton", "LostWisp",
                "SpiritGrafter", "TheLanternKey", "ZenWeaver",
            ],
            Encounters:
            [
                new("BowlbugsNormal", RoomType.Monster, false, "Workers"), new("BowlbugsWeak", RoomType.Monster, true, "Workers"),
                new("ChompersNormal", RoomType.Monster, false, "Chomper"), new("DecimillipedeElite", RoomType.Elite, false),
                new("EntomancerElite", RoomType.Elite, false), new("ExoskeletonsNormal", RoomType.Monster, false, "Exoskeletons"),
                new("ExoskeletonsWeak", RoomType.Monster, true, "Exoskeletons"), new("HunterKillerNormal", RoomType.Monster, false),
                new("KaiserCrabBoss", RoomType.Boss, false), new("InfestedPrismsElite", RoomType.Elite, false),
                new("KnowledgeDemonBoss", RoomType.Boss, false), new("LouseProgenitorNormal", RoomType.Monster, false),
                new("MytesNormal", RoomType.Monster, false), new("OvicopterNormal", RoomType.Monster, false),
                new("SlumberingBeetleNormal", RoomType.Monster, false, "Workers"), new("SpinyToadNormal", RoomType.Monster, false),
                new("TheInsatiableBoss", RoomType.Boss, false), new("TheObscuraNormal", RoomType.Monster, false),
                new("ThievingHopperWeak", RoomType.Monster, true, "Thieves"), new("TunnelerWeak", RoomType.Monster, true, "Burrower"),
            ],
            Ancients: [new("Orobas", "OROBAS_EPOCH"), new("Pael"), new("Tezcatara")],
            BossDiscoveryOrder: ["TheInsatiableBoss", "KnowledgeDemonBoss", "KaiserCrabBoss"]),
        new("Glory", Index: 2, IsDefault: true, UnlockEpoch: null, WeakEncounters: 2, Rooms: 13,
            Events:
            [
                "BattlewornDummy", "GraveOfTheForgotten", "HungryForMushrooms", "Reflections", "RoundTeaParty", "Trial", "TinkerTime",
            ],
            Encounters:
            [
                new("AxebotsNormal", RoomType.Monster, false), new("ConstructMenagerieNormal", RoomType.Monster, false),
                new("DevotedSculptorWeak", RoomType.Monster, true), new("AeonglassBoss", RoomType.Boss, false),
                new("FabricatorNormal", RoomType.Monster, false), new("FrogKnightNormal", RoomType.Monster, false),
                new("GlobeHeadNormal", RoomType.Monster, false), new("KnightsElite", RoomType.Elite, false, "Knights"),
                new("MechaKnightElite", RoomType.Elite, false), new("OwlMagistrateNormal", RoomType.Monster, false),
                new("QueenBoss", RoomType.Boss, false), new("ScrollsOfBitingNormal", RoomType.Monster, false, "Scrolls"),
                new("ScrollsOfBitingWeak", RoomType.Monster, true, "Scrolls"), new("SlimedBerserkerNormal", RoomType.Monster, false),
                new("SoulNexusElite", RoomType.Elite, false), new("TestSubjectBoss", RoomType.Boss, false),
                new("TheLostAndForgottenNormal", RoomType.Monster, false), new("TurretOperatorWeak", RoomType.Monster, true),
            ],
            Ancients: [new("Nonupeipe"), new("Tanx"), new("Vakuu")],
            BossDiscoveryOrder: ["QueenBoss", "TestSubjectBoss", "AeonglassBoss"]),
    ];

    private static readonly Dictionary<string, GameRelic> RelicsByName =
        SharedRelics.Concat(CharacterRelics.Values.SelectMany(r => r)).ToDictionary(r => r.ClassName, StringComparer.Ordinal);

    /// <summary>The act of a class name.</summary>
    /// <exception cref="ArgumentException">No act has that name.</exception>
    public static GameAct Act(string className) =>
        Acts.FirstOrDefault(a => a.ClassName == className) ?? throw new ArgumentException($"'{className}' is not an act ({string.Join(", ", Acts.Select(a => a.ClassName))}).", nameof(className));

    /// <summary>The acts a run can have at each act index, in the game's order: [Overgrowth, Underdocks], [Hive], [Glory].</summary>
    public static IReadOnlyList<IReadOnlyList<GameAct>> ActsByIndex { get; } =
        Enumerable.Range(0, Acts.Max(a => a.Index) + 1).Select(i => (IReadOnlyList<GameAct>)Acts.Where(a => a.Index == i).ToList()).ToList();

    /// <summary>A relic of the shared pool or of a character's pool, by class name.</summary>
    /// <exception cref="ArgumentException">The name is in neither (an event or Ancient relic never sits in a bag).</exception>
    public static GameRelic BagRelic(string className) =>
        RelicsByName.TryGetValue(className, out var relic) ? relic
            : throw new ArgumentException($"'{className}' is not a relic of the shared pool or of a character's pool.", nameof(className));

    public static bool IsBagRelic(string className) => RelicsByName.ContainsKey(className);
}
