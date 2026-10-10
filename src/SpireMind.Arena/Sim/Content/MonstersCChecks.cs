using System.Globalization;
using System.Text;

namespace SpireMind.Arena;

/// <summary>
/// Scripted checks for MonstersC. Every monster's move pattern is walked turn by turn against
/// numbers worked out by hand from the game's classes; each special rule (Shrink, Tangled,
/// Frail's timing, move Block, the order of a move's steps) has its own group; every encounter's
/// line-up is checked over many seeds, with the encounter and HP draws redone by hand.
/// </summary>
public sealed class MonstersCChecks : IArenaChecks
{
    private const int Seeds = 200;
    private const int RaiderSeeds = 600;

    /// <summary>What the game's classes say about each monster: name, HP range, first intents, first attack total.</summary>
    private sealed record Sheet(string Name, int MinHp, int MaxHp, string[] FirstIntents, int FirstIncoming);

    private static readonly Dictionary<string, Sheet> Monsters = new(StringComparer.Ordinal)
    {
        ["ShrinkerBeetle"] = new("Shrinker Beetle", 38, 40, ["debuffstrong"], 0),
        ["FuzzyWurmCrawler"] = new("Fuzzy Wurm Crawler", 55, 57, ["attack 4"], 4),
        ["AxeRubyRaider"] = new("Axe Raider", 20, 22, ["attack 5", "defend"], 5),
        ["AssassinRubyRaider"] = new("Assassin Raider", 18, 23, ["attack 10"], 10),
        ["BruteRubyRaider"] = new("Brute Raider", 30, 33, ["attack 7"], 7),
        ["CrossbowRubyRaider"] = new("Crossbow Raider", 18, 21, ["defend"], 0),
        ["TrackerRubyRaider"] = new("Tracker Raider", 21, 25, ["debuff"], 0),
        ["VineShambler"] = new("Vine Shambler", 61, 61, ["attack 6x2"], 12),
    };

    /// <summary>The raider kinds in the order of the game's list; the picks index into what is left of it.</summary>
    private static readonly string[] RaiderKinds =
        ["AxeRubyRaider", "AssassinRubyRaider", "BruteRubyRaider", "CrossbowRubyRaider", "TrackerRubyRaider"];

    private static readonly string[] Encounters =
        ["ShrinkerBeetleWeak", "FuzzyWurmCrawlerWeak", "OvergrowthCrawlers", "RubyRaidersNormal", "VineShamblerNormal"];

    // ---------- cards and powers that exist only for these checks ----------

    /// <summary>An Attack that stays in hand over the turn end, so that it is in hand when Tangled lands.</summary>
    private static readonly CardDef Keeper = new("CheckKeeperC", "Keeper", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
    {
        Keywords = CardKeywords.Retain,
        Vars = { ["Damage"] = 3 },
        Play = c => c.Attack(c.V("Damage")).Execute(),
    };

    /// <summary>An X-cost Attack: 2 damage X times.</summary>
    private static readonly CardDef Xer = new("CheckXerC", "Xer", CardType.Attack, CardRarity.Common, CardDef.CostX, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 2 },
        Play = c => c.Attack(c.V("Damage")).Hits(c.X).Execute(),
    };

    /// <summary>An Attack without a cost.</summary>
    private static readonly CardDef Costless = new("CheckCostlessC", "Costless", CardType.Attack, CardRarity.Common, CardDef.NoCost, TargetType.AnyEnemy)
    {
        Keywords = CardKeywords.Unplayable,
    };

    /// <summary>10 damage to every enemy, twice, free.</summary>
    private static readonly CardDef TwiceAll = new("CheckTwiceAllC", "Twice All", CardType.Attack, CardRarity.Common, 0, TargetType.AllEnemies)
    {
        Play = c => c.Attack(10).Hits(2).Execute(),
    };

    /// <summary>Notes, at the moment each hit reaches the player, what the hit was and what the
    /// attacker and the player looked like: the proof of the order of a move's steps.</summary>
    private sealed class HitWitness : Power
    {
        public List<string> Seen = new();

        public override string Name => "Witness";
        public override PowerType Type => PowerType.Buff;
        public override bool IsVisible => false;
        public override string Describe() => "";
        public override void OnCloned(CloneMap map) => Seen = new List<string>(Seen);

        public override void BeforeDamageReceived(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (!ReferenceEquals(target, Owner) || dealer is null) return;
            Seen.Add(string.Create(CultureInfo.InvariantCulture, $"{amount:0.##} with attacker Block {dealer.Block} and Tangled {Owner.PowerAmount<TangledPower>()}"));
        }
    }

    /// <summary>Hurts whoever lands a powered hit on its owner, right after the hit: a reaction
    /// that can kill an attacker in the middle of its move.</summary>
    private sealed class Spikes : Power
    {
        public override string Name => "Spikes";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner) && dealer is not null && props.IsPoweredAttack())
                f.Damage(dealer, Amount, Props.NonCardUnpowered, Owner);
        }
    }

    /// <summary>A late cost override: Attacks are free.</summary>
    private sealed class FreeAttacks : Power
    {
        public override string Name => "Free Attacks";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override decimal ModifyEnergyCostLate(Fight f, CardInstance card, decimal cost) => card.Type == CardType.Attack ? 0m : cost;
    }

    // ---------- helpers ----------

    /// <summary>One enemy turn of a walk: what the monster shows on the player's turn, the Block the
    /// player holds when it acts, and what is there once it has acted.</summary>
    private sealed record Turn(
        string Move, string[] Intents, int Incoming, int Block, int HpLost, int EnemyBlock = 0, string EnemyPowers = "", string PlayerPowers = "");

    /// <summary>One monster with fixed HP against the starter deck in a known order; the player has HP to spare.</summary>
    private static Fight Solo(Monster monster, int hp, int seed = 1) =>
        new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(300, 300).Enemy(monster, hp: hp).Seed(seed).Start();

    private static string Powers(Creature creature) =>
        string.Join(", ", creature.Powers.Where(p => p.IsVisible).Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Name} {p.Amount}")));

    private static List<string> Performed(Fight f) =>
        f.History.Entries.Where(e => e.Kind == HistoryKind.MonsterPerformedMove).Select(e => e.Name ?? "").ToList();

    private static List<string> Witnessed(Fight f) => f.Player.GetPower<HitWitness>()!.Seen;

    private static bool Marked(CardInstance card) => card.Affliction == TangledPower.Marker;

    private static int Lost(Fight f, Action act)
    {
        var before = f.Player.Hp;
        act();
        return before - f.Player.Hp;
    }

    /// <summary>
    /// Walks a single monster through its turns. Each step: the move and intents shown on the
    /// player's turn, then the turn is ended with a set amount of Block and the HP that got
    /// through, the monster's Block and both sides' powers are compared.
    /// </summary>
    private static void Walk(Check c, Fight f, params Turn[] turns)
    {
        var cards = f.AllCards.Count();
        for (var n = 0; n < turns.Length; n++)
        {
            var t = turns[n];
            var at = $"turn {n + 1}";
            var enemy = f.Enemy(0);
            c.Equal(n + 1, f.Turn, $"{at}: the turn number");
            c.Equal(t.Move, enemy.Monster!.NextMove?.Id, $"{at}: the move");
            c.Sequence(t.Intents, f.IntentTexts(enemy), $"{at}: the intents");
            c.Equal(t.Incoming, f.IncomingDamage(), $"{at}: incoming damage");

            f.SetBlock(f.Player, t.Block);
            var lost = Lost(f, () => f.EndTurn());
            c.Equal(t.HpLost, lost, $"{at}: HP lost through {t.Block} Block");
            c.Equal(t.EnemyBlock, f.Enemy(0).Block, $"{at}: the monster's Block after its move");
            c.Equal(t.EnemyPowers, Powers(f.Enemy(0)), $"{at}: the monster's powers after its move");
            c.Equal(t.PlayerPowers, Powers(f.Player), $"{at}: the player's powers after its move");
            c.Equal(cards, f.AllCards.Count(), $"{at}: no card was added");
        }

        c.Sequence(turns.Select(t => t.Move), Performed(f), "the moves performed, in order");
        c.Sequence(turns.Select(t => t.Move), f.Enemy(0).Monster!.MoveLog.Take(turns.Length), "the moves chosen, in order");
        c.Equal(0, f.Rng.MonsterAi.Counter, "nothing was drawn from the MonsterAi stream");
    }

    /// <summary>Collects what went wrong over many seeds, so that each property is one check and a failure names its seeds.</summary>
    private sealed class Tally
    {
        private readonly List<string> _order = new();
        private readonly Dictionary<string, List<int>> _bad = new(StringComparer.Ordinal);

        public void Expect(bool ok, int seed, string what)
        {
            if (!_bad.TryGetValue(what, out var seeds))
            {
                _bad[what] = seeds = new List<int>();
                _order.Add(what);
            }
            if (!ok) seeds.Add(seed);
        }

        public void Report(Check c)
        {
            foreach (var what in _order)
                c.That(_bad[what].Count == 0, $"{what}: wrong for {_bad[what].Count} seeds, first {string.Join(", ", _bad[what].Take(8))}");
        }
    }

    /// <summary>The game's HP roll, redone by hand: every value of the range no earlier enemy has, one of them by one Niche draw.</summary>
    private static int RollHp(GameRng niche, Sheet sheet, List<int> taken)
    {
        var free = Enumerable.Range(sheet.MinHp, sheet.MaxHp - sheet.MinHp + 1).Where(v => !taken.Contains(v)).ToList();
        var hp = free[niche.NextInt(0, free.Count)];
        taken.Add(hp);
        return hp;
    }

    /// <summary>What every enemy of a fresh fight must look like, whatever the encounter.</summary>
    private static void ExpectLineUp(Tally tally, Fight f, int seed, IReadOnlyList<string> kinds)
    {
        tally.Expect(f.Enemies.Select(e => e.Monster!.ClassName).SequenceEqual(kinds), seed, "the line-up, in acting order");
        tally.Expect(f.Roster.SequenceEqual(f.Enemies), seed, "the e-index follows the acting order");
        var niche = RngSet.For(seed, "niche");
        var taken = new List<int>();
        var incoming = 0;
        for (var j = 0; j < f.Enemies.Count && j < kinds.Count; j++)
        {
            var enemy = f.Enemies[j];
            var sheet = Monsters[kinds[j]];
            tally.Expect(enemy.Name == sheet.Name, seed, "the display name");
            tally.Expect(enemy.Slot is null, seed, "no slot");
            tally.Expect(enemy.MaxHp >= sheet.MinHp && enemy.MaxHp <= sheet.MaxHp, seed, "HP inside the monster's range");
            tally.Expect(enemy.Hp == enemy.MaxHp && enemy.Block == 0 && enemy.Powers.Count == 0, seed, "full HP, no Block, no starting power");
            tally.Expect(enemy.MaxHp == RollHp(niche, sheet, taken), seed, "the HP is the one the Niche stream gives by hand");
            tally.Expect(f.IntentTexts(enemy).SequenceEqual(sheet.FirstIntents), seed, "the first intents");
            incoming += sheet.FirstIncoming;
        }
        tally.Expect(f.Enemies.Select(e => e.MaxHp).Distinct().Count() == f.Enemies.Count, seed, "no two enemies start on the same HP");
        tally.Expect(f.Rng.Niche.Counter == kinds.Count, seed, "one Niche draw per monster");
        tally.Expect(f.Rng.MonsterAi.Counter == 0, seed, "no MonsterAi draw for a first move");
        tally.Expect(f.IncomingDamage() == incoming, seed, "incoming damage on turn 1");
    }

    // ---------- the checks ----------

    public void Run(Check c)
    {
        Registered(c);
        ShrinkerBeetleChecks(c);
        FuzzyWurmCrawlerChecks(c);
        RaiderChecks(c);
        VineShamblerChecks(c);
        TangledChecks(c);
        EncounterChecks(c);
        WholeFights(c);
        SecondReading.Run(c);
    }

    private static void Registered(Check c)
    {
        c.Group("Part C is registered under the game's class names", () =>
        {
            var registry = Content.Registry;
            foreach (var (className, sheet) in Monsters)
            {
                var monster = registry.NewMonster(className);
                c.Equal(sheet.Name, monster.Name, $"{className}: name");
                c.Equal(sheet.MinHp, monster.MinHp, $"{className}: lowest HP");
                c.Equal(sheet.MaxHp, monster.MaxHp, $"{className}: highest HP");
                c.Equal("MonstersC", registry.SourceOf(ContentKind.Monster, className), $"{className}: registered here");
            }
            foreach (var name in Encounters)
            {
                var encounter = registry.NewEncounter(name);
                c.Equal(RoomType.Monster, encounter.RoomType, $"{name}: a monster room");
                c.Equal(name is "ShrinkerBeetleWeak" or "FuzzyWurmCrawlerWeak", encounter.IsWeak, $"{name}: weak or not");
                c.Equal(0, encounter.Slots.Count, $"{name}: no slots");
            }

            var tangled = registry.NewPower("TangledPower");
            c.Equal("Tangled", tangled.Name, "Tangled: name");
            c.Equal(PowerType.Debuff, tangled.Type, "Tangled: a Debuff");
            c.Equal(PowerStackType.Counter, tangled.StackType, "Tangled: a counter");
            c.That(!tangled.AllowNegative && tangled.IsVisible, "Tangled: visible, never negative");
            c.That(registry.Affliction("Entangled").ExtraCardText is null, "Entangled adds no line to a card's text");
        });
    }

    // ---------- Shrinker Beetle ----------

    private static void ShrinkerBeetleChecks(Check c)
    {
        c.Group("Shrinker Beetle: Shrink once, then Chomp 7 and Stomp 13 in turn", () =>
        {
            const string shrunk = "Shrink -1";
            Walk(c, Solo(new ShrinkerBeetle(), 39),
                new("SHRINKER_MOVE", ["debuffstrong"], 0, Block: 0, HpLost: 0, PlayerPowers: shrunk),
                new("CHOMP_MOVE", ["attack 7"], 7, Block: 5, HpLost: 2, PlayerPowers: shrunk),
                new("STOMP_MOVE", ["attack 13"], 13, Block: 0, HpLost: 13, PlayerPowers: shrunk),
                new("CHOMP_MOVE", ["attack 7"], 7, Block: 10, HpLost: 0, PlayerPowers: shrunk),
                new("STOMP_MOVE", ["attack 13"], 13, Block: 4, HpLost: 9, PlayerPowers: shrunk),
                new("CHOMP_MOVE", ["attack 7"], 7, Block: 0, HpLost: 7, PlayerPowers: shrunk),
                new("STOMP_MOVE", ["attack 13"], 13, Block: 13, HpLost: 0, PlayerPowers: shrunk));
        });

        c.Group("Shrink: the player's attacks x 0.70 with the other factors, one round-down, no countdown", () =>
        {
            var f = Solo(new ShrinkerBeetle(), 39);
            f.Play("Strike", 0);
            c.Equal(33, f.Enemy(0).Hp, "before Shrink a Strike deals 6");
            f.EndTurn();

            var shrink = f.Player.GetPower<ShrinkPower>();
            c.That(shrink is not null && ReferenceEquals(shrink.Applier, f.Enemy(0)), "the beetle is the applier");
            var seen = f.Snapshot().State["me"]!["powers"]![0]!;
            c.Equal("Shrink", seen["name"]!.GetValue<string>(), "the model sees Shrink");
            c.Equal(-1, seen["amount"]!.GetValue<int>(), "with the raw amount -1");
            c.Equal("Debuff", seen["kind"]!.GetValue<string>(), "as a Debuff");

            c.Equal("Deal 4 damage.", Fight.CardText(f, f.Hand[f.HandIndex("Strike")], PileType.Hand), "the hand text counts Shrink");
            f.Play("Strike", 0);
            c.Equal(29, f.Enemy(0).Hp, "6 x 0.7 = 4.2, so 4");
            f.EndTurn();

            f.Put("Bash", PileType.Hand);
            f.Put("StrikeIronclad", PileType.Hand);
            f.Play("Bash", 0);
            c.Equal(24, f.Enemy(0).Hp, "8 x 0.7 = 5.6, so 5");
            f.Play("Strike", 0);
            c.Equal(18, f.Enemy(0).Hp, "against Vulnerable 6 x 1.5 x 0.7 = 6.3, so 6");
            f.Damage(f.Enemy(0), 10, Props.NonCardUnpowered, f.Player);
            c.Equal(8, f.Enemy(0).Hp, "unpowered damage is not reduced");

            f.EndTurn();
            f.EndTurn();
            f.EndTurn();
            c.Equal(-1, f.Player.PowerAmount<ShrinkPower>(), "three turns on it still reads -1");
        });

        c.Group("Shrink ends the moment the beetle dies, also with the Wurm Crawler still standing", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(300, 300)
                .Enemy(new ShrinkerBeetle(), hp: 39).Enemy(new FuzzyWurmCrawler(), hp: 56).Start();
            c.Sequence(["debuffstrong"], f.IntentTexts(f.Enemy(0)), "turn 1: the beetle");
            c.Sequence(["attack 4"], f.IntentTexts(f.Enemy(1)), "turn 1: the crawler");
            c.Equal(4, f.IncomingDamage(), "turn 1: incoming");
            c.Equal(4, Lost(f, () => f.EndTurn()), "turn 1: only the crawler hits");

            c.Equal(-1, f.Player.PowerAmount<ShrinkPower>(), "turn 2: shrunk");
            c.Sequence(["attack 7"], f.IntentTexts(f.Enemy(0)), "turn 2: the beetle");
            c.Sequence(["buff"], f.IntentTexts(f.Enemy(1)), "turn 2: the crawler");
            c.Equal(7, f.IncomingDamage(), "turn 2: incoming");

            f.SetHp(f.Enemy(0), 4);
            f.Play("Strike", 0);
            c.That(f.Enemy(0).IsDead, "a shrunk Strike deals 4 and kills the beetle on 4 HP");
            c.That(!f.Player.HasPower<ShrinkPower>(), "Shrink is gone at once");
            c.That(!f.IsOver && f.Enemies.Count == 1, "the fight goes on against the crawler");
            f.Play("Strike", 1);
            c.Equal(50, f.Enemy(1).Hp, "the next Strike deals 6 again");

            var enemies = f.Snapshot().State["enemies"]!.AsArray();
            c.Equal(1, enemies.Count, "one enemy is shown");
            c.Equal(1, enemies[0]!["i"]!.GetValue<int>(), "and it keeps its index e1");

            c.Equal(0, Lost(f, () => f.EndTurn()), "the dead beetle does not Chomp; the crawler inhales");
            c.Equal("Strength 7", Powers(f.Enemy(1)), "the crawler's Inhale");
            c.Sequence(["attack 11"], f.IntentTexts(f.Enemy(1)), "turn 3: the crawler alone");
        });

        c.Group("Shrink ends between two hits of one attack when the first hit kills the beetle", () =>
        {
            // Each hit is its own damage instance and its deaths are settled before the next hit;
            // Shrink removes itself in the death hook of the creature that applied it.
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(300, 300)
                .Enemy(new ShrinkerBeetle(), hp: 39).Enemy(new FuzzyWurmCrawler(), hp: 56).Start();
            f.EndTurn();
            c.Equal(-1, f.Player.PowerAmount<ShrinkPower>(), "shrunk");
            f.SetHp(f.Enemy(0), 5);
            f.Put(TwiceAll, PileType.Hand);
            f.Play("Twice All");
            c.That(f.Enemy(0).IsDead, "the first hit, 10 x 0.7 = 7, kills the beetle on 5 HP");
            c.That(!f.Player.HasPower<ShrinkPower>(), "Shrink is gone");
            c.Equal(56 - 7 - 10, f.Enemy(1).Hp, "the crawler takes 7 from the first hit and the full 10 from the second");
        });

        c.Group("Shrink is a debuff: Artifact on the player stops it", () =>
        {
            var f = Solo(new ShrinkerBeetle(), 39);
            f.ApplyPower<ArtifactPower>(f.Player, 1, f.Player);
            f.EndTurn();
            c.That(!f.Player.HasPower<ShrinkPower>(), "no Shrink");
            c.That(!f.Player.HasPower<ArtifactPower>(), "and the Artifact is used up");
            c.Sequence(["attack 7"], f.IntentTexts(f.Enemy(0)), "the beetle moves on to Chomp all the same");
            f.Play("Strike", 0);
            c.Equal(33, f.Enemy(0).Hp, "a Strike deals 6");
        });
    }

    // ---------- Fuzzy Wurm Crawler ----------

    private static void FuzzyWurmCrawlerChecks(Check c)
    {
        c.Group("Fuzzy Wurm Crawler: 4, Inhale, 11, 11, Inhale, 18, 18, Inhale, 25", () =>
        {
            Walk(c, Solo(new FuzzyWurmCrawler(), 56),
                new("FIRST_ACID_GOOP", ["attack 4"], 4, Block: 0, HpLost: 4),
                new("INHALE", ["buff"], 0, Block: 0, HpLost: 0, EnemyPowers: "Strength 7"),
                new("ACID_GOOP", ["attack 11"], 11, Block: 5, HpLost: 6, EnemyPowers: "Strength 7"),
                new("FIRST_ACID_GOOP", ["attack 11"], 11, Block: 0, HpLost: 11, EnemyPowers: "Strength 7"),
                new("INHALE", ["buff"], 0, Block: 0, HpLost: 0, EnemyPowers: "Strength 14"),
                new("ACID_GOOP", ["attack 18"], 18, Block: 20, HpLost: 0, EnemyPowers: "Strength 14"),
                new("FIRST_ACID_GOOP", ["attack 18"], 18, Block: 0, HpLost: 18, EnemyPowers: "Strength 14"),
                new("INHALE", ["buff"], 0, Block: 0, HpLost: 0, EnemyPowers: "Strength 21"),
                new("ACID_GOOP", ["attack 25"], 25, Block: 7, HpLost: 18, EnemyPowers: "Strength 21"));
        });

        c.Group("Fuzzy Wurm Crawler: its Strength is added before Vulnerable and Weak multiply", () =>
        {
            var f = Solo(new FuzzyWurmCrawler(), 56);
            f.EndTurn();
            f.EndTurn();
            c.Sequence(["attack 11"], f.IntentTexts(f.Enemy(0)), "4 + 7");
            f.ApplyPower<VulnerablePower>(f.Player, 1, f.Enemy(0));
            c.Sequence(["attack 16"], f.IntentTexts(f.Enemy(0)), "(4 + 7) x 1.5 = 16.5");
            f.ApplyPower<WeakPower>(f.Enemy(0), 1, f.Player);
            c.Sequence(["attack 12"], f.IntentTexts(f.Enemy(0)), "(4 + 7) x 0.75 x 1.5 = 12.375");
            c.Equal(12, f.IncomingDamage(), "incoming");
            c.Equal(12, Lost(f, () => f.EndTurn()), "and that is what lands");

            // Weak ticked away with the enemy turn; Vulnerable, new on the player, skipped its first tick.
            c.Sequence(["attack 16"], f.IntentTexts(f.Enemy(0)), "turn 4: Vulnerable only");
            c.Equal(16, Lost(f, () => f.EndTurn()), "turn 4: 16 lands");
            c.Equal("", Powers(f.Player), "turn 5: Vulnerable has worn off");
            c.Sequence(["buff"], f.IntentTexts(f.Enemy(0)), "turn 5: Inhale");
        });
    }

    // ---------- the Ruby Raiders ----------

    private static void RaiderChecks(Check c)
    {
        c.Group("Axe Raider: Swing, Swing, Big Swing", () =>
        {
            string[] swing = ["attack 5", "defend"];
            Walk(c, Solo(new AxeRubyRaider(), 21),
                new("SWING_1", swing, 5, Block: 0, HpLost: 5, EnemyBlock: 5),
                new("SWING_2", swing, 5, Block: 3, HpLost: 2, EnemyBlock: 5),
                new("BIG_SWING", ["attack 12"], 12, Block: 0, HpLost: 12, EnemyBlock: 0),
                new("SWING_1", swing, 5, Block: 5, HpLost: 0, EnemyBlock: 5),
                new("SWING_2", swing, 5, Block: 0, HpLost: 5, EnemyBlock: 5),
                new("BIG_SWING", ["attack 12"], 12, Block: 10, HpLost: 2, EnemyBlock: 0),
                new("SWING_1", swing, 5, Block: 0, HpLost: 5, EnemyBlock: 5));
        });

        c.Group("Axe Raider: a Swing hits first and then gives Block that stands through the player's next turn", () =>
        {
            var f = Solo(new AxeRubyRaider(), 21);
            f.ApplyPower<HitWitness>(f.Player, 1, f.Player);
            f.EndTurn();
            c.Equal(5, f.Enemy(0).Block, "turn 2: its 5 Block is up");
            f.EndTurn();
            c.Sequence(
                ["5 with attacker Block 0 and Tangled 0", "5 with attacker Block 0 and Tangled 0"], Witnessed(f),
                "both Swings landed while the raider had no Block: the old Block fell as the enemy turn began, the new one came after the hit");

            c.Equal(5, f.Enemy(0).Block, "turn 3: Block again");
            f.Put("StrikeIronclad", PileType.Hand);
            f.Play("Strike", 0);
            c.Equal(0, f.Enemy(0).Block, "a Strike breaks it");
            c.Equal(20, f.Enemy(0).Hp, "5 of the 6 go into the Block, 1 through");
            f.EndTurn();
            c.Equal(0, f.Enemy(0).Block, "turn 4: Big Swing gave none");
            f.ApplyPower<FrailPower>(f.Enemy(0), 1, f.Player);
            f.EndTurn();
            c.Equal(3, f.Enemy(0).Block, "Frail on the raider: 5 x 0.75 = 3.75, so 3");
        });

        c.Group("Assassin Raider: Killshot 10 every turn", () =>
        {
            Walk(c, Solo(new AssassinRubyRaider(), 20),
                new("KILLSHOT_MOVE", ["attack 10"], 10, Block: 0, HpLost: 10),
                new("KILLSHOT_MOVE", ["attack 10"], 10, Block: 4, HpLost: 6),
                new("KILLSHOT_MOVE", ["attack 10"], 10, Block: 10, HpLost: 0),
                new("KILLSHOT_MOVE", ["attack 10"], 10, Block: 15, HpLost: 0),
                new("KILLSHOT_MOVE", ["attack 10"], 10, Block: 0, HpLost: 10),
                new("KILLSHOT_MOVE", ["attack 10"], 10, Block: 9, HpLost: 1));
        });

        c.Group("Brute Raider: 7, Roar, 10, Roar, 13, Roar, 16", () =>
        {
            Walk(c, Solo(new BruteRubyRaider(), 31),
                new("BEAT_MOVE", ["attack 7"], 7, Block: 0, HpLost: 7),
                new("ROAR_MOVE", ["buff"], 0, Block: 0, HpLost: 0, EnemyPowers: "Strength 3"),
                new("BEAT_MOVE", ["attack 10"], 10, Block: 4, HpLost: 6, EnemyPowers: "Strength 3"),
                new("ROAR_MOVE", ["buff"], 0, Block: 0, HpLost: 0, EnemyPowers: "Strength 6"),
                new("BEAT_MOVE", ["attack 13"], 13, Block: 0, HpLost: 13, EnemyPowers: "Strength 6"),
                new("ROAR_MOVE", ["buff"], 0, Block: 0, HpLost: 0, EnemyPowers: "Strength 9"),
                new("BEAT_MOVE", ["attack 16"], 16, Block: 16, HpLost: 0, EnemyPowers: "Strength 9"));
        });

        c.Group("Crossbow Raider: Reload, Fire 14, in turn, starting with Reload", () =>
        {
            Walk(c, Solo(new CrossbowRubyRaider(), 19),
                new("RELOAD_MOVE", ["defend"], 0, Block: 0, HpLost: 0, EnemyBlock: 3),
                new("FIRE_MOVE", ["attack 14"], 14, Block: 5, HpLost: 9, EnemyBlock: 0),
                new("RELOAD_MOVE", ["defend"], 0, Block: 0, HpLost: 0, EnemyBlock: 3),
                new("FIRE_MOVE", ["attack 14"], 14, Block: 0, HpLost: 14, EnemyBlock: 0),
                new("RELOAD_MOVE", ["defend"], 0, Block: 6, HpLost: 0, EnemyBlock: 3),
                new("FIRE_MOVE", ["attack 14"], 14, Block: 14, HpLost: 0, EnemyBlock: 0));
        });

        c.Group("Crossbow Raider: Reload's Block is up on the turn before each shot; Frail cuts it", () =>
        {
            var f = Solo(new CrossbowRubyRaider(), 19);
            c.Equal(0, f.Enemy(0).Block, "turn 1: no Block yet");
            f.EndTurn();
            c.Equal(3, f.Enemy(0).Block, "turn 2: 3 Block");
            f.Play("Strike", 0);
            c.Equal(0, f.Enemy(0).Block, "a Strike breaks it");
            c.Equal(16, f.Enemy(0).Hp, "3 of the 6 go into the Block, 3 through");
            f.EndTurn();
            c.Equal(0, f.Enemy(0).Block, "turn 3: Fire gave none");
            f.ApplyPower<FrailPower>(f.Enemy(0), 1, f.Player);
            f.EndTurn();
            c.Equal(2, f.Enemy(0).Block, "Frail on the raider: 3 x 0.75 = 2.25, so 2");
        });

        c.Group("Tracker Raider: Track once, then Hounds 1x8 every turn", () =>
        {
            Walk(c, Solo(new TrackerRubyRaider(), 23),
                new("TRACK_MOVE", ["debuff"], 0, Block: 0, HpLost: 0, PlayerPowers: "Frail 2"),
                new("HOUNDS_MOVE", ["attack 1x8"], 8, Block: 0, HpLost: 8, PlayerPowers: "Frail 1"),
                new("HOUNDS_MOVE", ["attack 1x8"], 8, Block: 3, HpLost: 5),
                new("HOUNDS_MOVE", ["attack 1x8"], 8, Block: 8, HpLost: 0),
                new("HOUNDS_MOVE", ["attack 1x8"], 8, Block: 10, HpLost: 0),
                new("HOUNDS_MOVE", ["attack 1x8"], 8, Block: 0, HpLost: 8));
        });

        c.Group("Tracker Raider: its 2 Frail cuts the player's Block on turns 2 and 3 and is gone on turn 4", () =>
        {
            var f = Solo(new TrackerRubyRaider(), 23);
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "turn 1: a Defend gives 5");
            f.EndTurn();

            c.Equal(2, f.Player.PowerAmount<FrailPower>(), "turn 2: Frail 2, its first tick skipped");
            c.Equal("Gain 3 Block.", Fight.CardText(f, f.Hand[f.HandIndex("Defend")], PileType.Hand), "the hand text counts Frail");
            f.Play("Defend");
            c.Equal(3, f.Player.Block, "turn 2: 5 x 0.75 = 3.75, so 3");
            f.Play("Defend");
            c.Equal(6, f.Player.Block, "each gain is rounded on its own");
            c.Equal(2, Lost(f, () => f.EndTurn()), "Hounds: 6 of the 8 hits are blocked");

            c.Equal(1, f.Player.PowerAmount<FrailPower>(), "turn 3: Frail 1");
            f.Put("DefendIronclad", PileType.Hand);
            f.Play("Defend");
            c.Equal(3, f.Player.Block, "turn 3: still 3");
            f.EndTurn();

            c.That(!f.Player.HasPower<FrailPower>(), "turn 4: Frail is gone");
            f.Put("DefendIronclad", PileType.Hand);
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "turn 4: 5 again");
        });

        c.Group("Tracker Raider: each of the 8 hits is worked out and blocked on its own", () =>
        {
            var f = Solo(new TrackerRubyRaider(), 23);
            f.ApplyPower<HitWitness>(f.Player, 1, f.Player);
            f.EndTurn();
            f.ApplyPower<VulnerablePower>(f.Player, 2, f.Enemy(0));
            c.Sequence(["attack 1x8"], f.IntentTexts(f.Enemy(0)), "Vulnerable: 1 x 1.5 = 1.5 is still 1 a hit");
            c.Equal(8, f.IncomingDamage(), "incoming stays 8");
            f.SetBlock(f.Player, 3);
            c.Equal(5, Lost(f, () => f.EndTurn()), "3 hits go into 3 Block, 5 get through");
            c.Sequence(Enumerable.Repeat("1.5 with attacker Block 0 and Tangled 0", 8), Witnessed(f), "eight separate hits of 1.5");

            f.ApplyPower<StrengthPower>(f.Enemy(0), 2, f.Enemy(0));
            c.Sequence(["attack 4x8"], f.IntentTexts(f.Enemy(0)), "Strength adds to every hit: (1 + 2) x 1.5 = 4.5, so 4");
            c.Equal(32, f.IncomingDamage(), "incoming 32");
            f.RemovePower<VulnerablePower>(f.Player);
            c.Sequence(["attack 3x8"], f.IntentTexts(f.Enemy(0)), "without Vulnerable 3 a hit");
            c.Equal(24, Lost(f, () => f.EndTurn()), "24 lands");
        });

        c.Group("Tracker Raider: Hounds stops as soon as the raider dies in the middle of it", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(300, 300)
                .Enemy(new TrackerRubyRaider(), hp: 23).Enemy(new AssassinRubyRaider(), hp: 20).Start();
            f.EndTurn();
            f.SetHp(f.Enemy(0), 5);
            f.ApplyPower<Spikes>(f.Player, 2, f.Player);
            c.Equal(18, f.IncomingDamage(), "turn 2: 8 + 10 is announced");
            c.Equal(13, Lost(f, () => f.EndTurn()), "the third hound's Spikes kill the Tracker on 5 HP: 3 hits land, then the Assassin's 10");
            c.That(f.Enemy(0).IsDead && f.Enemies.Count == 1, "the Tracker is gone");
            c.Equal(18, f.Enemy(1).Hp, "the Assassin took 2 for its one hit");
            c.That(!f.IsOver, "the fight goes on");
            c.Sequence(["TRACK_MOVE", "KILLSHOT_MOVE", "HOUNDS_MOVE", "KILLSHOT_MOVE"], Performed(f), "the cut-short Hounds still counts as performed");
        });

        c.Group("Three raiders act in list order, each on its own script", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(300, 300)
                .Enemy(new TrackerRubyRaider(), hp: 23).Enemy(new AxeRubyRaider(), hp: 21).Enemy(new BruteRubyRaider(), hp: 31).Start();
            c.Sequence(["debuff"], f.IntentTexts(f.Enemy(0)), "turn 1: Tracker");
            c.Sequence(["attack 5", "defend"], f.IntentTexts(f.Enemy(1)), "turn 1: Axe");
            c.Sequence(["attack 7"], f.IntentTexts(f.Enemy(2)), "turn 1: Brute");
            c.Equal(12, f.IncomingDamage(), "turn 1: incoming 5 + 7");
            f.Play("Defend");
            f.Play("Defend");
            c.Equal(2, Lost(f, () => f.EndTurn()), "turn 1: 10 Block takes the 5 and 5 of the 7");

            c.Equal("Frail 2", Powers(f.Player), "turn 2: Frail from the Tracker");
            c.Equal(5, f.Enemy(1).Block, "turn 2: the Axe's Block");
            c.Sequence(["attack 1x8"], f.IntentTexts(f.Enemy(0)), "turn 2: Tracker");
            c.Sequence(["attack 5", "defend"], f.IntentTexts(f.Enemy(1)), "turn 2: Axe");
            c.Sequence(["buff"], f.IntentTexts(f.Enemy(2)), "turn 2: Brute");
            c.Equal(13, f.IncomingDamage(), "turn 2: incoming 8 + 5");
            f.Play("Defend");
            f.Play("Defend");
            c.Equal(6, f.Player.Block, "turn 2: two Frail Defends give 3 each");
            c.Equal(7, Lost(f, () => f.EndTurn()), "turn 2: the hounds lose 6 hits to the Block, then 2 + 5 land");

            c.Equal("Frail 1", Powers(f.Player), "turn 3: Frail 1");
            c.Equal("", Powers(f.Enemy(0)), "turn 3: the Roar gave the Tracker nothing");
            c.Equal("", Powers(f.Enemy(1)), "turn 3: nor the Axe");
            c.Equal("Strength 3", Powers(f.Enemy(2)), "turn 3: only the Brute has Strength");
            c.Sequence(["attack 1x8"], f.IntentTexts(f.Enemy(0)), "turn 3: Tracker");
            c.Sequence(["attack 12"], f.IntentTexts(f.Enemy(1)), "turn 3: Axe");
            c.Sequence(["attack 10"], f.IntentTexts(f.Enemy(2)), "turn 3: Brute");
            c.Equal(30, f.IncomingDamage(), "turn 3: incoming 8 + 12 + 10");
            c.Equal(30, Lost(f, () => f.EndTurn()), "turn 3: all of it lands");

            c.Sequence(
                ["TRACK_MOVE", "SWING_1", "BEAT_MOVE", "HOUNDS_MOVE", "SWING_2", "ROAR_MOVE", "HOUNDS_MOVE", "BIG_SWING", "BEAT_MOVE"],
                Performed(f), "the moves performed, in acting order");

            // Killing the one in the middle: the others keep their index and their order.
            f.RunAsAction(x => x.Damage(x.Enemy(1), 99, Props.NonCardUnpowered, x.Player));
            c.That(!f.IsOver && f.Enemies.Count == 2, "turn 4: the Axe is dead, the fight goes on");
            var shown = f.Snapshot().State["enemies"]!.AsArray();
            c.Sequence([0, 2], shown.Select(e => e!["i"]!.GetValue<int>()), "the Tracker stays e0, the Brute e2");
            c.Equal(8, Lost(f, () => f.EndTurn()), "turn 4: the hounds; the Brute roars");
            c.Equal("Strength 6", Powers(f.Enemy(2)), "turn 5: the Brute's second Roar");

            f.RunAsAction(x => x.Damage(x.Enemy(0), 99, Props.NonCardUnpowered, x.Player));
            c.That(!f.IsOver, "one raider left: no raider is a minion");
            f.RunAsAction(x => x.Damage(x.Enemy(2), 99, Props.NonCardUnpowered, x.Player));
            c.That(f.IsOver && f.Won, "the fight is won when the last raider dies");
            c.Equal(0, f.Rng.MonsterAi.Counter, "nothing was drawn from the MonsterAi stream");
        });
    }

    // ---------- Vine Shambler ----------

    private static void VineShamblerChecks(Check c)
    {
        c.Group("Vine Shambler: Swipe 6x2, Grasping Vines 8 and Tangled, Chomp 16", () =>
        {
            string[] vines = ["attack 8", "carddebuff"];
            Walk(c, Solo(new VineShambler(), 61),
                new("SWIPE_MOVE", ["attack 6x2"], 12, Block: 8, HpLost: 4),
                new("GRASPING_VINES_MOVE", vines, 8, Block: 0, HpLost: 8, PlayerPowers: "Tangled 1"),
                new("CHOMP_MOVE", ["attack 16"], 16, Block: 6, HpLost: 10),
                new("SWIPE_MOVE", ["attack 6x2"], 12, Block: 0, HpLost: 12),
                new("GRASPING_VINES_MOVE", vines, 8, Block: 10, HpLost: 0, PlayerPowers: "Tangled 1"),
                new("CHOMP_MOVE", ["attack 16"], 16, Block: 0, HpLost: 16),
                new("SWIPE_MOVE", ["attack 6x2"], 12, Block: 13, HpLost: 0));
        });

        c.Group("Vine Shambler: Swipe is two separate hits; Grasping Vines hits first and tangles after", () =>
        {
            var f = Solo(new VineShambler(), 61);
            f.ApplyPower<HitWitness>(f.Player, 1, f.Player);
            f.EndTurn();
            f.EndTurn();
            c.Sequence(
                ["6 with attacker Block 0 and Tangled 0", "6 with attacker Block 0 and Tangled 0", "8 with attacker Block 0 and Tangled 0"],
                Witnessed(f), "two Swipe hits, then the Vines hit before Tangled is there");
            c.Equal(1, f.Player.PowerAmount<TangledPower>(), "Tangled 1 after the hit");
            c.That(ReferenceEquals(f.Player.GetPower<TangledPower>()!.Applier, f.Enemy(0)), "the Shambler is the applier");
        });
    }

    // ---------- Tangled ----------

    private static void TangledChecks(Check c)
    {
        c.Group("Tangled: marks the Attack cards of all five piles and makes them cost 1 more", () =>
        {
            var f = Solo(new VineShambler(), 61);
            f.Put("StrikeIronclad", PileType.Discard);
            f.Put("Wound", PileType.Discard);
            f.Put("StrikeIronclad", PileType.Exhaust);
            f.Put("DefendIronclad", PileType.Exhaust);
            f.Put("Bash", PileType.Play);
            var xer = f.Put(Xer, PileType.Hand);
            var costless = f.Put(Costless, PileType.Hand);
            f.ApplyPower<TangledPower>(f.Player, 1, f.Enemy(0));

            c.That(f.AllCards.All(card => Marked(card) == (card.Type == CardType.Attack)), "every Attack is marked, nothing else is");
            c.Equal(5, f.Hand.Count(Marked), "hand: 2 Strikes, Bash, the X card, the costless card");
            c.Equal(3, f.DrawPile.Count(Marked), "draw pile: 3 Strikes");
            c.Equal(1, f.DiscardPile.Count(Marked), "discard pile: the Strike, not the Wound");
            c.Equal(1, f.ExhaustPile.Count(Marked), "exhaust pile: the Strike, not the Defend");
            c.Equal(1, f.PlayPile.Count(Marked), "play pile: the Bash");
            c.That(f.AllCards.Where(Marked).All(card => card.AfflictionAmount == 1), "the marker's amount is 1");

            var strike = f.Hand[f.HandIndex("Strike")];
            c.Equal(2, f.CostOf(strike), "Strike costs 2");
            c.Equal(3, f.CostOf(f.Hand[f.HandIndex("Bash")]), "Bash costs 3");
            c.Equal(1, f.CostOf(f.Hand[f.HandIndex("Defend")]), "Defend still costs 1");
            c.Equal("X", f.CostText(xer), "an X cost stays X");
            c.Equal(-1, f.CostOf(costless), "a card without a cost stays without one");
            c.Equal(2, f.CostOf(f.DrawPile.First(Marked)), "a marked card costs more in any pile");
            c.Equal("Deal 6 damage.", Fight.CardText(f, strike, PileType.Hand), "the marker adds nothing to the card's text");

            var state = f.Snapshot().State;
            c.Equal("2", state["hand"]![0]!["cost"]!.GetValue<string>(), "the model sees the Strike at 2");
            var seen = state["me"]!["powers"]![0]!;
            c.Equal("Tangled", seen["name"]!.GetValue<string>(), "the model sees Tangled");
            c.Equal(1, seen["amount"]!.GetValue<int>(), "amount 1");
            c.Equal("Debuff", seen["kind"]!.GetValue<string>(), "a Debuff");
            c.Equal("Attack cards cost 1 more energy until the end of this turn.", seen["text"]!.GetValue<string>(), "and what it does");

            f.Play("Strike", 0);
            c.Equal(1, f.Energy, "the Strike took 2 energy");
            c.Equal(55, f.Enemy(0).Hp, "and deals its 6");
            c.That(!f.Execute($"play:{f.HandIndex("Strike")}>e0").Ok, "the second Strike cannot be paid with 1 energy");
            f.Play("Defend");
            c.Equal(0, f.Energy, "a Defend still costs 1");

            f.SetEnergy(3);
            f.Play("Xer", 0);
            c.Equal(0, f.Energy, "the X card takes the 3 energy that is there");
            c.Equal(49, f.Enemy(0).Hp, "and hits 3 times for 2");
            f.SetEnergy(3);
            f.Play("Bash", 0);
            c.Equal(0, f.Energy, "Bash took all 3");

            f.EndTurn();
            c.That(!f.Player.HasPower<TangledPower>(), "gone at the end of the player's turn");
            c.That(!f.AllCards.Any(card => card.Affliction is not null), "and every marker with it, in every pile");
        });

        c.Group("Tangled from the Shambler: Attacks cost 1 more for exactly one player turn", () =>
        {
            var f = Solo(new VineShambler(), 61);
            var keeper = f.Put(Keeper, PileType.Hand);
            f.EndTurn();
            c.That(!f.Player.HasPower<TangledPower>() && !f.AllCards.Any(Marked), "turn 2: nothing yet");
            c.Equal(1, f.CostOf(keeper), "turn 2: the kept Attack costs 1");
            f.EndTurn();

            c.Equal(1, f.Player.PowerAmount<TangledPower>(), "turn 3: Tangled 1, no tick at the end of the enemy turn it landed in");
            c.That(keeper.Pile == PileType.Hand && Marked(keeper), "the Attack kept in hand was marked where it sat");
            c.That(f.AllCards.All(card => Marked(card) == (card.Type == CardType.Attack)), "every Attack in every pile is marked");
            c.That(f.Hand.All(card => f.CostOf(card) == card.BaseCost + (card.Type == CardType.Attack ? 1 : 0)), "every Attack in hand costs 1 more, every Skill the same");
            c.Equal(6, f.Hand.Count, "the kept card and five drawn ones");
            c.Equal("2", f.Snapshot().State["hand"]![0]!["cost"]!.GetValue<string>(), "the model sees the kept Attack at 2");
            f.Play("Keeper", 0);
            c.Equal(1, f.Energy, "it took 2 energy");
            c.Equal(58, f.Enemy(0).Hp, "and deals its 3");
            c.That(keeper.Pile == PileType.Discard && Marked(keeper), "played, it stays marked in the discard pile");
            f.EndTurn();

            c.That(!f.Player.HasPower<TangledPower>(), "turn 4: Tangled is gone");
            c.That(!f.AllCards.Any(Marked), "turn 4: no card is marked");
            c.That(f.Hand.All(card => f.CostOf(card) == card.BaseCost), "turn 4: every card costs what it says");
            f.EndTurn();
            f.EndTurn();
            c.Equal(1, f.Player.PowerAmount<TangledPower>(), "turn 6: tangled again");
            c.That(f.AllCards.All(card => Marked(card) == (card.Type == CardType.Attack)), "turn 6: every Attack is marked again");
        });

        c.Group("Tangled: an Attack that enters the fight while it is up is marked too", () =>
        {
            var f = Solo(new VineShambler(), 61);
            f.ApplyPower<TangledPower>(f.Player, 1, f.Enemy(0));

            var strike = f.CreateCard("StrikeIronclad");
            c.That(strike.Affliction is null, "a new card starts unmarked");
            f.AddGeneratedCard(strike, PileType.Hand);
            c.That(Marked(strike), "a generated Strike is marked on entering");
            c.Equal(2, f.CostOf(strike), "and costs 2");

            var defend = f.CreateCard("DefendIronclad");
            f.AddGeneratedCard(defend, PileType.Draw);
            c.That(defend.Affliction is null, "a generated Skill is not");

            var copy = f.CloneCard(strike);
            f.AddGeneratedCard(copy, PileType.Discard);
            c.That(Marked(copy), "a copy of a marked card is marked");
            c.Equal(1, copy.AfflictionAmount, "once: the marker does not stack");

            var changed = f.Transform(f.Hand[f.HandIndex("Defend")], Content.Card("StrikeIronclad"));
            c.That(changed is not null && Marked(changed), "a card transformed into an Attack is marked");

            // The game refuses to put Entangled on a card that carries any other marker.
            var other = Content.Registry.Afflictions.Keys.FirstOrDefault(k => k != TangledPower.Marker);
            if (other is not null)
            {
                var g = Solo(new VineShambler(), 61);
                var held = g.Hand[g.HandIndex("Strike")];
                g.Afflict(held, other);
                g.ApplyPower<TangledPower>(g.Player, 1, g.Enemy(0));
                c.Equal(other, held.Affliction, $"a card that carries {other} keeps it");
                // The hand is Strike, Strike, Defend, Defend, Bash: with one Strike taken, the other Strike and Bash are left.
                c.Equal(2, g.Hand.Count(Marked), "the other Attacks in hand are marked");
                g.RemovePower<TangledPower>(g.Player);
                c.Equal(other, held.Affliction, $"and Tangled's end does not clear {other}");
            }
        });

        c.Group("Tangled: the surcharge is its amount, added after the card's own cost changes and before late overrides", () =>
        {
            var f = Solo(new VineShambler(), 61);
            f.ApplyPower<TangledPower>(f.Player, 1, f.Enemy(0));
            f.ApplyPower<TangledPower>(f.Player, 1, f.Enemy(0));
            var strike = f.Hand[f.HandIndex("Strike")];
            c.Equal(2, f.Player.PowerAmount<TangledPower>(), "a second application adds to the amount");
            c.Equal(1, strike.AfflictionAmount, "and does not mark again");
            c.Equal(3, f.CostOf(strike), "Strike costs 1 + 2");

            strike.SetCostThisTurn(0);
            c.Equal(2, f.CostOf(strike), "a card set to 0 this turn costs 0 + 2");
            f.ApplyPower<FreeAttacks>(f.Player, 1, f.Player);
            c.Equal(0, f.CostOf(strike), "a late override that makes Attacks free still does");
        });

        c.Group("Tangled is a debuff: Artifact on the player stops it, not the hit", () =>
        {
            var f = Solo(new VineShambler(), 61);
            f.EndTurn();
            f.ApplyPower<ArtifactPower>(f.Player, 1, f.Player);
            c.Equal(8, Lost(f, () => f.EndTurn()), "Grasping Vines still hits for 8");
            c.That(!f.Player.HasPower<TangledPower>(), "no Tangled");
            c.That(!f.Player.HasPower<ArtifactPower>(), "the Artifact is used up");
            c.That(!f.AllCards.Any(Marked), "no card is marked");
            c.That(f.Hand.All(card => f.CostOf(card) == card.BaseCost), "every card costs what it says");
        });

        c.Group("Tangled survives a copy of the fight, and the copy is its own fight", () =>
        {
            var f = Solo(new VineShambler(), 61);
            f.EndTurn();
            f.EndTurn();
            var copy = f.Clone();
            c.Equal(f.Seen(), copy.Seen(), "the copy shows the same turn 3");
            c.That(copy.AllCards.All(card => Marked(card) == (card.Type == CardType.Attack)), "its Attacks are marked");

            copy.RemovePower<TangledPower>(copy.Player);
            c.That(!copy.AllCards.Any(Marked), "ending Tangled in the copy clears the copy's cards");
            c.That(f.Player.HasPower<TangledPower>() && f.AllCards.Count(Marked) == 6, "and leaves the original's six Attacks marked");

            var again = f.Clone();
            f.EndTurn();
            again.EndTurn();
            c.Equal(f.Seen(), again.Seen(), "original and copy reach the same turn 4");
        });
    }

    // ---------- the encounters ----------

    private static void EncounterChecks(Check c)
    {
        c.Group("Shrinker Beetle Weak: one beetle, 38 to 40 HP", () =>
        {
            var tally = new Tally();
            var hps = new SortedSet<int>();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter("ShrinkerBeetleWeak").Seed(seed).Start();
                ExpectLineUp(tally, f, seed, ["ShrinkerBeetle"]);
                tally.Expect(f.Rng.Encounter.Counter == 0, seed, "nothing drawn from the encounter stream");
                hps.Add(f.Enemy(0).MaxHp);
            }
            tally.Report(c);
            c.Sequence([38, 39, 40], hps, "every HP of the range turns up");
        });

        c.Group("Fuzzy Wurm Crawler Weak: one crawler, 55 to 57 HP", () =>
        {
            var tally = new Tally();
            var hps = new SortedSet<int>();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter("FuzzyWurmCrawlerWeak").Seed(seed).Start();
                ExpectLineUp(tally, f, seed, ["FuzzyWurmCrawler"]);
                tally.Expect(f.Rng.Encounter.Counter == 0, seed, "nothing drawn from the encounter stream");
                hps.Add(f.Enemy(0).MaxHp);
            }
            tally.Report(c);
            c.Sequence([55, 56, 57], hps, "every HP of the range turns up");
        });

        c.Group("Overgrowth Crawlers: the beetle first, the crawler second", () =>
        {
            var tally = new Tally();
            var pairs = new HashSet<(int, int)>();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter("OvergrowthCrawlers").Seed(seed).Start();
                ExpectLineUp(tally, f, seed, ["ShrinkerBeetle", "FuzzyWurmCrawler"]);
                tally.Expect(f.Rng.Encounter.Counter == 0, seed, "nothing drawn from the encounter stream");
                if (f.Enemies.Count == 2) pairs.Add((f.Enemy(0).MaxHp, f.Enemy(1).MaxHp));
            }
            tally.Report(c);
            c.Equal(9, pairs.Count, "all 3 x 3 HP pairs turn up");
        });

        c.Group("Ruby Raiders Normal: three different raiders, three picks without replacement from the encounter stream", () =>
        {
            var tally = new Tally();
            var triples = new HashSet<string>(StringComparer.Ordinal);
            var present = RaiderKinds.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
            var first = RaiderKinds.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
            var squeezed = 0;
            for (var seed = 1; seed <= RaiderSeeds; seed++)
            {
                // The three picks by hand: an index into what is left of the game's list, one draw each.
                var rng = RngSet.For(seed, "encounter");
                var left = RaiderKinds.ToList();
                var kinds = new List<string>();
                for (var pick = 0; pick < 3; pick++)
                {
                    var index = rng.NextInt(0, left.Count);
                    kinds.Add(left[index]);
                    left.RemoveAt(index);
                }

                var f = new FightBuilder().Encounter("RubyRaidersNormal").Seed(seed).Start();
                tally.Expect(f.Enemies.Count == 3, seed, "three raiders");
                tally.Expect(f.Enemies.Select(e => e.Monster!.ClassName).Distinct().Count() == 3, seed, "of three different kinds");
                ExpectLineUp(tally, f, seed, kinds);
                tally.Expect(f.Rng.Encounter.Counter == 3, seed, "three draws from the encounter stream");

                triples.Add(string.Join(">", kinds));
                first[kinds[0]]++;
                foreach (var kind in kinds) present[kind]++;

                // Did the no-duplicate rule take a value away from a later raider in this fight?
                for (var j = 1; j < kinds.Count; j++)
                {
                    var sheet = Monsters[kinds[j]];
                    if (f.Enemies.Take(j).Any(e => e.MaxHp >= sheet.MinHp && e.MaxHp <= sheet.MaxHp)) squeezed++;
                }
            }
            tally.Report(c);
            c.Equal(60, triples.Count, "all 5 x 4 x 3 ordered line-ups turn up");
            foreach (var kind in RaiderKinds)
            {
                c.That(Math.Abs(present[kind] - RaiderSeeds * 3 / 5) <= RaiderSeeds / 12, $"{kind} is in about 3 fights of 5 (in {present[kind]} of {RaiderSeeds})");
                c.That(Math.Abs(first[kind] - RaiderSeeds / 5) <= RaiderSeeds / 12, $"{kind} is first in about 1 fight of 5 (in {first[kind]} of {RaiderSeeds})");
            }
            c.That(squeezed > RaiderSeeds / 4, $"the no-duplicate-HP rule was put to work ({squeezed} rolls had a value taken away)");
        });

        c.Group("Vine Shambler Normal: one Shambler, always 61 HP", () =>
        {
            var tally = new Tally();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter("VineShamblerNormal").Seed(seed).Start();
                ExpectLineUp(tally, f, seed, ["VineShambler"]);
                tally.Expect(f.Enemies.Count == 1 && f.Enemy(0).MaxHp == 61, seed, "61 HP");
                tally.Expect(f.Rng.Encounter.Counter == 0, seed, "nothing drawn from the encounter stream");
            }
            tally.Report(c);
        });
    }

    // ---------- whole fights ----------

    private static void WholeFights(Check c)
    {
        c.Group("Every part C encounter plays to an end, the same way twice, and a copy taken on the way plays on alike", () =>
        {
            foreach (var name in Encounters)
            {
                for (var seed = 1; seed <= 4; seed++)
                {
                    var f = new FightBuilder().Encounter(name).Seed(seed).Start();
                    var log = new System.Text.StringBuilder();
                    var copiesAgree = true;
                    for (var n = 0; n < 5000 && !f.IsOver; n++)
                    {
                        var actions = f.LegalActions();
                        // The same rule as CheckKit.PlayOut, so that the two logs can be compared.
                        var pick = actions.FirstOrDefault(a => a.Id != "end_turn" && !a.Id.Contains("potion", StringComparison.Ordinal)) ?? actions[^1];
                        log.Append(pick.Id).Append('|').Append(f.Seen()).Append('\n');
                        var copy = f.Clone();
                        f.Do(pick.Id);
                        copy.Do(pick.Id);
                        copiesAgree &= copy.Seen() == f.Seen();
                    }
                    log.Append(f.Seen());

                    c.That(f.IsOver, $"{name} seed {seed}: the fight ends");
                    c.That(copiesAgree, $"{name} seed {seed}: a copy taken before any action ends up where the original does");
                    c.That(log.ToString() == CheckKit.PlayOut(new FightBuilder().Encounter(name).Seed(seed).Start()), $"{name} seed {seed}: the fight replays identically");
                    c.Equal(0, f.Rng.MonsterAi.Counter, $"{name} seed {seed}: no move was ever chosen at random");
                }
            }
        });
    }

    // ---------- a second reading of every hallway fight ----------

    /// <summary>
    /// Every hallway monster, encounter and rule of the three monster files written a second
    /// time, straight from the game's classes and by another reader, as a small fight of its
    /// own: the line-ups and HP rolls, each move machine with the game's branch rule, what each
    /// move does, and the rules a starter deck meets (Vulnerable, Frail, Shrink, Slippery,
    /// Artifact, Constrict, Tangled, Illusion, Minion, move Block, deaths). It shares no code
    /// with the simulator but the random streams. Each fight is played turn by turn in both and
    /// everything a player could see is compared.
    /// </summary>
    private static class SecondReading
    {
        private enum Rep { Forever, XTimes, CannotRepeat, Once }

        private sealed record Opt(string Id, Rep Rep, float Weight = 1f, int Max = 0, int Cooldown = 0);

        private const string Revive = "REVIVE_MOVE";

        /// <summary>Fights per encounter and style in the turn-by-turn comparison.</summary>
        private const int FightSeeds = 80;

        /// <summary>One monster of the second fight.</summary>
        private sealed class Unit
        {
            public string Kind = "";
            public int Roster, Hp, MaxHp, Block, Strength, Vulnerable, Slippery, Artifact;
            public bool Reviving, Spawned, Performed, RevivePerformed;
            public string Init = "", Cur = "", ReviveFollowUp = "";
            public Dictionary<string, string> Follow = new();
            public Dictionary<string, Opt[]> Branches = new();
            public List<string> Log = new();

            public bool Alive => Hp > 0;
            public bool IsMove(string id) => !Branches.ContainsKey(id);
        }

        /// <summary>The second fight: the player, the enemies in acting order, the two streams it draws from.</summary>
        private sealed class World
        {
            public int Hp, MaxHp, Block, Energy, Turn = 1;
            public int Vulnerable, Frail, Constrict, Tangled, Shrink, Slimed, Dazed;
            public bool SkipVulnerable, SkipFrail, Won;
            public List<Unit> Enemies = new();
            public int NextRoster, Draws;
            public GameRng Ai = null!, Niche = null!;
        }

        // ----- HP ranges, ascension 0 -----

        private static readonly Dictionary<string, (int Min, int Max)> HpRange = new(StringComparer.Ordinal)
        {
            ["Nibbit"] = (42, 46), ["CubexConstruct"] = (65, 65), ["Fogmog"] = (74, 74), ["EyeWithTeeth"] = (6, 6), ["Inklet"] = (11, 17),
            ["Mawler"] = (72, 72), ["LeafSlimeM"] = (32, 35), ["LeafSlimeS"] = (11, 15), ["TwigSlimeM"] = (26, 28), ["TwigSlimeS"] = (7, 11),
            ["Flyconid"] = (47, 49), ["SnappingJaxfruit"] = (31, 33), ["SlitheringStrangler"] = (53, 55), ["ShrinkerBeetle"] = (38, 40),
            ["FuzzyWurmCrawler"] = (55, 57), ["AxeRubyRaider"] = (20, 22), ["AssassinRubyRaider"] = (18, 23), ["BruteRubyRaider"] = (30, 33),
            ["CrossbowRubyRaider"] = (18, 21), ["TrackerRubyRaider"] = (21, 25), ["VineShambler"] = (61, 61),
        };

        internal static readonly string[] Hallway =
        [
            "NibbitsWeak", "NibbitsNormal", "CubexConstructNormal", "FogmogNormal", "InkletsNormal", "MawlerNormal",
            "SlimesWeak", "SlimesNormal", "FlyconidNormal", "SnappingJaxfruitNormal", "SlitheringStranglerNormal",
            "ShrinkerBeetleWeak", "FuzzyWurmCrawlerWeak", "OvergrowthCrawlers", "RubyRaidersNormal", "VineShamblerNormal",
        ];

        /// <summary>How many different line-ups the game's composition rule can produce.</summary>
        private static int LineUpCount(string encounter) => encounter switch
        {
            "SlimesWeak" => 4,                  // which small slime first, Leaf or Twig in the middle
            "SlimesNormal" => 2,                // the order of the two small ones
            "FlyconidNormal" => 2,              // Leaf or Twig
            "SlitheringStranglerNormal" => 7,   // Jaxfruit; a medium slime (2); two small slimes, each on its own (4)
            "RubyRaidersNormal" => 60,          // 5 x 4 x 3, in order
            _ => 1,
        };

        // ----- the line-up, by each encounter's own rule and stream -----

        /// <summary>Kind and variant ("alone", "front", "middle") of every monster, in creation order.</summary>
        private static List<(string Kind, string Variant)> LineUp(string encounter, GameRng rng)
        {
            string[] smalls = ["LeafSlimeS", "TwigSlimeS"];
            string[] mediums = ["LeafSlimeM", "TwigSlimeM"];
            List<(string, string)> Plain(params string[] kinds) => kinds.Select(k => (k, "")).ToList();
            switch (encounter)
            {
                case "NibbitsWeak": return [("Nibbit", "alone")];
                case "NibbitsNormal": return [("Nibbit", "front"), ("Nibbit", "")];
                case "CubexConstructNormal": return Plain("CubexConstruct");
                case "FogmogNormal": return Plain("Fogmog");
                case "InkletsNormal": return [("Inklet", ""), ("Inklet", "middle"), ("Inklet", "")];
                case "MawlerNormal": return Plain("Mawler");
                case "SlimesWeak":
                {
                    // One of the two small ones, then "one" of the one that is left (a draw all the same), then the medium one.
                    var left = smalls.ToList();
                    var first = left[rng.NextInt(0, left.Count)];
                    left.Remove(first);
                    var last = left[rng.NextInt(0, left.Count)];
                    var medium = mediums[rng.NextInt(0, mediums.Length)];
                    return Plain(first, medium, last);
                }
                case "SlimesNormal":
                {
                    var leafFirst = rng.NextInt(2) == 0;
                    return Plain("TwigSlimeM", "LeafSlimeM", leafFirst ? "LeafSlimeS" : "TwigSlimeS", leafFirst ? "TwigSlimeS" : "LeafSlimeS");
                }
                case "FlyconidNormal": return Plain(mediums[rng.NextInt(0, mediums.Length)], "Flyconid");
                case "SnappingJaxfruitNormal": return Plain("SnappingJaxfruit", "Flyconid");
                case "SlitheringStranglerNormal":
                {
                    var kinds = new List<string>();
                    switch (rng.NextInt(0, 3))
                    {
                        case 0: kinds.Add("SnappingJaxfruit"); break;
                        case 1: kinds.Add(mediums[rng.NextInt(0, mediums.Length)]); break;
                        default:
                            kinds.Add(smalls[rng.NextInt(0, smalls.Length)]);
                            kinds.Add(smalls[rng.NextInt(0, smalls.Length)]);
                            break;
                    }
                    kinds.Add("SlitheringStrangler");
                    return Plain(kinds.ToArray());
                }
                case "ShrinkerBeetleWeak": return Plain("ShrinkerBeetle");
                case "FuzzyWurmCrawlerWeak": return Plain("FuzzyWurmCrawler");
                case "OvergrowthCrawlers": return Plain("ShrinkerBeetle", "FuzzyWurmCrawler");
                case "RubyRaidersNormal":
                {
                    string[] all = ["AxeRubyRaider", "AssassinRubyRaider", "BruteRubyRaider", "CrossbowRubyRaider", "TrackerRubyRaider"];
                    var picked = new List<string>();
                    for (var i = 0; i < 3; i++)
                    {
                        var open = all.Where(r => !picked.Contains(r)).ToList();
                        picked.Add(open[rng.NextInt(0, open.Count)]);
                    }
                    return Plain(picked.ToArray());
                }
                case "VineShamblerNormal": return Plain("VineShambler");
                default: throw new InvalidOperationException(encounter);
            }
        }

        /// <summary>The HP roll: one of the values of the range that no enemy in the fight has as its
        /// maximum, in rising order; with none left, any value of the range.</summary>
        private static int RollHp(World w, string kind)
        {
            var (min, max) = HpRange[kind];
            var free = Enumerable.Range(min, max - min + 1).Where(v => w.Enemies.All(e => e.MaxHp != v)).ToList();
            return free.Count > 0 ? free[w.Niche.NextInt(0, free.Count)] : w.Niche.NextInt(min, max + 1);
        }

        // ----- the machines -----

        /// <summary>a, b, c, d: a is followed by b, b by c, c by d.</summary>
        private static void Chain(Unit u, params string[] moves)
        {
            for (var i = 0; i + 1 < moves.Length; i++) u.Follow[moves[i]] = moves[i + 1];
        }

        private static Unit NewUnit(World w, string kind, string variant)
        {
            var u = new Unit { Kind = kind, Roster = w.NextRoster++ };
            u.Hp = u.MaxHp = RollHp(w, kind);
            switch (kind)
            {
                case "Nibbit":
                    u.Init = variant == "alone" ? "BUTT_MOVE" : variant == "front" ? "SLICE_MOVE" : "HISS_MOVE";
                    Chain(u, "BUTT_MOVE", "SLICE_MOVE", "HISS_MOVE", "BUTT_MOVE");
                    break;
                case "CubexConstruct":
                    u.Init = "CHARGE_UP_MOVE";
                    Chain(u, "CHARGE_UP_MOVE", "REPEATER_BLAST_MOVE", "REPEATER_BLAST_MOVE_2", "EXPEL_MOVE", "REPEATER_BLAST_MOVE");
                    u.Artifact = 1;   // and no Block: the fight is not in progress yet when it asks for its 13
                    break;
                case "Fogmog":
                    u.Init = "ILLUSION_MOVE";
                    Chain(u, "ILLUSION_MOVE", "SWIPE_MOVE", "BRANCH");
                    Chain(u, "SWIPE_RANDOM_MOVE", "HEADBUTT_MOVE", "SWIPE_MOVE");
                    u.Branches["BRANCH"] = [new Opt("SWIPE_RANDOM_MOVE", Rep.CannotRepeat, 0.4f), new Opt("HEADBUTT_MOVE", Rep.CannotRepeat, 0.6f)];
                    break;
                case "EyeWithTeeth":
                    u.Init = "DISTRACT_MOVE";
                    Chain(u, "DISTRACT_MOVE", "DISTRACT_MOVE");
                    break;
                case "Inklet":
                    u.Init = variant == "middle" ? "WHIRLWIND_MOVE" : "JAB_MOVE";
                    Chain(u, "JAB_MOVE", "RAND");
                    Chain(u, "WHIRLWIND_MOVE", "JAB_MOVE");
                    Chain(u, "PIERCING_GAZE_MOVE", "JAB_MOVE");
                    u.Branches["RAND"] = [new Opt("PIERCING_GAZE_MOVE", Rep.CannotRepeat), new Opt("WHIRLWIND_MOVE", Rep.CannotRepeat)];
                    u.Slippery = 1;
                    break;
                case "Mawler":
                    u.Init = "CLAW_MOVE";
                    Chain(u, "RIP_AND_TEAR_MOVE", "RAND");
                    Chain(u, "ROAR_MOVE", "RAND");
                    Chain(u, "CLAW_MOVE", "RAND");
                    u.Branches["RAND"] = [new Opt("RIP_AND_TEAR_MOVE", Rep.CannotRepeat), new Opt("ROAR_MOVE", Rep.Once), new Opt("CLAW_MOVE", Rep.CannotRepeat)];
                    break;
                case "LeafSlimeM":
                    u.Init = "STICKY_SHOT";
                    Chain(u, "STICKY_SHOT", "CLUMP_SHOT", "STICKY_SHOT");
                    break;
                case "LeafSlimeS":
                    u.Init = "RAND";
                    Chain(u, "TACKLE_MOVE", "RAND");
                    Chain(u, "GOOP_MOVE", "RAND");
                    u.Branches["RAND"] = [new Opt("TACKLE_MOVE", Rep.CannotRepeat), new Opt("GOOP_MOVE", Rep.CannotRepeat)];
                    break;
                case "TwigSlimeM":
                    u.Init = "STICKY_SHOT_MOVE";
                    Chain(u, "POKEY_POUNCE_MOVE", "RAND");
                    Chain(u, "STICKY_SHOT_MOVE", "RAND");
                    u.Branches["RAND"] = [new Opt("POKEY_POUNCE_MOVE", Rep.XTimes, Max: 2), new Opt("STICKY_SHOT_MOVE", Rep.CannotRepeat)];
                    break;
                case "TwigSlimeS":
                    u.Init = "TACKLE_MOVE";
                    Chain(u, "TACKLE_MOVE", "TACKLE_MOVE");
                    break;
                case "Flyconid":
                    u.Init = "INITIAL";
                    Chain(u, "VULNERABLE_SPORES_MOVE", "RAND");
                    Chain(u, "FRAIL_SPORES_MOVE", "RAND");
                    Chain(u, "SMASH_MOVE", "RAND");
                    u.Branches["RAND"] =
                    [
                        new Opt("VULNERABLE_SPORES_MOVE", Rep.CannotRepeat, Cooldown: 3),
                        new Opt("FRAIL_SPORES_MOVE", Rep.CannotRepeat, Cooldown: 2),
                        new Opt("SMASH_MOVE", Rep.CannotRepeat),
                    ];
                    u.Branches["INITIAL"] = [new Opt("FRAIL_SPORES_MOVE", Rep.CannotRepeat, Cooldown: 2), new Opt("SMASH_MOVE", Rep.CannotRepeat)];
                    break;
                case "SnappingJaxfruit":
                    u.Init = "ENERGY_ORB_MOVE";
                    Chain(u, "ENERGY_ORB_MOVE", "ENERGY_ORB_MOVE");
                    break;
                case "SlitheringStrangler":
                    u.Init = "CONSTRICT";
                    Chain(u, "CONSTRICT", "rand");
                    Chain(u, "THWACK", "CONSTRICT");
                    Chain(u, "LASH", "CONSTRICT");
                    u.Branches["rand"] = [new Opt("THWACK", Rep.Forever), new Opt("LASH", Rep.Forever)];
                    break;
                case "ShrinkerBeetle":
                    u.Init = "SHRINKER_MOVE";
                    Chain(u, "SHRINKER_MOVE", "CHOMP_MOVE", "STOMP_MOVE", "CHOMP_MOVE");
                    break;
                case "FuzzyWurmCrawler":
                    u.Init = "FIRST_ACID_GOOP";
                    Chain(u, "FIRST_ACID_GOOP", "INHALE", "ACID_GOOP", "FIRST_ACID_GOOP");
                    break;
                case "AxeRubyRaider":
                    u.Init = "SWING_1";
                    Chain(u, "SWING_1", "SWING_2", "BIG_SWING", "SWING_1");
                    break;
                case "AssassinRubyRaider":
                    u.Init = "KILLSHOT_MOVE";
                    Chain(u, "KILLSHOT_MOVE", "KILLSHOT_MOVE");
                    break;
                case "BruteRubyRaider":
                    u.Init = "BEAT_MOVE";
                    Chain(u, "BEAT_MOVE", "ROAR_MOVE", "BEAT_MOVE");
                    break;
                case "CrossbowRubyRaider":
                    u.Init = "RELOAD_MOVE";
                    Chain(u, "RELOAD_MOVE", "FIRE_MOVE", "RELOAD_MOVE");
                    break;
                case "TrackerRubyRaider":
                    u.Init = "TRACK_MOVE";
                    Chain(u, "TRACK_MOVE", "HOUNDS_MOVE", "HOUNDS_MOVE");
                    break;
                case "VineShambler":
                    u.Init = "SWIPE_MOVE";
                    Chain(u, "SWIPE_MOVE", "GRASPING_VINES_MOVE", "CHOMP_MOVE", "SWIPE_MOVE");
                    break;
                default:
                    throw new InvalidOperationException(kind);
            }
            // The machine starts in its initial state; a move is logged at once, a branch is not.
            u.Cur = u.Init;
            if (u.IsMove(u.Cur)) u.Log.Add(u.Cur);
            return u;
        }

        /// <summary>The weight of one option of a random branch, from the log of chosen moves.</summary>
        private static float Weight(Unit u, Opt o)
        {
            var log = u.Log;
            var open = 1f;
            if (o.Rep == Rep.Once)
            {
                if (log.Contains(o.Id)) open = 0f;
            }
            else if (o.Rep != Rep.Forever)
            {
                // Shut exactly when the last n chosen moves were all this one (n = 1 for "cannot repeat").
                var n = o.Rep == Rep.CannotRepeat ? 1 : o.Max;
                open = log.Count < n ? 1f : 0f;
                for (var back = 0; log.Count >= n && back < n && log.Count - back > 0; back++)
                {
                    if (log[log.Count - 1 - back] != o.Id)
                    {
                        open = 1f;
                        break;
                    }
                }
            }
            // A cooldown shuts the option while it is among the last so many chosen moves.
            if (o.Cooldown > 0 && Enumerable.Reverse(log).Take(o.Cooldown).Contains(o.Id)) return 0f;
            return open * o.Weight;
        }

        /// <summary>Chooses the next move, as at the start of a player turn (and once when the monster joins on the player's side).</summary>
        private static void Roll(World w, Unit u)
        {
            if (u.Cur == Revive)
            {
                // A move that must be performed once cannot be left before that.
                if (!u.RevivePerformed) return;
                u.Cur = u.ReviveFollowUp;
                u.Log.Add(u.Cur);
                return;
            }
            // Before its first performed move a monster keeps the move it has.
            if (!u.Performed && u.IsMove(u.Cur)) return;
            string? logged = null;
            do
            {
                string next;
                if (u.IsMove(u.Cur))
                {
                    next = u.Follow[u.Cur];
                }
                else
                {
                    // One float below the sum of the weights, also when the sum is 0; then down the list.
                    var options = u.Branches[u.Cur];
                    var roll = w.Ai.NextFloat(options.Sum(o => Weight(u, o)));
                    w.Draws++;
                    next = "";
                    foreach (var o in options)
                    {
                        roll -= Weight(u, o);
                        if (roll <= 0f)
                        {
                            next = o.Id;
                            break;
                        }
                    }
                    if (next.Length == 0) throw new InvalidOperationException("The branch found no state.");
                }
                u.Cur = next;
                if (logged is null && u.IsMove(u.Cur)) logged = u.Cur;
            }
            while (!u.IsMove(u.Cur));
            if (logged is not null) u.Log.Add(logged);
        }

        // ----- what the moves do -----

        /// <summary>Base damage and hit count of a move's attack; (0, 0) for a move without one.</summary>
        private static (int Damage, int Hits) AttackOf(Unit u) => (u.Kind, u.Cur) switch
        {
            ("Nibbit", "BUTT_MOVE") => (12, 1),
            ("Nibbit", "SLICE_MOVE") => (6, 1),
            ("CubexConstruct", "REPEATER_BLAST_MOVE" or "REPEATER_BLAST_MOVE_2") => (7, 1),
            ("CubexConstruct", "EXPEL_MOVE") => (5, 2),
            ("Fogmog", "SWIPE_MOVE" or "SWIPE_RANDOM_MOVE") => (8, 1),
            ("Fogmog", "HEADBUTT_MOVE") => (14, 1),
            ("Inklet", "JAB_MOVE") => (3, 1),
            ("Inklet", "WHIRLWIND_MOVE") => (2, 3),
            ("Inklet", "PIERCING_GAZE_MOVE") => (10, 1),
            ("Mawler", "RIP_AND_TEAR_MOVE") => (14, 1),
            ("Mawler", "CLAW_MOVE") => (4, 2),
            ("LeafSlimeM", "CLUMP_SHOT") => (8, 1),
            ("LeafSlimeS", "TACKLE_MOVE") => (3, 1),
            ("TwigSlimeM", "POKEY_POUNCE_MOVE") => (11, 1),
            ("TwigSlimeS", "TACKLE_MOVE") => (4, 1),
            ("Flyconid", "FRAIL_SPORES_MOVE") => (8, 1),
            ("Flyconid", "SMASH_MOVE") => (11, 1),
            ("SnappingJaxfruit", "ENERGY_ORB_MOVE") => (3, 1),
            ("SlitheringStrangler", "THWACK") => (7, 1),
            ("SlitheringStrangler", "LASH") => (12, 1),
            ("ShrinkerBeetle", "CHOMP_MOVE") => (7, 1),
            ("ShrinkerBeetle", "STOMP_MOVE") => (13, 1),
            ("FuzzyWurmCrawler", "FIRST_ACID_GOOP" or "ACID_GOOP") => (4, 1),
            ("AxeRubyRaider", "SWING_1" or "SWING_2") => (5, 1),
            ("AxeRubyRaider", "BIG_SWING") => (12, 1),
            ("AssassinRubyRaider", "KILLSHOT_MOVE") => (10, 1),
            ("BruteRubyRaider", "BEAT_MOVE") => (7, 1),
            ("CrossbowRubyRaider", "FIRE_MOVE") => (14, 1),
            ("TrackerRubyRaider", "HOUNDS_MOVE") => (1, 8),
            ("VineShambler", "SWIPE_MOVE") => (6, 2),
            ("VineShambler", "GRASPING_VINES_MOVE") => (8, 1),
            ("VineShambler", "CHOMP_MOVE") => (16, 1),
            _ => (0, 0),
        };

        /// <summary>One hit of a monster on the player as it is now: Strength added, Vulnerable times 1.5, rounded down.</summary>
        private static int MonsterHit(World w, Unit u, int baseDamage)
        {
            decimal damage = baseDamage + u.Strength;
            if (w.Vulnerable > 0) damage *= 1.5m;
            return (int)damage;
        }

        private static int Incoming(World w) => w.Enemies.Sum(u =>
        {
            var (damage, hits) = AttackOf(u);
            return hits == 0 ? 0 : MonsterHit(w, u, damage) * hits;
        });

        /// <summary>Damage on the player that Block absorbs.</summary>
        private static void HurtPlayer(World w, int damage)
        {
            var blocked = Math.Min(w.Block, damage);
            w.Block -= blocked;
            w.Hp -= damage - blocked;
        }

        /// <summary>A debuff with a duration that is new on the player does not count down at the end of the turn it lands in.</summary>
        private static void AddVulnerable(World w, int amount)
        {
            if (w.Vulnerable == 0) w.SkipVulnerable = true;
            w.Vulnerable += amount;
        }

        private static void AddFrail(World w, int amount)
        {
            if (w.Frail == 0) w.SkipFrail = true;
            w.Frail += amount;
        }

        private static void Perform(World w, Unit u)
        {
            if (u.Cur == Revive)
            {
                u.Reviving = false;
                u.Hp = u.MaxHp;
                u.RevivePerformed = true;
                return;
            }

            // Every move that attacks does so first, hit by hit.
            var (damage, hits) = AttackOf(u);
            for (var i = 0; i < hits; i++) HurtPlayer(w, MonsterHit(w, u, damage));

            switch (u.Kind, u.Cur)
            {
                case ("Nibbit", "SLICE_MOVE"): u.Block += 5; break;
                case ("Nibbit", "HISS_MOVE"): u.Strength += 2; break;
                case ("CubexConstruct", "CHARGE_UP_MOVE"): u.Strength += 2; break;
                case ("CubexConstruct", "REPEATER_BLAST_MOVE" or "REPEATER_BLAST_MOVE_2"): u.Strength += 2; break;
                case ("Fogmog", "ILLUSION_MOVE"):
                {
                    // The Eye takes the slot before Fogmog's and does not act in the turn it arrives.
                    var eye = NewUnit(w, "EyeWithTeeth", "");
                    eye.Spawned = true;
                    w.Enemies.Insert(0, eye);
                    break;
                }
                case ("Fogmog", "SWIPE_MOVE" or "SWIPE_RANDOM_MOVE"): u.Strength += 1; break;
                case ("EyeWithTeeth", "DISTRACT_MOVE"): w.Dazed += 3; break;
                case ("Mawler", "ROAR_MOVE"): AddVulnerable(w, 3); break;
                case ("LeafSlimeM", "STICKY_SHOT"): w.Slimed += 2; break;
                case ("LeafSlimeS", "GOOP_MOVE"): w.Slimed += 1; break;
                case ("TwigSlimeM", "STICKY_SHOT_MOVE"): w.Slimed += 1; break;
                case ("Flyconid", "VULNERABLE_SPORES_MOVE"): AddVulnerable(w, 2); break;
                case ("Flyconid", "FRAIL_SPORES_MOVE"): AddFrail(w, 2); break;
                case ("SnappingJaxfruit", "ENERGY_ORB_MOVE"): u.Strength += 2; break;
                case ("SlitheringStrangler", "CONSTRICT"): w.Constrict += 3; break;
                case ("SlitheringStrangler", "THWACK"): u.Block += 5; break;
                case ("ShrinkerBeetle", "SHRINKER_MOVE"): w.Shrink = -1; break;
                case ("FuzzyWurmCrawler", "INHALE"): u.Strength += 7; break;
                case ("AxeRubyRaider", "SWING_1" or "SWING_2"): u.Block += 5; break;
                case ("BruteRubyRaider", "ROAR_MOVE"): u.Strength += 3; break;
                case ("CrossbowRubyRaider", "RELOAD_MOVE"): u.Block += 3; break;
                case ("TrackerRubyRaider", "TRACK_MOVE"): AddFrail(w, 2); break;
                case ("VineShambler", "GRASPING_VINES_MOVE"): w.Tangled += 1; break;
            }
        }

        // ----- the player's three cards -----

        private static bool CanBeHit(Unit u) => u.Alive && !u.Reviving;

        private static void Kill(World w, Unit u)
        {
            u.Hp = 0;
            if (u.Kind == "EyeWithTeeth")
            {
                // Illusion: it stays where it is, out of reach, loses its debuffs, and its next move is the revive.
                u.Reviving = true;
                u.Vulnerable = 0;
                u.ReviveFollowUp = u.Log[^1];
                u.RevivePerformed = false;
                u.Cur = Revive;
            }
            else
            {
                w.Enemies.Remove(u);
                // What the dead monster put on the player ends with it.
                if (u.Kind == "ShrinkerBeetle") w.Shrink = 0;
                if (u.Kind == "SlitheringStrangler") w.Constrict = 0;
                // The last primary enemy takes its minion along.
                var living = w.Enemies.Where(e => e.Alive).ToList();
                if (living.Count != 0 && living.All(e => e.Kind == "EyeWithTeeth"))
                    foreach (var minion in living) Kill(w, minion);
            }
            w.Won = !w.Enemies.Any(e => e.Alive && e.Kind != "EyeWithTeeth");
        }

        /// <summary>One hit of the player's Strike or Bash: Shrink times 0.7, the target's Vulnerable times 1.5, rounded down once.</summary>
        private static void PlayerHit(World w, Unit target, int baseDamage)
        {
            decimal damage = baseDamage;
            if (w.Shrink != 0) damage *= 0.7m;
            if (target.Vulnerable > 0) damage *= 1.5m;
            var hit = (int)damage;
            var blocked = Math.Min(target.Block, hit);
            target.Block -= blocked;
            var loss = hit - blocked;
            // Slippery: a loss of 1 or more is 1, and one stack goes.
            var slipped = target.Slippery > 0 && loss >= 1;
            if (slipped) loss = 1;
            target.Hp -= Math.Min(loss, target.Hp);
            if (target.Hp == 0) Kill(w, target);
            else if (slipped) target.Slippery--;
        }

        private static void PlayCard(World w, string card, Unit? target)
        {
            switch (card)
            {
                case "Strike":
                    w.Energy -= 1 + w.Tangled;
                    PlayerHit(w, target!, 6);
                    break;
                case "Bash":
                    w.Energy -= 2 + w.Tangled;
                    PlayerHit(w, target!, 8);
                    if (!w.Won && CanBeHit(target!))
                    {
                        // Artifact takes the debuff and loses a stack.
                        if (target!.Artifact > 0) target.Artifact--;
                        else target.Vulnerable += 2;
                    }
                    break;
                case "Defend":
                    w.Energy -= 1;
                    w.Block += w.Frail > 0 ? (int)(5 * 0.75m) : 5;
                    break;
            }
        }

        private static void EndTurn(World w)
        {
            // The end of the player's turn: Constrict hurts (Block absorbs it), Tangled is over.
            if (w.Constrict > 0) HurtPlayer(w, w.Constrict);
            w.Tangled = 0;

            // The enemy turn: every monster loses its Block, then those that were there at its start act in order.
            foreach (var u in w.Enemies) u.Block = 0;
            foreach (var u in w.Enemies.ToList())
            {
                if (!w.Enemies.Contains(u) || u.Spawned) continue;
                Perform(w, u);
                u.Performed = true;
            }

            // Its end: durations count down, for everyone.
            if (w.Vulnerable > 0)
            {
                if (w.SkipVulnerable) w.SkipVulnerable = false;
                else w.Vulnerable--;
            }
            if (w.Frail > 0)
            {
                if (w.SkipFrail) w.SkipFrail = false;
                else w.Frail--;
            }
            foreach (var u in w.Enemies)
            {
                if (u.Vulnerable > 0) u.Vulnerable--;
                u.Spawned = false;
            }

            // The next player turn: moves are chosen in list order, the player's Block falls, energy is 3 again.
            w.Turn++;
            foreach (var u in w.Enemies) Roll(w, u);
            w.Block = 0;
            w.Energy = 3;
        }

        private static World Begin(string encounter, int seed, int hp)
        {
            var w = new World { Hp = hp, MaxHp = hp, Energy = 3, Ai = RngSet.For(seed, "monster_ai"), Niche = RngSet.For(seed, "niche") };
            foreach (var (kind, variant) in LineUp(encounter, RngSet.For(seed, "encounter"))) w.Enemies.Add(NewUnit(w, kind, variant));
            // Each monster's first move is settled as it joins, in list order.
            foreach (var u in w.Enemies) Roll(w, u);
            return w;
        }

        // ----- what both fights show -----

        private static string Line(World w)
        {
            var s = new StringBuilder();
            s.Append(CultureInfo.InvariantCulture, $"turn {w.Turn} hp {w.Hp} block {w.Block} energy {w.Energy} vulnerable {w.Vulnerable} frail {w.Frail} constrict {w.Constrict}");
            s.Append(CultureInfo.InvariantCulture, $" tangled {w.Tangled} shrink {w.Shrink} slimed {w.Slimed} dazed {w.Dazed} draws {w.Draws} incoming {Incoming(w)}");
            foreach (var u in w.Enemies)
            {
                s.Append(CultureInfo.InvariantCulture, $" | e{u.Roster} {u.Kind} {u.Hp}/{u.MaxHp} block {u.Block} strength {u.Strength} vulnerable {u.Vulnerable}");
                s.Append(CultureInfo.InvariantCulture, $" slippery {u.Slippery} artifact {u.Artifact} {u.Cur}{(u.Reviving ? " reviving" : "")}");
            }
            return s.ToString();
        }

        private static string Line(Fight f)
        {
            var p = f.Player;
            int Cards(string name) => f.AllCards.Count(card => card.Def.ClassName == name);
            var s = new StringBuilder();
            s.Append(CultureInfo.InvariantCulture, $"turn {f.Turn} hp {p.Hp} block {p.Block} energy {f.Energy} vulnerable {p.PowerAmount<VulnerablePower>()} frail {p.PowerAmount<FrailPower>()} constrict {p.PowerAmount("ConstrictPower")}");
            s.Append(CultureInfo.InvariantCulture, $" tangled {p.PowerAmount("TangledPower")} shrink {p.PowerAmount<ShrinkPower>()} slimed {Cards("Slimed")} dazed {Cards("Dazed")} draws {f.Rng.MonsterAi.Counter} incoming {f.IncomingDamage()}");
            foreach (var e in f.Enemies)
            {
                s.Append(CultureInfo.InvariantCulture, $" | e{e.RosterIndex} {e.Monster!.ClassName} {e.Hp}/{e.MaxHp} block {e.Block} strength {e.PowerAmount<StrengthPower>()} vulnerable {e.PowerAmount<VulnerablePower>()}");
                s.Append(CultureInfo.InvariantCulture, $" slippery {e.PowerAmount<SlipperyPower>()} artifact {e.PowerAmount<ArtifactPower>()} {e.Monster.NextMove!.Id}{(e.IsDead ? " reviving" : "")}");
            }
            return s.ToString();
        }

        // ----- the players -----

        /// <summary>The cards a player of the given style plays this turn, decided from the second fight alone.
        /// 0 plays nothing; 1 attacks the first enemy it can, Bash before Strikes, then defends; 2 defends
        /// twice and then attacks the last enemy; 3 strikes the first and the last in turn and never bashes;
        /// 4 is 1 aimed at the last enemy.</summary>
        private static IEnumerable<(string Card, Unit? Target)> Plays(World w, int style)
        {
            if (style == 0) yield break;
            // Seventy cards in the starter order: no reshuffle in fourteen turns, so the hand is known.
            var strikes = w.Turn % 2 == 1 ? 2 : 3;
            var defends = 2;
            var bash = w.Turn % 2 == 1 ? 1 : 0;
            var card = 0;

            Unit? Target()
            {
                var open = w.Enemies.Where(CanBeHit).ToList();
                if (open.Count == 0) return null;
                var last = style is 2 or 4 || (style == 3 && card % 2 == 1);
                return last ? open[^1] : open[0];
            }

            if (style == 2)
            {
                while (defends > 0 && w.Energy >= 1)
                {
                    defends--;
                    yield return ("Defend", null);
                }
            }
            if (style != 3 && bash > 0 && w.Energy >= 2 + w.Tangled && Target() is { } bashed)
            {
                bash--;
                card++;
                yield return ("Bash", bashed);
            }
            while (!w.Won && strikes > 0 && w.Energy >= 1 + w.Tangled && Target() is { } struck)
            {
                strikes--;
                card++;
                yield return ("Strike", struck);
            }
            while (!w.Won && defends > 0 && w.Energy >= 1)
            {
                defends--;
                yield return ("Defend", null);
            }
        }

        internal static void Run(Check c)
        {
            c.Group("second reading: every line-up and HP roll, 400 seeds of every hallway encounter", () =>
            {
                foreach (var encounter in Hallway)
                {
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    var wrong = 0;
                    string? first = null;
                    for (var seed = 1; seed <= 400; seed++)
                    {
                        var w = Begin(encounter, seed, 80);
                        var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Encounter(encounter).Seed(seed).Start();
                        seen.Add(string.Join(",", w.Enemies.Select(u => u.Kind)));
                        var expected = string.Join(" ", w.Enemies.Select(u => $"{u.Kind}:{u.MaxHp}:{u.Hp}"));
                        var actual = string.Join(" ", f.Enemies.Select(e => $"{e.Monster!.ClassName}:{e.MaxHp}:{e.Hp}"));
                        if (expected == actual) continue;
                        wrong++;
                        first ??= $"seed {seed}: expected {expected}, got {actual}";
                    }
                    c.That(wrong == 0, $"{encounter}: the monsters, their order and their HP ({wrong} of 400 seeds differ; {first})");
                    c.Equal(LineUpCount(encounter), seen.Count, $"{encounter}: every line-up the game can make came up, and no other");
                }
            });

            string[] styles =
            [
                "plays nothing", "attacks the first enemy", "defends, then attacks the last enemy", "strikes the first and the last in turn",
                "attacks the last enemy",
            ];
            for (var style = 0; style < styles.Length; style++)
            {
                var s = style;
                c.Group($"second reading: whole fights, turn by turn, a player that {styles[s]}", () =>
                {
                    const int turns = 14;
                    const int hp = 9000;
                    var deck = Enumerable.Repeat(CheckKit.StarterInOrder, 7).SelectMany(x => x).ToArray();
                    foreach (var encounter in Hallway)
                    {
                        var wrong = 0;
                        var won = 0;
                        string? first = null;
                        for (var seed = 1; seed <= FightSeeds; seed++)
                        {
                            var w = Begin(encounter, seed, hp);
                            var f = new FightBuilder().DrawOrder(deck).Hp(hp, hp).Encounter(encounter).Seed(seed).Start();
                            string? difference = null;

                            void Compare(string at)
                            {
                                if (difference is not null) return;
                                var (expected, actual) = (Line(w), Line(f));
                                if (expected != actual) difference = $"seed {seed}, {at}: expected [{expected}], got [{actual}]";
                            }

                            while (difference is null && !w.Won && w.Turn <= turns)
                            {
                                Compare("at the start of the turn");
                                var wanted = w.Turn % 2 == 1 ? "Strike,Strike,Defend,Defend,Bash" : "Strike,Strike,Strike,Defend,Defend";
                                if (difference is null && string.Join(",", f.HandTitles()) != wanted) difference = $"seed {seed}, turn {w.Turn}: the hand is {string.Join(",", f.HandTitles())}";
                                foreach (var (card, target) in Plays(w, s))
                                {
                                    if (difference is not null) break;
                                    var done = f.Execute(target is null ? $"play:{f.HandIndex(card)}" : $"play:{f.HandIndex(card)}>e{target.Roster}");
                                    if (!done.Ok)
                                    {
                                        difference = $"seed {seed}, turn {w.Turn}: {card} was refused";
                                        break;
                                    }
                                    PlayCard(w, card, target);
                                    if (w.Won != f.IsOver) difference = $"seed {seed}, turn {w.Turn}: after {card} the fight is {(f.IsOver ? "over" : "not over")}";
                                    else if (!w.Won) Compare($"after {card}");
                                }
                                if (difference is not null || w.Won) break;
                                EndTurn(w);
                                f.EndTurn();
                                if (f.IsOver) difference = $"seed {seed}: the fight ended in an enemy turn";
                            }

                            if (difference is null && w.Won)
                            {
                                won++;
                                // Burning Blood after the win.
                                var expectedHp = Math.Min(w.MaxHp, w.Hp + 6);
                                if (!f.IsOver || !f.Won) difference = $"seed {seed}: the fight should be won";
                                else if (f.Player.Hp != expectedHp) difference = $"seed {seed}: HP after the win {f.Player.Hp}, expected {expectedHp}";
                            }
                            else if (difference is null && f.IsOver)
                            {
                                difference = $"seed {seed}: the fight is over after turn {turns} and should not be";
                            }

                            if (difference is null) continue;
                            wrong++;
                            first ??= difference;
                        }
                        c.That(wrong == 0, $"{encounter}: {FightSeeds} fights agree on every turn ({wrong} differ; {first})");
                        if (s is 1 or 3 or 4) c.That(won > 0, $"{encounter}: some of these fights are won within {turns} turns ({won})");
                    }
                });
            }
        }
    }
}
