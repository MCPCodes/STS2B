namespace SpireMind.Arena;

// The game's card pools (v0.107.1), by class name and in the game's order. A random pick of the
// game runs over one of these lists, so the order and the completeness matter: one name too few
// shifts every pick after it. Read from the game's files by script (each pool's list, each
// card's type, rarity and flags from its class, the epochs' card lists); names and numbers only.
// Made by Sim/Run/Tools/card_pools.py: run it again after a game update, never edit by hand.
// CardPoolsChecks counts every list.

/// <summary>
/// One card of a game pool, whether the arena has built it or not: what the game's own filters
/// ask of a card before a random pick: type, rarity, multiplayer only, may a fight generate it
/// (the game's CanBeGeneratedInCombat), may a random pick outside a fight make it (the game's
/// CanBeGeneratedByModifiers: Neow's Bones picks its curses among those), and the timeline
/// epoch a profile must have revealed.
/// </summary>
public sealed record GameCard(string ClassName, CardType Type, CardRarity Rarity, bool MultiplayerOnly = false, bool GeneratedInCombat = true,
    bool GeneratedByModifiers = true, string? Epoch = null);

/// <summary>The lists <see cref="CardPools"/> is built from. Use the pools, not these.</summary>
internal static class CardPoolLists
{
    /// <summary>The game looks a card's pool up in this order (the five characters' pools first, then the shared ones) and takes the first that holds it.</summary>
    public static readonly IReadOnlyList<string> GameOrder = ["Ironclad", "Silent", "Regent", "Necrobinder", "Defect", "Colorless", "Curse", "Deprecated", "Event", "Quest", "Status", "Token"];

    /// <summary>The Ironclad's own cards: card rewards, shops, and what a transformed Ironclad card becomes. 87 cards.</summary>
    public static readonly IReadOnlyList<GameCard> Ironclad =
    [
        new("Aggression", CardType.Power, CardRarity.Rare), new("Anger", CardType.Attack, CardRarity.Common),
        new("Armaments", CardType.Skill, CardRarity.Common), new("AshenStrike", CardType.Attack, CardRarity.Uncommon),
        new("Barricade", CardType.Power, CardRarity.Rare), new("Bash", CardType.Attack, CardRarity.Basic),
        new("BattleTrance", CardType.Skill, CardRarity.Uncommon), new("BloodWall", CardType.Skill, CardRarity.Common, Epoch: "IRONCLAD7_EPOCH"),
        new("Bloodletting", CardType.Skill, CardRarity.Common), new("Bludgeon", CardType.Attack, CardRarity.Uncommon),
        new("BodySlam", CardType.Attack, CardRarity.Common), new("Brand", CardType.Skill, CardRarity.Rare),
        new("Break", CardType.Attack, CardRarity.Ancient), new("Breakthrough", CardType.Attack, CardRarity.Common),
        new("Bully", CardType.Attack, CardRarity.Uncommon), new("BurningPact", CardType.Skill, CardRarity.Uncommon),
        new("Cascade", CardType.Skill, CardRarity.Rare), new("Cinder", CardType.Attack, CardRarity.Common, Epoch: "IRONCLAD5_EPOCH"),
        new("Colossus", CardType.Skill, CardRarity.Uncommon), new("Conflagration", CardType.Attack, CardRarity.Rare),
        new("Corruption", CardType.Power, CardRarity.Ancient), new("CrimsonMantle", CardType.Power, CardRarity.Rare),
        new("Cruelty", CardType.Power, CardRarity.Rare, Epoch: "IRONCLAD2_EPOCH"), new("DarkEmbrace", CardType.Power, CardRarity.Rare),
        new("DefendIronclad", CardType.Skill, CardRarity.Basic), new("DemonForm", CardType.Power, CardRarity.Rare),
        new("DemonicShield", CardType.Skill, CardRarity.Uncommon, MultiplayerOnly: true), new("Dismantle", CardType.Attack, CardRarity.Uncommon),
        new("Dominate", CardType.Skill, CardRarity.Uncommon, Epoch: "IRONCLAD2_EPOCH"),
        new("DrumOfBattle", CardType.Skill, CardRarity.Uncommon, Epoch: "IRONCLAD5_EPOCH"), new("EvilEye", CardType.Skill, CardRarity.Uncommon),
        new("ExpectAFight", CardType.Skill, CardRarity.Uncommon), new("Feed", CardType.Attack, CardRarity.Rare, GeneratedInCombat: false),
        new("FeelNoPain", CardType.Power, CardRarity.Uncommon), new("FiendFire", CardType.Attack, CardRarity.Rare),
        new("FightMe", CardType.Attack, CardRarity.Uncommon), new("FlameBarrier", CardType.Skill, CardRarity.Uncommon),
        new("ForgottenRitual", CardType.Skill, CardRarity.Uncommon), new("Havoc", CardType.Skill, CardRarity.Common),
        new("Headbutt", CardType.Attack, CardRarity.Common), new("Hellraiser", CardType.Power, CardRarity.Rare),
        new("Hemokinesis", CardType.Attack, CardRarity.Uncommon), new("HowlFromBeyond", CardType.Attack, CardRarity.Uncommon),
        new("Impervious", CardType.Skill, CardRarity.Rare), new("InfernalBlade", CardType.Skill, CardRarity.Uncommon),
        new("Inferno", CardType.Power, CardRarity.Uncommon, Epoch: "IRONCLAD7_EPOCH"), new("Inflame", CardType.Power, CardRarity.Uncommon),
        new("IronWave", CardType.Attack, CardRarity.Common), new("Juggernaut", CardType.Power, CardRarity.Rare),
        new("Juggling", CardType.Power, CardRarity.Uncommon), new("Mangle", CardType.Attack, CardRarity.Rare),
        new("MoltenFist", CardType.Attack, CardRarity.Common, Epoch: "IRONCLAD2_EPOCH"),
        new("NotYet", CardType.Skill, CardRarity.Rare, GeneratedInCombat: false), new("Offering", CardType.Skill, CardRarity.Rare),
        new("OneTwoPunch", CardType.Skill, CardRarity.Rare), new("PactsEnd", CardType.Attack, CardRarity.Rare, Epoch: "IRONCLAD5_EPOCH"),
        new("PerfectedStrike", CardType.Attack, CardRarity.Common), new("Pillage", CardType.Attack, CardRarity.Uncommon),
        new("PommelStrike", CardType.Attack, CardRarity.Common), new("PrimalForce", CardType.Skill, CardRarity.Rare),
        new("Pyre", CardType.Power, CardRarity.Rare), new("Rage", CardType.Skill, CardRarity.Uncommon),
        new("Rampage", CardType.Attack, CardRarity.Uncommon), new("Rupture", CardType.Power, CardRarity.Uncommon),
        new("SecondWind", CardType.Skill, CardRarity.Uncommon), new("SetupStrike", CardType.Attack, CardRarity.Common),
        new("ShrugItOff", CardType.Skill, CardRarity.Common), new("Spite", CardType.Attack, CardRarity.Uncommon),
        new("Stampede", CardType.Power, CardRarity.Uncommon), new("Stoke", CardType.Skill, CardRarity.Rare),
        new("Stomp", CardType.Attack, CardRarity.Uncommon), new("StoneArmor", CardType.Power, CardRarity.Uncommon),
        new("StrikeIronclad", CardType.Attack, CardRarity.Basic), new("SwordBoomerang", CardType.Attack, CardRarity.Common),
        new("Tank", CardType.Power, CardRarity.Rare, MultiplayerOnly: true), new("Taunt", CardType.Skill, CardRarity.Uncommon),
        new("TearAsunder", CardType.Attack, CardRarity.Rare, Epoch: "IRONCLAD7_EPOCH"), new("Thrash", CardType.Attack, CardRarity.Rare),
        new("Thunderclap", CardType.Attack, CardRarity.Common), new("Tremble", CardType.Skill, CardRarity.Common),
        new("TrueGrit", CardType.Skill, CardRarity.Common), new("TwinStrike", CardType.Attack, CardRarity.Common),
        new("Unmovable", CardType.Power, CardRarity.Rare), new("Unrelenting", CardType.Attack, CardRarity.Uncommon),
        new("Uppercut", CardType.Attack, CardRarity.Uncommon), new("Vicious", CardType.Power, CardRarity.Uncommon),
        new("Whirlwind", CardType.Attack, CardRarity.Uncommon),
    ];

    /// <summary>The colorless cards: a shop's two colorless slots, and what a transformed quest card or Event-, Ancient- or Token-rarity card becomes. 64 cards.</summary>
    public static readonly IReadOnlyList<GameCard> Colorless =
    [
        new("Alchemize", CardType.Skill, CardRarity.Rare, GeneratedInCombat: false, Epoch: "COLORLESS4_EPOCH"),
        new("Anointed", CardType.Skill, CardRarity.Rare, Epoch: "COLORLESS5_EPOCH"),
        new("Automation", CardType.Power, CardRarity.Uncommon, Epoch: "COLORLESS1_EPOCH"),
        new("BeaconOfHope", CardType.Power, CardRarity.Rare, MultiplayerOnly: true),
        new("BeatDown", CardType.Skill, CardRarity.Rare, Epoch: "COLORLESS3_EPOCH"),
        new("BelieveInYou", CardType.Skill, CardRarity.Uncommon, MultiplayerOnly: true), new("Bolas", CardType.Attack, CardRarity.Rare),
        new("Calamity", CardType.Power, CardRarity.Rare, Epoch: "COLORLESS5_EPOCH"),
        new("Catastrophe", CardType.Skill, CardRarity.Uncommon, Epoch: "COLORLESS1_EPOCH"),
        new("Coordinate", CardType.Skill, CardRarity.Uncommon, MultiplayerOnly: true), new("DarkShackles", CardType.Skill, CardRarity.Uncommon),
        new("Discovery", CardType.Skill, CardRarity.Uncommon), new("DramaticEntrance", CardType.Attack, CardRarity.Uncommon),
        new("Entropy", CardType.Power, CardRarity.Rare, Epoch: "COLORLESS1_EPOCH"), new("Equilibrium", CardType.Skill, CardRarity.Uncommon),
        new("EternalArmor", CardType.Power, CardRarity.Rare, Epoch: "COLORLESS2_EPOCH"), new("Fasten", CardType.Power, CardRarity.Uncommon),
        new("Finesse", CardType.Skill, CardRarity.Uncommon), new("Fisticuffs", CardType.Attack, CardRarity.Uncommon),
        new("FlashOfSteel", CardType.Attack, CardRarity.Uncommon), new("GangUp", CardType.Attack, CardRarity.Uncommon, MultiplayerOnly: true),
        new("GoldAxe", CardType.Attack, CardRarity.Rare), new("HandOfGreed", CardType.Attack, CardRarity.Rare, GeneratedInCombat: false),
        new("HiddenGem", CardType.Skill, CardRarity.Rare, GeneratedInCombat: false),
        new("HuddleUp", CardType.Skill, CardRarity.Uncommon, MultiplayerOnly: true), new("Impatience", CardType.Skill, CardRarity.Uncommon),
        new("Intercept", CardType.Skill, CardRarity.Uncommon, MultiplayerOnly: true), new("JackOfAllTrades", CardType.Skill, CardRarity.Uncommon),
        new("Jackpot", CardType.Attack, CardRarity.Rare, Epoch: "COLORLESS2_EPOCH"),
        new("Knockdown", CardType.Attack, CardRarity.Rare, MultiplayerOnly: true),
        new("Lift", CardType.Skill, CardRarity.Uncommon, MultiplayerOnly: true), new("MasterOfStrategy", CardType.Skill, CardRarity.Rare),
        new("Mayhem", CardType.Power, CardRarity.Rare), new("Mimic", CardType.Skill, CardRarity.Rare, MultiplayerOnly: true),
        new("MindBlast", CardType.Attack, CardRarity.Uncommon), new("Nostalgia", CardType.Power, CardRarity.Rare, Epoch: "COLORLESS4_EPOCH"),
        new("Omnislice", CardType.Attack, CardRarity.Uncommon), new("Panache", CardType.Power, CardRarity.Uncommon),
        new("PanicButton", CardType.Skill, CardRarity.Uncommon), new("PrepTime", CardType.Power, CardRarity.Uncommon, Epoch: "COLORLESS2_EPOCH"),
        new("Production", CardType.Skill, CardRarity.Uncommon), new("Prolong", CardType.Skill, CardRarity.Uncommon),
        new("Prowess", CardType.Power, CardRarity.Uncommon, Epoch: "COLORLESS3_EPOCH"), new("Purity", CardType.Skill, CardRarity.Uncommon),
        new("Rally", CardType.Skill, CardRarity.Rare, MultiplayerOnly: true),
        new("Rend", CardType.Attack, CardRarity.Rare, Epoch: "COLORLESS3_EPOCH"), new("Restlessness", CardType.Skill, CardRarity.Uncommon),
        new("RollingBoulder", CardType.Power, CardRarity.Rare), new("Salvo", CardType.Attack, CardRarity.Rare),
        new("Scrawl", CardType.Skill, CardRarity.Rare, Epoch: "COLORLESS4_EPOCH"), new("SecretTechnique", CardType.Skill, CardRarity.Rare),
        new("SecretWeapon", CardType.Skill, CardRarity.Rare), new("SeekerStrike", CardType.Attack, CardRarity.Uncommon),
        new("Shockwave", CardType.Skill, CardRarity.Uncommon), new("Splash", CardType.Skill, CardRarity.Uncommon, Epoch: "COLORLESS5_EPOCH"),
        new("Stratagem", CardType.Power, CardRarity.Uncommon), new("TagTeam", CardType.Attack, CardRarity.Uncommon, MultiplayerOnly: true),
        new("TheBomb", CardType.Skill, CardRarity.Uncommon), new("TheGambit", CardType.Skill, CardRarity.Rare),
        new("ThinkingAhead", CardType.Skill, CardRarity.Uncommon), new("ThrummingHatchet", CardType.Attack, CardRarity.Uncommon),
        new("UltimateDefend", CardType.Skill, CardRarity.Uncommon), new("UltimateStrike", CardType.Attack, CardRarity.Uncommon),
        new("Volley", CardType.Attack, CardRarity.Uncommon),
    ];

    /// <summary>The curses. A transformed curse becomes any other of them. 18 cards.</summary>
    public static readonly IReadOnlyList<GameCard> Curse =
    [
        new("AscendersBane", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false),
        new("BadLuck", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false), new("Clumsy", CardType.Curse, CardRarity.Curse),
        new("CurseOfTheBell", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false), new("Debt", CardType.Curse, CardRarity.Curse),
        new("Decay", CardType.Curse, CardRarity.Curse), new("Doubt", CardType.Curse, CardRarity.Curse),
        new("Enthralled", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false),
        new("Folly", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false),
        new("Greed", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false), new("Guilty", CardType.Curse, CardRarity.Curse),
        new("Injury", CardType.Curse, CardRarity.Curse), new("Normality", CardType.Curse, CardRarity.Curse),
        new("PoorSleep", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false), new("Regret", CardType.Curse, CardRarity.Curse),
        new("Shame", CardType.Curse, CardRarity.Curse), new("SporeMind", CardType.Curse, CardRarity.Curse, GeneratedByModifiers: false),
        new("Writhe", CardType.Curse, CardRarity.Curse),
    ];

    /// <summary>Cards only events and Ancients give. 27 cards.</summary>
    public static readonly IReadOnlyList<GameCard> Event =
    [
        new("Apotheosis", CardType.Skill, CardRarity.Ancient), new("Apparition", CardType.Skill, CardRarity.Ancient),
        new("BrightestFlame", CardType.Skill, CardRarity.Ancient), new("ByrdSwoop", CardType.Attack, CardRarity.Event),
        new("Caltrops", CardType.Power, CardRarity.Event), new("Clash", CardType.Attack, CardRarity.Event),
        new("Distraction", CardType.Skill, CardRarity.Event), new("DualWield", CardType.Skill, CardRarity.Event),
        new("Enlightenment", CardType.Skill, CardRarity.Event), new("Entrench", CardType.Skill, CardRarity.Event),
        new("Exterminate", CardType.Attack, CardRarity.Event), new("FeedingFrenzy", CardType.Skill, CardRarity.Event),
        new("HelloWorld", CardType.Power, CardRarity.Event), new("MadScience", CardType.Attack, CardRarity.Event),
        new("Maul", CardType.Attack, CardRarity.Ancient), new("Metamorphosis", CardType.Skill, CardRarity.Event),
        new("NeowsFury", CardType.Attack, CardRarity.Ancient, GeneratedInCombat: false), new("Outmaneuver", CardType.Skill, CardRarity.Event),
        new("Peck", CardType.Attack, CardRarity.Event), new("Rebound", CardType.Attack, CardRarity.Event),
        new("Relax", CardType.Skill, CardRarity.Ancient), new("RipAndTear", CardType.Attack, CardRarity.Event),
        new("Squash", CardType.Attack, CardRarity.Event), new("Stack", CardType.Skill, CardRarity.Event),
        new("ToricToughness", CardType.Skill, CardRarity.Event), new("Wish", CardType.Skill, CardRarity.Ancient),
        new("Whistle", CardType.Attack, CardRarity.Ancient),
    ];

    /// <summary>The quest cards. 3 cards.</summary>
    public static readonly IReadOnlyList<GameCard> Quest =
    [
        new("ByrdonisEgg", CardType.Quest, CardRarity.Quest), new("LanternKey", CardType.Quest, CardRarity.Quest),
        new("SpoilsMap", CardType.Quest, CardRarity.Quest),
    ];

    /// <summary>The status cards monsters and cards make in a fight. 12 cards.</summary>
    public static readonly IReadOnlyList<GameCard> Status =
    [
        new("Beckon", CardType.Status, CardRarity.Status), new("Burn", CardType.Status, CardRarity.Status),
        new("Dazed", CardType.Status, CardRarity.Status), new("Debris", CardType.Status, CardRarity.Status),
        new("FranticEscape", CardType.Status, CardRarity.Status, GeneratedInCombat: false), new("Infection", CardType.Status, CardRarity.Status),
        new("Wither", CardType.Status, CardRarity.Status), new("Slimed", CardType.Status, CardRarity.Status),
        new("Soot", CardType.Status, CardRarity.Status, GeneratedInCombat: false), new("Toxic", CardType.Status, CardRarity.Status),
        new("Void", CardType.Status, CardRarity.Status), new("Wound", CardType.Status, CardRarity.Status),
    ];

    /// <summary>Cards only other cards and relics make. 14 cards.</summary>
    public static readonly IReadOnlyList<GameCard> Token =
    [
        new("Disintegration", CardType.Status, CardRarity.Status, GeneratedInCombat: false), new("Fuel", CardType.Skill, CardRarity.Token),
        new("GiantRock", CardType.Attack, CardRarity.Token), new("Luminesce", CardType.Skill, CardRarity.Token),
        new("MindRot", CardType.Status, CardRarity.Status, GeneratedInCombat: false), new("MinionDiveBomb", CardType.Attack, CardRarity.Token),
        new("MinionSacrifice", CardType.Skill, CardRarity.Token), new("MinionStrike", CardType.Attack, CardRarity.Token),
        new("Shiv", CardType.Attack, CardRarity.Token), new("Sloth", CardType.Status, CardRarity.Status, GeneratedInCombat: false),
        new("Soul", CardType.Skill, CardRarity.Token), new("SovereignBlade", CardType.Attack, CardRarity.Token),
        new("SweepingGaze", CardType.Attack, CardRarity.Token), new("WasteAway", CardType.Status, CardRarity.Status, GeneratedInCombat: false),
    ];

}
