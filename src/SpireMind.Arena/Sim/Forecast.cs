using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The forecast experiment (<c>arena run --forecast</c>, <see cref="ExperimentOptions"/>'s
/// Forecast): each living enemy of a snapshot also says what it does after the intent it shows.
/// <c>next_moves</c> is a list of short strings, one per coming move, in order, in the words of
/// <c>intents</c> with a move's intents joined by " + ": <c>["attack 4x3", "attack 19"]</c>.
/// It lists moves only as far as they are certain, and at most <see cref="MostSteps"/> entries.
/// Where the next step is a random pick whose chances follow from the weights and the move log,
/// the last entry gives them: <c>"50% debuff | 50% attack 11"</c>. The key is left out when
/// nothing is known; there is never an empty list. One note, <c>next_moves_note</c>, stands
/// behind <c>enemies</c> on the board (the combat and hand selection snapshots) whenever some
/// enemy has the list. The keys are the mod's reader's (<c>CombatReader.EnemiesJson</c> and
/// <c>BoardState</c>); "next" alone is taken: the tools use it for the snapshot after an action.
/// <para>
/// <b>Where the real mod gets this.</b> From the game's live objects, by reading only. The recipe
/// is <c>sts2-notes/forecast-v0.107.1/forecast.md</c>; the mod's own version is
/// <c>src/SpireMind/Game/Forecast.cs</c>, described in <c>docs/forecast.md</c>, and this file shows
/// what that one shows: the same keys, words, marks and note. A move's follow-up is plain data on the move. A random
/// pick's options, weights, repeat rules and cooldowns are public data, so each option's chance
/// is its weight over the sum, against a private copy of the move log; the roll is never called.
/// A later attack's number is the game's own preview of that move's hit on the player, with the
/// Strength the enemy is known to gain first: from its own moves (a table read from the game's
/// code, <see cref="Roster.Gains"/>), from Territorial at each end of its turn, from temporary
/// Strength running out. Everything in this file below "the arithmetic" is that mod file's
/// arithmetic, kept line for line so the two cannot say different things; the part under "the
/// live fight" reads the simulator's objects where the mod reads the game's.
/// </para>
/// <para>
/// <b>What it never does.</b>
/// <list type="bullet">
/// <item>It draws no random number and looks at no random stream, and it changes neither a
/// machine nor a move log. A pick with more than one open option ends the list.</item>
/// <item>It foresees nothing that forces a move from outside the machine: a stun, an HP
/// threshold (the Ceremonial Beast's Plow), an enemy coming back from 0 HP. A summon is not
/// foreseen either: a new enemy gets its forecast once it is there. The forecast is built again
/// for every snapshot, so the next one is right again.</item>
/// <item>It follows a conditional branch only where a rule written for that monster makes the
/// answer certain (here: the Wriggler's slot). Anywhere else the list stops.</item>
/// <item>It prints no number plain that is known to come out different. A number carries "~"
/// when a debuff that shapes it runs out first (Weak or Shrink on the enemy, Vulnerable on the
/// player), when a coming move puts Vulnerable on the player or may, while the enemy or the
/// player has one of the powers the mod marks for (<see cref="Roster.EnemyMarks"/>,
/// <see cref="Roster.PlayerMarks"/>; of those the simulator has Colossus), and where a cap
/// decides today's number.</item>
/// <item>A monster class that was not read for this (<see cref="Roster.Reviewed"/>) gets no
/// forecast at all.</item>
/// </list>
/// </para>
/// The honesty check (<c>arena honesty</c>, Sim/HonestyForecast.cs) holds every entry against
/// what the enemy then does.
/// </summary>
public static class Forecast
{
    /// <summary>The enemy's key, as the mod's reader sets it in <c>CombatReader.EnemiesJson</c>.</summary>
    public const string NextKey = "next_moves";

    /// <summary>The key of the note, behind <c>enemies</c>, as the mod's reader sets it in <c>CombatReader.BoardState</c>.</summary>
    public const string HintKey = "next_moves_note";

    /// <summary>The note that goes with "next_moves" in a snapshot: the mod's <c>Forecast.Hint</c>, word for word
    /// (ForecastChecks reads the mod's file and fails when the two differ).</summary>
    public const string Hint =
        "next_moves = this enemy's moves after its shown intent, in order and only as far as they are certain "
        + "(a last entry with % is a random pick at exactly those chances); the list ends where the move after "
        + "depends on what happens in the fight, and no next_moves means unknown, not nothing. A number already counts "
        + "the Strength the enemy gains by then and otherwise assumes today's Weak, Vulnerable and other modifiers; "
        + "~ marks one that something already in play will change. What you do can replace the list: a stun, waking it, "
        + "or an HP threshold or death named in its powers.";

    /// <summary>The most entries <c>next_moves</c> has; a chance entry counts as one.</summary>
    public const int MostSteps = 3;

    /// <summary>More steps than this are never walked, whatever the caller asks for.</summary>
    private const int StepCap = 6;

    // ---------- what a snapshot gets ----------

    /// <summary>
    /// The enemy's coming moves after its shown intent as the snapshot lists them ("attack 4x3",
    /// "buff", "attack 18 + buff", "50% debuff | 50% attack 11"), or null when nothing is certain
    /// or no forecast can be made now. Never an empty list: that would read as "it does nothing".
    /// </summary>
    public static IReadOnlyList<string>? For(Fight f, Creature enemy, int maxSteps = MostSteps) => Read(f, enemy, maxSteps)?.Lines;

    /// <summary>
    /// Adds the forecast to a snapshot, wherever it lists the enemies with their intents, and
    /// exactly where the mod's reader puts it. Each enemy with a forecast gets
    /// <c>next_moves</c> behind <c>intents</c>: on the board of "combat" and "hand_select", and
    /// in the small picture of the fight a "card_select" carries under <c>combat</c> (the mod
    /// builds all three enemy lists with one routine, <c>CombatReader.EnemiesJson</c>, where its
    /// hook sits). The note, <c>next_moves_note</c>, goes behind <c>enemies</c> on the board
    /// only, when some enemy there has a forecast: the mod sets it in <c>BoardState</c>, which
    /// the card choice screens do not use, so their picture has the lists and no note. Nothing
    /// else of the snapshot changes.
    /// </summary>
    public static void AddTo(Fight f, GameSnapshot snapshot)
    {
        switch (snapshot.Screen)
        {
            case "combat" or "hand_select":
                AddTo(f, snapshot.State, withNote: true);
                break;
            case "card_select":
                if (snapshot.State["combat"] is JsonObject combat) AddTo(f, combat, withNote: false);
                break;
        }
    }

    private static void AddTo(Fight f, JsonObject holder, bool withNote)
    {
        if (holder["enemies"] is not JsonArray enemies) return;
        var any = false;
        foreach (var node in enemies)
        {
            if (node is not JsonObject entry || entry["i"] is not JsonValue index || !index.TryGetValue<int>(out var j) || j < 0 || j >= f.Roster.Count) continue;
            if (For(f, f.Roster[j]) is not { } lines) continue;
            var next = new JsonArray();
            foreach (var line in lines) next.Add(line);
            var at = entry.IndexOf("intents") + 1;
            entry.Insert(at > 0 ? at : entry.Count, NextKey, next);
            any = true;
        }
        if (any && withNote) holder.Insert(holder.IndexOf("enemies") + 1, HintKey, Hint);
    }

    // ---------- one enemy's forecast in full (what the checks judge) ----------

    /// <summary>A certain coming move: its id in the machine, its words, and whether one of its numbers carries "~".</summary>
    public sealed record Step(string MoveId, string Text, bool Rough);

    /// <summary>One move a random pick can give: its id, its words, its chance (0 to 1, not rounded), and whether a number carries "~".</summary>
    public sealed record Chance(string MoveId, string Text, double Probability, bool Rough);

    /// <summary>One enemy's forecast: the certain moves in order, then the options of the random pick after them (empty when there is none to tell).</summary>
    public sealed record Reading(IReadOnlyList<Step> Steps, IReadOnlyList<Chance> Odds)
    {
        /// <summary>What <c>next_moves</c> shows: one line per certain move, then one line for the pick.</summary>
        public IReadOnlyList<string> Lines
        {
            get
            {
                var lines = Steps.Select(s => s.Text).ToList();
                if (Odds.Count > 0) lines.Add(OddsText(Odds.Select(o => (o.Text, o.Probability)).ToList()));
                return lines;
            }
        }
    }

    /// <summary>A fault of the forecast itself (its arithmetic and the roll's disagree). Never caught here: the checks must see it.</summary>
    public sealed class Fault(string message) : Exception(message);

    /// <summary>
    /// What <paramref name="enemy"/> does after the move it shows, or null. Reads only. Null
    /// unless the shown intents are still to come (the fight is running, it is the player's side
    /// and the turn's roll has been made), the enemy is one the snapshot lists, its class was
    /// reviewed, and its shown move has not been performed since it was entered.
    /// </summary>
    public static Reading? Read(Fight f, Creature enemy, int maxSteps = MostSteps)
    {
        if (maxSteps < 1) return null;
        if (!f.InProgress || f.IsOverOrEnding || f.CurrentSide != Side.Player || f.Phase == TurnPhase.None) return null;
        if (!f.InFight(enemy)) return null;
        maxSteps = Math.Min(maxSteps, StepCap);
        if (Look(f, enemy, maxSteps) is not { } seen) return null;
        var numbers = new Numbers(f, seen, maxSteps);

        var steps = new List<Step>();
        var odds = new List<Chance>();
        for (var step = 1; step < seen.Moves.Count; step++)
        {
            // A step that cannot be put into words ends the list; what is certain before it stays.
            if (numbers.Text(seen.Moves[step], step) is not { } text) return steps.Count > 0 ? new Reading(steps, odds) : null;
            steps.Add(new Step(seen.Moves[step].Id, text.Text, text.Rough));
        }

        if (seen.Odds is { } options)
        {
            foreach (var option in options)
            {
                if (numbers.Text((MoveState)seen.Monster.StateById(option.Id)!, seen.Moves.Count) is not { } text)
                {
                    odds.Clear();
                    break;
                }
                odds.Add(new Chance(option.Id, text.Text, option.Chance, text.Rough));
            }
        }
        return steps.Count > 0 || odds.Count > 0 ? new Reading(steps, odds) : null;
    }

    // ---------- the arithmetic, on plain data: the mod's, line for line ----------
    //
    // src/SpireMind/Game/Forecast.cs holds the same types and functions under the same names.
    // A change to one is a change to the other; ForecastChecks runs that file's self-test cases
    // against this copy.

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

    // ---------- the live fight: the simulator's objects where the mod reads the game's ----------

    /// <summary>One enemy's walk: Moves[0] is the shown move, then each certain step; Odds is the chance list after them, if any.</summary>
    private sealed record Seen(Creature Creature, Monster Monster, List<MoveState> Moves, IReadOnlyList<Odds>? Odds, bool Open);

    /// <summary>
    /// Walks one enemy, or null when its shown move is not a sound place to start from: not a
    /// reviewed class, not rolled yet, acting right now, or already performed (the intent is
    /// then last turn's).
    /// </summary>
    private static Seen? Look(Fight f, Creature creature, int maxSteps)
    {
        if (creature.Monster is not { } monster) return null;
        if (!Roster.Reviewed.Contains(monster.ClassName)) return null;
        if (monster.IsPerformingMove || monster.NextMove is not { } shown || monster.ShownMovePerformed) return null;

        var outlook = Plan(MoveOf(monster, shown), monster.MoveLog, maxSteps, id => StateOf(f, monster, id));

        var moves = new List<MoveState> { shown };
        IReadOnlyList<Odds>? odds = null;
        foreach (var step in outlook.Steps)
        {
            if (step.Move is { } id) moves.Add((MoveState)monster.StateById(id)!);
            else odds = step.Odds;
        }
        return new Seen(creature, monster, moves, odds, outlook.Open);
    }

    /// <summary>The state the machine holds under an id, as the roll looks it up.</summary>
    private static PlanState? StateOf(Fight f, Monster monster, string id) => monster.StateById(id) switch
    {
        MoveState move => MoveOf(monster, move),
        RandomBranch branch => new PlanRandom(walk => Options(monster, branch, walk)),
        ConditionalBranch branch => new PlanChoice(walk => Pick(f, monster, branch, walk)),
        _ => null,
    };

    /// <summary>A move as the walk sees it. After an escape or a death blow the monster is gone.</summary>
    private static PlanMove MoveOf(Monster monster, MoveState move) =>
        new(move.Id, monster.FollowUpIdOf(move), move.Intents.Any(i => i.Type is IntentType.Escape or IntentType.DeathBlow));

    /// <summary>
    /// The options of a random branch with their weights, or null when the weights cannot be
    /// stood behind: a weight is only asked for where the monster's weights were read and found
    /// to be constants (<see cref="Roster.ConstantWeights"/>), never where one is worked out by code.
    /// </summary>
    private static IReadOnlyList<Option>? Options(Monster monster, RandomBranch branch, PlanWalk walk)
    {
        if (!Roster.ConstantWeights.Contains(monster.ClassName) || branch.HasComputedWeights) return null;

        var options = new List<Option>();
        for (var i = 0; i < branch.OptionCount; i++)
        {
            var entry = branch.OptionAt(i);
            if (string.IsNullOrEmpty(entry.StateId)) return null;
            var rule = entry.Repeat switch
            {
                MoveRepeat.Forever => Repeat.Forever,
                MoveRepeat.MaxInARow => Repeat.Times,
                MoveRepeat.CannotRepeat => Repeat.Never,
                _ => Repeat.Once,
            };
            options.Add(new Option(entry.StateId, rule, entry.MaxTimes, entry.Cooldown, entry.Weight));
        }

        // The roll's own weight routine must give the same numbers as the arithmetic above. The mod
        // can ask the game's only at the first roll (against the real log); the simulator's takes any log.
        for (var i = 0; i < options.Count; i++)
        {
            var theirs = branch.RollWeight(i, walk.Log);
            var ours = Weight(options[i], walk.Log);
            if (theirs != ours)
                throw new Fault(string.Create(CultureInfo.InvariantCulture, $"{monster.ClassName}: the roll weighs {options[i].Id} at {theirs}, the forecast at {ours}"));
        }
        return options;
    }

    /// <summary>
    /// The state a conditional branch will give, when that is certain at the time the game asks.
    /// Never "what the condition says now". Null for any branch without a rule. Of the monsters
    /// here only the Wriggler has a branch a walk can meet: after its do-nothing turn the slot it
    /// stands in decides, and that is fixed for the fight.
    /// </summary>
    private static string? Pick(Fight f, Monster monster, ConditionalBranch branch, PlanWalk walk)
    {
        if (monster.ClassName != "Wriggler" || branch.Id != "INIT_MOVE") return null;
        try
        {
            return branch.NextStateId(f, monster);
        }
        catch (InvalidOperationException)
        {
            // No option holds: the game throws there too when it gets that far. Until then, say nothing.
            return null;
        }
    }

    // ---------- numbers ----------

    /// <summary>
    /// The words and numbers of one enemy's coming moves. A number is the damage chain's own
    /// preview of the hit against the player at today's modifiers, plus the Strength the enemy
    /// is known to gain first: from its own moves (the Gains table), from Territorial at the end
    /// of each enemy turn, from temporary Strength running out. Where something else is known to
    /// change the hit and is not worked in, the number carries "~".
    /// </summary>
    private sealed class Numbers
    {
        private readonly Fight _f;
        private readonly Seen _seen;
        private readonly Creature _enemy;
        private readonly Standing _now;
        private readonly List<Effect> _effects;
        private readonly Power? _weak;
        private readonly Power? _shrink;
        private readonly Power? _vulnerable;
        private readonly bool _rough;
        private readonly int _vulnerableFrom;

        public Numbers(Fight f, Seen seen, int maxSteps)
        {
            _f = f;
            _seen = seen;
            _enemy = seen.Creature;
            var me = f.Player;

            // Ritual skips the end of the turn it was applied in.
            var ritual = _enemy.GetPower<RitualPower>();
            // Temporary Strength (the player's cards and potions) is undone at the end of the
            // enemy turn: the shown move has it, no later one does.
            var temporary = 0;
            foreach (var power in _enemy.Powers.OfType<TemporaryStrengthPower>())
                temporary += power.Type == PowerType.Debuff ? power.Amount : -power.Amount;
            _now = new Standing(
                PerTurn: _enemy.PowerAmount<TerritorialPower>() + _enemy.PowerAmount("HighVoltagePower"),
                Ritual: ritual?.Amount ?? 0,
                RitualWaits: ritual is { SkipsNextTurnEnd: true },
                Temporary: temporary,
                Vigor: _enemy.PowerAmount("VigorPower"));

            _effects = seen.Moves.Select(move => EffectOf(seen.Monster.ClassName, move)).ToList();

            _weak = _enemy.GetPower<WeakPower>();
            _shrink = _enemy.GetPower<ShrinkPower>();
            _vulnerable = me.GetPower<VulnerablePower>();

            // Things that change this enemy's hits in ways that are not worked out here, power for
            // power as the mod's file lists them. (The mod also marks for the Brimstone relic and for
            // a Queen or an Obscura beside the enemy; the simulator has none of those.)
            _rough = Roster.EnemyMarks.Any(_enemy.HasPower) || Roster.PlayerMarks.Any(me.HasPower);

            _vulnerableFrom = int.MaxValue;
            var actsFirst = true;
            foreach (var other in f.Enemies)
            {
                var self = ReferenceEquals(other, _enemy);
                if (self) actsFirst = false;

                var cls = other.Monster?.ClassName;
                if (cls is null || !Roster.Reviewed.Contains(cls))
                {
                    // An enemy nobody read could do anything to the others.
                    _rough = true;
                    continue;
                }

                // From the turn a move puts Vulnerable on the player, every hit is half again as
                // hard. An enemy earlier in the list acts first, so its move counts for the same turn.
                if (!Roster.VulnerableClasses.Contains(cls)) continue;
                var first = FirstVulnerable(self ? seen : Look(f, other, maxSteps), cls);
                if (first == int.MaxValue) continue;
                _vulnerableFrom = Math.Min(_vulnerableFrom, !self && actsFirst ? first : first + 1);
            }
        }

        /// <summary>What a move gives its own monster once performed, from the Gains table.</summary>
        private static Effect EffectOf(string cls, MoveState move)
        {
            var attacks = move.Intents.Any(i => i.IsAttack);
            return Roster.Gains.TryGetValue((cls, move.Id), out var strength) ? new Effect(attacks, Strength: strength) : new Effect(attacks);
        }

        /// <summary>The first position (0 = its shown move) at which this enemy makes, or may make, a move that applies Vulnerable.</summary>
        private static int FirstVulnerable(Seen? seen, string cls)
        {
            if (seen is null) return 0;
            for (var at = 0; at < seen.Moves.Count; at++)
                if (Roster.Vulnerable.Contains((cls, seen.Moves[at].Id))) return at;
            return seen.Open ? seen.Moves.Count : int.MaxValue;
        }

        /// <summary>
        /// A move's intents as the snapshot words them, joined with " + ", for the move at
        /// <paramref name="step"/> (1 = the next one), and whether a number in it carries "~".
        /// Null when it cannot be put into words (a move that shows nothing).
        /// </summary>
        public (string Text, bool Rough)? Text(MoveState move, int step)
        {
            var parts = new List<string>();
            var anyRough = false;
            foreach (var intent in move.Intents)
            {
                if (!intent.IsAttack)
                {
                    parts.Add(Fight.IntentWord(intent));
                    continue;
                }

                var extra = Extra(step, out var rough);
                var hits = Math.Max(1, intent.Repeats!());
                // Strength is added before anything multiplies, so "with E more Strength" is the preview of base + E.
                var from = intent.Damage!() + extra;
                var preview = _f.ModifyDamage(_f.Player, _enemy, from, Props.MonsterMove, null);
                var perHit = Math.Max(0, (int)preview);

                // A cap decides today's number and may be gone by then.
                rough |= preview != _f.ModifyDamageUncapped(_f.Player, _enemy, from, Props.MonsterMove, null);
                parts.Add(AttackText(perHit, hits, rough));
                anyRough |= rough;
            }
            return parts.Count > 0 ? (string.Join(" + ", parts), anyRough) : null;
        }

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

        private static bool Stays(Power? power, int step) => power is not null && Lasts(power.Amount, power.SkipNextDurationTick, step);

        private static bool Fades(Power? power, int step) => power is { Amount: > 0 } && !Stays(power, step);
    }

    // ---------- what was reviewed ----------

    /// <summary>
    /// The monster classes and moves this file may speak about: the simulator's 29, as the recipe
    /// and the mod's file group them. All of it was read from the game's code (v0.107.1); the
    /// mod's file has the same entries among its 104 classes. ForecastChecks holds every entry
    /// against the simulator's own monsters.
    /// </summary>
    internal static class Roster
    {
        /// <summary>Every follow-up is a move (after a start-of-fight branch at most).</summary>
        public static readonly IReadOnlySet<string> Fixed = new HashSet<string>(StringComparer.Ordinal)
        {
            "AssassinRubyRaider", "AxeRubyRaider", "BruteRubyRaider", "BygoneEffigy", "Byrdonis", "CeremonialBeast",
            "CrossbowRubyRaider", "CubexConstruct", "EyeWithTeeth", "FuzzyWurmCrawler", "KinFollower", "KinPriest",
            "LeafSlimeM", "Nibbit", "PhrogParasite", "ShrinkerBeetle", "SnappingJaxfruit", "TrackerRubyRaider",
            "TwigSlimeS", "Vantom", "VineShambler", "Wriggler",
        };

        /// <summary>A random pick inside the loop, with weights that are constants.</summary>
        public static readonly IReadOnlySet<string> Random = new HashSet<string>(StringComparer.Ordinal)
        {
            "Flyconid", "Fogmog", "Inklet", "LeafSlimeS", "Mawler", "SlitheringStrangler", "TwigSlimeM",
        };

        /// <summary>The enemy classes that were read. Any other class gets no forecast.</summary>
        public static readonly IReadOnlySet<string> Reviewed = new HashSet<string>(Fixed.Concat(Random), StringComparer.Ordinal);

        /// <summary>Classes whose random weights may be asked for: constants, the same now as at the roll.</summary>
        public static readonly IReadOnlySet<string> ConstantWeights = Random;

        /// <summary>
        /// The Strength a move gives its own monster, read from each move's code (the gain always
        /// comes after the move's own hit), at ascension 0. Used for numbers only, never to decide
        /// which move comes.
        /// </summary>
        public static readonly IReadOnlyDictionary<(string Monster, string Move), int> Gains = new Dictionary<(string, string), int>
        {
            [("BruteRubyRaider", "ROAR_MOVE")] = 3,
            [("BygoneEffigy", "WAKE_MOVE")] = 10,
            [("CeremonialBeast", "PLOW_MOVE")] = 2,
            [("CeremonialBeast", "CRUSH_MOVE")] = 3,
            [("CubexConstruct", "CHARGE_UP_MOVE")] = 2,
            [("CubexConstruct", "REPEATER_BLAST_MOVE")] = 2,
            [("CubexConstruct", "REPEATER_BLAST_MOVE_2")] = 2,
            [("Fogmog", "SWIPE_MOVE")] = 1,
            [("Fogmog", "SWIPE_RANDOM_MOVE")] = 1,
            [("FuzzyWurmCrawler", "INHALE")] = 7,
            [("KinFollower", "POWER_DANCE_MOVE")] = 2,
            [("KinPriest", "RITUAL_MOVE")] = 2,
            [("Nibbit", "HISS_MOVE")] = 2,
            [("SnappingJaxfruit", "ENERGY_ORB_MOVE")] = 2,
            [("Vantom", "PREPARE_MOVE")] = 2,
            [("Wriggler", "WRIGGLE_MOVE")] = 2,
        };

        /// <summary>The moves that put Vulnerable on the player: of all that monsters apply, the only thing that changes a monster's hit.</summary>
        public static readonly IReadOnlySet<(string Monster, string Move)> Vulnerable = new HashSet<(string, string)>
        {
            ("Flyconid", "VULNERABLE_SPORES_MOVE"), ("Mawler", "ROAR_MOVE"),
        };

        public static readonly IReadOnlySet<string> VulnerableClasses = new HashSet<string>(Vulnerable.Select(v => v.Monster), StringComparer.Ordinal);

        /// <summary>
        /// Powers on the enemy that mark every one of its numbers, as the mod's file tests them on
        /// the enemy: Suck (Strength for each of its hits that gets through), Enrage (Strength for
        /// every Skill the player plays), Debilitate (doubles Weak and Vulnerable and ends before
        /// they do). The simulator has none of the three; they are looked up by the game's class name.
        /// </summary>
        public static readonly IReadOnlyList<string> EnemyMarks = ["SuckPower", "EnragePower", "DebilitatePower"];

        /// <summary>
        /// Powers on the player that mark every number, as the mod's file tests them on the player:
        /// the short powers that change the damage taken (Colossus, Covered, Diamond Diadem,
        /// Intercept, Tainted), Debilitate, and Surrounded (a hit from behind is half again as hard,
        /// and the player turns around by targeting). Of these the simulator has Colossus.
        /// </summary>
        public static readonly IReadOnlyList<string> PlayerMarks =
            ["ColossusPower", "CoveredPower", "DiamondDiademPower", "InterceptPower", "TaintedPower", "DebilitatePower", "SurroundedPower"];
    }
}
