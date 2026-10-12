using System.Globalization;

namespace SpireMind.Arena;

/// <summary>How an Ironclad of act 1 "Overgrowth" comes by a relic.</summary>
public enum RelicSource
{
    /// <summary>The starting relic.</summary>
    Start,

    /// <summary>The player's relic bag: an elite's relic, a chest (shared relics only), a shop's three slots, a random-relic event or relic.</summary>
    Bag,

    /// <summary>One of Neow's options at the start of the run.</summary>
    Neow,

    /// <summary>An act 1 event hands it out by name.</summary>
    Event,
}

/// <summary>One relic an act 1 Ironclad can get.</summary>
/// <param name="ClassName">The game's class name.</param>
/// <param name="Source">How it is come by.</param>
/// <param name="Chunk">The chunk of the run-mode plan that builds it; 0 for what the arena had before run
/// mode; <see cref="ReachableRelics.Never"/> for a relic the plan leaves out even when the act is complete.</param>
public sealed record ReachableRelic(string ClassName, RelicSource Source, int Chunk);

/// <summary>
/// The 157 relics an Ironclad can get in a single-player run whose act 1 is Overgrowth
/// (v0.107.1, everything unlocked), by the game's class names: the starting relic, the 122 of
/// the player's relic bag (the shared pool's and the Ironclad's own relics of rarity Common,
/// Uncommon, Rare and Shop), the 27 Neow can offer in single player, and the 7 that Overgrowth's
/// and the shared act 1 events hand out by name. The other 140 relic classes of the game cannot
/// turn up: other characters', the Ancients of acts 2 and 3, other acts' events, starter upgrades.
/// <para>
/// <see cref="Completeness"/> holds the registry to this list: every relic of it is registered
/// or left out by name with a reason, and nothing else is registered. The leaving out is done
/// here (<see cref="ReachableRelicsLeftOut"/>): a relic of the list that has no class yet is
/// left out with the chunk of the plan that will build it as the reason. So a chunk that builds
/// a relic writes its class (top-level, named as the game names it) and registers it, and edits
/// nothing here.
/// </para>
/// </summary>
public static class ReachableRelics
{
    /// <summary>The <see cref="ReachableRelic.Chunk"/> of a relic no chunk of the plan builds.</summary>
    public const int Never = -1;

    /// <summary>The relic chunks of the run-mode plan by number (chunks.json).</summary>
    public static readonly IReadOnlyDictionary<int, string> Chunks = new Dictionary<int, string>
    {
        [12] = "relics-first", [14] = "neow-core", [15] = "events-a", [22] = "neow-rest", [23] = "relics-fight-b", [24] = "events-b",
        [25] = "relics-rooms", [26] = "relics-fight-c", [28] = "colorless-c-and-users", [29] = "relics-complex", [30] = "relics-counters",
        [32] = "enchantment-users", [33] = "neows-bones-and-last-curses", [35] = "pets-and-byrdonis",
    };

    // The plan's parts list (relics.json for what is reachable and how, chunks.json for who builds
    // it), group by group. Relics1Checks holds the bag's 122 to the game's own pools (GameLists).
    private static readonly (int Chunk, RelicSource Source, string[] Relics)[] Table =
    [
        (0, RelicSource.Start, ["BurningBlood"]),
        (12, RelicSource.Bag,
        [
            "Akabeko", "Anchor", "BagOfPreparation", "Bellows", "Bread", "Brimstone", "BronzeScales", "Candelabra", "CaptainsWheel", "Chandelier",
            "GamePiece", "HornCleat", "IceCream", "Lantern", "LeesWaffle", "Mango", "MercuryHourglass", "OddlySmoothStone", "Pear", "PetrifiedToad",
            "SlingOfCourage", "Strawberry", "TheAbacus", "TungstenRod", "Vajra",
        ]),
        (14, RelicSource.Neow,
        [
            "ArcaneScroll", "BoomingConch", "CursedPearl", "GoldenPearl", "HeftyTablet", "LeafyPoultice", "LostCoffer", "NeowsTalisman", "NeowsTorment",
            "NewLeaf", "NutritiousOyster", "Pomander", "PrecariousShears", "PreciseScissors",
        ]),
        (15, RelicSource.Event, ["SwordOfJade", "SwordOfStone"]),
        (22, RelicSource.Neow,
            ["FishingRod", "LargeCapsule", "LavaRock", "PhialHolster", "ScrollBoxes", "SilverCrucible", "SmallCapsule", "StoneHumidifier", "WingedBoots"]),
        (23, RelicSource.Bag,
        [
            "BagOfMarbles", "BloodVial", "CharonsAshes", "ChemicalX", "DemonTongue", "FestivePopper", "Gorget", "HappyFlower", "IntimidatingHelmet",
            "MiniatureCannon", "MummifiedHand", "PaperPhrog", "Pendulum", "RazorTooth", "RedMask", "SelfFormingClay", "StrikeDummy", "VenerableTeaSet",
        ]),
        (24, RelicSource.Event, ["BoneTea", "ChosenCheese", "EmberTea", "TeaOfDiscourtesy"]),
        (25, RelicSource.Bag,
        [
            "AmethystAubergine", "BowlerHat", "Cauldron", "DollysMirror", "DragonFruit", "EternalFeather", "JuzuBracelet", "LuckyFysh", "MealTicket",
            "MembershipCard", "MiniatureTent", "OldCoin", "Orrery", "Pantograph", "Planisphere", "PotionBelt", "PrayerWheel", "RegalPillow", "Shovel",
            "TinyMailbox", "WarPaint", "Whetstone", "WhiteBeastStatue", "WhiteStar",
        ]),
        (26, RelicSource.Bag,
        [
            "CentennialPuzzle", "CloakClasp", "GremlinHorn", "LizardTail", "Nunchaku", "Orichalcum", "ParryingShield", "Permafrost", "ReptileTrinket",
            "RingingTriangle", "ScreamingFlagon", "SparklingRouge", "StoneCracker", "SturdyClamp", "TuningFork", "UnceasingTop", "VexingPuzzlebox",
        ]),
        (28, RelicSource.Bag, ["DingyRug", "Toolbox"]),
        (28, RelicSource.Neow, ["LeadPaperweight"]),
        (29, RelicSource.Bag,
        [
            "BeatingRemnant", "BeltBuckle", "BookOfFiveRings", "BurningSticks", "FrozenEgg", "GamblingChip", "GhostSeed", "Girya", "LastingCandy",
            "LavaLamp", "MeatOnTheBone", "MoltenEgg", "RedSkull", "RuinedHelmet", "TheCourier", "ToxicEgg",
        ]),
        (30, RelicSource.Bag,
        [
            "ArtOfWar", "JossPaper", "Kunai", "Kusarigama", "LetterOpener", "OrnamentalFan", "PenNib", "Pocketwatch", "RainbowRing", "RippleBasin",
            "Shuriken", "StoneCalendar", "UnsettlingLamp", "Vambrace",
        ]),
        (32, RelicSource.Bag, ["GnarledHammer", "Kifuda", "MysticLighter", "PunchDagger", "RoyalStamp", "WingCharm"]),
        (32, RelicSource.Neow, ["SilkenTress"]),
        (33, RelicSource.Neow, ["NeowsBones"]),
        (35, RelicSource.Event, ["Byrdpip"]),
        (Never, RelicSource.Neow, ["Kaleidoscope"]),
    ];

    /// <summary>All 157, by class name.</summary>
    public static readonly IReadOnlyList<ReachableRelic> All = Table
        .SelectMany(group => group.Relics.Select(name => new ReachableRelic(name, group.Source, group.Chunk)))
        .OrderBy(relic => relic.ClassName, StringComparer.Ordinal)
        .ToList();

    private static readonly Dictionary<string, ReachableRelic> ByName = All
        .GroupBy(relic => relic.ClassName, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    /// <summary>The class names, in the order of <see cref="All"/>.</summary>
    public static IEnumerable<string> Names => All.Select(relic => relic.ClassName);

    /// <summary>The relic of this class name, or null when an act 1 Ironclad cannot get it.</summary>
    public static ReachableRelic? Find(string className) => ByName.GetValueOrDefault(className);

    /// <summary>The relics that come from one source, by class name.</summary>
    public static IEnumerable<ReachableRelic> From(RelicSource source) => All.Where(relic => relic.Source == source);

    /// <summary>
    /// True when the arena has a class for the relic: a top-level class of that name that is a
    /// <see cref="Relic"/>. (A check's own stand-in is nested in its checks class and does not
    /// count.) Such a relic is its module's to register.
    /// </summary>
    public static bool HasClass(string className) =>
        typeof(ReachableRelics).Assembly.GetType($"{typeof(ReachableRelics).Namespace}.{className}") is { IsAbstract: false } type
        && typeof(Relic).IsAssignableFrom(type);

    /// <summary>Why a relic of the list that has no class is left out, in one line.</summary>
    public static string WhyLeftOut(ReachableRelic relic) => relic.Chunk switch
    {
        Never when relic.ClassName == "Kaleidoscope" =>
            "it adds cards of the other four characters, whose card pools the arena does not have; the run-mode plan leaves it out even when Overgrowth is complete",
        Never => "no chunk of the run-mode plan builds it",
        _ => string.Create(CultureInfo.InvariantCulture,
            $"not built yet: chunk {relic.Chunk} ({Chunks.GetValueOrDefault(relic.Chunk, "?")}) of the run-mode plan builds it"),
    };
}

/// <summary>
/// Leaves out by name every relic of <see cref="ReachableRelics"/> that has no class yet, with
/// the reason <see cref="ReachableRelics.WhyLeftOut"/> gives. A pull from a relic bag passes
/// such a relic over (or, in strict mode, ends the run as unrepresentable and says this reason:
/// <see cref="RunRelics.WhyNotBuilt"/>), so it is never shown.
/// </summary>
public sealed class ReachableRelicsLeftOut : IContentModule
{
    public void Register(Registry r)
    {
        foreach (var relic in ReachableRelics.All)
            if (!ReachableRelics.HasClass(relic.ClassName)) r.Exclude(ContentKind.Relic, relic.ClassName, ReachableRelics.WhyLeftOut(relic));
    }
}
