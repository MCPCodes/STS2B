using System.Globalization;
using System.Runtime.CompilerServices;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>Who plays the rest of the fight in a Rollout player's play-outs.</summary>
public enum RolloutInner
{
    /// <summary>Lookahead itself with a small budget and one copy, searching again before every action.</summary>
    Lookahead,

    /// <summary>Lookahead's one-turn search with a small budget, planning each turn once.</summary>
    Planner,

    /// <summary>The Greedy player, from snapshots.</summary>
    Greedy,
}

/// <summary>What a Rollout player may spend on one decision.</summary>
/// <param name="Name">"fast", "normal" or "strong"; a setting of one's own carries any other name.</param>
/// <param name="Playouts">Fights played to the end per decision, over all the lines compared. 0 turns
/// the player into the inner planner itself: the line the one-turn search likes best.</param>
/// <param name="Lines">How many lines of the turn are compared, before the ones that are always compared.</param>
/// <param name="InnerBudget">Positions the inner player's search tries: per turn it plans (<see cref="RolloutInner.Planner"/>)
/// or per action (<see cref="RolloutInner.Lookahead"/>). Not used with <see cref="RolloutInner.Greedy"/>.</param>
/// <param name="Inner">Who plays the play-outs.</param>
/// <param name="SearchBudget">Positions tried when the lines of the real turn are listed.</param>
public sealed record RolloutSetting(string Name, int Playouts, int Lines, int InnerBudget,
    RolloutInner Inner = RolloutInner.Planner, int SearchBudget = LookaheadPolicy.DefaultBudget)
{
    public static readonly RolloutSetting Fast = new("fast", 24, 6, 60);
    public static readonly RolloutSetting Normal = new("normal", 64, 8, 60);
    public static readonly RolloutSetting Strong = new("strong", 192, 12, 60);

    public static IReadOnlyList<RolloutSetting> All => [Fast, Normal, Strong];

    /// <summary>The setting of that name, or null.</summary>
    public static RolloutSetting? Named(string name) =>
        All.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A player that judges a turn by what becomes of the fight after it. It lists the different
/// ways to play the turn (with Lookahead's search and evaluation), and for the few that are
/// worth comparing it plays the rest of the fight out a number of times with a fast inner
/// player and keeps the line that wins most often, then the one that keeps the most HP. A
/// Power that pays off over many turns, a potion that is worth more on a later turn and a race
/// that has to start a turn early are all judged the same way: by how the fight ends.
/// <para>
/// A line is the turn's actions up to and including the first one that shows something the
/// player could not know (a card drawn, a random target, an offer of cards), or the end of the
/// turn. Nothing is planned past such a point; the player decides again once it has seen it.
/// The lines compared always include the one-turn search's own choice, the line with the most
/// Block, the one that loses the least HP this turn, the one with the most damage (and with
/// the most on each enemy), and for every Power, every potion and every card picked in a
/// choice the best line that plays it, and the one of those with the most Block.
/// </para>
/// <para>
/// It can see no more than Lookahead can. Lines are listed on a copy of the fight in which
/// everything hidden is replaced (<see cref="Fight.CloneDeterminized"/>), and every play-out
/// runs on another such copy: its own order of the draw pile and its own random numbers, so
/// enemies' fixed patterns are walked as they are and their random choices are drawn afresh.
/// Every line meets the same copies, so the lines differ and not the luck. The inner player
/// plans on reshuffled copies of those copies. All lines are played on the same first copies
/// for half the budget; then the one-turn search's own choice and its one or two best
/// challengers share the rest. A decision costs at most <see cref="RolloutSetting.Playouts"/> play-outs.
/// </para>
/// <para>
/// It has no parameterless constructor on purpose: a fight costs seconds, not milliseconds, so
/// it is not one of <see cref="Policies.All"/> and plays only where it is asked for by name
/// (<see cref="Policies.ByName"/>: "Rollout", "Rollout:fast", "Rollout:strong"). The line it
/// settled on is remembered per running fight and followed while the fight shows exactly what
/// the line expected, so a turn is worked out once. A fight played from its start is played
/// the same way every time. A position handed over in the middle of a turn is worked out
/// afresh, and may get another answer than it got inside the turn it came from.
/// </para>
/// </summary>
public sealed class RolloutPolicy : IEnginePolicy
{
    /// <summary>A win counts for more than any amount of HP; then each HP left counts 1.</summary>
    private const double WinScore = 1000;

    /// <summary>Of two lost play-outs the one that came closer: this much for all the enemies' HP taken off.</summary>
    private const double LossProgress = 100;

    /// <summary>Turn plans per play-out before it is given up as lost: more than <see cref="Tuning.MaxTurns"/> turns ever need.</summary>
    private const int MostPlans = 600;

    private readonly RolloutSetting _setting;
    private readonly int _seed;
    private readonly LookaheadPolicy _fallback, _inner;
    private readonly GreedyPolicy _greedy = new();
    private readonly ConditionalWeakTable<Fight, Plan> _plans = new();

    /// <param name="setting">How much a decision may cost.</param>
    /// <param name="seed">Seeds the reshuffles: with the same seed, a decision that is worked out gets the same answer for the same snapshot.</param>
    public RolloutPolicy(RolloutSetting setting, int seed = 1)
    {
        _setting = setting;
        _seed = seed;
        _fallback = new LookaheadPolicy(seed);
        _inner = new LookaheadPolicy(seed, Math.Max(1, setting.InnerBudget), worlds: 1);
    }

    public RolloutSetting Setting => _setting;

    public string Name => _setting.Name == RolloutSetting.Normal.Name ? "Rollout" : "Rollout:" + _setting.Name;

    /// <summary>Play-outs run so far by this instance, for reports of what a decision costs.</summary>
    public long PlayoutsRun => Interlocked.Read(ref _playouts);

    /// <summary>Decisions that were worked out (not followed from a remembered line) so far.</summary>
    public long PlansMade => Interlocked.Read(ref _made);

    /// <summary>Play-outs that could not start because their line's last action was refused on the copy. 0 in a sound arena.</summary>
    public long PlayoutsFailed => Interlocked.Read(ref _failed);

    /// <summary>When set, every decision that was worked out is written here: the lines compared,
    /// how often each won and with how much HP, and the one taken. For reading why it played what it played.</summary>
    public Action<string>? Trace { get; set; }

    /// <summary>Decisions it could not work out and left to the one-turn search. 0 in a sound arena.</summary>
    public long FellBack => Interlocked.Read(ref _fellBack);

    private long _playouts, _made, _failed, _fellBack;

    /// <summary>Without the fight there is nothing to play out: it decides as Greedy does.</summary>
    public string Choose(GameSnapshot snapshot) => _greedy.Choose(snapshot);

    public string Choose(GameSnapshot snapshot, Fight fight)
    {
        var offered = snapshot.Actions;
        if (offered.Count == 0) return "end_turn";
        if (offered.Count == 1 || fight.IsOver) return offered[0].Id;

        // The line settled on earlier, as long as the fight shows exactly what that line expected.
        var shown = LookaheadPolicy.Hash(snapshot);
        if (_plans.TryGetValue(fight, out var plan) && plan.At(shown) is { } next && Offered(offered, next)) return next;

        string[]? line;
        try
        {
            line = Decide(fight, shown);
        }
        catch (Exception)
        {
            // A rule that throws inside a copy must not stop the fight: the one-turn search decides.
            line = null;
        }
        if (line is null || line.Length == 0 || !Offered(offered, line[0]))
        {
            Interlocked.Increment(ref _fellBack);
            return _fallback.Choose(snapshot, fight);
        }

        _plans.AddOrUpdate(fight, Remember(fight, line, WorldSeed(shown, -2)));
        return line[0];
    }

    private static bool Offered(IReadOnlyList<LegalAction> offered, string id)
    {
        foreach (var action in offered)
            if (action.Id == id) return true;
        return false;
    }

    private static bool HasPotion(Fight f)
    {
        foreach (var potion in f.Potions)
            if (potion is { Usage: PotionUsage.CombatOnly or PotionUsage.AnyTime }) return true;
        return false;
    }

    /// <summary>The seed of the decision's k-th copy: the player's seed, what is shown, and k.</summary>
    private int WorldSeed(ulong shown, int k) =>
        unchecked((int)(new GameRng(shown ^ ((ulong)(uint)_seed << 32) ^ (uint)k).NextU64() >> 33));

    // ---------- one decision ----------

    /// <summary>The actions to take from here, up to the end of the turn or to the first one that shows something new.</summary>
    private string[]? Decide(Fight fight, ulong shown)
    {
        Interlocked.Increment(ref _made);
        var root = fight.CloneDeterminized(WorldSeed(shown, -1));
        var lines = new TurnSearch(new LookaheadPolicy.Measure(fight), _setting.SearchBudget, HasPotion(fight)).Lines(root);
        if (lines.Count == 0) return null;

        var big = fight.RoomType is RoomType.Elite or RoomType.Boss;
        if (_setting.Playouts <= 0) return OneTurnChoice(lines, big)?.Actions;

        var compared = Pick(lines, _setting.Lines);
        if (compared.Count == 0) return OneTurnChoice(lines, big)?.Actions;
        if (compared.Count == 1)
        {
            Trace?.Invoke($"turn {fight.Turn}: {Describe(root, compared[0])}{(compared[0].Wins ? " (wins now)" : " (the only line)")}");
            return compared[0].Actions;
        }
        return Compare(compared, shown, fight, root, OneTurnChoice(lines, big)).Actions;
    }

    /// <summary>A line as the labels of its actions, for the trace.</summary>
    private static string Describe(Fight root, Line line)
    {
        var copy = root.Clone();
        var labels = new List<string>();
        foreach (var id in line.Actions)
        {
            labels.Add(copy.LegalActions().FirstOrDefault(a => a.Id == id)?.Label ?? id);
            if (!Try(copy, id)) break;
        }
        return string.Join(", ", labels);
    }

    /// <summary>
    /// What the one-turn search would do with these lines, potions held back as Lookahead holds
    /// them: a line with a potion only when it turns a loss into a win or a death into survival,
    /// or, against an elite or a boss, when it is worth a lot.
    /// </summary>
    private static Line? OneTurnChoice(List<Line> lines, bool bigFight)
    {
        Line? plain = null, potion = null;
        foreach (var line in lines)
        {
            if (!double.IsNegativeInfinity(line.Plain) && (plain is null || line.Plain > plain.Plain + 1e-9)) plain = line;
            if (!double.IsNegativeInfinity(line.Value) && (potion is null || line.Value > potion.Value + 1e-9)) potion = line;
        }
        if (plain is null) return potion;
        if (potion is null || ReferenceEquals(potion, plain)) return plain;
        var gain = potion.Value - plain.Plain;
        return gain >= LookaheadPolicy.Decisive || (bigFight && gain >= LookaheadPolicy.WorthAPotion) ? potion : plain;
    }

    // ---------- which lines are compared ----------

    /// <summary>
    /// The lines worth playing out. A line that wins the fight now, with nothing left to chance,
    /// is taken at once. Otherwise: what the one-turn search likes best with and without a
    /// potion, the extremes of the lines that set nothing up (most Block, least HP lost this
    /// turn, most damage, most damage on each enemy), then for every Power, potion or card pick
    /// the best line with it and the one with the most Block, and after those the next best
    /// lines until <paramref name="room"/> are listed.
    /// </summary>
    private static List<Line> Pick(List<Line> lines, int room)
    {
        var certain = Best(lines.Where(l => l.Wins), l => l.Value);
        if (certain is not null) return [certain];

        var chosen = new List<Line>();
        void Take(Line? line)
        {
            if (line is not null && !chosen.Contains(line)) chosen.Add(line);
        }

        Take(Best(lines.Where(l => !double.IsNegativeInfinity(l.Plain)), l => l.Plain));
        Take(Best(lines, l => l.Value));

        var plain = lines.Where(l => l.Marks.Length == 0).ToList();
        Take(Best(plain, l => l.Value));
        Take(Best(plain, l => l.Block));
        Take(Best(plain.Where(l => l.EndsTurn), l => l.HpAfter));
        Take(Best(plain, l => l.Damage));
        var enemies = plain.Count > 0 ? plain[0].DamageTo.Length : 0;
        if (enemies > 1)
            for (var j = 0; j < enemies; j++)
                if (plain.Any(l => l.DamageTo[j] > 0)) Take(Best(plain, l => l.DamageTo[j]));

        // What sets a line apart for longer than this turn: the Powers it plays, the potions it
        // uses, the cards it picks. One of each on its own is always compared; combinations while there is room.
        var groups = lines.Where(l => l.Marks.Length > 0)
            .GroupBy(l => l.Signature, StringComparer.Ordinal)
            .Select(g => g.ToList())
            .OrderByDescending(g => g.Max(l => l.Value))
            .ThenBy(g => g.Min(l => l.Order))
            .ToList();
        foreach (var group in groups.Where(g => g[0].Marks.Length == 1))
        {
            Take(Best(group, l => l.Value));
            Take(Best(group, l => l.Block));
        }
        foreach (var group in groups.Where(g => g[0].Marks.Length > 1))
        {
            if (chosen.Count >= room) break;
            Take(Best(group, l => l.Value));
        }

        foreach (var line in lines.OrderByDescending(l => l.Value).ThenBy(l => l.Order))
        {
            if (chosen.Count >= room) break;
            if (!double.IsNegativeInfinity(line.Value)) Take(line);
        }
        return chosen;
    }

    /// <summary>The line with the most of <paramref name="by"/>; of equals the one the one-turn search likes better, then the one found first.</summary>
    private static Line? Best(IEnumerable<Line> lines, Func<Line, double> by)
    {
        Line? best = null;
        var most = double.NegativeInfinity;
        foreach (var line in lines)
        {
            var amount = by(line);
            if (double.IsNegativeInfinity(amount)) continue;
            if (best is null || amount > most + 1e-9 || (amount > most - 1e-9 && (line.Value > best.Value + 1e-9 || (line.Value > best.Value - 1e-9 && line.Order < best.Order))))
            {
                best = line;
                most = amount;
            }
        }
        return best;
    }

    // ---------- comparing lines by playing the fight out ----------

    /// <summary>Copies each line of the last round gets at least, for a second challenger to be taken along.</summary>
    private const int FinalCopies = 4;

    /// <summary>
    /// Two rounds on the same copies. First every line is played out on the same few copies,
    /// for half the budget. Then the one-turn search's own choice and the one or two lines that
    /// did best in the first round share the rest of the budget, again on the same copies. Of
    /// those the one with the best sum of scores over all its copies is taken; of equals the
    /// search's choice, then the one listed first.
    /// <para>
    /// The search's choice is never dropped: the play-outs are few and their player is weaker
    /// than the search, so its choice stays in the comparison to the end. (The first version
    /// halved the lines round by round, the first time after one copy each. A few HP on one copy
    /// then threw out the line that keeps a potion for a hit that half the copies hold; the
    /// position is in <c>RolloutChecks</c>.)
    /// </para>
    /// </summary>
    private Line Compare(List<Line> lines, ulong shown, Fight fight, Fight root, Line? searchChoice)
    {
        var enemyHp = fight.EnemyHpLeft;
        var all = lines.Select((line, i) => new Running(line, i)).ToList();
        var standing = all.FirstOrDefault(r => ReferenceEquals(r.Line, searchChoice)) ?? all[0];
        var budget = _setting.Playouts;

        void Play(List<Running> who, int from, int to)
        {
            for (var k = from; k < to; k++)
            {
                var seed = WorldSeed(shown, k);
                foreach (var running in who)
                {
                    var score = Playout(running.Line, seed, enemyHp);
                    running.Sum += score;
                    running.Played++;
                    if (score >= WinScore) running.Won++;
                    else if (!double.IsNegativeInfinity(score)) running.LostSum += score;
                }
            }
        }

        // Round one: every line on the same copies, for half the budget (fewer copies when there are more lines than that pays for).
        var first = Math.Max(1, budget / (2 * all.Count));
        if (first * all.Count > budget) first = budget / all.Count;
        Play(all, 0, first);

        // Round two: the search's choice and its best challengers share what is left.
        var finalists = new List<Running> { standing };
        if (first > 0)
        {
            var left = budget - first * all.Count;
            finalists.AddRange(all.Where(r => !ReferenceEquals(r, standing))
                .OrderByDescending(r => r.Sum).ThenBy(r => r.Listed)
                .Take(left / 3 >= FinalCopies ? 2 : 1));
            Play(finalists, first, first + left / finalists.Count);
        }

        // Every finalist has met the same copies, so the sums compare.
        var taken = standing;
        foreach (var finalist in finalists)
            if (finalist.Sum > taken.Sum) taken = finalist;

        if (Trace is { } trace)
        {
            var text = new System.Text.StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"turn {fight.Turn}, {fight.Player.Hp} HP, {enemyHp} enemy HP: {all.Count} lines");
            foreach (var running in all)
            {
                var hp = running.Won > 0 ? (running.WonHp / running.Won).ToString("0.0", CultureInfo.InvariantCulture) : "-";
                var mark = ReferenceEquals(running, taken) ? "*" : ReferenceEquals(running, standing) ? "s" : " ";
                text.Append(CultureInfo.InvariantCulture, $"\n  {mark} won {running.Won}/{running.Played}, HP {hp}, one-turn value {running.Line.Value:0.0}: {Describe(root, running.Line)}");
            }
            trace(text.ToString());
        }
        return taken.Line;
    }

    private sealed class Running(Line line, int listed)
    {
        public Line Line { get; } = line;
        public int Listed { get; } = listed;
        public double Sum { get; set; }
        public int Played { get; set; }
        public int Won { get; set; }

        /// <summary>HP left over the play-outs won, added up: a win scores <see cref="WinScore"/> plus its HP.</summary>
        public double WonHp => Sum - Won * WinScore - LostSum;
        public double LostSum { get; set; }
    }

    /// <summary>One fight played to its end after <paramref name="line"/>, on a copy of its own, and what came of it.</summary>
    private double Playout(Line line, int worldSeed, int enemyHp)
    {
        Interlocked.Increment(ref _playouts);
        var world = line.Before.CloneDeterminized(worldSeed);
        if (!Try(world, line.Actions[^1]))
        {
            Interlocked.Increment(ref _failed);
            return double.NegativeInfinity;
        }
        PlayOn(world, worldSeed);
        if (world is { IsOver: true, Won: true }) return WinScore + world.Player.Hp;
        return LossProgress * Math.Clamp(1.0 - (double)world.EnemyHpLeft / Math.Max(1, enemyHp), 0, 1);
    }

    private static bool Try(Fight f, string id)
    {
        try
        {
            return f.Execute(id).Ok;
        }
        catch (Exception)
        {
            // A card that breaks on a copy: the copy is dropped by whoever asked.
            return false;
        }
    }

    /// <summary>The inner player takes the fight to its end.</summary>
    private void PlayOn(Fight world, int worldSeed)
    {
        if (_setting.Inner == RolloutInner.Greedy)
        {
            for (var n = 0; n < 6000 && !world.IsOver; n++)
                if (!Try(world, _greedy.Choose(world.Snapshot()))) Unstick(world);
            return;
        }

        if (_setting.Inner == RolloutInner.Lookahead)
        {
            for (var n = 0; n < 6000 && !world.IsOver; n++)
            {
                // The seed of its reshuffles stands in for what it is shown: the play-out's own seed and how far it has come.
                var actions = world.LegalActions();
                var id = actions.Count == 1 ? actions[0].Id : _inner.Decide(world, unchecked((ulong)(uint)worldSeed * 1099511628211UL + (ulong)n));
                if (id is null || !Try(world, id)) Unstick(world);
            }
            return;
        }

        var big = world.RoomType is RoomType.Elite or RoomType.Boss;
        for (var n = 0; n < MostPlans && !world.IsOver; n++)
        {
            // It plans on a copy of the copy: what this play-out's fight hides, its player does not know either.
            var copy = world.CloneDeterminized(unchecked(worldSeed * 16777619 + n + 1));
            var lines = new TurnSearch(new LookaheadPolicy.Measure(world), _setting.InnerBudget, HasPotion(world)).Lines(copy);
            var done = 0;
            if (OneTurnChoice(lines, big) is { } line)
            {
                foreach (var id in line.Actions)
                {
                    if (world.IsOver || !Try(world, id)) break;
                    done++;
                }
            }
            if (done == 0 && !world.IsOver) Unstick(world);
        }
    }

    /// <summary>Nothing the inner player wanted could run: whatever closes the turn or the choice.</summary>
    private static void Unstick(Fight world)
    {
        var actions = world.LegalActions();
        foreach (var id in new[] { "end_turn", "confirm", "skip" })
            if (actions.Any(a => a.Id == id) && Try(world, id)) return;
        foreach (var action in actions)
            if (!action.Id.StartsWith("discard_potion:", StringComparison.Ordinal) && Try(world, action.Id)) return;
    }

    // ---------- following a line once it is chosen ----------

    /// <summary>The chosen line, with what the snapshot must look like before each of its actions.</summary>
    private sealed class Plan(ulong[] shown, string[] actions)
    {
        public string? At(ulong now)
        {
            for (var i = 0; i < shown.Length; i++)
                if (shown[i] == now) return actions[i];
            return null;
        }
    }

    private static Plan Remember(Fight fight, string[] line, int seed)
    {
        var copy = fight.CloneDeterminized(seed);
        var shown = new List<ulong>();
        for (var i = 0; i < line.Length; i++)
        {
            shown.Add(LookaheadPolicy.Hash(copy.Snapshot()));
            if (i + 1 < line.Length && !Try(copy, line[i])) break;
        }
        return new Plan(shown.ToArray(), line);
    }

    // ---------- the lines of a turn ----------

    /// <summary>One way to play the turn from the decision on.</summary>
    private sealed class Line
    {
        /// <summary>The actions in order; the last one ends the turn, shows something hidden, or ends the fight.</summary>
        public required string[] Actions { get; init; }

        /// <summary>The search's copy of the fight with every action but the last one done.</summary>
        public required Fight Before { get; init; }

        /// <summary>The last action ended the turn (the enemies have acted).</summary>
        public required bool EndsTurn { get; init; }

        /// <summary>The fight is won when the line closes, and nothing in it was left to chance.</summary>
        public required bool Wins { get; init; }

        /// <summary>Lookahead's value of where the line leads on the search's copy, with no potion in it or after it; negative infinity when it uses one.</summary>
        public required double Plain { get; init; }

        /// <summary>The same with potions allowed.</summary>
        public required double Value { get; init; }

        /// <summary>The Powers it plays, the potions it uses and the cards it picks, sorted.</summary>
        public required string[] Marks { get; init; }
        public required string Signature { get; init; }

        /// <summary>The player's Block when the line closes, before the enemies act.</summary>
        public required double Block { get; init; }

        /// <summary>The player's HP after the enemies acted on the search's copy (a line that ends the turn).</summary>
        public required double HpAfter { get; init; }

        /// <summary>HP taken off the enemies by the time the line closes, in all and by enemy index.</summary>
        public required double Damage { get; init; }
        public required double[] DamageTo { get; init; }

        /// <summary>The order the search found it in.</summary>
        public required int Order { get; init; }
    }

    /// <summary>What a position leads to at best: without a potion from here on, and with potions allowed.</summary>
    private readonly record struct Rated(double Plain, double Potion, bool Complete)
    {
        public static readonly Rated Never = new(double.NegativeInfinity, double.NegativeInfinity, true);
    }

    /// <summary>
    /// Lookahead's search of the turn, kept as lines: every different position the turn can be
    /// brought to before something hidden shows or the turn ends, with the actions that lead
    /// there and Lookahead's value of it. Below an action that shows something hidden it only
    /// rates what follows on this copy, as Lookahead does, and lists nothing.
    /// </summary>
    private sealed class TurnSearch(LookaheadPolicy.Measure measure, int budget, bool usePotions)
    {
        private readonly Dictionary<string, Rated> _listed = new(StringComparer.Ordinal), _rated = new(StringComparer.Ordinal);
        private readonly HashSet<string> _closed = new(StringComparer.Ordinal);
        private readonly List<Line> _lines = new();
        private readonly List<string> _path = new(), _marks = new();
        private int _potions, _spent, _stopAt;
        private int[] _hp = [];

        private bool Exhausted => _spent >= _stopAt;

        public List<Line> Lines(Fight root)
        {
            _hp = new int[root.Roster.Count];
            for (var j = 0; j < _hp.Length; j++) _hp[j] = root.InFight(root.Roster[j]) ? root.Roster[j].Hp : -1;

            // The budget is shared out evenly over the first actions, as in Lookahead's search.
            var key = LookaheadPolicy.Search.Key(root);
            var actions = LookaheadPolicy.Search.Candidates(root, usePotions);
            for (var i = 0; i < actions.Count; i++)
            {
                _stopAt = _spent + Math.Max(1, (budget - _spent) / (actions.Count - i));
                After(root, key, actions[i], depth: 0, list: true);
            }
            return _lines;
        }

        private Rated After(Fight from, string fromKey, LegalAction action, int depth, bool list)
        {
            var id = action.Id;
            var isEndTurn = id == "end_turn";
            var isPotion = id.StartsWith("potion:", StringComparison.Ordinal);
            var rolls = isEndTurn ? 0 : LookaheadPolicy.Search.Rolls(from);
            var mark = list ? Mark(from, action) : null;

            var child = from.Clone();
            var ok = Try(child, id);
            _spent++;
            if (!ok) return Rated.Never;

            // Only what happens before the turn ends can show the player something new in time to act on it.
            var shows = !isEndTurn && (LookaheadPolicy.Search.Rolls(child) != rolls || (!child.IsOver && child.DrawPile.Count < from.DrawPile.Count));
            var leaf = measure.IsLeaf(child) || depth >= LookaheadPolicy.HardDepth;

            if (list)
            {
                _path.Add(id);
                if (mark is not null) _marks.Add(mark);
                if (isPotion) _potions++;
            }

            Rated below;
            if (leaf)
            {
                var value = measure.Evaluate(child);
                below = new Rated(value, value, measure.IsLeaf(child));
            }
            else
            {
                below = Explore(child, depth + 1, list && !shows);
            }

            if (list)
            {
                if ((leaf || shows) && _closed.Add(fromKey + ">" + id)) Add(from, child, below, shows);
                _path.RemoveAt(_path.Count - 1);
                if (mark is not null) _marks.RemoveAt(_marks.Count - 1);
                if (isPotion) _potions--;
            }

            return isPotion
                ? new Rated(double.NegativeInfinity, below.Potion - LookaheadPolicy.PerAction - LookaheadPolicy.PerPotion, below.Complete)
                : new Rated(below.Plain - LookaheadPolicy.PerAction, below.Potion - LookaheadPolicy.PerAction, below.Complete);
        }

        private Rated Explore(Fight f, int depth, bool list)
        {
            var key = LookaheadPolicy.Search.Key(f);
            var seen = list ? _listed : _rated;
            if (seen.TryGetValue(key, out var known) && (known.Complete || Exhausted)) return known;

            var actions = LookaheadPolicy.Search.Candidates(f, usePotions);
            double plain = double.NegativeInfinity, potion = double.NegativeInfinity;
            var complete = true;
            for (var i = 0; i < actions.Count; i++)
            {
                // Out of budget, or too deep: only the first action, which ends the turn or moves the choice on.
                if (i > 0 && (Exhausted || depth >= LookaheadPolicy.MaxDepth))
                {
                    complete = false;
                    break;
                }
                var rated = After(f, key, actions[i], depth, list);
                complete &= rated.Complete;
                plain = Math.Max(plain, rated.Plain);
                potion = Math.Max(potion, rated.Potion);
            }
            var result = new Rated(plain, potion, complete);
            seen[key] = result;
            return result;
        }

        /// <summary>What an action leaves behind for longer than this turn, or null: a Power played, a potion used, a card picked.</summary>
        private static string? Mark(Fight f, LegalAction action)
        {
            var id = action.Id;
            if (id.StartsWith("potion:", StringComparison.Ordinal))
            {
                var end = id.IndexOf('>');
                var digits = end < 0 ? id.AsSpan(7) : id.AsSpan(7, end - 7);
                return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) && slot < f.Potions.Count && f.Potions[slot] is { } potion
                    ? "potion " + potion.ClassName
                    : "potion";
            }
            if (id.StartsWith("play:", StringComparison.Ordinal))
            {
                var end = id.IndexOf('>');
                var digits = end < 0 ? id.AsSpan(5) : id.AsSpan(5, end - 5);
                return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < f.Hand.Count && f.Hand[i].Type == CardType.Power
                    ? "power " + f.Hand[i].Title
                    : null;
            }
            if (id.StartsWith("select:", StringComparison.Ordinal) || id.StartsWith("card:", StringComparison.Ordinal) || id == "skip")
                return "pick " + action.Label;
            return null;
        }

        private void Add(Fight from, Fight child, Rated below, bool shows)
        {
            var costs = _path.Count * LookaheadPolicy.PerAction + _potions * LookaheadPolicy.PerPotion;
            // The enemies acted inside the last action: what the line itself did is read off the position before it.
            var endsTurn = !shows && (child.Turn != from.Turn || (child.IsOver && _path[^1] == "end_turn"));
            var at = endsTurn ? from : child;

            var damageTo = new double[_hp.Length];
            var damage = 0.0;
            for (var j = 0; j < _hp.Length; j++)
            {
                if (_hp[j] < 0) continue;
                var enemy = at.Roster[j];
                damageTo[j] = _hp[j] - (enemy.IsDead ? 0 : enemy.Hp);
                damage += damageTo[j];
            }

            var marks = _marks.Order(StringComparer.Ordinal).ToArray();
            _lines.Add(new Line
            {
                Actions = _path.ToArray(),
                Before = from,
                EndsTurn = endsTurn,
                Wins = !shows && !endsTurn && child is { IsOver: true, Won: true },
                Plain = _potions == 0 ? below.Plain - costs : double.NegativeInfinity,
                Value = below.Potion - costs,
                Marks = marks,
                Signature = string.Join("; ", marks),
                Block = at.Player.Block,
                HpAfter = child.Player.Hp,
                Damage = damage,
                DamageTo = damageTo,
                Order = _lines.Count,
            });
        }
    }
}
