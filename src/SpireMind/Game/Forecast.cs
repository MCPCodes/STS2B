using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.MonsterMoves;
using M = MegaCrit.Sts2.Core.Models.Monsters;

namespace SpireMind.Game;

/// <summary>
/// "next" for an enemy in a combat snapshot: the moves it makes after the intent it is showing,
/// as far as they are certain, in the words <see cref="Describe.Intents"/> uses for the shown
/// one. Read from the live move machine (docs/forecast.md): a move's follow-up is plain data,
/// a random pick's chances follow from its weights and the move log, and a conditional branch
/// is followed only where a rule written for that monster makes its answer certain.
///
/// Nothing here changes the game: no roll, no random number, no state entered or left. A wrong
/// forecast is worse than none, so every doubt ends the list early, a monster that was not
/// reviewed gets none, and any exception drops the enemy's whole forecast. Main thread only.
/// </summary>
internal static class Forecast
{
    /// <summary>The note that goes with "next" in a snapshot, once per snapshot.</summary>
    public const string Hint =
        "next = this enemy's moves after its shown intent, in order and only as far as they are certain "
        + "(a last entry with % is a random pick at exactly those chances); the list ends where the move after "
        + "depends on what happens in the fight, and no next means unknown, not nothing. A number already counts "
        + "the Strength the enemy gains by then and otherwise assumes today's Weak, Vulnerable and other modifiers; "
        + "~ marks one that something already in play will change. What you do can replace the list: a stun, waking it, "
        + "or an HP threshold or death named in its powers.";

    /// <summary>The build every rule and table in this file was read from. Any other build gets no forecast.</summary>
    private const string ReviewedVersion = "v0.107.1";
    private const string ReviewedCommit = "59260271";

    private const string UnsetMoveId = "UNSET_MOVE";

    /// <summary>More steps than this are never walked, whatever the caller asks for.</summary>
    private const int StepCap = 6;

    /// <summary>Failed reads before the feature gives up for the session.</summary>
    private const int FailLimit = 3;

    private static bool _off;
    private static bool _ready;
    private static int _failures;
    private static readonly HashSet<string> LoggedClasses = new();

    /// <summary>
    /// The enemy's coming moves after its shown intent, at most <paramref name="maxSteps"/> short
    /// strings ("attack 4x3", "buff", "attack 18 + buff", "50% debuff | 50% attack 11"), or null
    /// when nothing is certain or the forecast is not available. Never an empty array: that would
    /// read as "it does nothing".
    /// </summary>
    public static JsonArray? For(Creature enemy, int maxSteps = 3)
    {
        if (_off || enemy is null || maxSteps < 1) return null;
        Type? cls = null;
        try
        {
            if (!Ready()) return null;
            if (enemy.Monster is not { } monster) return null;
            cls = monster.GetType();
            if (!Roster.Reviewed.Contains(cls)) return null;
            if (Fight.Read(enemy) is not { } fight) return null;

            // Nothing below may change the game. The monster AI stream, the move log and the
            // shown move are compared afterwards; a difference would mean a read was not pure.
            var stream = monster.RunRng?.MonsterAi;
            var drawn = stream?.Counter;
            var logged = monster.MoveStateMachine?.StateLog.Count;
            var shown = monster.NextMove;

            var next = Build(fight, enemy, Math.Min(maxSteps, StepCap));

            if (stream?.Counter != drawn || monster.MoveStateMachine?.StateLog.Count != logged || !ReferenceEquals(monster.NextMove, shown))
            {
                TurnOff($"reading {cls.Name} changed the game's own state, which must never happen");
                return null;
            }
            return next;
        }
        catch (Exception ex)
        {
            Failed(cls, ex);
            return null;
        }
    }

    // ---------- the arithmetic, on plain data: no game object below this line until "the live fight" ----------

    /// <summary>How soon a random option may come again, as the game's MoveRepeatType.</summary>
    internal enum Repeat { Forever, Times, Never, Once }

    /// <summary>One option of a random pick: the move it leads to, its repeat rule, its cooldown and its weight when allowed.</summary>
    internal readonly record struct Option(string Id, Repeat Rule, int MaxTimes, int Cooldown, float Weight);

    /// <summary>One move a random pick can give, with its chance.</summary>
    internal readonly record struct Odds(string Id, double Chance);

    /// <summary>A state of a move machine reduced to what the walk needs.</summary>
    internal abstract record PlanState;

    /// <summary>A move: what follows it (null: nothing) and whether the monster is gone once it has made it.</summary>
    internal sealed record PlanMove(string Id, string? Next, bool Ends) : PlanState;

    /// <summary>A random pick. The options are asked for at the visit; null means "cannot be known here".</summary>
    internal sealed record PlanRandom(Func<PlanWalk, IReadOnlyList<Option>?> Options) : PlanState;

    /// <summary>A conditional branch. The answer is the next state's id when it is certain at the time the game will ask, else null.</summary>
    internal sealed record PlanChoice(Func<PlanWalk, string?> Pick) : PlanState;

    /// <summary>What a rule may look at while one step is worked out.</summary>
    internal sealed class PlanWalk
    {
        /// <summary>The step being worked out: 1 is the move chosen at the next roll, after one more enemy turn has ended.</summary>
        public int Step;

        /// <summary>A private copy of the move log, with every walked move appended as the game will log it.</summary>
        public List<string> Log = new();

        /// <summary>The shown move, then every walked move: all of them are performed before this step's roll.</summary>
        public List<string> Passed = new();
    }

    /// <summary>One coming step: a certain move, or the chances of the first uncertain one.</summary>
    internal sealed record PlanStep(string? Move, IReadOnlyList<Odds>? Odds);

    /// <summary>The walked steps. Open: what comes after them is not known (false: the step limit, or the monster is gone).</summary>
    internal sealed record Outlook(List<PlanStep> Steps, bool Open);

    /// <summary>Branches passed inside one step before the walk gives up (a guard; no machine of this build needs more than two).</summary>
    internal const int MaxHops = 8;

    /// <summary>
    /// The weight one option has at a roll, given the moves logged so far (oldest first). Use
    /// once: not if it is anywhere in the log. Cannot repeat (n = 1) and at most n in a row: not
    /// if the log has n entries or more and its last n are all this move. A cooldown of c: not if
    /// it is among the last c entries, whatever the repeat rule says.
    /// </summary>
    internal static float Weight(Option option, IReadOnlyList<string> log)
    {
        for (var back = 1; back <= option.Cooldown && back <= log.Count; back++)
            if (log[log.Count - back] == option.Id) return 0f;

        var allowed = option.Rule switch
        {
            Repeat.Once => !log.Contains(option.Id),
            Repeat.Never => !EndsWith(log, option.Id, 1),
            Repeat.Times => !EndsWith(log, option.Id, option.MaxTimes),
            _ => true,
        };
        return allowed ? option.Weight : 0f;
    }

    private static bool EndsWith(IReadOnlyList<string> log, string id, int count)
    {
        if (log.Count < count) return false;
        for (var back = 1; back <= count; back++)
            if (log[log.Count - back] != id) return false;
        return true;
    }

    /// <summary>
    /// The chance of each move at one roll: weight over the sum of weights, in declared order.
    /// When every option is blocked the game takes the first declared one, so that one is
    /// certain. One entry means certain. Empty means the weights make no sense: say nothing.
    /// </summary>
    internal static List<Odds> Chances(IReadOnlyList<Option> options, IReadOnlyList<string> log)
    {
        var odds = new List<Odds>();
        if (options.Count == 0) return odds;

        var weights = new float[options.Count];
        var total = 0f;
        for (var i = 0; i < options.Count; i++)
        {
            var weight = Weight(options[i], log);
            if (!(weight >= 0f) || float.IsInfinity(weight)) return odds;
            weights[i] = weight;
            total += weight;
        }

        if (total <= 0f)
        {
            odds.Add(new Odds(options[0].Id, 1.0));
            return odds;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (weights[i] <= 0f) continue;
            var chance = (double)weights[i] / total;
            var seen = odds.FindIndex(o => o.Id == options[i].Id);
            if (seen < 0) odds.Add(new Odds(options[i].Id, chance));
            else odds[seen] = new Odds(options[i].Id, odds[seen].Chance + chance);
        }
        return odds;
    }

    /// <summary>
    /// Walks a machine from the shown move: fixed follow-ups, random picks that leave one option,
    /// and branches whose rule gives a certain answer. It stops at the first step that is not
    /// certain; a random pick with several open options is then the last step, as its chances.
    /// <paramref name="find"/> gives the state behind an id, or null when there is none or it is
    /// of a kind the walk does not know.
    /// </summary>
    internal static Outlook Plan(PlanMove shown, IEnumerable<string> log, int maxSteps, Func<string, PlanState?> find)
    {
        var walk = new PlanWalk { Log = log.ToList() };
        walk.Passed.Add(shown.Id);
        var steps = new List<PlanStep>();
        var at = shown;

        for (var step = 1; step <= maxSteps; step++)
        {
            if (at.Ends) return new Outlook(steps, false);
            walk.Step = step;

            var id = at.Next;
            PlanMove? move = null;
            for (var hops = 0; move is null;)
            {
                if (string.IsNullOrEmpty(id)) return new Outlook(steps, true);
                switch (find(id))
                {
                    case PlanMove found:
                        move = found;
                        continue;
                    case PlanChoice choice:
                        id = choice.Pick(walk);
                        break;
                    case PlanRandom random:
                        if (random.Options(walk) is not { } options) return new Outlook(steps, true);
                        var odds = Chances(options, walk.Log);
                        if (odds.Count == 0) return new Outlook(steps, true);
                        if (odds.Count > 1)
                        {
                            if (odds.All(o => find(o.Id) is PlanMove)) steps.Add(new PlanStep(null, odds));
                            return new Outlook(steps, true);
                        }
                        id = odds[0].Id;
                        break;
                    default:
                        return new Outlook(steps, true);
                }
                if (++hops > MaxHops) return new Outlook(steps, true);
            }

            steps.Add(new PlanStep(move.Id, null));
            walk.Log.Add(move.Id);
            walk.Passed.Add(move.Id);
            at = move;
        }
        return new Outlook(steps, false);
    }

    /// <summary>What a move, once performed, does to its own monster's later hits: it attacked (which spends Vigor), and what it gained after.</summary>
    internal readonly record struct Effect(bool Attacks, int Strength = 0, int Ritual = 0, int Vigor = 0, bool Unknown = false);

    /// <summary>
    /// What an enemy holds now that moves its Strength without any move: Strength at the end of
    /// every enemy turn (Territorial, High Voltage), Ritual and whether it still skips its first
    /// turn, temporary Strength that is undone at the end of the coming enemy turn (positive: it
    /// comes back), and Vigor, which goes into its next attack only.
    /// </summary>
    internal readonly record struct Standing(int PerTurn = 0, int Ritual = 0, bool RitualWaits = false, int Temporary = 0, int Vigor = 0);

    /// <summary>
    /// The Strength (Vigor counted the same way) an enemy has for the hit at <paramref name="step"/>
    /// beyond what it has now. <paramref name="before"/> is what the shown move and the walked
    /// moves do, in order; the first <paramref name="step"/> of them are performed before that
    /// hit, and <paramref name="step"/> enemy turns end before it. <paramref name="unsure"/>:
    /// a gain of unknown size lies in between, so the number must be marked.
    /// </summary>
    internal static int Projected(int step, IReadOnlyList<Effect> before, Standing now, out bool unsure)
    {
        unsure = false;
        var extra = step * now.PerTurn + now.Ritual * Math.Max(0, step - (now.RitualWaits ? 1 : 0)) + now.Temporary;
        var vigor = now.Vigor;
        var hasRitual = now.Ritual != 0;

        for (var at = 0; at < step && at < before.Count; at++)
        {
            var move = before[at];
            // Vigor goes into every hit of the next attack and is gone after it; a gain comes after the move's own hit.
            if (move.Attacks) vigor = 0;
            vigor += move.Vigor;
            extra += move.Strength;
            unsure |= move.Unknown;
            if (move.Ritual == 0) continue;
            // Ritual gives nothing at the end of the turn it lands in, then its amount every turn.
            // Ritual on top of Ritual was never read through: mark it.
            if (hasRitual) unsure = true;
            else extra += move.Ritual * Math.Max(0, step - at - 1);
            hasRitual = true;
        }
        return extra + vigor - now.Vigor;
    }

    /// <summary>
    /// A duration debuff (Weak, Vulnerable) of this amount is still there for the hit at
    /// <paramref name="step"/>: it loses 1 at the end of each enemy turn, the first one skipped
    /// when the game has flagged it so.
    /// </summary>
    internal static bool Lasts(int amount, bool skipsATick, int step) => amount - step + (skipsATick ? 1 : 0) >= 1;

    /// <summary>"attack 11", "attack 4x3"; with rough, "attack ~11", "attack ~4x3" (the hit count is never rough).</summary>
    internal static string AttackText(int perHit, int hits, bool rough)
    {
        var damage = (rough ? "~" : "") + perHit.ToString(CultureInfo.InvariantCulture);
        return hits > 1 ? $"attack {damage}x{hits.ToString(CultureInfo.InvariantCulture)}" : $"attack {damage}";
    }

    /// <summary>"50% debuff | 50% attack 11": whole percents, declared order, options that read the same merged.</summary>
    internal static string OddsText(IReadOnlyList<(string Text, double Chance)> options)
    {
        var merged = new List<(string Text, double Chance)>();
        foreach (var (text, chance) in options)
        {
            var seen = merged.FindIndex(m => m.Text == text);
            if (seen < 0) merged.Add((text, chance));
            else merged[seen] = (text, merged[seen].Chance + chance);
        }
        if (merged.Count == 1) return merged[0].Text;
        return string.Join(" | ", merged.Select(m =>
            ((int)Math.Round(m.Chance * 100, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) + "% " + m.Text));
    }

    // ---------- self-check (needs no game) ----------

    /// <summary>
    /// Checks the chance arithmetic and the walk against the worked examples of the recipe
    /// (Flyconid, Twig Slime M, Mawler, Fogmog, Two-Tailed Rat and small made-up machines). Touches
    /// no game object. The result starts with "forecast self-test: all" when every check passed,
    /// else it lists what failed. docs/forecast.md says how to run it.
    /// </summary>
    public static string SelfTest()
    {
        var failed = new List<string>();
        var checks = 0;
        void Same(string what, string got, string want)
        {
            checks++;
            if (got != want) failed.Add($"{what}: got [{got}], want [{want}]");
        }

        static string[] Ids(string log) => log.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        static string Show(IReadOnlyList<Odds> odds) => odds.Count == 0 ? "?" : OddsText(odds.Select(o => (o.Id, o.Chance)).ToList());
        static string Roll(Option[] options, string log) => Show(Chances(options, Ids(log)));
        static string Told(Outlook outlook) =>
            string.Join(" ", outlook.Steps.Select(s => s.Move ?? "(" + Show(s.Odds!) + ")")) + (outlook.Open ? " .." : " .");
        static Func<string, PlanState?> In(Dictionary<string, PlanState> states) => id => states.GetValueOrDefault(id);
        static PlanMove Move(string id, string? next, bool ends = false) => new(id, next, ends);

        // Flyconid: V vulnerable spores (cannot repeat, cooldown 3), F frail spores (cannot repeat,
        // cooldown 2), S smash (cannot repeat). The recipe's table, last entry of the log showing.
        Option[] flyconid = { new("V", Repeat.Never, 0, 3, 1f), new("F", Repeat.Never, 0, 2, 1f), new("S", Repeat.Never, 0, 0, 1f) };
        Same("Flyconid [S]", Roll(flyconid, "S"), "50% V | 50% F");
        Same("Flyconid [F]", Roll(flyconid, "F"), "50% V | 50% S");
        Same("Flyconid [S F]", Roll(flyconid, "S F"), "50% V | 50% S");
        Same("Flyconid [F S]", Roll(flyconid, "F S"), "V");
        Same("Flyconid [F V]", Roll(flyconid, "F V"), "S");
        Same("Flyconid [S V]", Roll(flyconid, "S V"), "50% F | 50% S");
        Same("Flyconid [S F V]", Roll(flyconid, "S F V"), "S");
        Same("Flyconid [S V F]", Roll(flyconid, "S V F"), "S");
        Same("Flyconid [V F S], all blocked", Roll(flyconid, "V F S"), "V");
        Same("Flyconid [S V F S], all blocked", Roll(flyconid, "S V F S"), "V");
        Option[] flyconidFirst = { new("F", Repeat.Never, 0, 2, 1f), new("S", Repeat.Never, 0, 0, 1f) };
        Same("Flyconid first move", Roll(flyconidFirst, ""), "50% F | 50% S");

        var flyconidMachine = new Dictionary<string, PlanState>
        {
            ["V"] = Move("V", "RAND"), ["F"] = Move("F", "RAND"), ["S"] = Move("S", "RAND"),
            ["RAND"] = new PlanRandom(_ => flyconid),
        };
        Same("Flyconid walk 3", Told(Plan(Move("V", "RAND"), Ids("S F V"), 3, In(flyconidMachine))), "S F S .");
        Same("Flyconid walk 5", Told(Plan(Move("V", "RAND"), Ids("S F V"), 5, In(flyconidMachine))), "S F S V (50% F | 50% S) ..");
        Same("Flyconid frail spores showing", Told(Plan(Move("F", "RAND"), Ids("S F"), 3, In(flyconidMachine))), "(50% V | 50% S) ..");

        // Twig Slime M: P pounce (at most 2 in a row), T sticky shot (cannot repeat); starts with T.
        Option[] twig = { new("P", Repeat.Times, 2, 0, 1f), new("T", Repeat.Never, 0, 0, 1f) };
        Same("Twig Slime [T]", Roll(twig, "T"), "P");
        Same("Twig Slime [T P]", Roll(twig, "T P"), "50% P | 50% T");
        Same("Twig Slime [T P P]", Roll(twig, "T P P"), "T");
        Same("Twig Slime [P], log shorter than the limit", Roll(twig, "P"), "50% P | 50% T");
        var twigMachine = new Dictionary<string, PlanState>
        {
            ["P"] = Move("P", "RAND"), ["T"] = Move("T", "RAND"), ["RAND"] = new PlanRandom(_ => twig),
        };
        Same("Twig Slime turn 1", Told(Plan(Move("T", "RAND"), Ids("T"), 3, In(twigMachine))), "P (50% P | 50% T) ..");
        // Stunned while its first pounce shows: the stun's follow-up is the pounce, which is then logged a second time.
        Same("Twig Slime stunned", Told(Plan(Move("STUNNED", "P"), Ids("T P"), 3, In(twigMachine))), "P T P .");

        // Mawler: R rip (cannot repeat), O roar (once per fight), C claw (cannot repeat); starts with C.
        Option[] mawler = { new("R", Repeat.Never, 0, 0, 1f), new("O", Repeat.Once, 0, 0, 1f), new("C", Repeat.Never, 0, 0, 1f) };
        Same("Mawler [C]", Roll(mawler, "C"), "50% R | 50% O");
        Same("Mawler [C O]", Roll(mawler, "C O"), "50% R | 50% C");
        Same("Mawler [C O R]", Roll(mawler, "C O R"), "C");
        Same("Mawler [C R]", Roll(mawler, "C R"), "50% O | 50% C");

        // Fogmog: after its swipe, 40% a second swipe, 60% the headbutt.
        Option[] fogmog = { new("SWIPE2", Repeat.Never, 0, 0, 0.4f), new("BUTT", Repeat.Never, 0, 0, 0.6f) };
        var fogmogMachine = new Dictionary<string, PlanState>
        {
            ["ILLUSION"] = Move("ILLUSION", "SWIPE"), ["SWIPE"] = Move("SWIPE", "BRANCH"), ["SWIPE2"] = Move("SWIPE2", "BUTT"),
            ["BUTT"] = Move("BUTT", "SWIPE"), ["BRANCH"] = new PlanRandom(_ => fogmog),
        };
        Same("Fogmog turn 1", Told(Plan(Move("ILLUSION", "SWIPE"), Ids("ILLUSION"), 3, In(fogmogMachine))), "SWIPE (40% SWIPE2 | 60% BUTT) ..");
        Same("Fogmog after the second swipe", Told(Plan(Move("SWIPE2", "BUTT"), Ids("ILLUSION SWIPE SWIPE2"), 2, In(fogmogMachine))), "BUTT SWIPE .");

        // Two-Tailed Rat: S scratch, D bite, R screech (cooldown 3), B backup (once). While it cannot
        // summon the weights are 1, 1, 1, 0; while it can, 1/12 each against 0.75 (never printed live).
        Option[] rat = { new("S", Repeat.Never, 0, 0, 1f), new("D", Repeat.Never, 0, 0, 1f), new("R", Repeat.Never, 0, 3, 1f), new("B", Repeat.Once, 0, 0, 0f) };
        Same("Rat [S]", Roll(rat, "S"), "50% D | 50% R");
        Same("Rat [R S]", Roll(rat, "R S"), "D");
        Same("Rat first move", Roll(rat, ""), "33% S | 33% D | 33% R");
        Option[] ratSummons = { new("S", Repeat.Never, 0, 0, 1f / 12f), new("D", Repeat.Never, 0, 0, 1f / 12f), new("R", Repeat.Never, 0, 3, 1f / 12f), new("B", Repeat.Once, 0, 0, 0.75f) };
        Same("Rat that may summon [S D]", Roll(ratSummons, "S D"), "9% S | 9% R | 82% B");
        Same("Rat that may summon [R S]", Roll(ratSummons, "R S"), "10% D | 90% B");

        // Repeat rules at their edges.
        Option[] edge = { new("A", Repeat.Times, 0, 0, 1f), new("B", Repeat.Forever, 0, 0, 3f) };
        Same("at most 0 in a row is never open", Roll(edge, ""), "B");
        Same("no options", Roll(Array.Empty<Option>(), "A"), "?");
        Same("a negative weight says nothing", Roll(new[] { new Option("A", Repeat.Forever, 0, 0, -1f), new Option("B", Repeat.Forever, 0, 0, 1f) }, ""), "?");
        Same("weights 1 and 3", Roll(new[] { new Option("A", Repeat.Forever, 0, 0, 1f), new Option("B", Repeat.Forever, 0, 0, 3f) }, "A A"), "25% A | 75% B");

        // A fixed cycle (Byrdonis: swoop and peck in turn).
        var byrdonis = new Dictionary<string, PlanState> { ["SWOOP"] = Move("SWOOP", "PECK"), ["PECK"] = Move("PECK", "SWOOP") };
        Same("fixed cycle", Told(Plan(Move("SWOOP", "PECK"), Ids("SWOOP"), 4, In(byrdonis))), "PECK SWOOP PECK SWOOP .");

        // A branch on a counter that the monster's own moves raise (Knowledge Demon: the curse comes
        // while fewer than 3 curses have been performed, counting those still ahead in the walk).
        Dictionary<string, PlanState> Demon(int curses) => new()
        {
            ["CURSE"] = Move("CURSE", "SLAP"), ["SLAP"] = Move("SLAP", "OVERWHELM"), ["OVERWHELM"] = Move("OVERWHELM", "PONDER"),
            ["PONDER"] = Move("PONDER", "BRANCH"),
            ["BRANCH"] = new PlanChoice(walk => curses + walk.Passed.Count(id => id == "CURSE") < 3 ? "CURSE" : "SLAP"),
        };
        Same("counter branch, second curse showing", Told(Plan(Move("CURSE", "SLAP"), Ids("CURSE"), 5, In(Demon(1)))), "SLAP OVERWHELM PONDER CURSE SLAP .");
        Same("counter branch, third curse showing", Told(Plan(Move("CURSE", "SLAP"), Ids("CURSE"), 5, In(Demon(2)))), "SLAP OVERWHELM PONDER SLAP OVERWHELM .");

        // A branch on a power that counts down at the end of each enemy turn (Lagavulin Matriarch, 3 turns asleep).
        var sleeper = new Dictionary<string, PlanState>
        {
            ["SLEEP"] = Move("SLEEP", "BRANCH"), ["SLASH"] = Move("SLASH", "SLASH"),
            ["BRANCH"] = new PlanChoice(walk => 3 - walk.Step >= 1 ? "SLEEP" : "SLASH"),
        };
        Same("countdown branch", Told(Plan(Move("SLEEP", "BRANCH"), Ids("SLEEP"), 4, In(sleeper))), "SLEEP SLEEP SLASH SLASH .");

        // A branch that leads to a random pick (Fabricator while it may still summon), and one with no certain answer.
        var fabricator = new Dictionary<string, PlanState>
        {
            ["MAKE"] = Move("MAKE", "BRANCH"), ["STRIKE"] = Move("STRIKE", "BRANCH"), ["BRANCH"] = new PlanChoice(_ => "RAND"),
            ["RAND"] = new PlanRandom(_ => new[] { new Option("MAKE", Repeat.Forever, 0, 0, 1f), new Option("STRIKE", Repeat.Forever, 0, 0, 1f) }),
        };
        Same("branch into a random pick", Told(Plan(Move("MAKE", "BRANCH"), Ids("MAKE"), 3, In(fabricator))), "(50% MAKE | 50% STRIKE) ..");
        var undecided = new Dictionary<string, PlanState> { ["BUFF"] = Move("BUFF", "BRANCH"), ["LASH"] = Move("LASH", "BUFF"), ["BRANCH"] = new PlanChoice(_ => null) };
        Same("branch without a certain answer", Told(Plan(Move("LASH", "BUFF"), Ids("LASH"), 3, In(undecided))), "BUFF ..");

        // Where the walk must stop.
        var ending = new Dictionary<string, PlanState> { ["BLOW"] = Move("BLOW", "BLOW", ends: true), ["WAIT"] = Move("WAIT", "BLOW") };
        Same("the shown move ends the monster", Told(Plan(Move("BLOW", "BLOW", ends: true), Ids(""), 3, In(ending))), " .");
        Same("a walked move ends the monster", Told(Plan(Move("WAIT", "BLOW"), Ids(""), 3, In(ending))), "BLOW .");
        Same("no follow-up", Told(Plan(Move("A", null), Ids("A"), 3, In(ending))), " ..");
        Same("follow-up not in the machine", Told(Plan(Move("A", "NOWHERE"), Ids("A"), 3, In(ending))), " ..");
        var loop = new Dictionary<string, PlanState> { ["X"] = new PlanChoice(_ => "Y"), ["Y"] = new PlanChoice(_ => "X") };
        Same("branches in a circle", Told(Plan(Move("A", "X"), Ids("A"), 3, In(loop))), " ..");
        var odd = new Dictionary<string, PlanState>
        {
            ["A"] = Move("A", "RAND"),
            ["RAND"] = new PlanRandom(_ => new[] { new Option("A", Repeat.Forever, 0, 0, 1f), new Option("X", Repeat.Forever, 0, 0, 1f) }),
            ["X"] = new PlanChoice(_ => "A"),
        };
        Same("a random option that is not a move", Told(Plan(Move("A", "RAND"), Ids("A"), 3, In(odd))), " ..");
        var unknown = new Dictionary<string, PlanState> { ["A"] = Move("A", "RAND"), ["RAND"] = new PlanRandom(_ => null) };
        Same("a random pick whose weights are not known", Told(Plan(Move("A", "RAND"), Ids("A"), 3, In(unknown))), " ..");

        // Strength ahead. Byrdonis has Territorial 1: one more Strength at the end of every enemy turn.
        static string Ahead(Effect[] before, Standing now, int steps) =>
            string.Join(" ", Enumerable.Range(1, steps).Select(step => Projected(step, before, now, out var unsure) + (unsure ? "?" : "")));
        var hit = new Effect(Attacks: true);
        var idle = new Effect(Attacks: false);
        Same("Territorial 1", Ahead(new[] { hit, hit, hit }, new Standing(PerTurn: 1), 3), "1 2 3");
        // Nibbit, butt showing: slice, hiss (2 Strength), butt again.
        Same("a buff move two steps ahead", Ahead(new[] { hit, hit, new Effect(false, Strength: 2) }, default, 3), "0 0 2");
        // Ceremonial Beast: stamp showing, then the plow (2 Strength after its hit) every turn: 18, 20, 22.
        var plow = new Effect(true, Strength: 2);
        Same("gain after the hit, stamp showing", Ahead(new[] { idle, plow, plow }, default, 3), "0 2 4");
        Same("gain after the hit, plow showing", Ahead(new[] { plow, plow, plow }, default, 3), "2 4 6");
        // Cultists: Ritual gives nothing at the end of the turn it is applied in.
        Same("Ritual about to be applied", Ahead(new[] { new Effect(false, Ritual: 2), hit, hit }, default, 3), "0 2 4");
        Same("Ritual in place", Ahead(new[] { hit, hit, hit }, new Standing(Ritual: 2), 3), "2 4 6");
        Same("Ritual just applied", Ahead(new[] { hit, hit, hit }, new Standing(Ritual: 2, RitualWaits: true), 3), "0 2 4");
        Same("Ritual on Ritual is marked", Ahead(new[] { new Effect(false, Ritual: 2), hit }, new Standing(Ritual: 2), 2), "2? 4?");
        // Terror Eel: its thrash gives 6 Vigor after its hits, spent by the crash that follows.
        var thrash = new Effect(true, Vigor: 6);
        Same("Vigor, crash showing with 6", Ahead(new[] { hit, thrash, hit }, new Standing(Vigor: 6), 3), "-6 0 -6");
        Same("Vigor, thrash showing with none", Ahead(new[] { thrash, hit, thrash }, default, 3), "6 0 6");
        Same("Vigor waits for an attack", Ahead(new[] { idle, idle, hit }, new Standing(Vigor: 4), 3), "0 0 -4");
        // Temporary Strength is undone at the end of the coming enemy turn: every later hit is without it.
        Same("a Strength loss that comes back", Ahead(new[] { hit, hit }, new Standing(Temporary: 3), 2), "3 3");
        Same("a gain of unknown size", Ahead(new[] { hit, new Effect(false, Unknown: true), hit }, default, 3), "0 0? 0?");

        // Durations: 2 Weak covers the next hit only; a tick still to be skipped adds one.
        Same("duration", string.Join(" ", Enumerable.Range(1, 3).Select(step => Lasts(2, false, step))), "True False False");
        Same("duration with a skipped tick", string.Join(" ", Enumerable.Range(1, 3).Select(step => Lasts(2, true, step))), "True True False");

        // The words.
        Same("attack", AttackText(11, 1, false), "attack 11");
        Same("multi attack", AttackText(4, 3, false), "attack 4x3");
        Same("rough attack", AttackText(11, 1, true), "attack ~11");
        Same("rough multi attack", AttackText(4, 3, true), "attack ~4x3");
        Same("chances", OddsText(new[] { ("debuff", 0.5), ("attack 11", 0.5) }), "50% debuff | 50% attack 11");
        Same("chances that read the same are merged", OddsText(new[] { ("attack 7", 1.0 / 3), ("buff", 1.0 / 3), ("attack 7", 1.0 / 3) }), "67% attack 7 | 33% buff");
        Same("chances that all read the same", OddsText(new[] { ("attack 7", 0.5), ("attack 7", 0.5) }), "attack 7");

        // Added in review, each worked out by hand from the game's weight routine.
        // Mawler, further logs: the roar is use-once, and a stun on the showing roar logs it twice.
        Same("Mawler [C R C]", Roll(mawler, "C R C"), "50% R | 50% O");
        Same("Mawler [C R O]", Roll(mawler, "C R O"), "50% R | 50% C");
        Same("Mawler [C O O], stunned on its roar", Roll(mawler, "C O O"), "50% R | 50% C");
        var mawlerMachine = new Dictionary<string, PlanState>
        {
            ["R"] = Move("R", "RAND"), ["O"] = Move("O", "RAND"), ["C"] = Move("C", "RAND"), ["RAND"] = new PlanRandom(_ => mawler),
        };
        Same("Mawler once the roar is used", Told(Plan(Move("C", "RAND"), Ids("C O R C"), 3, In(mawlerMachine))), "R C R .");
        Same("Mawler with the roar still open", Told(Plan(Move("R", "RAND"), Ids("C R"), 3, In(mawlerMachine))), "(50% O | 50% C) ..");
        Same("Mawler stunned while the roar shows", Told(Plan(Move("STUNNED", "O"), Ids("C O"), 3, In(mawlerMachine))), "O (50% R | 50% C) ..");

        // Leaf Slime S: K tackle and G goop, neither twice in a row; its first move is the pick itself.
        Option[] leaf = { new("K", Repeat.Never, 0, 0, 1f), new("G", Repeat.Never, 0, 0, 1f) };
        Same("Leaf Slime S first move", Roll(leaf, ""), "50% K | 50% G");
        Same("Leaf Slime S [K]", Roll(leaf, "K"), "G");
        Same("Leaf Slime S [K G]", Roll(leaf, "K G"), "K");
        var leafMachine = new Dictionary<string, PlanState> { ["K"] = Move("K", "RAND"), ["G"] = Move("G", "RAND"), ["RAND"] = new PlanRandom(_ => leaf) };
        Same("Leaf Slime S alternates", Told(Plan(Move("G", "RAND"), Ids("G"), 4, In(leafMachine))), "K G K G .");
        // Stunned while the goop shows: the goop is logged twice, the tackle still follows it.
        Same("Leaf Slime S stunned", Told(Plan(Move("STUNNED", "G"), Ids("K G"), 3, In(leafMachine))), "G K G .");

        // Flyconid after the forced spores, and stunned while the spores show: they are logged twice,
        // which pushes the frail spores out of their cooldown window of 2 a turn early.
        Same("Flyconid [S V F S V]", Roll(flyconid, "S V F S V"), "50% F | 50% S");
        Same("Flyconid [F V V], stunned on its spores", Roll(flyconid, "F V V"), "50% F | 50% S");
        Same("Flyconid [F V], not stunned", Roll(flyconid, "F V"), "S");
        Same("Flyconid all blocked, then on", Told(Plan(Move("S", "RAND"), Ids("V F S"), 2, In(flyconidMachine))), "V (50% F | 50% S) ..");

        // Twig Slime M: two pounces in the copied log close the pounce at the next roll of the same walk.
        Same("Twig Slime after one pounce", Told(Plan(Move("P", "RAND"), Ids("T P"), 3, In(twigMachine))), "(50% P | 50% T) ..");
        Same("Twig Slime after two pounces", Told(Plan(Move("P", "RAND"), Ids("T P P"), 3, In(twigMachine))), "T P (50% P | 50% T) ..");

        // Fossil Stalker: three moves, each at most twice in a row.
        Option[] stalker = { new("L", Repeat.Times, 2, 0, 1f), new("T", Repeat.Times, 2, 0, 1f), new("H", Repeat.Times, 2, 0, 1f) };
        Same("Fossil Stalker [L]", Roll(stalker, "L"), "33% L | 33% T | 33% H");
        Same("Fossil Stalker [L L]", Roll(stalker, "L L"), "50% T | 50% H");
        Same("Fossil Stalker [L L T T]", Roll(stalker, "L L T T"), "50% L | 50% H");

        // Exoskeleton: K skitter leads to the pick, M mandibles to E enrage, E to the pick (K or M, neither twice).
        Option[] exo = { new("K", Repeat.Never, 0, 0, 1f), new("M", Repeat.Never, 0, 0, 1f) };
        var exoMachine = new Dictionary<string, PlanState>
        {
            ["K"] = Move("K", "RAND"), ["M"] = Move("M", "E"), ["E"] = Move("E", "RAND"), ["RAND"] = new PlanRandom(_ => exo),
        };
        Same("Exoskeleton after its skitter", Told(Plan(Move("K", "RAND"), Ids("K"), 4, In(exoMachine))), "M E (50% K | 50% M) ..");

        // A Decimillipede segment coming back: the reattach is logged, so all three attacks are open.
        Option[] segment = { new("W", Repeat.Never, 0, 0, 1f), new("B", Repeat.Never, 0, 0, 1f), new("C", Repeat.Never, 0, 0, 1f) };
        var segmentMachine = new Dictionary<string, PlanState>
        {
            ["W"] = Move("W", "C"), ["B"] = Move("B", "W"), ["C"] = Move("C", "B"), ["DEAD"] = Move("DEAD", "REATTACH"),
            ["REATTACH"] = Move("REATTACH", "RAND"), ["RAND"] = new PlanRandom(_ => segment),
        };
        Same("segment down", Told(Plan(Move("DEAD", "REATTACH"), Ids("C B W"), 3, In(segmentMachine))), "REATTACH (33% W | 33% B | 33% C) ..");
        Same("segment back in its cycle", Told(Plan(Move("B", "W"), Ids("C B W REATTACH B"), 3, In(segmentMachine))), "W C B .");

        // A move that changes its monster's later hits by an unknown amount (Tough Egg's hatch strips its powers).
        Same("unknown change at the shown move", Ahead(new[] { new Effect(false, Unknown: true), hit, hit }, new Standing(PerTurn: 1), 3), "1? 2? 3?");
        Same("Ritual from a move, Territorial beside it", Ahead(new[] { new Effect(false, Ritual: 5), hit, hit }, new Standing(PerTurn: 1), 3), "1 7 13");
        Same("a Strength gain that runs out", Ahead(new[] { hit, hit }, new Standing(Temporary: -4), 2), "-4 -4");
        Same("duration 1 is gone for the next hit", Lasts(1, false, 1).ToString(), "False");
        Same("duration 1 with a skipped tick covers one more", Lasts(1, true, 1) + " " + Lasts(1, true, 2), "True False");
        Same("thirds", OddsText(new[] { ("a", 1.0 / 3), ("b", 1.0 / 3), ("c", 1.0 / 3) }), "33% a | 33% b | 33% c");

        return failed.Count == 0
            ? $"forecast self-test: all {checks} checks passed"
            : $"forecast self-test FAILED, {failed.Count} of {checks}: " + string.Join("; ", failed);
    }

    // ---------- the live fight ----------

    /// <summary>What every enemy of one snapshot shares. Targets is what the intent calls take, as Describe passes it.</summary>
    private sealed record Fight(ICombatState Cs, Player Me, List<Creature> Targets, List<Creature> Enemies)
    {
        /// <summary>
        /// Null unless the shown intents are still to come: the fight is running, it is the
        /// players' side, and the local player's phase is past None, which the game sets at the
        /// top of every turn start and leaves just before it rolls the enemies' moves.
        /// </summary>
        public static Fight? Read(Creature enemy)
        {
            var cm = CombatManager.Instance;
            if (cm is null || !cm.IsInProgress || cm.IsOverOrEnding) return null;
            if (enemy.CombatState is not { } cs || cs.CurrentSide != CombatSide.Player) return null;
            // The overload that takes the combat state throws when the local player is not in it.
            if (LocalContext.GetMe(cs.Players) is not { } me) return null;
            if (me.PlayerCombatState is not { } pcs || pcs.Phase == PlayerTurnPhase.None) return null;
            if (!cs.Enemies.Contains(enemy)) return null;
            return new Fight(cs, me, cs.Players.Select(p => p.Creature).ToList(), cs.Enemies.ToList());
        }
    }

    /// <summary>One enemy's walk: Moves[0] is the shown move, then each certain step; Odds is the chance list after them, if any.</summary>
    private sealed record Seen(Creature Creature, Type Cls, MonsterMoveStateMachine Machine, List<MoveState> Moves, IReadOnlyList<Odds>? Odds, bool Open);

    private static JsonArray? Build(Fight fight, Creature enemy, int maxSteps)
    {
        if (Look(enemy, maxSteps) is not { } seen) return null;
        var numbers = Numbers.Read(fight, seen, maxSteps);

        var next = new JsonArray();
        for (var step = 1; step < seen.Moves.Count; step++)
        {
            // A step that cannot be put into words ends the list; what is certain before it stays.
            if (numbers.Text(seen.Moves[step], step) is not { } text) return next.Count > 0 ? next : null;
            next.Add(text);
        }

        if (seen.Odds is { } odds)
        {
            var options = new List<(string Text, double Chance)>();
            foreach (var option in odds)
            {
                if (numbers.Text((MoveState)seen.Machine.States[option.Id], seen.Moves.Count) is not { } text) return next.Count > 0 ? next : null;
                options.Add((text, option.Chance));
            }
            next.Add(OddsText(options));
        }
        return next.Count > 0 ? next : null;
    }

    /// <summary>
    /// Walks one enemy, or null when its shown move is not a sound place to start from: not a
    /// reviewed class, no machine, not rolled yet, acting right now, already performed (the
    /// intent is then last turn's), or not the machine's own current state.
    /// </summary>
    private static Seen? Look(Creature creature, int maxSteps)
    {
        if (creature.Monster is not { } monster) return null;
        var cls = monster.GetType();
        if (!Roster.Reviewed.Contains(cls)) return null;
        if (monster.IsPerformingMove || monster.MoveStateMachine is not { } machine) return null;

        var shown = monster.NextMove;
        if (shown is null || shown.GetType() != typeof(MoveState) || shown.Id == UnsetMoveId) return null;
        if (Members.PerformedOnce.GetValue(shown) is not false) return null;
        if (!ReferenceEquals(Members.CurrentState.GetValue(machine), shown)) return null;
        if (MoveOf(shown) is not { } start) return null;

        var outlook = Plan(start, machine.StateLog.Select(s => s.Id), maxSteps, id => StateOf(creature, monster, machine, id));

        var moves = new List<MoveState> { shown };
        IReadOnlyList<Odds>? odds = null;
        foreach (var step in outlook.Steps)
        {
            if (step.Move is { } id) moves.Add((MoveState)machine.States[id]);
            else odds = step.Odds;
        }
        return new Seen(creature, cls, machine, moves, odds, outlook.Open);
    }

    /// <summary>
    /// The state the machine holds under an id, as the game looks it up at a roll. The three
    /// state classes are tested by exact type: they are open to subclassing, and a subclass
    /// could decide its follow-up in code.
    /// </summary>
    private static PlanState? StateOf(Creature creature, MonsterModel monster, MonsterMoveStateMachine machine, string id)
    {
        if (!machine.States.TryGetValue(id, out var state) || state is null) return null;
        var kind = state.GetType();
        if (kind == typeof(MoveState)) return MoveOf((MoveState)state);
        if (kind == typeof(RandomBranchState))
        {
            var branch = (RandomBranchState)state;
            return new PlanRandom(walk => Options(creature, monster, branch, walk));
        }
        if (kind == typeof(ConditionalBranchState))
        {
            var branch = (ConditionalBranchState)state;
            return new PlanChoice(walk => Pick(creature, monster, branch, walk));
        }
        return null;
    }

    /// <summary>A move as the walk sees it; null when one of its intents is of a kind this file does not know.</summary>
    private static PlanMove? MoveOf(MoveState move)
    {
        var ends = false;
        foreach (var intent in move.Intents)
        {
            if (intent is null || !Roster.Intents.Contains(intent.GetType())) return null;
            // After an escape or a death blow the monster is gone.
            ends |= intent is EscapeIntent or DeathBlowIntent;
        }
        return new PlanMove(move.Id, move.FollowUpState?.Id ?? move.FollowUpStateId, ends);
    }

    // ---------- per-monster rules ----------

    private const string RatBackup = "CALL_FOR_BACKUP_MOVE";
    private static readonly string[] RatOrdinary = { "SCRATCH_MOVE", "DISEASE_BITE_MOVE", "SCREECH_MOVE" };

    /// <summary>
    /// The options of a random branch with their weights, or null when the weights cannot be
    /// stood behind. A weight is a delegate; it is only called for the classes whose weights
    /// were read and found to be constants. Two-Tailed Rat's are not constants and are never
    /// called: while it certainly cannot summon they are known to be 1, 1, 1 and 0.
    /// </summary>
    private static IReadOnlyList<Option>? Options(Creature creature, MonsterModel monster, RandomBranchState branch, PlanWalk walk)
    {
        var cls = monster.GetType();
        var rat = monster as M.TwoTailedRat;
        if (rat is null && !Roster.ConstantWeights.Contains(cls)) return null;
        if (rat is not null && !CannotSummon(rat, walk)) return null;

        var options = new List<Option>();
        foreach (var entry in branch.States)
        {
            if (string.IsNullOrEmpty(entry.stateId)) return null;
            Repeat rule;
            switch (entry.repeatType)
            {
                case MoveRepeatType.CanRepeatForever: rule = Repeat.Forever; break;
                case MoveRepeatType.CanRepeatXTimes: rule = Repeat.Times; break;
                case MoveRepeatType.CannotRepeat: rule = Repeat.Never; break;
                case MoveRepeatType.UseOnlyOnce: rule = Repeat.Once; break;
                default: return null;
            }

            float weight;
            if (rat is not null)
            {
                if (entry.stateId == RatBackup) weight = 0f;
                else if (RatOrdinary.Contains(entry.stateId)) weight = 1f;
                else return null;
            }
            else
            {
                if (entry.weightLambda is null) return null;
                weight = entry.weightLambda();
            }
            options.Add(new Option(entry.stateId, rule, entry.maxTimes, entry.cooldown, weight));
        }

        // At the first roll the copied log is still the real one, so the game's own weight
        // routine (private, read-only) must give the same numbers as the arithmetic above.
        if (rat is null && walk.Step == 1)
        {
            for (var i = 0; i < options.Count; i++)
            {
                var theirs = (float)Members.StateWeight.Invoke(null, new object[] { branch.States[i], creature })!;
                var ours = Weight(options[i], walk.Log);
                if (theirs != ours)
                    throw new InvalidOperationException($"the game weighs {options[i].Id} at {theirs}, the forecast at {ours}");
            }
        }
        return options;
    }

    /// <summary>
    /// True when the rat certainly cannot call for backup at this roll: its countdown (minus one
    /// for each ordinary move it performs first) is still above 0, the shared count has reached
    /// 3, or it has made the call already. A stun or a kill only pushes these further that way.
    /// </summary>
    private static bool CannotSummon(M.TwoTailedRat rat, PlanWalk walk)
    {
        var turns = (int)Members.TurnsUntilSummonable.GetValue(rat)! - walk.Passed.Count(id => RatOrdinary.Contains(id));
        return turns > 0 || rat.CallForBackupCount >= 3 || walk.Log.Contains(RatBackup);
    }

    /// <summary>
    /// The state a conditional branch will give, when that is certain at the time the game asks
    /// (the start of a later player turn, after the moves in <see cref="PlanWalk.Passed"/> and
    /// <see cref="PlanWalk.Step"/> ends of enemy turns). Never "what the condition says now".
    /// Null for a branch that turns on what the player does, and for any branch without a rule.
    /// </summary>
    private static string? Pick(Creature creature, MonsterModel monster, ConditionalBranchState branch, PlanWalk walk)
    {
        switch (monster)
        {
            // The curse comes while fewer than 3 have been performed; each one performed first counts.
            case M.KnowledgeDemon when branch.Id == "CurseOfKnowledgeBranch":
                var curses = (int)Members.CurseCounter.GetValue(monster)! + walk.Passed.Count(id => id == "CURSE_OF_KNOWLEDGE_MOVE");
                return curses < 3 ? "CURSE_OF_KNOWLEDGE_MOVE" : "SLAP_MOVE";

            // The respawn move raises the counter itself before this branch is asked.
            case M.TestSubject when branch.Id == "REVIVE_BRANCH":
                var respawns = (int)Members.Respawns.GetValue(monster)! + walk.Passed.Count(id => id == "RESPAWN_MOVE");
                return respawns < 2 ? "MULTI_CLAW_MOVE" : "PHASE3_LACERATE_MOVE";

            // Asleep and Slumber lose 1 at the end of every enemy turn and are gone at 0.
            case M.LagavulinMatriarch when branch.Id == "SLEEP_BRANCH":
                return creature.GetPowerAmount<AsleepPower>() - walk.Step >= 1 ? "SLEEP_MOVE" : "SLASH_MOVE";
            case M.SlumberingBeetle when branch.Id == "SNORE_NEXT":
                return creature.GetPowerAmount<SlumberPower>() - walk.Step >= 1 ? "SNORE_MOVE" : "ROLL_OUT_MOVE";

            // Once it has charged it never charges again; before that the answer is its HP at the time.
            case M.FrogKnight when branch.Id == "HALF_HEALTH":
                return (bool)Members.BeetleCharged.GetValue(monster)! || walk.Passed.Contains("BEETLE_CHARGE") ? "TONGUE_LASH" : null;

            // One way only: once the Amalgam has died the flag stays set.
            case M.Queen when branch.Id is "YOURE_MINE_NOW_BRANCH" or "BURN_BRIGHT_FOR_ME_BRANCH":
                return (bool)Members.AmalgamDied.GetValue(monster)! ? "OFF_WITH_YOUR_HEAD_MOVE" : null;

            // Fewer than 4 alive on its side at the roll means the random pick. Nothing raises the
            // count but its own summons, so "alive now plus those still to come" is the most it can be.
            case M.Fabricator when branch.Id == "fabricateBranch":
                var alive = creature.CombatState!.GetTeammatesOf(creature).Count(c => c.IsAlive)
                    + 2 * walk.Passed.Count(id => id == "FABRICATE_MOVE") + walk.Passed.Count(id => id == "FABRICATING_STRIKE_MOVE");
                return alive < 4 ? "RAND" : null;

            // By slot name, fixed for the fight: the branch's own answer now is its answer then.
            case M.Wriggler when branch.Id == "INIT_MOVE":
                return branch.GetNextState(creature, null!);

            // Living Shield and Ovicopter turn on which enemies are alive at the time. Bowlbug Rock's
            // branch always gives the headbutt, but whether the branch is reached turns on the player's
            // Block: a fully blocked headbutt stuns it for a turn first, which is ordinary play, not a
            // rare trigger. So nothing is said after a shown headbutt.
            default:
                return null;
        }
    }

    // ---------- numbers ----------

    /// <summary>
    /// The words and numbers of one enemy's coming moves. A number is the game's own preview of
    /// the hit against the local player at today's modifiers, plus the Strength the enemy is
    /// known to gain first: from its own moves (the Gains table), from Territorial, High Voltage
    /// and Ritual at the end of each enemy turn, from temporary Strength running out, and with
    /// Vigor followed through its attacks. Where something else is known to change the hit and
    /// is not worked in, the number carries "~".
    /// </summary>
    private sealed class Numbers
    {
        private readonly Fight _fight;
        private readonly Seen _seen;
        private readonly Creature _enemy;
        private readonly Standing _now;
        private readonly List<Effect> _effects;
        private readonly PowerModel? _weak;
        private readonly PowerModel? _shrink;
        private readonly PowerModel? _vulnerable;
        private readonly bool _rough;
        private readonly int _vulnerableFrom;

        private Numbers(Fight fight, Seen seen, int maxSteps)
        {
            _fight = fight;
            _seen = seen;
            _enemy = seen.Creature;
            var me = fight.Me.Creature;

            // Ritual skips the end of the turn it was applied in; the game keeps that in a private flag.
            var ritual = _enemy.GetPower<RitualPower>();
            // Temporary Strength (the player's cards and potions) is undone at the end of the
            // enemy turn: the shown move has it, no later one does.
            var temporary = 0;
            foreach (var power in _enemy.GetPowerInstances<TemporaryStrengthPower>())
                temporary += power.Type == PowerType.Debuff ? power.Amount : -power.Amount;
            _now = new Standing(
                PerTurn: _enemy.GetPowerAmount<TerritorialPower>() + _enemy.GetPowerAmount<HighVoltagePower>(),
                Ritual: ritual?.Amount ?? 0,
                RitualWaits: ritual is not null && (bool)Members.RitualJustApplied.GetValue(ritual)!,
                Temporary: temporary,
                Vigor: _enemy.GetPowerAmount<VigorPower>());

            var deadly = AscensionHelper.HasAscension(AscensionLevel.DeadlyEnemies);
            _effects = seen.Moves.Select(move => EffectOf(seen.Cls, move, deadly)).ToList();

            _weak = _enemy.GetPower<WeakPower>();
            _shrink = _enemy.GetPower<ShrinkPower>();
            _vulnerable = me.GetPower<VulnerablePower>();

            // Things that change this enemy's hits in ways that are not worked out here.
            _rough = _enemy.HasPower<SuckPower>()                                    // Strength for each hit that gets through
                || _enemy.HasPower<EnragePower>()                                    // Strength for every Skill the player plays
                || fight.Cs.Players.Any(p => p.GetRelic<Brimstone>() is not null)   // Strength to every enemy each player turn
                || me.HasPower<ColossusPower>() || me.HasPower<CoveredPower>() || me.HasPower<DiamondDiademPower>()
                || me.HasPower<InterceptPower>() || me.HasPower<TaintedPower>()      // short powers that change the damage taken
                || _enemy.HasPower<DebilitatePower>() || me.HasPower<DebilitatePower>() // doubles Weak and Vulnerable, and ends before they do
                || me.HasPower<SurroundedPower>();                                   // half again from behind, and the player turns around

            _vulnerableFrom = int.MaxValue;
            var actsFirst = true;
            foreach (var other in fight.Enemies)
            {
                var self = ReferenceEquals(other, _enemy);
                if (self) actsFirst = false;

                var cls = other.Monster?.GetType();
                if (cls is null || !Roster.Reviewed.Contains(cls))
                {
                    // An enemy nobody read could do anything to the others.
                    _rough = true;
                    continue;
                }

                // The Queen (while her Amalgam lives) and The Obscura hand out Strength to the others.
                if (!self && other.IsAlive
                    && (cls == typeof(M.TheObscura) || (cls == typeof(M.Queen) && !(bool)Members.AmalgamDied.GetValue(other.Monster)!)))
                    _rough = true;

                // From the turn a move puts Vulnerable on the player, every hit is half again as
                // hard. An enemy earlier in the list acts first, so its move counts for the same turn.
                if (!Roster.VulnerableClasses.Contains(cls)) continue;
                var first = FirstVulnerable(self ? seen : Look(other, maxSteps), cls);
                if (first == int.MaxValue) continue;
                _vulnerableFrom = Math.Min(_vulnerableFrom, !self && actsFirst ? first : first + 1);
            }
        }

        /// <summary>What a move does to its own monster's later hits once performed, from the Gains table.</summary>
        private static Effect EffectOf(Type cls, MoveState move, bool deadly)
        {
            var attacks = move.Intents.Any(i => i is AttackIntent);
            if (!Roster.Gains.TryGetValue((cls, move.Id), out var gain)) return new Effect(attacks);
            var amount = deadly ? gain.Deadly : gain.Amount;
            return gain.Kind switch
            {
                GainKind.Strength => new Effect(attacks, Strength: amount),
                GainKind.Ritual => new Effect(attacks, Ritual: amount),
                GainKind.Vigor => new Effect(attacks, Vigor: amount),
                _ => new Effect(attacks, Unknown: true),
            };
        }

        public static Numbers Read(Fight fight, Seen seen, int maxSteps) => new(fight, seen, maxSteps);

        /// <summary>The first position (0 = its shown move) at which this enemy makes, or may make, a move that applies Vulnerable.</summary>
        private static int FirstVulnerable(Seen? seen, Type cls)
        {
            if (seen is null) return 0;
            for (var at = 0; at < seen.Moves.Count; at++)
                if (Roster.Vulnerable.Contains((cls, seen.Moves[at].Id))) return at;
            return seen.Open ? seen.Moves.Count : int.MaxValue;
        }

        /// <summary>
        /// A move's intents as the snapshot words them, joined with " + ", for the move at
        /// <paramref name="step"/> (1 = the next one). Null when it cannot be put into words.
        /// </summary>
        public string? Text(MoveState move, int step)
        {
            var parts = new List<string>();
            foreach (var intent in move.Intents)
            {
                if (intent is not AttackIntent attack)
                {
                    parts.Add(Label(intent, _fight.Targets, _enemy));
                    continue;
                }
                if (attack.DamageCalc is not { } calc) return null;

                var extra = Extra(step, out var rough);
                var hits = Math.Max(1, attack.Repeats);
                decimal? from = null;

                // The five intents whose number or hit count is worked out live by the monster.
                switch (_enemy.Monster)
                {
                    case M.TestSubject when move.Id == "MULTI_CLAW_MOVE":
                        hits += Performed(step, "MULTI_CLAW_MOVE");           // one more hit after each use
                        break;
                    case M.WaterfallGiant when move.Id == "PRESSURE_GUN_MOVE":
                        extra += 5 * Performed(step, "PRESSURE_GUN_MOVE");    // 5 more damage after each use
                        break;
                    case M.WaterfallGiant when move.Id == "EXPLODE_MOVE":
                        // Its damage is only stored when "about to blow" is performed: the amount of Steam Eruption.
                        if (step != 1 || _seen.Moves[0].Id != "ABOUT_TO_BLOW_MOVE") return null;
                        from = _enemy.GetPowerAmount<SteamEruptionPower>();
                        break;
                    case M.TheForgotten when move.Id == "DREAD":
                        extra += 2 * Performed(step, "MIASMA");               // its damage counts its Dexterity; Miasma gives 2
                        break;
                }

                int perHit;
                if (from is null && extra == 0)
                {
                    // The call Describe makes for the shown intent.
                    perHit = attack.GetSingleDamage(_fight.Targets, _enemy);
                    from = calc();
                }
                else
                {
                    // Strength is added before anything multiplies, so "with E more Strength" is the preview of base + E.
                    from = (from ?? calc()) + extra;
                    perHit = Math.Max(0, (int)Preview(from.Value, ModifyDamageHookType.All));
                }

                // A cap (the player's Intangible) decides today's number and counts down too.
                rough |= Preview(from.Value, ModifyDamageHookType.All) != Preview(from.Value, ModifyDamageHookType.Additive | ModifyDamageHookType.Multiplicative);
                parts.Add(AttackText(perHit, hits, rough));
            }
            return parts.Count > 0 ? string.Join(" + ", parts) : null;
        }

        /// <summary>The game's own preview of a monster hit on the local player, as AttackIntent.GetSingleDamage calls it.</summary>
        private decimal Preview(decimal damage, ModifyDamageHookType hooks) =>
            Hook.ModifyDamage(_fight.Me.RunState, _fight.Me.Creature.CombatState, _fight.Me.Creature, _enemy, damage,
                ValueProp.Move, null, hooks, CardPreviewMode.None, out _);

        /// <summary>How often a move is performed before the step: the shown move and the walked ones before it.</summary>
        private int Performed(int step, string moveId) => _seen.Moves.Take(step).Count(m => m.Id == moveId);

        /// <summary>
        /// The Strength this enemy has at <paramref name="step"/> beyond today's, and whether the
        /// number must be marked because something else changes the hit before then.
        /// </summary>
        private decimal Extra(int step, out bool rough)
        {
            var extra = Projected(step, _effects, _now, out var unsure);
            rough = _rough || unsure
                || Fades(_weak, step)                                           // the hit will be harder
                || (_shrink is { Amount: > 0 } && _shrink.Amount - step < 1)    // the same, for Shrink
                || Fades(_vulnerable, step)                                     // the hit will be softer
                || (step >= _vulnerableFrom && !Stays(_vulnerable, step));      // Vulnerable is on its way
            return extra;
        }

        private static bool Stays(PowerModel? power, int step) => power is not null && Lasts(power.Amount, power.SkipNextDurationTick, step);

        private static bool Fades(PowerModel? power, int step) => power is { Amount: > 0 } && !Stays(power, step);
    }

    /// <summary>
    /// A non-attack intent in the snapshot's words: its kind, with the game's label when that says
    /// more ("statuscard: 3"). The same lines as in <see cref="Describe.Intents"/>; keep the two in step.
    /// </summary>
    internal static string Label(AbstractIntent intent, IEnumerable<Creature> targets, Creature enemy)
    {
        var label = Describe.SafeText(() => intent.GetIntentLabel(targets, enemy).GetFormattedText());
        var kind = intent.IntentType.ToString().ToLowerInvariant();
        return label.Length > 0 && !label.Equals(kind, StringComparison.OrdinalIgnoreCase) ? $"{kind}: {label}" : kind;
    }

    // ---------- what was reviewed ----------

    /// <summary>Unknown: the move changes its monster's later hits by an amount this file does not work out.</summary>
    private enum GainKind { Strength, Ritual, Vigor, Unknown }

    /// <summary>What a move gives its own monster after it is performed; Deadly is the amount at the ascension level that makes enemies deadlier.</summary>
    private readonly record struct Gain(GainKind Kind, int Amount, int Deadly);

    /// <summary>
    /// The monster classes, intents and moves this file may speak about. All of it was read from
    /// the reviewed build's code and holds for that build only (<see cref="Ready"/>). A typeof
    /// that no longer compiles after a game update is the first sign that this needs a new look.
    /// </summary>
    private static class Roster
    {
        /// <summary>Every follow-up is a move (after a start-of-fight branch at most).</summary>
        private static readonly Type[] Fixed =
        {
            typeof(M.Aeonglass), typeof(M.AssassinRubyRaider), typeof(M.AxeRubyRaider), typeof(M.Axebot), typeof(M.BowlbugEgg),
            typeof(M.BowlbugNectar), typeof(M.BowlbugSilk), typeof(M.BruteRubyRaider), typeof(M.BygoneEffigy), typeof(M.Byrdonis),
            typeof(M.CalcifiedCultist), typeof(M.CeremonialBeast), typeof(M.Chomper), typeof(M.CorpseSlug), typeof(M.CrossbowRubyRaider),
            typeof(M.Crusher), typeof(M.CubexConstruct), typeof(M.DampCultist), typeof(M.DevotedSculptor), typeof(M.Entomancer),
            typeof(M.EyeWithTeeth), typeof(M.FatGremlin), typeof(M.FuzzyWurmCrawler), typeof(M.GasBomb), typeof(M.GlobeHead),
            typeof(M.GremlinMerc), typeof(M.Guardbot), typeof(M.HauntedShip), typeof(M.InfestedPrism), typeof(M.KinFollower),
            typeof(M.KinPriest), typeof(M.LeafSlimeM), typeof(M.LivingFog), typeof(M.LouseProgenitor), typeof(M.MagiKnight),
            typeof(M.MechaKnight), typeof(M.Myte), typeof(M.Nibbit), typeof(M.Noisebot), typeof(M.OwlMagistrate),
            typeof(M.Parafright), typeof(M.PhantasmalGardener), typeof(M.PhrogParasite), typeof(M.PunchConstruct), typeof(M.Rocket),
            typeof(M.Seapunk), typeof(M.SewerClam), typeof(M.ShrinkerBeetle), typeof(M.SkulkingColony), typeof(M.SlimedBerserker),
            typeof(M.SnappingJaxfruit), typeof(M.SneakyGremlin), typeof(M.SoulFysh), typeof(M.SpinyToad), typeof(M.Stabbot),
            typeof(M.TerrorEel), typeof(M.TheForgotten), typeof(M.TheInsatiable), typeof(M.TheLost), typeof(M.ThievingHopper),
            typeof(M.Toadpole), typeof(M.TorchHeadAmalgam), typeof(M.ToughEgg), typeof(M.TrackerRubyRaider), typeof(M.Tunneler),
            typeof(M.TurretOperator), typeof(M.TwigSlimeS), typeof(M.Vantom), typeof(M.VineShambler), typeof(M.WaterfallGiant),
            typeof(M.Wriggler), typeof(M.Zapbot),
        };

        /// <summary>A conditional branch inside the loop: each has its rule in <see cref="Pick"/>, or stops there.</summary>
        private static readonly Type[] Conditional =
        {
            typeof(M.BowlbugRock), typeof(M.Fabricator), typeof(M.FrogKnight), typeof(M.KnowledgeDemon), typeof(M.LagavulinMatriarch),
            typeof(M.LivingShield), typeof(M.Ovicopter), typeof(M.Queen), typeof(M.SlumberingBeetle), typeof(M.TestSubject),
        };

        /// <summary>A random pick inside the loop, with weights that are constants.</summary>
        private static readonly Type[] Random =
        {
            typeof(M.DecimillipedeSegmentBack), typeof(M.DecimillipedeSegmentFront), typeof(M.DecimillipedeSegmentMiddle),
            typeof(M.Exoskeleton), typeof(M.FakeMerchantMonster), typeof(M.FlailKnight), typeof(M.Flyconid), typeof(M.Fogmog),
            typeof(M.FossilStalker), typeof(M.HunterKiller), typeof(M.Inklet), typeof(M.LeafSlimeS), typeof(M.Mawler),
            typeof(M.MysteriousKnight), typeof(M.ScrollOfBiting), typeof(M.SlitheringStrangler), typeof(M.SludgeSpinner),
            typeof(M.SoulNexus), typeof(M.SpectralKnight), typeof(M.TheObscura), typeof(M.TwigSlimeM),
        };

        /// <summary>The enemy classes of the reviewed build. Any other class gets no forecast and none of its delegates is called.</summary>
        public static readonly HashSet<Type> Reviewed = new(Fixed.Concat(Conditional).Concat(Random).Append(typeof(M.TwoTailedRat)));

        /// <summary>Classes whose random weights may be asked for: constants, the same now as at the roll. Not Two-Tailed Rat.</summary>
        public static readonly HashSet<Type> ConstantWeights = new(Random.Append(typeof(M.Fabricator)));

        /// <summary>Every intent class of the reviewed build, by exact type.</summary>
        public static readonly HashSet<Type> Intents = new()
        {
            typeof(SingleAttackIntent), typeof(MultiAttackIntent), typeof(DeathBlowIntent), typeof(BuffIntent), typeof(DebuffIntent),
            typeof(CardDebuffIntent), typeof(DefendIntent), typeof(EscapeIntent), typeof(HealIntent), typeof(HiddenIntent),
            typeof(SleepIntent), typeof(StatusIntent), typeof(StunIntent), typeof(SummonIntent), typeof(UnknownIntent),
        };

        /// <summary>
        /// What a move gives its own monster, read from each move's code (the gain always comes
        /// after the move's own hit). Two numbers: normal, then at the ascension level that makes
        /// enemies deadlier. Used for numbers only, never to decide which move comes.
        /// </summary>
        public static readonly Dictionary<(Type, string), Gain> Gains = new()
        {
            [(typeof(M.BowlbugNectar), "BUFF_MOVE")] = Strength(15, 16),
            [(typeof(M.BruteRubyRaider), "ROAR_MOVE")] = Strength(3),
            [(typeof(M.BygoneEffigy), "WAKE_MOVE")] = Strength(10),
            [(typeof(M.CeremonialBeast), "PLOW_MOVE")] = Strength(2),
            [(typeof(M.CeremonialBeast), "CRUSH_MOVE")] = Strength(3, 4),
            [(typeof(M.Crusher), "ADAPT_MOVE")] = Strength(2, 3),
            [(typeof(M.CubexConstruct), "CHARGE_UP_MOVE")] = Strength(2),
            [(typeof(M.CubexConstruct), "REPEATER_BLAST_MOVE")] = Strength(2),
            [(typeof(M.CubexConstruct), "REPEATER_BLAST_MOVE_2")] = Strength(2),
            [(typeof(M.DecimillipedeSegmentBack), "BULK_MOVE")] = Strength(2),
            [(typeof(M.DecimillipedeSegmentFront), "BULK_MOVE")] = Strength(2),
            [(typeof(M.DecimillipedeSegmentMiddle), "BULK_MOVE")] = Strength(2),
            [(typeof(M.Exoskeleton), "ENRAGE_MOVE")] = Strength(2),
            [(typeof(M.FakeMerchantMonster), "ENRAGE_MOVE")] = Strength(2),
            [(typeof(M.FlailKnight), "WAR_CHANT")] = Strength(3),
            [(typeof(M.MysteriousKnight), "WAR_CHANT")] = Strength(3),
            [(typeof(M.Fogmog), "SWIPE_MOVE")] = Strength(1),
            [(typeof(M.Fogmog), "SWIPE_RANDOM_MOVE")] = Strength(1),
            [(typeof(M.FrogKnight), "FOR_THE_QUEEN")] = Strength(5),
            [(typeof(M.FuzzyWurmCrawler), "INHALE")] = Strength(7),
            [(typeof(M.GlobeHead), "GALVANIC_BURST")] = Strength(2),
            [(typeof(M.GremlinMerc), "HEHE_MOVE")] = Strength(2),
            [(typeof(M.KinFollower), "POWER_DANCE_MOVE")] = Strength(2, 3),
            [(typeof(M.KinPriest), "RITUAL_MOVE")] = Strength(2, 3),
            [(typeof(M.KnowledgeDemon), "PONDER_MOVE")] = Strength(2, 3),
            [(typeof(M.LagavulinMatriarch), "SOUL_SIPHON_MOVE")] = Strength(2),
            [(typeof(M.LivingShield), "SMASH_MOVE")] = Strength(3),
            [(typeof(M.LouseProgenitor), "CURL_AND_GROW_MOVE")] = Strength(5),
            [(typeof(M.MechaKnight), "WINDUP_MOVE")] = Strength(5),
            [(typeof(M.Myte), "SUCK_MOVE")] = Strength(2, 3),
            [(typeof(M.Nibbit), "HISS_MOVE")] = Strength(2, 3),
            [(typeof(M.Ovicopter), "NUTRITIONAL_PASTE_MOVE")] = Strength(3, 4),
            [(typeof(M.PhantasmalGardener), "ENLARGE_MOVE")] = Strength(2, 3),
            [(typeof(M.Queen), "ENRAGE_MOVE")] = Strength(2),
            [(typeof(M.Rocket), "CHARGE_UP_MOVE")] = Strength(2, 3),
            [(typeof(M.ScrollOfBiting), "MORE_TEETH")] = Strength(2),
            [(typeof(M.Seapunk), "BUBBLE_BURP_MOVE")] = Strength(1, 2),
            [(typeof(M.SewerClam), "PRESSURIZE_MOVE")] = Strength(4),
            [(typeof(M.SkulkingColony), "INERTIA_MOVE")] = Strength(2, 4),
            [(typeof(M.SlimedBerserker), "LEECHING_HUG_MOVE")] = Strength(3),
            [(typeof(M.SludgeSpinner), "RAGE_MOVE")] = Strength(3),
            [(typeof(M.SlumberingBeetle), "ROLL_OUT_MOVE")] = Strength(2),
            [(typeof(M.SnappingJaxfruit), "ENERGY_ORB_MOVE")] = Strength(2),
            [(typeof(M.TestSubject), "BURNING_GROWL_MOVE")] = Strength(2, 3),
            [(typeof(M.TheInsatiable), "SALIVATE_MOVE")] = Strength(2, 3),
            [(typeof(M.TheLost), "DEBILITATING_SMOG")] = Strength(2),
            [(typeof(M.TheObscura), "SAIL_MOVE")] = Strength(3),
            [(typeof(M.TurretOperator), "RELOAD_MOVE")] = Strength(1),
            [(typeof(M.Vantom), "PREPARE_MOVE")] = Strength(2),
            [(typeof(M.Wriggler), "WRIGGLE_MOVE")] = Strength(2),

            [(typeof(M.CalcifiedCultist), "INCANTATION_MOVE")] = new(GainKind.Ritual, 2, 2),
            [(typeof(M.DampCultist), "INCANTATION_MOVE")] = new(GainKind.Ritual, 5, 6),
            [(typeof(M.DevotedSculptor), "FORBIDDEN_INCANTATION_MOVE")] = new(GainKind.Ritual, 9, 9),

            // After its hits: 6 Vigor, which goes into every hit of its next attack only.
            [(typeof(M.TerrorEel), "THRASH_MOVE")] = new(GainKind.Vigor, 6, 6),

            // Hatching removes every power the egg has but Minion: its Strength, and Weak or Shrink
            // on it, are gone for the hatchling's hits. Not worked out, so those hits are marked.
            [(typeof(M.ToughEgg), "HATCH_MOVE")] = new(GainKind.Unknown, 0, 0),

            // Strength whose size is not a constant: more each time, by its stock, by another power.
            [(typeof(M.Aeonglass), "INCREASING_INTENSITY_MOVE")] = new(GainKind.Unknown, 0, 0),
            [(typeof(M.Axebot), "BOOT_UP_MOVE")] = new(GainKind.Unknown, 0, 0),
            [(typeof(M.Entomancer), "PHEROMONE_SPIT_MOVE")] = new(GainKind.Unknown, 0, 0),
        };

        /// <summary>The moves that put Vulnerable on the player: of all that monsters apply, the only thing that changes a monster's hit.</summary>
        public static readonly HashSet<(Type, string)> Vulnerable = new()
        {
            (typeof(M.Flyconid), "VULNERABLE_SPORES_MOVE"), (typeof(M.Mawler), "ROAR_MOVE"), (typeof(M.Ovicopter), "TENDERIZER_MOVE"),
            (typeof(M.OwlMagistrate), "VERDICT"), (typeof(M.Queen), "YOU_ARE_MINE_MOVE"), (typeof(M.SoulFysh), "SCREAM_MOVE"),
            (typeof(M.SoulNexus), "DRAIN_LIFE_MOVE"), (typeof(M.TerrorEel), "TERROR_MOVE"), (typeof(M.TestSubject), "SKULL_BASH_MOVE"),
        };

        public static readonly HashSet<Type> VulnerableClasses = new(Vulnerable.Select(v => v.Item1));

        private static Gain Strength(int amount) => new(GainKind.Strength, amount, amount);

        private static Gain Strength(int amount, int deadly) => new(GainKind.Strength, amount, deadly);
    }

    // ---------- reflection, failures, the off switch ----------

    /// <summary>
    /// The private members the forecast reads, found once and checked for their type. A missing
    /// one (a game update) turns the whole feature off; a default is never guessed.
    /// </summary>
    private static class Members
    {
        public static FieldInfo CurrentState = null!;
        public static FieldInfo PerformedOnce = null!;
        public static FieldInfo CurseCounter = null!;
        public static FieldInfo Respawns = null!;
        public static FieldInfo BeetleCharged = null!;
        public static FieldInfo AmalgamDied = null!;
        public static FieldInfo TurnsUntilSummonable = null!;
        public static FieldInfo RitualJustApplied = null!;
        public static MethodInfo StateWeight = null!;

        /// <summary>The name of the first member that is not there as expected, or null when all are.</summary>
        public static string? Resolve()
        {
            string? missing = null;
            CurrentState = Field(typeof(MonsterMoveStateMachine), "_currentState", typeof(MonsterState), ref missing);
            PerformedOnce = Field(typeof(MoveState), "_performedAtLeastOnce", typeof(bool), ref missing);
            CurseCounter = Field(typeof(M.KnowledgeDemon), "_curseOfKnowledgeCounter", typeof(int), ref missing);
            Respawns = Field(typeof(M.TestSubject), "_respawns", typeof(int), ref missing);
            BeetleCharged = Field(typeof(M.FrogKnight), "_hasBeetleCharged", typeof(bool), ref missing);
            AmalgamDied = Field(typeof(M.Queen), "_hasAmalgamDied", typeof(bool), ref missing);
            TurnsUntilSummonable = Field(typeof(M.TwoTailedRat), "_turnsUntilSummonable", typeof(int), ref missing);
            RitualJustApplied = Field(typeof(RitualPower), "_wasJustAppliedByEnemy", typeof(bool), ref missing);

            var weight = typeof(RandomBranchState).GetMethod("GetStateWeight", BindingFlags.Static | BindingFlags.NonPublic, null,
                new[] { typeof(RandomBranchState.StateWeight), typeof(Creature) }, null);
            if (weight is null || weight.ReturnType != typeof(float)) missing ??= "RandomBranchState.GetStateWeight";
            StateWeight = weight!;
            return missing;
        }

        private static FieldInfo Field(Type owner, string name, Type type, ref string? missing)
        {
            var field = owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is null || field.FieldType != type) missing ??= $"{owner.Name}.{name}";
            return field!;
        }
    }

    /// <summary>Checked once: the game is the reviewed build and every private member is where it was read.</summary>
    private static bool Ready()
    {
        if (_ready) return true;

        var release = ReleaseInfoManager.Instance.ReleaseInfo;
        if (release is null || release.Version != ReviewedVersion || release.Commit?.StartsWith(ReviewedCommit, StringComparison.Ordinal) != true)
        {
            TurnOff($"the game is {release?.Version ?? "an unknown build"} ({release?.Commit ?? "no commit"}); "
                + $"the monsters were read for {ReviewedVersion} ({ReviewedCommit}) and must be read again first");
            return false;
        }

        if (Members.Resolve() is { } missing)
        {
            TurnOff($"{missing} is not in the game as it was read");
            return false;
        }

        _ready = true;
        return true;
    }

    private static void Failed(Type? cls, Exception ex)
    {
        var name = cls?.Name ?? "an enemy";
        if (LoggedClasses.Add(name))
            ModEntry.LogError($"forecast: no forecast for {name}: {ex.GetType().Name}: {ex.Message}");
        if (++_failures >= FailLimit) TurnOff($"{_failures} reads failed");
    }

    private static void TurnOff(string why)
    {
        if (_off) return;
        _off = true;
        ModEntry.LogError($"forecast: switched off for this session: {why}");
    }
}
