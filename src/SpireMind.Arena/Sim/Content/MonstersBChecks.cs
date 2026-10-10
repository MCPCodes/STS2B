using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// Scripted checks of MonstersB: the four slimes, Flyconid, Snapping Jaxfruit, Slithering
/// Strangler, Constrict and the five encounters. Every expected number was worked out by hand
/// from the v0.107.1 monster, encounter and power classes (and the spec's encounters-1.json and
/// encounters-2.json), not read off the simulator. Random branches are met in two ways: a seed
/// is searched whose MonsterAi stream, run through the game's branch rule as written here (not
/// through the engine's), gives a move sequence written down by hand, and that fight is then
/// walked turn by turn; and many seeds are walked against the hand-made table of which move may
/// follow which.
/// </summary>
public sealed class MonstersBChecks : IArenaChecks
{
    // The game's move ids.
    private const string StickyLeaf = "STICKY_SHOT";
    private const string Clump = "CLUMP_SHOT";
    private const string StickyTwig = "STICKY_SHOT_MOVE";
    private const string Pokey = "POKEY_POUNCE_MOVE";
    private const string Tackle = "TACKLE_MOVE";
    private const string Goop = "GOOP_MOVE";
    private const string VulnSpores = "VULNERABLE_SPORES_MOVE";
    private const string FrailSpores = "FRAIL_SPORES_MOVE";
    private const string Smash = "SMASH_MOVE";
    private const string Orb = "ENERGY_ORB_MOVE";
    private const string Constrict = "CONSTRICT";
    private const string Thwack = "THWACK";
    private const string Lash = "LASH";

    private static readonly string[] MonsterClasses =
        ["LeafSlimeM", "LeafSlimeS", "TwigSlimeM", "TwigSlimeS", "Flyconid", "SnappingJaxfruit", "SlitheringStrangler"];

    private static readonly string[] EncounterClasses =
        ["SlimesWeak", "SlimesNormal", "FlyconidNormal", "SnappingJaxfruitNormal", "SlitheringStranglerNormal"];

    public void Run(Check c)
    {
        Registered(c);
        HpRolls(c);
        LeafSlimeMChecks(c);
        LeafSlimeSChecks(c);
        TwigSlimeMChecks(c);
        TwigSlimeSChecks(c);
        SlimedCards(c);
        FlyconidChecks(c);
        JaxfruitChecks(c);
        StranglerChecks(c);
        ConstrictChecks(c);
        PoweredMoves(c);
        ActingOrder(c);
        Compositions(c);
        EncounterWalks(c);
        Determinism(c);
    }

    // ---------- by-hand facts ----------

    /// <summary>The game's English name and ascension 0 HP range of each monster.</summary>
    private static (string Name, int Min, int Max) Facts(string className) => className switch
    {
        "LeafSlimeM" => ("Leaf Slime (M)", 32, 35),
        "LeafSlimeS" => ("Leaf Slime (S)", 11, 15),
        "TwigSlimeM" => ("Twig Slime (M)", 26, 28),
        "TwigSlimeS" => ("Twig Slime (S)", 7, 11),
        "Flyconid" => ("Flyconid", 47, 49),
        "SnappingJaxfruit" => ("Snapping Jaxfruit", 31, 33),
        "SlitheringStrangler" => ("Slithering Strangler", 53, 55),
        _ => throw new ArgumentException(className),
    };

    // ---------- the game's branch rule, written out independently of the engine ----------

    /// <summary>
    /// One visit to a random branch: one float in [0, total weight), then the options in listed
    /// order, each taking its weight off; the first that brings the rest to 0 or below is taken.
    /// Every weight here is 1, or 0 for an option that is shut. With all shut the draw is 0 and
    /// the first listed option is taken.
    /// </summary>
    private static string Pick(GameRng rng, params (string Id, bool Open)[] options)
    {
        var total = 0f;
        foreach (var o in options) total += o.Open ? 1f : 0f;
        var roll = rng.NextFloat(total);
        foreach (var o in options)
        {
            roll -= o.Open ? 1f : 0f;
            if (roll <= 0f) return o.Id;
        }
        throw new InvalidOperationException("no option");
    }

    private static bool InLast(List<string> log, int n, string id)
    {
        for (var back = 1; back <= n && back <= log.Count; back++)
            if (log[^back] == id) return true;
        return false;
    }

    /// <summary>
    /// The move a monster of this class chooses next, given every move it chose so far (empty for
    /// the first move), drawing from <paramref name="ai"/> exactly when its machine passes a
    /// random branch.
    /// </summary>
    private static string Next(string className, List<string> log, GameRng ai)
    {
        switch (className)
        {
            case "LeafSlimeM":
                // Sticky Shot first, then strict alternation; no branch.
                return log.Count == 0 || log[^1] == Clump ? StickyLeaf : Clump;
            case "LeafSlimeS":
                // The machine starts in the branch; neither move may follow itself.
                return Pick(ai, (Tackle, !InLast(log, 1, Tackle)), (Goop, !InLast(log, 1, Goop)));
            case "TwigSlimeM":
                if (log.Count == 0) return StickyTwig;
                // Pokey Pounce at most twice in a row, Sticky Shot never twice in a row.
                return Pick(ai,
                    (Pokey, !(log.Count >= 2 && log[^1] == Pokey && log[^2] == Pokey)),
                    (StickyTwig, !InLast(log, 1, StickyTwig)));
            case "TwigSlimeS":
                return Tackle;
            case "Flyconid":
                // Its own first branch holds only Frail Spores and Smash.
                if (log.Count == 0) return Pick(ai, (FrailSpores, true), (Smash, true));
                return Pick(ai,
                    (VulnSpores, !InLast(log, 3, VulnSpores)),
                    (FrailSpores, !InLast(log, 2, FrailSpores)),
                    (Smash, !InLast(log, 1, Smash)));
            case "SnappingJaxfruit":
                return Orb;
            case "SlitheringStrangler":
                if (log.Count == 0) return Constrict;
                return log[^1] == Constrict ? Pick(ai, (Thwack, true), (Lash, true)) : Constrict;
            default:
                throw new ArgumentException(className);
        }
    }

    private static GameRng AiStream(int seed) => RngSet.For(seed, "monster_ai");

    /// <summary>The first <paramref name="count"/> moves of a monster that is alone in a fight with this seed.</summary>
    private static List<string> Predict(string className, int seed, int count)
    {
        var ai = AiStream(seed);
        var log = new List<string>();
        while (log.Count < count) log.Add(Next(className, log, ai));
        return log;
    }

    /// <summary>The first seed on which a lone monster of this class makes exactly these moves.</summary>
    private static int FindSeed(string className, params string[] wanted)
    {
        for (var seed = 1; seed <= 100_000; seed++)
            if (Predict(className, seed, wanted.Length).SequenceEqual(wanted)) return seed;
        throw new InvalidOperationException($"no seed gives {className} the moves {string.Join(", ", wanted)}");
    }

    // ---------- walking a lone monster ----------

    /// <summary>One expected enemy turn of a lone monster against a player who only ends turns.</summary>
    /// <param name="Move">The move the monster intends during the player's turn and then performs.</param>
    /// <param name="Intents">The exact intent texts during the player's turn.</param>
    /// <param name="Block">Block the player holds when the turn is ended.</param>
    /// <param name="Hits">Every damage instance on the player from ending the turn to the next turn, in order,
    /// as "dealer:blocked/unblocked" with dealer "self" (Constrict) or "e" (the monster).</param>
    /// <param name="Powers">Powers the monster applied, in order, as "ClassName amount on player|self".</param>
    /// <param name="Guard">Block gains of the monster, in order.</param>
    /// <param name="Cards">Slimed cards created.</param>
    /// <param name="Ai">MonsterAi numbers drawn in the whole fight once the next turn has begun.</param>
    private sealed record Step(
        string Move, string[] Intents, int Block = 0, string[]? Hits = null, string[]? Powers = null, int[]? Guard = null, int Cards = 0, int Ai = 0);

    private static Fight Lone(string className, int hp, int seed = 1, int playerHp = 999) =>
        new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(playerHp, playerHp).Enemy(className, hp).Seed(seed).Start();

    /// <summary>The HP the player loses while <paramref name="act"/> runs.</summary>
    private static int Lost(Fight f, Action act)
    {
        var before = f.Player.Hp;
        act();
        return before - f.Player.Hp;
    }

    private static int CountSlimed(Fight f) => f.AllCards.Count(card => card.Def.ClassName == "Slimed");

    private static int IncomingOf(IEnumerable<string> intents)
    {
        var total = 0;
        foreach (var text in intents)
        {
            if (!text.StartsWith("attack ", StringComparison.Ordinal)) continue;
            var parts = text["attack ".Length..].Split('x');
            var hits = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 1;
            total += int.Parse(parts[0], CultureInfo.InvariantCulture) * hits;
        }
        return total;
    }

    private static List<HistoryEntry> Since(Fight f, int mark) => f.History.Entries.Skip(mark).ToList();

    private static List<string> PlayerHits(Fight f, IEnumerable<HistoryEntry> entries, Creature enemy) => entries
        .Where(e => e.Kind == HistoryKind.DamageReceived && e.ActorId == f.Player.Id)
        .Select(e => string.Create(CultureInfo.InvariantCulture,
            $"{(e.OtherId == f.Player.Id ? "self" : e.OtherId == enemy.Id ? "e" : "other")}:{e.Blocked}/{e.Amount}"))
        .ToList();

    /// <summary>
    /// Plays the steps against the fight's only enemy: before each end of turn the intended move,
    /// the intent text and the incoming damage; after it every damage instance on the player
    /// through Block, the powers the monster applied, the Block it gained, the cards it created,
    /// the move it performed and the MonsterAi draws so far.
    /// </summary>
    private static void Walk(Check c, Fight f, Step[] steps, string then, Action<int>? after = null)
    {
        var enemy = f.Enemy(0);
        var monster = enemy.Monster!;
        for (var i = 0; i < steps.Length; i++)
        {
            var s = steps[i];
            var turn = i + 1;
            var at = $"turn {turn}";
            c.Equal(turn, f.Turn, $"{at}: the turn number");
            c.Equal(s.Move, monster.NextMove?.Id, $"{at}: intended move");
            c.Sequence(s.Intents, f.IntentTexts(enemy), $"{at}: intent text");
            c.Equal(IncomingOf(s.Intents), f.IncomingDamage(), $"{at}: incoming damage");

            var hp = f.Player.Hp;
            var slimed = CountSlimed(f);
            var mark = f.History.EntryCount;
            f.SetBlock(f.Player, s.Block);
            f.EndTurn();
            var entries = Since(f, mark);

            var hits = s.Hits ?? [];
            c.Sequence(hits, PlayerHits(f, entries, enemy), $"{at}: damage on the player as dealer:blocked/unblocked");
            c.Equal(hits.Sum(h => int.Parse(h[(h.IndexOf('/') + 1)..], CultureInfo.InvariantCulture)), hp - f.Player.Hp, $"{at}: HP lost");
            c.Sequence(s.Powers ?? [],
                entries.Where(e => e.Kind == HistoryKind.PowerReceived && e.OtherId == enemy.Id)
                    .Select(e => string.Create(CultureInfo.InvariantCulture, $"{e.Name} {e.Amount} on {(e.ActorId == f.Player.Id ? "player" : "self")}")),
                $"{at}: powers the monster applied");
            c.Sequence(s.Guard ?? [], entries.Where(e => e.Kind == HistoryKind.BlockGained && e.ActorId == enemy.Id).Select(e => e.Amount),
                $"{at}: Block the monster gained");
            c.Sequence(Enumerable.Repeat("Slimed", s.Cards), entries.Where(e => e.Kind == HistoryKind.CardGenerated).Select(e => e.Card!.ClassName),
                $"{at}: cards created");
            c.Equal(s.Cards, CountSlimed(f) - slimed, $"{at}: Slimed added to the player's piles");
            c.Sequence([s.Move], entries.Where(e => e.Kind == HistoryKind.MonsterPerformedMove).Select(e => e.Name!), $"{at}: the move performed");
            c.Equal(s.Ai, f.Rng.MonsterAi.Counter, $"{at}: MonsterAi draws so far");
            after?.Invoke(turn);
        }
        c.Equal(then, monster.NextMove?.Id, "the move intended after the last walked turn");
    }

    // ---------- registration and HP ----------

    private static void Registered(Check c)
    {
        c.Group("registration: seven monsters, five encounters and Constrict come from MonstersB", () =>
        {
            var r = Content.Registry;
            foreach (var cls in MonsterClasses)
            {
                c.That(r.Has(ContentKind.Monster, cls), $"{cls} is registered");
                c.Equal("MonstersB", r.SourceOf(ContentKind.Monster, cls), $"{cls} comes from this file");
                var monster = r.NewMonster(cls);
                var (name, min, max) = Facts(cls);
                c.Equal(name, monster.Name, $"{cls}: the game's English name");
                c.Equal(min, monster.MinHp, $"{cls}: lowest HP");
                c.Equal(max, monster.MaxHp, $"{cls}: highest HP");
                c.That(!ListenerFields.UnsafeFields(monster.GetType()).Any(), $"{cls} keeps nothing a copy of the fight would share");
            }

            foreach (var cls in EncounterClasses)
            {
                c.That(r.Has(ContentKind.Encounter, cls), $"{cls} is registered");
                c.Equal("MonstersB", r.SourceOf(ContentKind.Encounter, cls), $"{cls} comes from this file");
                var encounter = r.NewEncounter(cls);
                c.Equal(RoomType.Monster, encounter.RoomType, $"{cls} is a monster room");
                c.Equal(cls == "SlimesWeak", encounter.IsWeak, $"{cls}: weak only for SlimesWeak");
                c.Equal(0, encounter.Slots.Count, $"{cls} names no slots");
            }

            c.That(r.Has(ContentKind.Power, "ConstrictPower"), "ConstrictPower is registered");
            c.Equal("MonstersB", r.SourceOf(ContentKind.Power, "ConstrictPower"), "ConstrictPower comes from this file");
            var constrict = r.NewPower("ConstrictPower");
            c.Equal("Constrict", constrict.Name, "the game's title is Constrict");
            c.Equal(PowerType.Debuff, constrict.Type, "Constrict is a Debuff");
            c.Equal(PowerStackType.Counter, constrict.StackType, "Constrict is a counter");
            c.Equal(PowerInstanceType.None, constrict.InstanceType, "one instance per creature");
            c.That(!constrict.AllowNegative, "Constrict cannot go negative");
            c.That(!ListenerFields.UnsafeFields(typeof(ConstrictPower)).Any(), "Constrict keeps nothing a copy would share");
        });
    }

    private static void HpRolls(Check c)
    {
        c.Group("HP: each monster alone rolls once on the Niche stream, anywhere in its range", () =>
        {
            foreach (var cls in MonsterClasses)
            {
                var (name, min, max) = Facts(cls);
                var seen = new HashSet<int>();
                for (var seed = 1; seed <= 120; seed++)
                {
                    var f = new FightBuilder().Enemy(cls).Seed(seed).Start();
                    var enemy = f.Enemy(0);
                    c.Equal(name, enemy.Name, $"{cls} seed {seed}: name");
                    c.That(enemy.MaxHp >= min && enemy.MaxHp <= max, $"{cls} seed {seed}: HP {enemy.MaxHp} outside {min}..{max}");
                    c.Equal(enemy.MaxHp, enemy.Hp, $"{cls} seed {seed}: starts at full HP");
                    c.Equal(PredictHp([cls], seed)[0], enemy.MaxHp, $"{cls} seed {seed}: the roll is the Niche stream's first pick over the range");
                    c.Equal(1, f.Rng.Niche.Counter, $"{cls} seed {seed}: one Niche draw");
                    c.Equal(0, enemy.Powers.Count, $"{cls} seed {seed}: no starting power");
                    c.Equal(0, enemy.Block, $"{cls} seed {seed}: no starting Block");
                    seen.Add(enemy.MaxHp);
                }
                c.Equal(max - min + 1, seen.Count, $"{cls}: every HP of {min}..{max} turns up over 120 seeds");
            }
        });
    }

    /// <summary>
    /// The HP of a line-up by the game's rule: monsters are created in line-up order; each takes
    /// one value, uniformly by one Niche draw, from the values of its range that no earlier
    /// monster of the fight has (the values in rising order).
    /// </summary>
    private static List<int> PredictHp(IReadOnlyList<string> lineUp, int seed)
    {
        var niche = RngSet.For(seed, "niche");
        var taken = new List<int>();
        foreach (var cls in lineUp)
        {
            var (_, min, max) = Facts(cls);
            var free = Enumerable.Range(min, max - min + 1).Where(v => !taken.Contains(v)).ToList();
            taken.Add(free[niche.NextInt(0, free.Count)]);
        }
        return taken;
    }

    // ---------- the slimes ----------

    private static void LeafSlimeMChecks(Check c)
    {
        c.Group("Leaf Slime (M): Sticky Shot and Clump Shot alternate, Sticky Shot first, with no MonsterAi draw", () =>
        {
            var f = Lone("LeafSlimeM", 34, seed: 5);
            Walk(c, f,
            [
                new Step(StickyLeaf, ["statuscard: 2"], Cards: 2),
                new Step(Clump, ["attack 8"], Hits: ["e:0/8"]),
                new Step(StickyLeaf, ["statuscard: 2"], Cards: 2),
                new Step(Clump, ["attack 8"], Block: 5, Hits: ["e:5/3"]),
                new Step(StickyLeaf, ["statuscard: 2"], Cards: 2),
                new Step(Clump, ["attack 8"], Block: 10, Hits: ["e:8/0"]),
                new Step(StickyLeaf, ["statuscard: 2"], Cards: 2),
            ], then: Clump);
            c.Equal(999 - 8 - 3, f.Player.Hp, "8 and 3 HP lost over seven turns");
            c.Equal(8, CountSlimed(f), "four Sticky Shots made 8 Slimed");
            c.Sequence([StickyLeaf, Clump, StickyLeaf, Clump, StickyLeaf, Clump, StickyLeaf, Clump], f.Enemy(0).Monster!.MoveLog, "the move log");
            c.Equal(34, f.Enemy(0).Hp, "it never hurts or heals itself");
        });

        c.Group("Leaf Slime (M): the pattern is the same on every seed", () =>
        {
            for (var seed = 1; seed <= 25; seed++)
            {
                var f = Lone("LeafSlimeM", 33, seed);
                var monster = f.Enemy(0).Monster!;
                for (var turn = 1; turn <= 8; turn++)
                {
                    c.Equal(turn % 2 == 1 ? StickyLeaf : Clump, monster.NextMove?.Id, $"seed {seed} turn {turn}");
                    f.EndTurn();
                }
                c.Equal(0, f.Rng.MonsterAi.Counter, $"seed {seed}: no MonsterAi draw in eight turns");
                c.Equal(999 - 4 * 8, f.Player.Hp, $"seed {seed}: four Clump Shots of 8");
            }
        });
    }

    private static void LeafSlimeSChecks(Check c)
    {
        c.Group("Leaf Slime (S) opening with Tackle: Tackle and Goop alternate, one MonsterAi draw per pick", () =>
        {
            var seed = FindSeed("LeafSlimeS", Tackle);
            var f = Lone("LeafSlimeS", 13, seed);
            c.Equal(1, f.Rng.MonsterAi.Counter, "the first move costs one draw when the monster joins");
            Walk(c, f,
            [
                new Step(Tackle, ["attack 3"], Hits: ["e:0/3"], Ai: 2),
                new Step(Goop, ["statuscard: 1"], Cards: 1, Ai: 3),
                new Step(Tackle, ["attack 3"], Block: 1, Hits: ["e:1/2"], Ai: 4),
                new Step(Goop, ["statuscard: 1"], Cards: 1, Ai: 5),
                new Step(Tackle, ["attack 3"], Block: 3, Hits: ["e:3/0"], Ai: 6),
                new Step(Goop, ["statuscard: 1"], Cards: 1, Ai: 7),
                new Step(Tackle, ["attack 3"], Hits: ["e:0/3"], Ai: 8),
            ], then: Goop);
            c.Equal(999 - 3 - 2 - 3, f.Player.Hp, "HP lost over seven turns");
            c.Equal(3, CountSlimed(f), "three Goops made 3 Slimed");
        });

        c.Group("Leaf Slime (S) opening with Goop: the same alternation from the other side", () =>
        {
            var seed = FindSeed("LeafSlimeS", Goop);
            var f = Lone("LeafSlimeS", 12, seed);
            c.Equal(1, f.Rng.MonsterAi.Counter, "the first move costs one draw when the monster joins");
            Walk(c, f,
            [
                new Step(Goop, ["statuscard: 1"], Cards: 1, Ai: 2),
                new Step(Tackle, ["attack 3"], Hits: ["e:0/3"], Ai: 3),
                new Step(Goop, ["statuscard: 1"], Cards: 1, Ai: 4),
                new Step(Tackle, ["attack 3"], Block: 2, Hits: ["e:2/1"], Ai: 5),
                new Step(Goop, ["statuscard: 1"], Cards: 1, Ai: 6),
                new Step(Tackle, ["attack 3"], Block: 9, Hits: ["e:3/0"], Ai: 7),
                new Step(Goop, ["statuscard: 1"], Cards: 1, Ai: 8),
            ], then: Tackle);
            c.Equal(999 - 3 - 1, f.Player.Hp, "HP lost over seven turns");
            c.Equal(4, CountSlimed(f), "four Goops made 4 Slimed");
        });

        c.Group("Leaf Slime (S): the first move is an even pick, never repeated afterwards (300 seeds)", () =>
        {
            var tackleFirst = 0;
            for (var seed = 1; seed <= 300; seed++)
            {
                var f = Lone("LeafSlimeS", 13, seed);
                var monster = f.Enemy(0).Monster!;
                var first = monster.NextMove!.Id;
                c.That(first is Tackle or Goop, $"seed {seed}: first move {first}");
                c.Equal(Predict("LeafSlimeS", seed, 1)[0], first, $"seed {seed}: the first move follows the first MonsterAi number");
                if (first == Tackle) tackleFirst++;
                var last = first;
                for (var turn = 2; turn <= 9; turn++)
                {
                    f.EndTurn();
                    var now = monster.NextMove!.Id;
                    c.That(now != last && now is Tackle or Goop, $"seed {seed} turn {turn}: {now} after {last}");
                    last = now;
                }
                c.Equal(9, f.Rng.MonsterAi.Counter, $"seed {seed}: one draw at the start and one per later turn");
            }
            c.That(tackleFirst is >= 110 and <= 190, $"Tackle opens {tackleFirst} of 300 fights; an even pick is expected");
        });
    }

    private static void TwigSlimeMChecks(Check c)
    {
        c.Group("Twig Slime (M): Sticky Shot, Pounce, Pounce, Sticky Shot, Pounce, Sticky Shot, Pounce (both outcomes of the branch)", () =>
        {
            var seed = FindSeed("TwigSlimeM", StickyTwig, Pokey, Pokey, StickyTwig, Pokey, StickyTwig, Pokey, Pokey);
            var f = Lone("TwigSlimeM", 27, seed);
            c.Equal(0, f.Rng.MonsterAi.Counter, "the fixed first move costs no draw");
            Walk(c, f,
            [
                new Step(StickyTwig, ["statuscard: 1"], Cards: 1, Ai: 1),
                new Step(Pokey, ["attack 11"], Hits: ["e:0/11"], Ai: 2),
                new Step(Pokey, ["attack 11"], Block: 4, Hits: ["e:4/7"], Ai: 3),
                new Step(StickyTwig, ["statuscard: 1"], Cards: 1, Ai: 4),
                new Step(Pokey, ["attack 11"], Block: 11, Hits: ["e:11/0"], Ai: 5),
                new Step(StickyTwig, ["statuscard: 1"], Cards: 1, Ai: 6),
                new Step(Pokey, ["attack 11"], Block: 20, Hits: ["e:11/0"], Ai: 7),
            ], then: Pokey);
            c.Equal(999 - 11 - 7, f.Player.Hp, "HP lost over seven turns");
            c.Equal(3, CountSlimed(f), "three Sticky Shots made 3 Slimed");
        });

        c.Group("Twig Slime (M): strict alternation when the branch keeps choosing Sticky Shot", () =>
        {
            var seed = FindSeed("TwigSlimeM", StickyTwig, Pokey, StickyTwig, Pokey, StickyTwig, Pokey, StickyTwig, Pokey);
            var f = Lone("TwigSlimeM", 28, seed);
            Walk(c, f,
            [
                new Step(StickyTwig, ["statuscard: 1"], Cards: 1, Ai: 1),
                new Step(Pokey, ["attack 11"], Hits: ["e:0/11"], Ai: 2),
                new Step(StickyTwig, ["statuscard: 1"], Cards: 1, Ai: 3),
                new Step(Pokey, ["attack 11"], Block: 10, Hits: ["e:10/1"], Ai: 4),
                new Step(StickyTwig, ["statuscard: 1"], Cards: 1, Ai: 5),
                new Step(Pokey, ["attack 11"], Hits: ["e:0/11"], Ai: 6),
                new Step(StickyTwig, ["statuscard: 1"], Cards: 1, Ai: 7),
            ], then: Pokey);
            c.Equal(999 - 11 - 1 - 11, f.Player.Hp, "HP lost over seven turns");
            c.Equal(4, CountSlimed(f), "four Sticky Shots made 4 Slimed");
        });

        c.Group("Twig Slime (M): which move may follow which, over 300 seeds", () =>
        {
            // Recent moves, oldest first, to the moves that may follow (S = Sticky Shot, P = Pokey Pounce).
            var allowed = new Dictionary<string, string>
            {
                ["S"] = "P",     // Sticky Shot cannot repeat
                ["SP"] = "PS",   // after one Pounce either
                ["PP"] = "S",    // Pounce at most twice in a row
                ["PS"] = "P",
            };
            var seenAfter = allowed.Keys.ToDictionary(k => k, _ => new HashSet<char>());
            for (var seed = 1; seed <= 300; seed++)
            {
                var f = Lone("TwigSlimeM", 27, seed);
                var monster = f.Enemy(0).Monster!;
                var predicted = Predict("TwigSlimeM", seed, 12);
                var letters = "";
                for (var turn = 1; turn <= 12; turn++)
                {
                    var move = monster.NextMove!.Id;
                    c.Equal(predicted[turn - 1], move, $"seed {seed} turn {turn}: the move the MonsterAi stream gives by the branch rule");
                    var letter = move == StickyTwig ? 'S' : 'P';
                    if (turn == 1)
                    {
                        c.Equal(StickyTwig, move, $"seed {seed}: the first move is Sticky Shot");
                    }
                    else
                    {
                        var key = letters.Length >= 2 ? letters[^2..] : letters;
                        c.That(allowed[key].Contains(letter), $"seed {seed} turn {turn}: {letter} after {key}");
                        seenAfter[key].Add(letter);
                    }
                    letters += letter;
                    if (turn < 12) f.EndTurn();
                }
                c.Equal(11, f.Rng.MonsterAi.Counter, $"seed {seed}: one draw per turn after the first, forced picks included");
            }
            foreach (var (key, next) in allowed)
                c.Equal(next.Length, seenAfter[key].Count, $"after {key} every allowed move ({next}) was seen");
        });
    }

    private static void TwigSlimeSChecks(Check c)
    {
        c.Group("Twig Slime (S): Tackle for 4 every turn, no MonsterAi draw", () =>
        {
            var f = Lone("TwigSlimeS", 9, seed: 3);
            Walk(c, f,
            [
                new Step(Tackle, ["attack 4"], Hits: ["e:0/4"]),
                new Step(Tackle, ["attack 4"], Block: 3, Hits: ["e:3/1"]),
                new Step(Tackle, ["attack 4"], Block: 4, Hits: ["e:4/0"]),
                new Step(Tackle, ["attack 4"], Hits: ["e:0/4"]),
                new Step(Tackle, ["attack 4"], Block: 9, Hits: ["e:4/0"]),
                new Step(Tackle, ["attack 4"], Block: 1, Hits: ["e:1/3"]),
                new Step(Tackle, ["attack 4"], Hits: ["e:0/4"]),
            ], then: Tackle);
            c.Equal(999 - 4 - 1 - 4 - 3 - 4, f.Player.Hp, "HP lost over seven turns");
            c.Equal(0, CountSlimed(f), "it makes no cards");
        });
    }

    private static void SlimedCards(Check c)
    {
        c.Group("Slimed is created in the discard pile, at the bottom, one card at a time", () =>
        {
            foreach (var (cls, hp, count) in new[] { ("LeafSlimeM", 34, 2), ("TwigSlimeM", 27, 1) })
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(cls, hp).Start();
                c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash"], f.HandTitles(), $"{cls}: the first hand");
                f.EndTurn();
                // The hand was flushed first (five cards in hand order); the enemy turn then appended the Slimed.
                var expected = new List<string> { "Strike", "Strike", "Defend", "Defend", "Bash" };
                expected.AddRange(Enumerable.Repeat("Slimed", count));
                c.Sequence(expected, f.DiscardPile.Select(card => card.Title), $"{cls}: the discard pile after turn 1");
                c.That(!f.Hand.Concat(f.DrawPile).Any(card => card.Def.ClassName == "Slimed"), $"{cls}: none in the hand or the draw pile");
                var slimed = f.DiscardPile.Where(card => card.Def.ClassName == "Slimed").ToList();
                c.That(slimed.Select(card => card.Id).Distinct().Count() == count, $"{cls}: {count} separate cards");
                for (var i = 1; i < slimed.Count; i++)
                    c.That(slimed[i].Id > slimed[i - 1].Id, $"{cls}: created in pile order");
                foreach (var card in slimed)
                {
                    c.Equal(CardType.Status, card.Type, $"{cls}: a Status card");
                    c.Equal(1, f.CostOf(card), $"{cls}: Slimed costs 1");
                    c.That(card.Has(CardKeywords.Exhaust), $"{cls}: Slimed has Exhaust");
                }
            }

            // Leaf Slime (S): the same from Goop.
            var g = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy("LeafSlimeS", 13).Seed(FindSeed("LeafSlimeS", Goop)).Start();
            g.EndTurn();
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Bash", "Slimed"], g.DiscardPile.Select(card => card.Title), "Leaf Slime (S): the discard pile after a Goop");
        });

        c.Group("slimes do not split, summon or leave anything when they die", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder)
                .Enemy("LeafSlimeM", 34).Enemy("TwigSlimeM", 27).Enemy("LeafSlimeS", 13).Enemy("TwigSlimeS", 9).Start();
            for (var j = 0; j < 3; j++)
            {
                f.Kill([f.Enemy(j)]);
                c.Equal(3 - j, f.Enemies.Count, $"{j + 1} dead: {3 - j} enemies left");
                c.Equal(4, f.Roster.Count, "no new enemy joined");
                c.That(!f.IsEnding, "the fight goes on while a slime lives");
            }
            c.Equal(0, CountSlimed(f), "dying adds no card");
            f.Kill([f.Enemy(3)]);
            f.EndTurn();
            c.That(f.IsOver && f.Won, "the fight is won when the last slime is dead");
        });
    }

    // ---------- Flyconid ----------

    private static void FlyconidChecks(Check c)
    {
        c.Group("Flyconid: Frail Spores, Vulnerable Spores, Smash, Frail Spores, Smash, Vulnerable Spores, Smash", () =>
        {
            var seed = FindSeed("Flyconid", FrailSpores, VulnSpores, Smash, FrailSpores, Smash, VulnSpores, Smash, FrailSpores);
            var f = Lone("Flyconid", 48, seed);
            c.Equal(1, f.Rng.MonsterAi.Counter, "the first move costs one draw when the monster joins");
            // The player's Vulnerable and Frail once the next turn has begun. A fresh debuff on the
            // player skips the tick of the enemy turn it lands in, so 2 covers the next two enemy turns.
            int[] vulnerable = [0, 2, 1, 0, 0, 2, 1];
            int[] frail = [2, 1, 0, 2, 1, 0, 0];
            Walk(c, f,
            [
                new Step(FrailSpores, ["attack 8", "debuff"], Hits: ["e:0/8"], Powers: ["FrailPower 2 on player"], Ai: 2),
                new Step(VulnSpores, ["debuff"], Powers: ["VulnerablePower 2 on player"], Ai: 3),
                // 11 x 1.5 = 16.5: 10 blocked, 6.5 through, 6 HP.
                new Step(Smash, ["attack 16"], Block: 10, Hits: ["e:10/6"], Ai: 4),
                // 8 x 1.5 = 12, all blocked; the Frail still lands.
                new Step(FrailSpores, ["attack 12", "debuff"], Block: 12, Hits: ["e:12/0"], Powers: ["FrailPower 2 on player"], Ai: 5),
                new Step(Smash, ["attack 11"], Block: 5, Hits: ["e:5/6"], Ai: 6),
                new Step(VulnSpores, ["debuff"], Powers: ["VulnerablePower 2 on player"], Ai: 7),
                new Step(Smash, ["attack 16"], Hits: ["e:0/16"], Ai: 8),
            ], then: FrailSpores, after: turn =>
            {
                c.Equal(vulnerable[turn - 1], f.Player.PowerAmount<VulnerablePower>(), $"after enemy turn {turn}: Vulnerable");
                c.Equal(frail[turn - 1], f.Player.PowerAmount<FrailPower>(), $"after enemy turn {turn}: Frail");
            });
            c.Equal(999 - 8 - 6 - 6 - 16, f.Player.Hp, "HP lost over seven turns");
            c.Equal(48, f.Enemy(0).Hp, "it never hurts itself");
            c.Equal(0, f.Enemy(0).Powers.Count, "it gains no power");
        });

        c.Group("Flyconid: Smash, Vulnerable Spores, Frail Spores, Smash, then Vulnerable Spores again with all three moves shut", () =>
        {
            var seed = FindSeed("Flyconid", Smash, VulnSpores, FrailSpores, Smash, VulnSpores, Smash, FrailSpores, Smash);
            var f = Lone("Flyconid", 47, seed);
            int[] vulnerable = [0, 2, 1, 0, 2, 1, 0];
            int[] frail = [0, 0, 2, 1, 0, 0, 2];
            Walk(c, f,
            [
                new Step(Smash, ["attack 11"], Hits: ["e:0/11"], Ai: 2),
                new Step(VulnSpores, ["debuff"], Powers: ["VulnerablePower 2 on player"], Ai: 3),
                new Step(FrailSpores, ["attack 12", "debuff"], Hits: ["e:0/12"], Powers: ["FrailPower 2 on player"], Ai: 4),
                // 16.5: 3 blocked, 13.5 through, 13 HP.
                new Step(Smash, ["attack 16"], Block: 3, Hits: ["e:3/13"], Ai: 5),
                // The log ends Vulnerable Spores, Frail Spores, Smash: every weight is 0, the first listed move is taken, and a number is still drawn.
                new Step(VulnSpores, ["debuff"], Powers: ["VulnerablePower 2 on player"], Ai: 6),
                new Step(Smash, ["attack 16"], Block: 20, Hits: ["e:16/0"], Ai: 7),
                new Step(FrailSpores, ["attack 12", "debuff"], Hits: ["e:0/12"], Powers: ["FrailPower 2 on player"], Ai: 8),
            ], then: Smash, after: turn =>
            {
                c.Equal(vulnerable[turn - 1], f.Player.PowerAmount<VulnerablePower>(), $"after enemy turn {turn}: Vulnerable");
                c.Equal(frail[turn - 1], f.Player.PowerAmount<FrailPower>(), $"after enemy turn {turn}: Frail");
            });
            c.Equal(999 - 11 - 12 - 13 - 12, f.Player.Hp, "HP lost over seven turns");
        });

        c.Group("Flyconid: the first move is Frail Spores or Smash, an even pick, never Vulnerable Spores (400 seeds)", () =>
        {
            var frailFirst = 0;
            for (var seed = 1; seed <= 400; seed++)
            {
                var f = Lone("Flyconid", 48, seed);
                var first = f.Enemy(0).Monster!.NextMove!.Id;
                c.That(first is FrailSpores or Smash, $"seed {seed}: first move {first}");
                c.Equal(Predict("Flyconid", seed, 1)[0], first, $"seed {seed}: the first move follows the first MonsterAi number");
                c.Equal(1, f.Rng.MonsterAi.Counter, $"seed {seed}: one draw");
                string[] intents = first == Smash ? ["attack 11"] : ["attack 8", "debuff"];
                c.Sequence(intents, f.IntentTexts(f.Enemy(0)), $"seed {seed}: the first intent");
                if (first == FrailSpores) frailFirst++;
            }
            c.That(frailFirst is >= 150 and <= 250, $"Frail Spores opens {frailFirst} of 400 fights; an even pick is expected");
        });

        c.Group("Flyconid: which move may follow which, over 400 seeds", () =>
        {
            // Recent moves, oldest first, to the moves that may follow (V = Vulnerable Spores, F =
            // Frail Spores, S = Smash). V is shut while among the last 3, F while among the last 2,
            // S right after itself; with all shut (after V, F, S) the first listed, V, is taken.
            var allowed = new Dictionary<string, string>
            {
                ["F"] = "VS", ["S"] = "VF",
                ["FS"] = "V", ["FV"] = "S", ["SF"] = "VS", ["SV"] = "FS",
                ["FSV"] = "FS", ["FVS"] = "F", ["SFS"] = "V", ["SFV"] = "S",
                ["SVF"] = "S", ["SVS"] = "F", ["VFS"] = "V", ["VSF"] = "S",
            };
            var seenAfter = allowed.Keys.ToDictionary(k => k, _ => new HashSet<char>());
            var counts = new Dictionary<char, int> { ['V'] = 0, ['F'] = 0, ['S'] = 0 };
            for (var seed = 1; seed <= 400; seed++)
            {
                var f = Lone("Flyconid", 48, seed);
                var monster = f.Enemy(0).Monster!;
                var predicted = Predict("Flyconid", seed, 15);
                var letters = "";
                for (var turn = 1; turn <= 15; turn++)
                {
                    var move = monster.NextMove!.Id;
                    c.Equal(predicted[turn - 1], move, $"seed {seed} turn {turn}: the move the MonsterAi stream gives by the branch rule");
                    var letter = move switch { VulnSpores => 'V', FrailSpores => 'F', _ => 'S' };
                    counts[letter]++;
                    if (turn > 1)
                    {
                        var key = letters.Length >= 3 ? letters[^3..] : letters;
                        if (allowed.TryGetValue(key, out var next))
                        {
                            c.That(next.Contains(letter), $"seed {seed} turn {turn}: {letter} after {key}");
                            seenAfter[key].Add(letter);
                        }
                        else
                        {
                            c.That(false, $"seed {seed} turn {turn}: the history {key} cannot occur");
                        }
                    }
                    letters += letter;
                    if (turn < 15) f.EndTurn();
                }
                c.Equal(15, f.Rng.MonsterAi.Counter, $"seed {seed}: one draw at the start and one per later turn");
            }
            foreach (var (key, next) in allowed)
                c.Equal(next.Length, seenAfter[key].Count, $"after {key} every allowed move ({next}) was seen");
            // Long run: Smash about 3 turns in 7, each spore move about 2 in 7.
            var total = counts.Values.Sum();
            c.That(counts['S'] * 7.0 / total is > 2.6 and < 3.4, $"Smash share {counts['S']} of {total}");
            c.That(counts['V'] * 7.0 / total is > 1.6 and < 2.4, $"Vulnerable Spores share {counts['V']} of {total}");
            c.That(counts['F'] * 7.0 / total is > 1.6 and < 2.4, $"Frail Spores share {counts['F']} of {total}");
        });

        c.Group("Flyconid: 2 Vulnerable from the spores raises its hits on the next two enemy turns and no longer", () =>
        {
            var f = Lone("Flyconid", 48);
            var fly = f.Enemy(0);
            fly.Monster!.ForceNextMove(VulnSpores);
            f.EndTurn();
            c.Equal(2, f.Player.PowerAmount<VulnerablePower>(), "2 Vulnerable, the first tick skipped");
            c.That(ReferenceEquals(fly, f.Player.GetPower<VulnerablePower>()!.Applier), "the Flyconid is its applier");
            c.Equal(999, f.Player.Hp, "no damage from the spores");

            int[] left = [1, 0, 0];
            string[] intent = ["attack 16", "attack 16", "attack 11"];
            int[] loss = [16, 16, 11];
            for (var i = 0; i < 3; i++)
            {
                fly.Monster.ForceNextMove(Smash);
                c.Sequence([intent[i]], f.IntentTexts(fly), $"Smash {i + 1}: intent");
                var hp = f.Player.Hp;
                f.EndTurn();
                c.Equal(loss[i], hp - f.Player.Hp, $"Smash {i + 1}: HP lost");
                c.Equal(left[i], f.Player.PowerAmount<VulnerablePower>(), $"Smash {i + 1}: Vulnerable left");
            }
        });

        c.Group("Flyconid: Frail Spores hits first; its 2 Frail cuts the player's Block on the next two turns", () =>
        {
            var f = Lone("Flyconid", 48);
            var fly = f.Enemy(0);
            fly.Monster!.ForceNextMove(FrailSpores);
            f.Put("DefendIronclad", PileType.Hand);
            f.Play("Defend");
            c.Equal(5, f.Player.Block, "before the spores a Defend gives 5");
            var mark = f.History.EntryCount;
            f.EndTurn();
            var kinds = Since(f, mark).Where(e => e.Kind is HistoryKind.DamageReceived or HistoryKind.PowerReceived).Select(e => e.Kind).ToList();
            c.Sequence([HistoryKind.DamageReceived, HistoryKind.PowerReceived], kinds, "the hit, then the Frail");
            c.Equal(999 - 3, f.Player.Hp, "8 against 5 Block: the Block of that turn was not cut");
            c.Equal(2, f.Player.PowerAmount<FrailPower>(), "2 Frail");
            c.That(ReferenceEquals(fly, f.Player.GetPower<FrailPower>()!.Applier), "the Flyconid is its applier");

            int[] block = [3, 3, 5];     // 5 x 0.75 = 3.75, so 3; the third turn is free again
            int[] left = [1, 0, 0];
            for (var i = 0; i < 3; i++)
            {
                fly.Monster.ForceNextMove(Smash);
                f.Put("DefendIronclad", PileType.Hand);
                f.Play("Defend");
                c.Equal(block[i], f.Player.Block, $"turn {i + 2}: Block from one Defend");
                var hp = f.Player.Hp;
                f.EndTurn();
                c.Equal(11 - block[i], hp - f.Player.Hp, $"turn {i + 2}: Smash through that Block");
                c.Equal(left[i], f.Player.PowerAmount<FrailPower>(), $"turn {i + 2}: Frail left");
            }
        });

        c.Group("Flyconid dying in the middle of Frail Spores still applies the Frail while the fight goes on", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy("Flyconid", 48).Enemy(new CheckDummy()).Start();
            var fly = f.Enemy(0);
            f.ApplyPower<Spikes>(f.Player, 99, f.Player);
            fly.Monster!.ForceNextMove(FrailSpores);
            f.EndTurn();
            c.That(fly.IsDead && !f.Enemies.Contains(fly), "the Flyconid died to the spikes and left the fight after its move");
            c.Equal(72, f.Player.Hp, "its hit of 8 still landed");
            c.Equal(2, f.Player.PowerAmount<FrailPower>(), "and so did the 2 Frail that follow the hit");
            c.That(!f.IsOver, "the other enemy keeps the fight going");
        });
    }

    /// <summary>Check-only: when its owner is hit by a powered attack, the attacker takes the amount first.</summary>
    private sealed class Spikes : Power
    {
        public override string Name => "Spikes";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "Check-only: attackers take damage.";

        public override void BeforeDamageReceived(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner) && dealer is not null && !ReferenceEquals(dealer, Owner) && props.IsPoweredAttack())
                f.Damage(dealer, Amount, Props.NonCardUnpowered, Owner);
        }
    }

    // ---------- Snapping Jaxfruit ----------

    private static void JaxfruitChecks(Check c)
    {
        c.Group("Snapping Jaxfruit: Energy Orb every turn; the hit comes before the 2 Strength, so 3, 5, 7, 9, 11, 13, 15", () =>
        {
            var f = Lone("SnappingJaxfruit", 32, seed: 7);
            Walk(c, f,
            [
                new Step(Orb, ["attack 3", "buff"], Hits: ["e:0/3"], Powers: ["StrengthPower 2 on self"]),
                new Step(Orb, ["attack 5", "buff"], Block: 2, Hits: ["e:2/3"], Powers: ["StrengthPower 2 on self"]),
                new Step(Orb, ["attack 7", "buff"], Block: 7, Hits: ["e:7/0"], Powers: ["StrengthPower 2 on self"]),
                new Step(Orb, ["attack 9", "buff"], Hits: ["e:0/9"], Powers: ["StrengthPower 2 on self"]),
                new Step(Orb, ["attack 11", "buff"], Block: 20, Hits: ["e:11/0"], Powers: ["StrengthPower 2 on self"]),
                new Step(Orb, ["attack 13", "buff"], Hits: ["e:0/13"], Powers: ["StrengthPower 2 on self"]),
                new Step(Orb, ["attack 15", "buff"], Block: 1, Hits: ["e:1/14"], Powers: ["StrengthPower 2 on self"]),
            ], then: Orb, after: turn => c.Equal(2 * turn, f.Enemy(0).PowerAmount<StrengthPower>(), $"after enemy turn {turn}: Strength"));
            c.Equal(999 - 3 - 3 - 9 - 13 - 14, f.Player.Hp, "HP lost over seven turns");
            c.Sequence(["attack 17", "buff"], f.IntentTexts(f.Enemy(0)), "turn 8 shows 3 + 14");
            c.Equal(0, CountSlimed(f), "it makes no cards");
        });

        c.Group("Snapping Jaxfruit: the hit is dealt before the Strength is gained", () =>
        {
            var f = Lone("SnappingJaxfruit", 32);
            var mark = f.History.EntryCount;
            f.EndTurn();
            var kinds = Since(f, mark).Where(e => e.Kind is HistoryKind.DamageReceived or HistoryKind.PowerReceived).Select(e => e.Kind).ToList();
            c.Sequence([HistoryKind.DamageReceived, HistoryKind.PowerReceived], kinds, "damage, then Strength");
            c.Equal(996, f.Player.Hp, "the first hit is 3, not 5");
            var strength = f.Enemy(0).GetPower<StrengthPower>();
            c.That(strength is not null && ReferenceEquals(strength.Applier, f.Enemy(0)), "it applies the Strength to itself");
        });

        c.Group("Snapping Jaxfruit with 4 Strength against a Vulnerable player: (3 + 4) x 1.5 = 10.5, so 10", () =>
        {
            var f = Lone("SnappingJaxfruit", 32);
            var jax = f.Enemy(0);
            f.EndTurn();
            f.EndTurn();
            c.Equal(4, jax.PowerAmount<StrengthPower>(), "4 Strength after two Energy Orbs");
            c.Sequence(["attack 7", "buff"], f.IntentTexts(jax), "3 + 4 before Vulnerable");
            f.ApplyPower<VulnerablePower>(f.Player, 2, jax);
            c.Sequence(["attack 10", "buff"], f.IntentTexts(jax), "the intent with Vulnerable");
            c.Equal(10, f.IncomingDamage(), "incoming damage");
            var hp = f.Player.Hp;
            f.SetBlock(f.Player, 4);
            f.EndTurn();
            c.Equal(6, hp - f.Player.Hp, "10.5 against 4 Block: 6.5 through, 6 HP");
        });
    }

    // ---------- Slithering Strangler ----------

    private static void StranglerChecks(Check c)
    {
        c.Group("Slithering Strangler: Constrict, Thwack, Constrict, Lash, Constrict, Thwack, Constrict", () =>
        {
            var seed = FindSeed("SlitheringStrangler", Constrict, Thwack, Constrict, Lash, Constrict, Thwack, Constrict, Lash);
            var f = Lone("SlitheringStrangler", 54, seed);
            c.Equal(0, f.Rng.MonsterAi.Counter, "the fixed first move costs no draw");
            int[] constrict = [3, 3, 6, 6, 9, 9, 12];
            int[] block = [0, 5, 0, 0, 0, 5, 0];    // its Block during the player's next turn
            Walk(c, f,
            [
                new Step(Constrict, ["debuff"], Powers: ["ConstrictPower 3 on player"], Ai: 1),
                // Constrict bites at the end of the player's turn, before the Strangler acts.
                new Step(Thwack, ["attack 7", "defend"], Hits: ["self:0/3", "e:0/7"], Guard: [5], Ai: 1),
                new Step(Constrict, ["debuff"], Hits: ["self:0/3"], Powers: ["ConstrictPower 3 on player"], Ai: 2),
                // 4 Block: Constrict's 6 takes it all and 2 HP; nothing is left for the Lash.
                new Step(Lash, ["attack 12"], Block: 4, Hits: ["self:4/2", "e:0/12"], Ai: 2),
                new Step(Constrict, ["debuff"], Hits: ["self:0/6"], Powers: ["ConstrictPower 3 on player"], Ai: 3),
                // 20 Block: 9 to Constrict, 7 to the Thwack.
                new Step(Thwack, ["attack 7", "defend"], Block: 20, Hits: ["self:9/0", "e:7/0"], Guard: [5], Ai: 3),
                new Step(Constrict, ["debuff"], Hits: ["self:0/9"], Powers: ["ConstrictPower 3 on player"], Ai: 4),
            ], then: Lash, after: turn =>
            {
                c.Equal(constrict[turn - 1], f.Player.PowerAmount<ConstrictPower>(), $"after enemy turn {turn}: Constrict");
                c.Equal(1, f.Player.Powers.Count(p => p is ConstrictPower), $"after enemy turn {turn}: one Constrict instance");
                c.Equal(block[turn - 1], f.Enemy(0).Block, $"after enemy turn {turn}: the Strangler's Block");
            });
            c.Equal(999 - 10 - 3 - 14 - 6 - 0 - 9, f.Player.Hp, "HP lost over seven turns");
            c.Equal(54, f.Enemy(0).Hp, "it never hurts itself");
        });

        c.Group("Slithering Strangler: Constrict on every odd turn, an even pick with no memory on every even turn (300 seeds)", () =>
        {
            var pairs = new HashSet<string>();
            var thwacks = 0;
            var picks = 0;
            for (var seed = 1; seed <= 300; seed++)
            {
                var f = Lone("SlitheringStrangler", 54, seed);
                var monster = f.Enemy(0).Monster!;
                var predicted = Predict("SlitheringStrangler", seed, 10);
                var last = "";
                for (var turn = 1; turn <= 10; turn++)
                {
                    var move = monster.NextMove!.Id;
                    c.Equal(predicted[turn - 1], move, $"seed {seed} turn {turn}: the move the MonsterAi stream gives by the branch rule");
                    if (turn % 2 == 1)
                    {
                        c.Equal(Constrict, move, $"seed {seed} turn {turn}: an odd turn");
                    }
                    else
                    {
                        c.That(move is Thwack or Lash, $"seed {seed} turn {turn}: {move} on an even turn");
                        picks++;
                        if (move == Thwack) thwacks++;
                        if (last.Length > 0) pairs.Add($"{last}>{move}");
                        last = move;
                    }
                    // One draw at the start of each player turn that follows a Constrict.
                    c.Equal(turn / 2, f.Rng.MonsterAi.Counter, $"seed {seed} turn {turn}: MonsterAi draws so far");
                    if (turn < 10) f.EndTurn();
                }
            }
            c.Equal(4, pairs.Count, "Thwack and Lash each follow themselves and each other");
            c.That(thwacks * 2 > picks * 0.9 && thwacks * 2 < picks * 1.1, $"Thwack was picked {thwacks} of {picks} times; an even pick is expected");
        });

        c.Group("Slithering Strangler: Thwack hits, then gives 5 Block that lasts through the player's turn", () =>
        {
            var f = Lone("SlitheringStrangler", 54);
            var strangler = f.Enemy(0);
            strangler.Monster!.ForceNextMove(Thwack);
            c.Sequence(["attack 7", "defend"], f.IntentTexts(strangler), "intent");
            var mark = f.History.EntryCount;
            f.EndTurn();
            var kinds = Since(f, mark).Where(e => e.Kind is HistoryKind.DamageReceived or HistoryKind.BlockGained).Select(e => e.Kind).ToList();
            c.Sequence([HistoryKind.DamageReceived, HistoryKind.BlockGained], kinds, "the hit, then the Block");
            c.Equal(992, f.Player.Hp, "7 damage");
            c.Equal(5, strangler.Block, "5 Block during the player's next turn");
            c.Equal(5, f.Snapshot().State["enemies"]![0]!["block"]!.GetValue<int>(), "the snapshot shows it");

            var untouched = f.Clone();
            f.Play("Strike", 0);
            c.Equal(0, strangler.Block, "a Strike of 6 takes the 5 Block");
            c.Equal(53, strangler.Hp, "and 1 HP");

            // Left alone, the Block falls when the enemies' next turn starts; the move after Thwack is Constrict, which gives none.
            c.Equal(Constrict, untouched.Enemy(0).Monster!.NextMove?.Id, "Constrict follows Thwack");
            untouched.EndTurn();
            c.Equal(0, untouched.Enemy(0).Block, "no Block left a turn later");
            c.Equal(54, untouched.Enemy(0).Hp, "nothing else changed its HP");
        });

        c.Group("Slithering Strangler: the Block of Thwack is move Block, so its own Frail cuts it to 3", () =>
        {
            var f = Lone("SlitheringStrangler", 54);
            var strangler = f.Enemy(0);
            f.ApplyPower<FrailPower>(strangler, 2, f.Player);
            strangler.Monster!.ForceNextMove(Thwack);
            f.EndTurn();
            c.Equal(3, strangler.Block, "5 x 0.75 = 3.75, so 3");
        });
    }

    // ---------- Constrict ----------

    /// <summary>Check-only: prevents its owner's death once, heals it by 1 and goes.</summary>
    private sealed class Undying : Power
    {
        public override string Name => "Undying";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "Check-only: survives one death.";

        public override bool ShouldDie(Fight f, Creature creature) => !ReferenceEquals(creature, Owner);

        public override void AfterPreventingDeath(Fight f, Creature creature)
        {
            f.Heal(Owner, 1);
            f.RemovePower(this);
        }
    }

    private static void ConstrictChecks(Check c)
    {
        c.Group("Constrict: its amount as damage at the end of its owner's turn, never modified, absorbed by Block, never counting down", () =>
        {
            var f = CheckKit.VsDummy();
            var dummy = f.Enemy(0);
            c.That(f.ApplyPower<ConstrictPower>(f.Player, 3, dummy) is not null, "applied");
            c.Equal(3, f.Player.PowerAmount<ConstrictPower>(), "3 Constrict");

            var mark = f.History.EntryCount;
            f.EndTurn();
            c.Equal(77, f.Player.Hp, "3 damage at the end of the player's turn");
            var hit = Since(f, mark).Single(e => e.Kind == HistoryKind.DamageReceived);
            c.Equal(Side.Player, hit.Side, "it lands while the side is still the player's");
            c.Equal(1, hit.Turn, "in the turn that was ended");
            c.Equal(f.Player.Id, hit.ActorId, "the owner is hit");
            c.Equal(f.Player.Id, hit.OtherId, "and the owner is the dealer");
            c.Equal(Props.NonCardUnpowered, hit.Props, "unpowered and blockable");
            c.Equal(3, f.Player.PowerAmount<ConstrictPower>(), "it does not count down");

            // The owner is also the dealer: its Strength and Weak would count for a powered hit, and so would its Vulnerable.
            f.ApplyPower<StrengthPower>(f.Player, 4, f.Player);
            f.ApplyPower<WeakPower>(f.Player, 5, dummy);
            f.ApplyPower<VulnerablePower>(f.Player, 5, dummy);
            f.EndTurn();
            c.Equal(74, f.Player.Hp, "still exactly 3 with Strength, Weak and Vulnerable on the owner");

            f.SetBlock(f.Player, 2);
            mark = f.History.EntryCount;
            f.EndTurn();
            c.Equal(73, f.Player.Hp, "2 Block absorbs 2 of the 3");
            hit = Since(f, mark).Single(e => e.Kind == HistoryKind.DamageReceived);
            c.Equal(2, hit.Blocked, "2 blocked");
            c.Equal(1, hit.Amount, "1 through");

            f.SetBlock(f.Player, 10);
            f.EndTurn();
            c.Equal(73, f.Player.Hp, "10 Block absorbs all of it");
            c.Equal(3, f.Player.PowerAmount<ConstrictPower>(), "still 3 after four turns");
        });

        c.Group("Constrict: nothing happens at the end of the enemy turn it lands in", () =>
        {
            var f = Lone("SlitheringStrangler", 54, playerHp: 80);
            var mark = f.History.EntryCount;
            f.EndTurn();
            c.Equal(3, f.Player.PowerAmount<ConstrictPower>(), "3 Constrict from the Strangler's first move");
            c.Equal(80, f.Player.Hp, "no damage yet");
            c.That(Since(f, mark).All(e => e.Kind != HistoryKind.DamageReceived), "no damage instance in that round");
            c.That(ReferenceEquals(f.Enemy(0), f.Player.GetPower<ConstrictPower>()!.Applier), "the Strangler is its applier");

            var power = f.Snapshot().State["me"]!["powers"]![0]!;
            c.Equal("Constrict", power["name"]!.GetValue<string>(), "snapshot: name");
            c.Equal(3, power["amount"]!.GetValue<int>(), "snapshot: amount");
            c.Equal("Debuff", power["kind"]!.GetValue<string>(), "snapshot: kind");
            c.Equal("At the end of its turn, takes 3 damage (Block absorbs it). Does not wear off; ends when Slithering Strangler dies.",
                power["text"]!.GetValue<string>(), "snapshot: text");
        });

        c.Group("Constrict: applying it again adds to the one instance, and the first applier stays its applier", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { DisplayName = "First" }, new CheckDummy { DisplayName = "Second" }, new CheckDummy { DisplayName = "Bystander" });
            var first = f.Enemy(0);
            var second = f.Enemy(1);
            f.ApplyPower<ConstrictPower>(f.Player, 3, first);
            f.ApplyPower<ConstrictPower>(f.Player, 3, second);
            c.Equal(1, f.Player.Powers.Count(p => p is ConstrictPower), "one instance");
            c.Equal(6, f.Player.PowerAmount<ConstrictPower>(), "3 + 3");
            c.That(ReferenceEquals(first, f.Player.GetPower<ConstrictPower>()!.Applier), "the first applier");
            f.EndTurn();
            c.Equal(74, f.Player.Hp, "6 damage at the end of the turn");

            f.Kill([second]);
            c.Equal(6, f.Player.PowerAmount<ConstrictPower>(), "the death of a later applier does not end it");
            f.Kill([f.Enemy(2)]);
            c.Equal(6, f.Player.PowerAmount<ConstrictPower>(), "nor does the death of a bystander");
            f.EndTurn();
            c.Equal(68, f.Player.Hp, "6 more");
        });

        c.Group("Constrict: it ends when its applier dies, not when that death is prevented", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { DisplayName = "Applier" }, new CheckDummy { DisplayName = "Bystander" });
            var applier = f.Enemy(0);
            f.ApplyPower<Undying>(applier, 1, applier);
            f.ApplyPower<ConstrictPower>(f.Player, 3, applier);

            f.Kill([applier]);
            c.That(applier.IsAlive, "the first death was prevented");
            c.Equal(3, f.Player.PowerAmount<ConstrictPower>(), "a prevented death leaves Constrict on");
            f.EndTurn();
            c.Equal(77, f.Player.Hp, "and it still bites");

            f.Kill([applier]);
            c.That(applier.IsDead, "the second death is real");
            c.That(!f.Player.HasPower<ConstrictPower>(), "Constrict is gone");
            f.EndTurn();
            c.Equal(77, f.Player.Hp, "no more damage");
        });

        c.Group("Constrict from the Strangler: killing the Strangler ends it with company alive; killing the company does not", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy("LeafSlimeM", 34).Enemy("SlitheringStrangler", 54).Start();
            f.EndTurn();
            c.Equal(3, f.Player.PowerAmount<ConstrictPower>(), "3 Constrict after the first enemy turn");
            c.Equal(80, f.Player.Hp, "no damage in round 1 (the slime made Slimed)");

            // In a copy the slime dies: Constrict stays and bites before the Strangler acts.
            var slimeDead = f.Clone();
            slimeDead.Kill([slimeDead.Enemy(0)]);
            c.Equal(3, slimeDead.Player.PowerAmount<ConstrictPower>(), "the slime's death leaves Constrict on");
            var mark = slimeDead.History.EntryCount;
            slimeDead.EndTurn();
            c.Equal("self:0/3", PlayerHits(slimeDead, Since(slimeDead, mark), slimeDead.Enemy(1))[0], "3 at the end of the turn");

            // In the fight itself the Strangler dies: Constrict goes at once, the slime fights on.
            f.Kill([f.Enemy(1)]);
            c.That(!f.Player.HasPower<ConstrictPower>(), "the Strangler's death ends Constrict");
            c.That(!f.IsEnding, "the slime keeps the fight going");
            mark = f.History.EntryCount;
            f.EndTurn();
            c.Sequence(["e:0/8"], PlayerHits(f, Since(f, mark), f.Enemy(0)), "only the slime's Clump Shot lands in round 2");
            c.Equal(72, f.Player.Hp, "8 HP lost");
        });

        c.Group("Constrict can kill: the player dies at the end of their own turn and the fight is lost", () =>
        {
            var f = Lone("SlitheringStrangler", 54, playerHp: 4);
            f.Enemy(0).Monster!.ForceNextMove(Constrict);
            f.EndTurn();
            c.Equal(4, f.Player.Hp, "Constrict lands without damage");
            c.That(!f.IsOver, "the fight goes on");

            // 3 against 2 Block leaves 3 HP; the Strangler is made to Constrict again so that nothing else hurts.
            f.Enemy(0).Monster!.ForceNextMove(Constrict);
            f.SetBlock(f.Player, 2);
            f.EndTurn();
            c.Equal(3, f.Player.Hp, "1 HP lost through 2 Block");
            c.Equal(6, f.Player.PowerAmount<ConstrictPower>(), "6 Constrict now");

            f.EndTurn();
            c.Equal(0, f.Player.Hp, "6 damage against 3 HP");
            c.That(f.IsOver && !f.Won, "the fight is lost");
        });

        c.Group("Constrict: its damage is HP lost in its owner's own turn (Rupture answers it), unless Block took all of it", () =>
        {
            // The game deals it in the hook that runs after the owner's side has ended its turn and
            // before the sides switch; Rupture asks for unblocked damage while it is its owner's side's turn.
            var f = Lone("SlitheringStrangler", 54);
            f.ApplyPower("RupturePower", f.Player, 1, f.Player);
            f.EndTurn();
            c.Equal(3, f.Player.PowerAmount<ConstrictPower>(), "3 Constrict after round 1");
            c.Equal(0, f.Player.PowerAmount<StrengthPower>(), "nothing happened in the enemy turn it landed in");

            // The Strangler is made to Constrict every turn, so that only Constrict hurts.
            f.Enemy(0).Monster!.ForceNextMove(Constrict);
            c.Equal(3, Lost(f, () => f.EndTurn()), "turn 2: 3 at the end of the player's turn");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "Rupture: 1 Strength for it");
            c.Equal(6, f.Player.PowerAmount<ConstrictPower>(), "6 Constrict now");

            f.Enemy(0).Monster!.ForceNextMove(Constrict);
            f.SetBlock(f.Player, 6);
            c.Equal(0, Lost(f, () => f.EndTurn()), "turn 3: 6 Block takes all 6");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "no HP lost, no Strength");

            f.Enemy(0).Monster!.ForceNextMove(Constrict);
            f.SetBlock(f.Player, 4);
            c.Equal(5, Lost(f, () => f.EndTurn()), "turn 4: 9 against 4 Block");
            c.Equal(2, f.Player.PowerAmount<StrengthPower>(), "Rupture again");
        });
    }

    // ---------- every attack and every Block gain is a powered move ----------

    private static void PoweredMoves(Check c)
    {
        // Class, HP, move, base damage. Expected: floor((base + 3) x 1.5) with 3 Strength against
        // a Vulnerable player, and floor(base x 0.75) when the monster is Weak.
        var attacks = new (string Class, int Hp, string Move, int WithStrengthAndVulnerable, int WithWeak)[]
        {
            ("LeafSlimeM", 34, Clump, 16, 6),             // 11 x 1.5 = 16.5; 8 x 0.75 = 6
            ("LeafSlimeS", 13, Tackle, 9, 2),             // 6 x 1.5 = 9; 3 x 0.75 = 2.25
            ("TwigSlimeM", 27, Pokey, 21, 8),             // 14 x 1.5 = 21; 11 x 0.75 = 8.25
            ("TwigSlimeS", 9, Tackle, 10, 3),             // 7 x 1.5 = 10.5; 4 x 0.75 = 3
            ("Flyconid", 48, FrailSpores, 16, 6),         // 11 x 1.5 = 16.5; 8 x 0.75 = 6
            ("Flyconid", 48, Smash, 21, 8),               // 14 x 1.5 = 21; 11 x 0.75 = 8.25
            ("SnappingJaxfruit", 32, Orb, 9, 2),          // 6 x 1.5 = 9; 3 x 0.75 = 2.25
            ("SlitheringStrangler", 54, Thwack, 15, 5),   // 10 x 1.5 = 15; 7 x 0.75 = 5.25
            ("SlitheringStrangler", 54, Lash, 22, 9),     // 15 x 1.5 = 22.5; 12 x 0.75 = 9
        };

        c.Group("every attack is a powered move: the monster's Strength and Weak and the player's Vulnerable count, in the intent and in the hit", () =>
        {
            foreach (var a in attacks)
            {
                var f = Lone(a.Class, a.Hp);
                var enemy = f.Enemy(0);
                enemy.Monster!.ForceNextMove(a.Move);
                f.ApplyPower<StrengthPower>(enemy, 3, enemy);
                f.ApplyPower<VulnerablePower>(f.Player, 1, enemy);
                c.Equal($"attack {a.WithStrengthAndVulnerable}", f.IntentTexts(enemy)[0], $"{a.Class} {a.Move}: intent with 3 Strength against Vulnerable");
                var hp = f.Player.Hp;
                f.EndTurn();
                c.Equal(a.WithStrengthAndVulnerable, hp - f.Player.Hp, $"{a.Class} {a.Move}: hit with 3 Strength against Vulnerable");

                var g = Lone(a.Class, a.Hp);
                enemy = g.Enemy(0);
                enemy.Monster!.ForceNextMove(a.Move);
                g.ApplyPower<WeakPower>(enemy, 1, g.Player);
                c.Equal($"attack {a.WithWeak}", g.IntentTexts(enemy)[0], $"{a.Class} {a.Move}: intent while Weak");
                hp = g.Player.Hp;
                g.EndTurn();
                c.Equal(a.WithWeak, hp - g.Player.Hp, $"{a.Class} {a.Move}: hit while Weak");
            }
        });

        c.Group("every attack is one hit on the player, with the monster as dealer", () =>
        {
            foreach (var a in attacks)
            {
                var f = Lone(a.Class, a.Hp);
                var enemy = f.Enemy(0);
                enemy.Monster!.ForceNextMove(a.Move);
                var mark = f.History.EntryCount;
                f.EndTurn();
                var entries = Since(f, mark);
                var hits = entries.Where(e => e.Kind == HistoryKind.DamageReceived).ToList();
                c.Equal(1, hits.Count, $"{a.Class} {a.Move}: one damage instance");
                c.Equal(enemy.Id, hits[0].OtherId, $"{a.Class} {a.Move}: the monster is the dealer");
                c.Equal(Props.MonsterMove, hits[0].Props, $"{a.Class} {a.Move}: a powered, blockable move");
                c.Equal(1, entries.Single(e => e.Kind == HistoryKind.CreatureAttacked).Amount, $"{a.Class} {a.Move}: one hit");
            }
        });
    }

    // ---------- who acts first ----------

    private static void ActingOrder(Check c)
    {
        c.Group("a slime before the Flyconid: the spores' Vulnerable lands after the slime has hit", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(999, 999).Enemy("TwigSlimeM", 27).Enemy("Flyconid", 48).Start();
            var slime = f.Enemy(0);
            var fly = f.Enemy(1);
            slime.Monster!.ForceNextMove(Pokey);
            fly.Monster!.ForceNextMove(VulnSpores);
            f.EndTurn();
            c.Equal(999 - 11, f.Player.Hp, "round 1: the Pounce is 11, not 16");

            slime.Monster.ForceNextMove(Pokey);
            fly.Monster.ForceNextMove(Smash);
            c.Sequence(["attack 16"], f.IntentTexts(slime), "round 2: the slime's intent with Vulnerable");
            c.Sequence(["attack 16"], f.IntentTexts(fly), "round 2: the Flyconid's intent with Vulnerable");
            c.Equal(32, f.IncomingDamage(), "round 2: incoming damage");
            f.EndTurn();
            c.Equal(999 - 11 - 32, f.Player.Hp, "round 2: 16 from each");
        });

        c.Group("SnappingJaxfruitNormal: the Jaxfruit acts before the Flyconid; Vulnerable raises both from the next round on", () =>
        {
            var f = new FightBuilder().Hp(999, 999).Encounter("SnappingJaxfruitNormal").Seed(2).Start();
            var jax = f.Enemy(0);
            var fly = f.Enemy(1);
            c.Equal("Snapping Jaxfruit", jax.Name, "e0");
            c.Equal("Flyconid", fly.Name, "e1");

            fly.Monster!.ForceNextMove(VulnSpores);
            f.EndTurn();
            c.Equal(999 - 3, f.Player.Hp, "round 1: the Jaxfruit's 3 is not raised by the Vulnerable that follows it");

            // Round 2: (3 + 2) x 1.5 = 7.5, so 7; Smash 16.
            fly.Monster.ForceNextMove(Smash);
            c.Sequence(["attack 7", "buff"], f.IntentTexts(jax), "round 2: the Jaxfruit's intent");
            f.EndTurn();
            c.Equal(999 - 3 - 7 - 16, f.Player.Hp, "round 2");

            // Round 3: (3 + 4) x 1.5 = 10.5, so 10; Smash 16. The Vulnerable ends with this round.
            fly.Monster.ForceNextMove(Smash);
            c.Sequence(["attack 10", "buff"], f.IntentTexts(jax), "round 3: the Jaxfruit's intent");
            f.EndTurn();
            c.Equal(999 - 3 - 7 - 16 - 10 - 16, f.Player.Hp, "round 3");
            c.That(!f.Player.HasPower<VulnerablePower>(), "the Vulnerable is gone after two rounds");

            // Round 4: 3 + 6 = 9; Smash 11.
            fly.Monster.ForceNextMove(Smash);
            c.Sequence(["attack 9", "buff"], f.IntentTexts(jax), "round 4: the Jaxfruit's intent");
            f.EndTurn();
            c.Equal(999 - 3 - 7 - 16 - 10 - 16 - 9 - 11, f.Player.Hp, "round 4");
        });
    }

    // ---------- the encounters' line-ups ----------

    /// <summary>
    /// The line-up of an encounter by the game's rule, drawn from the encounter stream of this
    /// seed in the game's order. The pools are [Leaf, Twig] for both sizes.
    /// </summary>
    private static List<string> PredictLineUp(string encounter, int seed, out int draws)
    {
        var rng = RngSet.For(seed, "encounter");
        string[] smalls = ["LeafSlimeS", "TwigSlimeS"];
        string[] mediums = ["LeafSlimeM", "TwigSlimeM"];
        List<string> lineUp;
        switch (encounter)
        {
            case "SlimesWeak":
            {
                // The first small slime; a pick among the one that is left (drawn all the same); the medium slime.
                var first = rng.NextInt(0, 2);
                rng.NextInt(0, 1);
                var medium = rng.NextInt(0, 2);
                lineUp = [smalls[first], mediums[medium], smalls[1 - first]];
                break;
            }
            case "SlimesNormal":
            {
                // One coin flip: true (a draw below 2 that gives 0) puts the Leaf slime before the Twig slime.
                var leafFirst = rng.NextInt(2) == 0;
                lineUp = ["TwigSlimeM", "LeafSlimeM", leafFirst ? "LeafSlimeS" : "TwigSlimeS", leafFirst ? "TwigSlimeS" : "LeafSlimeS"];
                break;
            }
            case "FlyconidNormal":
                lineUp = [mediums[rng.NextInt(0, 2)], "Flyconid"];
                break;
            case "SnappingJaxfruitNormal":
                lineUp = ["SnappingJaxfruit", "Flyconid"];
                break;
            case "SlitheringStranglerNormal":
                lineUp = rng.NextInt(0, 3) switch
                {
                    0 => ["SnappingJaxfruit"],
                    1 => [mediums[rng.NextInt(0, 2)]],
                    _ => [smalls[rng.NextInt(0, 2)], smalls[rng.NextInt(0, 2)]],
                };
                lineUp.Add("SlitheringStrangler");
                break;
            default:
                throw new ArgumentException(encounter);
        }
        draws = rng.Counter;
        return lineUp;
    }

    private static void Compositions(Check c)
    {
        const int Seeds = 400;

        foreach (var encounter in EncounterClasses)
        {
            c.Group($"{encounter}: line-up, order, HP and the streams used, over {Seeds} seeds", () =>
            {
                var variants = new Dictionary<string, int>();
                var crowded = 0;    // fights in which a monster's range held a value that an earlier monster had taken
                for (var seed = 1; seed <= Seeds; seed++)
                {
                    var f = new FightBuilder().Encounter(encounter).Seed(seed).Start();
                    var at = $"seed {seed}";
                    var lineUp = f.Roster.Select(e => e.Monster!.ClassName).ToList();
                    var expected = PredictLineUp(encounter, seed, out var draws);
                    c.Sequence(expected, lineUp, $"{at}: the line-up, in e-index order");
                    c.Sequence(lineUp, f.Enemies.Select(e => e.Monster!.ClassName), $"{at}: the acting order is the line-up order");
                    c.Equal(draws, f.Rng.Encounter.Counter, $"{at}: draws on the encounter stream");
                    c.Equal(lineUp.Count, f.Rng.Niche.Counter, $"{at}: one Niche draw per monster");
                    c.Equal(lineUp.Count(cls => cls is "LeafSlimeS" or "Flyconid"), f.Rng.MonsterAi.Counter,
                        $"{at}: one MonsterAi draw per monster whose first move is random");
                    c.That(f.Roster.All(e => e.Slot is null), $"{at}: no slots");

                    var hp = f.Roster.Select(e => e.MaxHp).ToList();
                    var taken = false;
                    for (var j = 0; j < lineUp.Count; j++)
                    {
                        var (name, min, max) = Facts(lineUp[j]);
                        c.Equal(name, f.Enemy(j).Name, $"{at} e{j}: name");
                        c.That(hp[j] >= min && hp[j] <= max, $"{at} e{j}: {lineUp[j]} HP {hp[j]} outside {min}..{max}");
                        c.Equal(hp[j], f.Enemy(j).Hp, $"{at} e{j}: starts at full HP");
                        taken |= hp.Take(j).Any(v => v >= min && v <= max);
                    }
                    if (taken) crowded++;
                    c.Equal(hp.Count, hp.Distinct().Count(), $"{at}: no two enemies start with the same max HP ({string.Join(", ", hp)})");
                    c.Sequence(PredictHp(lineUp, seed), hp, $"{at}: HP rolled in line-up order, each avoiding the values already taken");

                    var key = string.Join("+", lineUp);
                    variants[key] = variants.GetValueOrDefault(key) + 1;
                }

                // Leaf Slime (S) and Twig Slime (S) share the value 11, and the Strangler's two small slimes may be one kind.
                if (encounter is "SlimesWeak" or "SlimesNormal" or "SlitheringStranglerNormal")
                    c.That(crowded > 0, "the no-duplicate rule had a value to avoid in some fights");
                else
                    c.Equal(0, crowded, "the ranges of this line-up never overlap");

                int Fights(Func<string, bool> match) => variants.Where(v => match(v.Key)).Sum(v => v.Value);
                switch (encounter)
                {
                    case "SlimesWeak":
                        c.Equal(4, variants.Count, "four line-ups: either small slime first, either medium slime in the middle");
                        c.That(variants.Keys.All(k => k.Split('+').Length == 3), "always three enemies");
                        c.That(variants.Keys.All(k => k.Split('+')[1] is "LeafSlimeM" or "TwigSlimeM"), "the medium slime is in the middle");
                        c.That(variants.Keys.All(k => k.Contains("LeafSlimeS") && k.Contains("TwigSlimeS")), "both small slimes are always there");
                        c.That(Fights(k => k.StartsWith("LeafSlimeS", StringComparison.Ordinal)) is >= 150 and <= 250, "Leaf Slime (S) stands first about half the time");
                        c.That(Fights(k => k.Contains("LeafSlimeM")) is >= 150 and <= 250, "the medium slime is a Leaf about half the time");
                        break;
                    case "SlimesNormal":
                        c.Equal(2, variants.Count, "two line-ups: the small slimes in either order");
                        c.That(variants.Keys.All(k => k.StartsWith("TwigSlimeM+LeafSlimeM+", StringComparison.Ordinal)), "Twig Slime (M), then Leaf Slime (M), come first");
                        c.That(Fights(k => k.EndsWith("LeafSlimeS+TwigSlimeS", StringComparison.Ordinal)) is >= 150 and <= 250, "Leaf before Twig about half the time");
                        break;
                    case "FlyconidNormal":
                        c.Equal(2, variants.Count, "two line-ups: either medium slime");
                        c.That(variants.Keys.All(k => k.EndsWith("+Flyconid", StringComparison.Ordinal) && k.Split('+').Length == 2), "a slime, then the Flyconid");
                        c.That(Fights(k => k.StartsWith("LeafSlimeM", StringComparison.Ordinal)) is >= 150 and <= 250, "a Leaf slime about half the time");
                        break;
                    case "SnappingJaxfruitNormal":
                        c.Equal(1, variants.Count, "one line-up");
                        break;
                    case "SlitheringStranglerNormal":
                        c.Equal(7, variants.Count, "seven line-ups: the Jaxfruit, two medium slimes, four pairs of small slimes");
                        c.That(variants.Keys.All(k => k.EndsWith("+SlitheringStrangler", StringComparison.Ordinal)), "the Strangler is always last");
                        c.That(Fights(k => k.StartsWith("SnappingJaxfruit", StringComparison.Ordinal)) is >= 86 and <= 180, "the Jaxfruit about a third of the time");
                        c.That(Fights(k => k.Split('+').Length == 2 && k.Contains("SlimeM")) is >= 86 and <= 180, "one medium slime about a third of the time");
                        c.That(Fights(k => k.Split('+').Length == 3) is >= 86 and <= 180, "two small slimes about a third of the time");
                        foreach (var pair in new[] { "LeafSlimeS+LeafSlimeS", "LeafSlimeS+TwigSlimeS", "TwigSlimeS+LeafSlimeS", "TwigSlimeS+TwigSlimeS" })
                            c.That(variants.ContainsKey($"{pair}+SlitheringStrangler"), $"the pair {pair} occurs");
                        break;
                }
            });
        }

        c.Group("first intents of a line-up, as the snapshot shows them", () =>
        {
            var f = new FightBuilder().Encounter("SlimesNormal").Seed(1).Start();
            var enemies = f.Snapshot().State["enemies"]!.AsArray();
            c.Equal(4, enemies.Count, "four enemies");
            c.Equal("Twig Slime (M)", enemies[0]!["name"]!.GetValue<string>(), "e0");
            c.Equal("Leaf Slime (M)", enemies[1]!["name"]!.GetValue<string>(), "e1");
            c.Sequence(["statuscard: 1"], enemies[0]!["intents"]!.AsArray().Select(n => n!.GetValue<string>()), "Twig Slime (M) opens with Sticky Shot");
            c.Sequence(["statuscard: 2"], enemies[1]!["intents"]!.AsArray().Select(n => n!.GetValue<string>()), "Leaf Slime (M) opens with Sticky Shot");
            for (var j = 2; j < 4; j++)
            {
                var name = enemies[j]!["name"]!.GetValue<string>();
                var intents = enemies[j]!["intents"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
                if (name == "Twig Slime (S)") c.Sequence(["attack 4"], intents, "Twig Slime (S) opens with Tackle");
                else c.That(intents is ["attack 3"] or ["statuscard: 1"], $"Leaf Slime (S) opens with Tackle or Goop, not {string.Join(", ", intents)}");
            }

            var strangler = new FightBuilder().Encounter("SlitheringStranglerNormal").Seed(1).Start();
            c.Sequence(["debuff"], strangler.IntentTexts(strangler.Roster[^1]), "the Strangler opens with Constrict");
        });
    }

    // ---------- whole encounters against a by-hand model ----------

    /// <summary>One monster of the by-hand model: its class, the moves it chose so far, its Strength and Block.</summary>
    private sealed class Foe(string className)
    {
        public string Class { get; } = className;
        public List<string> Log { get; } = new();
        public int Strength { get; set; }
        public int Block { get; set; }
    }

    /// <summary>
    /// A fight worked out by hand for a player who has no Block and only ends turns: what each
    /// monster intends and does, in line-up order, with the game's numbers and timing. It shares
    /// nothing with the engine but the random stream's generator.
    /// </summary>
    private sealed class HandModel
    {
        private readonly GameRng _ai;
        private bool _vulnerableFresh, _frailFresh;

        public HandModel(IEnumerable<string> lineUp, int seed, int hp)
        {
            _ai = AiStream(seed);
            Hp = hp;
            // Each monster settles its first move as it joins, in line-up order.
            foreach (var cls in lineUp)
            {
                var foe = new Foe(cls);
                foe.Log.Add(Next(cls, foe.Log, _ai));
                Foes.Add(foe);
            }
        }

        public List<Foe> Foes { get; } = new();
        public int Hp { get; private set; }
        public int Vulnerable { get; private set; }
        public int Frail { get; private set; }
        public int ConstrictStacks { get; private set; }
        public int Slimed { get; private set; }
        public int AiDraws => _ai.Counter;

        /// <summary>A powered hit: Strength added, times 1.5 against Vulnerable, rounded down once.</summary>
        private int Dealt(Foe foe, int baseDamage) => Vulnerable > 0 ? (baseDamage + foe.Strength) * 3 / 2 : baseDamage + foe.Strength;

        public string[] Intents(Foe foe) => (foe.Class, foe.Log[^1]) switch
        {
            ("LeafSlimeM", StickyLeaf) => ["statuscard: 2"],
            ("LeafSlimeM", Clump) => [$"attack {Dealt(foe, 8)}"],
            ("LeafSlimeS", Tackle) => [$"attack {Dealt(foe, 3)}"],
            ("LeafSlimeS", Goop) => ["statuscard: 1"],
            ("TwigSlimeM", StickyTwig) => ["statuscard: 1"],
            ("TwigSlimeM", Pokey) => [$"attack {Dealt(foe, 11)}"],
            ("TwigSlimeS", Tackle) => [$"attack {Dealt(foe, 4)}"],
            ("Flyconid", VulnSpores) => ["debuff"],
            ("Flyconid", FrailSpores) => [$"attack {Dealt(foe, 8)}", "debuff"],
            ("Flyconid", Smash) => [$"attack {Dealt(foe, 11)}"],
            ("SnappingJaxfruit", Orb) => [$"attack {Dealt(foe, 3)}", "buff"],
            ("SlitheringStrangler", Constrict) => ["debuff"],
            ("SlitheringStrangler", Thwack) => [$"attack {Dealt(foe, 7)}", "defend"],
            ("SlitheringStrangler", Lash) => [$"attack {Dealt(foe, 12)}"],
            _ => throw new InvalidOperationException($"{foe.Class} has no move {foe.Log[^1]}"),
        };

        /// <summary>The player ends the turn: Constrict, the enemy turn, the ticks, and the next moves.</summary>
        public void EndTurn()
        {
            // End of the player's turn: Constrict straight to HP (no Block in this model).
            Hp -= ConstrictStacks;

            // The enemies' turn starts: their Block falls. Then each acts, in line-up order.
            foreach (var foe in Foes) foe.Block = 0;
            foreach (var foe in Foes)
            {
                switch (foe.Class, foe.Log[^1])
                {
                    case ("LeafSlimeM", StickyLeaf): Slimed += 2; break;
                    case ("LeafSlimeM", Clump): Hp -= Dealt(foe, 8); break;
                    case ("LeafSlimeS", Tackle): Hp -= Dealt(foe, 3); break;
                    case ("LeafSlimeS", Goop): Slimed += 1; break;
                    case ("TwigSlimeM", StickyTwig): Slimed += 1; break;
                    case ("TwigSlimeM", Pokey): Hp -= Dealt(foe, 11); break;
                    case ("TwigSlimeS", Tackle): Hp -= Dealt(foe, 4); break;
                    case ("Flyconid", VulnSpores):
                        if (Vulnerable == 0) _vulnerableFresh = true;
                        Vulnerable += 2;
                        break;
                    case ("Flyconid", FrailSpores):
                        Hp -= Dealt(foe, 8);
                        if (Frail == 0) _frailFresh = true;
                        Frail += 2;
                        break;
                    case ("Flyconid", Smash): Hp -= Dealt(foe, 11); break;
                    case ("SnappingJaxfruit", Orb):
                        Hp -= Dealt(foe, 3);
                        foe.Strength += 2;
                        break;
                    case ("SlitheringStrangler", Constrict): ConstrictStacks += 3; break;
                    case ("SlitheringStrangler", Thwack):
                        Hp -= Dealt(foe, 7);
                        foe.Block = 5;
                        break;
                    case ("SlitheringStrangler", Lash): Hp -= Dealt(foe, 12); break;
                    default: throw new InvalidOperationException($"{foe.Class} has no move {foe.Log[^1]}");
                }
            }

            // End of the enemies' turn: Vulnerable and Frail lose 1, except one that is new on the player this round.
            if (Vulnerable > 0)
            {
                if (_vulnerableFresh) _vulnerableFresh = false;
                else Vulnerable--;
            }
            if (Frail > 0)
            {
                if (_frailFresh) _frailFresh = false;
                else Frail--;
            }

            // Start of the next player turn: every monster chooses its next move, in line-up order, on the one shared stream.
            foreach (var foe in Foes) foe.Log.Add(Next(foe.Class, foe.Log, _ai));
        }
    }

    private static void EncounterWalks(Check c)
    {
        foreach (var encounter in EncounterClasses)
        {
            c.Group($"{encounter}: eight rounds against the by-hand model, 60 seeds", () =>
            {
                for (var seed = 1; seed <= 60; seed++)
                {
                    var f = new FightBuilder().Hp(999, 999).Encounter(encounter).Seed(seed).Start();
                    var model = new HandModel(f.Roster.Select(e => e.Monster!.ClassName), seed, 999);
                    for (var turn = 1; turn <= 8; turn++)
                    {
                        var at = $"seed {seed} turn {turn}";
                        var incoming = 0;
                        for (var j = 0; j < model.Foes.Count; j++)
                        {
                            var foe = model.Foes[j];
                            var enemy = f.Enemy(j);
                            c.Equal(foe.Log[^1], enemy.Monster!.NextMove?.Id, $"{at} e{j} {foe.Class}: intended move");
                            var intents = model.Intents(foe);
                            c.Sequence(intents, f.IntentTexts(enemy), $"{at} e{j} {foe.Class}: intent text");
                            incoming += IncomingOf(intents);
                            c.Equal(foe.Strength, enemy.PowerAmount<StrengthPower>(), $"{at} e{j} {foe.Class}: Strength");
                            c.Equal(foe.Block, enemy.Block, $"{at} e{j} {foe.Class}: Block");
                            c.Sequence(foe.Log, enemy.Monster.MoveLog, $"{at} e{j} {foe.Class}: move log");
                        }
                        c.Equal(incoming, f.IncomingDamage(), $"{at}: incoming damage");
                        c.Equal(model.Hp, f.Player.Hp, $"{at}: the player's HP");
                        c.Equal(model.Vulnerable, f.Player.PowerAmount<VulnerablePower>(), $"{at}: Vulnerable");
                        c.Equal(model.Frail, f.Player.PowerAmount<FrailPower>(), $"{at}: Frail");
                        c.Equal(model.ConstrictStacks, f.Player.PowerAmount<ConstrictPower>(), $"{at}: Constrict");
                        c.Equal(model.Slimed, CountSlimed(f), $"{at}: Slimed in the player's piles");
                        c.Equal(model.AiDraws, f.Rng.MonsterAi.Counter, $"{at}: MonsterAi draws so far");
                        c.Equal(turn, f.Turn, $"{at}: the turn number");

                        model.EndTurn();
                        f.EndTurn();
                    }
                    c.Equal(model.Hp, f.Player.Hp, $"seed {seed}: the player's HP after eight rounds");
                    c.That(!f.IsOver, $"seed {seed}: the fight is still running");
                }
            });
        }
    }

    // ---------- nothing kept outside the fight ----------

    private static void Determinism(Check c)
    {
        c.Group("each encounter plays to its end under a random player, 40 seeds", () =>
        {
            foreach (var encounter in EncounterClasses)
            {
                for (var seed = 1; seed <= 40; seed++)
                {
                    var at = $"{encounter} seed {seed}";
                    var f = new FightBuilder().Encounter(encounter).Seed(seed).Start();
                    var player = new RandomPolicy(seed);
                    for (var step = 0; step < 5000 && !f.IsOver; step++)
                    {
                        // Only a Strangler applies Constrict, and it ends the moment that Strangler dies.
                        if (f.Player.HasPower<ConstrictPower>())
                            c.That(f.Roster.Any(e => e.Monster is SlitheringStrangler && f.InFight(e)), $"{at}: Constrict without a living Strangler");
                        f.Do(player.Choose(f.Snapshot()));
                    }
                    c.That(f.IsOver, $"{at}: the fight did not end");
                    if (f.Won)
                    {
                        c.That(f.Roster.All(e => e.IsDead), $"{at}: won with an enemy alive");
                        c.Equal(0, f.Enemies.Count, $"{at}: won with an enemy still in the fight");
                    }
                    else if (!f.TimedOut)
                    {
                        c.Equal(0, f.Player.Hp, $"{at}: lost with HP left");
                    }
                }
            }
        });

        c.Group("each encounter replays identically from its seed, and a copy made mid-fight plays on identically", () =>
        {
            foreach (var encounter in EncounterClasses)
            {
                for (var seed = 1; seed <= 4; seed++)
                {
                    var first = CheckKit.PlayOut(new FightBuilder().Encounter(encounter).Seed(seed).Start());
                    var second = CheckKit.PlayOut(new FightBuilder().Encounter(encounter).Seed(seed).Start());
                    c.That(first == second, $"{encounter} seed {seed}: two runs differ");

                    var f = new FightBuilder().Hp(200, 200).Encounter(encounter).Seed(seed).Start();
                    f.EndTurn();
                    f.EndTurn();
                    f.EndTurn();
                    var copy = f.Clone();
                    var seen = f.Seen();
                    c.That(seen == copy.Seen(), $"{encounter} seed {seed}: the copy looks different");
                    var played = CheckKit.PlayOut(copy);
                    c.That(seen == f.Seen(), $"{encounter} seed {seed}: playing the copy changed the fight");
                    c.That(played == CheckKit.PlayOut(f), $"{encounter} seed {seed}: the copy played out differently");
                }
            }
        });
    }
}
