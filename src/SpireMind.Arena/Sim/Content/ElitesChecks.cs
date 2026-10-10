using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// Scripted checks of the three Overgrowth elites (Elites.cs). Every expected number is worked
/// out by hand from the game classes of v0.107.1 and encounters-3.json, never read back from
/// the simulator. Most fights use a deck of Defends only, drawn in a fixed order and long
/// enough never to reshuffle, so that nothing but the monster under test moves the numbers.
/// </summary>
public sealed class ElitesChecks : IArenaChecks
{
    public void Run(Check c)
    {
        Registered(c);
        LineUps(c);
        BygoneEffigyMoves(c);
        Slow(c);
        ByrdonisMoves(c);
        Territorial(c);
        PhrogParasiteMoves(c);
        Infested(c);
        Wrigglers(c);
        TenEnemyTurns(c);
        Copies(c);
    }

    // ---------- cards and powers that exist only for these checks ----------

    /// <summary>10 damage to one enemy, free.</summary>
    private static readonly CardDef Jab = new("EliteCheckJab", "Jab", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 10 },
        Play = c => c.Attack(c.V("Damage")).Execute(),
    };

    /// <summary>10 damage three times to one enemy, free.</summary>
    private static readonly CardDef Flurry = new("EliteCheckFlurry", "Flurry", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 10 },
        Play = c => c.Attack(c.V("Damage")).Hits(3).Execute(),
    };

    /// <summary>8 damage three times to all enemies, free: built like the game's Whirlwind, which reads the living enemies again before every hit.</summary>
    private static readonly CardDef Sweep = new("EliteCheckSweep", "Sweep", CardType.Attack, CardRarity.Common, 0, TargetType.AllEnemies)
    {
        Vars = { ["Damage"] = 8 },
        Play = c => c.Attack(c.V("Damage")).Hits(3).Execute(),
    };

    /// <summary>A Power card that does nothing, free.</summary>
    private static readonly CardDef Stance = new("EliteCheckStance", "Stance", CardType.Power, CardRarity.Common, 0, TargetType.Self);

    /// <summary>A Skill that does nothing but plays itself the moment it is drawn, the way the game's Hellraiser plays drawn Strikes.</summary>
    private static readonly CardDef Reflex = new("EliteCheckReflex", "Reflex", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Behaviour = () => new PlayWhenDrawn(),
    };

    private sealed class PlayWhenDrawn : CardBehaviour
    {
        public override void AfterCardDrawnEarly(Fight f, CardInstance card, bool fromHandDraw)
        {
            if (ReferenceEquals(card, Card)) f.AutoPlay(Card);
        }
    }

    /// <summary>Hits back whoever lands a powered attack on its owner, as the game's Flame Barrier does.</summary>
    private sealed class HitBack : Power
    {
        public override string Name => "Hit Back";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner) && dealer is not null && props.IsPoweredAttack())
                f.Damage(dealer, Amount, Props.NonCardUnpowered, Owner);
        }
    }

    /// <summary>Prevents its owner's death once and heals it by the amount.</summary>
    private sealed class SecondWind : Power
    {
        public override string Name => "Second Wind";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";

        public override bool ShouldDie(Fight f, Creature creature) => !ReferenceEquals(creature, Owner);

        public override void AfterPreventingDeath(Fight f, Creature creature)
        {
            f.Heal(Owner, Amount);
            f.RemovePower(this);
        }
    }

    /// <summary>Sits on the player and notes what the first enemy's Slow shows around the start of the enemy turn.</summary>
    private sealed class SlowWatch : Power
    {
        public int BeforeEnemyTurn = -1;
        public int AfterEnemyTurnStart = -1;

        public override string Name => "Slow Watch";
        public override PowerType Type => PowerType.Buff;
        public override bool IsVisible => false;
        public override string Describe() => "";

        private static int Shown(Fight f) => f.Enemies[0].GetPower<SlowPower>()!.DisplayAmount;

        public override void BeforeSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            if (side == Side.Enemy) BeforeEnemyTurn = Shown(f);
        }

        public override void AfterSideTurnStartLate(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            if (side == Side.Enemy) AfterEnemyTurnStart = Shown(f);
        }
    }

    /// <summary>Notes the first enemy's Strength in the two passes after the enemy turn. Where it sits decides when it is heard:
    /// on the player before every enemy power, on the enemy after the powers that enemy already had.</summary>
    private sealed class StrengthWatch : Power
    {
        public int InPass = -99;
        public int InLatePass = -99;

        public override string Name => "Strength Watch";
        public override PowerType Type => PowerType.Buff;
        public override bool IsVisible => false;
        public override string Describe() => "";

        public override void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            if (side == Side.Enemy) InPass = f.Enemies[0].PowerAmount<StrengthPower>();
        }

        public override void AfterSideTurnEndLate(Fight f, Side side, IReadOnlyList<Creature> participants)
        {
            if (side == Side.Enemy) InLatePass = f.Enemies[0].PowerAmount<StrengthPower>();
        }
    }

    /// <summary>Sits on the player and notes every card a monster adds: where it went, who was named as its creator, and the acting monster's Strength at that moment.</summary>
    private sealed class CardWatch : Power
    {
        public List<string> Seen = new();

        public override string Name => "Card Watch";
        public override PowerType Type => PowerType.Buff;
        public override bool IsVisible => false;
        public override string Describe() => "";
        public override void OnCloned(CloneMap map) => Seen = new List<string>(Seen);

        public override void AfterCardGeneratedForCombat(Fight f, CardInstance card, Creature? creator)
        {
            var actor = f.Enemies.FirstOrDefault(e => e.Monster!.IsPerformingMove);
            Seen.Add($"{card.Def.ClassName} to {card.Pile}, creator {creator?.Name ?? "none"}, {actor?.Slot ?? actor?.Name ?? "nobody"} at {actor?.PowerAmount<StrengthPower>() ?? -1} Strength");
        }
    }

    // ---------- helpers ----------

    private const string Phrog = "PhrogParasiteElite";
    private const string Effigy = "BygoneEffigyElite";
    private const string Bird = "ByrdonisElite";
    private const string WriggleIntent = "buff, statuscard: 1";

    private static string[] Defends(int count) => Enumerable.Repeat("DefendIronclad", count).ToArray();

    /// <summary>A fight against one of the elites with a deck of Defends that lasts 12 turns without a reshuffle.</summary>
    private static Fight VsElite(string encounter, int hp = 300, int seed = 5) =>
        new FightBuilder().DrawOrder(Defends(60)).Hp(hp, hp).Encounter(encounter).Seed(seed).Start();

    /// <summary>The same against the starter deck in its fixed order (turn 1: Strike, Strike, Defend, Defend, Bash).</summary>
    private static Fight StarterVs(string encounter, int seed = 5) =>
        new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Encounter(encounter).Seed(seed).Start();

    private static string Intents(Fight f, Creature enemy) => string.Join(", ", f.IntentTexts(enemy));

    private static int Strength(Creature creature) => creature.PowerAmount<StrengthPower>();

    private static int Infections(IEnumerable<CardInstance> cards) => cards.Count(card => card.Def.ClassName == "Infection");

    private static void PlayDefends(Fight f, int count)
    {
        if (count > f.Energy) f.SetEnergy(count);
        for (var i = 0; i < count; i++) f.Play("Defend");
    }

    /// <summary>Kills an enemy outright as a player action, so the win check that follows every action runs.</summary>
    private static void Slay(Fight f, int enemy) =>
        f.RunAsAction(x => x.Damage(x.Enemy(enemy), 999, Props.NonCardUnpowered, x.Player));

    /// <summary>The moves the enemies performed in the enemy turn of <paramref name="turn"/>, in the order they acted: "slot or name:MOVE_ID".</summary>
    private static List<string> MovesPerformed(Fight f, int turn) => f.History.Entries
        .Where(e => e.Kind == HistoryKind.MonsterPerformedMove && e.Turn == turn)
        .Select(e =>
        {
            var who = f.Roster.First(r => r.Id == e.ActorId);
            return $"{who.Slot ?? who.Name}:{e.Name}";
        })
        .ToList();

    private static JsonObject EnemyJson(Fight f, int position) => (JsonObject)((JsonArray)f.Snapshot().State["enemies"]!)[position]!;

    private static JsonObject? PowerJson(JsonObject creature, string name) =>
        (creature["powers"] as JsonArray)?.Select(p => (JsonObject)p!).FirstOrDefault(p => (string?)p["name"] == name);

    private static bool Throws(Action body)
    {
        try
        {
            body();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    // ---------- what is registered ----------

    private static void Registered(Check c)
    {
        c.Group("Elites: everything is registered under the game's class names", () =>
        {
            var r = Content.Registry;
            foreach (var monster in new[] { "BygoneEffigy", "Byrdonis", "PhrogParasite", "Wriggler" })
                c.Equal("Elites", r.SourceOf(ContentKind.Monster, monster), $"monster {monster}");
            foreach (var power in new[] { "SlowPower", "TerritorialPower", "InfestedPower" })
                c.Equal("Elites", r.SourceOf(ContentKind.Power, power), $"power {power}");
            foreach (var encounter in new[] { Effigy, Bird, Phrog })
            {
                c.Equal("Elites", r.SourceOf(ContentKind.Encounter, encounter), $"encounter {encounter}");
                c.Equal(RoomType.Elite, r.NewEncounter(encounter).RoomType, $"{encounter} is an elite room");
                c.That(!r.NewEncounter(encounter).IsWeak, $"{encounter} is not a weak encounter");
            }
            c.Sequence(Array.Empty<string>(), r.NewEncounter(Effigy).Slots, "the Effigy's encounter names no slots");
            c.Sequence(Array.Empty<string>(), r.NewEncounter(Bird).Slots, "Byrdonis' encounter names no slots");
            c.Sequence(["phrog", "wriggler1", "wriggler2", "wriggler3", "wriggler4"], r.NewEncounter(Phrog).Slots, "the Phrog's slots in acting order");

            c.Equal("Bygone Effigy", new BygoneEffigy().Name, "name");
            c.Equal("Byrdonis", new Byrdonis().Name, "name");
            c.Equal("Phrog Parasite", new PhrogParasite().Name, "name");
            c.Equal("Wriggler", new Wriggler().Name, "name");
            c.Equal("Slow", new SlowPower().Name, "power title");
            c.Equal("Territorial", new TerritorialPower().Name, "power title");
            c.Equal("Infested", new InfestedPower().Name, "power title");
        });
    }

    // ---------- the three line-ups, over many seeds ----------

    private static void LineUps(Check c)
    {
        const int Seeds = 300;

        c.Group("BygoneEffigyElite: one Bygone Effigy at 127 HP with Slow, in every one of 300 seeds", () =>
        {
            var wrong = new List<string>();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter(Effigy).Seed(seed).Start();
                var e = f.Enemies[0];
                var slow = e.GetPower<SlowPower>();
                var ok = f.Enemies.Count == 1 && f.Roster.Count == 1 && e.Monster is BygoneEffigy && e.Name == "Bygone Effigy"
                         && e.Slot is null && e.Hp == 127 && e.MaxHp == 127 && e.Block == 0
                         && e.Powers.Count == 1 && slow is { Amount: 1, DisplayAmount: 0, Type: PowerType.Debuff }
                         && Intents(f, e) == "sleep" && f.IncomingDamage() == 0 && f.RoomType == RoomType.Elite
                         // No draw for the line-up, one for the HP (over its single value), none for a move.
                         && f.Rng.Encounter.Counter == 0 && f.Rng.Niche.Counter == 1 && f.Rng.MonsterAi.Counter == 0;
                if (!ok) wrong.Add($"seed {seed}");
            }
            c.Sequence(Array.Empty<string>(), wrong, "seeds with a wrong line-up");
        });

        c.Group("ByrdonisElite: one Byrdonis, HP 81 to 84, Territorial 1, in every one of 300 seeds", () =>
        {
            var wrong = new List<string>();
            var hpSeen = new SortedSet<int>();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter(Bird).Seed(seed).Start();
                var e = f.Enemies[0];
                hpSeen.Add(e.MaxHp);
                var ok = f.Enemies.Count == 1 && f.Roster.Count == 1 && e.Monster is Byrdonis && e.Name == "Byrdonis"
                         && e.Slot is null && e.Hp == e.MaxHp && e.MaxHp is >= 81 and <= 84 && e.Block == 0
                         && e.Powers.Count == 1 && e.GetPower<TerritorialPower>() is { Amount: 1, Type: PowerType.Buff }
                         && Intents(f, e) == "attack 17" && f.IncomingDamage() == 17 && f.RoomType == RoomType.Elite
                         && f.Rng.Encounter.Counter == 0 && f.Rng.Niche.Counter == 1 && f.Rng.MonsterAi.Counter == 0;
                if (!ok) wrong.Add($"seed {seed}");
            }
            c.Sequence(Array.Empty<string>(), wrong, "seeds with a wrong line-up");
            c.Sequence([81, 82, 83, 84], hpSeen, "every HP of the range turns up, and nothing outside it");
        });

        c.Group("PhrogParasiteElite: one Phrog Parasite in the phrog slot, HP 61 to 64, Infested 4, no Wriggler yet, in every one of 300 seeds", () =>
        {
            var wrong = new List<string>();
            var hpSeen = new SortedSet<int>();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter(Phrog).Seed(seed).Start();
                var e = f.Enemies[0];
                hpSeen.Add(e.MaxHp);
                var ok = f.Enemies.Count == 1 && f.Roster.Count == 1 && e.Monster is PhrogParasite && e.Name == "Phrog Parasite"
                         && e.Slot == "phrog" && e.Hp == e.MaxHp && e.MaxHp is >= 61 and <= 64 && e.Block == 0
                         && e.Powers.Count == 1 && e.GetPower<InfestedPower>() is { Amount: 4, Type: PowerType.Buff, StackType: PowerStackType.Single }
                         && Intents(f, e) == "statuscard: 3" && f.IncomingDamage() == 0 && f.RoomType == RoomType.Elite
                         && f.Rng.Encounter.Counter == 0 && f.Rng.Niche.Counter == 1 && f.Rng.MonsterAi.Counter == 0;
                if (!ok) wrong.Add($"seed {seed}");
            }
            c.Sequence(Array.Empty<string>(), wrong, "seeds with a wrong line-up");
            c.Sequence([61, 62, 63, 64], hpSeen, "every HP of the range turns up, and nothing outside it");
        });

        c.Group("PhrogParasiteElite: after the Phrog's death four Wrigglers in slot order, four different HP values of 17 to 21, in every one of 300 seeds", () =>
        {
            var wrong = new List<string>();
            var hpSeen = new SortedSet<int>();
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var f = new FightBuilder().Encounter(Phrog).Seed(seed).Start();
                Slay(f, 0);
                var hps = f.Enemies.Select(e => e.MaxHp).ToList();
                foreach (var hp in hps) hpSeen.Add(hp);
                var ok = !f.IsOver && f.Enemies.Count == 4 && f.Roster.Count == 5
                         && f.Enemies.All(e => e.Monster is Wriggler { StartStunned: true } && e.Name == "Wriggler" && e.Hp == e.MaxHp && e.MaxHp is >= 17 and <= 21)
                         && f.Enemies.Select(e => e.Slot).SequenceEqual(["wriggler1", "wriggler2", "wriggler3", "wriggler4"])
                         && f.Enemies.Select(e => e.RosterIndex).SequenceEqual([1, 2, 3, 4])
                         && hps.Distinct().Count() == 4
                         // One HP draw for the Phrog and one for each Wriggler; still nothing for the line-up or a move.
                         && f.Rng.Niche.Counter == 5 && f.Rng.Encounter.Counter == 0 && f.Rng.MonsterAi.Counter == 0;
                if (!ok) wrong.Add($"seed {seed}: {string.Join("/", hps)}");
            }
            c.Sequence(Array.Empty<string>(), wrong, "seeds with wrong Wrigglers");
            c.Sequence([17, 18, 19, 20, 21], hpSeen, "every HP of the range turns up, and nothing outside it");
        });
    }

    // ---------- Bygone Effigy ----------

    private static void BygoneEffigyMoves(Check c)
    {
        c.Group("Bygone Effigy: sleeps, gains 10 Strength, then hits for 13 + 10 every turn (8 enemy turns)", () =>
        {
            var f = VsElite(Effigy, hp: 200);
            var effigy = f.Enemy(0);

            // Per player turn: the intent shown, the incoming damage shown, Defends played (5 Block each),
            // then after the enemy turn the player's HP and the Effigy's Strength.
            (string Intent, int Incoming, int Defends, int HpAfter, int StrengthAfter)[] turns =
            [
                ("sleep", 0, 0, 200, 0),
                ("buff", 0, 0, 200, 10),
                ("attack 23", 23, 0, 177, 10),
                ("attack 23", 23, 1, 159, 10), // 5 Block: 18 through
                ("attack 23", 23, 3, 151, 10), // 15 Block: 8 through
                ("attack 23", 23, 0, 128, 10),
                ("attack 23", 23, 5, 128, 10), // 25 Block: nothing through
                ("attack 23", 23, 0, 105, 10),
            ];
            for (var n = 0; n < turns.Length; n++)
            {
                var (intent, incoming, defends, hpAfter, strengthAfter) = turns[n];
                var turn = n + 1;
                c.Equal(turn, f.Turn, "turn number");
                c.Equal(intent, Intents(f, effigy), $"turn {turn}: intent");
                c.Equal(incoming, f.IncomingDamage(), $"turn {turn}: incoming damage");
                c.Equal(0, effigy.GetPower<SlowPower>()!.DisplayAmount, $"turn {turn}: Slow starts the turn at 0");
                PlayDefends(f, defends);
                f.EndTurn();
                c.Equal(hpAfter, f.Player.Hp, $"enemy turn {turn}: player HP");
                c.Equal(strengthAfter, Strength(effigy), $"enemy turn {turn}: the Effigy's Strength");
                c.Equal(0, effigy.Block, $"enemy turn {turn}: it never gains Block");
                c.Equal(127, effigy.Hp, $"enemy turn {turn}: its HP is untouched");
            }

            c.Equal("attack 23", Intents(f, effigy), "turn 9: still the slash");
            c.Sequence(
                ["SLEEP_MOVE", "WAKE_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE"],
                effigy.Monster!.MoveLog, "the moves chosen");
            c.Sequence(["Slow", "Strength"], effigy.Powers.Select(p => p.Name), "its powers: Slow from the start, Strength from waking");
            c.Equal(0, f.Player.Powers.Count, "it puts nothing on the player");
            c.Equal(60, f.AllCards.Count(), "it adds no cards");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no random number for a move");
        });

        c.Group("Bygone Effigy: being hit does not wake it early", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            f.SetEnergy(4);
            f.Play("Strike", 0); // 6
            f.Play("Strike", 0); // 6 x 1.1 = 6.6, so 6
            f.Play("Bash", 0);   // 8 x 1.2 = 9.6, so 9
            c.Equal(106, effigy.Hp, "127 - 6 - 6 - 9");
            c.Equal("sleep", Intents(f, effigy), "still asleep after three hits");
            f.EndTurn();
            c.Equal(80, f.Player.Hp, "the sleep turn does nothing");
            c.Equal(0, Strength(effigy), "no Strength yet");
            c.Equal("buff", Intents(f, effigy), "turn 2: it wakes on schedule");
            f.EndTurn();
            c.Equal(10, Strength(effigy), "10 Strength from waking");
            c.Equal("attack 23", Intents(f, effigy), "turn 3: the first slash");
        });

        c.Group("Bygone Effigy: the slash intent follows Weak on it and Vulnerable on the player", () =>
        {
            var f = VsElite(Effigy, hp: 200);
            var effigy = f.Enemy(0);
            f.EndTurn();
            f.EndTurn();
            c.Equal("attack 23", Intents(f, effigy), "13 + 10");
            f.ApplyPower<WeakPower>(effigy, 2, f.Player);
            c.Equal("attack 17", Intents(f, effigy), "23 x 0.75 = 17.25");
            f.ApplyPower<VulnerablePower>(f.Player, 2, effigy);
            c.Equal("attack 25", Intents(f, effigy), "23 x 0.75 x 1.5 = 25.875");
            c.Equal(25, f.IncomingDamage(), "incoming damage is the same number");
            f.EndTurn();
            c.Equal(175, f.Player.Hp, "the hit is what the intent said");
        });

        c.Group("Bygone Effigy: the second sleep move is in the machine and leads to the slash, though nothing leads to it", () =>
        {
            var f = VsElite(Effigy, hp: 200);
            var effigy = f.Enemy(0);
            effigy.Monster!.ForceNextMove("SLEEP_MOVE_2");
            c.Equal("sleep", Intents(f, effigy), "a sleep intent");
            f.EndTurn();
            c.Equal(200, f.Player.Hp, "it does nothing");
            c.Equal(0, Strength(effigy), "and gives no Strength");
            c.Equal("attack 13", Intents(f, effigy), "then the slash, here without the Strength of waking");
            f.EndTurn();
            c.Equal(187, f.Player.Hp, "13 damage");
        });
    }

    // ---------- Slow ----------

    private static void Slow(Check c)
    {
        c.Group("Slow: the first card is not boosted, the 2nd deals x1.1, the 3rd x1.2", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            var slow = effigy.GetPower<SlowPower>()!;
            f.SetEnergy(4);
            c.Equal(0, slow.DisplayAmount, "shown as 0 before any card");
            f.Play("Strike", 0);
            c.Equal(121, effigy.Hp, "1st card: 6");
            c.Equal(10, slow.DisplayAmount, "shown as 10 after one card");
            f.Play("Strike", 0);
            c.Equal(115, effigy.Hp, "2nd card: 6 x 1.1 = 6.6, so 6");
            c.Equal(20, slow.DisplayAmount, "shown as 20");
            f.Play("Bash", 0);
            c.Equal(106, effigy.Hp, "3rd card: 8 x 1.2 = 9.6, so 9");
            c.Equal(30, slow.DisplayAmount, "shown as 30");
            c.Equal(1, slow.Amount, "the applied amount stays 1 and is not what counts");
            c.Equal(2, effigy.PowerAmount<VulnerablePower>(), "Bash's Vulnerable landed");
        });

        c.Group("Slow: Skills count too; with Vulnerable the factors multiply; the count is back at 0 next turn", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            var slow = effigy.GetPower<SlowPower>()!;
            f.SetEnergy(6);
            f.Play("Defend");
            f.Play("Defend");
            c.Equal(20, slow.DisplayAmount, "two Skills: shown as 20");
            f.Play("Strike", 0);
            c.Equal(120, effigy.Hp, "3rd card: 6 x 1.2 = 7.2, so 7");
            f.Play("Strike", 0);
            c.Equal(113, effigy.Hp, "4th card: 6 x 1.3 = 7.8, so 7");
            f.Play("Bash", 0);
            c.Equal(102, effigy.Hp, "5th card: 8 x 1.4 = 11.2, so 11");
            c.Equal(50, slow.DisplayAmount, "shown as 50 after five cards");

            f.EndTurn();
            c.Equal(0, slow.DisplayAmount, "turn 2: back at 0");
            c.Equal(1, effigy.PowerAmount<VulnerablePower>(), "Vulnerable 2 ticked to 1 at the end of the enemy turn");
            f.Play("Strike", 0);
            c.Equal(93, effigy.Hp, "1st card of the turn: 6 x 1.5 = 9, no Slow factor");
            f.Play("Defend");
            f.Play("Strike", 0);
            c.Equal(83, effigy.Hp, "3rd card: 6 x 1.5 x 1.2 = 10.8, so 10");
            c.That(effigy.HasPower<SlowPower>(), "Slow is never removed");
        });

        c.Group("Slow: the count is reset when the enemy turn starts, before any enemy acts, not when the player's turn ends", () =>
        {
            var f = StarterVs(Effigy);
            f.ApplyPower<SlowWatch>(f.Player, 1, f.Player);
            f.Play("Defend");
            f.Play("Defend");
            f.Play("Strike", 0);
            f.EndTurn();
            var watch = f.Player.GetPower<SlowWatch>()!;
            c.Equal(30, watch.BeforeEnemyTurn, "still 30 in the first hook of the enemy turn, after the player's turn has ended");
            c.Equal(0, watch.AfterEnemyTurnStart, "0 once the after-turn-start hooks of the enemy side have run");
        });

        c.Group("Slow: the start of the player's turn does not reset it, so a card played by the turn's own draw counts", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            f.Put(Reflex, PileType.Draw, position: PilePosition.Top);
            f.EndTurn();
            c.Sequence(["Strike", "Strike", "Strike", "Defend"], f.HandTitles(), "turn 2: Reflex was drawn first and played itself");
            c.Equal(10, effigy.GetPower<SlowPower>()!.DisplayAmount, "it counts for this turn: shown as 10 when the player may act");
            f.Put(Jab, PileType.Hand);
            f.Play("Jab", 0);
            c.Equal(116, effigy.Hp, "the first card played by hand is the turn's second: 10 x 1.1 = 11");
        });

        c.Group("Slow: a Power card counts, and a card that runs twice counts once per run", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            var slow = effigy.GetPower<SlowPower>()!;
            f.Put(Stance, PileType.Hand);
            f.Put(Jab, PileType.Hand).ReplayCount = 1;
            f.Play("Stance");
            c.Equal(10, slow.DisplayAmount, "the Power card raised the count");
            f.Play("Jab", 0);
            c.Equal(104, effigy.Hp, "first run 10 x 1.1 = 11, second run 10 x 1.2 = 12");
            c.Equal(30, slow.DisplayAmount, "the two runs counted as two");
        });

        c.Group("Slow: every hit of one card gets the same factor", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            f.Put(Flurry, PileType.Hand);
            f.Play("Defend");
            f.Play("Flurry", 0);
            c.Equal(94, effigy.Hp, "three hits of 10 x 1.1 = 11");
            c.Equal(20, effigy.GetPower<SlowPower>()!.DisplayAmount, "one card, one count, whatever its hits");
        });

        c.Group("Slow: only powered attacks are multiplied, and a preview without a target is not", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            f.Play("Defend");
            f.Play("Defend");
            f.Put(Stance, PileType.Hand);
            f.Play("Stance");
            c.Equal(13m, f.ModifyDamage(effigy, f.Player, 10m, Props.Card, null), "a powered 10 after three cards is 13");
            c.Equal(10m, f.ModifyDamage(null, f.Player, 10m, Props.Card, null), "with no target (a card's text) it stays 10");
            c.Equal(10m, f.ModifyDamage(f.Player, effigy, 10m, Props.MonsterMove, null), "its own attacks on the player are not multiplied");
            f.Damage(effigy, 10, Props.NonCardUnpowered, f.Player);
            c.Equal(117, effigy.Hp, "damage from a power or potion: exactly 10");
            f.Damage(effigy, 10, Props.CardUnpowered, f.Player);
            c.Equal(107, effigy.Hp, "unpowered damage from a card: exactly 10");
            f.Damage(effigy, 10, Props.CardHpLoss, f.Player);
            c.Equal(97, effigy.Hp, "HP loss: exactly 10");
        });

        c.Group("Slow: it only covers its owner", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new BygoneEffigy()).Enemy(new CheckDummy()).Start();
            var effigy = f.Enemy(0);
            var dummy = f.Enemy(1);
            f.SetEnergy(4);
            f.Play("Defend");
            f.Play("Defend");
            f.Play("Strike", 1);
            c.Equal(94, dummy.Hp, "3rd card on the other enemy: a plain 6");
            f.Play("Strike", 0);
            c.Equal(120, effigy.Hp, "4th card on the Effigy: 6 x 1.3 = 7.8, so 7");
        });

        c.Group("Slow: Strength is added before it multiplies, Weak multiplies along with it, and card texts do not show it", () =>
        {
            var f = StarterVs(Effigy);
            var effigy = f.Enemy(0);
            f.SetEnergy(4);
            f.ApplyPower<StrengthPower>(f.Player, 2, f.Player);
            f.Play("Defend");
            f.Play("Defend");
            c.Equal("Deal 8 damage.", Fight.CardText(f, f.Hand[f.HandIndex("Strike")], PileType.Hand), "the Strike's text has the Strength but not Slow");
            f.Play("Strike", 0);
            c.Equal(118, effigy.Hp, "(6 + 2) x 1.2 = 9.6, so 9");
            f.ApplyPower<WeakPower>(f.Player, 1, effigy);
            f.Play("Strike", 0);
            c.Equal(111, effigy.Hp, "(6 + 2) x 0.75 x 1.3 = 7.8, so 7");
        });

        c.Group("Slow: in the snapshot it is a Debuff with no amount at 0 and the percentage after that", () =>
        {
            var f = StarterVs(Effigy);
            var slow = PowerJson(EnemyJson(f, 0), "Slow");
            c.That(slow is not null, "listed by its title");
            c.Equal("Debuff", (string?)slow!["kind"], "kind");
            c.That(slow["amount"] is null, "an amount of 0 is left out");
            c.That(((string?)slow["text"] ?? "").Contains("0% more", StringComparison.Ordinal), "its text carries the live number");
            f.Play("Defend");
            f.Play("Defend");
            slow = PowerJson(EnemyJson(f, 0), "Slow");
            c.Equal(20, (int?)slow!["amount"], "20 after two cards");
            c.That(((string?)slow["text"] ?? "").Contains("20% more", StringComparison.Ordinal), "and the text follows");
        });
    }

    // ---------- Byrdonis ----------

    private static void ByrdonisMoves(Check c)
    {
        c.Group("Byrdonis: Swoop and Peck in turn, 1 more Strength after every enemy turn (8 enemy turns)", () =>
        {
            var f = VsElite(Bird);
            var bird = f.Enemy(0);
            var maxHp = bird.MaxHp;

            // Strength on enemy turn n is n - 1: Swoop 17 + S, Peck (3 + S) x3.
            (string Intent, int Incoming, int Defends, int HpAfter, int StrengthAfter)[] turns =
            [
                ("attack 17", 17, 2, 293, 1),   // 10 Block: 7 through
                ("attack 4x3", 12, 1, 286, 2),  // 5 Block: 4 blocked; 1 blocked and 3 through; 4 through
                ("attack 19", 19, 0, 267, 3),
                ("attack 6x3", 18, 3, 264, 4),  // 15 Block: 6 and 6 blocked; 3 blocked and 3 through
                ("attack 21", 21, 0, 243, 5),
                ("attack 8x3", 24, 0, 219, 6),
                ("attack 23", 23, 3, 211, 7),   // 15 Block: 8 through
                ("attack 10x3", 30, 0, 181, 8),
            ];
            for (var n = 0; n < turns.Length; n++)
            {
                var (intent, incoming, defends, hpAfter, strengthAfter) = turns[n];
                var turn = n + 1;
                c.Equal(turn, f.Turn, "turn number");
                c.Equal(intent, Intents(f, bird), $"turn {turn}: intent");
                c.Equal(incoming, f.IncomingDamage(), $"turn {turn}: incoming damage");
                PlayDefends(f, defends);
                f.EndTurn();
                c.Equal(hpAfter, f.Player.Hp, $"enemy turn {turn}: player HP");
                c.Equal(strengthAfter, Strength(bird), $"enemy turn {turn}: Strength, gained after the move");
                c.Equal(0, bird.Block, $"enemy turn {turn}: it never gains Block");
                c.Equal(maxHp, bird.Hp, $"enemy turn {turn}: its HP is untouched");
            }

            c.Equal("attack 25", Intents(f, bird), "turn 9: Swoop with 8 Strength");
            c.Sequence(
                ["SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE"],
                bird.Monster!.MoveLog, "strict alternation, Swoop first");
            c.Equal(1, bird.PowerAmount<TerritorialPower>(), "Territorial stays at 1");
            c.Equal(0, f.Player.Powers.Count, "it puts nothing on the player");
            c.Equal(60, f.AllCards.Count(), "it adds no cards");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no random number for a move");
        });

        c.Group("Byrdonis: Peck is three separate hits, each worked out and cut off on its own", () =>
        {
            var f = VsElite(Bird);
            var bird = f.Enemy(0);
            bird.Monster!.ForceNextMove("PECK_MOVE");
            f.ApplyPower<VulnerablePower>(f.Player, 2, bird);
            c.Equal("attack 4x3", Intents(f, bird), "3 x 1.5 = 4.5 per hit, shown as 4");
            c.Equal(12, f.IncomingDamage(), "4 x 3");
            f.EndTurn();
            c.Equal(288, f.Player.Hp, "three hits of 4; one hit of 9 x 1.5 would have been 13");
            c.Equal("SWOOP_MOVE", bird.Monster.NextMove!.Id, "Swoop follows Peck");
        });
    }

    // ---------- Territorial ----------

    private static void Territorial(Check c)
    {
        c.Group("Territorial: no delay turn, and the gain comes after the move", () =>
        {
            var f = VsElite(Bird);
            var bird = f.Enemy(0);
            c.Equal(0, Strength(bird), "no Strength before the first enemy turn");
            f.EndTurn();
            c.Equal(283, f.Player.Hp, "the first Swoop is a plain 17");
            c.Equal(1, Strength(bird), "1 Strength right after the first enemy turn");
        });

        c.Group("Territorial: taking the Strength away does not stop it", () =>
        {
            var f = VsElite(Bird);
            var bird = f.Enemy(0);
            f.EndTurn(); // Swoop 17; Strength 1
            f.ApplyPower<StrengthPower>(bird, -1, f.Player);
            c.That(!bird.HasPower<StrengthPower>(), "Strength at exactly 0 is removed");
            c.Equal("attack 3x3", Intents(f, bird), "Peck without Strength");
            f.EndTurn(); // 9
            c.Equal(274, f.Player.Hp, "283 - 9");
            c.Equal(1, Strength(bird), "a new Strength of 1 at the end of the enemy turn");

            f.ApplyPower<StrengthPower>(bird, -5, f.Player);
            c.Equal(-4, Strength(bird), "pushed to -4");
            c.Equal("attack 13", Intents(f, bird), "Swoop 17 - 4");
            f.EndTurn();
            c.Equal(261, f.Player.Hp, "274 - 13");
            c.Equal(-3, Strength(bird), "it keeps adding 1: -3");
            c.Equal("attack 0x3", Intents(f, bird), "Peck 3 - 3: never below 0");
            f.EndTurn();
            c.Equal(261, f.Player.Hp, "three hits of 0");
            c.Equal(-2, Strength(bird), "-2");
            c.Equal("attack 15", Intents(f, bird), "Swoop 17 - 2");
        });

        c.Group("Territorial: it fires in the pass in which Weak wears off", () =>
        {
            var f = VsElite(Bird);
            var bird = f.Enemy(0);
            f.ApplyPower<WeakPower>(bird, 1, f.Player);
            c.Equal("attack 12", Intents(f, bird), "17 x 0.75 = 12.75");
            f.EndTurn();
            c.Equal(288, f.Player.Hp, "the weakened Swoop");
            c.That(!bird.HasPower<WeakPower>(), "Weak 1 is gone after the enemy turn");
            c.Equal(1, Strength(bird), "and the Strength is there");
            c.Equal("attack 4x3", Intents(f, bird), "Peck at full strength again, with 1 Strength");
        });

        c.Group("Territorial: it acts in the ordinary pass after the enemy turn, in its place among the powers", () =>
        {
            var f = VsElite(Bird);
            var bird = f.Enemy(0);
            f.ApplyPower<StrengthWatch>(f.Player, 1, f.Player);
            f.ApplyPower<StrengthWatch>(bird, 1, bird);
            f.EndTurn();
            var early = f.Player.GetPower<StrengthWatch>()!;
            var late = bird.GetPower<StrengthWatch>()!;
            c.Equal(0, early.InPass, "a power heard before it still sees no Strength in that pass");
            c.Equal(1, late.InPass, "a power heard after it in the same pass sees the new Strength");
            c.Equal(1, early.InLatePass, "and so does everyone in the late pass");
        });

        c.Group("Territorial: the Strength gained is the power's amount", () =>
        {
            var f = VsElite(Bird);
            var bird = f.Enemy(0);
            f.ApplyPower<TerritorialPower>(bird, 1, bird);
            c.Equal(2, bird.PowerAmount<TerritorialPower>(), "stacked to 2");
            f.EndTurn();
            c.Equal(2, Strength(bird), "2 Strength after one enemy turn");
            c.Equal("attack 5x3", Intents(f, bird), "Peck 3 + 2");
            f.EndTurn();
            c.Equal(4, Strength(bird), "4 after two");
        });

        c.Group("Territorial: in the snapshot it is a Buff with its amount", () =>
        {
            var f = VsElite(Bird);
            var power = PowerJson(EnemyJson(f, 0), "Territorial");
            c.That(power is not null, "listed by its title");
            c.Equal("Buff", (string?)power!["kind"], "kind");
            c.Equal(1, (int?)power["amount"], "amount");
            c.That(((string?)power["text"] ?? "").Contains("1 Strength", StringComparison.Ordinal), "its text carries the number");
        });
    }

    // ---------- Phrog Parasite ----------

    private static void PhrogParasiteMoves(Check c)
    {
        c.Group("Phrog Parasite: Infect and Lash in turn (8 enemy turns)", () =>
        {
            var f = VsElite(Phrog);
            var phrog = f.Enemy(0);
            var maxHp = phrog.MaxHp;

            // Per player turn: intent, incoming, Defends played; after the enemy turn: player HP and Infections in the discard pile.
            (string Intent, int Incoming, int Defends, int HpAfter, int InfectionsAfter)[] turns =
            [
                ("statuscard: 3", 0, 0, 300, 3),
                ("attack 4x4", 16, 2, 294, 3),  // 10 Block: 4 and 4 blocked; 2 blocked and 2 through; 4 through
                ("statuscard: 3", 0, 0, 294, 6),
                ("attack 4x4", 16, 0, 278, 6),
                ("statuscard: 3", 0, 0, 278, 9),
                ("attack 4x4", 16, 3, 277, 9),  // 15 Block: three hits blocked; 3 blocked and 1 through
                ("statuscard: 3", 0, 0, 277, 12),
                ("attack 4x4", 16, 0, 261, 12),
            ];
            for (var n = 0; n < turns.Length; n++)
            {
                var (intent, incoming, defends, hpAfter, infectionsAfter) = turns[n];
                var turn = n + 1;
                c.Equal(turn, f.Turn, "turn number");
                c.Equal(intent, Intents(f, phrog), $"turn {turn}: intent");
                c.Equal(incoming, f.IncomingDamage(), $"turn {turn}: incoming damage");
                PlayDefends(f, defends);
                f.EndTurn();
                c.Equal(hpAfter, f.Player.Hp, $"enemy turn {turn}: player HP");
                c.Equal(infectionsAfter, Infections(f.DiscardPile), $"enemy turn {turn}: Infections in the discard pile");
                c.Equal(infectionsAfter, Infections(f.AllCards), $"enemy turn {turn}: and nowhere else");
                c.Equal(0, Strength(phrog), $"enemy turn {turn}: it gains no Strength");
                c.Equal(0, phrog.Block, $"enemy turn {turn}: it never gains Block");
                c.Equal(maxHp, phrog.Hp, $"enemy turn {turn}: its HP is untouched");
            }

            c.Equal("statuscard: 3", Intents(f, phrog), "turn 9: Infect again");
            c.Sequence(
                ["INFECT_MOVE", "LASH_MOVE", "INFECT_MOVE", "LASH_MOVE", "INFECT_MOVE", "LASH_MOVE", "INFECT_MOVE", "LASH_MOVE", "INFECT_MOVE"],
                phrog.Monster!.MoveLog, "strict alternation, Infect first");
            c.Equal(4, phrog.PowerAmount<InfestedPower>(), "Infested stays");
            c.Equal(0, f.Player.Powers.Count, "it puts nothing on the player");
            c.Equal(72, f.AllCards.Count(), "60 cards and 12 Infections");
            c.Equal(0, f.Rng.MonsterAi.Counter, "the unused random branch never draws");
        });

        c.Group("Phrog Parasite: Infect puts its three cards at the bottom of the discard pile, and names no creator", () =>
        {
            var f = VsElite(Phrog);
            f.ApplyPower<CardWatch>(f.Player, 1, f.Player);
            f.EndTurn();
            c.Sequence(
                ["Defend", "Defend", "Defend", "Defend", "Defend", "Infection", "Infection", "Infection"],
                f.DiscardPile.Select(card => card.Title), "after the hand that was discarded before the enemy turn");
            c.Sequence(
                Enumerable.Repeat("Infection to Discard, creator none, phrog at 0 Strength", 3),
                f.Player.GetPower<CardWatch>()!.Seen, "three cards, one by one, during its move");
        });

        c.Group("Phrog Parasite: its Infections come back with the reshuffle, cannot be played, and hurt from the hand", () =>
        {
            // Five cards only: turn 2 draws from the five Defends and the three Infections shuffled together.
            Fight? f = null;
            var inHand = 0;
            for (var seed = 1; seed <= 20 && inHand == 0; seed++)
            {
                f = new FightBuilder().DrawOrder(Defends(5)).Encounter(Phrog).Seed(seed).Start();
                f.EndTurn();
                inHand = Infections(f.Hand);
            }
            c.That(inHand > 0, "some seed up to 20 draws an Infection on turn 2");
            c.Equal(3, Infections(f!.AllCards), "three Infections in the deck");
            var playable = f.LegalActions().Select(a => a.Id).ToList();
            for (var i = 0; i < f.Hand.Count; i++)
            {
                if (f.Hand[i].Def.ClassName == "Infection")
                    c.That(!playable.Contains($"play:{i}"), $"the Infection at hand index {i} cannot be played");
            }
            f.EndTurn();
            c.Equal(80 - 3 * inHand - 16, f.Player.Hp, "3 for every Infection in hand, then Lash 4x4");
            c.Equal(3, Infections(f.AllCards), "they are not used up");
            c.Equal(0, Infections(f.ExhaustPile), "and not exhausted");
        });

        c.Group("Phrog Parasite: Lash is four separate hits, each worked out and cut off on its own", () =>
        {
            var f = VsElite(Phrog);
            var phrog = f.Enemy(0);
            f.EndTurn();
            f.ApplyPower<StrengthPower>(phrog, 1, phrog);
            f.ApplyPower<WeakPower>(phrog, 1, f.Player);
            c.Equal("attack 3x4", Intents(f, phrog), "(4 + 1) x 0.75 = 3.75 per hit, shown as 3");
            c.Equal(12, f.IncomingDamage(), "3 x 4");
            f.EndTurn();
            c.Equal(288, f.Player.Hp, "four hits of 3; one hit of 20 x 0.75 would have been 15");
            c.Equal(4, f.History.Count(e => e.Kind == HistoryKind.DamageReceived && e.ActorId == f.Player.Id && e.Turn == 2), "four damage instances on the player");
        });
    }

    // ---------- Infested ----------

    private static void Infested(Check c)
    {
        c.Group("Infested: killing the Phrog on the player's turn does not end the fight; four stunned Wrigglers take the slots", () =>
        {
            var f = StarterVs(Phrog);
            var phrog = f.Enemy(0);
            f.SetHp(phrog, 6);
            f.Play("Strike", 0);

            c.That(!f.IsOver, "the fight goes on");
            c.That(f.InProgress && !f.IsEnding, "and is not ending");
            c.That(phrog.IsDead && !phrog.InCombat, "the Phrog is dead and gone");
            c.Equal(0, phrog.Powers.Count, "its Infested went with it");
            c.Sequence(["wriggler1", "wriggler2", "wriggler3", "wriggler4"], f.Enemies.Select(e => e.Slot ?? "none"), "the enemy list, in slot order");
            c.Sequence([1, 2, 3, 4], f.Enemies.Select(e => e.RosterIndex), "they are e1 to e4; e0 stays a gap");
            c.Equal(5, f.Roster.Count, "five enemies in all");
            foreach (var w in f.Enemies)
            {
                c.Equal("Wriggler", w.Name, $"{w.Slot}: name");
                c.That(w.MaxHp is >= 17 and <= 21 && w.Hp == w.MaxHp, $"{w.Slot}: HP 17 to 21, full");
                c.Equal(0, w.Powers.Count, $"{w.Slot}: no powers, so no Minion");
                c.That(w.IsPrimaryEnemy, $"{w.Slot}: a primary enemy");
                c.Equal("stun", Intents(f, w), $"{w.Slot}: shows a stun intent of its own, not the engine's 'stunned'");
                c.Sequence(["SPAWNED_MOVE"], w.Monster!.MoveLog, $"{w.Slot}: on its spawned move");
            }
            c.Equal(4, f.Enemies.Select(e => e.MaxHp).Distinct().Count(), "four different HP values");
            c.Equal(0, f.IncomingDamage(), "no incoming damage");
            c.Equal(5, f.Rng.Niche.Counter, "one HP draw each");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no random move");

            // What the model sees and may do.
            var ids = f.LegalActions().Select(a => a.Id).ToList();
            var strike = f.HandIndex("Strike");
            c.That(!ids.Contains($"play:{strike}>e0"), "the dead Phrog cannot be aimed at");
            for (var j = 1; j <= 4; j++) c.That(ids.Contains($"play:{strike}>e{j}"), $"Strike can be aimed at e{j}");
            var enemies = (JsonArray)f.Snapshot().State["enemies"]!;
            c.Sequence([1, 2, 3, 4], enemies.Select(e => (int)e!["i"]!), "the snapshot lists e1 to e4");
            c.That(enemies.All(e => e!["primary"] is null && e["powers"] is null), "none is marked: all four are alike, without powers");
            c.Sequence(["stun"], ((JsonArray)enemies[0]!["intents"]!).Select(i => i!.GetValue<string>()), "intent text in the snapshot");
        });

        c.Group("Infested: the fight is won only when all four Wrigglers are dead", () =>
        {
            var f = StarterVs(Phrog);
            Slay(f, 0);
            c.That(!f.IsOver && f.Enemies.Count == 4, "four Wrigglers after the Phrog");
            for (var j = 1; j <= 3; j++)
            {
                Slay(f, j);
                c.That(!f.IsOver, $"not over with e{j} dead");
                c.Equal(4 - j, f.Enemies.Count, "a Wriggler's death adds nothing");
            }
            Slay(f, 4);
            c.That(f.IsOver && f.Won, "won with the last Wriggler");
            c.Equal("arena_over", f.Snapshot().Screen, "the end screen");
        });

        c.Group("Infested: always four Wrigglers, whatever its amount; and it is shown with the amount 4", () =>
        {
            var f = StarterVs(Phrog);
            var phrog = f.Enemy(0);
            var power = PowerJson(EnemyJson(f, 0), "Infested");
            c.That(power is not null, "listed by its title");
            c.Equal("Buff", (string?)power!["kind"], "kind");
            c.Equal(4, (int?)power["amount"], "the reader prints the amount of a single-stack power too");
            c.That(((string?)power["text"] ?? "").Contains("4 Wrigglers", StringComparison.Ordinal), "its text names the four Wrigglers");

            f.ApplyPower<InfestedPower>(phrog, 3, phrog);
            c.Equal(7, phrog.PowerAmount<InfestedPower>(), "amount raised to 7");
            Slay(f, 0);
            c.Equal(4, f.Enemies.Count, "still four Wrigglers");
        });

        c.Group("Infested: only its owner's death counts", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder)
                .Slots("phrog", "wriggler1", "wriggler2", "wriggler3", "wriggler4")
                .Enemy(new PhrogParasite(), slot: "phrog").Enemy(new CheckDummy { Hp = 5 })
                .Start();
            var dummy = f.Enemies.First(e => e.Monster is CheckDummy);
            f.Play("Strike", dummy.RosterIndex);
            c.That(dummy.IsDead, "the other enemy died");
            c.Sequence(["Phrog Parasite"], f.Enemies.Select(e => e.Name), "no Wriggler came");
            c.That(!f.IsOver, "the fight goes on against the Phrog");
        });

        c.Group("Infested: a death that was prevented brings no Wrigglers; the real one does", () =>
        {
            var f = StarterVs(Phrog);
            var phrog = f.Enemy(0);
            f.ApplyPower<SecondWind>(phrog, 10, phrog);
            f.SetHp(phrog, 6);
            f.Play("Strike", 0);
            c.Equal(10, phrog.Hp, "the death was prevented and it was healed");
            c.Sequence(["Phrog Parasite"], f.Enemies.Select(e => e.Name), "no Wriggler yet");
            c.That(phrog.HasPower<InfestedPower>(), "Infested is still on it");
            f.SetHp(phrog, 6);
            f.Play("Strike", 0);
            c.That(phrog.IsDead, "now it is dead");
            c.Equal(4, f.Enemies.Count(e => e.Monster is Wriggler), "and the four Wrigglers came");
        });

        c.Group("Infested: an attack on all enemies goes on to the new Wrigglers with its remaining hits; an attack aimed at the Phrog stops", () =>
        {
            var f = StarterVs(Phrog);
            f.SetHp(f.Enemy(0), 5);
            f.Put(Sweep, PileType.Hand);
            f.Play("Sweep");
            c.Equal(4, f.Enemies.Count, "the first hit killed the Phrog");
            foreach (var w in f.Enemies) c.Equal(w.MaxHp - 16, w.Hp, $"{w.Slot}: took the second and the third hit of 8");

            var g = StarterVs(Phrog);
            g.SetHp(g.Enemy(0), 5);
            g.Put(Flurry, PileType.Hand);
            g.Play("Flurry", 0);
            c.Equal(4, g.Enemies.Count, "the first hit killed the Phrog");
            foreach (var w in g.Enemies) c.Equal(w.MaxHp, w.Hp, $"{w.Slot}: untouched, the other hits had lost their target");
        });

        c.Group("Infested: a Phrog that dies in its own Lash loses the remaining hits, and its Wrigglers lose one more round", () =>
        {
            var f = VsElite(Phrog);
            var phrog = f.Enemy(0);
            f.EndTurn(); // Infect
            c.Equal("attack 4x4", Intents(f, phrog), "turn 2: Lash");
            f.SetHp(phrog, 5);
            f.ApplyPower<HitBack>(f.Player, 5, f.Player);
            f.EndTurn();

            c.Equal(3, f.Turn, "turn 3");
            c.Equal(296, f.Player.Hp, "only the first hit of 4 landed");
            c.Sequence(["wriggler1", "wriggler2", "wriggler3", "wriggler4"], f.Enemies.Select(e => e.Slot ?? "none"), "the Wrigglers came during the enemy turn");
            c.Sequence(["phrog:LASH_MOVE"], MovesPerformed(f, 2), "none of them acted in that enemy turn");
            foreach (var w in f.Enemies)
            {
                c.Equal("stun", Intents(f, w), $"{w.Slot}: still on its spawned move for the whole of turn 3");
                c.That(!w.Monster!.HasPerformedAMove, $"{w.Slot}: has not performed it yet");
            }
            c.Equal(0, f.IncomingDamage(), "no incoming damage on turn 3");

            f.RemovePower<HitBack>(f.Player);
            f.EndTurn();
            c.Equal(296, f.Player.Hp, "enemy turn 3: they only get over the spawned move");
            c.Equal(3, Infections(f.AllCards), "no new Infection");
            c.Sequence(
                ["wriggler1:SPAWNED_MOVE", "wriggler2:SPAWNED_MOVE", "wriggler3:SPAWNED_MOVE", "wriggler4:SPAWNED_MOVE"],
                MovesPerformed(f, 3), "enemy turn 3");
            c.Sequence(["attack 6", WriggleIntent, "attack 6", WriggleIntent], f.Enemies.Select(w => Intents(f, w)), "turn 4: the first real intents");
            f.EndTurn();
            c.Equal(284, f.Player.Hp, "enemy turn 4: two bites of 6");
            c.Equal(5, Infections(f.AllCards), "and two more Infections");
        });
    }

    // ---------- Wriggler ----------

    private static void Wrigglers(Check c)
    {
        c.Group("Wrigglers: stunned in the enemy turn of the Phrog's death, then Nasty Bite and Wriggle by slot (7 enemy turns)", () =>
        {
            var f = new FightBuilder().DrawOrder(["StrikeIronclad", .. Defends(64)]).Hp(500, 500).Encounter(Phrog).Seed(9).Start();
            f.SetHp(f.Enemy(0), 6);
            f.Play("Strike", 0);
            var w = f.Enemies.ToList();
            var maxHp = w.Select(e => e.MaxHp).ToList();
            const string Bite6 = "attack 6", Bite8 = "attack 8", Bite10 = "attack 10", Bite12 = "attack 12", Wriggle = WriggleIntent;

            // Per player turn: the four intents in slot order, incoming, Defends played; after the enemy turn:
            // player HP, Infections in the discard pile, and the four Strengths.
            (string[] Intents, int Incoming, int Defends, int HpAfter, int InfectionsAfter, int[] StrengthAfter)[] turns =
            [
                (["stun", "stun", "stun", "stun"], 0, 0, 500, 0, [0, 0, 0, 0]),
                ([Bite6, Wriggle, Bite6, Wriggle], 12, 1, 493, 2, [0, 2, 0, 2]),    // 5 Block: 5 blocked and 1 through; 6 through
                ([Wriggle, Bite8, Wriggle, Bite8], 16, 0, 477, 4, [2, 2, 2, 2]),
                ([Bite8, Wriggle, Bite8, Wriggle], 16, 3, 476, 6, [2, 4, 2, 4]),    // 15 Block: 8 blocked; 7 blocked and 1 through
                ([Wriggle, Bite10, Wriggle, Bite10], 20, 0, 456, 8, [4, 4, 4, 4]),
                ([Bite10, Wriggle, Bite10, Wriggle], 20, 0, 436, 10, [4, 6, 4, 6]),
                ([Wriggle, Bite12, Wriggle, Bite12], 24, 0, 412, 12, [6, 6, 6, 6]),
            ];
            for (var n = 0; n < turns.Length; n++)
            {
                var (intents, incoming, defends, hpAfter, infectionsAfter, strengthAfter) = turns[n];
                var turn = n + 1;
                c.Equal(turn, f.Turn, "turn number");
                c.Sequence(intents, w.Select(e => Intents(f, e)), $"turn {turn}: intents of wriggler1 to wriggler4");
                c.Equal(incoming, f.IncomingDamage(), $"turn {turn}: incoming damage");
                PlayDefends(f, defends);
                f.EndTurn();
                c.Equal(hpAfter, f.Player.Hp, $"enemy turn {turn}: player HP");
                c.Equal(infectionsAfter, Infections(f.DiscardPile), $"enemy turn {turn}: Infections in the discard pile");
                c.Equal(infectionsAfter, Infections(f.AllCards), $"enemy turn {turn}: and nowhere else");
                c.Sequence(strengthAfter, w.Select(Strength), $"enemy turn {turn}: Strength of wriggler1 to wriggler4");
                c.Sequence([0, 0, 0, 0], w.Select(e => e.Block), $"enemy turn {turn}: they never gain Block");
                c.Sequence(maxHp, w.Select(e => e.Hp), $"enemy turn {turn}: their HP is untouched");
            }

            c.Sequence([Bite12, Wriggle, Bite12, Wriggle], w.Select(e => Intents(f, e)), "turn 8: intents");
            c.Sequence(
                ["wriggler1:SPAWNED_MOVE", "wriggler2:SPAWNED_MOVE", "wriggler3:SPAWNED_MOVE", "wriggler4:SPAWNED_MOVE"],
                MovesPerformed(f, 1), "enemy turn 1: all four perform the spawned move, in slot order");
            c.Sequence(
                ["wriggler1:NASTY_BITE_MOVE", "wriggler2:WRIGGLE_MOVE", "wriggler3:NASTY_BITE_MOVE", "wriggler4:WRIGGLE_MOVE"],
                MovesPerformed(f, 2), "enemy turn 2: they act in slot order");
            c.Sequence(
                ["wriggler1:WRIGGLE_MOVE", "wriggler2:NASTY_BITE_MOVE", "wriggler3:WRIGGLE_MOVE", "wriggler4:NASTY_BITE_MOVE"],
                MovesPerformed(f, 3), "enemy turn 3: the pairs have swapped");
            c.Sequence(
                ["SPAWNED_MOVE", "NASTY_BITE_MOVE", "WRIGGLE_MOVE", "NASTY_BITE_MOVE", "WRIGGLE_MOVE", "NASTY_BITE_MOVE", "WRIGGLE_MOVE", "NASTY_BITE_MOVE"],
                w[0].Monster!.MoveLog, "wriggler1's moves (the slot branch itself is not logged)");
            c.Sequence(
                ["SPAWNED_MOVE", "WRIGGLE_MOVE", "NASTY_BITE_MOVE", "WRIGGLE_MOVE", "NASTY_BITE_MOVE", "WRIGGLE_MOVE", "NASTY_BITE_MOVE", "WRIGGLE_MOVE"],
                w[3].Monster!.MoveLog, "wriggler4's moves");
            c.Equal(0, f.Player.Powers.Count, "they put nothing on the player");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no random number for a move");
        });

        c.Group("Wriggler: Wriggle adds the Infection first and gains the Strength after it; the card names no creator", () =>
        {
            var f = VsElite(Phrog, hp: 500);
            Slay(f, 0);
            f.ApplyPower<CardWatch>(f.Player, 1, f.Player);
            f.EndTurn(); // the spawned move
            f.EndTurn(); // wriggler2 and wriggler4 wriggle
            c.Sequence(
                ["Infection to Discard, creator none, wriggler2 at 0 Strength", "Infection to Discard, creator none, wriggler4 at 0 Strength"],
                f.Player.GetPower<CardWatch>()!.Seen, "the card is added while the Wriggler still has no Strength");
            c.Sequence([0, 2, 0, 2], f.Enemies.Select(Strength), "the Strength is there afterwards, on that Wriggler only");
            c.Sequence(["Infection", "Infection"], f.DiscardPile.Skip(f.DiscardPile.Count - 2).Select(card => card.Title), "both at the bottom of the discard pile");
        });

        c.Group("Wriggler: one that does not start stunned goes straight to its slot's move", () =>
        {
            var builder = new FightBuilder().DrawOrder(Defends(60)).Hp(300, 300).Slots("wriggler1", "wriggler2", "wriggler3", "wriggler4");
            for (var i = 0; i < 4; i++) builder.Enemy(new Wriggler(), slot: PhrogParasiteElite.WrigglerSlot(i));
            var f = builder.Start();
            c.Sequence(["attack 6", WriggleIntent, "attack 6", WriggleIntent], f.Enemies.Select(w => Intents(f, w)), "turn 1: by slot");
            c.Sequence(["NASTY_BITE_MOVE"], f.Enemies[0].Monster!.MoveLog, "wriggler1: the branch is resolved at once and only the move is logged");
            c.Sequence(["WRIGGLE_MOVE"], f.Enemies[1].Monster!.MoveLog, "wriggler2");
            c.Equal(4, f.Enemies.Select(w => w.MaxHp).Distinct().Count(), "four different HP values here too");
            f.EndTurn();
            c.Equal(288, f.Player.Hp, "enemy turn 1: two bites of 6");
            c.Sequence([0, 2, 0, 2], f.Enemies.Select(Strength), "and two Wriggles");
            c.Sequence([WriggleIntent, "attack 8", WriggleIntent, "attack 8"], f.Enemies.Select(w => Intents(f, w)), "turn 2: swapped");
            c.Equal(0, f.Rng.MonsterAi.Counter, "no random number");
        });

        c.Group("Wriggler: a slot the game does not name leaves its first move undecided, which is an error there too", () =>
        {
            c.That(Throws(() => new FightBuilder().Slots("elsewhere").Enemy(new Wriggler(), slot: "elsewhere").Start()), "starting un-stunned in an unknown slot throws");
            var f = new FightBuilder().DrawOrder(Defends(60)).Slots("elsewhere").Enemy(new Wriggler { StartStunned = true }, slot: "elsewhere").Start();
            c.Equal("stun", Intents(f, f.Enemy(0)), "a stunned one is fine until the branch is reached");
        });
    }

    // ---------- ten enemy turns of each elite against real cards and potions ----------
    //
    // A second, independent walk through each fight: the Ironclad's own cards (Strike 6, Defend 5,
    // Bash 8 and 2 Vulnerable, Twin Strike 5 twice, Thunderclap 4 to all and 1 Vulnerable, Uppercut
    // 13 with 1 Weak and 1 Vulnerable, Mangle 15 and 10 Strength away until the end of the enemy's
    // turn, Whirlwind 5 to all per energy, Flame Barrier 12 Block and 4 back per hit) and three
    // potions (Fire 20 and Explosive Ampoule 10 to all, neither modified; Shackling takes 7
    // Strength until the end of the enemy turn). The decks are drawn in the order written, five
    // cards a turn, and are long enough never to be reshuffled. Every number is worked out by hand
    // from the game's classes.

    private const string S = "StrikeIronclad", D = "DefendIronclad";

    private static readonly string[] FiveDefends = [D, D, D, D, D];

    private static string[] DrawnAs(params string[][] hands) => hands.SelectMany(h => h).ToArray();

    private static void Play(Fight f, string card, int? enemy = null, int times = 1)
    {
        for (var i = 0; i < times; i++) f.Play(card, enemy);
    }

    private static void TenEnemyTurns(Check c)
    {
        c.Group("Bygone Effigy: ten enemy turns against real cards and potions", () =>
        {
            var f = new FightBuilder()
                .DrawOrder(DrawnAs(
                    [S, S, D, "TwinStrike", "Bash"],
                    ["Thunderclap", D, D, S, "TwinStrike"],
                    ["Mangle", D, D, S, S],
                    ["Whirlwind", D, D, D, S],
                    ["FlameBarrier", S, D, D, D],
                    FiveDefends,
                    [S, D, D, D, D],
                    FiveDefends, FiveDefends, FiveDefends,
                    ["Bash", D, D, D, D],
                    FiveDefends))
                .Hp(300, 300).Potions("FirePotion", "ShacklingPotion", "ExplosiveAmpoule").Encounter(Effigy).Seed(5).Start();
            var effigy = f.Enemy(0);
            var slow = effigy.GetPower<SlowPower>()!;

            // Turn 1: it sleeps. Strike is the first card (x1.0), Twin Strike the second (x1.1 on both hits).
            c.Equal("sleep", Intents(f, effigy), "turn 1: intent");
            Play(f, S, 0);
            c.Equal(121, effigy.Hp, "turn 1: Strike as the first card, 6");
            Play(f, "TwinStrike", 0);
            c.Equal(111, effigy.Hp, "turn 1: Twin Strike as the second card, 5 x 1.1 = 5.5, so 5 twice");
            Play(f, D);
            c.Equal(30, slow.DisplayAmount, "turn 1: three cards, shown as 30");
            f.EndTurn();
            c.Equal(300, f.Player.Hp, "enemy turn 1: sleep");
            c.Equal(0, Strength(effigy), "enemy turn 1: no Strength");

            // Turn 2: it wakes. Vulnerable from Thunderclap multiplies with Slow; the potion is neither multiplied nor counted.
            c.Equal("buff", Intents(f, effigy), "turn 2: intent");
            c.Equal(0, slow.DisplayAmount, "turn 2: the count is back at 0");
            Play(f, "Thunderclap");
            c.Equal(107, effigy.Hp, "turn 2: Thunderclap as the first card, 4, before its own Vulnerable");
            Play(f, S, 0);
            c.Equal(98, effigy.Hp, "turn 2: Strike, 6 x 1.1 x 1.5 = 9.9, so 9");
            f.Do("potion:0>e0");
            c.Equal(78, effigy.Hp, "turn 2: Fire Potion, 20 flat");
            c.Equal(20, slow.DisplayAmount, "turn 2: a potion is no card");
            Play(f, "TwinStrike", 0);
            c.Equal(60, effigy.Hp, "turn 2: Twin Strike as the third card, 5 x 1.2 x 1.5 = 9 twice");
            f.EndTurn();
            c.Equal(300, f.Player.Hp, "enemy turn 2: waking deals nothing");
            c.Equal(10, Strength(effigy), "enemy turn 2: 10 Strength");
            c.That(!effigy.HasPower<VulnerablePower>(), "enemy turn 2: its 1 Vulnerable wore off");

            // Turn 3: Mangle takes all 10 Strength for this enemy turn; it comes back after the slash.
            c.Equal("attack 23", Intents(f, effigy), "turn 3: intent");
            Play(f, "Mangle", 0);
            c.Equal(45, effigy.Hp, "turn 3: Mangle as the first card, 15");
            c.Equal(0, Strength(effigy), "turn 3: 10 - 10 Strength");
            c.Equal("attack 13", Intents(f, effigy), "turn 3: the intent follows");
            f.EndTurn();
            c.Equal(287, f.Player.Hp, "enemy turn 3: slash for 13");
            c.Equal(10, Strength(effigy), "enemy turn 3: the Strength is back");

            // Turn 4: Whirlwind for 2 energy as the second card.
            c.Equal("attack 23", Intents(f, effigy), "turn 4: intent");
            Play(f, D);
            Play(f, "Whirlwind");
            c.Equal(35, effigy.Hp, "turn 4: Whirlwind X=2 as the second card, 5.5, so 5 twice");
            c.Equal(0, f.Energy, "turn 4: Whirlwind took the 2 energy left");
            f.EndTurn();
            c.Equal(269, f.Player.Hp, "enemy turn 4: 23 into 5 Block");

            // Turn 5: Flame Barrier answers the slash with 4, which Slow does not touch.
            Play(f, "FlameBarrier");
            Play(f, S, 0);
            c.Equal(29, effigy.Hp, "turn 5: Strike as the second card, 6.6, so 6");
            f.EndTurn();
            c.Equal(258, f.Player.Hp, "enemy turn 5: 23 into 12 Block");
            c.Equal(25, effigy.Hp, "enemy turn 5: 4 back from Flame Barrier");

            // Turn 6: the Shackling Potion.
            c.Equal("attack 23", Intents(f, effigy), "turn 6: intent");
            f.Do("potion:1");
            c.Equal(3, Strength(effigy), "turn 6: 10 - 7 Strength");
            c.Equal("attack 16", Intents(f, effigy), "turn 6: the intent follows");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(257, f.Player.Hp, "enemy turn 6: 16 into 15 Block");
            c.Equal(10, Strength(effigy), "enemy turn 6: the Strength is back");

            // Turn 7: a Strike as the third card.
            Play(f, D, times: 2);
            Play(f, S, 0);
            c.Equal(18, effigy.Hp, "turn 7: Strike as the third card, 7.2, so 7");
            f.EndTurn();
            c.Equal(244, f.Player.Hp, "enemy turn 7: 23 into 10 Block");

            // Turn 8: the Explosive Ampoule.
            f.Do("potion:2");
            c.Equal(8, effigy.Hp, "turn 8: Explosive Ampoule, 10 flat");
            f.EndTurn();
            c.Equal(221, f.Player.Hp, "enemy turn 8: 23");

            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(213, f.Player.Hp, "enemy turn 9: 23 into 15 Block");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(205, f.Player.Hp, "enemy turn 10: 23 into 15 Block");
            c.Equal(8, effigy.Hp, "enemy turn 10: its HP");
            c.Equal(10, Strength(effigy), "enemy turn 10: its Strength never grows");

            c.Equal(11, f.Turn, "turn 11");
            c.Equal("attack 23", Intents(f, effigy), "turn 11: intent");
            Play(f, "Bash", 0);
            c.That(f.IsOver && f.Won, "turn 11: Bash for 8 kills it at 8 HP");
            c.Equal(211, f.Player.Hp, "Burning Blood heals 6");
            c.Sequence(
                ["SLEEP_MOVE", "WAKE_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE", "SLASHES_MOVE"],
                effigy.Monster!.MoveLog, "the moves chosen, turn 1 to 11");
        });

        c.Group("Byrdonis: ten enemy turns against real cards and potions", () =>
        {
            var f = new FightBuilder()
                .DrawOrder(DrawnAs(
                    ["Bash", S, D, D, D],
                    ["TwinStrike", S, D, D, D],
                    ["Uppercut", D, D, D, D],
                    ["Mangle", D, D, D, D],
                    FiveDefends, FiveDefends, FiveDefends,
                    ["FlameBarrier", D, D, D, D],
                    ["Thunderclap", D, D, D, D],
                    FiveDefends,
                    [S, D, D, D, D],
                    FiveDefends))
                .Hp(300, 300).Potions("ShacklingPotion", "ExplosiveAmpoule").Encounter(Bird).Seed(5).Start();
            var bird = f.Enemy(0);
            f.SetHp(bird, 81);

            // Turn 1: Swoop 17.
            c.Equal("attack 17", Intents(f, bird), "turn 1: intent");
            Play(f, "Bash", 0);
            c.Equal(73, bird.Hp, "turn 1: Bash, 8");
            Play(f, D);
            f.EndTurn();
            c.Equal(288, f.Player.Hp, "enemy turn 1: 17 into 5 Block");
            c.Equal(1, Strength(bird), "enemy turn 1: Territorial");

            // Turn 2: Peck (3 + 1) x3, hit by hit into 10 Block.
            c.Equal("attack 4x3", Intents(f, bird), "turn 2: intent");
            Play(f, "TwinStrike", 0);
            c.Equal(59, bird.Hp, "turn 2: Twin Strike into 1 Vulnerable, 7.5, so 7 twice");
            Play(f, D, times: 2);
            f.EndTurn();
            c.Equal(286, f.Player.Hp, "enemy turn 2: 12 into 10 Block");
            c.Equal(2, Strength(bird), "enemy turn 2: Territorial");
            c.That(!bird.HasPower<VulnerablePower>(), "enemy turn 2: Vulnerable wore off");

            // Turn 3: Uppercut's Weak cuts the Swoop of this enemy turn, and is gone after it.
            c.Equal("attack 19", Intents(f, bird), "turn 3: intent");
            Play(f, "Uppercut", 0);
            c.Equal(46, bird.Hp, "turn 3: Uppercut, 13");
            c.Equal("attack 14", Intents(f, bird), "turn 3: 19 x 0.75 = 14.25");
            Play(f, D);
            f.EndTurn();
            c.Equal(277, f.Player.Hp, "enemy turn 3: 14.25 into 5 Block is 9.25, so 9");
            c.Equal(3, Strength(bird), "enemy turn 3: Territorial");
            c.That(!bird.HasPower<WeakPower>() && !bird.HasPower<VulnerablePower>(), "enemy turn 3: Weak and Vulnerable wore off");

            // Turn 4: Mangle. Territorial's 1 arrives while the Strength is down, and all 10 come back after it.
            c.Equal("attack 6x3", Intents(f, bird), "turn 4: intent");
            Play(f, "Mangle", 0);
            c.Equal(31, bird.Hp, "turn 4: Mangle, 15");
            c.Equal(-7, Strength(bird), "turn 4: 3 - 10 Strength");
            c.Equal("attack 0x3", Intents(f, bird), "turn 4: a hit cannot go below 0");
            c.Equal(0, f.IncomingDamage(), "turn 4: incoming damage");
            f.EndTurn();
            c.Equal(277, f.Player.Hp, "enemy turn 4: three hits of 0");
            c.Equal(4, Strength(bird), "enemy turn 4: -7 + 1 + 10");

            // Turn 5: the Shackling Potion.
            c.Equal("attack 21", Intents(f, bird), "turn 5: intent");
            f.Do("potion:0");
            c.Equal("attack 14", Intents(f, bird), "turn 5: 17 + 4 - 7");
            Play(f, D, times: 2);
            f.EndTurn();
            c.Equal(273, f.Player.Hp, "enemy turn 5: 14 into 10 Block");
            c.Equal(5, Strength(bird), "enemy turn 5: -3 + 1 + 7");

            c.Equal("attack 8x3", Intents(f, bird), "turn 6: intent");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(264, f.Player.Hp, "enemy turn 6: 24 into 15 Block");

            c.Equal("attack 23", Intents(f, bird), "turn 7: intent");
            f.EndTurn();
            c.Equal(241, f.Player.Hp, "enemy turn 7: 23");
            c.Equal(7, Strength(bird), "enemy turn 7: Strength");

            // Turn 8: Flame Barrier answers each of the three hits, blocked or not.
            c.Equal("attack 10x3", Intents(f, bird), "turn 8: intent");
            Play(f, "FlameBarrier");
            Play(f, D);
            f.EndTurn();
            c.Equal(228, f.Player.Hp, "enemy turn 8: 30 into 17 Block");
            c.Equal(19, bird.Hp, "enemy turn 8: 4 back for each of the three hits");

            // Turn 9: the Explosive Ampoule and Thunderclap.
            c.Equal("attack 25", Intents(f, bird), "turn 9: intent");
            f.Do("potion:1");
            c.Equal(9, bird.Hp, "turn 9: Explosive Ampoule, 10");
            Play(f, "Thunderclap");
            c.Equal(5, bird.Hp, "turn 9: Thunderclap, 4");
            f.EndTurn();
            c.Equal(203, f.Player.Hp, "enemy turn 9: 25");

            c.Equal("attack 12x3", Intents(f, bird), "turn 10: intent");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(182, f.Player.Hp, "enemy turn 10: 36 into 15 Block");
            c.Equal(10, Strength(bird), "enemy turn 10: Strength");

            c.Equal("attack 27", Intents(f, bird), "turn 11: intent");
            Play(f, S, 0);
            c.That(f.IsOver && f.Won, "turn 11: Strike for 6 kills it at 5 HP");
            c.Equal(188, f.Player.Hp, "Burning Blood heals 6");
            c.Sequence(
                ["SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE", "PECK_MOVE", "SWOOP_MOVE"],
                bird.Monster!.MoveLog, "the moves chosen, turn 1 to 11");
        });

        c.Group("Phrog Parasite and its Wrigglers: ten enemy turns against real cards and a potion", () =>
        {
            var f = new FightBuilder()
                .DrawOrder(DrawnAs(
                    ["Bash", S, S, D, D],
                    ["TwinStrike", S, D, D, D],
                    [S, S, S, D, D],
                    ["Whirlwind", D, D, D, D],
                    ["Thunderclap", D, D, D, D],
                    [S, D, D, D, D],
                    FiveDefends, FiveDefends, FiveDefends, FiveDefends, FiveDefends, FiveDefends))
                .Hp(300, 300).Potions("ExplosiveAmpoule").Encounter(Phrog).Seed(5).Start();
            var phrog = f.Enemy(0);
            f.SetHp(phrog, 61);

            c.Equal("statuscard: 3", Intents(f, phrog), "turn 1: intent");
            Play(f, "Bash", 0);
            Play(f, S, 0);
            c.Equal(44, phrog.Hp, "turn 1: Bash 8, then Strike into Vulnerable 9");
            f.EndTurn();
            c.Equal(300, f.Player.Hp, "enemy turn 1: Infect deals nothing");
            c.Equal(3, Infections(f.DiscardPile), "enemy turn 1: three Infections");

            c.Equal("attack 4x4", Intents(f, phrog), "turn 2: intent");
            Play(f, "TwinStrike", 0);
            Play(f, S, 0);
            c.Equal(21, phrog.Hp, "turn 2: Twin Strike 7 twice and Strike 9 into Vulnerable");
            Play(f, D);
            f.EndTurn();
            c.Equal(289, f.Player.Hp, "enemy turn 2: 16 into 5 Block");

            c.Equal("statuscard: 3", Intents(f, phrog), "turn 3: intent");
            Play(f, S, 0, times: 3);
            c.Equal(3, phrog.Hp, "turn 3: three Strikes of 6, Vulnerable is gone");
            f.EndTurn();
            c.Equal(6, Infections(f.DiscardPile), "enemy turn 3: three more Infections");

            // Turn 4: Whirlwind for 3. Its first hit kills the Phrog; the other two land on the Wrigglers that came.
            c.Equal("attack 4x4", Intents(f, phrog), "turn 4: intent");
            Play(f, "Whirlwind");
            c.That(phrog.IsDead && !f.IsOver, "turn 4: the Phrog is dead and the fight goes on");
            var w = f.Enemies.ToList();
            c.Sequence(["wriggler1", "wriggler2", "wriggler3", "wriggler4"], w.Select(e => e.Slot ?? "none"), "turn 4: four Wrigglers");
            c.Sequence([1, 2, 3, 4], w.Select(e => e.RosterIndex), "turn 4: they are e1 to e4");
            c.That(w.All(e => e.MaxHp is >= 17 and <= 21), "turn 4: HP rolled in 17 to 21");
            c.Sequence(w.Select(e => e.MaxHp - 10), w.Select(e => e.Hp), "turn 4: each took Whirlwind's second and third hit of 5");
            c.Sequence(["stun", "stun", "stun", "stun"], w.Select(e => Intents(f, e)), "turn 4: they show a stun");
            c.Equal(0, f.IncomingDamage(), "turn 4: the Phrog's Lash is gone with it");
            foreach (var e in w) f.SetHp(e, 7); // from here on with one known HP each (7 is the lowest any can have now)
            f.EndTurn();
            c.Equal(289, f.Player.Hp, "enemy turn 4: the Wrigglers only get over their stun");
            c.Equal(6, Infections(f.AllCards), "enemy turn 4: no new Infection");

            c.Sequence(["attack 6", WriggleIntent, "attack 6", WriggleIntent], w.Select(e => Intents(f, e)), "turn 5: intents");
            c.Equal(12, f.IncomingDamage(), "turn 5: incoming damage");
            Play(f, "Thunderclap");
            c.Sequence([3, 3, 3, 3], w.Select(e => e.Hp), "turn 5: Thunderclap, 4 to each");
            Play(f, D, times: 2);
            f.EndTurn();
            c.Equal(287, f.Player.Hp, "enemy turn 5: two bites of 6 into 10 Block");
            c.Sequence([0, 2, 0, 2], w.Select(Strength), "enemy turn 5: Strength");
            c.Equal(8, Infections(f.DiscardPile), "enemy turn 5: two Wriggles, two Infections");

            // Turn 6: wriggler1 dies. It is skipped from now on and the others keep their place in the script.
            c.Sequence([WriggleIntent, "attack 8", WriggleIntent, "attack 8"], w.Select(e => Intents(f, e)), "turn 6: intents");
            Play(f, S, 1);
            c.That(w[0].IsDead && !f.IsOver, "turn 6: wriggler1 is dead and the fight goes on");
            c.Equal(16, f.IncomingDamage(), "turn 6: incoming damage");
            Play(f, D, times: 2);
            f.EndTurn();
            c.Equal(281, f.Player.Hp, "enemy turn 6: two bites of 8 into 10 Block");
            c.Sequence([2, 2, 2], w.Skip(1).Select(Strength), "enemy turn 6: Strength of wriggler2 to wriggler4");
            c.Equal(9, Infections(f.DiscardPile), "enemy turn 6: one Wriggle, one Infection");

            c.Sequence([WriggleIntent, "attack 8", WriggleIntent], w.Skip(1).Select(e => Intents(f, e)), "turn 7: intents");
            Play(f, D);
            f.EndTurn();
            c.Equal(278, f.Player.Hp, "enemy turn 7: one bite of 8 into 5 Block");
            c.Sequence([4, 2, 4], w.Skip(1).Select(Strength), "enemy turn 7: Strength");
            c.Equal(11, Infections(f.DiscardPile), "enemy turn 7: Infections");

            c.Sequence(["attack 10", WriggleIntent, "attack 10"], w.Skip(1).Select(e => Intents(f, e)), "turn 8: intents");
            f.EndTurn();
            c.Equal(258, f.Player.Hp, "enemy turn 8: two bites of 10");
            c.Sequence([4, 4, 4], w.Skip(1).Select(Strength), "enemy turn 8: Strength");
            c.Equal(12, Infections(f.DiscardPile), "enemy turn 8: Infections");

            c.Sequence([WriggleIntent, "attack 10", WriggleIntent], w.Skip(1).Select(e => Intents(f, e)), "turn 9: intents");
            Play(f, D, times: 3);
            f.EndTurn();
            c.Equal(258, f.Player.Hp, "enemy turn 9: one bite of 10 into 15 Block");
            c.Sequence([6, 4, 6], w.Skip(1).Select(Strength), "enemy turn 9: Strength");
            c.Equal(14, Infections(f.DiscardPile), "enemy turn 9: Infections");

            c.Sequence(["attack 12", WriggleIntent, "attack 12"], w.Skip(1).Select(e => Intents(f, e)), "turn 10: intents");
            c.Equal(24, f.IncomingDamage(), "turn 10: incoming damage");
            f.EndTurn();
            c.Equal(234, f.Player.Hp, "enemy turn 10: two bites of 12");
            c.Sequence([6, 6, 6], w.Skip(1).Select(Strength), "enemy turn 10: Strength");
            c.Equal(15, Infections(f.DiscardPile), "enemy turn 10: Infections");
            c.Equal(15, Infections(f.AllCards), "enemy turn 10: and none anywhere else");

            c.Sequence([WriggleIntent, "attack 12", WriggleIntent], w.Skip(1).Select(e => Intents(f, e)), "turn 11: intents");
            f.Do("potion:0");
            c.That(f.IsOver && f.Won, "turn 11: the Explosive Ampoule's 10 kills the three at 3 HP");
            c.Equal(240, f.Player.Hp, "Burning Blood heals 6");
        });
    }

    // ---------- copies of a fight ----------

    private static void Copies(Check c)
    {
        c.Group("Copies: Slow's count belongs to the fight it is in", () =>
        {
            var f = StarterVs(Effigy);
            f.Play("Defend");
            f.Play("Defend");
            var copy = f.Clone();
            copy.Play("Strike", 0);
            c.Equal(120, copy.Enemy(0).Hp, "the copy carries the count: 6 x 1.2 = 7.2, so 7");
            c.Equal(30, copy.Enemy(0).GetPower<SlowPower>()!.DisplayAmount, "and counts on by itself");
            c.Equal(127, f.Enemy(0).Hp, "the original was not hit");
            c.Equal(20, f.Enemy(0).GetPower<SlowPower>()!.DisplayAmount, "and its count did not move");
            f.Play("Strike", 0);
            c.Equal(120, f.Enemy(0).Hp, "the original goes on as the copy did");
        });

        c.Group("Copies: copied Wrigglers keep their slot, their stunned start and their place in the script", () =>
        {
            var f = VsElite(Phrog, hp: 500);
            Slay(f, 0);
            var copy = f.Clone();
            copy.EndTurn();
            c.Sequence(["attack 6", WriggleIntent, "attack 6", WriggleIntent], copy.Enemies.Select(w => Intents(copy, w)), "the copy's Wrigglers, one turn on");
            c.Sequence(["stun", "stun", "stun", "stun"], f.Enemies.Select(w => Intents(f, w)), "the original has not moved");
            copy.EndTurn();
            var again = copy.Clone();
            again.EndTurn();
            copy.EndTurn();
            c.Equal(copy.Seen(), again.Seen(), "a copy made in mid-script goes on exactly like its source");
            c.Equal(500 - 12 - 16, copy.Player.Hp, "two bites of 6, then two of 8");
        });

        c.Group("Copies: every elite fight plays out the same from the same seed, and from a copy made on the way", () =>
        {
            foreach (var encounter in new[] { Effigy, Bird, Phrog })
            {
                for (var seed = 1; seed <= 6; seed++)
                {
                    var first = new FightBuilder().Encounter(encounter).Seed(seed).Start();
                    var whole = CheckKit.PlayOut(first);
                    c.That(first.IsOver, $"{encounter} seed {seed}: the fight ends");

                    var second = new FightBuilder().Encounter(encounter).Seed(seed).Start();
                    Step(second, 9 + seed * 4);
                    var copy = second.Clone();
                    var rest = CheckKit.PlayOut(second);
                    c.Equal(rest, CheckKit.PlayOut(copy), $"{encounter} seed {seed}: a copy finishes like its source");
                    c.That(whole.EndsWith(rest, StringComparison.Ordinal), $"{encounter} seed {seed}: and like a fresh fight of the same seed");
                }
            }
        });
    }

    /// <summary>Plays a number of actions by the rule <see cref="CheckKit.PlayOut"/> uses.</summary>
    private static void Step(Fight f, int actions)
    {
        for (var n = 0; n < actions && !f.IsOver; n++)
        {
            var legal = f.LegalActions();
            var pick = legal.FirstOrDefault(a => a.Id != "end_turn" && !a.Id.Contains("potion", StringComparison.Ordinal)) ?? legal[^1];
            f.Do(pick.Id);
        }
    }
}
