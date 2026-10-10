namespace SpireMind.Arena;

/// <summary>
/// What the arena should hold of the game (v0.107.1, Ironclad, act 1 "Overgrowth") against what
/// its registry holds: every card of the Ironclad's pool, every status card, every Overgrowth
/// encounter and every potion an Ironclad can find is either registered or left out by name
/// with a reason. The lists are the game's own (its card pools, the act's encounter list, the
/// two potion pools), by class name. <c>arena content</c> prints the table; the self-check
/// fails when a name is neither registered nor left out.
/// </summary>
public static class Completeness
{
    /// <summary>The 87 cards of the game's Ironclad card pool, in its order (by class name).</summary>
    public static readonly IReadOnlyList<string> IroncladPool =
    [
        "Aggression", "Anger", "Armaments", "AshenStrike", "Barricade", "Bash", "BattleTrance", "BloodWall", "Bloodletting", "Bludgeon",
        "BodySlam", "Brand", "Break", "Breakthrough", "Bully", "BurningPact", "Cascade", "Cinder", "Colossus", "Conflagration",
        "Corruption", "CrimsonMantle", "Cruelty", "DarkEmbrace", "DefendIronclad", "DemonForm", "DemonicShield", "Dismantle", "Dominate", "DrumOfBattle",
        "EvilEye", "ExpectAFight", "Feed", "FeelNoPain", "FiendFire", "FightMe", "FlameBarrier", "ForgottenRitual", "Havoc", "Headbutt",
        "Hellraiser", "Hemokinesis", "HowlFromBeyond", "Impervious", "InfernalBlade", "Inferno", "Inflame", "IronWave", "Juggernaut", "Juggling",
        "Mangle", "MoltenFist", "NotYet", "Offering", "OneTwoPunch", "PactsEnd", "PerfectedStrike", "Pillage", "PommelStrike", "PrimalForce",
        "Pyre", "Rage", "Rampage", "Rupture", "SecondWind", "SetupStrike", "ShrugItOff", "Spite", "Stampede", "Stoke",
        "Stomp", "StoneArmor", "StrikeIronclad", "SwordBoomerang", "Tank", "Taunt", "TearAsunder", "Thrash", "Thunderclap", "Tremble",
        "TrueGrit", "TwinStrike", "Unmovable", "Unrelenting", "Uppercut", "Vicious", "Whirlwind",
    ];

    /// <summary>The 12 cards of the game's status card pool.</summary>
    public static readonly IReadOnlyList<string> StatusCards =
        ["Beckon", "Burn", "Dazed", "Debris", "FranticEscape", "Infection", "Wither", "Slimed", "Soot", "Toxic", "Void", "Wound"];

    /// <summary>Cards that are in no pool and are only made by an Ironclad card (Primal Force's Giant Rock).</summary>
    public static readonly IReadOnlyList<string> TokenCards = ["GiantRock"];

    /// <summary>What random card generation in a fight may produce in a solo Ironclad run: the pool
    /// without Basic cards, multiplayer-only cards and the two cards flagged as never generated.</summary>
    public const int GeneratableCards = 78;
    public const int GeneratableAttacks = 33;
    public const int GeneratableSkills = 27;
    public const int GeneratablePowers = 18;

    /// <summary>One line of the table.</summary>
    /// <param name="Missing">Names that are neither registered nor left out: a hole.</param>
    public sealed record Row(string Group, ContentKind Kind, IReadOnlyList<string> Wanted, IReadOnlyList<string> Registered,
        IReadOnlyList<(string Name, string Reason)> LeftOut, IReadOnlyList<string> Missing);

    public static IReadOnlyList<Row> Rows()
    {
        var registry = Content.Registry;
        Row Make(string group, ContentKind kind, IEnumerable<string> names)
        {
            var wanted = names.ToList();
            var registered = wanted.Where(n => registry.Has(kind, n)).ToList();
            var leftOut = wanted.Where(n => !registry.Has(kind, n) && registry.IsExcluded(kind, n, out _))
                .Select(n => (n, registry.IsExcluded(kind, n, out var why) ? why : "")).ToList();
            var missing = wanted.Where(n => !registry.Has(kind, n) && !registry.IsExcluded(kind, n, out _)).ToList();
            return new Row(group, kind, wanted, registered, leftOut, missing);
        }

        return
        [
            Make("Ironclad card pool", ContentKind.Card, IroncladPool),
            Make("status cards", ContentKind.Card, StatusCards),
            Make("cards made by Ironclad cards", ContentKind.Card, TokenCards),
            Make("Overgrowth encounters", ContentKind.Encounter, Scenarios.Roster.Select(e => e.ClassName)),
            Make("potions an Ironclad can find", ContentKind.Potion, Scenarios.PotionPool.Select(p => p.ClassName)),
        ];
    }

    /// <summary>Everything that is wrong: a name of the game's lists that is neither registered nor
    /// left out, a registered card, encounter or potion that is on none of the lists, and a pool
    /// for random card generation that is not the game's.</summary>
    public static IReadOnlyList<string> Problems()
    {
        var registry = Content.Registry;
        var problems = new List<string>();
        var rows = Rows();
        foreach (var row in rows)
            foreach (var name in row.Missing)
                problems.Add($"{row.Group}: {name} is neither registered nor left out with a reason");

        void Strays(ContentKind kind, IEnumerable<string> registered)
        {
            var wanted = rows.Where(r => r.Kind == kind).SelectMany(r => r.Wanted).ToHashSet(StringComparer.Ordinal);
            foreach (var name in registered.Where(n => !wanted.Contains(n)).OrderBy(n => n, StringComparer.Ordinal))
                problems.Add($"{kind} {name} is registered but is on none of the game's lists the arena covers");
        }
        Strays(ContentKind.Card, registry.Cards.Keys);
        Strays(ContentKind.Encounter, registry.EncounterNames);
        Strays(ContentKind.Potion, registry.Potions.Keys);

        // Random generation picks by index from this list, so one card too few or too many shifts every pick.
        var generatable = registry.IroncladPool.Where(c => c.IsGeneratableInCombat).ToList();
        void Count(string what, int expected, int actual)
        {
            if (expected != actual) problems.Add($"random card generation has {actual} {what} to pick from; the game has {expected}");
        }
        Count("cards", GeneratableCards, generatable.Count);
        Count("Attacks", GeneratableAttacks, generatable.Count(c => c.Type == CardType.Attack));
        Count("Skills", GeneratableSkills, generatable.Count(c => c.Type == CardType.Skill));
        Count("Powers", GeneratablePowers, generatable.Count(c => c.Type == CardType.Power));
        return problems;
    }

    /// <summary>Writes the table: per group how many are registered and left out, then every name left out with its reason.</summary>
    /// <param name="names">Also list the registered names.</param>
    public static void Write(TextWriter output, bool names)
    {
        var rows = Rows();
        var table = new TextTable("group", "in the game", "registered", "left out", "missing");
        foreach (var row in rows)
            table.Add(row.Group, row.Wanted.Count.ToString(), row.Registered.Count.ToString(), row.LeftOut.Count.ToString(), row.Missing.Count.ToString());
        table.Write(output);

        output.WriteLine();
        output.WriteLine("Left out, with the reason:");
        foreach (var row in rows)
            foreach (var (name, reason) in row.LeftOut)
                output.WriteLine($"  {row.Kind} {name}: {reason}");
        foreach (var row in rows)
            foreach (var name in row.Missing)
                output.WriteLine($"  MISSING {row.Kind} {name}: neither registered nor left out");

        if (!names) return;
        foreach (var row in rows)
        {
            output.WriteLine();
            output.WriteLine($"{row.Group} ({row.Registered.Count}):");
            output.WriteLine("  " + string.Join(", ", row.Registered));
        }
    }
}
