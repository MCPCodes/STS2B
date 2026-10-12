namespace SpireMind.Arena;

/// <summary>
/// Rooms of three runs, rolled number by number by a second reader: what
/// <see cref="RoomTracesChecks"/> holds the rewards, the shop, the chest and the rest site to.
/// <para>
/// Nothing in this file was made by the arena. Every name and number was printed by
/// <c>Sim/Run/Tools/trace_rooms.py</c>, a script that shares nothing with the arena's code: it
/// has its own generator, its own string hash and its own 32-bit float arithmetic, reads the
/// game's lists itself from the decompiled class files, makes both relic bags from the seed
/// and walks the rooms the way the game's code rolls them. Run with <c>trace</c> it prints
/// every number it draws and what the number decides, so each room below can be followed by
/// hand. The same script gives the game's own record of two real runs: on seed 1BUSLN0FY5
/// Barricade, both fights' gold, potion and cards, Stone Cracker and the whole shop of floor 5
/// with Tear Asunder at 77 gold; on seed 75F277YN2G the shop of floor 3 with Anger at 24 gold,
/// and Strike Dummy and 51 gold in the chest of floor 10.
/// </para>
/// <para>
/// The three seeds were looked for (the script's <c>find</c>): their first act is Overgrowth,
/// and on the streams their rooms meet nothing the arena had not built on 2026-10-11 (every
/// potion, both colorless cards of every shop), so the arena draws nothing again and each room
/// must be the game's own, number for number. Relics are another matter: most of the game's
/// are not built, and a pull then takes the next built relic of the same list without drawing
/// anything. So a room names the relic the GAME gives, and each run starts with its two relic
/// bags in the game's order, from which the checks work out which relic the arena shows.
/// </para>
/// <para>
/// A room code: <c>M</c> a hallway fight's rewards, <c>E</c> an elite's, <c>B</c> a boss's,
/// <c>M+23</c> a hallway fight that also hands back 23 stolen gold, <c>S</c> a shop, <c>R</c> a
/// rest site, <c>T</c> a chest. The three counters are how many numbers the streams
/// <c>rewards</c>, <c>shops</c> and <c>treasure_room_relics</c> have given.
/// </para>
/// </summary>
internal static class RoomTraces
{
    /// <summary>How many numbers the player's rewards stream, the player's shops stream and the run's treasure_room_relics stream have given.</summary>
    internal readonly record struct Counters(int Rewards, int Shops, int Treasure);

    /// <summary>A relic bag as the run starts, list by list in the game's order (front first). The shared bag's Shop list is never rolled and is left out.</summary>
    internal sealed record Bag(string[] Common, string[] Uncommon, string[] Rare, string[]? Shop = null)
    {
        internal string[] Of(RelicRarity rarity) => rarity switch
        {
            RelicRarity.Common => Common,
            RelicRarity.Uncommon => Uncommon,
            RelicRarity.Rare => Rare,
            RelicRarity.Shop => Shop ?? throw new InvalidOperationException("The shared bag's Shop list is not traced."),
            _ => throw new ArgumentOutOfRangeException(nameof(rarity)),
        };
    }

    internal abstract record Room(Counters Before, Counters After);

    /// <summary>The rewards of a won fight.</summary>
    /// <param name="Kind">M, E or B.</param>
    /// <param name="Stolen">Gold a thief took and the fight won back: a gold row of that amount the set was made with; 0 for none.</param>
    /// <param name="Gold">The room's own gold row.</param>
    /// <param name="Potion">The class name of the potion that dropped, or null.</param>
    /// <param name="Cards">The three cards of the card reward, in the order rolled.</param>
    /// <param name="Rarities">The rarity each card's roll gave.</param>
    /// <param name="RelicRarity">An elite's rolled relic rarity, else null.</param>
    /// <param name="Relic">The relic the game gives: the front of that list of the player's bag as the game has it then.</param>
    /// <param name="Offset">The card rarity (pity) offset after the room.</param>
    /// <param name="Chance">The potion chance after the room.</param>
    internal sealed record FightRoom(string Kind, int Stolen, Counters Before, Counters After, int Gold, string? Potion,
        string[] Cards, string[] Rarities, string? RelicRarity, string? Relic, float Offset, float Chance) : Room(Before, After);

    /// <summary>A shop's shelf.</summary>
    /// <param name="Sale">Which of the five character cards is on sale.</param>
    /// <param name="Cards">The five character cards, then the two colorless ones.</param>
    /// <param name="CardPrices">Their prices; the card on sale at its halved price.</param>
    /// <param name="RelicRarities">The two rolled rarities, then Shop.</param>
    /// <param name="Relics">The relics the game shelves: from the back of each list of the player's bag as the game has it then.</param>
    /// <param name="RelicPrices">Their prices. A relic the arena shelves instead is of the same list, so of the same table value.</param>
    internal sealed record ShopRoomTrace(Counters Before, Counters After, int Sale, string[] Cards, int[] CardPrices,
        string[] RelicRarities, string[] Relics, int[] RelicPrices, string[] Potions, int[] PotionPrices, int Removal) : Room(Before, After);

    /// <summary>A rest site where Rest is taken: no stream moves.</summary>
    internal sealed record RestRoom(Counters At) : Room(At, At);

    /// <summary>A chest: the relic is settled on entering, the gold is paid on opening.</summary>
    /// <param name="Entered">The counters once the room is entered.</param>
    /// <param name="Relic">The relic the game puts into the chest: the front of that list of the shared bag as the game has it then.</param>
    internal sealed record ChestRoom(Counters Before, Counters Entered, Counters After, string Rarity, string Relic, int Gold) : Room(Before, After);

    /// <summary>One run: its seed, its rooms in order, both relic bags as the run starts, and what each room rolled.</summary>
    internal sealed record Trace(string Seed, string Script, Bag Player, Bag Shared, Room[] Rooms);

    /// <summary>
    /// A shop whose first character card, first relic or first potion has a price that lies on a
    /// half before it is rounded, so that it tells the game's arithmetic from two others: the
    /// shop is rolled after the run's rewards stream was moved on to <paramref name="RewardsAt"/>
    /// and its shops stream to <paramref name="ShopsAt"/>.
    /// </summary>
    /// <param name="Kind">card, relic or potion.</param>
    /// <param name="Thing">The card's or the potion's class name; for a relic the rarity of its place.</param>
    /// <param name="TableValue">What the thing costs before the factor.</param>
    /// <param name="Factor">The rolled factor.</param>
    /// <param name="Price">The game's price: the product as a 32-bit float, a half to the even number.</param>
    /// <param name="HalfUp">What rounding a half up would give.</param>
    /// <param name="InDoubles">What the product worked out in 64-bit numbers would give.</param>
    internal sealed record PriceTie(string Seed, int RewardsAt, int ShopsAt, string Kind, string Thing, int TableValue, float Factor, int Price, int HalfUp, int InDoubles);

    /// <summary>The relics a shop never sells, as the game's relic classes say it (IsAllowedInShops false).</summary>
    internal static readonly string[] NeverSold = ["AmethystAubergine", "BowlerHat", "LuckyFysh", "OldCoin", "TheCourier"];

    internal static readonly Trace[] Runs =
    [
        new("QEVNVN1RKF", "M,M,E,S,M,R,E,M,B,T",
            new(
                ["BagOfPreparation", "RedMask", "OddlySmoothStone", "BagOfMarbles", "StrikeDummy", "Vajra", "Anchor", "RegalPillow",
                 "CentennialPuzzle", "Whetstone", "WarPaint", "RedSkull", "HappyFlower", "Gorget", "BloodVial", "AmethystAubergine",
                 "BookOfFiveRings", "MealTicket", "VenerableTeaSet", "Pendulum", "Lantern", "JuzuBracelet", "PotionBelt", "BronzeScales",
                 "Strawberry", "FestivePopper"],
                ["EternalFeather", "ParryingShield", "Pantograph", "Kusarigama", "SparklingRouge", "TuningFork", "JossPaper", "ReptileTrinket",
                 "PaperPhrog", "BowlerHat", "Vambrace", "SelfFormingClay", "PenNib", "PetrifiedToad", "RippleBasin", "OrnamentalFan",
                 "MiniatureCannon", "Planisphere", "GremlinHorn", "LastingCandy", "StoneCracker", "Permafrost", "Nunchaku", "TinyMailbox",
                 "MercuryHourglass", "Pear", "LetterOpener", "LuckyFysh", "HornCleat", "Orichalcum", "Candelabra", "Akabeko"],
                ["CloakClasp", "GamePiece", "TheCourier", "Girya", "CharonsAshes", "OldCoin", "ToxicEgg", "UnsettlingLamp",
                 "Chandelier", "WhiteStar", "Kunai", "RainbowRing", "UnceasingTop", "MoltenEgg", "PrayerWheel", "GamblingChip",
                 "MummifiedHand", "Pocketwatch", "LizardTail", "RuinedHelmet", "Mango", "FrozenEgg", "TungstenRod", "ArtOfWar",
                 "VexingPuzzlebox", "DemonTongue", "Shovel", "MeatOnTheBone", "IceCream", "SturdyClamp", "IntimidatingHelmet", "CaptainsWheel",
                 "Bellows", "StoneCalendar", "RazorTooth", "WhiteBeastStatue", "Shuriken", "BeatingRemnant"],
                ["SlingOfCourage", "DingyRug", "Orrery", "PunchDagger", "BeltBuckle", "Bread", "DollysMirror", "ScreamingFlagon",
                 "DragonFruit", "LavaLamp", "Toolbox", "RingingTriangle", "ChemicalX", "GhostSeed", "Brimstone", "MysticLighter",
                 "Cauldron", "TheAbacus", "RoyalStamp", "WingCharm", "LeesWaffle", "GnarledHammer", "MiniatureTent", "MembershipCard",
                 "Kifuda", "BurningSticks"]
            ),
            new(
                ["HappyFlower", "FestivePopper", "Strawberry", "AmethystAubergine", "BloodVial", "CentennialPuzzle", "PotionBelt", "JuzuBracelet",
                 "OddlySmoothStone", "RedMask", "Pendulum", "StrikeDummy", "MealTicket", "BagOfPreparation", "BookOfFiveRings", "VenerableTeaSet",
                 "BagOfMarbles", "Lantern", "Gorget", "BronzeScales", "Vajra", "WarPaint", "Whetstone", "Anchor",
                 "RegalPillow"],
                ["TuningFork", "BowlerHat", "Planisphere", "StoneCracker", "MercuryHourglass", "MiniatureCannon", "OrnamentalFan", "Pantograph",
                 "Candelabra", "Orichalcum", "Nunchaku", "EternalFeather", "HornCleat", "Pear", "LuckyFysh", "LetterOpener",
                 "TinyMailbox", "ParryingShield", "Akabeko", "Permafrost", "SparklingRouge", "RippleBasin", "JossPaper", "ReptileTrinket",
                 "GremlinHorn", "Vambrace", "PenNib", "LastingCandy", "Kusarigama", "PetrifiedToad"],
                ["Shuriken", "Pocketwatch", "Mango", "LizardTail", "PrayerWheel", "MoltenEgg", "TungstenRod", "UnsettlingLamp",
                 "SturdyClamp", "BeatingRemnant", "ArtOfWar", "RazorTooth", "ToxicEgg", "Kunai", "CloakClasp", "GamePiece",
                 "WhiteStar", "Chandelier", "RainbowRing", "IceCream", "UnceasingTop", "Girya", "StoneCalendar", "GamblingChip",
                 "WhiteBeastStatue", "TheCourier", "VexingPuzzlebox", "FrozenEgg", "OldCoin", "MummifiedHand", "IntimidatingHelmet", "MeatOnTheBone",
                 "Bellows", "Shovel", "CaptainsWheel"]
            ),
            [
                new FightRoom("M", 0, new(0, 0, 0), new(11, 0, 0), 16, null,
                    ["Bloodletting", "IronWave", "Headbutt"], ["Common", "Common", "Common"], null, null, -0.019999999552965164f, 0.5f),
                new FightRoom("M", 0, new(11, 0, 0), new(24, 0, 0), 15, "PotionOfBinding",
                    ["Bloodletting", "Havoc", "BloodWall"], ["Common", "Common", "Common"], null, null, 0.009999999776482582f, 0.4000000059604645f),
                new FightRoom("E", 0, new(24, 0, 0), new(36, 0, 0), 35, null,
                    ["TwinStrike", "Tremble", "AshenStrike"], ["Common", "Common", "Uncommon"], "Common", "BagOfPreparation", 0.03999999910593033f, 0.5f),
                new ShopRoomTrace(new(36, 0, 0), new(50, 28, 0), 4,
                    ["HowlFromBeyond", "MoltenFist", "Tremble", "Havoc", "Juggling", "Finesse", "Scrawl"], [75, 51, 48, 50, 38, 84, 167],
                    ["Rare", "Common", "Shop"], ["BeatingRemnant", "FestivePopper", "BurningSticks"], [302, 159, 190],
                    ["SpeedPotion", "PowerPotion", "HeartOfIron"], [49, 48, 72], 75),
                new FightRoom("M", 0, new(50, 28, 0), new(63, 28, 0), 19, "EnergyPotion",
                    ["Inferno", "BodySlam", "Headbutt"], ["Uncommon", "Common", "Common"], null, null, 0.06999999284744263f, 0.4000000059604645f),
                new RestRoom(new(63, 28, 0)),
                new FightRoom("E", 0, new(63, 28, 0), new(75, 28, 0), 42, null,
                    ["Havoc", "Dominate", "SecondWind"], ["Common", "Uncommon", "Uncommon"], "Uncommon", "EternalFeather", 0.09999998658895493f, 0.5f),
                new FightRoom("M", 0, new(75, 28, 0), new(88, 28, 0), 15, "WeakPotion",
                    ["BodySlam", "PerfectedStrike", "FightMe"], ["Common", "Common", "Uncommon"], null, null, 0.12999998033046722f, 0.4000000059604645f),
                new FightRoom("B", 0, new(88, 28, 0), new(99, 28, 0), 100, null,
                    ["NotYet", "Aggression", "Hellraiser"], ["Rare", "Rare", "Rare"], null, null, -0.05000000074505806f, 0.5f),
                new ChestRoom(new(99, 28, 0), new(99, 28, 1), new(100, 28, 1), "Uncommon", "TuningFork", 51),
            ]),
        new("YG4K5JKTX0", "S,M,E,M,M+23,R,S,E,B,T",
            new(
                ["VenerableTeaSet", "RedSkull", "BloodVial", "BookOfFiveRings", "MealTicket", "PotionBelt", "BronzeScales", "RegalPillow",
                 "BagOfMarbles", "HappyFlower", "Whetstone", "BagOfPreparation", "AmethystAubergine", "Pendulum", "FestivePopper", "StrikeDummy",
                 "Anchor", "CentennialPuzzle", "RedMask", "Gorget", "Lantern", "OddlySmoothStone", "JuzuBracelet", "Strawberry",
                 "WarPaint", "Vajra"],
                ["MercuryHourglass", "EternalFeather", "Pantograph", "LastingCandy", "Kusarigama", "HornCleat", "ReptileTrinket", "StoneCracker",
                 "Candelabra", "SparklingRouge", "Permafrost", "PetrifiedToad", "GremlinHorn", "PenNib", "SelfFormingClay", "PaperPhrog",
                 "LetterOpener", "LuckyFysh", "BowlerHat", "Akabeko", "Vambrace", "Nunchaku", "MiniatureCannon", "JossPaper",
                 "Planisphere", "TinyMailbox", "Orichalcum", "TuningFork", "ParryingShield", "Pear", "RippleBasin", "OrnamentalFan"],
                ["Shovel", "MummifiedHand", "Pocketwatch", "RazorTooth", "OldCoin", "MoltenEgg", "WhiteBeastStatue", "PrayerWheel",
                 "CloakClasp", "StoneCalendar", "MeatOnTheBone", "UnsettlingLamp", "WhiteStar", "FrozenEgg", "GamePiece", "LizardTail",
                 "SturdyClamp", "TungstenRod", "RuinedHelmet", "RainbowRing", "GamblingChip", "IntimidatingHelmet", "IceCream", "Bellows",
                 "ToxicEgg", "Shuriken", "BeatingRemnant", "ArtOfWar", "Girya", "VexingPuzzlebox", "UnceasingTop", "CaptainsWheel",
                 "Kunai", "Chandelier", "Mango", "TheCourier", "DemonTongue", "CharonsAshes"],
                ["MysticLighter", "ScreamingFlagon", "BeltBuckle", "ChemicalX", "Kifuda", "Brimstone", "MiniatureTent", "TheAbacus",
                 "WingCharm", "PunchDagger", "Toolbox", "DollysMirror", "RoyalStamp", "Orrery", "SlingOfCourage", "Cauldron",
                 "BurningSticks", "Bread", "RingingTriangle", "DingyRug", "GhostSeed", "LeesWaffle", "DragonFruit", "MembershipCard",
                 "GnarledHammer", "LavaLamp"]
            ),
            new(
                ["BloodVial", "HappyFlower", "FestivePopper", "VenerableTeaSet", "PotionBelt", "Gorget", "BookOfFiveRings", "Strawberry",
                 "Lantern", "Whetstone", "BronzeScales", "RegalPillow", "OddlySmoothStone", "StrikeDummy", "JuzuBracelet", "CentennialPuzzle",
                 "Pendulum", "Anchor", "RedMask", "Vajra", "BagOfPreparation", "BagOfMarbles", "WarPaint", "MealTicket",
                 "AmethystAubergine"],
                ["LetterOpener", "Candelabra", "BowlerHat", "TuningFork", "Pantograph", "RippleBasin", "MercuryHourglass", "OrnamentalFan",
                 "Pear", "Kusarigama", "PetrifiedToad", "HornCleat", "StoneCracker", "LuckyFysh", "SparklingRouge", "ParryingShield",
                 "TinyMailbox", "Planisphere", "PenNib", "ReptileTrinket", "Vambrace", "EternalFeather", "JossPaper", "Orichalcum",
                 "Akabeko", "LastingCandy", "GremlinHorn", "MiniatureCannon", "Permafrost", "Nunchaku"],
                ["SturdyClamp", "Kunai", "GamePiece", "CloakClasp", "Mango", "Pocketwatch", "BeatingRemnant", "FrozenEgg",
                 "StoneCalendar", "Bellows", "VexingPuzzlebox", "MeatOnTheBone", "Shuriken", "RainbowRing", "OldCoin", "TungstenRod",
                 "GamblingChip", "Shovel", "TheCourier", "Chandelier", "MoltenEgg", "IceCream", "ToxicEgg", "UnsettlingLamp",
                 "CaptainsWheel", "LizardTail", "IntimidatingHelmet", "WhiteStar", "RazorTooth", "Girya", "WhiteBeastStatue", "MummifiedHand",
                 "UnceasingTop", "PrayerWheel", "ArtOfWar"]
            ),
            [
                new ShopRoomTrace(new(0, 0, 0), new(14, 28, 0), 3,
                    ["MoltenFist", "TwinStrike", "BloodWall", "BurningPact", "Vicious", "UltimateStrike", "Salvo"], [52, 49, 52, 37, 78, 82, 168],
                    ["Common", "Common", "Shop"], ["Vajra", "WarPaint", "LavaLamp"], [167, 182, 175],
                    ["PowerPotion", "StableSerum", "TouchOfInsanity"], [49, 73, 74], 75),
                new FightRoom("M", 0, new(14, 28, 0), new(27, 28, 0), 20, "ExplosiveAmpoule",
                    ["Colossus", "Bloodletting", "PommelStrike"], ["Uncommon", "Common", "Common"], null, null, -0.019999999552965164f, 0.30000001192092896f),
                new FightRoom("E", 0, new(27, 28, 0), new(39, 28, 0), 43, null,
                    ["SecondWind", "SetupStrike", "Tremble"], ["Uncommon", "Common", "Common"], "Uncommon", "MercuryHourglass", 0.009999999776482582f, 0.4000000059604645f),
                new FightRoom("M", 0, new(39, 28, 0), new(52, 28, 0), 17, "FlexPotion",
                    ["Anger", "Cascade", "PerfectedStrike"], ["Common", "Rare", "Common"], null, null, -0.03999999910593033f, 0.30000001192092896f),
                new FightRoom("M", 23, new(52, 28, 0), new(64, 28, 0), 16, null,
                    ["Spite", "Bloodletting", "BurningPact"], ["Uncommon", "Common", "Uncommon"], null, null, -0.009999999776482582f, 0.4000000059604645f),
                new RestRoom(new(64, 28, 0)),
                new ShopRoomTrace(new(64, 28, 0), new(78, 56, 0), 3,
                    ["SetupStrike", "SwordBoomerang", "Havoc", "Bloodletting", "StoneArmor", "Fisticuffs", "MasterOfStrategy"], [49, 49, 49, 26, 75, 87, 177],
                    ["Common", "Common", "Shop"], ["Strawberry", "JuzuBracelet", "GnarledHammer"], [162, 195, 216],
                    ["WeakPotion", "SpeedPotion", "StrengthPotion"], [49, 48, 50], 75),
                new FightRoom("E", 0, new(78, 56, 0), new(92, 56, 0), 40, "FirePotion",
                    ["PommelStrike", "Breakthrough", "IronWave"], ["Common", "Common", "Common"], "Common", "VenerableTeaSet", 0.019999999552965164f, 0.30000001192092896f),
                new FightRoom("B", 0, new(92, 56, 0), new(105, 56, 0), 100, "PowderedDemise",
                    ["CrimsonMantle", "Hellraiser", "Juggernaut"], ["Rare", "Rare", "Rare"], null, null, -0.05000000074505806f, 0.20000001788139343f),
                new ChestRoom(new(105, 56, 0), new(105, 56, 1), new(106, 56, 1), "Rare", "SturdyClamp", 51),
            ]),
        new("KPJ8BS3DSJ", "M,E,E,M,S,M,M,R,B,T",
            new(
                ["CentennialPuzzle", "Anchor", "StrikeDummy", "BagOfMarbles", "PotionBelt", "JuzuBracelet", "VenerableTeaSet", "OddlySmoothStone",
                 "Gorget", "Strawberry", "BagOfPreparation", "RedSkull", "RedMask", "AmethystAubergine", "HappyFlower", "Pendulum",
                 "RegalPillow", "WarPaint", "Lantern", "BloodVial", "Vajra", "BookOfFiveRings", "FestivePopper", "BronzeScales",
                 "Whetstone", "MealTicket"],
                ["Pear", "LetterOpener", "ParryingShield", "BowlerHat", "Vambrace", "RippleBasin", "PaperPhrog", "JossPaper",
                 "Kusarigama", "HornCleat", "GremlinHorn", "Permafrost", "TinyMailbox", "LastingCandy", "Orichalcum", "OrnamentalFan",
                 "PetrifiedToad", "ReptileTrinket", "EternalFeather", "Nunchaku", "LuckyFysh", "MiniatureCannon", "StoneCracker", "PenNib",
                 "TuningFork", "SparklingRouge", "Akabeko", "Planisphere", "SelfFormingClay", "Pantograph", "Candelabra", "MercuryHourglass"],
                ["DemonTongue", "CloakClasp", "Shuriken", "TheCourier", "PrayerWheel", "MeatOnTheBone", "LizardTail", "IntimidatingHelmet",
                 "Kunai", "BeatingRemnant", "WhiteStar", "CaptainsWheel", "CharonsAshes", "MoltenEgg", "VexingPuzzlebox", "StoneCalendar",
                 "Mango", "RazorTooth", "OldCoin", "FrozenEgg", "Girya", "TungstenRod", "RuinedHelmet", "Bellows",
                 "ToxicEgg", "RainbowRing", "GamePiece", "ArtOfWar", "SturdyClamp", "WhiteBeastStatue", "UnsettlingLamp", "MummifiedHand",
                 "Shovel", "UnceasingTop", "GamblingChip", "Pocketwatch", "IceCream", "Chandelier"],
                ["GnarledHammer", "DollysMirror", "BeltBuckle", "MembershipCard", "Toolbox", "ChemicalX", "DragonFruit", "RingingTriangle",
                 "PunchDagger", "MysticLighter", "Bread", "Orrery", "MiniatureTent", "LeesWaffle", "Brimstone", "BurningSticks",
                 "DingyRug", "SlingOfCourage", "WingCharm", "LavaLamp", "TheAbacus", "ScreamingFlagon", "Kifuda", "Cauldron",
                 "GhostSeed", "RoyalStamp"]
            ),
            new(
                ["StrikeDummy", "Strawberry", "FestivePopper", "HappyFlower", "Vajra", "AmethystAubergine", "BronzeScales", "VenerableTeaSet",
                 "BagOfPreparation", "BookOfFiveRings", "BloodVial", "BagOfMarbles", "JuzuBracelet", "Anchor", "MealTicket", "OddlySmoothStone",
                 "RedMask", "Whetstone", "PotionBelt", "Pendulum", "CentennialPuzzle", "WarPaint", "Lantern", "RegalPillow",
                 "Gorget"],
                ["Nunchaku", "RippleBasin", "SparklingRouge", "LuckyFysh", "LetterOpener", "Kusarigama", "ParryingShield", "Orichalcum",
                 "BowlerHat", "ReptileTrinket", "OrnamentalFan", "Planisphere", "TinyMailbox", "EternalFeather", "StoneCracker", "PenNib",
                 "PetrifiedToad", "Pantograph", "Permafrost", "LastingCandy", "Akabeko", "HornCleat", "Vambrace", "MercuryHourglass",
                 "GremlinHorn", "Candelabra", "Pear", "MiniatureCannon", "TuningFork", "JossPaper"],
                ["GamblingChip", "RainbowRing", "BeatingRemnant", "VexingPuzzlebox", "SturdyClamp", "Chandelier", "RazorTooth", "ToxicEgg",
                 "MeatOnTheBone", "MoltenEgg", "StoneCalendar", "GamePiece", "Shuriken", "TheCourier", "Shovel", "FrozenEgg",
                 "CaptainsWheel", "Kunai", "WhiteStar", "ArtOfWar", "MummifiedHand", "IntimidatingHelmet", "Mango", "CloakClasp",
                 "UnceasingTop", "UnsettlingLamp", "TungstenRod", "IceCream", "Bellows", "Pocketwatch", "PrayerWheel", "WhiteBeastStatue",
                 "LizardTail", "OldCoin", "Girya"]
            ),
            [
                new FightRoom("M", 0, new(0, 0, 0), new(13, 0, 0), 19, "FruitJuice",
                    ["Breakthrough", "TwinStrike", "FlameBarrier"], ["Common", "Common", "Uncommon"], null, null, -0.019999999552965164f, 0.30000001192092896f),
                new FightRoom("E", 0, new(13, 0, 0), new(27, 0, 0), 45, "FruitJuice",
                    ["BloodWall", "Pillage", "Thunderclap"], ["Common", "Uncommon", "Common"], "Common", "CentennialPuzzle", 0.009999999776482582f, 0.20000001788139343f),
                new FightRoom("E", 0, new(27, 0, 0), new(39, 0, 0), 36, null,
                    ["DrumOfBattle", "Havoc", "Dismantle"], ["Uncommon", "Common", "Uncommon"], "Rare", "DemonTongue", 0.03999999910593033f, 0.30000001192092896f),
                new FightRoom("M", 0, new(39, 0, 0), new(50, 0, 0), 17, null,
                    ["Breakthrough", "FlameBarrier", "Inferno"], ["Common", "Uncommon", "Uncommon"], null, null, 0.06999999284744263f, 0.4000000059604645f),
                new ShopRoomTrace(new(50, 0, 0), new(64, 28, 0), 1,
                    ["IronWave", "Hemokinesis", "Armaments", "BurningPact", "Inferno", "Finesse", "MasterOfStrategy"], [52, 37, 51, 74, 78, 88, 179],
                    ["Rare", "Uncommon", "Shop"], ["Chandelier", "MercuryHourglass", "RoyalStamp"], [314, 201, 214],
                    ["StrengthPotion", "GamblersBrew", "EnergyPotion"], [50, 76, 49], 75),
                new FightRoom("M", 0, new(64, 28, 0), new(77, 28, 0), 17, "FirePotion",
                    ["MoltenFist", "Juggling", "Bully"], ["Common", "Uncommon", "Uncommon"], null, null, 0.09999998658895493f, 0.30000001192092896f),
                new FightRoom("M", 0, new(77, 28, 0), new(88, 28, 0), 19, null,
                    ["TrueGrit", "Hemokinesis", "ForgottenRitual"], ["Common", "Uncommon", "Uncommon"], null, null, 0.12999998033046722f, 0.4000000059604645f),
                new RestRoom(new(88, 28, 0)),
                new FightRoom("B", 0, new(88, 28, 0), new(101, 28, 0), 100, "LiquidBronze",
                    ["PrimalForce", "Offering", "Juggernaut"], ["Rare", "Rare", "Rare"], null, null, -0.05000000074505806f, 0.30000001192092896f),
                new ChestRoom(new(101, 28, 0), new(101, 28, 1), new(102, 28, 1), "Common", "StrikeDummy", 47),
            ]),
    ];

    internal static readonly PriceTie[] PriceTies =
    [
        new("QEVNVN1RKF", 4, 182708, "card", "TearAsunder", 150, 0.9899999499320984f, 148, 149, 148),   // 150 x 0.9899999499320984 is 148.49999248981476, as a 32-bit float 148.5
        new("QEVNVN1RKF", 3, 184008, "card", "Hemokinesis", 75, 1.0199999809265137f, 76, 77, 76),   // 75 x 1.0199999809265137 is 76.49999856948853, as a 32-bit float 76.5
        new("QEVNVN1RKF", 4, 249551, "card", "Thrash", 150, 0.9766666293144226f, 146, 147, 146),   // 150 x 0.9766666293144226 is 146.4999943971634, as a 32-bit float 146.5
        new("QEVNVN1RKF", 4, 252695, "card", "PactsEnd", 150, 1.0366666316986084f, 156, 156, 155),   // 150 x 1.0366666316986084 is 155.49999475479126, as a 32-bit float 155.5
        new("QEVNVN1RKF", 5, 63649, "relic", "Rare", 275, 0.9727272391319275f, 268, 268, 267),   // 275 x 0.9727272391319275 is 267.49999076128006, as a 32-bit float 267.5
        new("QEVNVN1RKF", 2, 244561, "relic", "Uncommon", 225, 0.9266666769981384f, 208, 209, 209),   // 225 x 0.9266666769981384 is 208.50000232458115, as a 32-bit float 208.5
        new("QEVNVN1RKF", 5, 932208, "relic", "Rare", 275, 0.8963636159896851f, 246, 247, 246),   // 275 x 0.8963636159896851 is 246.4999943971634, as a 32-bit float 246.5
        new("QEVNVN1RKF", 5, 1931319, "relic", "Rare", 275, 0.910909116268158f, 250, 251, 251),   // 275 x 0.910909116268158 is 250.50000697374344, as a 32-bit float 250.5
        new("KPJ8BS3DSJ", 0, 1011214, "potion", "PowerPotion", 50, 0.9700000286102295f, 48, 49, 49),   // 50 x 0.9700000286102295 is 48.500001430511475, as a 32-bit float 48.5
        new("KPJ8BS3DSJ", 0, 2382175, "potion", "RadiantTincture", 75, 1.0199999809265137f, 76, 77, 76),   // 75 x 1.0199999809265137 is 76.49999856948853, as a 32-bit float 76.5
        new("KPJ8BS3DSJ", 0, 22240487, "potion", "FlexPotion", 50, 1.0299999713897705f, 52, 52, 51),   // 50 x 1.0299999713897705 is 51.499998569488525, as a 32-bit float 51.5
    ];

    /// <summary>
    /// One purchase in a shop whose bought places are filled again (a relic says so; the game's
    /// The Courier), and what stands on the place afterwards.
    /// </summary>
    /// <param name="Buy">The place: <c>card3</c> is the character card of place 3, <c>potion1</c> the potion of place 1.</param>
    /// <param name="Paid">The price of what was bought.</param>
    /// <param name="Now">What the place holds after it was filled again.</param>
    /// <param name="Price">Its price. A card place filled again is never on sale.</param>
    /// <param name="After">The counters afterwards: a card takes two numbers of rewards (rarity, upgrade roll) and two of shops
    /// (pick, price); a potion three of shops (rarity, pick, price).</param>
    internal sealed record Refill(string Buy, int Paid, string Now, int Price, Counters After);

    /// <summary>
    /// The first room of the second traced run, the shop of seed YG4K5JKTX0, with six purchases
    /// (the script's room <c>S!card3/card0/potion1/card3/potion1/potion0</c>). Place 3 holds the
    /// card on sale; it is bought twice, the second time the card that took its place. A new
    /// card is one the shelf does not show, the card just bought included; a new potion is any
    /// potion of the list (its pick goes over all 16 potions of the rolled rarity, though the shelf shows some of them).
    /// Relic places are not among them: buying a relic obtains it, and which relic the arena
    /// shelves depends on what is built.
    /// </summary>
    internal static readonly Refill[] Refilled =
    [
        new("card3", 37, "ForgottenRitual", 77, new(16, 30, 0)),
        new("card0", 52, "Breakthrough", 51, new(18, 32, 0)),
        new("potion1", 73, "HeartOfIron", 73, new(18, 35, 0)),
        new("card3", 77, "DrumOfBattle", 72, new(20, 37, 0)),
        new("potion1", 73, "StrengthPotion", 50, new(20, 40, 0)),
        new("potion0", 49, "LiquidBronze", 75, new(20, 43, 0)),
    ];

    /// <summary>
    /// A seed's first hallway fight with one of the run's two odds set so that a roll lands exactly
    /// on its threshold (the script's <c>edges</c>). The game's tests are "below": on the
    /// threshold itself no potion drops and the card is of the lower rarity; one 32-bit float
    /// step more and the other thing happens.
    /// </summary>
    /// <param name="Offset">The pity offset the run starts with.</param>
    /// <param name="Chance">The potion chance the run starts with.</param>
    /// <param name="Potion">The potion that drops, or null.</param>
    /// <param name="FirstRarity">The rarity the first card's roll gives.</param>
    /// <param name="FirstCard">The first card.</param>
    /// <param name="Above">What a slightly higher threshold gives instead: a potion, or the first card's rarity.</param>
    internal sealed record Edge(string Seed, string What, float Offset, float Chance, string? Potion, string FirstRarity, string FirstCard, string Above);

    internal static readonly Edge[] Edges =
    [
        new("QEVNVN1RKF", "the potion roll equals the potion chance: no potion", -0.05000000074505806f, 0.8112423419952393f, null, "Common", "Bloodletting", "potion"),
        new("QEVNVN1RKF", "the first card's roll equals the Rare threshold: Uncommon", 0.6654372215270996f, 0.4000000059604645f, null, "Uncommon", "Colossus", "Rare"),   // the roll is 0.6954371929168701
        new("QEVNVN1RKF", "the first card's roll equals the Uncommon threshold: Common", 0.29543715715408325f, 0.4000000059604645f, null, "Common", "Bloodletting", "Uncommon"),   // the roll is 0.6954371929168701
    ];

    /// <summary>
    /// A room rolled after a stream was moved on to a place where its next deciding number is
    /// exactly a fixed threshold as a 32-bit float (found by looking through the seed's stream;
    /// the script's <c>trace</c> with <c>rewards=</c> or <c>treasure=</c> prints the room). A
    /// potion's rarity is tested "at or below" 0.1 and 0.35; a relic's "below" 0.5 and 0.83.
    /// </summary>
    /// <param name="Room">M: a hallway fight whose potion chance is 2, so that a potion drops; its rarity roll is the third number. T: a chest; its rarity roll is the first number of treasure_room_relics.</param>
    /// <param name="RewardsAt">Where the rewards stream is moved on to first.</param>
    /// <param name="TreasureAt">Where the treasure_room_relics stream is moved on to first.</param>
    /// <param name="Roll">The deciding number.</param>
    /// <param name="Rarity">What the game makes of it.</param>
    /// <param name="Potion">The potion the pick then gives.</param>
    internal sealed record ExactRoll(string Seed, string Room, int RewardsAt, int TreasureAt, float Roll, string Rarity, string? Potion = null);

    internal static readonly ExactRoll[] ExactRolls =
    [
        new("QEVNVN1RKF", "M", 20214450, 0, 0.35f, "Uncommon", "LiquidBronze"),   // rewards #20214453 is 0.34999998293331613: place 9 of the 16 Uncommon potions
        new("QEVNVN1RKF", "M", 40258660, 0, 0.1f, "Rare", "BeetleJuice"),         // rewards #40258663 is 0.10000000380980223: place 1 of the 16 Rare potions
        new("QEVNVN1RKF", "T", 0, 44872195, 0.83f, "Rare"),                       // treasure_room_relics #44872196 is 0.829999984394405
        new("QEVNVN1RKF", "T", 0, 74525714, 0.5f, "Uncommon"),                    // treasure_room_relics #74525715 is 0.5000000255108894
    ];

    /// <summary>
    /// One floor of the game's own record of a real run (a <c>.run</c> file of the game's history
    /// folder), as far as a room's rolls show in it.
    /// </summary>
    /// <param name="Room">The room code; <c>M+</c> is a hallway fight that also handed stolen gold back (the record does not say how much; any amount draws one number).</param>
    /// <param name="Gold">The gold the room gave: a fight's gold row, a chest's gold.</param>
    /// <param name="Cards">A fight: the three cards as the record lists them, a picked card first, the others in the order offered.
    /// A shop: the character cards that were left, in shelf order.</param>
    /// <param name="FirstWasPicked">The first of <paramref name="Cards"/> was taken; false when the card reward was skipped.</param>
    /// <param name="Potion">The potion that dropped.</param>
    /// <param name="Relics">An elite's relic, a chest's relic, a shop's three relics in shelf order.</param>
    /// <param name="Bought">A shop: the character card that was bought, which was the one on sale.</param>
    /// <param name="BoughtFor">What it cost.</param>
    /// <param name="Colorless">A shop: its two colorless cards.</param>
    internal sealed record RecordedRoom(int Floor, string Room, int Gold = 0, string[]? Cards = null, bool FirstWasPicked = false, string? Potion = null,
        string[]? Relics = null, string? Bought = null, int BoughtFor = 0, string[]? Colorless = null);

    /// <summary>
    /// A stretch of a recorded run that the rooms' entry points can walk. It starts where the
    /// record's run stood: the rewards stream at <paramref name="RewardsAt"/>, the two odds as
    /// they were, and the relics gone from the bags that the run had seen by then.
    /// </summary>
    internal sealed record RecordedRun(string Seed, string Record, int RewardsAt, float Offset, float Chance, string[] GoneFromTheBags, RecordedRoom[] Rooms);

    /// <summary>
    /// The winning run on seed 75F277YN2G (v0.107.1; sts2-notes/golden-saves/history/1791592972.run),
    /// floors 2 to 14, typed in from the record. Its first act is Underdocks, whose fights the
    /// arena cannot play; what a room ROLLS does not ask which act it stands in. Neow's relic
    /// was Pomander, which draws nothing. Floor 3: Anger was on sale and bought for 24 (99 gold
    /// spent, 75 of it for the removal of a Strike). Floor 9 was the Gremlin Merc, which hands
    /// stolen gold back. Floor 15 is an event, where the stretch ends.
    /// <para>
    /// The second stretch starts behind the chest of floor 10, so that the relic taken from it
    /// (which is not the game's while Strike Dummy is not built) cannot touch the later floors.
    /// Its start values are not in the record: the rewards stream stood at 85 by the count of
    /// the floors before, and the two odds are what <c>trace_rooms.py</c> has there (one card
    /// that was not Rare after the Demon Form of floor 9; five fights without a potion, one with).
    /// </para>
    /// </summary>
    internal static readonly RecordedRun[] Recorded =
    [
        new("75F277YN2G", "1791592972.run", 0, -0.05f, 0.4f, [],
            [
                new(2, "M", 16, ["TwinStrike", "Headbutt", "Thunderclap"], FirstWasPicked: true),
                new(3, "S", Cards: ["PommelStrike", "Taunt", "SecondWind", "Juggling"], Bought: "Anger", BoughtFor: 24,
                    Colorless: ["JackOfAllTrades", "SecretTechnique"], Relics: ["Lantern", "RedSkull", "BurningSticks"]),
                new(4, "M", 10, ["Bloodletting", "SetupStrike", "Spite"], FirstWasPicked: true),
                new(5, "M", 12, ["TwinStrike", "BodySlam", "SetupStrike"], FirstWasPicked: true),
                new(6, "M", 18, ["Inflame", "Stampede", "Hemokinesis"], FirstWasPicked: true),
                new(7, "E", 44, ["InfernalBlade", "ForgottenRitual", "Vicious"], Potion: "Ashwater", Relics: ["HappyFlower"]),
                new(8, "R"),
                new(9, "M+", 16, ["DemonForm", "Spite", "Cinder"], FirstWasPicked: true),
                new(10, "T", 51, Relics: ["StrikeDummy"]),
            ]),
        new("75F277YN2G", "1791592972.run", 85, -0.03999999910593033f, 0.8000000715255737f, ["Lantern", "RedSkull", "BurningSticks", "HappyFlower", "StrikeDummy"],
            [
                new(11, "M", 13, ["PerfectedStrike", "Tremble", "ForgottenRitual"], FirstWasPicked: true, Potion: "FyshOil"),
                new(12, "R"),
                new(13, "E", 43, ["Colossus", "BloodWall", "Cinder"], FirstWasPicked: true, Potion: "PowerPotion", Relics: ["BagOfMarbles"]),
                new(14, "M", 13, ["Breakthrough", "SetupStrike", "SwordBoomerang"], FirstWasPicked: true),
            ]),
    ];
}
