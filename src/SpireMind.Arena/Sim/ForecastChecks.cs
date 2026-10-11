using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The forecast (Sim/Forecast.cs). Its arithmetic against the cases the mod's own file tests
/// itself with; its entries against moves and numbers worked out by hand from the recipe and
/// from each monster's machine, and against what the enemy then shows when the player passes;
/// where it must mark a number or say nothing; what it reads and never changes; what the switch
/// puts in a snapshot; and its tables against the simulator's own monsters.
/// </summary>
public sealed class ForecastChecks : IArenaChecks
{
    private const string V = "VULNERABLE_SPORES_MOVE", F = "FRAIL_SPORES_MOVE", S = "SMASH_MOVE";
    private const string Pounce = "POKEY_POUNCE_MOVE", Sticky = "STICKY_SHOT_MOVE";

    public void Run(Check c)
    {
        TheArithmetic(c);
        FixedCycles(c);
        RandomBranches(c);
        ForcedMoves(c);
        NumbersAndMarks(c);
        EveryPower(c);
        WhenNothingIsSaid(c);
        ReadsOnly(c);
        TheSnapshot(c);
        TheTables(c);
        TheModsFile(c);
    }

    // ---------- helpers ----------

    /// <summary>JSON as the tools send it to a model: "+", "~" and "|" as they are.</summary>
    private static string Plain(JsonNode node) => node.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static Fight Versus(string monster, int seed = 1) => new FightBuilder().Hp(900, 900).Enemy(monster).Seed(seed).Start();

    private static List<string> Next(Fight f, int enemy = 0) => (Forecast.For(f, f.Enemy(enemy)) ?? []).ToList();

    private static List<string> StepIds(Fight f, int enemy = 0) => (Forecast.Read(f, f.Enemy(enemy))?.Steps ?? []).Select(s => s.MoveId).ToList();

    private static List<(string Id, double Chance)> Odds(Fight f, int enemy = 0) =>
        (Forecast.Read(f, f.Enemy(enemy))?.Odds ?? []).Select(o => (o.MoveId, Math.Round(o.Probability, 4))).ToList();

    /// <summary>What the enemy shows on each of the coming turns when the player only passes: the move and its intents in one string.</summary>
    private static List<(string Id, string Text)> Passing(Fight f, int enemy, int turns)
    {
        var copy = f.Clone();
        copy.Player.MaxHp = 1_000_000;
        copy.Player.Hp = 1_000_000;
        var shown = new List<(string, string)>();
        for (var k = 0; k < turns && !copy.IsOver; k++)
        {
            copy.EndTurn();
            var e = copy.Enemy(enemy);
            if (copy.IsOver || !copy.InFight(e)) break;
            shown.Add((e.Monster!.NextMove!.Id, string.Join(" + ", copy.IntentTexts(e))));
        }
        return shown;
    }

    /// <summary>Every certain step of the enemy's forecast is the move it then shows when the player passes, and every number without "~" is its number then.</summary>
    private static void Holds(Check c, Fight f, int enemy, string what)
    {
        var reading = Forecast.Read(f, f.Enemy(enemy));
        var steps = reading?.Steps ?? [];
        var truth = Passing(f, enemy, steps.Count);
        c.Equal(steps.Count, truth.Count, $"{what}: every forecast step came due");
        for (var k = 0; k < steps.Count && k < truth.Count; k++)
        {
            c.Equal(steps[k].MoveId, truth[k].Id, $"{what}: the move {k + 1} turns on");
            if (!steps[k].Rough) c.Equal(steps[k].Text, truth[k].Text, $"{what}: its words and numbers {k + 1} turns on");
        }
    }

    // ---------- the arithmetic: the cases of the mod's own self-test ----------

    private static void TheArithmetic(Check c)
    {
        static string[] Ids(string log) => log.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static string Show(IReadOnlyList<Forecast.Odds> odds) => odds.Count == 0 ? "?" : Forecast.OddsText(odds.Select(o => (o.Id, o.Chance)).ToList());
        static string Roll(Forecast.Option[] options, string log) => Show(Forecast.Chances(options, Ids(log)));
        static string Told(Forecast.Outlook outlook) =>
            string.Join(" ", outlook.Steps.Select(s => s.Move ?? "(" + Show(s.Odds!) + ")")) + (outlook.Open ? " .." : " .");
        static Func<string, Forecast.PlanState?> In(Dictionary<string, Forecast.PlanState> states) => id => states.GetValueOrDefault(id);
        static Forecast.PlanMove Move(string id, string? next, bool ends = false) => new(id, next, ends);
        static Forecast.Option O(string id, Forecast.Repeat rule, int max, int cooldown, float weight) => new(id, rule, max, cooldown, weight);
        const Forecast.Repeat Never = Forecast.Repeat.Never, Once = Forecast.Repeat.Once, Times = Forecast.Repeat.Times, Forever = Forecast.Repeat.Forever;

        c.Group("the chance arithmetic: Flyconid, by the recipe's table", () =>
        {
            Forecast.Option[] flyconid = [O("V", Never, 0, 3, 1f), O("F", Never, 0, 2, 1f), O("S", Never, 0, 0, 1f)];
            c.Equal("50% V | 50% F", Roll(flyconid, "S"), "[S]");
            c.Equal("50% V | 50% S", Roll(flyconid, "F"), "[F]");
            c.Equal("50% V | 50% S", Roll(flyconid, "S F"), "[S F]");
            c.Equal("V", Roll(flyconid, "F S"), "[F S]");
            c.Equal("S", Roll(flyconid, "F V"), "[F V]");
            c.Equal("50% F | 50% S", Roll(flyconid, "S V"), "[S V]");
            c.Equal("S", Roll(flyconid, "S F V"), "[S F V]");
            c.Equal("S", Roll(flyconid, "S V F"), "[S V F]");
            c.Equal("V", Roll(flyconid, "V F S"), "[V F S]: all blocked, the first listed comes");
            c.Equal("V", Roll(flyconid, "S V F S"), "[S V F S]: all blocked");
            Forecast.Option[] first = [O("F", Never, 0, 2, 1f), O("S", Never, 0, 0, 1f)];
            c.Equal("50% F | 50% S", Roll(first, ""), "its first move");

            var machine = new Dictionary<string, Forecast.PlanState>
            {
                ["V"] = Move("V", "RAND"), ["F"] = Move("F", "RAND"), ["S"] = Move("S", "RAND"),
                ["RAND"] = new Forecast.PlanRandom(_ => flyconid),
            };
            c.Equal("S F S .", Told(Forecast.Plan(Move("V", "RAND"), Ids("S F V"), 3, In(machine))), "walk of 3");
            c.Equal("S F S V (50% F | 50% S) ..", Told(Forecast.Plan(Move("V", "RAND"), Ids("S F V"), 5, In(machine))), "walk of 5");
            c.Equal("(50% V | 50% S) ..", Told(Forecast.Plan(Move("F", "RAND"), Ids("S F"), 3, In(machine))), "frail spores showing");
        });

        c.Group("the chance arithmetic: Twig Slime (M), Mawler, Fogmog, Two-Tailed Rat", () =>
        {
            Forecast.Option[] twig = [O("P", Times, 2, 0, 1f), O("T", Never, 0, 0, 1f)];
            c.Equal("P", Roll(twig, "T"), "Twig Slime [T]");
            c.Equal("50% P | 50% T", Roll(twig, "T P"), "Twig Slime [T P]");
            c.Equal("T", Roll(twig, "T P P"), "Twig Slime [T P P]");
            c.Equal("50% P | 50% T", Roll(twig, "P"), "Twig Slime [P]: a log shorter than the limit");
            var twigMachine = new Dictionary<string, Forecast.PlanState>
            {
                ["P"] = Move("P", "RAND"), ["T"] = Move("T", "RAND"), ["RAND"] = new Forecast.PlanRandom(_ => twig),
            };
            c.Equal("P (50% P | 50% T) ..", Told(Forecast.Plan(Move("T", "RAND"), Ids("T"), 3, In(twigMachine))), "Twig Slime turn 1");
            // Stunned while its first pounce shows: the stun's follow-up is the pounce, which is then logged a second time.
            c.Equal("P T P .", Told(Forecast.Plan(Move("STUNNED", "P"), Ids("T P"), 3, In(twigMachine))), "Twig Slime stunned");

            Forecast.Option[] mawler = [O("R", Never, 0, 0, 1f), O("O", Once, 0, 0, 1f), O("C", Never, 0, 0, 1f)];
            c.Equal("50% R | 50% O", Roll(mawler, "C"), "Mawler [C]");
            c.Equal("50% R | 50% C", Roll(mawler, "C O"), "Mawler [C O]");
            c.Equal("C", Roll(mawler, "C O R"), "Mawler [C O R]");
            c.Equal("50% O | 50% C", Roll(mawler, "C R"), "Mawler [C R]");

            Forecast.Option[] fogmog = [O("SWIPE2", Never, 0, 0, 0.4f), O("BUTT", Never, 0, 0, 0.6f)];
            var fogmogMachine = new Dictionary<string, Forecast.PlanState>
            {
                ["ILLUSION"] = Move("ILLUSION", "SWIPE"), ["SWIPE"] = Move("SWIPE", "BRANCH"), ["SWIPE2"] = Move("SWIPE2", "BUTT"),
                ["BUTT"] = Move("BUTT", "SWIPE"), ["BRANCH"] = new Forecast.PlanRandom(_ => fogmog),
            };
            c.Equal("SWIPE (40% SWIPE2 | 60% BUTT) ..", Told(Forecast.Plan(Move("ILLUSION", "SWIPE"), Ids("ILLUSION"), 3, In(fogmogMachine))), "Fogmog turn 1");
            c.Equal("BUTT SWIPE .", Told(Forecast.Plan(Move("SWIPE2", "BUTT"), Ids("ILLUSION SWIPE SWIPE2"), 2, In(fogmogMachine))), "Fogmog after the second swipe");

            Forecast.Option[] rat = [O("S", Never, 0, 0, 1f), O("D", Never, 0, 0, 1f), O("R", Never, 0, 3, 1f), O("B", Once, 0, 0, 0f)];
            c.Equal("50% D | 50% R", Roll(rat, "S"), "Rat [S]");
            c.Equal("D", Roll(rat, "R S"), "Rat [R S]");
            c.Equal("33% S | 33% D | 33% R", Roll(rat, ""), "Rat first move");
            Forecast.Option[] ratSummons = [O("S", Never, 0, 0, 1f / 12f), O("D", Never, 0, 0, 1f / 12f), O("R", Never, 0, 3, 1f / 12f), O("B", Once, 0, 0, 0.75f)];
            c.Equal("9% S | 9% R | 82% B", Roll(ratSummons, "S D"), "Rat that may summon [S D]");
            c.Equal("10% D | 90% B", Roll(ratSummons, "R S"), "Rat that may summon [R S]");
        });

        c.Group("the chance arithmetic: repeat rules at their edges", () =>
        {
            c.Equal("B", Roll([O("A", Times, 0, 0, 1f), O("B", Forever, 0, 0, 3f)], ""), "at most 0 in a row is never open");
            c.Equal("?", Roll([], "A"), "no options");
            c.Equal("?", Roll([O("A", Forever, 0, 0, -1f), O("B", Forever, 0, 0, 1f)], ""), "a negative weight says nothing");
            c.Equal("25% A | 75% B", Roll([O("A", Forever, 0, 0, 1f), O("B", Forever, 0, 0, 3f)], "A A"), "weights 1 and 3");
        });

        c.Group("the walk: cycles, branches with a rule, and where it must stop", () =>
        {
            var byrdonis = new Dictionary<string, Forecast.PlanState> { ["SWOOP"] = Move("SWOOP", "PECK"), ["PECK"] = Move("PECK", "SWOOP") };
            c.Equal("PECK SWOOP PECK SWOOP .", Told(Forecast.Plan(Move("SWOOP", "PECK"), Ids("SWOOP"), 4, In(byrdonis))), "a fixed cycle");

            Dictionary<string, Forecast.PlanState> Demon(int curses) => new()
            {
                ["CURSE"] = Move("CURSE", "SLAP"), ["SLAP"] = Move("SLAP", "OVERWHELM"), ["OVERWHELM"] = Move("OVERWHELM", "PONDER"),
                ["PONDER"] = Move("PONDER", "BRANCH"),
                ["BRANCH"] = new Forecast.PlanChoice(walk => curses + walk.Passed.Count(id => id == "CURSE") < 3 ? "CURSE" : "SLAP"),
            };
            c.Equal("SLAP OVERWHELM PONDER CURSE SLAP .", Told(Forecast.Plan(Move("CURSE", "SLAP"), Ids("CURSE"), 5, In(Demon(1)))), "a counter branch, second curse showing");
            c.Equal("SLAP OVERWHELM PONDER SLAP OVERWHELM .", Told(Forecast.Plan(Move("CURSE", "SLAP"), Ids("CURSE"), 5, In(Demon(2)))), "a counter branch, third curse showing");

            var sleeper = new Dictionary<string, Forecast.PlanState>
            {
                ["SLEEP"] = Move("SLEEP", "BRANCH"), ["SLASH"] = Move("SLASH", "SLASH"),
                ["BRANCH"] = new Forecast.PlanChoice(walk => 3 - walk.Step >= 1 ? "SLEEP" : "SLASH"),
            };
            c.Equal("SLEEP SLEEP SLASH SLASH .", Told(Forecast.Plan(Move("SLEEP", "BRANCH"), Ids("SLEEP"), 4, In(sleeper))), "a countdown branch");

            var fabricator = new Dictionary<string, Forecast.PlanState>
            {
                ["MAKE"] = Move("MAKE", "BRANCH"), ["STRIKE"] = Move("STRIKE", "BRANCH"), ["BRANCH"] = new Forecast.PlanChoice(_ => "RAND"),
                ["RAND"] = new Forecast.PlanRandom(_ => [O("MAKE", Forever, 0, 0, 1f), O("STRIKE", Forever, 0, 0, 1f)]),
            };
            c.Equal("(50% MAKE | 50% STRIKE) ..", Told(Forecast.Plan(Move("MAKE", "BRANCH"), Ids("MAKE"), 3, In(fabricator))), "a branch into a random pick");
            var undecided = new Dictionary<string, Forecast.PlanState> { ["BUFF"] = Move("BUFF", "BRANCH"), ["LASH"] = Move("LASH", "BUFF"), ["BRANCH"] = new Forecast.PlanChoice(_ => null) };
            c.Equal("BUFF ..", Told(Forecast.Plan(Move("LASH", "BUFF"), Ids("LASH"), 3, In(undecided))), "a branch without a certain answer");

            var ending = new Dictionary<string, Forecast.PlanState> { ["BLOW"] = Move("BLOW", "BLOW", ends: true), ["WAIT"] = Move("WAIT", "BLOW") };
            c.Equal(" .", Told(Forecast.Plan(Move("BLOW", "BLOW", ends: true), Ids(""), 3, In(ending))), "the shown move ends the monster");
            c.Equal("BLOW .", Told(Forecast.Plan(Move("WAIT", "BLOW"), Ids(""), 3, In(ending))), "a walked move ends the monster");
            c.Equal(" ..", Told(Forecast.Plan(Move("A", null), Ids("A"), 3, In(ending))), "no follow-up");
            c.Equal(" ..", Told(Forecast.Plan(Move("A", "NOWHERE"), Ids("A"), 3, In(ending))), "a follow-up that is not in the machine");
            var loop = new Dictionary<string, Forecast.PlanState> { ["X"] = new Forecast.PlanChoice(_ => "Y"), ["Y"] = new Forecast.PlanChoice(_ => "X") };
            c.Equal(" ..", Told(Forecast.Plan(Move("A", "X"), Ids("A"), 3, In(loop))), "branches in a circle");
            var odd = new Dictionary<string, Forecast.PlanState>
            {
                ["A"] = Move("A", "RAND"),
                ["RAND"] = new Forecast.PlanRandom(_ => [O("A", Forever, 0, 0, 1f), O("X", Forever, 0, 0, 1f)]),
                ["X"] = new Forecast.PlanChoice(_ => "A"),
            };
            c.Equal(" ..", Told(Forecast.Plan(Move("A", "RAND"), Ids("A"), 3, In(odd))), "a random option that is not a move");
            var unknown = new Dictionary<string, Forecast.PlanState> { ["A"] = Move("A", "RAND"), ["RAND"] = new Forecast.PlanRandom(_ => null) };
            c.Equal(" ..", Told(Forecast.Plan(Move("A", "RAND"), Ids("A"), 3, In(unknown))), "a random pick whose weights are not known");
        });

        c.Group("Strength ahead, durations, and the words", () =>
        {
            static string Ahead(Forecast.Effect[] before, Forecast.Standing now, int steps) =>
                string.Join(" ", Enumerable.Range(1, steps).Select(step => Forecast.Projected(step, before, now, out var unsure) + (unsure ? "?" : "")));
            var hit = new Forecast.Effect(Attacks: true);
            var idle = new Forecast.Effect(Attacks: false);
            c.Equal("1 2 3", Ahead([hit, hit, hit], new Forecast.Standing(PerTurn: 1), 3), "Territorial 1");
            c.Equal("0 0 2", Ahead([hit, hit, new Forecast.Effect(false, Strength: 2)], default, 3), "a buff move two steps ahead");
            var plow = new Forecast.Effect(true, Strength: 2);
            c.Equal("0 2 4", Ahead([idle, plow, plow], default, 3), "a gain after the hit, stamp showing");
            c.Equal("2 4 6", Ahead([plow, plow, plow], default, 3), "a gain after the hit, plow showing");
            c.Equal("0 2 4", Ahead([new Forecast.Effect(false, Ritual: 2), hit, hit], default, 3), "Ritual about to be applied");
            c.Equal("2 4 6", Ahead([hit, hit, hit], new Forecast.Standing(Ritual: 2), 3), "Ritual in place");
            c.Equal("0 2 4", Ahead([hit, hit, hit], new Forecast.Standing(Ritual: 2, RitualWaits: true), 3), "Ritual just applied");
            c.Equal("2? 4?", Ahead([new Forecast.Effect(false, Ritual: 2), hit], new Forecast.Standing(Ritual: 2), 2), "Ritual on Ritual is marked");
            var thrash = new Forecast.Effect(true, Vigor: 6);
            c.Equal("-6 0 -6", Ahead([hit, thrash, hit], new Forecast.Standing(Vigor: 6), 3), "Vigor, crash showing with 6");
            c.Equal("6 0 6", Ahead([thrash, hit, thrash], default, 3), "Vigor, thrash showing with none");
            c.Equal("0 0 -4", Ahead([idle, idle, hit], new Forecast.Standing(Vigor: 4), 3), "Vigor waits for an attack");
            c.Equal("3 3", Ahead([hit, hit], new Forecast.Standing(Temporary: 3), 2), "a Strength loss that comes back");
            c.Equal("0 0? 0?", Ahead([hit, new Forecast.Effect(false, Unknown: true), hit], default, 3), "a gain of unknown size");

            c.Equal("True False False", string.Join(" ", Enumerable.Range(1, 3).Select(step => Forecast.Lasts(2, false, step))), "2 turns of a debuff cover the next hit only");
            c.Equal("True True False", string.Join(" ", Enumerable.Range(1, 3).Select(step => Forecast.Lasts(2, true, step))), "a tick still to be skipped adds one");

            c.Equal("attack 11", Forecast.AttackText(11, 1, false), "an attack");
            c.Equal("attack 4x3", Forecast.AttackText(4, 3, false), "a multi attack");
            c.Equal("attack ~11", Forecast.AttackText(11, 1, true), "a rough attack");
            c.Equal("attack ~4x3", Forecast.AttackText(4, 3, true), "a rough multi attack: the hit count is never rough");
            c.Equal("50% debuff | 50% attack 11", Forecast.OddsText([("debuff", 0.5), ("attack 11", 0.5)]), "chances");
            c.Equal("67% attack 7 | 33% buff", Forecast.OddsText([("attack 7", 1.0 / 3), ("buff", 1.0 / 3), ("attack 7", 1.0 / 3)]), "chances that read the same are merged");
            c.Equal("attack 7", Forecast.OddsText([("attack 7", 0.5), ("attack 7", 0.5)]), "chances that all read the same");
        });
    }

    // ---------- fixed cycles ----------

    private static void FixedCycles(Check c)
    {
        c.Group("Byrdonis: Swoop and Peck in turn, with the Strength Territorial gives by then (the recipe's example)", () =>
        {
            var f = Versus("Byrdonis");
            c.Sequence(["attack 17"], f.IntentTexts(f.Enemy(0)), "it shows the Swoop");
            c.Sequence(["attack 4x3", "attack 19", "attack 6x3"], Next(f), "turn 1: Peck at 1 Strength, Swoop at 2, Peck at 3");
            c.That(Odds(f).Count == 0, "a fixed cycle has no pick");
            Holds(c, f, 0, "turn 1");
            f.EndTurn();
            c.Sequence(["attack 4x3"], f.IntentTexts(f.Enemy(0)), "turn 2 shows what turn 1 forecast");
            c.Sequence(["attack 19", "attack 6x3", "attack 21"], Next(f), "turn 2");
            Holds(c, f, 0, "turn 2");
        });

        c.Group("Nibbit alone: Slice, Hiss, then Butt with the Hiss's 2 Strength (the recipe's example)", () =>
        {
            var f = new FightBuilder().Hp(900, 900).Enemy(new Nibbit { IsAlone = true }).Start();
            c.Sequence(["attack 12"], f.IntentTexts(f.Enemy(0)), "it shows the Butt");
            c.Sequence(["attack 6 + defend", "buff", "attack 14"], Next(f), "the Butt reads 14, not 12");
            Holds(c, f, 0, "turn 1");
        });

        c.Group("Ceremonial Beast: Stamp, then Plow for ever, 2 Strength more each time (the recipe's example)", () =>
        {
            var f = Versus("CeremonialBeast");
            c.Sequence(["buff"], f.IntentTexts(f.Enemy(0)), "it shows the Stamp");
            c.Sequence(["attack 18 + buff", "attack 20 + buff", "attack 22 + buff"], Next(f), "18, 20, 22");
            Holds(c, f, 0, "turn 1");
            f.EndTurn().EndTurn().EndTurn();
            c.Sequence(["attack 22", "buff"], f.IntentTexts(f.Enemy(0)), "turn 4 shows the Plow at 4 Strength");
            c.Sequence(["attack 24 + buff", "attack 26 + buff", "attack 28 + buff"], Next(f), "24, 26, 28");
            Holds(c, f, 0, "turn 4");
        });

        c.Group("Cubex Construct, Fuzzy Wurm Crawler, Vantom, the Kin: gains of their own moves are in the numbers", () =>
        {
            var cubex = Versus("CubexConstruct");
            c.Sequence(["attack 9 + buff", "attack 11 + buff", "attack 11x2"], Next(cubex), "Cubex: 7 + 2, 7 + 4, 5 + 6 twice");
            var wurm = Versus("FuzzyWurmCrawler");
            c.Sequence(["buff", "attack 11", "attack 11"], Next(wurm), "Wurm: 4 + 7 after it inhaled");
            var vantom = Versus("Vantom");
            c.Sequence(["attack 6x2", "attack 26 + statuscard: 3", "buff"], Next(vantom), "Vantom: no gain within three turns");
            foreach (var (fight, name) in new[] { (cubex, "Cubex"), (wurm, "Wurm"), (vantom, "Vantom") })
            {
                for (var turn = 1; turn <= 6; turn++)
                {
                    Holds(c, fight, 0, $"{name} turn {turn}");
                    fight.EndTurn();
                }
            }

            var kin = new FightBuilder().Hp(900, 900).Encounter("TheKinBoss").Start();
            c.Sequence(["attack 7", "attack 4x2", "buff"], Next(kin, 0), "the Follower that starts with its dance: 5 + 2, 2 + 2 twice");
            c.Sequence(["attack 8 + debuff", "attack 3x3", "buff"], Next(kin, 2), "the Priest");
            for (var turn = 1; turn <= 6; turn++)
            {
                for (var j = 0; j < 3; j++) Holds(c, kin, j, $"Kin e{j} turn {turn}");
                kin.EndTurn();
            }
        });

        c.Group("at most three entries", () =>
        {
            var f = Versus("TwigSlimeS");
            c.Sequence(["attack 4", "attack 4", "attack 4"], Next(f), "a monster with one move for ever");
            c.Equal(3, Forecast.MostSteps, "the most is three");
            c.Equal(1, (Forecast.For(f, f.Enemy(0), 1) ?? []).Count, "fewer when fewer are asked for");
            c.That(Forecast.For(f, f.Enemy(0), 0) is null, "none for none");
        });
    }

    // ---------- random branches ----------

    /// <summary>The Flyconid's branch by its rules alone: Vulnerable Spores not among the last three
    /// moves, Frail Spores not among the last two, Smash not twice running; with nothing open, the
    /// first listed (Vulnerable Spores). Walks on while one move is open, three entries at most.</summary>
    private static (List<string> Next, List<string> Pick) FlyconidByHand(IReadOnlyList<string> log)
    {
        var moves = log.ToList();
        var next = new List<string>();
        while (next.Count < Forecast.MostSteps)
        {
            var open = new List<string>();
            if (!moves.TakeLast(3).Contains(V)) open.Add(V);
            if (!moves.TakeLast(2).Contains(F)) open.Add(F);
            if (moves[^1] != S) open.Add(S);
            if (open.Count > 1) return (next, open);
            var move = open.Count == 1 ? open[0] : V;
            next.Add(move);
            moves.Add(move);
        }
        return (next, []);
    }

    private static void RandomBranches(Check c)
    {
        c.Group("Flyconid: every forecast over 60 fights is what its repeat rules and cooldowns give", () =>
        {
            int certain = 0, picks = 0, allBlocked = 0, wrong = 0;
            var ends = new HashSet<string>();
            for (var seed = 1; seed <= 60; seed++)
            {
                var f = Versus("Flyconid", seed);
                for (var turn = 1; turn <= 9; turn++)
                {
                    var log = f.Enemy(0).Monster!.MoveLog;
                    var (next, pick) = FlyconidByHand(log);
                    var chance = pick.Count == 0 ? 0 : Math.Round(1.0 / pick.Count, 4);
                    if (!StepIds(f).SequenceEqual(next) || !Odds(f).SequenceEqual(pick.Select(id => (id, chance)))) wrong++;
                    if (next.Count > 0) certain++;
                    if (pick.Count > 0) picks++;
                    if (log.Count >= 3 && log.TakeLast(3).SequenceEqual([V, F, S])) allBlocked++;
                    ends.Add(string.Join(",", log.TakeLast(3)));
                    Holds(c, f, 0, $"seed {seed} turn {turn}");
                    f.EndTurn();
                }
            }
            c.Equal(0, wrong, "forecasts that differ from the rules");
            c.That(certain > 50 && picks > 50, $"both kinds of step were met ({certain} certain, {picks} picks)");
            c.That(allBlocked > 0, "the log Vulnerable, Frail, Smash was met, where every option is blocked and the first listed one comes");
            c.That(ends.Count >= 8, $"{ends.Count} different ends of the log were met");
        });

        c.Group("Flyconid: the recipe's two worked examples", () =>
        {
            Fight? afterSmashFrailVulnerable = null, afterSmashFrail = null;
            for (var seed = 1; seed <= 400 && (afterSmashFrailVulnerable is null || afterSmashFrail is null); seed++)
            {
                var f = Versus("Flyconid", seed);
                if (!f.Enemy(0).Monster!.MoveLog.SequenceEqual([S])) continue;
                f.EndTurn();
                if (!f.Enemy(0).Monster!.MoveLog.SequenceEqual([S, F])) continue;
                afterSmashFrail ??= f.Clone();
                f.EndTurn();
                if (f.Enemy(0).Monster!.MoveLog.SequenceEqual([S, F, V])) afterSmashFrailVulnerable ??= f;
            }
            c.That(afterSmashFrail is not null && afterSmashFrailVulnerable is not null, "a fight that starts Smash, Frail Spores, Vulnerable Spores was found");
            if (afterSmashFrail is null || afterSmashFrailVulnerable is null) return;

            // Frail Spores is showing after a Smash: Vulnerable Spores or Smash, even odds, and nothing certain.
            c.Sequence(["attack 8", "debuff"], afterSmashFrail.IntentTexts(afterSmashFrail.Enemy(0)), "it shows Frail Spores");
            c.Sequence(["50% debuff | 50% attack 11"], Next(afterSmashFrail), "one entry: the pick");
            c.Sequence([(V, 0.5), (S, 0.5)], Odds(afterSmashFrail), "Vulnerable Spores or Smash");

            // Vulnerable Spores is showing after Smash and Frail Spores: Smash, Frail Spores, Smash are certain.
            // The spores put 2 Vulnerable on the player, so the next two hits land half again as hard (16 and 12)
            // and the third does not (11). The correction is not applied, so every number is marked: never plain 11, 8.
            c.Sequence(["debuff"], afterSmashFrailVulnerable.IntentTexts(afterSmashFrailVulnerable.Enemy(0)), "it shows Vulnerable Spores");
            c.Sequence(["attack ~11", "attack ~8 + debuff", "attack ~11"], Next(afterSmashFrailVulnerable), "three certain moves, every number marked");
            c.Sequence([S, F, S], StepIds(afterSmashFrailVulnerable), "Smash, Frail Spores, Smash");
            c.Sequence(["attack 16", "attack 12 + debuff", "attack 11"], Passing(afterSmashFrailVulnerable, 0, 3).Select(t => t.Text), "what then comes: 16, 12, 11");
        });

        c.Group("Twig Slime (M): Pounce at most twice running, Sticky Shot never twice (the recipe's example)", () =>
        {
            var f = Versus("TwigSlimeM");
            c.Sequence(["statuscard: 1"], f.IntentTexts(f.Enemy(0)), "it starts with the Sticky Shot");
            c.Sequence(["attack 11", "50% attack 11 | 50% statuscard: 1"], Next(f), "the Pounce is certain, then even odds");
            c.Sequence([(Pounce, 0.5), (Sticky, 0.5)], Odds(f), "the pick's moves");

            Fight? twice = null;
            for (var seed = 1; seed <= 200 && twice is null; seed++)
            {
                var g = Versus("TwigSlimeM", seed);
                g.EndTurn().EndTurn();
                if (g.Enemy(0).Monster!.MoveLog.SequenceEqual([Sticky, Pounce, Pounce])) twice = g;
            }
            c.That(twice is not null, "a fight with two Pounces running was found");
            if (twice is null) return;
            c.Sequence(["statuscard: 1", "attack 11", "50% attack 11 | 50% statuscard: 1"], Next(twice), "after two Pounces: Sticky Shot, Pounce, then even odds");
            Holds(c, twice, 0, "after two Pounces");
        });

        c.Group("Fogmog: the weights 0.4 and 0.6 are the chances, and the Swipe's Strength is in the pick's numbers", () =>
        {
            var f = new FightBuilder().Hp(900, 900).Encounter("FogmogNormal").Start();
            c.Sequence(["summon"], f.IntentTexts(f.Enemy(0)), "it starts by summoning");
            c.Sequence(["attack 8 + buff", "40% attack 9 + buff | 60% attack 15"], Next(f), "the Swipe, then a second Swipe at 40% or the Headbutt at 60%, both with 1 Strength");
            c.Sequence([("SWIPE_RANDOM_MOVE", 0.4), ("HEADBUTT_MOVE", 0.6)], Odds(f), "the pick's moves");
            f.EndTurn();
            c.Sequence(["statuscard: 3", "statuscard: 3", "statuscard: 3"], Next(f, 1), "the Eye with Teeth that joined has its own forecast");
        });

        c.Group("Mawler, Inklet, Leaf Slime (S), Slithering Strangler", () =>
        {
            var mawler = Versus("Mawler");
            c.Sequence(["50% attack 14 | 50% debuff"], Next(mawler), "Mawler after its Claw: Rip and Tear or the Roar");

            var inklet = Versus("Inklet");
            c.Sequence(["50% attack 10 | 50% attack 2x3"], Next(inklet), "Inklet after its Jab");
            inklet.EndTurn();
            c.Sequence(["attack 3", "50% attack 10 | 50% attack 2x3"], Next(inklet), "then the Jab is certain");

            var leaf = Versus("LeafSlimeS");
            var tackles = leaf.Enemy(0).Monster!.NextMove!.Id == "TACKLE_MOVE";
            c.Sequence(tackles ? new[] { "statuscard: 1", "attack 3", "statuscard: 1" } : new[] { "attack 3", "statuscard: 1", "attack 3" }, Next(leaf), "Leaf Slime (S): strict turns after its first move");
            Holds(c, leaf, 0, "Leaf Slime (S)");

            var strangler = Versus("SlitheringStrangler");
            c.Sequence(["50% attack 7 + defend | 50% attack 12"], Next(strangler), "Strangler after its Constrict");
            strangler.EndTurn();
            c.Sequence(["debuff", "50% attack 7 + defend | 50% attack 12"], Next(strangler), "then the Constrict is certain");
        });

        c.Group("the arithmetic weighs every option as the roll does, for every log up to five moves long", () =>
        {
            var logs = 0;
            foreach (var name in Forecast.Roster.Random)
            {
                var states = Content.Registry.NewMonster(name).BuildStates();
                var moves = states.OfType<MoveState>().Select(m => m.Id).ToList();
                foreach (var branch in states.OfType<RandomBranch>())
                {
                    var log = new List<string>();
                    void Walk(int depth)
                    {
                        logs++;
                        for (var i = 0; i < branch.OptionCount; i++)
                        {
                            var o = branch.OptionAt(i);
                            var rule = o.Repeat switch
                            {
                                MoveRepeat.Forever => Forecast.Repeat.Forever, MoveRepeat.MaxInARow => Forecast.Repeat.Times,
                                MoveRepeat.CannotRepeat => Forecast.Repeat.Never, _ => Forecast.Repeat.Once,
                            };
                            var ours = Forecast.Weight(new Forecast.Option(o.StateId, rule, o.MaxTimes, o.Cooldown, o.Weight), log);
                            if (ours != branch.RollWeight(i, log)) c.That(false, $"{name}.{branch.Id}: {o.StateId} after [{string.Join(",", log)}] weighs {ours}, the roll says {branch.RollWeight(i, log)}");
                        }
                        if (depth == 5) return;
                        foreach (var move in moves)
                        {
                            log.Add(move);
                            Walk(depth + 1);
                            log.RemoveAt(log.Count - 1);
                        }
                    }
                    Walk(0);
                }
            }
            c.That(logs > 1500, $"{logs} logs were weighed");
        });
    }

    // ---------- moves forced from outside the machine ----------

    private static void ForcedMoves(Check c)
    {
        c.Group("a stun: the move it was about to make follows, and the cycle goes on from there", () =>
        {
            var f = Versus("Byrdonis");
            var forced = f.Enemy(0).Monster!.ForcedMoves;
            f.Stun(f.Enemy(0));
            c.Sequence(["stunned"], f.IntentTexts(f.Enemy(0)), "it shows the stun");
            c.Sequence(["attack 18", "attack 5x3", "attack 20"], Next(f), "the Swoop it lost comes next, each number with Territorial's Strength of that turn");
            c.Equal(forced + 1, f.Enemy(0).Monster!.ForcedMoves, "the stun is counted as a forced move");
            Holds(c, f, 0, "after the stun");
        });

        c.Group("Twig Slime (M) stunned while its first Pounce shows: the delayed Pounce is logged twice (the recipe's example)", () =>
        {
            Fight? pouncing = null;
            for (var seed = 1; seed <= 50 && pouncing is null; seed++)
            {
                var g = Versus("TwigSlimeM", seed);
                g.EndTurn();
                if (g.Enemy(0).Monster!.MoveLog.SequenceEqual([Sticky, Pounce])) pouncing = g;
            }
            c.That(pouncing is not null, "a fight that shows the first Pounce was found");
            if (pouncing is null) return;
            pouncing.Stun(pouncing.Enemy(0));
            // The stun leads back to the Pounce; with the log reading Sticky, Pounce, Pounce the next roll finds the Pounce blocked.
            c.Sequence([Pounce, Sticky, Pounce], StepIds(pouncing), "Pounce, then the Sticky Shot for certain, then the Pounce");
            c.That(Odds(pouncing).Count == 0, "no pick within three steps");
            Holds(c, pouncing, 0, "after the stun");
        });

        c.Group("Ceremonial Beast: the stun at 150 HP leads into its second cycle, which no forecast foresaw (the recipe's example)", () =>
        {
            var f = new FightBuilder().Hp(900, 900).DrawOrder(CheckKit.StarterInOrder).Enemy("CeremonialBeast").Start();
            f.EndTurn().EndTurn();
            c.Sequence(["attack 20", "buff"], f.IntentTexts(f.Enemy(0)), "it shows the second Plow at 2 Strength");
            c.Sequence(["attack 22 + buff", "attack 24 + buff", "attack 26 + buff"], Next(f), "before the threshold: Plow for ever");
            f.SetHp(f.Enemy(0), 151);
            f.Play("Strike", 0);
            c.Sequence(["stunned"], f.IntentTexts(f.Enemy(0)), "at 145 HP it is stunned");
            c.Sequence(["debuff", "attack 15", "attack 17 + buff"], Next(f), "Beast Cry, Stomp, Crush, with its Strength gone");
            c.Equal(1, f.Enemy(0).Monster!.ForcedMoves, "one move was forced on it");
            Holds(c, f, 0, "after the stun");
            f.EndTurn().EndTurn().EndTurn();
            c.Sequence(["debuff", "attack 18", "attack 20 + buff"], Next(f), "one cycle on: the Crush's 3 Strength is in the Stomp, and in the next Crush");
            Holds(c, f, 0, "the second cycle");
        });

        c.Group("Wrigglers: after the do-nothing turn the slot decides, and that cannot change", () =>
        {
            var f = new FightBuilder().Hp(900, 900).DrawOrder(CheckKit.StarterInOrder).Encounter("PhrogParasiteElite").Start();
            c.Sequence(["attack 4x4", "statuscard: 3", "attack 4x4"], Next(f), "the Phrog Parasite alternates; its unused random branch is never met");
            f.SetHp(f.Enemy(0), 1);
            f.Play("Strike", 0);
            c.Equal(5, f.Roster.Count, "four Wrigglers joined");
            c.Sequence(["stun"], f.IntentTexts(f.Enemy(1)), "a new Wriggler shows its do-nothing move");
            c.Sequence(["attack 6", "buff + statuscard: 1", "attack 8"], Next(f, 1), "the first bites first; its second bite has the Wriggle's 2 Strength");
            c.Sequence(["buff + statuscard: 1", "attack 8", "buff + statuscard: 1"], Next(f, 2), "the second wriggles first");
            c.Sequence(["attack 6", "buff + statuscard: 1", "attack 8"], Next(f, 3), "the third bites first");
            c.Sequence(["buff + statuscard: 1", "attack 8", "buff + statuscard: 1"], Next(f, 4), "the fourth wriggles first");
            c.That(Forecast.For(f, f.Enemy(0)) is null, "the dead Phrog Parasite has no forecast");
            for (var j = 1; j <= 4; j++) Holds(c, f, j, $"Wriggler e{j}");
        });
    }

    // ---------- numbers, and where they carry "~" ----------

    /// <summary>A power for these checks only: its owner takes at most 5 from any powered hit.</summary>
    private sealed class CapAtFive : Power
    {
        public override string Name => "Cap";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "Takes at most 5 damage from an attack.";

        public override decimal ModifyDamageCap(Fight f, Creature? target, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
            ReferenceEquals(target, Owner) && props.IsPoweredAttack() ? 5m : decimal.MaxValue;
    }

    /// <summary>
    /// Stand-ins for powers of the game that the simulator does not have, named as the game names
    /// them: the forecast looks a marking power up by its class name, so a stand-in is found as the
    /// real one would be. They do nothing; only their presence is tested.
    /// </summary>
    private abstract class StandIn : Power
    {
        public override string Name => ClassName;
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "A stand-in for the forecast checks.";
    }

    private sealed class SuckPower : StandIn;
    private sealed class EnragePower : StandIn;
    private sealed class DebilitatePower : StandIn;
    private sealed class CoveredPower : StandIn;
    private sealed class DiamondDiademPower : StandIn;
    private sealed class InterceptPower : StandIn;
    private sealed class TaintedPower : StandIn;
    private sealed class SurroundedPower : StandIn;

    private static readonly (string Name, Action<Fight, Creature> Apply)[] StandIns =
    [
        (nameof(SuckPower), (f, owner) => f.ApplyPower<SuckPower>(owner, 1, owner)),
        (nameof(EnragePower), (f, owner) => f.ApplyPower<EnragePower>(owner, 1, owner)),
        (nameof(DebilitatePower), (f, owner) => f.ApplyPower<DebilitatePower>(owner, 1, owner)),
        (nameof(CoveredPower), (f, owner) => f.ApplyPower<CoveredPower>(owner, 1, owner)),
        (nameof(DiamondDiademPower), (f, owner) => f.ApplyPower<DiamondDiademPower>(owner, 1, owner)),
        (nameof(InterceptPower), (f, owner) => f.ApplyPower<InterceptPower>(owner, 1, owner)),
        (nameof(TaintedPower), (f, owner) => f.ApplyPower<TaintedPower>(owner, 1, owner)),
        (nameof(SurroundedPower), (f, owner) => f.ApplyPower<SurroundedPower>(owner, 1, owner)),
    ];

    private static void NumbersAndMarks(Check c)
    {
        Fight Nibbit() => new FightBuilder().Hp(900, 900).Enemy(new Nibbit { IsAlone = true }).Start();

        c.Group("the powers the mod marks for: each marks every number on its side, and only there", () =>
        {
            // What the mod's file tests (src/SpireMind/Game/Forecast.cs, the line that sets _rough), by hand.
            string[] onEnemy = ["SuckPower", "EnragePower", "DebilitatePower"];
            string[] onPlayer = ["ColossusPower", "CoveredPower", "DiamondDiademPower", "InterceptPower", "TaintedPower", "DebilitatePower", "SurroundedPower"];
            string[] plain = ["attack 6 + defend", "buff", "attack 14"];
            string[] marked = ["attack ~6 + defend", "buff", "attack ~14"];
            c.Sequence(plain, Next(Nibbit()), "with none of them every number is plain");

            foreach (var (name, apply) in StandIns)
            {
                foreach (var side in new[] { "enemy", "player" })
                {
                    var f = Nibbit();
                    var owner = side == "enemy" ? f.Enemy(0) : f.Player;
                    apply(f, owner);
                    c.That(owner.HasPower(name), $"{name} is on the {side}");
                    var marks = (side == "enemy" ? onEnemy : onPlayer).Contains(name);
                    c.Sequence(marks ? marked : plain, Next(f), $"{name} on the {side}");
                }
            }
            // The one of them the simulator has itself is checked with the real power, below.
            c.Sequence(onEnemy.Order(StringComparer.Ordinal), Forecast.Roster.EnemyMarks.Order(StringComparer.Ordinal), "the list for the enemy");
            c.Sequence(onPlayer.Order(StringComparer.Ordinal), Forecast.Roster.PlayerMarks.Order(StringComparer.Ordinal), "the list for the player");
        });

        c.Group("Weak on the enemy: plain while it lasts, marked once it has run out before the hit", () =>
        {
            var f = Nibbit();
            f.ApplyPower<WeakPower>(f.Enemy(0), 1, f.Player);
            c.Sequence(["attack 9"], f.IntentTexts(f.Enemy(0)), "the shown Butt is weakened");
            c.Sequence(["attack ~4 + defend", "buff", "attack ~10"], Next(f), "1 Weak is gone before the next hit: both later numbers are marked (they come out at 6 and 14)");
            c.Sequence(["attack 6 + defend", "buff", "attack 14"], Passing(f, 0, 3).Select(t => t.Text), "what then comes");

            var g = Nibbit();
            g.ApplyPower<WeakPower>(g.Enemy(0), 3, g.Player);
            c.Sequence(["attack 4 + defend", "buff", "attack ~10"], Next(g), "3 Weak: there for the next two turns, gone for the third");
            Holds(c, g, 0, "3 Weak");
        });

        c.Group("Vulnerable on the player: plain while it lasts, a skipped first tick counted", () =>
        {
            var f = Nibbit();
            f.ApplyPower<VulnerablePower>(f.Player, 2, f.Enemy(0));
            c.That(f.Player.GetPower<VulnerablePower>()!.SkipNextDurationTick, "a debuff that is new on the player skips its next tick");
            c.Sequence(["attack 9 + defend", "buff", "attack ~21"], Next(f), "2 Vulnerable and a tick to skip: there for two more turns, gone for the third");
            Holds(c, f, 0, "2 Vulnerable, new");
            f.EndTurn();
            c.Sequence(["buff", "attack ~21", "attack ~12 + defend"], Next(f), "a turn later: 2 left and no tick to skip, so only the next turn is covered");
            Holds(c, f, 0, "2 Vulnerable, a turn old");
        });

        c.Group("Shrink on the enemy counts down like Weak", () =>
        {
            var f = Nibbit();
            f.ApplyPower<ShrinkPower>(f.Enemy(0), 2, f.Player);
            c.Sequence(["attack 4 + defend", "buff", "attack ~9"], Next(f), "2 Shrink: one more turn, then gone");
            Holds(c, f, 0, "2 Shrink");
        });

        c.Group("temporary Strength: the shown move has it, no later one does", () =>
        {
            var f = Versus("Byrdonis");
            f.ApplyPower("ManglePower", f.Enemy(0), 10, f.Player);
            c.Sequence(["attack 7"], f.IntentTexts(f.Enemy(0)), "the shown Swoop is 10 short");
            c.Sequence(["attack 4x3", "attack 19", "attack 6x3"], Next(f), "the later ones are as without it");
            Holds(c, f, 0, "after a Mangle");
        });

        c.Group("Ritual on an enemy: nothing at the end of the turn it landed in, then its amount every turn", () =>
        {
            var f = Nibbit();
            f.ApplyPower<RitualPower>(f.Enemy(0), 2, f.Enemy(0));
            c.Sequence(["attack 6 + defend", "buff", "attack 18"], Next(f), "just applied: 0, then 2, then 4 more, and the Hiss's 2");
            Holds(c, f, 0, "Ritual just applied");
            f.EndTurn();
            c.Sequence(["buff", "attack 18", "attack 14 + defend"], Next(f), "a turn later it gives 2 at every end of turn");
            Holds(c, f, 0, "Ritual in place");
        });

        c.Group("Colossus on the player, and a cap: every number is marked", () =>
        {
            var f = Nibbit();
            f.ApplyPower("ColossusPower", f.Player, 1, f.Player);
            c.Sequence(["attack ~6 + defend", "buff", "attack ~14"], Next(f), "Colossus");

            var g = Nibbit();
            g.ApplyPower<CapAtFive>(g.Player, 1, g.Player);
            c.Sequence(["attack 5"], g.IntentTexts(g.Enemy(0)), "the cap decides today's number");
            c.Sequence(["attack ~5 + defend", "buff", "attack ~5"], Next(g), "and so every later one is marked");
        });

        c.Group("Vulnerable on its way: another enemy's move marks the numbers from the turn it lands", () =>
        {
            // The slime stands before the Flyconid, so the spores land after the slime's hit of the same turn.
            int showing = 0, mayCome = 0;
            for (var seed = 1; seed <= 80; seed++)
            {
                var f = new FightBuilder().Hp(900, 900).Encounter("FlyconidNormal").Seed(seed).Start();
                for (var turn = 1; turn <= 6; turn++)
                {
                    var fly = Forecast.Read(f, f.Enemy(1));
                    var slime = Forecast.Read(f, f.Enemy(0));
                    var shown = f.Enemy(1).Monster!.NextMove!.Id;
                    if (slime is not null && shown == V && !f.Player.HasPower<VulnerablePower>())
                    {
                        showing++;
                        foreach (var step in slime.Steps.Where(s => s.Text.Contains("attack", StringComparison.Ordinal)))
                            if (!step.Rough) c.That(false, $"seed {seed} turn {turn}: the slime's '{step.Text}' is plain though the Flyconid shows its Vulnerable Spores");
                    }
                    if (slime is not null && fly is { Steps.Count: 0, Odds.Count: > 0 } && shown != V && !f.Player.HasPower<VulnerablePower>())
                    {
                        mayCome++;
                        // The pick is the Flyconid's next move: the slime's next hit lands before it, the ones after may meet its spores.
                        for (var k = 0; k < slime.Steps.Count; k++)
                        {
                            var attack = slime.Steps[k].Text.Contains("attack", StringComparison.Ordinal);
                            if (attack && slime.Steps[k].Rough != (k >= 1))
                                c.That(false, $"seed {seed} turn {turn}: the slime's step {k + 1} '{slime.Steps[k].Text}' is marked wrongly for a pick one turn on");
                        }
                    }
                    Holds(c, f, 0, $"FlyconidNormal seed {seed} turn {turn}, the slime");
                    Holds(c, f, 1, $"FlyconidNormal seed {seed} turn {turn}, the Flyconid");
                    f.EndTurn();
                }
            }
            c.That(showing > 5 && mayCome > 5, $"both cases were met ({showing} with the spores showing, {mayCome} with a pick that may give them)");
        });
    }

    /// <summary>
    /// Every power the simulator has, put on the player or on an enemy, alone and beside the
    /// debuffs a fight is full of. Then the player only ends turns on a copy. A number the
    /// forecast printed without "~" must be the number that comes, and a move it called certain
    /// must be the move that comes, unless the enemy was stunned, brought to 0 HP or killed on
    /// the way. So a new card or potion whose power changes what a monster's hit does, now or as
    /// it runs out, fails here until the forecast works it in or marks it.
    /// </summary>
    private static void EveryPower(Check c)
    {
        static string Turns(int k) => k == 1 ? "1 turn" : $"{k} turns";

        c.Group("no power of the simulator, on the player or on an enemy, changes a number the forecast prints plain", () =>
        {
            // The player's own Strength kinds. No card, potion or move puts one on an enemy (Demon Form
            // and Setup Strike are applied to their player, the Flex Potion is drunk), and there the
            // forecast would be wrong: Demon Form is Strength every turn, and an Artifact stops the
            // taking back of the other two. The second half of this group holds that to be so.
            string[] neverOnAnEnemy = ["DemonFormPower", "FlexPotionPower", "SetupStrikePower"];
            string[] encounters = ["ByrdonisElite", "CubexConstructNormal", "FlyconidNormal", "TheKinBoss", "CeremonialBeastBoss", "MawlerNormal"];
            (string What, Action<Fight> Set)[] grounds =
            [
                ("nothing else in play", _ => { }),
                ("every enemy Vulnerable", f => { foreach (var e in f.Enemies) f.ApplyPower<VulnerablePower>(e, 3, f.Player); }),
                ("every enemy Weak", f => { foreach (var e in f.Enemies) f.ApplyPower<WeakPower>(e, 2, f.Player); }),
                ("the player Vulnerable", f => f.ApplyPower<VulnerablePower>(f.Player, 2, f.Enemies[0])),
            ];

            int setUps = 0, plain = 0, marked = 0, cutShort = 0;
            foreach (var encounter in encounters)
            {
                foreach (var (ground, set) in grounds)
                {
                    foreach (var power in Content.Registry.PowerNames.Order(StringComparer.Ordinal))
                    {
                        foreach (var onPlayer in new[] { true, false })
                        {
                            if (!onPlayer && neverOnAnEnemy.Contains(power)) continue;
                            var f = new FightBuilder().Hp(900, 900).Encounter(encounter).Start();
                            set(f);
                            foreach (var owner in onPlayer ? [f.Player] : f.Enemies.ToList())
                            {
                                var g = f.Clone();
                                var target = onPlayer ? g.Player : g.Roster[owner.RosterIndex];
                                // A debuff reaches the player from an enemy and an enemy from the player; a buff is its owner's own.
                                var other = onPlayer ? g.Enemies[0] : g.Player;
                                g.RunAsAction(x => x.ApplyPower(power, target, 2, Content.Registry.NewPower(power).Type == PowerType.Debuff ? other : target));
                                if (g.IsOver || g.HasPendingChoice) continue;
                                setUps++;
                                var where = $"{encounter}, {ground}, {power} on {(onPlayer ? "the player" : target.Name)}";

                                foreach (var enemy in g.Enemies.ToList())
                                {
                                    if (Forecast.Read(g, enemy) is not { } reading) continue;
                                    var copy = g.Clone();
                                    copy.Player.MaxHp = 1_000_000;
                                    copy.Player.Hp = 1_000_000;
                                    var forced = enemy.Monster!.ForcedMoves;
                                    for (var k = 1; k <= reading.Steps.Count; k++)
                                    {
                                        if (copy.IsOver || copy.HasPendingChoice || !copy.Execute("end_turn").Ok || copy.IsOver || copy.HasPendingChoice) break;
                                        var later = copy.Roster[enemy.RosterIndex];
                                        // Killed, at 0 HP, or stunned by what the power does to it: no forecast foresees that.
                                        if (!copy.InFight(later) || later.Monster!.ForcedMoves != forced)
                                        {
                                            cutShort++;
                                            break;
                                        }
                                        var step = reading.Steps[k - 1];
                                        var came = string.Join(" + ", copy.IntentTexts(later));
                                        if (later.Monster.NextMove!.Id != step.MoveId)
                                        {
                                            c.That(false, $"{where}: {enemy.Name}'s move {Turns(k)} on was forecast as {step.MoveId} and {later.Monster.NextMove.Id} came");
                                            break;
                                        }
                                        if (step.Rough) marked++;
                                        else if (step.Text == came) plain++;
                                        else c.That(false, $"{where}: {enemy.Name}'s '{step.Text}' {Turns(k)} on came as '{came}', and the forecast had not marked it");
                                    }
                                }
                            }
                        }
                    }
                }
            }
            c.That(setUps > 2000 && plain > 10_000 && marked > 1000, $"{setUps} set-ups were tried: {plain} plain entries held, {marked} were marked, {cutShort} forecasts were cut short by a death or a stun");

            // The three that are left out above never sit on an enemy in a fight.
            var random = new RandomPolicy();
            var looked = 0;
            foreach (var scenario in Scenarios.Generate(40, 12))
            {
                var f = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
                for (var n = 0; n < 4000 && !f.IsOver; n++)
                {
                    looked++;
                    foreach (var enemy in f.Enemies)
                        foreach (var power in neverOnAnEnemy)
                            if (enemy.HasPower(power)) c.That(false, $"{scenario.Id}: {enemy.Name} holds {power}");
                    f.Do(random.Choose(f.Snapshot()));
                }
            }
            c.That(looked > 500, $"{looked} moments of 40 fights played at random: no enemy ever held one of the three");
        });
    }

    // ---------- when nothing is said ----------

    private static void WhenNothingIsSaid(Check c)
    {
        c.Group("a monster class that was not read gets no forecast, and marks its neighbours' numbers", () =>
        {
            var f = new FightBuilder().Hp(900, 900).Enemy(new Nibbit { IsAlone = true }).Enemy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 7)] }).Start();
            c.That(Forecast.Read(f, f.Enemy(1)) is null, "the dummy has no forecast though its cycle is plain");
            c.Sequence(["attack ~6 + defend", "buff", "attack ~14"], Next(f, 0), "an enemy nobody read could do anything to the others");
        });

        c.Group("nothing for an enemy that is gone, and nothing once the fight is over", () =>
        {
            var f = new FightBuilder().Hp(900, 900).DrawOrder(CheckKit.StarterInOrder).Enemy("TwigSlimeS", hp: 3).Enemy("TwigSlimeS").Start();
            c.That(Forecast.Read(f, f.Enemy(0)) is not null, "alive: a forecast");
            f.Play("Strike", 0);
            c.That(Forecast.Read(f, f.Enemy(0)) is null, "dead: none");
            c.That(Forecast.Read(f, f.Player) is null, "the player has none");
            f.SetHp(f.Enemy(1), 1);
            f.Play("Strike", 1);
            c.That(f.IsOver && Forecast.Read(f, f.Enemy(1)) is null, "after the fight: none");
        });

        c.Group("every forecast in generated fights has one to three entries, and a chance entry only last", () =>
        {
            var greedy = new GreedyPolicy();
            var seen = 0;
            foreach (var scenario in Scenarios.Generate(30, 9))
            {
                var f = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
                for (var n = 0; n < 4000 && !f.IsOver; n++)
                {
                    foreach (var enemy in f.Roster)
                    {
                        if (Forecast.Read(f, enemy) is not { } reading) continue;
                        seen++;
                        var lines = reading.Lines;
                        if (lines.Count is < 1 or > Forecast.MostSteps || lines.Take(lines.Count - 1).Any(l => l.Contains('%')) || lines.Any(string.IsNullOrWhiteSpace))
                            c.That(false, $"{scenario.Id}: {enemy.Name} got [{string.Join(", ", lines)}]");
                        if (reading.Odds.Count == 1 || (reading.Odds.Count > 0 && Math.Abs(reading.Odds.Sum(o => o.Probability) - 1) > 1e-9))
                            c.That(false, $"{scenario.Id}: {enemy.Name}'s chances do not add up to 1");
                    }
                    f.Do(greedy.Choose(f.Snapshot()));
                }
            }
            c.That(seen > 500, $"{seen} forecasts were looked at");
        });
    }

    // ---------- reading only ----------

    private static void ReadsOnly(Check c)
    {
        c.Group("working a forecast out draws no number, moves no machine, and leaves the fight as it was", () =>
        {
            var greedy = new GreedyPolicy();
            foreach (var scenario in Scenarios.Generate(8, 4))
            {
                var watched = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
                var alone = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
                var same = true;
                for (var n = 0; n < 4000 && !watched.IsOver && same; n++)
                {
                    var rolls = Rolls(watched);
                    var machines = string.Join("|", watched.Roster.Select(e => $"{e.Monster!.NextMove?.Id}:{string.Join(",", e.Monster.MoveLog)}:{e.Monster.ForcedMoves}"));
                    var before = watched.Seen();
                    foreach (var enemy in watched.Roster) Forecast.Read(watched, enemy);
                    same &= rolls == Rolls(watched);
                    same &= machines == string.Join("|", watched.Roster.Select(e => $"{e.Monster!.NextMove?.Id}:{string.Join(",", e.Monster.MoveLog)}:{e.Monster.ForcedMoves}"));
                    same &= before == watched.Seen() && before == alone.Seen();
                    var choice = greedy.Choose(watched.Snapshot());
                    watched.Do(choice);
                    alone.Do(choice);
                }
                c.That(same && watched.IsOver && watched.Seen() == alone.Seen(), $"{scenario.Id} went the same way with every forecast worked out as without");
            }
        });

        c.Group("the forecast is the same when everything the player cannot know is replaced", () =>
        {
            var greedy = new GreedyPolicy();
            var compared = 0;
            foreach (var scenario in Scenarios.Generate(30, 5))
            {
                var f = new Fight(FightConfig.From(Scenarios.Setup(scenario)));
                for (var n = 0; n < 4000 && !f.IsOver; n++)
                {
                    if (!f.HasPendingChoice)
                    {
                        var blind = f.CloneDeterminized(1000 + n);
                        for (var j = 0; j < f.Roster.Count; j++)
                        {
                            compared++;
                            if (!(Forecast.For(f, f.Roster[j]) ?? []).SequenceEqual(Forecast.For(blind, blind.Roster[j]) ?? []))
                                c.That(false, $"{scenario.Id}, decision {n}: {f.Roster[j].Name}'s forecast changes with the random numbers to come");
                        }
                    }
                    f.Do(greedy.Choose(f.Snapshot()));
                }
            }
            c.That(compared > 500, $"{compared} forecasts were compared");
        });
    }

    private static int Rolls(Fight f)
    {
        var r = f.Rng;
        return r.Shuffle.Counter + r.MonsterAi.Counter + r.Niche.Counter + r.CombatTargets.Counter + r.CombatCardSelection.Counter
               + r.CombatCardGeneration.Counter + r.CombatPotionGeneration.Counter + r.CombatEnergyCosts.Counter + r.Encounter.Counter;
    }

    // ---------- what the switch puts in a snapshot ----------

    private static void TheSnapshot(Check c)
    {
        var scenario = new Scenario("check-forecast", 1, "normal", "a fight built for the forecast checks");
        var on = new ExperimentOptions(Forecast: true);

        c.Group("the switch adds next_moves behind intents and next_moves_note behind enemies, and nothing else", () =>
        {
            Fight Build() => new FightBuilder().DrawOrder("Armaments", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "Bash").Enemy("TwigSlimeM").Start();
            var without = new ArenaGame(scenario, Build()) { ActionDelay = TimeSpan.Zero };
            var with = new ArenaGame(scenario, Build()) { ActionDelay = TimeSpan.Zero, Experiments = on };
            c.That(!without.Experiments.Forecast, "a game starts with the switch off");

            var plain = without.CaptureAsync(CancellationToken.None).Result.State;
            var state = with.CaptureAsync(CancellationToken.None).Result.State;
            c.That(!plain.ContainsKey(Forecast.HintKey) && plain["enemies"]![0]![Forecast.NextKey] is null, "with the switch off there is no forecast");
            c.Equal(Build().Snapshot().State.ToJsonString(), plain.ToJsonString(), "with the switch off the snapshot is the fight's own");

            var keys = state.Select(kv => kv.Key).ToList();
            c.Equal(keys.IndexOf("enemies") + 1, keys.IndexOf(Forecast.HintKey), "the note follows the enemies");
            c.Equal(Forecast.Hint, state[Forecast.HintKey]!.GetValue<string>(), "the note's text");
            var enemy = state["enemies"]![0]!.AsObject();
            c.Equal("next_moves", Forecast.NextKey, "the list's key is the mod's");
            c.Equal("next_moves_note", Forecast.HintKey, "and so is the note's");
            c.Sequence(["i", "name", "hp", "max_hp", "intents", "next_moves"], enemy.Select(kv => kv.Key), "the enemy's keys");
            c.Equal("[\"attack 11\",\"50% attack 11 | 50% statuscard: 1\"]", Plain(enemy[Forecast.NextKey]!), "next_moves");

            state.Remove(Forecast.HintKey);
            enemy.Remove(Forecast.NextKey);
            c.Equal(plain.ToJsonString(), state.ToJsonString(), "without the two keys it is the plain snapshot, byte for byte");

            // The hand selection shows the same board, and so the same forecast.
            with.ExecuteAsync("play:0", CancellationToken.None).Wait();
            var choosing = with.CaptureAsync(CancellationToken.None).Result;
            c.Equal("hand_select", choosing.Screen, "Armaments opens the hand selection");
            c.That(choosing.State.ContainsKey(Forecast.HintKey) && choosing.State["enemies"]![0]![Forecast.NextKey] is JsonArray, "the hand selection carries the forecast too");
        });

        c.Group("a card selection carries the lists in its picture of the fight, and no note, as the mod's does", () =>
        {
            Fight Build() => new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Potions("AttackPotion").Enemy("Byrdonis").Start();
            var with = new ArenaGame(scenario, Build()) { ActionDelay = TimeSpan.Zero, Experiments = on };
            var without = new ArenaGame(scenario, Build()) { ActionDelay = TimeSpan.Zero };
            with.ExecuteAsync("potion:0", CancellationToken.None).Wait();
            without.ExecuteAsync("potion:0", CancellationToken.None).Wait();
            var choosing = with.CaptureAsync(CancellationToken.None).Result;
            c.Equal("card_select", choosing.Screen, "the Attack Potion opens a card selection");
            var combat = choosing.State["combat"]!.AsObject();
            // The mod's card choice screens take their enemies from the reader's EnemiesJson, where the
            // list is added, and never pass through BoardState, where the note is.
            c.That(!choosing.State.ContainsKey(Forecast.HintKey) && !combat.ContainsKey(Forecast.HintKey), "no note on a selection screen, neither at its top nor in 'combat'");
            var enemy = combat["enemies"]![0]!.AsObject();
            var keys = enemy.Select(kv => kv.Key).ToList();
            c.Equal(keys.IndexOf("intents") + 1, keys.IndexOf(Forecast.NextKey), "the list follows the enemy's intents inside 'combat'");
            c.Equal("[\"attack 4x3\",\"attack 19\",\"attack 6x3\"]", Plain(enemy[Forecast.NextKey]!), "next_moves");
            enemy.Remove(Forecast.NextKey);
            c.Equal(without.CaptureAsync(CancellationToken.None).Result.ToJson().ToJsonString(), choosing.ToJson().ToJsonString(), "without that one key the screen is the plain one, byte for byte");
        });

        c.Group("an enemy with nothing known gets no key, and a snapshot with no forecast no note", () =>
        {
            var fight = new FightBuilder().Enemy(new CheckDummy()).Start();
            var with = new ArenaGame(scenario, fight) { ActionDelay = TimeSpan.Zero, Experiments = on };
            var state = with.CaptureAsync(CancellationToken.None).Result.State;
            c.That(!state["enemies"]![0]!.AsObject().ContainsKey(Forecast.NextKey), "no next_moves");
            c.That(!state.ContainsKey(Forecast.HintKey), "no next_moves_note");
            c.Equal(fight.Snapshot().State.ToJsonString(), state.ToJsonString(), "the snapshot is the plain one");
        });

        c.Group("off, every snapshot of a fight is the fight's own; on, it differs only in the two keys, and the fight goes the same way", () =>
        {
            var greedy = new GreedyPolicy();
            var withNext = 0;
            foreach (var s in Scenarios.Generate(10, 6))
            {
                var plain = new ArenaGame(s) { ActionDelay = TimeSpan.Zero };
                var bare = new Fight(FightConfig.From(Scenarios.Setup(s)));
                var with = new ArenaGame(s) { ActionDelay = TimeSpan.Zero, Experiments = on };
                var ok = true;
                for (var n = 0; n < 4000 && !plain.IsOver && ok; n++)
                {
                    var off = plain.CaptureAsync(CancellationToken.None).Result;
                    var own = bare.Snapshot();
                    var seen = with.CaptureAsync(CancellationToken.None).Result;
                    var text = off.ToJson().ToJsonString();
                    ok &= text == own.ToJson().ToJsonString();
                    ok &= !text.Contains($"\"{Forecast.NextKey}\":", StringComparison.Ordinal) && !text.Contains(Forecast.HintKey, StringComparison.Ordinal);

                    var stripped = seen.ToJson();
                    foreach (var holder in new[] { stripped["state"] as JsonObject, stripped["state"]?["combat"] as JsonObject })
                    {
                        if (holder is null) continue;
                        holder.Remove(Forecast.HintKey);
                        foreach (var enemy in holder["enemies"] as JsonArray ?? [])
                            if ((enemy as JsonObject)?.Remove(Forecast.NextKey) == true) withNext++;
                    }
                    ok &= stripped.ToJsonString() == text;

                    var choice = greedy.Choose(off);
                    plain.ExecuteAsync(choice, CancellationToken.None).Wait();
                    bare.Execute(choice);
                    with.ExecuteAsync(choice, CancellationToken.None).Wait();
                }
                c.That(ok && plain.IsOver && with.IsOver && plain.Won == with.Won && plain.PlayerHp == with.PlayerHp && plain.Turn == with.Turn, $"{s.Id}");
            }
            c.That(withNext > 200, $"{withNext} enemy entries carried a forecast");
        });
    }

    // ---------- the tables, against the simulator's own monsters ----------

    /// <summary>What each move of a monster does when it is performed: the Strength it gives its own monster, and the Vulnerable it puts on the player.</summary>
    private static Dictionary<string, (int Strength, int Vulnerable)> MoveEffects(string className)
    {
        // An idle dummy stands beside it, so that a fight with only a minion in it does not end at once.
        Fight Build() => className == "Wriggler"
            ? new FightBuilder().Hp(900, 900).Slots(PhrogParasiteElite.WrigglerSlot(0)).Enemy(new Wriggler { StartStunned = true }, slot: PhrogParasiteElite.WrigglerSlot(0)).Enemy(new CheckDummy()).Start()
            : new FightBuilder().Hp(900, 900).Enemy(className).Enemy(new CheckDummy()).Start();

        var effects = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var move in Content.Registry.NewMonster(className).BuildStates().OfType<MoveState>())
        {
            var f = Build();
            var enemy = f.Roster.First(e => e.Monster!.ClassName == className);
            var events = new List<HistoryEntry>();
            f.HistoryTraced = events.Add;
            enemy.Monster!.ForceNextMove(move.Id);
            f.EndTurn();
            var performed = events.FindIndex(e => e.Kind == HistoryKind.MonsterPerformedMove && e.ActorId == enemy.Id && e.Name == move.Id);
            if (performed < 0) throw new InvalidOperationException($"{className} did not perform {move.Id}");
            var during = events.Take(performed).Where(e => e is { Kind: HistoryKind.PowerReceived, Side: Side.Enemy }).ToList();
            effects[move.Id] = (
                during.Where(e => e.ActorId == enemy.Id && e.Name == "StrengthPower").Sum(e => e.Amount),
                during.Where(e => e.ActorId == f.Player.Id && e.Name == "VulnerablePower").Sum(e => e.Amount));
        }
        return effects;
    }

    /// <summary>The checkout this build came from, when it holds both forecast files; null in a build that runs without its sources.</summary>
    private static string? Checkout()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, ModFile)) && File.Exists(Path.Combine(dir.FullName, ArenaFile))) return dir.FullName;
        return null;
    }

    private const string ModFile = "src/SpireMind/Game/Forecast.cs", ArenaFile = "src/SpireMind.Arena/Sim/Forecast.cs", ModDoc = "docs/forecast.md";
    private const string ModReader = "src/SpireMind/Game/Screens/CombatReader.cs", ModChoiceReader = "src/SpireMind/Game/Screens/CardSelectReader.cs";

    /// <summary>
    /// The mod's forecast file, source against source. The simulator cannot load the mod (it only
    /// references SpireMind.Core), so this reads both files where the checkout is at hand: the
    /// note, the arithmetic line for line, and the mod's tables for the monsters that are
    /// here. With no checkout at hand nothing is compared.
    /// </summary>
    private static void TheModsFile(Check c)
    {
        c.Group("the mod's forecast (src/SpireMind/Game/Forecast.cs) says the same", () =>
        {
            if (Checkout() is not { } root) return;
            var mod = File.ReadAllText(Path.Combine(root, ModFile));
            var arena = File.ReadAllText(Path.Combine(root, ArenaFile));
            var here = Content.Registry.MonsterNames.ToHashSet(StringComparer.Ordinal);

            // The note: the pieces of the mod's Hint constant, put together.
            var hint = System.Text.RegularExpressions.Regex.Match(mod, @"public const string Hint =(?<body>.*?"")\s*;", System.Text.RegularExpressions.RegexOptions.Singleline);
            var pieces = System.Text.RegularExpressions.Regex.Matches(hint.Groups["body"].Value, "\"(?<text>[^\"]*)\"").Select(m => m.Groups["text"].Value);
            c.Equal(string.Concat(pieces), Forecast.Hint, "the note, word for word");

            // The arithmetic: from the repeat rules down to the next section, line for line.
            static List<string> Arithmetic(string source)
            {
                var lines = source.Split('\n').Select(l => l.Trim()).ToList();
                var from = lines.FindIndex(l => l.StartsWith("internal enum Repeat", StringComparison.Ordinal));
                var to = lines.FindIndex(from + 1, l => l.StartsWith("// ----------", StringComparison.Ordinal));
                return from < 0 || to < 0 ? [] : lines.GetRange(from, to - from);
            }
            var theirs = Arithmetic(mod);
            c.That(theirs.Count > 150, $"the mod's arithmetic was found ({theirs.Count} lines)");
            c.Sequence(theirs, Arithmetic(arena), "the arithmetic, line for line");

            // The tables, for the monsters that are in the simulator.
            static string Block(string source, string start)
            {
                var from = source.IndexOf(start, StringComparison.Ordinal);
                if (from < 0) return "";
                var to = source.IndexOf("};", from, StringComparison.Ordinal);
                return to < 0 ? "" : source[from..to];
            }
            IEnumerable<string> Classes(string start) => System.Text.RegularExpressions.Regex.Matches(Block(mod, start), @"typeof\(M\.(?<name>\w+)\)")
                .Select(m => m.Groups["name"].Value).Where(here.Contains).Order(StringComparer.Ordinal);
            c.Sequence(Classes("Type[] Fixed ="), Forecast.Roster.Fixed.Order(StringComparer.Ordinal), "the fixed cycles");
            c.Sequence(Classes("Type[] Random ="), Forecast.Roster.Random.Order(StringComparer.Ordinal), "the random pickers");
            c.That(!Classes("Type[] Conditional =").Any(), "none of the mod's conditional monsters is here");

            var gains = System.Text.RegularExpressions.Regex.Matches(Block(mod, "Gains = new()"), @"\[\(typeof\(M\.(?<name>\w+)\), ""(?<move>\w+)""\)\] = (?<kind>Strength\((?<amount>\d+)|new\()")
                .Where(m => here.Contains(m.Groups["name"].Value))
                .Select(m => $"{m.Groups["name"].Value}.{m.Groups["move"].Value} {(m.Groups["amount"].Success ? m.Groups["amount"].Value : "not plain Strength")}")
                .Order(StringComparer.Ordinal);
            c.Sequence(gains, Forecast.Roster.Gains.Select(g => $"{g.Key.Monster}.{g.Key.Move} {g.Value}").Order(StringComparer.Ordinal), "the Strength a move gives its own monster");

            var vulnerable = System.Text.RegularExpressions.Regex.Matches(Block(mod, "> Vulnerable = new()"), @"\(typeof\(M\.(?<name>\w+)\), ""(?<move>\w+)""\)")
                .Where(m => here.Contains(m.Groups["name"].Value)).Select(m => $"{m.Groups["name"].Value}.{m.Groups["move"].Value}").Order(StringComparer.Ordinal);
            c.Sequence(vulnerable, Forecast.Roster.Vulnerable.Select(v => $"{v.Monster}.{v.Move}").Order(StringComparer.Ordinal), "the moves that put Vulnerable on the player");

            // The powers that mark every number, by the side the mod tests them on.
            IEnumerable<string> Marks(string owner) => System.Text.RegularExpressions.Regex.Matches(mod, owner + @"\.HasPower<(?<name>\w+)>\(\)")
                .Select(m => m.Groups["name"].Value).Distinct().Order(StringComparer.Ordinal);
            c.Sequence(Marks("me"), Forecast.Roster.PlayerMarks.Order(StringComparer.Ordinal), "the powers on the player that mark every number");
            c.Sequence(Marks("_enemy"), Forecast.Roster.EnemyMarks.Order(StringComparer.Ordinal), "the powers on the enemy that mark every number");

            // The keys, as the mod's page gives its hook.
            if (File.Exists(Path.Combine(root, ModDoc)))
            {
                var doc = File.ReadAllText(Path.Combine(root, ModDoc));
                c.That(doc.Contains($"json[\"{Forecast.NextKey}\"] = next", StringComparison.Ordinal), $"docs/forecast.md puts the list under \"{Forecast.NextKey}\"");
                c.That(doc.Contains($"state[\"{Forecast.HintKey}\"] = Forecast.Hint", StringComparison.Ordinal), $"docs/forecast.md puts the note under \"{Forecast.HintKey}\"");
            }

            // The keys and their places, as the mod's reader sets them: the list for every enemy the
            // shared routine lists, the note on the board only. Forecast.AddTo does the same here.
            var readerFile = Path.Combine(root, ModReader);
            var choiceFile = Path.Combine(root, ModChoiceReader);
            if (File.Exists(readerFile) && File.Exists(choiceFile))
            {
                var reader = File.ReadAllText(readerFile);
                var choices = File.ReadAllText(choiceFile);
                var lists = System.Text.RegularExpressions.Regex.Matches(reader, @"Forecast\.For\(enemy\) is \{ \} \w+\) json\[""(?<key>\w+)""\]").Select(m => m.Groups["key"].Value).ToList();
                c.Sequence([Forecast.NextKey], lists, "the reader puts an enemy's list under this key, in one place");
                var notes = System.Text.RegularExpressions.Regex.Matches(reader, @"state\[""(?<key>\w+)""\] = Forecast\.Hint").Select(m => m.Groups["key"].Value).ToList();
                c.Sequence([Forecast.HintKey], notes, "the reader puts the note under this key, in one place");

                static string Body(string source, string signature)
                {
                    var from = source.IndexOf(signature, StringComparison.Ordinal);
                    if (from < 0) return "";
                    var to = source.IndexOf("\n    }", from, StringComparison.Ordinal);
                    return to < 0 ? "" : source[from..to];
                }
                var enemiesJson = Body(reader, "internal static JsonArray EnemiesJson(");
                var boardState = Body(reader, "private static JsonObject BoardState(");
                c.That(enemiesJson.Contains("Forecast.For(enemy)", StringComparison.Ordinal), "the list is added where every enemy list is built (EnemiesJson)");
                c.That(enemiesJson.IndexOf("json[\"intents\"]", StringComparison.Ordinal) is var intents and >= 0
                       && enemiesJson.IndexOf("Forecast.For(enemy)", StringComparison.Ordinal) > intents
                       && enemiesJson.IndexOf("json[\"primary\"]", StringComparison.Ordinal) > enemiesJson.IndexOf("Forecast.For(enemy)", StringComparison.Ordinal),
                    "behind the intents and before the keys that follow them");
                c.That(boardState.IndexOf("state[\"enemies\"] = ", StringComparison.Ordinal) is var enemies and >= 0
                       && boardState.IndexOf("= Forecast.Hint", StringComparison.Ordinal) > enemies
                       && boardState.IndexOf("state[\"incoming_damage\"]", StringComparison.Ordinal) > boardState.IndexOf("= Forecast.Hint", StringComparison.Ordinal),
                    "the note is set on the board (BoardState), behind the enemies and before incoming_damage");
                c.That(choices.Contains("CombatReader.EnemiesJson(", StringComparison.Ordinal), "the card choice screens take their enemies from the same routine, so they carry the lists");
                c.That(!choices.Contains("Forecast.", StringComparison.Ordinal) && !choices.Contains(Forecast.HintKey, StringComparison.Ordinal), "and they add no note of their own");
            }
        });
    }

    private static void TheTables(Check c)
    {
        c.Group("every monster of the simulator is in exactly one group of the roster, and the roster names no other", () =>
        {
            var registered = Content.Registry.MonsterNames.Order(StringComparer.Ordinal).ToList();
            c.Sequence(registered, Forecast.Roster.Reviewed.Order(StringComparer.Ordinal), "the reviewed classes are the registered monsters");
            c.That(!Forecast.Roster.Fixed.Overlaps(Forecast.Roster.Random), "no class is in both groups");
            c.Equal(22, Forecast.Roster.Fixed.Count, "fixed cycles, as the recipe counts them for act 1");
            c.Equal(7, Forecast.Roster.Random.Count, "random with certain steps");
        });

        c.Group("the groups say what the machines are", () =>
        {
            foreach (var name in Content.Registry.MonsterNames)
            {
                var states = Content.Registry.NewMonster(name).BuildStates();
                var leadsToRandom = states.OfType<MoveState>().Any(m => m.FollowUp is RandomBranch);
                var leadsToConditional = states.OfType<MoveState>().Where(m => m.FollowUp is ConditionalBranch).Select(m => $"{name}.{m.FollowUp!.Id}").ToList();
                c.Equal(Forecast.Roster.Random.Contains(name), leadsToRandom, $"{name}: a move leads into a random branch");
                // The one branch with a rule: the Wriggler's, by its slot.
                c.Sequence(name == "Wriggler" ? new List<string> { "Wriggler.INIT_MOVE" } : new List<string>(), leadsToConditional, $"{name}: conditional branches a walk can meet");
                c.That(states.OfType<RandomBranch>().All(b => !b.HasComputedWeights), $"{name}: every weight is a constant");
                c.That(states.OfType<MoveState>().All(m => m.FollowUp is not null), $"{name}: every move has a follow-up");
                c.That(states.OfType<MoveState>().All(m => m.Intents.Count > 0 && m.Intents.All(i => i.Type is not (IntentType.Escape or IntentType.DeathBlow))), $"{name}: every move shows something, and none ends the monster");
                c.That(states.OfType<MoveState>().All(m => m.Intents.All(i => !i.IsAttack || i.Type == IntentType.Attack)), $"{name}: attack intents are plain attacks");
            }
        });

        c.Group("the Gains table is what the moves do: every Strength a move gives its own monster, and no other", () =>
        {
            var found = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var vulnerable = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var name in Content.Registry.MonsterNames)
            {
                foreach (var (move, (strength, vulnerability)) in MoveEffects(name))
                {
                    if (strength != 0) found[$"{name}.{move}"] = strength;
                    if (vulnerability != 0) vulnerable.Add($"{name}.{move}");
                }
            }
            var table = new SortedDictionary<string, int>(Forecast.Roster.Gains.ToDictionary(g => $"{g.Key.Monster}.{g.Key.Move}", g => g.Value), StringComparer.Ordinal);
            c.Sequence(table.Select(g => $"{g.Key} {g.Value}"), found.Select(g => $"{g.Key} {g.Value}"), "Strength by move");
            c.Sequence(Forecast.Roster.Vulnerable.Select(v => $"{v.Monster}.{v.Move}").Order(StringComparer.Ordinal), vulnerable, "the moves that put Vulnerable on the player");
        });

        c.Group("Strength that comes by itself at the end of an enemy's turn: Territorial, and nothing else a monster starts with", () =>
        {
            // Every monster as it enters its fight, played two passive turns with its first move replaced by one that
            // does nothing to Strength: whatever Strength it then has came from a power, not from a move.
            foreach (var encounter in Content.Registry.EncounterNames)
            {
                var f = new FightBuilder().Hp(900, 900).Encounter(encounter).Start();
                foreach (var enemy in f.Enemies)
                {
                    var perTurn = enemy.PowerAmount<TerritorialPower>();
                    var others = enemy.Powers.Where(p => p is not TerritorialPower).Select(p => p.ClassName).ToList();
                    c.That(others.All(p => p is "ArtifactPower" or "SlipperyPower" or "SlowPower" or "InfestedPower" or "MinionPower" or "IllusionPower"),
                        $"{encounter}: {enemy.Name} starts with [{string.Join(", ", others)}], and each of those was read: none gives Strength");
                    c.Equal(enemy.Monster!.ClassName == "Byrdonis" ? 1 : 0, perTurn, $"{encounter}: {enemy.Name}'s Territorial");
                }
            }
        });
    }
}
